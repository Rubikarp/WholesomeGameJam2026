using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
public class FlexibleImageGlobalSettingsWindow : EditorWindow
{
    private const string SoftMaskPackagePath = "Packages/com.olegknyazev.softmask";
    private const string SoftMaskGitURL = "https://github.com/olegknyazev/SoftMask";
    private const string SoftMaskPackageURL = "https://github.com/olegknyazev/SoftMask.git?path=/Packages/com.olegknyazev.softmask#1.7.0";

    private Vector2 scroll;
    private AddRequest addRequest;

    [MenuItem("Tools/FlexibleUI/Flexible Image Global Settings")]
    private static void Open()
    {
        var window = GetWindow<FlexibleImageGlobalSettingsWindow>(false, "FI Global Settings", true);
        window.Show();
    }

    private void OnEnable() => FlexibleImageFeatureManager.Reload();

    private void OnGUI()
    {
        var features = FlexibleImageFeatureManager.GetFeatures();
        EditorGUILayout.HelpBox("Toggle features on/off (somewhat experimental).\nDisable everything you don't use to reduce overhead.\nDisabling features *will not* adjust existing use in components, which can lead to undefined behaviour.", MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("Color Order", GUILayout.Width(75));
            var oldOrder = FlexibleImageFeatureManager.GetSectionOrder();
            var newOrder = (ProceduralGradientPatternOrder)EditorGUILayout.EnumPopup(oldOrder);
            EditorGUILayout.EndHorizontal();
            if (newOrder != oldOrder)
                FlexibleImageFeatureManager.SetSectionOrder(newOrder);
        }

        var softMaskSupportEnabled = FlexibleImageFeatureManager.IsConditionalBlockEnabled(FlexibleImageFeatureManager.SoftMaskConditionalID);
        var softMaskPackageInstalled = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(SoftMaskPackagePath) != null;
        if (softMaskPackageInstalled)
        {
            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("SoftMask Integration", GUILayout.Width(128));
            GUI.enabled = !softMaskSupportEnabled;
            if (GUILayout.Button("Enable"))
                FlexibleImageFeatureManager.SetConditionalBlockEnabled(FlexibleImageFeatureManager.SoftMaskConditionalID, true);
            GUI.enabled = softMaskSupportEnabled;
            if (GUILayout.Button("Disable"))
                FlexibleImageFeatureManager.SetConditionalBlockEnabled(FlexibleImageFeatureManager.SoftMaskConditionalID, false);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            var alreadyInstallingPackage = addRequest != null && !addRequest.IsCompleted;
            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            EditorGUILayout.LabelField("SoftMask Integration Not Available",GUILayout.Width(203));
            if (GUILayout.Button("About"))
                Application.OpenURL(SoftMaskGitURL);

            GUI.enabled = !alreadyInstallingPackage;
            if (GUILayout.Button(alreadyInstallingPackage ? "Working..." : "Add Package"))
                addRequest = UnityEditor.PackageManager.Client.Add(SoftMaskPackageURL);

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var feature in features)
            DrawFeatureEntry(feature);

        EditorGUILayout.EndScrollView();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reload from Disk", GUILayout.Height(24)))
                FlexibleImageFeatureManager.Reload();

            if (GUILayout.Button("Defaults", GUILayout.Height(24)))
            {
                SetAllFeatures(true, false);
                FlexibleImageFeatureManager.SetSectionOrder(ProceduralGradientPatternOrder.ProceduralGradientBeforePattern, false);
                FlexibleImageFeatureManager.SetConditionalBlockEnabled(FlexibleImageFeatureManager.SoftMaskConditionalID, false, false);
                FlexibleImageFeatureManager.RecompileShader();
            }
            if (GUILayout.Button("Disable All", GUILayout.Height(24)))
            {
                SetAllFeatures(false, false);
                FlexibleImageFeatureManager.SetConditionalBlockEnabled(FlexibleImageFeatureManager.SoftMaskConditionalID, false, false);
                FlexibleImageFeatureManager.RecompileShader();
            }
        }
    }

    private void DrawFeatureEntry(ShaderFeatureEntry feature)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        var featureEnabled = EditorGUILayout.Toggle(feature.IsEnabled, GUILayout.Width(16f));
        if (featureEnabled != feature.IsEnabled)
            FlexibleImageFeatureManager.SetFeatureEnabled(feature.Define, featureEnabled);

        GUI.enabled = featureEnabled;
        GUILayout.Label(FormatDisplayName(feature.Define), EditorStyles.label, GUILayout.ExpandWidth(true));
        GUI.enabled = true;

        if (feature.HasSubFeatures)
        {
            GUILayout.FlexibleSpace();

            // GUI.enabled = false;
            // GUILayout.Label(feature.IsFoldoutOpen ? "▼" : "▶");
            // GUI.enabled = true;

            var foldoutClickArea = GUILayoutUtility.GetLastRect();
            foldoutClickArea.width = float.PositiveInfinity;
            foldoutClickArea.x = 16;
            foldoutClickArea.height += 4;
            foldoutClickArea.y -= 2;

            // if (GUI.Button(foldoutClickArea, "", GUIStyle.none))
            //     feature.IsFoldoutOpen = !feature.IsFoldoutOpen;
        }
        EditorGUILayout.EndHorizontal();

        if (feature.HasSubFeatures)
        {
            foreach (var sub in feature.SubFeatures)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(24f);
                var subFeatureEnabled = EditorGUILayout.Toggle(sub.IsEnabled, GUILayout.Width(16f));
                if (subFeatureEnabled != sub.IsEnabled)
                    FlexibleImageFeatureManager.SetFeatureEnabled(sub.Define, subFeatureEnabled);
                
                GUI.enabled = featureEnabled && subFeatureEnabled;
                GUILayout.Label(FormatSubDisplayName(sub.Define, feature.Define), EditorStyles.label, GUILayout.ExpandWidth(true));
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.EndVertical();
        GUILayout.Space(2f);
    }

    private static string FormatDisplayName(string define)
    {
        var stripped = Regex.Replace(define, "^(Feature|SubFeature)", "");
        return Regex.Replace(stripped, "(?<=[a-z])(?=[A-Z])", " ");
    }

    private static string FormatSubDisplayName(string subDefine, string parentDefine)
    {
        var parentCore = Regex.Replace(parentDefine, "^Feature", "");
        var subCore = Regex.Replace(subDefine, "^SubFeature", "");

        if (subCore.StartsWith(parentCore))
            subCore = subCore.Substring(parentCore.Length).TrimStart();

        return Regex.Replace(subCore, "(?<=[a-z])(?=[A-Z])", " ");
    }

    private static void SetAllFeatures(bool enabled, bool recompile = true)
    {
        foreach (var feature in FlexibleImageFeatureManager.GetFeatures())
        {
            FlexibleImageFeatureManager.SetFeatureEnabled(feature.Define, enabled, recompile);
            foreach (var sub in feature.SubFeatures)
                FlexibleImageFeatureManager.SetFeatureEnabled(sub.Define, enabled, recompile);
        }
    }
}
}