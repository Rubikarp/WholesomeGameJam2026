// Building blocks for LightSide effect authoring — pure math, pipeline-agnostic, included by both
// custom preludes (LightSide_Custom.cginc / LightSide_Custom-URP.hlsl). Call these from LightSideEffect.

#ifndef LIGHTSIDE_EFFECT_LIB_INCLUDED
#define LIGHTSIDE_EFFECT_LIB_INCLUDED

half4 LightSideTintPremultiplied(half4 color, half4 tint)
{
    return color * half4(tint.rgb * tint.a, tint.a);
}

// IQ cosine palette: smooth RGB hue cycle over t (period 1). saturation 0 = white, 1 = full colour.
half3 LightSideCosPalette(float t, float saturation, float brightness)
{
    const half3 phase = half3(0.0, 0.333, 0.667);
    return lerp(half3(1, 1, 1), 0.5 + 0.5 * cos(6.28318 * (t + phase)), saturation) * brightness;
}

// Stable per-glyph random pair in [0,1) seeded by the atlas tile id — desyncs patterns between
// letters, size-invariantly. The preludes precompute this per vertex into LightSideFrag.tileHash.
float2 LightSideTileHash(float tileId)
{
    return frac(float2(sin(tileId * 0.012345) * 43758.5, sin(tileId * 0.098765) * 22578.1));
}

float LightSidePackTileHash(float2 hash)
{
    return floor(hash.x * 1023.0) + 0.25 + hash.y * 0.5;
}

float2 LightSideUnpackTileHash(float packedHash)
{
    float x = floor(packedHash);
    float y = saturate((packedHash - x - 0.25) * 2.0);
    return float2(x * (1.0 / 1023.0), y);
}

#endif
