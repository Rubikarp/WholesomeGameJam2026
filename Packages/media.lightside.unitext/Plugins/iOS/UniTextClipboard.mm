#import <UIKit/UIKit.h>
#import <string.h>

#if !__has_feature(objc_arc)
#error UniText iOS plugin sources require ARC (Unity's generated Xcode project enables it by default).
#endif

// ============================================================================
// UniText Clipboard — iOS (UIPasteboard)
// ============================================================================
//
// Provides clipboard access for UniText InputField on iOS.
// Uses UIPasteboard.generalPasteboard for plain text operations.
//
// String lifetime: returned char* pointers are allocated via strdup().
// The C# caller MUST free them via UniTextClipboard_FreeString().
// ============================================================================

extern "C" {

    /// Returns the current clipboard text as a UTF-8 C string, or NULL if empty.
    /// Caller MUST free the returned pointer via UniTextClipboard_FreeString().
    const char* UniTextClipboard_GetText() {
        @autoreleasepool {
            UIPasteboard *pasteboard = [UIPasteboard generalPasteboard];
            NSString *text = pasteboard.string;

            if (text == nil || text.length == 0)
                return NULL;

            return strdup([text UTF8String]);
        }
    }

    /// Sets the clipboard to the given UTF-8 text.
    /// Passing NULL or an empty string clears the clipboard — assigning @"" would
    /// leave an empty-text item that keeps hasStrings reporting true.
    void UniTextClipboard_SetText(const char* text) {
        @autoreleasepool {
            UIPasteboard *pasteboard = [UIPasteboard generalPasteboard];

            if (text == NULL || strlen(text) == 0) {
                pasteboard.items = @[];
                return;
            }

            pasteboard.string = [NSString stringWithUTF8String:text];
        }
    }

    /// Returns 1 if the clipboard contains text, 0 otherwise.
    int UniTextClipboard_HasText(void) {
        @autoreleasepool {
            UIPasteboard *pasteboard = [UIPasteboard generalPasteboard];
            return pasteboard.hasStrings ? 1 : 0;
        }
    }

    int UniTextClipboard_HasContent(void) {
        @autoreleasepool {
            return [UIPasteboard generalPasteboard].numberOfItems > 0 ? 1 : 0;
        }
    }

    /// Frees a string previously returned by UniTextClipboard_GetText().
    void UniTextClipboard_FreeString(const char* ptr) {
        if (ptr != NULL) {
            free((void*)ptr);
        }
    }

    // ========================================================================
    // Multi-format support — HTML / Markdown / URL alongside plain text.
    // Canonical MIME identifiers from C# (text/plain, text/html, text/markdown,
    // text/uri-list) are mapped to platform UTIs at the boundary. UIPasteboard
    // accepts arbitrary UTI strings; for cross-app interop we use Apple's
    // canonical constants (public.html, public.utf8-plain-text, public.url) and
    // John Gruber's de-facto Markdown UTI (net.daringfireball.markdown).
    // ========================================================================

    /// Maps a canonical clipboard identifier (MIME-style) to the UTI string the
    /// pasteboard expects. Built-in formats are remapped to their well-known
    /// Apple UTIs; anything else is passed through as the UTI itself — this is
    /// how UniText supports ClipboardFormat.Custom(identifier) for integrator
    /// app-private formats without per-format wiring on the iOS side.
    static NSString* MapIdentifierToUti(const char* identifier) {
        if (identifier == NULL || identifier[0] == '\0') return nil;
        if (strcmp(identifier, "text/plain") == 0)    return (NSString*)@"public.utf8-plain-text";
        if (strcmp(identifier, "text/html") == 0)     return (NSString*)@"public.html";
        if (strcmp(identifier, "text/markdown") == 0) return (NSString*)@"net.daringfireball.markdown";
        if (strcmp(identifier, "text/uri-list") == 0) return (NSString*)@"public.url";
        if (strcmp(identifier, "application/vnd.lightside.unitext") == 0)
            return (NSString*)@"com.lightside.unitext";
        if (strcmp(identifier, "image/png") == 0)  return (NSString*)@"public.png";
        if (strcmp(identifier, "image/jpeg") == 0) return (NSString*)@"public.jpeg";
        if (strcmp(identifier, "image/gif") == 0)  return (NSString*)@"com.compuserve.gif";
        return [NSString stringWithUTF8String:identifier];
    }

    /// Reads a non-plain-text format from the general pasteboard. Returns strdup'd
    /// UTF-8 C string (caller frees via UniTextClipboard_FreeString) or NULL when
    /// the format is not present.
    const char* UniTextClipboard_GetFormat(const char* identifier) {
        @autoreleasepool {
            NSString* uti = MapIdentifierToUti(identifier);
            if (uti == nil) return NULL;

            UIPasteboard* pasteboard = [UIPasteboard generalPasteboard];
            id value = [pasteboard valueForPasteboardType:uti];

            NSString* text = nil;
            if ([value isKindOfClass:[NSString class]]) {
                text = (NSString*)value;
            } else if ([value isKindOfClass:[NSData class]]) {
                text = [[NSString alloc] initWithData:(NSData*)value encoding:NSUTF8StringEncoding];
            } else if ([value isKindOfClass:[NSURL class]]) {
                text = [(NSURL*)value absoluteString];
            }

            if (text == nil || text.length == 0) return NULL;
            return strdup([text UTF8String]);
        }
    }

    /// Multi-format atomic write. Builds a SINGLE NSDictionary mixing (UTI, NSString)
    /// text pairs and one optional (UTI, NSData) image entry, and assigns it as the
    /// pasteboard's only item — UIPasteboard stores them all simultaneously, consumer
    /// paste picks the richest format it understands. imageFormat may be NULL when the
    /// write carries no image.
    void UniTextClipboard_SetItems(const char** formats, const char** payloads, int count,
                                   const char* imageFormat, const void* imageBytes, int imageLength) {
        bool hasTexts = count > 0 && formats != NULL && payloads != NULL;
        bool hasImage = imageFormat != NULL && imageBytes != NULL && imageLength > 0;
        if (!hasTexts && !hasImage) return;
        @autoreleasepool {
            NSMutableDictionary* dict = [NSMutableDictionary dictionaryWithCapacity:(NSUInteger)(count + 1)];
            if (hasTexts) {
                for (int i = 0; i < count; i++) {
                    NSString* uti = MapIdentifierToUti(formats[i]);
                    if (uti == nil) continue;

                    const char* payload = payloads[i];
                    NSString* text = payload != NULL
                        ? [NSString stringWithUTF8String:payload]
                        : @"";
                    if (text == nil) text = @"";
                    dict[uti] = text;
                }
            }
            if (hasImage) {
                NSString* uti = MapIdentifierToUti(imageFormat);
                if (uti != nil)
                    dict[uti] = [NSData dataWithBytes:imageBytes length:(NSUInteger)imageLength];
            }
            if (dict.count == 0) return;
            UIPasteboard* pasteboard = [UIPasteboard generalPasteboard];
            pasteboard.items = @[ dict ];
        }
    }

    // Binary media: bytes cross the boundary as pointer + length (a UTF-8 string would truncate at the
    // first NUL). The read buffer is malloc'd; the C# caller frees it via UniTextClipboard_FreeData.
    const void* UniTextClipboard_GetData(const char* identifier, int* outLength) {
        if (outLength) *outLength = 0;
        @autoreleasepool {
            NSString* uti = MapIdentifierToUti(identifier);
            if (uti == nil) return NULL;
            NSData* data = [[UIPasteboard generalPasteboard] dataForPasteboardType:uti];
            if (data == nil || data.length == 0) return NULL;
            NSUInteger len = data.length;
            void* buf = malloc(len);
            if (buf == NULL) return NULL;
            memcpy(buf, data.bytes, len);
            if (outLength) *outLength = (int)len;
            return buf;
        }
    }

    void UniTextClipboard_SetData(const char* identifier, const void* bytes, int length) {
        if (length < 0 || (length > 0 && bytes == NULL)) return;
        @autoreleasepool {
            NSString* uti = MapIdentifierToUti(identifier);
            if (uti == nil) return;
            NSData* data = [NSData dataWithBytes:bytes length:(NSUInteger)length];
            [[UIPasteboard generalPasteboard] setData:data forPasteboardType:uti];
        }
    }

    void UniTextClipboard_FreeData(const void* ptr) {
        if (ptr != NULL) free((void*)ptr);
    }

    int UniTextClipboard_HasFormatData(const char* identifier) {
        @autoreleasepool {
            NSString* uti = MapIdentifierToUti(identifier);
            if (uti == nil) return 0;
            return [[UIPasteboard generalPasteboard] containsPasteboardTypes:@[uti]] ? 1 : 0;
        }
    }

    // Presence probe stays hasURLs-only: reading .URLs here would trigger the
    // iOS paste notification for a boolean, so a web link may report true while
    // GetFiles (file URLs only) returns NULL — callers must handle NULL.
    int UniTextClipboard_HasFiles(void) {
        @autoreleasepool { return [UIPasteboard generalPasteboard].hasURLs ? 1 : 0; }
    }

    /// File attachments only — web links are not files (matches the desktop
    /// CF_HDROP / NSFilenamesPboardType semantics of this API).
    const char* UniTextClipboard_GetFiles(void) {
        @autoreleasepool {
            NSArray<NSURL*>* urls = [UIPasteboard generalPasteboard].URLs;
            if (urls == nil || urls.count == 0) return NULL;
            NSMutableString* joined = [NSMutableString string];
            for (NSURL* u in urls) {
                if (!u.isFileURL) continue;
                if (joined.length > 0) [joined appendString:@"\n"];
                [joined appendString:[u absoluteString]];
            }
            if (joined.length == 0) return NULL;
            return strdup([joined UTF8String]);
        }
    }

} // extern "C"
