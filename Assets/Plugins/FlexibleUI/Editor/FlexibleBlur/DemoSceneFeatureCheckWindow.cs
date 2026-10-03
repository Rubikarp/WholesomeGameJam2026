using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace JeffGrawAssets.FlexibleUI
{
[InitializeOnLoad]
public class DemoSceneFeatureCheckWindow : EditorWindow
{
    const float WindowWidth  = 500f;
    const float WindowHeight = 80;

    private ScriptableRendererData rendererData;

    static DemoSceneFeatureCheckWindow() => EditorSceneManager.sceneOpened += StartupCheck;

    static void StartupCheck(Scene scene, OpenSceneMode _)
    {
        // Only show the popup when we're loading a demo scene. In any other scene, the UIBlur or FlexibleImage component will provide directions when added, but in a demo scene the user may not check the existing components.
        var lowerPath = scene.path.ToLower();
        if (!lowerPath.Contains("flexibleblur") || !lowerPath.Contains("scenes"))
            return;

        var pipelineAsset = QualitySettings.GetRenderPipelineAssetAt(QualitySettings.GetQualityLevel()) as UniversalRenderPipelineAsset;
        if (pipelineAsset == null)
        {
            pipelineAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (pipelineAsset == null)
                return;
        }

        // If any renderer contains the feature, fair to say the user knows what they are doing.
#if UNITY_2023_2_OR_NEWER
        if (pipelineAsset.rendererDataList.ToArray().Any(x => x != null && x.TryGetRendererFeature<FlexibleBlurFeature>(out var _)))
            return;
#else
        var dataListField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
        var dataListObj = dataListField?.GetValue(pipelineAsset);
        if (dataListObj is not ScriptableRendererData[] dataList || dataList.SelectMany(x => x.rendererFeatures).Any(x => x is FlexibleBlurFeature))
            return;
#endif

        var window = GetWindowWithRect<DemoSceneFeatureCheckWindow>
        (
            new Rect(0.5f * (Screen.width - WindowWidth), 0.5f * (Screen.height - WindowHeight), WindowWidth, WindowHeight), true, "Flexible Blur", true
        );

        var defaultRendererIdxField = typeof(UniversalRenderPipelineAsset).GetField("m_DefaultRendererIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        var defaultRendererIdxFieldObj = defaultRendererIdxField?.GetValue(pipelineAsset);
        int defaultRendererIdx = defaultRendererIdxFieldObj is int obj ? obj : 0;

#if UNITY_2023_2_OR_NEWER
        window.rendererData = pipelineAsset.rendererDataList[defaultRendererIdx];
#else
        window.rendererData = dataList[defaultRendererIdx];
#endif
    }

     void OnGUI()
     {
         var mainMessage = new GUIStyle(EditorStyles.label)
         {
             alignment = TextAnchor.MiddleCenter,
             fontSize = 16
         };
         
         GUILayout.Space(6);
         GUILayout.Label($"{nameof(FlexibleBlurFeature)} should be present in at least one renderer.", mainMessage);
         GUILayout.FlexibleSpace();

         if (!GUILayout.Button("Open DEFAULT Renderer", GUILayout.Height(36)))
             return;

         EditorGUIUtility.PingObject(rendererData);
         Selection.activeObject = rendererData;
         Close();
     }
}
}