using System;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[Serializable]
public class ProceduralProperties
{
    [SerializeReference] public OutlineAnimData outlineAnim;
    [SerializeReference] public GradientAnimData gradientAnim;
    [SerializeReference] public PatternAnimData patternAnim;
    [SerializeReference] public CutoutAnimData cutoutAnim;
    [SerializeReference] public StrokeAnimData strokeAnim;
    [SerializeReference] public SkewAnimData skewAnim;

    [NonSerialized] private OutlineAnimData defaultOutline;
    [NonSerialized] private GradientAnimData defaultGradient;
    [NonSerialized] private PatternAnimData defaultPattern;
    [NonSerialized] private CutoutAnimData defaultCutout;
    [NonSerialized] private StrokeAnimData defaultStroke;
    [NonSerialized] private SkewAnimData defaultSkew;

    private OutlineAnimData DefaultOutline => defaultOutline ??= new OutlineAnimData();
    private GradientAnimData DefaultGradient => defaultGradient ??= new GradientAnimData();
    private PatternAnimData DefaultPattern => defaultPattern ??= new PatternAnimData();
    private CutoutAnimData DefaultCutout => defaultCutout ??= new CutoutAnimData();
    private StrokeAnimData DefaultStroke => defaultStroke ??= new StrokeAnimData();
    private SkewAnimData DefaultSkew => defaultSkew ??= new SkewAnimData();

    public const int Colors2dArrayDimensionSize = 3;
    public const int Colors1dArrayLength = Colors2dArrayDimensionSize * Colors2dArrayDimensionSize;

    public enum InterpolationType
    {
        Linear,
        QuadraticEaseIn,
        QuadraticEaseOut,
        QuadraticEaseInOut,
        SineEaseIn,
        SineEaseOut,
        SineEaseInOut,
        CircularEaseIn,
        CircularEaseOut,
        CircularEaseInOut,
        QuinticEaseIn,
        QuinticEaseOut,
        QuinticEaseInOut
    }

    public InterpolationType interpolationType = InterpolationType.Linear;
    public float duration = 0.1f;
    public Vector4 uvRect = new(0, 0, 1, 1);
    public Vector2 offset;
    public Vector2 sizeModifier;
    public float softness = 1f;
    public float rotation;
    public Vector4 cornerChamfer;
    public Vector4 cornerConcavity;
    public Color[] primaryColors = new Color[Colors1dArrayLength];
    public Vector2 primaryColorOffset;
    public float primaryColorRotation;
    public Vector2 primaryColorScale = Vector2.one;
    [Range(0, 255)] public byte primaryColorFade = 255;

    public float stroke { get => (strokeAnim ?? DefaultStroke).stroke; set { if (strokeAnim != null) strokeAnim.stroke = value; } }
    public Vector4 simpleCutout { get => (cutoutAnim ?? DefaultCutout).simpleCutout; set { if (cutoutAnim != null) cutoutAnim.simpleCutout = value; } }
    public Vector4 sdfCutoutChamfer { get => (cutoutAnim ?? DefaultCutout).sdfCutoutChamfer; set { if (cutoutAnim != null) cutoutAnim.sdfCutoutChamfer = value; } }
    public Vector4 sdfCutoutConcavity { get => (cutoutAnim ?? DefaultCutout).sdfCutoutConcavity; set { if (cutoutAnim != null) cutoutAnim.sdfCutoutConcavity = value; } }
    public Vector2 sdfCutoutPosition { get => (cutoutAnim ?? DefaultCutout).sdfCutoutPosition; set { if (cutoutAnim != null) cutoutAnim.sdfCutoutPosition = value; } }
    public Vector2 sdfCutoutSize { get => (cutoutAnim ?? DefaultCutout).sdfCutoutSize; set { if (cutoutAnim != null) cutoutAnim.sdfCutoutSize = value; } }
    public float sdfCutoutRotation { get => (cutoutAnim ?? DefaultCutout).sdfCutoutRotation; set { if (cutoutAnim != null) cutoutAnim.sdfCutoutRotation = value; } }
    public float collapsedCornerChamfer { get => (skewAnim ?? DefaultSkew).collapsedCornerChamfer; set { if (skewAnim != null) skewAnim.collapsedCornerChamfer = value; } }
    public float collapsedCornerConcavity { get => (skewAnim ?? DefaultSkew).collapsedCornerConcavity; set { if (skewAnim != null) skewAnim.collapsedCornerConcavity = value; } }
    public float collapseEdgeAmount { get => (skewAnim ?? DefaultSkew).collapseEdgeAmount; set { if (skewAnim != null) skewAnim.collapseEdgeAmount = value; } }
    public float collapseEdgeAmountAbsolute { get => (skewAnim ?? DefaultSkew).collapseEdgeAmountAbsolute; set { if (skewAnim != null) skewAnim.collapseEdgeAmountAbsolute = value; } }
    public float collapseEdgePosition { get => (skewAnim ?? DefaultSkew).collapseEdgePosition; set { if (skewAnim != null) skewAnim.collapseEdgePosition = value; } }
    public Color[] outlineColors { get => (outlineAnim ?? DefaultOutline).outlineColors; set { if (outlineAnim != null) outlineAnim.outlineColors = value; } }
    public Color[] proceduralGradientColors { get => (gradientAnim ?? DefaultGradient).proceduralGradientColors; set { if (gradientAnim != null) gradientAnim.proceduralGradientColors = value; } }
    public Color[] patternColors { get => (patternAnim ?? DefaultPattern).patternColors; set { if (patternAnim != null) patternAnim.patternColors = value; } }
    public Vector2 outlineColorOffset { get => (outlineAnim ?? DefaultOutline).outlineColorOffset; set { if (outlineAnim != null) outlineAnim.outlineColorOffset = value; } }
    public Vector2 proceduralGradientColorOffset { get => (gradientAnim ?? DefaultGradient).proceduralGradientColorOffset; set { if (gradientAnim != null) gradientAnim.proceduralGradientColorOffset = value; } }
    public Vector2 patternColorOffset { get => (patternAnim ?? DefaultPattern).patternColorOffset; set { if (patternAnim != null) patternAnim.patternColorOffset = value; } }
    public float outlineColorRotation { get => (outlineAnim ?? DefaultOutline).outlineColorRotation; set { if (outlineAnim != null) outlineAnim.outlineColorRotation = value; } }
    public float proceduralGradientColorRotation { get => (gradientAnim ?? DefaultGradient).proceduralGradientColorRotation; set { if (gradientAnim != null) gradientAnim.proceduralGradientColorRotation = value; } }
    public float patternColorRotation { get => (patternAnim ?? DefaultPattern).patternColorRotation; set { if (patternAnim != null) patternAnim.patternColorRotation = value; } }
    public Vector2 outlineColorScale { get => (outlineAnim ?? DefaultOutline).outlineColorScale; set { if (outlineAnim != null) outlineAnim.outlineColorScale = value; } }
    public Vector2 proceduralGradientColorScale { get => (gradientAnim ?? DefaultGradient).proceduralGradientColorScale; set { if (gradientAnim != null) gradientAnim.proceduralGradientColorScale = value; } }
    public Vector2 patternColorScale { get => (patternAnim ?? DefaultPattern).patternColorScale; set { if (patternAnim != null) patternAnim.patternColorScale = value; } }
    public float outlineWidth { get => (outlineAnim ?? DefaultOutline).outlineWidth; set { if (outlineAnim != null) outlineAnim.outlineWidth = value; } }
    public Vector2 proceduralGradientPosition { get => (gradientAnim ?? DefaultGradient).proceduralGradientPosition; set { if (gradientAnim != null) gradientAnim.proceduralGradientPosition = value; } }
    public Vector2 radialGradientSize { get => (gradientAnim ?? DefaultGradient).radialGradientSize; set { if (gradientAnim != null) gradientAnim.radialGradientSize = value; } }
    public float radialGradientStrength { get => (gradientAnim ?? DefaultGradient).radialGradientStrength; set { if (gradientAnim != null) gradientAnim.radialGradientStrength = value; } }
    public Vector2 angleGradientStrength { get => (gradientAnim ?? DefaultGradient).angleGradientStrength; set { if (gradientAnim != null) gradientAnim.angleGradientStrength = value; } }
    public float proceduralGradientAngle { get => (gradientAnim ?? DefaultGradient).proceduralGradientAngle; set { if (gradientAnim != null) gradientAnim.proceduralGradientAngle = value; } }
    public float sdfGradientInnerDistance { get => (gradientAnim ?? DefaultGradient).sdfGradientInnerDistance; set { if (gradientAnim != null) gradientAnim.sdfGradientInnerDistance = value; } }
    public float sdfGradientOuterDistance { get => (gradientAnim ?? DefaultGradient).sdfGradientOuterDistance; set { if (gradientAnim != null) gradientAnim.sdfGradientOuterDistance = value; } }
    public float sdfGradientInnerReach { get => (gradientAnim ?? DefaultGradient).sdfGradientInnerReach; set { if (gradientAnim != null) gradientAnim.sdfGradientInnerReach = value; } }
    public float sdfGradientOuterReach { get => (gradientAnim ?? DefaultGradient).sdfGradientOuterReach; set { if (gradientAnim != null) gradientAnim.sdfGradientOuterReach = value; } }
    public float proceduralGradientPointerStrength { get => (gradientAnim ?? DefaultGradient).proceduralGradientPointerStrength; set { if (gradientAnim != null) gradientAnim.proceduralGradientPointerStrength = value; } }
    public float conicalGradientCurvature { get => (gradientAnim ?? DefaultGradient).conicalGradientCurvature; set { if (gradientAnim != null) gradientAnim.conicalGradientCurvature = value; } }
    public float conicalGradientTailStrength { get => (gradientAnim ?? DefaultGradient).conicalGradientTailStrength; set { if (gradientAnim != null) gradientAnim.conicalGradientTailStrength = value; } }
    public uint noiseSeed { get => (gradientAnim ?? DefaultGradient).noiseSeed; set { if (gradientAnim != null) gradientAnim.noiseSeed = value; } }
    public float noiseScale { get => (gradientAnim ?? DefaultGradient).noiseScale; set { if (gradientAnim != null) gradientAnim.noiseScale = value; } }
    public float noiseEdge { get => (gradientAnim ?? DefaultGradient).noiseEdge; set { if (gradientAnim != null) gradientAnim.noiseEdge = value; } }
    public float noiseStrength { get => (gradientAnim ?? DefaultGradient).noiseStrength; set { if (gradientAnim != null) gradientAnim.noiseStrength = value; } }
    public float patternDensity { get => (patternAnim ?? DefaultPattern).patternDensity; set { if (patternAnim != null) patternAnim.patternDensity = value; } }
    public float patternSpeed { get => (patternAnim ?? DefaultPattern).patternSpeed; set { if (patternAnim != null) patternAnim.patternSpeed = value; } }
    public float patternCellParam { get => (patternAnim ?? DefaultPattern).patternCellParam; set { if (patternAnim != null) patternAnim.patternCellParam = value; } }
    public byte patternLineThickness { get => (patternAnim ?? DefaultPattern).patternLineThickness; set { if (patternAnim != null) patternAnim.patternLineThickness = value; } }
    public int patternSpriteRotation { get => (patternAnim ?? DefaultPattern).patternSpriteRotation; set { if (patternAnim != null) patternAnim.patternSpriteRotation = value; } }

    public ProceduralProperties() {}
    public ProceduralProperties(ProceduralProperties other) => Copy(other);

    public void Copy(ProceduralProperties other)
    {
        interpolationType = other.interpolationType;
        duration = other.duration;
        uvRect = other.uvRect;
        offset = other.offset;
        sizeModifier = other.sizeModifier;
        softness = other.softness;
        rotation = other.rotation;
        cornerChamfer = other.cornerChamfer;
        cornerConcavity = other.cornerConcavity;
        primaryColorOffset = other.primaryColorOffset;
        primaryColorRotation = other.primaryColorRotation;
        primaryColorScale = other.primaryColorScale;
        primaryColorFade = other.primaryColorFade;

        if (primaryColors.Length != Colors1dArrayLength)
            primaryColors = new Color[Colors1dArrayLength];
        if (other.primaryColors.Length != Colors1dArrayLength)
        {
            var oldLength = other.primaryColors.Length;
            Array.Resize(ref other.primaryColors, Colors1dArrayLength);
            for (int i = oldLength; i < Colors1dArrayLength; i++)
                other.primaryColors[i] = Color.white;
        }
        Array.Copy(other.primaryColors, primaryColors, Colors1dArrayLength);

        if (other.outlineAnim == null)
            outlineAnim = null;
        else
        {
            outlineAnim = new OutlineAnimData
            {
                outlineWidth = other.outlineAnim.outlineWidth,
                outlineColorOffset = other.outlineAnim.outlineColorOffset,
                outlineColorRotation = other.outlineAnim.outlineColorRotation,
                outlineColorScale = other.outlineAnim.outlineColorScale
            };
            Array.Copy(other.outlineAnim.outlineColors, outlineAnim.outlineColors, Colors1dArrayLength);
        }

        if (other.gradientAnim == null)
            gradientAnim = null;
        else
        {
            gradientAnim = new GradientAnimData
            {
                proceduralGradientColorOffset = other.gradientAnim.proceduralGradientColorOffset,
                proceduralGradientColorRotation = other.gradientAnim.proceduralGradientColorRotation,
                proceduralGradientColorScale = other.gradientAnim.proceduralGradientColorScale,
                proceduralGradientPosition = other.gradientAnim.proceduralGradientPosition,
                radialGradientSize = other.gradientAnim.radialGradientSize,
                radialGradientStrength = other.gradientAnim.radialGradientStrength,
                angleGradientStrength = other.gradientAnim.angleGradientStrength,
                proceduralGradientAngle = other.gradientAnim.proceduralGradientAngle,
                sdfGradientInnerDistance = other.gradientAnim.sdfGradientInnerDistance,
                sdfGradientOuterDistance = other.gradientAnim.sdfGradientOuterDistance,
                sdfGradientInnerReach = other.gradientAnim.sdfGradientInnerReach,
                sdfGradientOuterReach = other.gradientAnim.sdfGradientOuterReach,
                proceduralGradientPointerStrength = other.gradientAnim.proceduralGradientPointerStrength,
                conicalGradientCurvature = other.gradientAnim.conicalGradientCurvature,
                conicalGradientTailStrength = other.gradientAnim.conicalGradientTailStrength,
                noiseSeed = other.gradientAnim.noiseSeed,
                noiseScale = other.gradientAnim.noiseScale,
                noiseEdge = other.gradientAnim.noiseEdge,
                noiseStrength = other.gradientAnim.noiseStrength
            };
            Array.Copy(other.gradientAnim.proceduralGradientColors, gradientAnim.proceduralGradientColors, Colors1dArrayLength);
        }

        if (other.patternAnim == null)
            patternAnim = null;
        else
        {
            patternAnim = new PatternAnimData
            {
                patternColorOffset = other.patternAnim.patternColorOffset,
                patternColorRotation = other.patternAnim.patternColorRotation,
                patternColorScale = other.patternAnim.patternColorScale,
                patternDensity = other.patternAnim.patternDensity,
                patternSpeed = other.patternAnim.patternSpeed,
                patternCellParam = other.patternAnim.patternCellParam,
                patternLineThickness = other.patternAnim.patternLineThickness,
                patternSpriteRotation = other.patternAnim.patternSpriteRotation
            };
            Array.Copy(other.patternAnim.patternColors, patternAnim.patternColors, Colors1dArrayLength);
        }

        cutoutAnim = other.cutoutAnim == null ? null : new CutoutAnimData
        {
            simpleCutout = other.cutoutAnim.simpleCutout,
            sdfCutoutChamfer = other.cutoutAnim.sdfCutoutChamfer,
            sdfCutoutConcavity = other.cutoutAnim.sdfCutoutConcavity,
            sdfCutoutPosition = other.cutoutAnim.sdfCutoutPosition,
            sdfCutoutSize = other.cutoutAnim.sdfCutoutSize,
            sdfCutoutRotation = other.cutoutAnim.sdfCutoutRotation
        };
        strokeAnim = other.strokeAnim == null ? null : new StrokeAnimData { stroke = other.strokeAnim.stroke };
        skewAnim = other.skewAnim == null ? null : new SkewAnimData
        {
            collapseEdgeAmount = other.skewAnim.collapseEdgeAmount,
            collapseEdgeAmountAbsolute = other.skewAnim.collapseEdgeAmountAbsolute,
            collapseEdgePosition = other.skewAnim.collapseEdgePosition,
            collapsedCornerChamfer = other.skewAnim.collapsedCornerChamfer,
            collapsedCornerConcavity = other.skewAnim.collapsedCornerConcavity
        };
    }

    public bool ValuesEqual(ProceduralProperties other)
    {
        if (other == null) return false;
        if (interpolationType != other.interpolationType) return false;
        if (uvRect != other.uvRect) return false;
        if (!Mathf.Approximately(duration, other.duration)) return false;
        if (offset != other.offset) return false;
        if (sizeModifier != other.sizeModifier) return false;
        if (!Mathf.Approximately(softness, other.softness)) return false;
        if (!Mathf.Approximately(rotation, other.rotation)) return false;
        if (cornerChamfer != other.cornerChamfer) return false;
        if (cornerConcavity != other.cornerConcavity) return false;
        if (primaryColorOffset != other.primaryColorOffset) return false;
        if (!Mathf.Approximately(primaryColorRotation, other.primaryColorRotation)) return false;
        if (primaryColorScale != other.primaryColorScale) return false;
        if (primaryColorFade != other.primaryColorFade) return false;
        if (primaryColors.Length != other.primaryColors.Length) return false;
        for (int i = 0; i < Colors1dArrayLength; i++)
            if (primaryColors[i] != other.primaryColors[i]) return false;

        if ((outlineAnim == null) != (other.outlineAnim == null)) return false;
        if (outlineAnim != null)
        {
            if (!Mathf.Approximately(outlineAnim.outlineWidth, other.outlineAnim.outlineWidth)) return false;
            if (outlineAnim.outlineColorOffset != other.outlineAnim.outlineColorOffset) return false;
            if (!Mathf.Approximately(outlineAnim.outlineColorRotation, other.outlineAnim.outlineColorRotation)) return false;
            if (outlineAnim.outlineColorScale != other.outlineAnim.outlineColorScale) return false;
            for (int i = 0; i < Colors1dArrayLength; i++)
                if (outlineAnim.outlineColors[i] != other.outlineAnim.outlineColors[i]) return false;
        }

        if ((gradientAnim == null) != (other.gradientAnim == null)) return false;
        if (gradientAnim != null)
        {
            if (gradientAnim.proceduralGradientColorOffset != other.gradientAnim.proceduralGradientColorOffset) return false;
            if (!Mathf.Approximately(gradientAnim.proceduralGradientColorRotation, other.gradientAnim.proceduralGradientColorRotation)) return false;
            if (gradientAnim.proceduralGradientColorScale != other.gradientAnim.proceduralGradientColorScale) return false;
            if (gradientAnim.proceduralGradientPosition != other.gradientAnim.proceduralGradientPosition) return false;
            if (gradientAnim.radialGradientSize != other.gradientAnim.radialGradientSize) return false;
            if (!Mathf.Approximately(gradientAnim.radialGradientStrength, other.gradientAnim.radialGradientStrength)) return false;
            if (gradientAnim.angleGradientStrength != other.gradientAnim.angleGradientStrength) return false;
            if (!Mathf.Approximately(gradientAnim.proceduralGradientAngle, other.gradientAnim.proceduralGradientAngle)) return false;
            if (!Mathf.Approximately(gradientAnim.sdfGradientInnerDistance, other.gradientAnim.sdfGradientInnerDistance)) return false;
            if (!Mathf.Approximately(gradientAnim.sdfGradientOuterDistance, other.gradientAnim.sdfGradientOuterDistance)) return false;
            if (!Mathf.Approximately(gradientAnim.sdfGradientInnerReach, other.gradientAnim.sdfGradientInnerReach)) return false;
            if (!Mathf.Approximately(gradientAnim.sdfGradientOuterReach, other.gradientAnim.sdfGradientOuterReach)) return false;
            if (!Mathf.Approximately(gradientAnim.proceduralGradientPointerStrength, other.gradientAnim.proceduralGradientPointerStrength)) return false;
            if (!Mathf.Approximately(gradientAnim.conicalGradientCurvature, other.gradientAnim.conicalGradientCurvature)) return false;
            if (!Mathf.Approximately(gradientAnim.conicalGradientTailStrength, other.gradientAnim.conicalGradientTailStrength)) return false;
            if (gradientAnim.noiseSeed != other.gradientAnim.noiseSeed) return false;
            if (!Mathf.Approximately(gradientAnim.noiseScale, other.gradientAnim.noiseScale)) return false;
            if (!Mathf.Approximately(gradientAnim.noiseEdge, other.gradientAnim.noiseEdge)) return false;
            if (!Mathf.Approximately(gradientAnim.noiseStrength, other.gradientAnim.noiseStrength)) return false;
            for (int i = 0; i < Colors1dArrayLength; i++)
                if (gradientAnim.proceduralGradientColors[i] != other.gradientAnim.proceduralGradientColors[i]) return false;
        }

        if ((patternAnim == null) != (other.patternAnim == null)) return false;
        if (patternAnim != null)
        {
            if (patternAnim.patternColorOffset != other.patternAnim.patternColorOffset) return false;
            if (!Mathf.Approximately(patternAnim.patternColorRotation, other.patternAnim.patternColorRotation)) return false;
            if (patternAnim.patternColorScale != other.patternAnim.patternColorScale) return false;
            if (!Mathf.Approximately(patternAnim.patternDensity, other.patternAnim.patternDensity)) return false;
            if (!Mathf.Approximately(patternAnim.patternSpeed, other.patternAnim.patternSpeed)) return false;
            if (!Mathf.Approximately(patternAnim.patternCellParam, other.patternAnim.patternCellParam)) return false;
            if (patternAnim.patternLineThickness != other.patternAnim.patternLineThickness) return false;
            if (patternAnim.patternSpriteRotation != other.patternAnim.patternSpriteRotation) return false;
            for (int i = 0; i < Colors1dArrayLength; i++)
                if (patternAnim.patternColors[i] != other.patternAnim.patternColors[i]) return false;
        }

        if ((cutoutAnim == null) != (other.cutoutAnim == null)) return false;
        if (cutoutAnim != null)
        {
            if (cutoutAnim.simpleCutout != other.cutoutAnim.simpleCutout) return false;
            if (cutoutAnim.sdfCutoutChamfer != other.cutoutAnim.sdfCutoutChamfer) return false;
            if (cutoutAnim.sdfCutoutConcavity != other.cutoutAnim.sdfCutoutConcavity) return false;
            if (cutoutAnim.sdfCutoutPosition != other.cutoutAnim.sdfCutoutPosition) return false;
            if (cutoutAnim.sdfCutoutSize != other.cutoutAnim.sdfCutoutSize) return false;
            if (!Mathf.Approximately(cutoutAnim.sdfCutoutRotation, other.cutoutAnim.sdfCutoutRotation)) return false;
        }

        if ((strokeAnim == null) != (other.strokeAnim == null)) return false;
        if (strokeAnim != null && !Mathf.Approximately(strokeAnim.stroke, other.strokeAnim.stroke)) return false;
        if ((skewAnim == null) != (other.skewAnim == null)) return false;
        if (skewAnim != null)
        {
            if (!Mathf.Approximately(skewAnim.collapseEdgeAmount, other.skewAnim.collapseEdgeAmount)) return false;
            if (!Mathf.Approximately(skewAnim.collapseEdgeAmountAbsolute, other.skewAnim.collapseEdgeAmountAbsolute)) return false;
            if (!Mathf.Approximately(skewAnim.collapseEdgePosition, other.skewAnim.collapseEdgePosition)) return false;
            if (!Mathf.Approximately(skewAnim.collapsedCornerChamfer, other.skewAnim.collapsedCornerChamfer)) return false;
            if (!Mathf.Approximately(skewAnim.collapsedCornerConcavity, other.skewAnim.collapsedCornerConcavity)) return false;
        }
        return true;
    }

    public void SetDefaultColors()
    {
        for (int i = 0; i < Colors1dArrayLength; i++)
            primaryColors[i] = Color.white;
    }

    // Spiral indexing allows changing Colors2dArrayDimensionSize non-destructively. Ideally, we'd just use a 2D array, but those cannot be serialized by Unity.
    public static int GetColorSpiralIndex(int x, int y)
    {
        var k = Mathf.Max(x, y);
        var indexOffset = x == k ? y : k + 1 + (k - 1 - x);
        return k * k + indexOffset;
    }

    public Color GetPrimaryColor() => primaryColors[0];
    public Color GetProceduralGradientColor() => (gradientAnim ?? DefaultGradient).proceduralGradientColors[0];
    public Color GetOutlineColor() => (outlineAnim ?? DefaultOutline).outlineColors[0];
    public Color GetPrimaryColorAtCell(int indexX, int indexY) => GetColor(primaryColors, indexX, indexY);
    public Color GetOutlineColorAtCell(int indexX, int indexY) => GetColor(outlineAnim?.outlineColors, indexX, indexY);
    public Color GetProceduralGradientColorAtCell(int indexX, int indexY) => GetColor(gradientAnim?.proceduralGradientColors, indexX, indexY);
    public Color GetPatternColorAtCell(int indexX, int indexY) => GetColor(patternAnim?.patternColors, indexX, indexY);

    public Color GetColor(Color[] colorsArray, int indexX, int indexY)
    {
        if (colorsArray == null) return Color.clear;
        var index = GetColorSpiralIndex(indexX, indexY);
        if (index >= 0 && index < colorsArray.Length)
            return colorsArray[index];

        Debug.LogWarning($"Tried to get color at index [{indexX}, {indexY}] but index was out of bounds.");
        return Color.clear;
    }

    public void SetPrimaryColor(Color color) => primaryColors[0] = color;
    public void SetProceduralGradientColor(Color color) { if (gradientAnim != null) gradientAnim.proceduralGradientColors[0] = color; }
    public void SetOutlineColor(Color color) { if (outlineAnim != null) outlineAnim.outlineColors[0] = color; }
    public bool SetPrimaryColorAtCell(int indexX, int indexY, Color color) => SetColor(primaryColors, indexX, indexY, color);
    public bool SetOutlineColorAtCell(int indexX, int indexY, Color color) => SetColor(outlineAnim?.outlineColors, indexX, indexY, color);
    public bool SetProceduralGradientColorAtCell(int indexX, int indexY, Color color) => SetColor(gradientAnim?.proceduralGradientColors, indexX, indexY, color);
    public bool SetPatternColorAtCell(int indexX, int indexY, Color color) => SetColor(patternAnim?.patternColors, indexX, indexY, color);

    public bool SetColor(Color[] colorsArray, int indexX, int indexY, Color color)
    {
        if (colorsArray == null) return false;
        var index = GetColorSpiralIndex(indexX, indexY);
        if (index >= 0 && index < colorsArray.Length)
        {
            colorsArray[index] = color;
            return true;
        }
        Debug.LogWarning($"Tried to set color at index [{indexX}, {indexY}] but index was out of bounds.");
        return false;
    }
}
}
