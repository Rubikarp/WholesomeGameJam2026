using UnityEngine;

namespace LightSide
{
    /// <summary>The kind of input a <see cref="PointEditEvent"/> carries.</summary>
    public enum PointEditEventType
    {
        /// <summary>An event the host did not map to any of the kinds below; the point editor ignores it.</summary>
        None,
        PointerDown,
        PointerMove,
        PointerUp,
        KeyDown,
    }

    /// <summary>
    /// One input event in a form both an IMGUI SceneView host and a retained UI Toolkit host can produce, so
    /// <see cref="PointEditor"/> never sees either framework's own event type.
    /// </summary>
    public readonly struct PointEditEvent
    {
        /// <summary>What kind of input this is.</summary>
        public readonly PointEditEventType Type;

        /// <summary>Pointer button, 0 for primary; -1 when the event is not a pointer event.</summary>
        public readonly int Button;

        public readonly KeyCode KeyCode;
        public readonly char Character;
        public readonly EventModifiers Modifiers;

        /// <summary>Creates an event; every argument past the kind is optional and defaults to "not applicable".</summary>
        public PointEditEvent(PointEditEventType type, int button = -1,
            KeyCode keyCode = KeyCode.None, char character = default,
            EventModifiers modifiers = EventModifiers.None)
        {
            Type = type;
            Button = button;
            KeyCode = keyCode;
            Character = character;
            Modifiers = modifiers;
        }

        public bool Shift => (Modifiers & EventModifiers.Shift) != 0;
        public bool Control => (Modifiers & EventModifiers.Control) != 0;
        public bool Alt => (Modifiers & EventModifiers.Alt) != 0;
        public bool Command => (Modifiers & EventModifiers.Command) != 0;

        /// <summary>Adapts one IMGUI event; a type with no neutral equivalent yields <see cref="PointEditEventType.None"/>.</summary>
        public static PointEditEvent From(Event e)
        {
            var type = e.type switch
            {
                EventType.MouseDown => PointEditEventType.PointerDown,
                EventType.MouseDrag or EventType.MouseMove => PointEditEventType.PointerMove,
                EventType.MouseUp => PointEditEventType.PointerUp,
                EventType.KeyDown => PointEditEventType.KeyDown,
                _ => PointEditEventType.None,
            };
            return new PointEditEvent(type, e.button, e.keyCode, e.character, e.modifiers);
        }
    }
}
