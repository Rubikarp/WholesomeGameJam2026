// Shared helpers for the UniText Apple plugins: host-view lookup and coordinate
// conversion (iOS only) plus the IME clause extraction both the iOS and macOS
// input plugins share (the macOS source includes this header from Plugins/macOS~).
#pragma once
#include <TargetConditionals.h>
#include <math.h>
#import <Foundation/Foundation.h>

#if TARGET_OS_IPHONE
#import <UIKit/UIKit.h>
#else
#import <AppKit/AppKit.h>
#endif

#if !__has_feature(objc_arc)
#error UniText Apple plugin sources require ARC (Unity's generated Xcode project and Plugins/macOS~/build.command enable it).
#endif

#if TARGET_OS_IPHONE

#ifdef __cplusplus
extern "C" {
#endif

UIView* UnityGetGLView(void);

#ifdef __cplusplus
}
#endif

#endif // TARGET_OS_IPHONE

#if TARGET_OS_IPHONE

// ============================================================================
// COORDINATE CONVENTION — the single source of truth for every UniText iOS
// geometry bridge (input, paste control).
//
//   Unity side: screen PIXELS, origin bottom-left, Y-up. A rect's y is its
//   BOTTOM edge.
//   UIKit side: POINTS in the Unity GL host view's coordinate space, origin
//   top-left, Y-down.
//
// Conversion always divides by the host view's contentScaleFactor (never
// [UIScreen mainScreen].scale — they diverge under Resolution Scaling and iPad
// Stage Manager) and always flips Y against the host view's bounds height.
// Views positioned away from the host origin must additionally convert through
// -convertRect:/-convertPoint: — never assume view-local == host-local.
// ============================================================================

static inline UIView* UniTextHostView(void) {
    UIView* glView = UnityGetGLView();
    if (glView) return glView;

    UIWindow* window = nil;
    if (@available(iOS 15.0, *)) {
        for (UIScene* scene in [UIApplication.sharedApplication.connectedScenes allObjects]) {
            if ([scene isKindOfClass:[UIWindowScene class]]) {
                window = ((UIWindowScene*)scene).windows.firstObject;
                break;
            }
        }
    }
    if (!window) {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
        window = [UIApplication sharedApplication].keyWindow;
#pragma clang diagnostic pop
    }
    return window.rootViewController.view;
}

static inline CGFloat UniTextHostScale(UIView* host) {
    CGFloat scale = host ? host.contentScaleFactor : [UIScreen mainScreen].scale;
    return isfinite(scale) && scale > 0 ? scale : 1;
}

static inline CGFloat UniTextHostHeightPt(UIView* host) {
    return host ? host.bounds.size.height : [UIScreen mainScreen].bounds.size.height;
}

static inline CGPoint UniTextUnityToHostPoint(float unityX, float unityY) {
    UIView* host = UniTextHostView();
    CGFloat scale = UniTextHostScale(host);
    return CGPointMake(unityX / scale, UniTextHostHeightPt(host) - unityY / scale);
}

static inline void UniTextHostPointToUnity(CGPoint hostPt, float* outX, float* outY) {
    UIView* host = UniTextHostView();
    CGFloat scale = UniTextHostScale(host);
    *outX = (float)(hostPt.x * scale);
    *outY = (float)((UniTextHostHeightPt(host) - hostPt.y) * scale);
}

static inline BOOL UniTextTryUnityRectToHost(float unityX, float unityBottom,
                                              float unityW, float unityH,
                                              CGRect* outRect) {
    if (!outRect || !isfinite(unityX) || !isfinite(unityBottom) ||
        !isfinite(unityW) || !isfinite(unityH)) return NO;

    UIView* host = UniTextHostView();
    CGFloat scale = UniTextHostScale(host);
    CGFloat hPt = unityH / scale;
    CGRect rect = CGRectMake(unityX / scale,
                             UniTextHostHeightPt(host) - unityBottom / scale - hPt,
                             unityW / scale, hPt);
    if (!isfinite(rect.origin.x) || !isfinite(rect.origin.y) ||
        !isfinite(rect.size.width) || !isfinite(rect.size.height)) return NO;
    *outRect = rect;
    return YES;
}

#endif // TARGET_OS_IPHONE

static inline void UniTextRunOnMain(dispatch_block_t block) {
    if (NSThread.isMainThread) block();
    else dispatch_async(dispatch_get_main_queue(), block);
}

// ============================================================================
// IME CLAUSE EXTRACTION — shared by the iOS (UITextInput setMarkedText:) and
// macOS (NSTextInputClient setMarkedText:) plugins.
//
// A marked (preedit) NSAttributedString carries clause boundaries as runs of
// the NSMarkedClauseSegment attribute; the clause's visual role is encoded in
// underline weight / background color, and the clause overlapping the IME's
// selectedRange is the target (active) clause. Style ints match the C#
// CompositionClauseStyle enum.
// ============================================================================

// Runtime value of AppKit's NSMarkedClauseSegmentAttributeName; UIKit ships the
// same attribute without a public constant.
static NSString* const UniTextMarkedClauseSegmentAttribute = @"NSMarkedClauseSegment";

// CompositionClauseStyle values — must match the C# enum.
enum {
    UniTextClauseUnconverted        = 0,
    UniTextClauseTargetConverted    = 1,
    UniTextClauseConverted          = 2,
    UniTextClauseTargetNotConverted = 3,
    UniTextClauseError              = 4,
};

/// Maps one clause's attributes (underline weight, background highlight) plus
/// selection overlap to a CompositionClauseStyle int.
static inline int UniTextClauseStyleForRange(NSAttributedString* attrStr,
                                             NSRange range,
                                             NSRange selectedRange) {
    if (range.length == 0) return UniTextClauseUnconverted;

    BOOL isTarget = NO;
    if (selectedRange.location != NSNotFound && selectedRange.length > 0) {
        NSUInteger selStart = selectedRange.location;
        NSUInteger selEnd = selStart + selectedRange.length;
        NSUInteger clauseEnd = range.location + range.length;
        isTarget = (selStart < clauseEnd && selEnd > range.location);
    }

    NSDictionary* attrs = [attrStr attributesAtIndex:range.location effectiveRange:NULL];

    if (attrs[NSBackgroundColorAttributeName] != nil) {
        // Background highlight = target clause being converted.
        return UniTextClauseTargetConverted;
    }

    NSNumber* underlineStyle = attrs[NSUnderlineStyleAttributeName];
    if (underlineStyle) {
        NSInteger style = underlineStyle.integerValue;
        NSInteger pattern = style & 0xFF;

        if (pattern == NSUnderlineStyleThick || (style & NSUnderlineStyleThick) != 0)
            return isTarget ? UniTextClauseTargetConverted : UniTextClauseTargetNotConverted;

        if (pattern == NSUnderlineStyleSingle || (style & NSUnderlineStyleSingle) != 0)
            return isTarget ? UniTextClauseTargetConverted : UniTextClauseUnconverted;

        if (pattern == NSUnderlineStyleDouble)
            return UniTextClauseConverted;

        if ((style & NSUnderlineStylePatternDot) || (style & NSUnderlineStylePatternDash))
            return UniTextClauseError;
    }

    // No underline — likely already converted.
    return isTarget ? UniTextClauseTargetConverted : UniTextClauseConverted;
}

/// Walks the NSMarkedClauseSegment runs of a marked string into caller-provided
/// C arrays: outOffsets receives [start, end] pairs (2*maxClauses capacity),
/// outStyles one style int per clause. Returns the clause count; 0 when the
/// string carries no clause attribute (caller applies its single-clause fallback).
static inline int UniTextExtractClauses(NSAttributedString* attrStr,
                                        NSRange selectedRange,
                                        int maxClauses,
                                        int* outOffsets,
                                        int* outStyles) {
    NSUInteger textLen = attrStr.length;
    if (textLen == 0 || maxClauses <= 0) return 0;

    __block int clauseCount = 0;
    __block int prevSegmentValue = -1;
    __block NSUInteger clauseStart = 0;

    [attrStr enumerateAttribute:UniTextMarkedClauseSegmentAttribute
                        inRange:NSMakeRange(0, textLen)
                        options:0
                     usingBlock:^(id value, NSRange range, BOOL* stop)
    {
        int segValue = value ? [(NSNumber*)value intValue] : 0;
        if (segValue == prevSegmentValue) return;

        if (prevSegmentValue >= 0 && clauseCount < maxClauses) {
            outOffsets[clauseCount * 2] = (int)clauseStart;
            outOffsets[clauseCount * 2 + 1] = (int)range.location;
            outStyles[clauseCount] = UniTextClauseStyleForRange(
                attrStr, NSMakeRange(clauseStart, range.location - clauseStart), selectedRange);
            clauseCount++;
        }
        clauseStart = range.location;
        prevSegmentValue = segValue;

        if (clauseCount >= maxClauses) *stop = YES;
    }];

    if (prevSegmentValue >= 0 && clauseCount < maxClauses) {
        outOffsets[clauseCount * 2] = (int)clauseStart;
        outOffsets[clauseCount * 2 + 1] = (int)textLen;
        outStyles[clauseCount] = UniTextClauseStyleForRange(
            attrStr, NSMakeRange(clauseStart, textLen - clauseStart), selectedRange);
        clauseCount++;
    }

    return clauseCount;
}
