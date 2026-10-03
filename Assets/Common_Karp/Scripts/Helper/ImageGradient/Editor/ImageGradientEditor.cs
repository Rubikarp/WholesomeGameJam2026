#if UNITY_EDITOR
using UnityEditor.UI;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ImageGradient))]
public class ImageGradientEditor : ImageEditor
{
    private ImageGradient gradientImage;

    protected override void OnEnable()
    {
        base.OnEnable();
        gradientImage = (ImageGradient)target;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        // Use the public properties to ensure SetVerticesDirty() is called
        Color fromColor = EditorGUILayout.ColorField("From Color", gradientImage.fromColor);
        Color toColor = EditorGUILayout.ColorField("To Color", gradientImage.toColor);
        EGradientDir gradientDir = (EGradientDir)EditorGUILayout.EnumPopup("Gradient Direction", gradientImage.gradientDirection);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(gradientImage, "Changed Gradient Colors");

            gradientImage.fromColor = fromColor;
            gradientImage.toColor = toColor;
            gradientImage.gradientDirection = gradientDir;

            gradientImage.SetVerticesDirty();
            EditorUtility.SetDirty(gradientImage);
        }

        base.OnInspectorGUI();
        serializedObject.ApplyModifiedProperties();
    }
}

#endif