using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace JeffGrawAssets.FlexibleUI
{
[CustomEditor(typeof(FlexibleGlassCameraOverride))]
public partial class FlexibleGlassCameraOverrideEditor : Editor
{
    private SerializedProperty featureNumberProperty;
    private SerializedProperty overrideCompositionProperty;
    private SerializedProperty compositionBlendProperty;
    private SerializedProperty overrideRefractionProperty;
    private SerializedProperty backdropMipLevelsProperty;
    private SerializedProperty overrideLightingProperty;
    private SerializedProperty edgeLightModeProperty;
    private SerializedProperty edgeLightAngleProperty;
    private SerializedProperty pointLightPositionProperty;
    private SerializedProperty pointLightRadiusProperty;
    private SerializedProperty edgeLightSpreadProperty;
    private SerializedProperty edgeHighlightProperty;
    private SerializedProperty edgeShadowProperty;
    private SerializedProperty opposingEdgeLightStrengthProperty;
    private SerializedProperty overrideBlurProperty;
    private SerializedProperty iterationsProperty;
    private SerializedProperty sampleRadiusProperty;
    private SerializedProperty ditherStrengthProperty;
    private SerializedProperty blurPaddingProperty;
    private SerializedProperty blurFormatProperty;

    private void OnEnable()
    {
        featureNumberProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.FeatureNumberFieldName);
        overrideCompositionProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.OverrideCompositionFieldName);
        compositionBlendProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.CompositionBlendFieldName);
        overrideRefractionProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.OverrideRefractionFieldName);
        backdropMipLevelsProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.BackdropMipLevelsFieldName);
        overrideLightingProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.OverrideLightingFieldName);
        edgeLightModeProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.EdgeLightModeFieldName);
        edgeLightAngleProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.EdgeLightAngleFieldName);
        pointLightPositionProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.PointLightPositionFieldName);
        pointLightRadiusProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.PointLightRadiusFieldName);
        edgeLightSpreadProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.EdgeLightSpreadFieldName);
        edgeHighlightProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.EdgeHighlightFieldName);
        edgeShadowProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.EdgeShadowFieldName);
        opposingEdgeLightStrengthProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.OpposingEdgeLightStrengthFieldName);
        overrideBlurProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.OverrideBlurFieldName);
        iterationsProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.IterationsFieldName);
        sampleRadiusProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.SampleRadiusFieldName);
        ditherStrengthProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.DitherStrengthFieldName);
        blurPaddingProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.BlurPaddingFieldName);
        blurFormatProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.BlurFormatFieldName);
        OnEnableBlurIntegration();
    }

    private void OnDisable() => OnDisableBlurIntegration();

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(featureNumberProperty, FlexibleGlassEditorUtility.FeatureNumberContent);
        featureNumberProperty.intValue = Mathf.Max(0, featureNumberProperty.intValue);
        FlexibleGlassEditorUtility.DrawDiagnostics(((FlexibleGlassCameraOverride)target).GetComponent<Camera>(), featureNumberProperty.intValue);

        DrawOverrideGroup(overrideCompositionProperty, "Composition", "Override composition settings for this camera.", DrawCompositionSettings);
        DrawOverrideGroup(overrideRefractionProperty, "Refraction", "Override backdrop reconstruction settings for this camera.", DrawRefractionSettings);
        DrawOverrideGroup(overrideLightingProperty, "Lighting", "Override shared lip lighting for this camera.", DrawLightingSettings);
        DrawOverrideGroup(overrideBlurProperty, "Blur", "Override blur capture and processing for this camera.", DrawBlurSettings);

        if (serializedObject.ApplyModifiedProperties())
            FlexibleGlassEditorUtility.RefreshPreview();
    }

    private static void DrawOverrideGroup(SerializedProperty enabledProperty, string label, string tooltip, Action drawSettings)
    {
        EditorGUILayout.Space(2f);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.PropertyField(enabledProperty, new GUIContent(label, tooltip));
        if (enabledProperty.boolValue)
        {
            EditorGUILayout.Space(1f);
            drawSettings();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawCompositionSettings()
    {
        EditorGUILayout.PropertyField(compositionBlendProperty, new GUIContent("Join Softness", "Screen-pixel range used to round joins and flow nearby Add shapes together. 0 uses exact Boolean Add/Subtract operations."));
        compositionBlendProperty.floatValue = Mathf.Clamp(compositionBlendProperty.floatValue, 0f, 100f);
    }

    private void DrawRefractionSettings()
    {
        EditorGUILayout.PropertyField(backdropMipLevelsProperty, new GUIContent("Backdrop Mip Levels", "Mip levels available to high-displacement refraction. Blur reconstructs from its final result; 0 samples only the full-resolution texture."));
        backdropMipLevelsProperty.intValue = Mathf.Clamp(backdropMipLevelsProperty.intValue, 0, 8);
    }

    private void DrawLightingSettings()
    {
        EditorGUILayout.PropertyField(edgeLightModeProperty, new GUIContent("Lip Light Mode", "Directional uses one shared direction, Opposing adds light from the opposite direction, and Point derives direction from a shared viewport position."));
        var mode = (GlassEdgeLightMode)edgeLightModeProperty.enumValueIndex;
        if (edgeLightModeProperty.hasMultipleDifferentValues || mode != GlassEdgeLightMode.Point)
        {
            EditorGUILayout.PropertyField(edgeLightAngleProperty, new GUIContent("Lip Light Angle", "Shared screen-space direction for Directional and Opposing lip lighting. Values repeat every 360 degrees."));
        }
        if (edgeLightModeProperty.hasMultipleDifferentValues || mode == GlassEdgeLightMode.Point)
        {
            EditorGUILayout.PropertyField(pointLightPositionProperty, new GUIContent("Point Light Position", "Normalized camera viewport position. Values outside 0–1 place the light beyond the screen."));
            EditorGUILayout.PropertyField(pointLightRadiusProperty, new GUIContent("Point Light Radius", "Screen-space light radius measured as a fraction of the camera viewport height."));
            pointLightRadiusProperty.floatValue = Mathf.Max(0.01f, pointLightRadiusProperty.floatValue);
        }
        EditorGUILayout.PropertyField(edgeLightSpreadProperty, new GUIContent("Lip Light Spread", "Directional/Opposing: width of the falloff centered on each element, measured relative to viewport height. Moving an element does not move it through the falloff. Point: angular highlight width. Zero disables lip lighting."));
        if (edgeLightModeProperty.hasMultipleDifferentValues || mode == GlassEdgeLightMode.Opposing)
            EditorGUILayout.PropertyField(opposingEdgeLightStrengthProperty, new GUIContent("Opposing Light Strength", "Strength of the opposing fill relative to the primary lip light."));
        EditorGUILayout.PropertyField(edgeHighlightProperty, new GUIContent("Lip Highlight", "Highlight color. Alpha controls strength; zero alpha disables highlights."));
        EditorGUILayout.PropertyField(edgeShadowProperty, new GUIContent("Lip Shadow", "Shadow color. Alpha controls strength; zero alpha disables lip shadowing."));
        edgeLightSpreadProperty.floatValue = Mathf.Clamp01(edgeLightSpreadProperty.floatValue);
        opposingEdgeLightStrengthProperty.floatValue = Mathf.Clamp01(opposingEdgeLightStrengthProperty.floatValue);
    }

    private void DrawBlurSettings()
    {
        var integrated = false;
        DrawBlurIntegrationToggle(ref integrated);
        if (integrated)
            DrawBlurIntegrationSettings();
        else
            DrawStandaloneBlurSettings();

        EditorGUILayout.PropertyField(blurPaddingProperty, new GUIContent("Blur Padding", "Additional screen pixels captured around the composed glass bounds."));
        blurPaddingProperty.floatValue = Mathf.Max(0f, blurPaddingProperty.floatValue);
        if (!integrated)
            DrawBlurFormat();

        if (!integrated)
            return;

        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField("Standalone Package Fallback", EditorStyles.boldLabel);
        DrawStandaloneBlurSettings();
        DrawBlurFormat();
    }

    private void DrawStandaloneBlurSettings()
    {
        EditorGUILayout.PropertyField(iterationsProperty, new GUIContent("Blur Pyramid Levels", "Built-in blur downsample and upsample levels. 0 disables blur."));
        iterationsProperty.intValue = Mathf.Clamp(iterationsProperty.intValue, 0, 6);
        EditorGUILayout.PropertyField(sampleRadiusProperty, new GUIContent("Kernel Spread", "Sampling distance normalized to a 1080-pixel render height."));
        EditorGUILayout.PropertyField(ditherStrengthProperty, new GUIContent("Dither Strength", "Noise added to the final built-in blur to reduce color banding."));
        sampleRadiusProperty.floatValue = Mathf.Clamp(sampleRadiusProperty.floatValue, 0.5f, 2f);
        ditherStrengthProperty.floatValue = Mathf.Clamp(ditherStrengthProperty.floatValue, 0f, 5f);
    }

    private void DrawBlurFormat()
    {
        var format = (GraphicsFormat)EditorGUILayout.EnumPopup(new GUIContent("Blur Format", "Built-in blur texture format. Does not affect Flexible Blur."), (GraphicsFormat)blurFormatProperty.intValue);
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
