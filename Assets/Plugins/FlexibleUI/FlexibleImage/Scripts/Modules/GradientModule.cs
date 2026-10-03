using System;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[Serializable]
public class GradientAnimData
{
    public Color[] proceduralGradientColors = new Color[ProceduralProperties.Colors1dArrayLength];
    public Vector2 proceduralGradientColorOffset;
    public float proceduralGradientColorRotation;
    public Vector2 proceduralGradientColorScale = Vector2.one;
    public Vector2 proceduralGradientPosition = new(0.5f, 0.5f);
    public Vector2 radialGradientSize = new(0.5f, 0.5f);
    [Range(0, 1)] public float radialGradientStrength = 0.5f;
    public Vector2 angleGradientStrength = new(0.5f, 0.5f);
    public float proceduralGradientAngle;
    public float sdfGradientInnerDistance;
    public float sdfGradientOuterDistance;
    [Range(0, 1)] public float sdfGradientInnerReach;
    [Range(0, 1)] public float sdfGradientOuterReach;
    [Range(0, 1)] public float proceduralGradientPointerStrength = 0.5f;
    [Range(-1, 1)] public float conicalGradientCurvature;
    [Range(0, 1)] public float conicalGradientTailStrength = 0.5f;
    [Range(0, 32767)] public uint noiseSeed;
    [Range(0, 1)] public float noiseScale = 0.5f;
    [Range(0, 1)] public float noiseEdge = 0.5f;
    [Range(0, 1)] public float noiseStrength = 0.5f;

    public GradientAnimData()
    {
        for (int i = 0; i < proceduralGradientColors.Length; i++)
            proceduralGradientColors[i] = Color.black;
    }
}

[Serializable]
public class GradientConfig
{
    public QuadData.GradientType gradientType;
    public bool alphaIsBlend;
    public bool affectsInterior = true;
    public bool affectsOutline;
    public bool aspectCorrection;
    public bool positionFromPointer;
    public bool invert;
    public bool noiseAlternateMode;
    public bool screenSpace;
    public Vector2Int colorDimensions = Vector2Int.one;
    public QuadData.ColorGridWrapMode colorWrapModeX = QuadData.ColorGridWrapMode.Clamp;
    public QuadData.ColorGridWrapMode colorWrapModeY = QuadData.ColorGridWrapMode.Clamp;
    [Range(0, 1)] public float colorPresetMix = 1f;
}
}
