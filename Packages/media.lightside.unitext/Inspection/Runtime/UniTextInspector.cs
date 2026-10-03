using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide.Inspection
{
    /// <summary>
    /// Entry point for the in-scene text inspector. Toggle it from code on any platform (editor,
    /// play mode, and player builds); the overlay draws glyph / run / line / modifier data for the
    /// UniText under the cursor, or for an explicit <see cref="Target"/>.
    /// </summary>
    public static class UniTextInspector
    {
        static UniTextInspector()
        {
            InspectionCardVisuals.MonoFontName = () => SystemFontCatalog.CommonFamilyName(
                SystemFontCatalog.CommonMonospace, SystemFontCatalog.CurrentPlatform());
        }

        /// <summary>Whether inspection input, cards, and geometry are currently active.</summary>
        public static bool Enabled { get; private set; }

        /// <summary>Which overlay layers to draw.</summary>
        public static InspectionLayers Layers = InspectionLayers.Default;

        /// <summary>Whole-text highlight filter, independent of hover (e.g. mark all .notdef or fallback glyphs).</summary>
        public static InspectionFilter Filter = InspectionFilter.None;

        /// <summary>Overlay direction arrows on every visual run to reveal BiDi reordering.</summary>
        public static bool ShowBiDi;

        /// <summary>Show a whole-component statistics card (chars, glyphs, runs, fonts, scripts, …).</summary>
        public static bool ShowStats;

        /// <summary>Text to inspect; null auto-selects whatever UniText is under the cursor.</summary>
        public static UniTextBase Target;

        /// <summary>Runtime key that toggles the overlay. Editor toggle is Tools ▸ UniText ▸ Inspection Mode.</summary>
        public static KeyCode ToggleKey = KeyCode.F8;

        /// <summary>Runtime key that freezes the current card so the cursor can move freely.</summary>
        public static KeyCode PinKey = KeyCode.P;

        private static InspectorHotkeyListener hotkeyListener;
        private static InspectorOverlay overlay;
        private const string PanelSettingsResource = "UniText Inspection Panel Settings";

        /// <summary>Enables inspection, optionally pinning it to one text component.</summary>
        public static void Enable(UniTextBase target = null)
        {
            Target = target;
            Enabled = true;
            if (Application.isPlaying)
            {
                EnsureHotkeyListener();
                ShowOverlay();
            }
            RepaintEditor();
        }

        /// <summary>Disables inspection while preserving the configured target and layer options.</summary>
        public static void Disable()
        {
            Enabled = false;
            if (overlay != null)
                overlay.gameObject.SetActive(false);
            RepaintEditor();
        }

        /// <summary>Toggles inspection without changing its configured target.</summary>
        public static void Toggle()
        {
            if (Enabled) Disable();
            else Enable(Target);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHotkeyListener()
        {
            if (Debug.isDebugBuild || Application.isEditor)
                EnsureHotkeyListener();
        }

        private static void EnsureHotkeyListener()
        {
            if (hotkeyListener != null) return;
            var go = new GameObject("UniTextInspectorHotkeyListener") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            hotkeyListener = go.AddComponent<InspectorHotkeyListener>();
        }

        private static void ShowOverlay()
        {
            if (overlay != null)
            {
                overlay.gameObject.SetActive(true);
                return;
            }
            var panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource) ??
                                throw new InvalidOperationException(
                                    $"Required inspection panel settings '{PanelSettingsResource}' were not found.");
            var go = new GameObject("UniTextInspectorOverlay") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(go);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = short.MaxValue;
            overlay = go.AddComponent<InspectorOverlay>();
            go.SetActive(true);
        }

        private static void RepaintEditor()
        {
#if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
#endif
        }
    }
}
