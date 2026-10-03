#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

namespace JeffGrawAssets.FlexibleUI
{
public class TextureExportEditorWindow : EditorWindow
{
    private static int superSample = 4;
    private static bool addPadding;

    private FlexibleImage target; 
    private int textureWidth;
    private int textureHeight;
    private string path;

    public static void ShowWindow(FlexibleImage target)
    {
        var window = GetWindow<TextureExportEditorWindow>("Texture Exporter");
        window.target = target;
        //Todo: iterate through quads to get the desired resolution of the entire image. Currently assumes all quads are anchored to consume the full RectTransform Rect.
        var quadData = target.PrimaryQuadData;
        var additionalHeight = quadData.ScreenSpacePattern || quadData.ScreenSpaceProceduralGradient ? 40 : 0;
        window.minSize = window.maxSize = new Vector2(480, 200 + additionalHeight);
        var pixelsPerUnit = target.canvas.scaleFactor;
        var sizeModifier = quadData.GetSizeModifier(target.rectTransform);
        window.textureWidth = Mathf.RoundToInt((sizeModifier.x + target.rectTransform.rect.width) * pixelsPerUnit);
        window.textureHeight = Mathf.RoundToInt((sizeModifier.y + target.rectTransform.rect.height) * pixelsPerUnit);
        if (quadData.OutlineExpandsOutward)
        {
            var outlineExpansion = Mathf.RoundToInt(quadData.GetOutlineWidth(target.rectTransform) * 2);
            window.textureWidth += outlineExpansion;
            window.textureHeight += outlineExpansion;
        }
        window.path = $"BakedTextures/{target.name}.png";
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField($"Source: {target.name}", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        textureWidth = Mathf.Max(1, EditorGUILayout.IntField("Width", textureWidth));
        textureHeight = Mathf.Max(1, EditorGUILayout.IntField("Height", textureHeight));
        superSample = Mathf.Max(1, EditorGUILayout.IntField("Super Sample", superSample));
        addPadding = EditorGUILayout.Toggle("1px Padding", addPadding);
        GUI.enabled = superSample > 1;
        GUI.enabled = true;
        path = EditorGUILayout.TextField("Save Path (from Assets/)", path);
        EditorGUILayout.Space();
        var pathIsValid = !string.IsNullOrEmpty(path);
        var sizeIsValid = textureWidth > 0 && textureHeight > 0;

        if (pathIsValid)
        {
            var fullPath = Path.Combine(Application.dataPath, path);
            if (File.Exists(fullPath))
                EditorGUILayout.HelpBox("This file already exists and will be overwritten.", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox("Path cannot be empty.", MessageType.Error);
        }

        var quadDataContainer = target.ActiveQuadDataContainer;
        for (int i = 0; i < quadDataContainer.Count; i++)
        {
            var quadData = quadDataContainer[i];
            if (quadData.ScreenSpacePattern || quadData.ScreenSpaceProceduralGradient)
            {
                EditorGUILayout.HelpBox("Screen Space Pattern\\Procedural Gradient is not recommended.", MessageType.Warning);
                break;
            }
        }

        GUI.enabled = pathIsValid && sizeIsValid;
        if (GUILayout.Button("Bake"))
        {
            target.BakeToTexture(textureWidth, textureHeight, superSample, addPadding, path);
            EditorUtility.SetDirty(target);
            Close();
        }
        GUI.enabled = true;
    }
}
}
#endif