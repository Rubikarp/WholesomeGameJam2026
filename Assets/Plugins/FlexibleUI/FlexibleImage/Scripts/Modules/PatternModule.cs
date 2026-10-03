using System;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[Serializable]
public class PatternAnimData
{
    public Color[] patternColors = new Color[ProceduralProperties.Colors1dArrayLength];
    public Vector2 patternColorOffset;
    public float patternColorRotation;
    public Vector2 patternColorScale = Vector2.one;
    [Range(0, 1)] public float patternDensity;
    [Range(-1, 1)] public float patternSpeed;
    [Range(0, 1)] public float patternCellParam = 0.5f;
    [Range(0, 255)] public byte patternLineThickness = 127;
    public int patternSpriteRotation;

    public PatternAnimData()
    {
        for (int i = 0; i < patternColors.Length; i++)
            patternColors[i] = Color.black;
    }
}

[Serializable]
public class PatternConfig
{
    public QuadData.PatternType patternType = QuadData.PatternType.Horizontal;
    public QuadData.PatternOriginPosition originPos;
    public bool scanlineSpeedIsStaticOffset;
    public bool softPattern;
    public bool screenSpace;
    public QuadData.SpritePatternRotation spriteRotationMode;
    public QuadData.SpritePatternOffsetDirection spriteOffsetDirection;
    public bool affectsInterior = true;
    public bool affectsOutline;
    public bool alphaIsBlend;
    public Vector2Int colorDimensions = Vector2Int.one;
    public QuadData.ColorGridWrapMode colorWrapModeX = QuadData.ColorGridWrapMode.Clamp;
    public QuadData.ColorGridWrapMode colorWrapModeY = QuadData.ColorGridWrapMode.Clamp;
    [Range(0, 1)] public float colorPresetMix = 1f;
}
}
