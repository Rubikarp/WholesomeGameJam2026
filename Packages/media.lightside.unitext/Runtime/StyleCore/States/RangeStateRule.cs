using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightSide
{
    /// <summary>Receives the normalized weight of a transient modifier application.</summary>
    public interface IModifierRuleWeightReceiver
    {
        /// <summary>Sets the weight used by subsequent range applications in the current pass.</summary>
        void SetRuleWeight(float weight);
    }

    /// <summary>Whether one rule playback addresses a hit segment or every segment of an entity.</summary>
    public enum RangeRuleScope : byte
    {
        /// <summary>Each concrete segment owns independent signal matching and playback.</summary>
        Segment,
        /// <summary>One entity playback writes contributions to all current segments.</summary>
        Entity,
    }

    /// <summary>Cancelable event capable of triggering a rule independently from persistent signals.</summary>
    public enum RangeRuleEvent : byte
    {
        /// <summary>No event trigger; only selector enter/exit controls the rule.</summary>
        None,
        /// <summary>Confirmed click, tap or keyboard/gamepad activation.</summary>
        Activated,
        /// <summary>Secondary click, long press or semantic context request.</summary>
        ContextRequested,
    }

    /// <summary>Borrowed lifecycle context supplied to a rule playback.</summary>
    public readonly struct RangeRuleContext
    {
        /// <summary>Current typed signals for the affected entity.</summary>
        public RangeSignalReader Signals { get; }
        /// <summary>Interaction that caused this lifecycle step, or null for programmatic signals.</summary>
        public RangeInteraction Interaction { get; }
        /// <summary>Whether decorative motion should resolve directly to its final state.</summary>
        public bool PrefersReducedMotion { get; }
        /// <summary>Whether this is the first selector evaluation for the playback.</summary>
        public bool IsInitialEvaluation { get; }

        internal RangeRuleContext(in RangeSignalReader signals, RangeInteraction interaction,
            bool prefersReducedMotion, bool isInitialEvaluation)
        {
            Signals = signals;
            Interaction = interaction;
            PrefersReducedMotion = prefersReducedMotion;
            IsInitialEvaluation = isInitialEvaluation;
        }
    }

    /// <summary>Signature for observing a typed rule lifecycle without owning its playback.</summary>
    public delegate void RangeRuleLifecycleHandler(RangeRuleInstance rule,
        in RangeRuleContext context);

    /// <summary>Serializable playback policy controlling rule enter, exit, event and cancellation.</summary>
    [Serializable]
    public abstract class RangeStatePlayback : RangeConfigurationObject
    {
        /// <summary>Called when the selector changes from false to true.</summary>
        protected internal abstract void Enter(RangeRuleInstance rule,
            in RangeRuleContext context);
        /// <summary>Called when the selector changes from true to false.</summary>
        protected internal abstract void Exit(RangeRuleInstance rule,
            in RangeRuleContext context);
        /// <summary>Called for the configured one-shot event.</summary>
        protected internal virtual void Trigger(RangeRuleInstance rule,
            in RangeRuleContext context) => Enter(rule, in context);
        /// <summary>
        /// Called when signal values change while the selector remains matched. Scalar/progress
        /// drivers can update the same typed handles without restarting the playback.
        /// </summary>
        protected internal virtual void Update(RangeRuleInstance rule,
            in RangeRuleContext context) { }
        /// <summary>Called when the entity, definition or host disappears.</summary>
        protected internal virtual void Cancel(RangeRuleInstance rule) => rule.Release();
    }

    /// <summary>Applies the target contribution immediately and removes it immediately on exit.</summary>
    [Serializable]
    [TypeDescription("Instant: switch to the target value without interpolation")]
    public sealed class InstantPlayback : RangeStatePlayback
    {
        /// <inheritdoc/>
        protected internal override void Enter(RangeRuleInstance rule,
            in RangeRuleContext context) => rule.SetWeight(1f);

        /// <inheritdoc/>
        protected internal override void Exit(RangeRuleInstance rule,
            in RangeRuleContext context)
        {
            rule.SetWeight(0f);
            rule.ReleaseContributions();
        }

        /// <inheritdoc/>
        protected internal override void Trigger(RangeRuleInstance rule,
            in RangeRuleContext context) => rule.HoldOneFrame();
    }

    /// <summary>Interpolates parameter contributions with an explicit clock and easing policy.</summary>
    [Serializable]
    [TypeDescription("Parameter transition: duration, easing, scaled/unscaled/manual clock")]
    public sealed partial class TransitionPlayback : RangeStatePlayback
    {
        /// <summary>Duration used when a selector enters.</summary>
        [SerializeField, Min(0f), NumberStateProperty(nameof(NotifyConfigurationChanged), Min = 0)] private float enterDuration = 0.12f;
        /// <summary>Duration used when a selector exits.</summary>
        [SerializeField, Min(0f), NumberStateProperty(nameof(NotifyConfigurationChanged), Min = 0)] private float exitDuration = 0.10f;
        /// <summary>Interpolation curve.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private Ease easing = Ease.Of(EasingType.CubicOut);
        /// <summary>Time source.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private PlaybackClock clock = PlaybackClock.Unscaled;
        /// <summary>Whether a selector already matched on its first evaluation should animate in.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private bool animateInitialMatch;

        /// <inheritdoc/>
        protected internal override void Enter(RangeRuleInstance rule,
            in RangeRuleContext context)
            => rule.TransitionTo(1f,
                context.PrefersReducedMotion || context.IsInitialEvaluation && !animateInitialMatch
                    ? 0f
                    : enterDuration,
                easing, clock, false);

        /// <inheritdoc/>
        protected internal override void Exit(RangeRuleInstance rule,
            in RangeRuleContext context)
            => rule.TransitionTo(0f, context.PrefersReducedMotion ? 0f : exitDuration,
                easing, clock, true);

        /// <inheritdoc/>
        protected internal override void Trigger(RangeRuleInstance rule,
            in RangeRuleContext context)
            => rule.TriggerPulse(context.PrefersReducedMotion ? 0f : enterDuration,
                context.PrefersReducedMotion ? 0f : exitDuration, easing, clock);
    }

    /// <summary>Maps a built-in scalar signal directly to the rule's normalized weight.</summary>
    [Serializable]
    [TypeDescription("Signal progress: map long-press/loading/drag/voice progress to rule weight")]
    public sealed partial class SignalProgressPlayback : RangeStatePlayback
    {
        /// <summary>Scalar signal to sample whenever its value changes.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private BuiltInScalarSignal signal;
        /// <summary>Signal value mapped to <see cref="OutputMin"/>.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private float inputMin;
        /// <summary>Signal value mapped to <see cref="OutputMax"/>.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private float inputMax = 1f;
        /// <summary>Rule weight at or below <see cref="InputMin"/>.</summary>
        [SerializeField, Range(0f, 1f), NumberStateProperty(nameof(NotifyConfigurationChanged), Clamp01 = true)] private float outputMin;
        /// <summary>Rule weight at or above <see cref="InputMax"/>.</summary>
        [SerializeField, Range(0f, 1f), NumberStateProperty(nameof(NotifyConfigurationChanged), Clamp01 = true)] private float outputMax = 1f;
        /// <summary>Whether Enter immediately samples the signal instead of using <see cref="EnterWeight"/>.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private bool sampleOnEnter = true;
        /// <summary>Initial weight used when <see cref="SampleOnEnter"/> is false.</summary>
        [SerializeField, Range(0f, 1f), NumberStateProperty(nameof(NotifyConfigurationChanged), Clamp01 = true)] private float enterWeight = 1f;
        /// <summary>Optional fade duration from the sampled weight to zero when the selector exits.</summary>
        [SerializeField, Min(0f), NumberStateProperty(nameof(NotifyConfigurationChanged), Min = 0)] private float exitDuration;
        /// <summary>Interpolation curve used by the optional exit fade.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private Ease exitEasing = Ease.Of(EasingType.CubicOut);
        /// <summary>Time source used by the optional exit fade.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private PlaybackClock exitClock = PlaybackClock.Unscaled;

        /// <inheritdoc/>
        protected internal override void Enter(RangeRuleInstance rule,
            in RangeRuleContext context)
        {
            if (sampleOnEnter) Apply(rule, in context);
            else rule.SetWeight(enterWeight);
        }

        /// <inheritdoc/>
        protected internal override void Update(RangeRuleInstance rule,
            in RangeRuleContext context) => Apply(rule, in context);

        /// <inheritdoc/>
        protected internal override void Exit(RangeRuleInstance rule,
            in RangeRuleContext context)
            => rule.TransitionTo(0f, context.PrefersReducedMotion ? 0f : exitDuration,
                exitEasing, exitClock, true);

        private void Apply(RangeRuleInstance rule, in RangeRuleContext context)
        {
            var value = signal switch
            {
                BuiltInScalarSignal.LongPressProgress => context.Signals.Get(RangeSignals.LongPressProgress),
                BuiltInScalarSignal.LoadingProgress => context.Signals.Get(RangeSignals.LoadingProgress),
                BuiltInScalarSignal.DragProgress => context.Signals.Get(RangeSignals.DragProgress),
                BuiltInScalarSignal.VoiceProgress => context.Signals.Get(RangeSignals.VoiceProgress),
                _ => throw new ArgumentOutOfRangeException(),
            };
            var denominator = inputMax - inputMin;
            var normalized = Mathf.Clamp01((value - inputMin) / denominator);
            rule.SetWeight(Mathf.LerpUnclamped(outputMin, outputMax, normalized));
        }
    }

    /// <summary>
    /// Leaves parameter handles under project control. External tween/Animator/Playable code uses
    /// <see cref="RangeRuleInstance.GetParameter{TModifier,TValue}"/> and releases the instance.
    /// </summary>
    [Serializable]
    [TypeDescription("Manual: external code owns parameter values and lifetime")]
    public sealed class ManualPlayback : RangeStatePlayback
    {
        /// <inheritdoc/>
        protected internal override void Enter(RangeRuleInstance rule,
            in RangeRuleContext context) { }

        /// <inheritdoc/>
        protected internal override void Exit(RangeRuleInstance rule,
            in RangeRuleContext context) { }
    }

    /// <summary>Serializable boxed value used only while a ParameterRule binding is resolved.</summary>
    [Serializable]
    public abstract partial class RuleValue : RangeConfigurationObject
    {
        /// <summary>Concrete value type.</summary>
        public abstract Type ValueType { get; }
        /// <summary>Whether this target is authored directly or read from every entity payload.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private RangeValueSource source;
        /// <summary>Named payload path used when <see cref="Source"/> is <see cref="RangeValueSource.PayloadMember"/>.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private string payloadMember;
        /// <summary>Boxes the serialized target once while the stable parameter binding is created.</summary>
        protected internal abstract object BoxedValue { get; }

        internal bool UsesPayload => source == RangeValueSource.PayloadMember;

        internal object Resolve(RangePayloadView payload)
            => source == RangeValueSource.PayloadMember
                ? RangePayloadBinding.Read(payload, payloadMember, ValueType)
                : BoxedValue;
    }

    /// <summary>Typed <see cref="RuleValue"/> storing one unmanaged <typeparamref name="TValue"/>.</summary>
    [Serializable]
    public abstract partial class RuleValue<TValue> : RuleValue
        where TValue : unmanaged
    {
        /// <summary>Stored value.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private TValue value;

        /// <inheritdoc/>
        public sealed override Type ValueType => typeof(TValue);

        protected internal sealed override object BoxedValue => value;

        /// <summary>Creates the value at the type's default.</summary>
        protected RuleValue() { }

        /// <summary>Creates the value at <paramref name="value"/>.</summary>
        protected RuleValue(TValue value) => this.value = value;
    }

    /// <summary>Float ParameterRule target.</summary>
    [Serializable, TypeDescription("Float value")]
    public sealed class FloatRuleValue : RuleValue<float> { }

    /// <summary>UnitValue ParameterRule target.</summary>
    [Serializable, TypeDescription("Unit value (px/em)")]
    public sealed class UnitRuleValue : RuleValue<UnitValue> { }

    /// <summary>Vector2 ParameterRule target.</summary>
    [Serializable, TypeDescription("Vector2 value")]
    public sealed class Vector2RuleValue : RuleValue<Vector2> { }

    /// <summary>UnitVector2 ParameterRule target.</summary>
    [Serializable, TypeDescription("Unit Vector2 value (px/em)")]
    public sealed class UnitVector2RuleValue : RuleValue<UnitVector2> { }

    /// <summary>Color ParameterRule target.</summary>
    [Serializable, TypeDescription("Color value")]
    public sealed class ColorRuleValue : RuleValue<Color32>
    {
        /// <summary>Creates the value at opaque white.</summary>
        public ColorRuleValue() : base(new Color32(255, 255, 255, 255)) { }
    }

    /// <summary>Int ParameterRule target.</summary>
    [Serializable, TypeDescription("Int value")]
    public sealed class IntRuleValue : RuleValue<int> { }

    /// <summary>Bool ParameterRule target.</summary>
    [Serializable, TypeDescription("Bool value")]
    public sealed class BoolRuleValue : RuleValue<bool> { }

    /// <summary>String ParameterRule target.</summary>
    [Serializable, TypeDescription("String value")]
    public sealed partial class StringRuleValue : RuleValue
    {
        /// <summary>Stored value.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private string value;
        /// <inheritdoc/>
        public override Type ValueType => typeof(string);
        protected internal override object BoxedValue => value;
    }

    /// <summary>Enum ParameterRule target, closed over the driven enum type.</summary>
    [Serializable, TypeDescription("Enum value")]
    public sealed class EnumRuleValue<TEnum> : RuleValue<TEnum>
        where TEnum : unmanaged, Enum { }

    /// <summary>Base definition shared by transient modifier and parameter rules.</summary>
    [Serializable]
    public abstract partial class RangeStateRule : RangeConfigurationObject
    {
        /// <summary>Persistent signal predicate; null is invalid for selector-driven playback.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplySelectorChange))] private RangeStateSelector selector =
            new InteractionRangeStateSelector { Required = InteractionSignalMask.Hovered };
        /// <summary>Playback policy.</summary>
        [SerializeReference, TypeSelector, FormerlySerializedAs("driver"), StateProperty(nameof(ApplyPlaybackChange))] private RangeStatePlayback playback = new InstantPlayback();
        /// <summary>Whether one playback covers a segment or its complete entity.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private RangeRuleScope scope = RangeRuleScope.Entity;
        /// <summary>Optional one-shot semantic event.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private RangeRuleEvent trigger;
        /// <summary>Contribution priority; higher Replace values win.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private int priority;

        private void ApplySelectorChange(RangeStateSelector previous, RangeStateSelector current)
            => ApplyConfigurationChildChange(previous, current);

        private void ApplyPlaybackChange(RangeStatePlayback previous, RangeStatePlayback current)
            => ApplyConfigurationChildChange(previous, current);

        protected override void OnConfigurationBound()
        {
            BindConfigurationChild(selector);
            BindConfigurationChild(playback);
        }

        protected override void OnConfigurationUnbound()
        {
            UnbindConfigurationChild(selector);
            UnbindConfigurationChild(playback);
        }
    }

    /// <summary>Animates one typed parameter of an existing modifier in the owner's graph.</summary>
    [Serializable]
    [TypeDescription("Parameter rule: animate a parameter of another modifier in this graph")]
    public sealed partial class ParameterRule : RangeStateRule
    {
        [SerializeField, StateField(nameof(NotifyConfigurationChanged))] private ModifierNodeId targetNode;
        [SerializeField, FormerlySerializedAs("propertyId"), StateField(nameof(NotifyConfigurationChanged))] private string parameterId;
        /// <summary>Typed target or contribution value.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyTargetValueChange))] private RuleValue targetValue;

        /// <summary>Stable identity of the target node within the rule owner's modifier graph.</summary>
        public ModifierNodeId TargetNode => targetNode;
        /// <summary>Stable <see cref="ParameterDescriptor.Id"/> declared by that node.</summary>
        public string ParameterId => parameterId;

        internal void RemapTargetNode(IReadOnlyDictionary<ModifierNodeId, ModifierNodeId> identities)
        {
            if (identities.TryGetValue(targetNode, out var replacement))
                SetTargetNodeState(replacement);
        }
        /// <summary>How this rule combines with baseline and concurrent rules.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private ParameterComposition composition;

        private void ApplyTargetValueChange(RuleValue previous, RuleValue current)
            => ApplyConfigurationChildChange(previous, current);

        protected override void OnConfigurationBound()
        {
            base.OnConfigurationBound();
            BindConfigurationChild(targetValue);
        }

        protected override void OnConfigurationUnbound()
        {
            UnbindConfigurationChild(targetValue);
            base.OnConfigurationUnbound();
        }

        /// <summary>
        /// Replaces the target descriptor and its typed value as one invariant-preserving edit.
        /// Use this when changing the selected modifier parameter; the three values describe one
        /// binding and must never be updated independently.
        /// </summary>
        public void SetTarget(ModifierNodeId node, string id, RuleValue value)
        {
            BeginConfigurationUpdate();
            try
            {
                SetTargetNodeState(node);
                SetParameterIdState(id);
                TargetValue = value;
            }
            finally
            {
                EndConfigurationUpdate();
            }
        }

        /// <summary>Targets a typed parameter declared by a modifier in the same graph.</summary>
        public void SetTarget<TModifier, TValue>(TModifier modifier,
            ParameterDescriptor<TModifier, TValue> parameter) where TModifier : BaseModifier
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            if (!ReferenceEquals(ParameterDescriptor.Find(modifier, parameter.Id), parameter))
                throw new InvalidOperationException(
                    $"{modifier.GetType().Name} does not declare parameter '{parameter.Id}'.");
            BeginConfigurationUpdate();
            try
            {
                SetTargetNodeState(modifier.NodeId);
                SetParameterIdState(parameter.Id);
            }
            finally
            {
                EndConfigurationUpdate();
            }
        }
    }

    /// <summary>Adds a modifier template only while its selector or event playback is active.</summary>
    [Serializable]
    [TypeDescription("Modifier rule: apply a transient modifier or Composite to matching ranges")]
    public sealed partial class ModifierRule : RangeStateRule
    {
        /// <summary>Transient modifier graph applied to active entity segments.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(NotifyConfigurationChanged))] private BaseModifier modifierTemplate;

        /// <summary>Minimum invalidation needed by the transient graph.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private UniTextDirty dirtyStage = UniTextDirty.Mesh;
    }
}
