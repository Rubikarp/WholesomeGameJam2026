using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Default-action events that may execute an Inspector-authored range action.</summary>
    [Flags]
    public enum RangeActionEvents : byte
    {
        /// <summary>No routed event executes the action.</summary>
        None = 0,
        /// <summary>Primary click, tap or keyboard/gamepad activation.</summary>
        Activated = 1 << 0,
        /// <summary>Secondary click, long press or semantic context request.</summary>
        ContextRequested = 1 << 1,
    }

    /// <summary>Whether later actions in the same authored list should run.</summary>
    public enum RangeActionFlow : byte
    {
        /// <summary>Continue with the next action.</summary>
        Continue,
        /// <summary>Finish the current default-action list successfully.</summary>
        Stop,
    }

    /// <summary>Source resolved by an Inspector-authored string action value.</summary>
    public enum RangeActionStringSource : byte
    {
        /// <summary>Use the opaque primary value emitted by the source.</summary>
        PrimaryValue,
        /// <summary>Use a literal authored in the action.</summary>
        Literal,
        /// <summary>Read a named member from the entity payload.</summary>
        PayloadMember,
        /// <summary>Use the rendered text covered by the hit segment.</summary>
        SegmentText,
    }

    /// <summary>
    /// Stable value snapshot passed to ordered actions. Unlike <see cref="RangeInteraction"/>, it
    /// may be retained by asynchronous project work after routed dispatch returns.
    /// </summary>
    public readonly struct RangeActionContext
    {
        /// <summary>Text surface that owns the entity.</summary>
        public UniTextBase Text { get; }
        /// <summary>Activation or context event executing the action list.</summary>
        public RangeInteractionKind Kind { get; }
        /// <summary>Stable semantic channel.</summary>
        public RangeChannel Channel { get; }
        /// <summary>Source that owns the entity.</summary>
        public RangeSource Source { get; }
        /// <summary>Stable source-scoped entity identity.</summary>
        public RangeIdentity Entity { get; }
        /// <summary>Concrete segment that received the event.</summary>
        public RangeSegment HitSegment { get; }
        /// <summary>Typed immutable payload.</summary>
        public RangePayloadView Payload { get; }
        /// <summary>Rendered revision used to resolve the target.</summary>
        public TextRevision Revision { get; }
        /// <summary>Attributed interactive target.</summary>
        public InteractiveRange Range { get; }
        /// <summary>Input trigger that produced the action.</summary>
        public PointerTrigger Trigger { get; }
        /// <summary>Whether pointer identity and positions are present.</summary>
        public bool HasPointer { get; }
        /// <summary>Physical pointer class.</summary>
        public PointerKind PointerKind { get; }
        /// <summary>Platform pointer identity.</summary>
        public int PointerId { get; }
        /// <summary>Pointer position in screen coordinates.</summary>
        public Vector2 ScreenPosition { get; }
        /// <summary>Pointer position in component-local coordinates.</summary>
        public Vector2 LocalPosition { get; }
        /// <summary>Component-local union bounds suitable for popovers.</summary>
        public Rect AnchorRect { get; }

        internal RangeActionContext(UniTextBase text, RangeInteraction interaction)
        {
            Text = text;
            Kind = interaction.Kind;
            Channel = interaction.Channel;
            Source = interaction.Source;
            Entity = interaction.Entity;
            HitSegment = interaction.HitSegment;
            Payload = interaction.Payload;
            Revision = interaction.Revision;
            Range = interaction.Range;
            Trigger = interaction.Trigger;
            HasPointer = interaction.HasPointer;
            PointerKind = interaction.PointerKind;
            PointerId = interaction.PointerId;
            ScreenPosition = interaction.ScreenPosition;
            LocalPosition = interaction.LocalPosition;
            AnchorRect = interaction.AnchorRect;
        }
    }

    /// <summary>Serializable string resolver shared by built-in and custom range actions.</summary>
    [Serializable]
    public sealed partial class RangeActionString : RangeConfigurationObject
    {
        /// <summary>Source used for every invocation.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private RangeActionStringSource source;
        /// <summary>Value returned by <see cref="RangeActionStringSource.Literal"/>.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private string literal;
        /// <summary>Named payload path used by <see cref="RangeActionStringSource.PayloadMember"/>.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private string payloadMember;

        /// <summary>Resolves this value from the stable action context.</summary>
        public string Resolve(in RangeActionContext context)
        {
            return source switch
            {
                RangeActionStringSource.PrimaryValue => context.Range.PrimaryValue,
                RangeActionStringSource.Literal => literal,
                RangeActionStringSource.PayloadMember =>
                    RangePayloadBinding.ReadString(context.Payload, payloadMember),
                RangeActionStringSource.SegmentText =>
                    context.Text.GetRangeText(context.HitSegment.Range),
                _ => throw new ArgumentOutOfRangeException(),
            };
        }
    }

    /// <summary>
    /// Serializable product action executed after capture, target and bubble handlers have had a
    /// chance to call <see cref="RangeInteraction.PreventDefault"/>. Actions run in Inspector order.
    /// </summary>
    [Serializable]
    public abstract partial class RangeAction : RangeConfigurationObject
    {
        /// <summary>Routed default-action events accepted by this action.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private RangeActionEvents events = RangeActionEvents.Activated;

        internal RangeActionFlow TryExecute(in RangeActionContext context)
        {
            var current = context.Kind switch
            {
                RangeInteractionKind.Activated => RangeActionEvents.Activated,
                RangeInteractionKind.ContextRequested => RangeActionEvents.ContextRequested,
                _ => RangeActionEvents.None,
            };
            return current != RangeActionEvents.None && (events & current) != 0
                ? Execute(in context)
                : RangeActionFlow.Continue;
        }

        /// <summary>Executes the action against a stable context safe to retain for asynchronous work.</summary>
        protected internal abstract RangeActionFlow Execute(in RangeActionContext context);
    }

    /// <summary>Opens a URL resolved from the primary value, a literal, segment text or typed payload.</summary>
    [Serializable]
    [TypeDescription("Open URL using primary value, text, a literal or payload member")]
    public sealed partial class OpenUrlAction : RangeAction
    {
        [SerializeField, StateProperty(nameof(ApplyUrlChange))] private RangeActionString url = new();

        private void ApplyUrlChange(RangeActionString previous, ref RangeActionString current)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            ApplyConfigurationChildChange(previous, current);
        }

        protected override void OnConfigurationBound() => BindConfigurationChild(url);
        protected override void OnConfigurationUnbound() => UnbindConfigurationChild(url);

        protected internal override RangeActionFlow Execute(in RangeActionContext context)
        {
            var resolved = url.Resolve(in context);
            if (string.IsNullOrWhiteSpace(resolved))
                throw new InvalidOperationException("OpenUrlAction resolved an empty URL.");
            Application.OpenURL(resolved);
            return RangeActionFlow.Continue;
        }
    }

    /// <summary>Copies a resolved string through UniText's platform clipboard provider.</summary>
    [Serializable]
    [TypeDescription("Copy text using primary value, text, a literal or payload member")]
    public sealed partial class CopyRangeTextAction : RangeAction
    {
        [SerializeField, StateProperty(nameof(ApplyValueChange))] private RangeActionString value = new()
        {
            Source = RangeActionStringSource.SegmentText,
        };

        private void ApplyValueChange(RangeActionString previous, ref RangeActionString current)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            ApplyConfigurationChildChange(previous, current);
        }

        protected override void OnConfigurationBound() => BindConfigurationChild(value);
        protected override void OnConfigurationUnbound() => UnbindConfigurationChild(value);

        protected internal override RangeActionFlow Execute(in RangeActionContext context)
        {
            UniTextClipboard.SetText(value.Resolve(in context));
            return RangeActionFlow.Continue;
        }
    }

    /// <summary>Sets one configured GameObject active or inactive.</summary>
    [Serializable]
    [TypeDescription("Set Active: change one configured GameObject")]
    public sealed partial class SetActiveAction : RangeAction
    {
        /// <summary>Object whose active state is changed.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private GameObject target;
        /// <summary>State assigned when the action executes.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private bool active = true;

        protected internal override RangeActionFlow Execute(in RangeActionContext context)
        {
            if (target == null)
                throw new InvalidOperationException("SetActiveAction requires a target GameObject.");
            target.SetActive(active);
            return RangeActionFlow.Continue;
        }
    }
}
