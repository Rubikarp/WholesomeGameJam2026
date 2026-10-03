using System;

namespace LightSide
{
    /// <summary>
    /// Stable subscription surface for all interactive entities published through one
    /// <see cref="RangeChannel"/> asset on a specific UniText component.
    /// </summary>
    public sealed class RangeInteractionChannel
    {
        /// <summary>The channel asset used as routing identity.</summary>
        public RangeChannel Channel { get; }

        /// <summary>Occurs after a primary click, tap or keyboard activation is confirmed.</summary>
        public event RangeInteractionHandler Activated;
        /// <summary>Occurs for secondary click, long press or keyboard context requests.</summary>
        public event RangeInteractionHandler ContextRequested;
        /// <summary>Occurs when a pointer enters an entity.</summary>
        public event RangeInteractionHandler Entered;
        /// <summary>Occurs when a pointer exits an entity.</summary>
        public event RangeInteractionHandler Exited;
        /// <summary>Occurs for independent Hovered, Pressed, Focused or Disabled transitions.</summary>
        public event RangeInteractionHandler StateChanged;
        /// <summary>Occurs while a touch or pen hold advances toward a context request.</summary>
        public event RangeInteractionHandler LongPressProgress;
        /// <summary>Occurs for the advanced pointer lifecycle and claimed custom gestures.</summary>
        public event RangeInteractionHandler Gesture;
        /// <summary>Occurs when keyboard or gamepad focus enters or leaves an entity.</summary>
        public event RangeInteractionHandler FocusChanged;
        /// <summary>Occurs for every event delivered to this channel.</summary>
        public event RangeInteractionHandler Interaction;

        internal RangeInteractionChannel(RangeChannel channel)
        {
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

        internal void Raise(RangeInteraction interaction)
        {
            Interaction?.Invoke(interaction);
            if (interaction.Handled) return;

            switch (interaction.Kind)
            {
                case RangeInteractionKind.Activated:
                    Activated?.Invoke(interaction);
                    break;
                case RangeInteractionKind.ContextRequested:
                    ContextRequested?.Invoke(interaction);
                    break;
                case RangeInteractionKind.Entered:
                    Entered?.Invoke(interaction);
                    break;
                case RangeInteractionKind.Exited:
                    Exited?.Invoke(interaction);
                    break;
                case RangeInteractionKind.StateChanged:
                    StateChanged?.Invoke(interaction);
                    break;
                case RangeInteractionKind.LongPressProgress:
                    LongPressProgress?.Invoke(interaction);
                    break;
                case RangeInteractionKind.Pressed:
                case RangeInteractionKind.Released:
                case RangeInteractionKind.Canceled:
                case RangeInteractionKind.Gesture:
                    Gesture?.Invoke(interaction);
                    break;
                case RangeInteractionKind.Focused:
                case RangeInteractionKind.Blurred:
                    FocusChanged?.Invoke(interaction);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
