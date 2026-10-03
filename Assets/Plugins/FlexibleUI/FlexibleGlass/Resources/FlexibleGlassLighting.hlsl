#ifndef FLEXIBLE_GLASS_LIGHTING_INCLUDED
#define FLEXIBLE_GLASS_LIGHTING_INCLUDED

// Element-centered falloff with width measured relative to viewport height.
float GlassElementLightFalloff(float2 positionFromCenter, float viewportHeight, float2 lightDirection, float inverseWidth)
{
    const float2 centeredPosition = positionFromCenter / viewportHeight;
    const float distanceFromAxis = dot(centeredPosition, float2(-lightDirection.y, lightDirection.x));
    const float normalizedDistance = distanceFromAxis * min(inverseWidth, viewportHeight);
    const float coverage = saturate(1.0f - normalizedDistance * normalizedDistance);
    return coverage * coverage;
}

void GlassLipLightDirection(float2 viewportUv, float2 viewportSize, float4 light,
    out float2 direction, out float attenuation)
{
    direction = light.xy;
    attenuation = 1.0f;
    #if defined(FLEXIBLE_GLASS_EDGE_POINT)
        const float2 toLight = (light.xy - viewportUv) * viewportSize;
        const float radius = max(-light.w * viewportSize.y, 1.0f);
        attenuation = saturate(1.0f - length(toLight) / radius);
        attenuation *= attenuation * (3.0f - 2.0f * attenuation);
        direction = toLight * rsqrt(max(dot(toLight, toLight), 1e-6f));
    #endif
}

// Facing is (outer, inner). Normal reconstruction stays with each rendering path.
float2 GlassLipLightBeams(float2 facing, float spread, float opposingStrength)
{
    #if defined(FLEXIBLE_GLASS_EDGE_POINT)
        const float exponent = spread;
    #else
        const float exponent = 4.0f;
    #endif
    const float forwardOuter = pow(saturate(0.5f + 0.5f * facing.x), exponent);
    const float reverseInner = pow(saturate(0.5f - 0.5f * facing.y), exponent);
    float2 beams = float2(forwardOuter, reverseInner);
    #if defined(FLEXIBLE_GLASS_EDGE_OPPOSING)
        const float reverseOuter = pow(saturate(0.5f - 0.5f * facing.x), exponent);
        const float forwardInner = pow(saturate(0.5f + 0.5f * facing.y), exponent);
        beams += float2(reverseOuter, forwardInner) * opposingStrength;
    #endif
    #if defined(FLEXIBLE_GLASS_EDGE_POINT)
        beams *= rcp(max(beams.x + beams.y, 1e-4f));
    #endif
    return beams;
}

#endif
