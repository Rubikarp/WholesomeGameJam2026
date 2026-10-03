// Common functions for LightSide SDF text rendering
// Glyph outlines are extracted at runtime from FreeType and rasterized into
// adaptively-sized SDF tiles (64/128) on CPU. The shader samples one tex2D
// per pixel — O(1).
//
// SDF stores signed distance (not coverage), so one tile serves all effect
// variations (outline, underlay) and all screen sizes (bilinear interpolation).
//
// Atlas encoding: signed distance in EM-space [-0.5, 0.5] mapped to R16F [0, 1].
//   CPU pipeline:    encoded = saturate(sign * dist_glyph * glyphH + 0.5)
//   This shader:     dist_em = tex.r - 0.5
//
// Isotropic layout: uniform scale in X and Y, glyph centered with per-glyph pad-tier padding.
//   maxDim = max(aspect, 1), baseExtent = maxDim + 2*padGlyph
//   gutter = baseExtent / tileSize  (~1 texel, keeps the rim off the tile edge so bilinear never bleeds)
//   totalExtent = baseExtent + 2*gutter, scale = tileSize / totalExtent (same for both axes)
//   glyphOffset = ((maxDim - dim)/2 + padGlyph + gutter) per axis
//   padGlyph = the glyph's pad-tier rim (GlyphAtlas.PadTierToNorm), not a fixed pad
#ifndef LIGHTSIDE_GLYPH_FIELD_INCLUDED
#define LIGHTSIDE_GLYPH_FIELD_INCLUDED

#include "UnityCG.cginc"
#include "UnityUI.cginc"
#include "Packages/media.lightside.core/Runtime/Rendering/LightSideGlyphProperties.cginc"
#include "LightSideAtlasDecode.hlsl"

// _LightSideGlyphSdf / _LightSideGlyphMsdf / _LightSideGlyphColor are Texture2DArrays — one slice per atlas page (declared in
// LightSideGlyphProperties.cginc). One material binds all three; the per-glyph mode in UV1.w
// (LightSideGlyphMode) selects the array. Page layer is encoded in UV0.z for SDF/MSDF and carried
// directly in UV0.z for color.

#define SDF_PAD LIGHTSIDE_SDF_PAD
#define DILATE_SCALE SDF_PAD

// Common uniforms
float _UIMaskSoftnessX;
float _UIMaskSoftnessY;
int _UIVertexColorAlwaysGammaSpace;

// Input vertex structure for SDF text — every quad = coverage(mode) × paint(kind).
//   texcoord2 = (coverageMode, p0, p1, softness): 0 fill, 1 stroke, 2 shadow/glow, 3 inner-shadow.
//   texcoord3 = (paintU, paintV, rampRow, paintKind): 0 solid, 1/2/3 gradient ramp, 4/5 texture.
//               Where the colour is sampled from a texture the CPU cannot recolour (texture paint,
//               colour glyph), z carries a colour-matrix atlas row + 1 instead (<= 0 = unfiltered).
// Plain glyphs leave texcoord2/3 at 0 → fill + solid. `color` carries the straight (non-premultiplied)
// quad colour; the shader resolves paint, then premultiplies. Shared by the Canvas and Lit shaders.
struct LightSideSurfaceVertex
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    float4 vertex    : POSITION;
    float3 normal    : NORMAL;    // per-quad world-space normal written by WorldBatcher
    fixed4 color     : COLOR;     // straight vertex colour (sRGB authored); premultiplied in fragment after paint
    float4 texcoord0 : TEXCOORD0; // xy = glyph UV (color: normalized atlas UV), z = encoded tile id (color: page layer), w = glyphH (color: 0)
    float4 texcoord1 : TEXCOORD1; // x = aspect (glyphW/glyphH), y = faceDilate, z = cluster index, w = intra-glyph X fraction (0..1) + 2*glyphMode (0 SDF / 1 MSDF / 2 color)
    float4 texcoord2 : TEXCOORD2; // coverageMode, p0, p1, softness
    float4 texcoord3 : TEXCOORD3; // paintU, paintV, rampRow, paintKind
    float4 tangent   : TANGENT;   // per-surface extra params (analytic shape radii / counts / angles)
};

// SDF atlas lookup lives in LightSideAtlasDecode.hlsl (LightSideLoadGlyphTransform) — the glyph
// handle's table fetch shared with the URP common include and the custom-effect preludes.

// ============================================
// Mask / clipping
// ============================================

half4 ComputeMask(float4 vert, float2 pixelSize)
{
    float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
    half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX), max(_UIMaskSoftnessY, _MaskSoftnessY));
    return half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * maskSoftness + abs(pixelSize)));
}

float4 ApplyVertexOffset(float4 vertex)
{
    vertex.x += _VertexOffsetX;
    vertex.y += _VertexOffsetY;
    return vertex;
}

fixed4 GammaToLinearIfNeeded(fixed4 color)
{
    if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
    {
        color.rgb = UIGammaToLinear(color.rgb);
    }
    return color;
}

half4 ApplyClipping(half4 color, half4 mask)
{
    #if UNITY_UI_CLIP_RECT
    half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(mask.xy)) * mask.zw);
    color *= m.x * m.y;
    #endif

    #if UNITY_UI_ALPHACLIP
    clip(color.a - 0.001);
    #endif

    return color;
}

half4 BlendOver(half4 dst, half4 src)
{
    dst.rgb = dst.rgb * (1.0 - src.a) + src.rgb;
    dst.a = saturate(dst.a + src.a);
    return dst;
}

// ============================================
// Glyph sampling — the SDF/MSDF decode lives once in LightSideSdfSample.hlsl, selected per glyph
// by the vertex-stream mode; all fetches are gradient-explicit (see that header for why).
// ============================================

// Analytic shape outlines (polygon vertices) — the built-in flavour of the shared fetch.
sampler2D _LightSideShapeVertices;
#define LIGHTSIDE_SAMPLE_SHAPE_VERTS(u, v) tex2Dlod(_LightSideShapeVertices, float4(u, v, 0, 0))

#define LIGHTSIDE_SAMPLE_ATLAS_TEXEL_GRAD(uv, dx, dy) _LightSideGlyphSdf.SampleGrad(sampler_LightSideGlyphSdf, (uv), (dx), (dy))
#define LIGHTSIDE_SAMPLE_MSDF_TEXEL_GRAD(uv, dx, dy) _LightSideGlyphMsdf.SampleGrad(sampler_LightSideGlyphMsdf, (uv), (dx), (dy))
#define LIGHTSIDE_SAMPLE_COLOR_TEXEL_GRAD(uv, dx, dy) _LightSideGlyphColor.SampleLevel(sampler_LightSideGlyphColor, (uv), LightSideColorLod((dx), (dy)))
#include "LightSideSdfSample.hlsl"

#endif // LIGHTSIDE_GLYPH_FIELD_INCLUDED
