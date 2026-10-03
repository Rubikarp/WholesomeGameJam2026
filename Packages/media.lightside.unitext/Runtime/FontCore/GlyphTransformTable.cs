using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// The GPU side of the stable glyph handle: one RGBA32F texel per live SDF/MSDF atlas entry holding
    /// the glyph's atlas transform — isotropic scale, offset.xy, page layer — the exact values the vertex
    /// programs previously decoded from the physical tile id. Vertices carry the HANDLE (UV0.z), so any
    /// relocation (pad-tier upgrade, tile-size upgrade, compaction) rewrites one texel here instead of
    /// patching every mesh and every sub-mesh copy of the quads.
    /// </summary>
    /// <remarks>
    /// One table and one handle space serve both the SDF and MSDF instances — handles are globally
    /// unique, so meshes mixing modes need no per-mode disambiguation. Emoji entries allocate no handle
    /// (their quads carry the atlas rect directly). <see cref="FilterMode.Point"/> is a hard functional
    /// requirement: RGBA32F is non-filterable on the GLES3/WebGL2 floor and any other filter makes the
    /// texture incomplete (every fetch returns (0,0,0,1)). Bound globally like the gradient ramp;
    /// fetched with <c>Texture2D.Load</c> in the vertex stage (core texelFetch on GLSL ES 3.00).
    /// </remarks>
    internal static class GlyphTransformTable
    {
        /// <summary>SYNC: LIGHTSIDE_GLYPH_TABLE_WIDTH in LightSideAtlasDecode.hlsl.</summary>
        internal const int Width = 1024;

        private const int InitialRows = 4;

        private static Vector4[] texels = new Vector4[Width * InitialRows];
        private static readonly Stack<int> freeHandles = new();
        private static readonly List<int> deferredFrees = new();
        private static int highWater;
        private static Texture2D texture;
        private static bool dirty;

#if UNITY_EDITOR
        static GlyphTransformTable() => EditorLifecycle.UnmanagedCleaning += DisposeTable;
#endif

        internal static int Allocate()
        {
            if (freeHandles.Count > 0) return freeHandles.Pop();
            var handle = highWater++;
            if (handle >= texels.Length)
                Array.Resize(ref texels, texels.Length * 2);
            return handle;
        }

        /// <summary>
        /// Returns a handle to the pool one renderer publication later: meshes referencing a just-released
        /// entry may still be displayed this frame, and an immediately reused handle would retarget them.
        /// Deferred handles re-enter the pool in <see cref="CommitDeferredFrees"/> — the same publication
        /// point that commits deferred tile retirements.
        /// </summary>
        internal static void Free(int handle)
        {
            if (handle < 0) return;
            deferredFrees.Add(handle);
        }

        internal static void CommitDeferredFrees()
        {
            for (int i = 0; i < deferredFrees.Count; i++)
                freeHandles.Push(deferredFrees[i]);
            deferredFrees.Clear();
        }

        internal static void Set(int handle, float scale, float offsetX, float offsetY, int pageLayer)
        {
            texels[handle] = new Vector4(scale, offsetX, offsetY, pageLayer);
            dirty = true;
        }

        /// <summary>
        /// Uploads pending texel writes — once per frame from the atlas flush point, inside the pre-draw
        /// window, so command order alone guarantees the rows land before the first consuming draw (the
        /// same contract as the pixel uploads; relocation GPU copies issued earlier in the frame also
        /// precede it in command order).
        /// </summary>
        internal static void FlushIfDirty()
        {
            if (!dirty) return;
            dirty = false;

            int rows = texels.Length / Width;
            if (texture == null || texture.height != rows)
            {
                if (texture != null) ObjectUtils.SafeDestroy(texture);
                texture = new Texture2D(Width, rows, TextureFormat.RGBAFloat, false, true)
                {
                    name = "UniText Glyph Transform Table",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                Shader.SetGlobalTexture(ShaderIds.GlyphTable.Table, texture);
            }

            texture.SetPixelData(texels, 0);
            texture.Apply(false, false);
        }

        private static void DisposeTable()
        {
            if (texture != null)
            {
                Shader.SetGlobalTexture(ShaderIds.GlyphTable.Table, null);
                ObjectUtils.SafeDestroy(texture);
                texture = null;
            }
            texels = new Vector4[Width * InitialRows];
            freeHandles.Clear();
            deferredFrees.Clear();
            highWater = 0;
            dirty = false;
        }
    }
}
