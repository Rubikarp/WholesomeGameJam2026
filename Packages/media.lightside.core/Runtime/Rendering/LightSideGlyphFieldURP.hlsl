// URP-flavored counterpart of LightSideGlyphField.cginc.
// Same SDF / MSDF atlas math, but uses URP/Core HLSL API (TEXTURE2D_ARRAY, SAMPLE_TEXTURE2D_ARRAY)
// instead of legacy UNITY_* macros. Encoding (signed distance in EM-space [-0.5, 0.5]
// mapped to R16F [0, 1], shelf layout, page stride) is identical to the built-in path —
// CPU pipeline emits the same atlas for both renderers.

#ifndef LIGHTSIDE_GLYPH_FIELD_URP_INCLUDED
#define LIGHTSIDE_GLYPH_FIELD_URP_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "LightSideAtlasDecode.hlsl"

#define SDF_PAD       LIGHTSIDE_SDF_PAD
#define DILATE_SCALE  SDF_PAD

// Glyph atlases — one material binds all three; the per-glyph mode in UV1.w selects the array.
// Each keeps its own sampler state from the texture asset (SDF/MSDF linear/bilinear/mip-less,
// color sRGB/trilinear with a truncated mip chain).
TEXTURE2D_ARRAY(_LightSideGlyphSdf);   SAMPLER(sampler_LightSideGlyphSdf);   // SDF  (RHalf)
TEXTURE2D_ARRAY(_LightSideGlyphMsdf);   SAMPLER(sampler_LightSideGlyphMsdf);   // MSDF (RGBAHalf)
TEXTURE2D_ARRAY(_LightSideGlyphColor);  SAMPLER(sampler_LightSideGlyphColor);  // Color (RGBA32 sRGB + mips)

// Every quad = coverage(mode) × paint(kind). texcoord2 = (coverageMode, p0, p1, softness),
// texcoord3 = (paintU, paintV, rampRow, paintKind). `color` is the straight quad colour,
// premultiplied in the fragment after paint resolves. Same layout as the built-in path.
struct LightSideSurfaceVertex
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;     // per-quad world-space normal written by WorldBatcher
    float4 color      : COLOR;      // straight vertex colour; premultiplied in fragment after paint
    float4 texcoord0  : TEXCOORD0;  // xy = glyph UV (color: normalized atlas UV), z = encoded tile id (color: page layer), w = glyphH (color: 0)
    float4 texcoord1  : TEXCOORD1;  // x = aspect, y = faceDilate, z = cluster index, w = intra-glyph X fraction (0..1) + 2*glyphMode
    float4 texcoord2  : TEXCOORD2;  // coverageMode, p0, p1, softness
    float4 texcoord3  : TEXCOORD3;
    float4 tangent    : TANGENT;    // per-surface extra params (analytic shape radii / counts / angles)
};

// Glyph sampling — the SDF/MSDF decode lives once in LightSideSdfSample.hlsl, selected per glyph
// by the vertex-stream mode; all fetches are gradient-explicit (see that header for why).
// Analytic shape outlines (polygon vertices) — the URP flavour of the shared fetch.
TEXTURE2D(_LightSideShapeVertices); SAMPLER(sampler_LightSideShapeVertices);
#define LIGHTSIDE_SAMPLE_SHAPE_VERTS(u, v) SAMPLE_TEXTURE2D_LOD(_LightSideShapeVertices, sampler_LightSideShapeVertices, float2(u, v), 0)

#define LIGHTSIDE_SAMPLE_ATLAS_TEXEL_GRAD(uv, dx, dy) SAMPLE_TEXTURE2D_ARRAY_GRAD(_LightSideGlyphSdf, sampler_LightSideGlyphSdf, (uv).xy, (uv).z, dx, dy)
#define LIGHTSIDE_SAMPLE_MSDF_TEXEL_GRAD(uv, dx, dy) SAMPLE_TEXTURE2D_ARRAY_GRAD(_LightSideGlyphMsdf, sampler_LightSideGlyphMsdf, (uv).xy, (uv).z, dx, dy)
#define LIGHTSIDE_SAMPLE_COLOR_TEXEL_GRAD(uv, dx, dy) SAMPLE_TEXTURE2D_ARRAY_LOD(_LightSideGlyphColor, sampler_LightSideGlyphColor, (uv).xy, (uv).z, LightSideColorLod((dx), (dy)))
#include "LightSideSdfSample.hlsl"

#endif // LIGHTSIDE_GLYPH_FIELD_URP_INCLUDED
