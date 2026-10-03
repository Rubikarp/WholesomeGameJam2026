using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[BurstCompile(FloatMode = FloatMode.Deterministic, OptimizeFor = OptimizeFor.Performance)]
public unsafe struct UIVertexMeshSubdivisionJob : IJobParallelForBatch
{
    [NativeDisableUnsafePtrRestriction]
    [ReadOnly] public UIVertex* Vertices;
    [ReadOnly] public int InputStart, OutputStart;

    public void Execute(int startIndex, int count)
    {
        for (int batch = 0; batch < count; batch++)
        {
            var triangleIndex = startIndex + batch;
            var inputIndex = InputStart + triangleIndex * 3;
            var v0 = Vertices[inputIndex++];
            var v1 = Vertices[inputIndex++];
            var v2 = Vertices[inputIndex];
            var m01 = new UIVertex {position = (v0.position + v1.position) * 0.5f, uv0 = (v0.uv0 + v1.uv0) * 0.5f};
            var m12 = new UIVertex {position = (v1.position + v2.position) * 0.5f, uv0 = (v1.uv0 + v2.uv0) * 0.5f};
            var m20 = new UIVertex {position = (v2.position + v0.position) * 0.5f, uv0 = (v2.uv0 + v0.uv0) * 0.5f};
            var outputIndex = OutputStart + triangleIndex * 12;
            Vertices[outputIndex++] = v0;  Vertices[outputIndex++] = m01; Vertices[outputIndex++] = m20;
            Vertices[outputIndex++] = m01; Vertices[outputIndex++] = m12; Vertices[outputIndex++] = m20;
            Vertices[outputIndex++] = v1;  Vertices[outputIndex++] = m12; Vertices[outputIndex++] = m01;
            Vertices[outputIndex++] = v2;  Vertices[outputIndex++] = m20; Vertices[outputIndex]   = m12;
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Deterministic, OptimizeFor = OptimizeFor.Performance)]
public unsafe struct UIVertexMeshSubdivisionXJob : IJobParallelForBatch
{
    [NativeDisableUnsafePtrRestriction]
    [ReadOnly] public UIVertex* Vertices;
    [ReadOnly] public int InputStart, OutputStart;

    public void Execute(int startIndex, int count)
    {
        for (int batch = 0; batch < count; batch++)
        {
            var unitIndex = startIndex + batch;
            var inputIndex   = InputStart + unitIndex * 12;
            var v0   = Vertices[inputIndex];
            var v1   = Vertices[inputIndex + 1];
            var cOld = Vertices[inputIndex + 2];
            var v2   = Vertices[inputIndex + 4];
            var v3   = Vertices[inputIndex + 7];
            var m01 = new UIVertex {position = (v0.position + v1.position) * 0.5f, uv0 = (v0.uv0 + v1.uv0) * 0.5f};
            var m12 = new UIVertex {position = (v1.position + v2.position) * 0.5f, uv0 = (v1.uv0 + v2.uv0) * 0.5f};
            var m23 = new UIVertex {position = (v2.position + v3.position) * 0.5f, uv0 = (v2.uv0 + v3.uv0) * 0.5f};
            var m30 = new UIVertex {position = (v3.position + v0.position) * 0.5f, uv0 = (v3.uv0 + v0.uv0) * 0.5f};

            var outputIndex = OutputStart + unitIndex * 48;
            var corner0 = v1; var corner1 = m12; var corner2 = cOld; var corner3 = m01;
            var cNew = new UIVertex{position = (corner0.position + corner1.position + corner2.position + corner3.position) * 0.25f, uv0 = (corner0.uv0 + corner1.uv0 + corner2.uv0 + corner3.uv0) * 0.25f};
            Vertices[outputIndex++] = corner0;  Vertices[outputIndex++] = corner1;  Vertices[outputIndex++] = cNew;
            Vertices[outputIndex++] = corner1;  Vertices[outputIndex++] = corner2;  Vertices[outputIndex++] = cNew;
            Vertices[outputIndex++] = corner2;  Vertices[outputIndex++] = corner3;  Vertices[outputIndex++] = cNew;
            Vertices[outputIndex++] = corner3;  Vertices[outputIndex++] = corner0;  Vertices[outputIndex++] = cNew;

            corner0 = m12; corner1 = v2; corner2 = m23; corner3 = cOld;
            cNew = new UIVertex{position = (corner0.position + corner1.position + corner2.position + corner3.position) * 0.25f, uv0 = (corner0.uv0 + corner1.uv0 + corner2.uv0 + corner3.uv0) * 0.25f};
            Vertices[outputIndex++] = corner0;  Vertices[outputIndex++] = corner1;  Vertices[outputIndex++] = cNew;
            Vertices[outputIndex++] = corner1;  Vertices[outputIndex++] = corner2;  Vertices[outputIndex++] = cNew;
            Vertices[outputIndex++] = corner2;  Vertices[outputIndex++] = corner3;  Vertices[outputIndex++] = cNew;
            Vertices[outputIndex++] = corner3;  Vertices[outputIndex++] = corner0;  Vertices[outputIndex++] = cNew;

            corner0 = cOld; corner1 = m23; corner2 = v3; corner3 = m30;
            cNew = new UIVertex{position = (corner0.position + corner1.position + corner2.position + corner3.position) * 0.25f, uv0 = (corner0.uv0 + corner1.uv0 + corner2.uv0 + corner3.uv0) * 0.25f};
            Vertices[outputIndex++] = corner0;  Vertices[outputIndex++] = corner1;  Vertices[outputIndex++] = cNew;
            Vertices[outputIndex++] = corner1;  Vertices[outputIndex++] = corner2;  Vertices[outputIndex++] = cNew;
            Vertices[outputIndex++] = corner2;  Vertices[outputIndex++] = corner3;  Vertices[outputIndex++] = cNew;
            Vertices[outputIndex++] = corner3;  Vertices[outputIndex++] = corner0;  Vertices[outputIndex++] = cNew;

            corner0 = m01; corner1 = cOld; corner2 = m30; corner3 = v0;
            cNew = new UIVertex{position = (corner0.position + corner1.position + corner2.position + corner3.position) * 0.25f, uv0 = (corner0.uv0 + corner1.uv0 + corner2.uv0 + corner3.uv0) * 0.25f};
            Vertices[outputIndex++] = corner0;  Vertices[outputIndex++] = corner1;  Vertices[outputIndex++] = cNew;
            Vertices[outputIndex++] = corner1;  Vertices[outputIndex++] = corner2;  Vertices[outputIndex++] = cNew;
            Vertices[outputIndex++] = corner2;  Vertices[outputIndex++] = corner3;  Vertices[outputIndex++] = cNew;
            Vertices[outputIndex++] = corner3;  Vertices[outputIndex++] = corner0;  Vertices[outputIndex] = cNew;
        }
    }
}

[BurstCompile(FloatMode = FloatMode.Deterministic, OptimizeFor = OptimizeFor.Performance)]
public unsafe struct UIVertexProcessingJob : IJobParallelForBatch
{
    [NativeDisableUnsafePtrRestriction]
    [ReadOnly] public UIVertex* Vertices;
    [ReadOnly] public int FirstVertexIndex;
    [ReadOnly] public NativeArray<float4> PrimaryColors;
    [ReadOnly] public NativeArray<float4> ProceduralGradientColors;
    [ReadOnly] public NativeArray<float4> OutlineColors;
    [ReadOnly] public NativeArray<float4> PatternColors;
    [ReadOnly] public float4x4 TransformationMatrix;
    [ReadOnly] public float4 PresetPrimaryColor;
    [ReadOnly] public float4 PresetProceduralGradientColor;
    [ReadOnly] public float4 PresetOutlineColor;
    [ReadOnly] public float4 PresetPatternColor;
    [ReadOnly] public float4 UvRect;
    [ReadOnly] public float4 InitialUv1;
    [ReadOnly] public float4 Uv2;
    [ReadOnly] public float4 Uv3;
    [ReadOnly] public int2 PrimaryColorDimensions;
    [ReadOnly] public int2 ProceduralGradientColorDimensions;
    [ReadOnly] public int2 OutlineColorDimensions;
    [ReadOnly] public int2 PatternColorDimensions;
    [ReadOnly] public float PrimaryColorPresetMix;
    [ReadOnly] public float ProceduralGradientColorPresetMix;
    [ReadOnly] public float PresetOutlineColorMix;
    [ReadOnly] public float PresetPatternColorMix;
    [ReadOnly] public float ShaderFlagsOne;
    [ReadOnly] public float PackedCollapsedEdgePositionAmountAndFlagsTwo;
    [ReadOnly] public float PackedStrokeSoftnessAndFlagsThree;
    [ReadOnly] public float PackedWidthHeight;
    [ReadOnly] public bool NeedsProceduralGradient;
    [ReadOnly] public bool NeedsOutlineColor;
    [ReadOnly] public bool NeedsPatternColor;

    [ReadOnly] public bool PrimaryColorIsAdvanced;
    [ReadOnly] public float2 PrimaryColorOffset;
    [ReadOnly] public float2 PrimaryColorScale;
    [ReadOnly] public float PrimaryColorRotation;
    [ReadOnly] public byte PrimaryColorWrapModeX;
    [ReadOnly] public byte PrimaryColorWrapModeY;
    [ReadOnly] public bool OutlineColorIsAdvanced;
    [ReadOnly] public float2 OutlineColorOffset;
    [ReadOnly] public float2 OutlineColorScale;
    [ReadOnly] public float OutlineColorRotation;
    [ReadOnly] public byte OutlineColorWrapModeX;
    [ReadOnly] public byte OutlineColorWrapModeY;
    [ReadOnly] public bool ProceduralGradientColorIsAdvanced;
    [ReadOnly] public float2 ProceduralGradientColorOffset;
    [ReadOnly] public float2 ProceduralGradientColorScale;
    [ReadOnly] public float ProceduralGradientColorRotation;
    [ReadOnly] public byte ProceduralGradientColorWrapModeX;
    [ReadOnly] public byte ProceduralGradientColorWrapModeY;
    [ReadOnly] public bool PatternColorIsAdvanced;
    [ReadOnly] public float2 PatternColorOffset;
    [ReadOnly] public float2 PatternColorScale;
    [ReadOnly] public float PatternColorRotation;
    [ReadOnly] public byte PatternColorWrapModeX;
    [ReadOnly] public byte PatternColorWrapModeY;

    private static float EncodeFloat8_8_8_8(float4 color)
    {
        var ea = (uint)(color.x * 255f + 0.5f) & 0xFFu;
        var eb = (uint)(color.y * 255f + 0.5f) & 0xFFu;
        var ec = (uint)(color.z * 255f + 0.5f) & 0xFFu;
        var ed = (uint)(color.w * 255f + 0.5f) & 0xFFu;
        var packed = (ea << 24) | (eb << 16) | (ec << 8) | ed;
        return PunUintToFloat(packed, 24);
    }

    private static float EncodeFloat12_12_8(float a, float b, float c)
    {
        var ea = (uint)(a * 4095f) & 4095u;
        var eb = (uint)(b * 4095f) & 4095u;
        var ec = (uint)(c * 255f) & 255u;
        var packed = (ea << 20) | (eb << 8) | ec;
        return PunUintToFloat(packed);
    }

    private static float PunUintToFloat(uint input, byte sacrificeBit = 23)
    {
        var f = math.asfloat(input);
        var match = math.asuint(f) == input;
        var mask = match ? 0u : 1u << sacrificeBit;
        return math.asfloat(input ^ mask);
    }

    private static float4 GetVertexColor(float2 uv0, NativeArray<float4> colors, int2 dimensions, float4 presetColor, float presetMix)
    {
        if (dimensions is { x: 1, y: 1 })
            return math.lerp(colors[0], presetColor, presetMix);

        var u = uv0.x;
        var v = 1f - uv0.y;
        var scaledU = u * (dimensions.x - 1);
        var scaledV = v * (dimensions.y - 1);
        var uIndex = (int)scaledU;
        var vIndex = (int)scaledV;
        var maxIdx = ProceduralProperties.Colors2dArrayDimensionSize - 1;

        var uIndexPlusOne = uIndex + 1;
        if (uIndexPlusOne > maxIdx)
            uIndexPlusOne = maxIdx;

        var vIndexPlusOne = vIndex + 1;
        if (vIndexPlusOne > maxIdx)
            vIndexPlusOne = maxIdx;

        var idxBL = GetColorSpiralIndex(uIndex, vIndex);
        var idxBR = GetColorSpiralIndex(uIndexPlusOne, vIndex);
        var idxTL = GetColorSpiralIndex(uIndex, vIndexPlusOne);
        var idxTR = GetColorSpiralIndex(uIndexPlusOne, vIndexPlusOne);

        var bottomLeft = colors[idxBL];
        var bottomRight = colors[idxBR];
        var topLeft = colors[idxTL];
        var topRight = colors[idxTR];

        var uFrac = scaledU - uIndex;
        var vFrac = scaledV - vIndex;

        var colorBottom = math.lerp(bottomLeft, bottomRight, uFrac);
        var colorTop = math.lerp(topLeft, topRight, uFrac);
        var finalColor = math.lerp(colorBottom, colorTop, vFrac);

        return math.lerp(finalColor, presetColor, presetMix);
    }

    private static float4 GetVertexColorAdv(float2 uv, NativeArray<float4> colors, int2 dimensions, float4 presetColor, float presetMix, float2 gradientCenterUV, float2 gradientScale, float gradientRotationDeg, byte wrapModeU, byte wrapModeV)
    {
        if (dimensions is { x: 1, y: 1 })
            return math.lerp(colors[0], presetColor, presetMix);

        var u = uv.x;
        var v = 1f - uv.y;

        if (gradientScale.x != 1f || gradientScale.y != 1f || gradientRotationDeg != 0f)
        {
            var localX = (u - 0.5f) / gradientScale.x;
            var localY = -(v - 0.5f) / gradientScale.y;
            if (gradientRotationDeg != 0f)
            {
                var rad = math.radians(gradientRotationDeg);
                var cosA = math.cos(rad);
                var sinA = math.sin(rad);
                var rotatedX = localX * cosA - localY * sinA;
                var rotatedY = localX * sinA + localY * cosA;
                localX = rotatedX;
                localY = rotatedY;
            }
            u = 0.5f + localX;
            v = 0.5f - localY;
        }

        u -= gradientCenterUV.x;
        v -= gradientCenterUV.y;
        u *= dimensions.x - 1f;
        v *= dimensions.y - 1f;

        SampleAxis(u, dimensions.x, wrapModeU, out var uIndex, out var uIndexPlusOne, out var uFrac);
        SampleAxis(v, dimensions.y, wrapModeV, out var vIndex, out var vIndexPlusOne, out var vFrac);

        var idxBL = GetColorSpiralIndex(uIndex, vIndex);
        var idxBR = GetColorSpiralIndex(uIndexPlusOne, vIndex);
        var idxTL = GetColorSpiralIndex(uIndex, vIndexPlusOne);
        var idxTR = GetColorSpiralIndex(uIndexPlusOne, vIndexPlusOne);

        var bottomLeft = colors[idxBL];
        var bottomRight = colors[idxBR];
        var topLeft = colors[idxTL];
        var topRight = colors[idxTR];

        var colorBottom = math.lerp(bottomLeft, bottomRight, uFrac);
        var colorTop = math.lerp(topLeft, topRight, uFrac);
        var finalColor = math.lerp(colorBottom, colorTop, vFrac);

        return math.lerp(finalColor, presetColor, presetMix);

        void SampleAxis(float scaled, int dim, byte mode, out int i0, out int i1, out float frac)
        {
            if (dim <= 1)
            {
                i0 = 0; i1 = 0; frac = 0f;
                return;
            }

            switch (mode)
            {
                case 0:
                    scaled = math.clamp(scaled, 0f, math.max(0f, dim - 1f));
                    break;
                case 1:
                {
                    var baseIndex = (int)math.floor(scaled);
                    frac = scaled - baseIndex;
                    i0 = PositiveModInt(baseIndex, dim);
                    i1 = PositiveModInt(baseIndex + 1, dim);
                    return;
                }
                case 2:
                    scaled = Mirror(scaled, dim - 1);
                    break;
                default:
                    scaled = PingPong(scaled, dim - 1);
                    break;
            }

            var baseIdx = (int)math.floor(scaled);
            frac = scaled - baseIdx;
            i0 = baseIdx;
            i1 = baseIdx + 1;

            var max = dim - 1;
            if (i1 > max)
                i1 = max;
        }

        int PositiveModInt(int value, int length)
        {
            var r = value % length;
            return r < 0 ? r + length : r;
        }
        float PositiveMod(float value, float length) => (value % length + length) % length;
        float PingPong(float t, float length) => length - math.abs(PositiveMod(t, length * 2f) - length);
        float Mirror(float t, float length)
        {
            var period = length * 2f + 2f;
            var x = PositiveMod(t, period);

            if (x < length)
                return x;

            if (x < length + 1f)
                return length;

            if (x < length * 2f + 1f)
                return length * 2f + 1f - x;

            return 0f;
        }
    }

    private static int GetColorSpiralIndex(int x, int y)
    {
        var k = x > y ? x : y;
        var indexOffset = x == k ? y : k + 1 + (k - 1 - x);
        return k * k + indexOffset;
    }

    public void Execute(int startIndex, int count)
    {
        startIndex += FirstVertexIndex;
        var endIndex = startIndex + count;
        for (int i = startIndex; i < endIndex; i++)
        {
            var newVert = Vertices + i;
            var uv0 = new float2(newVert->uv0.x, newVert->uv0.y);
            
            var col = PrimaryColorIsAdvanced
                ? GetVertexColorAdv(uv0, PrimaryColors, PrimaryColorDimensions, PresetPrimaryColor, PrimaryColorPresetMix, PrimaryColorOffset, PrimaryColorScale, PrimaryColorRotation, PrimaryColorWrapModeX, PrimaryColorWrapModeY)
                : GetVertexColor(uv0, PrimaryColors, PrimaryColorDimensions, PresetPrimaryColor, PrimaryColorPresetMix);
            newVert->color = (Color)(Vector4)col;

            var uv1 = InitialUv1;
            if (NeedsProceduralGradient)
            {
                col = ProceduralGradientColorIsAdvanced
                    ? GetVertexColorAdv(uv0, ProceduralGradientColors, ProceduralGradientColorDimensions, PresetProceduralGradientColor, ProceduralGradientColorPresetMix, ProceduralGradientColorOffset, ProceduralGradientColorScale, ProceduralGradientColorRotation, ProceduralGradientColorWrapModeX, ProceduralGradientColorWrapModeY)
                    : GetVertexColor(uv0, ProceduralGradientColors, ProceduralGradientColorDimensions, PresetProceduralGradientColor, ProceduralGradientColorPresetMix);
                uv1.x = EncodeFloat8_8_8_8(col);
            }
            if (NeedsOutlineColor)
            {
                col = OutlineColorIsAdvanced
                    ? GetVertexColorAdv(uv0, OutlineColors, OutlineColorDimensions, PresetOutlineColor, PresetOutlineColorMix, OutlineColorOffset, OutlineColorScale, OutlineColorRotation, OutlineColorWrapModeX, OutlineColorWrapModeY)
                    : GetVertexColor(uv0, OutlineColors, OutlineColorDimensions, PresetOutlineColor, PresetOutlineColorMix);
                uv1.y = EncodeFloat8_8_8_8(col);
            }
            if (NeedsPatternColor)
            {
                col = PatternColorIsAdvanced
                    ? GetVertexColorAdv(uv0, PatternColors, PatternColorDimensions, PresetPatternColor, PresetPatternColorMix, PatternColorOffset, PatternColorScale, PatternColorRotation, PatternColorWrapModeX, PatternColorWrapModeY)
                    : GetVertexColor(uv0, PatternColors, PatternColorDimensions, PresetPatternColor, PresetPatternColorMix);
                uv1.z = EncodeFloat8_8_8_8(col);
            }

            var position = math.mul(TransformationMatrix, new float4(newVert->position.x, newVert->position.y, newVert->position.z, 1));
            newVert->position = new Vector3(position.x, position.y, position.z);

            var remappedU = (math.clamp(UvRect.x + uv0.x * UvRect.z, -1f, 2f) + 1f) / 3f;
            var remappedV = (math.clamp(UvRect.y + uv0.y * UvRect.w, -1f, 2f) + 1f) / 3f;
            var uvAndFlagsOnePacked = EncodeFloat12_12_8(remappedU, remappedV, ShaderFlagsOne);

            newVert->uv0 = new Vector4(uvAndFlagsOnePacked,
                                     PackedCollapsedEdgePositionAmountAndFlagsTwo,
                                     PackedStrokeSoftnessAndFlagsThree,
                                     PackedWidthHeight);
            newVert->uv1 = uv1;
            newVert->uv2 = Uv2;
            newVert->uv3 = Uv3;
        }
    }
}
}