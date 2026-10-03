using System;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[Serializable]
public class OutlineAnimData
{
    [Range(0, 511.875f)] public float outlineWidth;
    public Color[] outlineColors = new Color[ProceduralProperties.Colors1dArrayLength];
    public Vector2 outlineColorOffset;
    public float outlineColorRotation;
    public Vector2 outlineColorScale = Vector2.one;

    public OutlineAnimData()
    {
        for (int i = 0; i < outlineColors.Length; i++)
            outlineColors[i] = Color.black;
    }
}

[Serializable]
public class OutlineConfig
{
    public bool expandsOutward;
    public bool accommodatesCollapsedEdge;
    public bool fadeTowardsPerimeter;
    public bool adjustsChamfer;
    public bool addInteriorOutline;
    public bool alphaIsBlend;
    public Vector2Int colorDimensions = Vector2Int.one;
    public QuadData.ColorGridWrapMode colorWrapModeX = QuadData.ColorGridWrapMode.Clamp;
    public QuadData.ColorGridWrapMode colorWrapModeY = QuadData.ColorGridWrapMode.Clamp;
    [Range(0, 1)] public float colorPresetMix = 1f;
}
}
