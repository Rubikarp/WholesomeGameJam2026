using System.IO;
using UnityEditor;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
public static class PresetSavePath
{
    private const string PresetDirectory = "UI/Presets";
    public static string GetPresetSavePath(string assetName)
    {
        var fullPath = Path.Combine(Application.dataPath, PresetDirectory);
        if (!Directory.Exists(fullPath))
            Directory.CreateDirectory(fullPath);

        var path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine("Assets", PresetDirectory, assetName));
        return path;
    }
}
}
