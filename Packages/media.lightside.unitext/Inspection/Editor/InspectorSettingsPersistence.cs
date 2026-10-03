using UnityEditor;
using UnityEngine;

namespace LightSide.Inspection
{
    /// <summary>Persists inspector settings across domain reloads and editor restarts, scoped to this project.</summary>
    [InitializeOnLoad]
    internal static class InspectorSettingsPersistence
    {
        private const string Prefix = "UniText.Inspector.";

        static InspectorSettingsPersistence()
        {
            Load();
            EditorLifecycle.ManagedCleaning += Save;
            EditorApplication.quitting += Save;
        }

        private static void Load()
        {
            UniTextInspector.Layers = (InspectionLayers)GetInt("Layers", (int)UniTextInspector.Layers);
            UniTextInspector.Filter = (InspectionFilter)GetInt("Filter", (int)UniTextInspector.Filter);
            UniTextInspector.ShowBiDi = GetInt("ShowBiDi", UniTextInspector.ShowBiDi ? 1 : 0) != 0;
            UniTextInspector.ShowStats = GetInt("ShowStats", UniTextInspector.ShowStats ? 1 : 0) != 0;
            UniTextInspector.ToggleKey = (KeyCode)GetInt("ToggleKey", (int)UniTextInspector.ToggleKey);
            UniTextInspector.PinKey = (KeyCode)GetInt("PinKey", (int)UniTextInspector.PinKey);

            if (GetInt("Enabled", 0) != 0)
                UniTextInspector.Enable();
        }

        private static void Save()
        {
            SetInt("Enabled", UniTextInspector.Enabled ? 1 : 0);
            SetInt("Layers", (int)UniTextInspector.Layers);
            SetInt("Filter", (int)UniTextInspector.Filter);
            SetInt("ShowBiDi", UniTextInspector.ShowBiDi ? 1 : 0);
            SetInt("ShowStats", UniTextInspector.ShowStats ? 1 : 0);
            SetInt("ToggleKey", (int)UniTextInspector.ToggleKey);
            SetInt("PinKey", (int)UniTextInspector.PinKey);
        }

        private static int GetInt(string key, int fallback)
            => int.TryParse(EditorUserSettings.GetConfigValue(Prefix + key), out var n) ? n : fallback;

        private static void SetInt(string key, int value)
            => EditorUserSettings.SetConfigValue(Prefix + key, value.ToString());
    }
}
