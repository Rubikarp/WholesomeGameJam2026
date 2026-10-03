#pragma once

#import <UIKit/UIKit.h>

/** Single-line authority-preserving replica accepted from presenter creation callbacks. */
@interface UniTextReplicaTextField : UITextField
@end

/** Multi-line authority-preserving replica accepted from presenter creation callbacks. */
@interface UniTextReplicaTextView : UITextView
@end

/**
 * The return key the field declares, mirroring the managed ReturnKeyType. Default and Enter declare
 * no action, so the key is free to insert a line break; the rest name both what the key does and
 * how a presenter should label the control standing in for it.
 */
typedef NS_ENUM(int, UniTextNativeFieldReturnKey) {
    UniTextNativeFieldReturnKeyDefault = 0,
    UniTextNativeFieldReturnKeyGo,
    UniTextNativeFieldReturnKeySearch,
    UniTextNativeFieldReturnKeySend,
    UniTextNativeFieldReturnKeyNext,
    UniTextNativeFieldReturnKeyPrevious,
    UniTextNativeFieldReturnKeyDone,
    UniTextNativeFieldReturnKeyEnter,
};

/** Whether the return key commits the field rather than moving focus or declaring nothing. */
static inline BOOL UniTextNativeFieldReturnKeyCommits(UniTextNativeFieldReturnKey key) {
    return key == UniTextNativeFieldReturnKeyGo || key == UniTextNativeFieldReturnKeySearch
        || key == UniTextNativeFieldReturnKeySend || key == UniTextNativeFieldReturnKeyDone;
}

/** Read-only presentation snapshot whose string pointers remain valid only during the main-thread callback. */
typedef struct UniTextNativeFieldContext {
    int sessionId;
    /** Whether the editor wraps its text; with acceptsNewlines it selects the multi-line control. */
    int wraps;
    /** Whether the editor accepts line breaks; false leaves the return key free for its action. */
    int acceptsNewlines;
    /**
     * The return key the field declares. A field that accepts line breaks spends that key on them,
     * so a presenter that wants the declared action reachable has to surface it itself.
     */
    UniTextNativeFieldReturnKey returnKey;
    int secureTextEntry;
    int readOnly;
    int copyAllowed;
    const char* presenterId;
    const char* placeholder;
    const char* identifier;
    const char* presenterData;
} UniTextNativeFieldContext;

/** Returns a compatible replica and optionally its owning root; an invalid hierarchy rejects field creation. */
typedef UIView* (*UniTextNativeFieldPresenterCreateCallback)(
    const UniTextNativeFieldContext* context, UIView* hostView, UIView** containerPtr);
/** Applies current appearance state without replacing or detaching the retained field hierarchy. */
typedef void (*UniTextNativeFieldPresenterUpdateCallback)(
    const UniTextNativeFieldContext* context, UIView* nativeField, UIView* container);
/** Lays out the retained field hierarchy from current host, safe-area, and keyboard geometry. */
typedef void (*UniTextNativeFieldPresenterLayoutCallback)(
    const UniTextNativeFieldContext* context, UIView* nativeField, UIView* container,
    CGRect hostBounds, UIEdgeInsets safeAreaInsets, CGRect suggestedFieldFrame,
    CGFloat keyboardTop);
/** Releases presenter-owned resources; UniText then removes the retained hierarchy. */
typedef void (*UniTextNativeFieldPresenterDestroyCallback)(
    const UniTextNativeFieldContext* context, UIView* nativeField, UIView* container);

#ifdef __cplusplus
extern "C" {
#endif

/** Retains main-thread callbacks until replacement or removal and returns zero for an invalid, reserved, or currently active identifier. */
int UniTextNativeInput_RegisterNativeFieldPresenter(
    const char* presenterId,
    UniTextNativeFieldPresenterCreateCallback create,
    UniTextNativeFieldPresenterUpdateCallback update,
    UniTextNativeFieldPresenterLayoutCallback layout,
    UniTextNativeFieldPresenterDestroyCallback destroy);

/** Removes a presenter synchronously and returns zero when its identifier is invalid, reserved, absent, or currently active. */
int UniTextNativeInput_UnregisterNativeFieldPresenter(const char* presenterId);

/** Dispatches one supported action and returns zero when the action or matching active unfrozen session is unavailable. */
int UniTextNativeInput_PerformNativeFieldAction(int sessionId, const char* action);

#ifdef __cplusplus
}
#endif
