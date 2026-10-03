using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace JeffGrawAssets.FlexibleUI
{
public partial class FlexibleGlassFeatureEditor
{
    private SerializedProperty useFlexibleBlurProperty, flexibleBlurFeatureNumberProperty, flexibleBlurPresetProperty, flexibleBlurSettingsProperty;
    private ReorderableList downscaleSectionList, blurSectionList;
    private BlurPresetEditor blurPresetEditor;
    private BlurPreset activePreset;

    partial void OnEnableBlurIntegration()
    {
        useFlexibleBlurProperty = serializedObject.FindProperty(FlexibleGlassFeature.UseFlexibleBlurFieldName);
        flexibleBlurFeatureNumberProperty = serializedObject.FindProperty(FlexibleGlassFeature.FlexibleBlurFeatureNumberFieldName);
        flexibleBlurPresetProperty = serializedObject.FindProperty(FlexibleGlassFeature.FlexibleBlurPresetFieldName);
        flexibleBlurSettingsProperty = serializedObject.FindProperty(FlexibleGlassFeature.FlexibleBlurSettingsFieldName);
    }

    partial void OnDisableBlurIntegration()
    {
        if (blurPresetEditor)
            DestroyImmediate(blurPresetEditor);
        blurPresetEditor = null;
        activePreset = null;
    }

    partial void DrawBlurIntegrationToggle(ref bool integrated)
    {
        EditorGUILayout.PropertyField(useFlexibleBlurProperty, new GUIContent("Use Flexible Blur", "Have the selected FlexibleBlurFeature write this feature's blurred backdrop texture."));
        integrated = useFlexibleBlurProperty.boolValue;
    }

    partial void DrawBlurIntegrationSettings()
    {
        EditorGUILayout.PropertyField(flexibleBlurFeatureNumberProperty, new GUIContent("Flexible Blur Feature #", "Zero-based FlexibleBlurFeature number. Other renderer-feature types are not counted."));
        DrawFlexibleBlurFeatureDiagnostics();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(flexibleBlurPresetProperty, new GUIContent("Blur Preset", "Optional reusable Flexible Blur quality and algorithm settings."));
        var createPreset = GUILayout.Button("New", GUILayout.Width(44f));
        EditorGUILayout.EndHorizontal();
        if (createPreset)
        {
            var newPreset = CreateInstance<BlurPreset>();
            newPreset.TryFillSettings();
            var instanceSettings = flexibleBlurSettingsProperty.boxedValue as BlurSettings;
            foreach (var qualitySettings in newPreset.Settings)
                qualitySettings.CopySettings(instanceSettings);
            flexibleBlurPresetProperty.objectReferenceValue = newPreset;
            serializedObject.ApplyModifiedProperties();
            AssetDatabase.CreateAsset(newPreset, PresetSavePath.GetPresetSavePath("New Glass Blur Preset.asset"));
            AssetDatabase.SaveAssets();
            GUIUtility.ExitGUI();
        }
        var preset = flexibleBlurPresetProperty.objectReferenceValue as BlurPreset;
        if (!preset)
        {
            BlurPresetEditor.DrawBlurProperties(flexibleBlurSettingsProperty.Copy(), ref downscaleSectionList, ref blurSectionList);
            return;
        }

        if (activePreset != preset)
        {
            if (blurPresetEditor)
                DestroyImmediate(blurPresetEditor);
            activePreset = preset;
            blurPresetEditor = CreateEditor(preset) as BlurPresetEditor;
        }

        blurPresetEditor?.DrawGUI();
    }

    private void DrawFlexibleBlurFeatureDiagnostics()
    {
        var glassFeature = target as FlexibleGlassFeature;
        FlexibleGlassEditorUtility.GetFeatureNumber(glassFeature, out var rendererData);
        if (!rendererData)
            return;

        var requestedNumber = Mathf.Max(0, flexibleBlurFeatureNumberProperty.intValue);
        FlexibleBlurFeature blurFeature = null;
        var blurNumber = 0;
        foreach (var feature in rendererData.rendererFeatures)
        {
            if (feature is not FlexibleBlurFeature candidate)
                continue;
            if (blurNumber++ != requestedNumber)
                continue;
            blurFeature = candidate;
            break;
        }

        if (!blurFeature)
        {
            EditorGUILayout.HelpBox($"FlexibleBlurFeature #{requestedNumber} is missing from this renderer.", MessageType.Error);
            return;
        }

        var blurSerializedObject = new SerializedObject(blurFeature);
        var blurEvent = (RenderPassEvent)blurSerializedObject.FindProperty(FlexibleBlurFeature.RenderPassEventFieldName).intValue;
        var glassEvent = (RenderPassEvent)serializedObject.FindProperty(FlexibleGlassFeature.RenderPassEventFieldName).intValue;
        var blurIndex = rendererData.rendererFeatures.IndexOf(blurFeature);
        var glassIndex = rendererData.rendererFeatures.IndexOf(glassFeature);
        if (blurEvent > glassEvent || blurEvent == glassEvent && blurIndex > glassIndex)
            EditorGUILayout.HelpBox("The selected FlexibleBlurFeature must render before this FlexibleGlassFeature.", MessageType.Error);
    }
}
}
