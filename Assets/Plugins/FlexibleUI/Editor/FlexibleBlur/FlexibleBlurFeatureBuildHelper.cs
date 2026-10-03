using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.Rendering.Universal;

namespace JeffGrawAssets.FlexibleUI
{
public class FlexibleBlurFeatureBuildHelper : IPreprocessBuildWithReport
{
    public int callbackOrder { get; }
    public void OnPreprocessBuild(BuildReport report)
    {
        var guids = AssetDatabase.FindAssets($"t:{nameof(ScriptableRendererData)}");
        foreach (var guid in guids)
        {
            var srd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var feature in srd.rendererFeatures)
            {
                if (feature is not FlexibleBlurFeature blurFeature)
                    continue;

                blurFeature.UsePlatformSettings(report.summary.platform);
            }
        }
    }
}
}