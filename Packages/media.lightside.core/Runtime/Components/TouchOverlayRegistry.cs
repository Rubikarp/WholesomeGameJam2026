using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// One shared instance per prefab for global touch/overlay UI (selection handles, magnifier, context
    /// menu). The first request for a given prefab lazily creates a single top-most
    /// <see cref="RenderMode.ScreenSpaceOverlay"/> canvas under <c>DontDestroyOnLoad</c> and instantiates
    /// the prefab under it; every later request for the same prefab returns that instance. Consumers hold
    /// a PREFAB reference and resolve it through <see cref="GetOrCreate"/> at use time — so any number of
    /// fields across any number of scenes share one handle set / one menu, always rendered above
    /// everything else. Implementations must stay field-agnostic (pull geometry from the active editor at
    /// call time), since the instance is shared. One overlay unit is one density-independent pixel of the
    /// host platform; the requesting root canvas supplies the scale only where the display reports no
    /// usable density.
    /// </summary>
    public static class TouchOverlayRegistry
    {
        /// <summary>Top of the sorting range (Canvas sorting order is a 16-bit signed value) so the overlay renders above every scene canvas.</summary>
        private const int TopSortingOrder = 32767;

        private static Canvas canvas;
        private static TouchOverlayCanvasScaler canvasScaler;
        private static readonly Dictionary<Object, Component> instances = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            canvas = null;
            canvasScaler = null;
            instances.Clear();
        }

        /// <summary>The shared overlay canvas's RectTransform, created on first Play Mode access; null outside Play Mode.</summary>
        public static RectTransform Root => Application.isPlaying ? (RectTransform)EnsureCanvas().transform : null;

        /// <summary>Returns the existing shared instance without creating anything; null outside Play Mode.</summary>
        public static T GetExisting<T>(T prefab) where T : Component
        {
            if (!Application.isPlaying || prefab == null) return null;
            return instances.TryGetValue(prefab, out var existing) && existing != null
                ? (T)existing
                : null;
        }

        /// <summary>
        /// Returns the single shared instance of <paramref name="prefab"/>, instantiating it under the
        /// shared overlay canvas and registering it on first request. <paramref name="referenceCanvas"/>
        /// supplies the display and fallback scale when physical display density is unavailable.
        /// <see langword="null"/> prefab and Edit Mode return <see langword="null"/> without creating
        /// anything. A destroyed instance (domain edge cases) is re-created.
        /// </summary>
        public static T GetOrCreate<T>(T prefab, Canvas referenceCanvas = null) where T : Component
        {
            if (!Application.isPlaying || prefab == null) return null;

            var parent = EnsureCanvas().transform;
            ApplyReferenceCanvas(referenceCanvas);
            var existing = GetExisting(prefab);
            if (existing != null) return existing;

            var instance = Object.Instantiate(prefab, parent, false);
            instance.gameObject.hideFlags = HideFlags.DontSave;
            instances[prefab] = instance;
            return instance;
        }

        private static Canvas EnsureCanvas()
        {
            if (canvas == null)
            {
                var go = new GameObject("UniTextTouchOverlayCanvas", typeof(Canvas),
                    typeof(TouchOverlayCanvasScaler), typeof(GraphicRaycaster));
                go.hideFlags = HideFlags.DontSave;
                canvas = go.GetComponent<Canvas>();
                canvasScaler = go.GetComponent<TouchOverlayCanvasScaler>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = TopSortingOrder;
                Object.DontDestroyOnLoad(go);
            }

            return canvas;
        }

        private static void ApplyReferenceCanvas(Canvas referenceCanvas)
        {
            var rootCanvas = referenceCanvas != null ? referenceCanvas.rootCanvas : null;
            canvas.targetDisplay = rootCanvas != null ? rootCanvas.targetDisplay : 0;
            canvasScaler.ReferenceCanvas = rootCanvas != null
                                           && rootCanvas.renderMode != RenderMode.WorldSpace
                ? rootCanvas
                : null;
        }
    }

    internal sealed class TouchOverlayCanvasScaler : CanvasScaler
    {
        private Canvas referenceCanvas;

        internal Canvas ReferenceCanvas
        {
            get => referenceCanvas;
            set
            {
                referenceCanvas = value;
                HandleConstantPixelSize();
            }
        }

        /// <summary>
        /// One overlay unit is one density-independent pixel of the host platform: an Android dp /
        /// iOS pt on a device, a CSS reference pixel on a pointer display. The two baselines differ
        /// (160 dpi against 96 dpi), so the same authored unit count is physically larger where the
        /// display is viewed from further away.
        /// </summary>
        protected override void HandleConstantPixelSize()
        {
            scaleFactor = GestureMetrics.DpiScale(
                GestureMetrics.CanvasScale(referenceCanvas),
                CanvasUtil.IsMobilePlatform()
                    ? GestureMetrics.TouchDensityBaseline
                    : GestureMetrics.PointerDensityBaseline);
            referencePixelsPerUnit = referenceCanvas != null ? referenceCanvas.referencePixelsPerUnit : 100f;
            base.HandleConstantPixelSize();
        }
    }
}
