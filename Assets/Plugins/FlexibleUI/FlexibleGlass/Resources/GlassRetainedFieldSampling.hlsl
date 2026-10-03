#ifndef FLEXIBLE_GLASS_RETAINED_FIELD_SAMPLING
#define FLEXIBLE_GLASS_RETAINED_FIELD_SAMPLING

float2 GlassBSplineMiddleLeft(float2 position)
{
    return 0.16666667f + position * (0.5f + position * (0.5f - position * 0.5f));
}

float2 GlassBSplineMiddleRight(float2 position)
{
    return 0.66666667f + position * (-1.0f + 0.5f * position) * position;
}

float2 GlassBSplineRightmost(float2 position)
{
    return 0.16666667f + position * (-0.5f + position * (0.5f - position * 0.16666667f));
}

void GlassBicubicFilter(float2 fraction, out float2 weights[2], out float2 offsets[2])
{
    const float2 rightmost = GlassBSplineRightmost(fraction);
    const float2 middleRight = GlassBSplineMiddleRight(fraction);
    const float2 middleLeft = GlassBSplineMiddleLeft(fraction);
    const float2 leftmost = 1.0f - middleRight - middleLeft - rightmost;
    weights[0] = rightmost + middleRight;
    weights[1] = middleLeft + leftmost;
    offsets[0] = -1.0f + middleRight * rcp(weights[0]);
    offsets[1] = 1.0f + leftmost * rcp(weights[1]);
}

float4 SampleGlassRetainedFieldBicubicMip(float2 uv, float slice, float mipLod)
{
    // The terminal mip is constant across the slice; four filtered taps cannot
    // add information to a 1x1 field.
    float4 result;
    [branch] if (mipLod >= _GlassSdfMaxLod)
        result = FLEXIBLE_GLASS_SAMPLE_RETAINED_FIELD(uv, slice, mipLod);
    else
    {
        const float2 mipSize = max(_GlassSdfResolution * exp2(-mipLod), 1.0f);
        const float2 inverseMipSize = rcp(mipSize);
        const float2 texelPosition = uv * mipSize + 0.5f;
        const float2 integerPosition = floor(texelPosition);
        const float2 fraction = frac(texelPosition);
        float2 weights[2];
        float2 offsets[2];
        GlassBicubicFilter(fraction, weights, offsets);
        const float2 lowerLeftUv = (integerPosition + float2(offsets[0].x, offsets[0].y) - 0.5f) * inverseMipSize;
        const float2 lowerRightUv = (integerPosition + float2(offsets[1].x, offsets[0].y) - 0.5f) * inverseMipSize;
        const float2 upperLeftUv = (integerPosition + float2(offsets[0].x, offsets[1].y) - 0.5f) * inverseMipSize;
        const float2 upperRightUv = (integerPosition + float2(offsets[1].x, offsets[1].y) - 0.5f) * inverseMipSize;
        result = weights[0].y *
               (weights[0].x * FLEXIBLE_GLASS_SAMPLE_RETAINED_FIELD(lowerLeftUv, slice, mipLod) +
                weights[1].x * FLEXIBLE_GLASS_SAMPLE_RETAINED_FIELD(lowerRightUv, slice, mipLod)) +
               weights[1].y *
               (weights[0].x * FLEXIBLE_GLASS_SAMPLE_RETAINED_FIELD(upperLeftUv, slice, mipLod) +
                weights[1].x * FLEXIBLE_GLASS_SAMPLE_RETAINED_FIELD(upperRightUv, slice, mipLod));
    }
    return result;
}

float4 SampleGlassRetainedFieldBicubic(float2 uv, float slice, float lod)
{
    const float lowerLod = floor(lod);
    const float upperLod = min(lowerLod + 1.0f, _GlassSdfMaxLod);
    const float4 lower = SampleGlassRetainedFieldBicubicMip(uv, slice, lowerLod);
    float4 result = lower;
    [branch] if (lod != lowerLod)
    {
        const float4 upper = SampleGlassRetainedFieldBicubicMip(uv, slice, upperLod);
        result = lerp(lower, upper, saturate(lod - lowerLod));
    }
    return result;
}

float4 SampleGlassRetainedField(float2 uv, float slice, float lod)
{
    // Broad optical smoothing exposes bilinear gradient kinks. Blend to bicubic
    // over 32-64 base-level texel footprints; fine fields use one trilinear sample.
    const float cubicStartLod = 5.0f;
    const float cubicFullLod = 6.0f;
    float4 result;
    [branch] if (lod <= cubicStartLod)
        result = FLEXIBLE_GLASS_SAMPLE_RETAINED_FIELD(uv, slice, lod);
    else
    {
        result = SampleGlassRetainedFieldBicubic(uv, slice, lod);
        [branch] if (lod < cubicFullLod)
        {
            const float4 trilinear = FLEXIBLE_GLASS_SAMPLE_RETAINED_FIELD(uv, slice, lod);
            result = lerp(trilinear, result, saturate((lod - cubicStartLod) / (cubicFullLod - cubicStartLod)));
        }
    }
    return result;
}

#endif
