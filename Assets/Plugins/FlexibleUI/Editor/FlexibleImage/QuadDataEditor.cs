using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditorInternal;

namespace JeffGrawAssets.FlexibleUI
{
public class QuadDataEditor
{
    private static GUIStyle _sectionStyle;
    private static GUIStyle SectionStyle
    {
        get
        {
            if (_sectionStyle == null && EditorStyles.foldoutHeader != null)
                _sectionStyle = new GUIStyle(EditorStyles.foldoutHeader) { normal = { textColor = Color.white }, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, fontSize = 16 };
            return _sectionStyle ?? GUIStyle.none;
        }
    }
    private static GUIStyle _unavailableFeatureStyle;
    private static GUIStyle UnavailableFeatureStyle
    {
        get
        {
            if (_unavailableFeatureStyle == null && EditorStyles.helpBox != null)
                _unavailableFeatureStyle = new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter };
            return _unavailableFeatureStyle ?? GUIStyle.none;
        }
    }
    private static GUIStyle _advancedButtonActiveStyle;
    private static GUIStyle AdvancedButtonActiveStyle
    {
        get
        {
            if (_advancedButtonActiveStyle == null && GUI.skin?.button != null)
                _advancedButtonActiveStyle = new GUIStyle(GUI.skin.button) { normal = { textColor = EditorGUIUtility.isProSkin ? Color.cyan : Color.black }, hover = { textColor = Color.cyan }, active = { textColor = Color.cyan } };
            return _advancedButtonActiveStyle ?? GUIStyle.none;
        }
    }
    private static GUIStyle _moduleButtonStyle;
    private static GUIStyle ModuleButtonStyle => _moduleButtonStyle ??= new GUIStyle(GUI.skin.button) { richText = true };
    private static GUIContent _shapeQuickActionsContent;
    private static GUIContent ShapeQuickActionsContent => _shapeQuickActionsContent?.image != null ? _shapeQuickActionsContent : _shapeQuickActionsContent = new GUIContent(AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath("4e6f9c2a7b1d4385a0c6e9f2137d5b84")), "Shape Quick Actions");

    private const string ShowAnimationSectionKey = "jg_showAnimationSection";
    private const string ShowColorSectionKey = "jg_showColorSection";
    private const string ShowShapeSectionKey = "jg_showShapeSection";
    private const string ShowSkewKey = "jg_showSkew";
    private const string ShowCornersKey = "jg_showCorners";
    private const string ShowCutoutKey = "jg_showCutout";
    private const string ShowColorPresetKey = "jg_showColorPreset";
    private const string ShowPrimaryColorKey = "jg_showPrimaryColor";
    private const string ShowOutlineKey = "jg_showOutline";
    private const string ShowProceduralGradientKey = "jg_showProceduralGradient";
    private const string ShowPatternKey = "jg_showPattern";
    private const string ShowPrimaryGridAdvancedKey = "jg_showPrimaryGridAdvanced";
    private const string ShowOutlineGridAdvancedKey = "jg_showOutlineGridAdvanced";
    private const string ShowProceduralGradientGridAdvancedKey = "jg_showProcedrualGradientGridAdvanced";
    private const string ShowPatternGridAdvancedKey = "jg_showPatternGridAdvanced";
    private const float UndoButtonWidth = 20f;

    private enum SecondaryColorArea { Interior, Outline, All }
    private enum Module { Outline, Gradient, Pattern, Skew, Stroke, Cutout }

    private static readonly GUIContent SpriteContent = EditorGUIUtility.TrTextContent("Source Image"); 
    private static readonly GUIContent AnimationModeContent = new ("Driven By", "Whether animation state is driven by a Selectable (Button, Toggle, etc.) or via script.");
    private static readonly GUIContent FixHighlightedContent = new ("Highlighted Fix", "Allow transition from Selected to Highlighted state. Selectables by default do not perform this transition.");
    private static readonly GUIContent PrimaryColorFadeContent = new ("Fade", "Controls the visibility of the primary color independently of its alpha. Useful if you need to adjust primary color transparency without affecting secondary or outline color. When blur is enabled, controls how much the primary color is drawn over top the blur.");
    private static readonly GUIContent PresetMixContent = new ("Preset Mix", "How much the preset is mixed in to the instance color. At 1, the instance color is ignored.");
    private static readonly GUIContent NormalizeCornersContent = new ("Normalize", "Constrains chamfer so each corner can affect at most half the image.");
    private static readonly GUIContent CornerChamferContent = new ("Chamfer", "The size of the chamfer at each corner.");
    private static readonly GUIContent IsSquircleContent = new ("Squircle", "Instead of concavity, corners can be smoothed to create squircles.");
    private static readonly GUIContent CornerConcavityContent = new ("Concavity", "Whether the chamfer is convex (0), flat (1), or concave (2)");
    private static readonly GUIContent CornerSmoothingContent = new ("Smoothing");
    private static readonly GUIContent StrokeContent = new ("Stroke", "If non-zero, will hollow out the image after a certain distance in canvas units.");
    private static readonly GUIContent AddInteriorOutlineContent = new ("Add Interior Outline", "Adds an interior outline based on Stroke position.");
    private static readonly GUIContent SoftnessContent = new ("Softness", "How much to soften transition areas of the image. A small value of 0.5 or 1 effectively anti-aliases the image.");
    private static readonly GUIContent FeatherModeContent = new ("Feather Mode", "Whether softness feathers \"inwards,\" \"outwards,\" or in both directions.");
    private static readonly GUIContent SizeModifierContent = new ("Size Modifier", "How much to grow or shrink the image, in canvas units.");
    private static readonly GUIContent OffsetContent = new ("Offset", "The image offset, in canvas units.");
    private static readonly GUIContent RotationContent = new ("Rotation", "Z-axis rotation in degrees.");
    private static readonly GUIContent CollapseIntoParallelogramContent = new ("Parallelogram", "Applies a collapse at the inverse position of the opposite edge, yielding a parallelogram.");
    private static readonly GUIContent MirrorCollapseContent = new ("Mirror", "Mirrors the collapse, transforming parallelograms into chevrons, and trapezoids into... various other shapes.");
    private static readonly GUIContent OutlineWidthContent = new ("Width", "Width of the outline (finer gradations when softness is applied).");
    private static readonly GUIContent OutlineExpandsOutwardsContent = new ("Expand Outwards", "Rather than growing towards the interior, the outline will grow outwards from the shape. Useful for glows and shadows.");
    private static readonly GUIContent OutlineAccommodatesCollapsedEdgeContent = new ("Accommodate Skew", "Enlarges the image so that corners of the outline are not cut off when an edge is partially/fully collapsed. This affects vertex color gradients by pushing them outside the border perimeter.");
    private static readonly GUIContent OutlineFadeTowardsPerimeterContent = new ("Fade To Perimeter", "The outline will fade towards the perimeter of the image, useful for glows and shadows.");
    private static readonly GUIContent OutlineAdjustsChamferContent = new ("Massage Chamfer", "Chamfer will be automatically adjusted to maintain consistency and mitigate mach lines. Does nothing when outline direction is 0.");
    private static readonly GUIContent SkewContent = new ("Skew", "Adjustments to create trapezoids, triangles, and parallelograms.");
    private static readonly GUIContent CornersContent = new ("Corners", "Apply chamfer to corners, and set the concavity of the chamfer.");
    private static readonly GUIContent CutoutContent = new ("Cutout", "Specify an area that will be cut out from the image.");
    private static readonly GUIContent SimpleCutoutRuleContent = new ("Rule", "When set to AND, the intersecting area of adjacent edges is cutout. When set to OR, there is no adjacency requirement.");
    private static readonly GUIContent SdfCutoutBehaviourContent = new ("Behaviour");
    private static readonly GUIContent SdfCutoutMirrorContent = new ("Mirror");
    private static readonly GUIContent SdfCutoutMirrorIsDiagonalContent = new ("Diagonal");
    private static readonly GUIContent SdfCutoutPositioningContent = new ("Placement");
    private static readonly GUIContent InvertCutoutContent = new ("Invert", "Rather than defining an invisible area, cutout will define the visible area.");
    private static readonly GUIContent CutoutPositionIgnoresExpandedOutlinesContent = new ("Ignore Expanded Outline", "Keeps cutout position measured from the pre-outline image bounds when Expand Outwards is enabled.");
    private static readonly GUIContent CutoutAffectsOutlineOnlyContent = new ("Outline Only", "The cutout zone will only affect the outline, and leave the rest of the image intact.");
    private static readonly GUIContent ProceduralGradientInvertContent = new ("Invert", "Inverts the procedural gradient.");
    private static readonly GUIContent OutlineAlphaIsBlendContent = new ("Alpha Is Blend", "The outline alpha channel controls its blend with the primary color, instead of transparency.");
    private static readonly GUIContent ProceduralGradientAlphaIsBlendContent = new ("Alpha Is Blend", "The procedural gradient color alpha channel controls its blend with lower colors layers, instead of transparency.");
    private static readonly GUIContent PatternAlphaIsBlendContent = new ("Alpha Is Blend", "The pattern color alpha channel controls its blend with lower color layers, instead of transparency.");
    private static readonly GUIContent ProceduralGradientStrengthContent = new ("Gradient Strength", "The overall strength of the gradient.");
    private static readonly GUIContent ProceduralGradientRevealStrengthContent = new ("Reveal Strength", "The strength of the pointer-reveal effect.");
    private static readonly GUIContent ProceduralGradientAspectCorrectionContent = new ("Aspect Correction", "Keeps the gradient circular when Size.x and Size.y are equal. Otherwise, the gradient will stretch based on the aspect ratio of its container.");
    private static readonly GUIContent ConicalGradientCurvatureContent = new ("Curvature");
    private static readonly GUIContent ConicalGradientTailStrengthContent = new ("Strength");
    private static readonly GUIContent NoiseSeedContent = new ("Seed");
    private static readonly GUIContent NoiseScaleContent = new ("Scale");
    private static readonly GUIContent NoiseEdgeContent = new ("Edge");
    private static readonly GUIContent NoiseStrengthContent = new ("Strength");
    private static readonly GUIContent PatternDensityContent = new ("Density", "Controls the size of the pattern.");
    private static readonly GUIContent PatternSpeedContent = new ("Speed", "Controls the direction and speed of pattern movement.");
    private static readonly GUIContent PatternOffsetContent = new ("Offset", "Controls the offset of the pattern.");
    private static readonly GUIContent PatternOriginPosContent = new ("Origin");
    private static readonly GUIContent FitOriginalBoundsContent = new ("Fit Original Rect", "Resize the image so that after rotation it will fit inside its non-rotated rect.");
    private static readonly GUIContent ScreenSpaceContent = new ("Screen Space", "Display the effect using screen space coordinates as opposed to local coordinates. Useful for creating a more integrated appearance across several components.");
    private static readonly GUIContent SoftPatternContent = new ("Soft", "Softens the edges of the pattern.");
    private static readonly GUIContent PositionFromPointerContent = new ("Pointer Adjusts Pos");
    private static readonly GUIContent RevealedByPointerContent = new ("Revealed By Pointer");
    private static readonly GUIContent NoiseGradientAlternateModeContent = new ("Alternate Mode");
    private static readonly GUIContent ProceduralGradientAngleContent = new ("Angle");
    private static readonly GUIContent MeshSubdivisionsContent = new ("Mesh Subdivisions", "The appearance of vertex color gradients can be altered by subdividing the Image mesh.");
    private static readonly GUIContent MeshTopologyContent = new ("Topology", "The appearance of vertex color gradients can be altered by changing the topology of the Image mesh.");
    private static readonly GUIContent UnwindIndexContent = new ("Unwind:   Idx", "The index of the animation substate to \"unwind\" to before transitioning to the next animation state. Disabled when set to -1. Does nothing when the current substate is already lower than the index. Note that the animation is unwound to the very *beginning* of the substate, which is the same as the last frame of the substate *before* the index.");
    private static readonly GUIContent UnwindRateContent = new ("Rate", "The rate that the animation will be unwound. For example, if 2 an otherwise 1s substate will be unwound in only 0.5s");
    private static readonly GUIContent SizeModifierAspectCorrectionContent = new ("Aspect Correction", "Scales the size modifier of the Rect's lesser dimension by lesserDimension/greaterDimension, so that the overall aspect of the image will not change when size modifier x and y are equal.");
    private static readonly GUIContent ColorDimensionsContent = new ("Dimensions", "The dimensions of the color grid. Elements in the grid are applied as vertex color, meaning gradients are calculated by interpolating vertex attributes rather than per-pixel.");
    private static readonly GUIContent UVRectContent = new ("UV Rect", "Defines the UV region to sample from the texture (x, y, width, height), which also affects procedural shape. Similar to Camera viewport rect but for texture coordinates. Default (0,0,1,1) uses the full UV.");
    private static readonly GUIContent PosContent = new ("Pos");
    private static readonly GUIContent ScaleContent = new ("Scale");
    private static readonly GUIContent RotationContentNoTip = new ("Rotation");
    private static readonly GUIContent SizeContent = new ("Size");
    private static readonly GUIContent CopyContent = new ("Copy");
    private static readonly GUIContent PasteContent = new ("Paste");
    private static readonly GUIContent PasteAllContent = new ("Paste All");
    private static readonly GUIContent LightenContent = new ("Lighten");
    private static readonly GUIContent DarkenContent = new ("Darken");
    private static readonly GUIContent OrientationContent = new ("Orientation");
    private static readonly GUIContent FillContent = new ("Fill");
    private static readonly GUIContent ShapeContent = new ("Shape");
    private static readonly GUIContent FractalContent = new ("Fractal");
    private static readonly GUIContent StaticOffsetContent = new ("Static Offset");
    private static readonly GUIContent PatternLineThicknessContent = new ("Line Thickness");
    private static readonly GUIContent PatternLineThicknessUnavailableContent = new ("Unavailable If Blur Enabled", "There are a limited number of bits available in shader vertex attributes, as well as an effective limit on varyings. Flexible Image runs up against those limits. Blur Alpha Blend and Pattern Line Thickness overlap, as the alternative would be that one or the other would not be able to fit.\n\nIt's possible I will find a workaround at some point, but if you need this Pattern Line Thickness and don't need Blur Alpha Blend, it's not too difficult to modify the shader (you can ask me for help with that).");
    private static readonly GUIContent PatternLineThicknessUnavailableContentShort = new ("Unavailable", "There are a limited number of bits available in shader vertex attributes, as well as an effective limit on varyings. Flexible Image runs up against those limits. Blur Alpha Blend and Pattern Line Thickness overlap, as the alternative would be that one or the other would not be able to fit.\n\nIt's possible I will find a workaround at some point, but if you need this Pattern Line Thickness and don't need Blur Alpha Blend, it's not too difficult to modify the shader (you can ask me for help with that).");
    private static readonly GUIContent PatternSpriteRotationModeContent = new ("Rotation Mode", "Whether rotation affects the orientation of the sprite, which can then only be offset in its \"forward\" vector, or whether rotation affects the direction of the offset, while the sprite itself will not be rotated");
    private static readonly GUIContent PatternSpriteRotationContent = new ("Rotation", "Why are you rotating the sprite with an integer? Because there only 8-bits left for this in vertex attributes, so that fp values would be imprecise. This way, you know what you're getting!");
    private static readonly GUIContent PatternOffsetRotationContent = new ("Rotation", "Rotates the direction of the \"offset\" or \"speed.\" Only cardinals and diagonals are supported.");
    private static readonly GUIContent PatternSpriteRotationUnavailableContent = new ("Unavailable If Blur Enabled", "There are a limited number of bits available in shader vertex attributes, as well as an effective limit on varyings. Flexible Image runs up against those limits. Blur Alpha Blend and Pattern Sprite Rotation overlap, as the alternative would be that one or the other would not be able to fit.\n\nIt's possible I will find a workaround at some point, but if you need this Pattern Sprite Rotation and don't need Blur Alpha Blend, it's not too difficult to modify the shader (you can ask me for help with that).");
    private static readonly GUIContent PatternSpriteRotationUnavailableContentShort = new ("Unavailable", "There are a limited number of bits available in shader vertex attributes, as well as an effective limit on varyings. Flexible Image runs up against those limits. Blur Alpha Blend and Pattern Sprite Rotation overlap, as the alternative would be that one or the other would not be able to fit.\n\nIt's possible I will find a workaround at some point, but if you need this Pattern Sprite Rotation and don't need Blur Alpha Blend, it's not too difficult to modify the shader (you can ask me for help with that).");
    private static readonly GUIContent NormalizeChamferUnavailableContent = new ("Unavailable If Skew Mirrored", "Mirroring the collapsed edge makes normalization quite complex. At some point, I will probably find a workaround.");
    private static readonly GUIContent NormalizeChamferUnavailableContentShort = new ("Unavailable", "Mirroring the collapsed edge makes normalization quite complex. At some point, I will probably find a workaround.");
    private static readonly GUIContent NDirectionContent = new ("   N");
    private static readonly GUIContent EDirectionContent = new ("   E");
    private static readonly GUIContent SDirectionContent = new ("   S");
    private static readonly GUIContent WDirectionContent = new ("   W");
    private static readonly GUIContent NWContent = new ("NW");
    private static readonly GUIContent NEContent = new (" NE");
    private static readonly GUIContent SWContent = new ("SW");
    private static readonly GUIContent SEContent = new (" SE");
    private static readonly GUIContent XContent = new ("X");
    private static readonly GUIContent YContent = new ("Y");
    private static readonly GUIContent ZContent = new ("Z");
    private static readonly GUIContent WContent = new ("W");
    private static readonly GUIContent HContent = new ("H");
    private static readonly GUIContent TContent = new ("T");
    private static readonly GUIContent BContent = new ("B");
    private static readonly GUIContent LContent = new ("L");
    private static readonly GUIContent RContent = new ("R");

    private static readonly string[] AnimationStateNames = { "Normal", "Highlighted", "Pressed", "Selected", "Disabled" };
    private static readonly string[] ProceduralGradientNames = { "SDF", "Angle", "Radial", "Conical", "Noise" };
    private static readonly string[] ProceduralGradientSubFeatureNames = { FlexibleImageFeatureManager.ProceduralGradientSDFSubFeatureID, FlexibleImageFeatureManager.ProceduralGradientAngleSubFeatureID, FlexibleImageFeatureManager.ProceduralGradientRadialSubFeatureID, FlexibleImageFeatureManager.ProceduralGradientConicalSubFeatureID, FlexibleImageFeatureManager.ProceduralGradientNoiseSubFeatureID };

    private static readonly string[] RelativeAbsoluteDropDownNames = { "Relative", "Absolute" };
    private static readonly string[] SdfCutoutPositioningNames = { "Direct", "Anchors" };
    private static readonly string[] PatternCategoryNames = { "Line", "Shape", "Grid", "Fractal", "Sprite" };
    private static readonly string[] PatternCategorySubFeatureNames = { FlexibleImageFeatureManager.PatternLineSubFeatureID, FlexibleImageFeatureManager.PatternShapeSubFeatureID, FlexibleImageFeatureManager.PatternGridSubFeatureID, FlexibleImageFeatureManager.PatternFractalSubFeatureID, FlexibleImageFeatureManager.PatternSpriteSubFeatureID };
    private static readonly string[] PatternLineNames = { "Horizontal", "Vertical", "Slash", "Backslash" };
    private static readonly string[] PatternShapeNames = { "Diamond", "Circle", "Square", "Cross" };
    private static readonly string[] PatternGridNames = { "Diamond", "Square", "Diagonal", "Cardinal" };
    private static readonly string[] SpriteOffsetRotationDirectionNames = { "0°", "45°", "90°", "135°" };
    private static readonly string[] CutoutTypeNames = { "Simple", "SDF" };
    private static readonly string[] CutoutTypeSubFeatureNames = { FlexibleImageFeatureManager.CutoutSimpleSubFeatureID, FlexibleImageFeatureManager.CutoutSdfSubFeatureID };
    private static readonly int[] PatternCategoryEnumValues = { 0, 4, 29, 25, 26};
    private static readonly int[] PatternLineValues = { 3, 1, 2, 0 };
    private static readonly int[] PatternShapeValues = { 4, 9, 14, 19 };
    private static readonly int[] PatternGridValues = { 30, 31, 28, 29 };
    private static readonly Module[] ColorModules = { Module.Outline, Module.Gradient, Module.Pattern };
    private static readonly Module[] ShapeModules = { Module.Skew, Module.Stroke, Module.Cutout };

    public static int KeepExistingAnimationIndices; // Not a bool, because Unity calls OnEnable x2 when undoing, where Undo.isProcessing is false the second time. So we can set this to >1 and decrement to get around funky Unity issues.

    private static ProceduralProperties proceduralPropertiesClipBoard;
    private static ProceduralAnimationState animationStateClipBoard;
    private static ProceduralAnimationState[] allAnimationStatesClipBoard = {new (), new(), new(), new(), new()};
    private static (Vector2Int dimensions, Color[] colorsArray) colorClipBoard;
    private static Color[] colorRowClipBoard, colorColumnClipBoard;
    private static (Vector4 chamfer, Vector4 concavity, float? collapsedChamfer, float? collapsedConcavity, bool normalize, bool squircle)? cornersClipBoard;
    private static readonly Dictionary<Module, (string config, string[][] animation)> moduleClipBoards = new();
    private static readonly Dictionary<string, object[]> propertyGroupClipBoards = new();
    private static Color? colorCellClipBoard;
    private static bool hasAllAnimationStatesClipBoard;
    private static int prevAnimationStateIdx, prevAnimationSubStateIdx;

    public QuadData quadData;

    public SerializedProperty enabledProperty;
    public SerializedProperty advancedQuadSettingsProperty;
    public SerializedProperty anchorMinProperty;
    public SerializedProperty anchorMaxProperty;
    public SerializedProperty anchoredPositionProperty;
    public SerializedProperty sizeDeltaProperty;
    public SerializedProperty pivotProperty;

    private SerializedProperty baseProperty;
    private SerializedProperty highlightedFixProperty;
    private SerializedProperty proceduralAnimationStatesProperty;
    private SerializedProperty colorPresetProperty;
    private SerializedProperty primaryColorWrapModeXProperty;
    private SerializedProperty primaryColorWrapModeYProperty;
    private SerializedProperty outlineColorWrapModeXProperty;
    private SerializedProperty outlineColorWrapModeYProperty;
    private SerializedProperty proceduralGradientColorWrapModeXProperty;
    private SerializedProperty proceduralGradientColorWrapModeYProperty;
    private SerializedProperty patternColorWrapModeXProperty;
    private SerializedProperty patternColorWrapModeYProperty;
    private SerializedProperty primaryColorPresetMixProperty;
    private SerializedProperty outlineColorPresetMixProperty;
    private SerializedProperty proceduralGradientColorPresetMixProperty;
    private SerializedProperty patternColorPresetMixProperty;
    private SerializedProperty normalizeChamferProperty;
    private SerializedProperty concavityIsSmoothingProperty;
    private SerializedProperty collapsedEdgeProperty;
    private SerializedProperty collapseIntoParallelogramProperty;
    private SerializedProperty mirrorCollapseProperty;
    private SerializedProperty edgeCollapseAmountIsAbsoluteProperty;
    private SerializedProperty fitRotationWithinBoundsProperty;
    private SerializedProperty outlineFadesTowardsPerimeterProperty;
    private SerializedProperty outlineAdjustsChamferProperty;
    private SerializedProperty cutoutTypeProperty;
    private SerializedProperty simpleCutoutEdgeEnabledProperty;
    private SerializedProperty simpleCutoutRuleProperty;
    private SerializedProperty sdfCutoutBehaviourProperty;
    private SerializedProperty sdfCutoutChamferNormalizeProperty;
    private SerializedProperty sdfCutoutIsSquircleProperty;
    private SerializedProperty sdfCutoutMirrorProperty;
    private SerializedProperty sdfCutoutMirrorIsDiagonalProperty;
    private SerializedProperty sdfCutoutPositionIsAbsoluteProperty;
    private SerializedProperty sdfCutoutSizeIsAbsoluteProperty;
    private SerializedProperty sdfCutoutUsesAnchorsProperty;
    private SerializedProperty sdfCutoutAnchorMinProperty;
    private SerializedProperty sdfCutoutAnchorMaxProperty;
    private SerializedProperty sdfCutoutPivotProperty;
    private SerializedProperty cutoutPositionIgnoresExpandedOutlinesProperty;
    private SerializedProperty cutoutOnlyAffectsOutlineProperty;
    private SerializedProperty invertCutoutProperty;
    private SerializedProperty proceduralGradientColorAlphaIsBlendProperty;
    private SerializedProperty patternColorAlphaIsBlendProperty;
    private SerializedProperty outlineAlphaIsBlendProperty;
    private SerializedProperty outlineExpandsOutwardsProperty;
    private SerializedProperty outlineAccommodatesCollapsedEdgeProperty;
    private SerializedProperty addInteriorOutlineProperty;
    private SerializedProperty proceduralGradientTypeProperty;
    private SerializedProperty proceduralGradientAffectsInteriorProperty;
    private SerializedProperty proceduralGradientAffectsOutlineProperty;
    private SerializedProperty patternAffectsInteriorProperty;
    private SerializedProperty patternAffectsOutlineProperty;
    private SerializedProperty proceduralGradientPositionFromPointerProperty;
    private SerializedProperty noiseGradientAlternateModeProperty;
    private SerializedProperty screenSpaceProceduralGradientProperty;
    private SerializedProperty proceduralGradientAspectCorrectionProperty;
    private SerializedProperty sizeModifierAspectCorrectionProperty;
    private SerializedProperty screenSpacePatternProperty;
    private SerializedProperty softPatternProperty;
    private SerializedProperty spritePatternRotationModeProperty;
    private SerializedProperty spritePatternOffsetDirectionDegreesProperty;
    private SerializedProperty patternOrientationProperty;
    private SerializedProperty scanlinePatternSpeedIsStaticOffsetProperty;
    private SerializedProperty patternOriginPosProperty;
    private SerializedProperty proceduralGradientInvertProperty;
    private SerializedProperty softnessFeatherModeProperty;
    private SerializedProperty strokeOriginProperty;
    private SerializedProperty primaryColorDimensionsProperty;
    private SerializedProperty outlineColorDimensionsProperty;
    private SerializedProperty proceduralGradientColorDimensionsProperty;
    private SerializedProperty patternColorDimensionsProperty;
    private SerializedProperty meshSubdivisionsProperty;
    private SerializedProperty meshTopologyProperty;
    private SerializedProperty animationStateIdxProperty;
    private SerializedProperty animationSubStateIdxProperty;

    private SecondaryColorArea proceduralGradientArea;
    private SecondaryColorArea patternArea;

    private ColorPreset prevColorPreset;
    private ColorPresetEditor _colorEditor;
    private ColorPresetEditor ColorEditor => _colorEditor != null 
                                           ? _colorEditor
                                           : _colorEditor = quadData.ColorPreset != null 
                                                          ? (ColorPresetEditor)Editor.CreateEditor(quadData.ColorPreset) 
                                                          : null;
    private ProceduralPropertiesEditor defaultProps;
    private ProceduralPropertiesEditor currentProps;
    private ReorderableList animationSubStatesList;
    private float playTime;
    private double playStartTime;
    private bool previewingAnimation;
    private readonly int quadIdx;

    public QuadDataEditor(QuadData quadData, SerializedProperty collectionProperty, int idx)
    {
        this.quadData = quadData;
        quadIdx = idx;
        baseProperty = collectionProperty.GetArrayElementAtIndex(idx);

        enabledProperty = baseProperty.FindPropertyRelative(QuadData.EnabledFieldName);
        advancedQuadSettingsProperty = baseProperty.FindPropertyRelative(QuadData.AdvancedQuadSettingsFieldName);
        anchorMinProperty = baseProperty.FindPropertyRelative(QuadData.AnchorMinFieldName);
        anchorMaxProperty = baseProperty.FindPropertyRelative(QuadData.AnchorMaxFieldName);
        anchoredPositionProperty = baseProperty.FindPropertyRelative(QuadData.AnchoredPositionFieldName);
        sizeDeltaProperty = baseProperty.FindPropertyRelative(QuadData.SizeDeltaFieldName);
        pivotProperty = baseProperty.FindPropertyRelative(QuadData.PivotFieldName);
        highlightedFixProperty = baseProperty.FindPropertyRelative(nameof(QuadData.highlightedFix));
        proceduralAnimationStatesProperty = baseProperty.FindPropertyRelative(nameof(QuadData.proceduralAnimationStates));
        colorPresetProperty = baseProperty.FindPropertyRelative(QuadData.ColorPresetFieldName);
        primaryColorWrapModeXProperty = baseProperty.FindPropertyRelative(QuadData.PrimaryColorWrapModeXFieldName);
        primaryColorWrapModeYProperty = baseProperty.FindPropertyRelative(QuadData.PrimaryColorWrapModeYFieldName);
        primaryColorPresetMixProperty = baseProperty.FindPropertyRelative(QuadData.PrimaryColorPresetMixFieldName);
        normalizeChamferProperty = baseProperty.FindPropertyRelative(QuadData.NormalizeChamferFieldName);
        concavityIsSmoothingProperty = baseProperty.FindPropertyRelative(QuadData.ConcavityIsSmoothingFieldName);
        fitRotationWithinBoundsProperty = baseProperty.FindPropertyRelative(QuadData.FitRotationWithinBoundsFieldName);
        sizeModifierAspectCorrectionProperty = baseProperty.FindPropertyRelative(QuadData.SizeModifierAspectCorrectionFieldName);
        softnessFeatherModeProperty = baseProperty.FindPropertyRelative(QuadData.SoftnessFeatherModeFieldName);
        primaryColorDimensionsProperty = baseProperty.FindPropertyRelative(QuadData.PrimaryColorDimensionsFieldName);
        meshSubdivisionsProperty = baseProperty.FindPropertyRelative(QuadData.MeshSubdivisionsFieldName);
        meshTopologyProperty = baseProperty.FindPropertyRelative(QuadData.MeshTopologyFieldName);

        ResolveModuleProperties();
        animationStateIdxProperty = baseProperty.FindPropertyRelative(nameof(QuadData.editorSelectedAnimationState));
        animationSubStateIdxProperty = baseProperty.FindPropertyRelative(nameof(QuadData.editorSelectedAnimationSubState));
        prevColorPreset = colorPresetProperty.objectReferenceValue as ColorPreset;

        if (KeepExistingAnimationIndices > 0)
        {
            KeepExistingAnimationIndices--;
        }
        else if (!SessionState.GetBool(ShowAnimationSectionKey, false))
        {
            animationStateIdxProperty.intValue = animationSubStateIdxProperty.intValue = prevAnimationStateIdx = prevAnimationSubStateIdx = 0;
        }
        else
        {
            animationStateIdxProperty.intValue = prevAnimationStateIdx;
            animationSubStateIdxProperty.intValue = Mathf.Min(prevAnimationSubStateIdx, quadData.proceduralAnimationStates[animationStateIdxProperty.intValue].proceduralProperties.Count - 1);
        }

        defaultProps = new ProceduralPropertiesEditor(quadData.proceduralAnimationStates, baseProperty, 0, 0);
        currentProps = defaultProps;

        proceduralGradientArea =
            quadData.ProceduralGradientAffectsInterior
                ? quadData.ProceduralGradientAffectsOutline
                    ? SecondaryColorArea.All
                    : SecondaryColorArea.Interior
                : SecondaryColorArea.Outline;

        patternArea =
            quadData.PatternAffectsInterior
                ? quadData.PatternAffectsOutline
                    ? SecondaryColorArea.All
                    : SecondaryColorArea.Interior
                : SecondaryColorArea.Outline;
    }

    ~QuadDataEditor() => DisposeColorPresetEditor();
    public void DisposeColorPresetEditor()
    {
        if (_colorEditor == null)
            return;

        UnityEngine.Object.DestroyImmediate(_colorEditor);
        _colorEditor = null;
    }

    public bool DrawAnimationEditor(SerializedObject so, float originalLabelWidth, SerializedProperty animationModeProperty = null, SerializedProperty selectableProperty = null, FlexibleImage flexibleImage = null, List<FlexibleImage> allFlexibleImages = null, GUIStyle sectionStyleOverride = null)
    {
        var repaint = false;
        so.Update();
        var originalGUIColor = GUI.color;
        var originalGUIBgColor = GUI.backgroundColor;
        EditorGUIUtility.labelWidth = originalLabelWidth;
    
        GUI.backgroundColor = Color.cyan;
        EditorGUIUtility.labelWidth = EditorGUIUtility.currentViewWidth;
        var sectionRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        SessionState.SetBool(ShowAnimationSectionKey, EditorGUI.Foldout(sectionRect, SessionState.GetBool(ShowAnimationSectionKey, false), "Animation", sectionStyleOverride ?? SectionStyle));
        if (GUI.Button(sectionRect, "", GUIStyle.none))
            SessionState.SetBool(ShowAnimationSectionKey, !SessionState.GetBool(ShowAnimationSectionKey, false));

        EditorGUIUtility.labelWidth = originalLabelWidth;
        GUI.backgroundColor = originalGUIBgColor;

        if (!SessionState.GetBool(ShowAnimationSectionKey, false))
            return false;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(GUI.skin.box);

        if (animationModeProperty != null)
        {
            EditorGUILayout.PropertyField(animationModeProperty, AnimationModeContent);
            if (animationModeProperty.enumValueIndex == (int)FlexibleImage.AnimationStateDrivenBy.Script)
            {
                EditorGUILayout.HelpBox( new GUIContent($"Controlled with {nameof(FlexibleImage)}.{nameof(FlexibleImage.scriptDrivenAnimationState)}\nCurrent value: {flexibleImage?.scriptDrivenAnimationState ?? 0}"));
            }
            else if (selectableProperty != null)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(selectableProperty);
                if (EditorGUI.EndChangeCheck())
                {
                    so.ApplyModifiedProperties();
                    so.Update();
                }

                if (selectableProperty.objectReferenceValue == null)
                {
                    EditorGUILayout.EndVertical();
                    return false;
                }

                if (selectableProperty.objectReferenceValue != null)
                    EditorGUILayout.PropertyField(highlightedFixProperty, FixHighlightedContent);
            }
        }

        EditorGUILayout.Space(2f);
        EditorGUILayout.BeginHorizontal();
        var isDrivenByScript = animationModeProperty != null && animationModeProperty.enumValueIndex == (int)FlexibleImage.AnimationStateDrivenBy.Script;
        for (int i = 0; i < AnimationStateNames.Length; i++)
        {
            var style = GUI.skin.button;

            if (i == 0)
                style = new GUIStyle(GUI.skin.FindStyle($"{style.name}left"));
            else if (i == AnimationStateNames.Length - 1)
                style = new GUIStyle(GUI.skin.FindStyle($"{style.name}right"));
            else
                style = new GUIStyle(GUI.skin.FindStyle($"{style.name}mid"));

            string labelStr;
            var defaultColor = GUI.color;
            if (animationStateIdxProperty.intValue == i)
            {
                labelStr = isDrivenByScript ? $"{i}" : AnimationStateNames[i];
                GUI.color = Color.cyan;
                if (EditorGUIUtility.isProSkin)
                    style.normal.textColor = style.hover.textColor = style.active.textColor = Color.cyan;
            }
            else
            {
                labelStr = isDrivenByScript ? $"{i}" : EditorGUIUtility.currentViewWidth > 350f ? AnimationStateNames[i] : AnimationStateNames[i][..1];
            }

            if (!GUILayout.Button(labelStr, style))
            {
                GUI.color = defaultColor;
                continue;
            }

            previewingAnimation = false;
            GUI.FocusControl(null);
            playTime = 0f;

            GUI.color = defaultColor;
            if (Event.current.button == 0)
            {
                animationStateIdxProperty.intValue = i;
                so.ApplyModifiedProperties();
            }
            else if (Event.current.button == 1)
            {
                var menu = new GenericMenu();
                var idx = i; // prevent closure
                var existing = proceduralAnimationStatesProperty.GetArrayElementAtIndex(idx).boxedValue as ProceduralAnimationState;

                if (existing.proceduralProperties.Count > 0)
                {
                    menu.AddItem(new GUIContent("Copy State"), false, () => { animationStateClipBoard = new ProceduralAnimationState(existing); });
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Copy State"));
                }

                if (animationStateClipBoard != null)
                {
                    menu.AddItem(new GUIContent("Paste State"), false, () =>
                    {
                        var state = new ProceduralAnimationState(animationStateClipBoard);
                        foreach (var props in state.proceduralProperties)
                            MatchModulesToQuad(props);
                        proceduralAnimationStatesProperty.GetArrayElementAtIndex(idx).boxedValue = state;
                        so.ApplyModifiedProperties();
                        so.Update();
                    });
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Paste State"));
                }

                menu.AddSeparator("");
                if (idx > 0 && existing.proceduralProperties.Count > 0)
                {
                    menu.AddItem(new GUIContent("Clear"), false, () =>
                    {
                        proceduralAnimationStatesProperty.GetArrayElementAtIndex(idx).FindPropertyRelative(nameof(ProceduralAnimationState.proceduralProperties)).ClearArray();
                        so.ApplyModifiedProperties();
                        so.Update();
                    });
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Clear"));
                }

                menu.AddSeparator("");

                menu.AddItem(new GUIContent("Copy All States"), false, () =>
                {
                    allAnimationStatesClipBoard = new ProceduralAnimationState[]
                    {
                        new(proceduralAnimationStatesProperty.GetArrayElementAtIndex(0).boxedValue as ProceduralAnimationState),
                        new(proceduralAnimationStatesProperty.GetArrayElementAtIndex(1).boxedValue as ProceduralAnimationState),
                        new(proceduralAnimationStatesProperty.GetArrayElementAtIndex(2).boxedValue as ProceduralAnimationState),
                        new(proceduralAnimationStatesProperty.GetArrayElementAtIndex(3).boxedValue as ProceduralAnimationState),
                        new(proceduralAnimationStatesProperty.GetArrayElementAtIndex(4).boxedValue as ProceduralAnimationState)
                    };
                    hasAllAnimationStatesClipBoard = true;
                });
                if (hasAllAnimationStatesClipBoard)
                {
                    menu.AddItem(new GUIContent("Paste All States"), false, () =>
                    {
                        for (int j = 0; j < AnimationStateNames.Length; j++)
                        {
                            var state = new ProceduralAnimationState(allAnimationStatesClipBoard[j]);
                            foreach (var props in state.proceduralProperties)
                                MatchModulesToQuad(props);
                            proceduralAnimationStatesProperty.GetArrayElementAtIndex(j).boxedValue = state;
                        }

                        so.ApplyModifiedProperties();
                        so.Update();
                        repaint = true;
                    });
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Paste All States"));
                }

                menu.ShowAsContext();
                Event.current.Use();
            }
        }

        EditorGUILayout.EndHorizontal();

        var proceduralAnimationStateProperty = baseProperty.FindPropertyRelative($"{nameof(QuadData.proceduralAnimationStates)}.Array.data[{animationStateIdxProperty.intValue}]");
        var propsArray = proceduralAnimationStateProperty.FindPropertyRelative($"{nameof(ProceduralAnimationState.proceduralProperties)}");

        if (animationStateIdxProperty.intValue != prevAnimationStateIdx || animationSubStatesList == null)
        {
            animationSubStatesList = new ReorderableList(so, propsArray, true, false, true, animationStateIdxProperty.intValue > 0 || propsArray.arraySize > 1);
            animationSubStatesList.drawElementCallback = OnDrawListItems;
            animationSubStatesList.drawElementBackgroundCallback = OnDrawBackground;
            animationSubStatesList.onAddCallback = OnAddToList;
            animationSubStatesList.onRemoveCallback = OnRemoveFromList;

            if (animationSubStateIdxProperty.intValue >= 0 && animationSubStateIdxProperty.intValue < animationSubStatesList.count)
                animationSubStatesList.index = animationSubStateIdxProperty.intValue;
        }

        EditorGUILayout.Space(0.5f);
        var playbackTypeProp = proceduralAnimationStateProperty.FindPropertyRelative($"{nameof(ProceduralAnimationState.playbackType)}");
        var loopIdxProp = proceduralAnimationStateProperty.FindPropertyRelative($"{nameof(ProceduralAnimationState.loopStartIdx)}");
        var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        rect.y += 2.5f;

        var (firstLabelWidth, secondLabelWidth, firstLabelName, secondLabelName) = rect.width < 220
            ? (54f, 30f, "Playback", "Start")
            : (55f, 51.5f, "Playback", "Start Idx");

        var firstSection = (firstLabelWidth, 45f, 80f);
        var secondSection = (secondLabelWidth, 43f, 76f);
        var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Center, rect, 2f, 8f, 1f, firstSection, secondSection);

        EditorGUIUtility.labelWidth = firstLabelWidth;
        EditorGUI.PropertyField(rectArr[0], playbackTypeProp, new GUIContent(firstLabelName));

        GUI.enabled = playbackTypeProp.enumValueIndex > 0;

        secondSection = (secondLabelWidth, 20f, 30f);
        var playButtonSection = (0, 20f, 40f);

        rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Center, rectArr[1], 3f, 6f, 0f, secondSection, playButtonSection);

        EditorGUIUtility.labelWidth = secondLabelWidth;
        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(rectArr[0], loopIdxProp, new GUIContent(secondLabelName));
        var proceduralAnimationState = quadData.proceduralAnimationStates[animationStateIdxProperty.intValue];
        if (EditorGUI.EndChangeCheck())
            loopIdxProp.intValue = Mathf.Clamp(loopIdxProp.intValue, 0, proceduralAnimationState.proceduralProperties.Count - 1);

        EditorGUIUtility.labelWidth = originalLabelWidth;

        if (previewingAnimation)
        {
            var unadjustedTime = (float)(EditorApplication.timeSinceStartup - playStartTime);
            playTime = proceduralAnimationState.GetAdjustedTime(unadjustedTime, out _);
        }

        var enablePlaybackButtons = !Application.isPlaying && proceduralAnimationState.proceduralProperties.Count > 0;
        for (int i = 0; i < proceduralAnimationState.proceduralProperties.Count; i++)
        {
            if (proceduralAnimationState.proceduralProperties[i].duration > 0f)
                break;

            if (i == proceduralAnimationState.proceduralProperties.Count - 1)
                enablePlaybackButtons = false;
        }

        var playButtonStyle = new GUIStyle(GUI.skin.FindStyle($"{GUI.skin.button.name}right"));
        if (previewingAnimation)
        {
            GUI.color = Color.cyan;
            if (EditorGUIUtility.isProSkin)
                playButtonStyle.normal.textColor = playButtonStyle.hover.textColor = playButtonStyle.active.textColor = Color.cyan;
        }

        if (flexibleImage != null)
        {
            GUI.enabled = enablePlaybackButtons;
            if (GUI.Button(rectArr[1], new GUIContent("▶"), playButtonStyle))
            {
                if (previewingAnimation)
                {
                    previewingAnimation = false;
                    playTime = 0f;
                }
                else
                {
                    previewingAnimation = true;
                    playStartTime = EditorApplication.timeSinceStartup;
                }
            }
        }

        GUI.color = originalGUIColor;
        GUI.enabled = true;

        EditorGUILayout.Space(1);
        rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        rect.y += 2.5f;

        (firstLabelWidth, secondLabelWidth) = (78f, 30f);
        firstSection = (firstLabelWidth, 20f, 30f);
        secondSection = (secondLabelWidth, 25f, 35f);
        rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Center, rect, 2f, 4f, 1f, firstSection, secondSection);

        var unwindIdxProp = proceduralAnimationStateProperty.FindPropertyRelative($"{nameof(ProceduralAnimationState.unwindToIdx)}");
        EditorGUIUtility.labelWidth = firstLabelWidth;
        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(rectArr[0], unwindIdxProp, UnwindIndexContent);
        if (EditorGUI.EndChangeCheck())
            unwindIdxProp.intValue = Mathf.Clamp(unwindIdxProp.intValue, -1, proceduralAnimationState.proceduralProperties.Count - 1);

        GUI.enabled = unwindIdxProp.intValue >= 0;
        var unwindRateProp = proceduralAnimationStateProperty.FindPropertyRelative($"{nameof(ProceduralAnimationState.unwindRate)}");
        EditorGUIUtility.labelWidth = secondLabelWidth;
        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(rectArr[1], unwindRateProp, UnwindRateContent);
        if (EditorGUI.EndChangeCheck())
            unwindRateProp.floatValue = Mathf.Max(unwindRateProp.floatValue, 0.1f);

        GUI.enabled = true;
        EditorGUILayout.Space(0.5f);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(-3f);

        animationSubStatesList.DoLayoutList();
        EditorGUIUtility.labelWidth = originalLabelWidth;

        while (animationSubStateIdxProperty.intValue >= propsArray.arraySize)
            animationSubStateIdxProperty.intValue--;

        while (animationSubStatesList.index >= propsArray.arraySize)
            animationSubStatesList.index--;

        if (animationSubStatesList.selectedIndices.Count == 0 || animationSubStatesList.index < 0)
        {
            if (animationSubStateIdxProperty.intValue >= 0 && animationSubStateIdxProperty.intValue < propsArray.arraySize)
            {
                animationSubStatesList.Select(animationSubStateIdxProperty.intValue);
            }
            else if (propsArray.arraySize > 0)
            {
                animationSubStatesList.Select(0);
                animationSubStateIdxProperty.intValue = 0;
            }
        }

        void OnDrawBackground(Rect backgroundRect, int index, bool isActive, bool isFocused)
        {
            if (isActive)
                EditorGUI.DrawRect(backgroundRect, EditorGUIUtility.isProSkin ? new Color(0.3f, 0.45f, 0.45f, 1f) : new Color(0.7f, 0.7f, 0.75f, 1f));

            if (!previewingAnimation)
                return;

            var timeLeft = playTime;
            var progress = 0f;

            var props = quadData.proceduralAnimationStates[animationStateIdxProperty.intValue].proceduralProperties;
            for (int i = 0; i < props.Count; i++)
            {
                var duration = props[i].duration;

                if (i == index)
                {
                    progress = Mathf.Min(timeLeft / duration, 1f);
                    break;
                }

                if (duration <= timeLeft)
                    timeLeft -= duration;
                else
                    break;
            }

            var animationStatusRect = new Rect(backgroundRect.position, new Vector2(backgroundRect.width, backgroundRect.height * progress));
            EditorGUI.DrawRect(animationStatusRect, new Color(1f, 1f, 1f, 0.1f));
        }

        void OnDrawListItems(Rect rect, int index, bool isActive, bool isFocused)
        {
            rect.y += 2.5f;
            rect.height -= 5f;

            var (firstLabelWidth, secondLabelWidth, firstLabelName, secondLabelName) = rect.width < 300f
                ? (32.5f, 32.5f, "Time", "Type")
                : (55f, 78f, "Duration", "Interpolation");

            var firstElement = (firstLabelWidth, 35f, 50f);
            var secondElement = (secondLabelWidth, 60f, 150f);
            var rects = EditorHelpers.DivideRect(EditorHelpers.Alignment.Center, rect, 4, 20, 2, firstElement, secondElement);
            var element = animationSubStatesList.serializedProperty.GetArrayElementAtIndex(index);
            EditorGUIUtility.labelWidth = firstLabelWidth;
            var durationProperty = element.FindPropertyRelative(nameof(ProceduralProperties.duration));
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rects[0], durationProperty, new GUIContent(firstLabelName));
            if (EditorGUI.EndChangeCheck())
                durationProperty.floatValue = Mathf.Max(0f, durationProperty.floatValue);

            EditorGUIUtility.labelWidth = secondLabelWidth;
            EditorGUI.PropertyField(rects[1], element.FindPropertyRelative(nameof(ProceduralProperties.interpolationType)), new GUIContent(secondLabelName));
            EditorGUIUtility.labelWidth = originalLabelWidth;

            if (isActive)
            {
                // We never want the focused element and the active element to differ. Can't even think of a case where you'd want a distinction.
                animationSubStatesList.index = animationSubStateIdxProperty.intValue = index;
            }

            if (Event.current.type != EventType.ContextClick || !rect.Contains(Event.current.mousePosition))
                return;

            var menu = new GenericMenu();
            menu.AddItem(CopyContent, false, () =>
            {
                var selectedProps = element.boxedValue as ProceduralProperties;
                proceduralPropertiesClipBoard ??= new ProceduralProperties();
                proceduralPropertiesClipBoard.Copy(selectedProps);
            });
            if (proceduralPropertiesClipBoard != null)
            {
                menu.AddItem(PasteContent, false, () =>
                {
                    var newProps = new ProceduralProperties(proceduralPropertiesClipBoard);
                    MatchModulesToQuad(newProps);
                    element.boxedValue = newProps;
                    so.ApplyModifiedProperties();
                    so.Update();
                    repaint = true;
                });
            }
            else
            {
                menu.AddDisabledItem(PasteContent);
            }

            if (animationStateIdxProperty.intValue != 0 && animationSubStateIdxProperty.intValue != 0)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Reset"), false, () =>
                {
                    var newProps = new ProceduralProperties(propsArray.GetArrayElementAtIndex(0).boxedValue as ProceduralProperties);
                    MatchModulesToQuad(newProps);
                    element.boxedValue = newProps;
                    so.ApplyModifiedProperties();
                    so.Update();
                    repaint = true;
                });
            }

            menu.ShowAsContext();
            Event.current.Use();
        }

        void OnAddToList(ReorderableList list)
        {
            var newItem = new ProceduralProperties();
            if (propsArray.arraySize == 0)
                newItem.Copy((ProceduralProperties)baseProperty.FindPropertyRelative($"{nameof(QuadData.proceduralAnimationStates)}.Array.data[{0}].{nameof(ProceduralAnimationState.proceduralProperties)}").GetArrayElementAtIndex(0).boxedValue);
            else
                newItem.Copy((ProceduralProperties)propsArray.GetArrayElementAtIndex(propsArray.arraySize - 1).boxedValue);

            propsArray.arraySize++;
            propsArray.GetArrayElementAtIndex(propsArray.arraySize - 1).boxedValue = newItem;
            list.displayRemove = true;

            so.ApplyModifiedProperties();
            so.Update();
        }

        void OnRemoveFromList(ReorderableList list)
        {
            propsArray.DeleteArrayElementAtIndex(animationSubStateIdxProperty.intValue);
            if (animationSubStateIdxProperty.intValue >= propsArray.arraySize)
                animationSubStateIdxProperty.intValue--;

            if (animationSubStateIdxProperty.intValue >= 0)
                animationSubStatesList.index = animationSubStateIdxProperty.intValue;

            if (animationStateIdxProperty.intValue == 0 && propsArray.arraySize == 1)
                list.displayRemove = false;

            if (propsArray.arraySize == (animationStateIdxProperty.intValue == 0 ? 1 : 0))
                currentProps = new ProceduralPropertiesEditor(quadData.proceduralAnimationStates, baseProperty, 0, 0);

            so.ApplyModifiedProperties();
            so.Update();
            if (animationStateIdxProperty.intValue == 0 && animationSubStateIdxProperty.intValue == 0)
                    defaultProps = new ProceduralPropertiesEditor(quadData.proceduralAnimationStates, baseProperty, 0, 0);
        }

        if (propsArray.arraySize == 0)
        {
            if (currentProps.AnimationStateIdx != 0 || currentProps.AnimationStateSubIdx != 0)
                currentProps = new ProceduralPropertiesEditor(quadData.proceduralAnimationStates, baseProperty, 0, 0);
        }
        else if (currentProps.AnimationStateIdx != animationStateIdxProperty.intValue || currentProps.AnimationStateSubIdx != animationSubStateIdxProperty.intValue)
        {
            currentProps = new ProceduralPropertiesEditor(quadData.proceduralAnimationStates, baseProperty, animationStateIdxProperty.intValue, animationSubStateIdxProperty.intValue);
            repaint = true;
        }

        if (flexibleImage == null)
        {
            prevAnimationStateIdx = animationStateIdxProperty.intValue;
            prevAnimationSubStateIdx = animationSubStateIdxProperty.intValue;
            return repaint;
        }

        if (!Application.isPlaying)
        {
            if (previewingAnimation)
            {
                var quadContainer = flexibleImage.ActiveQuadDataContainer;
                if (flexibleImage.GetAnimationValuesFromInspector == null)
                    flexibleImage.PopulateAnimationValuesFromInspector();

                var quadIdx = quadContainer.IndexOf(quadData);
                var animValues = flexibleImage.GetAnimationValuesFromInspector[quadIdx];

                var runningTime = 0f;
                var progress = 1f;
                var nextSubstate = 0;
                var animationStates = quadData.proceduralAnimationStates;
                var currenAnimationState = quadData.proceduralAnimationStates[animationStateIdxProperty.intValue];
    
                for (int i = 0; i < currenAnimationState.proceduralProperties.Count; i++)
                {
                    var prop = currenAnimationState.proceduralProperties[i];
                    var duration = prop.duration;
    
                    if (runningTime + duration < playTime - 1e-4)
                    {
                        runningTime += duration;
                        nextSubstate++;
                    }
                    else
                    {
                        progress = duration == 0 ? 1 : (playTime - runningTime) / duration;
                        break;
                    }
                }
    
                if (nextSubstate == 0)
                {
                    // State 0 starts from its first substate, matching runtime initialization.
                    // Other states' 0th substates will typically be preceded by the last idle substate.
                    // Except for the 0th pressed substate, which will typically be preceded by the last highlighted substate.
                    // There are exceptions to these rules, but for the preview it's good enough. More obscure cases can always be tested in play mode.
                    var stateTransitionedFrom = animationStateIdxProperty.intValue == 2 && animationStates[1].proceduralProperties.Count > 0 ? 1 : 0;
                    var substateTransitionedFrom = animationStateIdxProperty.intValue == 0 ? 0 : animationStates[stateTransitionedFrom].proceduralProperties.Count - 1;
                    quadData.PreviewInEditor(animValues, stateTransitionedFrom, substateTransitionedFrom, animationStateIdxProperty.intValue, nextSubstate, progress);
                }
                else
                {
                    quadData.PreviewInEditor(animValues, animationStateIdxProperty.intValue, nextSubstate - 1, animationStateIdxProperty.intValue, nextSubstate, progress);
                }

                flexibleImage.SetVerticesDirty();
                EditorUtility.SetDirty(so.targetObject);
                repaint = true;
            }
            else if (allFlexibleImages != null)
            {
                foreach (var fi in allFlexibleImages)
                    if (fi.DisplayAnimationStateFromInspector(animationStateIdxProperty.intValue, animationSubStateIdxProperty.intValue))
                        EditorUtility.SetDirty(fi);
            }
        }

        prevAnimationStateIdx = animationStateIdxProperty.intValue;
        prevAnimationSubStateIdx = animationSubStateIdxProperty.intValue;
        so.ApplyModifiedProperties();
        return repaint;
    }

    public (bool verticesDirty, bool raycastAreaDirty, bool earlyReturn) DrawQuadDataEditor (SerializedObject serializedObject, float originalLabelWidth, float originalFieldWidth, FlexibleImage flexibleImage = null, bool useAdvancedRaycast = false, SerializedProperty spriteProperty = null, GUIStyle sectionStyleOverride = null)
    {
        var (verticesDirty, raycastAreaDirty, earlyReturn) = (false, false, false);
        var outlineFeatureEnabled = FlexibleImageFeatureManager.IsFeatureEnabled(FlexibleImageFeatureManager.OutlineFeatureID);
        var screenSpacePatternFeatureEnabled = FlexibleImageFeatureManager.IsFeatureEnabled(FlexibleImageFeatureManager.PatternScreenSpaceSubFeatureID);
        var screenSpaceProceduralGradientFeatureEnabled = FlexibleImageFeatureManager.IsFeatureEnabled(FlexibleImageFeatureManager.ProceduralGradientScreenSpaceSubFeatureID);
        var pointerRelativeProceduralGradientFeatureEnabled = FlexibleImageFeatureManager.IsFeatureEnabled(FlexibleImageFeatureManager.ProceduralGradientPointerAdjustPosSubFeatureID);

        serializedObject.Update();
        var outlineModuleVisible = outlineFeatureEnabled && AllTargetsHaveModule(serializedObject, data => data.HasOutline);
        var gradientModuleVisible = FlexibleImageFeatureManager.IsFeatureEnabled(FlexibleImageFeatureManager.ProceduralGradientFeatureID) && AllTargetsHaveModule(serializedObject, data => data.HasGradient);
        var patternModuleVisible = FlexibleImageFeatureManager.IsFeatureEnabled(FlexibleImageFeatureManager.PatternFeatureID) && AllTargetsHaveModule(serializedObject, data => data.HasPattern);
        var skewModuleVisible = FlexibleImageFeatureManager.IsFeatureEnabled(FlexibleImageFeatureManager.SkewFeatureID) && AllTargetsHaveModule(serializedObject, data => data.HasSkew);
        var strokeModuleVisible = FlexibleImageFeatureManager.IsFeatureEnabled(FlexibleImageFeatureManager.StrokeFeatureID) && AllTargetsHaveModule(serializedObject, data => data.HasStroke);
        var cutoutModuleVisible = FlexibleImageFeatureManager.IsFeatureEnabled(FlexibleImageFeatureManager.CutoutFeatureID) && AllTargetsHaveModule(serializedObject, data => data.HasCutout);

        EditorGUIUtility.labelWidth = originalLabelWidth;
        EditorGUIUtility.fieldWidth = originalFieldWidth;

        var originalGUIColor = GUI.color;
        var originalBackgroundColor = GUI.backgroundColor;
        var isDefaultProps = currentProps.BaseSerializedProperty.propertyPath == defaultProps.BaseSerializedProperty.propertyPath;

        GUI.backgroundColor = Color.cyan;
        EditorGUIUtility.labelWidth = EditorGUIUtility.currentViewWidth;
        var sectionRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        var colorSectionRects = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, sectionRect, 2f, 2f, 0f, (0, 0, 9999), (58, 0, 0));
        SessionState.SetBool(ShowColorSectionKey, EditorGUI.Foldout(colorSectionRects[0], SessionState.GetBool(ShowColorSectionKey, false), "Color", sectionStyleOverride ?? SectionStyle));
        if (GUI.Button(colorSectionRects[0], "", GUIStyle.none))
            SessionState.SetBool(ShowColorSectionKey, !SessionState.GetBool(ShowColorSectionKey, false));
        (ColorModules[1], ColorModules[2]) = FlexibleImageFeatureManager.GetSectionOrder() == ProceduralGradientPatternOrder.ProceduralGradientBeforePattern ? (Module.Gradient, Module.Pattern) : (Module.Pattern, Module.Gradient);
        DrawModuleSelector(colorSectionRects[1], serializedObject, ColorModules);

        EditorGUIUtility.labelWidth = originalLabelWidth;

        GUI.backgroundColor = originalBackgroundColor;
        if (SessionState.GetBool(ShowColorSectionKey, false))
        {
            EditorGUILayout.Space(4);

            var screenSpacePatternHazard = !screenSpacePatternFeatureEnabled && (screenSpacePatternProperty?.boolValue ?? false);
            var screenSpaceProceduralGradientHazard = !screenSpaceProceduralGradientFeatureEnabled && (screenSpaceProceduralGradientProperty?.boolValue ?? false);
            var pointerRelativeProceduralGradientHazard = !pointerRelativeProceduralGradientFeatureEnabled && (proceduralGradientPositionFromPointerProperty?.boolValue ?? false);
            var outlineWidthHazard = !outlineFeatureEnabled && (currentProps.outlineWidthProperty?.floatValue ?? 0f) > 0f;
            var outlineAccomodateSkewHazard = !outlineFeatureEnabled && (outlineExpandsOutwardsProperty?.boolValue ?? false) && (outlineAccommodatesCollapsedEdgeProperty?.boolValue ?? false);

            if (screenSpacePatternHazard || screenSpaceProceduralGradientHazard || pointerRelativeProceduralGradientHazard || outlineWidthHazard || outlineAccomodateSkewHazard)
            {
                EditorGUILayout.HelpBox("Contains hidden settings from one or more disabled subfeatures which may result in undefined behaviour", MessageType.Warning);
                if (GUILayout.Button("Fix Now"))
                {
                    if (screenSpacePatternHazard)
                        screenSpacePatternProperty.boolValue = false;
                    if (screenSpaceProceduralGradientHazard)
                        screenSpaceProceduralGradientProperty.boolValue = false;
                    if (pointerRelativeProceduralGradientHazard)
                        proceduralGradientPositionFromPointerProperty.boolValue = false;
                    if (outlineWidthHazard)
                        currentProps.outlineWidthProperty.floatValue = 0f;
                    if (outlineAccomodateSkewHazard)
                        outlineAccommodatesCollapsedEdgeProperty.boolValue = false;
                }
            }

            if (quadData.UsingVertexColor)
            {
                GUI.enabled = isDefaultProps;
                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                EditorGUIUtility.fieldWidth = 20f;
                EditorGUILayout.PropertyField(meshSubdivisionsProperty, MeshSubdivisionsContent);
                if (EditorGUI.EndChangeCheck())
                {
                    meshSubdivisionsProperty.intValue = Mathf.Clamp(meshSubdivisionsProperty.intValue, 0, FlexibleImage.MaxMeshSubdivisions);
                }
                GUILayout.FlexibleSpace();
                EditorGUIUtility.labelWidth = 57.5f;
                EditorGUIUtility.fieldWidth = 65f;
                EditorGUILayout.PropertyField(meshTopologyProperty, MeshTopologyContent);
                EditorGUIUtility.labelWidth = originalLabelWidth;
                EditorGUIUtility.fieldWidth = originalFieldWidth;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                if (meshSubdivisionsProperty.intValue > 3)
                    EditorGUILayout.HelpBox("High subdivisions intended primarily for large, static elements that are not regularly dirtied.", MessageType.Warning);

                GUI.enabled = true;
            }

            GUI.enabled = isDefaultProps;
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.BeginHorizontal();
            var showColorPreset = false;
            if (prevColorPreset)
            {
                var foldoutRect = GUILayoutUtility.GetRect(-3, EditorGUIUtility.singleLineHeight);
                var propertyRect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth - 39, EditorGUIUtility.singleLineHeight);
                SessionState.SetBool(ShowColorPresetKey, EditorGUI.Foldout(foldoutRect, SessionState.GetBool(ShowColorPresetKey, false), string.Empty));
                EditorGUIUtility.fieldWidth = originalFieldWidth;
                showColorPreset = SessionState.GetBool(ShowColorPresetKey, false);
                EditorGUIUtility.labelWidth += 3;
                EditorGUI.PropertyField(propertyRect, colorPresetProperty);
                EditorGUIUtility.labelWidth -= 3;
            }
            else
            {
                 var propertyRect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth - 40, EditorGUIUtility.singleLineHeight);
                 var buttonRect = propertyRect;
                 buttonRect.width = 50;
                 buttonRect.x = originalLabelWidth + 20;
                 EditorGUIUtility.labelWidth += 52;
                 EditorGUI.PropertyField(propertyRect, colorPresetProperty);
                 EditorGUIUtility.labelWidth = originalLabelWidth;
                 if (GUI.Button(buttonRect, "New"))
                 {
                     _colorEditor = null;
                     showColorPreset = true;
                     SessionState.SetBool(ShowColorPresetKey, showColorPreset);
                     var preset = ScriptableObject.CreateInstance<ColorPreset>();
                     colorPresetProperty.objectReferenceValue = preset;
                     verticesDirty |= serializedObject.hasModifiedProperties;
                     serializedObject.ApplyModifiedProperties();
                     serializedObject.Update();
                     var path = PresetSavePath.GetPresetSavePath("New Color Preset.asset");
                     AssetDatabase.CreateAsset(preset, path);
                     AssetDatabase.SaveAssets();
                     earlyReturn = true;
                     return (verticesDirty, raycastAreaDirty, earlyReturn);
                 }
            }

            EditorGUILayout.EndHorizontal();

            var newColorPreset = colorPresetProperty.objectReferenceValue as ColorPreset;
            if (newColorPreset != prevColorPreset)
            {
                _colorEditor = null;
                if (prevColorPreset)
                    prevColorPreset.ColorChangeEvent -= quadData.SetVerticesDirty;
            
                if (newColorPreset)
                    newColorPreset.ColorChangeEvent += quadData.SetVerticesDirty;
            
                prevColorPreset = newColorPreset;
            }
            
            var hasColorPreset = newColorPreset != null;
            if (showColorPreset && hasColorPreset)
            {
               EditorGUILayout.Space(6);
               EditorGUI.indentLevel++;
               verticesDirty |= serializedObject.hasModifiedProperties;
               serializedObject.ApplyModifiedProperties();
               serializedObject.Update();
               ColorEditor.DrawGUI();
               EditorGUI.indentLevel--;
               EditorGUILayout.Space(8);
            }
            EditorGUILayout.EndVertical();
            GUI.enabled = true;

            EditorGUILayout.BeginVertical(GUI.skin.box);
            var presetMixLabelWidth = 66f;
            var showPrimaryColorSection = SessionState.GetBool(ShowPrimaryColorKey, true);
            var labelSection = (EditorGUIUtility.labelWidth, 0f, 0f);
            var controlSection = (0f, 45f, 9999f);
            var lastSection = hasColorPreset && !showPrimaryColorSection ? (presetMixLabelWidth, 25f, 35f) : (0f, 0f, 0f);

            var foldoutRectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, EditorHelpers.FlexibleSpaceAllocation.SmallestFlexibleAreaFirst, EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight), 2f, 5f, 0f, labelSection, controlSection, lastSection);
            ColorContextMenu(serializedObject, foldoutRectArr[0], currentProps.primaryColorsProperty, primaryColorDimensionsProperty);

            SessionState.SetBool(ShowPrimaryColorKey, EditorGUI.Foldout(foldoutRectArr[0], SessionState.GetBool(ShowPrimaryColorKey, true), "Primary Color"));
            if (showPrimaryColorSection)
            {
                EditorGUIUtility.labelWidth = 35;
                PropertyFieldWithDefault(foldoutRectArr[1], defaultProps.primaryColorFadeProperty, currentProps.primaryColorFadeProperty, PrimaryColorFadeContent, shrinkField: true);
                EditorGUIUtility.labelWidth = originalLabelWidth;

                if (hasColorPreset)
                {
                    GUI.enabled = isDefaultProps;
                    EditorGUILayout.PropertyField(primaryColorPresetMixProperty, PresetMixContent);
                    GUI.enabled = true;
                }

                EditorGUI.indentLevel++;
                DrawColorGrid(serializedObject, primaryColorDimensionsProperty, primaryColorWrapModeXProperty, primaryColorWrapModeYProperty, defaultProps.primaryColorsProperty, currentProps.primaryColorsProperty, defaultProps.primaryColorOffsetProperty, currentProps.primaryColorOffsetProperty, defaultProps.primaryColorRotationProperty, currentProps.primaryColorRotationProperty, defaultProps.primaryColorScaleProperty, currentProps.primaryColorScaleProperty, isDefaultProps, ShowPrimaryGridAdvancedKey);
                EditorGUI.indentLevel--;
            }
            else
            {
                foldoutRectArr[1].x -= 2;
                PropertyFieldWithDefault(foldoutRectArr[1], defaultProps.primaryColorsProperty.GetArrayElementAtIndex(0), currentProps.primaryColorsProperty.GetArrayElementAtIndex(0), GUIContent.none, shrinkField: true);
                if (hasColorPreset)
                {
                    GUI.enabled = isDefaultProps;
                    EditorGUIUtility.labelWidth = presetMixLabelWidth;
                    EditorGUI.PropertyField(foldoutRectArr[2], primaryColorPresetMixProperty, PresetMixContent);
                    EditorGUIUtility.labelWidth = originalLabelWidth;
                    GUI.enabled = true;
                }
            }
            EditorGUILayout.EndVertical();

            if (outlineModuleVisible || gradientModuleVisible || patternModuleVisible)
            {
                var separator = EditorGUILayout.GetControlRect(false, 5f);
                EditorGUI.DrawRect(new Rect(separator.x + 4f, separator.y + 2f, separator.width - 8f, 1f), new Color(0f, 0f, 0f, 0.3f));
            }

            var alphaIsBlendLabelWidth = 87.5f;
            if (outlineModuleVisible)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                var showOutlineSection = SessionState.GetBool(ShowOutlineKey, true);
                controlSection = showOutlineSection ? (0, 0f, 9999f) : (0f, 45f, 9999f);
                lastSection = (hasColorPreset, showOutlineSection) switch
                {
                    (true, false) => (presetMixLabelWidth, 25f, 35f),
                    (_, true) => (alphaIsBlendLabelWidth, 18f, 18f),
                    _ => (0f, 0f, 0f)
                };

                foldoutRectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, EditorHelpers.FlexibleSpaceAllocation.SmallestFlexibleAreaFirst, EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight), 2f, 2f, 0f, labelSection, controlSection, lastSection);
                ModuleContextMenu(foldoutRectArr[0], serializedObject, Module.Outline, baseProperty.FindPropertyRelative(QuadData.OutlineConfigFieldName), currentProps.outlineColorsProperty);

                SessionState.SetBool(ShowOutlineKey, EditorGUI.Foldout(foldoutRectArr[0], SessionState.GetBool(ShowOutlineKey, true), "Outline"));
                if (showOutlineSection)
                {
                    GUI.enabled = isDefaultProps;
                    EditorGUIUtility.labelWidth = alphaIsBlendLabelWidth;
                    EditorGUI.PropertyField(foldoutRectArr[2], outlineAlphaIsBlendProperty, OutlineAlphaIsBlendContent);
                    EditorGUIUtility.labelWidth = originalLabelWidth;
                    GUI.enabled = true;

                    if (hasColorPreset)
                    {
                        GUI.enabled = isDefaultProps;
                        EditorGUILayout.PropertyField(outlineColorPresetMixProperty, PresetMixContent);
                        GUI.enabled = true;
                    }

                    EditorGUI.indentLevel++;
                    DrawColorGrid(serializedObject, outlineColorDimensionsProperty, outlineColorWrapModeXProperty, outlineColorWrapModeYProperty, defaultProps.outlineColorsProperty, currentProps.outlineColorsProperty, defaultProps.outlineColorOffsetProperty, currentProps.outlineColorOffsetProperty, defaultProps.outlineColorRotationProperty, currentProps.outlineColorRotationProperty, defaultProps.outlineColorScaleProperty, currentProps.outlineColorScaleProperty, isDefaultProps, ShowOutlineGridAdvancedKey);
                    EditorGUI.BeginChangeCheck();
                    DrawDetentedFloatFieldWithDefault(defaultProps.outlineWidthProperty, currentProps.outlineWidthProperty, OutlineWidthContent, 4096, 0f, 511.875f, shrinkField: true);
                    if (EditorGUI.EndChangeCheck() && useAdvancedRaycast && outlineExpandsOutwardsProperty.boolValue)
                        raycastAreaDirty = true;

                    GUI.enabled = isDefaultProps;
                    EditorGUILayout.PropertyField(addInteriorOutlineProperty, AddInteriorOutlineContent);
                    EditorGUILayout.PropertyField(outlineExpandsOutwardsProperty, OutlineExpandsOutwardsContent);
                    if (outlineExpandsOutwardsProperty.boolValue)
                    {
                        EditorGUILayout.PropertyField(outlineAccommodatesCollapsedEdgeProperty, OutlineAccommodatesCollapsedEdgeContent);
                        EditorGUILayout.Space(2);
                    }

                    EditorGUILayout.PropertyField(outlineFadesTowardsPerimeterProperty, OutlineFadeTowardsPerimeterContent);
                    EditorGUILayout.PropertyField(outlineAdjustsChamferProperty, OutlineAdjustsChamferContent);
                    GUI.enabled = true;
                    EditorGUILayout.Space(4);
                    EditorGUI.indentLevel--;
                }
                else
                {
                    PropertyFieldWithDefault(foldoutRectArr[1], defaultProps.outlineColorsProperty.GetArrayElementAtIndex(0), currentProps.outlineColorsProperty.GetArrayElementAtIndex(0), GUIContent.none, shrinkField: true);
                    if (hasColorPreset)
                    {
                        GUI.enabled = isDefaultProps;
                        EditorGUIUtility.labelWidth = presetMixLabelWidth;
                        EditorGUI.PropertyField(foldoutRectArr[2], outlineColorPresetMixProperty, PresetMixContent);
                        EditorGUIUtility.labelWidth = originalLabelWidth;
                        GUI.enabled = true;
                    }
                }
                EditorGUILayout.EndVertical();
            }

            if (FlexibleImageFeatureManager.GetSectionOrder() == ProceduralGradientPatternOrder.ProceduralGradientBeforePattern)
            {
                DrawProceduralGradientSubSection(hasColorPreset, outlineFeatureEnabled, alphaIsBlendLabelWidth, presetMixLabelWidth, labelSection);
                DrawPatternSubSection(hasColorPreset, outlineFeatureEnabled, alphaIsBlendLabelWidth, presetMixLabelWidth, labelSection);
            }
            else
            {
                DrawPatternSubSection(hasColorPreset, outlineFeatureEnabled, alphaIsBlendLabelWidth, presetMixLabelWidth, labelSection);
                DrawProceduralGradientSubSection(hasColorPreset, outlineFeatureEnabled, alphaIsBlendLabelWidth, presetMixLabelWidth, labelSection);
            }
        }

        EditorGUILayout.Space(1.5f);

        GUI.backgroundColor = Color.cyan;
        EditorGUIUtility.labelWidth = EditorGUIUtility.currentViewWidth;

        sectionRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
        var sectionRectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, sectionRect, 2f, 2f, 0f, (0, 0, 9999), (48, 0, 0), (58, 0, 0));
        SessionState.SetBool(ShowShapeSectionKey, EditorGUI.Foldout(sectionRectArr[0], SessionState.GetBool(ShowShapeSectionKey, false), "Shape", sectionStyleOverride ?? SectionStyle));
        if (GUI.Button(sectionRectArr[0], "", GUIStyle.none))
            SessionState.SetBool(ShowShapeSectionKey, !SessionState.GetBool(ShowShapeSectionKey, false));
        DrawModuleSelector(sectionRectArr[2], serializedObject, ShapeModules);

        var shapeSectionExpanded = SessionState.GetBool(ShowShapeSectionKey, false);
        var contentColor = GUI.contentColor;
        GUI.contentColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;
        if (flexibleImage != null && GUI.Button(sectionRectArr[1], ShapeQuickActionsContent))
            ShapeQuickActionsMenu(serializedObject, flexibleImage);
        GUI.contentColor = contentColor;

        EditorGUIUtility.labelWidth = originalLabelWidth;
        GUI.backgroundColor = originalBackgroundColor;

        if (shapeSectionExpanded)
        {
            EditorGUILayout.Space(4);

            float collapsePercent;
            if (flexibleImage != null && (edgeCollapseAmountIsAbsoluteProperty?.boolValue ?? false))
            {
                var size = quadData.GetSizeModifier(flexibleImage.rectTransform);
                size += flexibleImage.rectTransform.rect.size;
                var dimensionalSize = (collapsedEdgeProperty?.enumValueIndex ?? 0) <= 1 ? size.x : size.y;
                collapsePercent = Mathf.Min(1f, currentProps.collapseEdgeAmountAbsoluteProperty.floatValue / Mathf.Max(dimensionalSize, 0.01f));
            }
            else
            {
                collapsePercent = currentProps.collapseEdgeAmountProperty?.floatValue ?? 0f;
            }

            var threeSided = !(collapseIntoParallelogramProperty?.boolValue ?? false) && !(mirrorCollapseProperty?.boolValue ?? false) && Mathf.Approximately(collapsePercent, 1);
            
            EditorGUILayout.BeginVertical(GUI.skin.box);
            SessionState.SetBool(ShowCornersKey, EditorGUILayout.Foldout(SessionState.GetBool(ShowCornersKey, true), GUIContent.none));
            var cornersHeaderRect = GUILayoutUtility.GetLastRect();
            CornersContextMenu(cornersHeaderRect, serializedObject);
            if (SessionState.GetBool(ShowCornersKey, false))
            {
                EditorGUI.LabelField(cornersHeaderRect, CornersContent);

                EditorGUI.indentLevel++;
                GUI.enabled = isDefaultProps;
                if (mirrorCollapseProperty?.boolValue ?? false)
                {
                    var controlRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                    var labelRect = controlRect;
                    labelRect.width = originalLabelWidth;
                    EditorGUI.LabelField(labelRect, NormalizeCornersContent);
                    var labelOffset = originalLabelWidth - 15 * EditorGUI.indentLevel;
                    controlRect.x += labelOffset;
                    controlRect.width -= labelOffset;
                    EditorGUI.LabelField(controlRect, controlRect.width > 182.5f ? NormalizeChamferUnavailableContent : NormalizeChamferUnavailableContentShort, UnavailableFeatureStyle);
                }
                else
                {
                    GUI.enabled = isDefaultProps;
                    EditorGUILayout.PropertyField(normalizeChamferProperty, NormalizeCornersContent);
                    GUI.enabled = true;
                }

                GUI.enabled = true;

                EditorGUILayout.Space(0.5f);

                DrawCornersSection(CornerChamferContent, 5f, 65536, 0f, 4095.9375f, 0.5f, currentProps.cornerChamferProperty, defaultProps.cornerXChamferProperty, currentProps.cornerXChamferProperty, defaultProps.cornerYChamferProperty, currentProps.cornerYChamferProperty,
                    defaultProps.cornerZChamferProperty, currentProps.cornerZChamferProperty, defaultProps.cornerWChamferProperty, currentProps.cornerWChamferProperty, defaultProps.collapsedCornerChamferProperty, currentProps.collapsedCornerChamferProperty, threeSided, originalLabelWidth);

                EditorGUILayout.Space(2);

                EditorGUI.BeginChangeCheck();
                GUI.enabled = isDefaultProps;
                EditorGUILayout.PropertyField(concavityIsSmoothingProperty, IsSquircleContent);
                GUI.enabled = true;
                if (EditorGUI.EndChangeCheck())
                    currentProps.cornerConcavityProperty.vector4Value = Vector4.Min(currentProps.cornerConcavityProperty.vector4Value, Vector4.one);

                EditorGUILayout.Space(0.5f);
                DrawCornersSection(concavityIsSmoothingProperty.boolValue ? CornerSmoothingContent : CornerConcavityContent, 0.5f, 256, 0f, concavityIsSmoothingProperty.boolValue ? 1f : 1.9921875f, 0.01f, currentProps.cornerConcavityProperty, defaultProps.cornerXConcavityProperty, currentProps.cornerXConcavityProperty, defaultProps.cornerYConcavityProperty, currentProps.cornerYConcavityProperty,
                    defaultProps.cornerZConcavityProperty, currentProps.cornerZConcavityProperty, defaultProps.cornerWConcavityProperty, currentProps.cornerWConcavityProperty, defaultProps.collapsedCornerConcavityProperty, currentProps.collapsedCornerConcavityProperty, threeSided, originalLabelWidth);

                EditorGUILayout.Space(4);
                EditorGUI.indentLevel--;
            }
            else
            {
                var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, cornersHeaderRect, 8f, 9999f, 0f, (0, 0, 9999), (0, 0, 9999));
                EditorGUIUtility.labelWidth = Mathf.Max(cornersHeaderRect.width * 0.5f - 85, 64);

                var p = currentProps;
                var collapsed = (p.collapsedCornerChamferProperty, p.collapsedCornerConcavityProperty);
                var x = (p.cornerXChamferProperty, p.cornerXConcavityProperty);
                var y = (p.cornerYChamferProperty, p.cornerYConcavityProperty);
                var z = (p.cornerZChamferProperty, p.cornerZConcavityProperty);
                var w = (p.cornerWChamferProperty, p.cornerWConcavityProperty);
                List<(SerializedProperty chamfer, SerializedProperty concavity)> activeCorners = (threeSided, collapsedEdgeProperty?.intValue ?? 0) switch
                {
                    (false, _) => new(4) { x, y, z, w },
                    (true, 0) => new(3) { collapsed, z, w }, // Top
                    (true, 1) => new(3) { x, y, collapsed }, // Bottom
                    (true, 2) => new(3) { collapsed, y, w }, // Left
                    _ => new(3) { x, z, collapsed }, // Right
                };

                var uniformChamfer = activeCorners.All(c => Mathf.Approximately(c.chamfer.floatValue, activeCorners[0].chamfer.floatValue));
                var mixedChamfer = !uniformChamfer || threeSided && currentProps.collapsedCornerChamferProperty.hasMultipleDifferentValues || currentProps.cornerChamferProperty.hasMultipleDifferentValues;

                EditorGUI.showMixedValue = mixedChamfer;
                EditorGUI.BeginChangeCheck();
                (SerializedProperty defaultSingleProp, SerializedProperty currentSingleProp) = threeSided ? (defaultProps.collapsedCornerChamferProperty, defaultProps.collapsedCornerChamferProperty) : (defaultProps.cornerXChamferProperty, currentProps.cornerXChamferProperty);
                DrawDetentedFloatFieldWithDefault(rectArr[0], defaultSingleProp, currentSingleProp, CornerChamferContent, 65536, 0f, 4095.9375f, shrinkField: true, useFloatFieldMethods: true);
                if (EditorGUI.EndChangeCheck())
                {
                    currentProps.cornerChamferProperty.vector4Value = Vector4.one * currentSingleProp.floatValue;
                    if (currentProps.collapsedCornerChamferProperty != null)
                        currentProps.collapsedCornerChamferProperty.floatValue = currentSingleProp.floatValue;
                }

                activeCorners.ForEach(c => c.chamfer.floatValue = currentSingleProp.floatValue);

                var uniformConcavity = activeCorners.All(c => Mathf.Approximately(c.concavity.floatValue, activeCorners[0].concavity.floatValue));
                var mixedConcavity = !uniformConcavity || threeSided && currentProps.collapsedCornerConcavityProperty.hasMultipleDifferentValues || currentProps.cornerConcavityProperty.hasMultipleDifferentValues;

                EditorGUI.showMixedValue = mixedConcavity;
                EditorGUI.BeginChangeCheck();
                (defaultSingleProp, currentSingleProp) = threeSided ? (defaultProps.collapsedCornerConcavityProperty, defaultProps.collapsedCornerConcavityProperty) : (defaultProps.cornerXConcavityProperty, currentProps.cornerXConcavityProperty);
                DrawDetentedFloatFieldWithDefault(rectArr[1], defaultSingleProp, currentSingleProp, concavityIsSmoothingProperty.boolValue ? CornerSmoothingContent : CornerConcavityContent, 256, 0f, concavityIsSmoothingProperty.boolValue ? 1f : 1.9921875f, shrinkField: true, useFloatFieldMethods: true);
                if (EditorGUI.EndChangeCheck())
                {
                    currentProps.cornerConcavityProperty.vector4Value = Vector4.one * currentSingleProp.floatValue;
                    if (currentProps.collapsedCornerConcavityProperty != null)
                        currentProps.collapsedCornerConcavityProperty.floatValue = currentSingleProp.floatValue;
                }

                EditorGUI.showMixedValue = false;
                EditorGUIUtility.labelWidth = originalLabelWidth;
            }

            EditorGUILayout.EndVertical();

            using (var raycasterAreaChangeCheck = new EditorGUI.ChangeCheckScope())
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                GUI.enabled = isDefaultProps;
                EditorGUILayout.PropertyField(softnessFeatherModeProperty, FeatherModeContent);
                GUI.enabled = true;
                DrawDetentedFloatFieldWithDefault(defaultProps.softnessProperty, currentProps.softnessProperty, SoftnessContent, 4096, 0f, 255.9375f, shrinkField: true);
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical(GUI.skin.box);
                var controlRect = EditorGUILayout.GetControlRect(true, 2 + EditorGUIUtility.singleLineHeight * 2);
                var labelSection = (EditorGUIUtility.labelWidth - 15, 0f, 0f);
                var remainderSection = (0f, 0f, 9999f);
                var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, controlRect, 0f, 0f, 0f, labelSection, remainderSection);
                EditorGUI.BeginProperty(rectArr[0], OffsetContent, currentProps.offsetProperty);
                EditorGUI.LabelField(rectArr[0], OffsetContent);
                EditorGUI.EndProperty();

                rectArr[0].width -= 20f;
                var scrubAmt = EditorHelpers.Scrub(rectArr[0]);
                if (!Mathf.Approximately(scrubAmt, 0f))
                {
                    scrubAmt *= 0.25f;
                    currentProps.offsetXProperty.floatValue += scrubAmt;
                    currentProps.offsetYProperty.floatValue += scrubAmt;
                }

                EditorGUIUtility.labelWidth = 15;
                rectArr[1].height -= 2 + EditorGUIUtility.singleLineHeight;
                PropertyFieldWithDefault(rectArr[1], defaultProps.offsetXProperty, currentProps.offsetXProperty, XContent, shrinkField: true);
                rectArr[1].y += 2 + EditorGUIUtility.singleLineHeight;
                PropertyFieldWithDefault(rectArr[1], defaultProps.offsetYProperty, currentProps.offsetYProperty, YContent, shrinkField: true);
                EditorGUIUtility.labelWidth = originalLabelWidth;
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical(GUI.skin.box);
                controlRect = EditorGUILayout.GetControlRect(true, 2 + EditorGUIUtility.singleLineHeight * 2);
                labelSection = (EditorGUIUtility.labelWidth - 18, 0f, 0f);
                remainderSection = (0f, 0f, 9999f);
                rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, controlRect, 3f, 3f, 0f, labelSection, remainderSection);
                EditorGUI.BeginProperty(rectArr[0], SizeModifierContent, currentProps.sizeModifierProperty);
                EditorGUI.LabelField(rectArr[0], SizeModifierContent);
                EditorGUI.EndProperty();

                rectArr[0].width -= 20f;
                scrubAmt = EditorHelpers.Scrub(rectArr[0]);
                if (!Mathf.Approximately(scrubAmt, 0f))
                {
                    scrubAmt *= 0.25f;
                    currentProps.sizeModifierXProperty.floatValue += scrubAmt;
                    currentProps.sizeModifierYProperty.floatValue += scrubAmt;
                }

                var xSectionRect = rectArr[1];
                xSectionRect.height -= 2 + EditorGUIUtility.singleLineHeight;
                var ySectionRect = new Rect(xSectionRect);
                ySectionRect.y += 2 + EditorGUIUtility.singleLineHeight;
                EditorGUIUtility.labelWidth = 15;
                PropertyFieldWithDefault(xSectionRect, defaultProps.sizeModifierXProperty, currentProps.sizeModifierXProperty, XContent, shrinkField: true);
                PropertyFieldWithDefault(ySectionRect, defaultProps.sizeModifierYProperty, currentProps.sizeModifierYProperty, YContent, shrinkField: true);

                var rectTransform = flexibleImage?.rectTransform;
                if (rectTransform != null && sizeModifierAspectCorrectionProperty.boolValue && !Mathf.Approximately(rectTransform.rect.width, rectTransform.rect.height))
                {
                    Rect rectToUse;
                    SerializedProperty aspectCorrectedDimension;
                    float aspectCorrection;
                    bool notDefault;
                    if (rectTransform.rect.width > rectTransform.rect.height)
                    {
                        rectToUse = ySectionRect;
                        aspectCorrectedDimension = currentProps.sizeModifierYProperty;
                        aspectCorrection = rectTransform.rect.height / rectTransform.rect.width;
                        notDefault = !Mathf.Approximately(defaultProps.sizeModifierXProperty.floatValue, currentProps.sizeModifierXProperty.floatValue);
                    }
                    else
                    {
                        rectToUse = xSectionRect;
                        aspectCorrectedDimension = currentProps.sizeModifierXProperty;
                        aspectCorrection = rectTransform.rect.width / rectTransform.rect.height;
                        notDefault = !Mathf.Approximately(defaultProps.sizeModifierYProperty.floatValue, currentProps.sizeModifierYProperty.floatValue);
                    }

                    var correctedDimensionContent = new GUIContent($"({aspectCorrection * aspectCorrectedDimension.floatValue:0.##})");
                    var desiredSize = EditorStyles.label.CalcSize(correctedDimensionContent).x + 2.5f;
                    var extraSizeForResetButton = notDefault ? UndoButtonWidth : 0f;
                    if (desiredSize < (rectToUse.width - extraSizeForResetButton * 0.5f) * 0.425f)
                    {
                        var hintRect = new Rect(rectToUse);
                        hintRect.x += rectToUse.width - desiredSize - extraSizeForResetButton - 2.5f;
                        EditorGUIUtility.labelWidth = hintRect.width = desiredSize;
                        GUI.color = Color.gray;
                        EditorGUI.LabelField(hintRect, correctedDimensionContent);
                        GUI.color = originalGUIColor;
                    }
                }

                EditorGUIUtility.labelWidth = originalLabelWidth;
                GUI.enabled = isDefaultProps;
                EditorGUILayout.PropertyField(sizeModifierAspectCorrectionProperty, SizeModifierAspectCorrectionContent);
                GUI.enabled = true;
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUIUtility.labelWidth = originalLabelWidth;
                PropertyFieldWithDefault(defaultProps.rotationProperty, currentProps.rotationProperty, RotationContent, shrinkField: true);
                GUI.enabled = isDefaultProps;
                EditorGUILayout.PropertyField(fitRotationWithinBoundsProperty, FitOriginalBoundsContent);
                GUI.enabled = true;
                EditorGUILayout.EndVertical();

                if (raycasterAreaChangeCheck.changed)
                    raycastAreaDirty = true;
            }

            EditorGUILayout.BeginVertical(GUI.skin.box);
            var cRect = EditorGUILayout.GetControlRect(true, 2 + EditorGUIUtility.singleLineHeight * 2);
            var labelSection2 = (EditorGUIUtility.labelWidth - 15f, 0f, 0f);
            var componentSection = (0f, 50f, 9999f);
            var rectArray = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, cRect, 0f, 0f, 0f, labelSection2, componentSection, componentSection);

            EditorGUI.BeginProperty(rectArray[0], UVRectContent, currentProps.uvRectProperty);
            EditorGUI.LabelField(rectArray[0], UVRectContent);
            EditorGUI.EndProperty();
            EditorGUIUtility.labelWidth = 15;
            rectArray[1].height -= 2 + EditorGUIUtility.singleLineHeight;
            rectArray[1].width -= 2.5f;
            PropertyFieldWithDefault(rectArray[1], defaultProps.uvRectXProperty, currentProps.uvRectXProperty, XContent, shrinkField: true);
            rectArray[1].y += 2 + EditorGUIUtility.singleLineHeight;
            PropertyFieldWithDefault(rectArray[1], defaultProps.uvRectZProperty, currentProps.uvRectZProperty, WContent, shrinkField: true);
            if (!currentProps.uvRectZProperty.hasMultipleDifferentValues)
                currentProps.uvRectZProperty.floatValue = Mathf.Clamp(currentProps.uvRectZProperty.floatValue, 0f, 2);

            rectArray[2].height -= 2 + EditorGUIUtility.singleLineHeight;
            rectArray[2].width -= 2.5f;
            rectArray[2].x += 2.5f;
            PropertyFieldWithDefault(rectArray[2], defaultProps.uvRectYProperty, currentProps.uvRectYProperty, YContent, shrinkField: true);
            rectArray[2].y += 2 + EditorGUIUtility.singleLineHeight;
            PropertyFieldWithDefault(rectArray[2], defaultProps.uvRectWProperty, currentProps.uvRectWProperty, HContent, shrinkField: true);
            if (!currentProps.uvRectWProperty.hasMultipleDifferentValues)
                currentProps.uvRectWProperty.floatValue = Mathf.Clamp(currentProps.uvRectWProperty.floatValue, 0f, 2);

            EditorGUIUtility.labelWidth = originalLabelWidth;
            EditorGUILayout.EndVertical();

            if (skewModuleVisible || strokeModuleVisible || cutoutModuleVisible)
            {
                var separator = EditorGUILayout.GetControlRect(false, 5f);
                EditorGUI.DrawRect(new Rect(separator.x + 4f, separator.y + 2f, separator.width - 8f, 1f), new Color(0f, 0f, 0f, 0.3f));
            }

            if (skewModuleVisible)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                SessionState.SetBool(ShowSkewKey, EditorGUILayout.Foldout(SessionState.GetBool(ShowSkewKey, true), SkewContent));
                ModuleContextMenu(GUILayoutUtility.GetLastRect(), serializedObject, Module.Skew, baseProperty.FindPropertyRelative(QuadData.SkewConfigFieldName));
                if (SessionState.GetBool(ShowSkewKey, false))
                {
                    EditorGUI.indentLevel++;

                    GUI.enabled = isDefaultProps;
                    EditorGUILayout.PropertyField(collapsedEdgeProperty);
                    GUI.enabled = true;

                    var controlRect = EditorGUILayout.GetControlRect();
                    var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, EditorHelpers.FlexibleSpaceAllocation.SmallestFlexibleAreaFirst, controlRect, 0f, 0f, 0f, (originalLabelWidth, 25, 999f), (80f, 0f, 10f));
                    rectArr[0].width += 10f;
                    if (edgeCollapseAmountIsAbsoluteProperty.boolValue)
                        PropertyFieldWithDefault(rectArr[0], defaultProps.collapseEdgeAmountAbsoluteProperty, currentProps.collapseEdgeAmountAbsoluteProperty, new GUIContent("Collapse Amount"), isFloat: true, shrinkField: true);
                    else
                        PropertyFieldWithDefault(rectArr[0], defaultProps.collapseEdgeAmountProperty, currentProps.collapseEdgeAmountProperty, new GUIContent("Collapse Amount"), isFloat: true, shrinkField: true);

                    GUI.enabled = isDefaultProps;
                    edgeCollapseAmountIsAbsoluteProperty.boolValue = EditorGUI.Popup(rectArr[1], Convert.ToInt32(edgeCollapseAmountIsAbsoluteProperty.boolValue), RelativeAbsoluteDropDownNames) > 0;
                    GUI.enabled = true;

                    PropertyFieldWithDefault(defaultProps.collapseEdgePositionProperty, currentProps.collapseEdgePositionProperty, new GUIContent("Collapse Pos"), isFloat: true, shrinkField: true);

                    GUI.enabled = isDefaultProps;
                    EditorGUILayout.PropertyField(collapseIntoParallelogramProperty, CollapseIntoParallelogramContent);
                    EditorGUILayout.PropertyField(mirrorCollapseProperty, MirrorCollapseContent);
                    GUI.enabled = true;

                    EditorGUILayout.Space(4);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
            }

            if (strokeModuleVisible)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                var strokeHeaderRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                ModuleContextMenu(strokeHeaderRect, serializedObject, Module.Stroke, baseProperty.FindPropertyRelative(QuadData.StrokeConfigFieldName));
                GUI.enabled = isDefaultProps;
                EditorGUI.PropertyField(strokeHeaderRect, strokeOriginProperty);
                GUI.enabled = true;

                EditorGUI.BeginChangeCheck();
                PropertyFieldWithDefault(defaultProps.strokeProperty, currentProps.strokeProperty, StrokeContent, shrinkField: true);
                if (EditorGUI.EndChangeCheck())
                    currentProps.strokeProperty.floatValue = Mathf.Max(currentProps.strokeProperty.floatValue, 0);

                EditorGUILayout.EndVertical();
            }

            if (cutoutModuleVisible)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                var showCutoutSection = SessionState.GetBool(ShowCutoutKey, true);
                var cutoutFoldoutSection = (EditorStyles.foldout.CalcSize(CutoutContent).x + 4f, 0f, 0f);
                var cutoutToggleLabelWidth = EditorStyles.label.CalcSize(CutoutPositionIgnoresExpandedOutlinesContent).x + 4f;
                var cutoutFoldoutRectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, EditorHelpers.FlexibleSpaceAllocation.SmallestFlexibleAreaFirst, EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight), 2f, 2f, 0f, cutoutFoldoutSection, (0, 0f, 9999f), (cutoutToggleLabelWidth, 18f, 18f));
                showCutoutSection = EditorGUI.Foldout(cutoutFoldoutRectArr[0], showCutoutSection, CutoutContent);
                SessionState.SetBool(ShowCutoutKey, showCutoutSection);
                ModuleContextMenu(cutoutFoldoutRectArr[0], serializedObject, Module.Cutout, baseProperty.FindPropertyRelative(QuadData.CutoutConfigFieldName));

                EditorGUI.BeginChangeCheck();
                GUI.enabled = isDefaultProps;
                EditorGUIUtility.labelWidth = cutoutToggleLabelWidth;
                EditorGUI.PropertyField(cutoutFoldoutRectArr[2], cutoutPositionIgnoresExpandedOutlinesProperty, CutoutPositionIgnoresExpandedOutlinesContent);
                EditorGUIUtility.labelWidth = originalLabelWidth;
                GUI.enabled = true;
                if (EditorGUI.EndChangeCheck())
                    raycastAreaDirty = true;

                if (showCutoutSection)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUI.enabled = isDefaultProps;
                    for (int i = 0; i < CutoutTypeNames.Length; i++)
                    {
                        var style = GUI.skin.button;

                        if (i == 0)
                            style = new GUIStyle(GUI.skin.FindStyle($"{style.name}left")) { margin = { left = EditorGUI.indentLevel * 15 } };
                        else if (i == CutoutTypeNames.Length - 1)
                            style = new GUIStyle(GUI.skin.FindStyle($"{style.name}right"));
                        else
                            style = new GUIStyle(GUI.skin.FindStyle($"{style.name}mid"));

                        if (i == cutoutTypeProperty.enumValueIndex)
                        {
                            GUI.color = Color.cyan;
                            if (EditorGUIUtility.isProSkin)
                                style.normal.textColor = style.hover.textColor = style.active.textColor = Color.cyan;
                        }

                        var featureEnabled = FlexibleImageFeatureManager.IsFeatureEnabled(CutoutTypeSubFeatureNames[i]);
                        GUI.enabled = isDefaultProps && featureEnabled;
                        var clicked = GUILayout.Button(CutoutTypeNames[i], style);
                        var contextClicked = clicked && Event.current.button == 1 || !isDefaultProps && featureEnabled && Event.current.type == EventType.MouseDown && Event.current.button == 1 && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition);
                        GUI.enabled = true;
                        GUI.color = originalGUIColor;
                        if (contextClicked)
                        {
                            var properties = i == (int)QuadData.CutoutType.Simple
                                ? new[] { simpleCutoutRuleProperty, cutoutOnlyAffectsOutlineProperty, invertCutoutProperty, simpleCutoutEdgeEnabledProperty, currentProps.simpleCutoutXProperty, currentProps.simpleCutoutYProperty, currentProps.simpleCutoutZProperty, currentProps.simpleCutoutWProperty }
                                : new[] { sdfCutoutBehaviourProperty, sdfCutoutMirrorProperty, sdfCutoutMirrorIsDiagonalProperty, invertCutoutProperty, sdfCutoutUsesAnchorsProperty, sdfCutoutAnchorMinProperty, sdfCutoutAnchorMaxProperty, sdfCutoutPivotProperty, sdfCutoutPositionIsAbsoluteProperty, sdfCutoutSizeIsAbsoluteProperty, currentProps.sdfCutoutPositionProperty, currentProps.sdfCutoutSizeProperty, currentProps.sdfCutoutRotationProperty, sdfCutoutChamferNormalizeProperty, sdfCutoutIsSquircleProperty, currentProps.sdfCutoutChamferProperty, currentProps.sdfCutoutConcavityProperty };
                            PropertyGroupContextMenu(CutoutTypeSubFeatureNames[i], serializedObject, properties);
                        }
                        else if (clicked && Event.current.button == 0)
                            cutoutTypeProperty.enumValueIndex = i;
                    }
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();

                    if (cutoutTypeProperty.enumValueIndex == (int)QuadData.CutoutType.SDF)
                    {
                        EditorGUI.indentLevel++;

                        GUI.enabled = isDefaultProps;
                        EditorGUILayout.PropertyField(sdfCutoutBehaviourProperty, SdfCutoutBehaviourContent);
                        if (sdfCutoutMirrorProperty.enumValueIndex == 0)
                        {
                            EditorGUILayout.PropertyField(sdfCutoutMirrorProperty, SdfCutoutMirrorContent);
                        }
                        else
                        {
                            var mirrorRowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                            var mirrorIndentLevel = EditorGUI.indentLevel;
                            var mirrorIndentWidth = mirrorIndentLevel * 15f;
                            mirrorRowRect.x += mirrorIndentWidth;
                            mirrorRowRect.width -= mirrorIndentWidth;
                            EditorGUI.indentLevel = 0;
                            var mirrorMinLabelWidth = EditorStyles.label.CalcSize(SdfCutoutMirrorContent).x + 4f;
                            var mirrorDefaultLabelWidth = Mathf.Max(mirrorMinLabelWidth, originalLabelWidth - mirrorIndentWidth);
                            var mirrorDropdownMinWidth = 80f;
                            var diagonalLabelWidth = EditorStyles.label.CalcSize(SdfCutoutMirrorIsDiagonalContent).x + 8f;
                            var mirrorRowRects = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, EditorHelpers.FlexibleSpaceAllocation.SmallestFlexibleAreaFirst, mirrorRowRect, 8f, 8f, 0f, (mirrorMinLabelWidth, mirrorDropdownMinWidth, 999f), (diagonalLabelWidth, 18f, 18f));
                            var originalMirrorLabelWidth = EditorGUIUtility.labelWidth;
                            EditorGUIUtility.labelWidth = Mathf.Clamp(mirrorRowRects[0].width - mirrorDropdownMinWidth, mirrorMinLabelWidth, mirrorDefaultLabelWidth);
                            EditorGUI.PropertyField(mirrorRowRects[0], sdfCutoutMirrorProperty, SdfCutoutMirrorContent);
                            EditorGUIUtility.labelWidth = diagonalLabelWidth;
                            EditorGUI.PropertyField(mirrorRowRects[1], sdfCutoutMirrorIsDiagonalProperty, SdfCutoutMirrorIsDiagonalContent);
                            EditorGUIUtility.labelWidth = originalMirrorLabelWidth;
                            EditorGUI.indentLevel = mirrorIndentLevel;
                        }
                        EditorGUILayout.PropertyField(invertCutoutProperty, InvertCutoutContent);
                        GUI.enabled = true;

                        EditorGUILayout.Space(4);

                        var vec2SliderLabelSize = EditorStyles.label.CalcSize(SizeContent).x + EditorGUI.indentLevel * 15;
                        var currentSdfCutoutProps = currentProps.BaseSerializedProperty.boxedValue as ProceduralProperties;
                        var sdfCutoutReferenceSize = flexibleImage != null ? flexibleImage.rectTransform.rect.size + quadData.GetSizeModifier(flexibleImage.rectTransform, currentSdfCutoutProps) : QuadDataPresetEditor.ReferenceParentSize;
                        if (sdfCutoutReferenceSize.x <= 1e-5f || sdfCutoutReferenceSize.y <= 1e-5f)
                            sdfCutoutReferenceSize = Vector2.one;
                        var sdfCutoutExpandedReferenceSize = (outlineExpandsOutwardsProperty?.boolValue ?? false) && flexibleImage != null ? sdfCutoutReferenceSize + Vector2.one * quadData.GetOutlineWidth(flexibleImage.rectTransform, currentSdfCutoutProps) * 2f : sdfCutoutReferenceSize;

                        EditorGUI.BeginChangeCheck();
                        GUI.enabled = isDefaultProps;
                        EditorGUI.showMixedValue = sdfCutoutUsesAnchorsProperty.hasMultipleDifferentValues;
                        var sdfCutoutPositioning = EditorGUILayout.Popup(SdfCutoutPositioningContent, Convert.ToInt32(sdfCutoutUsesAnchorsProperty.boolValue), SdfCutoutPositioningNames);
                        EditorGUI.showMixedValue = false;
                        GUI.enabled = true;
                        if (EditorGUI.EndChangeCheck())
                        {
                            ConvertSDFCutoutPositioning(serializedObject, flexibleImage, sdfCutoutPositioning > 0);
                            raycastAreaDirty = true;
                        }

                        if (sdfCutoutUsesAnchorsProperty.boolValue)
                        {
                            EditorGUI.BeginChangeCheck();
                            var anchorRect = EditorGUILayout.GetControlRect(false, 20f + EditorGUIUtility.singleLineHeight * 5);
                            anchorRect.height = EditorGUIUtility.singleLineHeight;
                            anchorRect = EditorGUI.IndentedRect(anchorRect);
                            var anchorIndentLevel = EditorGUI.indentLevel;
                            EditorGUI.indentLevel = 0;
                            var anchorComponentLabelWidth = Mathf.Approximately(sdfCutoutAnchorMinProperty.vector2Value.x, sdfCutoutAnchorMaxProperty.vector2Value.x) && Mathf.Approximately(sdfCutoutAnchorMinProperty.vector2Value.y, sdfCutoutAnchorMaxProperty.vector2Value.y) ? 12.5f : 45f;
                            var anchorRects = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, anchorRect, 2f, 8f, 0f, (75f, 0f, 0f), (anchorComponentLabelWidth, 15f, 9999f), (anchorComponentLabelWidth, 15f, 9999f));
                            var anchorLabelWidth = EditorGUIUtility.labelWidth;
                            EditorGUIUtility.labelWidth = anchorComponentLabelWidth;
                            EditorGUI.LabelField(anchorRects[0], "Position");
                            anchorRects[0].y += 1.5f + EditorGUIUtility.singleLineHeight;
                            EditorGUI.LabelField(anchorRects[0], "Size");
                            QuadDataPresetEditor.DrawPositionAndSizeFields(ref anchorRects[1], ref anchorRects[2], sdfCutoutAnchorMinProperty, sdfCutoutAnchorMaxProperty, currentProps.sdfCutoutPositionProperty, currentProps.sdfCutoutSizeProperty, sdfCutoutPivotProperty, true);
                            anchorRects[0].y = anchorRects[1].y = anchorRects[2].y = anchorRect.y + 8f + EditorGUIUtility.singleLineHeight * 2;
                            GUI.enabled = isDefaultProps;
                            var anchorReferenceSize = cutoutPositionIgnoresExpandedOutlinesProperty.boolValue ? sdfCutoutReferenceSize : sdfCutoutExpandedReferenceSize;
                            QuadDataPresetEditor.DrawAnchorFields(ref anchorRects[0], ref anchorRects[1], ref anchorRects[2], sdfCutoutAnchorMinProperty, sdfCutoutAnchorMaxProperty, currentProps.sdfCutoutPositionProperty, currentProps.sdfCutoutSizeProperty, sdfCutoutPivotProperty, anchorReferenceSize);
                            QuadDataPresetEditor.DrawPivotField(ref anchorRects[0], ref anchorRects[1], ref anchorRects[2], currentProps.sdfCutoutPositionProperty, currentProps.sdfCutoutSizeProperty, sdfCutoutPivotProperty);
                            GUI.enabled = true;
                            EditorGUI.indentLevel = anchorIndentLevel;
                            EditorGUIUtility.labelWidth = anchorRects[1].x - anchorRect.x + anchorComponentLabelWidth + anchorIndentLevel * 15f;
                            FloatFieldWithDefault(defaultProps.sdfCutoutRotationProperty, currentProps.sdfCutoutRotationProperty, RotationContentNoTip, true);
                            EditorGUIUtility.labelWidth = anchorLabelWidth;
                            if (EditorGUI.EndChangeCheck())
                                raycastAreaDirty = true;
                        }
                        else
                        {
                            EditorGUI.BeginChangeCheck();
                            var modeRowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                            var modeRowRects = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, EditorHelpers.FlexibleSpaceAllocation.SmallestFlexibleAreaFirst, modeRowRect, -10f, -10f, 0f, (0f, 25f, 999f), (80f, 0f, 10f));
                            if (sdfCutoutPositionIsAbsoluteProperty.boolValue)
                            {
                                Vec2FieldsWithDefault(defaultProps.sdfCutoutPositionProperty, currentProps.sdfCutoutPositionProperty, "Pos", vec2SliderLabelSize, 10f, "X", "Y", modeRowRects[0]);
                                currentProps.sdfCutoutPositionProperty.vector2Value = Vector2.Min(Vector2.one * 8191.5f, Vector2.Max(Vector2.one * -8192f, currentProps.sdfCutoutPositionProperty.vector2Value));
                            }
                            else
                                Vec2SlidersWithDefault(defaultProps.sdfCutoutPositionProperty, currentProps.sdfCutoutPositionProperty, -1f, 2f, "Pos", vec2SliderLabelSize, 10f, "X", "Y", modeRowRects[0]);

                            GUI.enabled = isDefaultProps;
                            var wasAbsolute = sdfCutoutPositionIsAbsoluteProperty.boolValue;
                            var isAbsolute = EditorGUI.Popup(modeRowRects[1], Convert.ToInt32(wasAbsolute), RelativeAbsoluteDropDownNames) > 0;
                            if (isAbsolute != wasAbsolute) { var posReferenceSize = cutoutPositionIgnoresExpandedOutlinesProperty.boolValue ? sdfCutoutReferenceSize : sdfCutoutExpandedReferenceSize; var pos = currentProps.sdfCutoutPositionProperty.vector2Value; currentProps.sdfCutoutPositionProperty.vector2Value = isAbsolute ? Vector2.Min(Vector2.one * 8191.5f, Vector2.Max(Vector2.one * -8192f, Vector2.Scale(pos - Vector2.one * 0.5f, posReferenceSize))) : Vector2.Min(Vector2.one * 2f, Vector2.Max(Vector2.one * -1f, new Vector2(0.5f + pos.x / posReferenceSize.x, 0.5f + pos.y / posReferenceSize.y))); sdfCutoutPositionIsAbsoluteProperty.boolValue = isAbsolute; }
                            GUI.enabled = true;

                            modeRowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                            modeRowRects = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, EditorHelpers.FlexibleSpaceAllocation.SmallestFlexibleAreaFirst, modeRowRect, -10f, -10f, 0f, (0f, 25f, 999f), (80f, 0f, 10f));
                            if (sdfCutoutSizeIsAbsoluteProperty.boolValue)
                            {
                                Vec2FieldsWithDefault(defaultProps.sdfCutoutSizeProperty, currentProps.sdfCutoutSizeProperty, "Size", vec2SliderLabelSize, 10f, "X", "Y", modeRowRects[0]);
                                currentProps.sdfCutoutSizeProperty.vector2Value = Vector2.Min(Vector2.one * 16383.5f, Vector2.Max(Vector2.zero, currentProps.sdfCutoutSizeProperty.vector2Value));
                            }
                            else
                                Vec2SlidersWithDefault(defaultProps.sdfCutoutSizeProperty, currentProps.sdfCutoutSizeProperty, 0f, 2f, "Size", vec2SliderLabelSize, 10f, "X", "Y", modeRowRects[0]);

                            GUI.enabled = isDefaultProps;
                            var wasSizeAbsolute = sdfCutoutSizeIsAbsoluteProperty.boolValue;
                            var isSizeAbsolute = EditorGUI.Popup(modeRowRects[1], Convert.ToInt32(wasSizeAbsolute), RelativeAbsoluteDropDownNames) > 0;
                            if (isSizeAbsolute != wasSizeAbsolute) { var size = Vector2.Max(Vector2.zero, currentProps.sdfCutoutSizeProperty.vector2Value); var sizeReferenceSize = cutoutPositionIgnoresExpandedOutlinesProperty.boolValue ? sdfCutoutReferenceSize : sdfCutoutExpandedReferenceSize; currentProps.sdfCutoutSizeProperty.vector2Value = isSizeAbsolute ? Vector2.Min(Vector2.one * 16383.5f, Vector2.Scale(size, sizeReferenceSize)) : Vector2.Min(Vector2.one * 2f, Vector2.Max(Vector2.zero, new Vector2(size.x / sizeReferenceSize.x, size.y / sizeReferenceSize.y))); sdfCutoutSizeIsAbsoluteProperty.boolValue = isSizeAbsolute; }
                            GUI.enabled = true;

                            var rotationRowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                            var rotationLabelWidth = EditorStyles.label.CalcSize(RotationContentNoTip).x + 18f;
                            var rotationRowRects = EditorHelpers.DivideRect(EditorHelpers.Alignment.Center, rotationRowRect, 0f, 0f, 0f, (rotationLabelWidth, 50f + UndoButtonWidth, 50f + UndoButtonWidth));
                            var originalRotationLabelWidth = EditorGUIUtility.labelWidth;
                            EditorGUIUtility.labelWidth = rotationLabelWidth;
                            FloatFieldWithDefault(rotationRowRects[0], defaultProps.sdfCutoutRotationProperty, currentProps.sdfCutoutRotationProperty, RotationContentNoTip, true);
                            EditorGUIUtility.labelWidth = originalRotationLabelWidth;
                            if (EditorGUI.EndChangeCheck())
                                raycastAreaDirty = true;
                        }

                        EditorGUILayout.Space(5);

                        GUI.enabled = isDefaultProps;
                        EditorGUILayout.PropertyField(sdfCutoutChamferNormalizeProperty, NormalizeCornersContent);
                        GUI.enabled = true;

                        EditorGUILayout.Space(0.5f);
                        DrawCornersSection(CornerChamferContent, 5f, 4096, 0f, 1023.75f, 0.5f, currentProps.sdfCutoutChamferProperty, defaultProps.sdfCutoutChamferXProperty, currentProps.sdfCutoutChamferXProperty, defaultProps.sdfCutoutChamferYProperty, currentProps.sdfCutoutChamferYProperty,
                            defaultProps.sdfCutoutChamferZProperty, currentProps.sdfCutoutChamferZProperty, defaultProps.sdfCutoutChamferWProperty, currentProps.sdfCutoutChamferWProperty, null, null, false, originalLabelWidth);

                        EditorGUILayout.Space(2);

                        EditorGUI.BeginChangeCheck();
                        GUI.enabled = isDefaultProps;
                        EditorGUILayout.PropertyField(sdfCutoutIsSquircleProperty, IsSquircleContent);
                        GUI.enabled = true;
                        if (EditorGUI.EndChangeCheck())
                            currentProps.sdfCutoutConcavityProperty.vector4Value = Vector4.Min(currentProps.sdfCutoutConcavityProperty.vector4Value, Vector4.one);

                        EditorGUILayout.Space(0.5f);
                        DrawCornersSection(sdfCutoutIsSquircleProperty.boolValue ? CornerSmoothingContent : CornerConcavityContent, 0.5f, 256, 0f, sdfCutoutIsSquircleProperty.boolValue ? 1f : 1.9921875f, 0.01f, currentProps.sdfCutoutConcavityProperty, defaultProps.sdfCutoutConcavityXProperty, currentProps.sdfCutoutConcavityXProperty, defaultProps.sdfCutoutConcavityYProperty, currentProps.sdfCutoutConcavityYProperty,
                            defaultProps.sdfCutoutConcavityZProperty, currentProps.sdfCutoutConcavityZProperty, defaultProps.sdfCutoutConcavityWProperty, currentProps.sdfCutoutConcavityWProperty, null, null, false, originalLabelWidth);

                        EditorGUI.indentLevel--;
                    }
                    else
                    {
                        EditorGUI.indentLevel++;
                        GUI.enabled = isDefaultProps;
                        EditorGUILayout.PropertyField(simpleCutoutRuleProperty, SimpleCutoutRuleContent);
                        EditorGUILayout.Space(1);
                        EditorGUILayout.PropertyField(cutoutOnlyAffectsOutlineProperty, CutoutAffectsOutlineOnlyContent);
                        EditorGUILayout.PropertyField(invertCutoutProperty, InvertCutoutContent);
                        GUI.enabled = true;
                        EditorGUI.indentLevel--;

                        EditorGUILayout.Space(8);
                        EditorGUIUtility.labelWidth = 12;
                        var controlRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                        var edgePadding = controlRect.width * Mathf.Lerp(0f, 0.2f, controlRect.width / 600f);
                        (float, float, float)[] sections = { (0, 10, 99999), (0, 10, 99999), (0, 10, 99999) };
                        var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Justified, controlRect, 0f, 50f, edgePadding, sections);
                        var rectToUse = rectArr[1];
                        var toggleRect = new Rect(rectToUse.x - 17f, rectToUse.y, 15, rectToUse.height);
                        var toggleProp = simpleCutoutEdgeEnabledProperty.GetArrayElementAtIndex(2);
                        GUI.enabled = isDefaultProps;
                        EditorGUI.showMixedValue = toggleProp.hasMultipleDifferentValues;
                        EditorGUI.BeginChangeCheck();
                        var newToggleValue = EditorGUI.Toggle(toggleRect, toggleProp.boolValue);
                        if (EditorGUI.EndChangeCheck())
                            toggleProp.boolValue = newToggleValue;

                        EditorGUI.showMixedValue = false;
                        GUI.enabled = toggleProp.boolValue;
                        DrawDetentedFloatFieldWithDefault(rectToUse, defaultProps.simpleCutoutZProperty, currentProps.simpleCutoutZProperty, TContent, 4095, 0f, 1023.5f, shrinkField: true);
                        GUI.enabled = isDefaultProps;
                        controlRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                        rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Justified, controlRect, 0f, 50f, edgePadding, sections);
                        rectToUse = rectArr[0];
                        toggleRect = new Rect(rectToUse.x - 17f, rectToUse.y, 15, rectToUse.height);
                        toggleProp = simpleCutoutEdgeEnabledProperty.GetArrayElementAtIndex(0);
                        EditorGUI.showMixedValue = toggleProp.hasMultipleDifferentValues;
                        EditorGUI.BeginChangeCheck();
                        newToggleValue = EditorGUI.Toggle(toggleRect, toggleProp.boolValue);
                        if (EditorGUI.EndChangeCheck())
                            toggleProp.boolValue = newToggleValue;

                        EditorGUI.showMixedValue = false;
                        GUI.enabled = toggleProp.boolValue;
                        DrawDetentedFloatFieldWithDefault(rectToUse, defaultProps.simpleCutoutXProperty, currentProps.simpleCutoutXProperty, LContent, 4095, 0f, 1023.5f, shrinkField: true);
                        GUI.enabled = isDefaultProps;
                        rectToUse = rectArr[2];
                        toggleRect = new Rect(rectToUse.x - 17f, rectToUse.y, 15, rectToUse.height);
                        toggleProp = simpleCutoutEdgeEnabledProperty.GetArrayElementAtIndex(1);
                        EditorGUI.showMixedValue = toggleProp.hasMultipleDifferentValues;
                        EditorGUI.BeginChangeCheck();
                        newToggleValue = EditorGUI.Toggle(toggleRect, toggleProp.boolValue);
                        if (EditorGUI.EndChangeCheck())
                            toggleProp.boolValue = newToggleValue;

                        EditorGUI.showMixedValue = false;
                        GUI.enabled = toggleProp.boolValue;
                        DrawDetentedFloatFieldWithDefault(rectToUse, defaultProps.simpleCutoutYProperty, currentProps.simpleCutoutYProperty, RContent, 4095, 0f, 1023.5f, shrinkField: true);
                        GUI.enabled = isDefaultProps;
                        controlRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                        rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Justified, controlRect, 0f, 50f, edgePadding, sections);
                        rectToUse = rectArr[1];
                        toggleRect = new Rect(rectToUse.x - 17f, rectToUse.y, 15, rectToUse.height);
                        toggleProp = simpleCutoutEdgeEnabledProperty.GetArrayElementAtIndex(3);
                        EditorGUI.showMixedValue = toggleProp.hasMultipleDifferentValues;
                        EditorGUI.BeginChangeCheck();
                        newToggleValue = EditorGUI.Toggle(toggleRect, toggleProp.boolValue);
                        if (EditorGUI.EndChangeCheck())
                            toggleProp.boolValue = newToggleValue;

                        EditorGUI.showMixedValue = false;
                        GUI.enabled = toggleProp.boolValue;
                        DrawDetentedFloatFieldWithDefault(rectToUse, defaultProps.simpleCutoutWProperty, currentProps.simpleCutoutWProperty, BContent, 4095, 0f, 1023.5f, shrinkField: true);
                        GUI.enabled = true;
                    }

                    EditorGUIUtility.labelWidth = originalLabelWidth;
                    EditorGUILayout.Space(4);
                }

                EditorGUILayout.EndVertical();
            }
        }

        verticesDirty |= serializedObject.hasModifiedProperties;
        serializedObject.ApplyModifiedProperties();
        return (verticesDirty, raycastAreaDirty, earlyReturn);

        void DrawProceduralGradientSubSection(bool hasColorPreset, bool outlineFeatureEnabled, float alphaIsBlendLabelWidth, float presetMixLabelWidth, (float, float, float) labelSection)
        {
            if (!gradientModuleVisible)
                return;

            EditorGUILayout.BeginVertical(GUI.skin.box);
            var showProceduralGradientSection = SessionState.GetBool(ShowProceduralGradientKey, true);
            var sdfCutoutActive = cutoutTypeProperty?.enumValueIndex == (int)QuadData.CutoutType.SDF;
            var controlSection = showProceduralGradientSection ? (0, 0f, 9999f) : (0f, 45f, 9999f);
            var lastSection = (hasColorPreset, showProceduralGradientSection) switch
            {
                (true, false) => (presetMixLabelWidth, 25f, 35f),
                (_, true) => (alphaIsBlendLabelWidth, 18f, 18f),
                _ => (0f, 0f, 0f)
            };

            var foldoutRectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, EditorHelpers.FlexibleSpaceAllocation.SmallestFlexibleAreaFirst, EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight), 2f, 2f, 0f, labelSection, controlSection, lastSection);
            ModuleContextMenu(foldoutRectArr[0], serializedObject, Module.Gradient, baseProperty.FindPropertyRelative(QuadData.GradientConfigFieldName), sdfCutoutActive ? null : currentProps.proceduralGradientColorsProperty);

            SessionState.SetBool(ShowProceduralGradientKey, EditorGUI.Foldout(foldoutRectArr[0], SessionState.GetBool(ShowProceduralGradientKey, true), "Procedural Gradient"));
            if (sdfCutoutActive && showProceduralGradientSection)
                EditorGUILayout.HelpBox("Procedural Gradient is unavailable while SDF cutout is enabled.", MessageType.Info);

            EditorGUI.BeginDisabledGroup(sdfCutoutActive);
            if (!showProceduralGradientSection)
            {
                PropertyFieldWithDefault(foldoutRectArr[1], defaultProps.proceduralGradientColorsProperty.GetArrayElementAtIndex(0), currentProps.proceduralGradientColorsProperty.GetArrayElementAtIndex(0), GUIContent.none, shrinkField: true);
                if (hasColorPreset)
                {
                    GUI.enabled = isDefaultProps;
                    EditorGUIUtility.labelWidth = presetMixLabelWidth;
                    EditorGUI.PropertyField(foldoutRectArr[2], proceduralGradientColorPresetMixProperty, PresetMixContent);
                    EditorGUIUtility.labelWidth = originalLabelWidth;
                    GUI.enabled = true;
                }
            }
            else
            {
                GUI.enabled = isDefaultProps;
                EditorGUIUtility.labelWidth = alphaIsBlendLabelWidth;
                EditorGUI.PropertyField(foldoutRectArr[2], proceduralGradientColorAlphaIsBlendProperty, ProceduralGradientAlphaIsBlendContent);
                EditorGUIUtility.labelWidth = originalLabelWidth;
                GUI.enabled = true;

                if (hasColorPreset)
                {
                    GUI.enabled = isDefaultProps;
                    EditorGUILayout.PropertyField(proceduralGradientColorPresetMixProperty, PresetMixContent);
                    GUI.enabled = true;
                }

                if (proceduralGradientArea == SecondaryColorArea.Outline)
                {
                    if (!outlineFeatureEnabled)
                        EditorGUILayout.HelpBox("Set to outline region, but the outline feature is disabled!", MessageType.Warning);
                    else if ((currentProps.outlineWidthProperty?.floatValue ?? 0f) <= 0f)
                        EditorGUILayout.HelpBox("Set to outline region, but outline width is 0.", MessageType.Warning);
                }

                EditorGUI.indentLevel++;
                DrawColorGrid(serializedObject, proceduralGradientColorDimensionsProperty, proceduralGradientColorWrapModeXProperty, proceduralGradientColorWrapModeYProperty, defaultProps.proceduralGradientColorsProperty, currentProps.proceduralGradientColorsProperty, defaultProps.proceduralGradientColorOffsetProperty, currentProps.proceduralGradientColorOffsetProperty, defaultProps.proceduralGradientColorRotationProperty, currentProps.proceduralGradientColorRotationProperty, defaultProps.proceduralGradientColorScaleProperty, currentProps.proceduralGradientColorScaleProperty, isDefaultProps, ShowProceduralGradientGridAdvancedKey);
                EditorGUILayout.Space(7.5f);
                var headerRects = EditorHelpers.DivideRect(EditorHelpers.Alignment.Center, EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight), -8f, -8f, 5f, (60f, 0f, 0), (0f, 25f, 120f));
                var boxRect = headerRects[0];
                boxRect.width += headerRects[1].width + 2;
                boxRect.height += 20;
                boxRect.y -= 5;
                boxRect.x += 3f;

                GUI.Box(boxRect, GUIContent.none);
                headerRects[0].y += 2;
                EditorGUI.LabelField(headerRects[0], "Region");
                EditorGUIUtility.labelWidth = 0;
                GUI.enabled = isDefaultProps;
                EditorGUI.BeginChangeCheck();

                proceduralGradientArea =
                    proceduralGradientAffectsInteriorProperty.boolValue
                        ? proceduralGradientAffectsOutlineProperty.boolValue
                            ? SecondaryColorArea.All
                            : SecondaryColorArea.Interior
                        : SecondaryColorArea.Outline;

                EditorGUI.showMixedValue = proceduralGradientAffectsInteriorProperty.hasMultipleDifferentValues || proceduralGradientAffectsOutlineProperty.hasMultipleDifferentValues;
                headerRects[1].y += 3;
                proceduralGradientArea = (SecondaryColorArea)EditorGUI.EnumPopup(headerRects[1], GUIContent.none, proceduralGradientArea);
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                {
                    proceduralGradientAffectsInteriorProperty.boolValue = proceduralGradientArea != SecondaryColorArea.Outline;
                    proceduralGradientAffectsOutlineProperty.boolValue = proceduralGradientArea != SecondaryColorArea.Interior;
                }

                GUI.enabled = true;
                EditorGUIUtility.labelWidth = originalLabelWidth;
                EditorGUILayout.Space(4);

                EditorGUILayout.BeginHorizontal();
                GUI.enabled = isDefaultProps;
                for (int i = 0; i < ProceduralGradientNames.Length; i++)
                {
                    var style = GUI.skin.button;

                    if (i == 0)
                        style = new GUIStyle(GUI.skin.FindStyle($"{style.name}left")) { margin = { left = EditorGUI.indentLevel * 15 } };
                    else if (i == ProceduralGradientNames.Length - 1)
                        style = new GUIStyle(GUI.skin.FindStyle($"{style.name}right"));
                    else
                        style = new GUIStyle(GUI.skin.FindStyle($"{style.name}mid"));

                    if (i == proceduralGradientTypeProperty.enumValueIndex)
                    {
                        GUI.color = Color.cyan;
                        if (EditorGUIUtility.isProSkin)
                            style.normal.textColor = style.hover.textColor = style.active.textColor = Color.cyan;
                    }

                    var featureEnabled = FlexibleImageFeatureManager.IsFeatureEnabled(ProceduralGradientSubFeatureNames[i]);
                    GUI.enabled = featureEnabled;
                    var clicked = GUILayout.Button(ProceduralGradientNames[i], style);
                    var contextClicked = clicked && Event.current.button == 1;
                    GUI.enabled = true;
                    GUI.color = originalGUIColor;
                    if (contextClicked)
                    {
                        var properties = i switch
                        {
                            (int)QuadData.GradientType.SDF => new[] { currentProps.sdfGradientInnerDistanceProperty, currentProps.sdfGradientOuterDistanceProperty, currentProps.sdfGradientInnerReachProperty, currentProps.sdfGradientOuterReachProperty, proceduralGradientInvertProperty, screenSpaceProceduralGradientProperty, proceduralGradientPositionFromPointerProperty, currentProps.proceduralGradientPointerStrengthProperty },
                            (int)QuadData.GradientType.Angle => new[] { currentProps.proceduralGradientPositionProperty, currentProps.angleGradientStrengthProperty, currentProps.proceduralGradientAngleProperty, proceduralGradientInvertProperty, proceduralGradientAspectCorrectionProperty, screenSpaceProceduralGradientProperty, proceduralGradientPositionFromPointerProperty },
                            (int)QuadData.GradientType.Radial => new[] { currentProps.proceduralGradientPositionProperty, currentProps.radialGradientSizeProperty, currentProps.radialGradientStrengthProperty, proceduralGradientInvertProperty, proceduralGradientAspectCorrectionProperty, screenSpaceProceduralGradientProperty, proceduralGradientPositionFromPointerProperty },
                            (int)QuadData.GradientType.Conical => new[] { currentProps.proceduralGradientPositionProperty, currentProps.conicalGradientTailStrengthProperty, currentProps.conicalGradientCurvatureProperty, currentProps.proceduralGradientAngleProperty, proceduralGradientInvertProperty, screenSpaceProceduralGradientProperty, proceduralGradientPositionFromPointerProperty },
                            _ => new[] { currentProps.noiseStrengthProperty, currentProps.noiseScaleProperty, currentProps.noiseEdgeProperty, currentProps.noiseSeedProperty, noiseGradientAlternateModeProperty, proceduralGradientInvertProperty, screenSpaceProceduralGradientProperty, proceduralGradientPositionFromPointerProperty, currentProps.proceduralGradientPointerStrengthProperty }
                        };
                        PropertyGroupContextMenu(ProceduralGradientSubFeatureNames[i], serializedObject, properties);
                    }
                    else if (clicked && Event.current.button == 0)
                        proceduralGradientTypeProperty.enumValueIndex = i;
                }

                GUI.enabled = true;


                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(3);

                GUI.enabled = true;
                var vec2SliderLabelSize = EditorStyles.label.CalcSize(SizeContent).x + EditorGUI.indentLevel * 15;

                if (proceduralGradientTypeProperty.enumValueIndex == (int)QuadData.GradientType.SDF)
                {
                    var positionSliderMaxLimit = Mathf.Max(256, currentProps.sdfGradientInnerDistanceProperty.floatValue);
                    MinMaxSliderWithDefaults(defaultProps.sdfGradientOuterDistanceProperty, defaultProps.sdfGradientInnerDistanceProperty, currentProps.sdfGradientOuterDistanceProperty, currentProps.sdfGradientInnerDistanceProperty, 0f, positionSliderMaxLimit, PosContent, 60f, 12f, 65f, "O", " I", 32768, 0f, 4095.875f);
                    DoubleSlidersWithDefaults(defaultProps.sdfGradientOuterReachProperty, defaultProps.sdfGradientInnerReachProperty, currentProps.sdfGradientOuterReachProperty, currentProps.sdfGradientInnerReachProperty, 0, 1, "Reach", 52f, 12f, "O", "I");
                    EditorGUILayout.Space(4);
                    GUI.enabled = isDefaultProps;
                    EditorGUILayout.PropertyField(proceduralGradientInvertProperty, ProceduralGradientInvertContent);

                    if (screenSpaceProceduralGradientFeatureEnabled)
                        EditorGUILayout.PropertyField(screenSpaceProceduralGradientProperty, ScreenSpaceContent);

                    if (pointerRelativeProceduralGradientFeatureEnabled)
                        EditorGUILayout.PropertyField(proceduralGradientPositionFromPointerProperty, RevealedByPointerContent);

                    GUI.enabled = true;
                    if (pointerRelativeProceduralGradientFeatureEnabled && proceduralGradientPositionFromPointerProperty.boolValue)
                        PropertyFieldWithDefault(defaultProps.proceduralGradientPointerStrengthProperty, currentProps.proceduralGradientPointerStrengthProperty, ProceduralGradientRevealStrengthContent, shrinkField: true);
                }
                else if (proceduralGradientTypeProperty.enumValueIndex == (int)QuadData.GradientType.Noise)
                {
                    PropertyFieldWithDefault(defaultProps.noiseStrengthProperty, currentProps.noiseStrengthProperty, NoiseStrengthContent, shrinkField: true);
                    PropertyFieldWithDefault(defaultProps.noiseScaleProperty, currentProps.noiseScaleProperty, NoiseScaleContent, shrinkField: true);
                    PropertyFieldWithDefault(defaultProps.noiseEdgeProperty, currentProps.noiseEdgeProperty, NoiseEdgeContent, shrinkField: true);
                    PropertyFieldWithDefault(defaultProps.noiseSeedProperty, currentProps.noiseSeedProperty, NoiseSeedContent, shrinkField: true);
                    GUI.enabled = isDefaultProps;
                    EditorGUILayout.PropertyField(noiseGradientAlternateModeProperty, NoiseGradientAlternateModeContent);
                    EditorGUILayout.PropertyField(proceduralGradientInvertProperty, ProceduralGradientInvertContent);
                    
                    if (screenSpaceProceduralGradientFeatureEnabled)
                        EditorGUILayout.PropertyField(screenSpaceProceduralGradientProperty, ScreenSpaceContent);

                    if (pointerRelativeProceduralGradientFeatureEnabled)
                        EditorGUILayout.PropertyField(proceduralGradientPositionFromPointerProperty, RevealedByPointerContent);

                    GUI.enabled = true;
                    if (pointerRelativeProceduralGradientFeatureEnabled && proceduralGradientPositionFromPointerProperty.boolValue)
                        PropertyFieldWithDefault(defaultProps.proceduralGradientPointerStrengthProperty, currentProps.proceduralGradientPointerStrengthProperty, ProceduralGradientRevealStrengthContent, shrinkField: true);
                }
                else
                {
                    if (proceduralGradientTypeProperty.enumValueIndex == (int)QuadData.GradientType.Angle)
                    {
                        Vec2SlidersWithDefault(defaultProps.proceduralGradientPositionProperty, currentProps.proceduralGradientPositionProperty, -0.5f, 1.5f, "Pos", vec2SliderLabelSize, 10f, "X", "Y");
                        Vec2SlidersWithDefault(defaultProps.angleGradientStrengthProperty, currentProps.angleGradientStrengthProperty, 0, 1, "Size", vec2SliderLabelSize, 10f, "L", "R");
                        EditorGUILayout.Space(4);
                        EditorGUILayout.BeginHorizontal();
                        PropertyFieldWithDefault(defaultProps.proceduralGradientAngleProperty, currentProps.proceduralGradientAngleProperty, ProceduralGradientAngleContent, shrinkField: true);
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.Space(4);
                        GUI.enabled = isDefaultProps;
                        EditorGUILayout.PropertyField(proceduralGradientInvertProperty, ProceduralGradientInvertContent);
                        EditorGUILayout.PropertyField(proceduralGradientAspectCorrectionProperty, ProceduralGradientAspectCorrectionContent);
                        GUI.enabled = true;
                    }
                    else if (proceduralGradientTypeProperty.enumValueIndex == (int)QuadData.GradientType.Conical)
                    {
                        Vec2SlidersWithDefault(defaultProps.proceduralGradientPositionProperty, currentProps.proceduralGradientPositionProperty, -0.5f, 1.5f, "Pos", vec2SliderLabelSize, 10f, "X", "Y");
                        EditorGUILayout.Space(4);
                        PropertyFieldWithDefault(defaultProps.conicalGradientTailStrengthProperty, currentProps.conicalGradientTailStrengthProperty, ConicalGradientTailStrengthContent, shrinkField: true);
                        PropertyFieldWithDefault(defaultProps.conicalGradientCurvatureProperty, currentProps.conicalGradientCurvatureProperty, ConicalGradientCurvatureContent, shrinkField: true);
                        EditorGUILayout.BeginHorizontal();
                        PropertyFieldWithDefault(defaultProps.proceduralGradientAngleProperty, currentProps.proceduralGradientAngleProperty, ProceduralGradientAngleContent, shrinkField: true);
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.Space(4);
                        GUI.enabled = isDefaultProps;
                        EditorGUILayout.PropertyField(proceduralGradientInvertProperty, ProceduralGradientInvertContent);
                        GUI.enabled = true;
                    }
                    else
                    {
                        Vec2SlidersWithDefault(defaultProps.proceduralGradientPositionProperty, currentProps.proceduralGradientPositionProperty, -0.5f, 1.5f, "Pos", vec2SliderLabelSize, 10f, "X", "Y");
                        Vec2SlidersWithDefault(defaultProps.radialGradientSizeProperty, currentProps.radialGradientSizeProperty, 0, 1, "Size", vec2SliderLabelSize, 10f, "X", "Y");
                        EditorGUILayout.Space(4);
                        PropertyFieldWithDefault(defaultProps.radialGradientStrengthProperty, currentProps.radialGradientStrengthProperty, ProceduralGradientStrengthContent, shrinkField: true);
                        EditorGUILayout.Space(4);
                        GUI.enabled = isDefaultProps;
                        EditorGUILayout.PropertyField(proceduralGradientInvertProperty, ProceduralGradientInvertContent);
                        EditorGUILayout.PropertyField(proceduralGradientAspectCorrectionProperty, ProceduralGradientAspectCorrectionContent);
                        GUI.enabled = true;
                    }

                    GUI.enabled = isDefaultProps;
                    
                    if (screenSpaceProceduralGradientFeatureEnabled)
                        EditorGUILayout.PropertyField(screenSpaceProceduralGradientProperty, ScreenSpaceContent);

                    if (pointerRelativeProceduralGradientFeatureEnabled)
                        EditorGUILayout.PropertyField(proceduralGradientPositionFromPointerProperty, PositionFromPointerContent);

                    GUI.enabled = true;

                    if (pointerRelativeProceduralGradientFeatureEnabled && proceduralGradientPositionFromPointerProperty.boolValue && !screenSpaceProceduralGradientProperty.boolValue && proceduralGradientTypeProperty.enumValueIndex != (int)QuadData.GradientType.SDF && (meshSubdivisionsProperty.intValue > 2 || meshSubdivisionsProperty.intValue == 2 && meshTopologyProperty.enumValueIndex == (int)QuadData.Topology.X))
                        EditorGUILayout.HelpBox("Object-space, pointer relative gradients may exhibit discontinuities with >=3 subdivisions (or 2 with \"X\" topology), primarily for hard straight edges. If noticeable, consider reducing subdivisions or changing to screenspace.", MessageType.Warning);
                }

                if (pointerRelativeProceduralGradientFeatureEnabled && proceduralGradientPositionFromPointerProperty.boolValue && !Application.isPlaying && flexibleImage != null)
                {
                    var mainCamera = Camera.main;
                    if (mainCamera != null)
                    {
                        var imagePos = GetScreenSpaceCenter(flexibleImage.rectTransform);
                        Shader.SetGlobalVector(FlexibleImage.ScreenSpacePointerPosID, new Vector4(imagePos.x, imagePos.y, imagePos.x / mainCamera.pixelWidth, imagePos.y / mainCamera.pixelHeight));
                    }
                }

                EditorGUILayout.Space(4);
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();
        }

        void DrawPatternSubSection(bool hasColorPreset, bool outlineFeatureEnabled, float alphaIsBlendLabelWidth, float presetMixLabelWidth, (float, float, float) labelSection)
        {
            if (!patternModuleVisible)
                return;

            EditorGUILayout.BeginVertical(GUI.skin.box);
            var showPatternSection = SessionState.GetBool(ShowPatternKey, true);
            var controlSection = showPatternSection ? (0, 0f, 9999f) : (0f, 45f, 9999f);
            var lastSection = (hasColorPreset, showPatternSection) switch
            {
                (true, false) => (presetMixLabelWidth, 25f, 35f),
                (_, true) => (alphaIsBlendLabelWidth, 18f, 18f),
                _ => (0f, 0f, 0f)
            };

            var foldoutRectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, EditorHelpers.FlexibleSpaceAllocation.SmallestFlexibleAreaFirst, EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight), 2f, 2f, 0f, labelSection, controlSection, lastSection);

            ModuleContextMenu(foldoutRectArr[0], serializedObject, Module.Pattern, baseProperty.FindPropertyRelative(QuadData.PatternConfigFieldName), currentProps.patternColorsProperty);
            SessionState.SetBool(ShowPatternKey, EditorGUI.Foldout(foldoutRectArr[0], SessionState.GetBool(ShowPatternKey, true), "Pattern"));

            if (!SessionState.GetBool(ShowPatternKey, true))
            {
                PropertyFieldWithDefault(foldoutRectArr[1], defaultProps.patternColorsProperty.GetArrayElementAtIndex(0), currentProps.patternColorsProperty.GetArrayElementAtIndex(0), GUIContent.none, shrinkField: true);
                if (hasColorPreset)
                {
                    GUI.enabled = isDefaultProps;
                    EditorGUIUtility.labelWidth = presetMixLabelWidth;
                    EditorGUI.PropertyField(foldoutRectArr[2], patternColorPresetMixProperty, PresetMixContent);
                    EditorGUIUtility.labelWidth = originalLabelWidth;
                    GUI.enabled = true;
                }
            }
            else
            {
                GUI.enabled = isDefaultProps;
                EditorGUIUtility.labelWidth = alphaIsBlendLabelWidth;
                EditorGUI.PropertyField(foldoutRectArr[2], patternColorAlphaIsBlendProperty, PatternAlphaIsBlendContent);
                EditorGUIUtility.labelWidth = originalLabelWidth;
                GUI.enabled = true;

                if (hasColorPreset)
                {
                    GUI.enabled = isDefaultProps;
                    EditorGUILayout.PropertyField(patternColorPresetMixProperty, PresetMixContent);
                    GUI.enabled = true;
                }

                if (patternArea == SecondaryColorArea.Outline)
                {
                    if (!outlineFeatureEnabled)
                        EditorGUILayout.HelpBox("Set to outline region, but the outline feature is disabled!", MessageType.Warning);
                    else if ((currentProps.outlineWidthProperty?.floatValue ?? 0f) <= 0f)
                        EditorGUILayout.HelpBox("Set to outline region, but outline width is 0.", MessageType.Warning);
                }


                EditorGUI.indentLevel++;
                DrawColorGrid(serializedObject, patternColorDimensionsProperty, patternColorWrapModeXProperty, patternColorWrapModeYProperty, defaultProps.patternColorsProperty, currentProps.patternColorsProperty, defaultProps.patternColorOffsetProperty, currentProps.patternColorOffsetProperty, defaultProps.patternColorRotationProperty, currentProps.patternColorRotationProperty, defaultProps.patternColorScaleProperty, currentProps.patternColorScaleProperty, isDefaultProps, ShowPatternGridAdvancedKey);
                EditorGUILayout.Space(7.5f);
                var headerRects = EditorHelpers.DivideRect(EditorHelpers.Alignment.Center, EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight), -8f, -8f, 5f, (60f, 0f, 0), (0f, 25f, 120f));
                var boxRect = headerRects[0];
                boxRect.width += headerRects[1].width + 2;
                boxRect.height += 20;
                boxRect.y -= 5;
                boxRect.x += 3f;

                GUI.Box(boxRect, GUIContent.none);
                headerRects[0].y += 2;
                EditorGUI.LabelField(headerRects[0], "Region");
                EditorGUIUtility.labelWidth = 0;
                GUI.enabled = isDefaultProps;
                EditorGUI.BeginChangeCheck();

                patternArea =
                    patternAffectsInteriorProperty.boolValue
                        ? patternAffectsOutlineProperty.boolValue
                            ? SecondaryColorArea.All
                            : SecondaryColorArea.Interior
                        : SecondaryColorArea.Outline;

                EditorGUI.showMixedValue = patternAffectsInteriorProperty.hasMultipleDifferentValues || patternAffectsOutlineProperty.hasMultipleDifferentValues;
                headerRects[1].y += 3;
                patternArea = (SecondaryColorArea)EditorGUI.EnumPopup(headerRects[1], GUIContent.none, patternArea);
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                {
                    patternAffectsInteriorProperty.boolValue = patternArea != SecondaryColorArea.Outline;
                    patternAffectsOutlineProperty.boolValue = patternArea != SecondaryColorArea.Interior;
                }

                GUI.enabled = true;
                EditorGUIUtility.labelWidth = originalLabelWidth;
                EditorGUILayout.Space(4);

                EditorGUILayout.BeginHorizontal();
                GUI.enabled = isDefaultProps;
                var enumValue = patternOrientationProperty.enumValueFlag;
                int activeCategory = 0;
                for (int i = 0; i < PatternCategoryNames.Length; i++)
                {
                    var style = GUI.skin.button;

                    if (i == 0)
                        style = new GUIStyle(GUI.skin.FindStyle($"{style.name}left")) { margin = { left = EditorGUI.indentLevel * 15 } };
                    else if (i == ProceduralGradientNames.Length - 1)
                        style = new GUIStyle(GUI.skin.FindStyle($"{style.name}right"));
                    else
                        style = new GUIStyle(GUI.skin.FindStyle($"{style.name}mid"));

                    bool categoryIsActive;
                    if (i == 0)
                        categoryIsActive = enumValue < PatternCategoryEnumValues[1];
                    else if (i == 1)
                        categoryIsActive = enumValue >= PatternCategoryEnumValues[1] && enumValue < PatternCategoryEnumValues[3];
                    else if (i == 2)
                        categoryIsActive = enumValue != PatternCategoryEnumValues[4] && enumValue > PatternCategoryEnumValues[3];
                    else if (i == 3)
                        categoryIsActive = enumValue == PatternCategoryEnumValues[3];
                    else
                        categoryIsActive = enumValue == PatternCategoryEnumValues[4];

                    if (categoryIsActive)
                    {
                        activeCategory = i;

                        GUI.color = Color.cyan;
                        if (EditorGUIUtility.isProSkin)
                            style.normal.textColor = style.hover.textColor = style.active.textColor = Color.cyan;
                    }

                    var featureEnabled = FlexibleImageFeatureManager.IsFeatureEnabled(PatternCategorySubFeatureNames[i]);
                    GUI.enabled = featureEnabled;
                    var clicked = GUILayout.Button(PatternCategoryNames[i], style);
                    var contextClicked = categoryIsActive && clicked && Event.current.button == 1;
                    GUI.enabled = true;
                    GUI.color = originalGUIColor;

                    if (contextClicked)
                    {
                        var properties = i switch
                        {
                            0 => new[] { patternOrientationProperty, currentProps.patternDensityProperty, currentProps.patternLineThicknessProperty, currentProps.patternSpeedProperty, scanlinePatternSpeedIsStaticOffsetProperty, softPatternProperty, screenSpacePatternProperty },
                            1 => new[] { patternOrientationProperty, patternOriginPosProperty, currentProps.patternDensityProperty, currentProps.patternLineThicknessProperty, currentProps.patternSpeedProperty, scanlinePatternSpeedIsStaticOffsetProperty, softPatternProperty, screenSpacePatternProperty },
                            2 => new[] { patternOrientationProperty, currentProps.patternDensityProperty, currentProps.patternCellParamProperty, softPatternProperty, screenSpacePatternProperty },
                            3 => new[] { patternOrientationProperty, currentProps.patternDensityProperty, currentProps.patternLineThicknessProperty, currentProps.patternCellParamProperty, softPatternProperty, screenSpacePatternProperty },
                            _ => new[] { patternOrientationProperty, spritePatternRotationModeProperty, spritePatternOffsetDirectionDegreesProperty, currentProps.patternSpriteRotationProperty, currentProps.patternDensityProperty, currentProps.patternSpeedProperty, scanlinePatternSpeedIsStaticOffsetProperty, screenSpacePatternProperty }
                        };
                        PropertyGroupContextMenu(PatternCategorySubFeatureNames[i], serializedObject, properties);
                    }
                    else if (clicked && Event.current.button == 0 && !categoryIsActive)
                    {
                        if (i == 0)
                            patternOrientationProperty.enumValueFlag = enumValue = PatternLineValues[0];
                        else if (i == 2)
                            patternOrientationProperty.enumValueFlag = enumValue = PatternGridValues[0];
                        else
                            patternOrientationProperty.enumValueFlag = enumValue = PatternCategoryEnumValues[i];
                        activeCategory = i;
                    }
                }

                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(3);

                if (activeCategory <= 1)
                {
                    if (activeCategory == 0)
                    {
                        GUI.enabled = isDefaultProps;
                        EditorGUI.BeginChangeCheck();
                        var newValue = PatternLineValues[EditorGUILayout.Popup(OrientationContent, PatternLineValues[enumValue], PatternLineNames)];
                        if (EditorGUI.EndChangeCheck())
                            patternOrientationProperty.enumValueFlag = newValue;

                        GUI.enabled = true;
                    }
                    else
                    {
                        GUI.enabled = isDefaultProps;
                        int currentEnumIdx = 0;
                        for (int i = 0; i < PatternShapeValues.Length; i++)
                        {
                            if (patternOrientationProperty.enumValueFlag != PatternShapeValues[i])
                                continue;

                            currentEnumIdx = i;
                            break;
                        }

                        EditorGUI.BeginChangeCheck();
                        var newValue = PatternShapeValues[EditorGUILayout.Popup(ShapeContent, currentEnumIdx, PatternShapeNames)];
                        if (EditorGUI.EndChangeCheck())
                            patternOrientationProperty.enumValueFlag = newValue;

                        EditorGUILayout.PropertyField(patternOriginPosProperty, PatternOriginPosContent);
                        GUI.enabled = true;
                    }

                    PropertyFieldWithDefault(defaultProps.patternDensityProperty, currentProps.patternDensityProperty, PatternDensityContent, shrinkField: true);
                    HandlePatternLineThickness(PatternLineThicknessContent);
                    if (scanlinePatternSpeedIsStaticOffsetProperty.boolValue)
                        FloatFieldWithDefault(defaultProps.patternSpeedProperty, currentProps.patternSpeedProperty, PatternOffsetContent, true);
                    else
                        DrawDetentedFloatFieldWithDefault(defaultProps.patternSpeedProperty, currentProps.patternSpeedProperty, PatternSpeedContent, 4096, -0.999511719f, 1f, shrinkField: true);

                    GUI.enabled = isDefaultProps;
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(scanlinePatternSpeedIsStaticOffsetProperty, StaticOffsetContent);
                    if (EditorGUI.EndChangeCheck())
                        currentProps.patternSpeedProperty.floatValue = 0f;

                    GUI.enabled = true;
                }
                else if (activeCategory == 2)
                {
                    int currentEnumIdx = 0;
                    for (int i = 0; i < PatternGridValues.Length; i++)
                    {
                        if (patternOrientationProperty.enumValueFlag != PatternGridValues[i])
                            continue;

                        currentEnumIdx = i;
                        break;
                    }

                    EditorGUI.BeginChangeCheck();
                    var newValue = PatternGridValues[EditorGUILayout.Popup(ShapeContent, currentEnumIdx, PatternGridNames)];
                    if (EditorGUI.EndChangeCheck())
                        patternOrientationProperty.enumValueFlag = newValue;

                    PropertyFieldWithDefault(defaultProps.patternDensityProperty, currentProps.patternDensityProperty, PatternDensityContent, shrinkField: true);
                    PropertyFieldWithDefault(defaultProps.patternCellParamProperty, currentProps.patternCellParamProperty, FillContent, shrinkField: true);
                }
                else if (activeCategory == 3)
                {
                    PropertyFieldWithDefault(defaultProps.patternDensityProperty, currentProps.patternDensityProperty, PatternDensityContent, shrinkField: true);
                    HandlePatternLineThickness(PatternLineThicknessContent);
                    PropertyFieldWithDefault(defaultProps.patternCellParamProperty, currentProps.patternCellParamProperty, FractalContent, shrinkField: true);
                }
                else
                {
                    if (spriteProperty != null)
                    {
                        EditorGUILayout.PropertyField(spriteProperty, SpriteContent);
                        if (flexibleImage != null)
                        {
                            if (spriteProperty.boxedValue != null && flexibleImage.sprite.packed)
                                EditorGUILayout.HelpBox("Sprite pattern does not support atlased sprites.", MessageType.Warning);
                            if (flexibleImage.type == Image.Type.Sliced || flexibleImage.type == Image.Type.Tiled)
                                EditorGUILayout.HelpBox($"Sprite pattern may not show correctly with Image Type - {flexibleImage.type}. {Image.Type.Simple} or {Image.Type.Filled} are recommended.", MessageType.Warning);
                            if (spriteProperty.boxedValue != null && patternColorDimensionsProperty.vector2IntValue == Vector2Int.one && currentProps.patternColorsProperty.GetArrayElementAtIndex(0).colorValue == Color.black)
                                EditorGUILayout.HelpBox("Pattern color is black. Unless it has transparency, the sprite may not be visible.", MessageType.Info);
                        }

                        EditorGUILayout.Space(1f);
                    }

                    var blurEnabled = flexibleImage != null && CheckPrivateBooleanField(flexibleImage, "_blurEnabled");
                    if (blurEnabled)
                    {
                        var controlRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                        EditorGUI.LabelField(controlRect, PatternSpriteRotationContent);
                        var labelOffset = originalLabelWidth - 15 * EditorGUI.indentLevel;
                        controlRect.x += labelOffset;
                        controlRect.width -= labelOffset;
                        EditorGUI.LabelField(controlRect, controlRect.width > 175 ? PatternSpriteRotationUnavailableContent : PatternSpriteRotationUnavailableContentShort, UnavailableFeatureStyle);
                    }
                    else
                    {
                        GUI.enabled = isDefaultProps;
                        EditorGUILayout.PropertyField(spritePatternRotationModeProperty, PatternSpriteRotationModeContent);
                        GUI.enabled = true;

                        if (spritePatternRotationModeProperty.enumValueIndex == (int)QuadData.SpritePatternRotation.Sprite)
                        {
                            PropertyFieldWithDefault(defaultProps.patternSpriteRotationProperty, currentProps.patternSpriteRotationProperty, PatternSpriteRotationContent, shrinkField: true);
                        }
                        else
                        {
                            GUI.enabled = isDefaultProps;
                            EditorGUI.BeginChangeCheck();
                            var newValue = EditorGUILayout.Popup(PatternOffsetRotationContent, spritePatternOffsetDirectionDegreesProperty.enumValueIndex, SpriteOffsetRotationDirectionNames);
                            if (EditorGUI.EndChangeCheck())
                                spritePatternOffsetDirectionDegreesProperty.enumValueIndex = newValue;

                            GUI.enabled = true;
                        }
                    }

                    PropertyFieldWithDefault(defaultProps.patternDensityProperty, currentProps.patternDensityProperty, PatternDensityContent, shrinkField: true);

                    if (scanlinePatternSpeedIsStaticOffsetProperty.boolValue)
                        FloatFieldWithDefault(defaultProps.patternSpeedProperty, currentProps.patternSpeedProperty, PatternOffsetContent, true);
                    else
                        DrawDetentedFloatFieldWithDefault(defaultProps.patternSpeedProperty, currentProps.patternSpeedProperty, PatternSpeedContent, 4096, -0.999511719f, 1f, shrinkField: true);

                    GUI.enabled = isDefaultProps;
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(scanlinePatternSpeedIsStaticOffsetProperty, StaticOffsetContent);
                    if (EditorGUI.EndChangeCheck())
                        currentProps.patternSpeedProperty.floatValue = 0f;

                    GUI.enabled = true;
                }

                void HandlePatternLineThickness(GUIContent content)
                {
                    var blurEnabled = flexibleImage != null && CheckPrivateBooleanField(flexibleImage, "_blurEnabled");
                    if (blurEnabled)
                    {
                        var controlRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                        EditorGUI.LabelField(controlRect, content);
                        var labelOffset = originalLabelWidth - 15 * EditorGUI.indentLevel;
                        controlRect.x += labelOffset;
                        controlRect.width -= labelOffset;
                        EditorGUI.LabelField(controlRect, controlRect.width > 175 ? PatternLineThicknessUnavailableContent : PatternLineThicknessUnavailableContentShort, UnavailableFeatureStyle);
                    }
                    else
                    {
                        PropertyFieldWithDefault(defaultProps.patternLineThicknessProperty, currentProps.patternLineThicknessProperty, content, shrinkField: true);
                    }
                }

                GUI.enabled = isDefaultProps;
                if (activeCategory != 4)
                    EditorGUILayout.PropertyField(softPatternProperty, SoftPatternContent);

                if (screenSpacePatternFeatureEnabled)
                    EditorGUILayout.PropertyField(screenSpacePatternProperty, ScreenSpaceContent);

                GUI.enabled = true;
                EditorGUILayout.Space(4);
                EditorGUI.indentLevel--;

            }
            EditorGUILayout.EndVertical();
        }
    }
    
    private void DrawCornersSection(GUIContent titleContent, float quickStep, int detents, float min, float max, float labelScrubSensitivity, SerializedProperty currentProp, SerializedProperty defaultPropTL, SerializedProperty currentPropTL, SerializedProperty defaultPropTR, SerializedProperty currentPropTR,
        SerializedProperty defaultPropBL, SerializedProperty currentPropBL, SerializedProperty defaultPropBR, SerializedProperty currentPropBR, SerializedProperty defaultPropCollapsedCorner, SerializedProperty currentPropCollapsedCorner, bool threeSided, float originalLabelWidth)
    {
        var controlRect = EditorGUILayout.GetControlRect(true, 2 + EditorGUIUtility.singleLineHeight * 2);
        var originalIndentLevel = EditorGUI.indentLevel;
        var indentWidth = EditorGUI.indentLevel * 15;
        EditorGUI.indentLevel = 0;
        controlRect.x += indentWidth;
        controlRect.width -= indentWidth;

        var titleSection = (EditorGUIUtility.labelWidth, 0f, 0f);
        var contentSection = (0f, 0f, 9999f);
        var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, controlRect, 0f, 0f, 0f, titleSection, contentSection);
        var titleRect = rectArr[0];
        titleRect.width -= 50;
        titleRect.height = EditorGUIUtility.singleLineHeight;

        EditorGUI.BeginProperty(titleRect, titleContent, currentProp);
        EditorGUI.LabelField(titleRect, titleContent);
        EditorGUI.EndProperty();
        var quickRect = titleRect;
        quickRect.y += EditorGUIUtility.singleLineHeight + 2f;
        var quickRects = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, quickRect, 2f, 0f, 0f, (0f, 35f, 40f), (0f, 35f, 40f));
        bool AllAt(float value) => !currentProp.hasMultipleDifferentValues && Mathf.Approximately(currentPropTL.floatValue, value) && Mathf.Approximately(currentPropTR.floatValue, value) && Mathf.Approximately(currentPropBL.floatValue, value) && Mathf.Approximately(currentPropBR.floatValue, value) && (!threeSided || !currentPropCollapsedCorner.hasMultipleDifferentValues && Mathf.Approximately(currentPropCollapsedCorner.floatValue, value));
        var wasEnabled = GUI.enabled;
        GUI.enabled &= !AllAt(min);
        var adjustment = GUI.Button(quickRects[0], $"-{quickStep:0.#}", EditorStyles.miniButtonLeft) ? -quickStep : 0f;
        GUI.enabled = wasEnabled && !AllAt(max);
        if (GUI.Button(quickRects[1], $"+{quickStep:0.#}", EditorStyles.miniButtonRight))
            adjustment = quickStep;
        GUI.enabled = wasEnabled;
        if (!Mathf.Approximately(adjustment, 0f))
        {
            float Adjust(float value) => Mathf.Clamp((adjustment < 0f && Mathf.Approximately(value, max) ? Mathf.Ceil(value) : value) + adjustment, min, max);
            currentPropTL.floatValue = Adjust(currentPropTL.floatValue);
            currentPropTR.floatValue = Adjust(currentPropTR.floatValue);
            currentPropBL.floatValue = Adjust(currentPropBL.floatValue);
            currentPropBR.floatValue = Adjust(currentPropBR.floatValue);
            if (threeSided)
                currentPropCollapsedCorner.floatValue = Adjust(currentPropCollapsedCorner.floatValue);
        }
        var cornerPropSection = (0f, 65f, 130f);

        var componentLabelWidth = 24f;
        var horOffset = indentWidth + componentLabelWidth;
        rectArr[1].x -= horOffset;
        rectArr[1].width += horOffset;

        rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Center, rectArr[1], 2f, 4f, 0, cornerPropSection, cornerPropSection);

        var (tl, tr, bl, br) = (rectArr[0], rectArr[1], rectArr[0], rectArr[1]);
        tl.height = tr.height = bl.height = br.height = EditorGUIUtility.singleLineHeight;
        bl.y += EditorGUIUtility.singleLineHeight + 2;
        br.y += EditorGUIUtility.singleLineHeight + 2;

        titleRect.width = Mathf.Min(titleRect.width, tl.x - titleRect.x - 5f);
        var scrubAmt = EditorHelpers.Scrub(titleRect);
        if (!Mathf.Approximately(scrubAmt, 0f))
        {
            var movement = scrubAmt * labelScrubSensitivity;
            currentPropTL.floatValue = MassageValueToDetent(currentPropTL.floatValue + movement, detents, min, max);
            currentPropTR.floatValue = MassageValueToDetent(currentPropTR.floatValue + movement, detents, min, max);
            currentPropBL.floatValue = MassageValueToDetent(currentPropBL.floatValue + movement, detents, min, max);
            currentPropBR.floatValue = MassageValueToDetent(currentPropBR.floatValue + movement, detents, min, max);
            if (threeSided)
                currentPropCollapsedCorner.floatValue = MassageValueToDetent(currentPropCollapsedCorner.floatValue + movement, detents, min, max);
        }

        EditorGUIUtility.labelWidth = 24;
        var collapsedEdgeIdx = collapsedEdgeProperty?.intValue ?? 0;
        if (threeSided)
        {
            if (collapsedEdgeIdx == 0) // Top
            {
                tl.x += (tr.x - tl.x) * 0.5f;
                DrawDetentedFloatFieldWithDefault(tl, defaultPropCollapsedCorner, currentPropCollapsedCorner, NDirectionContent, detents, min, max, shrinkField: false);
            }
            else if (collapsedEdgeIdx == 1) // Bottom
            {
                bl.x += (br.x - bl.x) * 0.5f;
                DrawDetentedFloatFieldWithDefault(bl, defaultPropCollapsedCorner, currentPropCollapsedCorner, SDirectionContent, detents, min, max, shrinkField: false);
            }
            else if (collapsedEdgeIdx == 2) // Left
            {
                bl.y += (tl.y - bl.y) * 0.5f;
                DrawDetentedFloatFieldWithDefault(bl, defaultPropCollapsedCorner, currentPropCollapsedCorner, WDirectionContent, detents, min, max, shrinkField: true);
            }
            else // Right
            {
                br.y += (tr.y - br.y) * 0.5f;
                DrawDetentedFloatFieldWithDefault(br, defaultPropCollapsedCorner, currentPropCollapsedCorner, EDirectionContent, detents, min, max, shrinkField: true);
            }

            // Hack fix that stops the Collapse Amount slider losing focus (mid-drag!) when it gets to, or changes from 1 (which changes the number of corners, and hence changes the layout prior to the slider)
            var fakedRect = new Rect(tr.x + 999999f, tr.y, 0, 0);
            EditorGUI.LabelField(fakedRect, "");
        }

        if (!threeSided || collapsedEdgeIdx != 0 && collapsedEdgeIdx != 2)
            DrawDetentedFloatFieldWithDefault(tl, defaultPropTL, currentPropTL, NWContent, detents, min, max, shrinkField: true);
        if (!threeSided || collapsedEdgeIdx != 0 && collapsedEdgeIdx != 3)
            DrawDetentedFloatFieldWithDefault(tr, defaultPropTR, currentPropTR, NEContent, detents, min, max, shrinkField: true);
        if (!threeSided || collapsedEdgeIdx != 1 && collapsedEdgeIdx != 2)
            DrawDetentedFloatFieldWithDefault(bl, defaultPropBL, currentPropBL, SWContent, detents, min, max, shrinkField: true);
        if (!threeSided || collapsedEdgeIdx != 1 && collapsedEdgeIdx != 3)
            DrawDetentedFloatFieldWithDefault(br, defaultPropBR, currentPropBR, SEContent, detents, min, max, shrinkField: true);

        EditorGUIUtility.labelWidth = originalLabelWidth;
        EditorGUI.indentLevel = originalIndentLevel;
    }

    private static void DrawDetentedFloatFieldWithDefault(SerializedProperty defaultProp, SerializedProperty currentProp, GUIContent content, int numDetents, float min, float max, bool disregardMiddleValue = false, bool shrinkField = false)
        => DrawDetentedFloatFieldWithDefault(EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight), defaultProp, currentProp, content, numDetents, min, max, disregardMiddleValue, shrinkField);

    private static void DrawDetentedFloatFieldWithDefault(Rect rect, SerializedProperty defaultProp, SerializedProperty currentProp, GUIContent content, int numDetents, float min, float max, bool disregardMiddleValue = false, bool shrinkField = false, bool forceRecheck = false, bool useFloatFieldMethods = false)
    {
        EditorGUI.BeginChangeCheck();
        
        if (useFloatFieldMethods)
            FloatFieldWithDefault(rect, defaultProp, currentProp, content, shrinkField);
        else
            PropertyFieldWithDefault(rect, defaultProp, currentProp, content, true, shrinkField);

        if (!EditorGUI.EndChangeCheck() && !forceRecheck || currentProp.hasMultipleDifferentValues || EditorGUI.showMixedValue)
            return;

        currentProp.floatValue = MassageValueToDetent(currentProp.floatValue, numDetents, min, max, disregardMiddleValue);
    }

    private static float MassageValueToDetent(float value, int numDetents, float min, float max, bool disregardMiddleValue = false)
    {
        if (disregardMiddleValue && Mathf.Approximately(value, (min + max) * 0.5f))
            return value;

        numDetents--;
        var step = (max - min) / numDetents;
        var intValue = Mathf.RoundToInt((value - min) / step);
        intValue = Mathf.Clamp(intValue, 0, numDetents);
        return min + intValue * step;
    }

    private static void PropertyFieldWithDefault(SerializedProperty defaultProp, SerializedProperty currentProp, GUIContent content, bool isFloat = false, bool shrinkField = false)
        => PropertyFieldWithDefault(EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight), defaultProp, currentProp, content, isFloat, shrinkField);

    private static void PropertyFieldWithDefault(Rect rect, SerializedProperty defaultProp, SerializedProperty currentProp, GUIContent content, bool isFloat = false, bool shrinkField = false)
    {
        var originalShowMixedValue = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue |= currentProp.hasMultipleDifferentValues;
        if (isFloat && Mathf.Approximately(defaultProp.floatValue, currentProp.floatValue) || !isFloat && Equals(defaultProp.boxedValue, currentProp.boxedValue))
        {
            EditorGUI.PropertyField(rect, currentProp, content);
            EditorGUI.showMixedValue = originalShowMixedValue;
            return;
        }

        EditorGUI.BeginProperty(rect, content, currentProp);

        var fieldRect = new Rect(rect.x, rect.y, rect.width - (shrinkField ? UndoButtonWidth : 0) - 2.5f, rect.height);
        var buttonRect = new Rect(fieldRect.xMax + 2.5f, rect.y, UndoButtonWidth, rect.height);

        var oldContentColor = GUI.contentColor;
        GUI.contentColor = new Color(0.8f, 0.9f, 1, 1f);
        EditorStyles.label.fontStyle = FontStyle.BoldAndItalic;
        EditorGUI.PropertyField(fieldRect, currentProp, content);

        EditorStyles.label.fontStyle = FontStyle.Normal;
        GUI.contentColor = oldContentColor;

        if (GUI.Button(buttonRect, "↻"))
        {
            currentProp.boxedValue = defaultProp.boxedValue;
            currentProp.serializedObject.ApplyModifiedProperties();
            currentProp.serializedObject.Update();
        }
        EditorGUI.EndProperty();
        EditorGUI.showMixedValue = originalShowMixedValue;
    }

    private static void FloatFieldWithDefault(SerializedProperty defaultProp, SerializedProperty currentProp, GUIContent content = null, bool shrinkField = false)
        => FloatFieldWithDefault(EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight), defaultProp, currentProp, content, shrinkField);

    private static void FloatFieldWithDefault(Rect rect, SerializedProperty defaultProp, SerializedProperty currentProp, GUIContent content = null, bool shrinkField = false)
    {
        var originalShowMixedValue = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue |= currentProp.hasMultipleDifferentValues;
        float newValue;
        if (Mathf.Approximately(defaultProp.floatValue, currentProp.floatValue))
        {
            newValue = EditorGUI.FloatField(rect, content ?? GUIContent.none, currentProp.floatValue);
            if (!Mathf.Approximately(currentProp.floatValue, newValue))
                currentProp.floatValue = newValue;

            EditorGUI.showMixedValue = originalShowMixedValue;
            return;
        }

        var fieldRect = new Rect(rect.x, rect.y, rect.width - (shrinkField ? UndoButtonWidth : 0) - 2.5f, rect.height);
        var buttonRect = new Rect(fieldRect.xMax + 2.5f, rect.y, UndoButtonWidth, rect.height);

        var oldContentColor = GUI.contentColor;
        GUI.contentColor = new Color(0.8f, 0.9f, 1, 1f);
        EditorStyles.label.fontStyle = FontStyle.BoldAndItalic;

        newValue = EditorGUI.FloatField(fieldRect, content ?? GUIContent.none, currentProp.floatValue);
        if (!Mathf.Approximately(currentProp.floatValue, newValue))
            currentProp.floatValue = newValue;

        EditorStyles.label.fontStyle = FontStyle.Normal;
        GUI.contentColor = oldContentColor;

        if (GUI.Button(buttonRect, "↻"))
        {
            currentProp.boxedValue = defaultProp.boxedValue;
            currentProp.serializedObject.ApplyModifiedProperties();
            currentProp.serializedObject.Update();
        }
        EditorGUI.showMixedValue = originalShowMixedValue;
    }

    private static void PropertyFieldWithDefaultSlider(SerializedProperty defaultProp, SerializedProperty currentProp, GUIContent content, float min, float max, bool shrinkField = false) 
        => PropertyFieldWithDefaultSlider(EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight), defaultProp, currentProp, content, min, max, shrinkField);

    private static void PropertyFieldWithDefaultSlider(Rect rect, SerializedProperty defaultProp, SerializedProperty currentProp, GUIContent content, float min, float max, bool shrinkField = false)
    {
        if (Equals(defaultProp.boxedValue, currentProp.boxedValue))
        {
            EditorGUI.Slider(rect, currentProp, min, max, content);
            return;
        }

        var fieldRect = new Rect(rect.x, rect.y, rect.width - (shrinkField ? UndoButtonWidth : 0) - 2.5f, rect.height);
        var buttonRect = new Rect(fieldRect.xMax + 2.5f, rect.y, UndoButtonWidth, rect.height);

        var oldContentColor = GUI.contentColor;
        GUI.contentColor = new Color(0.8f, 0.9f, 1, 1f);
        EditorStyles.label.fontStyle = FontStyle.BoldAndItalic;
        EditorGUI.Slider(fieldRect, currentProp, min, max, content);
        EditorStyles.label.fontStyle = FontStyle.Normal;
        GUI.contentColor = oldContentColor;

        if (GUI.Button(buttonRect, "↻"))
        {
            currentProp.boxedValue = defaultProp.boxedValue;
            currentProp.serializedObject.ApplyModifiedProperties();
            currentProp.serializedObject.Update();
        }
    }

    private static void DrawColorGrid(SerializedObject serializedObject, SerializedProperty dimensionsProp, SerializedProperty wrapModeXProp, SerializedProperty wrapModeYProp, SerializedProperty defaultGridProp, SerializedProperty currentGridProp, SerializedProperty defaultOffsetProp, SerializedProperty currentOffsetProp, SerializedProperty defaultRotationProp, SerializedProperty currentRotationProp,  SerializedProperty defaultScaleProp, SerializedProperty currentScaleProp, bool isDefaultProps, string advancedOptionsKey)
    {
        EditorGUILayout.Space(1);
        if (currentGridProp.arraySize != ProceduralProperties.Colors1dArrayLength)
            currentGridProp.arraySize = ProceduralProperties.Colors1dArrayLength;

        var originalLabelWidth = EditorGUIUtility.labelWidth;
        var originalIndentLevel = EditorGUI.indentLevel;
        var originalGUIColor = GUI.color;
        var indentWidth = EditorGUI.indentLevel * 15;
        EditorGUI.indentLevel = 0;

        var xProp = dimensionsProp.FindPropertyRelative("x");
        var yProp = dimensionsProp.FindPropertyRelative("y");
        var (gridSizeX, gridSizeY) = (xProp.intValue, yProp.intValue);

        var cRectHeight = gridSizeY > 1 ? 3 : 2;
        var controlRect = EditorGUILayout.GetControlRect(true, cRectHeight * EditorGUIUtility.singleLineHeight);
        controlRect.x += indentWidth;
        controlRect.width -= indentWidth;
        var dimensionLabelWidth = EditorStyles.label.CalcSize(ColorDimensionsContent).x;

        var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Center, controlRect, 12f, 12f, 0f, (dimensionLabelWidth, 0, 0), (controlRect.width - dimensionLabelWidth - 12f, 0, 0));
        var colorsGridRect = rectArr[1];
        EditorGUI.BeginChangeCheck();
        GUI.enabled = isDefaultProps;
        var dimensionRect = new Rect(rectArr[0]) { height = EditorGUIUtility.singleLineHeight };
        dimensionRect.y -= 2.5f;

        EditorGUI.BeginProperty(dimensionRect, ColorDimensionsContent, dimensionsProp);
        EditorGUI.LabelField(dimensionRect, ColorDimensionsContent);
        dimensionRect.y += dimensionRect.height;
        var (xyLabelWidth, xyFieldWidth) = (12.5f, 18f);
        rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, dimensionRect, 2f, 8f, 0f, (xyLabelWidth, xyFieldWidth, xyFieldWidth), (xyLabelWidth, xyFieldWidth, xyFieldWidth));
        EditorGUIUtility.labelWidth = xyLabelWidth;
        EditorGUI.PropertyField(rectArr[0], xProp);
        EditorGUI.PropertyField(rectArr[1], yProp);
        EditorGUI.EndProperty();

        if (gridSizeY == 3)
        {
            dimensionRect.x -= 8;
            dimensionRect.width += 16;
            dimensionRect.y += dimensionRect.height + 2f;
            dimensionRect.height = EditorGUIUtility.singleLineHeight * 1.1f;
            var oldState = SessionState.GetBool(advancedOptionsKey, false);
            GUI.color = SessionState.GetBool(advancedOptionsKey, false) ? Color.cyan : new Color(0.7f, 0.7f, 0.75f, 1f);

            var arrowChar = oldState ? '▴' : '▾';
            if (GUI.Button(dimensionRect, new GUIContent($"Advanced {arrowChar}"), oldState ? AdvancedButtonActiveStyle : GUI.skin.button))
                SessionState.SetBool(advancedOptionsKey, !oldState);

            GUI.color = originalGUIColor;
        }

        EditorGUIUtility.labelWidth = originalLabelWidth;

        GUI.enabled = true;
        if (EditorGUI.EndChangeCheck())
        {
            xProp.intValue = Mathf.Clamp(xProp.intValue, 1, ProceduralProperties.Colors2dArrayDimensionSize);
            yProp.intValue = Mathf.Clamp(yProp.intValue, 1, ProceduralProperties.Colors2dArrayDimensionSize);
        }

        var regionArr = new (float, float, float)[gridSizeX];
        var colorRegion = (0f, 50f, 999f);
        Array.Fill(regionArr, colorRegion);

        var rowHeight = colorsGridRect.height / 3f;
        colorsGridRect.height = rowHeight;
        if (gridSizeY == 1)
            colorsGridRect.y += gridSizeX == 1 ? rowHeight * (2/3f) : rowHeight * 0.125f;

        for (int y = 0; y < gridSizeY; y++)
        {
            EditorGUILayout.BeginHorizontal();
            rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Right, colorsGridRect, 5f, 10f, 0f, regionArr);
            colorsGridRect.y += rowHeight;

            for (int x = 0; x < gridSizeX; x++)
            {
                var spiralIdx = ProceduralProperties.GetColorSpiralIndex(x, y);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && rectArr[x].Contains(Event.current.mousePosition))
                {
                    var (capturedX, capturedY) = (x, y); // prevent closure
                    var menu = new GenericMenu();
                    menu.AddItem(CopyContent, false, () =>
                    {
                        colorCellClipBoard = currentGridProp.GetArrayElementAtIndex(spiralIdx).colorValue;
                    });
                    if (colorCellClipBoard.HasValue)
                    {
                        menu.AddItem(PasteContent, false, () =>
                        {
                            currentGridProp.GetArrayElementAtIndex(spiralIdx).colorValue = colorCellClipBoard.Value;
                            serializedObject.ApplyModifiedProperties();
                            serializedObject.Update();
                        });
                        menu.AddItem(PasteAllContent, false, () =>
                        {
                            for (int i = 0; i < ProceduralProperties.Colors1dArrayLength; i++)
                                currentGridProp.GetArrayElementAtIndex(i).colorValue = colorCellClipBoard.Value;

                            serializedObject.ApplyModifiedProperties();
                            serializedObject.Update();
                        });
                    }
                    else
                    {
                        menu.AddDisabledItem(PasteContent);
                        menu.AddDisabledItem(PasteAllContent);
                    }
                    if (gridSizeX > 1 || gridSizeY > 1)
                    {
                        menu.AddSeparator("");
                        AddStripItems("Column", gridSizeX > 1, true);
                        AddStripItems("Row", gridSizeY > 1, false);
                        if (gridSizeX > 1 && gridSizeY > 1)
                        {
                            menu.AddItem(new GUIContent("Copy Color Grid"), false, () => CopyColorGrid(dimensionsProp, currentGridProp));
                            if (colorClipBoard.dimensions.x > 1 && colorClipBoard.dimensions.y > 1)
                                menu.AddItem(new GUIContent("Paste Color Grid"), false, () => PasteColorGrid(serializedObject, dimensionsProp, currentGridProp));
                            else
                                menu.AddDisabledItem(new GUIContent("Paste Color Grid"));
                        }
                    }
                    menu.AddSeparator("");
                    menu.AddItem(LightenContent, false, () =>
                    {
                        var color = currentGridProp.GetArrayElementAtIndex(spiralIdx).colorValue;
                        currentGridProp.GetArrayElementAtIndex(spiralIdx).colorValue = Color.Lerp(color, new Color(1,1, 1, color.a), 0.1f);
                        serializedObject.ApplyModifiedProperties();
                        serializedObject.Update();
                    });
                    menu.AddItem(DarkenContent, false, () =>
                    {
                        var color = currentGridProp.GetArrayElementAtIndex(spiralIdx).colorValue;
                        currentGridProp.GetArrayElementAtIndex(spiralIdx).colorValue = Color.Lerp(color, new Color(0,0, 0, color.a), 0.1f);
                        serializedObject.ApplyModifiedProperties();
                        serializedObject.Update();
                    });
                    if (gridSizeX > 1 || gridSizeY > 1)
                    {
                        menu.AddSeparator("");
                        if (gridSizeX > 1)
                        {
                            menu.AddItem(new GUIContent("Invert Row"), false, () =>
                            {
                                for (int ix = 0; ix < gridSizeX / 2; ix++)
                                {
                                    var leftIndex = ProceduralProperties.GetColorSpiralIndex(ix, capturedY);
                                    var rightIndex = ProceduralProperties.GetColorSpiralIndex(gridSizeX - 1 - ix, capturedY);
                                    var temp = currentGridProp.GetArrayElementAtIndex(leftIndex).colorValue;
                                    currentGridProp.GetArrayElementAtIndex(leftIndex).colorValue = currentGridProp.GetArrayElementAtIndex(rightIndex).colorValue;
                                    currentGridProp.GetArrayElementAtIndex(rightIndex).colorValue = temp;
                                }
                                serializedObject.ApplyModifiedProperties();
                                serializedObject.Update();
                            });
                        }
                        if (gridSizeY > 1)
                        {
                            menu.AddItem(new GUIContent("Invert Column"), false, () =>
                            {
                                for (int iy = 0; iy < gridSizeY / 2; iy++)
                                {
                                    var topIndex = ProceduralProperties.GetColorSpiralIndex(capturedX, iy);
                                    var bottomIndex = ProceduralProperties.GetColorSpiralIndex(capturedX, gridSizeY - 1 - iy);
                                    var temp = currentGridProp.GetArrayElementAtIndex(topIndex).colorValue;
                                    currentGridProp.GetArrayElementAtIndex(topIndex).colorValue = currentGridProp.GetArrayElementAtIndex(bottomIndex).colorValue;
                                    currentGridProp.GetArrayElementAtIndex(bottomIndex).colorValue = temp;
                                }
                                serializedObject.ApplyModifiedProperties();
                                serializedObject.Update();
                            });
                        }
                    }

                    menu.ShowAsContext();
                    Event.current.Use();

                    void AddStripItems(string name, bool show, bool column)
                    {
                        if (!show) return;
                        var length = column ? gridSizeY : gridSizeX;
                        menu.AddItem(new GUIContent($"Copy {name}"), false, () =>
                        {
                            var colors = new Color[length];
                            for (int i = 0; i < length; i++)
                                colors[i] = currentGridProp.GetArrayElementAtIndex(ProceduralProperties.GetColorSpiralIndex(column ? capturedX : i, column ? i : capturedY)).colorValue;
                            if (column) colorColumnClipBoard = colors; else colorRowClipBoard = colors;
                        });
                        var clipBoard = column ? colorColumnClipBoard : colorRowClipBoard;
                        if (clipBoard?.Length == length)
                            menu.AddItem(new GUIContent($"Paste {name}"), false, () =>
                            {
                                for (int i = 0; i < length; i++)
                                    currentGridProp.GetArrayElementAtIndex(ProceduralProperties.GetColorSpiralIndex(column ? capturedX : i, column ? i : capturedY)).colorValue = clipBoard[i];
                                serializedObject.ApplyModifiedProperties();
                                serializedObject.Update();
                            });
                        else
                            menu.AddDisabledItem(new GUIContent($"Paste {name}"));
                    }
                }

                PropertyFieldWithDefault(rectArr[x], defaultGridProp.GetArrayElementAtIndex(spiralIdx), currentGridProp.GetArrayElementAtIndex(spiralIdx), GUIContent.none, shrinkField: true);
            }
            EditorGUILayout.EndHorizontal();
        }

        if (gridSizeX > 1 && gridSizeY != 3 || gridSizeY == 2)
        {
            colorsGridRect.height = EditorGUIUtility.singleLineHeight * 1.05f;

            if (gridSizeY == 1)
            {
                colorsGridRect.y += EditorGUIUtility.singleLineHeight * 0.4f;
            }
            else if (gridSizeY == 2)
            {
                var wDiff = controlRect.width - colorsGridRect.width + indentWidth;
                colorsGridRect.width += wDiff;
                colorsGridRect.x -= wDiff;
                colorsGridRect.y += 3f;
            }

            var oldState = SessionState.GetBool(advancedOptionsKey, false);
            GUI.color = SessionState.GetBool(advancedOptionsKey, false) ? Color.cyan : new Color(0.7f, 0.7f, 0.75f, 1f);

            var arrowChar = oldState ? '▴' : '▾';
            if (GUI.Button(colorsGridRect, new GUIContent($"Advanced {arrowChar}"), oldState ? AdvancedButtonActiveStyle : GUI.skin.button))
                SessionState.SetBool(advancedOptionsKey, !oldState);

            GUI.color = originalGUIColor;
        }

        if (gridSizeX == 1 && gridSizeY == 1 || !SessionState.GetBool(advancedOptionsKey, false))
        {
            EditorGUI.indentLevel = originalIndentLevel;
            EditorGUILayout.Space(1);
            return;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.Space(0.5f);
        controlRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight * 3 + 4);
        controlRect.x += 5;
        controlRect.width -= 10;
        controlRect.height = EditorGUIUtility.singleLineHeight;

        var leftColumnLabelWidth = 40f;
        var leftColumnFieldMin = 40f;
        var leftColumnFieldMax = 160f;
        var componentLabelWidth = 12f;
        var rotationLabelWidth = 52.5f;

        rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, EditorHelpers.FlexibleSpaceAllocation.LargestMinSizeFirst, controlRect, 4f, 32f, 0f, (0f, 100f, 408f), (rotationLabelWidth, 0f, 9999f));
        var secondaryRectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, EditorHelpers.FlexibleSpaceAllocation.Proportional, rectArr[0], 2f, 10f, 0f, (leftColumnLabelWidth, 0 ,0), (componentLabelWidth, leftColumnFieldMin, leftColumnFieldMax), (componentLabelWidth, leftColumnFieldMin, leftColumnFieldMax));
        EditorGUIUtility.labelWidth = leftColumnLabelWidth;
        GUI.enabled = isDefaultProps;
        EditorGUI.LabelField(secondaryRectArr[0], new GUIContent("Wrap"));
        EditorGUIUtility.labelWidth = componentLabelWidth;
        GUI.enabled = isDefaultProps && gridSizeX > 1;
        EditorGUI.PropertyField(secondaryRectArr[1], wrapModeXProp, XContent);
        GUI.enabled = isDefaultProps && gridSizeY > 1;
        EditorGUI.PropertyField(secondaryRectArr[2], wrapModeYProp, YContent);
        GUI.enabled = true;

        if (GUI.Button(rectArr[1], "Reset"))
        {
            if (isDefaultProps)
                wrapModeXProp.enumValueIndex = wrapModeYProp.enumValueIndex = 0;

            currentRotationProp.floatValue = 0;
            currentOffsetProp.vector2Value = Vector2.zero;
            currentScaleProp.vector2Value = Vector2.one;
        }

        rectArr[1].y += EditorGUIUtility.singleLineHeight + 5.5f;
        EditorGUIUtility.labelWidth = rectArr[1].width + 9999; // basically, just push the field off the edge of the screen (which we need to get a draggable label). I hope there's a nicer way to do this, but can't find one yet.
        EditorGUI.BeginProperty(rectArr[1], RotationContentNoTip, currentRotationProp);
        currentRotationProp.floatValue = EditorGUI.FloatField(rectArr[1], RotationContentNoTip, currentRotationProp.floatValue);
        rectArr[1].y += EditorGUIUtility.singleLineHeight - 1.5f;
        FloatFieldWithDefault(rectArr[1], defaultRotationProp, currentRotationProp, shrinkField: true);
        EditorGUI.EndProperty();

        secondaryRectArr[0].y += EditorGUIUtility.singleLineHeight + 2;
        secondaryRectArr[1].y += EditorGUIUtility.singleLineHeight + 2;
        secondaryRectArr[2].y += EditorGUIUtility.singleLineHeight + 2;
        var xOffsetPropDefault = defaultOffsetProp.FindPropertyRelative("x");
        var yOffsetPropDefault = defaultOffsetProp.FindPropertyRelative("y");
        var xOffsetPropCurrent = currentOffsetProp.FindPropertyRelative("x");
        var yOffsetPropCurrent = currentOffsetProp.FindPropertyRelative("y");
        EditorGUIUtility.labelWidth = leftColumnLabelWidth;

        EditorGUI.BeginProperty(secondaryRectArr[0], PosContent, currentOffsetProp);
        EditorGUI.LabelField(secondaryRectArr[0], PosContent);
        var scrubAmt = EditorHelpers.Scrub(secondaryRectArr[0]);
        if (!Mathf.Approximately(scrubAmt, 0f))
        {
            scrubAmt *= 0.025f;
            xOffsetPropCurrent.floatValue += scrubAmt;
            yOffsetPropCurrent.floatValue += scrubAmt;
        }
        EditorGUIUtility.labelWidth = componentLabelWidth;
        GUI.enabled = gridSizeX > 1;
        PropertyFieldWithDefault(secondaryRectArr[1], xOffsetPropDefault, xOffsetPropCurrent, XContent, true, true);
        GUI.enabled = gridSizeY > 1;
        PropertyFieldWithDefault(secondaryRectArr[2], yOffsetPropDefault, yOffsetPropCurrent, YContent, true, true);
        GUI.enabled = true;
        EditorGUI.EndProperty();

        secondaryRectArr[0].y += EditorGUIUtility.singleLineHeight + 2;
        secondaryRectArr[1].y += EditorGUIUtility.singleLineHeight + 2;
        secondaryRectArr[2].y += EditorGUIUtility.singleLineHeight + 2;
        var xScalePropDefault = defaultScaleProp.FindPropertyRelative("x");
        var yScalePropDefault = defaultScaleProp.FindPropertyRelative("y");
        var xScalePropCurrent = currentScaleProp.FindPropertyRelative("x");
        var yScalePropCurrent = currentScaleProp.FindPropertyRelative("y");
        EditorGUIUtility.labelWidth = leftColumnLabelWidth;

        EditorGUI.BeginProperty(secondaryRectArr[0], ScaleContent, currentScaleProp);
        EditorGUI.LabelField(secondaryRectArr[0], ScaleContent);
        scrubAmt = EditorHelpers.Scrub(secondaryRectArr[0]);
        if (!Mathf.Approximately(scrubAmt, 0f))
        {
            scrubAmt *= 0.025f;
            xScalePropCurrent.floatValue = Mathf.Max(xScalePropCurrent.floatValue + scrubAmt, 0.1f);
            yScalePropCurrent.floatValue = Mathf.Max(yScalePropCurrent.floatValue + scrubAmt, 0.1f);
        }
        EditorGUIUtility.labelWidth = componentLabelWidth;
        PropertyFieldWithDefault(secondaryRectArr[1], xScalePropDefault, xScalePropCurrent, XContent, true, true);
        xScalePropCurrent.floatValue = Mathf.Max(xScalePropCurrent.floatValue, 0.1f);
        PropertyFieldWithDefault(secondaryRectArr[2], yScalePropDefault, yScalePropCurrent, YContent, true, true);
        yScalePropCurrent.floatValue = Mathf.Max(yScalePropCurrent.floatValue, 0.1f);
        EditorGUI.EndProperty();

        EditorGUIUtility.labelWidth = originalLabelWidth;
        EditorGUILayout.Space(2);
        EditorGUILayout.EndVertical();
        EditorGUI.indentLevel = originalIndentLevel;
    }

    private static void ColorContextMenu(SerializedObject serializedObject, Rect titleRect, SerializedProperty colorArrayProp, SerializedProperty dimensionsProp)
    {
        if (Event.current.type != EventType.MouseDown || Event.current.button != 1 || !titleRect.Contains(Event.current.mousePosition))
            return;

        var menu = new GenericMenu();
        menu.AddItem(CopyContent, false, () => CopyColorGrid(dimensionsProp, colorArrayProp));
        if (colorClipBoard.dimensions != Vector2Int.zero)
            menu.AddItem(PasteContent, false, () => PasteColorGrid(serializedObject, dimensionsProp, colorArrayProp));
        else
            menu.AddDisabledItem(PasteContent);
        menu.AddSeparator("");
        AddColorAdjustmentItems(menu, serializedObject, colorArrayProp);
        menu.ShowAsContext();
        Event.current.Use();
    }

    private static void AddColorAdjustmentItems(GenericMenu menu, SerializedObject so, SerializedProperty colorsProp)
    {
        menu.AddItem(LightenContent, false, () => Adjust(Color.white));
        menu.AddItem(DarkenContent, false, () => Adjust(Color.black));
        void Adjust(Color target)
        {
            for (int i = 0; i < colorsProp.arraySize; i++)
            {
                var color = colorsProp.GetArrayElementAtIndex(i).colorValue;
                colorsProp.GetArrayElementAtIndex(i).colorValue = Color.Lerp(color, new Color(target.r, target.g, target.b, color.a), 0.1f);
            }
            so.ApplyModifiedProperties();
            so.Update();
        }
    }

    private static void CopyColorGrid(SerializedProperty dimensionsProp, SerializedProperty colorsProp)
    {
        var colors = new Color[colorsProp.arraySize];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = colorsProp.GetArrayElementAtIndex(i).colorValue;
        colorClipBoard = (dimensionsProp.vector2IntValue, colors);
    }

    private static void PasteColorGrid(SerializedObject so, SerializedProperty dimensionsProp, SerializedProperty colorsProp)
    {
        dimensionsProp.vector2IntValue = colorClipBoard.dimensions;
        colorsProp.arraySize = colorClipBoard.colorsArray.Length;
        for (int i = 0; i < colorsProp.arraySize; i++)
            colorsProp.GetArrayElementAtIndex(i).colorValue = colorClipBoard.colorsArray[i];
        so.ApplyModifiedProperties();
        so.Update();
    }

    private void CornersContextMenu(Rect rect, SerializedObject so)
    {
        if (Event.current.type != EventType.MouseDown || Event.current.button != 1 || !rect.Contains(Event.current.mousePosition))
            return;

        var menu = new GenericMenu();
        menu.AddItem(CopyContent, false, () => cornersClipBoard = (currentProps.cornerChamferProperty.vector4Value, currentProps.cornerConcavityProperty.vector4Value, currentProps.collapsedCornerChamferProperty?.floatValue, currentProps.collapsedCornerConcavityProperty?.floatValue, normalizeChamferProperty.boolValue, concavityIsSmoothingProperty.boolValue));
        if (cornersClipBoard.HasValue)
        {
            menu.AddItem(PasteContent, false, () =>
            {
                var values = cornersClipBoard.Value;
                currentProps.cornerChamferProperty.vector4Value = values.chamfer;
                currentProps.cornerConcavityProperty.vector4Value = values.concavity;
                if (values.collapsedChamfer.HasValue && currentProps.collapsedCornerChamferProperty != null) currentProps.collapsedCornerChamferProperty.floatValue = values.collapsedChamfer.Value;
                if (values.collapsedConcavity.HasValue && currentProps.collapsedCornerConcavityProperty != null) currentProps.collapsedCornerConcavityProperty.floatValue = values.collapsedConcavity.Value;
                normalizeChamferProperty.boolValue = values.normalize;
                concavityIsSmoothingProperty.boolValue = values.squircle;
                so.ApplyModifiedProperties();
                so.Update();
                DirtyTargets(so, FlexibleImage.AdvancedRaycastOptions.ChamferAndCollapse);
            });
        }
        else
            menu.AddDisabledItem(PasteContent);
        menu.ShowAsContext();
        Event.current.Use();
    }

    private void PropertyGroupContextMenu(string key, SerializedObject so, params SerializedProperty[] properties)
    {
        var flattened = new List<SerializedProperty>();
        foreach (var property in properties)
            if (property.isArray)
                for (int i = 0; i < property.arraySize; i++) flattened.Add(property.GetArrayElementAtIndex(i));
            else
                flattened.Add(property);

        var menu = new GenericMenu();
        menu.AddItem(CopyContent, false, () => propertyGroupClipBoards[key] = flattened.Select(property => property.boxedValue).ToArray());
        if (propertyGroupClipBoards.TryGetValue(key, out var values) && values.Length == flattened.Count && values.Select((value, i) => value?.GetType() == flattened[i].boxedValue?.GetType()).All(matches => matches))
            menu.AddItem(PasteContent, false, () =>
            {
                for (int i = 0; i < values.Length; i++) flattened[i].boxedValue = values[i];
                so.ApplyModifiedProperties();
                so.Update();
                DirtyTargets(so, (FlexibleImage.AdvancedRaycastOptions)(-1));
            });
        else
            menu.AddDisabledItem(PasteContent);
        menu.ShowAsContext();
        Event.current.Use();
    }

    private void ModuleContextMenu(Rect rect, SerializedObject so, Module module, SerializedProperty config, SerializedProperty colors = null)
    {
        if (Event.current.type != EventType.MouseDown || Event.current.button != 1 || !rect.Contains(Event.current.mousePosition))
            return;

        var moduleName = GetModuleName(module);
        var menu = new GenericMenu();
        menu.AddItem(CopyContent, false, () => moduleClipBoards[module] = (JsonUtility.ToJson(config.boxedValue), quadData.proceduralAnimationStates.Select(state => state.proceduralProperties.Select(props => JsonUtility.ToJson(GetModuleAnimation(props, module))).ToArray()).ToArray()));
        if (moduleClipBoards.TryGetValue(module, out var clipBoard))
        {
            menu.AddItem(PasteContent, false, () =>
            {
                so.ApplyModifiedProperties();
                Undo.RecordObjects(so.targetObjects, $"Paste {moduleName}");
                config.boxedValue = JsonUtility.FromJson(clipBoard.config, config.boxedValue.GetType());
                so.ApplyModifiedProperties();
                foreach (var target in so.targetObjects)
                {
                    var targetQuadData = GetTargetQuadData(target);
                    if (targetQuadData == null) continue;
                    for (int i = 0; i < targetQuadData.proceduralAnimationStates.Length; i++)
                    {
                        var sourceState = clipBoard.animation[Mathf.Min(i, clipBoard.animation.Length - 1)];
                        if (sourceState.Length == 0) sourceState = clipBoard.animation[0];
                        var props = targetQuadData.proceduralAnimationStates[i].proceduralProperties;
                        for (int j = 0; j < props.Count; j++)
                        {
                            var json = sourceState[Mathf.Min(j, sourceState.Length - 1)];
                            JsonUtility.FromJsonOverwrite(json, GetModuleAnimation(props[j], module));
                        }
                    }
                }
                RefreshModuleProperties(so);
                DirtyTargets(so, (FlexibleImage.AdvancedRaycastOptions)(-1));
            });
        }
        else
            menu.AddDisabledItem(PasteContent);
        if (colors != null)
        {
            menu.AddSeparator("");
            AddColorAdjustmentItems(menu, so, colors);
        }
        menu.AddSeparator("");
        menu.AddItem(new GUIContent($"Remove {moduleName}"), false, () => UpdateModules(so, $"Remove {moduleName}", data => SetModule(data, module, false)));
        menu.ShowAsContext();
        Event.current.Use();
    }

    private static void Vec2SlidersWithDefault(SerializedProperty defaultProp, SerializedProperty currentProp, float min, float max, string labelStr, float labelWidth, float propLabelWidth, string firstPropStr, string secondPropStr, Rect? controlRectOverride = null) =>
        DoubleSlidersWithDefaults(defaultProp.FindPropertyRelative("x"), defaultProp.FindPropertyRelative("y"), currentProp.FindPropertyRelative("x"), currentProp.FindPropertyRelative("y"), min, max, labelStr, labelWidth, propLabelWidth, firstPropStr, secondPropStr, controlRectOverride);

    private static void Vec2FieldsWithDefault(SerializedProperty defaultProp, SerializedProperty currentProp, string labelStr, float labelWidth, float propLabelWidth, string firstPropStr, string secondPropStr, Rect? controlRectOverride = null) =>
        DoubleFieldsWithDefaults(defaultProp.FindPropertyRelative("x"), defaultProp.FindPropertyRelative("y"), currentProp.FindPropertyRelative("x"), currentProp.FindPropertyRelative("y"), labelStr, labelWidth, propLabelWidth, firstPropStr, secondPropStr, controlRectOverride);

    private static void DoubleFieldsWithDefaults(SerializedProperty defaultProp1, SerializedProperty defaultProp2, SerializedProperty currentProp1, SerializedProperty currentProp2, string labelStr, float labelWidth, float propLabelWidth, string firstPropStr, string secondPropStr, Rect? controlRectOverride = null)
    {
        var originalLabelWidth = EditorGUIUtility.labelWidth;
        var originalIndentLevel = EditorGUI.indentLevel;
        var indentWidth = EditorGUI.indentLevel * 15;
        labelWidth -= indentWidth;
        EditorGUI.indentLevel = 0;
        var controlRect = controlRectOverride ?? EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        controlRect.x += indentWidth;
        controlRect.width -= indentWidth;

        var labelSection = (labelWidth, 0f, 0f);
        var xySection = (propLabelWidth, 34f, 999f);
        var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, controlRect, 8f, 999f, 0f, labelSection, xySection, xySection);

        EditorGUIUtility.labelWidth = labelWidth;
        if (!Equals(defaultProp1.boxedValue, currentProp1.boxedValue) || !Equals(defaultProp2.boxedValue, currentProp2.boxedValue))
        {
            var oldContentColor = GUI.contentColor;
            GUI.contentColor = new Color(0.8f, 0.9f, 1, 1f);
            EditorStyles.label.fontStyle = FontStyle.BoldAndItalic;
            EditorGUI.LabelField(rectArr[0], labelStr);
            EditorStyles.label.fontStyle = FontStyle.Normal;
            GUI.contentColor = oldContentColor;
        }
        else
        {
            EditorGUI.LabelField(rectArr[0], labelStr);
        }

        var scrubAmt = EditorHelpers.Scrub(rectArr[0]);
        if (!Mathf.Approximately(scrubAmt, 0f))
        {
            scrubAmt *= 0.25f;
            currentProp1.floatValue += scrubAmt;
            currentProp2.floatValue += scrubAmt;
        }

        EditorGUIUtility.labelWidth = 10;
        FloatFieldWithDefault(rectArr[1], defaultProp1, currentProp1, new GUIContent(firstPropStr), true);
        FloatFieldWithDefault(rectArr[2], defaultProp2, currentProp2, new GUIContent(secondPropStr), true);
        EditorGUI.indentLevel = originalIndentLevel;
        EditorGUIUtility.labelWidth = originalLabelWidth;
    }

    private static void DoubleSlidersWithDefaults(SerializedProperty defaultProp1, SerializedProperty defaultProp2, SerializedProperty currentProp1, SerializedProperty currentProp2, float min, float max, string labelStr, float labelWidth, float propLabelWidth, string firstPropStr, string secondPropStr, Rect? controlRectOverride = null)
    {
        var originalLabelWidth = EditorGUIUtility.labelWidth;
        var originalIndentLevel = EditorGUI.indentLevel;
        var indentWidth = EditorGUI.indentLevel * 15;
        labelWidth -= indentWidth;
        EditorGUI.indentLevel = 0;
        var controlRect = controlRectOverride ?? EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        controlRect.x += indentWidth;
        controlRect.width -= indentWidth;

        var labelSection = (labelWidth, 0f, 0f);
        var xySection = (propLabelWidth, 34f, 999f);
        var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, controlRect, 8f, 999f, 0f, labelSection, xySection, xySection);

        EditorGUIUtility.labelWidth = labelWidth;
        if (!Equals(defaultProp1.boxedValue, currentProp1.boxedValue) || !Equals(defaultProp2.boxedValue, currentProp2.boxedValue))
        {
            var oldContentColor = GUI.contentColor;
            GUI.contentColor = new Color(0.8f, 0.9f, 1, 1f);
            EditorStyles.label.fontStyle = FontStyle.BoldAndItalic;
            EditorGUI.LabelField(rectArr[0], labelStr);
            EditorStyles.label.fontStyle = FontStyle.Normal;
            GUI.contentColor = oldContentColor;
        }
        else
        {
            EditorGUI.LabelField(rectArr[0], labelStr);
        }

        var scrubAmt = EditorHelpers.Scrub(rectArr[0]);
        if (!Mathf.Approximately(scrubAmt, 0f))
        {
            scrubAmt *= 0.01f;
            currentProp1.floatValue = Mathf.Clamp(currentProp1.floatValue + scrubAmt, min, max);
            currentProp2.floatValue = Mathf.Clamp(currentProp2.floatValue + scrubAmt, min, max);
        }

        EditorGUIUtility.labelWidth = 10;
        PropertyFieldWithDefaultSlider(rectArr[1], defaultProp1, currentProp1, new GUIContent(firstPropStr), min, max, true);
        PropertyFieldWithDefaultSlider(rectArr[2], defaultProp2, currentProp2, new GUIContent(secondPropStr), min, max, true);
        EditorGUI.indentLevel = originalIndentLevel;
        EditorGUIUtility.labelWidth = originalLabelWidth;
    }

    private static int minFieldID, maxFieldID;
    private static void MinMaxSliderWithDefaults(SerializedProperty defaultMin, SerializedProperty defaultMax, SerializedProperty currentMin, SerializedProperty currentMax, float minLimit, float maxLimit, GUIContent labelContent, float labelWidth, float propLabelWidth, float fieldWidth, string firstPropStr, string secondPropStr, int numDetents, float min, float max)
    {
        var originalShowMixedValue = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = currentMin.hasMultipleDifferentValues || currentMax.hasMultipleDifferentValues;
        var originalLabelWidth = EditorGUIUtility.labelWidth;
        var originalIndentLevel = EditorGUI.indentLevel;
        var indentWidth = EditorGUI.indentLevel * 15;
        labelWidth -= indentWidth;

        EditorGUI.indentLevel = 0;
        var controlRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        controlRect.x += indentWidth;
        controlRect.width -= indentWidth;

        var labelSection = (labelWidth, 0f, 0f);
        var remainingSection = (0f, 0f, 9999f);
        var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, controlRect, 0f, 0f, 0f, labelSection, remainingSection);

        EditorGUIUtility.labelWidth = labelWidth;

        var minsEqual = Mathf.Approximately(defaultMin.floatValue, currentMin.floatValue);
        var maxesEqual = Mathf.Approximately(defaultMax.floatValue, currentMax.floatValue);

        var oldContentColor = GUI.contentColor;
        if (!minsEqual || !maxesEqual)
        {
            GUI.contentColor = new Color(0.8f, 0.9f, 1, 1f);
            EditorStyles.label.fontStyle = FontStyle.BoldAndItalic;
        }

        EditorGUI.LabelField(rectArr[0], labelContent);

        rectArr[0].width -= 5;
        var scrubAmt = EditorHelpers.Scrub(rectArr[0]);
        if (!Mathf.Approximately(scrubAmt, 0f))
        {
            scrubAmt *= 0.25f;
            currentMin.floatValue = MassageValueToDetent(currentMin.floatValue + scrubAmt, numDetents, min, max);
            currentMax.floatValue = MassageValueToDetent(currentMax.floatValue + scrubAmt, numDetents, min, max);
        }

        EditorStyles.label.fontStyle = FontStyle.Normal;
        GUI.contentColor = oldContentColor;

        var firstFieldSection = (fieldWidth + UndoButtonWidth, 0f, 0f);
        var secondFieldSection = (fieldWidth + UndoButtonWidth * 0.5f + (maxesEqual ? 0f : UndoButtonWidth), 0f, 0f);

        var minMaxSliderSection = (0f, 25f, 9999f); 
        rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, rectArr[1], 2f, 2f, 0f, firstFieldSection, minMaxSliderSection, secondFieldSection);
        var (currentMinFloat, currentMaxFloat) = (currentMin.floatValue, currentMax.floatValue);
        EditorGUI.MinMaxSlider(rectArr[1], GUIContent.none, ref currentMinFloat, ref currentMaxFloat, minLimit, maxLimit);

        if (!EditorGUI.showMixedValue)
            (currentMin.floatValue, currentMax.floatValue) = (currentMinFloat, currentMaxFloat);

        EditorGUIUtility.labelWidth = propLabelWidth;

        rectArr[2].x += UndoButtonWidth * 0.5f;
        rectArr[2].width -= UndoButtonWidth * 0.5f;
        if (minsEqual)
            rectArr[0].width -= UndoButtonWidth;

        DrawDetentedFloatFieldWithDefault(rectArr[0], defaultMin, currentMin, new GUIContent(firstPropStr), numDetents, min, max, shrinkField: true, forceRecheck: true);
        DrawDetentedFloatFieldWithDefault(rectArr[2], defaultMax, currentMax, new GUIContent(secondPropStr), numDetents, min, max, shrinkField: true, forceRecheck: true);

        if (!EditorGUI.showMixedValue)
        {
            var focusedControlID = GUIUtility.keyboardControl;

            // Unity will change the focusControlID to negative something while typing, so we need to check for that
            var newMinFieldID = GUIUtility.GetControlID(FocusType.Keyboard, rectArr[0]) - 2;
            if (newMinFieldID >= 0)
                minFieldID = newMinFieldID;

            var newMaxFieldID = GUIUtility.GetControlID(FocusType.Keyboard, rectArr[2]) - 2;
            if (newMaxFieldID >= 0)
                maxFieldID = newMaxFieldID;

            // Editing min can push max, but editing max should only push min after the max field loses focus
            if (focusedControlID == minFieldID)
            {
                if (currentMin.floatValue > currentMax.floatValue)
                    currentMax.floatValue = currentMin.floatValue;
            }
            // *Unless* we're scrubbing the max value, in which case, push away!
            else if (focusedControlID == maxFieldID && GUIUtility.hotControl == maxFieldID)
            {
                if (currentMax.floatValue < currentMin.floatValue)
                    currentMin.floatValue = currentMax.floatValue;
            }
            else if (focusedControlID != maxFieldID)
            {
                currentMin.floatValue = Mathf.Min(currentMin.floatValue, currentMax.floatValue);
                currentMax.floatValue = Mathf.Max(currentMax.floatValue, currentMin.floatValue);
            }
        }

        EditorGUI.indentLevel = originalIndentLevel;
        EditorGUIUtility.labelWidth = originalLabelWidth;
        EditorGUI.showMixedValue = originalShowMixedValue;
    }

    private void ShapeQuickActionsMenu(SerializedObject so, FlexibleImage flexibleImage)
    {
        var menu = new GenericMenu();

        var (oneEighth, oneQuarter, threeEighths, oneHalf, fiveEighths, threeQuarters ) = (12.5f, 25f, 37.5f, 50f, 62.5f, 75f);
        if (flexibleImage != null)
        {
            var minSide = Mathf.Min(flexibleImage.rectTransform.rect.width, flexibleImage.rectTransform.rect.height);
            oneEighth = MassageValueToDetent(minSide * 0.125f, 65536, 0f, 4095.9375f);
            oneQuarter = MassageValueToDetent(minSide * 0.25f, 65536, 0f, 4095.9375f);
            threeEighths = MassageValueToDetent(minSide * 0.375f, 65536, 0f, 4095.9375f);
            oneHalf = MassageValueToDetent(minSide * 0.5f, 65536, 0f, 4095.9375f);
            fiveEighths = MassageValueToDetent(minSide * 0.625f, 65536, 0f, 4095.9375f);
            threeQuarters = MassageValueToDetent(minSide * 0.75f, 65536, 0f, 4095.9375f);
        }

        menu.AddItem(new GUIContent("Rectangle"), false, () =>
        {
            currentProps.cornerChamferProperty.vector4Value = Vector4.zero;
            if (currentProps.collapsedCornerChamferProperty != null) currentProps.collapsedCornerChamferProperty.floatValue = 0f;
            if (currentProps.collapseEdgeAmountProperty != null) currentProps.collapseEdgeAmountProperty.floatValue = 0f;
            if (currentProps.collapseEdgeAmountAbsoluteProperty != null) currentProps.collapseEdgeAmountAbsoluteProperty.floatValue = 0f;
            so.ApplyModifiedProperties();
            so.Update();
        });
        menu.AddItem(new GUIContent("Circle"), false, () =>
        {
            currentProps.cornerChamferProperty.vector4Value = Vector4.one * 4095.9375f;
            if (currentProps.collapsedCornerChamferProperty != null) currentProps.collapsedCornerChamferProperty.floatValue = 4095.9375f;
            currentProps.cornerConcavityProperty.vector4Value = Vector4.zero;
            if (currentProps.collapsedCornerConcavityProperty != null) currentProps.collapsedCornerConcavityProperty.floatValue = 0f;
            if (currentProps.collapseEdgeAmountProperty != null) currentProps.collapseEdgeAmountProperty.floatValue = 0f;
            if (currentProps.collapseEdgeAmountAbsoluteProperty != null) currentProps.collapseEdgeAmountAbsoluteProperty.floatValue = 0f;
            normalizeChamferProperty.boolValue = true;
            so.ApplyModifiedProperties();
            so.Update();
        });
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Square Corners"), false, () =>
        {
            currentProps.cornerChamferProperty.vector4Value = Vector4.zero;
            if (currentProps.collapsedCornerChamferProperty != null) currentProps.collapsedCornerChamferProperty.floatValue = 0f;
            so.ApplyModifiedProperties();
            so.Update();
        });
        menu.AddItem(new GUIContent("Round Corners/12.5%"), false, () => RoundedCornersCommon(oneEighth));
        menu.AddItem(new GUIContent("Round Corners/25%"),   false, () => RoundedCornersCommon(oneQuarter));
        menu.AddItem(new GUIContent("Round Corners/37.5%"), false, () => RoundedCornersCommon(threeEighths));
        menu.AddItem(new GUIContent("Sharp Corners/12.5%"), false, () => SharpCornersCommon(oneEighth));
        menu.AddItem(new GUIContent("Sharp Corners/25%"),   false, () => SharpCornersCommon(oneQuarter));
        menu.AddItem(new GUIContent("Sharp Corners/37.5%"), false, () => SharpCornersCommon(threeEighths));
        menu.AddItem(new GUIContent("Squircle/12.5%"),      false, () => SquircleCommon(oneEighth));
        menu.AddItem(new GUIContent("Squircle/25%"),        false, () => SquircleCommon(oneQuarter));
        menu.AddItem(new GUIContent("Squircle/37.5%"),      false, () => SquircleCommon(threeEighths));
        menu.AddSeparator("");
        if (AllTargetsHaveModule(so, data => data.HasSkew))
        {
            menu.AddItem(new GUIContent("No Collapse"), false, () =>
            {
                currentProps.collapseEdgeAmountAbsoluteProperty.floatValue = currentProps.collapseEdgeAmountProperty.floatValue = 0f;
                so.ApplyModifiedProperties();
                so.Update();
            });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("No Collapse"));
        }
        menu.AddItem(new GUIContent("Triangle/Top/Left"),      false, () => TriangleCommon(QuadData.CollapsedEdgeType.Top, 0f));
        menu.AddItem(new GUIContent("Triangle/Top/Middle"),    false, () => TriangleCommon(QuadData.CollapsedEdgeType.Top, 0.5f));
        menu.AddItem(new GUIContent("Triangle/Top/Right"),     false, () => TriangleCommon(QuadData.CollapsedEdgeType.Top, 1f));
        menu.AddItem(new GUIContent("Triangle/Bottom/Left"),   false, () => TriangleCommon(QuadData.CollapsedEdgeType.Bottom, 0f));
        menu.AddItem(new GUIContent("Triangle/Bottom/Middle"), false, () => TriangleCommon(QuadData.CollapsedEdgeType.Bottom, 0.5f));
        menu.AddItem(new GUIContent("Triangle/Bottom/Right"),  false, () => TriangleCommon(QuadData.CollapsedEdgeType.Bottom, 1f));
        menu.AddItem(new GUIContent("Triangle/Left/Top"),      false, () => TriangleCommon(QuadData.CollapsedEdgeType.Left, 1f));
        menu.AddItem(new GUIContent("Triangle/Left/Middle"),   false, () => TriangleCommon(QuadData.CollapsedEdgeType.Left, 0.5f));
        menu.AddItem(new GUIContent("Triangle/Left/Bottom"),   false, () => TriangleCommon(QuadData.CollapsedEdgeType.Left, 0f));
        menu.AddItem(new GUIContent("Triangle/Right/Top"),     false, () => TriangleCommon(QuadData.CollapsedEdgeType.Right, 1f));
        menu.AddItem(new GUIContent("Triangle/Right/Middle"),  false, () => TriangleCommon(QuadData.CollapsedEdgeType.Right, 0.5f));
        menu.AddItem(new GUIContent("Triangle/Right/Bottom"),  false, () => TriangleCommon(QuadData.CollapsedEdgeType.Right, 0f));

        menu.AddItem(new GUIContent("Trapezoid/Top/12.5%"),    false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Top, 0.125f, oneEighth));
        menu.AddItem(new GUIContent("Trapezoid/Top/25%"),      false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Top, 0.25f, oneQuarter));
        menu.AddItem(new GUIContent("Trapezoid/Top/37.5%"),    false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Top,0.375f, threeEighths));
        menu.AddItem(new GUIContent("Trapezoid/Top/50%"),      false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Top,0.5f, oneHalf));
        menu.AddItem(new GUIContent("Trapezoid/Top/62.5%"),    false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Top,0.625f, fiveEighths));
        menu.AddItem(new GUIContent("Trapezoid/Top/75%"),      false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Top,0.75f, threeQuarters));
        menu.AddItem(new GUIContent("Trapezoid/Bottom/12.5%"), false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Bottom, 0.125f, oneEighth));
        menu.AddItem(new GUIContent("Trapezoid/Bottom/25%"),   false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Bottom, 0.25f, oneQuarter));
        menu.AddItem(new GUIContent("Trapezoid/Bottom/37.5%"), false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Bottom,0.375f, threeEighths));
        menu.AddItem(new GUIContent("Trapezoid/Bottom/50%"),   false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Bottom,0.5f, oneHalf));
        menu.AddItem(new GUIContent("Trapezoid/Bottom/62.5%"), false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Bottom,0.625f, fiveEighths));
        menu.AddItem(new GUIContent("Trapezoid/Bottom/75%"),   false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Bottom,0.75f, threeQuarters));
        menu.AddItem(new GUIContent("Trapezoid/Left/12.5%"),   false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Left, 0.125f, oneEighth));
        menu.AddItem(new GUIContent("Trapezoid/Left/25%"),     false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Left, 0.25f, oneQuarter));
        menu.AddItem(new GUIContent("Trapezoid/Left/37.5%"),   false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Left,0.375f, threeEighths));
        menu.AddItem(new GUIContent("Trapezoid/Left/50%"),     false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Left,0.5f, oneHalf));
        menu.AddItem(new GUIContent("Trapezoid/Left/62.5%"),   false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Left,0.625f, fiveEighths));
        menu.AddItem(new GUIContent("Trapezoid/Left/75%"),     false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Left,0.75f, threeQuarters));
        menu.AddItem(new GUIContent("Trapezoid/Right/12.5%"),  false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Right, 0.125f, oneEighth));
        menu.AddItem(new GUIContent("Trapezoid/Right/25%"),    false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Right, 0.25f, oneQuarter));
        menu.AddItem(new GUIContent("Trapezoid/Right/37.5%"),  false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Right,0.375f, threeEighths));
        menu.AddItem(new GUIContent("Trapezoid/Right/50%"),    false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Right,0.5f, oneHalf));
        menu.AddItem(new GUIContent("Trapezoid/Right/62.5%"),  false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Right,0.625f, fiveEighths));
        menu.AddItem(new GUIContent("Trapezoid/Right/75%"),    false, () => TrapezoidCommon(QuadData.CollapsedEdgeType.Right,0.75f, threeQuarters));

        menu.AddItem(new GUIContent("Parallelogram/Vertical Left->Right/12.5%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, true, 0.125f, oneEighth));
        menu.AddItem(new GUIContent("Parallelogram/Vertical Left->Right/25%"),     false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, true, 0.25f, oneQuarter));
        menu.AddItem(new GUIContent("Parallelogram/Vertical Left->Right/37.5%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, true, 0.375f, threeEighths));
        menu.AddItem(new GUIContent("Parallelogram/Vertical Left->Right/50%"),     false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, true, 0.5f, oneHalf));
        menu.AddItem(new GUIContent("Parallelogram/Vertical Left->Right/62.5%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, true, 0.625f, fiveEighths));
        menu.AddItem(new GUIContent("Parallelogram/Vertical Left->Right/75%"),     false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, true, 0.75f, threeQuarters));
        menu.AddItem(new GUIContent("Parallelogram/Vertical Right->Left/12.5%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, false, 0.125f, oneEighth));
        menu.AddItem(new GUIContent("Parallelogram/Vertical Right->Left/25%"),     false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, false, 0.25f, oneQuarter));
        menu.AddItem(new GUIContent("Parallelogram/Vertical Right->Left/37.5%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, false, 0.375f, threeEighths));
        menu.AddItem(new GUIContent("Parallelogram/Vertical Right->Left/50%"),     false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, false, 0.5f, oneHalf));
        menu.AddItem(new GUIContent("Parallelogram/Vertical Right->Left/62.5%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, false, 0.625f, fiveEighths));
        menu.AddItem(new GUIContent("Parallelogram/Vertical Right->Left/75%"),     false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, false, 0.75f, threeQuarters));
        menu.AddItem(new GUIContent("Parallelogram/Horizontal Bottom->Top/12.5%"), false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, true, 0.125f, oneEighth));
        menu.AddItem(new GUIContent("Parallelogram/Horizontal Bottom->Top/25%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, true, 0.25f, oneQuarter));
        menu.AddItem(new GUIContent("Parallelogram/Horizontal Bottom->Top/37.5%"), false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, true, 0.375f, threeEighths));
        menu.AddItem(new GUIContent("Parallelogram/Horizontal Bottom->Top/50%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, true, 0.5f, oneHalf));
        menu.AddItem(new GUIContent("Parallelogram/Horizontal Bottom->Top/62.5%"), false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, true, 0.625f, fiveEighths));
        menu.AddItem(new GUIContent("Parallelogram/Horizontal Bottom->Top/75%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, true, 0.75f, threeQuarters));
        menu.AddItem(new GUIContent("Parallelogram/Horizontal Top->Bottom/12.5%"), false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, false, 0.125f, oneEighth));
        menu.AddItem(new GUIContent("Parallelogram/Horizontal Top->Bottom/25%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, false, 0.25f, oneQuarter));
        menu.AddItem(new GUIContent("Parallelogram/Horizontal Top->Bottom/37.5%"), false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, false, 0.375f, threeEighths));
        menu.AddItem(new GUIContent("Parallelogram/Horizontal Top->Bottom/50%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, false, 0.5f, oneHalf));
        menu.AddItem(new GUIContent("Parallelogram/Horizontal Top->Bottom/62.5%"), false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, false, 0.625f, fiveEighths));
        menu.AddItem(new GUIContent("Parallelogram/Horizontal Top->Bottom/75%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, false, 0.75f, threeQuarters));

        menu.AddItem(new GUIContent("Chevron/Pointing Right/12.5%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, false, 0.125f, oneEighth, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Right/25%"),     false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, false, 0.25f, oneQuarter, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Right/37.5%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, false, 0.375f, threeEighths, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Right/50%"),     false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, false, 0.5f, oneHalf, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Right/62.5%"),   false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, false, 0.625f, fiveEighths, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Right/75%"),     false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, false, 0.75f, threeQuarters, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Left/12.5%"),    false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, true, 0.125f, oneEighth, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Left/25%"),      false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, true, 0.25f, oneQuarter, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Left/37.5%"),    false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, true, 0.375f, threeEighths, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Left/50%"),      false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, true, 0.5f, oneHalf, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Left/62.5%"),    false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, true, 0.625f, fiveEighths, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Left/75%"),      false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Top, true, 0.75f, threeQuarters, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Up/12.5%"),      false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, true, 0.125f, oneEighth, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Up/25%"),        false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, true, 0.25f, oneQuarter, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Up/37.5%"),      false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, true, 0.375f, threeEighths, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Up/50%"),        false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, true, 0.5f, oneHalf, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Up/62.5%"),      false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, true, 0.625f, fiveEighths, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Up/75%"),        false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, true, 0.75f, threeQuarters, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Down/12.5%"),    false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, false, 0.125f, oneEighth, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Down/25%"),      false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, false, 0.25f, oneQuarter, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Down/37.5%"),    false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, false, 0.375f, threeEighths, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Down/50%"),      false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, false, 0.5f, oneHalf, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Down/62.5%"),    false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, false, 0.625f, fiveEighths, true));
        menu.AddItem(new GUIContent("Chevron/Pointing Down/75%"),      false, () => ParallelogramCommon(QuadData.CollapsedEdgeType.Right, false, 0.75f, threeQuarters, true));

        menu.ShowAsContext();
        Event.current.Use();

        void RoundedCornersCommon(float chamfer)
        {
            currentProps.cornerChamferProperty.vector4Value = Vector4.one * chamfer;
            if (currentProps.collapsedCornerChamferProperty != null) currentProps.collapsedCornerChamferProperty.floatValue = chamfer;
            currentProps.cornerConcavityProperty.vector4Value = Vector4.zero;
            if (currentProps.collapsedCornerConcavityProperty != null) currentProps.collapsedCornerConcavityProperty.floatValue = 0f;
            so.ApplyModifiedProperties();
            so.Update();
        }
        
        void SharpCornersCommon(float chamfer)
        {
            concavityIsSmoothingProperty.boolValue = false;
            currentProps.cornerChamferProperty.vector4Value = Vector4.one * chamfer;
            if (currentProps.collapsedCornerChamferProperty != null) currentProps.collapsedCornerChamferProperty.floatValue = chamfer;
            currentProps.cornerConcavityProperty.vector4Value = Vector4.one;
            if (currentProps.collapsedCornerConcavityProperty != null) currentProps.collapsedCornerConcavityProperty.floatValue = 1f;
            so.ApplyModifiedProperties();
            so.Update();
        }

        void SquircleCommon(float chamfer)
        {
            concavityIsSmoothingProperty.boolValue = true;
            currentProps.cornerChamferProperty.vector4Value = Vector4.one * chamfer;
            if (currentProps.collapsedCornerChamferProperty != null) currentProps.collapsedCornerChamferProperty.floatValue = chamfer;
            currentProps.cornerConcavityProperty.vector4Value = Vector4.one * 0.4f;
            if (currentProps.collapsedCornerConcavityProperty != null) currentProps.collapsedCornerConcavityProperty.floatValue = 0.4f;
            so.ApplyModifiedProperties();
            so.Update();
        }

        void TrapezoidCommon(QuadData.CollapsedEdgeType edge, float relative, float absolute)
        {
            EnsureSkew();
            collapsedEdgeProperty.enumValueIndex = (int)edge;
            collapseIntoParallelogramProperty.boolValue = false;
            mirrorCollapseProperty.boolValue = false;
            currentProps.collapseEdgeAmountProperty.floatValue = relative;
            currentProps.collapseEdgeAmountAbsoluteProperty.floatValue = absolute;
            currentProps.collapseEdgePositionProperty.floatValue = 0.5f;
            so.ApplyModifiedProperties();
            so.Update();
        }

        void ParallelogramCommon(QuadData.CollapsedEdgeType edge, bool direction, float relative, float absolute, bool chevron = false)
        {
            EnsureSkew();
            collapsedEdgeProperty.enumValueIndex = (int)edge;
            collapseIntoParallelogramProperty.boolValue = true;
            mirrorCollapseProperty.boolValue = chevron;
            currentProps.collapseEdgeAmountProperty.floatValue = relative;
            currentProps.collapseEdgeAmountAbsoluteProperty.floatValue = absolute;
            currentProps.collapseEdgePositionProperty.floatValue = direction ? 1f : 0f;
            so.ApplyModifiedProperties();
            so.Update();
        }

        void TriangleCommon(QuadData.CollapsedEdgeType edge, float position)
        {
            EnsureSkew();
            collapsedEdgeProperty.enumValueIndex = (int)edge;
            collapseIntoParallelogramProperty.boolValue = false;
            mirrorCollapseProperty.boolValue = false;
            currentProps.collapseEdgeAmountProperty.floatValue = 1f;
            currentProps.collapseEdgeAmountAbsoluteProperty.floatValue = 9999f;
            currentProps.collapseEdgePositionProperty.floatValue = position;
            so.ApplyModifiedProperties();
            so.Update();
        }

        void EnsureSkew()
        {
            if (!AllTargetsHaveModule(so, data => data.HasSkew))
                UpdateModules(so, "Add Skew", data => data.EnableSkew());
        }
    }

    private static Vector2 GetScreenSpaceCenter(RectTransform rectTransform)
    {
        var canvas = rectTransform.GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas == null)
            return Vector2.zero;

        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return rectTransform.position;

        var camera = canvas.worldCamera ?? Camera.main;
        if (camera == null)
            return Vector2.zero;

        return camera.WorldToScreenPoint(rectTransform.position);
    }

    public static bool CheckPrivateBooleanField(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null || field.FieldType != typeof(bool))
            return false;

        return field.GetValue(obj) is true;
    }

    private void ResolveModuleProperties()
    {
        var outlineConfigProperty = baseProperty.FindPropertyRelative(QuadData.OutlineConfigFieldName);
        outlineColorWrapModeXProperty = outlineConfigProperty?.FindPropertyRelative(QuadData.OutlineColorWrapModeXFieldName);
        outlineColorWrapModeYProperty = outlineConfigProperty?.FindPropertyRelative(QuadData.OutlineColorWrapModeYFieldName);
        outlineColorPresetMixProperty = outlineConfigProperty?.FindPropertyRelative(QuadData.OutlineColorPresetMixFieldName);
        outlineFadesTowardsPerimeterProperty = outlineConfigProperty?.FindPropertyRelative(QuadData.OutlineFadeTowardsPerimeterFieldName);
        outlineAdjustsChamferProperty = outlineConfigProperty?.FindPropertyRelative(QuadData.OutlineAdjustsChamferFieldName);
        outlineAlphaIsBlendProperty = outlineConfigProperty?.FindPropertyRelative(QuadData.OutlineAlphaIsBlendFieldName);
        addInteriorOutlineProperty = outlineConfigProperty?.FindPropertyRelative(QuadData.AddInteriorOutlineFieldName);
        outlineExpandsOutwardsProperty = outlineConfigProperty?.FindPropertyRelative(QuadData.OutlineExpandsOutwardsFieldName);
        outlineAccommodatesCollapsedEdgeProperty = outlineConfigProperty?.FindPropertyRelative(QuadData.OutlineAccommodatesCollapsedEdgeFieldName);
        outlineColorDimensionsProperty = outlineConfigProperty?.FindPropertyRelative(QuadData.OutlineColorDimensionsFieldName);

        var gradientConfigProperty = baseProperty.FindPropertyRelative(QuadData.GradientConfigFieldName);
        proceduralGradientColorWrapModeXProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.ProceduralGradientColorWrapModeXFieldName);
        proceduralGradientColorWrapModeYProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.ProceduralGradientColorWrapModeYFieldName);
        proceduralGradientColorPresetMixProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.ProceduralGradientColorPresetMixFieldName);
        proceduralGradientColorAlphaIsBlendProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.ProceduralGradientAlphaIsBlendFieldName);
        proceduralGradientTypeProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.ProceduralGradientTypeFieldName);
        proceduralGradientAffectsInteriorProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.ProceduralGradientAffectsInteriorFieldName);
        proceduralGradientAffectsOutlineProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.ProceduralGradientAffectsOutlineFieldName);
        proceduralGradientAspectCorrectionProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.ProceduralGradientAspectCorrectionFieldName);
        proceduralGradientPositionFromPointerProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.ProceduralGradientPositionFromPointerFieldName);
        noiseGradientAlternateModeProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.NoiseGradientAlternateModeFieldName);
        screenSpaceProceduralGradientProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.ScreenSpaceProceduralGradientFieldName);
        proceduralGradientInvertProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.ProceduralGradientInvertFieldName);
        proceduralGradientColorDimensionsProperty = gradientConfigProperty?.FindPropertyRelative(QuadData.ProceduralGradientColorDimensionsFieldName);

        var patternConfigProperty = baseProperty.FindPropertyRelative(QuadData.PatternConfigFieldName);
        patternColorWrapModeXProperty = patternConfigProperty?.FindPropertyRelative(QuadData.PatternColorWrapModeXFieldName);
        patternColorWrapModeYProperty = patternConfigProperty?.FindPropertyRelative(QuadData.PatternColorWrapModeYFieldName);
        patternColorPresetMixProperty = patternConfigProperty?.FindPropertyRelative(QuadData.PatternColorPresetMixFieldName);
        patternColorAlphaIsBlendProperty = patternConfigProperty?.FindPropertyRelative(QuadData.PatternColorAlphaIsBlendFieldName);
        patternAffectsInteriorProperty = patternConfigProperty?.FindPropertyRelative(QuadData.PatternAffectsInteriorFieldName);
        patternAffectsOutlineProperty = patternConfigProperty?.FindPropertyRelative(QuadData.PatternAffectsOutlineFieldName);
        screenSpacePatternProperty = patternConfigProperty?.FindPropertyRelative(QuadData.ScreenSpacePatternFieldName);
        softPatternProperty = patternConfigProperty?.FindPropertyRelative(QuadData.SoftPatternFieldName);
        spritePatternRotationModeProperty = patternConfigProperty?.FindPropertyRelative(QuadData.SpritePatternRotationModeFieldName);
        spritePatternOffsetDirectionDegreesProperty = patternConfigProperty?.FindPropertyRelative(QuadData.SpritePatternOffsetDirectionDegreesFieldName);
        patternOrientationProperty = patternConfigProperty?.FindPropertyRelative(QuadData.PatternFieldName);
        scanlinePatternSpeedIsStaticOffsetProperty = patternConfigProperty?.FindPropertyRelative(QuadData.ScanlinePatternSpeedIsStaticOffsetFieldName);
        patternOriginPosProperty = patternConfigProperty?.FindPropertyRelative(QuadData.PatternOriginPosFieldName);
        patternColorDimensionsProperty = patternConfigProperty?.FindPropertyRelative(QuadData.PatternColorDimensionsFieldName);

        var cutoutConfigProperty = baseProperty.FindPropertyRelative(QuadData.CutoutConfigFieldName);
        cutoutTypeProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.CutoutTypeFieldName);
        simpleCutoutEdgeEnabledProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.CutoutEnabledFieldName);
        simpleCutoutRuleProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.CutoutRuleFieldName);
        sdfCutoutBehaviourProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.CutoutBehaviourFieldName);
        sdfCutoutChamferNormalizeProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.SdfCutoutChamferNormalizeFieldName);
        sdfCutoutIsSquircleProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.SdfCutoutIsSquircleFieldName);
        sdfCutoutMirrorProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.SdfCutoutMirrorFieldName);
        sdfCutoutMirrorIsDiagonalProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.SdfCutoutMirrorIsDiagonalFieldName);
        sdfCutoutPositionIsAbsoluteProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.SdfCutoutPositionIsAbsoluteFieldName);
        sdfCutoutSizeIsAbsoluteProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.SdfCutoutSizeIsAbsoluteFieldName);
        sdfCutoutUsesAnchorsProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.SdfCutoutUsesAnchorsFieldName);
        sdfCutoutAnchorMinProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.SdfCutoutAnchorMinFieldName);
        sdfCutoutAnchorMaxProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.SdfCutoutAnchorMaxFieldName);
        sdfCutoutPivotProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.SdfCutoutPivotFieldName);
        cutoutPositionIgnoresExpandedOutlinesProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.CutoutPositionIgnoresExpandedOutlinesFieldName);
        cutoutOnlyAffectsOutlineProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.CutoutOnlyAffectsOutlineFieldName);
        invertCutoutProperty = cutoutConfigProperty?.FindPropertyRelative(QuadData.InvertCutoutFieldName);

        var strokeConfigProperty = baseProperty.FindPropertyRelative(QuadData.StrokeConfigFieldName);
        strokeOriginProperty = strokeConfigProperty?.FindPropertyRelative(QuadData.StrokeOriginFieldName);

        var skewConfigProperty = baseProperty.FindPropertyRelative(QuadData.SkewConfigFieldName);
        collapsedEdgeProperty = skewConfigProperty?.FindPropertyRelative(QuadData.CollapsedEdgeFieldName);
        collapseIntoParallelogramProperty = skewConfigProperty?.FindPropertyRelative(QuadData.CollapseIntoParallelogramFieldName);
        mirrorCollapseProperty = skewConfigProperty?.FindPropertyRelative(QuadData.MirrorCollapseFieldName);
        edgeCollapseAmountIsAbsoluteProperty = skewConfigProperty?.FindPropertyRelative(QuadData.EdgeCollapseAmountIsAbsoluteFieldName);
    }

    private void MatchModulesToQuad(ProceduralProperties props)
    {
        props.outlineAnim = quadData.HasOutline ? props.outlineAnim ?? new OutlineAnimData() : null;
        props.gradientAnim = quadData.HasGradient ? props.gradientAnim ?? new GradientAnimData() : null;
        props.patternAnim = quadData.HasPattern ? props.patternAnim ?? new PatternAnimData() : null;
        props.cutoutAnim = quadData.HasCutout ? props.cutoutAnim ?? new CutoutAnimData() : null;
        props.strokeAnim = quadData.HasStroke ? props.strokeAnim ?? new StrokeAnimData() : null;
        props.skewAnim = quadData.HasSkew ? props.skewAnim ?? new SkewAnimData() : null;
    }

    private static string GetModuleName(Module module) => module == Module.Gradient ? "Procedural Gradient" : module.ToString();
    private static string GetModuleFeature(Module module) => module switch { Module.Outline => FlexibleImageFeatureManager.OutlineFeatureID, Module.Gradient => FlexibleImageFeatureManager.ProceduralGradientFeatureID, Module.Pattern => FlexibleImageFeatureManager.PatternFeatureID, Module.Skew => FlexibleImageFeatureManager.SkewFeatureID, Module.Stroke => FlexibleImageFeatureManager.StrokeFeatureID, _ => FlexibleImageFeatureManager.CutoutFeatureID };
    private static bool HasModule(QuadData data, Module module) => module switch { Module.Outline => data.HasOutline, Module.Gradient => data.HasGradient, Module.Pattern => data.HasPattern, Module.Skew => data.HasSkew, Module.Stroke => data.HasStroke, _ => data.HasCutout };
    private static object GetModuleAnimation(ProceduralProperties props, Module module) => module switch { Module.Outline => props.outlineAnim, Module.Gradient => props.gradientAnim, Module.Pattern => props.patternAnim, Module.Skew => props.skewAnim, Module.Stroke => props.strokeAnim, _ => props.cutoutAnim };
    private static void SetModule(QuadData data, Module module, bool enabled)
    {
        switch (module)
        {
            case Module.Outline: if (enabled) data.EnableOutline(); else data.DisableOutline(); break;
            case Module.Gradient: if (enabled) data.EnableGradient(); else data.DisableGradient(); break;
            case Module.Pattern: if (enabled) data.EnablePattern(); else data.DisablePattern(); break;
            case Module.Skew: if (enabled) data.EnableSkew(); else data.DisableSkew(); break;
            case Module.Stroke: if (enabled) data.EnableStroke(); else data.DisableStroke(); break;
            case Module.Cutout: if (enabled) data.EnableCutout(); else data.DisableCutout(); break;
        }
    }

    private void DrawModuleSelector(Rect rect, SerializedObject so, Module[] modules)
    {
        var (available, present, mixed, unavailable) = (0, 0, false, false);
        foreach (var module in modules)
        {
            var (modulePresent, targets) = GetModuleState(so, module);
            if (FlexibleImageFeatureManager.IsFeatureEnabled(GetModuleFeature(module)))
            {
                available++;
                if (targets > 0 && modulePresent == targets) present++;
                else if (modulePresent > 0) mixed = true;
            }
            else if (modulePresent > 0)
                unavailable = true;
        }

        var label = available == 0 && unavailable ? "<color=#ff5555>!</color>" : $"{(mixed ? "-" : present)} / {available}{(unavailable ? "  <color=#ff5555>!</color>" : "")}";
        var wasEnabled = GUI.enabled;
        GUI.enabled &= available > 0 || unavailable;
        if (GUI.Button(rect, label, ModuleButtonStyle))
            PopupWindow.Show(rect, new ModulePopup(this, so, modules));
        GUI.enabled = wasEnabled;
    }

    private (int present, int targets) GetModuleState(SerializedObject so, Module module)
    {
        var (present, targets) = (0, 0);
        foreach (var target in so.targetObjects)
        {
            var targetQuadData = GetTargetQuadData(target);
            if (targetQuadData == null) continue;
            targets++;
            if (HasModule(targetQuadData, module)) present++;
        }
        return (present, targets);
    }

    private sealed class ModulePopup : PopupWindowContent
    {
        private readonly QuadDataEditor owner;
        private readonly SerializedObject serializedObject;
        private readonly Module[] modules;

        public ModulePopup(QuadDataEditor owner, SerializedObject serializedObject, Module[] modules) => (this.owner, this.serializedObject, this.modules) = (owner, serializedObject, modules);

        public override Vector2 GetWindowSize()
        {
            var (available, unavailable) = (modules.Count(module => FlexibleImageFeatureManager.IsFeatureEnabled(GetModuleFeature(module))), modules.Count(module => !FlexibleImageFeatureManager.IsFeatureEnabled(GetModuleFeature(module)) && owner.GetModuleState(serializedObject, module).present > 0));
            return new Vector2(190f, 8f + available * 20f + (unavailable > 0 ? 25f + unavailable * 20f : 0f));
        }

        public override void OnGUI(Rect rect)
        {
            if (serializedObject.targetObject == null)
            {
                editorWindow.Close();
                return;
            }

            GUILayout.Space(3f);
            foreach (var module in modules)
                if (FlexibleImageFeatureManager.IsFeatureEnabled(GetModuleFeature(module)))
                    DrawToggle(module, false);

            var unavailable = modules.Where(module => !FlexibleImageFeatureManager.IsFeatureEnabled(GetModuleFeature(module)) && owner.GetModuleState(serializedObject, module).present > 0).ToArray();
            if (unavailable.Length == 0)
                return;

            var separator = EditorGUILayout.GetControlRect(false, 1f);
            separator.x += 4f;
            separator.width -= 8f;
            EditorGUI.DrawRect(separator, new Color(0f, 0f, 0f, 0.3f));
            EditorGUILayout.LabelField("Unavailable", EditorStyles.miniBoldLabel);
            foreach (var module in unavailable)
                DrawToggle(module, true);
        }

        private void DrawToggle(Module module, bool unavailable)
        {
            var (present, targets) = owner.GetModuleState(serializedObject, module);
            var all = targets > 0 && present == targets;
            var oldMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = present > 0 && !all;
            var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var toggleRect = new Rect(rowRect.xMax - 18f, rowRect.y, 18f, rowRect.height);
            rowRect.width -= 18f;
            EditorGUI.LabelField(rowRect, GetModuleName(module));
            var value = EditorGUI.Toggle(toggleRect, all);
            var changed = value != all;
            if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none)) { value = !all; changed = true; }
            if (changed)
            {
                owner.UpdateModules(serializedObject, $"{(unavailable || !value ? "Remove" : "Add")} {GetModuleName(module)}", data => SetModule(data, module, !unavailable && value));
                editorWindow.Repaint();
                InternalEditorUtility.RepaintAllViews();
            }
            EditorGUI.showMixedValue = oldMixedValue;
        }
    }

    private void UpdateModules(SerializedObject so, string undoName, Action<QuadData> action)
    {
        Undo.RecordObjects(so.targetObjects, undoName);
        foreach (var target in so.targetObjects)
        {
            var targetQuadData = GetTargetQuadData(target);
            if (targetQuadData != null)
                action(targetQuadData);
        }
        RefreshModuleProperties(so);
    }

    private void RefreshModuleProperties(SerializedObject so)
    {
        so.Update();
        ResolveModuleProperties();
        defaultProps = new ProceduralPropertiesEditor(quadData.proceduralAnimationStates, baseProperty, 0, 0);
        currentProps = new ProceduralPropertiesEditor(quadData.proceduralAnimationStates, baseProperty, animationStateIdxProperty.intValue, animationSubStateIdxProperty.intValue);
    }

    private void DirtyTargets(SerializedObject so, FlexibleImage.AdvancedRaycastOptions raycastOptions)
    {
        foreach (var target in so.targetObjects)
        {
            var targetQuadData = GetTargetQuadData(target);
            targetQuadData?.SetVerticesDirty();
            targetQuadData?.SetRayCastAreaDirty(raycastOptions);
        }
    }

    private bool AllTargetsHaveModule(SerializedObject so, Func<QuadData, bool> hasModule)
    {
        foreach (var target in so.targetObjects)
        {
            var targetQuadData = GetTargetQuadData(target);
            if (targetQuadData == null || !hasModule(targetQuadData))
                return false;
        }
        return true;
    }

    private void ConvertSDFCutoutPositioning(SerializedObject so, FlexibleImage flexibleImage, bool useAnchors)
    {
        so.ApplyModifiedProperties();
        Undo.RecordObjects(so.targetObjects, "Convert SDF Cutout Positioning");
        foreach (var target in so.targetObjects)
        {
            var targetQuadData = GetTargetQuadData(target);
            var rectTransform = target is FlexibleImage image ? image.rectTransform : flexibleImage?.rectTransform;
            targetQuadData?.ConvertSDFCutoutPositioning(useAnchors, rectTransform, QuadDataPresetEditor.ReferenceParentSize);
        }
        so.Update();
    }

    private QuadData GetTargetQuadData(UnityEngine.Object target)
    {
        var container = target switch
        {
            FlexibleImage image => image.InstanceQuadDataContainer,
            QuadDataPreset preset => preset.quadDataContainer,
            _ => null
        };
        return container != null && quadIdx >= 0 && quadIdx < container.Count ? container[quadIdx] : null;
    }
}
}
