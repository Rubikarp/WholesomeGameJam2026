using UnityEngine;
using UnityEditor;

namespace JeffGrawAssets.FlexibleUI
{
[CustomEditor(typeof(ColorPreset))]
public class ColorPresetEditor : Editor
{
    private static readonly GUIContent PrimaryContent = new ("Primary");
    private static readonly GUIContent OutlineContent = new ("Outline");
    private static readonly GUIContent ProceduralGradientContent = new ("Procedural Gradient");
    private static readonly GUIContent PatternContent = new ("Pattern");

    private ColorPreset colorPreset;
    private SerializedProperty primaryColorProperty, outlineColorProperty, proceduralGradientColorProperty, patternColorProperty;
    private Color prevPrimaryColor, prevOutlineColor, prevProceduralGradientColor, prevPatternColor;

    private void OnEnable()
    {
        colorPreset = serializedObject.targetObject as ColorPreset;
        primaryColorProperty = serializedObject.FindProperty(ColorPreset.PrimaryColorFieldName);
        outlineColorProperty = serializedObject.FindProperty(ColorPreset.OutlineColorFieldName);
        proceduralGradientColorProperty = serializedObject.FindProperty(ColorPreset.ProceduralGradientColorFieldName);
        patternColorProperty = serializedObject.FindProperty(ColorPreset.PatternColorFieldName);
        prevPrimaryColor = primaryColorProperty.colorValue;
        prevOutlineColor = outlineColorProperty.colorValue;
        prevProceduralGradientColor = proceduralGradientColorProperty.colorValue;
        prevPatternColor = patternColorProperty.colorValue;
    }

    public override void OnInspectorGUI() => DrawGUI();

    public void DrawGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(primaryColorProperty, PrimaryContent);
        EditorGUILayout.PropertyField(outlineColorProperty, OutlineContent);
        EditorGUILayout.PropertyField(proceduralGradientColorProperty, ProceduralGradientContent);
        EditorGUILayout.PropertyField(patternColorProperty, PatternContent);

        var (newPrimaryColor, newOutlineColor, newProceduralGradientColor, newPatternColor) = (primaryColorProperty.colorValue, outlineColorProperty.colorValue, proceduralGradientColorProperty.colorValue, patternColorProperty.colorValue);
        if (newPrimaryColor == prevPrimaryColor && newProceduralGradientColor == prevProceduralGradientColor && newOutlineColor == prevOutlineColor && newPatternColor == prevPatternColor)
            return;

        (prevPrimaryColor, prevOutlineColor, prevProceduralGradientColor, prevPatternColor) = (newPrimaryColor, newOutlineColor, newProceduralGradientColor, newPatternColor);
        serializedObject.ApplyModifiedProperties();
        colorPreset.ForceInvokeColorChangeEvent();
    }
}
}