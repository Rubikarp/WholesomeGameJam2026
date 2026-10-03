#ifndef LIGHTSIDE_PAINT_INCLUDED
#define LIGHTSIDE_PAINT_INCLUDED

// Unified paint math shared by every LightSide surface — text glyphs, decorations, vector shapes.
// Sampler declarations stay in each shader (built-in vs URP differ); this header is pure math + the
// compose step.
// paint = TEXCOORD3 payload: (paintU, paintV, rampRow, paintKind + 8 * paintSpread).
// The spread step is 8 (see PaintSpreadExtensions.CodeStep) so both parts survive interpolation
// exactly at any precision; texture kinds never carry a spread.
//
// LIGHTSIDE_SAMPLE_RAMP(u, v) MUST sample with explicit LOD 0 (tex2Dlod / SAMPLE_TEXTURE2D_LOD):
// the ramp atlas has no mips by construction (GradientRampAtlas), and an implicit-derivative
// fetch inside the varying-dependent gradient branch would force the compiler to flatten it —
// every plain fragment would then pay the ramp fetch.

// Paint kinds carried in TEXCOORD3.w (plus 8 x spread):
//   0 solid, 1 linear, 2 radial, 3 angular, 4 texture, 5 tiled texture, 6 distance.
// Distance ramps by the surface's own signed distance, which only the surface can supply: define
// LIGHTSIDE_PAINT_DISTANCE_T to an expression yielding that parameter before including this header.
// Without the define the kind is unreachable and costs nothing.
#define LIGHTSIDE_PAINT_KIND_DISTANCE 6.0

// Colour-matrix filter rows (ColorMatrixAtlas): one row = 3 RGBAFloat texels, texel i =
// (m_i0, m_i1, m_i2, offset_i); output channel i = dot(texel_i.xyz, rgb) + texel_i.w. A quad whose
// colour is sampled from a texture the CPU cannot recolour — a texture paint, a colour glyph —
// carries row + 1 in TEXCOORD3.z, the ramp-row slot those kinds leave unused, so an unallocated 0
// reads as "no filter". Solid and gradient colours never reach here: the CPU owns their samples and
// folds the filter into the vertex colour or the baked ramp row instead. Requires
// LIGHTSIDE_SAMPLE_MATRIX(u, v) with explicit LOD 0 (the atlas has no mips) and the
// _LightSideColorMatrixRows global. The transform runs on values as sampled — in a linear-space
// project that is linear light, while CPU-folded paints transform their sRGB-authored values; the
// divergence is confined to bitmap content.
#ifdef LIGHTSIDE_SAMPLE_MATRIX
half3 LightSideApplyColorMatrix(half3 rgb, float row)
{
    float v = (row + 0.5) / max(_LightSideColorMatrixRows, 1.0);
    half4 r0 = LIGHTSIDE_SAMPLE_MATRIX(0.5 / 3.0, v);
    half4 r1 = LIGHTSIDE_SAMPLE_MATRIX(1.5 / 3.0, v);
    half4 r2 = LIGHTSIDE_SAMPLE_MATRIX(2.5 / 3.0, v);
    return saturate(half3(
        dot(r0.xyz, rgb) + r0.w,
        dot(r1.xyz, rgb) + r1.w,
        dot(r2.xyz, rgb) + r2.w));
}

// Premultiplied variant: the linear part acts on premultiplied rgb directly and the offset scales
// by alpha, so transparent pixels stay transparent without an unpremultiply divide; rgb clamps to
// [0, a], matching the CPU paths' straight-colour clamp.
half4 LightSideApplyColorMatrixPremultiplied(half4 pm, float row)
{
    float v = (row + 0.5) / max(_LightSideColorMatrixRows, 1.0);
    half4 r0 = LIGHTSIDE_SAMPLE_MATRIX(0.5 / 3.0, v);
    half4 r1 = LIGHTSIDE_SAMPLE_MATRIX(1.5 / 3.0, v);
    half4 r2 = LIGHTSIDE_SAMPLE_MATRIX(2.5 / 3.0, v);
    half3 rgb = half3(dot(r0.xyz, pm.rgb), dot(r1.xyz, pm.rgb), dot(r2.xyz, pm.rgb))
        + half3(r0.w, r1.w, r2.w) * pm.a;
    pm.rgb = clamp(rgb, 0.0, pm.a);
    return pm;
}
#endif

// Raw gradient parameter; only angular is inherently bounded, the rest leave [0,1] once the
// projection frame is scaled or panned and rely on the spread wrap below.
float LightSideGradientT(float kind, float2 coord)
{
    if (kind < 1.5)
        return coord.x;                                 // linear
    if (kind < 2.5)
        return length(coord * 2.0 - 1.0);               // radial
    if (kind < 3.5)
    {
        float2 d = coord * 2.0 - 1.0;                    // angular
        return frac(atan2(d.y, d.x) / 6.2831853 + 0.5);
    }
#ifdef LIGHTSIDE_PAINT_DISTANCE_T
    return LIGHTSIDE_PAINT_DISTANCE_T;                   // distance
#else
    return coord.x;
#endif
}

// Folds the raw parameter into the ramp's [0,1] domain. Branchless: three cheap ALU forms selected
// by the packed mode, so a plain clamped gradient never pays a divergent branch. Matches
// PaintSpreadExtensions.Wrap, including the floor-based fold of negative input.
float LightSideSpreadWrap(float spread, float t)
{
    float repeated = frac(t);
    float mirrored = 1.0 - abs(frac(t * 0.5) * 2.0 - 1.0);
    return (spread < 0.5) ? saturate(t) : ((spread < 1.5) ? repeated : mirrored);
}

float LightSideRampV(float rampRow, float rampRows)
{
    return (rampRow + 0.5) / max(rampRows, 1.0);
}

// Ramp U with the half-texel remap matching the bake: GradientRampAtlas writes 256 texels with
// gradient value i/255 at texel i, whose centre sits at (i+0.5)/256 — t = 0 / 1 must land on the
// first / last texel centre, not the row edges (LightSideRampV applies the same centring in V).
float LightSideRampU(float t)
{
    return t * (255.0 / 256.0) + (0.5 / 256.0);
}

half4 LightSideComposePaintCoverage(half4 paintCol, float coverage)
{
    half4 result;
    result.rgb = paintCol.rgb * paintCol.a;
    result.a = paintCol.a;
    return result * coverage;
}

#ifdef LIGHTSIDE_SAMPLE_RAMP
// Resolves gradient-ramp paint: gradient kinds sample the shared ramp atlas modulated by the
// straight vertex colour; every other kind passes the vertex colour through.
half4 LightSideResolveGradientPaint(float4 paint, half4 vcolor)
{
    float spread = floor(paint.w * 0.125);
    float kind = paint.w - spread * 8.0;
    half4 result = vcolor;
    if (kind > 0.5 && (kind < 3.5 || kind > 5.5))
    {
        float t = LightSideSpreadWrap(spread, LightSideGradientT(kind, paint.xy));
        float u = LightSideRampU(t);
        float v = LightSideRampV(paint.z, _LightSideGradientRampRows);
        result = LIGHTSIDE_SAMPLE_RAMP(u, v) * vcolor;
    }
    return result;
}

// Full paint resolve for a painted quad. The texture path additionally requires the
// LIGHTSIDE_SAMPLE_PAINT macro (a keyword-gated whole-material mode, so implicit derivatives are fine
// there — no dynamic branch around the fetch).
//
// fit mirrors PaintFit: 0 stretch, 1 contain, 2 cover, 3 tile. Only contain discards outside the frame.
// texelInset is the half-texel guard the clamped kinds stay inside; pass 0 to sample the raw coordinate.
#ifdef LIGHTSIDE_SAMPLE_PAINT
half4 LightSideResolvePaint(float4 paint, half4 vcolor, float fit, float2 texelInset)
{
    #ifdef LIGHTSIDE_PAINT_TEXTURE
        float kind = paint.w - floor(paint.w * 0.125) * 8.0;
        bool tiled = kind > 4.5;
        float2 puv = tiled ? paint.xy : clamp(paint.xy, texelInset, 1.0 - texelInset);
        half inside = (!tiled && fit > 0.5 && fit < 1.5)
            ? ((paint.x >= 0.0 && paint.x <= 1.0 && paint.y >= 0.0 && paint.y <= 1.0) ? 1.0 : 0.0)
            : 1.0;
        half4 sampled = LIGHTSIDE_SAMPLE_PAINT(puv) * vcolor * inside;
        #ifdef LIGHTSIDE_SAMPLE_MATRIX
        if (paint.z > 0.5)
            sampled.rgb = LightSideApplyColorMatrix(sampled.rgb, paint.z - 1.0);
        #endif
        return sampled;
    #else
        return LightSideResolveGradientPaint(paint, vcolor);
    #endif
}

half4 LightSideResolvePaint(float4 paint, half4 vcolor)
{
    return LightSideResolvePaint(paint, vcolor, 1.0, float2(0.0, 0.0));
}
#endif
#endif

#endif // LIGHTSIDE_PAINT_INCLUDED
