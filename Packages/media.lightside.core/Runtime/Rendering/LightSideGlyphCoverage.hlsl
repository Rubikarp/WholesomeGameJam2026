#ifndef LIGHTSIDE_GLYPH_COVERAGE_INCLUDED
#define LIGHTSIDE_GLYPH_COVERAGE_INCLUDED

// Glyph application of the shared coverage primitive: samples the glyph field, then composes it
// through LightSideCoverage. The primitive itself — and every mode/corner convention it obeys —
// lives in Core and is shared with analytic shape fields.

#include "Packages/media.lightside.core/Runtime/Rendering/LightSideCoverage.hlsl"

// Full coverage for a painted quad, including the inner-shadow second SDF tap. Shared by every
// SDF shader (Canvas + world) so the contract lives once. Requires SAMPLE_SDF2_MODE_GRAD from
// LightSideSdfSample.hlsl + DILATE_SCALE from the including pipeline header (hence the guard).
// atlasUV = (uv, page, glyphMode) — w selects the SDF vs MSDF decode per glyph; uvDx/uvDy are the
// caller's pre-branch derivatives of atlasUV.xy (every fetch here is gradient-explicit, so the
// quad-constant mode branch and the coverage-mode branch never flatten into extra fetches).
// cov = TEXCOORD2 (mode, p0, p1, softness); extra = (faceDilate, glyphH, sdfScale.x, sdfScale.y).
#ifdef SAMPLE_SDF2_MODE_GRAD
float LightSideResolveGlyphCoverage(float4 atlasUV, float4 cov, float4 extra, float aa, float2 uvDx, float2 uvDy)
{
    float faceDilate = extra.x;
    float2 sd = SAMPLE_SDF2_MODE_GRAD(atlasUV.w, atlasUV.xyz, uvDx, uvDy) - faceDilate * DILATE_SCALE;
    float mode, corner;
    LightSideDecodeCoverageMode(cov.x, mode, corner);
    float result = 0.0;
    if (mode > 2.5)
    {
        // cov.yz is the tap offset in em. One glyphUV unit spans glyphH em (isotropic tile layout),
        // so divide by glyphH (extra.y) before applying the glyphUV->atlasUV scale — otherwise the
        // same em offset would shift by a different physical distance on every glyph.
        float2 off = float2(cov.y, cov.z) * (float2(extra.z, extra.w) / max(extra.y, 1e-6));
        float2 sd2 = SAMPLE_SDF2_MODE_GRAD(atlasUV.w, float3(atlasUV.xy - off, atlasUV.z), uvDx, uvDy)
                   - faceDilate * DILATE_SCALE;
        float inside = LightSideInsideCorner(sd, 0.0, aa, corner);
        float offsetInside = LightSideInsideCorner(sd2, 0.0, max(aa, cov.w * DILATE_SCALE), corner);
        result = inside * (1.0 - offsetInside);
    }
    else
        result = LightSideCoverage(mode, sd, aa, cov.y, cov.z, cov.w, DILATE_SCALE, corner);
    return result;
}

// Silhouette coverage for shadow casting: inner-shadow (mode 3) casts as the face; stroke/shadow/glow
// cast at their outer extent so the cast shadow matches the visible paint layers, not just the core glyph.
float LightSideResolveGlyphCastCoverage(float4 atlasUV, float4 cov, float faceDilate, float aa, float2 uvDx, float2 uvDy)
{
    float2 sd = SAMPLE_SDF2_MODE_GRAD(atlasUV.w, atlasUV.xyz, uvDx, uvDy) - faceDilate * DILATE_SCALE;
    float mode, corner;
    LightSideDecodeCoverageMode(cov.x, mode, corner);
    if (mode > 2.5)                        // inner-shadow casts the plain face, not its inset tap params
        return LightSideInsideCorner(sd, 0.0, aa, corner);
    return LightSideCoverage(mode, sd, aa, cov.y, cov.z, cov.w, DILATE_SCALE, corner);
}
#endif

#endif
