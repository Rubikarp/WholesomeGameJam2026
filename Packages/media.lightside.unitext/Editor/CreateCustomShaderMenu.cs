using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal static class CreateCustomShaderMenu
    {
        /// <summary>Generates the effect include plus its Canvas and World shells into the selected folder.
        /// The effect file is pipeline-neutral and stays verbatim; the shells keep their relative include of
        /// the co-located effect and only have the package-prelude includes rewritten to resolved asset
        /// paths.</summary>
        [MenuItem(UniTextMenu.Create.CustomMaterialShader, false, 200)]
        internal static void Create()
        {
            if (!TryLocate("t:Shader LightSide_Custom-Example", "/LightSide_Custom-Example.shader", out var canvasTemplate) ||
                !TryLocate("t:Shader LightSide_Custom-World-Example", "/LightSide_Custom-World-Example.shader", out var worldTemplate) ||
                !TryLocate("LightSide_Effect-Example", "/LightSide_Effect-Example.hlsl", out var effectTemplate) ||
                !TryLocate("LightSide_Custom t:TextAsset", "/LightSide_Custom.cginc", out var cginc) ||
                !TryLocate("LightSide_Custom-URP", "/LightSide_Custom-URP.hlsl", out var urp))
            {
                Debug.LogError(
                    "[UniText] Custom-effect templates not found. Reinstall the package or verify that " +
                    "Packages/media.lightside.unitext/Shaders/Templates exists.");
                return;
            }

            var folder = ResolveTargetFolder();
            var stem = "UniTextEffect";
            for (var n = 1; AnyOutputExists(folder, stem); n++)
                stem = $"UniTextEffect {n}";
            var effectPath = $"{folder}/{stem}.hlsl";
            var canvasPath = $"{folder}/{stem}.shader";
            var worldPath = $"{folder}/{stem}-World.shader";
            var shaderName = UniqueShaderName(stem);

            File.WriteAllText(Abs(effectPath), File.ReadAllText(Abs(effectTemplate)));

            File.WriteAllText(Abs(canvasPath), File.ReadAllText(Abs(canvasTemplate))
                .Replace("\"../LightSide_Custom.cginc\"", $"\"{cginc}\"")
                .Replace("\"LightSide_Effect-Example.hlsl\"", $"\"{stem}.hlsl\"")
                .Replace("Shader \"LightSide/Custom/Example\"", $"Shader \"{shaderName}\""));

            File.WriteAllText(Abs(worldPath), File.ReadAllText(Abs(worldTemplate))
                .Replace("\"../LightSide_Custom.cginc\"", $"\"{cginc}\"")
                .Replace("\"../LightSide_Custom-URP.hlsl\"", $"\"{urp}\"")
                .Replace("\"LightSide_Effect-Example.hlsl\"", $"\"{stem}.hlsl\"")
                .Replace("Shader \"LightSide/Custom/World Example\"", $"Shader \"{shaderName} World\""));

            AssetDatabase.Refresh();
            var effect = AssetDatabase.LoadAssetAtPath<Object>(effectPath);
            Selection.activeObject = effect;
            EditorGUIUtility.PingObject(effect);
        }

        private static bool TryLocate(string filter, string suffix, out string assetPath)
        {
            var guids = AssetDatabase.FindAssets(filter);
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith(suffix))
                {
                    assetPath = path;
                    return true;
                }
            }
            assetPath = null;
            return false;
        }

        private static string ResolveTargetFolder()
        {
            foreach (var obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;
                if (File.Exists(path)) path = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(path)) return path.Replace("\\", "/");
            }
            return "Assets";
        }

        private static bool AnyOutputExists(string folder, string stem) =>
            File.Exists(Abs($"{folder}/{stem}.hlsl")) ||
            File.Exists(Abs($"{folder}/{stem}.shader")) ||
            File.Exists(Abs($"{folder}/{stem}-World.shader"));

        /// <summary>
        /// A duplicate <c>Shader "name"</c> anywhere in the project is a compile conflict even when the
        /// files live in different folders, so the stem-derived name is probed via <see cref="Shader.Find"/>
        /// and numbered until free.
        /// </summary>
        private static string UniqueShaderName(string stem)
        {
            var name = $"LightSide/Custom/{stem}";
            for (var n = 2; Shader.Find(name) != null || Shader.Find(name + " World") != null; n++)
                name = $"LightSide/Custom/{stem} {n}";
            return name;
        }

        private static string Abs(string assetPath) => Path.GetFullPath(assetPath);
    }
}
