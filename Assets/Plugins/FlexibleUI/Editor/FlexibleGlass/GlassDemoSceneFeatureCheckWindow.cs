using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace JeffGrawAssets.FlexibleUI
{
[InitializeOnLoad]
public sealed class GlassDemoSceneFeatureCheckWindow : EditorWindow
{
    [SerializeField] private List<ScriptableRendererData> missingRenderers = new();

    static GlassDemoSceneFeatureCheckWindow() => EditorSceneManager.sceneOpened += CheckDemoScene;

    private static void CheckDemoScene(Scene scene, OpenSceneMode mode)
    {
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        var missing = FindMissingRenderers(scene);
        if (missing.Count == 0)
            return;

        var window = GetWindow<GlassDemoSceneFeatureCheckWindow>(true, "Flexible Glass Demo", true);
        window.missingRenderers = missing;
        window.minSize = new Vector2(460f, 145f + missing.Count * 34f);
        window.Show();
    }

    private static List<ScriptableRendererData> FindMissingRenderers(Scene scene)
    {
        var missing = new List<ScriptableRendererData>();
        if (!scene.IsValid() || !scene.isLoaded ||
            !(scene.path.EndsWith("/Demos/FlexibleGlass/Scenes/FlexibleGlass Desktop.unity", StringComparison.OrdinalIgnoreCase) ||
              scene.path.EndsWith("/Demos/FlexibleGlass/Scenes/FlexibleGlass Shapes.unity", StringComparison.OrdinalIgnoreCase)))
            return missing;

        foreach (var root in scene.GetRootGameObjects())
        foreach (var camera in root.GetComponentsInChildren<Camera>(true))
        {
            FlexibleGlassEditorUtility.GetFeatureCount(camera, out var renderer);
            if (!renderer || missing.Contains(renderer))
                continue;
            var hasActiveGlass = false;
            foreach (var feature in renderer.rendererFeatures)
                if (feature is FlexibleGlassFeature)
                {
                    hasActiveGlass = feature.isActive;
                    break;
                }
            if (!hasActiveGlass)
                missing.Add(renderer);
        }
        return missing;
    }

    private void OnGUI()
    {
        GUILayout.Space(8f);
        EditorGUILayout.HelpBox("This demo uses your project's default renderer. Add or enable its first Flexible Glass Feature, leaving Render Pass Event at After Rendering Post Processing. No additional renderer features or custom layers are needed.", MessageType.Info);
        EditorGUILayout.LabelField("No features or project settings are changed automatically.", EditorStyles.wordWrappedLabel);
        GUILayout.Space(8f);
        foreach (var renderer in missingRenderers)
        {
            if (!renderer || !GUILayout.Button($"Open {renderer.name}", GUILayout.Height(28f)))
                continue;
            Selection.activeObject = renderer;
            EditorGUIUtility.PingObject(renderer);
            Close();
        }
    }
}
}
