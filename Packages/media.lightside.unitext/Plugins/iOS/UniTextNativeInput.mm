#import "UniTextNativeInput.h"
#import "UniTextAppleCommon.h"
#include <limits.h>

typedef void (*TextInputCallback)(const char* text);
typedef void (*TextReplacementCallback)(int contextVersion, int charStart, int charLength, const char* text);
typedef void (*KeyDownCallback)(int keyCode, int modifiers);
typedef void (*CompositionCallback)(const char* text, const int* clauseStarts, const int* clauseEnds, const int* clauseStyles, int clauseCount, int cursorPos);
typedef void (*CompositionEndedCallback)(void);
typedef void (*KeyboardEventCallback)(int phase, float x, float y, float w, float h,
                                       float animationDuration, int easing, float animationFraction);
typedef void (*NativeFieldEditCallback)(int sessionId, int nativeRevision, int authorityRevision,
                                        int rangeStart, int rangeLength, const char* replacement,
                                        int selectionStart, int selectionEnd);
typedef void (*NativeFieldCompositionCallback)(int sessionId, int nativeRevision,
                                               int authorityRevision, int phase,
                                               int replacementStart, int replacementLength,
                                               const char* compositionText, int cursorPosition);
typedef void (*NativeFieldSelectionCallback)(int sessionId, int nativeRevision,
                                             int authorityRevision,
                                             int selectionStart, int selectionEnd);
typedef void (*NativeFieldActionCallback)(int sessionId, int nativeRevision,
                                          int authorityRevision, const char* action,
                                          int modifiers);
typedef void (*NativeInputQuiescedCallback)(int sessionId, int nativeRevision,
                                            int authorityRevision, int requestId);
typedef void (*NativeInputFaultCallback)(int sessionId, const char* message);
typedef void (*FloatingCursorPointCallback)(float screenX, float screenY);

typedef void (*SetSelectionCharRangeCallback)(int charStart, int charLength);

typedef int (*GetCharRangeRectCallback)(int charStart, int charLength, float* outRect);

typedef int (*ClosestCharAtPointCallback)(float screenX, float screenY);

typedef int (*WritingDirectionCallback)(int charIndex);

typedef struct {
    int keyboardType;
    int returnKeyType;
    int returnKey;
    int autoCapitalization;
    int autoCorrection;
    int spellChecking;
    int secureTextEntry;
    int autofillHint;
    int smartQuotes;
    int smartDashes;
    int smartInsertDelete;
    int enablesReturnKeyAuto;
    int showSoftwareKeyboard;
    int useNativeField;
    int wraps;
    int acceptsNewlines;
    int readOnly;
    int copyAllowed;
    int selectionStart;
    int selectionEnd;
    int sessionId;
    int authorityRevision;
    const char* passwordRules;
    const char* initialText;
    const char* accessibilityIdentifier;
    const char* placeholder;
    const char* presenterId;
    const char* presenterData;
} UniTextShowKeyboardArgs;

/** Whether the field's line facts require the multi-line control: a wrapping field needs one even
    when it accepts no line breaks. */
static BOOL UniTextArgsAreMultiLine(const UniTextShowKeyboardArgs* args) {
    return args->wraps != 0 || args->acceptsNewlines != 0;
}

static BOOL UniTextInputViewsDiffer(const UniTextShowKeyboardArgs* previous,
                                    const UniTextShowKeyboardArgs* next,
                                    NSString* previousPasswordRules,
                                    NSString* nextPasswordRules) {
    BOOL samePasswordRules = previousPasswordRules == nextPasswordRules
        || [previousPasswordRules isEqualToString:nextPasswordRules];
    return previous->keyboardType != next->keyboardType
        || previous->returnKeyType != next->returnKeyType
        || previous->autoCapitalization != next->autoCapitalization
        || previous->autoCorrection != next->autoCorrection
        || previous->spellChecking != next->spellChecking
        || previous->secureTextEntry != next->secureTextEntry
        || previous->autofillHint != next->autofillHint
        || previous->smartQuotes != next->smartQuotes
        || previous->smartDashes != next->smartDashes
        || previous->smartInsertDelete != next->smartInsertDelete
        || previous->enablesReturnKeyAuto != next->enablesReturnKeyAuto
        || !samePasswordRules;
}

enum {
    KBP_WillShow          = 0,
    KBP_AnimationProgress = 1,
    KBP_DidShow           = 2,
    KBP_WillHide          = 3,
    KBP_DidHide           = 4,
    KBP_WillChangeFrame   = 5,
};

enum {
    KBE_Linear    = 0,
    KBE_EaseIn    = 1,
    KBE_EaseOut   = 2,
    KBE_EaseInOut = 3,
};

enum {
    NCP_Update = 0,
    NCP_End    = 1,
    NCP_Cancel = 2,
};

static TextInputCallback              s_onTextInput           = NULL;
static TextReplacementCallback        s_onTextReplacement     = NULL;
static KeyDownCallback                s_onKeyDown             = NULL;
static CompositionCallback            s_onComposition         = NULL;
static CompositionEndedCallback       s_onCompositionEnded    = NULL;
static KeyboardEventCallback          s_onKeyboardEvent       = NULL;
static NativeFieldEditCallback        s_onNativeFieldEdit      = NULL;
static NativeFieldCompositionCallback s_onNativeFieldComposition = NULL;
static NativeFieldSelectionCallback   s_onNativeFieldSelection = NULL;
static NativeFieldActionCallback      s_onNativeFieldAction    = NULL;
static NativeInputQuiescedCallback    s_onNativeInputQuiesced  = NULL;
static NativeInputFaultCallback       s_onNativeInputFault     = NULL;
static FloatingCursorPointCallback    s_onFloatingCursorPoint = NULL;
static SetSelectionCharRangeCallback  s_setSelectionCharRange = NULL;
static GetCharRangeRectCallback       s_getCharRangeRect      = NULL;
static ClosestCharAtPointCallback     s_closestCharAtPoint    = NULL;
static WritingDirectionCallback       s_writingDirection      = NULL;

static int UniTextBeginTransparentCallback(void);
static void UniTextEndTransparentCallback(int revision);

enum {
    NKC_A = 0, NKC_B, NKC_C, NKC_D, NKC_E, NKC_F, NKC_H, NKC_K,
    NKC_N, NKC_P, NKC_T, NKC_V, NKC_X, NKC_Y, NKC_Z,
    NKC_LeftArrow, NKC_RightArrow, NKC_UpArrow, NKC_DownArrow,
    NKC_Home, NKC_End, NKC_PageUp, NKC_PageDown,
    NKC_Backspace, NKC_Delete, NKC_Return, NKC_KeypadEnter,
    NKC_Tab, NKC_Escape,
    NKC_Insert = 29, NKC_None = 30, NKC_I = 31, NKC_U = 32, NKC_Backslash = 33,
    NKC_G = 34, NKC_J, NKC_L, NKC_M, NKC_O, NKC_Q, NKC_R, NKC_S, NKC_W,
};

@interface UniTextTextKitInputView : UITextView <UITextViewDelegate>
{
    NSString* _compositionOriginalText;
    NSInteger _windowStart;
    int _contextVersion;
    CGRect _caretRect;
    BOOL _syncing;
    BOOL _editingText;
    BOOL _compositionActive;
    BOOL _hasPendingEdit;
    BOOL _hasCompositionSnapshot;
    BOOL _hasContext;
    NSRange _pendingEditRange;
    NSRange _compositionRange;
    NSUInteger _pendingEditBaseLength;
    NSUInteger _compositionBaseLength;
    UIView* _suppressedSoftwareKeyboardView;
}

@property (nonatomic) BOOL showsSoftwareKeyboard;
@property (nonatomic) BOOL inputProducerFrozen;
@property (nonatomic) BOOL terminallyAborted;

- (void)setCursorRect:(CGRect)rect;
- (void)setTextContext:(nullable NSString*)text
               version:(int)version
           windowStart:(NSInteger)windowStart
        selectionStart:(NSInteger)selectionStart
          selectionEnd:(NSInteger)selectionEnd
           forceRestart:(BOOL)forceRestart;
- (void)completeComposition;
- (void)cancelComposition;

@end

@implementation UniTextTextKitInputView

- (instancetype)initWithFrame:(CGRect)frame {
    self = [super initWithFrame:frame];
    if (self) {
        _caretRect = CGRectMake(0, 0, 1, 20);
        self.delegate = self;
        self.backgroundColor = UIColor.clearColor;
        self.textColor = UIColor.clearColor;
        self.tintColor = UIColor.clearColor;
        self.textContainerInset = UIEdgeInsetsZero;
        self.textContainer.lineFragmentPadding = 0;
        self.scrollEnabled = NO;
        self.alpha = 0.01f;
        self.isAccessibilityElement = YES;
        self.accessibilityTraits = UIAccessibilityTraitAllowsDirectInteraction;
    }
    return self;
}

- (UIView*)hitTest:(CGPoint)point withEvent:(UIEvent*)event {
    return nil;
}

- (UIView*)inputView {
    if (_showsSoftwareKeyboard) return nil;
    if (!_suppressedSoftwareKeyboardView)
        _suppressedSoftwareKeyboardView = [[UIView alloc] initWithFrame:CGRectZero];
    return _suppressedSoftwareKeyboardView;
}

- (void)setCursorRect:(CGRect)rect {
    _caretRect = rect;
}

- (NSRange)clampedLocalSelectionStart:(NSInteger)start end:(NSInteger)end {
    NSInteger length = (NSInteger)self.text.length;
    NSInteger localStart = MAX(0, MIN(start - _windowStart, length));
    NSInteger localEnd = MAX(localStart, MIN(end - _windowStart, length));
    return NSMakeRange((NSUInteger)localStart, (NSUInteger)(localEnd - localStart));
}

- (void)setTextContext:(nullable NSString*)text
               version:(int)version
           windowStart:(NSInteger)windowStart
        selectionStart:(NSInteger)selectionStart
          selectionEnd:(NSInteger)selectionEnd
           forceRestart:(BOOL)forceRestart {
    if (_compositionActive || self.markedTextRange) return;

    _syncing = YES;
    _contextVersion = version;
    _windowStart = windowStart;
    if (forceRestart && !text) text = [self.text copy];
    if (text && (forceRestart || ![self.text isEqualToString:text])) self.text = text;
    NSRange selection = [self clampedLocalSelectionStart:selectionStart end:selectionEnd];
    if (!NSEqualRanges(self.selectedRange, selection)) self.selectedRange = selection;
    _hasPendingEdit = NO;
    _hasCompositionSnapshot = NO;
    _compositionOriginalText = nil;
    _hasContext = YES;
    _syncing = NO;
}

- (void)emitComposition {
    UITextRange* marked = self.markedTextRange;
    if (!marked || !s_onComposition) return;

    NSInteger markStart = [self offsetFromPosition:self.beginningOfDocument toPosition:marked.start];
    NSInteger markEnd = [self offsetFromPosition:self.beginningOfDocument toPosition:marked.end];
    if (markStart < 0 || markEnd < markStart || markEnd > (NSInteger)self.text.length) return;

    NSRange markRange = NSMakeRange((NSUInteger)markStart, (NSUInteger)(markEnd - markStart));
    NSAttributedString* markedText = [self.attributedText attributedSubstringFromRange:markRange];
    NSInteger cursor = (NSInteger)self.selectedRange.location - markStart;
    cursor = MAX(0, MIN(cursor, (NSInteger)markedText.length));

    const int maxClauses = 32;
    int clauseOffsets[maxClauses * 2];
    int clauseStyles[maxClauses];
    NSRange selected = NSMakeRange((NSUInteger)cursor, 0);
    int clauseCount = UniTextExtractClauses(markedText, selected, maxClauses,
                                            clauseOffsets, clauseStyles);
    if (clauseCount > 0) {
        int starts[maxClauses];
        int ends[maxClauses];
        for (int i = 0; i < clauseCount; i++) {
            starts[i] = clauseOffsets[i * 2];
            ends[i] = clauseOffsets[i * 2 + 1];
        }
        s_onComposition(markedText.string.UTF8String, starts, ends, clauseStyles,
                        clauseCount, (int)cursor);
    } else {
        int start = 0;
        int end = (int)markedText.length;
        int style = 0;
        s_onComposition(markedText.string.UTF8String, &start, &end, &style, 1,
                        (int)cursor);
    }
}

- (void)dispatchCommittedChange {
    NSString* after = self.text ?: @"";
    if (!_hasPendingEdit || _pendingEditRange.location > _pendingEditBaseLength
            || _pendingEditRange.length > _pendingEditBaseLength - _pendingEditRange.location)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A transparent native edit has no valid source range."];
    NSInteger insertedLength = (NSInteger)after.length
        - ((NSInteger)_pendingEditBaseLength - (NSInteger)_pendingEditRange.length);
    if (insertedLength < 0
            || _pendingEditRange.location + (NSUInteger)insertedLength > after.length)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A transparent native edit has an invalid replacement range."];
    NSRange range = _pendingEditRange;
    _hasPendingEdit = NO;
    if (_terminallyAborted || !s_onTextReplacement) return;
    NSString* replacement = insertedLength > 0
        ? [after substringWithRange:NSMakeRange(range.location, (NSUInteger)insertedLength)]
        : @"";
    s_onTextReplacement(_contextVersion, (int)(_windowStart + (NSInteger)range.location),
                        (int)range.length, replacement.UTF8String);
}

- (void)dispatchCompositionCommit {
    NSString* after = self.text ?: @"";
    if (!_hasCompositionSnapshot || _compositionRange.location > _compositionBaseLength
            || _compositionRange.length > _compositionBaseLength - _compositionRange.location)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A transparent native composition has no valid source range."];
    NSInteger insertedLength = (NSInteger)after.length
        - ((NSInteger)_compositionBaseLength - (NSInteger)_compositionRange.length);
    if (insertedLength < 0
            || _compositionRange.location + (NSUInteger)insertedLength > after.length)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A transparent native composition has an invalid replacement range."];
    if (_terminallyAborted) return;
    if (s_onTextInput) {
        NSString* inserted = insertedLength > 0
            ? [after substringWithRange:NSMakeRange(
                _compositionRange.location, (NSUInteger)insertedLength)]
            : @"";
        s_onTextInput(inserted.UTF8String);
    }
}

- (void)setMarkedText:(NSString*)markedText selectedRange:(NSRange)selectedRange {
    if (!_syncing && !_inputProducerFrozen && !_hasCompositionSnapshot) {
        NSRange range = self.selectedRange;
        NSString* value = self.text ?: @"";
        if (range.location > value.length || range.length > value.length - range.location)
            [NSException raise:NSInternalInconsistencyException
                        format:@"A transparent native composition has an invalid source range."];
        _compositionRange = range;
        _compositionBaseLength = value.length;
        _compositionOriginalText = range.length > 0
            ? [value substringWithRange:range]
            : @"";
        _hasCompositionSnapshot = YES;
    }
    [super setMarkedText:markedText selectedRange:selectedRange];
}

- (void)textViewDidChange:(UITextView*)textView {
    if (_syncing || !_hasContext || _inputProducerFrozen) return;
    int callbackRevision = UniTextBeginTransparentCallback();
    if (callbackRevision == 0) return;
    _editingText = NO;
    if (self.markedTextRange) {
        if (!_compositionActive && s_setSelectionCharRange) {
            if (!_hasCompositionSnapshot)
                [NSException raise:NSInternalInconsistencyException
                            format:@"A transparent native composition has no source snapshot."];
            s_setSelectionCharRange(
                (int)(_windowStart + (NSInteger)_compositionRange.location),
                (int)_compositionRange.length);
        }
        _hasPendingEdit = NO;
        _compositionActive = YES;
        [self emitComposition];
        UniTextEndTransparentCallback(callbackRevision);
        return;
    }

    BOOL endedComposition = _compositionActive;
    if (endedComposition) {
        [self dispatchCompositionCommit];
    } else {
        [self dispatchCommittedChange];
    }
    _compositionActive = NO;
    _hasCompositionSnapshot = NO;
    _compositionOriginalText = nil;
    _hasPendingEdit = NO;
    if (!_terminallyAborted && endedComposition && s_onCompositionEnded) s_onCompositionEnded();
    UniTextEndTransparentCallback(callbackRevision);
}

- (void)textViewDidChangeSelection:(UITextView*)textView {
    if (_syncing || !_hasContext || _inputProducerFrozen) return;
    int callbackRevision = UniTextBeginTransparentCallback();
    if (callbackRevision == 0) return;
    if (self.markedTextRange) {
        [self emitComposition];
        UniTextEndTransparentCallback(callbackRevision);
        return;
    }
    if (!_editingText && s_setSelectionCharRange)
        s_setSelectionCharRange((int)(_windowStart + (NSInteger)self.selectedRange.location),
                                (int)self.selectedRange.length);
    UniTextEndTransparentCallback(callbackRevision);
}

- (BOOL)textView:(UITextView*)textView
 shouldChangeTextInRange:(NSRange)range
 replacementText:(NSString*)text {
    if (_inputProducerFrozen) return NO;
    if (!self.markedTextRange && [text isEqualToString:@"\n"]) {
        if (s_onKeyDown) {
            int callbackRevision = UniTextBeginTransparentCallback();
            s_onKeyDown(NKC_Return, 0);
            UniTextEndTransparentCallback(callbackRevision);
        }
        return NO;
    }
    NSString* value = self.text ?: @"";
    if (range.location > value.length || range.length > value.length - range.location)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A transparent native edit proposed an invalid source range."];
    _pendingEditRange = range;
    _pendingEditBaseLength = value.length;
    _hasPendingEdit = YES;
    _editingText = YES;
    return YES;
}

- (void)completeComposition {
    if (_inputProducerFrozen || !self.markedTextRange) return;
    [self unmarkText];
    [self textViewDidChange:self];
}

- (void)cancelComposition {
    if (!_compositionActive && !self.markedTextRange) return;
    if (!_hasCompositionSnapshot)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A transparent native composition has no source snapshot."];
    NSString* value = self.text ?: @"";
    NSInteger replacementLength = (NSInteger)value.length
        - ((NSInteger)_compositionBaseLength - (NSInteger)_compositionRange.length);
    if (replacementLength < 0
            || _compositionRange.location + (NSUInteger)replacementLength > value.length)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A transparent native composition has an invalid discard range."];
    _syncing = YES;
    [self unmarkText];
    [self.textStorage replaceCharactersInRange:NSMakeRange(
        _compositionRange.location, (NSUInteger)replacementLength)
                                     withString:_compositionOriginalText ?: @""];
    NSUInteger location = _compositionRange.location + _compositionOriginalText.length;
    self.selectedRange = NSMakeRange(location, 0);
    _compositionActive = NO;
    _hasCompositionSnapshot = NO;
    _compositionOriginalText = nil;
    _hasPendingEdit = NO;
    _editingText = NO;
    _syncing = NO;
}

- (CGRect)rectForLocalStart:(NSInteger)localStart length:(NSInteger)length {
    CGRect hostRect = _caretRect;
    float rect[4] = {0, 0, 0, 0};
    CGRect queriedRect;
    NSInteger fullStart = _windowStart + localStart;
    BOOL insideMarkedText = NO;
    UITextRange* markedTextRange = self.markedTextRange;
    if (markedTextRange) {
        NSInteger markedStart = [self offsetFromPosition:self.beginningOfDocument
                                               toPosition:markedTextRange.start];
        NSInteger markedEnd = [self offsetFromPosition:self.beginningOfDocument
                                             toPosition:markedTextRange.end];
        insideMarkedText = localStart >= markedStart && localStart <= markedEnd;
    }
    if (!insideMarkedText && s_getCharRangeRect
        && s_getCharRangeRect((int)fullStart, (int)length, rect) != 0
        && rect[3] > 0.0f
        && UniTextTryUnityRectToHost(rect[0], rect[1], MAX(rect[2], 1.0f), rect[3], &queriedRect)) {
        hostRect = queriedRect;
    }
    UIView* host = UniTextHostView();
    return host && self.window ? [self convertRect:hostRect fromView:host] : hostRect;
}

- (CGRect)firstRectForRange:(UITextRange*)range {
    NSInteger start = [self offsetFromPosition:self.beginningOfDocument toPosition:range.start];
    NSInteger end = [self offsetFromPosition:self.beginningOfDocument toPosition:range.end];
    return [self rectForLocalStart:MAX(0, start) length:MAX(1, end - start)];
}

- (CGRect)caretRectForPosition:(UITextPosition*)position {
    NSInteger start = [self offsetFromPosition:self.beginningOfDocument toPosition:position];
    return [self rectForLocalStart:MAX(0, start) length:0];
}

- (NSArray<UITextSelectionRect*>*)selectionRectsForRange:(UITextRange*)range {
    return @[];
}

- (UITextPosition*)closestPositionToPoint:(CGPoint)point {
    UIView* host = UniTextHostView();
    CGPoint hostPoint = host && self.window ? [self convertPoint:point toView:host] : point;
    float screenX, screenY;
    UniTextHostPointToUnity(hostPoint, &screenX, &screenY);
    NSInteger local = (NSInteger)self.selectedRange.location;
    if (s_closestCharAtPoint) {
        int full = s_closestCharAtPoint(screenX, screenY);
        if (full >= 0) local = full - _windowStart;
    }
    local = MAX(0, MIN(local, (NSInteger)self.text.length));
    return [self positionFromPosition:self.beginningOfDocument offset:local];
}

- (UITextPosition*)closestPositionToPoint:(CGPoint)point withinRange:(UITextRange*)range {
    UITextPosition* position = [self closestPositionToPoint:point];
    if ([self comparePosition:position toPosition:range.start] == NSOrderedAscending) return range.start;
    if ([self comparePosition:position toPosition:range.end] == NSOrderedDescending) return range.end;
    return position;
}

- (UITextRange*)characterRangeAtPoint:(CGPoint)point {
    UITextPosition* start = [self closestPositionToPoint:point];
    UITextPosition* end = [self positionFromPosition:start offset:1] ?: start;
    return [self textRangeFromPosition:start toPosition:end];
}

- (NSWritingDirection)baseWritingDirectionForPosition:(UITextPosition*)position
                                          inDirection:(UITextStorageDirection)direction {
    if (!s_writingDirection) return NSWritingDirectionNatural;
    NSInteger local = [self offsetFromPosition:self.beginningOfDocument toPosition:position];
    switch (s_writingDirection((int)(_windowStart + local))) {
        case 1: return NSWritingDirectionLeftToRight;
        case 2: return NSWritingDirectionRightToLeft;
        default: return NSWritingDirectionNatural;
    }
}

- (void)beginFloatingCursorAtPoint:(CGPoint)point {
}

- (void)updateFloatingCursorAtPoint:(CGPoint)point {
    if (_inputProducerFrozen) return;
    UIView* host = UniTextHostView();
    CGPoint hostPoint = host && self.window ? [self convertPoint:point toView:host] : point;
    float screenX, screenY;
    UniTextHostPointToUnity(hostPoint, &screenX, &screenY);
    if (s_onFloatingCursorPoint) {
        int callbackRevision = UniTextBeginTransparentCallback();
        s_onFloatingCursorPoint(screenX, screenY);
        UniTextEndTransparentCallback(callbackRevision);
    }
}

- (void)endFloatingCursor {
}

- (void)pressesBegan:(NSSet<UIPress*>*)presses withEvent:(UIPressesEvent*)event {
    if (_inputProducerFrozen) return;
    if (self.markedTextRange) {
        [super pressesBegan:presses withEvent:event];
        return;
    }

    NSMutableSet<UIPress*>* unhandled = nil;
    for (UIPress* press in presses) {
        if (_inputProducerFrozen) continue;
        BOOL consumed = NO;
        if (@available(iOS 13.4, *)) {
            UIKey* key = press.key;
            if (key) {
                int nativeKey = -1;
                int modifiers = 0;
                UIKeyModifierFlags flags = key.modifierFlags;
                if (flags & UIKeyModifierShift) modifiers |= 1;
                if (flags & UIKeyModifierControl) modifiers |= 2;
                if (flags & UIKeyModifierAlternate) modifiers |= 4;
                if (flags & UIKeyModifierCommand) modifiers |= 8;
                BOOL printable = NO;
                switch (key.keyCode) {
                    case UIKeyboardHIDUsageKeyboardA: nativeKey = NKC_A; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardB: nativeKey = NKC_B; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardC: nativeKey = NKC_C; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardD: nativeKey = NKC_D; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardE: nativeKey = NKC_E; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardF: nativeKey = NKC_F; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardG: nativeKey = NKC_G; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardH: nativeKey = NKC_H; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardI: nativeKey = NKC_I; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardJ: nativeKey = NKC_J; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardK: nativeKey = NKC_K; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardL: nativeKey = NKC_L; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardM: nativeKey = NKC_M; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardN: nativeKey = NKC_N; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardO: nativeKey = NKC_O; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardP: nativeKey = NKC_P; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardQ: nativeKey = NKC_Q; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardR: nativeKey = NKC_R; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardS: nativeKey = NKC_S; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardT: nativeKey = NKC_T; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardU: nativeKey = NKC_U; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardV: nativeKey = NKC_V; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardW: nativeKey = NKC_W; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardX: nativeKey = NKC_X; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardY: nativeKey = NKC_Y; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardZ: nativeKey = NKC_Z; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardBackslash: nativeKey = NKC_Backslash; printable = YES; break;
                    case UIKeyboardHIDUsageKeyboardLeftArrow: nativeKey = NKC_LeftArrow; break;
                    case UIKeyboardHIDUsageKeyboardRightArrow: nativeKey = NKC_RightArrow; break;
                    case UIKeyboardHIDUsageKeyboardUpArrow: nativeKey = NKC_UpArrow; break;
                    case UIKeyboardHIDUsageKeyboardDownArrow: nativeKey = NKC_DownArrow; break;
                    case UIKeyboardHIDUsageKeyboardHome: nativeKey = NKC_Home; break;
                    case UIKeyboardHIDUsageKeyboardEnd: nativeKey = NKC_End; break;
                    case UIKeyboardHIDUsageKeyboardPageUp: nativeKey = NKC_PageUp; break;
                    case UIKeyboardHIDUsageKeyboardPageDown: nativeKey = NKC_PageDown; break;
                    case UIKeyboardHIDUsageKeyboardDeleteOrBackspace: nativeKey = NKC_Backspace; break;
                    case UIKeyboardHIDUsageKeyboardDeleteForward: nativeKey = NKC_Delete; break;
                    case UIKeyboardHIDUsageKeyboardReturnOrEnter: nativeKey = NKC_Return; break;
                    case UIKeyboardHIDUsageKeypadEnter: nativeKey = NKC_KeypadEnter; break;
                    case UIKeyboardHIDUsageKeyboardTab: nativeKey = NKC_Tab; break;
                    case UIKeyboardHIDUsageKeyboardEscape: nativeKey = NKC_Escape; break;
                    default: break;
                }
                if ((printable && (modifiers & 0xA) == 0)
                    || ((nativeKey == NKC_Backspace || nativeKey == NKC_Delete) && modifiers == 0)) {
                    nativeKey = -1;
                }
                if (nativeKey >= 0 && s_onKeyDown) {
                    int callbackRevision = UniTextBeginTransparentCallback();
                    s_onKeyDown(nativeKey, modifiers);
                    UniTextEndTransparentCallback(callbackRevision);
                    consumed = YES;
                }
            }
        }
        if (!consumed) {
            if (!unhandled) unhandled = [NSMutableSet set];
            [unhandled addObject:press];
        }
    }
    if (!_inputProducerFrozen && unhandled.count > 0)
        [super pressesBegan:unhandled withEvent:event];
}

@end

/** Whether the presenter context selects the multi-line control. */
static BOOL UniTextNativeFieldIsMultiLine(const UniTextNativeFieldContext* context) {
    return context->wraps != 0 || context->acceptsNewlines != 0;
}

/** The editor action the return key stands for: a field that accepts line breaks always spends the
    key on one, so it reports the plain key and lets the editor's key bindings resolve it. */
static const char* UniTextNativeFieldReturnKeyAction(const UniTextNativeFieldContext* context) {
    if (context->acceptsNewlines) return "return";
    if (UniTextNativeFieldReturnKeyCommits(context->returnKey)) return "submit";
    switch (context->returnKey) {
        case UniTextNativeFieldReturnKeyNext:     return "next";
        case UniTextNativeFieldReturnKeyPrevious: return "previous";
        default: return "return";
    }
}

typedef void (^UniTextReplicaActionHandler)(const char* action, int modifiers);
typedef void (^UniTextReplicaReturnKeyHandler)(int modifiers);

/**
 * Reports a hardware return press with its modifier state and answers whether the press set was
 * consumed. Only a hardware keyboard produces key presses here: the software keyboard's return key
 * arrives as a text replacement instead.
 */
static BOOL UniTextReplicaConsumeReturnPresses(NSSet<UIPress*>* presses,
                                               UniTextReplicaReturnKeyHandler handler) {
    if (!handler) return NO;
    if (@available(iOS 13.4, *)) {
        for (UIPress* press in presses) {
            UIKey* key = press.key;
            if (!key) continue;
            if (key.keyCode != UIKeyboardHIDUsageKeyboardReturnOrEnter &&
                key.keyCode != UIKeyboardHIDUsageKeypadEnter) continue;
            UIKeyModifierFlags flags = key.modifierFlags;
            int modifiers = 0;
            if (flags & UIKeyModifierShift) modifiers |= 1;
            if (flags & UIKeyModifierControl) modifiers |= 2;
            if (flags & UIKeyModifierAlternate) modifiers |= 4;
            if (flags & UIKeyModifierCommand) modifiers |= 8;
            handler(modifiers);
            return YES;
        }
    }
    return NO;
}

static BOOL UniTextReplicaOwnsAction(SEL action) {
    return action == @selector(undo:) || action == @selector(redo:)
        || action == @selector(copy:) || action == @selector(cut:)
        || action == @selector(paste:) || action == @selector(pasteAndMatchStyle:);
}

static BOOL UniTextNativeFieldActionIsSupported(NSString* action) {
    return [action isEqualToString:@"submit"] || [action isEqualToString:@"return"]
        || [action isEqualToString:@"done"]
        || [action isEqualToString:@"next"] || [action isEqualToString:@"previous"]
        || [action isEqualToString:@"cancel"] || [action isEqualToString:@"undo"]
        || [action isEqualToString:@"redo"] || [action isEqualToString:@"copy"]
        || [action isEqualToString:@"cut"] || [action isEqualToString:@"paste"]
        || [action isEqualToString:@"pastePlain"];
}

@interface UniTextReplicaTextField ()
@property (nonatomic, copy) UniTextReplicaActionHandler uniTextActionHandler;
@property (nonatomic, copy) UniTextReplicaReturnKeyHandler uniTextReturnKeyHandler;
@property (nonatomic) BOOL uniTextCopyAllowed;
@property (nonatomic) BOOL uniTextReadOnly;
@property (nonatomic) NSUInteger uniTextContentMutationEpoch;
@property (nonatomic) NSUInteger uniTextSelectionMutationEpoch;
@property (nonatomic) BOOL uniTextPresenterCallbackActive;
@end

@implementation UniTextReplicaTextField
- (void)setText:(NSString*)text {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica text."];
    [super setText:text];
    _uniTextContentMutationEpoch++;
}
- (void)setAttributedText:(NSAttributedString*)attributedText {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica text."];
    [super setAttributedText:attributedText];
    _uniTextContentMutationEpoch++;
}
- (void)setSelectedTextRange:(UITextRange*)selectedTextRange {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica selection."];
    [super setSelectedTextRange:selectedTextRange];
    _uniTextSelectionMutationEpoch++;
}
- (void)replaceRange:(UITextRange*)range withText:(NSString*)text {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica text."];
    [super replaceRange:range withText:text];
    _uniTextContentMutationEpoch++;
    _uniTextSelectionMutationEpoch++;
}
- (void)insertText:(NSString*)text {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica text."];
    [super insertText:text];
}
- (void)deleteBackward {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica text."];
    [super deleteBackward];
}
- (void)setMarkedText:(NSString*)markedText selectedRange:(NSRange)selectedRange {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica composition."];
    [super setMarkedText:markedText selectedRange:selectedRange];
}
- (void)unmarkText {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica composition."];
    [super unmarkText];
}
- (BOOL)canPerformAction:(SEL)action withSender:(id)sender {
    if ((action == @selector(copy:) || action == @selector(cut:)) && !_uniTextCopyAllowed) return NO;
    if (_uniTextReadOnly && action != @selector(copy:) && UniTextReplicaOwnsAction(action)) return NO;
    return _uniTextActionHandler && UniTextReplicaOwnsAction(action)
        ? YES
        : [super canPerformAction:action withSender:sender];
}
- (void)undo:(id)sender { if (!_uniTextReadOnly && _uniTextActionHandler) _uniTextActionHandler("undo", 0); }
- (void)redo:(id)sender { if (!_uniTextReadOnly && _uniTextActionHandler) _uniTextActionHandler("redo", 0); }
- (void)copy:(id)sender { if (_uniTextCopyAllowed && _uniTextActionHandler) _uniTextActionHandler("copy", 0); }
- (void)cut:(id)sender { if (!_uniTextReadOnly && _uniTextCopyAllowed && _uniTextActionHandler) _uniTextActionHandler("cut", 0); }
- (void)paste:(id)sender { if (!_uniTextReadOnly && _uniTextActionHandler) _uniTextActionHandler("paste", 0); }
- (void)pasteAndMatchStyle:(id)sender { if (!_uniTextReadOnly && _uniTextActionHandler) _uniTextActionHandler("pastePlain", 0); }
- (void)pressesBegan:(NSSet<UIPress*>*)presses withEvent:(UIPressesEvent*)event {
    if (self.markedTextRange || !UniTextReplicaConsumeReturnPresses(presses, _uniTextReturnKeyHandler))
        [super pressesBegan:presses withEvent:event];
}
@end

@interface UniTextReplicaTextView ()
@property (nonatomic, copy) UniTextReplicaActionHandler uniTextActionHandler;
@property (nonatomic, copy) UniTextReplicaReturnKeyHandler uniTextReturnKeyHandler;
@property (nonatomic) BOOL uniTextCopyAllowed;
@property (nonatomic) BOOL uniTextReadOnly;
@property (nonatomic, copy) NSString* placeholder;
@property (nonatomic, strong) UITextField* placeholderField;
@property (nonatomic) NSUInteger uniTextContentMutationEpoch;
@property (nonatomic) NSUInteger uniTextSelectionMutationEpoch;
@property (nonatomic) BOOL uniTextObservesTextStorage;
@property (nonatomic) BOOL uniTextPresenterCallbackActive;
- (void)updatePlaceholder;
@end

@implementation UniTextReplicaTextView
@synthesize placeholder = _placeholder;
- (void)didMoveToWindow {
    [super didMoveToWindow];
    if (!_uniTextObservesTextStorage) {
        _uniTextObservesTextStorage = YES;
        [[NSNotificationCenter defaultCenter] addObserver:self
                                                 selector:@selector(uniTextStorageDidProcessEditing:)
                                                     name:NSTextStorageDidProcessEditingNotification
                                                   object:self.textStorage];
    }
}
- (void)dealloc {
    [[NSNotificationCenter defaultCenter] removeObserver:self];
}
- (void)uniTextStorageDidProcessEditing:(NSNotification*)notification {
    _uniTextContentMutationEpoch++;
}
- (BOOL)canPerformAction:(SEL)action withSender:(id)sender {
    if ((action == @selector(copy:) || action == @selector(cut:)) && !_uniTextCopyAllowed) return NO;
    if (_uniTextReadOnly && action != @selector(copy:) && UniTextReplicaOwnsAction(action)) return NO;
    return _uniTextActionHandler && UniTextReplicaOwnsAction(action)
        ? YES
        : [super canPerformAction:action withSender:sender];
}
- (void)setPlaceholder:(NSString*)placeholder {
    _placeholder = [placeholder copy];
    if (_placeholder.length > 0 && !_placeholderField) {
        _placeholderField = [[UITextField alloc] initWithFrame:CGRectZero];
        _placeholderField.userInteractionEnabled = NO;
        _placeholderField.isAccessibilityElement = NO;
        [self addSubview:_placeholderField];
    }
    _placeholderField.placeholder = _placeholder;
    [self updatePlaceholder];
    [self setNeedsLayout];
}
- (void)setText:(NSString*)text {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica text."];
    [super setText:text];
    _uniTextContentMutationEpoch++;
    [self updatePlaceholder];
}
- (void)setAttributedText:(NSAttributedString*)attributedText {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica text."];
    [super setAttributedText:attributedText];
    _uniTextContentMutationEpoch++;
    [self updatePlaceholder];
}
- (void)setSelectedTextRange:(UITextRange*)selectedTextRange {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica selection."];
    [super setSelectedTextRange:selectedTextRange];
    _uniTextSelectionMutationEpoch++;
}
- (void)setSelectedRange:(NSRange)selectedRange {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica selection."];
    [super setSelectedRange:selectedRange];
    _uniTextSelectionMutationEpoch++;
}
- (void)replaceRange:(UITextRange*)range withText:(NSString*)text {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica text."];
    [super replaceRange:range withText:text];
    _uniTextContentMutationEpoch++;
    _uniTextSelectionMutationEpoch++;
}
- (void)insertText:(NSString*)text {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica text."];
    [super insertText:text];
}
- (void)deleteBackward {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica text."];
    [super deleteBackward];
}
- (void)setMarkedText:(NSString*)markedText selectedRange:(NSRange)selectedRange {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica composition."];
    [super setMarkedText:markedText selectedRange:selectedRange];
}
- (void)unmarkText {
    if (_uniTextPresenterCallbackActive)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter cannot mutate replica composition."];
    [super unmarkText];
}
- (void)updatePlaceholder {
    _placeholderField.hidden = self.textStorage.length != 0 || _placeholder.length == 0;
}
- (void)layoutSubviews {
    [super layoutSubviews];
    _placeholderField.font = self.font;
    _placeholderField.textAlignment = self.textAlignment;
    CGFloat x = self.textContainerInset.left + self.textContainer.lineFragmentPadding;
    CGFloat y = self.textContainerInset.top;
    CGFloat width = MAX(0, CGRectGetWidth(self.bounds) - x - self.textContainerInset.right
                           - self.textContainer.lineFragmentPadding);
    CGFloat height = [_placeholderField sizeThatFits:CGSizeMake(width, CGFLOAT_MAX)].height;
    _placeholderField.frame = CGRectMake(x, y, width, height);
}
- (void)undo:(id)sender { if (!_uniTextReadOnly && _uniTextActionHandler) _uniTextActionHandler("undo", 0); }
- (void)redo:(id)sender { if (!_uniTextReadOnly && _uniTextActionHandler) _uniTextActionHandler("redo", 0); }
- (void)copy:(id)sender { if (_uniTextCopyAllowed && _uniTextActionHandler) _uniTextActionHandler("copy", 0); }
- (void)cut:(id)sender { if (!_uniTextReadOnly && _uniTextCopyAllowed && _uniTextActionHandler) _uniTextActionHandler("cut", 0); }
- (void)paste:(id)sender { if (!_uniTextReadOnly && _uniTextActionHandler) _uniTextActionHandler("paste", 0); }
- (void)pasteAndMatchStyle:(id)sender { if (!_uniTextReadOnly && _uniTextActionHandler) _uniTextActionHandler("pastePlain", 0); }
- (void)pressesBegan:(NSSet<UIPress*>*)presses withEvent:(UIPressesEvent*)event {
    if (self.markedTextRange || !UniTextReplicaConsumeReturnPresses(presses, _uniTextReturnKeyHandler))
        [super pressesBegan:presses withEvent:event];
}
@end

@interface UniTextNativeFieldPresenter : NSObject
@property (nonatomic) UniTextNativeFieldPresenterCreateCallback create;
@property (nonatomic) UniTextNativeFieldPresenterUpdateCallback update;
@property (nonatomic) UniTextNativeFieldPresenterLayoutCallback layout;
@property (nonatomic) UniTextNativeFieldPresenterDestroyCallback destroy;
@end

@implementation UniTextNativeFieldPresenter
@end

static NSMutableDictionary<NSString*, UniTextNativeFieldPresenter*>* s_nativeFieldPresenters;
static NSString* s_activeNativeFieldPresenterId;

static const NSInteger kUniTextActionButtonTag = 0x554E4442;

/**
 * Whether the running system shapes a glass surface through its own corner configuration. When it
 * does, a layer mask is the wrong tool: the material carries its own edge treatment and casts a
 * shadow past its bounds, and clipping cuts both away.
 */
static BOOL UniTextSurfaceShapesItself(UIView* view) {
    return NSClassFromString(@"UICornerRadius") != nil
        && NSClassFromString(@"UICornerConfiguration") != nil
        && [view respondsToSelector:NSSelectorFromString(@"setCornerConfiguration:")];
}

/** Rounds a surface through its corner configuration, or answers NO when only a layer mask is available. */
static BOOL UniTextApplySurfaceCornerRadius(UIView* view, CGFloat radius) {
    if (!UniTextSurfaceShapesItself(view)) return NO;
    Class radiusClass = NSClassFromString(@"UICornerRadius");
    Class configurationClass = NSClassFromString(@"UICornerConfiguration");
    SEL fixedSelector = NSSelectorFromString(@"fixedRadius:");
    SEL configurationSelector = NSSelectorFromString(@"configurationWithRadius:");
    SEL applySelector = NSSelectorFromString(@"setCornerConfiguration:");
    if (![radiusClass respondsToSelector:fixedSelector]
        || ![configurationClass respondsToSelector:configurationSelector]) return NO;
    id (*makeRadius)(id, SEL, CGFloat) =
        (id (*)(id, SEL, CGFloat))[radiusClass methodForSelector:fixedSelector];
    id (*makeConfiguration)(id, SEL, id) =
        (id (*)(id, SEL, id))[configurationClass methodForSelector:configurationSelector];
    void (*applyConfiguration)(id, SEL, id) =
        (void (*)(id, SEL, id))[view methodForSelector:applySelector];
    applyConfiguration(view, applySelector,
        makeConfiguration(configurationClass, configurationSelector,
            makeRadius(radiusClass, fixedSelector, radius)));
    return YES;
}

/**
 * Builds the button standing in for the return key, or nil when the field declares no action.
 * The system paints it: the prominent configuration is what fills a confirming button with the
 * accent color, and only the choice of which action is prominent belongs to us.
 */
static UIButton* UniTextSystemPresenterCreateActionButton(const UniTextNativeFieldContext* context) {
    NSString* symbolName;
    NSString* label;
    const char* actionName;
    BOOL prominent = NO;
    switch (context->returnKey) {
        case UniTextNativeFieldReturnKeyGo:
            symbolName = @"arrow.right";
            label = @"Go";
            actionName = "submit";
            prominent = YES;
            break;
        case UniTextNativeFieldReturnKeySearch:
            symbolName = @"magnifyingglass";
            label = @"Search";
            actionName = "submit";
            prominent = YES;
            break;
        case UniTextNativeFieldReturnKeySend:
            symbolName = @"arrow.up";
            label = @"Send";
            actionName = "submit";
            prominent = YES;
            break;
        case UniTextNativeFieldReturnKeyDone:
            symbolName = @"checkmark";
            label = @"Done";
            actionName = "submit";
            prominent = YES;
            break;
        case UniTextNativeFieldReturnKeyNext:
            symbolName = @"chevron.down";
            label = @"Next field";
            actionName = "next";
            break;
        case UniTextNativeFieldReturnKeyPrevious:
            symbolName = @"chevron.up";
            label = @"Previous field";
            actionName = "previous";
            break;
        default:
            return nil;
    }

    int sessionId = context->sessionId;
    UIButton* button = [UIButton buttonWithType:UIButtonTypeSystem];
    button.tag = kUniTextActionButtonTag;
    // Resolved by name for the same reason as the surface material: naming the glass configuration
    // directly would keep the plugin from building against an older SDK.
    Class configurationClass = NSClassFromString(@"UIButtonConfiguration");
    SEL glassSelector = NSSelectorFromString(
        prominent ? @"prominentGlassButtonConfiguration" : @"glassButtonConfiguration");
    if ([configurationClass respondsToSelector:glassSelector]) {
        id (*glassConfiguration)(id, SEL) =
            (id (*)(id, SEL))[configurationClass methodForSelector:glassSelector];
        id configuration = glassConfiguration(configurationClass, glassSelector);
        if ([configuration isKindOfClass:configurationClass])
            button.configuration = (UIButtonConfiguration*)configuration;
    }
    [button setImage:[UIImage systemImageNamed:symbolName] forState:UIControlStateNormal];
    // A symbol image alone leaves VoiceOver reading the symbol's own name; the label states the
    // action instead. It is English: the package ships no localized strings, and a presenter is
    // the extension point for a localized one.
    button.accessibilityLabel = label;
    [button addAction:[UIAction actionWithHandler:^(UIAction* triggered) {
        (void)triggered;
        UniTextNativeInput_PerformNativeFieldAction(sessionId, actionName);
    }] forControlEvents:UIControlEventTouchUpInside];
    return button;
}

static UIView* UniTextSystemPresenterCreate(const UniTextNativeFieldContext* context,
                                            UIView* hostView,
                                            UIView** containerPtr) {
    // A bar carries no surface of its own: the current design gives its material to bar items, and
    // an item-less bar draws nothing. A docked field is a custom surface, and the material for one
    // comes from a visual-effect view. The effect is resolved by class presence rather than by OS
    // version so the plugin builds against any SDK and still asks for the newest material the
    // running system has.
    Class glassEffectClass = NSClassFromString(@"UIGlassEffect");
    UIVisualEffect* effect = glassEffectClass
        ? [[glassEffectClass alloc] init]
        : [UIBlurEffect effectWithStyle:UIBlurEffectStyleSystemChromeMaterial];
    UIVisualEffectView* container = [[UIVisualEffectView alloc] initWithEffect:effect];
    if (!UniTextSurfaceShapesItself(container)) {
        container.layer.cornerCurve = kCACornerCurveContinuous;
        container.clipsToBounds = YES;
    }

    UIView* field;
    if (UniTextNativeFieldIsMultiLine(context)) {
        // Clear background for the same reason the single-line field carries no border style: the
        // bar is the surface, and the text view's own opaque fill would cover the bar's material.
        UniTextReplicaTextView* textView = [[UniTextReplicaTextView alloc] initWithFrame:CGRectZero];
        textView.font = [UIFont preferredFontForTextStyle:UIFontTextStyleBody];
        textView.adjustsFontForContentSizeCategory = YES;
        textView.backgroundColor = UIColor.clearColor;
        if (!context->wraps) {
            // A text container that stops tracking the view's width lays every paragraph out on one
            // line, and the scroll view then carries it horizontally.
            textView.textContainer.widthTracksTextView = NO;
            textView.textContainer.size = CGSizeMake(CGFLOAT_MAX, CGFLOAT_MAX);
        }
        field = textView;

        // The return key already declares this field's action, but a field that accepts line breaks
        // spends that key on them. The button is the same action made reachable again, so a field
        // that declares none — or whose return key is still free to carry it — gets no button.
        if (context->acceptsNewlines) {
            UIButton* action = UniTextSystemPresenterCreateActionButton(context);
            if (action) [container.contentView addSubview:action];
        }
    } else {
        // No border style: the bar is the surface, and a bordered field paints its own opaque slab
        // over whatever material the bar draws.
        UITextField* textField = [[UniTextReplicaTextField alloc] initWithFrame:CGRectZero];
        textField.font = [UIFont preferredFontForTextStyle:UIFontTextStyleBody];
        textField.adjustsFontForContentSizeCategory = YES;
        field = textField;
    }
    [hostView addSubview:container];
    // A visual-effect view composites only what sits in its content view; a direct subview would
    // float above the material instead of on it.
    [container.contentView addSubview:field];
    if (containerPtr) *containerPtr = container;
    return field;
}

static void UniTextSystemPresenterUpdate(const UniTextNativeFieldContext* context,
                                         UIView* nativeField, UIView* container) {
    (void)container;
    if (![nativeField isKindOfClass:[UITextView class]]) return;
    NSTextContainer* textContainer = ((UITextView*)nativeField).textContainer;
    BOOL wraps = context->wraps != 0;
    if (textContainer.widthTracksTextView == wraps) return;
    textContainer.widthTracksTextView = wraps;
    if (!wraps) textContainer.size = CGSizeMake(CGFLOAT_MAX, CGFLOAT_MAX);
}

static void UniTextSystemPresenterLayout(const UniTextNativeFieldContext* context,
                                         UIView* nativeField, UIView* container,
                                         CGRect hostBounds, UIEdgeInsets safeAreaInsets,
                                         CGRect suggestedFieldFrame, CGFloat keyboardTop) {
    (void)suggestedFieldFrame;
    // The surface is an element floating over the content, so it keeps a margin from the screen
    // edges and from the keyboard instead of bleeding into them, and its corners are rounded
    // continuously like the other floating surfaces the system stacks here.
    static const CGFloat kSurfaceMargin = 8;
    static const CGFloat kSurfacePadding = 8;
    static const CGFloat kSurfaceMaxRadius = 22;
    // Text has to clear the rounded corner, but only at its own height, not at the corner's widest
    // point: a capsule has already opened up by the time it reaches the text band, so the inset
    // sits between the vertical padding and the full radius.
    static const CGFloat kSurfaceTextInset = 16;
    // The text band never shrinks below one comfortable line and never grows past four; past that
    // the text view scrolls. When the keyboard leaves less room than the minimum the surface keeps
    // its height and rides over the keyboard: a surface too short to show the caret is worse than
    // one that overlaps.
    static const CGFloat kSurfaceMinFieldHeight = 44;
    static const CGFloat kSurfaceMaxFieldHeight = 132;
    CGRect safeBounds = UIEdgeInsetsInsetRect(hostBounds, safeAreaInsets);
    CGFloat width = MAX(0, CGRectGetWidth(safeBounds) - kSurfaceMargin * 2);
    CGFloat fieldHeight;
    if (UniTextNativeFieldIsMultiLine(context)) {
        fieldHeight = ceil([nativeField sizeThatFits:
            CGSizeMake(MAX(0, width - kSurfaceTextInset * 2), kSurfaceMaxFieldHeight)].height);
        fieldHeight = MIN(kSurfaceMaxFieldHeight, MAX(kSurfaceMinFieldHeight, fieldHeight));
    } else {
        fieldHeight = MAX(34, ceil(nativeField.intrinsicContentSize.height));
    }
    CGFloat bottom = MIN(MAX((CGFloat)keyboardTop, CGRectGetMinY(safeBounds)),
                         CGRectGetMaxY(safeBounds)) - kSurfaceMargin;
    CGFloat availableHeight = MAX(0, bottom - CGRectGetMinY(safeBounds));
    CGFloat minContainerHeight = kSurfaceMinFieldHeight + kSurfacePadding * 2;
    CGFloat containerHeight = MAX(MIN(fieldHeight + kSurfacePadding * 2, availableHeight),
                                  minContainerHeight);
    fieldHeight = MAX(0, containerHeight - kSurfacePadding * 2);
    CGFloat y = bottom - containerHeight;
    CGRect containerFrame = CGRectMake(
        CGRectGetMinX(safeBounds) + kSurfaceMargin, y, width, containerHeight);
    CGFloat fieldWidth = MAX(0, width - kSurfaceTextInset * 2);
    UIView* actionButton = [container viewWithTag:kUniTextActionButtonTag];
    if (actionButton) {
        // Committing an empty field is never what the tap meant. The layout callback is the only
        // presenter hook that runs on both local typing and authoritative reconciliation, so the
        // emptiness test lives here.
        if ([actionButton isKindOfClass:[UIControl class]]
                && [nativeField conformsToProtocol:@protocol(UIKeyInput)]) {
            BOOL commits = UniTextNativeFieldReturnKeyCommits(context->returnKey);
            BOOL enabled = !commits || [(id<UIKeyInput>)nativeField hasText];
            if (((UIControl*)actionButton).enabled != enabled)
                ((UIControl*)actionButton).enabled = enabled;
        }
        CGSize actionSize = [actionButton sizeThatFits:CGSizeMake(width, containerHeight)];
        CGFloat actionWidth = MAX(44, ceil(actionSize.width));
        CGFloat actionHeight = MIN(containerHeight - kSurfacePadding * 2,
                                   MAX(44, ceil(actionSize.height)));
        fieldWidth = MAX(0, fieldWidth - actionWidth - kSurfacePadding);
        actionButton.frame = CGRectMake(width - kSurfaceTextInset - actionWidth,
                                        containerHeight - kSurfacePadding - actionHeight,
                                        actionWidth, actionHeight);
    }
    CGRect fieldFrame = CGRectMake(kSurfaceTextInset, kSurfacePadding, fieldWidth, fieldHeight);
    container.frame = containerFrame;
    CGFloat cornerRadius = MIN(kSurfaceMaxRadius, containerHeight / 2);
    if (!UniTextApplySurfaceCornerRadius(container, cornerRadius))
        container.layer.cornerRadius = cornerRadius;
    nativeField.frame = fieldFrame;
}

static void UniTextSystemPresenterDestroy(const UniTextNativeFieldContext* context,
                                          UIView* nativeField, UIView* container) {
    (void)context;
    [nativeField removeFromSuperview];
    [container removeFromSuperview];
}

static BOOL UniTextRegisterNativeFieldPresenter(NSString* identifier,
                                                UniTextNativeFieldPresenterCreateCallback create,
                                                UniTextNativeFieldPresenterUpdateCallback update,
                                                UniTextNativeFieldPresenterLayoutCallback layout,
                                                UniTextNativeFieldPresenterDestroyCallback destroy) {
    if (identifier.length == 0 || !create || !update || !layout || !destroy) return NO;
    if ([identifier isEqualToString:s_activeNativeFieldPresenterId]) return NO;
    if (!s_nativeFieldPresenters) s_nativeFieldPresenters = [NSMutableDictionary dictionary];
    UniTextNativeFieldPresenter* presenter = [[UniTextNativeFieldPresenter alloc] init];
    presenter.create = create;
    presenter.update = update;
    presenter.layout = layout;
    presenter.destroy = destroy;
    s_nativeFieldPresenters[identifier] = presenter;
    return YES;
}

static void UniTextEnsureSystemPresenter(void) {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        UniTextRegisterNativeFieldPresenter(@"system",
                                            UniTextSystemPresenterCreate,
                                            UniTextSystemPresenterUpdate,
                                            UniTextSystemPresenterLayout,
                                            UniTextSystemPresenterDestroy);
    });
}


@interface UniTextNativeInputController : NSObject <UITextFieldDelegate, UITextViewDelegate>
{
    UniTextTextKitInputView* _transparentView;
    UIView*           _nativeFieldHostView;
    UIView*           _nativeFieldContainer;
    UniTextReplicaTextField* _nativeTextField;
    UniTextReplicaTextView*  _nativeTextView;
    BOOL              _keyboardVisible;
    CGRect            _keyboardFrame;
    UniTextNativeFieldPresenter* _nativePresenter;
}

+ (instancetype)shared;
- (void)initializeWithCallbacks;
- (void)shutdown;
- (void)setCursorPosX:(float)x y:(float)y w:(float)w h:(float)h;
- (void)setTextContext:(nullable NSString*)text
               version:(int)version
           windowStart:(NSInteger)windowStart
        selectionStart:(NSInteger)selectionStart
          selectionEnd:(NSInteger)selectionEnd
           forceRestart:(BOOL)forceRestart;
- (BOOL)showKeyboardWithArgs:(const UniTextShowKeyboardArgs*)args;
- (void)hideKeyboard;
- (void)setInputFieldRectUnityX:(float)unityX unityY:(float)unityY
                          width:(float)unityW height:(float)unityH;
- (void)setNativeFieldStateForSession:(int)sessionId
                 sourceNativeRevision:(int)sourceNativeRevision
                    authorityRevision:(int)authorityRevision
                                 text:(nullable NSString*)text
                       selectionStart:(NSInteger)selectionStart
                         selectionEnd:(NSInteger)selectionEnd;
- (BOOL)updateNativeFieldWithArgs:(const UniTextShowKeyboardArgs*)args;
- (BOOL)performNativeFieldActionForSession:(int)sessionId action:(NSString*)action;
- (void)closeNativeFieldForSession:(int)sessionId;
- (void)focusNativeFieldForSession:(int)sessionId;
- (void)quiesceInputForSession:(int)sessionId
                   disposition:(int)disposition
                     requestId:(int)requestId;
- (BOOL)abortInputForSession:(int)sessionId;
- (void)resumeInputProducer;
- (void)beginNativeCallbackForRevision:(int)revision;
- (void)endNativeCallbackForRevision:(int)revision;
- (int)advanceRevision:(int*)revision sessionId:(int)sessionId;
- (int)beginTransparentCallback;
- (void)endTransparentCallback:(int)revision;
- (void)enqueueQuiesceForSession:(int)sessionId
                     disposition:(int)disposition
                       requestId:(int)requestId;
- (void)schedulePendingQuiesces;
- (void)drainPendingQuiesces;
- (void)applyNativeSelection:(NSRange)selection;
- (void)applyNativeSelection:(NSRange)selection toField:(UIView*)field;
- (NSString*)nativeFieldText;
- (NSString*)nativeFieldTextForField:(UIView*)field;
- (NSRange)nativeFieldSelection;
- (NSRange)nativeFieldSelectionForField:(UIView*)field;
- (NSRange)nativeFieldMarkedRange;
- (void)handleNativeFieldChange;
- (void)completeNativeFieldComposition;
- (void)cancelNativeFieldComposition;
- (void)clearNativeFieldComposition;
- (void)applyNativeFieldPolicyAfterCompositionIfPossible;
- (void)emitNativeSelection;
- (void)applyDeferredNativeFieldState;
- (void)suppressNativeFieldCallbacksThroughTurn;
- (BOOL)emitNativeAction:(const char*)action modifiers:(int)modifiers;
- (void)configureNativeField:(UIView*)field
                        text:(NSString*)text
                   selection:(NSRange)selection
              reloadInputViews:(BOOL)reloadInputViews;
- (void)configureNativeFieldPolicy:(UIView*)field;
- (void)setNativeField:(UIView*)field presenterCallbackActive:(BOOL)active;
- (BOOL)isValidNativeFieldHierarchy:(UIView*)field container:(UIView*)container;
- (BOOL)restoreNativeFieldHierarchy:(UIView*)field
                          container:(UIView*)container
                     fieldSuperview:(UIView*)fieldSuperview
                 containerSuperview:(UIView*)containerSuperview;
- (void)layoutNativeField:(UIView*)field
                container:(UIView*)container
                presenter:(UniTextNativeFieldPresenter*)presenter
                    force:(BOOL)force;
- (void)destroyNativeField:(UIView*)field
                 container:(UIView*)container
                 presenter:(UniTextNativeFieldPresenter*)presenter
                   context:(const UniTextNativeFieldContext*)context;
- (BOOL)createNativeFieldWithPresenter:(UniTextNativeFieldPresenter*)presenter
                                  text:(NSString*)text
                             selection:(NSRange)selection
                                 field:(UIView**)fieldOut
                             container:(UIView**)containerOut;
- (void)activateNativeField:(UIView*)field
                  container:(UIView*)container
                  presenter:(UniTextNativeFieldPresenter*)presenter
                   selection:(NSRange)selection
                       focus:(BOOL)focus;

@end

@implementation UniTextNativeInputController {
    BOOL    _hasPendingRect;
    CGFloat _pendingRectUnityX;
    CGFloat _pendingRectUnityY;
    CGFloat _pendingRectUnityW;
    CGFloat _pendingRectUnityH;
    BOOL    _hidingKeyboard;
    BOOL    _syncingNativeField;
    BOOL    _suppressingNativeFieldCallbacks;
    BOOL    _nativeFieldCompositionActive;
    BOOL    _hasPendingNativeEdit;
    BOOL    _selectionFollowsNativeEdit;
    BOOL    _hasDeferredNativeState;
    BOOL    _deferredNativeHasText;
    BOOL    _hasNativeCompositionSnapshot;
    BOOL    _replacingNativePresenter;
    BOOL    _inputProducerFrozen;
    BOOL    _quiescingInput;
    BOOL    _nativeCallbackAcknowledged;
    BOOL    _pendingQuiesceDrainScheduled;
    BOOL    _nativePresenterLayoutDirty;
    BOOL    _hasNativePresenterLayoutSnapshot;
    BOOL    _applyNativeFieldPolicyAfterComposition;
    BOOL    _reloadNativeInputViewsAfterComposition;
    NSRange _pendingNativeRange;
    NSRange _nativeCompositionRange;
    NSRange _deferredNativeSelection;
    CGRect _lastNativePresenterBounds;
    CGRect _lastNativePresenterSuggestedFrame;
    UIEdgeInsets _lastNativePresenterSafeAreaInsets;
    CGFloat _lastNativePresenterKeyboardTop;
    UniTextNativeFieldContext _nativeFieldContext;
    UniTextShowKeyboardArgs _nativeFieldConfiguration;
    UniTextShowKeyboardArgs _nativeFieldAppliedConfiguration;
    NSString* _pendingNativeReplacement;
    NSString* _pendingNativeOriginalText;
    NSString* _nativeCompositionOriginalText;
    NSString* _initialNativeText;
    NSString* _deferredNativeText;
    NSString* _lastNativeCompositionText;
    NSString* _nativeContextPresenterId;
    NSString* _nativeContextPlaceholder;
    NSString* _nativeContextIdentifier;
    NSString* _nativeContextPresenterData;
    NSString* _nativePasswordRules;
    NSString* _nativeFieldAppliedPasswordRules;
    NSString* _nativeFieldAuthoritativeText;
    UITextInputPasswordRules* _nativePasswordRulesObject;
    UniTextReplicaActionHandler _nativeFieldActionHandler;
    UniTextReplicaReturnKeyHandler _nativeFieldReturnKeyHandler;
    NSRange _nativeFieldAuthoritativeSelection;
    NSMutableArray<NSArray<NSNumber*>*>* _pendingQuiesces;
    int _sessionId;
    int _nativeRevision;
    int _authorityRevision;
    int _transparentSessionId;
    int _transparentRevision;
    int _nativeCallbackInFlightRevision;
    int _transparentCallbackDepth;
    int _nativeCallbackSuppressionGeneration;
    int _deferredSourceNativeRevision;
    int _deferredAuthorityRevision;
    NSInteger _lastNativeCompositionCursor;
    NSUInteger _nativeCompositionBaseLength;
}

+ (instancetype)shared {
    static UniTextNativeInputController* instance = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        instance = [[UniTextNativeInputController alloc] init];
    });
    return instance;
}

- (void)initializeWithCallbacks {
    _keyboardVisible = NO;
    _keyboardFrame = CGRectZero;

    NSNotificationCenter* nc = [NSNotificationCenter defaultCenter];
    [nc addObserver:self selector:@selector(keyboardWillShow:)
               name:UIKeyboardWillShowNotification object:nil];
    [nc addObserver:self selector:@selector(keyboardDidShow:)
               name:UIKeyboardDidShowNotification object:nil];
    [nc addObserver:self selector:@selector(keyboardWillHide:)
               name:UIKeyboardWillHideNotification object:nil];
    [nc addObserver:self selector:@selector(keyboardDidHide:)
               name:UIKeyboardDidHideNotification object:nil];
    [nc addObserver:self selector:@selector(keyboardWillChangeFrame:)
               name:UIKeyboardWillChangeFrameNotification object:nil];
    [nc addObserver:self selector:@selector(contentSizeCategoryDidChange:)
               name:UIContentSizeCategoryDidChangeNotification object:nil];
}

- (void)shutdown {
    [[NSNotificationCenter defaultCenter] removeObserver:self];
    [self hideKeyboard];
}

- (void)contentSizeCategoryDidChange:(NSNotification*)notification {
    (void)notification;
    _nativePresenterLayoutDirty = YES;
    [self applyPendingRectIfPossible];
}

- (void)keyboardWillShow:(NSNotification*)notification {
    _keyboardVisible = YES;
    [self dispatchKeyboardEvent:notification phase:KBP_WillShow];
}

- (void)keyboardDidShow:(NSNotification*)notification {
    [self dispatchKeyboardEvent:notification phase:KBP_DidShow];
}

- (void)keyboardWillHide:(NSNotification*)notification {
    _keyboardVisible = NO;
    [self dispatchKeyboardEvent:notification phase:KBP_WillHide];
    _keyboardFrame = CGRectZero;
}

- (void)keyboardDidHide:(NSNotification*)notification {
    [self dispatchKeyboardEvent:notification phase:KBP_DidHide];
}

- (void)keyboardWillChangeFrame:(NSNotification*)notification {
    [self dispatchKeyboardEvent:notification phase:KBP_WillChangeFrame];
}

- (int)mapUIViewAnimationCurveToEasing:(NSUInteger)curve {
    switch ((UIViewAnimationCurve)curve) {
        case UIViewAnimationCurveEaseInOut: return KBE_EaseInOut;
        case UIViewAnimationCurveEaseIn:    return KBE_EaseIn;
        case UIViewAnimationCurveEaseOut:   return KBE_EaseOut;
        case UIViewAnimationCurveLinear:    return KBE_Linear;
        // Newer system curves (e.g. value 7) are not part of the public enum but match the
        // standard iOS keyboard animation — treat as EaseInOut for downstream handlers.
        default:                            return KBE_EaseInOut;
    }
}

- (void)dispatchKeyboardEvent:(NSNotification*)notification phase:(int)phase {
    NSValue* frameValue = notification.userInfo[UIKeyboardFrameEndUserInfoKey];
    CGRect frame = frameValue ? [frameValue CGRectValue] : CGRectZero;

    // Convert the keyboard rect (screen space) into the host view's space so
    // the Unity-pixel result is correct in iPad Split View / Stage Manager,
    // where the app window is neither screen-sized nor at the screen origin.
    UIView* host = UniTextHostView();
    UIWindow* window = host.window;
    if (window && !CGRectEqualToRect(frame, CGRectZero)) {
        frame = [window convertRect:frame fromWindow:nil];
        frame = [host convertRect:frame fromView:window];
    }
    _keyboardFrame = frame;

    CGFloat hostH = UniTextHostHeightPt(host);
    CGFloat scale = UniTextHostScale(host);

    float kbX = (float)(frame.origin.x * scale);
    float kbY = (float)((hostH - frame.origin.y - frame.size.height) * scale);
    float kbW = (float)(frame.size.width * scale);
    float kbH = (float)(frame.size.height * scale);

    NSNumber* durationVal = notification.userInfo[UIKeyboardAnimationDurationUserInfoKey];
    NSNumber* curveVal = notification.userInfo[UIKeyboardAnimationCurveUserInfoKey];

    float duration = durationVal ? (float)durationVal.doubleValue : 0.0f;
    int easing = curveVal ? [self mapUIViewAnimationCurveToEasing:curveVal.unsignedIntegerValue]
                          : KBE_EaseInOut;

    if ((phase == KBP_WillShow || phase == KBP_WillHide || phase == KBP_WillChangeFrame)
            && (_nativeTextField || _nativeTextView)) {
        void (^applyFrame)(void) = ^{
            [self applyPendingRectIfPossible];
        };
        if (duration > 0.0f) {
            UIViewAnimationOptions options = UIViewAnimationOptionBeginFromCurrentState;
            if (curveVal) {
                options |= (UIViewAnimationOptions)(curveVal.unsignedIntegerValue << 16);
            }
            [UIView animateWithDuration:duration
                                  delay:0
                                options:options
                             animations:applyFrame
                             completion:nil];
        } else {
            applyFrame();
        }
    }

    if (s_onKeyboardEvent) {
        // iOS does not deliver per-frame progress — fraction is meaningless for non-Progress phases.
        s_onKeyboardEvent(phase, kbX, kbY, kbW, kbH, duration, easing, 0.0f);
    }
}

- (void)setCursorPosX:(float)x y:(float)y w:(float)w h:(float)h {
    if (_transparentView) {
        CGRect rect;
        if (!UniTextTryUnityRectToHost(x, y, w, h, &rect)) return;
        [_transparentView setCursorRect:rect];
    }
}

- (void)setTextContext:(nullable NSString*)text
               version:(int)version
           windowStart:(NSInteger)windowStart
        selectionStart:(NSInteger)selectionStart
          selectionEnd:(NSInteger)selectionEnd
           forceRestart:(BOOL)forceRestart {
    [_transparentView setTextContext:text
                             version:version
                         windowStart:windowStart
                      selectionStart:selectionStart
                        selectionEnd:selectionEnd
                         forceRestart:forceRestart];
}

- (BOOL)showKeyboardWithArgs:(const UniTextShowKeyboardArgs*)args {
    if (_hidingKeyboard) return NO;
    UIView* unityView = UnityGetGLView();
    if (!unityView) return NO;
    if (args->sessionId <= 0) return NO;
    if ((_transparentView || _nativeTextField || _nativeTextView)
            && (!_inputProducerFrozen || _quiescingInput)) return NO;

    if (args->useNativeField) {
        NSString* passwordRules = args->passwordRules
            ? [NSString stringWithUTF8String:args->passwordRules]
            : nil;
        NSString* initialText = args->initialText
            ? [NSString stringWithUTF8String:args->initialText]
            : @"";
        NSString* presenterId = args->presenterId
            ? [NSString stringWithUTF8String:args->presenterId]
            : nil;
        NSString* placeholder = args->placeholder
            ? [NSString stringWithUTF8String:args->placeholder]
            : @"";
        NSString* identifier = args->accessibilityIdentifier
            ? [NSString stringWithUTF8String:args->accessibilityIdentifier]
            : @"";
        NSString* presenterData = args->presenterData
            ? [NSString stringWithUTF8String:args->presenterData]
            : @"";
        if ((args->passwordRules && !passwordRules) || !initialText || presenterId.length == 0
                || !placeholder || !identifier || !presenterData
                || args->authorityRevision < 1
                || args->selectionStart < 0 || args->selectionEnd < args->selectionStart
                || (NSUInteger)args->selectionEnd > initialText.length) return NO;
        UniTextEnsureSystemPresenter();
        UniTextNativeFieldPresenter* presenter = s_nativeFieldPresenters[presenterId];
        if (!presenter) return NO;

        UniTextTextKitInputView* previousTransparentView = _transparentView;
        UIView* previousField = _nativeTextField ?: _nativeTextView;
        UIView* previousContainer = _nativeFieldContainer;
        UIView* previousHostView = _nativeFieldHostView;
        UniTextNativeFieldPresenter* previousPresenter = _nativePresenter;
        UniTextNativeFieldContext previousContext = _nativeFieldContext;
        UniTextShowKeyboardArgs previousConfiguration = _nativeFieldConfiguration;
        UniTextShowKeyboardArgs previousAppliedConfiguration =
            _nativeFieldAppliedConfiguration;
        NSString* previousPasswordRules = _nativePasswordRules;
        NSString* previousAppliedPasswordRules = _nativeFieldAppliedPasswordRules;
        NSString* previousAuthoritativeText = _nativeFieldAuthoritativeText;
        NSRange previousAuthoritativeSelection = _nativeFieldAuthoritativeSelection;
        NSString* previousInitialText = _initialNativeText;
        NSString* previousPresenterId = _nativeContextPresenterId;
        NSString* previousPlaceholder = _nativeContextPlaceholder;
        NSString* previousIdentifier = _nativeContextIdentifier;
        NSString* previousPresenterData = _nativeContextPresenterData;
        int previousSessionId = _sessionId;
        int previousNativeRevision = _nativeRevision;
        int previousAuthorityRevision = _authorityRevision;
        int previousTransparentSessionId = _transparentSessionId;
        int previousTransparentRevision = _transparentRevision;
        BOOL previousInputProducerFrozen = _inputProducerFrozen;
        BOOL previousQuiescingInput = _quiescingInput;
        BOOL previousReplacingNativePresenter = _replacingNativePresenter;
        BOOL previousPresenterLayoutDirty = _nativePresenterLayoutDirty;
        BOOL previousHasPresenterLayoutSnapshot = _hasNativePresenterLayoutSnapshot;
        BOOL previousApplyPolicyAfterComposition = _applyNativeFieldPolicyAfterComposition;
        BOOL previousReloadInputViewsAfterComposition =
            _reloadNativeInputViewsAfterComposition;
        UITextInputPasswordRules* previousPasswordRulesObject = _nativePasswordRulesObject;
        CGRect previousPresenterBounds = _lastNativePresenterBounds;
        CGRect previousPresenterSuggestedFrame = _lastNativePresenterSuggestedFrame;
        UIEdgeInsets previousPresenterSafeAreaInsets = _lastNativePresenterSafeAreaInsets;
        CGFloat previousPresenterKeyboardTop = _lastNativePresenterKeyboardTop;

        _sessionId = args->sessionId;
        _nativeRevision = args->sessionId == previousSessionId ? previousNativeRevision : 0;
        _authorityRevision = args->authorityRevision;
        _transparentSessionId = 0;
        _transparentRevision = 0;
        _inputProducerFrozen = YES;
        _quiescingInput = previousQuiescingInput;
        _replacingNativePresenter = YES;
        _nativeFieldConfiguration = *args;
        _nativePasswordRules = [passwordRules copy];
        _nativePasswordRulesObject = nil;
        _nativeFieldConfiguration.passwordRules = _nativePasswordRules.UTF8String;
        _nativeFieldConfiguration.initialText = NULL;
        _nativeFieldConfiguration.accessibilityIdentifier = NULL;
        _nativeFieldConfiguration.placeholder = NULL;
        _nativeFieldConfiguration.presenterId = NULL;
        _nativeFieldConfiguration.presenterData = NULL;
        _nativeFieldAppliedPasswordRules = _nativePasswordRules;
        _nativeFieldAppliedConfiguration = _nativeFieldConfiguration;
        _nativeFieldAppliedConfiguration.passwordRules =
            _nativeFieldAppliedPasswordRules.UTF8String;
        _initialNativeText = [initialText copy];
        _nativeContextPresenterId = [presenterId copy];
        _nativeContextPlaceholder = [placeholder copy];
        _nativeContextIdentifier = [identifier copy];
        _nativeContextPresenterData = [presenterData copy];
        _nativeFieldContext.sessionId = _sessionId;
        _nativeFieldContext.wraps = args->wraps;
        _nativeFieldContext.acceptsNewlines = args->acceptsNewlines;
        _nativeFieldContext.returnKey = (UniTextNativeFieldReturnKey)args->returnKey;
        _nativeFieldContext.secureTextEntry = args->secureTextEntry;
        _nativeFieldContext.readOnly = args->readOnly;
        _nativeFieldContext.copyAllowed = args->copyAllowed;
        _nativeFieldContext.presenterId = _nativeContextPresenterId.UTF8String;
        _nativeFieldContext.placeholder = _nativeContextPlaceholder.UTF8String;
        _nativeFieldContext.identifier = _nativeContextIdentifier.UTF8String;
        _nativeFieldContext.presenterData = _nativeContextPresenterData.UTF8String;
        _nativeFieldHostView = unityView;
        NSRange selection = NSMakeRange((NSUInteger)args->selectionStart,
                                       (NSUInteger)(args->selectionEnd - args->selectionStart));
        UIView* field = nil;
        UIView* container = nil;
        if (![self createNativeFieldWithPresenter:presenter text:_initialNativeText
                                         selection:selection field:&field container:&container]) {
            _sessionId = previousSessionId;
            _nativeRevision = previousNativeRevision;
            _authorityRevision = previousAuthorityRevision;
            _transparentSessionId = previousTransparentSessionId;
            _transparentRevision = previousTransparentRevision;
            _inputProducerFrozen = previousInputProducerFrozen;
            _quiescingInput = previousQuiescingInput;
            _replacingNativePresenter = previousReplacingNativePresenter;
            _nativePresenterLayoutDirty = previousPresenterLayoutDirty;
            _hasNativePresenterLayoutSnapshot = previousHasPresenterLayoutSnapshot;
            _applyNativeFieldPolicyAfterComposition = previousApplyPolicyAfterComposition;
            _reloadNativeInputViewsAfterComposition =
                previousReloadInputViewsAfterComposition;
            _lastNativePresenterBounds = previousPresenterBounds;
            _lastNativePresenterSuggestedFrame = previousPresenterSuggestedFrame;
            _lastNativePresenterSafeAreaInsets = previousPresenterSafeAreaInsets;
            _lastNativePresenterKeyboardTop = previousPresenterKeyboardTop;
            _nativePasswordRulesObject = previousPasswordRulesObject;
            _nativeFieldHostView = previousHostView;
            _nativeFieldContext = previousContext;
            _nativeFieldConfiguration = previousConfiguration;
            _nativeFieldAppliedConfiguration = previousAppliedConfiguration;
            _nativePasswordRules = previousPasswordRules;
            _nativeFieldAppliedPasswordRules = previousAppliedPasswordRules;
            _nativeFieldAppliedConfiguration.passwordRules =
                _nativeFieldAppliedPasswordRules.UTF8String;
            _nativeFieldAuthoritativeText = previousAuthoritativeText;
            _nativeFieldAuthoritativeSelection = previousAuthoritativeSelection;
            _initialNativeText = previousInitialText;
            _nativeContextPresenterId = previousPresenterId;
            _nativeContextPlaceholder = previousPlaceholder;
            _nativeContextIdentifier = previousIdentifier;
            _nativeContextPresenterData = previousPresenterData;
            return NO;
        }
        if (previousTransparentView) {
            [previousTransparentView resignFirstResponder];
            [previousTransparentView removeFromSuperview];
            _transparentView = nil;
        }
        if (previousField) {
            if ([previousField isKindOfClass:[UniTextReplicaTextField class]]) {
                [[NSNotificationCenter defaultCenter] removeObserver:self
                                                                name:UITextFieldTextDidChangeNotification
                                                              object:previousField];
                ((UniTextReplicaTextField*)previousField).delegate = nil;
                ((UniTextReplicaTextField*)previousField).uniTextActionHandler = nil;
                ((UniTextReplicaTextField*)previousField).uniTextReturnKeyHandler = nil;
            } else {
                ((UniTextReplicaTextView*)previousField).delegate = nil;
                ((UniTextReplicaTextView*)previousField).uniTextActionHandler = nil;
                ((UniTextReplicaTextView*)previousField).uniTextReturnKeyHandler = nil;
            }
            [previousField resignFirstResponder];
            [self destroyNativeField:previousField container:previousContainer
                           presenter:previousPresenter context:&previousContext];
        }
        _nativeTextField = nil;
        _nativeTextView = nil;
        _nativeFieldContainer = nil;
        _nativePresenter = nil;
        _hasPendingNativeEdit = NO;
        _selectionFollowsNativeEdit = NO;
        _hasDeferredNativeState = NO;
        _deferredNativeHasText = NO;
        _applyNativeFieldPolicyAfterComposition = NO;
        _reloadNativeInputViewsAfterComposition = NO;
        [self clearNativeFieldComposition];
        _pendingNativeReplacement = nil;
        _pendingNativeOriginalText = nil;
        _deferredNativeText = nil;
        [_pendingQuiesces removeAllObjects];
        _pendingQuiesceDrainScheduled = NO;
        _inputProducerFrozen = NO;
        _quiescingInput = NO;
        [self activateNativeField:field container:container presenter:presenter
                         selection:selection focus:args->readOnly == 0];
        _replacingNativePresenter = NO;
        _initialNativeText = nil;
    } else {
        // Keep an existing transparent view alive across refocus — tearing it
        // down and recreating flickers the keyboard.
        BOOL created = _transparentView == nil;
        UniTextTextKitInputView* nextTransparentView = created
            ? [[UniTextTextKitInputView alloc] initWithFrame:CGRectMake(0, 0, 1, 1)]
            : _transparentView;
        if (!nextTransparentView) return NO;

        [self applyTraits:nextTransparentView args:args];
        nextTransparentView.showsSoftwareKeyboard = args->showSoftwareKeyboard != 0;
        nextTransparentView.inputProducerFrozen = NO;

        if (created) {
            [unityView addSubview:nextTransparentView];
        } else if (nextTransparentView.superview != unityView) {
            [nextTransparentView removeFromSuperview];
            [unityView addSubview:nextTransparentView];
        }
        const char* accessibilityIdentifier = args->accessibilityIdentifier;
        if (accessibilityIdentifier && accessibilityIdentifier[0] != '\0') {
            nextTransparentView.accessibilityIdentifier =
                [NSString stringWithUTF8String:accessibilityIdentifier];
        }
        if (_nativeTextField || _nativeTextView) [self hideKeyboard];
        _transparentView = nextTransparentView;
        _transparentSessionId = args->sessionId;
        _transparentRevision = 0;
        _sessionId = 0;
        _nativeRevision = 0;
        _authorityRevision = 0;
        _inputProducerFrozen = NO;
        _quiescingInput = NO;
        [_transparentView reloadInputViews];
        [_transparentView becomeFirstResponder];
    }

    [self applyPendingRectIfPossible];
    return YES;
}

- (UIView*)currentInputViewForOverlay {
    if (_nativeTextField) return _nativeTextField;
    if (_nativeTextView) return _nativeTextView;
    if (_transparentView) return _transparentView;
    return nil;
}

- (void)setInputFieldRectUnityX:(float)unityX unityY:(float)unityY
                          width:(float)unityW height:(float)unityH {
    if (_hasPendingRect && _pendingRectUnityX == unityX && _pendingRectUnityY == unityY
            && _pendingRectUnityW == unityW && _pendingRectUnityH == unityH) return;
    _pendingRectUnityX = unityX;
    _pendingRectUnityY = unityY;
    _pendingRectUnityW = unityW;
    _pendingRectUnityH = unityH;
    _hasPendingRect = YES;

    [self applyPendingRectIfPossible];
}

- (void)layoutNativeField:(UIView*)field
                container:(UIView*)container
                presenter:(UniTextNativeFieldPresenter*)presenter
                    force:(BOOL)force {
    CGRect bounds = _nativeFieldHostView.bounds;
    UIEdgeInsets safe = _nativeFieldHostView.safeAreaInsets;
    CGRect suggestedFieldFrame = CGRectNull;
    if (_hasPendingRect)
        UniTextTryUnityRectToHost(_pendingRectUnityX, _pendingRectUnityY,
                                 _pendingRectUnityW, _pendingRectUnityH,
                                 &suggestedFieldFrame);
    CGFloat keyboardTop = _keyboardVisible && !CGRectIsEmpty(_keyboardFrame)
        ? CGRectGetMinY(_keyboardFrame)
        : CGRectGetMaxY(bounds) - safe.bottom;
    BOOL suggestedFrameMatches =
        (CGRectIsNull(suggestedFieldFrame)
            && CGRectIsNull(_lastNativePresenterSuggestedFrame))
        || CGRectEqualToRect(suggestedFieldFrame, _lastNativePresenterSuggestedFrame);
    if (!force && !_nativePresenterLayoutDirty && _hasNativePresenterLayoutSnapshot
            && CGRectEqualToRect(bounds, _lastNativePresenterBounds)
            && UIEdgeInsetsEqualToEdgeInsets(safe, _lastNativePresenterSafeAreaInsets)
            && suggestedFrameMatches
            && keyboardTop == _lastNativePresenterKeyboardTop) return;
    NSUInteger contentMutationEpoch = UniTextNativeFieldIsMultiLine(&_nativeFieldContext)
        ? ((UniTextReplicaTextView*)field).uniTextContentMutationEpoch
        : ((UniTextReplicaTextField*)field).uniTextContentMutationEpoch;
    NSUInteger selectionMutationEpoch = UniTextNativeFieldIsMultiLine(&_nativeFieldContext)
        ? ((UniTextReplicaTextView*)field).uniTextSelectionMutationEpoch
        : ((UniTextReplicaTextField*)field).uniTextSelectionMutationEpoch;
    UIView* fieldSuperview = field.superview;
    UIView* containerSuperview = container.superview;
    BOOL previousSyncing = _syncingNativeField;
    _syncingNativeField = YES;
    [self setNativeField:field presenterCallbackActive:YES];
    presenter.layout(&_nativeFieldContext, field, container,
                     bounds, safe, suggestedFieldFrame, keyboardTop);
    [self setNativeField:field presenterCallbackActive:NO];
    BOOL retainedHierarchy = [self restoreNativeFieldHierarchy:field
        container:container fieldSuperview:fieldSuperview
        containerSuperview:containerSuperview];
    BOOL contentMutated = contentMutationEpoch != (UniTextNativeFieldIsMultiLine(&_nativeFieldContext)
        ? ((UniTextReplicaTextView*)field).uniTextContentMutationEpoch
        : ((UniTextReplicaTextField*)field).uniTextContentMutationEpoch);
    BOOL selectionMutated = selectionMutationEpoch != (UniTextNativeFieldIsMultiLine(&_nativeFieldContext)
        ? ((UniTextReplicaTextView*)field).uniTextSelectionMutationEpoch
        : ((UniTextReplicaTextField*)field).uniTextSelectionMutationEpoch);
    if (contentMutated || selectionMutated)
        [self configureNativeField:field text:_nativeFieldAuthoritativeText
                         selection:_nativeFieldAuthoritativeSelection
                 reloadInputViews:NO];
    else
        [self configureNativeFieldPolicy:field];
    _syncingNativeField = previousSyncing;
    if (!retainedHierarchy)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter detached its replica during layout."];
    _lastNativePresenterBounds = bounds;
    _lastNativePresenterSafeAreaInsets = safe;
    _lastNativePresenterSuggestedFrame = suggestedFieldFrame;
    _lastNativePresenterKeyboardTop = keyboardTop;
    _hasNativePresenterLayoutSnapshot = YES;
    _nativePresenterLayoutDirty = NO;
}

- (void)applyPendingRectIfPossible {
    UIView* view = [self currentInputViewForOverlay];
    if (!view) return;

    if (view == _nativeTextField || view == _nativeTextView) {
        if (!_nativeFieldHostView || !_nativeFieldContainer || !_nativePresenter) return;
        [self layoutNativeField:view container:_nativeFieldContainer
                      presenter:_nativePresenter force:NO];
        return;
    }

    if (!_hasPendingRect) return;
    CGRect newFrame;
    if (!UniTextTryUnityRectToHost(_pendingRectUnityX, _pendingRectUnityY,
                                  _pendingRectUnityW, _pendingRectUnityH, &newFrame)) return;
    if (!CGRectEqualToRect(view.frame, newFrame)) {
        view.frame = newFrame;
    }
}

- (void)applyTraits:(id<UITextInputTraits>)traits args:(const UniTextShowKeyboardArgs*)args {
    UIKeyboardType keyboardType = [self mapKeyboardType:args->keyboardType];
    UIReturnKeyType returnKeyType = [self mapReturnKeyType:args->returnKeyType];
    UITextAutocapitalizationType capitalization =
        [self mapAutoCapitalization:args->autoCapitalization];
    UITextAutocorrectionType correction = [self mapAutoCorrection:args->autoCorrection];
    UITextSpellCheckingType spellChecking = [self mapSpellChecking:args->spellChecking];
    BOOL secure = args->secureTextEntry != 0;
    BOOL automaticReturn = args->enablesReturnKeyAuto != 0;
    UITextSmartQuotesType smartQuotes = [self mapSmartQuotes:args->smartQuotes];
    UITextSmartDashesType smartDashes = [self mapSmartDashes:args->smartDashes];
    UITextSmartInsertDeleteType smartInsertDelete =
        [self mapSmartInsertDelete:args->smartInsertDelete];
    UITextContentType contentType = [self mapAutofillHint:args->autofillHint];
    if (traits.keyboardType != keyboardType) traits.keyboardType = keyboardType;
    if (traits.returnKeyType != returnKeyType) traits.returnKeyType = returnKeyType;
    if (traits.autocapitalizationType != capitalization)
        traits.autocapitalizationType = capitalization;
    if (traits.autocorrectionType != correction) traits.autocorrectionType = correction;
    if (traits.spellCheckingType != spellChecking) traits.spellCheckingType = spellChecking;
    if (traits.secureTextEntry != secure) traits.secureTextEntry = secure;
    if (traits.enablesReturnKeyAutomatically != automaticReturn)
        traits.enablesReturnKeyAutomatically = automaticReturn;
    if (traits.smartQuotesType != smartQuotes) traits.smartQuotesType = smartQuotes;
    if (traits.smartDashesType != smartDashes) traits.smartDashesType = smartDashes;
    if (traits.smartInsertDeleteType != smartInsertDelete)
        traits.smartInsertDeleteType = smartInsertDelete;
    if (traits.textContentType != contentType && ![traits.textContentType isEqualToString:contentType])
        traits.textContentType = contentType;

    const char* passwordRules = args->passwordRules;
    if (passwordRules && passwordRules[0] != '\0') {
        if (!_nativePasswordRulesObject) {
            NSString* rulesStr = [NSString stringWithUTF8String:passwordRules];
            if (rulesStr.length > 0)
                _nativePasswordRulesObject =
                    [UITextInputPasswordRules passwordRulesWithDescriptor:rulesStr];
        }
        if (traits.passwordRules != _nativePasswordRulesObject)
            traits.passwordRules = _nativePasswordRulesObject;
        return;
    }
    _nativePasswordRulesObject = nil;
    if (traits.passwordRules) traits.passwordRules = nil;
}

- (NSString*)nativeFieldTextForField:(UIView*)field {
    if ([field isKindOfClass:[UITextField class]]) return ((UITextField*)field).text ?: @"";
    if ([field isKindOfClass:[UITextView class]]) return ((UITextView*)field).textStorage.string ?: @"";
    return @"";
}

- (NSString*)nativeFieldText {
    return [self nativeFieldTextForField:_nativeTextField ?: _nativeTextView];
}

- (NSRange)nativeFieldSelectionForField:(UIView*)field {
    id<UITextInput> input = (id<UITextInput>)field;
    UITextRange* range = input.selectedTextRange;
    if (!range) return NSMakeRange(0, 0);
    NSInteger start = [input offsetFromPosition:input.beginningOfDocument toPosition:range.start];
    NSInteger end = [input offsetFromPosition:input.beginningOfDocument toPosition:range.end];
    NSInteger length = (NSInteger)[self nativeFieldTextForField:field].length;
    if (start < 0 || end < start || end > length)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field exposed an invalid selection range."];
    return NSMakeRange((NSUInteger)start, (NSUInteger)(end - start));
}

- (NSRange)nativeFieldSelection {
    UIView* field = _nativeTextField ?: _nativeTextView;
    return field ? [self nativeFieldSelectionForField:field] : NSMakeRange(0, 0);
}

- (void)applyNativeSelection:(NSRange)selection toField:(UIView*)field {
    if (!field) return;
    id<UITextInput> input = (id<UITextInput>)field;
    NSInteger length = (NSInteger)[self nativeFieldTextForField:field].length;
    if (selection.location > (NSUInteger)length
            || selection.length > (NSUInteger)length - selection.location)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field received an invalid selection range."];
    NSInteger start = (NSInteger)selection.location;
    NSInteger end = (NSInteger)NSMaxRange(selection);
    if (NSEqualRanges([self nativeFieldSelectionForField:field], selection)) return;
    UITextPosition* startPosition = [input positionFromPosition:input.beginningOfDocument offset:start];
    UITextPosition* endPosition = [input positionFromPosition:input.beginningOfDocument offset:end];
    UITextRange* range = startPosition && endPosition
        ? [input textRangeFromPosition:startPosition toPosition:endPosition]
        : nil;
    if (!range)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field could not project its authoritative selection."];
    input.selectedTextRange = range;
}

- (void)applyNativeSelection:(NSRange)selection {
    [self applyNativeSelection:selection toField:_nativeTextField ?: _nativeTextView];
}

- (NSRange)nativeFieldMarkedRange {
    UIView* field = _nativeTextField ?: _nativeTextView;
    if (!field) return NSMakeRange(NSNotFound, 0);
    id<UITextInput> input = (id<UITextInput>)field;
    UITextRange* range = input.markedTextRange;
    if (!range) return NSMakeRange(NSNotFound, 0);
    NSInteger start = [input offsetFromPosition:input.beginningOfDocument toPosition:range.start];
    NSInteger end = [input offsetFromPosition:input.beginningOfDocument toPosition:range.end];
    NSInteger length = (NSInteger)[self nativeFieldTextForField:field].length;
    if (start < 0 || end < start || end > length)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field exposed an invalid marked range."];
    return NSMakeRange((NSUInteger)start, (NSUInteger)(end - start));
}

- (void)configureNativeField:(UIView*)field
                        text:(NSString*)text
                   selection:(NSRange)selection
              reloadInputViews:(BOOL)reloadInputViews {
    BOOL wasFirstResponder = field.isFirstResponder;
    [self configureNativeFieldPolicy:field];
    NSString* value = text ?: @"";
    if (UniTextNativeFieldIsMultiLine(&_nativeFieldContext)) {
        UniTextReplicaTextView* textView = (UniTextReplicaTextView*)field;
        if (![textView.text isEqualToString:value]) textView.text = value;
    } else {
        UniTextReplicaTextField* textField = (UniTextReplicaTextField*)field;
        if (![textField.text isEqualToString:value]) textField.text = value;
    }
    [self applyNativeSelection:selection toField:field];
    _nativeFieldAuthoritativeText = [value copy];
    _nativeFieldAuthoritativeSelection = selection;
    if (_nativeFieldAppliedConfiguration.readOnly && wasFirstResponder)
        [field resignFirstResponder];
    else if (wasFirstResponder && reloadInputViews) [field reloadInputViews];
}

- (void)configureNativeFieldPolicy:(UIView*)field {
    if (!_nativeFieldActionHandler) {
        _nativeFieldActionHandler = ^(const char* action, int modifiers) {
            [self emitNativeAction:action modifiers:modifiers];
        };
    }
    if (!_nativeFieldReturnKeyHandler) {
        _nativeFieldReturnKeyHandler = ^(int modifiers) {
            [self emitNativeAction:UniTextNativeFieldReturnKeyAction(&self->_nativeFieldContext)
                         modifiers:modifiers];
        };
    }
    if (UniTextNativeFieldIsMultiLine(&_nativeFieldContext)) {
        UniTextReplicaTextView* textView = (UniTextReplicaTextView*)field;
        textView.delegate = self;
        if (textView.uniTextActionHandler != _nativeFieldActionHandler)
            textView.uniTextActionHandler = _nativeFieldActionHandler;
        if (textView.uniTextReturnKeyHandler != _nativeFieldReturnKeyHandler)
            textView.uniTextReturnKeyHandler = _nativeFieldReturnKeyHandler;
        BOOL copyAllowed = _nativeFieldContext.copyAllowed != 0;
        BOOL readOnly = _nativeFieldAppliedConfiguration.readOnly != 0;
        NSString* identifier = _nativeContextIdentifier.length > 0
            ? _nativeContextIdentifier
            : nil;
        if (textView.uniTextCopyAllowed != copyAllowed)
            textView.uniTextCopyAllowed = copyAllowed;
        if (textView.uniTextReadOnly != readOnly) textView.uniTextReadOnly = readOnly;
        if (textView.editable == readOnly) textView.editable = !readOnly;
        if (!textView.selectable) textView.selectable = YES;
        if (textView.placeholder != _nativeContextPlaceholder
                && ![textView.placeholder isEqualToString:_nativeContextPlaceholder])
            textView.placeholder = _nativeContextPlaceholder;
        if (textView.accessibilityIdentifier != identifier
                && ![textView.accessibilityIdentifier isEqualToString:identifier])
            textView.accessibilityIdentifier = identifier;
        [self applyTraits:textView args:&_nativeFieldAppliedConfiguration];
    } else {
        UniTextReplicaTextField* textField = (UniTextReplicaTextField*)field;
        textField.delegate = self;
        if (textField.uniTextActionHandler != _nativeFieldActionHandler)
            textField.uniTextActionHandler = _nativeFieldActionHandler;
        if (textField.uniTextReturnKeyHandler != _nativeFieldReturnKeyHandler)
            textField.uniTextReturnKeyHandler = _nativeFieldReturnKeyHandler;
        BOOL copyAllowed = _nativeFieldContext.copyAllowed != 0;
        BOOL readOnly = _nativeFieldAppliedConfiguration.readOnly != 0;
        NSString* identifier = _nativeContextIdentifier.length > 0
            ? _nativeContextIdentifier
            : nil;
        if (textField.uniTextCopyAllowed != copyAllowed)
            textField.uniTextCopyAllowed = copyAllowed;
        if (textField.uniTextReadOnly != readOnly) textField.uniTextReadOnly = readOnly;
        if (!textField.enabled) textField.enabled = YES;
        if (textField.placeholder != _nativeContextPlaceholder
                && ![textField.placeholder isEqualToString:_nativeContextPlaceholder])
            textField.placeholder = _nativeContextPlaceholder;
        if (textField.accessibilityIdentifier != identifier
                && ![textField.accessibilityIdentifier isEqualToString:identifier])
            textField.accessibilityIdentifier = identifier;
        [self applyTraits:textField args:&_nativeFieldAppliedConfiguration];
    }
}

- (void)setNativeField:(UIView*)field presenterCallbackActive:(BOOL)active {
    if ([field isKindOfClass:[UniTextReplicaTextView class]])
        ((UniTextReplicaTextView*)field).uniTextPresenterCallbackActive = active;
    else if ([field isKindOfClass:[UniTextReplicaTextField class]])
        ((UniTextReplicaTextField*)field).uniTextPresenterCallbackActive = active;
}

- (BOOL)isValidNativeFieldHierarchy:(UIView*)field container:(UIView*)container {
    return field && container && container != _nativeFieldHostView
        && (field == container || [field isDescendantOfView:container])
        && [container isDescendantOfView:_nativeFieldHostView];
}

- (BOOL)restoreNativeFieldHierarchy:(UIView*)field
                          container:(UIView*)container
                     fieldSuperview:(UIView*)fieldSuperview
                 containerSuperview:(UIView*)containerSuperview {
    BOOL retained = field.superview == fieldSuperview
        && container.superview == containerSuperview
        && [self isValidNativeFieldHierarchy:field container:container];
    if (retained) return YES;
    if (container.superview != containerSuperview) {
        [container removeFromSuperview];
        [containerSuperview addSubview:container];
    }
    if (field.superview != fieldSuperview) {
        [field removeFromSuperview];
        [fieldSuperview addSubview:field];
    }
    if (![self isValidNativeFieldHierarchy:field container:container]) {
        [field removeFromSuperview];
        [container addSubview:field];
    }
    return NO;
}

- (void)destroyNativeField:(UIView*)field
                 container:(UIView*)container
                 presenter:(UniTextNativeFieldPresenter*)presenter
                   context:(const UniTextNativeFieldContext*)context {
    if (presenter && field && container) presenter.destroy(context, field, container);
    [field removeFromSuperview];
    if (container != _nativeFieldHostView) [container removeFromSuperview];
}

- (BOOL)createNativeFieldWithPresenter:(UniTextNativeFieldPresenter*)presenter
                                  text:(NSString*)text
                             selection:(NSRange)selection
                                 field:(UIView**)fieldOut
                             container:(UIView**)containerOut {
    UIView* container = nil;
    UIView* field = presenter.create(&_nativeFieldContext, _nativeFieldHostView, &container);
    if (field && !container) container = field;
    Class expectedClass = UniTextNativeFieldIsMultiLine(&_nativeFieldContext)
        ? [UniTextReplicaTextView class]
        : [UniTextReplicaTextField class];
    BOOL valid = field && container && container != _nativeFieldHostView
        && [field isKindOfClass:expectedClass]
        && (field == container || [field isDescendantOfView:container]);
    if (valid && !container.superview) [_nativeFieldHostView addSubview:container];
    valid = valid && [self isValidNativeFieldHierarchy:field container:container];
    if (!valid) {
        [self destroyNativeField:field container:container presenter:presenter
                         context:&_nativeFieldContext];
        return NO;
    }

    container.hidden = YES;
    _syncingNativeField = YES;
    [self suppressNativeFieldCallbacksThroughTurn];
    [self configureNativeField:field text:text selection:selection
            reloadInputViews:NO];
    [self setNativeField:field presenterCallbackActive:YES];
    presenter.update(&_nativeFieldContext, field, container);
    [self setNativeField:field presenterCallbackActive:NO];
    if (![self isValidNativeFieldHierarchy:field container:container]) {
        _syncingNativeField = NO;
        [self destroyNativeField:field container:container presenter:presenter
                         context:&_nativeFieldContext];
        return NO;
    }
    [self configureNativeField:field text:text selection:selection
            reloadInputViews:NO];
    [self layoutNativeField:field container:container presenter:presenter force:YES];
    container.hidden = YES;
    _syncingNativeField = NO;
    if (fieldOut) *fieldOut = field;
    if (containerOut) *containerOut = container;
    return YES;
}

- (void)activateNativeField:(UIView*)field
                  container:(UIView*)container
                  presenter:(UniTextNativeFieldPresenter*)presenter
                   selection:(NSRange)selection
                       focus:(BOOL)focus {
    _nativeTextField = [field isKindOfClass:[UniTextReplicaTextField class]]
        ? (UniTextReplicaTextField*)field
        : nil;
    _nativeTextView = [field isKindOfClass:[UniTextReplicaTextView class]]
        ? (UniTextReplicaTextView*)field
        : nil;
    _nativeFieldContainer = container;
    _nativePresenter = presenter;
    s_activeNativeFieldPresenterId = [_nativeContextPresenterId copy];
    if (_nativeTextField) {
        [[NSNotificationCenter defaultCenter] addObserver:self
                                                 selector:@selector(textFieldDidChange:)
                                                     name:UITextFieldTextDidChangeNotification
                                                   object:_nativeTextField];
    }
    _syncingNativeField = YES;
    [self suppressNativeFieldCallbacksThroughTurn];
    container.hidden = NO;
    [self applyPendingRectIfPossible];
    if (focus && _nativeFieldContext.readOnly == 0) [field becomeFirstResponder];
    [self applyNativeSelection:selection];
    _syncingNativeField = NO;
}

- (void)hideKeyboard {
    if (_hidingKeyboard) return;
    _hidingKeyboard = YES;
    if (_transparentView) {
        [_transparentView resignFirstResponder];
        [_transparentView removeFromSuperview];
        _transparentView = nil;
    }

    UIView* field = _nativeTextField ?: _nativeTextView;
    UIView* container = _nativeFieldContainer;
    UniTextNativeFieldPresenter* presenter = _nativePresenter;
    if (_nativeTextField) {
        [[NSNotificationCenter defaultCenter] removeObserver:self
                                                        name:UITextFieldTextDidChangeNotification
                                                      object:_nativeTextField];
    }
    if (field) {
        if ([field isKindOfClass:[UniTextReplicaTextField class]]) {
            ((UniTextReplicaTextField*)field).delegate = nil;
            ((UniTextReplicaTextField*)field).uniTextActionHandler = nil;
            ((UniTextReplicaTextField*)field).uniTextReturnKeyHandler = nil;
        } else {
            ((UniTextReplicaTextView*)field).delegate = nil;
            ((UniTextReplicaTextView*)field).uniTextActionHandler = nil;
            ((UniTextReplicaTextView*)field).uniTextReturnKeyHandler = nil;
        }
        [field resignFirstResponder];
        [self destroyNativeField:field container:container presenter:presenter
                         context:&_nativeFieldContext];
    }
    _nativeTextField = nil;
    _nativeTextView = nil;
    _nativeFieldContainer = nil;
    _nativeFieldHostView = nil;
    _nativePresenter = nil;
    _nativePresenterLayoutDirty = NO;
    _hasNativePresenterLayoutSnapshot = NO;
    _applyNativeFieldPolicyAfterComposition = NO;
    _reloadNativeInputViewsAfterComposition = NO;
    _hasPendingRect = NO;
    _hasPendingNativeEdit = NO;
    _selectionFollowsNativeEdit = NO;
    _hasDeferredNativeState = NO;
    _deferredNativeHasText = NO;
    [self clearNativeFieldComposition];
    _pendingNativeReplacement = nil;
    _pendingNativeOriginalText = nil;
    _initialNativeText = nil;
    _deferredNativeText = nil;
    _nativeContextPresenterId = nil;
    _nativeContextPlaceholder = nil;
    _nativeContextIdentifier = nil;
    _nativeContextPresenterData = nil;
    _nativePasswordRules = nil;
    _nativeFieldAppliedPasswordRules = nil;
    _nativeFieldAuthoritativeText = nil;
    _nativeFieldAuthoritativeSelection = NSMakeRange(0, 0);
    _nativePasswordRulesObject = nil;
    _nativeFieldActionHandler = nil;
    _nativeFieldReturnKeyHandler = nil;
    memset(&_nativeFieldContext, 0, sizeof(_nativeFieldContext));
    memset(&_nativeFieldConfiguration, 0, sizeof(_nativeFieldConfiguration));
    memset(&_nativeFieldAppliedConfiguration, 0, sizeof(_nativeFieldAppliedConfiguration));
    s_activeNativeFieldPresenterId = nil;
    _sessionId = 0;
    _nativeRevision = 0;
    _authorityRevision = 0;
    _transparentSessionId = 0;
    _transparentRevision = 0;
    _inputProducerFrozen = NO;
    _quiescingInput = NO;
    [_pendingQuiesces removeAllObjects];
    _pendingQuiesceDrainScheduled = NO;
    _suppressingNativeFieldCallbacks = NO;
    _nativeCallbackSuppressionGeneration++;
    _replacingNativePresenter = NO;
    _hidingKeyboard = NO;
}

- (BOOL)textFieldShouldReturn:(UITextField*)textField {
    if (textField != _nativeTextField) return YES;
    if (_inputProducerFrozen || _quiescingInput) return NO;
    [self emitNativeAction:UniTextNativeFieldReturnKeyAction(&_nativeFieldContext) modifiers:0];
    return NO;
}

- (void)textFieldDidChange:(NSNotification*)notification {
    UITextField* field = notification.object;
    if (field == _nativeTextField && !_inputProducerFrozen && !_syncingNativeField) {
        _nativeFieldAuthoritativeText = [field.text copy] ?: @"";
        _nativeFieldAuthoritativeSelection = [self nativeFieldSelectionForField:field];
        _nativePresenterLayoutDirty = YES;
        [self applyPendingRectIfPossible];
        [self handleNativeFieldChange];
    }
}

- (BOOL)textFieldShouldEndEditing:(UITextField*)textField {
    return YES;
}

- (BOOL)textField:(UITextField*)textField shouldChangeCharactersInRange:(NSRange)range
                                                     replacementString:(NSString*)string {
    if (textField != _nativeTextField) return YES;
    if (_nativeFieldAppliedConfiguration.readOnly || _inputProducerFrozen || _quiescingInput)
        return NO;
    if (_suppressingNativeFieldCallbacks) {
        _suppressingNativeFieldCallbacks = NO;
        _nativeCallbackSuppressionGeneration++;
    }
    NSString* currentText = textField.text ?: @"";
    NSUInteger length = currentText.length;
    if (range.location > length || range.length > length - range.location)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native text field proposed an invalid edit range."];
    _pendingNativeRange = range;
    _pendingNativeReplacement = [string copy] ?: @"";
    _pendingNativeOriginalText = [currentText substringWithRange:_pendingNativeRange];
    _hasPendingNativeEdit = YES;
    return YES;
}

- (void)textFieldDidEndEditing:(UITextField*)textField {
    if (textField == _nativeTextField && !_hidingKeyboard && !_replacingNativePresenter
            && !_syncingNativeField && !_suppressingNativeFieldCallbacks
            && !_inputProducerFrozen && !_quiescingInput)
        [self emitNativeAction:"cancel" modifiers:0];
}

- (void)textFieldDidChangeSelection:(UITextField*)textField {
    if (textField != _nativeTextField || _inputProducerFrozen || _syncingNativeField) return;
    _nativeFieldAuthoritativeSelection = [self nativeFieldSelectionForField:textField];
    if ([self nativeFieldMarkedRange].location != NSNotFound)
        [self handleNativeFieldChange];
    else
        [self emitNativeSelection];
}

- (void)textViewDidChange:(UITextView*)textView {
    if (textView != _nativeTextView || _inputProducerFrozen || _syncingNativeField) return;
    [(UniTextReplicaTextView*)textView updatePlaceholder];
    _nativeFieldAuthoritativeText = [textView.textStorage.string copy] ?: @"";
    _nativeFieldAuthoritativeSelection = [self nativeFieldSelectionForField:textView];
    _nativePresenterLayoutDirty = YES;
    [self applyPendingRectIfPossible];
    [self handleNativeFieldChange];
}

- (void)textViewDidEndEditing:(UITextView*)textView {
    if (textView == _nativeTextView && !_hidingKeyboard && !_replacingNativePresenter
            && !_syncingNativeField && !_suppressingNativeFieldCallbacks
            && !_inputProducerFrozen && !_quiescingInput)
        [self emitNativeAction:"cancel" modifiers:0];
}

- (BOOL)textView:(UITextView*)textView shouldChangeTextInRange:(NSRange)range
                                               replacementText:(NSString*)text {
    if (textView != _nativeTextView) return YES;
    if (_nativeFieldAppliedConfiguration.readOnly || _inputProducerFrozen || _quiescingInput)
        return NO;
    if (_suppressingNativeFieldCallbacks) {
        _suppressingNativeFieldCallbacks = NO;
        _nativeCallbackSuppressionGeneration++;
    }
    if ([text isEqualToString:@"\n"]
            && [self nativeFieldMarkedRange].location == NSNotFound) {
        [self emitNativeAction:UniTextNativeFieldReturnKeyAction(&_nativeFieldContext) modifiers:0];
        return NO;
    }
    NSString* currentText = textView.textStorage.string ?: @"";
    NSUInteger length = currentText.length;
    if (range.location > length || range.length > length - range.location)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native text view proposed an invalid edit range."];
    _pendingNativeRange = range;
    _pendingNativeReplacement = [text copy] ?: @"";
    _pendingNativeOriginalText = [currentText substringWithRange:_pendingNativeRange];
    _hasPendingNativeEdit = YES;
    return YES;
}

- (void)textViewDidChangeSelection:(UITextView*)textView {
    if (textView != _nativeTextView || _inputProducerFrozen || _syncingNativeField) return;
    _nativeFieldAuthoritativeSelection = [self nativeFieldSelectionForField:textView];
    if ([self nativeFieldMarkedRange].location != NSNotFound)
        [self handleNativeFieldChange];
    else
        [self emitNativeSelection];
}

- (void)beginNativeCallbackForRevision:(int)revision {
    if (_nativeCallbackInFlightRevision != 0)
        [NSException raise:NSInternalInconsistencyException
                    format:@"Native field callbacks cannot nest."];
    _nativeCallbackInFlightRevision = revision;
    _nativeCallbackAcknowledged = NO;
}

- (void)endNativeCallbackForRevision:(int)revision {
    if (_nativeCallbackInFlightRevision != revision)
        [NSException raise:NSInternalInconsistencyException
                    format:@"Native field callback ordering was corrupted."];
    BOOL acknowledged = _nativeCallbackAcknowledged;
    _nativeCallbackInFlightRevision = 0;
    _nativeCallbackAcknowledged = NO;
    if (acknowledged) [self schedulePendingQuiesces];
}

- (int)advanceRevision:(int*)revision sessionId:(int)sessionId {
    if (*revision < INT_MAX) return ++*revision;
    const char* message = revision == &_nativeRevision
        ? "The iOS native field revision space is exhausted."
        : "The iOS transparent input revision space is exhausted.";
    if (![self abortInputForSession:sessionId])
        NSLog(@"UniText native input revision cleanup found no matching session.");
    if (s_onNativeInputFault) s_onNativeInputFault(sessionId, message);
    else NSLog(@"%s", message);
    return 0;
}

- (int)beginTransparentCallback {
    if (!_transparentView || _transparentSessionId <= 0) return 0;
    if (_transparentCallbackDepth++ > 0) return _nativeCallbackInFlightRevision;
    int revision = [self advanceRevision:&_transparentRevision
                               sessionId:_transparentSessionId];
    if (revision == 0) {
        _transparentCallbackDepth = 0;
        return 0;
    }
    [self beginNativeCallbackForRevision:revision];
    _nativeCallbackAcknowledged = YES;
    return revision;
}

- (void)endTransparentCallback:(int)revision {
    if (revision <= 0) return;
    if (_transparentCallbackDepth <= 0)
        [NSException raise:NSInternalInconsistencyException
                    format:@"Transparent input callback ordering was corrupted."];
    if (--_transparentCallbackDepth == 0)
        [self endNativeCallbackForRevision:revision];
}

- (void)enqueueQuiesceForSession:(int)sessionId
                     disposition:(int)disposition
                       requestId:(int)requestId {
    if (!_pendingQuiesces) _pendingQuiesces = [NSMutableArray array];
    [_pendingQuiesces addObject:@[@(sessionId), @(disposition), @(requestId)]];
}

- (void)schedulePendingQuiesces {
    if (_pendingQuiesceDrainScheduled || _pendingQuiesces.count == 0
            || _nativeCallbackInFlightRevision != 0) return;
    _pendingQuiesceDrainScheduled = YES;
    dispatch_async(dispatch_get_main_queue(), ^{
        self->_pendingQuiesceDrainScheduled = NO;
        [self drainPendingQuiesces];
    });
}

- (void)drainPendingQuiesces {
    if (_nativeCallbackInFlightRevision != 0 || _quiescingInput) return;
    while (_pendingQuiesces.count > 0) {
        NSArray<NSNumber*>* request = _pendingQuiesces.firstObject;
        [_pendingQuiesces removeObjectAtIndex:0];
        [self quiesceInputForSession:request[0].intValue
                        disposition:request[1].intValue
                          requestId:request[2].intValue];
        if (_nativeCallbackInFlightRevision != 0 || _quiescingInput) return;
    }
}

- (void)emitNativeSelection {
    if (_syncingNativeField || _suppressingNativeFieldCallbacks || _hasPendingNativeEdit
            || _nativeFieldCompositionActive || _selectionFollowsNativeEdit
            || _inputProducerFrozen) return;
    NSRange selection = [self nativeFieldSelection];
    int revision = [self advanceRevision:&_nativeRevision sessionId:_sessionId];
    if (revision == 0) return;
    if (s_onNativeFieldSelection) {
        [self beginNativeCallbackForRevision:revision];
        s_onNativeFieldSelection(_sessionId, revision, _authorityRevision,
                                 (int)selection.location, (int)NSMaxRange(selection));
        [self endNativeCallbackForRevision:revision];
    }
}

- (void)handleNativeFieldChange {
    if (_syncingNativeField || _suppressingNativeFieldCallbacks || _inputProducerFrozen) return;
    NSRange marked = [self nativeFieldMarkedRange];
    NSRange selection = [self nativeFieldSelection];

    if (marked.location != NSNotFound) {
        NSString* text = [self nativeFieldText];
        if (!_nativeFieldCompositionActive) {
            if (!_hasPendingNativeEdit)
                [NSException raise:NSInternalInconsistencyException
                            format:@"A native composition changed without an exact replacement intent."];
            _nativeFieldCompositionActive = YES;
            _nativeCompositionRange = _pendingNativeRange;
            _nativeCompositionOriginalText = [_pendingNativeOriginalText copy] ?: @"";
            _nativeCompositionBaseLength = text.length - _pendingNativeReplacement.length
                + _pendingNativeRange.length;
        }
        if (NSMaxRange(marked) > text.length)
            [NSException raise:NSInternalInconsistencyException
                        format:@"A native composition reported an invalid marked range."];
        NSString* compositionText = [text substringWithRange:marked];
        NSInteger cursor = MAX(0, MIN((NSInteger)selection.location - (NSInteger)marked.location,
                                      (NSInteger)compositionText.length));
        _hasPendingNativeEdit = NO;
        _pendingNativeReplacement = nil;
        _pendingNativeOriginalText = nil;
        if (!_hasNativeCompositionSnapshot
                || ![_lastNativeCompositionText isEqualToString:compositionText]
                || _lastNativeCompositionCursor != cursor) {
            _hasNativeCompositionSnapshot = YES;
            _lastNativeCompositionText = [compositionText copy];
            _lastNativeCompositionCursor = cursor;
            int revision = [self advanceRevision:&_nativeRevision sessionId:_sessionId];
            if (revision == 0) return;
            if (s_onNativeFieldComposition) {
                [self beginNativeCallbackForRevision:revision];
                s_onNativeFieldComposition(_sessionId, revision, _authorityRevision, NCP_Update,
                                           (int)_nativeCompositionRange.location,
                                           (int)_nativeCompositionRange.length,
                                           compositionText.UTF8String ?: "", (int)cursor);
                [self endNativeCallbackForRevision:revision];
            }
        }
        return;
    }

    if (_nativeFieldCompositionActive) {
        NSString* text = [self nativeFieldText];
        NSRange baseRange = _nativeCompositionRange;
        NSInteger replacementLength = (NSInteger)text.length
            - ((NSInteger)_nativeCompositionBaseLength - (NSInteger)baseRange.length);
        if (replacementLength < 0 || baseRange.location + (NSUInteger)replacementLength > text.length)
            [NSException raise:NSInternalInconsistencyException
                        format:@"A native composition committed an invalid replacement range."];
        NSString* replacement = [text substringWithRange:NSMakeRange(baseRange.location,
                                                                      (NSUInteger)replacementLength)];
        [self clearNativeFieldComposition];
        _hasPendingNativeEdit = NO;
        _pendingNativeReplacement = nil;
        _pendingNativeOriginalText = nil;
        _selectionFollowsNativeEdit = YES;
        int sessionId = _sessionId;
        int editRevision = [self advanceRevision:&_nativeRevision sessionId:_sessionId];
        if (editRevision == 0) return;
        if (s_onNativeFieldEdit) {
            [self beginNativeCallbackForRevision:editRevision];
            s_onNativeFieldEdit(_sessionId, editRevision, _authorityRevision,
                                (int)baseRange.location, (int)baseRange.length,
                                replacement.UTF8String ?: "",
                                (int)selection.location, (int)NSMaxRange(selection));
            [self endNativeCallbackForRevision:editRevision];
        }
        if (sessionId != _sessionId || !(_nativeTextField || _nativeTextView)) return;
        int endRevision = [self advanceRevision:&_nativeRevision sessionId:_sessionId];
        if (endRevision == 0) return;
        if (s_onNativeFieldComposition) {
            [self beginNativeCallbackForRevision:endRevision];
            s_onNativeFieldComposition(_sessionId, endRevision, _authorityRevision, NCP_End,
                                       (int)baseRange.location, (int)baseRange.length, "", -1);
            [self endNativeCallbackForRevision:endRevision];
        }
        if (sessionId != _sessionId || !(_nativeTextField || _nativeTextView)) return;
        dispatch_async(dispatch_get_main_queue(), ^{
            if (self->_sessionId == sessionId) self->_selectionFollowsNativeEdit = NO;
        });
        [self applyDeferredNativeFieldState];
        [self applyNativeFieldPolicyAfterCompositionIfPossible];
        return;
    }

    if (!_hasPendingNativeEdit)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field changed without an exact replacement intent."];
    NSRange range = _pendingNativeRange;
    NSString* replacement = _pendingNativeReplacement ?: @"";
    _hasPendingNativeEdit = NO;
    _pendingNativeReplacement = nil;
    _pendingNativeOriginalText = nil;
    _selectionFollowsNativeEdit = YES;
    int sessionId = _sessionId;
    int revision = [self advanceRevision:&_nativeRevision sessionId:_sessionId];
    if (revision == 0) return;
    if (s_onNativeFieldEdit) {
        [self beginNativeCallbackForRevision:revision];
        s_onNativeFieldEdit(_sessionId, revision, _authorityRevision,
                            (int)range.location, (int)range.length,
                            replacement.UTF8String ?: "",
                            (int)selection.location, (int)NSMaxRange(selection));
        [self endNativeCallbackForRevision:revision];
    }
    dispatch_async(dispatch_get_main_queue(), ^{
        if (self->_sessionId == sessionId) self->_selectionFollowsNativeEdit = NO;
    });
}

- (void)clearNativeFieldComposition {
    _nativeFieldCompositionActive = NO;
    _hasNativeCompositionSnapshot = NO;
    _nativeCompositionRange = NSMakeRange(0, 0);
    _nativeCompositionBaseLength = 0;
    _lastNativeCompositionText = nil;
    _nativeCompositionOriginalText = nil;
    _lastNativeCompositionCursor = 0;
}

- (void)applyNativeFieldPolicyAfterCompositionIfPossible {
    if (!_applyNativeFieldPolicyAfterComposition || _nativeFieldCompositionActive) return;
    UIView* field = _nativeTextField ?: _nativeTextView;
    if (!field || [self nativeFieldMarkedRange].location != NSNotFound) return;
    BOOL reloadInputViews = _reloadNativeInputViewsAfterComposition;
    _applyNativeFieldPolicyAfterComposition = NO;
    _reloadNativeInputViewsAfterComposition = NO;
    _nativeFieldAppliedPasswordRules = _nativePasswordRules;
    _nativeFieldAppliedConfiguration = _nativeFieldConfiguration;
    _nativeFieldAppliedConfiguration.passwordRules =
        _nativeFieldAppliedPasswordRules.UTF8String;
    NSString* text = [[self nativeFieldTextForField:field] copy];
    NSRange selection = [self nativeFieldSelectionForField:field];
    _syncingNativeField = YES;
    [self suppressNativeFieldCallbacksThroughTurn];
    [self configureNativeField:field text:text selection:selection
            reloadInputViews:reloadInputViews];
    _syncingNativeField = NO;
}

- (void)completeNativeFieldComposition {
    if (!_nativeFieldCompositionActive) return;
    UIView* field = _nativeTextField ?: _nativeTextView;
    [(id<UITextInput>)field unmarkText];
    if (_nativeFieldCompositionActive && [self nativeFieldMarkedRange].location == NSNotFound)
        [self handleNativeFieldChange];
}

- (void)cancelNativeFieldComposition {
    if (!_nativeFieldCompositionActive) return;
    UIView* field = _nativeTextField ?: _nativeTextView;
    NSString* text = [self nativeFieldText];
    NSRange replacementRange = [self nativeFieldMarkedRange];
    if (replacementRange.location == NSNotFound) {
        NSInteger replacementLength = (NSInteger)text.length
            - ((NSInteger)_nativeCompositionBaseLength - (NSInteger)_nativeCompositionRange.length);
        if (replacementLength < 0
                || _nativeCompositionRange.location + (NSUInteger)replacementLength > text.length)
            [NSException raise:NSInternalInconsistencyException
                        format:@"A native composition exposed an invalid discard range."];
        replacementRange = NSMakeRange(_nativeCompositionRange.location,
                                       (NSUInteger)replacementLength);
    }
    NSMutableString* restored = [text mutableCopy];
    [restored replaceCharactersInRange:replacementRange
                            withString:_nativeCompositionOriginalText ?: @""];
    NSUInteger cursor = _nativeCompositionRange.location
        + (_nativeCompositionOriginalText ?: @"").length;
    _syncingNativeField = YES;
    [self suppressNativeFieldCallbacksThroughTurn];
    [(id<UITextInput>)field unmarkText];
    if (_nativeTextField) _nativeTextField.text = restored;
    else _nativeTextView.text = restored;
    [self applyNativeSelection:NSMakeRange(cursor, 0)];
    _hasPendingNativeEdit = NO;
    _pendingNativeReplacement = nil;
    _pendingNativeOriginalText = nil;
    [self clearNativeFieldComposition];
    _syncingNativeField = NO;
    [self applyDeferredNativeFieldState];
    [self applyNativeFieldPolicyAfterCompositionIfPossible];
}

- (void)suppressNativeFieldCallbacksThroughTurn {
    _suppressingNativeFieldCallbacks = YES;
    int generation = ++_nativeCallbackSuppressionGeneration;
    dispatch_async(dispatch_get_main_queue(), ^{
        if (self->_nativeCallbackSuppressionGeneration == generation)
            self->_suppressingNativeFieldCallbacks = NO;
    });
}

- (void)setNativeFieldStateForSession:(int)sessionId
                 sourceNativeRevision:(int)sourceNativeRevision
                    authorityRevision:(int)authorityRevision
                                 text:(nullable NSString*)text
                       selectionStart:(NSInteger)selectionStart
                         selectionEnd:(NSInteger)selectionEnd {
    if (!(_nativeTextField || _nativeTextView) || sessionId != _sessionId)
        [NSException raise:NSInternalInconsistencyException
                    format:@"The native field reconciliation session is not active."];
    if (authorityRevision < _authorityRevision)
        [NSException raise:NSInternalInconsistencyException
                    format:@"The native field reconciliation authority is stale."];
    if (sourceNativeRevision >= 0 && sourceNativeRevision != _nativeRevision)
        [NSException raise:NSInternalInconsistencyException
                    format:@"The native field reconciliation revision is stale."];
    if (_nativeCallbackInFlightRevision != 0
            && (sourceNativeRevision == -1
                || sourceNativeRevision == _nativeCallbackInFlightRevision))
        _nativeCallbackAcknowledged = YES;
    NSInteger targetLength = (NSInteger)(text ? text.length : [self nativeFieldText].length);
    if (selectionStart < 0 || selectionEnd < selectionStart || selectionEnd > targetLength)
        [NSException raise:NSInvalidArgumentException
                    format:@"Invalid native field selection range."];
    NSRange selection = NSMakeRange((NSUInteger)selectionStart,
                                    (NSUInteger)(selectionEnd - selectionStart));
    if (_nativeFieldCompositionActive || [self nativeFieldMarkedRange].location != NSNotFound) {
        _hasDeferredNativeState = YES;
        _deferredNativeHasText = text != nil;
        _deferredNativeText = [text copy];
        _deferredNativeSelection = selection;
        _deferredSourceNativeRevision = sourceNativeRevision;
        _deferredAuthorityRevision = authorityRevision;
        return;
    }
    BOOL presenterNeedsUpdate = text != nil;
    _authorityRevision = authorityRevision;
    if (!text && NSEqualRanges([self nativeFieldSelection], selection)) return;
    _syncingNativeField = YES;
    [self suppressNativeFieldCallbacksThroughTurn];
    if (text && ![[self nativeFieldText] isEqualToString:text]) {
        if (_nativeTextField) _nativeTextField.text = text;
        else _nativeTextView.text = text;
    }
    [self applyNativeSelection:selection];
    BOOL retainedHierarchy = YES;
    if (presenterNeedsUpdate) {
        UIView* field = _nativeTextField ?: _nativeTextView;
        UIView* fieldSuperview = field.superview;
        UIView* containerSuperview = _nativeFieldContainer.superview;
        UIView* previousAccessory = field.inputAccessoryView;
        [self setNativeField:field presenterCallbackActive:YES];
        _nativePresenter.update(&_nativeFieldContext, field, _nativeFieldContainer);
        [self setNativeField:field presenterCallbackActive:NO];
        retainedHierarchy = [self restoreNativeFieldHierarchy:field
            container:_nativeFieldContainer fieldSuperview:fieldSuperview
            containerSuperview:containerSuperview];
        [self configureNativeField:field text:text selection:selection
                   reloadInputViews:field.inputAccessoryView != previousAccessory];
        _nativePresenterLayoutDirty = YES;
    }
    _syncingNativeField = NO;
    if (!retainedHierarchy)
        [NSException raise:NSInternalInconsistencyException
                    format:@"A native field presenter detached its replica during update."];
    if (text) [self applyPendingRectIfPossible];
}

- (void)applyDeferredNativeFieldState {
    if (!_hasDeferredNativeState || _nativeFieldCompositionActive) return;
    int sourceNativeRevision = _deferredSourceNativeRevision;
    int authorityRevision = _deferredAuthorityRevision;
    NSString* text = _deferredNativeHasText ? _deferredNativeText : nil;
    NSRange selection = _deferredNativeSelection;
    _hasDeferredNativeState = NO;
    _deferredNativeHasText = NO;
    _deferredNativeText = nil;
    [self setNativeFieldStateForSession:_sessionId
                   sourceNativeRevision:sourceNativeRevision
                      authorityRevision:authorityRevision
                                   text:text
                         selectionStart:(NSInteger)selection.location
                           selectionEnd:(NSInteger)NSMaxRange(selection)];
}

- (BOOL)updateNativeFieldWithArgs:(const UniTextShowKeyboardArgs*)args {
    if (!(_nativeTextField || _nativeTextView) || args->sessionId != _sessionId) return NO;
    if (args->authorityRevision < _authorityRevision) return NO;

    NSString* presenterId = args->presenterId
        ? [NSString stringWithUTF8String:args->presenterId]
        : nil;
    NSString* nextPlaceholder = args->placeholder
        ? [NSString stringWithUTF8String:args->placeholder]
        : @"";
    NSString* nextIdentifier = args->accessibilityIdentifier
        ? [NSString stringWithUTF8String:args->accessibilityIdentifier]
        : @"";
    NSString* nextPresenterData = args->presenterData
        ? [NSString stringWithUTF8String:args->presenterData]
        : @"";
    NSString* nextPasswordRules = args->passwordRules
        ? [NSString stringWithUTF8String:args->passwordRules]
        : nil;
    if (presenterId.length == 0 || !nextPlaceholder || !nextIdentifier || !nextPresenterData
            || (args->passwordRules && !nextPasswordRules)) return NO;

    UniTextEnsureSystemPresenter();
    UniTextNativeFieldPresenter* nextPresenter =
        [presenterId isEqualToString:_nativeContextPresenterId]
            ? _nativePresenter
            : s_nativeFieldPresenters[presenterId];
    if (!nextPresenter) return NO;

    BOOL compositionActive = _nativeFieldCompositionActive
        || [self nativeFieldMarkedRange].location != NSNotFound;
    NSString* text = [[self nativeFieldText] copy];
    NSRange selection = [self nativeFieldSelection];
    UIView* previousField = _nativeTextField ?: _nativeTextView;
    BOOL wasFirstResponder = previousField.isFirstResponder;
    int previousAuthorityRevision = _authorityRevision;
    UniTextShowKeyboardArgs previousConfiguration = _nativeFieldConfiguration;
    UniTextShowKeyboardArgs previousAppliedConfiguration =
        _nativeFieldAppliedConfiguration;
    UniTextNativeFieldContext previousContext = _nativeFieldContext;
    BOOL replaceField = ![presenterId isEqualToString:_nativeContextPresenterId]
        || UniTextNativeFieldIsMultiLine(&previousContext) != UniTextArgsAreMultiLine(args);
    if (replaceField && compositionActive) return NO;
    BOOL becameWritable = previousContext.readOnly != 0 && args->readOnly == 0;
    BOOL focusWritableField = becameWritable && !_inputProducerFrozen && !_quiescingInput;
    NSString* previousPresenterId = _nativeContextPresenterId;
    NSString* previousPlaceholder = _nativeContextPlaceholder;
    NSString* previousIdentifier = _nativeContextIdentifier;
    NSString* previousPresenterData = _nativeContextPresenterData;
    NSString* previousPasswordRules = _nativePasswordRules;
    NSString* previousAppliedPasswordRules = _nativeFieldAppliedPasswordRules;
    NSString* previousAuthoritativeText = _nativeFieldAuthoritativeText;
    NSRange previousAuthoritativeSelection = _nativeFieldAuthoritativeSelection;
    UITextInputPasswordRules* previousPasswordRulesObject = _nativePasswordRulesObject;
    BOOL previousPresenterLayoutDirty = _nativePresenterLayoutDirty;
    BOOL previousHasPresenterLayoutSnapshot = _hasNativePresenterLayoutSnapshot;
    CGRect previousPresenterBounds = _lastNativePresenterBounds;
    CGRect previousPresenterSuggestedFrame = _lastNativePresenterSuggestedFrame;
    UIEdgeInsets previousPresenterSafeAreaInsets = _lastNativePresenterSafeAreaInsets;
    CGFloat previousPresenterKeyboardTop = _lastNativePresenterKeyboardTop;
    BOOL previousApplyPolicyAfterComposition = _applyNativeFieldPolicyAfterComposition;
    BOOL previousReloadInputViewsAfterComposition =
        _reloadNativeInputViewsAfterComposition;
    BOOL inputViewsChanged = UniTextInputViewsDiffer(
        &previousConfiguration, args, previousPasswordRules, nextPasswordRules);
    BOOL policyChanged = inputViewsChanged
        || previousConfiguration.readOnly != args->readOnly;
    BOOL reloadInputViews = inputViewsChanged && !compositionActive;
    if (inputViewsChanged) _nativePasswordRulesObject = nil;

    _authorityRevision = MAX(args->authorityRevision, _authorityRevision);
    _nativePasswordRules = [nextPasswordRules copy];
    _nativeFieldConfiguration = *args;
    _nativeFieldConfiguration.passwordRules = _nativePasswordRules.UTF8String;
    _nativeFieldConfiguration.initialText = NULL;
    _nativeFieldConfiguration.accessibilityIdentifier = NULL;
    _nativeFieldConfiguration.placeholder = NULL;
    _nativeFieldConfiguration.presenterId = NULL;
    _nativeFieldConfiguration.presenterData = NULL;
    if (compositionActive) {
        if (policyChanged) _applyNativeFieldPolicyAfterComposition = YES;
        if (inputViewsChanged) _reloadNativeInputViewsAfterComposition = YES;
    } else {
        _nativeFieldAppliedPasswordRules = _nativePasswordRules;
        _nativeFieldAppliedConfiguration = _nativeFieldConfiguration;
        _nativeFieldAppliedConfiguration.passwordRules =
            _nativeFieldAppliedPasswordRules.UTF8String;
        _applyNativeFieldPolicyAfterComposition = NO;
        _reloadNativeInputViewsAfterComposition = NO;
    }
    _nativeContextPresenterId = [presenterId copy];
    _nativeContextPlaceholder = [nextPlaceholder copy];
    _nativeContextIdentifier = [nextIdentifier copy];
    _nativeContextPresenterData = [nextPresenterData copy];
    _nativeFieldContext.sessionId = _sessionId;
    _nativeFieldContext.wraps = args->wraps;
    _nativeFieldContext.acceptsNewlines = args->acceptsNewlines;
    _nativeFieldContext.returnKey = (UniTextNativeFieldReturnKey)args->returnKey;
    _nativeFieldContext.secureTextEntry = args->secureTextEntry;
    _nativeFieldContext.readOnly = args->readOnly;
    _nativeFieldContext.copyAllowed = args->copyAllowed;
    _nativeFieldContext.presenterId = _nativeContextPresenterId.UTF8String;
    _nativeFieldContext.placeholder = _nativeContextPlaceholder.UTF8String;
    _nativeFieldContext.identifier = _nativeContextIdentifier.UTF8String;
    _nativeFieldContext.presenterData = _nativeContextPresenterData.UTF8String;
    UniTextNativeFieldContext nextContext = _nativeFieldContext;

    if (!replaceField) {
        UIView* fieldSuperview = previousField.superview;
        UIView* containerSuperview = _nativeFieldContainer.superview;
        _syncingNativeField = YES;
        [self suppressNativeFieldCallbacksThroughTurn];
        [self configureNativeField:previousField text:text selection:selection
                   reloadInputViews:NO];
        [self setNativeField:previousField presenterCallbackActive:YES];
        nextPresenter.update(&_nativeFieldContext, previousField, _nativeFieldContainer);
        [self setNativeField:previousField presenterCallbackActive:NO];
        BOOL retainedHierarchy = [self restoreNativeFieldHierarchy:previousField
            container:_nativeFieldContainer fieldSuperview:fieldSuperview
            containerSuperview:containerSuperview];
        [self configureNativeField:previousField text:text selection:selection
                reloadInputViews:reloadInputViews];
        _nativePresenterLayoutDirty = YES;
        _syncingNativeField = NO;
        if (retainedHierarchy) [self applyPendingRectIfPossible];
        if (!retainedHierarchy) {
            _authorityRevision = previousAuthorityRevision;
            _nativeFieldConfiguration = previousConfiguration;
            _nativeFieldAppliedConfiguration = previousAppliedConfiguration;
            _nativePasswordRules = previousPasswordRules;
            _nativeFieldAppliedPasswordRules = previousAppliedPasswordRules;
            _nativeFieldConfiguration.passwordRules = _nativePasswordRules.UTF8String;
            _nativeFieldAppliedConfiguration.passwordRules =
                _nativeFieldAppliedPasswordRules.UTF8String;
            _nativeFieldAuthoritativeText = previousAuthoritativeText;
            _nativeFieldAuthoritativeSelection = previousAuthoritativeSelection;
            _nativeContextPresenterId = previousPresenterId;
            _nativeContextPlaceholder = previousPlaceholder;
            _nativeContextIdentifier = previousIdentifier;
            _nativeContextPresenterData = previousPresenterData;
            _nativeFieldContext = previousContext;
            _nativePasswordRulesObject = previousPasswordRulesObject;
            _applyNativeFieldPolicyAfterComposition = previousApplyPolicyAfterComposition;
            _reloadNativeInputViewsAfterComposition =
                previousReloadInputViewsAfterComposition;
            _nativePresenterLayoutDirty = previousPresenterLayoutDirty;
            _hasNativePresenterLayoutSnapshot = previousHasPresenterLayoutSnapshot;
            _lastNativePresenterBounds = previousPresenterBounds;
            _lastNativePresenterSuggestedFrame = previousPresenterSuggestedFrame;
            _lastNativePresenterSafeAreaInsets = previousPresenterSafeAreaInsets;
            _lastNativePresenterKeyboardTop = previousPresenterKeyboardTop;
            _syncingNativeField = YES;
            [self configureNativeField:previousField text:text selection:selection
                       reloadInputViews:reloadInputViews];
            _syncingNativeField = NO;
            NSLog(@"UniText native field presenter update failed: "
                  @"a presenter detached its replica during update.");
            return NO;
        }
        if (focusWritableField) [previousField becomeFirstResponder];
        return YES;
    }

    UIView* nextField = nil;
    UIView* nextContainer = nil;
    _replacingNativePresenter = YES;
    if (![self createNativeFieldWithPresenter:nextPresenter text:text selection:selection
                                        field:&nextField container:&nextContainer]) {
        _authorityRevision = previousAuthorityRevision;
        _nativeFieldConfiguration = previousConfiguration;
        _nativeFieldAppliedConfiguration = previousAppliedConfiguration;
        _nativePasswordRules = previousPasswordRules;
        _nativeFieldAppliedPasswordRules = previousAppliedPasswordRules;
        _nativeFieldAppliedConfiguration.passwordRules =
            _nativeFieldAppliedPasswordRules.UTF8String;
        _nativeFieldConfiguration.passwordRules = _nativePasswordRules.UTF8String;
        _nativeFieldAuthoritativeText = previousAuthoritativeText;
        _nativeFieldAuthoritativeSelection = previousAuthoritativeSelection;
        _nativeContextPresenterId = previousPresenterId;
        _nativeContextPlaceholder = previousPlaceholder;
        _nativeContextIdentifier = previousIdentifier;
        _nativeContextPresenterData = previousPresenterData;
        _nativeFieldContext = previousContext;
        _nativePasswordRulesObject = previousPasswordRulesObject;
        _applyNativeFieldPolicyAfterComposition = previousApplyPolicyAfterComposition;
        _reloadNativeInputViewsAfterComposition =
            previousReloadInputViewsAfterComposition;
        _nativePresenterLayoutDirty = previousPresenterLayoutDirty;
        _hasNativePresenterLayoutSnapshot = previousHasPresenterLayoutSnapshot;
        _lastNativePresenterBounds = previousPresenterBounds;
        _lastNativePresenterSuggestedFrame = previousPresenterSuggestedFrame;
        _lastNativePresenterSafeAreaInsets = previousPresenterSafeAreaInsets;
        _lastNativePresenterKeyboardTop = previousPresenterKeyboardTop;
        _replacingNativePresenter = NO;
        return NO;
    }

    UIView* previousContainer = _nativeFieldContainer;
    UniTextNativeFieldPresenter* previousPresenter = _nativePresenter;
    if (_nativeTextField) {
        [[NSNotificationCenter defaultCenter] removeObserver:self
                                                        name:UITextFieldTextDidChangeNotification
                                                      object:_nativeTextField];
        _nativeTextField.delegate = nil;
        _nativeTextField.uniTextActionHandler = nil;
    } else {
        _nativeTextView.delegate = nil;
        _nativeTextView.uniTextActionHandler = nil;
    }
    [self activateNativeField:nextField container:nextContainer presenter:nextPresenter
                     selection:selection focus:wasFirstResponder || focusWritableField];
    [previousField resignFirstResponder];
    _nativeFieldContext = previousContext;
    [self destroyNativeField:previousField container:previousContainer
                   presenter:previousPresenter context:&_nativeFieldContext];
    _nativeFieldContext = nextContext;
    _replacingNativePresenter = NO;
    return YES;
}

- (BOOL)emitNativeAction:(const char*)action modifiers:(int)modifiers {
    if (!action || action[0] == '\0' || !(_nativeTextField || _nativeTextView)
            || _hidingKeyboard || _replacingNativePresenter
            || _syncingNativeField || _suppressingNativeFieldCallbacks
            || _inputProducerFrozen || _quiescingInput) return NO;
    int sessionId = _sessionId;
    [self completeNativeFieldComposition];
    if (sessionId != _sessionId || !(_nativeTextField || _nativeTextView)
            || _hidingKeyboard || _replacingNativePresenter
            || _inputProducerFrozen || _quiescingInput) return NO;
    int revision = [self advanceRevision:&_nativeRevision sessionId:_sessionId];
    if (revision == 0) return NO;
    if (s_onNativeFieldAction) {
        [self beginNativeCallbackForRevision:revision];
        s_onNativeFieldAction(_sessionId, revision, _authorityRevision, action, modifiers);
        [self endNativeCallbackForRevision:revision];
    }
    return YES;
}

- (BOOL)performNativeFieldActionForSession:(int)sessionId action:(NSString*)action {
    if (sessionId != _sessionId || !(_nativeTextField || _nativeTextView)
            || action.length == 0) return NO;
    return [self emitNativeAction:action.UTF8String modifiers:0];
}

- (void)closeNativeFieldForSession:(int)sessionId {
    if (sessionId != _sessionId || !(_nativeTextField || _nativeTextView))
        [NSException raise:NSInternalInconsistencyException
                    format:@"The native field close session is not active."];
    [self hideKeyboard];
}

- (void)focusNativeFieldForSession:(int)sessionId {
    if (sessionId != _sessionId || !(_nativeTextField || _nativeTextView))
        [NSException raise:NSInternalInconsistencyException
                    format:@"The native field focus session is not active."];
    _syncingNativeField = YES;
    [self suppressNativeFieldCallbacksThroughTurn];
    if (_nativeFieldContext.readOnly == 0)
        [_nativeTextField ?: _nativeTextView becomeFirstResponder];
    _syncingNativeField = NO;
    [self resumeInputProducer];
}

- (void)resumeInputProducer {
    _inputProducerFrozen = NO;
    _transparentView.inputProducerFrozen = NO;
}

- (void)quiesceInputForSession:(int)sessionId
                   disposition:(int)disposition
                     requestId:(int)requestId {
    BOOL nativeField = (_nativeTextField || _nativeTextView) && sessionId == _sessionId;
    BOOL transparent = _transparentView && sessionId == _transparentSessionId;
    if (!nativeField && !transparent)
        [NSException raise:NSInternalInconsistencyException
                    format:@"The native input quiescence session is not active."];

    if (_nativeCallbackInFlightRevision != 0) {
        _inputProducerFrozen = YES;
        _transparentView.inputProducerFrozen = YES;
        [self enqueueQuiesceForSession:sessionId
                           disposition:disposition
                             requestId:requestId];
        return;
    }
    if (_quiescingInput) {
        _inputProducerFrozen = YES;
        _transparentView.inputProducerFrozen = YES;
        [self enqueueQuiesceForSession:sessionId
                           disposition:disposition
                             requestId:requestId];
        [self schedulePendingQuiesces];
        return;
    }

    _quiescingInput = YES;
    _inputProducerFrozen = NO;
    _transparentView.inputProducerFrozen = NO;
    if (disposition == 1) {
        if (nativeField) [self completeNativeFieldComposition];
        else [_transparentView completeComposition];
    } else if (disposition == 2) {
        if (nativeField) [self cancelNativeFieldComposition];
        else [_transparentView cancelComposition];
    }

    nativeField = (_nativeTextField || _nativeTextView) && sessionId == _sessionId;
    transparent = _transparentView && sessionId == _transparentSessionId;
    if (!nativeField && !transparent) {
        _quiescingInput = NO;
        return;
    }

    _inputProducerFrozen = YES;
    _transparentView.inputProducerFrozen = YES;
    _quiescingInput = NO;
    int revision = nativeField
        ? [self advanceRevision:&_nativeRevision sessionId:sessionId]
        : [self advanceRevision:&_transparentRevision sessionId:sessionId];
    if (revision == 0) return;
    int authorityRevision = nativeField ? _authorityRevision : 1;
    if (s_onNativeInputQuiesced)
        s_onNativeInputQuiesced(sessionId, revision, authorityRevision, requestId);
}

- (BOOL)abortInputForSession:(int)sessionId {
    BOOL nativeField = (_nativeTextField || _nativeTextView) && sessionId == _sessionId;
    BOOL transparent = _transparentView && sessionId == _transparentSessionId;
    if (!nativeField && !transparent) return NO;
    _inputProducerFrozen = YES;
    _transparentView.terminallyAborted = YES;
    _transparentView.inputProducerFrozen = YES;
    _nativeCallbackAcknowledged = NO;
    [_pendingQuiesces removeAllObjects];
    _pendingQuiesceDrainScheduled = NO;
    [self hideKeyboard];
    return YES;
}

- (UIKeyboardType)mapKeyboardType:(int)value {
    switch (value) {
        case 1:  return UIKeyboardTypeASCIICapable;
        case 2:  return UIKeyboardTypeNumbersAndPunctuation;
        case 3:  return UIKeyboardTypeURL;
        case 4:  return UIKeyboardTypeNumberPad;
        case 5:  return UIKeyboardTypePhonePad;
        case 6:  return UIKeyboardTypeNamePhonePad;
        case 7:  return UIKeyboardTypeEmailAddress;
        case 8:  return UIKeyboardTypeDecimalPad;
        case 9:  return UIKeyboardTypeTwitter;
        case 10: return UIKeyboardTypeWebSearch;
        case 11: return UIKeyboardTypeASCIICapableNumberPad;
        default: return UIKeyboardTypeDefault;
    }
}

- (UIReturnKeyType)mapReturnKeyType:(int)value {
    switch (value) {
        case 1:  return UIReturnKeyGo;
        case 2:  return UIReturnKeyGoogle;
        case 3:  return UIReturnKeyJoin;
        case 4:  return UIReturnKeyNext;
        case 5:  return UIReturnKeyRoute;
        case 6:  return UIReturnKeySearch;
        case 7:  return UIReturnKeySend;
        case 8:  return UIReturnKeyYahoo;
        case 9:  return UIReturnKeyDone;
        case 10: return UIReturnKeyEmergencyCall;
        case 11: return UIReturnKeyContinue;
        default: return UIReturnKeyDefault;
    }
}

- (UITextAutocapitalizationType)mapAutoCapitalization:(int)value {
    switch (value) {
        case 1:  return UITextAutocapitalizationTypeNone;
        case 2:  return UITextAutocapitalizationTypeWords;
        case 3:  return UITextAutocapitalizationTypeSentences;
        case 4:  return UITextAutocapitalizationTypeAllCharacters;
        default: return UITextAutocapitalizationTypeSentences; // iOS default is sentences
    }
}

- (UITextAutocorrectionType)mapAutoCorrection:(int)value {
    switch (value) {
        case 1:  return UITextAutocorrectionTypeYes;  // Enabled
        case 2:  return UITextAutocorrectionTypeNo;   // Disabled
        default: return UITextAutocorrectionTypeDefault;
    }
}

- (UITextSpellCheckingType)mapSpellChecking:(int)value {
    switch (value) {
        case 1:  return UITextSpellCheckingTypeYes;   // Enabled
        case 2:  return UITextSpellCheckingTypeNo;    // Disabled
        default: return UITextSpellCheckingTypeDefault;
    }
}

- (UITextSmartQuotesType)mapSmartQuotes:(int)value {
    switch (value) {
        case 1:  return UITextSmartQuotesTypeYes;     // Enabled
        case 2:  return UITextSmartQuotesTypeNo;      // Disabled
        default: return UITextSmartQuotesTypeDefault;
    }
}

- (UITextSmartDashesType)mapSmartDashes:(int)value {
    switch (value) {
        case 1:  return UITextSmartDashesTypeYes;     // Enabled
        case 2:  return UITextSmartDashesTypeNo;      // Disabled
        default: return UITextSmartDashesTypeDefault;
    }
}

- (UITextSmartInsertDeleteType)mapSmartInsertDelete:(int)value {
    switch (value) {
        case 1:  return UITextSmartInsertDeleteTypeYes;     // Enabled
        case 2:  return UITextSmartInsertDeleteTypeNo;      // Disabled
        default: return UITextSmartInsertDeleteTypeDefault;
    }
}

- (UITextContentType)mapAutofillHint:(int)value {
    switch (value) {
        case 1:  return UITextContentTypeUsername;
        case 2:  return UITextContentTypePassword;
        case 3:  return UITextContentTypeNewPassword;
        case 4:  return UITextContentTypeOneTimeCode;
        case 5:  return UITextContentTypeEmailAddress;
        case 6:  return UITextContentTypeTelephoneNumber;
        case 7:  return UITextContentTypeName;
        case 8:  return UITextContentTypeGivenName;
        case 9:  return UITextContentTypeFamilyName;
        case 10: return UITextContentTypeFullStreetAddress;
        case 11: return UITextContentTypePostalCode;
        case 12: return UITextContentTypeCreditCardNumber;
        case 13: return UITextContentTypeURL;
        default: return nil; // None — no autofill
    }
}

@end

static int UniTextBeginTransparentCallback(void) {
    return [[UniTextNativeInputController shared] beginTransparentCallback];
}

static void UniTextEndTransparentCallback(int revision) {
    [[UniTextNativeInputController shared] endTransparentCallback:revision];
}

extern "C" {

void UniTextNativeInput_Init(
    TextInputCallback              onTextInput,
    TextReplacementCallback        onTextReplacement,
    KeyDownCallback                onKeyDown,
    CompositionCallback            onComposition,
    CompositionEndedCallback       onCompositionEnded,
    KeyboardEventCallback          onKeyboardEvent,
    NativeFieldEditCallback        onNativeFieldEdit,
    NativeFieldCompositionCallback onNativeFieldComposition,
    NativeFieldSelectionCallback   onNativeFieldSelection,
    NativeFieldActionCallback      onNativeFieldAction,
    NativeInputQuiescedCallback    onNativeInputQuiesced,
    NativeInputFaultCallback       onNativeInputFault,
    FloatingCursorPointCallback    onFloatingCursorPoint
) {
    s_onTextInput           = onTextInput;
    s_onTextReplacement     = onTextReplacement;
    s_onKeyDown             = onKeyDown;
    s_onComposition         = onComposition;
    s_onCompositionEnded    = onCompositionEnded;
    s_onKeyboardEvent       = onKeyboardEvent;
    s_onNativeFieldEdit      = onNativeFieldEdit;
    s_onNativeFieldComposition = onNativeFieldComposition;
    s_onNativeFieldSelection = onNativeFieldSelection;
    s_onNativeFieldAction    = onNativeFieldAction;
    s_onNativeInputQuiesced  = onNativeInputQuiesced;
    s_onNativeInputFault     = onNativeInputFault;
    s_onFloatingCursorPoint = onFloatingCursorPoint;

    [[UniTextNativeInputController shared] initializeWithCallbacks];
}

void UniTextNativeInput_Shutdown(void) {
    [[UniTextNativeInputController shared] shutdown];

    s_onTextInput           = NULL;
    s_onTextReplacement     = NULL;
    s_onKeyDown             = NULL;
    s_onComposition         = NULL;
    s_onCompositionEnded    = NULL;
    s_onKeyboardEvent       = NULL;
    s_onNativeFieldEdit      = NULL;
    s_onNativeFieldComposition = NULL;
    s_onNativeFieldSelection = NULL;
    s_onNativeFieldAction    = NULL;
    s_onNativeInputQuiesced  = NULL;
    s_onNativeInputFault     = NULL;
    s_onFloatingCursorPoint = NULL;
    s_setSelectionCharRange = NULL;
    s_getCharRangeRect      = NULL;
    s_closestCharAtPoint    = NULL;
    s_writingDirection      = NULL;
}

void UniTextNativeInput_RegisterSelectionCallback(
    SetSelectionCharRangeCallback setSelectionCharRange)
{
    s_setSelectionCharRange = setSelectionCharRange;
}

void UniTextNativeInput_RegisterGeometryQueries(
    GetCharRangeRectCallback getCharRangeRect,
    ClosestCharAtPointCallback closestCharAtPoint,
    WritingDirectionCallback writingDirection)
{
    s_getCharRangeRect   = getCharRangeRect;
    s_closestCharAtPoint = closestCharAtPoint;
    s_writingDirection   = writingDirection;
}

void UniTextNativeInput_SetCursorPos(float x, float y, float w, float h) {
    [[UniTextNativeInputController shared] setCursorPosX:x y:y w:w h:h];
}

void UniTextNativeInput_SetTextContext(int version, const unsigned short* text, int textLength,
                                       int windowStart, int selectionStart, int selectionEnd,
                                       int forceRestart) {
    NSString* value = nil;
    if (textLength >= 0) {
        value = textLength == 0
            ? @""
            : [NSString stringWithCharacters:(const unichar*)text length:(NSUInteger)textLength];
    }
    [[UniTextNativeInputController shared] setTextContext:value
                                                  version:version
                                               windowStart:windowStart
                                            selectionStart:selectionStart
                                              selectionEnd:selectionEnd
                                               forceRestart:forceRestart != 0];
}

int UniTextNativeInput_ShowKeyboard(const UniTextShowKeyboardArgs* args) {
    if (args == NULL) return 0;
    __block BOOL shown = NO;
    dispatch_block_t block = ^{
        shown = [[UniTextNativeInputController shared] showKeyboardWithArgs:args];
    };
    if ([NSThread isMainThread]) block();
    else dispatch_sync(dispatch_get_main_queue(), block);
    return shown ? 1 : 0;
}

void UniTextNativeInput_HideKeyboard(void) {
    [[UniTextNativeInputController shared] hideKeyboard];
}

void UniTextNativeInput_SetInputFieldRect(float unityX, float unityY, float unityW, float unityH) {
    [[UniTextNativeInputController shared] setInputFieldRectUnityX:unityX unityY:unityY
                                                              width:unityW height:unityH];
}

void UniTextNativeInput_SetNativeFieldState(int sessionId, int sourceNativeRevision,
                                            int authorityRevision,
                                            const unsigned short* text, int textLength,
                                            int selectionStart, int selectionEnd) {
    if (sessionId <= 0 || sourceNativeRevision < -1 || authorityRevision < 1
            || selectionStart < 0 || selectionEnd < selectionStart
            || textLength < -1 || (textLength > 0 && text == NULL)
            || (textLength >= 0 && selectionEnd > textLength))
        [NSException raise:NSInvalidArgumentException
                    format:@"Invalid native field reconciliation state."];
    NSString* value = nil;
    if (textLength >= 0) {
        value = textLength == 0
            ? @""
            : [NSString stringWithCharacters:(const unichar*)text length:(NSUInteger)textLength];
    }
    dispatch_block_t block = ^{
        [[UniTextNativeInputController shared]
            setNativeFieldStateForSession:sessionId
                     sourceNativeRevision:sourceNativeRevision
                        authorityRevision:authorityRevision
                                     text:value
                           selectionStart:selectionStart
                             selectionEnd:selectionEnd];
    };
    if ([NSThread isMainThread]) block();
    else dispatch_sync(dispatch_get_main_queue(), block);
}

int UniTextNativeInput_UpdateNativeField(const UniTextShowKeyboardArgs* args) {
    if (args == NULL) return 0;
    __block BOOL updated = NO;
    dispatch_block_t block = ^{
        updated = [[UniTextNativeInputController shared]
            updateNativeFieldWithArgs:args];
    };
    if ([NSThread isMainThread]) block();
    else dispatch_sync(dispatch_get_main_queue(), block);
    return updated ? 1 : 0;
}

void UniTextNativeInput_CloseNativeField(int sessionId) {
    dispatch_block_t block = ^{
        [[UniTextNativeInputController shared] closeNativeFieldForSession:sessionId];
    };
    if ([NSThread isMainThread]) block();
    else dispatch_sync(dispatch_get_main_queue(), block);
}

void UniTextNativeInput_FocusNativeField(int sessionId) {
    dispatch_block_t block = ^{
        [[UniTextNativeInputController shared] focusNativeFieldForSession:sessionId];
    };
    if ([NSThread isMainThread]) block();
    else dispatch_sync(dispatch_get_main_queue(), block);
}

void UniTextNativeInput_QuiesceInput(int sessionId, int disposition, int requestId) {
    if (sessionId <= 0 || disposition < 0 || disposition > 2 || requestId <= 0)
        [NSException raise:NSInvalidArgumentException
                    format:@"Invalid native input quiesce request."];
    dispatch_block_t block = ^{
        [[UniTextNativeInputController shared]
            quiesceInputForSession:sessionId disposition:disposition requestId:requestId];
    };
    if ([NSThread isMainThread]) block();
    else dispatch_sync(dispatch_get_main_queue(), block);
}

int UniTextNativeInput_AbortInput(int sessionId) {
    if (sessionId <= 0)
        [NSException raise:NSInvalidArgumentException
                    format:@"Invalid native input abort request."];
    __block BOOL aborted = NO;
    dispatch_block_t block = ^{
        aborted = [[UniTextNativeInputController shared] abortInputForSession:sessionId];
    };
    if ([NSThread isMainThread]) block();
    else dispatch_sync(dispatch_get_main_queue(), block);
    return aborted ? 1 : 0;
}

int UniTextNativeInput_RegisterNativeFieldPresenter(
    const char* presenterId,
    UniTextNativeFieldPresenterCreateCallback create,
    UniTextNativeFieldPresenterUpdateCallback update,
    UniTextNativeFieldPresenterLayoutCallback layout,
    UniTextNativeFieldPresenterDestroyCallback destroy) {
    NSString* identifier = presenterId ? [NSString stringWithUTF8String:presenterId] : nil;
    identifier = [identifier stringByTrimmingCharactersInSet:
        [NSCharacterSet whitespaceAndNewlineCharacterSet]];
    if (identifier.length == 0 || [identifier isEqualToString:@"system"]
            || !create || !update || !layout || !destroy) return 0;
    __block BOOL registered = NO;
    dispatch_block_t block = ^{
        UniTextEnsureSystemPresenter();
        registered = UniTextRegisterNativeFieldPresenter(identifier, create, update, layout, destroy);
    };
    if ([NSThread isMainThread]) block();
    else dispatch_sync(dispatch_get_main_queue(), block);
    return registered ? 1 : 0;
}

int UniTextNativeInput_UnregisterNativeFieldPresenter(const char* presenterId) {
    NSString* identifier = presenterId ? [NSString stringWithUTF8String:presenterId] : nil;
    identifier = [identifier stringByTrimmingCharactersInSet:
        [NSCharacterSet whitespaceAndNewlineCharacterSet]];
    if (identifier.length == 0 || [identifier isEqualToString:@"system"]) return 0;
    __block BOOL removed = NO;
    dispatch_block_t block = ^{
        UniTextEnsureSystemPresenter();
        removed = ![identifier isEqualToString:s_activeNativeFieldPresenterId]
            && s_nativeFieldPresenters[identifier] != nil;
        if (removed) [s_nativeFieldPresenters removeObjectForKey:identifier];
    };
    if ([NSThread isMainThread]) block();
    else dispatch_sync(dispatch_get_main_queue(), block);
    return removed ? 1 : 0;
}

int UniTextNativeInput_PerformNativeFieldAction(int sessionId, const char* action) {
    NSString* actionValue = action ? [NSString stringWithUTF8String:action] : nil;
    if (!UniTextNativeFieldActionIsSupported(actionValue)) return 0;
    __block BOOL performed = NO;
    dispatch_block_t block = ^{
        performed = [[UniTextNativeInputController shared]
            performNativeFieldActionForSession:sessionId action:actionValue];
    };
    if ([NSThread isMainThread]) block();
    else dispatch_sync(dispatch_get_main_queue(), block);
    return performed ? 1 : 0;
}

} // extern "C"
