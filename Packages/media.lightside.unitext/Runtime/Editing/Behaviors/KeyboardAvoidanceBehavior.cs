using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Keeps the field (or any assigned target) visible above the software keyboard by translating it
    /// upward, animated in lockstep with the system keyboard — the classic "lift the form so the
    /// keyboard doesn't cover it" reaction.
    /// </summary>
    /// <remarks>
    /// Translation moves <c>anchoredPosition.y</c>, so the target must not be driven by a layout group
    /// on that axis — point <see cref="target"/> at the popup / panel root, not a layout child (for
    /// layout-driven forms a resize or scroll reaction fits better). On platforms without frame-synced
    /// keyboard animation (iOS, Android &lt;30, WebGL) the motion is driven client-side from the
    /// event's timing metadata, or <see cref="fallbackDuration"/> / <see cref="fallbackEasing"/>
    /// when the platform provides none.
    /// </remarks>
    [Serializable]
    [TypeDescription("Lift the field / target above the soft keyboard so it stays visible")]
    [TypeGroup("Field", 4)]
    public sealed partial class KeyboardAvoidanceBehavior : KeyboardBehavior
    {
        /// <summary>Rect translated to remain visible; null resolves the field's clipping parent.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("RectTransform translated to stay above the keyboard. Null = the direct clipping parent, or the editor itself when there is none.")]
        private RectTransform target;

        /// <summary>Physical gap retained above the keyboard, measured in density-independent pixels.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Breathing room kept between the field's visible bottom and the keyboard top, in dp " +
                 "(density-independent pixels) — a constant physical gap across screen densities.")]
        private float keyboardPadding = 32f;

        /// <summary>Whether an already visible target remains at its authored position.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Skip translation when the target is already fully above the keyboard. Off: also pull the target down so its bottom sits exactly at keyboard top + padding.")]
        private bool onlyWhenOverlapping = true;

        /// <summary>Client-side animation duration used when the platform supplies no timing.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Fallback animation duration when the platform did not report one.")]
        private float fallbackDuration = 0.25f;

        /// <summary>Client-side easing used when the platform supplies no animation curve.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Fallback easing used when the platform did not report a system curve.")]
        private KeyboardEasing fallbackEasing = KeyboardEasing.EaseOut;

        private RectTransform resolved;
        private float baselineAnchoredY;
        private bool baselineCaptured;

        private float currentOffset;
        private float animFrom;
        private float animTo;
        private float animElapsed;
        private float animDuration;
        private KeyboardEasing animEasing;
        private bool animActive;
        private bool animFrameSynced;
        private bool animUsesFallback;

        /// <inheritdoc/>
        protected override bool IsAnimating => animActive && !animFrameSynced;

        /// <inheritdoc/>
        protected override void OnFocusGained()
        {
            var previous = resolved;
            resolved = ResolveTarget();
            if (resolved != previous)
            {
                baselineCaptured = false;
                currentOffset = 0f;
            }
            EnsureBaseline();
            animActive = false;
        }

        /// <inheritdoc/>
        protected override void OnFocusLost()
        {
            if (!IsTearingDown && resolved != null && !Mathf.Approximately(currentOffset, 0f))
            {
                StartClientExit();
                return;
            }
            RestoreBaseline();
            resolved = null;
            animActive = false;
            baselineCaptured = false;
        }

        /// <inheritdoc/>
        protected override void OnKeyboardWillShow(KeyboardEvent e) => StartAnimationTo(ComputeOffset(e), e);

        /// <inheritdoc/>
        protected override void OnKeyboardWillChangeFrame(KeyboardEvent e) => StartAnimationTo(ComputeOffset(e), e);

        /// <inheritdoc/>
        protected override void OnKeyboardWillHide(KeyboardEvent e) => StartAnimationTo(0f, e);

        /// <inheritdoc/>
        protected override void OnKeyboardAnimationProgress(KeyboardEvent e)
        {
            if (animActive && animFrameSynced)
                ApplyOffset(Mathf.LerpUnclamped(animFrom, animTo, e.animationFraction));
        }

        /// <inheritdoc/>
        protected override void OnKeyboardDidShow(KeyboardEvent e) => CompleteAnimation(animTo);

        /// <inheritdoc/>
        protected override void OnKeyboardDidHide(KeyboardEvent e) => CompleteAnimation(0f);

        /// <inheritdoc/>
        protected override void OnKeyboardUpdate(float deltaTime)
        {
            if (!animActive || animFrameSynced) return;

            animElapsed += deltaTime;
            float t = animDuration > 0f ? Mathf.Clamp01(animElapsed / animDuration) : 1f;
            ApplyOffset(Mathf.LerpUnclamped(animFrom, animTo, ApplyEasing(t, animEasing)));
            if (t < 1f) return;

            animActive = false;
            if (!editable.IsActive)
            {
                resolved = null;
                baselineCaptured = false;
            }
        }

        private RectTransform ResolveTarget()
        {
            if (target != null) return target;
            if (editable == null) return null;
            return editable.GetFieldRectTransform();
        }

        /// <summary>
        /// Vertical offset to move the target by, in the target parent's local units (what
        /// <c>anchoredPosition</c> is measured in). The overlap is measured strictly between the keyboard
        /// top (<see cref="KeyboardEvent.area"/>) and the <b>field's visible</b> bottom edge
        /// (<see cref="UniTextEditable.GetViewportScreenRect"/>, the masked viewport) — never the target's,
        /// whose own size and position are irrelevant to the amount. Both are Unity screen pixels, and
        /// <see cref="keyboardPadding"/> (authored in dp) is converted to them through
        /// <see cref="GestureMetrics.SlopPx"/> so the gap stays a constant physical size on any density.
        /// The target is reset to its
        /// baseline first so the field is measured unavoided (matters when the field is nested under the
        /// target). The screen-pixel delta is then mapped through the target parent rect — correct under
        /// any CanvasScaler factor, nested canvases, and Screen Space - Camera / World Space projection.
        /// </summary>
        private float ComputeOffset(KeyboardEvent e)
        {
            if (e.area.height <= 0f || resolved == null || editable == null) return 0f;

            EnsureBaseline();
            var current = resolved.anchoredPosition;
            resolved.anchoredPosition = new Vector2(current.x, baselineAnchoredY);
            var fieldRect = editable.GetViewportScreenRect();
            resolved.anchoredPosition = current;

            float paddingPx = GestureMetrics.SlopPx(keyboardPadding, resolved.GetComponentInParent<Canvas>());
            float deficit = e.area.yMax + paddingPx - fieldRect.yMin;
            if (onlyWhenOverlapping && deficit <= 0f) return 0f;
            return ScreenDeltaToLocalY(deficit, fieldRect.min);
        }

        private float ScreenDeltaToLocalY(float screenDelta, Vector2 screenPoint)
        {
            var parent = resolved.parent as RectTransform;
            if (parent == null) return screenDelta;
            var canvas = resolved.GetComponentInParent<Canvas>();
            var cam = CanvasUtil.GetCanvasCamera(canvas != null ? canvas.rootCanvas : null);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, cam, out var basePoint))
                return screenDelta;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint + new Vector2(0f, screenDelta), cam, out var shifted))
                return screenDelta;
            return shifted.y - basePoint.y;
        }

        private void ApplyOffset(float offset)
        {
            EnsureBaseline();
            if (resolved == null) return;
            var pos = resolved.anchoredPosition;
            resolved.anchoredPosition = new Vector2(pos.x, baselineAnchoredY + offset);
            currentOffset = offset;
        }

        private void StartAnimationTo(float toOffset, KeyboardEvent e)
        {
            EnsureBaseline();
            animFrom = currentOffset;
            animTo = toOffset;
            animElapsed = 0f;
            animDuration = e.animationDuration > 0f ? e.animationDuration : fallbackDuration;
            animEasing = e.animationDuration > 0f ? e.easing : fallbackEasing;
            animFrameSynced = e.hasFrameSyncedAnimation;
            animUsesFallback = !animFrameSynced && e.animationDuration <= 0f;
            animActive = true;
        }

        private void CompleteAnimation(float offset)
        {
            if (animActive && animUsesFallback) return;
            SnapToOffset(offset);
        }

        private void SnapToOffset(float offset)
        {
            ApplyOffset(offset);
            animFrom = offset;
            animTo = offset;
            animActive = false;
        }

        /// <summary>
        /// Eases the target back to rest after focus loss, client-side (the frame-synced keyboard events
        /// stop with focus, so the exit runs on the fallback duration/easing regardless of platform). Ridden
        /// by <see cref="OnKeyboardUpdate"/> on the base's post-focus tick tail; on completion the target is
        /// released so the next focus re-resolves and re-captures a fresh baseline.
        /// </summary>
        private void StartClientExit()
        {
            EnsureBaseline();
            animFrom = currentOffset;
            animTo = 0f;
            animElapsed = 0f;
            animDuration = fallbackDuration;
            animEasing = fallbackEasing;
            animFrameSynced = false;
            animActive = true;
        }

        private void EnsureBaseline()
        {
            if (!baselineCaptured) CaptureBaseline();
        }

        private void CaptureBaseline()
        {
            if (resolved == null) return;
            baselineAnchoredY = resolved.anchoredPosition.y;
            baselineCaptured = true;
        }

        private void RestoreBaseline()
        {
            if (!baselineCaptured || resolved == null) return;
            var pos = resolved.anchoredPosition;
            resolved.anchoredPosition = new Vector2(pos.x, baselineAnchoredY);
            currentOffset = 0f;
        }

        private static float ApplyEasing(float t, KeyboardEasing easing)
            => (easing switch
            {
                KeyboardEasing.Linear => EasingType.Linear,
                KeyboardEasing.EaseIn => EasingType.QuadraticIn,
                KeyboardEasing.EaseOut => EasingType.QuadraticOut,
                KeyboardEasing.EaseInOut => EasingType.QuadraticInOut,
                _ => EasingType.Linear,
            }).Evaluate(t);
    }
}
