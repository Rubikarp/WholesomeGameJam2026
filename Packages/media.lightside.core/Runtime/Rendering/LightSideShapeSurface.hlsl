#ifndef LIGHTSIDE_SHAPE_SURFACE_INCLUDED
#define LIGHTSIDE_SHAPE_SURFACE_INCLUDED

#include "LightSideShapeField.hlsl"

// Coverage of one analytic shape quad, dispatched by the draw mode packed in TEXCOORD2.x.
//   p   local position relative to the shape centre       b    half-size
//   prm per-shape params (radii / ratios / counts)        aux  corner smoothing or generic aux
//   style the kind's own style variant — corner style for a cornered kind, cap style for the arc
//   mp  mode params — stroke (width, align), shadow (offset.xy, blur, spread),
//       bevel (lightAngle, width, strength, side), noise (frequency, seed, contrast)
// The field value is returned through `d` so a caller driving distance paint reuses this evaluation
// instead of paying for a second one.
float LightSideShapeCoverage(float kind, float mode, float2 p, float2 b, float4 prm, float aux,
                             float4 mp, float style, out float d)
{
    // Mode 4 — no analytic field: coverage rides the vertex stream in mp.x and is resolved to a
    // screen-space antialiased edge here. Emitters of arbitrary triangles have no box to describe them,
    // and a zero-sized box would read as distance 0 across the whole surface — half coverage everywhere.
    if (mode > 3.5)
    {
        d = 0.0;
        float edge = max(fwidth(mp.x), 1e-4);
        return saturate((mp.x - 0.5) / edge + 0.5);
    }

    d = evalShapeSdf(kind, p, b, prm, aux, style);
    float aa = max(fwidth(d), 1e-4);

    if (mode < 0.5)
        return saturate(0.5 - d / aa);

    if (mode < 1.5)
    {
        float halfW = mp.x * 0.5;
        float off   = mp.y * halfW;
        float sd    = abs(d - off) - halfW;
        return saturate(0.5 - sd / aa);
    }

    if (mode < 2.5)
    {
        float ds   = evalShapeSdf(kind, p - mp.xy, b, prm, aux, style) - mp.w;
        float band = max(mp.z * 2.0, aa);
        return saturate(0.5 - ds / band);
    }

    if (mode < 3.5)
    {
        float inside    = saturate(0.5 - d / aa);
        float dOff      = evalShapeSdf(kind, p - mp.xy, b, prm, aux, style);
        float shadowAmt = saturate(dOff / max(mp.z, aa));
        return inside * shadowAmt;
    }

    if (mode < 4.5)
    {
        // Bevel: rim light from the field gradient (edge normal) against the light direction.
        // side +1 lights toward the light, -1 away.
        float2 g = float2(ddx(d), ddy(d));
        float gl = length(g);
        g = gl > 1e-6 ? g / gl : float2(0.0, 1.0);
        float2 L = float2(cos(mp.x), sin(mp.x));
        float lit = dot(g, L) * mp.w;
        float profile = saturate(1.0 + d / max(mp.y, aa));   // 1 at edge -> 0 at width inside
        float shapeMask = saturate(0.5 - d / aa);
        return shapeMask * profile * saturate(lit) * mp.z;
    }

    // Noise: procedural value noise masked by the shape.
    float nz = uiSdfValueNoise(p * mp.x + mp.y);
    nz = saturate((nz - 0.5) * mp.z + 0.5);
    return saturate(0.5 - d / aa) * nz;
}

// The distance-paint parameter for a shape: the field measured inward from the outline, clamped there
// because outside is not part of the interior domain — a repeating ramp must not fold the antialiased
// fringe onto its far end.
float LightSideShapeDistanceT(float d, float2 b, float scale)
{
    return max(-d / max(min(b.x, b.y) * scale, 1e-3), 0.0);
}

#endif // LIGHTSIDE_SHAPE_SURFACE_INCLUDED
