// The ONE copy of the SDF/MSDF distance decode, shared by every pipeline header — the same move
// LightSideAtlasDecode.hlsl makes for the shelf/tile decode. The format is selected per glyph by the
// vertex-stream mode (LightSideGlyphMode of UV1.w), not by a material keyword. Before including, the
// pipeline header defines the gradient-explicit texel fetches:
//   LIGHTSIDE_SAMPLE_ATLAS_TEXEL_GRAD(uv, dx, dy) — SDF atlas (_LightSideGlyphSdf, RHalf)
//   LIGHTSIDE_SAMPLE_MSDF_TEXEL_GRAD(uv, dx, dy)  — MSDF atlas (_LightSideGlyphMsdf, RGBAHalf)
// Gradient-explicit fetches are mandatory: the mode branch is quad-constant but varying-dependent,
// and an implicit-derivative fetch inside it would force the compiler to flatten the branch —
// every fragment would then pay both atlas fetches.
//
// Two distances ride together (MTSDF): x = sharp (perpendicular / median of RGB), y = round
// (true Euclidean, alpha channel). Single-channel SDF supplies x == y.

#ifndef LIGHTSIDE_SDF_SAMPLE_INCLUDED
#define LIGHTSIDE_SDF_SAMPLE_INCLUDED

float LightSideMedian3(float3 v)
{
    return max(min(v.r, v.g), min(max(v.r, v.g), v.b));
}

float2 LightSideSdf2FromSdf(float4 texel)  { return texel.rr - 0.5; }
float2 LightSideSdf2FromMsdf(float4 texel) { return float2(LightSideMedian3(texel.rgb), texel.a) - 0.5; }

#define SAMPLE_SDF2_MODE_GRAD(mode, uv, dx, dy) ((mode) < 0.5 \
    ? LightSideSdf2FromSdf(LIGHTSIDE_SAMPLE_ATLAS_TEXEL_GRAD(uv, dx, dy)) \
    : LightSideSdf2FromMsdf(LIGHTSIDE_SAMPLE_MSDF_TEXEL_GRAD(uv, dx, dy)))

#endif
