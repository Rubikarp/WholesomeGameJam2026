using UnityEditor;
using UnityEditor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
[CustomEditor(typeof(GlassImage))]
[CanEditMultipleObjects]
public class GlassImageEditor : ImageEditor
{
    private enum RaycastTargetType { Disabled, Standard, Advanced }

    private const string ShapeSectionKey = "jg_glassImageShape";
    private const string AppearanceSectionKey = "jg_glassImageAppearance";
    private const string ImageSectionKey = "jg_glassImageSource";
    private static readonly GUIContent[] ImageTypes = { new("Simple"), new("Filled") };
    private static readonly int[] ImageTypeValues = { (int)Image.Type.Simple, (int)Image.Type.Filled };
    private static readonly GUIContent ImageTypeContent = new("Image Type", "Simple shows the whole glass surface. Filled reveals part of it without changing its optical shape or rebuilding its cached field. Sliced and Tiled are not supported.");
    private static readonly GUIContent FillAmountContent = new("Fill Amount", "Reveals the glass and its shadow. The reveal edge does not add a new glass bevel.");
    private static readonly GUIContent RaycastTargetContent = new("Raycast Target", "Disabled: ignores pointer events.\n\nStandard: tests the procedural outline, or uses Image alpha hit testing for sprites. Sprite alpha testing requires Read/Write when its threshold is above zero.\n\nAdvanced: tests the cached signed distance field (SDF), including sprite silhouettes, without requiring Read/Write. Raycast Padding shrinks or expands the outline. Hits become available once the field readback is ready.");
    private static readonly GUIContent AdvancedRaycastPaddingContent = new("Raycast Padding", "Hit-area padding in local UI units. Positive values shrink the hit area; negative values expand it, matching normal Image raycast padding. Does not change the visible glass. Fill edges and UI masks still clip hits.");
    private static readonly GUIContent[] RaycastPaddingSides = { new("Left"), new("Bottom"), new("Right"), new("Top") };
    private static readonly string[] RaycastPaddingFields = { "x", "y", "z", "w" };

    private SerializedProperty spriteProperty, colorProperty;
    private SerializedProperty referenceSourceProperty, cameraReferenceProperty, featureNumberProperty;
    private SerializedProperty shapeTypeProperty, shapeExponentProperty, alphaThresholdProperty, surfaceSmoothnessModeProperty, surfaceSmoothnessProperty, shapeProperty, appearanceProperty;
    private SerializedProperty canonicalCornerRadiusProperty;
    private SerializedProperty depthFallbackProperty;
    private SerializedProperty imageTypeProperty, preserveAspectProperty, fillMethodProperty, fillOriginProperty, fillAmountProperty, fillClockwiseProperty;
    private SerializedProperty refractionStrengthProperty, refractiveIndexProperty, abbeNumberProperty;
    private SerializedProperty sdfRaycastProperty, raycastExpansionProperty, raycastTargetProperty, raycastPaddingProperty;

    protected override void OnEnable()
    {
        spriteProperty = serializedObject.FindProperty("m_Sprite");
        colorProperty = serializedObject.FindProperty("m_Color");
        referenceSourceProperty = serializedObject.FindProperty(GlassImage.ReferenceSourceFieldName);
        cameraReferenceProperty = serializedObject.FindProperty(GlassImage.CameraReferenceFieldName);
        featureNumberProperty = serializedObject.FindProperty(GlassImage.FeatureNumberFieldName);
        imageTypeProperty = serializedObject.FindProperty("m_Type");
        preserveAspectProperty = serializedObject.FindProperty("m_PreserveAspect");
        fillMethodProperty = serializedObject.FindProperty("m_FillMethod");
        fillOriginProperty = serializedObject.FindProperty("m_FillOrigin");
        fillAmountProperty = serializedObject.FindProperty("m_FillAmount");
        fillClockwiseProperty = serializedObject.FindProperty("m_FillClockwise");
        shapeTypeProperty = serializedObject.FindProperty(GlassImage.ShapeTypeFieldName);
        shapeExponentProperty = serializedObject.FindProperty(GlassImage.ShapeExponentFieldName);
        canonicalCornerRadiusProperty = serializedObject.FindProperty(GlassImage.CanonicalCornerRadiusFieldName);
        alphaThresholdProperty = serializedObject.FindProperty(GlassImage.AlphaThresholdFieldName);
        surfaceSmoothnessModeProperty = serializedObject.FindProperty(GlassImage.SurfaceSmoothnessModeFieldName);
        surfaceSmoothnessProperty = serializedObject.FindProperty(GlassImage.SurfaceSmoothnessFieldName);
        depthFallbackProperty = serializedObject.FindProperty(GlassImage.DepthFallbackFieldName);
        shapeProperty = serializedObject.FindProperty(GlassImage.ShapeFieldName);
        appearanceProperty = serializedObject.FindProperty(GlassImage.AppearanceFieldName);
        refractionStrengthProperty = serializedObject.FindProperty(GlassImage.RefractionStrengthFieldName);
        refractiveIndexProperty = serializedObject.FindProperty(GlassImage.RefractiveIndexFieldName);
        abbeNumberProperty = serializedObject.FindProperty(GlassImage.AbbeNumberFieldName);
        sdfRaycastProperty = serializedObject.FindProperty(GlassImage.SdfRaycastFieldName);
        raycastExpansionProperty = serializedObject.FindProperty(GlassImage.RaycastExpansionFieldName);
        raycastTargetProperty = serializedObject.FindProperty("m_RaycastTarget");
        raycastPaddingProperty = serializedObject.FindProperty("m_RaycastPadding");
        base.OnEnable();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        if (!FlexibleGlassEditorUtility.DrawGlassSource(referenceSourceProperty, cameraReferenceProperty, featureNumberProperty, ((GlassImage)serializedObject.targetObject).gameObject, nameof(GlassImage)))
        {
            serializedObject.ApplyModifiedProperties();
            return;
        }
        FlexibleGlassEditorUtility.DrawGlassImageShape(shapeTypeProperty, shapeProperty, shapeExponentProperty, canonicalCornerRadiusProperty, spriteProperty, alphaThresholdProperty, ShapeSectionKey);
        EditorGUILayout.Space(2f);
        var canonical = !shapeTypeProperty.hasMultipleDifferentValues && shapeTypeProperty.enumValueIndex == (int)GlassImageShapeType.Canonical;
        FlexibleGlassEditorUtility.DrawAppearance(appearanceProperty, AppearanceSectionKey, refractionStrengthProperty, refractiveIndexProperty, abbeNumberProperty, surfaceSmoothnessModeProperty, surfaceSmoothnessProperty, canonical, colorProperty, depthFallbackProperty);

        serializedObject.ApplyModifiedProperties();
        serializedObject.Update();
        var spriteShape = !shapeTypeProperty.hasMultipleDifferentValues && shapeTypeProperty.enumValueIndex == (int)GlassImageShapeType.Sprite;
        EditorGUILayout.Space(2f);
        if (FlexibleGlassEditorUtility.DrawSectionHeader("Image Settings", ImageSectionKey))
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            DrawImageControls(spriteShape);
            DrawGlassRaycastControls();
            MaskableControlsGUI();
            EditorGUILayout.EndVertical();
        }
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawGlassRaycastControls()
    {
        var mode = !raycastTargetProperty.boolValue ? RaycastTargetType.Disabled :
            sdfRaycastProperty.boolValue ? RaycastTargetType.Advanced : RaycastTargetType.Standard;
        var mixed = raycastTargetProperty.hasMultipleDifferentValues ||
            raycastTargetProperty.boolValue && sdfRaycastProperty.hasMultipleDifferentValues;
        EditorGUI.showMixedValue = mixed;
        EditorGUI.BeginChangeCheck();
        mode = (RaycastTargetType)EditorGUILayout.EnumPopup(RaycastTargetContent, mode);
        if (EditorGUI.EndChangeCheck())
        {
            SetRaycastTargetType(mode);
            mixed = false;
        }
        EditorGUI.showMixedValue = false;
        if (mixed || mode == RaycastTargetType.Disabled)
            return;
        if (mode == RaycastTargetType.Standard)
        {
            DrawRaycastPadding();
            return;
        }
        DrawAdvancedRaycastPadding();
        if (!SystemInfo.supportsAsyncGPUReadback)
            EditorGUILayout.HelpBox("This graphics device does not support asynchronous GPU readback. Select Standard to use regular hit testing.", MessageType.Warning);
    }

    private float AdvancedRaycastPadding
    {
        get => -raycastExpansionProperty.floatValue;
        set => raycastExpansionProperty.floatValue = -value;
    }

    private void DrawAdvancedRaycastPadding()
    {
        var rect = EditorGUILayout.GetControlRect();
        EditorGUI.BeginProperty(rect, AdvancedRaycastPaddingContent, raycastExpansionProperty);
        EditorGUI.showMixedValue = raycastExpansionProperty.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        var padding = EditorGUI.FloatField(rect, AdvancedRaycastPaddingContent, AdvancedRaycastPadding);
        if (EditorGUI.EndChangeCheck())
            AdvancedRaycastPadding = padding;
        EditorGUI.showMixedValue = false;
        EditorGUI.EndProperty();
    }

    private void SetRaycastTargetType(RaycastTargetType mode)
    {
        raycastTargetProperty.boolValue = mode != RaycastTargetType.Disabled;
        sdfRaycastProperty.boolValue = mode == RaycastTargetType.Advanced;
        serializedObject.ApplyModifiedProperties();
        serializedObject.Update();
        foreach (Graphic graphic in targets)
            graphic.SetRaycastDirty();
    }

    private void DrawRaycastPadding()
    {
        raycastPaddingProperty.isExpanded = EditorGUILayout.Foldout(raycastPaddingProperty.isExpanded, "Raycast Padding", true);
        if (!raycastPaddingProperty.isExpanded)
            return;
        EditorGUI.indentLevel++;
        for (int i = 0; i < RaycastPaddingFields.Length; i++)
            EditorGUILayout.PropertyField(raycastPaddingProperty.FindPropertyRelative(RaycastPaddingFields[i]), RaycastPaddingSides[i]);
        EditorGUI.indentLevel--;
    }

    private void DrawImageControls(bool spriteShape)
    {
        EditorGUILayout.IntPopup(imageTypeProperty, ImageTypes, ImageTypeValues, ImageTypeContent);
        if (!imageTypeProperty.hasMultipleDifferentValues && imageTypeProperty.intValue == (int)Image.Type.Filled)
        {
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(fillMethodProperty);
            if (EditorGUI.EndChangeCheck())
                fillOriginProperty.intValue = 0;
            if (!fillMethodProperty.hasMultipleDifferentValues)
            {
                var method = (Image.FillMethod)fillMethodProperty.intValue;
                System.Enum origin = method switch
                {
                    Image.FillMethod.Horizontal => (Image.OriginHorizontal)fillOriginProperty.intValue,
                    Image.FillMethod.Vertical => (Image.OriginVertical)fillOriginProperty.intValue,
                    Image.FillMethod.Radial90 => (Image.Origin90)fillOriginProperty.intValue,
                    Image.FillMethod.Radial180 => (Image.Origin180)fillOriginProperty.intValue,
                    _ => (Image.Origin360)fillOriginProperty.intValue
                };
                EditorGUI.showMixedValue = fillOriginProperty.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                origin = EditorGUILayout.EnumPopup("Fill Origin", origin);
                if (EditorGUI.EndChangeCheck())
                    fillOriginProperty.intValue = System.Convert.ToInt32(origin);
                EditorGUI.showMixedValue = false;
                if (method > Image.FillMethod.Vertical)
                    EditorGUILayout.PropertyField(fillClockwiseProperty, new GUIContent("Clockwise"));
            }
            EditorGUILayout.PropertyField(fillAmountProperty, FillAmountContent);
            EditorGUI.indentLevel--;
        }
        else if (!imageTypeProperty.hasMultipleDifferentValues && imageTypeProperty.intValue != (int)Image.Type.Simple)
            EditorGUILayout.HelpBox("Sliced and Tiled are not supported by GlassImage; this image is rendered as Simple.", MessageType.Warning);

        if (spriteShape)
        {
            EditorGUILayout.PropertyField(preserveAspectProperty);
            using (new EditorGUI.DisabledScope(!((GlassImage)target).overrideSprite))
            {
                if (GUILayout.Button("Set Native Size"))
                {
                    serializedObject.ApplyModifiedProperties();
                    foreach (GlassImage image in targets)
                    {
                        Undo.RecordObject(image.rectTransform, "Set Glass Image Native Size");
                        image.SetNativeSize();
                    }
                    serializedObject.Update();
                }
            }
        }
    }
}
}
