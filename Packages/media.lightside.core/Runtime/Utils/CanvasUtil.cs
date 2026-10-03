using UnityEngine;

namespace LightSide
{
    /// <summary>Canvas and screen-space helpers: platform detection, canvas camera resolution, screen rects, and overlay containers.</summary>
    public static class CanvasUtil
    {
        /// <summary>True only on a real iOS/Android device (false in the editor and on every other platform).</summary>
        public static bool IsMobilePlatform()
        {
#if UNITY_IOS || UNITY_ANDROID
            return !Application.isEditor;
#else
            return false;
#endif
        }

        /// <summary>
        /// Event camera for screen-space conversions: <see langword="null"/> for Screen Space - Overlay
        /// canvases, otherwise the canvas world camera. Pass the same canvas the caller already resolved.
        /// </summary>
        public static Camera GetCanvasCamera(Canvas canvas)
            => canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

        /// <summary>
        /// Screen-space rect of a RectTransform's world corners (bottom-left to top-right) via the given
        /// event camera. Main-thread only — reuses a shared corner buffer.
        /// </summary>
        public static Rect WorldCornersToScreenRect(RectTransform rt, Camera cam)
        {
            var corners = cornersBuffer ??= new Vector3[4];
            rt.GetWorldCorners(corners);
            var bl = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            var tr = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            return new Rect(
                Mathf.Min(bl.x, tr.x),
                Mathf.Min(bl.y, tr.y),
                Mathf.Abs(tr.x - bl.x),
                Mathf.Abs(tr.y - bl.y));
        }

        private static Vector3[] cornersBuffer;

        /// <summary>Sentinel "no rect reported yet" — NaN x fails every <see cref="RectChanged"/> comparison, forcing the first push.</summary>
        public static readonly Rect NoRect = new(float.NaN, 0f, 0f, 0f);

        /// <summary>Whether a rect re-publish is due: <paramref name="last"/> is the sentinel or any edge moved at least <paramref name="eps"/> px.</summary>
        public static bool RectChanged(in Rect last, in Rect current, float eps = 0.5f)
            => float.IsNaN(last.x)
               || Mathf.Abs(current.x - last.x) >= eps || Mathf.Abs(current.y - last.y) >= eps
               || Mathf.Abs(current.width - last.width) >= eps || Mathf.Abs(current.height - last.height) >= eps;

        /// <summary>
        /// Full-canvas overlay RectTransform under the host's ROOT canvas — the spawn parent for
        /// overlay UI that must escape any RectMask2D viewport (handles, popups, magnifiers).
        /// Reused per canvas by <paramref name="name"/>; created on demand.
        /// </summary>
        public static RectTransform GetOverlayContainer(Component host, string name)
        {
            var canvas = host.GetComponentInParent<Canvas>();
            if (canvas == null) return host.transform as RectTransform;
            var root = canvas.rootCanvas.transform;
            if (root.Find(name) is RectTransform existing)
            {
                existing.SetAsLastSibling();
                return existing;
            }

            var go = new GameObject(name, typeof(RectTransform));
            go.hideFlags = HideFlags.DontSave;
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.StretchToParent();
            rt.SetAsLastSibling();
            return rt;
        }
    }
}
