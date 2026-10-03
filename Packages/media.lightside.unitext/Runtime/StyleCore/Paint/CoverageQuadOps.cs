using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>Coverage-mode codes written to TEXCOORD2.x. Must match <c>UniText_Coverage.hlsl</c>.</summary>
    public static class CoverageMode
    {
        public const float Fill = 0f;
        public const float Stroke = 1f;
        public const float Shadow = 2f;
        public const float InnerShadow = 3f;

        /// <summary>Corner code for round (Euclidean) corners; 0 = legacy perpendicular field (plain fill).</summary>
        public const float RoundCorners = 0.25f;

        /// <summary>
        /// Packs a corner code into the mode: <c>mode + 16 × code</c>, where code is 0 (legacy
        /// perpendicular field), <see cref="RoundCorners"/>, or a Sharp miter limit quantized to
        /// 0.25 steps (1..8). Unallocated UV2 reads as 0 → Fill + legacy, so plain text is unaffected.
        /// </summary>
        public static float WithCorner(float mode, float cornerCode)
        {
            if (cornerCode <= 0f) return mode;
            float code = cornerCode <= RoundCorners
                ? RoundCorners
                : Mathf.Round(Mathf.Clamp(cornerCode, 1f, 8f) * 4f) * 0.25f;
            return mode + 16f * code;
        }
    }

    /// <summary>
    /// Writes the unified paint vertex contract — colour, coverage (TEXCOORD2) and paint
    /// (TEXCOORD3) — into a reserved quad whose positions/UV0/UV1 were already copied from the glyph
    /// template by <see cref="EffectModifier.ReserveQuad"/>. One writer shared by every layer kind
    /// so the contract lives in a single place.
    /// </summary>
    /// <remarks>
    /// TEXCOORD3 is written only when the buffer is allocated (text uses a gradient/texture paint).
    /// Plain solid text leaves UV2/UV3 unallocated, so the shader reads 0 → Fill + Solid for free.
    /// </remarks>
    public static class CoverageQuadOps
    {
        /// <summary>
        /// Encodes a resolved paint into the TEXCOORD3.w contract. Core projection kinds are
        /// zero-based; gradient shader codes reserve zero for solid paint, and a gradient's spread
        /// mode rides the same float in steps of <see cref="PaintSpreadExtensions.CodeStep"/>.
        /// Texture codes carry no spread — a texture's wrap comes from the asset's own mode.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float PaintKindCode(in TextPaint p)
        {
            if (p.kind == PaintSourceKind.Texture) return p.fit == PaintFit.Tile ? TiledTexturePaintKind : TexturePaintKind;
            if (p.kind == PaintSourceKind.Solid) return 0f;
            return p.spread.Pack((float)p.shape + 1f);
        }

        /// <summary>TEXCOORD3.w code of a clamped texture paint — the shader samples the material's paint texture at the quad's paint coordinates.</summary>
        internal const float TexturePaintKind = 4f;

        /// <summary>TEXCOORD3.w code of a tiled texture paint.</summary>
        internal const float TiledTexturePaintKind = 5f;

        /// <summary>
        /// Fills the four destination vertices. <paramref name="frame"/> maps each vertex to its
        /// paint coordinate (gradient/texture); <paramref name="rampRow"/> is the gradient's ramp
        /// row — for a texture paint a <see cref="ColorMatrixAtlas"/> row + 1 (≤ 0 = unfiltered),
        /// ignored for solid; <paramref name="fade"/> (the component colour alpha)
        /// scales every layer's alpha so fading the text fades all layers together.
        /// </summary>
        public static void Write(UniTextMeshGenerator gen, int destBaseIdx,
            in TextPaint paint, in PaintFrame frame, int rampRow,
            float coverageMode, float p0, float p1, float softness, byte fade)
            => Write(gen.Colors, gen.Uvs2, gen.Uvs3, gen.Vertices, destBaseIdx,
                in paint, in frame, rampRow, coverageMode, p0, p1, softness, fade);

        /// <summary>
        /// Array-targeted overload for sub-mesh paths (texture paint) that accumulate into their own
        /// pooled buffers instead of the generator's. <paramref name="verts"/> supplies the quad
        /// positions each gradient/texture coordinate is mapped from.
        /// </summary>
        public static void Write(Color32[] cols, Vector4[] uvs2, Vector4[] uvs3, Vector3[] verts, int destBaseIdx,
            in TextPaint paint, in PaintFrame frame, int rampRow,
            float coverageMode, float p0, float p1, float softness, byte fade)
        {
            var alpha = (byte)((paint.color.a * fade + 127) / 255);
            var vcol = new Color32(paint.color.r, paint.color.g, paint.color.b, alpha);
            var uv2 = new Vector4(coverageMode, p0, p1, softness);
            var solid = paint.kind == PaintSourceKind.Solid;
            var kindCode = PaintKindCode(in paint);

            for (var i = 0; i < 4; i++)
            {
                var idx = destBaseIdx + i;
                cols[idx] = vcol;
                uvs2[idx] = uv2;

                if (uvs3 == null) continue;
                if (solid)
                {
                    uvs3[idx] = Vector4.zero;
                }
                else
                {
                    ref readonly var v = ref verts[idx];
                    var c = PaintProjectionMath.Coord(in frame, v.x, v.y);
                    uvs3[idx] = new Vector4(c.x, c.y, rampRow, kindCode);
                }
            }
        }

        /// <summary>Displaces a reserved effect quad's four vertices in the X/Y plane (Z untouched).</summary>
        public static void ApplyOffset(UniTextMeshGenerator gen, int destBaseIdx, float offsetX, float offsetY)
        {
            if (offsetX == 0f && offsetY == 0f) return;
            var verts = gen.Vertices;
            verts[destBaseIdx].x     += offsetX; verts[destBaseIdx].y     += offsetY;
            verts[destBaseIdx + 1].x += offsetX; verts[destBaseIdx + 1].y += offsetY;
            verts[destBaseIdx + 2].x += offsetX; verts[destBaseIdx + 2].y += offsetY;
            verts[destBaseIdx + 3].x += offsetX; verts[destBaseIdx + 3].y += offsetY;
        }

        /// <summary>
        /// Multiplies each destination vertex alpha by the GLYPH's own alpha at the source face vertex
        /// (<see cref="UniTextMeshGenerator.FaceAlpha"/> — the pre-claim stash when a fill claimed the
        /// quad, so a layer never inherits another layer's paint alpha) — the canonical formula keeping
        /// outline / shadow / extrude / textured-fill fades synchronised with face fades
        /// (per-character <c>&lt;alpha&gt;</c> ramps, <see cref="ColorModifier"/> alpha).
        /// Run after <see cref="Write"/>, whose uniform alpha carries only the component-level fade.
        /// </summary>
        public static void ModulateAlpha(Color32[] dstCols, int dstBaseIdx, UniTextMeshGenerator gen, int srcBaseIdx)
        {
            for (var i = 0; i < 4; i++)
            {
                ref var c = ref dstCols[dstBaseIdx + i];
                c.a = (byte)((c.a * gen.FaceAlpha(srcBaseIdx + i) + 127) / 255);
            }
        }
    }
}
