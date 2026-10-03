#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Editor-only Scene view overlay and global toggle that controls whether <see cref="UniTextBase"/>
    /// components react to Unity's Scene Visibility (the eye icon in Hierarchy). When the toggle is off,
    /// hiding a UniText GameObject in the Hierarchy no longer clears its rendered text — components
    /// behave as if the visibility flag were not set. State is per-developer (stored in
    /// <see cref="EditorPrefs"/>) and survives domain reload.
    /// </summary>
    /// <remarks>
    /// Why this exists: UniText renders through a shared batched mesh owned by
    /// <c>UniTextWorldBatcher</c>'s hidden GameObjects, not through the component's own renderer.
    /// Unity's Scene Visibility system only suppresses rendering of the GameObject it targets, so the
    /// batcher cannot honor the eye-icon state without explicit handling. The explicit handling clears
    /// the entry from the batch in <em>both</em> Scene and Game views, which is broader than Unity's
    /// usual "Scene-view-only" semantics. This toggle lets developers opt out of that behavior.
    /// </remarks>
    [Overlay(typeof(SceneView), OverlayId, "❤️", true)]
    [Icon("Packages/media.lightside.unitext/Editor/Resources/UniText/Icons/unitext-visibility-icon.png")]
    internal sealed class SceneVisibilityOverlay : Overlay, ITransientOverlay
    {
        private const string OverlayId = "lightside-unitext-scene-visibility";
        private const string PrefKey = "LightSide.UniText.RespectSceneVisibility";
        private const string ShowPrefKey = "LightSide.UniText.ShowSceneVisibilityOverlay";
        private const string IconResourcePath = "UniText/Icons/unitext-visibility-icon";
        private const float IconSize = 16f;

        private static readonly Color darkThemeTint = new(0.85f, 0.85f, 0.85f, 1f);
        private static readonly Color lightThemeTint = new(0.2f, 0.2f, 0.2f, 1f);

        private static bool cached;
        private static bool cacheLoaded;
        private static bool cachedShow;
        private static bool cacheShowLoaded;
        private Toggle respectToggle;

        public static event Action Changed;

        public static bool Respect
        {
            get
            {
                if (!cacheLoaded)
                {
                    cached = EditorPrefs.GetBool(PrefKey, true);
                    cacheLoaded = true;
                }
                return cached;
            }
            set
            {
                if (cacheLoaded && cached == value) return;
                cached = value;
                cacheLoaded = true;
                EditorPrefs.SetBool(PrefKey, value);
                Menu.SetChecked(UniTextMenu.Tools.RespectSceneVisibility, value);
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// Whether the UniText Scene Visibility overlay is shown in the SceneView toolbar.
        /// Stored in <see cref="EditorPrefs"/> (per-developer, shared across all Unity projects on
        /// the machine). Defaults to <c>false</c>: the overlay is invisible until the developer
        /// opts in via <c>Tools/UniText/Show Scene Visibility Overlay</c>. The behavior toggle
        /// <see cref="Respect"/> remains independently accessible from the same menu.
        /// </summary>
        public static bool Show
        {
            get
            {
                if (!cacheShowLoaded)
                {
                    cachedShow = EditorPrefs.GetBool(ShowPrefKey, false);
                    cacheShowLoaded = true;
                }
                return cachedShow;
            }
            set
            {
                if (cacheShowLoaded && cachedShow == value) return;
                cachedShow = value;
                cacheShowLoaded = true;
                EditorPrefs.SetBool(ShowPrefKey, value);
                Menu.SetChecked(UniTextMenu.Tools.ShowSceneVisibilityOverlay, value);
                SceneView.RepaintAll();
            }
        }

        bool ITransientOverlay.visible => Show;

        [InitializeOnLoadMethod]
        private static void SyncMenuOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                Menu.SetChecked(UniTextMenu.Tools.RespectSceneVisibility, Respect);
                Menu.SetChecked(UniTextMenu.Tools.ShowSceneVisibilityOverlay, Show);
            };
        }

        [MenuItem(UniTextMenu.Tools.RespectSceneVisibility, false, 200)]
        private static void ToggleMenu() => Respect = !Respect;

        [MenuItem(UniTextMenu.Tools.RespectSceneVisibility, true)]
        private static bool ToggleMenuValidate()
        {
            Menu.SetChecked(UniTextMenu.Tools.RespectSceneVisibility, Respect);
            return true;
        }

        [MenuItem(UniTextMenu.Tools.ShowSceneVisibilityOverlay, false, 201)]
        private static void ToggleShowOverlayMenu() => Show = !Show;

        [MenuItem(UniTextMenu.Tools.ShowSceneVisibilityOverlay, true)]
        private static bool ToggleShowOverlayMenuValidate()
        {
            Menu.SetChecked(UniTextMenu.Tools.ShowSceneVisibilityOverlay, Show);
            return true;
        }

        public override void OnCreated() => Changed += OnRespectChanged;
        public override void OnWillBeDestroyed() => Changed -= OnRespectChanged;
        private void OnRespectChanged()
        {
            respectToggle?.SetValueWithoutNotify(Respect);
            SceneView.RepaintAll();
        }

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            var icon = Resources.Load<Texture2D>(IconResourcePath);
            if (icon != null)
            {
                var image = new Image
                {
                    image = icon,
                    tooltip = "UniText: respect scene visibility (eye icon in Hierarchy)",
                    scaleMode = ScaleMode.ScaleToFit,
                    tintColor = EditorGUIUtility.isProSkin ? darkThemeTint : lightThemeTint,
                };
                image.style.width = IconSize;
                image.style.height = IconSize;
                root.Add(image);
            }
            respectToggle = new Toggle { value = Respect };
            respectToggle.style.width = IconSize;
            respectToggle.RegisterValueChangedCallback(evt => Respect = evt.newValue);
            root.Add(respectToggle);
            return root;
        }
    }
}
#endif
