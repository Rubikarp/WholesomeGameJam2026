using Unity.Burst;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Per-glyph inputs the base-quad kernel needs, pre-resolved on the calling thread (atlas lookup,
    /// font metrics, culling/hidden filtering). One entry per EMITTING glyph — skipped, hidden, emoji
    /// and not-in-atlas glyphs never reach the array, so the kernel writes a dense <c>i*4</c> quad layout.
    /// </summary>
    internal struct GlyphQuadInput
    {
        public float x, y;
        public int cluster;
        public float tileIdx;
        public float bearingXNorm, bearingYNorm;
        public float glyphH, aspect;
        public float metricsFactor;
        public Color32 color;
    }

    /// <summary>
    /// Burst base-quad builder for the fast path (no per-glyph modifier hooks, fake-bold or glyph-metric
    /// overrides — those keep the managed loop). Burst Direct Call keeps the same body callable from
    /// mesh worker threads and executes it as managed code when Burst is unavailable.
    /// Geometry is a 1:1 port of the managed per-glyph block; output is visually identical, not
    /// bit-identical (Burst codegen differs from Mono, and <see cref="FloatMode.Fast"/> reassociates
    /// for SIMD — divergence stays far below sub-pixel).
    /// </summary>
    [BurstCompile]
    internal static unsafe class UniTextQuadBurst
    {
        /// <summary>
        /// Builds <paramref name="count"/> base quads into the pinned output buffers. Callable from any thread.
        /// <paramref name="uv1wBias"/> is the glyph-mode offset packed into UV1.w alongside the intra-glyph
        /// X fraction (0 SDF / 2 MSDF — see LightSideGlyphMode in LightSideAtlasDecode.hlsl).
        /// </summary>
        public static void Build(GlyphQuadInput* glyphs, int count,
            Vector3* verts, Vector4* uv0, Vector4* uv1, Color32* cols, float offX, float offY, float uv1wBias)
            => BuildEntry(glyphs, count, verts, uv0, uv1, cols, offX, offY, uv1wBias);

        private const float SdfPadding = UniTextMeshGenerator.DefaultSdfPadding;

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
        private static void BuildEntry(GlyphQuadInput* glyphs, int count,
            Vector3* verts, Vector4* uv0, Vector4* uv1, Color32* cols, float offX, float offY, float uv1wBias)
        {
            for (var i = 0; i < count; i++)
            {
                ref var g = ref glyphs[i];

                var glyphH = g.glyphH;
                var aspect = g.aspect;
                var maxDim = aspect > 1f ? aspect : 1f;
                var marginX = (maxDim - aspect) * 0.5f;
                var marginY = (maxDim - 1f) * 0.5f;

                var padEmX = (marginX + SdfPadding) * glyphH;
                var padEmY = (marginY + SdfPadding) * glyphH;
                var sideScaled = (maxDim + SdfPadding * 2f) * glyphH * g.metricsFactor;

                var bearingXScaled = (g.bearingXNorm - padEmX) * g.metricsFactor;
                var bearingYScaled = (g.bearingYNorm + padEmY) * g.metricsFactor;

                var tlX = offX + g.x + bearingXScaled;
                var tlY = offY - g.y + bearingYScaled;
                var blY = tlY - sideScaled;
                var trX = tlX + sideScaled;

                var uvMinX = -(marginX + SdfPadding);
                var uvMinY = -(marginY + SdfPadding);
                var uvMaxX = aspect + marginX + SdfPadding;
                var uvMaxY = 1f + marginY + SdfPadding;

                var tileIdx = g.tileIdx;
                var cluster = (float)g.cluster;
                var b = i * 4;

                verts[b] = new Vector3(tlX, blY, 0f);
                verts[b + 1] = new Vector3(tlX, tlY, 0f);
                verts[b + 2] = new Vector3(trX, tlY, 0f);
                verts[b + 3] = new Vector3(trX, blY, 0f);

                uv0[b] = new Vector4(uvMinX, uvMinY, tileIdx, glyphH);
                uv0[b + 1] = new Vector4(uvMinX, uvMaxY, tileIdx, glyphH);
                uv0[b + 2] = new Vector4(uvMaxX, uvMaxY, tileIdx, glyphH);
                uv0[b + 3] = new Vector4(uvMaxX, uvMinY, tileIdx, glyphH);

                uv1[b] = new Vector4(aspect, 0f, cluster, uv1wBias);
                uv1[b + 1] = new Vector4(aspect, 0f, cluster, uv1wBias);
                uv1[b + 2] = new Vector4(aspect, 0f, cluster, uv1wBias + 1f);
                uv1[b + 3] = new Vector4(aspect, 0f, cluster, uv1wBias + 1f);

                var c = g.color;
                cols[b] = c;
                cols[b + 1] = c;
                cols[b + 2] = c;
                cols[b + 3] = c;
            }
        }
    }
}
