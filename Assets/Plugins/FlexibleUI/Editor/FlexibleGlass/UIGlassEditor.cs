using UnityEditor;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[CustomEditor(typeof(UIGlass))]
[CanEditMultipleObjects]
public class UIGlassEditor : Editor
{
    private const string ShapeSectionKey = "jg_flexibleGlassShape";
    private const string AppearanceSectionKey = "jg_flexibleGlassAppearance";

    private static readonly GUIContent[] OperationOptions =
    {
        new("Add / Union", "Joins this shape to the composed Glass surface."),
        new("Subtract / Cutout", "Cuts this shape from the complete additive Glass surface.")
    };

    private SerializedProperty referenceSourceProperty;
    private SerializedProperty cameraReferenceProperty;
    private SerializedProperty featureNumberProperty;
    private SerializedProperty operationProperty;
    private SerializedProperty sdfSourceProperty;
    private SerializedProperty sdfSpriteProperty;
    private SerializedProperty alphaThresholdProperty;
    private SerializedProperty shapeTypeProperty;
    private SerializedProperty shapeProperty;
    private SerializedProperty shapeExponentProperty;
    private SerializedProperty canonicalCornerRadiusProperty;
    private SerializedProperty surfaceSmoothnessModeProperty;
    private SerializedProperty surfaceSmoothnessProperty;
    private SerializedProperty depthFallbackProperty;
    private SerializedProperty refractionStrengthProperty;
    private SerializedProperty refractiveIndexProperty;
    private SerializedProperty abbeNumberProperty;
    private SerializedProperty appearanceProperty;

    private void OnEnable()
    {
        referenceSourceProperty = serializedObject.FindProperty(UIGlass.ReferenceSourceFieldName);
        cameraReferenceProperty = serializedObject.FindProperty(UIGlass.CameraReferenceFieldName);
        featureNumberProperty = serializedObject.FindProperty(UIGlass.FeatureNumberFieldName);
        operationProperty = serializedObject.FindProperty(UIGlass.OperationFieldName);
        sdfSourceProperty = serializedObject.FindProperty(UIGlass.SdfSourceFieldName);
        sdfSpriteProperty = serializedObject.FindProperty(UIGlass.SdfSpriteFieldName);
        alphaThresholdProperty = serializedObject.FindProperty(UIGlass.AlphaThresholdFieldName);
        shapeTypeProperty = serializedObject.FindProperty(UIGlass.ShapeTypeFieldName);
        shapeProperty = serializedObject.FindProperty(UIGlass.ShapeFieldName);
        shapeExponentProperty = serializedObject.FindProperty(UIGlass.ShapeExponentFieldName);
        canonicalCornerRadiusProperty = serializedObject.FindProperty(UIGlass.CanonicalCornerRadiusFieldName);
        surfaceSmoothnessModeProperty = serializedObject.FindProperty(UIGlass.SurfaceSmoothnessModeFieldName);
        surfaceSmoothnessProperty = serializedObject.FindProperty(UIGlass.SurfaceSmoothnessFieldName);
        depthFallbackProperty = serializedObject.FindProperty(UIGlass.DepthFallbackFieldName);
        refractionStrengthProperty = serializedObject.FindProperty(UIGlass.RefractionStrengthFieldName);
        refractiveIndexProperty = serializedObject.FindProperty(UIGlass.RefractiveIndexFieldName);
        abbeNumberProperty = serializedObject.FindProperty(UIGlass.AbbeNumberFieldName);
        appearanceProperty = serializedObject.FindProperty(UIGlass.AppearanceFieldName);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var glass = (UIGlass)target;
        if (!FlexibleGlassEditorUtility.DrawGlassSource(referenceSourceProperty, cameraReferenceProperty, featureNumberProperty, glass.gameObject, nameof(UIGlass)))
            return;

        EditorGUILayout.Space(2f);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        if (operationProperty.hasMultipleDifferentValues)
        {
            EditorGUILayout.PropertyField(operationProperty);
        }
        else
        {
            EditorGUI.BeginChangeCheck();
            var operation = GUILayout.Toolbar(operationProperty.enumValueIndex, OperationOptions);
            if (EditorGUI.EndChangeCheck())
                operationProperty.enumValueIndex = operation;
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(2f);
        FlexibleGlassEditorUtility.DrawUIGlassShape(sdfSourceProperty, shapeTypeProperty, shapeProperty, shapeExponentProperty, canonicalCornerRadiusProperty, sdfSpriteProperty, alphaThresholdProperty, ShapeSectionKey);
        EditorGUILayout.Space(2f);
        var canonical = !sdfSourceProperty.hasMultipleDifferentValues && !shapeTypeProperty.hasMultipleDifferentValues &&
            sdfSourceProperty.enumValueIndex == (int)GlassSdfSource.Shape && shapeTypeProperty.intValue == (int)GlassShapeType.Canonical;
        FlexibleGlassEditorUtility.DrawAppearance(appearanceProperty, AppearanceSectionKey, refractionStrengthProperty, refractiveIndexProperty, abbeNumberProperty, surfaceSmoothnessModeProperty, surfaceSmoothnessProperty, canonical, depthFallbackProperty: depthFallbackProperty);
        serializedObject.ApplyModifiedProperties();
    }
}
}
