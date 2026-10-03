using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using Color = UnityEngine.Color;
using Image = UnityEngine.UI.Image;
using Screen = UnityEngine.Screen;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace JeffGrawAssets.FlexibleUI
{
public partial class FlexibleImage : Image, IMeshModifier
{
    public const int MaxMeshSubdivisions = 5;
    private const string ImageShaderName = "Hidden/JeffGrawAssets/ProceduralBlurredImage";

    public static int ScreenSpacePointerPosID = Shader.PropertyToID("_ScreenSpacePointerPos");
    private static int ScaledScreenParamsID = Shader.PropertyToID("_ScaledScreenParams");

    private static readonly List<UIVertex> DefaultSimpleMeshVertexList = new()
    {
        new UIVertex { position = new Vector2(-0.5f, -0.5f)},
        new UIVertex { position = new Vector2(-0.5f, 0.5f), uv0 = new Vector2(0f, 1f)},
        new UIVertex { position = new Vector2(0.5f, 0.5f), uv0 = Vector2.one},
        new UIVertex { position = new Vector2(0.5f, 0.5f), uv0 = Vector2.one},
        new UIVertex { position = new Vector2(0.5f, -0.5f), uv0 = new Vector2(1f, 0f)},
        new UIVertex { position = new Vector2(-0.5f, -0.5f)},
    };

    [Flags] public enum AdvancedRaycastOptions { Size = 1, ChamferAndCollapse = 2, Stroke = 4, Cutout = 8, IgnoreOutline = 16, Offset = 32, Rotation = 64, UVRect = 128 }
    public enum QuadDataMode { Single, Multiple, Preset }
    public enum AnimationStateDrivenBy { Selectable, Script }

#if UNITY_EDITOR
    public static readonly string NormalRaycastPaddingFieldName = nameof(_normalRaycastPadding);
    public static readonly string MobileRaycastPaddingFieldName = nameof(_mobileRaycastPadding);
    public static readonly string UseAdvancedRaycastFieldName = nameof(_useAdvancedRaycast);
    public static readonly string AdvancedRaycastFlagsFieldName = nameof(_advancedRaycastFlags);
    public static readonly string DataModeFieldName = nameof(_dataMode);
    public static readonly string QuadDataContainerFieldName = nameof(_instanceQuadDataContainer);
    public static readonly string FlexibleImageDataPresetFieldName = nameof(quadDataPreset);

    public void SubscribeToActiveDataContainerFromInspector() => SubscribeToActiveContainer();
    public void UnsubscribeFromActiveDataContainerFromInspector() => UnsubscribeFromActiveContainer();
    public List<AnimationValues> GetAnimationValuesFromInspector => animationValues;
    public void PopulateAnimationValuesFromInspector()
    {
        var container = ActiveQuadDataContainer;
        var count = container.Count;
        animationValues = new List<AnimationValues>(count);
        for (int i = 0; i < count; i++)
        {
            animationValues.Add(new AnimationValues());
            animationValues[i].SetCurrentProps(container[i].DefaultProceduralProps, true);
        }
    }

    [ContextMenu("Bake To Texture", false, 9999)]
    private void BakeToTextureContextMenu() => TextureExportEditorWindow.ShowWindow(this);

    private Vector2 captureResolution = Vector2.zero;
    private string capturePath;
    private int superSample;
    private bool addPadding;

    public void BakeToTexture(int width, int height, int superSample, bool addPadding, string path = null)
    {
        captureResolution = new Vector2(width, height);
        capturePath = path;
        this.superSample = superSample;
        this.addPadding = addPadding;
        ForceMeshUpdateInEditor();
    }

    public void ForceMeshUpdateInEditor()
    {
        using var vh = new VertexHelper();
        OnPopulateMesh(vh);
        ModifyMesh(vh);
    }
#endif

    private static Shader _imageShader;
    private static Shader ImageShader
    {
        get
        {
            if (!_imageShader)
                _imageShader = Shader.Find(ImageShaderName);
            return _imageShader;
        }
    }

    private static Material _defaultProceduralBlurredImageMat;
    private static Material DefaultProceduralBlurredImageMat
    {
        get
        {
            if (!_defaultProceduralBlurredImageMat)
                _defaultProceduralBlurredImageMat = new Material(ImageShader) { name = "DefaultProceduralBlurredImage" };

            return _defaultProceduralBlurredImageMat;
        }
    }

    [SerializeField] private Vector4 _normalRaycastPadding;
    public Vector4 NormalRaycastPadding
    {
        get => _normalRaycastPadding;
        set
        {
            _normalRaycastPadding = value;
            rayCastAreaDirty = true;
        }
    }

    // Additional raycast padding that is only applied for Android and IOS. Useful when you want to only adjust the padding for touch devices while leaving desktop unmolested.
    [SerializeField] private Vector4 _mobileRaycastPadding;
    public Vector4 MobileRaycastPadding
    {
        get => _mobileRaycastPadding;
        set
        {
            _mobileRaycastPadding = value;
            rayCastAreaDirty = true;
        }
    }

    [SerializeField] private bool _useAdvancedRaycast;
    public bool UseAdvancedRaycast
    {
        get => _useAdvancedRaycast;
        set
        {
            _useAdvancedRaycast = value;
            rayCastAreaDirty = true;
        }
    }

    [SerializeField] private AdvancedRaycastOptions _advancedRaycastFlags = AdvancedRaycastOptions.ChamferAndCollapse | AdvancedRaycastOptions.Size | AdvancedRaycastOptions.UVRect;
    public AdvancedRaycastOptions AdvancedRaycastFlags
    {
        get => _advancedRaycastFlags;
        set
        {
            _advancedRaycastFlags = value;
            rayCastAreaDirty = true;
        }
    }

    [SerializeField] private QuadDataMode _dataMode;
    public QuadDataMode DataMode
    {
        get => _dataMode;
        set
        {
            if (value != QuadDataMode.Preset && _instanceQuadDataContainer.Count == 0)
                _instanceQuadDataContainer.AddQuadData();

            UnsubscribeFromActiveContainer();
            _dataMode = value;
            SubscribeToActiveContainer();
            SetVerticesDirty();
            if (UseAdvancedRaycast)
                rayCastAreaDirty = true;
        }
    }

    [SerializeField] private QuadDataPreset quadDataPreset;
    public QuadDataPreset QuadDataPreset
    {
        get => quadDataPreset;
        set
        {
            var activeContainerChanges = DataMode == QuadDataMode.Preset;
            if (activeContainerChanges)
                UnsubscribeFromActiveContainer();

            quadDataPreset = value;

            if (activeContainerChanges)
                SubscribeToActiveContainer();

            SetVerticesDirty();
            if (UseAdvancedRaycast)
                rayCastAreaDirty = true;
        }
    }

    // For "follower" helper scripts that let ordinary Image, Text, etc. UI components follow non-transform positional changes by modifying their own vertex positions.
    public Matrix4x4 FollowerTransformationMatrix { get; private set; }
    public Vector2 FinalVertexScale { get; private set; }
    public Vector2 FinalMeshCenter { get; private set; }

    [FormerlySerializedAs("_quadDataContainer")]
    [SerializeField] private QuadDataContainer _instanceQuadDataContainer = new();
    public QuadDataContainer InstanceQuadDataContainer { get => _instanceQuadDataContainer; set => _instanceQuadDataContainer = value; }

    // Helpers to easily get/set values on the primary quad / animation substate.
    public QuadDataContainer ActiveQuadDataContainer => DataMode == QuadDataMode.Preset && QuadDataPreset != null && QuadDataPreset.quadDataContainer != null ? QuadDataPreset.quadDataContainer : _instanceQuadDataContainer;
    public QuadData PrimaryQuadData => ActiveQuadDataContainer.PrimaryQuadData;
    public ProceduralProperties PrimaryProceduralProperties => PrimaryQuadData.DefaultProceduralProps;
    public ColorPreset ColorPreset { get => PrimaryQuadData.ColorPreset; set => PrimaryQuadData.ColorPreset = value; }
    public QuadData.QuadModifiers AdvancedQuadSettings { get => PrimaryQuadData.AdvancedQuadSettings; set => PrimaryQuadData.AdvancedQuadSettings = value; }
    public QuadData.CollapsedEdgeType CollapsedEdge { get => PrimaryQuadData.CollapsedEdge; set => PrimaryQuadData.CollapsedEdge = value; }
    public QuadData.SimpleCutoutRule SimpleCutoutRule { get => PrimaryQuadData.CutoutRule; set => PrimaryQuadData.CutoutRule = value; }
    public QuadData.FeatherMode SoftnessFeatherMode { get => PrimaryQuadData.SoftnessFeatherMode; set => PrimaryQuadData.SoftnessFeatherMode = value; }
    public QuadData.GradientType ProceduralGradientType { get => PrimaryQuadData.ProceduralGradientType; set => PrimaryQuadData.ProceduralGradientType = value; }
    public QuadData.PatternOriginPosition PatternOriginPos { get => PrimaryQuadData.PatternOriginPos; set => PrimaryQuadData.PatternOriginPos = value; }
    public QuadData.PatternType Pattern { get => PrimaryQuadData.Pattern; set => PrimaryQuadData.Pattern = value; }
    public QuadData.SpritePatternOffsetDirection SpritePatternOffsetDirectionDegrees { get => PrimaryQuadData.SpritePatternOffsetDirectionDegrees; set => PrimaryQuadData.SpritePatternOffsetDirectionDegrees = value; }
    public QuadData.SpritePatternRotation SpritePatternRotationMode { get => PrimaryQuadData.SpritePatternRotationMode; set => PrimaryQuadData.SpritePatternRotationMode = value; }
    public QuadData.StrokeOriginLocation StrokeOrigin { get => PrimaryQuadData.StrokeOrigin; set => PrimaryQuadData.StrokeOrigin = value; }
    public QuadData.Topology MeshTopology { get => PrimaryQuadData.MeshTopology; set => PrimaryQuadData.MeshTopology = value; }
    public Color[] OutlineColorsWithoutDirtying => PrimaryQuadData.OutlineColorsWithoutDirtying;
    public Color[] OutlineColors { get => PrimaryQuadData.OutlineColors; set => PrimaryQuadData.OutlineColors = value; }
    public Color[] PatternColorsWithoutDirtying => PrimaryQuadData.PatternColorsWithoutDirtying;
    public Color[] PatternColors { get => PrimaryQuadData.PatternColors; set => PrimaryQuadData.PatternColors = value; }
    public Color[] PrimaryColorsWithoutDirtying => PrimaryQuadData.PrimaryColorsWithoutDirtying;
    public Color[] PrimaryColors { get => PrimaryQuadData.PrimaryColors; set => PrimaryQuadData.PrimaryColors = value; }
    public Color[] ProceduralGradientWithoutDirtying => PrimaryQuadData.ProceduralGradientColorsWithoutDirtying;
    public Color[] ProceduralGradientColors { get => PrimaryQuadData.ProceduralGradientColors; set => PrimaryQuadData.ProceduralGradientColors = value; }
    public bool[] CutoutEnabled { get => PrimaryQuadData.SimpleCutoutEdgeEnabled; set => PrimaryQuadData.SimpleCutoutEdgeEnabled = value; }
    public Vector4 CornerChamfer { get => PrimaryQuadData.CornerChamfer; set => PrimaryQuadData.CornerChamfer = value; }
    public Vector4 CornerConcavity { get => PrimaryQuadData.CornerConcavity; set => PrimaryQuadData.CornerConcavity = value; }
    public Vector4 Cutout { get => PrimaryQuadData.SimpleCutout; set => PrimaryQuadData.SimpleCutout = value; }
    public Vector4 UVRect { get => PrimaryQuadData.UVRect; set => PrimaryQuadData.UVRect = value; }
    public Vector2 AnchoredPosition { get => PrimaryQuadData.AnchoredPosition; set => PrimaryQuadData.AnchoredPosition = value; }
    public Vector2 AnchorMax { get => PrimaryQuadData.AnchorMax; set => PrimaryQuadData.AnchorMax = value; }
    public Vector2 AnchorMin { get => PrimaryQuadData.AnchorMin; set => PrimaryQuadData.AnchorMin = value; }
    public Vector2 AngleGradientStrength { get => PrimaryQuadData.AngleGradientStrength; set => PrimaryQuadData.AngleGradientStrength = value; }
    public Vector2 Offset { get => PrimaryQuadData.Offset; set => PrimaryQuadData.Offset = value; }
    public Vector2 Pivot { get => PrimaryQuadData.Pivot; set => PrimaryQuadData.Pivot = value; }
    public Vector2 ProceduralGradientPosition { get => PrimaryQuadData.ProceduralGradientPosition; set => PrimaryQuadData.ProceduralGradientPosition = value; }
    public Vector2 SDFCutoutAnchorMin { get => PrimaryQuadData.SDFCutoutAnchorMin; set => PrimaryQuadData.SDFCutoutAnchorMin = value; }
    public Vector2 SDFCutoutAnchorMax { get => PrimaryQuadData.SDFCutoutAnchorMax; set => PrimaryQuadData.SDFCutoutAnchorMax = value; }
    public Vector2 SDFCutoutPivot { get => PrimaryQuadData.SDFCutoutPivot; set => PrimaryQuadData.SDFCutoutPivot = value; }
    public Vector2 SDFCutoutPosition { get => PrimaryQuadData.SDFCutoutPosition; set => PrimaryQuadData.SDFCutoutPosition = value; }
    public Vector2 SDFCutoutSize { get => PrimaryQuadData.SDFCutoutSize; set => PrimaryQuadData.SDFCutoutSize = value; }
    public Vector2 RadialGradientSize { get => PrimaryQuadData.RadialGradientSize; set => PrimaryQuadData.RadialGradientSize = value; }
    public Vector2 RawSizeModifier { get => PrimaryQuadData.RawSizeModifier; set => PrimaryQuadData.RawSizeModifier = value; }
    public Vector2 SizeDelta { get => PrimaryQuadData.SizeDelta; set => PrimaryQuadData.SizeDelta = value; }
    public Vector2 PrimaryColorOffset { get => PrimaryQuadData.PrimaryColorOffset; set => PrimaryQuadData.PrimaryColorOffset = value; }
    public Vector2 OutlineColorOffset { get => PrimaryQuadData.OutlineColorOffset; set => PrimaryQuadData.OutlineColorOffset = value; }
    public Vector2 ProceduralGradientColorOffset { get => PrimaryQuadData.ProceduralGradientColorOffset; set => PrimaryQuadData.ProceduralGradientColorOffset = value; }
    public Vector2 PatternColorOffset { get => PrimaryQuadData.PatternColorOffset; set => PrimaryQuadData.PatternColorOffset = value; }
    public Vector2 PrimaryColorScale { get => PrimaryQuadData.PrimaryColorScale; set => PrimaryQuadData.PrimaryColorScale = value; }
    public Vector2 OutlineColorScale { get => PrimaryQuadData.OutlineColorScale; set => PrimaryQuadData.OutlineColorScale = value; }
    public Vector2 ProceduralGradientColorScale { get => PrimaryQuadData.ProceduralGradientColorScale; set => PrimaryQuadData.ProceduralGradientColorScale = value; }
    public Vector2 PatternColorScale { get => PrimaryQuadData.PatternColorScale; set => PrimaryQuadData.PatternColorScale = value; }
    public Vector2Int OutlineColorDimensions { get => PrimaryQuadData.OutlineColorDimensions; set => PrimaryQuadData.OutlineColorDimensions = value; }
    public Vector2Int PatternColorDimensions { get => PrimaryQuadData.PatternColorDimensions; set => PrimaryQuadData.PatternColorDimensions = value; }
    public Vector2Int PrimaryColorDimensions { get => PrimaryQuadData.PrimaryColorDimensions; set => PrimaryQuadData.PrimaryColorDimensions = value; }
    public Vector2Int ProceduralGradientColorDimensions { get => PrimaryQuadData.ProceduralGradientColorDimensions; set => PrimaryQuadData.ProceduralGradientColorDimensions = value; }
    public float AngleGradientAngle { get => PrimaryQuadData.AngleGradientAngle; set => PrimaryQuadData.AngleGradientAngle = value; }
    public float CollapseEdgeAmount { get => PrimaryQuadData.CollapseEdgeAmount; set => PrimaryQuadData.CollapseEdgeAmount = value; }
    public float CollapseEdgeAmountAbsolute { get => PrimaryQuadData.CollapseEdgeAmountAbsolute; set => PrimaryQuadData.CollapseEdgeAmountAbsolute = value; }
    public float CollapseEdgePosition { get => PrimaryQuadData.CollapseEdgePosition; set => PrimaryQuadData.CollapseEdgePosition = value; }
    public float CollapsedCornerChamfer { get => PrimaryQuadData.CollapsedCornerChamfer; set => PrimaryQuadData.CollapsedCornerChamfer = value; }
    public float CollapsedCornerConcavity { get => PrimaryQuadData.CollapsedCornerConcavity; set => PrimaryQuadData.CollapsedCornerConcavity = value; }
    public float ConicalGradientTailStrength { get => PrimaryQuadData.ConicalGradientTailStrength; set => PrimaryQuadData.ConicalGradientTailStrength = value; }
    public float ConicalGradientCurvature { get => PrimaryQuadData.ConicalGradientCurvature; set => PrimaryQuadData.ConicalGradientCurvature = value; }
    public float OutlineColorPresetMix { get => PrimaryQuadData.OutlineColorPresetMix; set => PrimaryQuadData.OutlineColorPresetMix = value; }
    public float NoiseEdge { get => PrimaryQuadData.NoiseEdge; set => PrimaryQuadData.NoiseEdge = value; }
    public float NoiseScale { get => PrimaryQuadData.NoiseScale; set => PrimaryQuadData.NoiseScale = value; }
    public float NoiseStrength { get => PrimaryQuadData.NoiseStrength; set => PrimaryQuadData.NoiseStrength = value; }
    public float PatternCellParam { get => PrimaryQuadData.PatternCellParam; set => PrimaryQuadData.PatternCellParam = value; }
    public float PatternColorPresetMix { get => PrimaryQuadData.PatternColorPresetMix; set => PrimaryQuadData.PatternColorPresetMix = value; }
    public float PatternDensity { get => PrimaryQuadData.PatternDensity; set => PrimaryQuadData.PatternDensity = value; }
    public float PatternSpeed { get => PrimaryQuadData.PatternSpeed; set => PrimaryQuadData.PatternSpeed = value; }
    public float PrimaryColorPresetMix { get => PrimaryQuadData.PrimaryColorPresetMix; set => PrimaryQuadData.PrimaryColorPresetMix = value; }
    public float ProceduralGradientColorPresetMix { get => PrimaryQuadData.ProceduralGradientColorPresetMix; set => PrimaryQuadData.ProceduralGradientColorPresetMix = value; }
    public float RadialGradientStrength { get => PrimaryQuadData.RadialGradientStrength; set => PrimaryQuadData.RadialGradientStrength = value; }
    public float Rotation { get => PrimaryQuadData.Rotation; set => PrimaryQuadData.Rotation = value; }
    public float SDFGradientInnerDistance { get => PrimaryQuadData.SDFGradientInnerDistance; set => PrimaryQuadData.SDFGradientInnerDistance = value; }
    public float SDFGradientInnerReach { get => PrimaryQuadData.SDFGradientInnerReach; set => PrimaryQuadData.SDFGradientInnerReach = value; }
    public float SDFGradientOuterDistance { get => PrimaryQuadData.SDFGradientOuterDistance; set => PrimaryQuadData.SDFGradientOuterDistance = value; }
    public float SDFGradientOuterReach { get => PrimaryQuadData.SDFGradientOuterReach; set => PrimaryQuadData.SDFGradientOuterReach = value; }
    public float SDFCutoutRotation { get => PrimaryQuadData.SDFCutoutRotation; set => PrimaryQuadData.SDFCutoutRotation = value; }
    public float Softness { get => PrimaryQuadData.Softness; set => PrimaryQuadData.Softness = value; }
    public float Stroke { get => PrimaryQuadData.Stroke; set => PrimaryQuadData.Stroke = value; }
    public float PrimaryColorRotation { get => PrimaryQuadData.PrimaryColorRotation; set => PrimaryQuadData.PrimaryColorRotation = value; }
    public float OutlineColorRotation { get => PrimaryQuadData.OutlineColorRotation; set => PrimaryQuadData.OutlineColorRotation = value; }
    public float ProceduralGradientColorRotation { get => PrimaryQuadData.ProceduralGradientColorRotation; set => PrimaryQuadData.ProceduralGradientColorRotation = value; }
    public float PatternColorRotation { get => PrimaryQuadData.PatternColorRotation; set => PrimaryQuadData.PatternColorRotation = value; }
    public int MeshSubdivisions { get => PrimaryQuadData.MeshSubdivisions; set => PrimaryQuadData.MeshSubdivisions = value; }
    public int PatternSpriteRotation { get => PrimaryQuadData.PatternSpriteRotation; set => PrimaryQuadData.PatternSpriteRotation = value; }
    public uint NoiseSeed { get => PrimaryQuadData.NoiseSeed; set => PrimaryQuadData.NoiseSeed = value; }
    public byte PatternLineThickness { get => PrimaryQuadData.PatternLineThickness; set => PrimaryQuadData.PatternLineThickness = value; }
    public byte PrimaryColorFade { get => PrimaryQuadData.PrimaryColorFade; set => PrimaryQuadData.PrimaryColorFade = value; }
    public bool AddInteriorOutline { get => PrimaryQuadData.AddInteriorOutline; set => PrimaryQuadData.AddInteriorOutline = value; }
    public bool CollapseIntoParallelogram { get => PrimaryQuadData.CollapseIntoParallelogram; set => PrimaryQuadData.CollapseIntoParallelogram = value; }
    public bool MirrorCollapse { get => PrimaryQuadData.MirrorCollapse; set => PrimaryQuadData.MirrorCollapse = value; }
    public bool ConcavityIsSmoothing { get => PrimaryQuadData.ConcavityIsSmoothing; set => PrimaryQuadData.ConcavityIsSmoothing = value; }
    public bool SDFCutoutIsSquircle { get => PrimaryQuadData.SDFCutoutIsSquircle; set => PrimaryQuadData.SDFCutoutIsSquircle = value; }
    public QuadData.SDFCutoutMirrorMode SDFCutoutMirror { get => PrimaryQuadData.SDFCutoutMirror; set => PrimaryQuadData.SDFCutoutMirror = value; }
    public bool SDFCutoutMirrorIsDiagonal { get => PrimaryQuadData.SDFCutoutMirrorIsDiagonal; set => PrimaryQuadData.SDFCutoutMirrorIsDiagonal = value; }
    public bool SDFCutoutPositionIsAbsolute { get => PrimaryQuadData.SDFCutoutPositionIsAbsolute; set => PrimaryQuadData.SDFCutoutPositionIsAbsolute = value; }
    public bool SDFCutoutSizeIsAbsolute { get => PrimaryQuadData.SDFCutoutSizeIsAbsolute; set => PrimaryQuadData.SDFCutoutSizeIsAbsolute = value; }
    public bool SDFCutoutUsesAnchors { get => PrimaryQuadData.SDFCutoutUsesAnchors; set => PrimaryQuadData.SDFCutoutUsesAnchors = value; }
    public bool CutoutPositionIgnoresExpandedOutlines { get => PrimaryQuadData.CutoutPositionIgnoresExpandedOutlines; set => PrimaryQuadData.CutoutPositionIgnoresExpandedOutlines = value; }
    public bool CutoutOnlyAffectsOutline { get => PrimaryQuadData.CutoutOnlyAffectsOutline; set => PrimaryQuadData.CutoutOnlyAffectsOutline = value; }
    public bool EdgeCollapseAmountIsAbsolute { get => PrimaryQuadData.EdgeCollapseAmountIsAbsolute; set => PrimaryQuadData.EdgeCollapseAmountIsAbsolute = value; }
    public bool FitRotatedImageWithinBounds { get => PrimaryQuadData.FitRotatedImageWithinBounds; set => PrimaryQuadData.FitRotatedImageWithinBounds = value; }
    public bool HighlightedFix { get => PrimaryQuadData.highlightedFix; set => PrimaryQuadData.highlightedFix = value; }
    public bool InvertCutout { get => PrimaryQuadData.InvertCutout; set => PrimaryQuadData.InvertCutout = value; }
    public bool NormalizeChamfer { get => PrimaryQuadData.NormalizeChamfer; set => PrimaryQuadData.NormalizeChamfer = value; }
    public bool OutlineAccommodatesCollapsedEdge { get => PrimaryQuadData.OutlineAccommodatesCollapsedEdge; set => PrimaryQuadData.OutlineAccommodatesCollapsedEdge = value; }
    public bool OutlineAdjustsChamfer { get => PrimaryQuadData.OutlineAdjustsChamfer; set => PrimaryQuadData.OutlineAdjustsChamfer = value; }
    public bool OutlineAlphaIsBlend { get => PrimaryQuadData.OutlineAlphaIsBlend; set => PrimaryQuadData.OutlineAlphaIsBlend = value; }
    public bool OutlineExpandsOutward { get => PrimaryQuadData.OutlineExpandsOutward; set => PrimaryQuadData.OutlineExpandsOutward = value; }
    public bool OutlineFadeTowardsInterior { get => PrimaryQuadData.OutlineFadeTowardsInterior; set => PrimaryQuadData.OutlineFadeTowardsInterior = value; }
    public bool PatternAffectsInterior { get => PrimaryQuadData.PatternAffectsInterior; set => PrimaryQuadData.PatternAffectsInterior = value; }
    public bool PatternAffectsOutline { get => PrimaryQuadData.PatternAffectsOutline; set => PrimaryQuadData.PatternAffectsOutline = value; }
    public bool PatternColorAlphaIsBlend { get => PrimaryQuadData.PatternColorAlphaIsBlend; set => PrimaryQuadData.PatternColorAlphaIsBlend = value; }
    public bool ProceduralGradientAffectsInterior { get => PrimaryQuadData.ProceduralGradientAffectsInterior; set => PrimaryQuadData.ProceduralGradientAffectsInterior = value; }
    public bool ProceduralGradientAffectsOutline { get => PrimaryQuadData.ProceduralGradientAffectsOutline; set => PrimaryQuadData.ProceduralGradientAffectsOutline = value; }
    public bool ProceduralGradientAlphaIsBlend { get => PrimaryQuadData.ProceduralGradientAlphaIsBlend; set => PrimaryQuadData.ProceduralGradientAlphaIsBlend = value; }
    public bool ProceduralGradientAspectCorrection { get => PrimaryQuadData.ProceduralGradientAspectCorrection; set => PrimaryQuadData.ProceduralGradientAspectCorrection = value; }
    public bool ProceduralGradientInvert { get => PrimaryQuadData.ProceduralGradientInvert; set => PrimaryQuadData.ProceduralGradientInvert = value; }
    public bool ProceduralGradientPositionFromPointer { get => PrimaryQuadData.ProceduralGradientPositionFromPointer; set => PrimaryQuadData.ProceduralGradientPositionFromPointer = value; }
    public bool ScanlinePatternSpeedIsStaticOffset { get => PrimaryQuadData.ScanlinePatternSpeedIsStaticOffset; set => PrimaryQuadData.ScanlinePatternSpeedIsStaticOffset = value; }
    public bool SoftPattern { get => PrimaryQuadData.SoftPattern; set => PrimaryQuadData.SoftPattern = value; }
    public bool ScreenSpacePattern { get => PrimaryQuadData.ScreenSpacePattern; set => PrimaryQuadData.ScreenSpacePattern = value; }
    public bool ScreenSpaceProceduralGradient { get => PrimaryQuadData.ScreenSpaceProceduralGradient; set => PrimaryQuadData.ScreenSpaceProceduralGradient = value; }
    public bool SizeModifierAspectCorrection { get => PrimaryQuadData.SizeModifierAspectCorrection; set => PrimaryQuadData.SizeModifierAspectCorrection = value; }
    public new Color color { get => GetPrimaryColorAtCell(0, 0); set => SetPrimaryColorAtCell(0, 0, value); } // Hides the base Image color, which is not used by FlexibleImage, and instead gets/sets the first primary color cell.
    public Color GetPrimaryColorAtCell(int x, int y) => PrimaryQuadData.GetPrimaryColorAtCell(x, y);
    public Color GetOutlineColorAtCell(int x, int y) => PrimaryQuadData.GetOutlineColorAtCell(x, y);
    public Color GetProceduralGradientColorAtCell(int x, int y) => PrimaryQuadData.GetProceduralGradientColorAtCell(x, y);
    public Color GetPatternColorAtCell(int x, int y) => PrimaryQuadData.GetPatternColorAtCell(x, y);
    public void CutoutFill(QuadData.CutoutFillOrigin origin, float percent) => PrimaryQuadData.CutoutFill(rectTransform, origin, percent);
    public bool SetPrimaryColorAtCell(int x, int y, Color c) => PrimaryQuadData.SetPrimaryColorAtCell(x, y, c);
    public bool SetOutlineColorAtCell(int x, int y, Color c) => PrimaryQuadData.SetOutlineColorAtCell(x, y, c);
    public bool SetProceduralGradientColorAtCell(int x, int y, Color c) => PrimaryQuadData.SetProceduralGradientColorAtCell(x, y, c);
    public bool SetPatternColorAtCell(int x, int y, Color c) => PrimaryQuadData.SetPatternColorAtCell(x, y, c);
    public void SetPrimaryColorWrapMode(QuadData.ColorGridWrapMode xWrapMode, QuadData.ColorGridWrapMode yWrapMode) { PrimaryQuadData.PrimaryColorWrapModeX = xWrapMode; PrimaryQuadData.PrimaryColorWrapModeY = yWrapMode; }
    public void SetOutlineColorWrapMode(QuadData.ColorGridWrapMode xWrapMode, QuadData.ColorGridWrapMode yWrapMode) { PrimaryQuadData.OutlineColorWrapModeX = xWrapMode; PrimaryQuadData.OutlineColorWrapModeY = yWrapMode; }
    public void SetProceduralGradientColorWrapMode(QuadData.ColorGridWrapMode xWrapMode, QuadData.ColorGridWrapMode yWrapMode) { PrimaryQuadData.ProceduralGradientColorWrapModeX = xWrapMode; PrimaryQuadData.ProceduralGradientColorWrapModeY = yWrapMode; }
    public void SetPatternColorWrapMode(QuadData.ColorGridWrapMode xWrapMode, QuadData.ColorGridWrapMode yWrapMode) { PrimaryQuadData.PatternColorWrapModeX = xWrapMode; PrimaryQuadData.PatternColorWrapModeY = yWrapMode; }

    public AnimationStateDrivenBy animationMode = AnimationStateDrivenBy.Selectable;
    public Selectable selectable;
    [NonSerialized] public int scriptDrivenAnimationState;

    /* Workaround for dumb Unity thing.
    So: Classes that implement ICanvasRaycastFilter implement an IsRaycastLocationValid function that ostensibly does a few different things depending on the implementer.
    For image components, they use it to alpha test pixels on a sprite so that under a certain alpha threshold, the image component won't capture a click.
    It doesn't test for what you would expect, which is whether the pointer is inside the UI rect. No, that is tested somewhere else. If you aren't alpha testing, it just returns true.
    Except for Masks, which also implement ICanvasRaycastFilter, where whether the pointer is inside the UI rect is the *only* thing that is tested. Uh...???
    So is IsRaycastLocationValid being used completely differently for Images vs Masks? Is it wholly redundant for Masks?
    In either case pee-ew, bad design smell! But I digress...
    The whole alpha test thing is half-baked because when used it breaks basic functionality for child components.
    So, for example, if you were to parent an image to another image so that neither image overlaps, usually the pointer could interact properly with the child.
    But if the parent is alpha-tested and has transparent pixels on its boundary, now the child can never be interacted with.
    Because when you test the child both the parent and child are tested, and if *either* returns false the whole thing fails.
    So obviously, all that unfortunate crap also applies when we use IsRaycastLocationValid to try to conform the raycast area to the shape of a procedural image.
    It works perfectly fine for leaf GameObjects, but breaks children when used for parents.
    But here's the thing: I can't see any reason why you would ever need to test the parent if the child has already passed its own test.
    And the child is tested first.
    So this hack basically forces the raycast to return true for parents if a child has already returned true. Also fixes issues with alpha test.
    And I expected something would break. But no, as far as I can tell nothing ever does. Masking still works....
    And even complex scenarios like a masked child component with other components in front of and behind the parent work perfectly.
    But this workaround only works for FlexibleImages. If a child is a regular Image then all the same broken crap still applies.
    If there is some scenario that I missed that breaks terribly, please let me know! */
    private static int lastSucessfulRaycastFrame = -1;
    private static int lastFrameShadersHandled = -1;

    private static bool isURP;

    private static readonly Vector2 UVHalf = new(0.5f, 0.5f);
    private static readonly Func<Selectable, int> GetSelectableStateDel;
    private static readonly Func<Selectable, bool> GetSelectablePointerInsideDel;
    private static readonly Func<Selectable, bool> GetSelectablePointerDownDel;
    private static readonly Func<List<UIVertex>, UIVertex[]> GetUIVertexListArrayDel;
    private static readonly Action<List<UIVertex>, int> SetListArraySizeDel;

    private List<AnimationValues> animationValues;
    private Vector3 prevScale;
    private Vector2 prevWidthHeight;
    private bool rayCastAreaDirty = true;

    static FlexibleImage()
    {
        try
        {
            var property = typeof(Selectable).GetProperty("currentSelectionState", BindingFlags.NonPublic | BindingFlags.Instance);
            GetSelectableStateDel = (Func<Selectable, int>)Delegate.CreateDelegate(typeof(Func<Selectable, int>), property.GetGetMethod(true));
            property = typeof(Selectable).GetProperty("isPointerInside", BindingFlags.NonPublic | BindingFlags.Instance);
            GetSelectablePointerInsideDel = (Func<Selectable, bool>)Delegate.CreateDelegate(typeof(Func<Selectable, bool>), property.GetGetMethod(true));
            property = typeof(Selectable).GetProperty("isPointerDown", BindingFlags.NonPublic | BindingFlags.Instance);
            GetSelectablePointerDownDel = (Func<Selectable, bool>)Delegate.CreateDelegate(typeof(Func<Selectable, bool>), property.GetGetMethod(true));
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to find Selectable properties via reflection. Member signatures may have changed!\n{e}");
        }

        var field = typeof(List<UIVertex>).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);
        var listParam = Expression.Parameter(typeof(List<UIVertex>), "list");
        var fieldAccess = Expression.Field(listParam, field);
        GetUIVertexListArrayDel = Expression.Lambda<Func<List<UIVertex>, UIVertex[]>>(fieldAccess, listParam).Compile();

        field = typeof(List<UIVertex>).GetField("_size", BindingFlags.NonPublic | BindingFlags.Instance);
        var valueParam = Expression.Parameter(typeof(int), "value");
        var sizeAssign = Expression.Assign(Expression.Field(listParam, field), valueParam);
        SetListArraySizeDel = Expression.Lambda<Action<List<UIVertex>, int>>(sizeAssign, listParam, valueParam).Compile();
    }

    partial void GetAlphaBlend(ref byte alphaBlend);
    partial void GetSourceImageFade(ref byte sourceImageFade);

    protected override void Awake()
    {
        base.Awake();
        material = DefaultProceduralBlurredImageMat;

        if (lastFrameShadersHandled >= 0)
            return;

        lastFrameShadersHandled = 0;
        var pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline == null)
        {
            isURP = false;
        }
        else
        {
            var assembly = pipeline.GetType().Assembly;
            isURP = assembly.GetName().Name.Contains("universal", StringComparison.InvariantCultureIgnoreCase);
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorApplication.update += () =>
            {
                var viewDimensions = GetViewportDimensions();
                Shader.SetGlobalVector(ScaledScreenParamsID, new Vector4(viewDimensions.x, viewDimensions.y, 1f + 1f / viewDimensions.x, 1f + 1f / viewDimensions.y));
            };
        }
#endif
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SubscribeToActiveContainer();
        OnEnableBlur();
    }

    partial void OnEnableBlur();

    protected override void OnDisable()
    {
        base.OnDisable();
        UnsubscribeFromActiveContainer();
        OnDisableBlur();
    }
    partial void OnDisableBlur();

    private void SubscribeToActiveContainer()
    {
        var activeContainer = ActiveQuadDataContainer;
        activeContainer.VerticesDirtyEvent += SetVerticesDirty;
        activeContainer.RaycastAreaDirtyEvent += SetRaycastDirtyIfApplicable;
    }

    private void UnsubscribeFromActiveContainer()
    {
        var activeContainer = ActiveQuadDataContainer;
        activeContainer.VerticesDirtyEvent -= SetVerticesDirty;
        activeContainer.RaycastAreaDirtyEvent -= SetRaycastDirtyIfApplicable;
    }

    private void SetRaycastDirtyIfApplicable(AdvancedRaycastOptions flags)
    {
        if (AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.Rotation))
            rayCastAreaDirty = true;
    }

#if UNITY_EDITOR
    protected override void Reset()
    {
        base.Reset();
        material = DefaultProceduralBlurredImageMat;
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        material = DefaultProceduralBlurredImageMat;
        SetVerticesDirty();
        OnValidateBlur();
    }
    partial void OnValidateBlur();
#endif

    private void ValidateCanvasShaderChannels()
    {
        if (!canvas)
            return;

        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord2;
        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord3;
    }

    public void SetRaycastAreaDirty(bool immediate = false)
    {
        rayCastAreaDirty = true;
        if (immediate)
            CheckRaycastArea();
    }

    void Update()
    {
        CheckRaycastArea();
        CalculateBlur();

        if (lastFrameShadersHandled == Time.frameCount)
            return;

        lastFrameShadersHandled = Time.frameCount;

        var viewDimensions = GetViewportDimensions();
        if (!isURP)
            Shader.SetGlobalVector(ScaledScreenParamsID, new Vector4(viewDimensions.x, viewDimensions.y, 1f + 1f / viewDimensions.x, 1f + 1f / viewDimensions.y));

        if (!Application.isPlaying)
            return;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var pointerPos = UnityEngine.InputSystem.Pointer.current?.position?.ReadValue() ?? Vector2.negativeInfinity;
#else
        var pointerPos = Input.mousePosition;
#endif
        Shader.SetGlobalVector(ScreenSpacePointerPosID, new Vector4(pointerPos.x, pointerPos.y, pointerPos.x / viewDimensions.x, pointerPos.y / viewDimensions.y));
    }

    public bool DisplayAnimationStateFromInspector(int state, int substate)
    {
        bool dirtied = false;
        if (state == 0 && substate == 0 || substate < 0)
        {
            dirtied = animationValues != null;
            if (dirtied)
                SetVerticesDirty();

            animationValues = null;
            return dirtied;
        }

        var quadContainer = ActiveQuadDataContainer;
        if (animationValues == null || animationValues.Count != quadContainer.Count)
        {
            animationValues = new List<AnimationValues>(quadContainer.Count);
            for (int i = 0; i < quadContainer.Count; i++)
                animationValues.Add(new AnimationValues());
        }

        for (int i = 0; i < quadContainer.Count; i++)
        {
            var quad = quadContainer[i];
            var animationState = quad.proceduralAnimationStates[state];
            var propCount = animationState.proceduralProperties.Count;
            var props = substate < propCount 
                ? animationState.proceduralProperties[substate] 
                : propCount > 0 ? animationState.proceduralProperties[^1] 
                                : quad.DefaultProceduralProps;

            if (animationValues[i].CurrentProperties == props)
                continue;

            dirtied = true;
            animationValues[i].SetCurrentProps(props, false);
            SetVerticesDirty();
        }

        return dirtied;
    }

    private void LateUpdate()
    {
        if (Application.isPlaying)
            ComputeAnimation();

        if (PrimaryQuadData.Rotation != 0)
        {
            if (prevScale != transform.lossyScale)
            {
                prevScale = transform.lossyScale;
                SetVerticesDirty();
            }

            if (AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.Rotation))
            {
                var widthHeight = new Vector2(rectTransform.rect.width, rectTransform.rect.height);
                if (prevWidthHeight != widthHeight)
                {
                    prevWidthHeight = widthHeight;
                    rayCastAreaDirty = true;
                }
            }
        }

        LateUpdateBlur();
    }
    partial void LateUpdateBlur();

    partial void CalculateBlur();

    private Vector2Int GetViewportDimensions()
    {
#if UNITY_EDITOR
        var mainCamera = Camera.main;
        return mainCamera != null ? new Vector2Int(mainCamera.pixelWidth, mainCamera.pixelHeight) : new Vector2Int(Screen.width, Screen.height);
#else
        return new Vector2Int(Screen.width, Screen.height);
#endif
    }

    private void ComputeAnimation()
    {
        var drivenBySelectable = animationMode == AnimationStateDrivenBy.Selectable;
        if (drivenBySelectable && selectable == null)
        {
            animationValues = null;
            return;
        }

        var quadContainer = ActiveQuadDataContainer;
        if (animationValues == null || animationValues.Count != quadContainer.Count)
        {
            animationValues = new List<AnimationValues>(quadContainer.Count);
            for (int i = 0; i < quadContainer.Count; i++)
            {
                animationValues.Add(new AnimationValues());
                animationValues[i].SetCurrentProps(quadContainer[i].DefaultProceduralProps, false);
            }
        }

        int currentSelectionState;
        bool pointerDown, pointerInside;
        if (drivenBySelectable)
        {

            currentSelectionState = GetSelectableStateDel(selectable);
            if (currentSelectionState < 0)
                return;

            pointerInside = GetSelectablePointerInsideDel(selectable);
            pointerDown = GetSelectablePointerDownDel(selectable);
        }
        else
        {
            if (scriptDrivenAnimationState < 0)
                return;

            currentSelectionState = Mathf.Min(scriptDrivenAnimationState, 4); // We still only use 5 possible states, even when not driven by a selectable, to keep things simple.
            pointerInside = pointerDown = false;
        }

        for (int i = 0; i < quadContainer.Count; i++)
        {
            if (quadContainer[i].SetAnimationValues(animationValues[i], currentSelectionState, pointerInside, pointerDown))
                SetVerticesDirty();
        }
    }

    private static float EncodeFloat16_16(Vector2 input) => EncodeFloat16_16(input.x, input.y);
    private static float EncodeFloat16_16(float a, float b)
    {
        uint ea = (uint)(a * 65535f) & 65535;
        uint eb = (uint)(b * 65535f) & 65535;
        uint packed = (ea << 16) | eb;
        return PunUintToFloat(packed);
    }

    private static float EncodeFloat15_15_FixedPoint(Vector2 input, int fracBits) => EncodeFloat15_15_FixedPoint(input.x, input.y, fracBits);
    public static float EncodeFloat15_15_FixedPoint(float a, float b, int fracBits)
    {
        uint ah = ToUnsignedFixed15(a);
        uint bl = ToUnsignedFixed15(b);
        uint packed = ((ah >> 8) << 24) | ((ah & 255) << 15) | (bl & 32767);
        return PunUintToFloat(packed);

        uint ToUnsignedFixed15(float v)
        {
            var scale = 1 << fracBits;
            var scaled = Mathf.Round(Mathf.Max(0f, v) * scale);
            scaled = Mathf.Min(scaled, 32767f);
            return (uint)scaled;
        }
    }

    private static float EncodeFloat16_16_FixedPoint(Vector2 input, int fracBits) => EncodeFloat16_16_FixedPoint(input.x, input.y, fracBits);
    public static float EncodeFloat16_16_FixedPoint(float a, float b, int fracBits)
    {
        uint ah = ToUnsignedFixed16(a);
        uint bl = ToUnsignedFixed16(b);
        uint packed = (ah << 16) | (bl & 65535);
        return PunUintToFloat(packed);

        uint ToUnsignedFixed16(float v)
        {
            var scale = 1 << fracBits;
            var scaled = Mathf.Round(Mathf.Max(0f, v) * scale);
            scaled = Mathf.Min(scaled, 65535f);
            return (uint)scaled;
        }
    }

    private static float EncodeFloat12_12_5_3(Vector4 input) => EncodeFloat12_12_5_3(input.x, input.y, input.z, input.w);
    private static float EncodeFloat12_12_5_3(float a, float b, float c, float d)
    {
        uint ea = (uint)(a * 4095f) & 4095;
        uint eb = (uint)(b * 4095f) & 4095;
        uint ec = (uint)Math.Floor(c * 31f + 0.5f) & 31;
        uint ed = (uint)Math.Floor(d * 7f + 0.5f) & 7;
        uint packed = (ea << 20) | (eb << 8) | (ec << 3) | ed;
        return PunUintToFloat(packed);
    }

    private static float EncodeFloat16_14_2(Vector3 input) => EncodeFloat16_14_2(input.x, input.y, input.z);
    private static float EncodeFloat16_14_2(float a, float b, float c)
    {
        uint ea = (uint)(a * 65535f) & 65535;
        uint eb = (uint)(b * 16383f) & 16383;
        uint ec = (uint)(c * 3f) & 3;
        uint packed = (ea << 16) | (eb << 2) | ec;
        return PunUintToFloat(packed);
    }

    private static float EncodeFloat8_8_8_8(Vector4 input) => EncodeFloat8_8_8_8(input.x, input.y, input.z, input.w);
    private static float EncodeFloat8_8_8_8_Clamped(Vector4 input) => EncodeFloat8_8_8_8(Mathf.Clamp01(input.x), Mathf.Clamp01(input.y), Mathf.Clamp01(input.z), Mathf.Clamp01(input.w));
    private static float EncodeFloat8_8_8_8(float a, float b, float c, float d)
    {
        uint ea = (uint)Math.Floor(a * 255f + 0.5f) & 255;
        uint eb = (uint)Math.Floor(b * 255f + 0.5f) & 255;
        uint ec = (uint)Math.Floor(c * 255f + 0.5f) & 255;
        uint ed = (uint)Math.Floor(d * 255f + 0.5f) & 255;
        uint packed = (ea << 24) | (eb << 16) | (ec << 8) | ed;
        return PunUintToFloat(packed, 24);
    }

    private static float EncodeFloat12_12_8(Vector3 input) => EncodeFloat12_12_8(input.x, input.y, input.z);
    private static float EncodeFloat12_12_8(float a, float b, float c)
    {
        uint ea = (uint)(a * 4095f) & 4095;
        uint eb = (uint)(b * 4095f) & 4095;
        uint ec = (uint)Math.Floor(c * 255f + 0.5f) & 255;
        uint packed = (ea << 20) | (eb << 8) | ec;
        return PunUintToFloat(packed);
    }

    private static float EncodeFloat8_12_12(Vector3 input) => EncodeFloat8_12_12(input.x, input.y, input.z);
    private static float EncodeFloat8_12_12(float a, float b, float c)
    {
        uint ea = (uint)Math.Floor(a * 255f + 0.5f) & 255;
        uint eb = (uint)(b * 4095f) & 4095;
        uint ec = (uint)(c * 4095f) & 4095;
        uint packed = (ea << 24) | (eb << 12) | ec;
        return PunUintToFloat(packed, 24);
    }

    // If only Unity didn't force us to pass floats for vertex attributes, this could all be avoided...
    // Anyway, basically returns a float that is bit-equivalent to a uint, since HLSL land doesn't care about types in C# land, just the raw bits.
    // BUT the runtime may (or may not) molest bits when a float is NaN, so when we detect a flipped bit, we can "sacrifice" an exponent bit (set it to 0) so that the result is now non-NaN.
    // With proper care, this "sacrifice" may be so trivial as to be unnoticeable. But with improper care, it could create an obviously broken output.
    private static float PunUintToFloat(uint input, byte sacrificeBit = 23)
    {
        var f = MemoryMarshal.Cast<uint, float>(MemoryMarshal.CreateSpan(ref input, 1))[0];
        var roundtrip = MemoryMarshal.Cast<float, uint>(MemoryMarshal.CreateSpan(ref f, 1))[0];

        if (roundtrip == input)
            return f;

        if (sacrificeBit is < 23 or > 30)
            throw new ArgumentOutOfRangeException(nameof(sacrificeBit), $"{nameof(sacrificeBit)} must be in the exponent field (23–30).");

        //Debug.Log("Performed bit sacrifice!");
        var fixedBits = input ^ (1u << sacrificeBit);
        return MemoryMarshal.Cast<uint, float>(MemoryMarshal.CreateSpan(ref fixedBits, 1))[0];
    }

    public virtual void ModifyMesh(Mesh mesh)
    {
        using var vh = new VertexHelper(mesh);
        ModifyMesh(vh);
        vh.FillMesh(mesh);
    }

    public virtual void ModifyMesh(VertexHelper vh)
    {
        if (canvas == null || rectTransform.rect.width <= 0 || rectTransform.rect.height <= 0)
            return;

        ValidateCanvasShaderChannels();

        var container = ActiveQuadDataContainer;
        var generateFilledMesh = false;
        if (type == Type.Filled)
        {
            if (sprite == null)
            {
                generateFilledMesh = true;
            }
            else for (int i = 0; i < container.Count; i++) // If using the sprite as a pattern, then we don't want tight sprite packing to affect the mesh.
            {
                if (container[i].Enabled && container[i].Pattern == QuadData.PatternType.Sprite && sprite.packingMode == SpritePackingMode.Tight && container[i].PatternDensity > 0)
                {
                    generateFilledMesh = true;
                    break;
                }
            }
        }

        if (generateFilledMesh)
            FilledMeshHelper.GenerateFilledMesh(vh, this);

        var (rectWidth, rectHeight) = (rectTransform.rect.width, rectTransform.rect.height);
        var proceduralGradientSizeParams = Vector2.zero;
        var secondaryGradientStrengthOrAngle = 0f;

        var baseVertices = UnityEngine.Pool.ListPool<UIVertex>.Get();
        vh.GetUIVertexStream(baseVertices);

#if UNITY_EDITOR
        var quadVertexCounts = UnityEngine.Pool.ListPool<int>.Get();
#endif
        var useAnimationValues = animationValues != null && animationValues.Count == container.Count;
        var totalVertices = UnityEngine.Pool.ListPool<UIVertex>.Get();

        for (int i = 0; i < container.Count; i++)
        {
            var quadData = container[i];
            if (!quadData.Enabled)
                continue;

            var isDefaultQuadData = i == container.primaryQuadIdx;
            if (DataMode == QuadDataMode.Single && !isDefaultQuadData)
                continue;

            var defaultProps = quadData.DefaultProceduralProps;
            if (useAnimationValues)
                animationValues[i].RemoveDisabledModules(quadData);
            var animationProps = useAnimationValues ? animationValues[i].CurrentProperties : null;

            var (uvRect, offset, softness, rotation, cutout, collapseEdgeAmountRelative, collapseEdgeAmountAbsolute, collapseEdgePosition, proceduralGradientPosition, radialGradientSize, radialGradientStrength, angleGradientStrength, angleGradientAngle, sdfGradientInnerDistance, sdfGradientOuterDistance, sdfGradientInnerReach, sdfGradientOuterReach, conicalGradientCurvature, conicalGradientTailStrength, noiseSeed, noiseScale, noiseEdge, noiseStrength, proceduralGradientPointerStrength, patternDensity, patternSpeed, patternCellParam, patternLineThickness, patternSpriteRotation, primaryColorFade, primaryColors, primaryColorOffset, primaryColorRotation, primaryColorScale, outlineColors, outlineColorOffset, outlineColorRotation, outlineColorScale, proceduralGradientColors, proceduralGradientColorOffset, proceduralGradientColorRotation, proceduralGradientColorScale, patternColors, patternColorOffset, patternColorRotation, patternColorScale) = useAnimationValues
                ? (animationProps.uvRect, animationProps.offset, animationProps.softness, animationProps.rotation, cutout: animationProps.simpleCutout, animationProps.collapseEdgeAmount, animationProps.collapseEdgeAmountAbsolute, animationProps.collapseEdgePosition, animationProps.proceduralGradientPosition, animationProps.radialGradientSize, animationProps.radialGradientStrength, animationProps.angleGradientStrength, angleGradientAngle: animationProps.proceduralGradientAngle, animationProps.sdfGradientInnerDistance, animationProps.sdfGradientOuterDistance, animationProps.sdfGradientInnerReach, animationProps.sdfGradientOuterReach, animationProps.conicalGradientCurvature, animationProps.conicalGradientTailStrength, animationProps.noiseSeed, animationProps.noiseScale, animationProps.noiseEdge, animationProps.noiseStrength, animationProps.proceduralGradientPointerStrength, animationProps.patternDensity, animationProps.patternSpeed, animationProps.patternCellParam, animationProps.patternLineThickness, (int)Mathf.Repeat(animationProps.patternSpriteRotation, 360), animationProps.primaryColorFade, animationProps.primaryColors, animationProps.primaryColorOffset, animationProps.primaryColorRotation, animationProps.primaryColorScale, animationProps.outlineColors, animationProps.outlineColorOffset, animationProps.outlineColorRotation, animationProps.primaryColorScale, animationProps.proceduralGradientColors, animationProps.proceduralGradientColorOffset, animationProps.proceduralGradientColorRotation, animationProps.proceduralGradientColorScale, animationProps.patternColors, animationProps.patternColorOffset, animationProps.patternColorRotation, animationProps.patternColorScale)
                : (defaultProps.uvRect,   defaultProps.offset,   defaultProps.softness,   defaultProps.rotation,   cutout: defaultProps.simpleCutout,   defaultProps.collapseEdgeAmount,   defaultProps.collapseEdgeAmountAbsolute,   defaultProps.collapseEdgePosition,   defaultProps.proceduralGradientPosition,   defaultProps.radialGradientSize,   defaultProps.radialGradientStrength,   defaultProps.angleGradientStrength,   angleGradientAngle: defaultProps.proceduralGradientAngle,   defaultProps.sdfGradientInnerDistance,   defaultProps.sdfGradientOuterDistance,   defaultProps.sdfGradientInnerReach,   defaultProps.sdfGradientOuterReach,   defaultProps.conicalGradientCurvature,   defaultProps.conicalGradientTailStrength,   defaultProps.noiseSeed,   defaultProps.noiseScale,   defaultProps.noiseEdge,   defaultProps.noiseStrength,   defaultProps.proceduralGradientPointerStrength,   defaultProps.patternDensity,   defaultProps.patternSpeed,   defaultProps.patternCellParam,   defaultProps.patternLineThickness,   (int)Mathf.Repeat(defaultProps.patternSpriteRotation, 360),   defaultProps.primaryColorFade,   defaultProps.primaryColors,   defaultProps.primaryColorOffset,   defaultProps.primaryColorRotation,   defaultProps.primaryColorScale,   defaultProps.outlineColors,   defaultProps.outlineColorOffset,   defaultProps.outlineColorRotation,   defaultProps.outlineColorScale,   defaultProps.proceduralGradientColors,   defaultProps.proceduralGradientColorOffset,   defaultProps.proceduralGradientColorRotation,   defaultProps.proceduralGradientColorScale,   defaultProps.patternColors,   defaultProps.patternColorOffset,   defaultProps.patternColorRotation,   defaultProps.patternColorScale);

            var (sdfCutoutChamfer, sdfCutoutConcavity) = useAnimationValues ? (animationProps.sdfCutoutChamfer, animationProps.sdfCutoutConcavity) : (defaultProps.sdfCutoutChamfer, defaultProps.sdfCutoutConcavity);
            var (sdfCutoutPosition, sdfCutoutSize, sdfCutoutRotation) = useAnimationValues
                ? (animationProps.sdfCutoutPosition, animationProps.sdfCutoutSize, animationProps.sdfCutoutRotation)
                : (defaultProps.sdfCutoutPosition, defaultProps.sdfCutoutSize, defaultProps.sdfCutoutRotation);

            // Make sure color arrays are properly sized
            if (primaryColors.Length != ProceduralProperties.Colors1dArrayLength)
                Array.Resize(ref primaryColors, ProceduralProperties.Colors1dArrayLength);
            if (outlineColors.Length != ProceduralProperties.Colors1dArrayLength)
                Array.Resize(ref outlineColors, ProceduralProperties.Colors1dArrayLength);
            if (proceduralGradientColors.Length != ProceduralProperties.Colors1dArrayLength)
                Array.Resize(ref proceduralGradientColors, ProceduralProperties.Colors1dArrayLength);
            if (patternColors.Length != ProceduralProperties.Colors1dArrayLength)
                Array.Resize(ref patternColors, ProceduralProperties.Colors1dArrayLength);

            var sizeModWithoutOutline = quadData.GetSizeModifier(rectTransform, animationProps);
            var sizeModWithOutline = sizeModWithoutOutline;
            var expandedOutlineWidth = quadData.OutlineExpandsOutward ? quadData.GetOutlineWidth(rectTransform, animationProps) : 0f;
            if (expandedOutlineWidth > 0f)
            {
                var outlineExpansion = expandedOutlineWidth * 2;
                sizeModWithOutline += new Vector2(outlineExpansion, outlineExpansion);
            }

            var rotationScaleFactor = 1f;
            if (quadData.FitRotatedImageWithinBounds && rotation != 0f)
            {
                var angleRad = rotation * Mathf.Deg2Rad;
                var cosTheta = Mathf.Abs(Mathf.Cos(angleRad));
                var sinTheta = Mathf.Abs(Mathf.Sin(angleRad));
                var scaleX = rectWidth / (rectWidth * cosTheta + rectHeight * sinTheta);
                var scaleY = rectHeight / (rectWidth * sinTheta + rectHeight * cosTheta);
                rotationScaleFactor = Mathf.Min(scaleX, scaleY);
            }

            var rotationQuat = Quaternion.Euler(0, 0, rotation);
            var pivotAdjust = new Vector2(rectWidth * (0.5f - rectTransform.pivot.x), rectHeight * (0.5f - rectTransform.pivot.y));
            var anchorOffset = quadData.GetQuadPositionAdjustment(rectTransform);
            var offsetWithQuadAnchoring = (Vector3)(anchorOffset + offset);

            var pixelRect = RectTransformUtility.PixelAdjustRect(rectTransform, canvas);
            var oldBottomLeft  = new Vector2(pixelRect.xMin, pixelRect.yMin);
            var oldTopRight    = new Vector2(pixelRect.xMax, pixelRect.yMax);
            var oldBottomRight = new Vector2(pixelRect.xMax, pixelRect.yMin);
            var newBottomLeft = FindNewPosition(oldBottomLeft, Vector2.zero, offsetWithQuadAnchoring, sizeModWithOutline);
            var newTopRight = FindNewPosition(oldTopRight, Vector2.one, offsetWithQuadAnchoring, sizeModWithOutline);
            var newBottomRight = FindNewPosition(oldBottomRight, new Vector2(1, 0), offsetWithQuadAnchoring, sizeModWithOutline);
            var vertexTransformationMatrix = ConstructTransformationMatrix(oldBottomLeft, oldTopRight, oldBottomRight, newBottomLeft, newTopRight, newBottomRight);

            if (isDefaultQuadData)
            {
                newBottomLeft = FindNewPosition(oldBottomLeft, Vector2.zero, offset, sizeModWithoutOutline);
                newTopRight = FindNewPosition(oldTopRight, Vector2.one, offset, sizeModWithoutOutline);
                newBottomRight = FindNewPosition(oldBottomRight, new Vector2(1, 0), offset, sizeModWithoutOutline);

                FinalVertexScale = new Vector2(Vector2.Distance(newBottomRight, newTopRight) / Vector2.Distance(oldBottomRight, oldTopRight), Vector2.Distance(newBottomLeft, newBottomRight) / Vector2.Distance(oldBottomLeft, oldBottomRight));
                FinalMeshCenter = (newBottomLeft + newTopRight) * 0.5f - offset;
                if (rectHeight > 0 && rectWidth > 0)
                    FollowerTransformationMatrix = ConstructTransformationMatrix(oldBottomLeft, oldTopRight, oldBottomRight, newBottomLeft, newTopRight, newBottomRight);
            }

            Vector2 FindNewPosition(Vector2 position, Vector2 uvOffset, Vector2 finalOffset, Vector2 sizeMod)
            {
                position -= pivotAdjust;
                uvOffset -= UVHalf;
                uvOffset.Scale(sizeMod);
                position += uvOffset;
                position = rotationQuat * position;
                position *= rotationScaleFactor;
                position += pivotAdjust + finalOffset;
                return position;
            }

            var minScale = new Vector2(0.01f, 0.01f); // Sanitization
            primaryColorScale = Vector2.Max(primaryColorScale, minScale);
            outlineColorScale = Vector2.Max(outlineColorScale, minScale);
            proceduralGradientColorScale = Vector2.Max(proceduralGradientColorScale, minScale);
            patternColorScale = Vector2.Max(patternColorScale, minScale);

            Vector2 proceduralGradientPositionParams;
            if (quadData.Cutout == QuadData.CutoutType.SDF)
            {
                var sdfCutoutShaderSize = sdfCutoutSize;
                var sdfCutoutDecodeSize = new Vector2(rectWidth + sizeModWithoutOutline.x, rectHeight + sizeModWithoutOutline.y);
                var sdfCutoutReferenceSize = new Vector2(rectWidth + sizeModWithOutline.x, rectHeight + sizeModWithOutline.y);
                var sdfCutoutSizeReferenceSize = sdfCutoutReferenceSize;
                var sdfCutoutReferenceOrigin = Vector2.zero;
                if (quadData.CutoutPositionIgnoresExpandedOutlines && expandedOutlineWidth > 0f)
                {
                    sdfCutoutReferenceSize = sdfCutoutDecodeSize;
                    sdfCutoutReferenceOrigin = Vector2.one * expandedOutlineWidth;
                }

                Vector2 sdfCutoutCenter;
                if (quadData.SDFCutoutUsesAnchors)
                {
                    quadData.GetSDFCutoutAnchorPositionAndSize(sdfCutoutReferenceOrigin, sdfCutoutReferenceSize, animationProps, out sdfCutoutCenter, out sdfCutoutShaderSize);
                }
                else
                {
                    sdfCutoutCenter = sdfCutoutReferenceOrigin + (quadData.SDFCutoutPositionIsAbsolute
                        ? sdfCutoutReferenceSize * 0.5f + sdfCutoutPosition
                        : Vector2.Scale(sdfCutoutPosition, sdfCutoutReferenceSize));
                    if (quadData.SDFCutoutSizeIsAbsolute)
                    {
                        var sdfCutoutSizeReferenceSizeRcp = new Vector2(
                            1f / Mathf.Max(sdfCutoutSizeReferenceSize.x, 0.001f),
                            1f / Mathf.Max(sdfCutoutSizeReferenceSize.y, 0.001f)
                        );
                        sdfCutoutShaderSize = Vector2.Scale(sdfCutoutSize, sdfCutoutSizeReferenceSizeRcp);
                    }
                    else if (quadData.CutoutPositionIgnoresExpandedOutlines && expandedOutlineWidth > 0f)
                        sdfCutoutShaderSize = Vector2.Scale(sdfCutoutSize, new Vector2(sdfCutoutDecodeSize.x / Mathf.Max(sdfCutoutSizeReferenceSize.x, 0.001f), sdfCutoutDecodeSize.y / Mathf.Max(sdfCutoutSizeReferenceSize.y, 0.001f)));

                    sdfCutoutShaderSize = Vector2.Scale(sdfCutoutSizeReferenceSize, sdfCutoutShaderSize);
                }
                var sdfCutoutShaderPosition = sdfCutoutCenter + Vector2.one * 8192f;
                sdfCutoutShaderPosition = Vector2.Min(Vector2.one * 16383.5f, Vector2.Max(Vector2.zero, sdfCutoutShaderPosition));
                sdfCutoutShaderSize = Vector2.Min(Vector2.one * 16383.5f, Vector2.Max(Vector2.zero, sdfCutoutShaderSize));
                proceduralGradientSizeParams = sdfCutoutShaderSize;
                proceduralGradientPositionParams = sdfCutoutShaderPosition;
                secondaryGradientStrengthOrAngle = Mathf.Repeat(sdfCutoutRotation, 360f) / 360f;
            }
            else if (quadData.ProceduralGradientType == QuadData.GradientType.Noise)
            {
                proceduralGradientPositionParams = new Vector2(noiseScale, (noiseSeed + (quadData.NoiseGradientAlternateMode ? 32768f : 0)) / 65534.5f);
                proceduralGradientSizeParams = new Vector2(1f - noiseEdge, noiseStrength);
                secondaryGradientStrengthOrAngle = proceduralGradientPointerStrength;
            }
            else if (quadData.ProceduralGradientType == QuadData.GradientType.SDF)
            {
                proceduralGradientPositionParams = Vector2.Min(new Vector2(4095.9375f, 4095.9375f), new Vector2(sdfGradientOuterDistance, sdfGradientInnerDistance) / 4095.9375f);
                proceduralGradientSizeParams = new Vector2(sdfGradientInnerReach, sdfGradientOuterReach);
                secondaryGradientStrengthOrAngle = proceduralGradientPointerStrength;
            }
            else
            {
                proceduralGradientPositionParams = (proceduralGradientPosition + Vector2.one * 0.5f) / 2f;
                var flipYPos = !quadData.ScreenSpaceProceduralGradient && quadData.ProceduralGradientPositionFromPointer && canvas.renderMode == RenderMode.ScreenSpaceOverlay || canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null;
                if (flipYPos)
                    proceduralGradientPositionParams.y = 1f - proceduralGradientPositionParams.y;

                if (quadData.ProceduralGradientType == QuadData.GradientType.Angle || quadData.ProceduralGradientType == QuadData.GradientType.Conical)
                {
                    var adjustedProceduralGradientAngle = angleGradientAngle;
                    if (quadData.ProceduralGradientAspectCorrection)
                    {
                        var viewDimensions = GetViewportDimensions();
                        var aspect = quadData.ScreenSpaceProceduralGradient ? (float)viewDimensions.x / viewDimensions.y : rectWidth / rectHeight;
                        var angleRadians = adjustedProceduralGradientAngle * Mathf.Deg2Rad;
                        var sinTheta = Mathf.Sin(angleRadians);
                        var cosTheta = Mathf.Cos(angleRadians);
                        var adjustedAngleRadians = Mathf.Atan2(sinTheta, cosTheta * aspect);
                        adjustedProceduralGradientAngle = adjustedAngleRadians * Mathf.Rad2Deg;
                    }

                    if (quadData.ProceduralGradientType == QuadData.GradientType.Angle)
                    {
                        proceduralGradientSizeParams = angleGradientStrength;
                        secondaryGradientStrengthOrAngle = Mathf.Repeat(adjustedProceduralGradientAngle, 360f) / 360f;
                    }
                    else
                    {
                        proceduralGradientSizeParams = new Vector2(conicalGradientTailStrength, (conicalGradientCurvature + 1) * 0.5f);
                        secondaryGradientStrengthOrAngle = Mathf.Repeat(-adjustedProceduralGradientAngle - 90, 360f) / 360f;
                    }

                    if (flipYPos)
                        secondaryGradientStrengthOrAngle = 1 - secondaryGradientStrengthOrAngle;
                }
                else if (quadData.ProceduralGradientType == QuadData.GradientType.Radial)
                {
                    secondaryGradientStrengthOrAngle = radialGradientStrength;
                    proceduralGradientSizeParams = radialGradientSize;
                    if (quadData.ProceduralGradientAspectCorrection)
                    {
                        float secondaryZParamMultiplier, secondaryWParamMultiplier;
                        if (quadData.ScreenSpaceProceduralGradient)
                        {
                            var viewDimensions = GetViewportDimensions();
                            secondaryZParamMultiplier = Mathf.Min(1f, (float)viewDimensions.x / viewDimensions.y);
                            secondaryWParamMultiplier = Mathf.Min(1f, (float)viewDimensions.y / viewDimensions.x);
                        }
                        else
                        {
                            secondaryZParamMultiplier = Mathf.Min(1f, rectWidth / rectHeight);
                            secondaryWParamMultiplier = Mathf.Min(1f, rectHeight / rectWidth);
                        }
                        const float k = 5f;
                        var sx = secondaryZParamMultiplier * (k / Mathf.Max(proceduralGradientSizeParams.x, 1e-12f) - k);
                        var sy = secondaryWParamMultiplier * (k / Mathf.Max(proceduralGradientSizeParams.y, 1e-12f) - k);
                        proceduralGradientSizeParams.x = k / (k + Mathf.Max(sx, 0f));
                        proceduralGradientSizeParams.y = k / (k + Mathf.Max(sy, 0f));
                    }
                }
            }

            Color presetPrimaryColor, presetOutlineColor, presetProceduralGradientColor, presetPatternColor;
            Color singularOutlineColor, singularProceduralGradientColor, singularPatternColor;
            var colorPreset = quadData.ColorPreset;
            if (colorPreset != null)
            {
                (presetPrimaryColor, presetOutlineColor, presetProceduralGradientColor, presetPatternColor) = (colorPreset.PrimaryColor, colorPreset.OutlineColor, colorPreset.ProceduralGradientColor, colorPreset.PatternColor);
                singularOutlineColor = Color.Lerp(outlineColors[0], presetOutlineColor, quadData.OutlineColorPresetMix);
                singularProceduralGradientColor = Color.Lerp(proceduralGradientColors[0], presetProceduralGradientColor, quadData.ProceduralGradientColorPresetMix);
                singularPatternColor = Color.Lerp(patternColors[0], presetPatternColor, quadData.PatternColorPresetMix);
            }
            else
            {
                presetPrimaryColor = presetOutlineColor = presetProceduralGradientColor = presetPatternColor = Color.clear;
                singularOutlineColor = outlineColors[0];
                singularProceduralGradientColor = proceduralGradientColors[0];
                singularPatternColor = patternColors[0];
            }

            var packedOutlineColor = EncodeFloat8_8_8_8(singularOutlineColor);
            var packedPatternColor = EncodeFloat8_8_8_8(singularPatternColor);
            var packedProceduralGradientColorOrCutoutConcavity = quadData.Cutout == QuadData.CutoutType.SDF 
                ? EncodeFloat8_8_8_8(quadData.SDFCutoutIsSquircle ? sdfCutoutConcavity : sdfCutoutConcavity / 1.9921875f)
                : EncodeFloat8_8_8_8(singularProceduralGradientColor);

            byte sourceImageFade = 255;
            if (!quadData.AdvancedQuadSettings.HasFlag(QuadData.QuadModifiers.DisableSprite))
                GetSourceImageFade(ref sourceImageFade);

            float patternParam1, patternParam2;
            if (quadData.Pattern < QuadData.PatternType.Fractal || quadData.Pattern == QuadData.PatternType.Sprite)
            {
                patternParam1 = quadData.ScanlinePatternSpeedIsStaticOffset ? Mathf.Repeat(patternSpeed, 1f) : (patternSpeed + 2047f / 2048f) / (4095f / 2048f);
                patternParam2 = patternDensity;
            }
            else
            {
                patternParam1 = patternCellParam;
                patternParam2 = patternDensity;
            }

            var shaderFlagsOne = (Convert.ToUInt32(quadData.CutoutRule == QuadData.SimpleCutoutRule.OR)
                                    | (Convert.ToUInt32(quadData.InvertCutout) << 1)
                                    | (Convert.ToUInt32(quadData.Cutout == QuadData.CutoutType.SDF ? quadData.CutoutBehaviour == QuadData.SDFCutoutBehaviour.OutlineOnly : quadData.CutoutOnlyAffectsOutline) << 2)
                                    | (Convert.ToUInt32(quadData.Cutout == QuadData.CutoutType.SDF ? quadData.SDFCutoutIsSquircle : quadData.ProceduralGradientAlphaIsBlend) << 3)
                                    | (Convert.ToUInt32(quadData.Cutout == QuadData.CutoutType.SDF ? ((int)quadData.SDFCutoutMirror & 1) != 0 : quadData.ProceduralGradientAffectsInterior) << 4)
                                    | (Convert.ToUInt32(quadData.Cutout == QuadData.CutoutType.SDF ? ((int)quadData.SDFCutoutMirror & 2) != 0 : quadData.ProceduralGradientAffectsOutline) << 5)
                                    | (Convert.ToUInt32(quadData.Cutout == QuadData.CutoutType.SDF ? quadData.CutoutBehaviour == QuadData.SDFCutoutBehaviour.OutlineAndInterior : quadData.ScreenSpaceProceduralGradient) << 6)
                                    | (Convert.ToUInt32(quadData.OutlineAlphaIsBlend) << 7)) / 255f;

            var shaderFlagsTwo = (Convert.ToUInt32(quadData.OutlineFadeTowardsInterior)
                                    | (Convert.ToUInt32(quadData.OutlineExpandsOutward) << 1)
                                    | (Convert.ToUInt32(quadData.PatternAffectsInterior) << 2)
                                    | (Convert.ToUInt32(quadData.PatternAffectsOutline) << 3)
                                    | (Convert.ToUInt32(quadData.ScreenSpacePattern) << 4)
                                    | (Convert.ToUInt32(quadData.CollapseIntoParallelogram) << 5)
                                    | (Convert.ToUInt32(quadData.OutlineAccommodatesCollapsedEdge) << 6)
                                    | (Convert.ToUInt32(quadData.Cutout == QuadData.CutoutType.SDF ? quadData.SDFCutoutMirrorIsDiagonal : quadData.ProceduralGradientInvert) << 7)) / 255f;

            var stroke01 = quadData.GetStroke01(rectTransform, animationProps);

            var isBlurEnabled = false;
            IsBlurEnabled(ref isBlurEnabled);
            var shaderFlagsThree = (Convert.ToUInt32(quadData.PatternColorAlphaIsBlend)
                                      | (Convert.ToUInt32(quadData.AddInteriorOutline && (!quadData.OutlineExpandsOutward || stroke01 < 1)) << 1)
                                      | (Convert.ToUInt32((sprite != null || isBlurEnabled) 
                                                       && (quadData.Pattern != QuadData.PatternType.Sprite || Mathf.Approximately(patternDensity, 0f) || isBlurEnabled) 
                                                       && (DataMode == QuadDataMode.Single || !quadData.AdvancedQuadSettings.HasFlag(QuadData.QuadModifiers.DisableSprite))) << 2)
                                      | (Convert.ToUInt32(quadData.ProceduralGradientPositionFromPointer) << 3)
                                      | (Convert.ToUInt32((animationProps?.cornerConcavity ?? quadData.CornerConcavity).magnitude > 0 && quadData.ConcavityIsSmoothing) << 4)
                                      | (Convert.ToUInt32(quadData.ScanlinePatternSpeedIsStaticOffset) << 5)
                                      | (Convert.ToUInt32(quadData.SoftPattern || quadData.Pattern == QuadData.PatternType.Sprite && (patternSpriteRotation >= 255 || quadData.SpritePatternRotationMode == QuadData.SpritePatternRotation.Offset)) << 6)
                                      | (Convert.ToUInt32(quadData.MirrorCollapse) << 7)) / 255f;

            var packedStrokeSoftnessAndFlagsThree = EncodeFloat12_12_8(stroke01, softness / 255.9375f, shaderFlagsThree);

            var totalSdfGradientValue = animationProps != null
                ? animationProps.sdfGradientInnerDistance + animationProps.sdfGradientOuterDistance + animationProps.sdfGradientInnerReach + animationProps.sdfGradientOuterReach
                : quadData.SDFGradientInnerDistance + quadData.SDFGradientOuterDistance + quadData.SDFGradientInnerReach + quadData.SDFGradientOuterReach;

            var gradientInt = (int)quadData.ProceduralGradientType;
            if (quadData.Cutout == QuadData.CutoutType.SDF)
            {
                // If we're using an SDF cutout, the procedual gradient is disabled to free up space in vertex attributes, which gradient ID of 6 signifies.
                gradientInt = 6;
            }
            else if (!quadData.ProceduralGradientInvert && (quadData.ProceduralGradientType == QuadData.GradientType.SDF && Mathf.Approximately(totalSdfGradientValue, 0)
                                                   || quadData.ProceduralGradientType == QuadData.GradientType.Angle && Mathf.Approximately(angleGradientStrength.x, 0) && Mathf.Approximately(angleGradientStrength.y, 0)
                                                   || quadData.ProceduralGradientType == QuadData.GradientType.Radial && (Mathf.Approximately(radialGradientStrength, 0) || Mathf.Approximately(radialGradientSize.x, 0) || Mathf.Approximately(radialGradientSize.y, 0))
                                                   || quadData.ProceduralGradientType == QuadData.GradientType.Conical && Mathf.Approximately(conicalGradientTailStrength, 0)
                                                   || quadData.ProceduralGradientType == QuadData.GradientType.Noise && Mathf.Approximately(noiseStrength, 0)))
            {
                // If we're at the defaults where nothing should be visible, we basically tell the shader to do nothing, which gradient ID of 7 signifies.
                gradientInt = 7;
            }

            var patternInt = (int)quadData.Pattern;
            if (quadData.Pattern is >= QuadData.PatternType.DiamondShape and <= QuadData.PatternType.CrossShape)
                patternInt += (int)quadData.PatternOriginPos;

            var packedPatternParam1OutlineWidthGradientModeAndPatternMode = EncodeFloat12_12_5_3(patternParam1, quadData.GetOutlineWithoutCollapsedEdgeAdjustment(animationProps) / 511.875f, patternInt / 31f, gradientInt / 7f);
            var adjustedChamfer = quadData.GetAdjustedChamfer(rectTransform, animationProps);
            var packedChamferXY = EncodeFloat16_16(adjustedChamfer.x / 4095.9375f, adjustedChamfer.y / 4095.9375f);
            var packedChamferZW = EncodeFloat16_16(adjustedChamfer.z / 4095.9375f, adjustedChamfer.w / 4095.9375f);
            var adjustedConcavity = quadData.GetAdjustedConcavity(animationProps);
            if (!quadData.ConcavityIsSmoothing)
                adjustedConcavity /= 1.9921875f;

            var packedConcavity = EncodeFloat8_8_8_8_Clamped(adjustedConcavity);

            var packedGradientStrengthPatternParam2AndCollapsedEdgeIdx = EncodeFloat16_14_2(secondaryGradientStrengthOrAngle, patternParam2, (int)quadData.CollapsedEdge / 3f);
            
            Vector4 cutoutEdgesOrChamfer;
            if (quadData.Cutout == QuadData.CutoutType.SDF)
            {
                var adjustedSdfCutoutChamfer = quadData.GetAdjustedCutoutChamfer(rectTransform, animationProps);

                cutoutEdgesOrChamfer = adjustedSdfCutoutChamfer / 1023.75f;
            }
            else
            {
                if (quadData.CutoutPositionIgnoresExpandedOutlines && expandedOutlineWidth > 0f)
                    cutout = Vector4.Min(Vector4.one * 1023.5f, cutout + Vector4.one * expandedOutlineWidth);
                cutoutEdgesOrChamfer = new Vector4(quadData.SimpleCutoutEdgeEnabled[0] ? cutout.x : -0.25f, quadData.SimpleCutoutEdgeEnabled[1] ? cutout.y : -0.25f, quadData.SimpleCutoutEdgeEnabled[2] ? cutout.z : -0.25f, quadData.SimpleCutoutEdgeEnabled[3] ? cutout.w : -0.25f);
                cutoutEdgesOrChamfer = (cutoutEdgesOrChamfer + Vector4.one * 0.25f) / 1023.75f;
            }

            var packedPrimaryColorFadeAndCutoutXY = EncodeFloat8_12_12((primaryColorFade / 255f) * (sourceImageFade / 255f), cutoutEdgesOrChamfer.x, cutoutEdgesOrChamfer.y);

            var modifiedPatternSpriteRotation = quadData.SpritePatternRotationMode == QuadData.SpritePatternRotation.Offset
                ? (byte)(quadData.SpritePatternOffsetDirectionDegrees + 106)
                : (byte)Mathf.Repeat(patternSpriteRotation, 255);

            byte patternLineThicknessOrPatternRotationOrAlphaBlend = quadData.Pattern == QuadData.PatternType.Sprite ? modifiedPatternSpriteRotation : (byte)(255 - patternLineThickness);
            GetAlphaBlend(ref patternLineThicknessOrPatternRotationOrAlphaBlend);

            var alphaBlendOrPatternLineThicknessAndPackedCutoutZW = EncodeFloat8_12_12(patternLineThicknessOrPatternRotationOrAlphaBlend / 255f, cutoutEdgesOrChamfer.z, cutoutEdgesOrChamfer.w);
            var packedProceduralGradientParamsXY = quadData.Cutout == QuadData.CutoutType.SDF
                ? EncodeFloat15_15_FixedPoint(proceduralGradientPositionParams, 1)
                : EncodeFloat16_16(proceduralGradientPositionParams);
            var packedProceduralGradientParamsZW = quadData.Cutout == QuadData.CutoutType.SDF
                ? EncodeFloat15_15_FixedPoint(proceduralGradientSizeParams, 1)
                : EncodeFloat16_16(proceduralGradientSizeParams);

            var uv1 = new Vector4(packedProceduralGradientColorOrCutoutConcavity, packedOutlineColor, packedPatternColor, packedPatternParam1OutlineWidthGradientModeAndPatternMode);
            var uv2 = new Vector4(packedChamferXY, packedChamferZW, packedConcavity, packedGradientStrengthPatternParam2AndCollapsedEdgeIdx);
            var uv3 = new Vector4(packedPrimaryColorFadeAndCutoutXY, alphaBlendOrPatternLineThicknessAndPackedCutoutZW, packedProceduralGradientParamsXY, packedProceduralGradientParamsZW);

            var quadVertStartIdx = totalVertices.Count;
            var isForcedSimpleType = quadData.AdvancedQuadSettings.HasFlag(QuadData.QuadModifiers.ForceSimpleMesh);
            isForcedSimpleType |= type == Type.Simple && sprite != null && sprite.packingMode == SpritePackingMode.Tight && quadData.Pattern == QuadData.PatternType.Sprite && patternDensity > 0f; // If using the sprite as a pattern, then we don't want tight sprite packing to affect the mesh.
            if (isForcedSimpleType)
            {
                for (int j = 0; j < DefaultSimpleMeshVertexList.Count; j++)
                    totalVertices.Add(new UIVertex { position = DefaultSimpleMeshVertexList[j].position * rectTransform.rect.size, uv0 = DefaultSimpleMeshVertexList[j].uv0 });
            }
            else
            {
                totalVertices.AddRange(baseVertices);
            }

            if (quadData.MeshTopology == QuadData.Topology.X)
                ConvertToXTopology(totalVertices, quadVertStartIdx);
            else if (quadData.MeshTopology == QuadData.Topology.Flipped)
                ChangePlaneTopologyDiagonalDirection(totalVertices, quadVertStartIdx);

            var subdivisions = quadData.MeshSubdivisions;
            if (type == Type.Filled && !isForcedSimpleType)
            {
                // Radial360 subdivides the mesh already, so we want to remove one subdivision when it is selected and engaged.
                if (fillAmount < 1 && fillMethod == FillMethod.Radial360)
                    subdivisions--;

                // While Radial180 subdivides the mesh, but only either horizontally or vertically, so we'll replicate that to prevent incongruities when it is selected but *not* engaged. 
                else if (Mathf.Approximately(fillAmount, 1) && fillMethod == FillMethod.Radial180)
                {
                    if (quadData.MeshTopology == QuadData.Topology.X)
                        HalfSubdivideX(totalVertices, quadVertStartIdx, fillOrigin % 2 == 0);
                    else if (quadData.MeshTopology == QuadData.Topology.Original)
                        HalfSubdivide(totalVertices, quadVertStartIdx, fillOrigin % 2 == 0);
                    else
                        HalfSubdivide(totalVertices, quadVertStartIdx, fillOrigin % 2 == 1);
                }
            }

            UIVertex[] internalArray;
            if (subdivisions > 0)
            {
                int meshSize = totalVertices.Count - quadVertStartIdx;
                int requiredCapacityForQuad = totalVertices.Count - quadVertStartIdx;
                for (int j = 0; j < subdivisions; j++)
                {
                    requiredCapacityForQuad += 4 * meshSize;
                    meshSize *= 4;
                }
            
                if (totalVertices.Capacity < requiredCapacityForQuad + quadVertStartIdx)
                    totalVertices.Capacity = requiredCapacityForQuad + quadVertStartIdx;
            
                int end = totalVertices.Count;
                SetListArraySizeDel(totalVertices, requiredCapacityForQuad + quadVertStartIdx);

                var start = quadVertStartIdx;
                internalArray = GetUIVertexListArrayDel.Invoke(totalVertices);
                unsafe
                {
                    fixed (UIVertex* nativeVertices = &internalArray[0])
                    {
                        for (int j = 0; j < subdivisions; j++)
                        {
                            var numInputVertsPerUnit = quadData.MeshTopology == QuadData.Topology.X ? 12 : 3;
                            var numUnits = (end - start) / numInputVertsPerUnit;
                            if (quadData.MeshTopology == QuadData.Topology.X)
                            {
                                var job = new UIVertexMeshSubdivisionXJob
                                {
                                    Vertices = nativeVertices,
                                    InputStart = start,
                                    OutputStart = end
                                };
                                var handle = job.ScheduleBatch(numUnits, 16);
                                handle.Complete();
                            }
                            else
                            {
                                var subdivisionJob = new UIVertexMeshSubdivisionJob
                                {
                                    Vertices = nativeVertices,
                                    InputStart = start,
                                    OutputStart = end
                                };
                                var handle = subdivisionJob.ScheduleBatch(numUnits, 64);
                                handle.Complete();
                            }
                            var inputSize = end - start;
                            start = end;
                            end += 4 * inputSize;
                        }
                    }
                }
                totalVertices.RemoveRange(quadVertStartIdx, start - quadVertStartIdx);
            }

            var numVertices = totalVertices.Count - quadVertStartIdx;
#if UNITY_EDITOR
            quadVertexCounts.Add(numVertices);
#endif

            var (width, height) = (rectWidth + sizeModWithoutOutline.x, rectHeight + sizeModWithoutOutline.y);
            var packedWidthHeight = EncodeFloat16_16_FixedPoint(width, height, 2);
            var collapseEdgeAmount = quadData.EdgeCollapseAmountIsAbsolute ? Mathf.Min(1, collapseEdgeAmountAbsolute / (quadData.CollapsedEdge <= QuadData.CollapsedEdgeType.Bottom ? width : height)) : Mathf.Clamp01(collapseEdgeAmountRelative);
            var packedCollapsedEdgePositionAmountAndFlagsTwo = EncodeFloat12_12_8(collapseEdgePosition, collapseEdgeAmount, shaderFlagsTwo);
            var uv1NeedsRecalc = quadData.OutlineColorDimensions != Vector2Int.one || quadData.ProceduralGradientColorDimensions != Vector2Int.one || quadData.PatternColorDimensions != Vector2Int.one;
            var needsProceduralGradient = uv1NeedsRecalc && quadData.Cutout != QuadData.CutoutType.SDF && quadData.ProceduralGradientColorDimensions != Vector2Int.one;
            var needsOutlineColor = uv1NeedsRecalc && quadData.OutlineColorDimensions != Vector2Int.one;
            var needsPatternColor = uv1NeedsRecalc && quadData.PatternColorDimensions != Vector2Int.one;
            var primaryColorIsAdvanced            = quadData.PrimaryColorDimensions != Vector2Int.one && (primaryColorOffset != default || primaryColorRotation != 0 || primaryColorScale != Vector2.one);
            var outlineColorIsAdvanced            = quadData.OutlineColorDimensions != Vector2Int.one && (outlineColorOffset != default || outlineColorRotation != 0 || outlineColorScale != Vector2.one);
            var proceduralGradientColorIsAdvanced = quadData.ProceduralGradientColorDimensions != Vector2Int.one && (proceduralGradientColorOffset != default || proceduralGradientColorRotation != 0 || proceduralGradientColorScale != Vector2.one);
            var patternColorIsAdvanced            = quadData.PatternColorDimensions != Vector2Int.one && (patternColorOffset != default || patternColorRotation != 0 || patternColorScale != Vector2.one);
            var nativePrimaryColors = new NativeArray<Color>(primaryColors, Allocator.TempJob);
            var nativeProceduralGradientColors = new NativeArray<Color>(proceduralGradientColors, Allocator.TempJob);
            var nativeOutlineColors = new NativeArray<Color>(outlineColors, Allocator.TempJob);
            var nativePatternColors = new NativeArray<Color>(patternColors, Allocator.TempJob);
            var vertProcessJob = new UIVertexProcessingJob
            {
                FirstVertexIndex = quadVertStartIdx,
                PrimaryColors = nativePrimaryColors.Reinterpret<float4>(),
                ProceduralGradientColors = nativeProceduralGradientColors.Reinterpret<float4>(),
                OutlineColors = nativeOutlineColors.Reinterpret<float4>(),
                PatternColors = nativePatternColors.Reinterpret<float4>(),
                TransformationMatrix = vertexTransformationMatrix,
                PresetPrimaryColor = (Vector4)presetPrimaryColor,
                PresetProceduralGradientColor = (Vector4)presetProceduralGradientColor,
                PresetOutlineColor = (Vector4)presetOutlineColor,
                PresetPatternColor = (Vector4)presetPatternColor,
                UvRect = uvRect,
                InitialUv1 = uv1,
                Uv2 = uv2,
                Uv3 = uv3,
                PrimaryColorDimensions = new int2(quadData.PrimaryColorDimensions.x, quadData.PrimaryColorDimensions.y),
                ProceduralGradientColorDimensions = new int2(quadData.ProceduralGradientColorDimensions.x, quadData.ProceduralGradientColorDimensions.y),
                OutlineColorDimensions = new int2(quadData.OutlineColorDimensions.x, quadData.OutlineColorDimensions.y),
                PatternColorDimensions = new int2(quadData.PatternColorDimensions.x, quadData.PatternColorDimensions.y),
                PrimaryColorPresetMix = quadData.PrimaryColorPresetMix,
                ProceduralGradientColorPresetMix = quadData.ProceduralGradientColorPresetMix,
                PresetOutlineColorMix = quadData.OutlineColorPresetMix,
                PresetPatternColorMix = quadData.PatternColorPresetMix,
                ShaderFlagsOne = shaderFlagsOne,
                PackedCollapsedEdgePositionAmountAndFlagsTwo = packedCollapsedEdgePositionAmountAndFlagsTwo,
                PackedStrokeSoftnessAndFlagsThree = packedStrokeSoftnessAndFlagsThree,
                PackedWidthHeight = packedWidthHeight,
                NeedsProceduralGradient = needsProceduralGradient,
                NeedsOutlineColor = needsOutlineColor,
                NeedsPatternColor = needsPatternColor,
                PrimaryColorIsAdvanced = primaryColorIsAdvanced,
                PrimaryColorOffset = primaryColorOffset,
                PrimaryColorScale = primaryColorScale,
                PrimaryColorRotation = primaryColorRotation,
                PrimaryColorWrapModeX = (byte)quadData.PrimaryColorWrapModeX,
                PrimaryColorWrapModeY = (byte)quadData.PrimaryColorWrapModeY,
                OutlineColorIsAdvanced = outlineColorIsAdvanced,
                OutlineColorOffset = outlineColorOffset,
                OutlineColorScale = outlineColorScale,
                OutlineColorRotation = outlineColorRotation,
                OutlineColorWrapModeX = (byte)quadData.OutlineColorWrapModeX,
                OutlineColorWrapModeY = (byte)quadData.OutlineColorWrapModeY,
                ProceduralGradientColorIsAdvanced = proceduralGradientColorIsAdvanced,
                ProceduralGradientColorOffset = proceduralGradientColorOffset,
                ProceduralGradientColorScale = proceduralGradientColorScale,
                ProceduralGradientColorRotation = proceduralGradientColorRotation,
                ProceduralGradientColorWrapModeX = (byte)quadData.ProceduralGradientColorWrapModeX,
                ProceduralGradientColorWrapModeY = (byte)quadData.ProceduralGradientColorWrapModeY,
                PatternColorIsAdvanced = patternColorIsAdvanced,
                PatternColorOffset = patternColorOffset,
                PatternColorScale = patternColorScale,
                PatternColorRotation = patternColorRotation,
                PatternColorWrapModeX = (byte)quadData.PatternColorWrapModeX,
                PatternColorWrapModeY = (byte)quadData.PatternColorWrapModeY
            };
            internalArray = GetUIVertexListArrayDel.Invoke(totalVertices);
            unsafe
            {
                fixed (UIVertex* nativeVertices = &internalArray[0])
                {
                    vertProcessJob.Vertices = nativeVertices;
                    var handle = vertProcessJob.ScheduleBatch(numVertices, 512);
                    handle.Complete();
                }
            }
            nativePrimaryColors.Dispose();
            nativeProceduralGradientColors.Dispose();
            nativeOutlineColors.Dispose();
            nativePatternColors.Dispose();
        }

#if UNITY_EDITOR
        if (captureResolution is { x: > 0, y: > 0 })
        {
            UIGraphicToTexture.BakeTexture(totalVertices, quadVertexCounts, material, sprite != null ? sprite.texture : null, (int)captureResolution.x, (int)captureResolution.y, superSample, addPadding, capturePath);
            captureResolution = Vector2.zero;
        }
        UnityEngine.Pool.ListPool<int>.Release(quadVertexCounts);
#endif
        vh.AddUIVertexTriangleStream(totalVertices);
        UnityEngine.Pool.ListPool<UIVertex>.Release(baseVertices);
        UnityEngine.Pool.ListPool<UIVertex>.Release(totalVertices);
    }

    private static void ChangePlaneTopologyDiagonalDirection(List<UIVertex> verts, int startIdx)
    {
        for (int i = startIdx; i < verts.Count; i += 6)
        {
            // 2nd and 3rd (as well as 0th and 5th) vertices are duplicates in the input vertex stream, so skip over.
            var (a, b, c, d) = (verts[i], verts[i + 1], verts[i + 2], verts[i + 4]);
            verts[i    ] = b; verts[i + 1] = c; verts[i + 2] = d;
            verts[i + 3] = d; verts[i + 4] = a; verts[i + 5] = b;
        }
    }
    
    private static void ConvertToXTopology(List<UIVertex> verts, int startIdx)
    {
        var oldVertCount = verts.Count;
        for (int i = startIdx; i < oldVertCount; i += 6)
        {
            var bl = verts[i];
            var tl = verts[i + 1];
            var tr = verts[i + 2];
            var br = verts[i + 4];

            var center = LerpVertex(LerpVertex(bl, tl), LerpVertex(br, tr));
            center.normal = bl.normal;
            center.tangent = bl.tangent;

            verts.Add(bl); verts.Add(tl); verts.Add(center);
            verts.Add(tl); verts.Add(tr); verts.Add(center);
            verts.Add(tr); verts.Add(br); verts.Add(center);
            verts.Add(br); verts.Add(bl); verts.Add(center);
        }
        verts.RemoveRange(startIdx, oldVertCount - startIdx);
    }

    private static void HalfSubdivide(List<UIVertex> verts, int startIdx, bool horizontal)
    {
        int oldVertCount = verts.Count;
        if (horizontal)
        {
            for (int i = startIdx; i < oldVertCount; i += 6)
            {
                var (v0, v1, v2, v3) = (verts[i], verts[i + 1], verts[i + 2], verts[i + 4]);
                var m03 = LerpVertex(v0, v3); 
                var m12 = LerpVertex(v1, v2);
                verts.Add(v3);  verts.Add(m03); verts.Add(v2);
                verts.Add(v2);  verts.Add(m03); verts.Add(m12);
                verts.Add(m03); verts.Add(v0);  verts.Add(m12);
                verts.Add(v0);  verts.Add(v1);  verts.Add(m12);
            }
        }
        else
        {
            for (int i = startIdx; i < oldVertCount; i += 6)
            {
                var (v0, v1, v2, v3) = (verts[i], verts[i + 1], verts[i + 2], verts[i + 4]);
                var m01 = LerpVertex(v0, v1);
                var m23 = LerpVertex(v2, v3);
                verts.Add(v3);  verts.Add(v0);  verts.Add(m23);
                verts.Add(v0);  verts.Add(m01); verts.Add(m23);
                verts.Add(v2);  verts.Add(m23); verts.Add(m01);
                verts.Add(v1);  verts.Add(v2);  verts.Add(m01);
            }
        }
        verts.RemoveRange(startIdx, oldVertCount - startIdx);
    }

    private static void HalfSubdivideX(List<UIVertex> verts, int startIdx, bool horizontal)
    {
        var oldVertCount = verts.Count;
        for (int i = startIdx; i < oldVertCount; i += 12)
        {
            var (v0, v1, v2, v3) = (verts[i], verts[i + 1], verts[i + 4], verts[i + 7]);
            if (horizontal)
            {
                var m03 = LerpVertex(v0, v3);
                var m12 = LerpVertex(v1, v2);
                AddX(verts, m03, m12, v2, v3);
                AddX(verts, v0, v1, m12, m03);
            }
            else
            {
                var m01 = LerpVertex(v0, v1);
                var m23 = LerpVertex(v2, v3);
                AddX(verts, v0, m01, m23, v3);
                AddX(verts, m01, v1, v2, m23);
            }
        }

        verts.RemoveRange(startIdx, oldVertCount - startIdx);
    }

    private static void AddX(List<UIVertex> verts, in UIVertex tl, in UIVertex tr, in UIVertex br, in UIVertex bl)
    {
        var center = LerpVertex(LerpVertex(tl, tr), LerpVertex(br, bl));
        verts.Add(bl); verts.Add(tl); verts.Add(center);
        verts.Add(tl); verts.Add(tr); verts.Add(center);
        verts.Add(tr); verts.Add(br); verts.Add(center);
        verts.Add(br); verts.Add(bl); verts.Add(center);
    }

    private static UIVertex LerpVertex(in UIVertex a, in UIVertex b) => new()
    {
        position = (a.position + b.position) * 0.5f,
        uv0 = (a.uv0 + b.uv0) * 0.5f
    };

    private Matrix4x4 ConstructTransformationMatrix(in Vector2 p1, in Vector2 p2, in Vector2 p3, in Vector2 q1, in Vector2 q2, in Vector2 q3)
    {
        var det = p1.x * (p2.y - p3.y) + p2.x * (p3.y - p1.y) + p3.x * (p1.y - p2.y);
        var invDet = 1.0f / det;
        var a11 = (p2.y - p3.y) * invDet;
        var a12 = (p3.x - p2.x) * invDet;
        var a13 = (p2.x * p3.y - p3.x * p2.y) * invDet;
        var a21 = (p3.y - p1.y) * invDet;
        var a22 = (p1.x - p3.x) * invDet;
        var a23 = (p3.x * p1.y - p1.x * p3.y) * invDet;
        var a31 = (p1.y - p2.y) * invDet;
        var a32 = (p2.x - p1.x) * invDet;
        var a33 = (p1.x * p2.y - p2.x * p1.y) * invDet;
        var m00 = q1.x * a11 + q2.x * a21 + q3.x * a31;
        var m01 = q1.x * a12 + q2.x * a22 + q3.x * a32;
        var m03 = q1.x * a13 + q2.x * a23 + q3.x * a33;
        var m10 = q1.y * a11 + q2.y * a21 + q3.y * a31;
        var m11 = q1.y * a12 + q2.y * a22 + q3.y * a32;
        var m13 = q1.y * a13 + q2.y * a23 + q3.y * a33;
        return new Matrix4x4
        {
            m00 = m00, m01 = m01, m02 = 0, m03 = m03,
            m10 = m10, m11 = m11, m12 = 0, m13 = m13,
            m20 = 0, m21 = 0, m22 = 1, m23 = 0,
            m30 = 0, m31 = 0, m32 = 0, m33 = 1
        };
    }

    private void CheckRaycastArea()
    {
        if (!rayCastAreaDirty)
            return;

        if (!_useAdvancedRaycast)
        {
            raycastPadding = NormalRaycastPadding;
            rayCastAreaDirty = false;
            return;
        }

        var quadData = PrimaryQuadData;

        Vector4 raycastPaddingTemp;
        if (AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.Size))
        {
            var sizeAddition = quadData.GetSizeModifier(rectTransform) * -0.5f;
            raycastPaddingTemp = new Vector4(sizeAddition.x, sizeAddition.y, sizeAddition.x, sizeAddition.y);
        }
        else
        {
            raycastPaddingTemp = Vector4.zero;
        }

        if (quadData.OutlineExpandsOutward)
        {
            var outlineAddition = quadData.GetOutlineWidth(rectTransform);
            raycastPaddingTemp -= new Vector4(outlineAddition, outlineAddition, outlineAddition, outlineAddition);
        }

        raycastPaddingTemp += NormalRaycastPadding;
#if UNITY_ANDROID || UNITY_IOS
        raycastPaddingTemp += MobileRaycastPadding;
#endif
        if (AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.Offset))
            raycastPaddingTemp += new Vector4(quadData.Offset.x, quadData.Offset.y, -quadData.Offset.x, -quadData.Offset.y);

        if (AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.Rotation) && !quadData.FitRotatedImageWithinBounds && quadData.Rotation != 0f)
        {
            var angleRad = quadData.Rotation * Mathf.Deg2Rad;
            var (width, height) = (rectTransform.rect.width, rectTransform.rect.height);
            var (cosTheta, sinTheta) = (Mathf.Abs(Mathf.Cos(angleRad)), Mathf.Abs(Mathf.Sin(angleRad)));
            var (newWidth, newHeight) = (width * cosTheta + height * sinTheta, width * sinTheta + height * cosTheta);
            var (padH, padV) = ((newWidth - width) * 0.5f, (newHeight - height) * 0.5f);
            var rotationPadding = new Vector4(padH, padV, padH, padV);
            raycastPaddingTemp -= rotationPadding;
        }

        raycastPadding = raycastPaddingTemp;
        rayCastAreaDirty = false;
    }

    public override bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        if (lastSucessfulRaycastFrame == Time.frameCount)
            return true;

        var baseValue = base.IsRaycastLocationValid(screenPoint, eventCamera);
        if (!baseValue || !_useAdvancedRaycast)
            return baseValue;

        var quadData = PrimaryQuadData;

        var rect = GetPixelAdjustedRect();
        var rotated = AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.Rotation) && quadData.Rotation != 0;

        var rotationScaleFactor = 1f;
        if (rotated && quadData.FitRotatedImageWithinBounds)
        {
            var angleRad = quadData.Rotation * Mathf.Deg2Rad;
            var cosTheta = Mathf.Abs(Mathf.Cos(angleRad));
            var sinTheta = Mathf.Abs(Mathf.Sin(angleRad));
            var scaleX = rect.width / (rect.width * cosTheta + rect.height * sinTheta);
            var scaleY = rect.height / (rect.width * sinTheta + rect.height * cosTheta);
            rotationScaleFactor = Mathf.Min(scaleX, scaleY);
        }

        var sizeModifier = quadData.GetSizeModifier(rectTransform);
        var (innerRectWidth, innerRectHeight) = (rect.width + sizeModifier.x, rect.height + sizeModifier.y);
        var innerAspect = Mathf.Max(innerRectWidth, innerRectHeight) / Mathf.Max(Mathf.Min(innerRectWidth, innerRectHeight), 1e-5f);
        var outlineWidth = quadData.GetOutlineWidth(rectTransform);
        var outlineAddition = quadData.OutlineExpandsOutward ? outlineWidth * 2f : 0f;
        var (fullRectWidth, fullRectHeight) = (innerRectWidth + outlineAddition, innerRectHeight + outlineAddition);
        var (rotatedRectWidth, rotatedRectHeight) = (fullRectWidth * rotationScaleFactor, fullRectHeight * rotationScaleFactor);
        var rotatedOutlineWidth = outlineWidth * rotatedRectHeight / fullRectHeight;

        var midPoint = new Vector2(rotatedRectWidth, rotatedRectHeight) * 0.5f;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out var localPoint);
        if (AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.Offset))
            localPoint -= quadData.Offset;

        var regularCenter = new Vector2((0.5f - rectTransform.pivot.x) * rect.width, (0.5f - rectTransform.pivot.y) * rect.height);
        localPoint -= regularCenter;

        if (rotated)
        {
            localPoint = Quaternion.Euler(0, 0, -quadData.Rotation) * localPoint;
            localPoint /= rotationScaleFactor;
        }

        localPoint += new Vector2(fullRectWidth * 0.5f, fullRectHeight * 0.5f);

        if (rotated)
            localPoint *= rotationScaleFactor;

        if (AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.UVRect))
        {
            localPoint.x /= rotatedRectWidth;
            localPoint.x = quadData.UVRect.x + localPoint.x * quadData.UVRect.z;
            localPoint.x *= rotatedRectWidth;

            localPoint.y /= rotatedRectHeight;
            localPoint.y = quadData.UVRect.y + localPoint.y * quadData.UVRect.w;
            localPoint.y *= rotatedRectHeight;
        }

        var sdf = ComputeSdf();
        var cutoutResult = ComputeCutout();
        var cutoutOnlyAffectsOutline = quadData.Cutout == QuadData.CutoutType.SDF
            ? quadData.CutoutBehaviour == QuadData.SDFCutoutBehaviour.OutlineOnly
            : quadData.CutoutOnlyAffectsOutline;

        var stroke = quadData.GetStroke01(rectTransform);
        stroke *= 0.25f * Mathf.Min(innerRectWidth, innerRectHeight) + (quadData.OutlineExpandsOutward ? quadData.GetOutlineWithoutCollapsedEdgeAdjustment() * 0.5f : 0f);
        stroke *= rotationScaleFactor;

        if (rotatedOutlineWidth > 0)
        {
            var adjustedSoftness = quadData.OutlineAccommodatesCollapsedEdge ? quadData.Softness * 2.41421356237f * innerAspect : quadData.Softness;
            var collapseAdjust = quadData.OutlineAccommodatesCollapsedEdge ? 2 * (0.41421356237f / innerAspect) - 1 : 1f;
            var outlineAdjustedWidth = (rotatedOutlineWidth + adjustedSoftness * Smoothstep(0, 4, quadData.Softness)) * 0.5f;

            float outlineSDF = Mathf.Abs(outlineAdjustedWidth * collapseAdjust - sdf);
            if (quadData.AddInteriorOutline)
            {
                if (quadData.OutlineExpandsOutward)
                    stroke += outlineAdjustedWidth;

                var innerOutlineCenter = stroke * 2 - outlineAdjustedWidth;
                if (quadData.OutlineAccommodatesCollapsedEdge)
                    stroke -= 0.58594f * outlineAdjustedWidth;

                var innerOutlineSDF = Mathf.Abs(innerOutlineCenter - sdf);
                outlineSDF = Mathf.Min(outlineSDF, innerOutlineSDF);
            }
        
            var visibleOutline = outlineAdjustedWidth - outlineSDF;
            if (AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.IgnoreOutline) && (!cutoutOnlyAffectsOutline || cutoutResult > 0) && visibleOutline > 0)
                return false;
        }

        if (cutoutOnlyAffectsOutline)
            cutoutResult = 1f;

        if (AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.Stroke) && stroke > 0)
        {
            var strokeSDF = stroke - Mathf.Abs(stroke - sdf);
            if (strokeSDF < 0)
                return false;
        }

        var valid = sdf * cutoutResult > 0;
        if (valid)
            lastSucessfulRaycastFrame = Time.frameCount;

        return valid;

        float SdLine(Vector2 p, Vector2 a, Vector2 b)
        {
            var ba = b - a;
            var pa = p - a;
            var squaredLen = Vector2.Dot(ba, ba);
            if (squaredLen <= 1e-6f)
                return 1e6f;

            return (pa.x * ba.y - pa.y * ba.x) / Mathf.Sqrt(squaredLen);
        }

        float CalculateCornerSDF(float dist1, float dist2, float chamferValue, float concavityValue, bool concavityIsSmoothing)
        {
            if (concavityIsSmoothing)
            {
                var d = new Vector2(chamferValue - dist1, chamferValue - dist2);
                return chamferValue - Mathf.Pow(Mathf.Pow(d.x, concavityValue) + Mathf.Pow(d.y, concavityValue), 1f / concavityValue);
            }

            const float twoThirds   =  2f / 3f;
            const float negOneThird = -1f / 3f;
            var curved = chamferValue - Vector2.Distance(new Vector2(dist1, dist2), new Vector2(chamferValue, chamferValue));
            var flat = twoThirds * (dist1 + dist2) + negOneThird * chamferValue;
            return Mathf.LerpUnclamped(curved, flat, concavityValue);
        }

        float ComputeSdf()
        {
            var pos = localPoint;
            var size = new Vector2(rotatedRectWidth, rotatedRectHeight);
            var outline = quadData.OutlineExpandsOutward ? rotatedOutlineWidth : 0f;

            Vector4 chamfer, concavity;
            if (AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.ChamferAndCollapse))
            {
                chamfer = quadData.GetAdjustedChamfer(rectTransform) * rotationScaleFactor;
                concavity = quadData.GetAdjustedConcavity();
                if (ConcavityIsSmoothing)
                {
                    concavity.x = Mathf.Lerp(2, 10, concavity.x);
                    concavity.y = Mathf.Lerp(2, 10, concavity.y);
                    concavity.z = Mathf.Lerp(2, 10, concavity.z);
                    concavity.w = Mathf.Lerp(2, 10, concavity.w);
                }
            }
            else
            {
                chamfer = concavity = Vector4.zero;
            }

            var collapseAmount = quadData.EdgeCollapseAmountIsAbsolute ? Mathf.Min(1, quadData.CollapseEdgeAmountAbsolute / (quadData.CollapsedEdge <= QuadData.CollapsedEdgeType.Bottom ? innerRectWidth : innerRectHeight)) : Mathf.Clamp01(quadData.CollapseEdgeAmount);
            if (quadData.MirrorCollapse)
                collapseAmount = Mathf.Min(collapseAmount, 0.9999f);

            var collapseDelta = (size - new Vector2(2,2) * outline) * collapseAmount;
            if (quadData.CollapseIntoParallelogram)
                collapseAmount = 0f; // Cannot yield a triangle, so prevent corner function from trying to affect a non-existent corner.

            var collapseEdgeInt = (int)quadData.CollapsedEdge;
            var collapsePos = quadData.CollapseEdgePosition;

            if (quadData.MirrorCollapse)
            {
                if (collapseEdgeInt <=1)
                {
                    if (pos.y < size.y * 0.5)
                    {
                        pos.y = size.y - pos.y;
                        chamfer = new Vector4(chamfer.z, chamfer.w, chamfer.x, chamfer.y);
                        concavity = new Vector4(concavity.z, concavity.w, concavity.x, concavity.y);
                    }
                }
                else if (pos.x > size.x * 0.5)
                {
                    pos.x = size.x - pos.x;
                    chamfer = new Vector4(chamfer.y, chamfer.x, chamfer.w, chamfer.z);
                    concavity = new Vector4(concavity.y, concavity.x, concavity.w, concavity.z);
                }
            }

            var bl = new Vector2(outline, outline);
            var br = new Vector2(size.x - outline, outline);
            var tr = new Vector2(size.x - outline, size.y - outline);
            var tl = new Vector2(outline, size.y - outline);

            if (collapseEdgeInt == 0)
            {
                tl.x += collapseDelta.x * collapsePos;
                tr.x += collapseDelta.x * (collapsePos - 1);
                if (quadData.CollapseIntoParallelogram)
                {
                    br.x -= collapseDelta.x * collapsePos;
                    bl.x += collapseDelta.x * (1 - collapsePos);
                }
            }
            else if (collapseEdgeInt == 1)
            {
                bl.x += collapseDelta.x * collapsePos;
                br.x += collapseDelta.x * (collapsePos - 1);
                if (quadData.CollapseIntoParallelogram)
                {
                    tr.x -= collapseDelta.x * collapsePos;
                    tl.x += collapseDelta.x * (1 - collapsePos);
                }
            }
            else if (collapseEdgeInt == 2)
            {
                bl.y += collapseDelta.y * collapsePos;
                tl.y += collapseDelta.y * (collapsePos - 1);
                if (quadData.CollapseIntoParallelogram)
                {
                    tr.y -= collapseDelta.y * collapsePos;
                    br.y += collapseDelta.y * (1 - collapsePos);
                }
            }
            else
            {
                br.y += collapseDelta.y * collapsePos;
                tr.y += collapseDelta.y * (collapsePos - 1);
                if (quadData.CollapseIntoParallelogram)
                {
                    tl.y -= collapseDelta.y * collapsePos;
                    bl.y += collapseDelta.y * (1 - collapsePos);
                }
            }

            var edgeDistances = new Vector4(
                SdLine(pos, tl, tr), // Top
                SdLine(pos, br, bl), // Bottom
                SdLine(pos, bl, tl), // Left
                SdLine(pos, tr, br)  // Right
            );

            var outlineAdjust = quadData.OutlineAccommodatesCollapsedEdge ? 0.41421356237f / innerAspect : 1f;
            var adjustedOutline = outline * outlineAdjust;
            edgeDistances += new Vector4(adjustedOutline, adjustedOutline, adjustedOutline, adjustedOutline);

            var sdf = Mathf.Min(Mathf.Min(edgeDistances.x, edgeDistances.y), Mathf.Min(edgeDistances.z, edgeDistances.w));
            if (!AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.ChamferAndCollapse) || chamfer.magnitude <= 0)
                return sdf;

            if (edgeDistances.x < chamfer.x && edgeDistances.z < chamfer.x)
                sdf = Mathf.Min(sdf, CalculateCornerSDF(edgeDistances.x, edgeDistances.z, chamfer.x, concavity.x, ConcavityIsSmoothing));
            if (edgeDistances.x < chamfer.y && edgeDistances.w < chamfer.y)
                sdf = Mathf.Min(sdf, CalculateCornerSDF(edgeDistances.x, edgeDistances.w, chamfer.y, concavity.y, ConcavityIsSmoothing));
            if (edgeDistances.y < chamfer.z && edgeDistances.z < chamfer.z)
                sdf = Mathf.Min(sdf, CalculateCornerSDF(edgeDistances.y, edgeDistances.z, chamfer.z, concavity.z, ConcavityIsSmoothing));
            if (edgeDistances.y < chamfer.w && edgeDistances.w < chamfer.w)
                sdf = Mathf.Min(sdf, CalculateCornerSDF(edgeDistances.y, edgeDistances.w, chamfer.w, concavity.w, ConcavityIsSmoothing));

            if (collapseAmount < 1f)
                return sdf;

            sdf = collapseEdgeInt switch
            {
                0 when edgeDistances.z < chamfer.x && edgeDistances.w < chamfer.x => Mathf.Min(sdf, CalculateCornerSDF(edgeDistances.z, edgeDistances.w, chamfer.x, concavity.x, ConcavityIsSmoothing)),
                1 when edgeDistances.z < chamfer.w && edgeDistances.w < chamfer.w => Mathf.Min(sdf, CalculateCornerSDF(edgeDistances.z, edgeDistances.w, chamfer.w, concavity.w, ConcavityIsSmoothing)),
                2 when edgeDistances.x < chamfer.x && edgeDistances.y < chamfer.x => Mathf.Min(sdf, CalculateCornerSDF(edgeDistances.x, edgeDistances.y, chamfer.x, concavity.x, ConcavityIsSmoothing)),
                _ => Mathf.Min(sdf, CalculateCornerSDF(edgeDistances.x, edgeDistances.y, chamfer.w, concavity.w, ConcavityIsSmoothing))
            };

            return sdf;
       }

        float ComputeCutout()
        {
            if (!AdvancedRaycastFlags.HasFlag(AdvancedRaycastOptions.Cutout))
                return 1f;

            if (quadData.Cutout == QuadData.CutoutType.SDF)
                return ComputeSDFCutout();

            var simpleCutout = quadData.SimpleCutout;
            if (quadData.CutoutPositionIgnoresExpandedOutlines && quadData.OutlineExpandsOutward && rotatedOutlineWidth > 0f)
                simpleCutout = Vector4.Min(Vector4.one * 1023.5f, simpleCutout + Vector4.one * rotatedOutlineWidth);
            var cutout = new Vector4(
                quadData.SimpleCutoutEdgeEnabled[0] ? simpleCutout.x : midPoint.x,
                quadData.SimpleCutoutEdgeEnabled[1] ? simpleCutout.y : midPoint.x,
                quadData.SimpleCutoutEdgeEnabled[2] ? simpleCutout.z : midPoint.y,
                quadData.SimpleCutoutEdgeEnabled[3] ? simpleCutout.w : midPoint.y
            );

            var effectiveSoftness = Mathf.Max(quadData.Softness, 0.001f);
            var leftCutout = Smoothstep(localPoint.x - effectiveSoftness, localPoint.x, cutout.x);
            var rightCutout = Smoothstep(localPoint.x + effectiveSoftness, localPoint.x, rotatedRectWidth - cutout.y);
            var topCutout = Smoothstep(localPoint.y + effectiveSoftness, localPoint.y, rotatedRectHeight - cutout.z);
            var bottomCutout = Smoothstep(localPoint.y - effectiveSoftness, localPoint.y, cutout.w);

            var cutoutAlpha = quadData.CutoutRule == QuadData.SimpleCutoutRule.OR
                ? Mathf.Min(leftCutout + rightCutout, topCutout + bottomCutout)
                : Mathf.Max(leftCutout + rightCutout, topCutout + bottomCutout);

            return quadData.InvertCutout ? 1 - cutoutAlpha : cutoutAlpha;
        }

        float ComputeSDFCutout()
        {
            var texSize = new Vector2(rotatedRectWidth, rotatedRectHeight);
            var sdfCutoutDecodeSize = new Vector2(innerRectWidth, innerRectHeight) * rotationScaleFactor;
            var sdfCutoutSizeReferenceSize = texSize;
            var sdfCutoutReferenceSize = texSize;
            var sdfCutoutReferenceOrigin = Vector2.zero;
            if (quadData.CutoutPositionIgnoresExpandedOutlines && quadData.OutlineExpandsOutward && rotatedOutlineWidth > 0f)
            {
                sdfCutoutReferenceSize = sdfCutoutDecodeSize;
                sdfCutoutReferenceOrigin = Vector2.one * rotatedOutlineWidth;
            }

            Vector2 shapeCenter, subSize;
            if (quadData.SDFCutoutUsesAnchors)
            {
                var anchorReferenceSize = new Vector2(fullRectWidth, fullRectHeight);
                var anchorReferenceOrigin = Vector2.zero;
                if (quadData.CutoutPositionIgnoresExpandedOutlines && quadData.OutlineExpandsOutward && rotatedOutlineWidth > 0f)
                {
                    anchorReferenceSize = new Vector2(innerRectWidth, innerRectHeight);
                    anchorReferenceOrigin = Vector2.one * outlineWidth;
                }
                quadData.GetSDFCutoutAnchorPositionAndSize(anchorReferenceOrigin, anchorReferenceSize, null, out shapeCenter, out subSize);
                shapeCenter *= rotationScaleFactor;
                subSize = Vector2.Min(Vector2.one * 16383.5f, Vector2.Max(Vector2.zero, subSize * rotationScaleFactor));
            }
            else
            {
                var sdfCutoutSize = quadData.SDFCutoutSize;
                if (quadData.SDFCutoutSizeIsAbsolute)
                {
                    var sdfCutoutReferenceSizeRcp = new Vector2(
                        1f / Mathf.Max(sdfCutoutSizeReferenceSize.x, 0.001f),
                        1f / Mathf.Max(sdfCutoutSizeReferenceSize.y, 0.001f)
                    );
                    sdfCutoutSize = Vector2.Scale(quadData.SDFCutoutSize, sdfCutoutReferenceSizeRcp);
                }
                else if (quadData.CutoutPositionIgnoresExpandedOutlines && quadData.OutlineExpandsOutward && rotatedOutlineWidth > 0f)
                    sdfCutoutSize = Vector2.Scale(sdfCutoutSize, new Vector2(sdfCutoutDecodeSize.x / Mathf.Max(sdfCutoutSizeReferenceSize.x, 0.001f), sdfCutoutDecodeSize.y / Mathf.Max(sdfCutoutSizeReferenceSize.y, 0.001f)));
                sdfCutoutSize = Vector2.Min(Vector2.one * 2f, Vector2.Max(Vector2.zero, sdfCutoutSize));
                shapeCenter = sdfCutoutReferenceOrigin + (quadData.SDFCutoutPositionIsAbsolute
                    ? sdfCutoutReferenceSize * 0.5f + quadData.SDFCutoutPosition
                    : Vector2.Scale(quadData.SDFCutoutPosition, sdfCutoutReferenceSize));
                subSize = Vector2.Min(Vector2.one * 16383.5f, Vector2.Scale(sdfCutoutSizeReferenceSize, sdfCutoutSize));
            }
            shapeCenter = Vector2.Min(Vector2.one * 8191.5f, Vector2.Max(Vector2.one * -8192f, shapeCenter));

            var effectiveSoftness = Mathf.Max(quadData.Softness, 0.001f);
            subSize += Vector2.one * (2f + effectiveSoftness);
            Vector2 subPos;

            var mirrorH = ((int)quadData.SDFCutoutMirror & 1) != 0;
            var mirrorV = ((int)quadData.SDFCutoutMirror & 2) != 0;
            if (mirrorH || mirrorV)
            {
                var diagonalMirror = quadData.SDFCutoutMirrorIsDiagonal;
                var texCenter = texSize * 0.5f;
                var mirroredPos = localPoint - texCenter;
                shapeCenter -= texCenter;
                if (diagonalMirror)
                {
                    mirroredPos = new Vector2(mirroredPos.x - mirroredPos.y, mirroredPos.x + mirroredPos.y);
                    shapeCenter = new Vector2(shapeCenter.x - shapeCenter.y, shapeCenter.x + shapeCenter.y);
                }

                if (mirrorH)
                {
                    mirroredPos.x = Mathf.Abs(mirroredPos.x);
                    shapeCenter.x = Mathf.Abs(shapeCenter.x);
                }

                if (mirrorV)
                {
                    mirroredPos.y = Mathf.Abs(mirroredPos.y);
                    shapeCenter.y = Mathf.Abs(shapeCenter.y);
                }

                subPos = mirroredPos - shapeCenter;
                if (diagonalMirror)
                {
                    subPos = new Vector2(subPos.x + subPos.y, subPos.y - subPos.x) * 0.5f;
                    subSize = new Vector2(subSize.y, subSize.x);
                }
            }
            else
            {
                subPos = localPoint - shapeCenter;
            }

            var angle = -Mathf.Repeat(quadData.SDFCutoutRotation, 360f) * Mathf.Deg2Rad;
            var s = Mathf.Sin(angle);
            var c = Mathf.Cos(angle);
            subPos = new Vector2(subPos.x * c + subPos.y * s, -subPos.x * s + subPos.y * c);
            subPos += subSize * 0.5f;

            var chamfer = quadData.GetAdjustedCutoutChamfer(rectTransform) * rotationScaleFactor;
            var concavity = quadData.SDFCutoutConcavity;
            var concavityIsSmoothing = quadData.SDFCutoutIsSquircle;
            if (concavityIsSmoothing)
            {
                concavity.x = Mathf.Lerp(2f, 10f, concavity.x);
                concavity.y = Mathf.Lerp(2f, 10f, concavity.y);
                concavity.z = Mathf.Lerp(2f, 10f, concavity.z);
                concavity.w = Mathf.Lerp(2f, 10f, concavity.w);
            }

            var edgeDistances = new Vector4(subPos.x, subPos.y, subSize.x - subPos.x, subSize.y - subPos.y);
            var subSdf = Mathf.Min(Mathf.Min(edgeDistances.x, edgeDistances.y), Mathf.Min(edgeDistances.z, edgeDistances.w));
            if (edgeDistances.x < chamfer.x && edgeDistances.w < chamfer.x)
                subSdf = Mathf.Min(subSdf, CalculateCornerSDF(edgeDistances.x, edgeDistances.w, chamfer.x, concavity.x, concavityIsSmoothing));
            if (edgeDistances.z < chamfer.y && edgeDistances.w < chamfer.y)
                subSdf = Mathf.Min(subSdf, CalculateCornerSDF(edgeDistances.z, edgeDistances.w, chamfer.y, concavity.y, concavityIsSmoothing));
            if (edgeDistances.x < chamfer.z && edgeDistances.y < chamfer.z)
                subSdf = Mathf.Min(subSdf, CalculateCornerSDF(edgeDistances.x, edgeDistances.y, chamfer.z, concavity.z, concavityIsSmoothing));
            if (edgeDistances.z < chamfer.w && edgeDistances.y < chamfer.w)
                subSdf = Mathf.Min(subSdf, CalculateCornerSDF(edgeDistances.z, edgeDistances.y, chamfer.w, concavity.w, concavityIsSmoothing));

            subSdf -= 1.5f + effectiveSoftness * 0.5f;
            if (!quadData.InvertCutout)
                subSdf = -subSdf;

            if (quadData.CutoutBehaviour == QuadData.SDFCutoutBehaviour.OutlineOnly || quadData.CutoutBehaviour == QuadData.SDFCutoutBehaviour.OutlineAndInterior)
                return Mathf.Clamp01(subSdf);

            sdf = Mathf.Min(sdf, subSdf);
            return 1f;
        }

        float Smoothstep(float a, float b, float t)
        {
            var d = b - a;
            if (d == 0)
                return 0f;

            t = Mathf.Clamp((t - a) / d, 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }

    protected override void OnDidApplyAnimationProperties()
    {
        SetVerticesDirty();
        SetRaycastDirty();
    }

    public override Material materialForRendering
    {
        get
        {
            var mat = base.materialForRendering;
            GetMaterialForRenderingBlur(ref mat);
            return mat;
        }
    }
    partial void GetMaterialForRenderingBlur(ref Material mat);
    partial void IsBlurEnabled (ref bool enabled);
}
}
