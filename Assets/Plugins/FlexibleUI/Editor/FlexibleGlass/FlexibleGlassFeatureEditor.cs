using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace JeffGrawAssets.FlexibleUI
{
[CustomEditor(typeof(FlexibleGlassFeature))]
public partial class FlexibleGlassFeatureEditor : Editor
{
    private static readonly GUIContent[] FieldResolutionLabels = { new("64"), new("128"), new("256"), new("512"), new("1024") };
    private static readonly int[] FieldResolutions = { 64, 128, 256, 512, 1024 };
    private SerializedProperty renderPassEventProperty;
    private SerializedProperty iterationsProperty;
    private SerializedProperty sampleRadiusProperty;
    private SerializedProperty ditherStrengthProperty;
    private SerializedProperty compositionBlendProperty;
    private SerializedProperty sdfResolutionProperty;
    private SerializedProperty backdropMipLevelsProperty;
    private SerializedProperty edgeLightModeProperty;
    private SerializedProperty edgeLightAngleProperty;
    private SerializedProperty pointLightPositionProperty;
    private SerializedProperty pointLightRadiusProperty;
    private SerializedProperty edgeLightSpreadProperty;
    private SerializedProperty edgeHighlightProperty;
    private SerializedProperty edgeShadowProperty;
    private SerializedProperty opposingEdgeLightStrengthProperty;
    private SerializedProperty blurPaddingProperty;
    private SerializedProperty blurFormatProperty;

    private void OnEnable()
    {
        renderPassEventProperty = serializedObject.FindProperty(FlexibleGlassFeature.RenderPassEventFieldName);
        iterationsProperty = serializedObject.FindProperty(FlexibleGlassFeature.IterationsFieldName);
        sampleRadiusProperty = serializedObject.FindProperty(FlexibleGlassFeature.SampleRadiusFieldName);
        ditherStrengthProperty = serializedObject.FindProperty(FlexibleGlassFeature.DitherStrengthFieldName);
        compositionBlendProperty = serializedObject.FindProperty(FlexibleGlassFeature.CompositionBlendFieldName);
        sdfResolutionProperty = serializedObject.FindProperty(FlexibleGlassFeature.SdfResolutionFieldName);
        backdropMipLevelsProperty = serializedObject.FindProperty(FlexibleGlassFeature.BackdropMipLevelsFieldName);
        edgeLightModeProperty = serializedObject.FindProperty(FlexibleGlassFeature.EdgeLightModeFieldName);
        edgeLightAngleProperty = serializedObject.FindProperty(FlexibleGlassFeature.EdgeLightAngleFieldName);
        pointLightPositionProperty = serializedObject.FindProperty(FlexibleGlassFeature.PointLightPositionFieldName);
        pointLightRadiusProperty = serializedObject.FindProperty(FlexibleGlassFeature.PointLightRadiusFieldName);
        edgeLightSpreadProperty = serializedObject.FindProperty(FlexibleGlassFeature.EdgeLightSpreadFieldName);
        edgeHighlightProperty = serializedObject.FindProperty(FlexibleGlassFeature.EdgeHighlightFieldName);
        edgeShadowProperty = serializedObject.FindProperty(FlexibleGlassFeature.EdgeShadowFieldName);
        opposingEdgeLightStrengthProperty = serializedObject.FindProperty(FlexibleGlassFeature.OpposingEdgeLightStrengthFieldName);
        blurPaddingProperty = serializedObject.FindProperty(FlexibleGlassFeature.BlurPaddingFieldName);
        blurFormatProperty = serializedObject.FindProperty(FlexibleGlassFeature.BlurFormatFieldName);
        OnEnableBlurIntegration();
    }

    private void OnDisable()
    {
        OnDisableBlurIntegration();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(renderPassEventProperty, new GUIContent("Render Pass Event", "Camera-color stage captured and composited by this Glass group."));
        sdfResolutionProperty.intValue = EditorGUILayout.IntPopup(new GUIContent("Field Resolution", "Retained signed-distance resolution generated once per unique visible shape. Memory use grows with the square of this value."), Mathf.Clamp(Mathf.ClosestPowerOfTwo(sdfResolutionProperty.intValue), 64, 1024), FieldResolutionLabels, FieldResolutions);
        EditorGUILayout.PropertyField(compositionBlendProperty, new GUIContent("Join Softness", "Screen-pixel range used to round joins and flow nearby Add shapes together. 0 uses exact Boolean Add/Subtract operations."));
        compositionBlendProperty.floatValue = Mathf.Clamp(compositionBlendProperty.floatValue, 0f, 100f);
        EditorGUILayout.PropertyField(backdropMipLevelsProperty, new GUIContent("Backdrop Mip Levels", "Mip levels available to high-displacement refraction. Blur reconstructs from its final result; 0 samples only the full-resolution texture."));
        backdropMipLevelsProperty.intValue = Mathf.Clamp(backdropMipLevelsProperty.intValue, 0, 8);
        EditorGUILayout.PropertyField(edgeLightModeProperty, new GUIContent("Lip Light Mode", "Directional uses one shared direction, Opposing adds light from the opposite direction, and Point derives direction from a shared viewport position."));
        var edgeLightMode = (GlassEdgeLightMode)edgeLightModeProperty.enumValueIndex;
        if (edgeLightModeProperty.hasMultipleDifferentValues || edgeLightMode != GlassEdgeLightMode.Point)
        {
            EditorGUILayout.PropertyField(edgeLightAngleProperty, new GUIContent("Lip Light Angle", "Shared screen-space direction for Directional and Opposing lip lighting. Values repeat every 360 degrees."));
        }
        if (edgeLightModeProperty.hasMultipleDifferentValues || edgeLightMode == GlassEdgeLightMode.Point)
        {
            EditorGUILayout.PropertyField(pointLightPositionProperty, new GUIContent("Point Light Position", "Normalized camera viewport position. Values outside 0–1 place the light beyond the screen."));
            EditorGUILayout.PropertyField(pointLightRadiusProperty, new GUIContent("Point Light Radius", "Screen-space light radius measured as a fraction of the camera viewport height."));
        }
        EditorGUILayout.PropertyField(edgeLightSpreadProperty, new GUIContent("Lip Light Spread", "Directional/Opposing: width of the falloff centered on each element, measured relative to viewport height. Moving an element does not move it through the falloff. Point: angular highlight width. Zero disables lip lighting."));
        if (edgeLightModeProperty.hasMultipleDifferentValues || edgeLightMode == GlassEdgeLightMode.Opposing)
            EditorGUILayout.PropertyField(opposingEdgeLightStrengthProperty, new GUIContent("Opposing Light Strength", "Strength of the opposing fill relative to the primary lip light."));
        EditorGUILayout.PropertyField(edgeHighlightProperty, new GUIContent("Lip Highlight", "Highlight color. Alpha controls strength; zero alpha disables highlights."));
        EditorGUILayout.PropertyField(edgeShadowProperty, new GUIContent("Lip Shadow", "Shadow color. Alpha controls strength; zero alpha disables lip shadowing."));
        edgeLightSpreadProperty.floatValue = Mathf.Clamp01(edgeLightSpreadProperty.floatValue);
        opposingEdgeLightStrengthProperty.floatValue = Mathf.Clamp01(opposingEdgeLightStrengthProperty.floatValue);

        var integrated = false;
        EditorGUILayout.Space(2f);
        DrawBlurIntegrationToggle(ref integrated);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        if (integrated)
            DrawBlurIntegrationSettings();
        else
            DrawBuiltInBlurSettings();
        EditorGUILayout.Space(2f);
        DrawBlurPadding();
        if (!integrated)
            DrawBlurFormat();
        EditorGUILayout.EndVertical();

        if (integrated)
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField("Standalone Package Fallback", EditorStyles.boldLabel);
            DrawBuiltInBlurSettings();
            DrawBlurFormat();
            EditorGUILayout.EndVertical();
        }

        if (SystemInfo.graphicsShaderLevel < 45 || !SystemInfo.supportsComputeShaders)
            EditorGUILayout.HelpBox("Flexible Glass requires compute shaders and shader model 4.5. It cannot render on this graphics device.", MessageType.Error);

        if (serializedObject.ApplyModifiedProperties())
            FlexibleGlassEditorUtility.RefreshPreview();
    }

    private void DrawBuiltInBlurSettings()
    {
        EditorGUILayout.PropertyField(iterationsProperty, new GUIContent("Blur Pyramid Levels", "Built-in blur downsample and upsample levels. 0 disables blur."));
        iterationsProperty.intValue = Mathf.Clamp(iterationsProperty.intValue, 0, 6);
        EditorGUILayout.PropertyField(sampleRadiusProperty, new GUIContent("Kernel Spread", "Sampling distance normalized to a 1080-pixel render height. 1 uses the canonical half-pixel tap positions at 1080p."));
        EditorGUILayout.PropertyField(ditherStrengthProperty, new GUIContent("Dither Strength", "Noise added to the final built-in blur to reduce color banding."));
        sampleRadiusProperty.floatValue = Mathf.Clamp(sampleRadiusProperty.floatValue, 0.5f, 2f);
        ditherStrengthProperty.floatValue = Mathf.Clamp(ditherStrengthProperty.floatValue, 0f, 5f);
    }

    private void DrawBlurPadding()
    {
        EditorGUILayout.PropertyField(blurPaddingProperty, new GUIContent("Blur Padding", "Additional screen pixels captured around the composed glass bounds. Increase this when a custom blur or optical setup reaches farther than the calculated region."));
        blurPaddingProperty.floatValue = Mathf.Max(0f, blurPaddingProperty.floatValue);
    }

    private void DrawBlurFormat()
    {
        var format = (GraphicsFormat)EditorGUILayout.EnumPopup(new GUIContent("Blur Format", "Built-in blur texture format. Defaults to 32-bit color for the active color space; does not affect Flexible Blur."), (GraphicsFormat)blurFormatProperty.intValue);
        blurFormatProperty.intValue = (int)format;
        if (FlexibleGlassFeature.FormatFallbackDict.TryGetValue(format, out var fallback) && fallback != format)
            EditorGUILayout.HelpBox($"{format} is unavailable in this Editor configuration; runtime fallback is {fallback}.", MessageType.Warning);
    }

    partial void OnEnableBlurIntegration();
    partial void OnDisableBlurIntegration();
    partial void DrawBlurIntegrationToggle(ref bool integrated);
    partial void DrawBlurIntegrationSettings();
}
}
