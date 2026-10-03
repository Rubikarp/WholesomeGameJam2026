using UnityEditor;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[CustomEditor(typeof(GlassReferenceProvider))]
public class GlassReferenceProviderEditor : Editor
{
    private SerializedProperty cameraReferenceProperty;
    private SerializedProperty featureNumberProperty;

    private void OnEnable()
    {
        cameraReferenceProperty = serializedObject.FindProperty(GlassReferenceProvider.CameraReferenceFieldName);
        featureNumberProperty = serializedObject.FindProperty(GlassReferenceProvider.FeatureNumberFieldName);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("Canvas Glass Source", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(cameraReferenceProperty, FlexibleGlassEditorUtility.CameraContent);
        if (!cameraReferenceProperty.objectReferenceValue && Camera.main && GUILayout.Button("Assign Camera.main"))
            cameraReferenceProperty.objectReferenceValue = Camera.main;

        var camera = cameraReferenceProperty.objectReferenceValue as Camera;
        var featureCount = FlexibleGlassEditorUtility.GetFeatureCount(camera ? camera : Camera.main, out _);
        if (featureCount > 1 || featureNumberProperty.intValue != 0)
            EditorGUILayout.PropertyField(featureNumberProperty, FlexibleGlassEditorUtility.FeatureNumberContent);
        featureNumberProperty.intValue = Mathf.Max(0, featureNumberProperty.intValue);
        FlexibleGlassEditorUtility.DrawDiagnostics(camera, featureNumberProperty.intValue);
        EditorGUILayout.EndVertical();
        serializedObject.ApplyModifiedProperties();
    }
}
}
