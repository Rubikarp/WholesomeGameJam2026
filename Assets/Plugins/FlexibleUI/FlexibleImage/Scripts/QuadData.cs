using System;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{ 
[Serializable]
[ExecuteAlways]
public class QuadData : ISerializationCallbackReceiver
{
    [Flags] public enum QuadModifiers { DisableSprite = 1, ForceSimpleMesh = 2}
    public enum Topology { Original, Flipped, X }
    public enum ColorGridWrapMode : byte { Clamp, Repeat, Mirror, PingPong}
    public enum FeatherMode { Inwards, Outwards, Bidirectional }
    public enum StrokeOriginLocation { Center, Perimeter, Outline }
    public enum GradientType { SDF, Angle, Radial, Conical, Noise }
    public enum CutoutType { Simple, SDF }
    public enum SimpleCutoutRule { OR, AND }
    public enum SDFCutoutBehaviour { MinShape, OutlineAndInterior, OutlineOnly }
    public enum SDFCutoutMirrorMode { None, Horizontal, Vertical, Both }

    public enum PatternOriginPosition { Center, Left, Right, Top, Bottom }
    public enum SpritePatternRotation { Sprite, Offset }
    public enum SpritePatternOffsetDirection { Zero, FortyFive, Ninety, OneThirtyFive }
    public enum CollapsedEdgeType { Top, Bottom, Left, Right }

    public enum PatternType
    {
        BottomRightToTopLeft = 0, Vertical = 1, BottomLeftToTopRight = 2, Horizontal = 3, DiamondShape = 4, CircleShape = 9,
        SquareShape = 14, CrossShape = 19, Fractal = 25, Sprite = 26, StraightGrid = 28, DiagonalGrid = 29, SquareGrid = 30, DiamondGrid = 31
    }

    public enum CutoutFillOrigin
    {
        Left, Right, Top, Bottom, HorizontalFromCenter, HorizontalFromPerimeter, VerticalFromCenter, VerticalFromPerimeter, 
        BothFromCenter, BothFromPerimeter, BothFromCenterCross, BothFromPerimeterCross, TopLeft, TopRight, BottomLeft, BottomRight
    }

#if UNITY_EDITOR
    public static readonly string EnabledFieldName = nameof(_enabled);
    public static readonly string AdvancedQuadSettingsFieldName = nameof(_advancedQuadSettings);
    public static readonly string ColorPresetFieldName = nameof(_colorPreset);
    public static readonly string PrimaryColorWrapModeXFieldName = nameof(_primaryColorWrapModeX);
    public static readonly string PrimaryColorWrapModeYFieldName = nameof(_primaryColorWrapModeY);
    public static readonly string OutlineColorWrapModeXFieldName = nameof(OutlineConfig.colorWrapModeX);
    public static readonly string OutlineColorWrapModeYFieldName = nameof(OutlineConfig.colorWrapModeY);
    public static readonly string ProceduralGradientColorWrapModeXFieldName = nameof(GradientConfig.colorWrapModeX);
    public static readonly string ProceduralGradientColorWrapModeYFieldName = nameof(GradientConfig.colorWrapModeY);
    public static readonly string PatternColorWrapModeXFieldName = nameof(PatternConfig.colorWrapModeX);
    public static readonly string PatternColorWrapModeYFieldName = nameof(PatternConfig.colorWrapModeY);
    public static readonly string PrimaryColorPresetMixFieldName = nameof(_primaryColorPresetMix);
    public static readonly string ProceduralGradientColorPresetMixFieldName = nameof(GradientConfig.colorPresetMix);
    public static readonly string PatternColorPresetMixFieldName = nameof(PatternConfig.colorPresetMix);
    public static readonly string ProceduralGradientAspectCorrectionFieldName = nameof(GradientConfig.aspectCorrection);
    public static readonly string OutlineColorPresetMixFieldName = nameof(OutlineConfig.colorPresetMix);
    public static readonly string NormalizeChamferFieldName = nameof(_normalizeChamfer);
    public static readonly string SdfCutoutChamferNormalizeFieldName = nameof(CutoutConfig.sdfCutoutChamferNormalize);
    public static readonly string SdfCutoutIsSquircleFieldName = nameof(CutoutConfig.sdfCutoutIsSquircle);
    public static readonly string SdfCutoutMirrorFieldName = nameof(CutoutConfig.sdfCutoutMirror);
    public static readonly string SdfCutoutMirrorIsDiagonalFieldName = nameof(CutoutConfig.sdfCutoutMirrorIsDiagonal);
    public static readonly string SdfCutoutPositionIsAbsoluteFieldName = nameof(CutoutConfig.sdfCutoutPositionIsAbsolute);
    public static readonly string SdfCutoutSizeIsAbsoluteFieldName = nameof(CutoutConfig.sdfCutoutSizeIsAbsolute);
    public static readonly string SdfCutoutUsesAnchorsFieldName = nameof(CutoutConfig.sdfCutoutUsesAnchors);
    public static readonly string SdfCutoutAnchorMinFieldName = nameof(CutoutConfig.sdfCutoutAnchorMin);
    public static readonly string SdfCutoutAnchorMaxFieldName = nameof(CutoutConfig.sdfCutoutAnchorMax);
    public static readonly string SdfCutoutPivotFieldName = nameof(CutoutConfig.sdfCutoutPivot);
    public static readonly string CutoutPositionIgnoresExpandedOutlinesFieldName = nameof(CutoutConfig.cutoutPositionIgnoresExpandedOutlines);
    public static readonly string ConcavityIsSmoothingFieldName = nameof(_concavityIsSmoothing);
    public static readonly string CollapsedEdgeFieldName = nameof(SkewConfig.collapsedEdge);
    public static readonly string CollapseIntoParallelogramFieldName = nameof(SkewConfig.collapseIntoParallelogram);
    public static readonly string MirrorCollapseFieldName = nameof(SkewConfig.mirrorCollapse);
    public static readonly string EdgeCollapseAmountIsAbsoluteFieldName = nameof(SkewConfig.edgeCollapseAmountIsAbsolute);
    public static readonly string FitRotationWithinBoundsFieldName = nameof(_fitRotatedImageWithinBounds);
    public static readonly string OutlineFadeTowardsPerimeterFieldName = nameof(OutlineConfig.fadeTowardsPerimeter);
    public static readonly string OutlineAdjustsChamferFieldName = nameof(OutlineConfig.adjustsChamfer);
    public static readonly string ProceduralGradientTypeFieldName = nameof(GradientConfig.gradientType);
    public static readonly string ProceduralGradientAffectsInteriorFieldName = nameof(GradientConfig.affectsInterior);
    public static readonly string ProceduralGradientAffectsOutlineFieldName = nameof(GradientConfig.affectsOutline);
    public static readonly string PatternAffectsInteriorFieldName = nameof(PatternConfig.affectsInterior);
    public static readonly string PatternAffectsOutlineFieldName = nameof(PatternConfig.affectsOutline);
    public static readonly string ProceduralGradientPositionFromPointerFieldName = nameof(GradientConfig.positionFromPointer);
    public static readonly string NoiseGradientAlternateModeFieldName = nameof(GradientConfig.noiseAlternateMode);
    public static readonly string ScreenSpaceProceduralGradientFieldName = nameof(GradientConfig.screenSpace);
    public static readonly string ScreenSpacePatternFieldName = nameof(PatternConfig.screenSpace);
    public static readonly string SoftPatternFieldName = nameof(PatternConfig.softPattern);
    public static readonly string SpritePatternRotationModeFieldName = nameof(PatternConfig.spriteRotationMode);
    public static readonly string SpritePatternOffsetDirectionDegreesFieldName = nameof(PatternConfig.spriteOffsetDirection);
    public static readonly string PatternFieldName = nameof(PatternConfig.patternType);
    public static readonly string ScanlinePatternSpeedIsStaticOffsetFieldName = nameof(PatternConfig.scanlineSpeedIsStaticOffset);
    public static readonly string PatternOriginPosFieldName = nameof(PatternConfig.originPos);
    public static readonly string CutoutTypeFieldName = nameof(CutoutConfig.cutout);
    public static readonly string CutoutRuleFieldName = nameof(CutoutConfig.simpleCutoutRule);
    public static readonly string CutoutBehaviourFieldName = nameof(CutoutConfig.sdfCutoutBehaviour);
    public static readonly string CutoutEnabledFieldName = nameof(CutoutConfig.simpleCutoutEdgeEnabled);
    public static readonly string CutoutOnlyAffectsOutlineFieldName = nameof(CutoutConfig.cutoutOnlyAffectsOutline);
    public static readonly string InvertCutoutFieldName = nameof(CutoutConfig.invertCutout);
    public static readonly string PrimaryColorDimensionsFieldName = nameof(_primaryColorDimensions);
    public static readonly string OutlineColorDimensionsFieldName = nameof(OutlineConfig.colorDimensions);
    public static readonly string ProceduralGradientColorDimensionsFieldName = nameof(GradientConfig.colorDimensions);
    public static readonly string PatternColorDimensionsFieldName = nameof(PatternConfig.colorDimensions);
    public static readonly string ProceduralGradientAlphaIsBlendFieldName = nameof(GradientConfig.alphaIsBlend);
    public static readonly string PatternColorAlphaIsBlendFieldName = nameof(PatternConfig.alphaIsBlend);
    public static readonly string OutlineAlphaIsBlendFieldName = nameof(OutlineConfig.alphaIsBlend);
    public static readonly string AddInteriorOutlineFieldName = nameof(OutlineConfig.addInteriorOutline);
    public static readonly string OutlineExpandsOutwardsFieldName = nameof(OutlineConfig.expandsOutward);
    public static readonly string OutlineAccommodatesCollapsedEdgeFieldName = nameof(OutlineConfig.accommodatesCollapsedEdge);
    public static readonly string MeshSubdivisionsFieldName = nameof(_meshSubdivisions);
    public static readonly string MeshTopologyFieldName = nameof(_meshTopology);
    public static readonly string SizeModifierAspectCorrectionFieldName = nameof(_sizeModifierAspectCorrection);
    public static readonly string ProceduralGradientInvertFieldName = nameof(GradientConfig.invert);
    public static readonly string SoftnessFeatherModeFieldName = nameof(_softnessFeatherMode);
    public static readonly string StrokeOriginFieldName = nameof(StrokeConfig.strokeOrigin);
    public static readonly string OutlineConfigFieldName = nameof(_outlineConfig);
    public static readonly string GradientConfigFieldName = nameof(_gradientConfig);
    public static readonly string PatternConfigFieldName = nameof(_patternConfig);
    public static readonly string CutoutConfigFieldName = nameof(_cutoutConfig);
    public static readonly string StrokeConfigFieldName = nameof(_strokeConfig);
    public static readonly string SkewConfigFieldName = nameof(_skewConfig);
    public static readonly string AnchorMinFieldName = nameof(_anchorMin);
    public static readonly string AnchorMaxFieldName = nameof(_anchorMax);
    public static readonly string AnchoredPositionFieldName = nameof(_anchoredPosition);
    public static readonly string SizeDeltaFieldName = nameof(_sizeDelta);
    public static readonly string PivotFieldName = nameof(_pivot);

    public void PreviewInEditor(AnimationValues animationValues, int prevState, int prevSubstate, int state, int substate, float percentageDone)
    {
        var prev = proceduralAnimationStates[prevState].proceduralProperties[prevSubstate];
        var next = proceduralAnimationStates[state].proceduralProperties[substate];
        animationValues.SetCurrentProps(new ProceduralProperties(), false);
        ProceduralAnimationState.LerpProperties(animationValues.CurrentProperties, prev, next, percentageDone);
    }

    // Only used for previewing in the editor, and specifically used for undo.
    public int editorSelectedAnimationState, editorSelectedAnimationSubState;
#endif

    [SerializeReference] private OutlineConfig _outlineConfig;
    [SerializeReference] private GradientConfig _gradientConfig;
    [SerializeReference] private PatternConfig _patternConfig;
    [SerializeReference] private CutoutConfig _cutoutConfig;
    [SerializeReference] private StrokeConfig _strokeConfig;
    [SerializeReference] private SkewConfig _skewConfig;

    public bool HasOutline => _outlineConfig != null;
    public bool HasGradient => _gradientConfig != null;
    public bool HasPattern => _patternConfig != null;
    public bool HasCutout => _cutoutConfig != null;
    public bool HasStroke => _strokeConfig != null;
    public bool HasSkew => _skewConfig != null;

    public string name;

    [SerializeField] private Vector2 _anchorMin;
    [SerializeField] private Vector2 _anchorMax = Vector2.one;
    [SerializeField] private Vector2 _anchoredPosition;
    [SerializeField] private Vector2 _sizeDelta;
    [SerializeField] private Vector2 _pivot = new (0.5f, 0.5f);

    public Vector2 AnchorMin
    {
        get => _anchorMin;
        set => _anchorMin = value;
    }
    public Vector2 AnchorMax
    {
        get => _anchorMax;
        set => _anchorMax = value;
    }
    public Vector2 AnchoredPosition
    {
        get => _anchoredPosition;
        set => _anchoredPosition = value;
    }
    public Vector2 SizeDelta
    {
        get => _sizeDelta;
        set => _sizeDelta = value;
    }
    public Vector2 Pivot
    {
        get => _pivot;
        set => _pivot = value;
    }

    public Vector2 GetQuadSizeAdjustment(in RectTransform rectTransform)
    {
        Vector2 parentSize = rectTransform.rect.size;
        Vector2 anchorDiff = _anchorMax - _anchorMin;
        return Vector2.Scale(parentSize, anchorDiff - Vector2.one) + _sizeDelta;
    }

    public Vector2 GetQuadPositionAdjustment(in RectTransform rectTransform)
    {
        Vector2 parentSize = rectTransform.rect.size;
        Vector2 anchorMinPos = Vector2.Scale(_anchorMin, parentSize);
        Vector2 anchorMaxPos = Vector2.Scale(_anchorMax, parentSize);
        Vector2 anchorCenter = Vector2.Lerp(anchorMinPos, anchorMaxPos, 0.5f);
        Vector2 pivotPos = anchorCenter + _anchoredPosition;
        Vector2 initialCenter = parentSize * 0.5f;
        return pivotPos + (Vector2.one * 0.5f - _pivot) * _sizeDelta - initialCenter;
    }

    public bool highlightedFix = true;
    public ProceduralAnimationState[] proceduralAnimationStates = { new(), new(), new(), new(), new() };
    public ProceduralProperties DefaultProceduralProps => proceduralAnimationStates[0].proceduralProperties[0];

    [SerializeField] private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            SetVerticesDirty();
        }
    }

    [SerializeField] private QuadModifiers _advancedQuadSettings;
    public QuadModifiers AdvancedQuadSettings
    {
        get => _advancedQuadSettings;
        set
        {
            _advancedQuadSettings = value;
            SetVerticesDirty();
        }
    }

    private bool colorPresetCallbackAssigned;
    [SerializeField] private ColorPreset _colorPreset;
    public ColorPreset ColorPreset
    {
        get => _colorPreset;
        set
        {
            if (_colorPreset && colorPresetCallbackAssigned)
            {
                _colorPreset.ColorChangeEvent -= SetVerticesDirty;
                colorPresetCallbackAssigned = false;
            }
            _colorPreset = value;
            if (_colorPreset)
            {
                _colorPreset.ColorChangeEvent += SetVerticesDirty;
                colorPresetCallbackAssigned = true;
            }
            SetVerticesDirty();
        }
    }
    
    [SerializeField] private ColorGridWrapMode _primaryColorWrapModeX = ColorGridWrapMode.Clamp;
    public ColorGridWrapMode PrimaryColorWrapModeX
    {
        get => _primaryColorWrapModeX;
        set
        {
            _primaryColorWrapModeX = value;
            SetVerticesDirty();
        }
    }
    
    [SerializeField] private ColorGridWrapMode _primaryColorWrapModeY = ColorGridWrapMode.Clamp;
    public ColorGridWrapMode PrimaryColorWrapModeY
    {
        get => _primaryColorWrapModeY;
        set
        {
            _primaryColorWrapModeY = value;
            SetVerticesDirty();
        }
    }

    public ColorGridWrapMode OutlineColorWrapModeX
    {
        get => _outlineConfig?.colorWrapModeX ?? ColorGridWrapMode.Clamp;
        set
        {
            if (_outlineConfig == null) return;
            _outlineConfig.colorWrapModeX = value;
            SetVerticesDirty();
        }
    }

    public ColorGridWrapMode OutlineColorWrapModeY
    {
        get => _outlineConfig?.colorWrapModeY ?? ColorGridWrapMode.Clamp;
        set
        {
            if (_outlineConfig == null) return;
            _outlineConfig.colorWrapModeY = value;
            SetVerticesDirty();
        }
    }

    public ColorGridWrapMode ProceduralGradientColorWrapModeX
    {
        get => _gradientConfig?.colorWrapModeX ?? ColorGridWrapMode.Clamp;
        set
        {
            if (_gradientConfig == null) return;
            _gradientConfig.colorWrapModeX = value;
            SetVerticesDirty();
        }
    }

    public ColorGridWrapMode ProceduralGradientColorWrapModeY
    {
        get => _gradientConfig?.colorWrapModeY ?? ColorGridWrapMode.Clamp;
        set
        {
            if (_gradientConfig == null) return;
            _gradientConfig.colorWrapModeY = value;
            SetVerticesDirty();
        }
    }

    public ColorGridWrapMode PatternColorWrapModeX
    {
        get => _patternConfig?.colorWrapModeX ?? ColorGridWrapMode.Clamp;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.colorWrapModeX = value;
            SetVerticesDirty();
        }
    }

    public ColorGridWrapMode PatternColorWrapModeY
    {
        get => _patternConfig?.colorWrapModeY ?? ColorGridWrapMode.Clamp;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.colorWrapModeY = value;
            SetVerticesDirty();
        }
    }

    public Vector4 UVRect
    {
        get => DefaultProceduralProps.uvRect;
        set
        {
            DefaultProceduralProps.uvRect = value;
            SetVerticesDirty();
        }
    }
    
    public byte PrimaryColorFade
    {
        get => DefaultProceduralProps.primaryColorFade;
        set
        {
            DefaultProceduralProps.primaryColorFade = value;
            SetVerticesDirty();
        }
    }

    public Vector2 RawSizeModifier
    {
        get => DefaultProceduralProps.sizeModifier;
        set
        {
            DefaultProceduralProps.sizeModifier = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Size);
        }
    }

    public Vector2 GetSizeModifier(in RectTransform rectTransform, ProceduralProperties animationProps = null)
    {
        var (softness, sizeModifier) =  animationProps != null
            ? (animationProps.softness, animationProps.sizeModifier)
            : (DefaultProceduralProps.softness, DefaultProceduralProps.sizeModifier);

        sizeModifier += GetQuadSizeAdjustment(rectTransform);
        var softnessContribution = softness * SoftnessFeatherMode switch
        {
            FeatherMode.Inwards       => 0f,
            FeatherMode.Bidirectional => 1f,
            _                         => 2f
        };

        if (!SizeModifierAspectCorrection)
            return new Vector2(softnessContribution + sizeModifier.x, softnessContribution + sizeModifier.y);

        var (rectWidth, rectHeight) = (rectTransform.rect.width, rectTransform.rect.height);
        return rectWidth > rectHeight 
            ? new Vector2(softnessContribution + sizeModifier.x, softnessContribution + sizeModifier.y * (rectHeight / rectWidth)) 
            : new Vector2(softnessContribution + sizeModifier.x * (rectWidth / rectHeight), softnessContribution + sizeModifier.y);
    }

    [SerializeField] private bool _sizeModifierAspectCorrection;
    public bool SizeModifierAspectCorrection
    {
        get => _sizeModifierAspectCorrection;
        set
        {
            _sizeModifierAspectCorrection = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Size);

        }
    }

    public Vector2 Offset
    {
        get => DefaultProceduralProps.offset;
        set
        {
            DefaultProceduralProps.offset = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Offset);

        }
    }
    
    public float Rotation
    {
        get => DefaultProceduralProps.rotation;
        set
        {
            DefaultProceduralProps.rotation = value;
            SetVerticesDirty();
            if (!FitRotatedImageWithinBounds)
                SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Rotation);

        }
    }
    
    [SerializeField] private bool _fitRotatedImageWithinBounds;
    public bool FitRotatedImageWithinBounds
    {
        get => _fitRotatedImageWithinBounds;
        set
        {
            _fitRotatedImageWithinBounds = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Rotation);

        }
    }

    public bool[] SimpleCutoutEdgeEnabled
    {
        get => _cutoutConfig?.simpleCutoutEdgeEnabled ?? new bool[4];
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.simpleCutoutEdgeEnabled = value;
            SetVerticesDirty();
            if (Cutout == CutoutType.Simple)
                SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public Vector4 SimpleCutout
    {
        get => DefaultProceduralProps.simpleCutout;
        set
        {
            DefaultProceduralProps.simpleCutout = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public Vector4 SDFCutoutChamfer
    {
        get => DefaultProceduralProps.sdfCutoutChamfer;
        set
        {
            DefaultProceduralProps.sdfCutoutChamfer = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public bool SdfCutoutChamferNormalize
    {
        get => _cutoutConfig?.sdfCutoutChamferNormalize ?? true;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.sdfCutoutChamferNormalize = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public bool SDFCutoutIsSquircle
    {
        get => _cutoutConfig?.sdfCutoutIsSquircle ?? false;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.sdfCutoutIsSquircle = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public SDFCutoutMirrorMode SDFCutoutMirror
    {
        get => _cutoutConfig?.sdfCutoutMirror ?? SDFCutoutMirrorMode.None;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.sdfCutoutMirror = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public bool SDFCutoutMirrorIsDiagonal
    {
        get => _cutoutConfig?.sdfCutoutMirrorIsDiagonal ?? false;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.sdfCutoutMirrorIsDiagonal = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public bool SDFCutoutPositionIsAbsolute
    {
        get => _cutoutConfig?.sdfCutoutPositionIsAbsolute ?? false;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.sdfCutoutPositionIsAbsolute = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public bool SDFCutoutSizeIsAbsolute
    {
        get => _cutoutConfig?.sdfCutoutSizeIsAbsolute ?? false;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.sdfCutoutSizeIsAbsolute = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public bool SDFCutoutUsesAnchors
    {
        get => _cutoutConfig?.sdfCutoutUsesAnchors ?? false;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.sdfCutoutUsesAnchors = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public Vector2 SDFCutoutAnchorMin
    {
        get => _cutoutConfig?.sdfCutoutAnchorMin ?? Vector2.one * 0.5f;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.sdfCutoutAnchorMin = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public Vector2 SDFCutoutAnchorMax
    {
        get => _cutoutConfig?.sdfCutoutAnchorMax ?? Vector2.one * 0.5f;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.sdfCutoutAnchorMax = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public Vector2 SDFCutoutPivot
    {
        get => _cutoutConfig?.sdfCutoutPivot ?? Vector2.one * 0.5f;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.sdfCutoutPivot = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public bool CutoutPositionIgnoresExpandedOutlines
    {
        get => _cutoutConfig?.cutoutPositionIgnoresExpandedOutlines ?? false;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.cutoutPositionIgnoresExpandedOutlines = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public Vector4 SDFCutoutConcavity
    {
        get => DefaultProceduralProps.sdfCutoutConcavity;
        set
        {
            DefaultProceduralProps.sdfCutoutConcavity = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public Vector2 SDFCutoutPosition
    {
        get => DefaultProceduralProps.sdfCutoutPosition;
        set
        {
            DefaultProceduralProps.sdfCutoutPosition = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public Vector2 SDFCutoutSize
    {
        get => DefaultProceduralProps.sdfCutoutSize;
        set
        {
            DefaultProceduralProps.sdfCutoutSize = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public float SDFCutoutRotation
    {
        get => DefaultProceduralProps.sdfCutoutRotation;
        set
        {
            DefaultProceduralProps.sdfCutoutRotation = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public void GetSDFCutoutAnchorPositionAndSize(Vector2 referenceOrigin, Vector2 referenceSize, ProceduralProperties animationProps, out Vector2 position, out Vector2 size)
    {
        var props = animationProps ?? DefaultProceduralProps;
        var sizeDelta = props.sdfCutoutSize;
        size = Vector2.Scale(referenceSize, SDFCutoutAnchorMax - SDFCutoutAnchorMin) + sizeDelta;
        position = referenceOrigin + Vector2.Scale((SDFCutoutAnchorMin + SDFCutoutAnchorMax) * 0.5f, referenceSize) + props.sdfCutoutPosition + Vector2.Scale(Vector2.one * 0.5f - SDFCutoutPivot, sizeDelta);
    }

    public void ConvertSDFCutoutPositioning(bool useAnchors, RectTransform rectTransform, Vector2 fallbackReferenceSize)
    {
        if (_cutoutConfig == null || SDFCutoutUsesAnchors == useAnchors)
            return;

        var positionIsAbsolute = SDFCutoutPositionIsAbsolute;
        var sizeIsAbsolute = SDFCutoutSizeIsAbsolute;
        foreach (var state in proceduralAnimationStates)
        foreach (var props in state.proceduralProperties)
        {
            if (props.cutoutAnim == null)
                continue;

            var referenceSize = rectTransform != null ? rectTransform.rect.size + GetSizeModifier(rectTransform, props) : fallbackReferenceSize;
            if (referenceSize.x <= 1e-5f || referenceSize.y <= 1e-5f)
                referenceSize = Vector2.one;
            if (rectTransform != null && !CutoutPositionIgnoresExpandedOutlines && OutlineExpandsOutward)
                referenceSize += Vector2.one * GetOutlineWidth(rectTransform, props) * 2f;

            var position = props.sdfCutoutPosition;
            var size = props.sdfCutoutSize;
            if (SDFCutoutUsesAnchors)
            {
                GetSDFCutoutAnchorPositionAndSize(Vector2.zero, referenceSize, props, out position, out size);
                position -= referenceSize * 0.5f;
            }
            else
            {
                if (!positionIsAbsolute)
                    position = Vector2.Scale(position - Vector2.one * 0.5f, referenceSize);
                if (!sizeIsAbsolute)
                    size = Vector2.Scale(size, referenceSize);
            }

            if (useAnchors)
            {
                var sizeDelta = size - Vector2.Scale(referenceSize, SDFCutoutAnchorMax - SDFCutoutAnchorMin);
                position -= Vector2.Scale((SDFCutoutAnchorMin + SDFCutoutAnchorMax - Vector2.one) * 0.5f, referenceSize) + Vector2.Scale(Vector2.one * 0.5f - SDFCutoutPivot, sizeDelta);
                size = sizeDelta;
            }
            props.sdfCutoutPosition = position;
            props.sdfCutoutSize = size;
        }

        _cutoutConfig.sdfCutoutPositionIsAbsolute = true;
        _cutoutConfig.sdfCutoutSizeIsAbsolute = true;
        _cutoutConfig.sdfCutoutUsesAnchors = useAnchors;
        SetVerticesDirty();
        SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
    }

    [SerializeField] private Vector2Int _primaryColorDimensions = Vector2Int.one;
    public Vector2Int PrimaryColorDimensions
    {
        get => _primaryColorDimensions;
        set
        {
            value.Clamp(Vector2Int.one, new Vector2Int(ProceduralProperties.Colors2dArrayDimensionSize, ProceduralProperties.Colors2dArrayDimensionSize));
            _primaryColorDimensions = value;
            SetVerticesDirty();
        }
    }

    public Vector2Int OutlineColorDimensions
    {
        get => _outlineConfig?.colorDimensions ?? Vector2Int.one;
        set
        {
            if (_outlineConfig == null) return;
            value.Clamp(Vector2Int.one, new Vector2Int(ProceduralProperties.Colors2dArrayDimensionSize, ProceduralProperties.Colors2dArrayDimensionSize));
            _outlineConfig.colorDimensions = value;
            SetVerticesDirty();
        }
    }

    public Vector2Int ProceduralGradientColorDimensions
    {
        get => _gradientConfig?.colorDimensions ?? Vector2Int.one;
        set
        {
            if (_gradientConfig == null) return;
            value.Clamp(Vector2Int.one, new Vector2Int(ProceduralProperties.Colors2dArrayDimensionSize, ProceduralProperties.Colors2dArrayDimensionSize));
            _gradientConfig.colorDimensions = value;
            SetVerticesDirty();
        }
    }

    public Vector2Int PatternColorDimensions
    {
        get => _patternConfig?.colorDimensions ?? Vector2Int.one;
        set
        {
            if (_patternConfig == null) return;
            value.Clamp(Vector2Int.one, new Vector2Int(ProceduralProperties.Colors2dArrayDimensionSize, ProceduralProperties.Colors2dArrayDimensionSize));
            _patternConfig.colorDimensions = value;
            SetVerticesDirty();
        }
    }

    public Color[] PrimaryColorsWithoutDirtying => DefaultProceduralProps.primaryColors;
    public Color[] PrimaryColors
    {
        get
        {
            SetVerticesDirty();
            return DefaultProceduralProps.primaryColors;
        }
        set
        {
            DefaultProceduralProps.primaryColors = value;
            SetVerticesDirty();
        }
    }

    public Color[] OutlineColorsWithoutDirtying => DefaultProceduralProps.outlineColors;
    public Color[] OutlineColors
    {
        get
        {
            SetVerticesDirty();
            return DefaultProceduralProps.outlineColors;
        }
        set
        {
            DefaultProceduralProps.outlineColors = value;
            SetVerticesDirty();
        }
    }

    public Color[] ProceduralGradientColorsWithoutDirtying => DefaultProceduralProps.proceduralGradientColors;

    public Color[] ProceduralGradientColors
    {
        get
        {
            SetVerticesDirty();
            return DefaultProceduralProps.proceduralGradientColors;
        }
        set
        {
            DefaultProceduralProps.proceduralGradientColors = value;
            SetVerticesDirty();
        }
    }

    public Color[] PatternColorsWithoutDirtying => DefaultProceduralProps.patternColors;
    public Color[] PatternColors
    {
        get
        {
            SetVerticesDirty();
            return DefaultProceduralProps.patternColors;
        }
        set
        {
            if (value.Length != ProceduralProperties.Colors2dArrayDimensionSize * ProceduralProperties.Colors1dArrayLength)
                Array.Resize(ref value, ProceduralProperties.Colors1dArrayLength);

            DefaultProceduralProps.patternColors = value;
            SetVerticesDirty();
        }
    }

    public Color GetPrimaryColorAtCell(int x, int y) => DefaultProceduralProps.GetPrimaryColorAtCell(x, y);
    public Color GetOutlineColorAtCell(int x, int y) => DefaultProceduralProps.GetOutlineColorAtCell(x, y);
    public Color GetProceduralGradientColorAtCell(int x, int y) => DefaultProceduralProps.GetProceduralGradientColorAtCell(x, y);
    public Color GetPatternColorAtCell(int x, int y) => DefaultProceduralProps.GetPatternColorAtCell(x, y);
    public bool SetPrimaryColorAtCell(int x, int y, Color c)
    {
        SetVerticesDirty();
        return DefaultProceduralProps.SetPrimaryColorAtCell(x, y, c);
    }

    public bool SetOutlineColorAtCell(int x, int y, Color c)
    {
        SetVerticesDirty();
        return DefaultProceduralProps.SetOutlineColorAtCell(x, y, c);
    }

    public bool SetProceduralGradientColorAtCell(int x, int y, Color c)
    {
        SetVerticesDirty();
        return DefaultProceduralProps.SetProceduralGradientColorAtCell(x, y, c);
    }

    public bool SetPatternColorAtCell(int x, int y, Color c)
    {
        SetVerticesDirty();
        return DefaultProceduralProps.SetPatternColorAtCell(x, y, c);
    }

    public Vector2 PrimaryColorOffset
    {
        get => DefaultProceduralProps.primaryColorOffset;
        set
        {
            DefaultProceduralProps.primaryColorOffset = value;
            SetVerticesDirty();
        }
    }

    public Vector2 OutlineColorOffset
    {
        get => DefaultProceduralProps.outlineColorOffset;
        set
        {
            DefaultProceduralProps.outlineColorOffset = value;
            SetVerticesDirty();
        }
    }

    public Vector2 ProceduralGradientColorOffset
    {
        get => DefaultProceduralProps.proceduralGradientColorOffset;
        set
        {
            DefaultProceduralProps.proceduralGradientColorOffset = value;
            SetVerticesDirty();
        }
    }

    public Vector2 PatternColorOffset
    {
        get => DefaultProceduralProps.patternColorOffset;
        set
        {
            DefaultProceduralProps.patternColorOffset = value;
            SetVerticesDirty();
        }
    }

    public float PrimaryColorRotation
    {
        get => DefaultProceduralProps.primaryColorRotation;
        set
        {
            DefaultProceduralProps.primaryColorRotation = value;
            SetVerticesDirty();
        }
    }

    public float OutlineColorRotation
    {
        get => DefaultProceduralProps.outlineColorRotation;
        set
        {
            DefaultProceduralProps.outlineColorRotation = value;
            SetVerticesDirty();
        }
    }

    public float ProceduralGradientColorRotation
    {
        get => DefaultProceduralProps.proceduralGradientColorRotation;
        set
        {
            DefaultProceduralProps.proceduralGradientColorRotation = value;
            SetVerticesDirty();
        }
    }

    public float PatternColorRotation
    {
        get => DefaultProceduralProps.patternColorRotation;
        set
        {
            DefaultProceduralProps.patternColorRotation = value;
            SetVerticesDirty();
        }
    }

    public Vector2 PrimaryColorScale
    {
        get => DefaultProceduralProps.primaryColorScale;
        set
        {
            DefaultProceduralProps.primaryColorScale = value;
            SetVerticesDirty();
        }
    }

    public Vector2 OutlineColorScale
    {
        get => DefaultProceduralProps.outlineColorScale;
        set
        {
            DefaultProceduralProps.outlineColorScale = value;
            SetVerticesDirty();
        }
    }

    public Vector2 ProceduralGradientColorScale
    {
        get => DefaultProceduralProps.proceduralGradientColorScale;
        set
        {
            DefaultProceduralProps.proceduralGradientColorScale = value;
            SetVerticesDirty();
        }
    }

    public Vector2 PatternColorScale
    {
        get => DefaultProceduralProps.patternColorScale;
        set
        {
            DefaultProceduralProps.patternColorScale = value;
            SetVerticesDirty();
        }
    }

    public bool OutlineFadeTowardsInterior
    {
        get => _outlineConfig?.fadeTowardsPerimeter ?? false;
        set
        {
            if (_outlineConfig == null) return;
            _outlineConfig.fadeTowardsPerimeter = value;
            SetVerticesDirty();
        }
    }

    public bool OutlineAdjustsChamfer
    {
        get => _outlineConfig?.adjustsChamfer ?? false;
        set
        {
            if (_outlineConfig == null) return;
            _outlineConfig.adjustsChamfer = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.ChamferAndCollapse);

        }
    }

    public float GetOutlineWithoutCollapsedEdgeAdjustment(ProceduralProperties animationProps = null, Vector2? additionalScale = null) => (animationProps ?? DefaultProceduralProps).outlineWidth;
    public float GetOutlineWidth(in RectTransform rectTransform, ProceduralProperties animationProps = null)
    {
        var outlineWidth = GetOutlineWithoutCollapsedEdgeAdjustment(animationProps);
        if (!OutlineAccommodatesCollapsedEdge)
            return outlineWidth;

        var sizeMod = GetSizeModifier(rectTransform);
        var (width, height) = (rectTransform.rect.width + sizeMod.x, rectTransform.rect.height + sizeMod.y);

        var maxAspect = Mathf.Max(width, height) / Mathf.Min(width, height);
        return outlineWidth * 2.41421356237f * maxAspect;
    }

    public void SetOutlineWidth(float value)
    {
        DefaultProceduralProps.outlineWidth = value;
        SetVerticesDirty();
        if (OutlineExpandsOutward)
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.IgnoreOutline);
    }

    [SerializeField] [Range(0, 1)] private float _primaryColorPresetMix = 1f;
    public float PrimaryColorPresetMix
    {
        get => ColorPreset != null ? _primaryColorPresetMix : 0f;
        set
        {
            _primaryColorPresetMix = value;
            SetVerticesDirty();
        }
    }

    public float OutlineColorPresetMix
    {
        get => ColorPreset != null ? _outlineConfig?.colorPresetMix ?? 0f : 0f;
        set
        {
            if (_outlineConfig == null) return;
            _outlineConfig.colorPresetMix = value;
            SetVerticesDirty();
        }
    }

    public float ProceduralGradientColorPresetMix
    {
        get => ColorPreset != null ? _gradientConfig?.colorPresetMix ?? 0f : 0f;
        set
        {
            if (_gradientConfig == null) return;
            _gradientConfig.colorPresetMix = value;
            SetVerticesDirty();
        }
    }

    public float PatternColorPresetMix
    {
        get => ColorPreset != null ? _patternConfig?.colorPresetMix ?? 0f : 0f;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.colorPresetMix = value;
            SetVerticesDirty();
        }
    }

    public GradientType ProceduralGradientType
    {
        get => _gradientConfig?.gradientType ?? GradientType.SDF;
        set
        {
            if (_gradientConfig == null) return;
            _gradientConfig.gradientType = value;
            SetVerticesDirty();
        }
    }

    public bool ProceduralGradientAlphaIsBlend
    {
        get => _gradientConfig?.alphaIsBlend ?? false;
        set
        {
            if (_gradientConfig == null) return;
            _gradientConfig.alphaIsBlend = value;
            SetVerticesDirty();
        }
    }
    
    public bool PatternColorAlphaIsBlend
    {
        get => _patternConfig?.alphaIsBlend ?? false;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.alphaIsBlend = value;
            SetVerticesDirty();
        }
    }
    
    public bool ProceduralGradientAspectCorrection
    {
        get => _gradientConfig?.aspectCorrection ?? false;
        set
        {
            if (_gradientConfig == null) return;
            _gradientConfig.aspectCorrection = value;
            SetVerticesDirty();
        }
    }

    public bool ProceduralGradientAffectsInterior
    {
        get => _gradientConfig?.affectsInterior ?? true;
        set
        {
            if (_gradientConfig == null) return;
            _gradientConfig.affectsInterior = value;
            SetVerticesDirty();
        }
    }

    public bool ProceduralGradientAffectsOutline
    {
        get => _gradientConfig?.affectsOutline ?? false;
        set
        {
            if (_gradientConfig == null) return;
            _gradientConfig.affectsOutline = value;
            SetVerticesDirty();
        }
    }
    
    public bool PatternAffectsInterior
    {
        get => _patternConfig?.affectsInterior ?? true;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.affectsInterior = value;
            SetVerticesDirty();
        }
    }

    public bool PatternAffectsOutline
    {
        get => _patternConfig?.affectsOutline ?? false;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.affectsOutline = value;
            SetVerticesDirty();
        }
    }

    public Vector2 ProceduralGradientPosition
    {
        get => DefaultProceduralProps.proceduralGradientPosition;
        set
        {
            DefaultProceduralProps.proceduralGradientPosition = value;
            SetVerticesDirty();
        }
    }

    public Vector2 RadialGradientSize
    {
        get => DefaultProceduralProps.radialGradientSize;
        set
        {
            DefaultProceduralProps.radialGradientSize = value;
            SetVerticesDirty();
        }
    }
    
    public float RadialGradientStrength
    {
        get => DefaultProceduralProps.radialGradientStrength;
        set
        {
            DefaultProceduralProps.radialGradientStrength = value;
            SetVerticesDirty();
        }
    }

    public Vector2 AngleGradientStrength
    {
        get => DefaultProceduralProps.angleGradientStrength;
        set
        {
            DefaultProceduralProps.angleGradientStrength = value;
            SetVerticesDirty();
        }
    }

    public float AngleGradientAngle
    {
        get => DefaultProceduralProps.proceduralGradientAngle;
        set
        {
            DefaultProceduralProps.proceduralGradientAngle = value;
            SetVerticesDirty();
        }
    }

    public float SDFGradientInnerDistance
    {
        get => DefaultProceduralProps.sdfGradientInnerDistance;
        set
        {
            DefaultProceduralProps.sdfGradientInnerDistance = value;
            SetVerticesDirty();
        }
    }

    public float SDFGradientOuterDistance
    {
        get => DefaultProceduralProps.sdfGradientOuterDistance;
        set
        {
            DefaultProceduralProps.sdfGradientOuterDistance = value;
            SetVerticesDirty();
        }
    }
    
    public float SDFGradientInnerReach
    {
        get => DefaultProceduralProps.sdfGradientInnerReach;
        set
        {
            DefaultProceduralProps.sdfGradientInnerReach = value;
            SetVerticesDirty();
        }
    }

    public float SDFGradientOuterReach
    {
        get => DefaultProceduralProps.sdfGradientOuterReach;
        set
        {
            DefaultProceduralProps.sdfGradientOuterReach = value;
            SetVerticesDirty();
        }
    }

    public float ConicalGradientTailStrength
    {
        get => DefaultProceduralProps.conicalGradientTailStrength;
        set
        {
            DefaultProceduralProps.conicalGradientTailStrength = value;
            SetVerticesDirty();
        }
    }

    public float ConicalGradientCurvature
    {
        get => DefaultProceduralProps.conicalGradientCurvature;
        set
        {
            DefaultProceduralProps.conicalGradientCurvature = value;
            SetVerticesDirty();
        }
    }

    public uint NoiseSeed
    {
        get => DefaultProceduralProps.noiseSeed;
        set
        {
            DefaultProceduralProps.noiseSeed = value;
            SetVerticesDirty();
        }
    }

    public float NoiseScale
    {
        get => DefaultProceduralProps.noiseScale;
        set
        {
            DefaultProceduralProps.noiseScale = value;
            SetVerticesDirty();
        }
    }

    public float NoiseEdge
    {
        get => DefaultProceduralProps.noiseEdge;
        set
        {
            DefaultProceduralProps.noiseEdge = value;
            SetVerticesDirty();
        }
    }

    public float NoiseStrength
    {
        get => DefaultProceduralProps.noiseStrength;
        set
        {
            DefaultProceduralProps.noiseStrength = value;
            SetVerticesDirty();
        }
    }

    public bool ProceduralGradientPositionFromPointer
    {
        get => _gradientConfig?.positionFromPointer ?? false;
        set
        {
            if (_gradientConfig == null) return;
            _gradientConfig.positionFromPointer = value;
            SetVerticesDirty();
        }
    }

    public bool NoiseGradientAlternateMode
    {
        get => _gradientConfig?.noiseAlternateMode ?? false;
        set
        {
            if (_gradientConfig == null) return;
            _gradientConfig.noiseAlternateMode = value;
            SetVerticesDirty();
        }
    }

    public bool ScreenSpaceProceduralGradient
    {
        get => _gradientConfig?.screenSpace ?? false;
        set
        {
            if (_gradientConfig == null) return;
            _gradientConfig.screenSpace = value;
            SetVerticesDirty();
        }
    }

    public bool ScreenSpacePattern
    {
        get => _patternConfig?.screenSpace ?? false;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.screenSpace = value;
            SetVerticesDirty();
        }
    }

    public bool SoftPattern
    {
        get => _patternConfig?.softPattern ?? false;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.softPattern = value;
            SetVerticesDirty();
        }
    }

    public SpritePatternRotation SpritePatternRotationMode
    {
        get => _patternConfig?.spriteRotationMode ?? SpritePatternRotation.Sprite;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.spriteRotationMode = value;
            SetVerticesDirty();
        }
    }

    // Only does something when SpritePatternRotationMode is set to SpritePatternRotation.Offset!
    public SpritePatternOffsetDirection SpritePatternOffsetDirectionDegrees
    {
        get => _patternConfig?.spriteOffsetDirection ?? SpritePatternOffsetDirection.Zero;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.spriteOffsetDirection = value;
            SetVerticesDirty();
        }
    }

    public PatternType Pattern
    {
        get => _patternConfig?.patternType ?? PatternType.Horizontal;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.patternType = value;
            SetVerticesDirty();
        }
    }

    public PatternOriginPosition PatternOriginPos
    {
        get => _patternConfig?.originPos ?? PatternOriginPosition.Center;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.originPos = value;
            SetVerticesDirty();
        }
    }

    public bool ScanlinePatternSpeedIsStaticOffset
    {
        get => _patternConfig?.scanlineSpeedIsStaticOffset ?? false;
        set
        {
            if (_patternConfig == null) return;
            _patternConfig.scanlineSpeedIsStaticOffset = value;
            SetVerticesDirty();
        }
    }

    public float PatternDensity
    {
        get => DefaultProceduralProps.patternDensity;
        set
        {
            DefaultProceduralProps.patternDensity = value;
            SetVerticesDirty();
        }
    }

    public float PatternSpeed
    {
        get => DefaultProceduralProps.patternSpeed;
        set
        {
            DefaultProceduralProps.patternSpeed = value;
            SetVerticesDirty();
        }
    }

    public float PatternCellParam
    {
        get => DefaultProceduralProps.patternCellParam;
        set
        {
            DefaultProceduralProps.patternCellParam = value;
            SetVerticesDirty();
        }
    }

    public byte PatternLineThickness
    {
        get => DefaultProceduralProps.patternLineThickness;
        set
        {
            DefaultProceduralProps.patternLineThickness = value;
            SetVerticesDirty();
        }
    }

    public int PatternSpriteRotation
    {
        get => DefaultProceduralProps.patternSpriteRotation;
        set
        {
            DefaultProceduralProps.patternSpriteRotation = value;
            SetVerticesDirty();
        }
    }

    public float Softness
    {
        get => DefaultProceduralProps.softness;
        set
        {
            DefaultProceduralProps.softness = value;
            SetVerticesDirty();
        }
    }
    
    [SerializeField] private FeatherMode _softnessFeatherMode = FeatherMode.Bidirectional;
    public FeatherMode SoftnessFeatherMode
    {
        get => _softnessFeatherMode;
        set
        {
            _softnessFeatherMode = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Size);
        }
    }

    public StrokeOriginLocation StrokeOrigin
    {
        get => _strokeConfig?.strokeOrigin ?? StrokeOriginLocation.Center;
        set
        {
            if (_strokeConfig == null) return;
            _strokeConfig.strokeOrigin = value;
            SetVerticesDirty();
        }
    }

    public float Stroke
    {
        get => DefaultProceduralProps.stroke;
        set
        {
            DefaultProceduralProps.stroke = value;
            SetVerticesDirty();
        }
    }

    [SerializeField] private bool _normalizeChamfer = true;
    public bool NormalizeChamfer
    {
        get => _normalizeChamfer;
        set
        {
            _normalizeChamfer = value;
            SetVerticesDirty();
        }
    }

    public Vector4 CornerChamfer
    {
        get => DefaultProceduralProps.cornerChamfer;
        set
        {
            DefaultProceduralProps.cornerChamfer = value;
            SetVerticesDirty();
        }
    }

    [SerializeField] private bool _concavityIsSmoothing;
    public bool ConcavityIsSmoothing
    {
        get => _concavityIsSmoothing;
        set
        {
            _concavityIsSmoothing = value;
            SetVerticesDirty();
        }
    }

    public Vector4 CornerConcavity
    {
        get => DefaultProceduralProps.cornerConcavity;
        set
        {
            DefaultProceduralProps.cornerConcavity = value;
            SetVerticesDirty();
        }
    }

    public float CollapsedCornerChamfer
    {
        get => DefaultProceduralProps.collapsedCornerChamfer;
        set
        {
            DefaultProceduralProps.collapsedCornerChamfer = value;
            if (!CollapseIntoParallelogram && !MirrorCollapse && Mathf.Approximately(CollapseEdgeAmount, 1))
                SetVerticesDirty();
        }
    }

    public float CollapsedCornerConcavity
    {
        get => DefaultProceduralProps.collapsedCornerConcavity;
        set
        {
            DefaultProceduralProps.collapsedCornerConcavity = value;
            if (!CollapseIntoParallelogram && !MirrorCollapse && Mathf.Approximately(CollapseEdgeAmount, 1))
                SetVerticesDirty();
        }
    }
    
    public CollapsedEdgeType CollapsedEdge
    {
        get => _skewConfig?.collapsedEdge ?? CollapsedEdgeType.Top;
        set
        {
            if (_skewConfig == null) return;
            _skewConfig.collapsedEdge = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.ChamferAndCollapse);
        }
    }

    public bool CollapseIntoParallelogram
    {
        get => _skewConfig?.collapseIntoParallelogram ?? false;
        set
        {
            if (_skewConfig == null) return;
            _skewConfig.collapseIntoParallelogram = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.ChamferAndCollapse);
        }
    }

    public bool MirrorCollapse
    {
        get => _skewConfig?.mirrorCollapse ?? false;
        set
        {
            if (_skewConfig == null) return;
            _skewConfig.mirrorCollapse = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.ChamferAndCollapse);
        }
    }

    public float CollapseEdgeAmount
    {
        get => DefaultProceduralProps.collapseEdgeAmount;
        set
        {
            DefaultProceduralProps.collapseEdgeAmount = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.ChamferAndCollapse);
        }
    }

    public float CollapseEdgeAmountAbsolute
    {
        get => DefaultProceduralProps.collapseEdgeAmountAbsolute;
        set
        {
            DefaultProceduralProps.collapseEdgeAmountAbsolute = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.ChamferAndCollapse);
        }
    }

    public bool EdgeCollapseAmountIsAbsolute
    {
        get => _skewConfig?.edgeCollapseAmountIsAbsolute ?? false;
        set
        {
            if (_skewConfig == null) return;
            _skewConfig.edgeCollapseAmountIsAbsolute = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.ChamferAndCollapse);
        }
    }

    public float CollapseEdgePosition
    {
        get => DefaultProceduralProps.collapseEdgePosition;
        set
        {
            DefaultProceduralProps.collapseEdgePosition = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.ChamferAndCollapse);

        }
    }

    public bool ProceduralGradientInvert
    {
        get => _gradientConfig?.invert ?? false;
        set
        {
            if (_gradientConfig == null) return;
            _gradientConfig.invert = value;
            SetVerticesDirty();
        }
    }

    public bool OutlineAlphaIsBlend
    {
        get => _outlineConfig?.alphaIsBlend ?? false;
        set
        {
            if (_outlineConfig == null) return;
            _outlineConfig.alphaIsBlend = value;
            SetVerticesDirty();
        }
    }

    public bool AddInteriorOutline
    {
        get => _outlineConfig?.addInteriorOutline ?? false;
        set
        {
            if (_outlineConfig == null) return;
            _outlineConfig.addInteriorOutline = value;
            SetVerticesDirty();
        }
    }

    public bool OutlineExpandsOutward
    {
        get => _outlineConfig?.expandsOutward ?? false;
        set
        {
            if (_outlineConfig == null) return;
            _outlineConfig.expandsOutward = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Size);
        }
    }

    public bool OutlineAccommodatesCollapsedEdge
    {
        get => (_outlineConfig?.accommodatesCollapsedEdge ?? false) && OutlineExpandsOutward;
        set
        {
            if (_outlineConfig == null) return;
            _outlineConfig.accommodatesCollapsedEdge = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Size);
        }
    }

    public bool CutoutOnlyAffectsOutline
    {
        get => _cutoutConfig?.cutoutOnlyAffectsOutline ?? false;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.cutoutOnlyAffectsOutline = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public bool InvertCutout
    {
        get => _cutoutConfig?.invertCutout ?? false;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.invertCutout = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public CutoutType Cutout
    {
        get => _cutoutConfig?.cutout ?? CutoutType.Simple;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.cutout = value;
            SetVerticesDirty();
            SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public SimpleCutoutRule CutoutRule
    {
        get => _cutoutConfig?.simpleCutoutRule ?? SimpleCutoutRule.OR;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.simpleCutoutRule = value;
            SetVerticesDirty();
            if (Cutout == CutoutType.Simple)
                SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public SDFCutoutBehaviour CutoutBehaviour
    {
        get => _cutoutConfig?.sdfCutoutBehaviour ?? SDFCutoutBehaviour.MinShape;
        set
        {
            if (_cutoutConfig == null) return;
            _cutoutConfig.sdfCutoutBehaviour = value;
            SetVerticesDirty();
            if (Cutout == CutoutType.Simple)
                SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions.Cutout);
        }
    }

    public bool UsingVertexColor => PrimaryColorDimensions * ProceduralGradientColorDimensions * OutlineColorDimensions * PatternColorDimensions != Vector2Int.one;

    [SerializeField] private int _meshSubdivisions = 2; // Mesh can be subdivided to tweak the appearance of vertex color gradients.
    public int MeshSubdivisions
    {
        get => UsingVertexColor ? _meshSubdivisions : 0;
        set
        {
            _meshSubdivisions = Mathf.Clamp(value, 0 , FlexibleImage.MaxMeshSubdivisions);
            SetVerticesDirty();
        }
    }

    [SerializeField] private Topology _meshTopology; // Flip the diagonal edges of the mesh, which also changes the appearance of vertex color gradients. 
    public Topology MeshTopology
    {
        get => UsingVertexColor ? _meshTopology : Topology.Original;
        set
        {
            _meshTopology = value;
            SetVerticesDirty();
        }
    }

    [NonSerialized] public QuadDataContainer container;
    public void SetVerticesDirty() => container?.MessageVerticesDirty();

    public void SetRayCastAreaDirty(FlexibleImage.AdvancedRaycastOptions flags = (FlexibleImage.AdvancedRaycastOptions)(-1)) => container?.MessageRaycastAreaDirty(this, flags);

    public QuadData() => proceduralAnimationStates[0].PopulateIfEmptyAndGetFirstProps();

    public QuadData(QuadDataContainer container, string name = null)
    {
        this.container = container;
        proceduralAnimationStates[0].PopulateIfEmptyAndGetFirstProps();
        if (name != null)
            this.name = name;
    }

    public QuadData(QuadDataContainer container, QuadData other)
    {
        this.container = container;
        Copy(other);
    }

    ~QuadData()
    {
        if (ColorPreset != null)
            ColorPreset.ColorChangeEvent -= SetVerticesDirty;
    }

    public void Copy(QuadData other, bool setDirty = true)
    {
        for (int i = 0; i < proceduralAnimationStates.Length; i++)
            proceduralAnimationStates[i] = new ProceduralAnimationState(other.proceduralAnimationStates[i]);

        if (_colorPreset && colorPresetCallbackAssigned)
        {
            _colorPreset.ColorChangeEvent -= SetVerticesDirty;
            colorPresetCallbackAssigned = false;
        }

        _colorPreset = other._colorPreset;
        if (_colorPreset)
        {
            _colorPreset.ColorChangeEvent += SetVerticesDirty;
            colorPresetCallbackAssigned = true;
        }

        highlightedFix = other.highlightedFix;
        _concavityIsSmoothing = other._concavityIsSmoothing;
        _advancedQuadSettings = other._advancedQuadSettings;
        _anchorMin = other._anchorMin;
        _anchorMax = other._anchorMax;
        _anchoredPosition = other._anchoredPosition;
        _sizeDelta = other._sizeDelta;
        _pivot = other._pivot;
        _sizeModifierAspectCorrection = other._sizeModifierAspectCorrection;
        _fitRotatedImageWithinBounds = other._fitRotatedImageWithinBounds;
        _primaryColorDimensions = other._primaryColorDimensions;
        _primaryColorWrapModeX = other._primaryColorWrapModeX;
        _primaryColorWrapModeY = other._primaryColorWrapModeY;
        _primaryColorPresetMix = other._primaryColorPresetMix;
        _softnessFeatherMode = other._softnessFeatherMode;
        _normalizeChamfer = other._normalizeChamfer;
        _meshSubdivisions = other._meshSubdivisions;
        _meshTopology = other._meshTopology;

        _outlineConfig = other._outlineConfig == null ? null : new OutlineConfig
        {
            expandsOutward = other._outlineConfig.expandsOutward,
            accommodatesCollapsedEdge = other._outlineConfig.accommodatesCollapsedEdge,
            fadeTowardsPerimeter = other._outlineConfig.fadeTowardsPerimeter,
            adjustsChamfer = other._outlineConfig.adjustsChamfer,
            addInteriorOutline = other._outlineConfig.addInteriorOutline,
            alphaIsBlend = other._outlineConfig.alphaIsBlend,
            colorDimensions = other._outlineConfig.colorDimensions,
            colorWrapModeX = other._outlineConfig.colorWrapModeX,
            colorWrapModeY = other._outlineConfig.colorWrapModeY,
            colorPresetMix = other._outlineConfig.colorPresetMix
        };
        _gradientConfig = other._gradientConfig == null ? null : new GradientConfig
        {
            gradientType = other._gradientConfig.gradientType,
            alphaIsBlend = other._gradientConfig.alphaIsBlend,
            affectsInterior = other._gradientConfig.affectsInterior,
            affectsOutline = other._gradientConfig.affectsOutline,
            aspectCorrection = other._gradientConfig.aspectCorrection,
            positionFromPointer = other._gradientConfig.positionFromPointer,
            invert = other._gradientConfig.invert,
            noiseAlternateMode = other._gradientConfig.noiseAlternateMode,
            screenSpace = other._gradientConfig.screenSpace,
            colorDimensions = other._gradientConfig.colorDimensions,
            colorWrapModeX = other._gradientConfig.colorWrapModeX,
            colorWrapModeY = other._gradientConfig.colorWrapModeY,
            colorPresetMix = other._gradientConfig.colorPresetMix
        };
        _patternConfig = other._patternConfig == null ? null : new PatternConfig
        {
            patternType = other._patternConfig.patternType,
            originPos = other._patternConfig.originPos,
            scanlineSpeedIsStaticOffset = other._patternConfig.scanlineSpeedIsStaticOffset,
            softPattern = other._patternConfig.softPattern,
            screenSpace = other._patternConfig.screenSpace,
            spriteRotationMode = other._patternConfig.spriteRotationMode,
            spriteOffsetDirection = other._patternConfig.spriteOffsetDirection,
            affectsInterior = other._patternConfig.affectsInterior,
            affectsOutline = other._patternConfig.affectsOutline,
            alphaIsBlend = other._patternConfig.alphaIsBlend,
            colorDimensions = other._patternConfig.colorDimensions,
            colorWrapModeX = other._patternConfig.colorWrapModeX,
            colorWrapModeY = other._patternConfig.colorWrapModeY,
            colorPresetMix = other._patternConfig.colorPresetMix
        };
        _cutoutConfig = other._cutoutConfig == null ? null : new CutoutConfig
        {
            cutout = other._cutoutConfig.cutout,
            simpleCutoutEdgeEnabled = (bool[])other._cutoutConfig.simpleCutoutEdgeEnabled.Clone(),
            simpleCutoutRule = other._cutoutConfig.simpleCutoutRule,
            sdfCutoutBehaviour = other._cutoutConfig.sdfCutoutBehaviour,
            sdfCutoutChamferNormalize = other._cutoutConfig.sdfCutoutChamferNormalize,
            sdfCutoutIsSquircle = other._cutoutConfig.sdfCutoutIsSquircle,
            sdfCutoutMirror = other._cutoutConfig.sdfCutoutMirror,
            sdfCutoutMirrorIsDiagonal = other._cutoutConfig.sdfCutoutMirrorIsDiagonal,
            sdfCutoutPositionIsAbsolute = other._cutoutConfig.sdfCutoutPositionIsAbsolute,
            sdfCutoutSizeIsAbsolute = other._cutoutConfig.sdfCutoutSizeIsAbsolute,
            sdfCutoutUsesAnchors = other._cutoutConfig.sdfCutoutUsesAnchors,
            sdfCutoutAnchorMin = other._cutoutConfig.sdfCutoutAnchorMin,
            sdfCutoutAnchorMax = other._cutoutConfig.sdfCutoutAnchorMax,
            sdfCutoutPivot = other._cutoutConfig.sdfCutoutPivot,
            cutoutPositionIgnoresExpandedOutlines = other._cutoutConfig.cutoutPositionIgnoresExpandedOutlines,
            cutoutOnlyAffectsOutline = other._cutoutConfig.cutoutOnlyAffectsOutline,
            invertCutout = other._cutoutConfig.invertCutout
        };
        _strokeConfig = other._strokeConfig == null ? null : new StrokeConfig { strokeOrigin = other._strokeConfig.strokeOrigin };
        _skewConfig = other._skewConfig == null ? null : new SkewConfig
        {
            collapsedEdge = other._skewConfig.collapsedEdge,
            collapseIntoParallelogram = other._skewConfig.collapseIntoParallelogram,
            mirrorCollapse = other._skewConfig.mirrorCollapse,
            edgeCollapseAmountIsAbsolute = other._skewConfig.edgeCollapseAmountIsAbsolute
        };

        if (setDirty)
        {
            SetVerticesDirty();
            SetRayCastAreaDirty();
        }
    }

    private static Vector4 GetSquircleChamferScales(Vector4 chamfer, Vector4 concavity)
    {
        var circleMid = 1f - Mathf.Sqrt(0.5f);
        var scales = Vector4.one;
        if (concavity.x > 0f && chamfer.x > 0f)
        {
            var p = Mathf.Lerp(2f, 10f, concavity.x);
            var mid = 1f - Mathf.Pow(2f, -1f / p);
            scales.x = circleMid / mid;
        }
        if (concavity.y > 0f && chamfer.y > 0f)
        {
            var p = Mathf.Lerp(2f, 10f, concavity.y);
            var mid = 1f - Mathf.Pow(2f, -1f / p);
            scales.y = circleMid / mid;
        }
        if (concavity.z > 0f && chamfer.z > 0f)
        {
            var p = Mathf.Lerp(2f, 10f, concavity.z);
            var mid = 1f - Mathf.Pow(2f, -1f / p);
            scales.z = circleMid / mid;
        }
        if (concavity.w > 0f && chamfer.w > 0f)
        {
            var p = Mathf.Lerp(2f, 10f, concavity.w);
            var mid = 1f - Mathf.Pow(2f, -1f / p);
            scales.w = circleMid / mid;
        }

        return scales;
    }

    private static float GetSquircleVisibleChamfer(float chamfer, float smoothing, float visibleInset)
    {
        if (chamfer <= 0f || smoothing <= 0f)
            return chamfer;

        var p = Mathf.Lerp(2f, 10f, smoothing);
        var inset01 = Mathf.Clamp01(visibleInset / chamfer);
        return chamfer * (1f - Mathf.Pow(1f - Mathf.Pow(1f - inset01, p), 1f / p));
    }

    private static Vector4 GetSquircleVisibleChamfer(Vector4 chamfer, Vector4 concavity, float visibleInset)
    {
        return new Vector4(
            GetSquircleVisibleChamfer(chamfer.x, concavity.x, visibleInset),
            GetSquircleVisibleChamfer(chamfer.y, concavity.y, visibleInset),
            GetSquircleVisibleChamfer(chamfer.z, concavity.z, visibleInset),
            GetSquircleVisibleChamfer(chamfer.w, concavity.w, visibleInset)
        );
    }

    public Vector4 GetAdjustedCutoutChamfer(in RectTransform rectTransform, ProceduralProperties animationProps = null)
    {
        var chamfer = animationProps?.sdfCutoutChamfer ?? SDFCutoutChamfer;
        var concavity = animationProps?.sdfCutoutConcavity ?? SDFCutoutConcavity;
        var scale = animationProps?.sdfCutoutSize ?? SDFCutoutSize;
        var size = GetSizeModifier(rectTransform, animationProps) + rectTransform.rect.size;
        if (SDFCutoutUsesAnchors)
        {
            if (!CutoutPositionIgnoresExpandedOutlines && OutlineExpandsOutward)
                size += Vector2.one * GetOutlineWidth(rectTransform, animationProps) * 2f;
            size = Vector2.Scale(size, SDFCutoutAnchorMax - SDFCutoutAnchorMin) + scale;
        }
        else if (SDFCutoutSizeIsAbsolute)
            size = scale;
        else
            size *= scale;

        if (SdfCutoutChamferNormalize)
        {
            var (width, height) = (size.x, size.y);
            var shrink = Vector4.one;

            var totalChamferTop = chamfer.x + chamfer.y;
            if (totalChamferTop > 0 && width > 0)
            {
                var shrinkFactor = width / totalChamferTop;
                shrink.x = Mathf.Min(shrink.x, shrinkFactor);
                shrink.y = Mathf.Min(shrink.y, shrinkFactor);
            }
            var totalChamferBottom = chamfer.w + chamfer.z;
            if (totalChamferBottom > 0 && width > 0)
            {
                var shrinkFactor = width / totalChamferBottom;
                shrink.w = Mathf.Min(shrink.w, shrinkFactor);
                shrink.z = Mathf.Min(shrink.z, shrinkFactor);
            }
            var totalChamferLeft = chamfer.x + chamfer.z;
            if (totalChamferLeft > 0 && height > 0)
            {
                var shrinkFactor = height / totalChamferLeft;
                shrink.x = Mathf.Min(shrink.x, shrinkFactor);
                shrink.z = Mathf.Min(shrink.z, shrinkFactor);
            }
            var totalChamferRight = chamfer.w + chamfer.y;
            if (totalChamferRight > 0 && height > 0)
            {
                var shrinkFactor = height / totalChamferRight;
                shrink.w = Mathf.Min(shrink.w, shrinkFactor);
                shrink.y = Mathf.Min(shrink.y, shrinkFactor);
            }

            chamfer = Vector4.Scale(chamfer, shrink);
        }

        if (chamfer.magnitude > 0 && concavity.magnitude > 0)
        {
            if (SDFCutoutIsSquircle)
            {
                chamfer = Vector4.Scale(chamfer, GetSquircleChamferScales(chamfer, concavity));
                if (SdfCutoutChamferNormalize)
                {
                    var (width, height) = (size.x, size.y);
                    var visibleInset = Mathf.Max(animationProps?.softness ?? Softness, 1f);
                    var visibleChamfer = GetSquircleVisibleChamfer(chamfer, concavity, visibleInset);
                    var shrink = Vector4.one;

                    var totalChamferTop = visibleChamfer.x + visibleChamfer.y;
                    if (totalChamferTop > 0 && width > 0)
                    {
                        var shrinkFactor = width / totalChamferTop;
                        shrink.x = Mathf.Min(shrink.x, shrinkFactor);
                        shrink.y = Mathf.Min(shrink.y, shrinkFactor);
                    }
                    var totalChamferBottom = visibleChamfer.w + visibleChamfer.z;
                    if (totalChamferBottom > 0 && width > 0)
                    {
                        var shrinkFactor = width / totalChamferBottom;
                        shrink.w = Mathf.Min(shrink.w, shrinkFactor);
                        shrink.z = Mathf.Min(shrink.z, shrinkFactor);
                    }
                    var totalChamferLeft = visibleChamfer.x + visibleChamfer.z;
                    if (totalChamferLeft > 0 && height > 0)
                    {
                        var shrinkFactor = height / totalChamferLeft;
                        shrink.x = Mathf.Min(shrink.x, shrinkFactor);
                        shrink.z = Mathf.Min(shrink.z, shrinkFactor);
                    }
                    var totalChamferRight = visibleChamfer.w + visibleChamfer.y;
                    if (totalChamferRight > 0 && height > 0)
                    {
                        var shrinkFactor = height / totalChamferRight;
                        shrink.w = Mathf.Min(shrink.w, shrinkFactor);
                        shrink.y = Mathf.Min(shrink.y, shrinkFactor);
                    }

                    chamfer = Vector4.Scale(chamfer, shrink);
                }
            }
            else
            {
                // Higher concavity requires more chamfer in that if we imagine each corner as an elastic band held between two points, then without adjustments, those points come closer together as concavity increases.
                // I haven't been able to work out the exact relationship, but we can get very close via curve-fitting.
                var scaledConcavity = concavity;
                scaledConcavity.Scale(concavity);
                var cc2 = scaledConcavity;
                scaledConcavity.Scale(concavity);
                var cc3 = scaledConcavity;
                scaledConcavity.Scale(concavity);
                var cc4 = scaledConcavity;
                chamfer.Scale(Vector4.one + 1.708333f * concavity - 1.166667f * cc2 + 0.5416667f * cc3 - 0.08333333f * cc4);
            }
        }

        // if (OutlineAdjustsChamfer)
        // {
        //     if (OutlineExpandsOutward)
        //         chamfer += Vector4.one * GetOutlineWithoutCollapsedEdgeAdjustment(animationProps);
        //     else
        //         chamfer = Vector4.Max(Vector4.one * GetOutlineWithoutCollapsedEdgeAdjustment(animationProps), chamfer);
        // }

        // Between the curve-fit and OutlineAdjustsChamfer, it's possible for chamfer to overflow the packed vertex attribute range.
        return Vector4.Min(Vector4.one * 1023.75f, chamfer);
    }

    public Vector4 GetAdjustedChamfer(in RectTransform rectTransform, ProceduralProperties animationProps = null)
    {
        var (chamfer, collapseEdgeAmountRelative, collapseEdgeAmountAbsolute, collapsedCornerChamfer, collapseEdgePosition) = animationProps != null 
            ? (animationProps.cornerChamfer,         animationProps.collapseEdgeAmount,         animationProps.collapseEdgeAmountAbsolute,         animationProps.collapsedCornerChamfer,         animationProps.collapseEdgePosition) 
            : (DefaultProceduralProps.cornerChamfer, DefaultProceduralProps.collapseEdgeAmount, DefaultProceduralProps.collapseEdgeAmountAbsolute, DefaultProceduralProps.collapsedCornerChamfer, DefaultProceduralProps.collapseEdgePosition);

        float collapseEdgeAmount;
        if (EdgeCollapseAmountIsAbsolute)
        {
            var size = GetSizeModifier(rectTransform);
            size += rectTransform.rect.size;
            var dimensionalSize = CollapsedEdge <= CollapsedEdgeType.Bottom ? size.x : size.y;
            collapseEdgeAmount = Mathf.Min(1f, collapseEdgeAmountAbsolute / Mathf.Max(dimensionalSize, 0.01f));
        }
        else
        {
            collapseEdgeAmount = collapseEdgeAmountRelative;
        }
        
        if (!CollapseIntoParallelogram && !MirrorCollapse && Mathf.Approximately(collapseEdgeAmount, 1))
        {
            if ((int)CollapsedEdge == 0)
            {
                chamfer.x = collapsedCornerChamfer;
                chamfer.y = 0;
            }
            else if ((int)CollapsedEdge == 1)
            {
                chamfer.w = collapsedCornerChamfer;
                chamfer.z = 0;
            }
            else if ((int)CollapsedEdge == 2)
            {
                chamfer.x = collapsedCornerChamfer;
                chamfer.z = 0;
            }
            else
            {
                chamfer.w = collapsedCornerChamfer;
                chamfer.y = 0;
            }
        }

        var adjustedConcavity = GetAdjustedConcavity(animationProps);

        float effectiveTop = 0f, effectiveBottom = 0f, effectiveLeft = 0f, effectiveRight = 0f;

        // When normalized, no corner can affect more than half the overall image.
        if (NormalizeChamfer && !MirrorCollapse)
        {
            var sizeMod = GetSizeModifier(rectTransform);
            var (rectWidth, rectHeight) = (rectTransform.rect.width, rectTransform.rect.height);
            var (width, height) = (rectWidth + sizeMod.x, rectHeight + sizeMod.y);

            // Compute effective edge lengths accounting for collapsed edges
            var bl = new Vector2(0, 0);
            var br = new Vector2(width, 0);
            var tr = new Vector2(width, height);
            var tl = new Vector2(0, height);
            var collapseEdgeInt = (int)CollapsedEdge;
            if (collapseEdgeInt == 0) // Top
            {
                var innerCollapsePoint = (tr.x - tl.x) * collapseEdgePosition + tl.x;
                tl.x += (innerCollapsePoint - tl.x) * collapseEdgeAmount;
                tr.x += (innerCollapsePoint - tr.x) * collapseEdgeAmount;
                if (CollapseIntoParallelogram)
                {
                    var innerOppositeCollapsePoint = width - innerCollapsePoint;
                    bl.x += (innerOppositeCollapsePoint - bl.x) * collapseEdgeAmount;
                    br.x += (innerOppositeCollapsePoint - br.x) * collapseEdgeAmount;
                }
            }
            else if (collapseEdgeInt == 1) // Bottom
            {
                var innerCollapsePoint = (br.x - bl.x) * collapseEdgePosition + bl.x;
                bl.x += (innerCollapsePoint - bl.x) * collapseEdgeAmount;
                br.x += (innerCollapsePoint - br.x) * collapseEdgeAmount;
                if (CollapseIntoParallelogram)
                {
                    var innerOppositeCollapsePoint = width - innerCollapsePoint;
                    tl.x += (innerOppositeCollapsePoint - tl.x) * collapseEdgeAmount;
                    tr.x += (innerOppositeCollapsePoint - tr.x) * collapseEdgeAmount;
                }
            }
            else if (collapseEdgeInt == 2) // Left
            {
                var innerCollapsePoint = (tl.y - bl.y) * collapseEdgePosition + bl.y;
                bl.y += (innerCollapsePoint - bl.y) * collapseEdgeAmount;
                tl.y += (innerCollapsePoint - tl.y) * collapseEdgeAmount;
                if (CollapseIntoParallelogram)
                {
                    var innerOppositeCollapsePoint = height - innerCollapsePoint;
                    br.y += (innerOppositeCollapsePoint - br.y) * collapseEdgeAmount;
                    tr.y += (innerOppositeCollapsePoint - tr.y) * collapseEdgeAmount;
                }
            }
            else // Right
            {
                var innerCollapsePoint = (tr.y - br.y) * collapseEdgePosition + br.y;
                br.y += (innerCollapsePoint - br.y) * collapseEdgeAmount;
                tr.y += (innerCollapsePoint - tr.y) * collapseEdgeAmount;
                if (CollapseIntoParallelogram)
                {
                    var innerOppositeCollapsePoint = height - innerCollapsePoint;
                    bl.y += (innerOppositeCollapsePoint - bl.y) * collapseEdgeAmount;
                    tl.y += (innerOppositeCollapsePoint - tl.y) * collapseEdgeAmount;
                }
            }
            effectiveTop = Vector2.Distance(tl, tr);
            effectiveBottom = Vector2.Distance(bl, br);
            effectiveLeft = Vector2.Distance(bl, tl);
            effectiveRight = Vector2.Distance(br, tr);

            var shrink = Vector4.one;
            var totalChaferTop = chamfer.x + (effectiveRight == 0 ? chamfer.w : chamfer.y);
            if (totalChaferTop > 0 && effectiveTop > 0)
            {
                var shrinkFactor = effectiveTop / totalChaferTop;
                shrink.x = Mathf.Min(shrink.x, shrinkFactor);
                if (effectiveRight == 0)
                    shrink.w = Mathf.Min(shrink.w, shrinkFactor);
                else
                    shrink.y = Mathf.Min(shrink.y, shrinkFactor);
            }
            var totalChamferBottom = chamfer.w + (effectiveLeft == 0 ? chamfer.x : chamfer.z);
            if (totalChamferBottom > 0 && effectiveBottom > 0)
            {
                var shrinkFactor = effectiveBottom / totalChamferBottom;
                shrink.w = Mathf.Min(shrink.w, shrinkFactor);
                if (effectiveLeft == 0)
                    shrink.x = Mathf.Min(shrink.x, shrinkFactor);
                else
                    shrink.z = Mathf.Min(shrink.z, shrinkFactor);
            }
            var totalChamferLeft = chamfer.x + (effectiveBottom == 0 ? chamfer.w : chamfer.z);
            if (totalChamferLeft > 0 && effectiveLeft > 0)
            {
                var shrinkFactor = effectiveLeft / totalChamferLeft;
                shrink.x = Mathf.Min(shrink.x, shrinkFactor);
                if (effectiveBottom == 0)
                    shrink.w = Mathf.Min(shrink.w, shrinkFactor);
                else
                    shrink.z = Mathf.Min(shrink.z, shrinkFactor);
            }
            var totalChamferRight = chamfer.w + (effectiveTop == 0 ? chamfer.x : chamfer.y);
            if (totalChamferRight > 0 && effectiveRight > 0)
            {
                var shrinkFactor = effectiveRight / totalChamferRight;
                shrink.w = Mathf.Min(shrink.w, shrinkFactor);
                if (effectiveTop == 0)
                    shrink.x = Mathf.Min(shrink.x, shrinkFactor);
                else
                    shrink.y = Mathf.Min(shrink.y, shrinkFactor);
            }
            chamfer = Vector4.Scale(chamfer, shrink);
        }

        if (chamfer.magnitude > 0 && adjustedConcavity.magnitude > 0)
        {
            if (ConcavityIsSmoothing)
            {
                chamfer = Vector4.Scale(chamfer, GetSquircleChamferScales(chamfer, adjustedConcavity));
                if (NormalizeChamfer && !MirrorCollapse)
                {
                    var visibleInset = Mathf.Max(animationProps?.softness ?? Softness, 1f);
                    var visibleChamfer = GetSquircleVisibleChamfer(chamfer, adjustedConcavity, visibleInset);
                    var shrink = Vector4.one;

                    var totalChaferTop = visibleChamfer.x + (effectiveRight == 0 ? visibleChamfer.w : visibleChamfer.y);
                    if (totalChaferTop > 0 && effectiveTop > 0)
                    {
                        var shrinkFactor = effectiveTop / totalChaferTop;
                        shrink.x = Mathf.Min(shrink.x, shrinkFactor);
                        if (effectiveRight == 0)
                            shrink.w = Mathf.Min(shrink.w, shrinkFactor);
                        else
                            shrink.y = Mathf.Min(shrink.y, shrinkFactor);
                    }
                    var totalChamferBottom = visibleChamfer.w + (effectiveLeft == 0 ? visibleChamfer.x : visibleChamfer.z);
                    if (totalChamferBottom > 0 && effectiveBottom > 0)
                    {
                        var shrinkFactor = effectiveBottom / totalChamferBottom;
                        shrink.w = Mathf.Min(shrink.w, shrinkFactor);
                        if (effectiveLeft == 0)
                            shrink.x = Mathf.Min(shrink.x, shrinkFactor);
                        else
                            shrink.z = Mathf.Min(shrink.z, shrinkFactor);
                    }
                    var totalChamferLeft = visibleChamfer.x + (effectiveBottom == 0 ? visibleChamfer.w : visibleChamfer.z);
                    if (totalChamferLeft > 0 && effectiveLeft > 0)
                    {
                        var shrinkFactor = effectiveLeft / totalChamferLeft;
                        shrink.x = Mathf.Min(shrink.x, shrinkFactor);
                        if (effectiveBottom == 0)
                            shrink.w = Mathf.Min(shrink.w, shrinkFactor);
                        else
                            shrink.z = Mathf.Min(shrink.z, shrinkFactor);
                    }
                    var totalChamferRight = visibleChamfer.w + (effectiveTop == 0 ? visibleChamfer.x : visibleChamfer.y);
                    if (totalChamferRight > 0 && effectiveRight > 0)
                    {
                        var shrinkFactor = effectiveRight / totalChamferRight;
                        shrink.w = Mathf.Min(shrink.w, shrinkFactor);
                        if (effectiveTop == 0)
                            shrink.x = Mathf.Min(shrink.x, shrinkFactor);
                        else
                            shrink.y = Mathf.Min(shrink.y, shrinkFactor);
                    }

                    chamfer = Vector4.Scale(chamfer, shrink);
                }
            }
            else
            {
                // Higher concavity requires more chamfer in that if we imagine each corner as an elastic band held between two points, then without adjustments, those points come closer together as concavity increases.
                // I haven't been able to work out the exact relationship, but we can get very close via curve-fitting.
                var scaledConcavity = adjustedConcavity;
                scaledConcavity.Scale(adjustedConcavity);
                var cc2 = scaledConcavity;
                scaledConcavity.Scale(adjustedConcavity);
                var cc3 = scaledConcavity;
                scaledConcavity.Scale(adjustedConcavity);
                var cc4 = scaledConcavity;
                chamfer.Scale(Vector4.one + 1.708333f * adjustedConcavity - 1.166667f * cc2 + 0.5416667f * cc3 - 0.08333333f * cc4);
            }
        }

        if (OutlineAdjustsChamfer)
        {
            if (OutlineExpandsOutward)
                chamfer += Vector4.one * GetOutlineWithoutCollapsedEdgeAdjustment(animationProps);
            else
                chamfer = Vector4.Max(Vector4.one * GetOutlineWithoutCollapsedEdgeAdjustment(animationProps), chamfer);
        }

        // Between the curve-fit and the OutlineAdjustsChamfer, it's possible to get a chamfer overflows the range of the packed vertex attribute. No one is likely to use values large enough to notice clamping.
        return Vector4.Min(Vector4.one * 4095.9375f, chamfer);
    }

    public Vector4 GetAdjustedConcavity(ProceduralProperties animationProps = null)
    {
        var collapseEdgeAmount = animationProps?.collapseEdgeAmount ?? DefaultProceduralProps.collapseEdgeAmount;
        if (CollapseIntoParallelogram || MirrorCollapse || collapseEdgeAmount < 1)
            return animationProps?.cornerConcavity ?? DefaultProceduralProps.cornerConcavity;

        var (concavity, collapsedCornerConcavity) = animationProps != null
            ? (animationProps.cornerConcavity,         animationProps.collapsedCornerConcavity)
            : (DefaultProceduralProps.cornerConcavity, DefaultProceduralProps.collapsedCornerConcavity);

        if ((int)CollapsedEdge == 0)
        {
            concavity.x = collapsedCornerConcavity;
            concavity.y = 0;
        }
        else if ((int)CollapsedEdge == 1)
        {
            concavity.w = collapsedCornerConcavity;
            concavity.z = 0;
        }
        else if ((int)CollapsedEdge == 2)
        {
            concavity.x = collapsedCornerConcavity;
            concavity.z = 0;
        }
        else
        {
            concavity.w = collapsedCornerConcavity;
            concavity.y = 0;
        }
        return concavity;
    }

    public float GetStroke01(in RectTransform rectTransform, ProceduralProperties animationProps = null)
    {
        var sizeMod = GetSizeModifier(rectTransform);
        var (width, height) = (rectTransform.rect.width + sizeMod.x, rectTransform.rect.height + sizeMod.y);
        if (OutlineExpandsOutward)
        {
            var outlineWidth = 2 * GetOutlineWithoutCollapsedEdgeAdjustment();
            width += outlineWidth;
            height += outlineWidth;
        }

        var minSide = Mathf.Min(width, height) * 0.5f;
        var stroke01 = animationProps?.stroke ?? DefaultProceduralProps.stroke;

        if (StrokeOrigin == StrokeOriginLocation.Outline)
            stroke01 += GetOutlineWithoutCollapsedEdgeAdjustment();

        stroke01 /= minSide;
        stroke01 = Mathf.Clamp01(stroke01);
        if (StrokeOrigin == StrokeOriginLocation.Center)
            stroke01 = 1f - stroke01;

        // Don't adjust for softness if stroke == 1 (no stroke)
        if (Mathf.Approximately(stroke01, 1f))
            return 1;

        var scaledSoftness = Softness / minSide;
        if (SoftnessFeatherMode == FeatherMode.Inwards)
            stroke01 += scaledSoftness;
        else if (SoftnessFeatherMode == FeatherMode.Bidirectional)
            stroke01 += scaledSoftness * 0.5f;

        // Max: 1 - 1/4095, since the stroke disappears entirely at 1, creating a discontinuity when softness adjustment gets high enough.
        stroke01 = Mathf.Min(stroke01, 0.99975579975f);
        return stroke01;
    }

    public bool SetAnimationValues(AnimationValues animationValues, int currentSelectionStateIdx, bool pointerInside, bool pointerDown)
    {
        if (highlightedFix && pointerInside && !pointerDown && currentSelectionStateIdx < 4)
            currentSelectionStateIdx = 1;

        if (proceduralAnimationStates[currentSelectionStateIdx].proceduralProperties.Count == 0)
            currentSelectionStateIdx = 0;

        if (currentSelectionStateIdx == animationValues.lastReachedStateIdx && currentSelectionStateIdx == animationValues.lastSelectionStateIdx)
            return false;

        var dirtiedVertices = false;
        if (animationValues.lastSelectionStateIdx != currentSelectionStateIdx)
        {
            animationValues.lastReachedStateIdx = animationValues.lastSelectionStateIdx;
            animationValues.lastSelectionStateIdx = currentSelectionStateIdx;
    
            if (animationValues.lastReachedStateIdx >= 0)
                animationValues.checkUnwind = true;
            else
                animationValues.Reset();
        }

        if (animationValues.checkUnwind)
        {
            if (proceduralAnimationStates[animationValues.lastReachedStateIdx].Unwind(animationValues))
            {
                dirtiedVertices = true;
            }
            else
            {
                animationValues.checkUnwind = false;
                animationValues.Reset();
            }
        }
        else
        {
            dirtiedVertices = true;
            if (proceduralAnimationStates[currentSelectionStateIdx].ComputeProperties(animationValues))
            {
                animationValues.SetCurrentProps(proceduralAnimationStates[currentSelectionStateIdx].proceduralProperties[^1], false);
                animationValues.lastReachedStateIdx = currentSelectionStateIdx;
            }
        }

        return dirtiedVertices;
    }

    // Helper method which uses the cutout region to create a "fill" effect.
    // Will generally *not* work well with an expanded outline when Massage Collapse is enabled.
    // Cutouts have a maximum size, so cutout fills will not work if the rect size (+ expanded outline) exceeds it.
    public void CutoutFill(RectTransform rectTransform, CutoutFillOrigin origin, float percent)
    {
        if (percent >= 1)
        {
            SimpleCutoutEdgeEnabled[0] = SimpleCutoutEdgeEnabled[1] = SimpleCutoutEdgeEnabled[2] = SimpleCutoutEdgeEnabled[3] = false;
            InvertCutout = false;
            return;
        }

        CutoutRule = SimpleCutoutRule.OR;

        var totalSize = rectTransform.rect.size;
        if (OutlineExpandsOutward)
            totalSize += new Vector2(2, 2) * GetOutlineWidth(rectTransform, DefaultProceduralProps);

        if (origin <= CutoutFillOrigin.Right)
        {
            if (totalSize.x > 1023.5f)
            {
                Debug.LogWarning($"{rectTransform.name} is too wide for horizontal CutoutFill. Maximum width is 1023.5 canvas units.");
                return;
            }
            var absoluteCutoutSize = totalSize.x * percent;
            InvertCutout = false;
            SimpleCutoutEdgeEnabled[0] = SimpleCutoutEdgeEnabled[1] = true;
            SimpleCutoutEdgeEnabled[2] = SimpleCutoutEdgeEnabled[3] = false;
            SimpleCutout = origin == CutoutFillOrigin.Left 
                ? new Vector4(absoluteCutoutSize, 0, 0, 0)
                : new Vector4(0, absoluteCutoutSize, 0, 0);
        }
        else if (origin <= CutoutFillOrigin.Bottom)
        {
            if (totalSize.y > 1023.5f)
            {
                Debug.LogWarning($"{rectTransform.name} is too tall for vertical CutoutFill. Maximum height is 1023.5 canvas units.");
                return;
            }
            var absoluteCutoutSize = totalSize.y * percent;
            InvertCutout = false;
            SimpleCutoutEdgeEnabled[0] = SimpleCutoutEdgeEnabled[1] = false;
            SimpleCutoutEdgeEnabled[2] = SimpleCutoutEdgeEnabled[3] = true;
            SimpleCutout = origin == CutoutFillOrigin.Top
                ? new Vector4(0, 0, absoluteCutoutSize, 0)
                : new Vector4(0, 0, 0, absoluteCutoutSize);
        }
        else if (origin <= CutoutFillOrigin.HorizontalFromPerimeter)
        {
            if (totalSize.x > 2047f)
            {
                Debug.LogWarning($"{rectTransform.name} is too wide for mirrored horizontal CutoutFill. Maximum width is 2047 canvas units.");
                return;
            }
            var fromPerimeter = percent > 0 && origin == CutoutFillOrigin.HorizontalFromCenter;
            InvertCutout = fromPerimeter;
            percent = fromPerimeter ? 1 - percent : percent;
            var absoluteCutoutSize = totalSize.x * percent * 0.5f;
            SimpleCutoutEdgeEnabled[0] = SimpleCutoutEdgeEnabled[1] = true;
            SimpleCutoutEdgeEnabled[2] = SimpleCutoutEdgeEnabled[3] = false;
            SimpleCutout = new Vector4(absoluteCutoutSize, absoluteCutoutSize, 0, 0);
        }
        else if (origin <= CutoutFillOrigin.VerticalFromPerimeter)
        {
            if (totalSize.y > 2047f)
            {
                Debug.LogWarning($"{rectTransform.name} is too tall for mirrored vertical CutoutFill. Maximum height is 2047 canvas units.");
                return;
            }
            var fromPerimeter = percent > 0 && origin == CutoutFillOrigin.VerticalFromCenter;
            InvertCutout = fromPerimeter;
            percent = fromPerimeter ? 1 - percent : percent;
            var absoluteCutoutSize = totalSize.y * percent * 0.5f;
            SimpleCutoutEdgeEnabled[0] = SimpleCutoutEdgeEnabled[1] = false;
            SimpleCutoutEdgeEnabled[2] = SimpleCutoutEdgeEnabled[3] = true;
            SimpleCutout = new Vector4(0, 0, absoluteCutoutSize, absoluteCutoutSize);
        }
        else if (origin <= CutoutFillOrigin.BothFromPerimeterCross)
        {
            if (totalSize.x > 2047f || totalSize.y > 2047f)
            {
                Debug.LogWarning($"{rectTransform.name} is too large for horizontal + vertical CutoutFill. Maximum size is 2047x2047 canvas units.");
                return;
            }
            CutoutRule = origin <= CutoutFillOrigin.BothFromPerimeter ? QuadData.SimpleCutoutRule.AND : QuadData.SimpleCutoutRule.OR;
            var fromPerimeter = percent > 0 && origin is CutoutFillOrigin.BothFromCenter or CutoutFillOrigin.BothFromCenterCross;
            InvertCutout = fromPerimeter;
            percent = fromPerimeter ? 1 - percent : percent;
            var absoluteCutoutSizeH = totalSize.x * percent * 0.5f;
            var absoluteCutoutSizeV = totalSize.y * percent * 0.5f;
            SimpleCutoutEdgeEnabled[0] = SimpleCutoutEdgeEnabled[1] = true;
            SimpleCutoutEdgeEnabled[2] = SimpleCutoutEdgeEnabled[3] = true;
            SimpleCutout = new Vector4(absoluteCutoutSizeH, absoluteCutoutSizeH, absoluteCutoutSizeV, absoluteCutoutSizeV);
        }
        else
        {
            if (totalSize.x > 1023.5f || totalSize.y > 1023.5f)
            {
                Debug.LogWarning($"{rectTransform.name} is too large for corner CutoutFill. Maximum size is 1023.5 x 1023.5 canvas units.");
                return;
            }

            CutoutRule = SimpleCutoutRule.AND;
            InvertCutout = false;

            var absoluteCutoutSizeH = totalSize.x * percent;
            var absoluteCutoutSizeV = totalSize.y * percent;

            SimpleCutoutEdgeEnabled[0] = SimpleCutoutEdgeEnabled[1] = SimpleCutoutEdgeEnabled[2] = SimpleCutoutEdgeEnabled[3] = true;
            switch (origin)
            {
                case CutoutFillOrigin.TopLeft:
                    SimpleCutout = new Vector4(absoluteCutoutSizeH, 0, absoluteCutoutSizeV, 0);
                    break;
                case CutoutFillOrigin.TopRight:
                    SimpleCutout = new Vector4(0, absoluteCutoutSizeH, absoluteCutoutSizeV, 0);
                    break;
                case CutoutFillOrigin.BottomLeft:
                    SimpleCutout = new Vector4(absoluteCutoutSizeH, 0, 0, absoluteCutoutSizeV);
                    break;
                case CutoutFillOrigin.BottomRight:
                    SimpleCutout = new Vector4(0, absoluteCutoutSizeH, 0, absoluteCutoutSizeV);
                    break;
            }
        }
    }

    public void EnableOutline()
    {
        _outlineConfig ??= new OutlineConfig();
        foreach (var state in proceduralAnimationStates)
            foreach (var props in state.proceduralProperties)
                props.outlineAnim ??= new OutlineAnimData();
        SetVerticesDirty();
        SetRayCastAreaDirty();
    }

    public void DisableOutline()
    {
        _outlineConfig = null;
        foreach (var state in proceduralAnimationStates)
            foreach (var props in state.proceduralProperties)
                props.outlineAnim = null;
        SetVerticesDirty();
        SetRayCastAreaDirty();
    }

    public void EnableGradient()
    {
        _gradientConfig ??= new GradientConfig();
        foreach (var state in proceduralAnimationStates)
            foreach (var props in state.proceduralProperties)
                props.gradientAnim ??= new GradientAnimData();
        SetVerticesDirty();
        SetRayCastAreaDirty();
    }

    public void DisableGradient()
    {
        _gradientConfig = null;
        foreach (var state in proceduralAnimationStates)
            foreach (var props in state.proceduralProperties)
                props.gradientAnim = null;
        SetVerticesDirty();
        SetRayCastAreaDirty();
    }

    public void EnablePattern()
    {
        _patternConfig ??= new PatternConfig();
        foreach (var state in proceduralAnimationStates)
            foreach (var props in state.proceduralProperties)
                props.patternAnim ??= new PatternAnimData();
        SetVerticesDirty();
        SetRayCastAreaDirty();
    }

    public void DisablePattern()
    {
        _patternConfig = null;
        foreach (var state in proceduralAnimationStates)
            foreach (var props in state.proceduralProperties)
                props.patternAnim = null;
        SetVerticesDirty();
        SetRayCastAreaDirty();
    }

    public void EnableCutout()
    {
        _cutoutConfig ??= new CutoutConfig();
        foreach (var state in proceduralAnimationStates)
            foreach (var props in state.proceduralProperties)
                props.cutoutAnim ??= new CutoutAnimData();
        SetVerticesDirty();
        SetRayCastAreaDirty();
    }

    public void DisableCutout()
    {
        _cutoutConfig = null;
        foreach (var state in proceduralAnimationStates)
            foreach (var props in state.proceduralProperties)
                props.cutoutAnim = null;
        SetVerticesDirty();
        SetRayCastAreaDirty();
    }

    public void EnableStroke()
    {
        _strokeConfig ??= new StrokeConfig();
        foreach (var state in proceduralAnimationStates)
            foreach (var props in state.proceduralProperties)
                props.strokeAnim ??= new StrokeAnimData();
        SetVerticesDirty();
        SetRayCastAreaDirty();
    }

    public void DisableStroke()
    {
        _strokeConfig = null;
        foreach (var state in proceduralAnimationStates)
            foreach (var props in state.proceduralProperties)
                props.strokeAnim = null;
        SetVerticesDirty();
        SetRayCastAreaDirty();
    }

    public void EnableSkew()
    {
        _skewConfig ??= new SkewConfig();
        foreach (var state in proceduralAnimationStates)
            foreach (var props in state.proceduralProperties)
                props.skewAnim ??= new SkewAnimData();
        SetVerticesDirty();
        SetRayCastAreaDirty();
    }

    public void DisableSkew()
    {
        _skewConfig = null;
        foreach (var state in proceduralAnimationStates)
            foreach (var props in state.proceduralProperties)
                props.skewAnim = null;
        SetVerticesDirty();
        SetRayCastAreaDirty();
    }

    public void OnBeforeSerialize() {}
    public void OnAfterDeserialize()
    {
        colorPresetCallbackAssigned = false;

        if (_colorPreset == null)
            return;

        _colorPreset.ColorChangeEvent -= SetVerticesDirty;
        _colorPreset.ColorChangeEvent += SetVerticesDirty;
        colorPresetCallbackAssigned = true;
    }
}
}
