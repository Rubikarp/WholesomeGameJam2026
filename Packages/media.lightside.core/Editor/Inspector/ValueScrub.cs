using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// The press-and-scrub gesture a value control drags on: the press hides and anchors the pointer and
    /// withholds focus, travel past the shared threshold starts the drag and is applied through the control's own
    /// <c>ApplyInputDeviceDelta</c> — so the value moves at whatever sensitivity its own type gives it, growing
    /// with its magnitude for a number — one drag collapses into a single undo step, Escape restores the value the
    /// drag started from, and a press that never crosses the threshold is handed back as a click. Mouse only: no
    /// other pointer device can be anchored.
    /// </summary>
    /// <typeparam name="TValue">Value type the driven control carries.</typeparam>
    public sealed class ValueScrub<TValue>
    {
        private readonly VisualElement zone;
        private readonly IValueField<TValue> driven;
        private readonly Func<bool> canScrub;
        private readonly Action clicked;
        private readonly bool horizontal;

        private VisualElement pressTarget;
        private Vector2 travel;
        private TValue startValue;
        private int pointerId = -1;
        private int undoGroup = -1;
        private bool scrubbing;
        private bool refocusable;

        /// <summary>Installs the gesture; it lives as long as <paramref name="zone"/> does.</summary>
        /// <param name="zone">Element the gesture presses on and captures the pointer to.</param>
        /// <param name="driven">Control the travel is applied to.</param>
        /// <param name="canScrub">Whether a press may start a drag at all — where a control is read-only, disabled, or already being typed into, it refuses here.</param>
        /// <param name="horizontal">Whether horizontal travel alone drives the value, as a single-axis control needs; otherwise both axes are passed through.</param>
        /// <param name="clicked">Invoked for a press that never crossed the drag threshold, or <see langword="null"/> where a click means nothing to the control.</param>
        /// <exception cref="ArgumentNullException"><paramref name="zone"/>, <paramref name="driven"/> or <paramref name="canScrub"/> is <see langword="null"/>.</exception>
        public ValueScrub(VisualElement zone, IValueField<TValue> driven, Func<bool> canScrub, bool horizontal,
            Action clicked = null)
        {
            this.zone = zone ?? throw new ArgumentNullException(nameof(zone));
            this.driven = driven ?? throw new ArgumentNullException(nameof(driven));
            this.canScrub = canScrub ?? throw new ArgumentNullException(nameof(canScrub));
            this.horizontal = horizontal;
            this.clicked = clicked;

            zone.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            zone.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            zone.RegisterCallback<PointerUpEvent>(OnPointerUp);
            zone.RegisterCallback<PointerCaptureOutEvent>(_ => Release());
            zone.RegisterCallback<KeyDownEvent>(OnKeyDown);
            InspectorGestures.HoldPressedState(zone);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.pointerType != UnityEngine.UIElements.PointerType.mouse || !canScrub()) return;
            pointerId = evt.pointerId;
            travel = Vector2.zero;
            startValue = driven.value;
            scrubbing = false;
            pressTarget = evt.target as VisualElement ?? zone;
            WithholdFocus();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Drag Value");
            undoGroup = Undo.GetCurrentGroup();
            zone.CapturePointer(pointerId);
            InspectorGestures.HidePointer(zone.panel, evt.position);
            evt.StopPropagation();
        }

        /// <summary>
        /// Keeps the press from focusing the control: the focus controller reads the pressed element after the
        /// event is dispatched, so stopping propagation cannot hold it back and only an unfocusable element can.
        /// </summary>
        private void WithholdFocus()
        {
            if (!pressTarget.focusable) return;
            pressTarget.focusable = false;
            refocusable = true;
        }

        private void RestoreFocusable()
        {
            if (!refocusable) return;
            refocusable = false;
            pressTarget.focusable = true;
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != pointerId || !zone.HasPointerCapture(pointerId)) return;
            evt.StopPropagation();
            var delta = InspectorGestures.TrackPointer(evt.deltaPosition, evt.position);
            if (delta.sqrMagnitude <= Mathf.Epsilon) return;
            travel += new Vector2(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            if (!scrubbing)
            {
                if (!InspectorGestures.ExceedsDragThreshold(travel)) return;
                scrubbing = true;
                driven.StartDragging();
            }
            driven.ApplyInputDeviceDelta(horizontal ? new Vector2(delta.x, 0f) : delta, Speed(evt), startValue);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != pointerId) return;
            var scrubbed = scrubbing;
            Release();
            if (!scrubbed) clicked?.Invoke();
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (pointerId < 0 || evt.keyCode != KeyCode.Escape) return;
            var scrubbed = scrubbing;
            Release();
            if (scrubbed) driven.value = startValue;
            evt.StopPropagation();
        }

        private void Release()
        {
            if (pointerId < 0) return;
            var captured = pointerId;
            pointerId = -1;
            InspectorGestures.ShowPointer();
            RestoreFocusable();
            if (scrubbing)
            {
                scrubbing = false;
                driven.StopDragging();
            }
            if (zone.HasPointerCapture(captured)) zone.ReleasePointer(captured);
            Undo.CollapseUndoOperations(undoGroup);
            undoGroup = -1;
        }

        private static DeltaSpeed Speed(IPointerEvent evt) => evt.shiftKey
            ? DeltaSpeed.Fast
            : evt.altKey ? DeltaSpeed.Slow : DeltaSpeed.Normal;
    }
}
