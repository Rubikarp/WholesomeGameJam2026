using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Timing / distance thresholds consumed by <see cref="TouchGestureRecognizer"/>.
    /// Distances are density-independent pixels (Android dp, 160 dpi baseline), times are seconds.
    /// </summary>
    public struct GestureThresholds
    {
        /// <summary>Pointer travel in dp beyond which a press becomes a drag instead of a tap / hold.</summary>
        public float DragSlopDp;

        /// <summary>Maximum distance between chained taps, in dp.</summary>
        public float MultiTapSlopDp;

        /// <summary>Maximum interval between chained taps, in seconds.</summary>
        public float MultiTapWindow;

        /// <summary>Hold duration in seconds that promotes a press into a long-press gesture.</summary>
        public float LongPressDuration;

        /// <summary>Platform-convention defaults (iOS / Android): 10 dp drag slop, 100 dp tap slop, 0.3 s tap window, 0.5 s long press.</summary>
        public static readonly GestureThresholds Default = new()
        {
            DragSlopDp = 10f,
            MultiTapSlopDp = 100f,
            MultiTapWindow = 0.3f,
            LongPressDuration = 0.5f,
        };
    }

    /// <summary>
    /// Detects touch gestures for text input: single tap, double tap, triple tap,
    /// long press, drag, and single-finger scroll. Reports recognized gestures via callbacks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pure state machine with no Unity component dependencies: the consumer feeds pointer events
    /// (<see cref="OnPointerDown"/>, <see cref="OnPointerUp"/>, <see cref="OnPointerMove"/>) plus a
    /// per-frame <see cref="Update"/> tick, and the recognizer fires the matching callbacks.
    /// </para>
    /// <para>
    /// Gesture semantics match iOS/Android platform conventions:
    /// <list type="bullet">
    /// <item>Single tap: place caret at tap position.</item>
    /// <item>Double tap: select word under tap.</item>
    /// <item>Triple tap: select line/paragraph under tap.</item>
    /// <item>Long press: show magnifier for precise cursor placement, or select word + show context menu.</item>
    /// <item>Drag after double tap: word-by-word selection extension; single-finger swipe scrolls.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Thresholds come from <see cref="ThresholdsSource"/>. Distances are authored in
    /// density-independent pixels (Android dp, 160 dpi baseline) and scaled by the device DPI at
    /// use (<see cref="GestureMetrics.SlopPx"/>) — re-read on every event, never cached, with
    /// <see cref="CanvasSource"/> covering displays that report no dpi. Time values use
    /// <c>Time.unscaledTime</c> to stay consistent under time-scale changes.
    /// </para>
    /// </remarks>
    public sealed class TouchGestureRecognizer
    {
        private enum GestureState
        {
            Idle,
            PointerDown,
            LongPress,
            Dragging,
            Scrolling
        }

        private GestureState state = GestureState.Idle;
        private TapChain tapChain;
        private Vector2 downPosition;
        private float downTime;
        private bool longPressDragStarted;
        private int dragOriginTapCount;
        private Vector2 lastScrollPosition;

        /// <summary>
        /// Canvas whose display density scales the dp thresholds when <c>Screen.dpi</c> is
        /// unreliable (0 on some Android devices and most WebGL browsers): the host supplies its
        /// text component's canvas so thresholds still track the UI scale. Unset / null result =
        /// 1 px per dp.
        /// </summary>
        public Func<Canvas> CanvasSource;

        /// <summary>
        /// Threshold provider — invoked on every event, never cached, so live configuration
        /// changes apply immediately. Unset = <see cref="GestureThresholds.Default"/>.
        /// </summary>
        public Func<GestureThresholds> ThresholdsSource;

        private GestureThresholds Thresholds => ThresholdsSource?.Invoke() ?? GestureThresholds.Default;

        /// <summary>
        /// Occurs on the release of a first tap, immediately — before the multi-tap window resolves.
        /// Parameter is the tap position in screen coordinates. Intended for provisional caret placement
        /// so the caret tracks the finger with no latency (iOS/Android convention); it is always followed
        /// by either <see cref="OnSingleTap"/> (the tap was solitary — run its affordances) or
        /// <see cref="OnDoubleTap"/>/<see cref="OnTripleTap"/> (a chained tap superseded it with a
        /// word/line selection). Side effects that would flicker on a chained tap belong in those, not here.
        /// </summary>
        public Action<Vector2> OnTapCaret;

        /// <summary>Occurs when a single tap has been recognized, after the multi-tap window closes with no follow-up. Parameter is the tap position in screen coordinates.</summary>
        public Action<Vector2> OnSingleTap;

        /// <summary>Occurs when a double tap has been recognized. Parameter is the tap position in screen coordinates.</summary>
        public Action<Vector2> OnDoubleTap;

        /// <summary>Occurs when a triple tap has been recognized. Parameter is the tap position in screen coordinates.</summary>
        public Action<Vector2> OnTripleTap;

        /// <summary>
        /// Occurs when a long press has been recognized. Parameter is the press position in screen coordinates.
        /// Fired once when the threshold is reached — the pointer is still down.
        /// </summary>
        public Action<Vector2> OnLongPress;

        /// <summary>Occurs when a drag gesture has begun. Parameter is the start position in screen coordinates.</summary>
        public Action<Vector2> OnDragStart;

        /// <summary>Occurs when an ongoing drag gesture updates. Parameter is the current position in screen coordinates.</summary>
        public Action<Vector2> OnDragUpdate;

        /// <summary>Occurs when a drag gesture has ended.</summary>
        public Action OnDragEnd;

        /// <summary>
        /// Occurs when a long-press drag has ended (pointer up after long press).
        /// Consumers typically finalize a magnifier → context menu transition here.
        /// </summary>
        public Action<Vector2> OnLongPressEnd;

        /// <summary>
        /// Occurs when a single-finger swipe has begun (pointer moved beyond drag threshold
        /// without multi-tap). Consumers typically scroll content from here.
        /// Parameter: delta since the previous event (screen pixels).
        /// </summary>
        public Action<Vector2> OnScrollDrag;

        /// <summary>Occurs when a single-finger scroll drag has ended.</summary>
        public Action OnScrollDragEnd;

        private float DragThresholdPx => GestureMetrics.SlopPx(Thresholds.DragSlopDp, CanvasSource?.Invoke());
        private float MultiTapSlopPx => GestureMetrics.SlopPx(Thresholds.MultiTapSlopDp, CanvasSource?.Invoke());

        /// <summary>
        /// Called when the pointer goes down (touch begin or mouse button press).
        /// </summary>
        /// <param name="position">Position in screen coordinates.</param>
        /// <param name="time">Current unscaled time (<c>Time.unscaledTime</c>).</param>
        public void OnPointerDown(Vector2 position, float time)
        {
            pendingSingleTap = false;
            downPosition = position;
            downTime = time;
            tapChain.Advance(position, time, Thresholds.MultiTapWindow, MultiTapSlopPx);
            state = GestureState.PointerDown;
        }

        /// <summary>
        /// Called when the pointer moves (touch move or mouse move while button is held).
        /// </summary>
        /// <param name="position">Current position in screen coordinates.</param>
        /// <param name="time">Current unscaled time.</param>
        public void OnPointerMove(Vector2 position, float time)
        {
            switch (state)
            {
                case GestureState.PointerDown:
                    if (Vector2.Distance(position, downPosition) >= DragThresholdPx)
                    {
                        if (tapChain.Count >= 2)
                        {
                            state = GestureState.Dragging;
                            dragOriginTapCount = tapChain.Count;
                            OnDragStart?.Invoke(downPosition);
                            OnDragUpdate?.Invoke(position);
                        }
                        else
                        {
                            state = GestureState.Scrolling;
                            lastScrollPosition = downPosition;
                            var delta = position - lastScrollPosition;
                            lastScrollPosition = position;
                            OnScrollDrag?.Invoke(delta);
                        }
                    }
                    break;

                case GestureState.Dragging:
                    OnDragUpdate?.Invoke(position);
                    break;

                case GestureState.Scrolling:
                {
                    var delta = position - lastScrollPosition;
                    lastScrollPosition = position;
                    OnScrollDrag?.Invoke(delta);
                    break;
                }

                case GestureState.LongPress:
                    if (!longPressDragStarted && Vector2.Distance(position, downPosition) >= DragThresholdPx)
                    {
                        longPressDragStarted = true;
                        OnDragStart?.Invoke(position);
                    }
                    OnDragUpdate?.Invoke(position);
                    break;
            }
        }

        /// <summary>
        /// Called when the pointer is released (touch end or mouse button release).
        /// </summary>
        /// <param name="position">Final position in screen coordinates.</param>
        /// <param name="time">Current unscaled time.</param>
        /// <remarks>
        /// A press still in <see cref="GestureState.PointerDown"/> at release whose travel from the
        /// down point already exceeds the drag threshold is a drag, not a tap — even when the consumer
        /// never delivered the intervening <see cref="OnPointerMove"/> calls (e.g. the moves were routed
        /// to an enclosing scroll / drag container). Classifying by down→up distance keeps a drag from
        /// reading as a tap purely because its moves bypassed the recogniser.
        /// </remarks>
        public void OnPointerUp(Vector2 position, float time)
        {
            switch (state)
            {
                case GestureState.PointerDown:
                    if (Vector2.Distance(position, downPosition) >= DragThresholdPx)
                    {
                        tapChain.Reset();
                        break;
                    }
                    tapChain.Stamp(position, time);
                    DispatchTap(position, time);
                    break;

                case GestureState.LongPress:
                    OnLongPressEnd?.Invoke(position);
                    tapChain.Reset();
                    break;

                case GestureState.Dragging:
                    OnDragEnd?.Invoke();
                    tapChain.Reset();
                    break;

                case GestureState.Scrolling:
                    OnScrollDragEnd?.Invoke();
                    tapChain.Reset();
                    break;
            }

            state = GestureState.Idle;
        }

        /// <summary>
        /// Must be called every frame (or at regular intervals) to detect long-press
        /// gestures while the pointer is held down without moving.
        /// </summary>
        /// <param name="time">Current unscaled time.</param>
        public void Update(float time)
        {
            if (pendingSingleTap && time - pendingSingleTapTime >= Thresholds.MultiTapWindow)
            {
                pendingSingleTap = false;
                OnSingleTap?.Invoke(pendingSingleTapPosition);
            }

            if (state != GestureState.PointerDown)
                return;

            if (time - downTime >= Thresholds.LongPressDuration)
            {
                state = GestureState.LongPress;
                longPressDragStarted = false;
                OnLongPress?.Invoke(downPosition);
            }
        }

        /// <summary>
        /// Resets all state. Call when focus is lost or the recognizer should be reset.
        /// </summary>
        public void Reset()
        {
            state = GestureState.Idle;
            tapChain.Reset();
            longPressDragStarted = false;
            pendingSingleTap = false;
        }

        /// <summary>
        /// Returns the tap count that initiated the current drag.
        /// 1 = single-tap drag (character selection), 2 = double-tap drag (word selection).
        /// 0 if no drag is active.
        /// </summary>
        public int DragOriginTapCount => state == GestureState.Dragging ? dragOriginTapCount : 0;

        /// <summary>
        /// Whether a long press is currently active (pointer still held down after long press fired).
        /// </summary>
        public bool IsLongPressActive => state == GestureState.LongPress;

        /// <summary>
        /// Whether a drag is currently in progress.
        /// </summary>
        public bool IsDragging => state == GestureState.Dragging;

        /// <summary>An armed single tap is waiting for the multi-tap window to close — the consumer must keep ticking <see cref="Update"/> until it fires.</summary>
        public bool HasPendingDispatch => pendingSingleTap;

        private bool pendingSingleTap;
        private Vector2 pendingSingleTapPosition;
        private float pendingSingleTapTime;

        /// <summary>
        /// A first tap places the caret immediately via <see cref="OnTapCaret"/> (no latency — the
        /// caret must track the finger), then arms a pending <see cref="OnSingleTap"/> fired from
        /// <see cref="Update"/> once the multi-tap window closes with no follow-up press. Only the
        /// flicker-prone affordances (insertion handle, context menu) wait for that window: running
        /// them immediately would show them on every double tap for the double-tap handler to undo a
        /// frame later (visible menu flicker, keyboard blink). Caret placement has no such conflict —
        /// a chained tap simply replaces it with a word/line selection.
        /// </summary>
        private void DispatchTap(Vector2 position, float time)
        {
            switch (tapChain.Count)
            {
                case 1:
                    OnTapCaret?.Invoke(position);
                    pendingSingleTap = true;
                    pendingSingleTapPosition = position;
                    pendingSingleTapTime = time;
                    break;
                case 2:
                    pendingSingleTap = false;
                    OnDoubleTap?.Invoke(position);
                    break;
                case >= 3:
                    pendingSingleTap = false;
                    OnTripleTap?.Invoke(position);
                    tapChain.Reset();
                    break;
            }
        }
    }
}
