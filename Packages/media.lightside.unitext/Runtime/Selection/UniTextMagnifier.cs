using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// Shipped Unity-UI presentation used by <see cref="PrefabMagnifier"/>: a rectangular panel showing a zoomed capture
    /// of the canvas around the focal point, re-rendered through a secondary camera with a zoomed
    /// projection matrix. Assign its prefab inside a <see cref="PrefabMagnifier"/> entity; the panel lives in a canvas-level overlay
    /// container so the field's RectMask2D never clips it.
    /// </summary>
    /// <remarks>
    /// Capture requires a canvas camera: Built-in RP renders via <see cref="Camera.Render"/>; URP / HDRP
    /// go through <c>RenderPipeline.SubmitRenderRequest</c> with a <c>StandardRequest</c> (2023.1+ —
    /// <see cref="Camera.Render"/> is unsupported under any SRP). When capture is impossible the
    /// magnifier stays hidden rather than showing an opaque blank panel: silently on
    /// Screen Space - Overlay canvases (no camera), with a one-time informational log on an SRP
    /// without the render-request API.
    /// </remarks>
    [AddComponentMenu(UniTextMenu.AddComponent.UniTextMagnifier)]
    public sealed partial class UniTextMagnifier : MonoBehaviour
    {
        /// <summary>Loupe width in density-independent pixels.</summary>
        [SerializeField, Min(1f), NumberStateProperty(nameof(ApplyConfigurationChange), Min = 1)]
        [Tooltip("Loupe panel width in dp (density-independent pixels).")]
        private float magnifierWidth = 140f;

        /// <summary>Loupe height in density-independent pixels.</summary>
        [SerializeField, Min(1f), NumberStateProperty(nameof(ApplyConfigurationChange), Min = 1)]
        [Tooltip("Loupe panel height in dp (density-independent pixels).")]
        private float magnifierHeight = 80f;

        /// <summary>Vertical lift above the focal point in density-independent pixels.</summary>
        [SerializeField, StateProperty(nameof(ApplyConfigurationChange))]
        [Tooltip("Vertical lift above the focal point in dp — keeps the loupe clear of the finger.")]
        private float verticalOffset = 60f;

        /// <summary>Magnification applied to the captured region.</summary>
        [SerializeField, Min(1f), NumberStateProperty(nameof(ApplyConfigurationChange), Min = 1)]
        [Tooltip("Magnification of the captured region.")]
        private float zoomFactor = 1.5f;

        /// <summary>Colour of the loupe frame.</summary>
        [SerializeField, StateProperty(nameof(ApplyConfigurationChange))]
        [Tooltip("Frame color around the loupe.")]
        private Color borderColor = new(0.3f, 0.3f, 0.3f, 1f);

        /// <summary>Colour behind the captured image and capture clear colour.</summary>
        [SerializeField, StateProperty(nameof(ApplyConfigurationChange))]
        [Tooltip("Fill behind the captured image; also the capture clear color.")]
        private Color backgroundColor = new(1f, 1f, 1f, 1f);

        /// <summary>Frame thickness in density-independent pixels.</summary>
        [SerializeField, StateProperty(nameof(ApplyConfigurationChange))]
        [Tooltip("Frame thickness in dp.")]
        private float borderWidth = 2f;

#if !UNITY_2023_1_OR_NEWER
        private static bool srpUnsupportedLogged;
#endif

        private RectTransform overlay;
        private RectTransform magnifierRoot;
        private Image borderImage;
        private RectTransform contentRect;
        private RawImage magnifierImage;
        private RenderTexture renderTexture;
        private Camera captureCamera;
        private bool isVisible;

        internal object PresentationOwner { get; set; }

        private Canvas rootCanvas;
        private Camera canvasCamera;
        private Vector2 lastScreenPosition;

        private void Awake()
        {
            overlay = CanvasUtil.GetOverlayContainer(this, "UniTextTouchOverlay");
            CacheCanvas();
            BuildMagnifier();
            magnifierRoot.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (magnifierRoot != null) Destroy(magnifierRoot.gameObject);

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }

            if (captureCamera != null)
            {
                Destroy(captureCamera.gameObject);
                captureCamera = null;
            }
        }

        public void Show(Vector2 screenPosition)
        {
            CacheCanvas();
            if (!CanCapture()) return;

            isVisible = true;
            EnsureRenderTexture();
            magnifierRoot.gameObject.SetActive(true);
            UpdatePosition(screenPosition);
        }

        public void Hide()
        {
            isVisible = false;
            PresentationOwner = null;
            if (magnifierRoot != null)
                magnifierRoot.gameObject.SetActive(false);
        }

        public void UpdatePosition(Vector2 screenPosition)
        {
            if (!isVisible || magnifierRoot == null) return;
            lastScreenPosition = screenPosition;

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(overlay, screenPosition, canvasCamera, out var world))
            {
                magnifierRoot.position = world;
                magnifierRoot.anchoredPosition += new Vector2(0f, verticalOffset);
            }

            CaptureRegion(screenPosition);
        }

        /// <summary>
        /// Whether a capture path exists: a canvas camera is required (Overlay canvases have none), and
        /// under an SRP the render-request API must be available (2023.1+).
        /// </summary>
        private bool CanCapture()
        {
            if (canvasCamera == null) return false;
            if (GraphicsSettings.currentRenderPipeline == null) return true;
#if UNITY_2023_1_OR_NEWER
            return true;
#else
            if (!srpUnsupportedLogged)
            {
                srpUnsupportedLogged = true;
                Debug.Log("[UniText] UniTextMagnifier: capturing under a Scriptable Render Pipeline requires " +
                          "Unity 2023.1+ (RenderPipeline.SubmitRenderRequest). The magnifier stays hidden on " +
                          "this Unity version; assign a custom IMagnifier implementation for a loupe here.", this);
            }
            return false;
#endif
        }

        private void BuildMagnifier()
        {
            var rootGo = new GameObject("Magnifier", typeof(RectTransform), typeof(Image));
            rootGo.hideFlags = HideFlags.DontSave;

            magnifierRoot = rootGo.GetComponent<RectTransform>();
            magnifierRoot.SetParent(overlay, false);

            magnifierRoot.pivot = new Vector2(0.5f, 0f);
            magnifierRoot.anchorMin = new Vector2(0.5f, 0.5f);
            magnifierRoot.anchorMax = new Vector2(0.5f, 0.5f);

            borderImage = rootGo.GetComponent<Image>();
            borderImage.raycastTarget = false;
            borderImage.maskable = false;

            var innerGo = new GameObject("MagnifierContent", typeof(RectTransform), typeof(RawImage));
            innerGo.hideFlags = HideFlags.DontSave;

            contentRect = innerGo.GetComponent<RectTransform>();
            contentRect.SetParent(magnifierRoot, false);
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;

            magnifierImage = innerGo.GetComponent<RawImage>();
            magnifierImage.raycastTarget = false;
            magnifierImage.maskable = false;

            ApplyStyle();
        }

        /// <summary>Pushes every serialized visual onto the live objects after creation or a reconciled state change.</summary>
        private void ApplyStyle()
        {
            if (magnifierRoot == null) return;
            magnifierRoot.sizeDelta = new Vector2(magnifierWidth + borderWidth * 2f,
                magnifierHeight + borderWidth * 2f);
            borderImage.color = borderColor;
            contentRect.offsetMin = new Vector2(borderWidth, borderWidth);
            contentRect.offsetMax = new Vector2(-borderWidth, -borderWidth);
            magnifierImage.color = backgroundColor;
        }

        private void ApplyConfigurationChange()
        {
            ApplyStyle();
            if (!isVisible) return;
            EnsureRenderTexture();
            UpdatePosition(lastScreenPosition);
        }

        private void CacheCanvas()
        {
            var canvas = overlay != null ? overlay.GetComponentInParent<Canvas>() : GetComponentInParent<Canvas>();
            rootCanvas = canvas != null ? canvas.rootCanvas : null;
            canvasCamera = CanvasUtil.GetCanvasCamera(rootCanvas);
        }

        private float CanvasScale() => GestureMetrics.CanvasScale(rootCanvas);

        /// <summary>
        /// (Re)creates the RT at the loupe's actual on-screen pixel size — dp × canvas scale —
        /// so sharpness is device-invariant; re-checked on every Show because the scale can change.
        /// </summary>
        private void EnsureRenderTexture()
        {
            float scale = CanvasScale();
            int width = Mathf.Max(1, Mathf.RoundToInt(magnifierWidth * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(magnifierHeight * scale));
            if (renderTexture != null && renderTexture.width == width && renderTexture.height == height) return;

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }

            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            renderTexture.hideFlags = HideFlags.DontSave;
            renderTexture.antiAliasing = 1;
            renderTexture.filterMode = FilterMode.Bilinear;
            renderTexture.Create();

            if (magnifierImage != null)
                magnifierImage.texture = renderTexture;

            if (captureCamera != null)
            {
                captureCamera.targetTexture = renderTexture;
                return;
            }

            var camGo = new GameObject("MagnifierCamera", typeof(Camera));
            camGo.hideFlags = HideFlags.DontSave;
            camGo.transform.SetParent(transform, false);

            captureCamera = camGo.GetComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.targetTexture = renderTexture;
        }

        /// <summary>
        /// Re-renders the canvas content around the focal point into the magnifier's RT: the capture
        /// camera copies the canvas camera and pre-multiplies a crop-zoom matrix over its projection.
        /// </summary>
        private void CaptureRegion(Vector2 screenPosition)
        {
            if (renderTexture == null || canvasCamera == null) return;

            captureCamera.CopyFrom(canvasCamera);
            captureCamera.targetTexture = renderTexture;
            captureCamera.enabled = false;
            captureCamera.rect = new Rect(0, 0, 1, 1);
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = backgroundColor;

            float scale = CanvasScale();
            var captureWidth = magnifierWidth * scale / zoomFactor;
            var captureHeight = magnifierHeight * scale / zoomFactor;

            var nx = (screenPosition.x - captureWidth * 0.5f) / Screen.width;
            var ny = (screenPosition.y - captureHeight * 0.5f) / Screen.height;
            var nw = captureWidth / Screen.width;
            var nh = captureHeight / Screen.height;

            nx = Mathf.Clamp(nx, 0f, 1f - nw);
            ny = Mathf.Clamp(ny, 0f, 1f - nh);

            var scaleX = 1f / nw;
            var scaleY = 1f / nh;
            var offsetX = 1f - (2f * nx + nw) * scaleX;
            var offsetY = 1f - (2f * ny + nh) * scaleY;

            var zoom = Matrix4x4.identity;
            zoom.m00 = scaleX;
            zoom.m11 = scaleY;
            zoom.m03 = offsetX;
            zoom.m13 = offsetY;

            captureCamera.projectionMatrix = zoom * canvasCamera.projectionMatrix;

            if (GraphicsSettings.currentRenderPipeline == null)
            {
                captureCamera.Render();
                return;
            }

#if UNITY_2023_1_OR_NEWER
            var request = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(captureCamera, request))
            {
                request.destination = renderTexture;
                RenderPipeline.SubmitRenderRequest(captureCamera, request);
            }
#endif
        }
    }
}
