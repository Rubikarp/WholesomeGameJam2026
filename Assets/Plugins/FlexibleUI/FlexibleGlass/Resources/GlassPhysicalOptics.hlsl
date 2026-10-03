#ifndef FLEXIBLE_GLASS_PHYSICAL_OPTICS
#define FLEXIBLE_GLASS_PHYSICAL_OPTICS

float ResolveGlassNormalizedDepth(float opticalDepth, float geometricDepth, float thickness, float fallbackAmount)
{
    const float originalOpticalDepth = opticalDepth;
    [branch] if (fallbackAmount > 0.0f)
    {
        // A coarse distance mip can cross zero inside the shape. Before the
        // correction T + D - G collapses, recover toward the geometric depth.
        // With u = (T + D - G) / T, the corrected denominator becomes
        // T * lerp(u, 1, fallback), so it stays positive through the transition.
        // The outer half of the geometric lip is unchanged for D >= 0.
        const float correctionRatio = (thickness + opticalDepth - geometricDepth) / thickness;
        const float fallback = 1.0f - smoothstep(0.25f, 0.5f, correctionRatio);
        opticalDepth = lerp(opticalDepth, geometricDepth, fallback);
    }
    float normalizedDepth = min(opticalDepth / max(thickness + opticalDepth - geometricDepth, 1e-3f), 4.0f);
    [branch] if (fallbackAmount > 0.0f && fallbackAmount < 1.0f)
    {
        // Blend the resolved profiles at mixed UIGlass joins, not the denominator:
        // partially repairing a collapsed denominator would create another pole.
        const float originalDepth = min(originalOpticalDepth / max(thickness + originalOpticalDepth - geometricDepth, 1e-3f), 4.0f);
        normalizedDepth = lerp(originalDepth, normalizedDepth, fallbackAmount);
    }
    return normalizedDepth;
}

float PhysicalDispersionCoefficient(float refractiveIndex, float abbeNumber)
{
    const float blueWavelength = 486.1327f;
    const float redWavelength = 656.2725f;
    const float principalDispersion = (refractiveIndex - 1.0f) / clamp(abbeNumber, 0.1f, 64.0f);
    const float blueInverseSquare = 1000000.0f / (blueWavelength * blueWavelength);
    const float redInverseSquare = 1000000.0f / (redWavelength * redWavelength);
    return principalDispersion / (blueInverseSquare - redInverseSquare);
}

float PhysicalRefractiveIndex(float refractiveIndex, float dispersionCoefficient, float wavelengthNanometers)
{
    const float referenceWavelength = 587.5618f;
    const float wavelength = clamp(wavelengthNanometers, 486.1327f, 656.2725f);
    const float wavelengthInverseSquare = 1000000.0f / (wavelength * wavelength);
    const float referenceInverseSquare = 1000000.0f / (referenceWavelength * referenceWavelength);
    return max(1.0f, refractiveIndex + dispersionCoefficient * (wavelengthInverseSquare - referenceInverseSquare));
}

float PhysicalRefractionLateralNormal(float distance, float opticalLip)
{
    const float lipPosition = saturate(-distance / max(opticalLip, 1e-4f));
    const float linearNormal = 1.0f - lipPosition;
    return linearNormal * linearNormal * (3.0f - 2.0f * linearNormal);
}

float PhysicalRefractionDisplacement(float distance, float opticalLip, float strength, float refractiveIndex)
{
    const float lateralNormal = PhysicalRefractionLateralNormal(distance, opticalLip);
    const float verticalNormal = sqrt(saturate(1.0f - lateralNormal * lateralNormal));
    const float eta = rcp(max(refractiveIndex, 1.0001f));
    const float transmittedRoot = sqrt(saturate(1.0f - eta * eta * lateralNormal * lateralNormal));
    const float bend = transmittedRoot - eta * verticalNormal;
    const float raySlope = bend * lateralNormal / max(eta + bend * verticalNormal, 1e-4f);
    return -opticalLip * max(strength, 0.0f) * raySlope;
}

float PhysicalRefractionReconstructionLod(float distance, float opticalLip, float strength, float normalCoherence, float refractiveIndex, float dispersionCoefficient, float magnification, float maximumLod)
{
    // The actual source mapping scales displacement by the filtered normal's
    // length. Its scalar spatial/spectral estimate must use that same strength.
    // The separate screen-mapping footprint covers spatial changes in the vector.
    const float coherentStrength = max(strength, 0.0f) * saturate(normalCoherence);
    // Both profile probes are flat here. The independently measured screen
    // footprint still accounts for spatial changes in the full source mapping.
    float result = 0.0f;
    [branch] if (coherentStrength != 0.0f && distance + 0.5f > -max(opticalLip, 1e-4f))
    {
        const float lowerDistance = distance - 0.5f;
        const float upperDistance = distance + 0.5f;
        const float inverseMagnification = rcp(max(magnification, 1.0f));
        const float2 wavelengthInverseSquare = 1000000.0f / (float2(486.1327f, 656.2725f) * float2(486.1327f, 656.2725f));
        const float referenceInverseSquare = 1000000.0f / (587.5618f * 587.5618f);
        const float2 indices = max(1.0f, refractiveIndex + dispersionCoefficient * (wavelengthInverseSquare - referenceInverseSquare));
        const float2 eta = rcp(max(indices, 1.0001f));
        const float lowerLateralNormal = PhysicalRefractionLateralNormal(lowerDistance, opticalLip);
        const float upperLateralNormal = PhysicalRefractionLateralNormal(upperDistance, opticalLip);
        const float lowerVerticalNormal = sqrt(saturate(1.0f - lowerLateralNormal * lowerLateralNormal));
        const float upperVerticalNormal = sqrt(saturate(1.0f - upperLateralNormal * upperLateralNormal));
        const float2 lowerTransmittedRoot = sqrt(saturate(1.0f - eta * eta * lowerLateralNormal * lowerLateralNormal));
        const float2 upperTransmittedRoot = sqrt(saturate(1.0f - eta * eta * upperLateralNormal * upperLateralNormal));
        const float2 lowerBend = lowerTransmittedRoot - eta * lowerVerticalNormal;
        const float2 upperBend = upperTransmittedRoot - eta * upperVerticalNormal;
        const float2 lowerRaySlope = lowerBend * lowerLateralNormal / max(eta + lowerBend * lowerVerticalNormal, 1e-4f);
        const float2 upperRaySlope = upperBend * upperLateralNormal / max(eta + upperBend * upperVerticalNormal, 1e-4f);
        const float2 lowerSource = (lowerDistance - opticalLip * coherentStrength * lowerRaySlope) * inverseMagnification;
        const float2 upperSource = (upperDistance - opticalLip * coherentStrength * upperRaySlope) * inverseMagnification;
        const float spatialFootprint = max(abs(upperSource.x - lowerSource.x), abs(upperSource.y - lowerSource.y));
        const float spectralFootprint = abs((lowerSource.x + upperSource.x) - (lowerSource.y + upperSource.y)) * (1.0f / 12.0f);
        const float footprint = max(1.0f, max(spatialFootprint, spectralFootprint));
        result = min(log2(footprint), maximumLod);
    }
    return result;
}

float PhysicalRefractionScreenFootprintLod(float2 sourcePositionPixels, float maximumLod)
{
    // The radial profile above measures how quickly the displacement magnitude
    // changes across the lip. It cannot see the second dimension of the mapping:
    // a curved or projectively compressed surface may rotate its normal rapidly
    // along the edge. Measure the final source mapping as well so reconstruction
    // covers both displacement magnitude and direction.
    const float2 sourceDx = ddx(sourcePositionPixels);
    const float2 sourceDy = ddy(sourcePositionPixels);
    const float majorAxis = max(length(sourceDx), length(sourceDy));
    return min(log2(max(majorAxis, 1.0f)), maximumLod);
}

#endif

