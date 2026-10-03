using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>Immutable on-demand diagnostic view of one logical interactive entity.</summary>
    public sealed class RangeEntityDiagnostic
    {
        private readonly RangeSegment[] segments;
        private readonly Rect[] fragments;

        /// <summary>Stable source-scoped identity.</summary>
        public RangeIdentity Identity { get; }
        /// <summary>Source that emitted this entity.</summary>
        public RangeSource Source { get; }
        /// <summary>Stable interaction channel, or null for modifier-local routing.</summary>
        public RangeChannel Channel { get; }
        /// <summary>Typed immutable payload.</summary>
        public RangePayloadView Payload { get; }
        /// <summary>Every attributed segment currently participating in the entity.</summary>
        public ReadOnlySpan<RangeSegment> Segments => segments;
        /// <summary>Final hit-test fragments in component-local coordinates.</summary>
        public ReadOnlySpan<Rect> HitFragments => fragments;
        /// <summary>Union of current hit-test fragments.</summary>
        public Rect Bounds { get; }
        /// <summary>Owning modifier type.</summary>
        public Type ModifierType { get; }
        /// <summary>Stable identity within the owning modifier graph.</summary>
        public ModifierNodeId ModifierNode { get; }
        /// <summary>Resolved opaque primary source value.</summary>
        public string PrimaryValue { get; }
        /// <summary>Current aggregate pointer state.</summary>
        public RangeState State { get; }
        /// <summary>Whether this entity currently owns keyboard/gamepad focus.</summary>
        public bool Focused { get; }
        /// <summary>Resolved overlap priority.</summary>
        public int Priority { get; }
        /// <summary>Owning Style order.</summary>
        public int StyleOrder { get; }
        /// <summary>Stable modifier registration order used as the final overlap tie-break.</summary>
        public int RegistrationOrder { get; }
        /// <summary>Whether the target observes events without capturing underlying UI.</summary>
        public bool PassThrough { get; }

        internal RangeEntityDiagnostic(in InteractiveRange range, RangeSegment[] segments,
            Rect[] fragments, Rect bounds, InteractiveModifier modifier, RangeState state,
            bool focused, int priority, int styleOrder, int registrationOrder, bool passThrough)
        {
            Identity = range.Identity;
            Source = range.Source;
            Channel = range.Channel;
            Payload = range.Payload;
            this.segments = segments;
            this.fragments = fragments;
            Bounds = bounds;
            ModifierType = modifier.GetType();
            ModifierNode = modifier.NodeId;
            PrimaryValue = range.PrimaryValue;
            State = state;
            Focused = focused;
            Priority = priority;
            StyleOrder = styleOrder;
            RegistrationOrder = registrationOrder;
            PassThrough = passThrough;
        }
    }

    /// <summary>Immutable on-demand diagnostic view of one state-rule playback.</summary>
    public sealed class RangeRuleDiagnostic
    {
        /// <summary>Stable entity addressed by the playback.</summary>
        public RangeIdentity Entity { get; }
        /// <summary>Segment ID for segment scope, or an invalid ID for entity scope.</summary>
        public RangeSegmentId Segment { get; }
        /// <summary>Rule definition type.</summary>
        public Type EffectType { get; }
        /// <summary>Playback type.</summary>
        public Type PlaybackType { get; }
        /// <summary>Property key and ID, modifier template type, or rule type name.</summary>
        public string Target { get; }
        /// <summary>Current normalized contribution weight.</summary>
        public float Weight { get; }
        /// <summary>Whether the selector currently matches.</summary>
        public bool SelectorMatched { get; }
        /// <summary>Whether a built-in transition is currently active.</summary>
        public bool Transitioning { get; }
        /// <summary>Current segment target count.</summary>
        public int TargetCount { get; }
        /// <summary>Built-in Hovered signal.</summary>
        public bool Hovered { get; }
        /// <summary>Built-in Pressed signal.</summary>
        public bool Pressed { get; }
        /// <summary>Built-in Focused signal.</summary>
        public bool Focused { get; }
        /// <summary>Built-in Disabled signal.</summary>
        public bool Disabled { get; }
        /// <summary>Built-in LongPressProgress signal.</summary>
        public float LongPressProgress { get; }
        /// <summary>Built-in LoadingProgress signal.</summary>
        public float LoadingProgress { get; }
        /// <summary>Built-in DragProgress signal.</summary>
        public float DragProgress { get; }
        /// <summary>Built-in VoiceProgress signal.</summary>
        public float VoiceProgress { get; }

        internal RangeRuleDiagnostic(RangeRuleInstance instance, in RangeSignalReader signals,
            bool selectorMatched, bool transitioning)
        {
            Entity = instance.Entity;
            Segment = instance.Segment;
            EffectType = instance.Definition.GetType();
            PlaybackType = instance.Definition.Playback.GetType();
            Target = instance.Definition switch
            {
                ParameterRule property => $"{property.TargetNode}.{property.ParameterId}",
                ModifierRule modifier => modifier.ModifierTemplate?.GetType().Name ?? "missing modifier",
                _ => instance.Definition.GetType().Name,
            };
            Weight = instance.Weight;
            SelectorMatched = selectorMatched;
            Transitioning = transitioning;
            TargetCount = instance.TargetCount;
            Hovered = signals.Get(RangeSignals.Hovered);
            Pressed = signals.Get(RangeSignals.Pressed);
            Focused = signals.Get(RangeSignals.Focused);
            Disabled = signals.Get(RangeSignals.Disabled);
            LongPressProgress = signals.Get(RangeSignals.LongPressProgress);
            LoadingProgress = signals.Get(RangeSignals.LoadingProgress);
            DragProgress = signals.Get(RangeSignals.DragProgress);
            VoiceProgress = signals.Get(RangeSignals.VoiceProgress);
        }
    }

    /// <summary>
    /// Stable allocation-owning snapshot captured only on explicit diagnostic demand. Runtime hot
    /// paths never retain or update it.
    /// </summary>
    public sealed class UniTextRangeDebugSnapshot
    {
        private readonly RangeEntityDiagnostic[] entities;
        private readonly RangeRuleDiagnostic[] rules;

        /// <summary>Text component from which this snapshot was captured.</summary>
        public UniTextBase Text { get; }
        /// <summary>Rendered revision associated with the current range graph.</summary>
        public TextRevision Revision { get; }
        /// <summary>Logical interactive entities in navigation order.</summary>
        public ReadOnlySpan<RangeEntityDiagnostic> Entities => entities;
        /// <summary>Current rule control instances.</summary>
        public ReadOnlySpan<RangeRuleDiagnostic> Rules => rules;

        /// <summary>Captures current range diagnostics for a text component.</summary>
        public static UniTextRangeDebugSnapshot Capture(UniTextBase text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var entities = new List<RangeEntityDiagnostic>();
            var rules = new List<RangeRuleDiagnostic>();
            if (UniTextInteractions.TryGet(text, out var interactions))
                interactions.AppendDiagnostics(entities);
            if (UniTextRanges.TryGet(text, out var rangesRuntime))
                rangesRuntime.AppendDiagnostics(rules);
            return new UniTextRangeDebugSnapshot(text,
                text.AttributeParser?.TextSnapshot.Revision ?? default,
                entities.ToArray(), rules.ToArray());
        }

        internal UniTextRangeDebugSnapshot(UniTextBase text, TextRevision revision,
            RangeEntityDiagnostic[] entities, RangeRuleDiagnostic[] rules)
        {
            Text = text;
            Revision = revision;
            this.entities = entities;
            this.rules = rules;
        }
    }
}
