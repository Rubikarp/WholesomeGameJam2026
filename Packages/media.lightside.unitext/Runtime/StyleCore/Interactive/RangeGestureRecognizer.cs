using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Phase delivered to a custom range gesture recognizer.</summary>
    public enum RangeGesturePhase : byte
    {
        /// <summary>The primary pointer was pressed over the target.</summary>
        Pressed,
        /// <summary>The pointer moved while the target still owns the press lifecycle.</summary>
        Moved,
        /// <summary>A touch or pen hold advanced toward the long-press threshold.</summary>
        LongPressProgress,
        /// <summary>The claimed gesture ended through pointer release.</summary>
        Released,
        /// <summary>The press target disappeared, lost arbitration or was canceled.</summary>
        Canceled,
    }

    /// <summary>Decision returned by a custom gesture recognizer before arbitration resolves.</summary>
    public enum RangeGestureDecision : byte
    {
        /// <summary>Keep observing later phases without owning the gesture yet.</summary>
        Pending,
        /// <summary>Stop considering this recognizer for the current phase.</summary>
        Reject,
        /// <summary>Claim the gesture using the recognizer's priority and compatibility.</summary>
        Claim,
    }

    /// <summary>Built-in gesture owners that may run alongside a claimed custom gesture.</summary>
    [Flags]
    public enum RangeGestureCompatibility : byte
    {
        /// <summary>The custom gesture exclusively owns an otherwise conflicting drag.</summary>
        None = 0,
        /// <summary>Text selection may continue receiving the same drag lifecycle.</summary>
        TextSelection = 1 << 0,
        /// <summary>An enclosing ScrollRect or other parent drag handler may continue receiving it.</summary>
        ParentScroll = 1 << 1,
    }

    /// <summary>
    /// Borrowed input snapshot evaluated by a custom recognizer. A recognizer instance may serve
    /// multiple entities and pointers, so persistent state must be keyed by <see cref="PointerId"/>
    /// and <see cref="InteractiveRange.Identity"/> rather than stored as one implicit session.
    /// </summary>
    public readonly struct RangeGestureContext
    {
        /// <summary>Concrete attributed range that received the original press.</summary>
        public InteractiveRange Range { get; }
        /// <summary>Current gesture phase.</summary>
        public RangeGesturePhase Phase { get; }
        /// <summary>Physical pointer class.</summary>
        public PointerKind PointerKind { get; }
        /// <summary>Platform pointer identity.</summary>
        public int PointerId { get; }
        /// <summary>Position at primary pointer down, in screen coordinates.</summary>
        public Vector2 StartScreenPosition { get; }
        /// <summary>Current position in screen coordinates.</summary>
        public Vector2 ScreenPosition { get; }
        /// <summary>Current position in UniText local coordinates.</summary>
        public Vector2 LocalPosition { get; }
        /// <summary>Screen-space delta since the last delivered phase.</summary>
        public Vector2 Delta { get; }
        /// <summary>Camera associated with the current pointer event.</summary>
        public Camera EventCamera { get; }
        /// <summary>Keyboard modifiers held for the current pointer event.</summary>
        public PointerModifiers Modifiers { get; }
        /// <summary>Whether travel from pointer down reached the configured drag slop.</summary>
        public bool DragSlopExceeded { get; }
        /// <summary>Normalized built-in hold progress for <see cref="RangeGesturePhase.LongPressProgress"/>.</summary>
        public float Progress { get; }
        /// <summary>Current unscaled Unity time.</summary>
        public float Timestamp { get; }

        internal RangeGestureContext(in InteractiveRange range, RangeGesturePhase phase,
            PointerKind pointerKind, int pointerId, Vector2 startScreenPosition,
            Vector2 screenPosition, Vector2 localPosition, Vector2 delta, Camera eventCamera,
            PointerModifiers modifiers, bool dragSlopExceeded, float progress, float timestamp)
        {
            Range = range;
            Phase = phase;
            PointerKind = pointerKind;
            PointerId = pointerId;
            StartScreenPosition = startScreenPosition;
            ScreenPosition = screenPosition;
            LocalPosition = localPosition;
            Delta = delta;
            EventCamera = eventCamera;
            Modifiers = modifiers;
            DragSlopExceeded = dragSlopExceeded;
            Progress = progress;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Serializable custom gesture policy evaluated by the component's shared interaction router.
    /// Return <see cref="RangeGestureDecision.Claim"/> only when the gesture is recognized; the
    /// router resolves simultaneous claims by priority and then stable Inspector declaration order.
    /// </summary>
    [Serializable]
    public abstract partial class RangeGestureRecognizer : RangeConfigurationObject
    {
        /// <summary>
        /// Priority of the built-in selection and scroll drag owners. A custom drag claim must use
        /// a greater value; zero or lower yields when the pointer crosses drag slop.
        /// </summary>
        public const int BuiltInDragPriority = 0;

        /// <summary>Arbitration priority; higher values win before Inspector declaration order.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))]
        [Tooltip("Wins when multiple custom recognizers claim the same pointer phase.")]
        private int priority = BuiltInDragPriority + 1;

        /// <summary>Built-in drag owners allowed to run alongside a claimed gesture.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))]
        [Tooltip("Built-in drag owners allowed to continue after this recognizer claims.")]
        private RangeGestureCompatibility compatibility;

        /// <summary>
        /// Evaluates one pointer phase. After this recognizer claims, it exclusively receives later
        /// phases until Released or Canceled; the returned decision is ignored for those terminal phases.
        /// </summary>
        protected internal abstract RangeGestureDecision Evaluate(in RangeGestureContext context);
    }

    /// <summary>Claims a custom drag after the shared pointer slop is exceeded.</summary>
    [Serializable]
    [TypeDescription("Drag: claim after pointer slop")]
    public sealed class DragRangeGestureRecognizer : RangeGestureRecognizer
    {
        /// <inheritdoc/>
        protected internal override RangeGestureDecision Evaluate(in RangeGestureContext context)
            => context.Phase == RangeGesturePhase.Moved && context.DragSlopExceeded
                ? RangeGestureDecision.Claim
                : RangeGestureDecision.Pending;
    }
}
