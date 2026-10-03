using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Device- and process-global state shared by every <see cref="GpuAtlas{TEntry}"/> family:
    /// the slice-blit material, the texture-array slice ceiling and the retirement-polling
    /// capability verdicts are properties of the device/session, not of a closed generic type.
    /// </summary>
    internal static class GpuAtlasShared
    {
        private static Material atlasBlitMaterial;

        internal static readonly int BlitSourceId = Shader.PropertyToID("_LsAtlasBlitSource");
        internal static readonly int BlitSliceId = Shader.PropertyToID("_LsAtlasBlitSlice");
        internal static readonly int BlitLodId = Shader.PropertyToID("_LsAtlasBlitLod");

        internal static Material AtlasBlitMaterial
        {
            get
            {
                if (atlasBlitMaterial != null) return atlasBlitMaterial;
                var shader = Resources.Load<Shader>("LightSideAtlasBlit");
                if (shader == null)
                    throw new InvalidOperationException(
                        "LightSideAtlasBlit.shader is missing from the LightSide.Core Resources folder.");
                atlasBlitMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                return atlasBlitMaterial;
            }
        }

        private static void DestroyAtlasBlitMaterial()
        {
            if (atlasBlitMaterial == null) return;
            ObjectUtils.SafeDestroy(atlasBlitMaterial);
            atlasBlitMaterial = null;
        }

#if UNITY_EDITOR
        static GpuAtlasShared()
        {
            EditorLifecycle.UnmanagedCleaning += DestroyAtlasBlitMaterial;
        }
#endif

        private const int GuaranteedTextureArraySlices = 256;
        private static int textureArraySliceLimit;

        /// <summary>Device texture-array slice ceiling, probed once per session; falls back to the API-guaranteed floor when the runtime cannot report it.</summary>
        internal static int TextureArraySliceLimit
        {
            get
            {
                if (textureArraySliceLimit != 0) return textureArraySliceLimit;
                int reported = GuaranteedTextureArraySlices;
                try
                {
                    var property = typeof(SystemInfo).GetProperty("maxTextureArraySlices",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (property?.GetValue(null) is int value && value > 0)
                        reported = value;
                }
                catch (Exception)
                {
                }
                textureArraySliceLimit = reported;
                return textureArraySliceLimit;
            }
        }

        internal static bool retirementReadbackPollingUnavailable;
    }
}
