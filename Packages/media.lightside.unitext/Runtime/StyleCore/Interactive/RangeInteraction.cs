using UnityEngine;

namespace LightSide
{
    /// <summary>Semantic or gesture event emitted for one interactive range entity.</summary>
    public enum RangeInteractionKind : byte
    {
        /// <summary>A primary click, tap or keyboard command activated the entity.</summary>
        Activated,
        /// <summary>A secondary click, long press or keyboard command requested contextual actions.</summary>
        ContextRequested,
        /// <summary>A pointer entered the entity's hit geometry.</summary>
        Entered,
        /// <summary>A pointer left the entity's hit geometry.</summary>
        Exited,
        /// <summary>An independent interaction signal changed.</summary>
        StateChanged,
        /// <summary>A long-press recognizer reported normalized progress.</summary>
        LongPressProgress,
        /// <summary>A primary pointer began a captured gesture over the entity.</summary>
        Pressed,
        /// <summary>A captured primary pointer was released.</summary>
        Released,
        /// <summary>A captured gesture ended without activation.</summary>
        Canceled,
        /// <summary>The entity received keyboard or gamepad focus.</summary>
        Focused,
        /// <summary>The entity lost keyboard or gamepad focus.</summary>
        Blurred,
        /// <summary>A custom recognizer claimed, updated or completed its gesture.</summary>
        Gesture,
    }

    /// <summary>Current route location of a <see cref="RangeInteraction"/> event.</summary>
    public enum RangeInteractionRoute : byte
    {
        /// <summary>The host-level preview handler runs before the target channel.</summary>
        Capture,
        /// <summary>The channel and owning modifier receive the event.</summary>
        Target,
        /// <summary>The host-level handler runs after the target channel.</summary>
        Bubble,
    }

    /// <summary>Scope used when comparing the press origin with a later activation target.</summary>
    public enum RangeInteractionScope : byte
    {
        /// <summary>The concrete contiguous segment must match.</summary>
        Segment,
        /// <summary>Any segment belonging to the same stable entity matches.</summary>
        Entity,
        /// <summary>Any enabled target published through the same channel matches.</summary>
        Channel,
    }

    /// <summary>Direction requested by keyboard, gamepad or programmatic range navigation.</summary>
    public enum RangeNavigationDirection : byte
    {
        /// <summary>Previous item in logical source order.</summary>
        Previous,
        /// <summary>Next item in logical source order.</summary>
        Next,
        /// <summary>Nearest visual item to the left.</summary>
        Left,
        /// <summary>Nearest visual item to the right.</summary>
        Right,
        /// <summary>Nearest visual item above.</summary>
        Up,
        /// <summary>Nearest visual item below.</summary>
        Down,
        /// <summary>First eligible item.</summary>
        Home,
        /// <summary>Last eligible item.</summary>
        End,
    }

    /// <summary>Ordering used for arrow/gamepad movement inside one text surface.</summary>
    public enum RangeNavigationOrder : byte
    {
        /// <summary>Rendered source order, stable across reflow.</summary>
        Logical,
        /// <summary>Nearest final visual geometry in the requested direction.</summary>
        Visual,
    }

    /// <summary>Group boundary used by internal range navigation.</summary>
    public enum RangeNavigationGroup : byte
    {
        /// <summary>All interactive entities on the UniText component.</summary>
        Host,
        /// <summary>Only entities on the focused range's channel.</summary>
        Channel,
    }

    /// <summary>Independent boolean interaction signal changed by a state event.</summary>
    public enum RangeInteractionSignal : byte
    {
        /// <summary>At least one pointer currently rests over the entity.</summary>
        Hovered,
        /// <summary>At least one pointer currently holds an uncanceled press lease.</summary>
        Pressed,
        /// <summary>Keyboard or gamepad navigation currently addresses the entity.</summary>
        Focused,
        /// <summary>The entity is present semantically but rejects interaction.</summary>
        Disabled,
    }

    /// <summary>How hit fragments separated by wrapping or BiDi layout are combined.</summary>
    public enum WrappedGapPolicy : byte
    {
        /// <summary>Every final layout fragment remains an independent target.</summary>
        Separate,
        /// <summary>Fragments on the same visual line are joined across their horizontal gap.</summary>
        JoinLineFragments,
        /// <summary>The complete multi-line entity uses one explicit bounding block.</summary>
        BoundingBlock,
    }

    /// <summary>Whether layout whitespace or only rendered glyph faces contribute to hit geometry.</summary>
    public enum RangeWhitespacePolicy : byte
    {
        /// <summary>Use final line fragments, including meaningful spaces and tabs inside the range.</summary>
        Layout,
        /// <summary>Use only final rendered glyph fragments.</summary>
        VisibleGlyphs,
    }

    /// <summary>How U+FFFC inline-object clusters participate in range hit geometry.</summary>
    public enum InlineObjectPolicy : byte
    {
        /// <summary>Inline objects and ordinary text are both targetable.</summary>
        Include,
        /// <summary>Inline-object fragments are excluded.</summary>
        Exclude,
        /// <summary>Only inline-object fragments are targetable.</summary>
        Only,
    }

    /// <summary>
    /// Mutable, borrowed event context passed synchronously through capture, target and bubble routing.
    /// Copy values needed after the callback; the router reuses the instance after dispatch returns.
    /// </summary>
    public sealed class RangeInteraction
    {
        /// <summary>Kind of semantic or gesture event being routed.</summary>
        public RangeInteractionKind Kind { get; internal set; }
        /// <summary>Current capture, target or bubble route location.</summary>
        public RangeInteractionRoute Route { get; internal set; }
        /// <summary>Stable semantic channel, or null for modifier-local routing.</summary>
        public RangeChannel Channel { get; internal set; }
        /// <summary>Range source that owns the entity.</summary>
        public RangeSource Source { get; internal set; }
        /// <summary>Stable source-scoped entity identity.</summary>
        public RangeIdentity Entity { get; internal set; }
        /// <summary>Concrete segment hit by this event.</summary>
        public RangeSegment HitSegment { get; internal set; }
        /// <summary>Typed read-only semantic value carried by the entity.</summary>
        public RangePayloadView Payload { get; internal set; }
        /// <summary>Rendered-text revision against which the event target was resolved.</summary>
        public TextRevision Revision { get; internal set; }
        /// <summary>Attributed interactive target owned by the target modifier.</summary>
        public InteractiveRange Range { get; internal set; }
        /// <summary>Text hit corresponding to the concrete visual fragment.</summary>
        public TextHitResult TextHit { get; internal set; }
        /// <summary>Input trigger that produced the event.</summary>
        public PointerTrigger Trigger { get; internal set; }
        /// <summary>Physical pointer class. Keyboard events set <see cref="HasPointer"/> to false.</summary>
        public PointerKind PointerKind { get; internal set; }
        /// <summary>Platform pointer identity, valid only when <see cref="HasPointer"/> is true.</summary>
        public int PointerId { get; internal set; }
        /// <summary>Whether pointer positions and identity are present.</summary>
        public bool HasPointer { get; internal set; }
        /// <summary>Pointer position in screen coordinates.</summary>
        public Vector2 ScreenPosition { get; internal set; }
        /// <summary>Pointer position in component-local coordinates.</summary>
        public Vector2 LocalPosition { get; internal set; }
        /// <summary>Camera used to convert the originating pointer coordinates.</summary>
        public Camera EventCamera { get; internal set; }
        /// <summary>Keyboard modifiers held when the event was emitted.</summary>
        public PointerModifiers Modifiers { get; internal set; }
        /// <summary>Component-local union bounds suitable for popovers and accessibility.</summary>
        public Rect AnchorRect { get; internal set; }
        /// <summary>Previous state for <see cref="RangeInteractionKind.StateChanged"/>.</summary>
        public RangeState PreviousState { get; internal set; }
        /// <summary>Current state for <see cref="RangeInteractionKind.StateChanged"/>.</summary>
        public RangeState State { get; internal set; }
        /// <summary>Independent signal changed by a state event.</summary>
        public RangeInteractionSignal Signal { get; internal set; }
        /// <summary>Previous boolean value of <see cref="Signal"/>.</summary>
        public bool PreviousSignalValue { get; internal set; }
        /// <summary>Current boolean value of <see cref="Signal"/>.</summary>
        public bool SignalValue { get; internal set; }
        /// <summary>Normalized gesture progress for <see cref="RangeInteractionKind.LongPressProgress"/>.</summary>
        public float Progress { get; internal set; }
        /// <summary>Custom recognizer responsible for <see cref="RangeInteractionKind.Gesture"/>.</summary>
        public RangeGestureRecognizer GestureRecognizer { get; internal set; }
        /// <summary>Custom lifecycle phase for <see cref="RangeInteractionKind.Gesture"/>.</summary>
        public RangeGesturePhase GesturePhase { get; internal set; }
        /// <summary>Whether a handler stopped the remaining capture/target/bubble route.</summary>
        public bool Handled { get; set; }
        /// <summary>Whether the target's built-in action must be skipped after user handlers return.</summary>
        public bool DefaultPrevented { get; private set; }

        /// <summary>Stops later route stages without canceling the built-in action.</summary>
        public void Handle() => Handled = true;

        /// <summary>Cancels the built-in action without implicitly stopping event propagation.</summary>
        public void PreventDefault() => DefaultPrevented = true;

        internal void ResetRouting()
        {
            Handled = false;
            DefaultPrevented = false;
            Route = RangeInteractionRoute.Capture;
            PreviousState = RangeState.Normal;
            State = RangeState.Normal;
            Signal = RangeInteractionSignal.Hovered;
            PreviousSignalValue = false;
            SignalValue = false;
            Progress = 0f;
            GestureRecognizer = null;
            GesturePhase = default;
        }
    }

    /// <summary>Signature for synchronous range interaction handlers.</summary>
    public delegate void RangeInteractionHandler(RangeInteraction interaction);

    /// <summary>Platform-neutral request to reveal one interactive entity in an owning viewport.</summary>
    public readonly struct RangeScrollRequest
    {
        /// <summary>Text component containing the entity.</summary>
        public UniTextBase Text { get; }
        /// <summary>Representative attributed range for the logical entity.</summary>
        public InteractiveRange Range { get; }
        /// <summary>Union of current final local-space hit bounds.</summary>
        public Rect Bounds { get; }

        internal RangeScrollRequest(UniTextBase text, in InteractiveRange range, Rect bounds)
        {
            Text = text;
            Range = range;
            Bounds = bounds;
        }
    }
}
