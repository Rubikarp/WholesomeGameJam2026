using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// What initiated a <see cref="TextPointerEvent"/>. Lets handlers behave differently
    /// for tap-style versus context-menu-style triggers without inspecting the underlying
    /// Unity event.
    /// </summary>
    public enum PointerTrigger
    {
        /// <summary>Primary button press (left mouse button, single tap on touch).</summary>
        PrimaryClick,

        /// <summary>Secondary button press (right mouse button). Context-menu intent on desktop.</summary>
        SecondaryClick,

        /// <summary>Pointer held in place past the long-press threshold. Context-menu intent on touch / pen.</summary>
        LongPress,

        /// <summary>Hover movement without a press (<see cref="UniTextBase.PointerEntered"/>/<see cref="UniTextBase.PointerExited"/>).</summary>
        Hover,

        /// <summary>Keyboard-driven request (e.g. Shift+F10, Application key). Reserved; not yet emitted.</summary>
        Keyboard
    }

    /// <summary>
    /// Physical device class behind a pointer event. Resolved once by <see cref="UniTextBase"/>
    /// (new Input System pointer type when available, legacy pointerId convention otherwise) —
    /// consumers must use this instead of re-deriving touchness from pointer ids.
    /// </summary>
    public enum PointerKind : byte
    {
        Mouse,
        Touch,
        Pen
    }

    /// <summary>Keyboard modifier keys held at the moment a pointer event was raised.</summary>
    [Flags]
    public enum PointerModifiers
    {
        None    = 0,
        Shift   = 1 << 0,
        Control = 1 << 1,
        Alt     = 1 << 2,
        Meta    = 1 << 3,
    }

    /// <summary>
    /// Mutable carrier for consumable pointer events emitted by <see cref="UniTextBase"/>.
    /// On click/context resolution, <see cref="Consumed"/> suppresses propagation to parent UI.
    /// During <see cref="UniTextBase.PointerPressed"/> it suppresses later ordered component
    /// defaults such as caret placement while preserving parent drag/scroll propagation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host reuses ONE instance across emissions (zero allocation per pointer event, the
    /// <c>PointerEventData</c> model) — do not retain a reference beyond the callback; copy
    /// the fields you need.
    /// </para>
    /// <para>
    /// Multi-tap detection (double / triple click → word / line selection) is the consumer's
    /// responsibility — the host emits one event per click, without aggregating taps.
    /// </para>
    /// </remarks>
    public sealed class TextPointerEvent
    {
        /// <summary>The hit-test result produced from the originating screen position.</summary>
        public TextHitResult Hit { get; private set; }

        /// <summary>
        /// What initiated the event. Use to distinguish primary clicks from context-menu
        /// requests (right-click on desktop, long-press on touch / pen).
        /// </summary>
        public PointerTrigger Trigger { get; private set; }

        /// <summary>Physical device class behind the event.</summary>
        public PointerKind Kind { get; private set; }

        /// <summary>Platform pointer identity, stable for the lifetime of one pointer contact.</summary>
        public int PointerId { get; private set; }

        /// <summary>
        /// Originating pointer position in screen coordinates. Useful for placing
        /// context-menu UI directly under the press without re-deriving from the hit's
        /// glyph position (which lives in text-local space).
        /// </summary>
        public Vector2 ScreenPosition { get; private set; }

        /// <summary>
        /// Camera that owns the event (null for screen-space overlay canvases). Required
        /// for any subsequent screen-to-local conversions on the consumer side.
        /// </summary>
        public Camera EventCamera { get; private set; }

        /// <summary>Keyboard modifier keys held at the moment of the event.</summary>
        public PointerModifiers Modifiers { get; private set; }

        /// <summary>
        /// Set to <see langword="true"/> by a subscriber that claimed the event. Meaning is
        /// phase-specific: a press suppresses later ordered component defaults; release suppresses
        /// the pending click; resolved click/context also suppresses propagation to the parent UI.
        /// </summary>
        public bool Consumed { get; set; }

        /// <summary>Creates an empty reusable carrier that the host populates before dispatch.</summary>
        public TextPointerEvent() { }

        /// <summary>
        /// Creates a pointer-event snapshot.
        /// </summary>
        /// <param name="hit">Range hit produced at the event position, or <see cref="TextHitResult.None"/>.</param>
        /// <param name="trigger">Semantic pointer action that produced the event.</param>
        /// <param name="screenPosition">Originating position in screen coordinates.</param>
        /// <param name="eventCamera">Camera used for screen-to-local conversion, or null for overlay UI.</param>
        /// <param name="modifiers">Keyboard modifiers held at dispatch time.</param>
        /// <param name="kind">Physical pointer device class.</param>
        /// <param name="pointerId">Pointer identity for this contact.</param>
        public TextPointerEvent(TextHitResult hit, PointerTrigger trigger, Vector2 screenPosition,
            Camera eventCamera, PointerModifiers modifiers = PointerModifiers.None,
            PointerKind kind = PointerKind.Mouse, int pointerId = 0)
        {
            Set(hit, trigger, screenPosition, eventCamera, modifiers, kind, pointerId);
        }

        internal TextPointerEvent Set(TextHitResult hit, PointerTrigger trigger, Vector2 screenPosition,
            Camera eventCamera, PointerModifiers modifiers, PointerKind kind, int pointerId = 0)
        {
            Hit = hit;
            Trigger = trigger;
            ScreenPosition = screenPosition;
            EventCamera = eventCamera;
            Modifiers = modifiers;
            Kind = kind;
            PointerId = pointerId;
            Consumed = false;
            return this;
        }
    }
}
