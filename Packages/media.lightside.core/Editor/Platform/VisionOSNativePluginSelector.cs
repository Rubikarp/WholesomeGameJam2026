#if UNITY_VISIONOS
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Includes exactly one of the paired visionOS static libraries per build: the plugin
    /// importer has no device/simulator distinction, so libraries under
    /// <c>Plugins/visionOS/Device/</c> and <c>Plugins/visionOS/Simulator/</c> are gated here
    /// against <c>PlayerSettings.VisionOS.sdkVersion</c>.
    /// </summary>
    [InitializeOnLoad]
    internal static class VisionOSNativePluginSelector
    {
        static VisionOSNativePluginSelector()
        {
            foreach (var importer in PluginImporter.GetAllImporters())
            {
                var path = importer.assetPath;
                if (string.IsNullOrEmpty(path)) continue;

                if (path.Contains("/Plugins/visionOS/Device/"))
                    importer.SetIncludeInBuildDelegate(_ =>
                        PlayerSettings.VisionOS.sdkVersion == VisionOSSdkVersion.Device);
                else if (path.Contains("/Plugins/visionOS/Simulator/"))
                    importer.SetIncludeInBuildDelegate(_ =>
                        PlayerSettings.VisionOS.sdkVersion == VisionOSSdkVersion.Simulator);
            }
        }
    }
}
#endif
