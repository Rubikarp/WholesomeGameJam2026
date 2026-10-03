#if UNITY_WEBGL
using UnityEditor;

namespace LightSide
{
    [InitializeOnLoad]
    internal static class WebGLNativePluginSelector
    {
        private const bool IsModernEmscripten =
#if UNITY_6000_5_OR_NEWER
            true;
#else
            false;
#endif

        static WebGLNativePluginSelector()
        {
            foreach (var importer in PluginImporter.GetAllImporters())
            {
                var path = importer.assetPath;
                if (string.IsNullOrEmpty(path) || !path.EndsWith("/libunitext_native.a"))
                    continue;

                if (path.Contains("/Plugins/WebGL/LegacyDefault/"))
                    importer.SetIncludeInBuildDelegate(_ => !IsModernEmscripten && !IsWasm2023);
                else if (path.Contains("/Plugins/WebGL/LegacyWasm/"))
                    importer.SetIncludeInBuildDelegate(_ => !IsModernEmscripten && IsWasm2023);
                else if (path.Contains("/Plugins/WebGL/ModernDefault/"))
                    importer.SetIncludeInBuildDelegate(_ => IsModernEmscripten && !IsWasm2023);
                else if (path.Contains("/Plugins/WebGL/ModernWasm/"))
                    importer.SetIncludeInBuildDelegate(_ => IsModernEmscripten && IsWasm2023);
            }
        }

        private static bool IsWasm2023
        {
#if UNITY_6000_0_OR_NEWER
            get => PlayerSettings.WebGL.wasm2023;
#else
            get => false;
#endif
        }
    }
}
#endif
