using UnityEditor;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[CustomEditor(typeof(BlurReferenceProvider))]
public class BlurReferenceProviderEditor : Editor
{
    private GameObject go;
    private SerializedProperty cameraProperty;
    private SerializedProperty featureNumberProperty;
    
    void OnEnable()
    {
        go = ((BlurReferenceProvider)serializedObject.targetObject).gameObject;
        cameraProperty = serializedObject.FindProperty(BlurReferenceProvider.CameraReferenceFieldName);
        featureNumberProperty = serializedObject.FindProperty(BlurReferenceProvider.FeatureNumberFieldName);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        UIBlurEditor.DrawBlurCommonPropertiesOne(null, cameraProperty, featureNumberProperty, go, nameof(BlurReferenceProvider));
        serializedObject.ApplyModifiedProperties();
    }
}
}
