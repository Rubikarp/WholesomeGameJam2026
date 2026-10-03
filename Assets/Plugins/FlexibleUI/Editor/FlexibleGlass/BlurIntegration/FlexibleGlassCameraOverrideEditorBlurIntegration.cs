using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
public partial class FlexibleGlassCameraOverrideEditor
{
    private SerializedProperty useFlexibleBlurProperty;
    private SerializedProperty flexibleBlurFeatureNumberProperty;
    private SerializedProperty flexibleBlurPresetProperty;
    private SerializedProperty flexibleBlurSettingsProperty;
    private ReorderableList downscaleSectionList;
    private ReorderableList blurSectionList;
    private BlurPresetEditor blurPresetEditor;
    private BlurPreset activePreset;

    partial void OnEnableBlurIntegration()
    {
        useFlexibleBlurProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.UseFlexibleBlurFieldName);
        flexibleBlurFeatureNumberProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.FlexibleBlurFeatureNumberFieldName);
        flexibleBlurPresetProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.FlexibleBlurPresetFieldName);
        flexibleBlurSettingsProperty = serializedObject.FindProperty(FlexibleGlassCameraOverride.FlexibleBlurSettingsFieldName);
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
        EditorGUILayout.PropertyField(useFlexibleBlurProperty, new GUIContent("Use Flexible Blur", "Have the selected FlexibleBlurFeature generate this camera's Glass backdrop."));
        integrated = useFlexibleBlurProperty.boolValue;
    }

    partial void DrawBlurIntegrationSettings()
    {
        EditorGUILayout.PropertyField(flexibleBlurFeatureNumberProperty, new GUIContent("Flexible Blur Feature #", "Zero-based FlexibleBlurFeature number. Other renderer-feature types are not counted."));
        flexibleBlurFeatureNumberProperty.intValue = Mathf.Max(0, flexibleBlurFeatureNumberProperty.intValue);
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
            AssetDatabase.CreateAsset(newPreset, PresetSavePath.GetPresetSavePath("New Glass Camera Blur Preset.asset"));
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
}
}
