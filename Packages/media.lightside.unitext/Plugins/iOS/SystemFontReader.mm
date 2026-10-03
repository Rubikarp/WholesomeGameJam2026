#import <Foundation/Foundation.h>
#import <CoreText/CoreText.h>
#import <CoreGraphics/CoreGraphics.h>
#import <TargetConditionals.h>
#import <pthread.h>
#import <string.h>
#include <fcntl.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/stat.h>
#include <unistd.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <limits>
#include <mutex>
#include <new>
#include <string>
#include <utility>
#include <vector>

#define MAX_EMOJI_SIZE 160

typedef struct {
    uint64_t generation;
    CTFontRef sourceFont;
    CTFontRef font;
    CGFloat fontSize;
    CGContextRef context;
    int contextWidth;
    int contextHeight;
    CGColorSpaceRef colorSpace;
    unsigned char* pixelBuffer;
} ThreadRenderContext;

static pthread_key_t tlsRenderContextKey;
static pthread_once_t tlsRenderContextKeyOnce = PTHREAD_ONCE_INIT;
static int tlsRenderContextKeyStatus = -1;
static std::atomic<uint64_t> renderContextGeneration{1};

static void DestroyThreadRenderContext(void* ptr) {
    if (ptr) {
        ThreadRenderContext* ctx = (ThreadRenderContext*)ptr;
        if (ctx->sourceFont) CFRelease(ctx->sourceFont);
        if (ctx->font) CFRelease(ctx->font);
        if (ctx->context) CGContextRelease(ctx->context);
        if (ctx->colorSpace) CGColorSpaceRelease(ctx->colorSpace);
        if (ctx->pixelBuffer) free(ctx->pixelBuffer);
        free(ctx);
    }
}

static void CreateRenderContextKey() {
    tlsRenderContextKeyStatus = pthread_key_create(
        &tlsRenderContextKey, DestroyThreadRenderContext);
}

static ThreadRenderContext* GetThreadRenderContext() {
    if (pthread_once(&tlsRenderContextKeyOnce, CreateRenderContextKey) != 0
        || tlsRenderContextKeyStatus != 0) return NULL;

    ThreadRenderContext* ctx = (ThreadRenderContext*)pthread_getspecific(tlsRenderContextKey);
    uint64_t generation = renderContextGeneration.load(std::memory_order_acquire);
    if (ctx && ctx->generation != generation) {
        if (pthread_setspecific(tlsRenderContextKey, NULL) != 0) return NULL;
        DestroyThreadRenderContext(ctx);
        ctx = NULL;
    }
    if (!ctx) {
        ctx = (ThreadRenderContext*)calloc(1, sizeof(ThreadRenderContext));
        if (!ctx) return NULL;
        ctx->generation = generation;
        ctx->colorSpace = CGColorSpaceCreateDeviceRGB();
        if (!ctx->colorSpace) {
            free(ctx);
            return NULL;
        }
        if (pthread_setspecific(tlsRenderContextKey, ctx) != 0) {
            DestroyThreadRenderContext(ctx);
            return NULL;
        }
    }
    return ctx;
}

static CTFontRef EnsureFont(ThreadRenderContext* ctx, CTFontRef sourceFont, CGFloat size) {
    if (ctx->font && ctx->sourceFont == sourceFont && ctx->fontSize == size) {
        return ctx->font;
    }

    if (ctx->font) {
        CFRelease(ctx->font);
        ctx->font = NULL;
    }
    if (ctx->sourceFont) {
        CFRelease(ctx->sourceFont);
        ctx->sourceFont = NULL;
    }

    if (!sourceFont) return NULL;
    ctx->sourceFont = (CTFontRef)CFRetain(sourceFont);
    ctx->font = CTFontCreateCopyWithAttributes(sourceFont, size, NULL, NULL);
    ctx->fontSize = size;

    return ctx->font;
}

static CGContextRef EnsureContext(ThreadRenderContext* ctx, int width, int height) {
    // Reuse existing context if large enough
    if (ctx->context && ctx->contextWidth >= width && ctx->contextHeight >= height) {
        CGContextClearRect(ctx->context, CGRectMake(0, 0, width, height));
        return ctx->context;
    }

    // Need larger context - release old one
    if (ctx->context) {
        CGContextRelease(ctx->context);
        ctx->context = NULL;
    }
    if (ctx->pixelBuffer) {
        free(ctx->pixelBuffer);
        ctx->pixelBuffer = NULL;
    }

    // Allocate for max size to avoid future reallocations
    int allocWidth = (width > MAX_EMOJI_SIZE) ? width : MAX_EMOJI_SIZE;
    int allocHeight = (height > MAX_EMOJI_SIZE) ? height : MAX_EMOJI_SIZE;
    size_t bytesPerRow = allocWidth * 4;

    ctx->pixelBuffer = (unsigned char*)calloc(allocHeight * bytesPerRow, 1);
    if (!ctx->pixelBuffer) {
        return NULL;
    }

    ctx->context = CGBitmapContextCreate(
        ctx->pixelBuffer,
        allocWidth,
        allocHeight,
        8,                              // bits per component
        bytesPerRow,
        ctx->colorSpace,
        kCGImageAlphaPremultipliedLast | kCGBitmapByteOrder32Big  // RGBA
    );

    if (!ctx->context) {
        free(ctx->pixelBuffer);
        ctx->pixelBuffer = NULL;
        return NULL;
    }

    ctx->contextWidth = allocWidth;
    ctx->contextHeight = allocHeight;

    CGContextSetInterpolationQuality(ctx->context, kCGInterpolationHigh);
    CGContextSetShouldAntialias(ctx->context, true);
    CGContextSetShouldSmoothFonts(ctx->context, true);

    return ctx->context;
}

static void AppendU32BE(std::vector<uint8_t>& data, uint32_t value) {
    data.push_back((uint8_t)(value >> 24));
    data.push_back((uint8_t)(value >> 16));
    data.push_back((uint8_t)(value >> 8));
    data.push_back((uint8_t)value);
}

static void AppendU16BE(std::vector<uint8_t>& data, uint16_t value) {
    data.push_back((uint8_t)(value >> 8));
    data.push_back((uint8_t)value);
}

static uint32_t SfntTableChecksum(const uint8_t* data, size_t length, bool clearHeadAdjustment) {
    uint32_t sum = 0;
    size_t words = (length + 3) / 4;
    for (size_t i = 0; i < words; i++) {
        uint32_t v = 0;
        for (int k = 0; k < 4; k++) {
            size_t idx = i * 4 + k;
            bool cleared = clearHeadAdjustment && idx >= 8 && idx < 12;
            v = (v << 8) | (idx < length && !cleared ? data[idx] : 0);
        }
        sum += v;
    }
    return sum;
}

// CoreText and CoreGraphics store table tags as unboxed pointer-width values in CFArray.
static uint32_t ReadTableTag(const void* value) {
    return (uint32_t)(uintptr_t)value;
}

static bool IsCoreTextOutlineTable(uint32_t tag) {
    return tag == 0x6876676C || tag == 0x6876706D;
}

static bool IncludeSfntTable(uint32_t tag, bool omitCoreTextOutlines) {
    if (tag == 0x44534947) return false;
    return !omitCoreTextOutlines || !IsCoreTextOutlineTable(tag);
}

struct SfntTableAccess {
    CTFontRef font = NULL;
    CGFontRef graphicsFont = NULL;
    CFArrayRef tags = NULL;
    bool coreText = false;

    ~SfntTableAccess() { Close(); }

    void Close() {
        if (tags) CFRelease(tags);
        if (graphicsFont) CGFontRelease(graphicsFont);
        tags = NULL;
        graphicsFont = NULL;
        font = NULL;
    }
};

struct SfntTableRecord {
    uint32_t tag;
    uint32_t checksum;
    uint32_t offset;
    uint32_t length;
};

static const size_t MaxSfntTableCount = std::numeric_limits<uint16_t>::max() / 16;

static bool OpenSfntTableAccess(CTFontRef font, bool coreText, SfntTableAccess& access) {
    access.Close();
    access.font = font;
    access.coreText = coreText;
    if (coreText) {
        access.tags = CTFontCopyAvailableTables(font, kCTFontTableOptionNoOptions);
    } else {
        access.graphicsFont = CTFontCopyGraphicsFont(font, NULL);
        if (access.graphicsFont) access.tags = CGFontCopyTableTags(access.graphicsFont);
    }
    if (access.tags && CFArrayGetCount(access.tags) > 0) return true;
    access.Close();
    return false;
}

static CFDataRef CopySfntTable(const SfntTableAccess& access, uint32_t tag) {
    return access.coreText
        ? CTFontCopyTable(access.font, tag, kCTFontTableOptionNoOptions)
        : CGFontCopyTableForTag(access.graphicsFont, tag);
}

static bool CollectSfntTableRecords(const SfntTableAccess& access,
    bool omitCoreTextOutlines, std::vector<SfntTableRecord>& records,
    bool& hasCff, uint32_t& headOffset, uint32_t& fileLength) {
    std::vector<uint32_t> tags;
    CFIndex tagCount = CFArrayGetCount(access.tags);
    tags.reserve((size_t)tagCount);
    for (CFIndex i = 0; i < tagCount; i++) {
        uint32_t tag = ReadTableTag(CFArrayGetValueAtIndex(access.tags, i));
        if (IncludeSfntTable(tag, omitCoreTextOutlines)) tags.push_back(tag);
    }
    std::sort(tags.begin(), tags.end());
    tags.erase(std::unique(tags.begin(), tags.end()), tags.end());
    if (tags.empty() || tags.size() > MaxSfntTableCount) return false;

    records.clear();
    records.reserve(tags.size());
    hasCff = false;
    bool hasHead = false;
    bool hasName = false;
    bool hasCmap = false;
    for (uint32_t tag : tags) {
        CFDataRef data = CopySfntTable(access, tag);
        if (!data) continue;
        CFIndex dataLength = CFDataGetLength(data);
        if (dataLength <= 0 || (uint64_t)dataLength > std::numeric_limits<uint32_t>::max()) {
            CFRelease(data);
            return false;
        }
        bool head = tag == 0x68656164;
        if (head && dataLength < 12) {
            CFRelease(data);
            return false;
        }
        records.push_back({
            tag,
            SfntTableChecksum(CFDataGetBytePtr(data), (size_t)dataLength, head),
            0,
            (uint32_t)dataLength
        });
        hasHead |= head;
        hasName |= tag == 0x6E616D65;
        hasCmap |= tag == 0x636D6170;
        hasCff |= tag == 0x43464620 || tag == 0x43464632;
        CFRelease(data);
    }
    if (!hasHead || !hasName || !hasCmap || records.empty()
        || records.size() > MaxSfntTableCount)
        return false;

    uint64_t nextOffset = 12 + records.size() * 16;
    headOffset = 0;
    for (SfntTableRecord& record : records) {
        if (nextOffset > std::numeric_limits<uint32_t>::max()) return false;
        record.offset = (uint32_t)nextOffset;
        if (record.tag == 0x68656164) headOffset = record.offset;
        nextOffset += ((uint64_t)record.length + 3) & ~(uint64_t)3;
    }
    if (nextOffset > INT_MAX) return false;
    fileLength = (uint32_t)nextOffset;
    return headOffset != 0;
}

static bool SelectSfntTableAccess(CTFontRef font, bool coreTextOutlines,
    SfntTableAccess& access, std::vector<SfntTableRecord>& records,
    bool& hasCff, uint32_t& headOffset, uint32_t& fileLength) {
    bool primaryCoreText = coreTextOutlines;
    if (OpenSfntTableAccess(font, primaryCoreText, access)
        && CollectSfntTableRecords(access, coreTextOutlines, records,
            hasCff, headOffset, fileLength)) return true;
    if (OpenSfntTableAccess(font, !primaryCoreText, access)
        && CollectSfntTableRecords(access, coreTextOutlines, records,
            hasCff, headOffset, fileLength)) return true;
    access.Close();
    return false;
}

static bool WriteSfntBytes(FILE* output, const void* bytes, size_t length) {
    return length == 0 || fwrite(bytes, 1, length, output) == length;
}

static std::vector<uint8_t> BuildSfntDirectory(const std::vector<SfntTableRecord>& records,
    bool hasCff, uint32_t& adjustment) {
    uint16_t numTables = (uint16_t)records.size();
    uint16_t entrySelector = 0;
    uint16_t powerOfTwo = 1;
    while ((uint32_t)powerOfTwo * 2 <= numTables) {
        powerOfTwo <<= 1;
        entrySelector++;
    }
    uint16_t searchRange = (uint16_t)(powerOfTwo * 16);
    uint16_t rangeShift = (uint16_t)(numTables * 16 - searchRange);
    std::vector<uint8_t> directory;
    directory.reserve(12 + records.size() * 16);
    AppendU32BE(directory, hasCff ? 0x4F54544F : 0x00010000);
    AppendU16BE(directory, numTables);
    AppendU16BE(directory, searchRange);
    AppendU16BE(directory, entrySelector);
    AppendU16BE(directory, rangeShift);
    for (const SfntTableRecord& record : records) {
        AppendU32BE(directory, record.tag);
        AppendU32BE(directory, record.checksum);
        AppendU32BE(directory, record.offset);
        AppendU32BE(directory, record.length);
    }
    uint32_t checksum = SfntTableChecksum(directory.data(), directory.size(), false);
    for (const SfntTableRecord& record : records) checksum += record.checksum;
    adjustment = 0xB1B0AFBA - checksum;
    return directory;
}

static int StreamSfntTables(FILE* output, const SfntTableAccess& access,
    const std::vector<SfntTableRecord>& records, uint32_t headOffset,
    uint32_t adjustment) {
    static const uint8_t zeros[4] = {0, 0, 0, 0};
    for (const SfntTableRecord& record : records) {
        CFDataRef data = CopySfntTable(access, record.tag);
        if (!data) return -4;
        CFIndex dataLength = CFDataGetLength(data);
        bool head = record.tag == 0x68656164;
        bool unchanged = dataLength == record.length
            && SfntTableChecksum(CFDataGetBytePtr(data), (size_t)dataLength, head) == record.checksum;
        if (!unchanged) {
            CFRelease(data);
            return -4;
        }

        const uint8_t* bytes = CFDataGetBytePtr(data);
        bool written;
        if (head) {
            written = WriteSfntBytes(output, bytes, 8)
                && WriteSfntBytes(output, zeros, 4)
                && WriteSfntBytes(output, bytes + 12, record.length - 12);
        } else {
            written = WriteSfntBytes(output, bytes, record.length);
        }
        CFRelease(data);
        size_t padding = (((size_t)record.length + 3) & ~(size_t)3) - record.length;
        if (!written || !WriteSfntBytes(output, zeros, padding)) return -3;
    }

    uint8_t adjustmentBytes[4] = {
        (uint8_t)(adjustment >> 24), (uint8_t)(adjustment >> 16),
        (uint8_t)(adjustment >> 8), (uint8_t)adjustment
    };
    if (fseeko(output, (off_t)headOffset + 8, SEEK_SET) != 0
        || !WriteSfntBytes(output, adjustmentBytes, sizeof(adjustmentBytes))) return -3;
    return 1;
}

static int WriteStandaloneSfnt(CTFontRef font, bool coreTextOutlines,
    const char* path, int64_t* outLength) {
    if (!font || !path || !path[0] || !outLength) return -1;
    *outLength = 0;
    SfntTableAccess access;
    std::vector<SfntTableRecord> records;
    bool hasCff = false;
    uint32_t headOffset = 0;
    uint32_t fileLength = 0;
    if (!SelectSfntTableAccess(font, coreTextOutlines, access, records,
            hasCff, headOffset, fileLength)) return -2;

    uint32_t adjustment;
    std::vector<uint8_t> directory = BuildSfntDirectory(records, hasCff, adjustment);

    int descriptor = open(path, O_CREAT | O_EXCL | O_WRONLY, 0600);
    if (descriptor < 0) return -3;
    FILE* output = fdopen(descriptor, "wb");
    if (!output) {
        close(descriptor);
        unlink(path);
        return -3;
    }

    int result = WriteSfntBytes(output, directory.data(), directory.size())
        ? StreamSfntTables(output, access, records, headOffset, adjustment)
        : -3;
    if (result == 1 && (fflush(output) != 0 || fsync(fileno(output)) != 0)) result = -3;
    if (fclose(output) != 0 && result == 1) result = -3;
    if (result != 1) {
        unlink(path);
        return result;
    }
    *outLength = fileLength;
    return 1;
}

enum FontOutlineKind {
    FontOutlineNone = 0,
    FontOutlineSfnt = 1,
    FontOutlineCoreText = 2,
};

static FontOutlineKind OutlineKindFromTags(CFArrayRef tags) {
    bool hasCoreTextOutlines = false;
    bool hasSfntOutlines = false;
    CFIndex n = CFArrayGetCount(tags);
    for (CFIndex i = 0; i < n; i++) {
        uint32_t tag = ReadTableTag(CFArrayGetValueAtIndex(tags, i));
        if (tag == 0x676C7966 || tag == 0x43464620 || tag == 0x43464632)
            hasSfntOutlines = true;
        if (tag == 0x6876676C) hasCoreTextOutlines = true;
    }
    return hasCoreTextOutlines ? FontOutlineCoreText
        : hasSfntOutlines ? FontOutlineSfnt
        : FontOutlineNone;
}

static FontOutlineKind GetFontOutlineKind(CTFontRef font) {
    CFArrayRef tags = CTFontCopyAvailableTables(font, kCTFontTableOptionNoOptions);
    if (tags) {
        FontOutlineKind kind = OutlineKindFromTags(tags);
        CFRelease(tags);
        if (kind != FontOutlineNone) return kind;
    }
    CGFontRef cg = CTFontCopyGraphicsFont(font, NULL);
    if (!cg) return FontOutlineNone;
    CFArrayRef cgTags = CGFontCopyTableTags(cg);
    FontOutlineKind kind = FontOutlineNone;
    if (cgTags) {
        kind = OutlineKindFromTags(cgTags);
        CFRelease(cgTags);
    }
    CGFontRelease(cg);
    return kind;
}

static bool IsLastResortFont(CTFontRef font) {
    CFStringRef name = CTFontCopyPostScriptName(font);
    if (!name) return false;
    bool last = CFStringFind(name, CFSTR("LastResort"), kCFCompareCaseInsensitive).location != kCFNotFound;
    CFRelease(name);
    return last;
}

// ============================================================================
// Extern C Functions
// ============================================================================

extern "C" {

    int UniText_GetSystemFontReaderAbiVersion() {
        return 3;
    }

    /// Frees a buffer allocated by UniText functions.
    void UniText_FreeBuffer(unsigned char* buffer) {
        if (buffer) {
            free(buffer);
        }
    }

    static float CoreTextWeightForCss(int cssWeight) {
        static const float values[] = { -0.8f, -0.6f, -0.4f, 0.0f, 0.23f, 0.3f, 0.4f, 0.56f, 0.62f };
        int weight = cssWeight < 100 ? 100 : (cssWeight > 900 ? 900 : cssWeight);
        int lower = (weight - 100) / 100;
        if (lower >= 8) return values[8];
        float fraction = (weight - (lower + 1) * 100) / 100.0f;
        return values[lower] + (values[lower + 1] - values[lower]) * fraction;
    }

    void UniText_TrimFontCaches() {
        renderContextGeneration.fetch_add(1, std::memory_order_acq_rel);
        if (pthread_once(&tlsRenderContextKeyOnce, CreateRenderContextKey) != 0
            || tlsRenderContextKeyStatus != 0) return;
        ThreadRenderContext* ctx = (ThreadRenderContext*)pthread_getspecific(tlsRenderContextKey);
        if (ctx && pthread_setspecific(tlsRenderContextKey, NULL) == 0) {
            DestroyThreadRenderContext(ctx);
        }
    }

    static bool IsCoverageIgnorable(uint32_t codepoint) {
        return codepoint == 0x00AD || codepoint == 0x034F || codepoint == 0x061C
            || (codepoint >= 0x115F && codepoint <= 0x1160)
            || (codepoint >= 0x17B4 && codepoint <= 0x17B5)
            || (codepoint >= 0x180B && codepoint <= 0x180F)
            || (codepoint >= 0x200B && codepoint <= 0x200F)
            || (codepoint >= 0x202A && codepoint <= 0x202E)
            || (codepoint >= 0x2060 && codepoint <= 0x206F)
            || codepoint == 0x3164
            || (codepoint >= 0xFE00 && codepoint <= 0xFE0F)
            || codepoint == 0xFEFF || codepoint == 0xFFA0
            || (codepoint >= 0xFFF0 && codepoint <= 0xFFF8)
            || codepoint == 0x110BD || codepoint == 0x110CD
            || (codepoint >= 0x13430 && codepoint <= 0x1345F)
            || (codepoint >= 0x1BCA0 && codepoint <= 0x1BCA3)
            || (codepoint >= 0x1D173 && codepoint <= 0x1D17A)
            || (codepoint >= 0xE0000 && codepoint <= 0xE0FFF);
    }

    static bool CharacterSetCoversText(CFCharacterSetRef characters,
        const UniChar* text, int length) {
        if (!characters || !text || length <= 0) return false;
        bool covers = true;
        int offset = 0;
        while (offset < length) {
            uint32_t codepoint = text[offset++];
            if (codepoint >= 0xD800 && codepoint <= 0xDBFF && offset < length) {
                uint32_t low = text[offset];
                if (low >= 0xDC00 && low <= 0xDFFF) {
                    offset++;
                    codepoint = 0x10000 + ((codepoint - 0xD800) << 10) + (low - 0xDC00);
                }
            }
            if (!IsCoverageIgnorable(codepoint)
                && !CFCharacterSetIsLongCharacterMember(characters, (UTF32Char)codepoint)) {
                covers = false;
                break;
            }
        }
        return covers;
    }

    static bool FontCoversText(CTFontRef font, const UniChar* text, int length) {
        if (!font) return false;
        CFCharacterSetRef characters = CTFontCopyCharacterSet(font);
        if (!characters) return false;
        bool covers = CharacterSetCoversText(characters, text, length);
        CFRelease(characters);
        return covers;
    }

    // The system UI font is virtual: no family-name descriptor search matches it, so it is created
    // through the UI-font API and re-styled from its own descriptor.
    static CTFontRef CreateSystemUIFont(NSDictionary* traits, bool styled) {
        CTFontRef font = CTFontCreateUIFontForLanguage(kCTFontUIFontSystem, 12.0, NULL);
        if (!font || !styled) return font;
        CTFontDescriptorRef descriptor = CTFontCopyFontDescriptor(font);
        if (!descriptor) return font;
        CTFontDescriptorRef styledDescriptor = CTFontDescriptorCreateCopyWithAttributes(descriptor,
            (__bridge CFDictionaryRef)@{ (__bridge id)kCTFontTraitsAttribute: traits });
        CFRelease(descriptor);
        if (!styledDescriptor) return font;
        CTFontRef styledFont = CTFontCreateWithFontDescriptor(styledDescriptor, 12.0, NULL);
        CFRelease(styledDescriptor);
        if (!styledFont) return font;
        CFRelease(font);
        return styledFont;
    }

    static CTFontRef CreateRequestedBaseFont(const char* family, int requestWeight, int requestItalic) {
        NSString* familyName = family && family[0]
            ? [NSString stringWithUTF8String:family]
            : @"Helvetica";
        float weight = requestWeight > 0 ? CoreTextWeightForCss(requestWeight) : 0.0f;
        NSDictionary* traits = @{
            (__bridge id)kCTFontWeightTrait: @(weight),
            (__bridge id)kCTFontSlantTrait: @(requestItalic ? 0.07f : 0.0f),
            (__bridge id)kCTFontSymbolicTrait: @(requestItalic ? kCTFontItalicTrait : 0)
        };
        if ([familyName isEqualToString:@".AppleSystemUIFont"])
            return CreateSystemUIFont(traits, requestWeight > 0 || requestItalic != 0);
        NSDictionary* attributes = @{
            (__bridge id)kCTFontFamilyNameAttribute: familyName,
            (__bridge id)kCTFontTraitsAttribute: traits
        };
        CTFontDescriptorRef descriptor = CTFontDescriptorCreateWithAttributes(
            (__bridge CFDictionaryRef)attributes);
        if (!descriptor) return NULL;
        CTFontRef font = CTFontCreateWithFontDescriptor(descriptor, 12.0, NULL);
        CFRelease(descriptor);
        return font;
    }

    struct SystemFontNativeFaceInfo {
        int unitsPerEm;
        int lineHeight;
        int ascentLine;
        int capLine;
        int meanLine;
        int descentLine;
        int typoAscent;
        int typoDescent;
        int typoLineGap;
        int winAscent;
        int winDescent;
        int useTypoMetrics;
        int superscriptOffset;
        int superscriptSize;
        int subscriptOffset;
        int subscriptSize;
        int underlineOffset;
        int underlineThickness;
        int strikethroughOffset;
        int strikethroughThickness;
        int tabWidth;
        int weightClass;
        int isItalic;
    };

    struct SystemFontBatchAxis {
        int tag;
        float value;
    };

    struct SystemFontBatchMatch {
        int sourceIndex = -1;
        int error = 0;
        int utf16Start = -1;
        int utf16Length = 0;
        std::vector<SystemFontBatchAxis> axes;
    };

    struct SystemFontBatchSource {
        std::string key;
        std::string postScriptName;
        std::string filePath;
        std::string familyName;
        std::string styleName;
        std::vector<SystemFontBatchAxis> instanceAxes;
        CTFontRef font = NULL;
        CTFontRef outlineFont = NULL;
        FontOutlineKind outlineKind;
        SystemFontNativeFaceInfo faceInfo = {};
    };

    struct SystemFontBatch {
        std::vector<SystemFontBatchMatch> matches;
        std::vector<SystemFontBatchSource> sources;

        ~SystemFontBatch() {
            for (const SystemFontBatchSource& source : sources) {
                if (source.outlineFont) CFRelease(source.outlineFont);
                if (source.font) CFRelease(source.font);
            }
        }
    };

    struct SystemFontBatchFont {
        CTFontRef font;
        CFCharacterSetRef characters;
        bool usable;
    };

    static std::string Utf8String(CFStringRef value) {
        if (!value) return std::string();
        const char* utf8 = [(__bridge NSString*)value UTF8String];
        return utf8 ? std::string(utf8) : std::string();
    }

    static std::string ReadableSystemFontPath(CTFontRef font) {
        std::string result;
        CFTypeRef attribute = CTFontCopyAttribute(font, kCTFontURLAttribute);
        if (attribute) {
            if (CFGetTypeID(attribute) == CFURLGetTypeID()) {
                NSURL* url = (__bridge NSURL*)attribute;
                if (url.fileURL) {
                    const char* path = url.fileSystemRepresentation;
                    char* resolved = path ? realpath(path, NULL) : NULL;
                    if (resolved) {
                        struct stat info = {};
                        if (stat(resolved, &info) == 0 && S_ISREG(info.st_mode)
                            && info.st_size > 0 && access(resolved, R_OK) == 0)
                            result.assign(resolved);
                        free(resolved);
                    }
                }
            }
            CFRelease(attribute);
        }
        return result;
    }

    static uint16_t ReadU16BE(CFDataRef data, CFIndex offset) {
        if (!data || offset < 0 || offset > CFDataGetLength(data) - 2) return 0;
        const UInt8* bytes = CFDataGetBytePtr(data) + offset;
        return (uint16_t)((uint16_t)bytes[0] << 8 | bytes[1]);
    }

    static int16_t ReadS16BE(CFDataRef data, CFIndex offset) {
        return (int16_t)ReadU16BE(data, offset);
    }

    static int CssWeightForCoreText(float value) {
        static const float values[] = { -0.8f, -0.6f, -0.4f, 0.0f, 0.23f, 0.3f, 0.4f, 0.56f, 0.62f };
        if (value <= values[0]) return 100;
        for (int i = 0; i < 8; i++) {
            if (value > values[i + 1]) continue;
            float span = values[i + 1] - values[i];
            float fraction = span > 0.0f ? (value - values[i]) / span : 0.0f;
            return (int)lround((i + 1) * 100 + fraction * 100.0f);
        }
        return 900;
    }

    static bool ReadCoreTextFaceInfo(CTFontRef font, SystemFontNativeFaceInfo* outInfo,
        CTFontRef* outOutlineFont) {
        if (!font || !outInfo || !outOutlineFont) return false;
        *outOutlineFont = NULL;
        SystemFontNativeFaceInfo info = {};
        info.unitsPerEm = (int)CTFontGetUnitsPerEm(font);
        if (info.unitsPerEm <= 0) return false;

        CTFontRef sized = CTFontCreateCopyWithAttributes(font, (CGFloat)info.unitsPerEm, NULL, NULL);
        if (!sized) return false;
        CGFloat ascent = CTFontGetAscent(sized);
        CGFloat descent = CTFontGetDescent(sized);
        CGFloat leading = CTFontGetLeading(sized);
        CGFloat capHeight = CTFontGetCapHeight(sized);
        CGFloat xHeight = CTFontGetXHeight(sized);
        CGFloat underlinePosition = CTFontGetUnderlinePosition(sized);
        CGFloat underlineThickness = CTFontGetUnderlineThickness(sized);
        if (!std::isfinite(ascent) || !std::isfinite(descent) || !std::isfinite(leading)
            || !std::isfinite(capHeight) || !std::isfinite(xHeight)
            || !std::isfinite(underlinePosition) || !std::isfinite(underlineThickness)) {
            CFRelease(sized);
            return false;
        }
        info.ascentLine = (int)lround(ascent);
        info.descentLine = -(int)lround(descent);
        info.lineHeight = (int)lround(ascent + descent + leading);
        info.capLine = (int)lround(capHeight);
        info.meanLine = (int)lround(xHeight);
        info.underlineOffset = (int)lround(underlinePosition);
        info.underlineThickness = (int)lround(underlineThickness);
        info.isItalic = (CTFontGetSymbolicTraits(font) & kCTFontItalicTrait) != 0;

        CFDataRef os2 = CTFontCopyTable(font, 0x4F532F32, kCTFontTableOptionNoOptions);
        if (os2) {
            CFIndex length = CFDataGetLength(os2);
            if (length >= 8) info.weightClass = ReadU16BE(os2, 4);
            if (length >= 30) {
                info.subscriptSize = ReadS16BE(os2, 12);
                info.subscriptOffset = ReadS16BE(os2, 16);
                info.superscriptSize = ReadS16BE(os2, 20);
                info.superscriptOffset = ReadS16BE(os2, 24);
                info.strikethroughThickness = ReadS16BE(os2, 26);
                info.strikethroughOffset = ReadS16BE(os2, 28);
            }
            if (length >= 64) info.useTypoMetrics = (ReadU16BE(os2, 62) & 0x80) != 0;
            if (length >= 78) {
                info.typoAscent = ReadS16BE(os2, 68);
                info.typoDescent = ReadS16BE(os2, 70);
                info.typoLineGap = ReadS16BE(os2, 72);
                info.winAscent = ReadU16BE(os2, 74);
                info.winDescent = -(int)ReadU16BE(os2, 76);
            }
            if (length >= 90 && ReadU16BE(os2, 0) >= 2) {
                int meanLine = ReadS16BE(os2, 86);
                int capLine = ReadS16BE(os2, 88);
                if (meanLine > 0) info.meanLine = meanLine;
                if (capLine > 0) info.capLine = capLine;
            }
            CFRelease(os2);
        }

        CFDataRef post = CTFontCopyTable(font, 0x706F7374, kCTFontTableOptionNoOptions);
        if (post) {
            if (CFDataGetLength(post) >= 12) {
                info.underlineOffset = ReadS16BE(post, 8);
                info.underlineThickness = ReadS16BE(post, 10);
            }
            CFRelease(post);
        }

        if (info.useTypoMetrics && info.typoAscent != 0 && info.typoDescent != 0) {
            info.ascentLine = info.typoAscent;
            info.descentLine = info.typoDescent;
            info.lineHeight = info.typoAscent - info.typoDescent + info.typoLineGap;
        }
        if (info.lineHeight <= 0) info.lineHeight = info.ascentLine - info.descentLine;
        if (info.capLine <= 0) info.capLine = (int)lround(info.ascentLine * 0.75);
        if (info.meanLine <= 0) info.meanLine = (int)lround(info.ascentLine * 0.5);
        if (info.underlineThickness <= 0)
            info.underlineThickness = (int)lround(info.ascentLine * 0.05);
        if (info.strikethroughOffset == 0)
            info.strikethroughOffset = (int)lround(info.meanLine * 0.5);
        if (info.strikethroughThickness <= 0)
            info.strikethroughThickness = info.underlineThickness;
        if (info.superscriptSize <= 0) info.superscriptSize = info.unitsPerEm;
        if (info.superscriptOffset == 0) info.superscriptOffset = info.ascentLine;
        if (info.subscriptSize <= 0) info.subscriptSize = info.unitsPerEm;
        if (info.subscriptOffset == 0) info.subscriptOffset = info.descentLine;

        UniChar space = 0x20;
        CGGlyph spaceGlyph = 0;
        if (CTFontGetGlyphsForCharacters(sized, &space, &spaceGlyph, 1)) {
            CGSize advance = CGSizeZero;
            CTFontGetAdvancesForGlyphs(sized, kCTFontOrientationHorizontal,
                &spaceGlyph, &advance, 1);
            if (std::isfinite(advance.width)) info.tabWidth = (int)lround(advance.width);
        }
        if (info.tabWidth <= 0) info.tabWidth = info.ascentLine;

        if (info.weightClass <= 0) {
            CFDictionaryRef traitsRef = CTFontCopyTraits(font);
            if (traitsRef) {
                CFNumberRef weightRef = (CFNumberRef)CFDictionaryGetValue(traitsRef, kCTFontWeightTrait);
                float weight = 0.0f;
                if (weightRef && CFNumberGetValue(weightRef, kCFNumberFloatType, &weight))
                    info.weightClass = CssWeightForCoreText(weight);
                CFRelease(traitsRef);
            }
        }
        if (info.weightClass <= 0) info.weightClass = 400;
        *outInfo = info;
        *outOutlineFont = sized;
        return true;
    }

    struct SystemEmojiRunFontIdentity {
        CTFontDescriptorRef descriptor;

        explicit SystemEmojiRunFontIdentity(CTFontRef font)
            : descriptor(font ? CTFontCopyFontDescriptor(font) : NULL) {}

        ~SystemEmojiRunFontIdentity() {
            if (descriptor) CFRelease(descriptor);
        }
    };

    struct SystemEmojiGlyphBinding {
        CTFontRef font;
        CTFontDescriptorRef descriptor;
        CGGlyph glyph;
    };

    struct SystemEmojiFace {
        std::atomic<int> references;
        CTFontRef font;
        uint32_t primaryGlyphCount;
        std::mutex glyphMutex;
        std::vector<SystemEmojiGlyphBinding> substitutedGlyphs;
        SystemFontNativeFaceInfo faceInfo;
        std::string identity;
        std::string familyName;
        std::string styleName;

        SystemEmojiFace(CTFontRef font, uint32_t primaryGlyphCount,
            const SystemFontNativeFaceInfo& faceInfo,
            std::string identity, std::string familyName, std::string styleName)
            : references(1), font(font), primaryGlyphCount(primaryGlyphCount), faceInfo(faceInfo),
              identity(std::move(identity)), familyName(std::move(familyName)),
              styleName(std::move(styleName)) {}

        ~SystemEmojiFace() {
            for (const SystemEmojiGlyphBinding& binding : substitutedGlyphs) {
                CFRelease(binding.font);
                CFRelease(binding.descriptor);
            }
            if (font) CFRelease(font);
        }
    };

    static bool RegisterSubstitutedSystemEmojiGlyph(SystemEmojiFace* face, CTFontRef font,
        const SystemEmojiRunFontIdentity& identity, CGGlyph glyph, uint32_t* outGlyph) {
        if (!identity.descriptor) return false;
        std::lock_guard<std::mutex> lock(face->glyphMutex);
        for (size_t i = 0; i < face->substitutedGlyphs.size(); i++) {
            const SystemEmojiGlyphBinding& binding = face->substitutedGlyphs[i];
            if (binding.glyph != glyph || !CFEqual(binding.descriptor, identity.descriptor))
                continue;
            *outGlyph = face->primaryGlyphCount + (uint32_t)i;
            return true;
        }

        size_t capacity = (size_t)UINT16_MAX + 1 - face->primaryGlyphCount;
        if (face->substitutedGlyphs.size() >= capacity) return false;
        face->substitutedGlyphs.push_back({font, identity.descriptor, glyph});
        CFRetain(font);
        CFRetain(identity.descriptor);
        *outGlyph = face->primaryGlyphCount
            + (uint32_t)face->substitutedGlyphs.size() - 1;
        return true;
    }

    static bool ResolveSystemEmojiGlyph(SystemEmojiFace* face, uint32_t glyphIndex,
        CTFontRef* outFont, CGGlyph* outGlyph) {
        if (!face || !outFont || !outGlyph || glyphIndex == 0 || glyphIndex > UINT16_MAX)
            return false;
        if (glyphIndex < face->primaryGlyphCount) {
            *outFont = face->font;
            *outGlyph = (CGGlyph)glyphIndex;
            return true;
        }

        std::lock_guard<std::mutex> lock(face->glyphMutex);
        size_t index = (size_t)(glyphIndex - face->primaryGlyphCount);
        if (index >= face->substitutedGlyphs.size()) return false;
        const SystemEmojiGlyphBinding& binding = face->substitutedGlyphs[index];
        *outFont = binding.font;
        *outGlyph = binding.glyph;
        return true;
    }

    static bool IsValidUnicodeScalar(uint32_t codepoint) {
        return codepoint <= 0x10FFFF
            && (codepoint < 0xD800 || codepoint > 0xDFFF);
    }

    static int ReadSystemEmojiGlyph(CTFontRef font, uint32_t codepoint, CGGlyph* outGlyph) {
        if (!font || !outGlyph || !IsValidUnicodeScalar(codepoint)) return -1;
        *outGlyph = 0;
        UniChar characters[2] = {};
        CGGlyph glyphs[2] = {};
        CFIndex length = 1;
        if (codepoint <= 0xFFFF) {
            characters[0] = (UniChar)codepoint;
        } else {
            uint32_t value = codepoint - 0x10000;
            characters[0] = (UniChar)(0xD800 + (value >> 10));
            characters[1] = (UniChar)(0xDC00 + (value & 0x3FF));
            length = 2;
        }
        CTFontGetGlyphsForCharacters(font, characters, glyphs, length);
        if (glyphs[0] != 0 && glyphs[1] != 0 && glyphs[0] != glyphs[1]) return -1;
        *outGlyph = glyphs[0] != 0 ? glyphs[0] : glyphs[1];
        return *outGlyph != 0 ? 1 : 0;
    }

    static int ReadSystemEmojiAdvance(CTFontRef font, CGGlyph glyph, int32_t* outAdvance) {
        if (!font || !outAdvance || glyph == 0 || glyph >= CTFontGetGlyphCount(font)) return -1;
        CGSize advance = CGSizeZero;
        CTFontGetAdvancesForGlyphs(font, kCTFontOrientationHorizontal, &glyph, &advance, 1);
        if (!std::isfinite(advance.width)
            || advance.width < (CGFloat)INT32_MIN || advance.width > (CGFloat)INT32_MAX) return -1;
        *outAdvance = (int32_t)lround(advance.width);
        return 1;
    }

    static SystemEmojiFace* CreateSystemEmojiFace() {
        NSDictionary* attributes = @{
            (__bridge id)kCTFontFamilyNameAttribute: @"Apple Color Emoji"
        };
        CTFontDescriptorRef descriptor = CTFontDescriptorCreateWithAttributes(
            (__bridge CFDictionaryRef)attributes);
        if (!descriptor) return NULL;
        CTFontRef selectedFont = CTFontCreateWithFontDescriptor(descriptor, 0.0, NULL);
        CFRelease(descriptor);
        if (!selectedFont || IsLastResortFont(selectedFont)) {
            if (selectedFont) CFRelease(selectedFont);
            return NULL;
        }

        CFStringRef identityRef = CTFontCopyPostScriptName(selectedFont);
        CFStringRef familyRef = CTFontCopyFamilyName(selectedFont);
        CFStringRef styleRef = CTFontCopyName(selectedFont, kCTFontStyleNameKey);
        bool exactFamily = familyRef
            && CFStringCompare(familyRef, CFSTR("Apple Color Emoji"), 0) == kCFCompareEqualTo;
        std::string identity;
        std::string familyName;
        std::string styleName;
        identity = Utf8String(identityRef);
        familyName = Utf8String(familyRef);
        styleName = Utf8String(styleRef);
        if (familyRef) CFRelease(familyRef);
        if (styleRef) CFRelease(styleRef);
        if (!exactFamily || identity.empty() || familyName.empty() || styleName.empty()) {
            if (identityRef) CFRelease(identityRef);
            CFRelease(selectedFont);
            return NULL;
        }
        CFRelease(identityRef);

        SystemFontNativeFaceInfo faceInfo = {};
        CTFontRef designFont = NULL;
        if (!ReadCoreTextFaceInfo(selectedFont, &faceInfo, &designFont)) {
            CFRelease(selectedFont);
            return NULL;
        }
        CFRelease(selectedFont);

        CGGlyph defaultEmoji = 0;
        if (ReadSystemEmojiGlyph(designFont, 0x1F600, &defaultEmoji) != 1) {
            CFRelease(designFont);
            return NULL;
        }

        CFIndex primaryGlyphCount = CTFontGetGlyphCount(designFont);
        if (primaryGlyphCount <= 0 || primaryGlyphCount > (CFIndex)UINT16_MAX + 1) {
            CFRelease(designFont);
            return NULL;
        }

        SystemEmojiFace* result = new (std::nothrow) SystemEmojiFace(designFont,
            (uint32_t)primaryGlyphCount, faceInfo, std::move(identity),
            std::move(familyName), std::move(styleName));
        if (!result) {
            CFRelease(designFont);
            return NULL;
        }
        return result;
    }

    void* UniText_OpenSystemEmojiFace(SystemFontNativeFaceInfo* outInfo,
        const char** identity, const char** family, const char** style) {
        if (!outInfo || !identity || !family || !style) return NULL;
        *outInfo = {};
        *identity = NULL;
        *family = NULL;
        *style = NULL;
        @autoreleasepool {
            SystemEmojiFace* face = CreateSystemEmojiFace();
            if (!face) return NULL;
            *outInfo = face->faceInfo;
            *identity = face->identity.c_str();
            *family = face->familyName.c_str();
            *style = face->styleName.c_str();
            return face;
        }
    }

    void* UniText_RetainSystemEmojiFace(void* handle) {
        if (!handle) return NULL;
        SystemEmojiFace* face = (SystemEmojiFace*)handle;
        face->references.fetch_add(1, std::memory_order_relaxed);
        return face;
    }

    void UniText_ReleaseSystemEmojiFace(void* handle) {
        if (!handle) return;
        SystemEmojiFace* face = (SystemEmojiFace*)handle;
        if (face->references.fetch_sub(1, std::memory_order_acq_rel) == 1) delete face;
    }

    int UniText_GetSystemEmojiGlyph(void* handle, uint32_t codepoint,
        uint32_t* outGlyph, int32_t* outAdvance) {
        if (!handle || !outGlyph || !outAdvance) return -1;
        *outGlyph = 0;
        *outAdvance = 0;
        SystemEmojiFace* face = (SystemEmojiFace*)handle;
        CGGlyph glyph = 0;
        int result = ReadSystemEmojiGlyph(face->font, codepoint, &glyph);
        if (result != 1) return result;
        if (ReadSystemEmojiAdvance(face->font, glyph, outAdvance) != 1) return -1;
        *outGlyph = glyph;
        return 1;
    }

    int UniText_GetSystemEmojiGlyphAdvance(void* handle, uint32_t glyphIndex,
        int32_t* outAdvance) {
        if (!outAdvance) return -1;
        *outAdvance = 0;
        SystemEmojiFace* face = (SystemEmojiFace*)handle;
        CTFontRef font = NULL;
        CGGlyph glyph = 0;
        if (!ResolveSystemEmojiGlyph(face, glyphIndex, &font, &glyph)) return -1;
        return ReadSystemEmojiAdvance(font, glyph, outAdvance);
    }

    static CTFontRef ResolveBatchFont(const SystemFontBatchFont& base, CFStringRef string,
        const UniChar* text, int start, int length,
        CFStringRef language) {
        if (base.usable && CharacterSetCoversText(base.characters, text + start, length)) {
            CFRetain(base.font);
            return base.font;
        }

        CTFontRef candidate = CTFontCreateForStringWithLanguage(base.font, string,
            CFRangeMake(start, length), language);
        if (candidate) {
#if TARGET_OS_OSX
            bool covers = true;
#else
            bool covers = FontCoversText(candidate, text + start, length);
#endif
            if (covers && GetFontOutlineKind(candidate) != FontOutlineNone
                && !IsLastResortFont(candidate))
                return candidate;
            CFRelease(candidate);
        }
        return NULL;
    }

    static bool ReadSystemFontAxes(CTFontRef font, std::vector<SystemFontBatchAxis>& axes) {
        CFDictionaryRef variationRef = CTFontCopyVariation(font);
        if (!variationRef) return true;
        NSDictionary* variations = (NSDictionary*)CFBridgingRelease(variationRef);
        axes.reserve(variations.count);
        for (NSNumber* tag in variations) {
            double value = [variations[tag] doubleValue];
            if (!std::isfinite(value)) return false;
            value = (double)lround(value * 65536.0) / 65536.0;
            axes.push_back({tag.intValue, (float)value});
        }
        std::sort(axes.begin(), axes.end(), [](const SystemFontBatchAxis& left,
            const SystemFontBatchAxis& right) { return left.tag < right.tag; });
        return true;
    }

    static bool SystemFontAxesEqual(const std::vector<SystemFontBatchAxis>& left,
        const std::vector<SystemFontBatchAxis>& right) {
        if (left.size() != right.size()) return false;
        for (size_t i = 0; i < left.size(); i++)
            if (left[i].tag != right[i].tag || left[i].value != right[i].value)
                return false;
        return true;
    }

    static bool ReadSystemFontBatchMatch(CTFontRef font, SystemFontBatch* batch,
        SystemFontBatchMatch& match) {
        std::vector<SystemFontBatchAxis> axes;
        if (!ReadSystemFontAxes(font, axes)) return false;
        CFStringRef nameRef = CTFontCopyPostScriptName(font);
        std::string postScriptName = Utf8String(nameRef);
        if (nameRef) CFRelease(nameRef);
        if (postScriptName.empty()) return false;

        std::string filePath = ReadableSystemFontPath(font);
        std::string key = filePath.empty()
            ? std::string("coretext-session:") + postScriptName
            : std::string("coretext-file:") + filePath;
        for (size_t i = 0; i < batch->sources.size(); i++)
            if (batch->sources[i].key == key
                && batch->sources[i].postScriptName == postScriptName
                && SystemFontAxesEqual(batch->sources[i].instanceAxes, axes)) {
                match.sourceIndex = (int)i;
                break;
            }

        if (match.sourceIndex < 0) {
            FontOutlineKind kind = GetFontOutlineKind(font);
            if (kind == FontOutlineNone) return false;
            SystemFontNativeFaceInfo faceInfo = {};
            CTFontRef outlineFont = NULL;
            if (kind == FontOutlineCoreText
                && !ReadCoreTextFaceInfo(font, &faceInfo, &outlineFont))
                return false;
            CFStringRef familyRef = CTFontCopyFamilyName(font);
            CFStringRef styleRef = CTFontCopyName(font, kCTFontStyleNameKey);
            SystemFontBatchSource source = {};
            source.key = key;
            source.postScriptName = postScriptName;
            source.filePath = filePath;
            source.familyName = Utf8String(familyRef);
            source.styleName = Utf8String(styleRef);
            source.instanceAxes = axes;
            source.font = (CTFontRef)CFRetain(font);
            source.outlineFont = outlineFont;
            source.outlineKind = kind;
            source.faceInfo = faceInfo;
            if (familyRef) CFRelease(familyRef);
            if (styleRef) CFRelease(styleRef);
            batch->sources.push_back(source);
            match.sourceIndex = (int)batch->sources.size() - 1;
        }

        match.axes = axes;
        return true;
    }

    void* UniText_ResolveSystemFontBatch(const uint16_t* text, int textLength,
        const int* offsets, int count, const char* language, const char* family,
        int requestWeight, int requestItalic) {
        if (!text || textLength <= 0 || !offsets || count <= 0) return NULL;
        @autoreleasepool {
            CTFontRef base = CreateRequestedBaseFont(family, requestWeight, requestItalic);
            if (!base) return NULL;
            CFStringRef string = CFStringCreateWithCharacters(kCFAllocatorDefault,
                (const UniChar*)text, textLength);
            if (!string) {
                CFRelease(base);
                return NULL;
            }
            SystemFontBatchFont baseEntry = {
                base,
                CTFontCopyCharacterSet(base),
                GetFontOutlineKind(base) != FontOutlineNone && !IsLastResortFont(base)
            };

            NSString* languageTag = nil;
            if (language && language[0]) {
                languageTag = [NSString stringWithUTF8String:language];
            }

            SystemFontBatch* batch = new SystemFontBatch();
            batch->matches.resize((size_t)count);
            for (int i = 0; i < count; i++) {
                int start = offsets[i];
                int length = offsets[i + 1] - start;
                if (start < 0 || length <= 0 || start + length > textLength) continue;
                CTFontRef font = ResolveBatchFont(baseEntry, string, (const UniChar*)text,
                    start, length, (__bridge CFStringRef)languageTag);
                if (!font) continue;
                if (!ReadSystemFontBatchMatch(font, batch, batch->matches[(size_t)i]))
                    batch->matches[(size_t)i].error = 1;
                CFRelease(font);
            }

            if (baseEntry.characters) CFRelease(baseEntry.characters);
            CFRelease(string);
            CFRelease(base);
            return batch;
        }
    }

#if TARGET_OS_OSX
    static bool SystemFontRunMatches(const SystemFontBatchMatch& left,
        const SystemFontBatchMatch& right) {
        return left.sourceIndex == right.sourceIndex
            && left.error == right.error
            && SystemFontAxesEqual(left.axes, right.axes);
    }

    static bool NormalizeSystemFontRuns(SystemFontBatch* batch, int textLength) {
        std::sort(batch->matches.begin(), batch->matches.end(),
            [](const SystemFontBatchMatch& left, const SystemFontBatchMatch& right) {
                return left.utf16Start < right.utf16Start;
            });

        std::vector<SystemFontBatchMatch> normalized;
        normalized.reserve(batch->matches.size() + 1);
        int previousEnd = 0;
        for (SystemFontBatchMatch& match : batch->matches) {
            if (match.utf16Start < previousEnd || match.utf16Length <= 0
                || match.utf16Start > textLength
                || match.utf16Length > textLength - match.utf16Start)
                return false;
            if (match.utf16Start > previousEnd) {
                SystemFontBatchMatch gap;
                gap.utf16Start = previousEnd;
                gap.utf16Length = match.utf16Start - previousEnd;
                if (!normalized.empty() && SystemFontRunMatches(normalized.back(), gap))
                    normalized.back().utf16Length += gap.utf16Length;
                else
                    normalized.push_back(std::move(gap));
            }
            if (!normalized.empty()) {
                SystemFontBatchMatch& previous = normalized.back();
                int normalizedEnd = previous.utf16Start + previous.utf16Length;
                if (match.utf16Start == normalizedEnd
                    && SystemFontRunMatches(previous, match)) {
                    previous.utf16Length += match.utf16Length;
                    previousEnd = match.utf16Start + match.utf16Length;
                    continue;
                }
            }
            previousEnd = match.utf16Start + match.utf16Length;
            normalized.push_back(std::move(match));
        }
        if (previousEnd < textLength) {
            SystemFontBatchMatch gap;
            gap.utf16Start = previousEnd;
            gap.utf16Length = textLength - previousEnd;
            if (!normalized.empty() && SystemFontRunMatches(normalized.back(), gap))
                normalized.back().utf16Length += gap.utf16Length;
            else
                normalized.push_back(std::move(gap));
        }
        batch->matches = std::move(normalized);
        return !batch->matches.empty();
    }

    void* UniText_ResolveSystemFontRuns(const uint16_t* text, int textLength,
        const char* language, const char* family,
        int requestWeight, int requestItalic) {
        if (!text || textLength <= 0) return NULL;
        @autoreleasepool {
            CTFontRef base = CreateRequestedBaseFont(family, requestWeight, requestItalic);
            if (!base) return NULL;

            CFStringRef string = CFStringCreateWithCharacters(kCFAllocatorDefault,
                (const UniChar*)text, textLength);
            if (!string) {
                CFRelease(base);
                return NULL;
            }

            CFStringRef languageTag = NULL;
            if (language && language[0]) {
                languageTag = CFStringCreateWithCString(kCFAllocatorDefault, language,
                    kCFStringEncodingUTF8);
                if (!languageTag) {
                    CFRelease(string);
                    CFRelease(base);
                    return NULL;
                }
            }

            const void* keys[2] = { kCTFontAttributeName, kCTLanguageAttributeName };
            const void* values[2] = { base, languageTag };
            CFDictionaryRef attributes = CFDictionaryCreate(kCFAllocatorDefault,
                keys, values, languageTag ? 2 : 1,
                &kCFCopyStringDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
            if (!attributes) {
                if (languageTag) CFRelease(languageTag);
                CFRelease(string);
                CFRelease(base);
                return NULL;
            }

            CFAttributedStringRef attributed = CFAttributedStringCreate(kCFAllocatorDefault,
                string, attributes);
            CFRelease(attributes);
            if (languageTag) CFRelease(languageTag);
            CFRelease(string);
            CFRelease(base);
            if (!attributed) return NULL;

            CTLineRef line = CTLineCreateWithAttributedString(attributed);
            CFRelease(attributed);
            if (!line) return NULL;

            SystemFontBatch* batch = new SystemFontBatch();
            CFArrayRef runs = CTLineGetGlyphRuns(line);
            bool valid = runs != NULL;
            CFIndex runCount = runs ? CFArrayGetCount(runs) : 0;
            batch->matches.reserve((size_t)runCount);
            for (CFIndex i = 0; i < runCount; i++) {
                CTRunRef run = (CTRunRef)CFArrayGetValueAtIndex(runs, i);
                if (!run) {
                    valid = false;
                    break;
                }
                CFRange range = CTRunGetStringRange(run);
                if (range.length == 0) continue;
                if (range.location < 0 || range.length < 0
                    || range.location > textLength
                    || range.length > textLength - range.location) {
                    valid = false;
                    break;
                }

                CFDictionaryRef runAttributes = CTRunGetAttributes(run);
                CFTypeRef fontValue = runAttributes
                    ? CFDictionaryGetValue(runAttributes, kCTFontAttributeName)
                    : NULL;
                if (!fontValue || CFGetTypeID(fontValue) != CTFontGetTypeID()) {
                    valid = false;
                    break;
                }

                SystemFontBatchMatch match;
                match.utf16Start = (int)range.location;
                match.utf16Length = (int)range.length;
                CTFontRef font = (CTFontRef)fontValue;
                if (!IsLastResortFont(font)
                    && GetFontOutlineKind(font) != FontOutlineNone
                    && !ReadSystemFontBatchMatch(font, batch, match))
                    match.error = 1;
                batch->matches.push_back(std::move(match));
            }

            CFRelease(line);
            if (!valid || !NormalizeSystemFontRuns(batch, textLength)) {
                delete batch;
                return NULL;
            }
            return batch;
        }
    }

    void* UniText_ResolveNamedSystemFont(const uint16_t* text, int textLength,
        const char* postScriptName) {
        if (!text || textLength <= 0 || !postScriptName || !postScriptName[0]) return NULL;
        @autoreleasepool {
            CFStringRef requestedName = CFStringCreateWithCString(kCFAllocatorDefault,
                postScriptName, kCFStringEncodingUTF8);
            if (!requestedName) return NULL;

            SystemFontBatch* batch = new SystemFontBatch();
            batch->matches.resize(1);
            SystemFontBatchMatch& match = batch->matches[0];
            match.utf16Start = 0;
            match.utf16Length = textLength;

            CTFontOptions options = (CTFontOptions)(kCTFontOptionsPreferSystemFont
                | kCTFontOptionsPreventAutoDownload);
            CTFontRef font = CTFontCreateWithNameAndOptions(requestedName, 12.0, NULL, options);
            if (font) {
                CFStringRef actualName = CTFontCopyPostScriptName(font);
                if (actualName && CFEqual(actualName, requestedName)
                    && !IsLastResortFont(font)
                    && GetFontOutlineKind(font) != FontOutlineNone
                    && !ReadSystemFontBatchMatch(font, batch, match))
                    match.error = 1;
                if (actualName) CFRelease(actualName);
                CFRelease(font);
            }

            CFRelease(requestedName);
            return batch;
        }
    }

    int UniText_GetSystemFontRunRange(void* handle, int index,
        int* utf16Start, int* utf16Length) {
        if (!handle || !utf16Start || !utf16Length) return 0;
        SystemFontBatch* batch = (SystemFontBatch*)handle;
        if (index < 0 || (size_t)index >= batch->matches.size()) return 0;
        const SystemFontBatchMatch& match = batch->matches[(size_t)index];
        if (match.utf16Start < 0 || match.utf16Length <= 0) return 0;
        *utf16Start = match.utf16Start;
        *utf16Length = match.utf16Length;
        return 1;
    }
#endif

    int UniText_GetSystemFontBatchCount(void* handle) {
        if (!handle) return 0;
        return (int)((SystemFontBatch*)handle)->matches.size();
    }

    int UniText_GetSystemFontBatchSource(void* handle, int index,
        const char** sourceKey, const char** postScriptName, const char** filePath,
        int* axisCount, int* usesCoreTextOutlines) {
        if (!handle || !sourceKey || !postScriptName || !filePath
            || !axisCount || !usesCoreTextOutlines) return 0;
        SystemFontBatch* batch = (SystemFontBatch*)handle;
        if (index < 0 || (size_t)index >= batch->matches.size()) return 0;
        const SystemFontBatchMatch& match = batch->matches[(size_t)index];
        if (match.error != 0) return -1;
        if (match.sourceIndex < 0 || (size_t)match.sourceIndex >= batch->sources.size()) return 0;
        const SystemFontBatchSource& source = batch->sources[(size_t)match.sourceIndex];
        *sourceKey = source.key.c_str();
        *postScriptName = source.postScriptName.c_str();
        *filePath = source.filePath.empty() ? NULL : source.filePath.c_str();
        *axisCount = (int)match.axes.size();
        *usesCoreTextOutlines = source.outlineKind == FontOutlineCoreText ? 1 : 0;
        return 1;
    }

    int UniText_GetSystemFontBatchAxis(void* handle, int matchIndex, int axisIndex,
        int* tag, float* value) {
        if (!handle || !tag || !value) return 0;
        SystemFontBatch* batch = (SystemFontBatch*)handle;
        if (matchIndex < 0 || (size_t)matchIndex >= batch->matches.size()) return 0;
        const std::vector<SystemFontBatchAxis>& axes = batch->matches[(size_t)matchIndex].axes;
        if (axisIndex < 0 || (size_t)axisIndex >= axes.size()) return 0;
        *tag = axes[(size_t)axisIndex].tag;
        *value = axes[(size_t)axisIndex].value;
        return 1;
    }

    int UniText_WriteSystemFontBatchSfnt(void* handle, int matchIndex,
        const char* path, int64_t* outLength) {
        if (!handle || !path || !outLength) return -1;
        SystemFontBatch* batch = (SystemFontBatch*)handle;
        if (matchIndex < 0 || (size_t)matchIndex >= batch->matches.size()) return -1;
        int sourceIndex = batch->matches[(size_t)matchIndex].sourceIndex;
        if (sourceIndex < 0 || (size_t)sourceIndex >= batch->sources.size()) return -1;
        @autoreleasepool {
            const SystemFontBatchSource& source = batch->sources[(size_t)sourceIndex];
            return WriteStandaloneSfnt(source.font,
                source.outlineKind == FontOutlineCoreText, path, outLength);
        }
    }

    int UniText_GetSystemFontBatchFaceInfo(void* handle, int matchIndex,
        SystemFontNativeFaceInfo* info, const char** family, const char** style) {
        if (!handle || !info || !family || !style) return 0;
        SystemFontBatch* batch = (SystemFontBatch*)handle;
        if (matchIndex < 0 || (size_t)matchIndex >= batch->matches.size()) return 0;
        int sourceIndex = batch->matches[(size_t)matchIndex].sourceIndex;
        if (sourceIndex < 0 || (size_t)sourceIndex >= batch->sources.size()) return 0;
        const SystemFontBatchSource& source = batch->sources[(size_t)sourceIndex];
        if (source.outlineKind != FontOutlineCoreText) return 0;
        *info = source.faceInfo;
        *family = source.familyName.empty() ? NULL : source.familyName.c_str();
        *style = source.styleName.empty() ? NULL : source.styleName.c_str();
        return 1;
    }

    void* UniText_CreateSystemFontBatchOutlineFace(void* handle, int matchIndex) {
        if (!handle) return NULL;
        SystemFontBatch* batch = (SystemFontBatch*)handle;
        if (matchIndex < 0 || (size_t)matchIndex >= batch->matches.size()) return NULL;
        int sourceIndex = batch->matches[(size_t)matchIndex].sourceIndex;
        if (sourceIndex < 0 || (size_t)sourceIndex >= batch->sources.size()) return NULL;
        const SystemFontBatchSource& source = batch->sources[(size_t)sourceIndex];
        if (source.outlineKind != FontOutlineCoreText || !source.outlineFont) return NULL;
        return (void*)CFRetain(source.outlineFont);
    }

    void* UniText_CreateSystemFontVariation(void* font, const int* tags,
        const int* coordinates, int count) {
        if (!font || !tags || !coordinates || count <= 0) return NULL;
        @autoreleasepool {
            NSMutableDictionary* variations = [NSMutableDictionary dictionaryWithCapacity:(NSUInteger)count];
            for (int i = 0; i < count; i++)
                variations[@(tags[i])] = @((double)coordinates[i] / 65536.0);
            NSDictionary* attributes = @{ (__bridge id)kCTFontVariationAttribute: variations };
            CTFontDescriptorRef descriptor = CTFontDescriptorCreateWithAttributes(
                (__bridge CFDictionaryRef)attributes);
            if (!descriptor) return NULL;
            CTFontRef result = CTFontCreateCopyWithAttributes((CTFontRef)font, 0.0, NULL, descriptor);
            CFRelease(descriptor);
            return (void*)result;
        }
    }

    struct SystemFontOutlineWriter {
        float* curves;
        int* types;
        int* contours;
        int maxCurves;
        int maxContours;
        int curveCount;
        int contourCount;
        int contourStart;
        int error;
        double toleranceSquared;
        bool contourOpen;
        CGPoint start;
        CGPoint current;
    };

    static bool SystemFontSamePoint(CGPoint left, CGPoint right) {
        return left.x == right.x && left.y == right.y;
    }

    static void SystemFontEmitQuadratic(SystemFontOutlineWriter* writer,
        CGPoint start, CGPoint control, CGPoint end) {
        if (writer->error != 0) return;
        if (writer->curveCount >= writer->maxCurves) {
            writer->error = -2;
            return;
        }
        float* curve = writer->curves + writer->curveCount * 8;
        curve[0] = (float)start.x;
        curve[1] = (float)start.y;
        curve[2] = (float)control.x;
        curve[3] = (float)control.y;
        curve[4] = (float)end.x;
        curve[5] = (float)end.y;
        curve[6] = 0.0f;
        curve[7] = 0.0f;
        writer->types[writer->curveCount] = 2;
        writer->curveCount++;
        writer->current = end;
    }

    static void SystemFontEmitLine(SystemFontOutlineWriter* writer, CGPoint end) {
        CGPoint control = CGPointMake(
            (writer->current.x + end.x) * 0.5,
            (writer->current.y + end.y) * 0.5);
        SystemFontEmitQuadratic(writer, writer->current, control, end);
    }

    static void SystemFontEmitCubic(SystemFontOutlineWriter* writer,
        CGPoint p0, CGPoint p1, CGPoint p2, CGPoint p3, int depth) {
        if (writer->error != 0) return;
        double errorX = p3.x - 3.0 * p2.x + 3.0 * p1.x - p0.x;
        double errorY = p3.y - 3.0 * p2.y + 3.0 * p1.y - p0.y;
        double errorSquared = (errorX * errorX + errorY * errorY) / 432.0;
        if (errorSquared <= writer->toleranceSquared || depth >= 8) {
            CGPoint control = CGPointMake(
                (3.0 * p1.x + 3.0 * p2.x - p0.x - p3.x) * 0.25,
                (3.0 * p1.y + 3.0 * p2.y - p0.y - p3.y) * 0.25);
            SystemFontEmitQuadratic(writer, p0, control, p3);
            return;
        }

        CGPoint p01 = CGPointMake((p0.x + p1.x) * 0.5, (p0.y + p1.y) * 0.5);
        CGPoint p12 = CGPointMake((p1.x + p2.x) * 0.5, (p1.y + p2.y) * 0.5);
        CGPoint p23 = CGPointMake((p2.x + p3.x) * 0.5, (p2.y + p3.y) * 0.5);
        CGPoint p012 = CGPointMake((p01.x + p12.x) * 0.5, (p01.y + p12.y) * 0.5);
        CGPoint p123 = CGPointMake((p12.x + p23.x) * 0.5, (p12.y + p23.y) * 0.5);
        CGPoint midpoint = CGPointMake((p012.x + p123.x) * 0.5,
            (p012.y + p123.y) * 0.5);
        SystemFontEmitCubic(writer, p0, p01, p012, midpoint, depth + 1);
        SystemFontEmitCubic(writer, midpoint, p123, p23, p3, depth + 1);
    }

    static void SystemFontFinishContour(SystemFontOutlineWriter* writer) {
        if (!writer->contourOpen || writer->error != 0) return;
        if (!SystemFontSamePoint(writer->current, writer->start))
            SystemFontEmitLine(writer, writer->start);
        if (writer->error != 0) return;
        if (writer->curveCount > writer->contourStart) {
            if (writer->contourCount >= writer->maxContours) {
                writer->error = -3;
                return;
            }
            writer->contours[writer->contourCount++] = writer->curveCount - 1;
        }
        writer->contourOpen = false;
    }

    static void SystemFontApplyPathElement(void* context, const CGPathElement* element) {
        SystemFontOutlineWriter* writer = (SystemFontOutlineWriter*)context;
        if (writer->error != 0) return;
        switch (element->type) {
            case kCGPathElementMoveToPoint:
                SystemFontFinishContour(writer);
                if (writer->error != 0) return;
                writer->contourOpen = true;
                writer->contourStart = writer->curveCount;
                writer->start = element->points[0];
                writer->current = element->points[0];
                break;
            case kCGPathElementAddLineToPoint:
                if (!writer->contourOpen) {
                    writer->error = -1;
                    return;
                }
                SystemFontEmitLine(writer, element->points[0]);
                break;
            case kCGPathElementAddQuadCurveToPoint:
                if (!writer->contourOpen) {
                    writer->error = -1;
                    return;
                }
                SystemFontEmitQuadratic(writer, writer->current,
                    element->points[0], element->points[1]);
                break;
            case kCGPathElementAddCurveToPoint:
                if (!writer->contourOpen) {
                    writer->error = -1;
                    return;
                }
                SystemFontEmitCubic(writer, writer->current,
                    element->points[0], element->points[1], element->points[2], 0);
                break;
            case kCGPathElementCloseSubpath:
                SystemFontFinishContour(writer);
                break;
        }
    }

    int UniText_DecomposeSystemFontGlyph(void* font, uint32_t glyphIndex,
        float* outCurves, int* outTypes, int* outCurveCount, int maxCurves,
        int* outContours, int* outContourCount, int maxContours,
        int* outBearingX, int* outBearingY, int* outAdvanceX,
        int* outWidth, int* outHeight) {
        if (outCurveCount) *outCurveCount = 0;
        if (outContourCount) *outContourCount = 0;
        if (outBearingX) *outBearingX = 0;
        if (outBearingY) *outBearingY = 0;
        if (outAdvanceX) *outAdvanceX = 0;
        if (outWidth) *outWidth = 0;
        if (outHeight) *outHeight = 0;
        if (!font || glyphIndex > 0xFFFF || !outCurves || !outTypes || !outCurveCount
            || !outContours || !outContourCount || maxCurves <= 0 || maxContours <= 0
            || !outBearingX || !outBearingY || !outAdvanceX || !outWidth || !outHeight)
            return -1;

        CTFontRef coreTextFont = (CTFontRef)font;
        if ((CFIndex)glyphIndex >= CTFontGetGlyphCount(coreTextFont)) return -1;
        CGGlyph glyph = (CGGlyph)glyphIndex;
        CGRect bounds = CGRectZero;
        CTFontGetBoundingRectsForGlyphs(coreTextFont, kCTFontOrientationHorizontal,
            &glyph, &bounds, 1);
        CGSize advance = CGSizeZero;
        CTFontGetAdvancesForGlyphs(coreTextFont, kCTFontOrientationHorizontal,
            &glyph, &advance, 1);
        double minX = CGRectGetMinX(bounds);
        double maxY = CGRectGetMaxY(bounds);
        double width = CGRectGetWidth(bounds);
        double height = CGRectGetHeight(bounds);
        if (CGRectIsNull(bounds) || CGRectIsInfinite(bounds)
            || !std::isfinite(minX) || !std::isfinite(maxY)
            || !std::isfinite(width) || !std::isfinite(height)
            || !std::isfinite(advance.width))
            return -1;
        *outBearingX = (int)lround(minX);
        *outBearingY = (int)lround(maxY);
        *outWidth = (int)lround(width);
        *outHeight = (int)lround(height);
        *outAdvanceX = (int)lround(advance.width);

        CGPathRef path = CTFontCreatePathForGlyph(coreTextFont, glyph, NULL);
        if (!path) return CGRectIsEmpty(bounds) ? 0 : -1;

        double unitsPerEm = (double)CTFontGetUnitsPerEm(coreTextFont);
        if (unitsPerEm <= 0.0 || !std::isfinite(unitsPerEm)) {
            CGPathRelease(path);
            return -1;
        }
        double tolerance = unitsPerEm / 1024.0;
        SystemFontOutlineWriter writer = {
            outCurves,
            outTypes,
            outContours,
            maxCurves,
            maxContours,
            0,
            0,
            0,
            0,
            tolerance * tolerance,
            false,
            CGPointZero,
            CGPointZero,
        };
        CGPathApply(path, &writer, SystemFontApplyPathElement);
        SystemFontFinishContour(&writer);
        CGPathRelease(path);
        if (writer.error == 0) {
            *outCurveCount = writer.curveCount;
            *outContourCount = writer.contourCount;
        }
        return writer.error;
    }

    void UniText_ReleaseSystemFont(void* font) {
        if (font) CFRelease((CTFontRef)font);
    }

    void* UniText_RetainSystemFont(void* font) {
        return font ? (void*)CFRetain((CTFontRef)font) : NULL;
    }

    void UniText_ReleaseSystemFontBatch(void* handle) {
        delete (SystemFontBatch*)handle;
    }

    int UniText_RenderSystemEmojiGlyph(
        void* handle,
        uint32_t glyphIndex,
        int pixelSize,
        unsigned char** outPixels,
        int* outWidth,
        int* outHeight,
        int* outBearingX,
        int* outBearingY,
        float* outAdvance
    ) {
        if (!handle || !outPixels || !outWidth || !outHeight
            || !outBearingX || !outBearingY || !outAdvance) return -1;

        *outPixels = NULL;
        *outWidth = 0;
        *outHeight = 0;
        *outBearingX = 0;
        *outBearingY = 0;
        *outAdvance = 0;

        SystemEmojiFace* face = (SystemEmojiFace*)handle;
        CTFontRef sourceFont = NULL;
        CGGlyph glyph = 0;
        if (pixelSize <= 0
            || !ResolveSystemEmojiGlyph(face, glyphIndex, &sourceFont, &glyph)) return -1;
        if (glyph >= CTFontGetGlyphCount(sourceFont)) return -1;

        @autoreleasepool {
            ThreadRenderContext* ctx = GetThreadRenderContext();
            if (!ctx) return -1;

            CTFontRef font = EnsureFont(ctx, sourceFont, (CGFloat)pixelSize);
            if (!font) return -1;

            CGSize advance = CGSizeZero;
            CTFontGetAdvancesForGlyphs(font, kCTFontOrientationHorizontal, &glyph, &advance, 1);
            if (!std::isfinite(advance.width)) return -1;

            CGFloat ascent = CTFontGetAscent(font);
            CGFloat descent = CTFontGetDescent(font);
            CGFloat totalHeight = ascent + descent;
            if (!std::isfinite(ascent) || !std::isfinite(descent) || !std::isfinite(totalHeight)
                || totalHeight <= 0 || totalHeight > (CGFloat)INT_MAX - 2) return -1;

            int renderWidth = (int)ceil(totalHeight) + 2;
            int renderHeight = (int)ceil(totalHeight) + 2;

            if (renderWidth <= 2 || renderHeight <= 2) {
                *outAdvance = (float)advance.width;
                return 0;
            }

            if (renderWidth > MAX_EMOJI_SIZE * 2) renderWidth = MAX_EMOJI_SIZE * 2;
            if (renderHeight > MAX_EMOJI_SIZE * 2) renderHeight = MAX_EMOJI_SIZE * 2;

            CGContextRef context = EnsureContext(ctx, renderWidth, renderHeight);
            if (!context) return -1;

            CGFloat drawX = 1.0;
            CGFloat drawY = 1.0 + descent;
            CGPoint position = CGPointMake(drawX, drawY);
            CGContextSaveGState(context);
            CTFontDrawGlyphs(font, &glyph, &position, 1, context);
            CGContextRestoreGState(context);

            unsigned char* src = ctx->pixelBuffer;
            size_t srcBytesPerRow = ctx->contextWidth * 4;

            int minX = renderWidth, maxX = -1;
            int minY = renderHeight, maxY = -1;

            for (int y = 0; y < renderHeight; y++) {
                unsigned char* row = src + y * srcBytesPerRow;
                for (int x = 0; x < renderWidth; x++) {
                    if (row[x * 4 + 3] > 0) {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < 0 || maxY < 0) {
                *outAdvance = (float)advance.width;
                return 0;
            }

            if (minX > 0) minX--;
            if (minY > 0) minY--;
            if (maxX < renderWidth - 1) maxX++;
            if (maxY < renderHeight - 1) maxY++;

            int croppedWidth = maxX - minX + 1;
            int croppedHeight = maxY - minY + 1;

            int finalBearingX = minX - (int)drawX;
            int finalBearingY = maxY - (int)drawY;

            size_t dataSize = (size_t)croppedWidth * (size_t)croppedHeight * 4;
            if (dataSize > INT_MAX) return -1;
            *outPixels = (unsigned char*)malloc(dataSize);
            if (!*outPixels) return -1;

            unsigned char* dst = *outPixels;
            size_t dstBytesPerRow = croppedWidth * 4;
            for (int y = 0; y < croppedHeight; y++) {
                memcpy(dst + y * dstBytesPerRow,
                       src + (minY + y) * srcBytesPerRow + minX * 4,
                       dstBytesPerRow);
            }

            *outWidth = croppedWidth;
            *outHeight = croppedHeight;
            *outBearingX = finalBearingX;
            *outBearingY = finalBearingY;
            *outAdvance = (float)advance.width;

            return 1;
        }
    }

    struct SystemEmojiShapedGlyph {
        uint32_t glyph;
        int32_t advance;
        int32_t cluster;
    };

    struct SystemEmojiLineOwner {
        CTLineRef line;
        explicit SystemEmojiLineOwner(CTLineRef line) : line(line) {}
        ~SystemEmojiLineOwner() {
            if (line) CFRelease(line);
        }
    };

    int UniText_ShapeSystemEmojiRun(
        void* handle,
        const int32_t* codepoints,
        int codepointCount,
        uint32_t* outGlyphIds,
        int32_t* outAdvances,
        int32_t* outClusters,
        int maxOutput
    ) {
        if (!handle || !codepoints || codepointCount <= 0
            || codepointCount > INT_MAX / 2 || maxOutput < 0) return INT_MIN;

        @autoreleasepool {
                SystemEmojiFace* face = (SystemEmojiFace*)handle;
                std::vector<UniChar> utf16;
                std::vector<int32_t> utf16ToCodepoint;
                utf16.reserve((size_t)codepointCount * 2);
                utf16ToCodepoint.reserve((size_t)codepointCount * 2);
                for (int32_t i = 0; i < codepointCount; i++) {
                    uint32_t codepoint = (uint32_t)codepoints[i];
                    if (!IsValidUnicodeScalar(codepoint)) return INT_MIN;
                    if (codepoint <= 0xFFFF) {
                        utf16.push_back((UniChar)codepoint);
                        utf16ToCodepoint.push_back(i);
                    } else {
                        uint32_t value = codepoint - 0x10000;
                        utf16.push_back((UniChar)(0xD800 + (value >> 10)));
                        utf16ToCodepoint.push_back(i);
                        utf16.push_back((UniChar)(0xDC00 + (value & 0x3FF)));
                        utf16ToCodepoint.push_back(i);
                    }
                }

                NSString* string = [[NSString alloc]
                    initWithCharacters:utf16.data() length:utf16.size()];
                if (!string) return INT_MIN;
                NSDictionary* attributes = @{
                    (__bridge id)kCTFontAttributeName: (__bridge id)face->font
                };
                NSAttributedString* attributedString = [[NSAttributedString alloc]
                    initWithString:string attributes:attributes];
                if (!attributedString) return INT_MIN;
                NSDictionary* options = @{
                    (__bridge id)kCTTypesetterOptionAllowUnboundedLayout: @YES
                };
                CTTypesetterRef typesetter = CTTypesetterCreateWithAttributedStringAndOptions(
                    (__bridge CFAttributedStringRef)attributedString,
                    (__bridge CFDictionaryRef)options);
                if (!typesetter) return INT_MIN;
                SystemEmojiLineOwner lineOwner(
                    CTTypesetterCreateLine(typesetter,
                        CFRangeMake(0, (CFIndex)utf16.size())));
                CFRelease(typesetter);
                CTLineRef line = lineOwner.line;
                if (!line) return INT_MIN;

                std::vector<SystemEmojiShapedGlyph> shaped;
                CFIndex lineGlyphCount = CTLineGetGlyphCount(line);
                if (lineGlyphCount < 0 || lineGlyphCount > INT_MAX) return INT_MIN;
                shaped.reserve((size_t)lineGlyphCount);
                bool valid = true;
                CFArrayRef runs = CTLineGetGlyphRuns(line);
                if (!runs) return INT_MIN;
                CFIndex runCount = CFArrayGetCount(runs);
                for (CFIndex runIndex = 0; runIndex < runCount && valid; runIndex++) {
                    CTRunRef run = (CTRunRef)CFArrayGetValueAtIndex(runs, runIndex);
                    if (!run) {
                        valid = false;
                        break;
                    }
                    CFDictionaryRef runAttributes = CTRunGetAttributes(run);
                    if (!runAttributes) {
                        valid = false;
                        break;
                    }
                    CFTypeRef runFontValue = CFDictionaryGetValue(runAttributes, kCTFontAttributeName);
                    if (!runFontValue || CFGetTypeID(runFontValue) != CTFontGetTypeID()) {
                        valid = false;
                        break;
                    }
                    CTFontRef runFont = (CTFontRef)runFontValue;
                    bool primaryGlyphs = CFEqual(runFont, face->font);
                    CFIndex runFontGlyphCount = CTFontGetGlyphCount(runFont);
                    if (runFontGlyphCount <= 0 || runFontGlyphCount > (CFIndex)UINT16_MAX + 1) {
                        valid = false;
                        break;
                    }

                    CFIndex glyphCount = CTRunGetGlyphCount(run);
                    if (glyphCount < 0 || glyphCount > INT_MAX
                        || shaped.size() + (size_t)glyphCount > INT_MAX) {
                        valid = false;
                        break;
                    }
                    if (glyphCount == 0) continue;
                    SystemEmojiRunFontIdentity runFontIdentity(primaryGlyphs ? NULL : runFont);
                    if (!primaryGlyphs && !runFontIdentity.descriptor) {
                        valid = false;
                        break;
                    }

                    std::vector<CGGlyph> glyphStorage;
                    std::vector<CGSize> advanceStorage;
                    std::vector<CFIndex> indexStorage;
                    const CGGlyph* glyphs = CTRunGetGlyphsPtr(run);
                    const CGSize* advances = CTRunGetAdvancesPtr(run);
                    const CFIndex* indices = CTRunGetStringIndicesPtr(run);
                    if (!glyphs) {
                        glyphStorage.resize((size_t)glyphCount);
                        CTRunGetGlyphs(run, CFRangeMake(0, 0), glyphStorage.data());
                        glyphs = glyphStorage.data();
                    }
                    if (!advances) {
                        advanceStorage.resize((size_t)glyphCount);
                        CTRunGetAdvances(run, CFRangeMake(0, 0), advanceStorage.data());
                        advances = advanceStorage.data();
                    }
                    if (!indices) {
                        indexStorage.resize((size_t)glyphCount);
                        CTRunGetStringIndices(run, CFRangeMake(0, 0), indexStorage.data());
                        indices = indexStorage.data();
                    }

                    CFRange runStringRange = CTRunGetStringRange(run);
                    CFIndex utf16Count = (CFIndex)utf16ToCodepoint.size();
                    bool validRunStringRange = runStringRange.location >= 0
                        && runStringRange.length > 0
                        && runStringRange.location < utf16Count
                        && runStringRange.length <= utf16Count - runStringRange.location;
                    for (CFIndex glyphIndex = 0; glyphIndex < glyphCount; glyphIndex++) {
                        CGGlyph glyph = glyphs[glyphIndex];
                        if (glyph == 0) continue;
                        CGFloat advance = advances[glyphIndex].width;
                        CFIndex utf16Index = indices[glyphIndex];
                        if (utf16Index < 0 || utf16Index >= utf16Count) {
                            if (!validRunStringRange) {
                                valid = false;
                                break;
                            }
                            utf16Index = runStringRange.location;
                        }
                        uint32_t mappedGlyph = glyph;
                        CFIndex glyphSpaceCount = primaryGlyphs
                            ? (CFIndex)face->primaryGlyphCount : runFontGlyphCount;
                        if (glyph >= glyphSpaceCount || !std::isfinite(advance)
                            || advance < (CGFloat)INT32_MIN || advance > (CGFloat)INT32_MAX) {
                            valid = false;
                            break;
                        }
                        if (!primaryGlyphs && !RegisterSubstitutedSystemEmojiGlyph(
                                face, runFont, runFontIdentity, glyph, &mappedGlyph)) {
                            valid = false;
                            break;
                        }
                        shaped.push_back({
                            mappedGlyph,
                            (int32_t)lround(advance),
                            utf16ToCodepoint[(size_t)utf16Index]
                        });
                    }
                }
                if (!valid) return INT_MIN;

                int required = (int)shaped.size();
                if (required > maxOutput) return -required;
                if (required == 0) return 0;
                if (!outGlyphIds || !outAdvances || !outClusters) return INT_MIN;
                for (int i = 0; i < required; i++) {
                    outGlyphIds[i] = shaped[(size_t)i].glyph;
                    outAdvances[i] = shaped[(size_t)i].advance;
                    outClusters[i] = shaped[(size_t)i].cluster;
                }
            return required;
        }
    }

} // extern "C"
