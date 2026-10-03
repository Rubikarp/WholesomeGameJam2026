// Registers the static GPU plugin before Unity starts without claiming the app's controller subclass.

#import "UnityAppController.h"
#import <objc/runtime.h>

extern "C" {
    void UnityPluginLoad(struct IUnityInterfaces* unityInterfaces);
    void UnityPluginUnload(void);

    void LightSideGpuRegisterRenderingPlugin(void)
    {
#if UNITY_VERSION_VER >= 6000
        UnityRegisterPlugin(&UnityPluginLoad, &UnityPluginUnload);
#else
        UnityRegisterRenderingPluginV5(&UnityPluginLoad, &UnityPluginUnload);
#endif
    }
}

@interface UnityAppController (LightSideGpuRegistration)
- (void)lightSideGpuPreStartUnity;
@end

@implementation UnityAppController (LightSideGpuRegistration)
+ (void)load
{
    Method unityPreStart = class_getInstanceMethod(self, @selector(preStartUnity));
    Method lightSidePreStart = class_getInstanceMethod(self, @selector(lightSideGpuPreStartUnity));
    method_exchangeImplementations(unityPreStart, lightSidePreStart);
}

- (void)lightSideGpuPreStartUnity
{
    LightSideGpuRegisterRenderingPlugin();
    [self lightSideGpuPreStartUnity];
}
@end
