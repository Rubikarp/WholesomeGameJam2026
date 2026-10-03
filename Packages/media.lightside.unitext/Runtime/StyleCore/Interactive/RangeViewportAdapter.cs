using System;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// Component adapter that consumes <see cref="UniTextInteractions.ScrollIntoViewRequested"/>
    /// without placing Canvas, world-camera or project viewport policy in the interaction router.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract partial class RangeViewportAdapter : MonoBehaviour
    {
        [SerializeField, StateProperty(nameof(ApplyTextChange))] private UniTextBase text;
        [NonSerialized] private bool viewportBound;

        private void ApplyTextChange(UniTextBase previous, UniTextBase current)
        {
            if (!viewportBound) return;
            if (previous != null)
            {
                if (UniTextInteractions.TryGet(previous, out var interactions))
                    interactions.ScrollIntoViewRequested -= OnRequested;
                previous.VisibleWindow = null;
            }
            if (current == null)
                throw new InvalidOperationException($"{GetType().Name} requires a UniText component.");
            UniTextInteractions.For(current).ScrollIntoViewRequested += OnRequested;
            RefreshVisibleWindow();
        }

        /// <summary>Reveals one range immediately using this adapter's configured viewport.</summary>
        public void Reveal(in RangeScrollRequest request)
        {
            ValidateText();
            if (!ReferenceEquals(request.Text, text))
                throw new ArgumentException("The request belongs to another UniText component.", nameof(request));
            RevealRange(in request);
        }

        /// <summary>
        /// Recomputes <see cref="UniTextBase.VisibleWindow"/> from the configured viewport so glyph,
        /// paragraph and decoration emission share the engine's existing culling path.
        /// </summary>
        public void RefreshVisibleWindow()
        {
            ValidateText();
            text.VisibleWindow = ResolveVisibleWindow();
        }

        protected virtual void OnEnable()
        {
            if (text == null) Text = GetComponent<UniTextBase>();
            ValidateText();
            UniTextInteractions.For(text).ScrollIntoViewRequested += OnRequested;
            viewportBound = true;
            RefreshVisibleWindow();
        }

        protected virtual void OnDisable()
        {
            if (text == null) return;
            viewportBound = false;
            if (UniTextInteractions.TryGet(text, out var interactions))
                interactions.ScrollIntoViewRequested -= OnRequested;
            text.VisibleWindow = null;
        }

        /// <summary>Implements platform or project-specific reveal behavior.</summary>
        protected abstract void RevealRange(in RangeScrollRequest request);

        /// <summary>Returns the current viewport in UniText-local coordinates.</summary>
        protected abstract Rect ResolveVisibleWindow();

        /// <summary>Transforms an axis-aligned source rect into an axis-aligned target-local rect.</summary>
        protected static Rect TransformRect(RectTransform source, Rect rect, RectTransform target)
        {
            var a = (Vector2)target.InverseTransformPoint(source.TransformPoint(
                new Vector3(rect.xMin, rect.yMin)));
            var b = (Vector2)target.InverseTransformPoint(source.TransformPoint(
                new Vector3(rect.xMin, rect.yMax)));
            var c = (Vector2)target.InverseTransformPoint(source.TransformPoint(
                new Vector3(rect.xMax, rect.yMin)));
            var d = (Vector2)target.InverseTransformPoint(source.TransformPoint(
                new Vector3(rect.xMax, rect.yMax)));
            var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void OnRequested(RangeScrollRequest request) => Reveal(in request);

        private void ValidateText()
        {
            if (text == null)
                throw new InvalidOperationException(
                    $"{GetType().Name} requires a UniText component on the same GameObject or in its Text slot.");
        }

#if UNITY_EDITOR
        protected virtual void Reset() => Text = GetComponent<UniTextBase>();
#endif
    }

    /// <summary>Reveals Canvas ranges by minimally shifting a configured ScrollRect content transform.</summary>
    [AddComponentMenu(UniTextMenu.AddComponent.ScrollRectRangeViewport)]
    public sealed partial class ScrollRectRangeViewport : RangeViewportAdapter
    {
        [SerializeField, StateProperty(nameof(ApplyScrollRectChange))] private ScrollRect scrollRect;
        [SerializeField, StateProperty(nameof(ApplyPaddingChange))]
        [Tooltip("Viewport inset in pixels: left, bottom, right, top.")]
        private Vector4 padding;
        [NonSerialized] private bool scrollRectBound;

        private void ApplyScrollRectChange(ScrollRect previous, ScrollRect current)
        {
            if (!scrollRectBound) return;
            if (previous != null) previous.onValueChanged.RemoveListener(OnScrolled);
            if (current == null)
                throw new InvalidOperationException("ScrollRectRangeViewport requires a ScrollRect.");
            current.onValueChanged.AddListener(OnScrolled);
            RefreshVisibleWindow();
        }

        private void ApplyPaddingChange()
        {
            if (scrollRectBound) RefreshVisibleWindow();
        }

        protected override void RevealRange(in RangeScrollRequest request)
        {
            ValidateViewport();
            var content = scrollRect.content;
            var viewport = scrollRect.viewport != null
                ? scrollRect.viewport
                : (RectTransform)scrollRect.transform;
            var target = TransformRect(request.Text.rectTransform, request.Bounds, content);
            var visible = TransformRect(viewport, viewport.rect, content);

            var delta = Vector2.zero;
            if (scrollRect.horizontal)
            {
                var left = visible.xMin + Mathf.Max(0f, padding.x);
                var right = visible.xMax - Mathf.Max(0f, padding.z);
                if (target.xMin < left) delta.x = left - target.xMin;
                else if (target.xMax > right) delta.x = right - target.xMax;
            }
            if (scrollRect.vertical)
            {
                var bottom = visible.yMin + Mathf.Max(0f, padding.y);
                var top = visible.yMax - Mathf.Max(0f, padding.w);
                if (target.yMin < bottom) delta.y = bottom - target.yMin;
                else if (target.yMax > top) delta.y = top - target.yMax;
            }
            if (delta == Vector2.zero) return;
            scrollRect.StopMovement();
            content.anchoredPosition += delta;
            RefreshVisibleWindow();
        }

        protected override Rect ResolveVisibleWindow()
        {
            ValidateViewport();
            var viewport = scrollRect.viewport != null
                ? scrollRect.viewport
                : (RectTransform)scrollRect.transform;
            return TransformRect(viewport, viewport.rect, Text.rectTransform);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            scrollRect.onValueChanged.AddListener(OnScrolled);
            scrollRectBound = true;
        }

        protected override void OnDisable()
        {
            scrollRectBound = false;
            if (scrollRect != null) scrollRect.onValueChanged.RemoveListener(OnScrolled);
            base.OnDisable();
        }

        private void ValidateViewport()
        {
            if (scrollRect == null)
                throw new InvalidOperationException("ScrollRectRangeViewport requires a ScrollRect.");
            if (scrollRect.content == null)
                throw new InvalidOperationException("The configured ScrollRect has no Content RectTransform.");
        }

        private void OnScrolled(Vector2 _) => RefreshVisibleWindow();

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            ScrollRect = GetComponentInParent<ScrollRect>();
        }
#endif
    }

    /// <summary>
    /// Reveals world-space ranges by panning a camera or configured camera rig in its view plane.
    /// It never changes zoom, projection, rotation or range layout.
    /// </summary>
    [AddComponentMenu(UniTextMenu.AddComponent.CameraRangeViewport)]
    public sealed partial class CameraRangeViewport : RangeViewportAdapter
    {
        /// <summary>Camera whose viewport must contain the requested range.</summary>
        [SerializeField, StateProperty(nameof(RefreshViewport))] private Camera targetCamera;
        /// <summary>Transform moved in world space; null moves the camera itself.</summary>
        [SerializeField, StateProperty(nameof(RefreshViewport))] private Transform moveTarget;
        /// <summary>Symmetric screen-space inset in pixels.</summary>
        [SerializeField, Min(0f), NumberStateProperty(nameof(RefreshViewport), Min = 0)] private float padding = 16f;

        private void RefreshViewport()
        {
            if (isActiveAndEnabled && Text != null) RefreshVisibleWindow();
        }

        protected override void RevealRange(in RangeScrollRequest request)
        {
            if (targetCamera == null)
                throw new InvalidOperationException("CameraRangeViewport requires a Camera.");
            var rect = request.Bounds;
            var transform = request.Text.transform;
            Span<Vector3> points = stackalloc Vector3[4];
            points[0] = targetCamera.WorldToViewportPoint(transform.TransformPoint(
                new Vector3(rect.xMin, rect.yMin)));
            points[1] = targetCamera.WorldToViewportPoint(transform.TransformPoint(
                new Vector3(rect.xMin, rect.yMax)));
            points[2] = targetCamera.WorldToViewportPoint(transform.TransformPoint(
                new Vector3(rect.xMax, rect.yMin)));
            points[3] = targetCamera.WorldToViewportPoint(transform.TransformPoint(
                new Vector3(rect.xMax, rect.yMax)));
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            var depth = 0f;
            for (var i = 0; i < points.Length; i++)
            {
                if (points[i].z <= 0f)
                    throw new InvalidOperationException(
                        "CameraRangeViewport cannot reveal a range behind the configured camera.");
                min = Vector2.Min(min, points[i]);
                max = Vector2.Max(max, points[i]);
                depth += points[i].z;
            }
            depth /= points.Length;
            var inset = new Vector2(padding / Mathf.Max(1f, targetCamera.pixelWidth),
                padding / Mathf.Max(1f, targetCamera.pixelHeight));
            var shift = Vector2.zero;
            if (min.x < inset.x) shift.x = inset.x - min.x;
            else if (max.x > 1f - inset.x) shift.x = 1f - inset.x - max.x;
            if (min.y < inset.y) shift.y = inset.y - min.y;
            else if (max.y > 1f - inset.y) shift.y = 1f - inset.y - max.y;
            if (shift == Vector2.zero) return;

            var center = (min + max) * 0.5f;
            var before = targetCamera.ViewportToWorldPoint(new Vector3(center.x, center.y, depth));
            var after = targetCamera.ViewportToWorldPoint(
                new Vector3(center.x - shift.x, center.y - shift.y, depth));
            (moveTarget != null ? moveTarget : targetCamera.transform).position += after - before;
            RefreshVisibleWindow();
        }

        protected override Rect ResolveVisibleWindow()
        {
            if (targetCamera == null)
                throw new InvalidOperationException("CameraRangeViewport requires a Camera.");
            var plane = new Plane(Text.transform.forward, Text.transform.position);
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            Span<Vector2> viewportCorners = stackalloc Vector2[4];
            viewportCorners[0] = Vector2.zero;
            viewportCorners[1] = Vector2.up;
            viewportCorners[2] = Vector2.one;
            viewportCorners[3] = Vector2.right;
            for (var i = 0; i < viewportCorners.Length; i++)
            {
                var ray = targetCamera.ViewportPointToRay(viewportCorners[i]);
                if (!plane.Raycast(ray, out var distance))
                    throw new InvalidOperationException(
                        "Camera viewport rays do not intersect the UniText plane.");
                var local = (Vector2)Text.transform.InverseTransformPoint(ray.GetPoint(distance));
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            TargetCamera = Camera.main;
        }
#endif
    }
}
