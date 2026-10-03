using System;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[Serializable]
public class CutoutAnimData
{
    public Vector4 simpleCutout;
    public Vector4 sdfCutoutChamfer;
    public Vector4 sdfCutoutConcavity;
    public Vector2 sdfCutoutPosition = new(0.5f, 0.5f);
    public Vector2 sdfCutoutSize;
    public float sdfCutoutRotation;
}

[Serializable]
public class CutoutConfig
{
    public QuadData.CutoutType cutout;
    public bool[] simpleCutoutEdgeEnabled = new bool[4];
    public QuadData.SimpleCutoutRule simpleCutoutRule;
    public QuadData.SDFCutoutBehaviour sdfCutoutBehaviour;
    public bool sdfCutoutChamferNormalize = true;
    public bool sdfCutoutIsSquircle;
    public QuadData.SDFCutoutMirrorMode sdfCutoutMirror;
    public bool sdfCutoutMirrorIsDiagonal;
    public bool sdfCutoutPositionIsAbsolute;
    public bool sdfCutoutSizeIsAbsolute;
    public bool sdfCutoutUsesAnchors;
    public Vector2 sdfCutoutAnchorMin = new(0.5f, 0.5f);
    public Vector2 sdfCutoutAnchorMax = new(0.5f, 0.5f);
    public Vector2 sdfCutoutPivot = new(0.5f, 0.5f);
    public bool cutoutPositionIgnoresExpandedOutlines;
    public bool cutoutOnlyAffectsOutline;
    public bool invertCutout;
}
}
