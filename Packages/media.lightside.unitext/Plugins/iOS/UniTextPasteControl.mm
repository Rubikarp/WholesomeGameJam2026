#import <UIKit/UIKit.h>
#import <Foundation/Foundation.h>
#import <string.h>
#import "UniTextAppleCommon.h"

// UniText UIPasteControl wrapper (iOS 16+). Bypasses the system paste-permission
// prompt: data arrives via NSItemProvider in -pasteItemProviders:, cached, then
// surfaced to C# via UnitySendMessage. iOS <16 uses the C# uGUI fallback.

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

// Per-tap payload cache. One slot is enough — taps serialize on the main thread.
static char* s_capturedPlain   = NULL;
static char* s_capturedHtml    = NULL;
static char* s_capturedUniText = NULL;

static void StoreCapturedSlot(char** slot, NSString* value) {
    if (*slot) { free(*slot); *slot = NULL; }
    if (value && value.length > 0) {
        const char* utf8 = [value UTF8String];
        if (utf8) *slot = strdup(utf8);
    }
}

static void ClearCaptured(void) {
    if (s_capturedPlain)   { free(s_capturedPlain);   s_capturedPlain   = NULL; }
    if (s_capturedHtml)    { free(s_capturedHtml);    s_capturedHtml    = NULL; }
    if (s_capturedUniText) { free(s_capturedUniText); s_capturedUniText = NULL; }
}

API_AVAILABLE(ios(16.0))
@interface UniTextPasteResponder : UIView
@property (nonatomic, copy) NSString* gameObjectName;
@end

@implementation UniTextPasteResponder

- (BOOL)canBecomeFirstResponder { return YES; }

- (BOOL)canPasteItemProviders:(NSArray<NSItemProvider*>*)itemProviders {
    return itemProviders.count > 0;
}

- (void)pasteItemProviders:(NSArray<NSItemProvider*>*)itemProviders {
    if (itemProviders.count == 0) return;
    NSItemProvider* item = itemProviders.firstObject;
    if (item == nil) return;

    NSString* __block plain   = nil;
    NSString* __block html    = nil;
    NSString* __block uniText = nil;

    dispatch_group_t group = dispatch_group_create();

    if ([item hasItemConformingToTypeIdentifier:@"public.utf8-plain-text"]) {
        dispatch_group_enter(group);
        [item loadDataRepresentationForTypeIdentifier:@"public.utf8-plain-text"
                                    completionHandler:^(NSData* data, NSError* error) {
            if (data) plain = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
            dispatch_group_leave(group);
        }];
    }

    if ([item hasItemConformingToTypeIdentifier:@"public.html"]) {
        dispatch_group_enter(group);
        [item loadDataRepresentationForTypeIdentifier:@"public.html"
                                    completionHandler:^(NSData* data, NSError* error) {
            if (data) html = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
            dispatch_group_leave(group);
        }];
    }

    if ([item hasItemConformingToTypeIdentifier:@"com.lightside.unitext"]) {
        dispatch_group_enter(group);
        [item loadDataRepresentationForTypeIdentifier:@"com.lightside.unitext"
                                    completionHandler:^(NSData* data, NSError* error) {
            if (data) uniText = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
            dispatch_group_leave(group);
        }];
    }

    NSString* targetGameObject = self.gameObjectName;
    dispatch_group_notify(group, dispatch_get_main_queue(), ^{
        ClearCaptured();
        StoreCapturedSlot(&s_capturedPlain,   plain);
        StoreCapturedSlot(&s_capturedHtml,    html);
        StoreCapturedSlot(&s_capturedUniText, uniText);

        if (targetGameObject) {
            UnitySendMessage([targetGameObject UTF8String], "OnNativePasteReady", "");
        }
    });
}

@end

@interface UniTextPasteHandle : NSObject
@property (nonatomic, strong) UniTextPasteResponder* responder API_AVAILABLE(ios(16.0));
@property (nonatomic, strong) UIControl* pasteControl API_AVAILABLE(ios(16.0));
@property (nonatomic, assign) BOOL attached;
@end
@implementation UniTextPasteHandle
@end

static NSMutableDictionary<NSNumber*, UniTextPasteHandle*>* s_handles = nil;
static int s_nextHandleId = 1;

static UniTextPasteHandle* HandleForId(int handleId) {
    return s_handles ? s_handles[@(handleId)] : nil;
}

extern "C" {

    int UniTextPasteControl_IsSupported(void) {
        if (@available(iOS 16.0, *)) return 1;
        return 0;
    }

    int UniTextPasteControl_Create(const char* gameObjectName, int displayMode, int cornerStyle) {
        if (gameObjectName == NULL) return 0;
        if (@available(iOS 16.0, *)) {
            __block int assignedId = 0;
            void (^createBlock)(void) = ^{
                @autoreleasepool {
                    if (!s_handles) s_handles = [NSMutableDictionary new];

                    UniTextPasteResponder* responder = [[UniTextPasteResponder alloc] initWithFrame:CGRectZero];
                    responder.backgroundColor = [UIColor clearColor];
                    responder.userInteractionEnabled = YES;
                    responder.hidden = YES;
                    responder.gameObjectName = [NSString stringWithUTF8String:gameObjectName];

                    NSMutableArray* types = [NSMutableArray new];
                    [types addObject:(NSString*)@"public.utf8-plain-text"];
                    [types addObject:(NSString*)@"public.html"];
                    [types addObject:(NSString*)@"com.lightside.unitext"];
                    responder.pasteConfiguration = [[UIPasteConfiguration alloc]
                                                    initWithAcceptableTypeIdentifiers:types];

                    UIPasteControlConfiguration* cfg = [[UIPasteControlConfiguration alloc] init];
                    switch (displayMode) {
                        case 1: cfg.displayMode = UIPasteControlDisplayModeIconOnly; break;
                        case 2: cfg.displayMode = UIPasteControlDisplayModeLabelOnly; break;
                        default: cfg.displayMode = UIPasteControlDisplayModeIconAndLabel; break;
                    }
                    switch (cornerStyle) {
                        case 1: cfg.cornerStyle = UIButtonConfigurationCornerStyleDynamic; break;
                        case 2: cfg.cornerStyle = UIButtonConfigurationCornerStyleFixed; break;
                        case 3: cfg.cornerStyle = UIButtonConfigurationCornerStyleSmall; break;
                        default: cfg.cornerStyle = UIButtonConfigurationCornerStyleCapsule; break;
                    }
                    UIPasteControl* control = [[UIPasteControl alloc] initWithConfiguration:cfg];
                    control.target = responder;
                    control.translatesAutoresizingMaskIntoConstraints = YES;
                    control.frame = responder.bounds;
                    control.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
                    [responder addSubview:control];

                    UIView* host = UniTextHostView();
                    if (host) [host addSubview:responder];

                    UniTextPasteHandle* handle = [UniTextPasteHandle new];
                    handle.responder = responder;
                    handle.pasteControl = control;
                    handle.attached = (host != nil);

                    assignedId = s_nextHandleId++;
                    s_handles[@(assignedId)] = handle;
                }
            };
            if ([NSThread isMainThread]) createBlock();
            else dispatch_sync(dispatch_get_main_queue(), createBlock);
            return assignedId;
        }
        return 0;
    }

    void UniTextPasteControl_SetFrame(int handleId, float x, float yBottom, float width, float height) {
        if (handleId == 0) return;
        if (@available(iOS 16.0, *)) {
            UniTextRunOnMain(^{
                @autoreleasepool {
                    UniTextPasteHandle* handle = HandleForId(handleId);
                    if (!handle || !handle.responder) return;

                    UIView* host = UniTextHostView();
                    if (host && !handle.attached) {
                        [host addSubview:handle.responder];
                        handle.attached = YES;
                    }

                    CGRect frame;
                    if (UniTextTryUnityRectToHost(x, yBottom, width, height, &frame)) {
                        handle.responder.frame = frame;
                    }
                }
            });
        }
    }

    void UniTextPasteControl_SetHidden(int handleId, int hidden) {
        if (handleId == 0) return;
        if (@available(iOS 16.0, *)) {
            UniTextRunOnMain(^{
                UniTextPasteHandle* handle = HandleForId(handleId);
                if (handle && handle.responder) handle.responder.hidden = (hidden != 0);
            });
        }
    }

    void UniTextPasteControl_Destroy(int handleId) {
        if (handleId == 0) return;
        if (@available(iOS 16.0, *)) {
            UniTextRunOnMain(^{
                @autoreleasepool {
                    UniTextPasteHandle* handle = HandleForId(handleId);
                    if (!handle) return;
                    if (handle.responder) [handle.responder removeFromSuperview];
                    [s_handles removeObjectForKey:@(handleId)];
                }
            });
        }
    }

    // Pointer is plugin-owned; caller must NOT free. Valid until next tap or Clear.
    const char* UniTextPasteControl_GetCapturedPayload(const char* identifier) {
        if (identifier == NULL) return NULL;
        if (strcmp(identifier, "text/plain") == 0) return s_capturedPlain;
        if (strcmp(identifier, "text/html")  == 0) return s_capturedHtml;
        if (strcmp(identifier, "application/vnd.lightside.unitext") == 0) return s_capturedUniText;
        return NULL;
    }

    void UniTextPasteControl_ClearCapturedPayload(void) {
        ClearCaptured();
    }

} // extern "C"
