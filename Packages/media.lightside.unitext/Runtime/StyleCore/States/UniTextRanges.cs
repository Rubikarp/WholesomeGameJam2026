using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    internal interface IRangeRuleOutput
    {
        void ReconcileTargets(IReadOnlyList<InteractiveRange> targets);
        void SetWeight(float value);
        void Release();
    }

    internal interface IOwnedParameterAccess<TValue>
    {
        bool IsAlive { get; }
        ParameterComposition Composition { get; }
        TValue GetValue();
        TValue GetBaseline();
        void SetValue(TValue value);
        void Release();
    }

    internal readonly struct ParameterSlotKey : IEquatable<ParameterSlotKey>
    {
        public readonly BaseModifier modifier;
        public readonly ParameterDescriptor parameter;
        public readonly RangeIdentity entity;
        public readonly RangeSegmentId segment;

        public ParameterSlotKey(BaseModifier modifier, ParameterDescriptor parameter,
            RangeIdentity entity, RangeSegmentId segment)
        {
            this.modifier = modifier;
            this.parameter = parameter;
            this.entity = entity;
            this.segment = segment;
        }

        public bool Equals(ParameterSlotKey other)
            => ReferenceEquals(modifier, other.modifier) && ReferenceEquals(parameter, other.parameter) &&
               entity == other.entity && segment == other.segment;

        public override bool Equals(object obj)
            => obj is ParameterSlotKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(RuntimeHelpers.GetHashCode(modifier),
                RuntimeHelpers.GetHashCode(parameter), entity, segment);
    }

    internal sealed class ParameterSlot<TModifier, TValue>
        where TModifier : BaseModifier
    {
        private readonly UniTextRanges runtime;
        private readonly TModifier modifier;
        private readonly ParameterDescriptor<TModifier, TValue> parameter;
        private readonly ParameterSlotKey key;
        private readonly List<ParameterContribution<TModifier, TValue>> contributions = new(2);
        private TValue baseline;
        private TValue resolved;

        public TValue Baseline => baseline;

        public ParameterSlot(UniTextRanges runtime, TModifier modifier,
            ParameterDescriptor<TModifier, TValue> parameter, in ParameterSlotKey key)
        {
            this.runtime = runtime;
            this.modifier = modifier;
            this.parameter = parameter;
            this.key = key;
            baseline = parameter.ReadRoot(modifier);
            resolved = baseline;
        }

        public void Add(ParameterContribution<TModifier, TValue> contribution)
        {
            contributions.Add(contribution);
            contributions.Sort(CompareContributions);
            for (var i = 0; i < contributions.Count; i++)
                contributions[i].RefreshFromBaseline();
            Recalculate(true);
        }

        public void Remove(ParameterContribution<TModifier, TValue> contribution)
        {
            if (!contributions.Remove(contribution)) return;
            for (var i = 0; i < contributions.Count; i++)
                contributions[i].RefreshFromBaseline();
            Recalculate(true);
            if (contributions.Count == 0) runtime.ReleaseSlot(in key, this);
        }

        public TValue Resolve(TValue value)
        {
            baseline = value;
            for (var i = 0; i < contributions.Count; i++)
                contributions[i].RefreshFromBaseline();
            Recalculate(false);
            return resolved;
        }

        public void ContributionChanged()
        {
            for (var i = 0; i < contributions.Count; i++)
                contributions[i].RefreshFromBaseline();
            Recalculate(true);
        }

        public TValue ReplacementBaselineBelow(ParameterContribution<TModifier, TValue> target)
        {
            var result = baseline;
            for (var i = 0; i < contributions.Count; i++)
            {
                var contribution = contributions[i];
                if (ReferenceEquals(contribution, target)) break;
                if (contribution.IsActive &&
                    contribution.Composition == ParameterComposition.Replace)
                    result = contribution.Value;
            }
            return result;
        }

        private void Recalculate(bool invalidate)
        {
            var next = baseline;
            ParameterContribution<TModifier, TValue> replacement = null;
            for (var i = 0; i < contributions.Count; i++)
            {
                var contribution = contributions[i];
                if (contribution.IsActive &&
                    contribution.Composition == ParameterComposition.Replace)
                    replacement = contribution;
            }

            if (replacement != null) next = replacement.Value;
            for (var i = 0; i < contributions.Count; i++)
            {
                var contribution = contributions[i];
                if (!contribution.IsActive ||
                    contribution.Composition != ParameterComposition.Add) continue;
                next = parameter.Compose(next, contribution.Value, ParameterComposition.Add);
            }
            for (var i = 0; i < contributions.Count; i++)
            {
                var contribution = contributions[i];
                if (!contribution.IsActive ||
                    contribution.Composition != ParameterComposition.Multiply) continue;
                next = parameter.Compose(next, contribution.Value, ParameterComposition.Multiply);
            }
            for (var i = 0; i < contributions.Count; i++)
            {
                var contribution = contributions[i];
                if (!contribution.IsActive ||
                    contribution.Composition != ParameterComposition.Custom) continue;
                next = parameter.Compose(next, contribution.Value, ParameterComposition.Custom);
            }

            if (ValueEquality<TValue>.Same(resolved, next)) return;
            resolved = next;
            if (invalidate) runtime.Invalidate(modifier, parameter);
        }

        private static int CompareContributions(ParameterContribution<TModifier, TValue> a,
            ParameterContribution<TModifier, TValue> b)
        {
            var priority = a.Priority.CompareTo(b.Priority);
            return priority != 0 ? priority : a.DeclarationOrder.CompareTo(b.DeclarationOrder);
        }
    }

    internal sealed class ParameterContribution<TModifier, TValue>
        where TModifier : BaseModifier
    {
        private readonly ParameterDescriptor<TModifier, TValue> parameter;
        private readonly TValue target;
        private ParameterSlot<TModifier, TValue> slot;
        private TValue value;
        private bool active;
        private bool manualValue;
        private float weight;

        public ParameterComposition Composition { get; }
        public int Priority { get; }
        public int DeclarationOrder { get; }
        public TValue Value => value;
        public TValue Baseline => slot.Baseline;
        public bool IsActive => active;
        internal ParameterSlot<TModifier, TValue> Slot => slot;

        public ParameterContribution(ParameterSlot<TModifier, TValue> slot,
            ParameterDescriptor<TModifier, TValue> parameter, TValue target,
            ParameterComposition composition, int priority, int declarationOrder)
        {
            this.slot = slot;
            this.parameter = parameter;
            this.target = target;
            Composition = composition;
            Priority = priority;
            DeclarationOrder = declarationOrder;
            value = composition == ParameterComposition.Replace
                ? slot.Baseline
                : parameter.Identity(composition);
            slot.Add(this);
        }

        public void SetWeight(float weight)
        {
            this.weight = Mathf.Clamp01(weight);
            manualValue = false;
            var nextActive = this.weight > 0f;
            var from = Composition == ParameterComposition.Replace
                ? slot.ReplacementBaselineBelow(this)
                : parameter.Identity(Composition);
            var next = parameter.Lerp(from, target, this.weight);
            if (active == nextActive && ValueEquality<TValue>.Same(value, next)) return;
            active = nextActive;
            value = next;
            slot.ContributionChanged();
        }

        public void SetValue(TValue next)
        {
            if (active && ValueEquality<TValue>.Same(value, next)) return;
            active = true;
            manualValue = true;
            value = next;
            slot.ContributionChanged();
        }

        /// <summary>Deactivates the contribution, keeping its value and slot membership; the next <see cref="SetValue"/> re-engages it.</summary>
        public bool Withhold()
        {
            if (!active || slot == null) return false;
            active = false;
            slot.ContributionChanged();
            return true;
        }

        public void RefreshFromBaseline()
        {
            if (manualValue || slot == null) return;
            var from = Composition == ParameterComposition.Replace
                ? slot.ReplacementBaselineBelow(this)
                : parameter.Identity(Composition);
            value = parameter.Lerp(from, target, weight);
        }

        public void Release()
        {
            if (slot == null) return;
            var owner = slot;
            slot = null;
            active = false;
            owner.Remove(this);
        }
    }

    internal sealed class ParameterRuleOutput<TModifier, TValue> : IRangeRuleOutput,
        IOwnedParameterAccess<TValue>
        where TModifier : BaseModifier
    {
        private readonly UniTextRanges runtime;
        private readonly RangeRuleInstance instance;
        private readonly TModifier modifier;
        private readonly ParameterDescriptor<TModifier, TValue> parameter;
        private readonly ParameterRule definition;
        private readonly TValue targetValue;
        private readonly List<ParameterContribution<TModifier, TValue>> contributions = new(2);
        private OwnedParameter<TValue> handle;
        private float weight;
        private bool alive = true;

        public bool IsAlive => alive;
        public ParameterComposition Composition => definition.Composition;
        public bool Matches(TModifier candidate, ParameterDescriptor<TModifier, TValue> descriptor)
            => ReferenceEquals(modifier, candidate) && ReferenceEquals(parameter, descriptor);

        public ParameterRuleOutput(UniTextRanges runtime, RangeRuleInstance instance,
            TModifier modifier, ParameterDescriptor<TModifier, TValue> parameter,
            ParameterRule definition, TValue targetValue)
        {
            this.runtime = runtime;
            this.instance = instance;
            this.modifier = modifier;
            this.parameter = parameter;
            this.definition = definition;
            this.targetValue = targetValue;
        }

        public OwnedParameter<TValue> GetHandle()
            => handle is { IsAlive: true } ? handle : handle = new OwnedParameter<TValue>(this);

        public TValue GetValue()
            => contributions.Count == 0 ? targetValue : contributions[0].Value;

        public TValue GetBaseline()
            => contributions.Count == 0 ? parameter.ReadRoot(modifier) : contributions[0].Baseline;

        public void SetValue(TValue value)
        {
            RequireAlive();
            for (var i = 0; i < contributions.Count; i++) contributions[i].SetValue(value);
        }

        public void ReconcileTargets(IReadOnlyList<InteractiveRange> targets)
        {
            RequireAlive();
            for (var i = contributions.Count - 1; i >= 0; i--)
            {
                var contribution = contributions[i];
                var found = false;
                for (var j = 0; j < targets.Count; j++)
                {
                    var target = targets[j];
                    if (!runtime.SameSlot(contribution, modifier, parameter, in target)) continue;
                    found = true;
                    break;
                }
                if (found) continue;
                contribution.Release();
                contributions.RemoveAt(i);
            }

            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var exists = false;
                for (var j = 0; j < contributions.Count; j++)
                {
                    if (!runtime.SameSlot(contributions[j], modifier, parameter, in target)) continue;
                    exists = true;
                    break;
                }
                if (exists) continue;
                var slot = runtime.GetOrCreateSlot(modifier, parameter, in target);
                var contribution = new ParameterContribution<TModifier, TValue>(slot, parameter,
                    targetValue, definition.Composition, definition.Priority,
                    instance.DeclarationOrder);
                contributions.Add(contribution);
                contribution.SetWeight(weight);
            }
        }

        public void SetWeight(float value)
        {
            RequireAlive();
            weight = Mathf.Clamp01(value);
            for (var i = 0; i < contributions.Count; i++) contributions[i].SetWeight(weight);
        }

        public void Release()
        {
            if (!alive) return;
            alive = false;
            for (var i = contributions.Count - 1; i >= 0; i--) contributions[i].Release();
            contributions.Clear();
        }

        private void RequireAlive()
        {
            if (!alive) throw new ObjectDisposedException(nameof(OwnedParameter<TValue>));
        }
    }

    internal interface IOwnedContribution
    {
        BaseModifier Modifier { get; }
        RangeIdentity Entity { get; }
        RangeSegmentId Segment { get; }
        bool IsAlive { get; }
        void Kill();
    }

    internal sealed class OwnedContribution<TModifier, TValue> : IOwnedParameterAccess<TValue>,
        IOwnedContribution
        where TModifier : BaseModifier
    {
        private readonly UniTextRanges runtime;
        private readonly TModifier modifier;
        private readonly ParameterDescriptor<TModifier, TValue> parameter;
        private readonly ParameterComposition composition;
        private ParameterContribution<TModifier, TValue> contribution;
        private OwnedParameter<TValue> handle;

        public BaseModifier Modifier => modifier;
        public RangeIdentity Entity { get; }
        public RangeSegmentId Segment { get; }
        public bool IsAlive => contribution != null;
        public ParameterComposition Composition => composition;

        public OwnedContribution(UniTextRanges runtime, TModifier modifier,
            ParameterDescriptor<TModifier, TValue> parameter, RangeIdentity entity,
            RangeSegmentId segment, ParameterComposition composition, int priority)
        {
            this.runtime = runtime;
            this.modifier = modifier;
            this.parameter = parameter;
            this.composition = composition;
            Entity = entity;
            Segment = segment;
            var slot = runtime.GetOrCreateSlot(modifier, parameter, entity, segment);
            contribution = new ParameterContribution<TModifier, TValue>(slot, parameter,
                parameter.Identity(composition), composition, priority,
                runtime.NextDeclarationOrder());
        }

        public OwnedParameter<TValue> GetHandle()
            => handle ??= new OwnedParameter<TValue>(this);

        public TValue GetValue() => contribution.Value;

        public TValue GetBaseline() => contribution.Baseline;

        public void SetValue(TValue value) => contribution.SetValue(value);

        public void Release()
        {
            if (contribution == null) return;
            Kill();
            runtime.RemoveOwned(this);
        }

        public void Kill()
        {
            var current = contribution;
            if (current == null) return;
            contribution = null;
            current.Release();
        }
    }

    internal sealed class ModifierRuleOutput : IRangeRuleOutput
    {
        private readonly ModifierRuleBinding binding;
        private readonly List<InteractiveRange> targets = new(2);
        private bool alive = true;
        private float weight;

        public bool IsAlive => alive;
        public float Weight => weight;
        public IReadOnlyList<InteractiveRange> Targets => targets;

        public ModifierRuleOutput(ModifierRuleBinding binding)
        {
            this.binding = binding;
            binding.Add(this);
        }

        public void ReconcileTargets(IReadOnlyList<InteractiveRange> value)
        {
            targets.Clear();
            for (var i = 0; i < value.Count; i++) targets.Add(value[i]);
            binding.MarkDirty();
        }

        public void SetWeight(float value)
        {
            var next = Mathf.Clamp01(value);
            if (Mathf.Approximately(weight, next)) return;
            weight = next;
            binding.MarkDirty();
        }

        public void Release()
        {
            if (!alive) return;
            alive = false;
            binding.Remove(this);
        }
    }

    internal sealed class ModifierRuleBinding : IDisposable
    {
        private readonly UniTextRanges runtime;
        private readonly ModifierRule definition;
        private readonly BaseModifier template;
        private readonly List<ModifierRuleOutput> outputs = new(2);
        private readonly List<IModifierRuleWeightReceiver> weightReceivers = new(2);
        private readonly UniTextCommitChanges commitChanges;
        private readonly bool hasExactCommitChanges;
        private int applyCycle;
        private bool disposed;

        public ModifierRule Definition => definition;

        public ModifierRuleBinding(UniTextRanges runtime, ModifierRule definition)
        {
            this.runtime = runtime;
            this.definition = definition;
            template = definition.ModifierTemplate ?? throw new InvalidOperationException(
                "ModifierRule requires a ModifierTemplate.");
            if (template.IsInitialized)
                throw new InvalidOperationException(
                    "A ModifierRule template cannot be shared with an active Style or another rule binding.");
            template.SetOwner(runtime.Owner);
            template.Prepare();
            CollectWeightReceivers(template);
            hasExactCommitChanges = TryGetCommitChanges(template, out commitChanges);
        }

        public void Add(ModifierRuleOutput output)
        {
            outputs.Add(output);
            MarkDirty();
        }

        public void Remove(ModifierRuleOutput output)
        {
            outputs.Remove(output);
            MarkDirty();
        }

        public void MarkDirty() => runtime.QueueModifierBinding(this);

        public void Rebuild()
        {
            if (disposed) return;
            applyCycle++;
            template.BeginApply(applyCycle);
            for (var i = 0; i < outputs.Count; i++)
            {
                var output = outputs[i];
                if (!output.IsAlive || output.Weight <= 0f) continue;
                for (var receiver = 0; receiver < weightReceivers.Count; receiver++)
                    weightReceivers[receiver].SetRuleWeight(output.Weight);
                var targets = output.Targets;
                for (var j = 0; j < targets.Count; j++)
                {
                    var target = targets[j];
                    var context = RangeApplyContext.ForRule(in target);
                    template.Apply(in context, applyCycle);
                }
            }
            if (hasExactCommitChanges)
                runtime.Owner.SetDirty(definition.DirtyStage, commitChanges);
            else
                runtime.Owner.SetDirty(definition.DirtyStage);
        }

        private void CollectWeightReceivers(BaseModifier modifier)
        {
            if (modifier is IModifierRuleWeightReceiver receiver)
                weightReceivers.Add(receiver);
            if (modifier.Children is not { } children) return;
            for (var i = 0; i < children.Count; i++)
                if (children[i] != null) CollectWeightReceivers(children[i]);
        }

        private static bool TryGetCommitChanges(BaseModifier modifier,
            out UniTextCommitChanges changes)
        {
            if (modifier is IModifierCommitChanges source)
            {
                changes = source.CommitChanges;
                return changes != UniTextCommitChanges.None;
            }
            if (modifier.Children is not { Count: > 0 } children)
            {
                changes = default;
                return false;
            }
            changes = UniTextCommitChanges.None;
            for (var i = 0; i < children.Count; i++)
            {
                if (children[i] == null ||
                    !TryGetCommitChanges(children[i], out var childChanges))
                {
                    changes = default;
                    return false;
                }
                changes |= childChanges;
            }
            return changes != UniTextCommitChanges.None;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            outputs.Clear();
            template.Destroy();
            template.SetOwner(null);
        }
    }

    /// <summary>
    /// One stable rule playback for an entity or segment. Built-in and external drivers share
    /// this lifetime, parameter-handle and interruption surface.
    /// </summary>
    public sealed class RangeRuleInstance : IPlaybackHost
    {
        private readonly UniTextRanges runtime;
        private readonly List<IRangeRuleOutput> outputs = new(1);
        private readonly List<InteractiveRange> targets = new(2);
        private bool targetAlive = true;
        private bool selectorMatched;
        private bool selectorEvaluated;
        private Playback playback;
        private object boundPayload;

        internal int DeclarationOrder { get; }
        internal InteractiveModifier Owner { get; }
        internal bool Seen { get; set; }
        internal bool UsesFrameClock => playback.UsesFrameClock;
        internal bool UsesManualClock => playback.UsesManualClock;
        internal PlaybackClock TransitionClock => playback.Clock;
        internal bool SelectorMatched => selectorMatched;
        internal bool Transitioning => playback.Transitioning;

        /// <summary>Definition whose selector or event owns this playback.</summary>
        public RangeStateRule Definition { get; }
        /// <summary>Stable entity addressed by the playback.</summary>
        public RangeIdentity Entity { get; }
        /// <summary>Concrete segment for segment scope, or an invalid ID for entity scope.</summary>
        public RangeSegmentId Segment { get; }
        /// <summary>Number of current segments addressed by this playback.</summary>
        public int TargetCount => targets.Count;
        /// <summary>Representative segment including source, payload, channel and revision data.</summary>
        public InteractiveRange RepresentativeRange => targets.Count == 0 ? default : targets[0];
        /// <summary>Whether the source entity still exists in the latest range revision.</summary>
        public bool IsAlive => targetAlive;
        /// <summary>Current normalized contribution weight.</summary>
        public float Weight
        {
            get => playback.Weight;
            set => SetWeight(value);
        }

        /// <summary>Returns one current target segment without exposing the mutable backing list.</summary>
        public InteractiveRange GetTarget(int index)
        {
            if ((uint)index >= (uint)targets.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return targets[index];
        }

        internal RangeRuleInstance(UniTextRanges runtime, InteractiveModifier owner,
            RangeStateRule definition, RangeIdentity entity, RangeSegmentId segment,
            int declarationOrder)
        {
            this.runtime = runtime;
            Owner = owner;
            Definition = definition;
            Entity = entity;
            Segment = segment;
            DeclarationOrder = declarationOrder;
        }

        /// <summary>Returns the typed handle bound by this ParameterRule.</summary>
        public OwnedParameter<TValue> GetParameter<TModifier, TValue>(
            ModifierKey<TModifier> modifierKey,
            ParameterDescriptor<TModifier, TValue> parameter)
            where TModifier : BaseModifier
        {
            if (!targetAlive) throw new ObjectDisposedException(nameof(RangeRuleInstance));
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            var modifier = runtime.ResolveModifier(modifierKey, Owner);
            EnsureOutputs();
            for (var i = 0; i < outputs.Count; i++)
                if (outputs[i] is ParameterRuleOutput<TModifier, TValue> output &&
                    output.Matches(modifier, parameter)) return output.GetHandle();
            throw new InvalidOperationException(
                $"The playback has no ParameterRule binding for '{modifierKey.NodeId}.{parameter.Id}'.");
        }

        /// <summary>Sets the normalized built-in weight without choosing a tween implementation.</summary>
        public void SetWeight(float value)
        {
            if (!targetAlive) throw new ObjectDisposedException(nameof(RangeRuleInstance));
            playback.SetWeight(this, value);
        }

        void IPlaybackHost.ApplyWeight(float value)
        {
            if (value > 0f) EnsureOutputs();
            for (var i = 0; i < outputs.Count; i++) outputs[i].SetWeight(value);
        }

        void IPlaybackHost.ReleaseOutputs() => ReleaseContributions();

        /// <summary>Releases current contributions while preserving the entity control instance.</summary>
        public void Release()
        {
            playback.Reset();
            ReleaseContributions();
            runtime.RefreshTickSubscription();
        }

        internal void ClearTargets()
        {
            Seen = false;
            targets.Clear();
        }

        internal void AddTarget(in InteractiveRange range)
        {
            Seen = true;
            targets.Add(range);
        }

        internal void CommitTargets()
        {
            var payload = targets.Count == 0 ? null : targets[0].Payload.Value;
            if (outputs.Count != 0 && Definition is ParameterRule parameterRule &&
                parameterRule.TargetValue is { UsesPayload: true } &&
                !ReferenceEquals(boundPayload, payload))
            {
                ReleaseContributions();
                if (playback.Weight > 0f) EnsureOutputs();
            }
            for (var i = 0; i < outputs.Count; i++) outputs[i].ReconcileTargets(targets);
        }

        internal void Evaluate(RangeInteraction interaction, RangeRuleEvent trigger,
            bool updateMatched)
        {
            if (!targetAlive) return;
            var signals = runtime.GetSignals(this);
            var context = new RangeRuleContext(in signals, interaction,
                runtime.PrefersReducedMotion, !selectorEvaluated);
            if (trigger != RangeRuleEvent.None && Definition.Trigger == trigger)
            {
                EnsureOutputs();
                Definition.Playback.Trigger(this, in context);
                runtime.RaiseTriggered(this, in context);
            }

            if (Definition.Selector == null) return;
            var matches = Definition.Selector.Matches(in signals);
            selectorEvaluated = true;
            if (matches == selectorMatched)
            {
                if (matches && updateMatched)
                {
                    Definition.Playback.Update(this, in context);
                    runtime.RaiseUpdated(this, in context);
                }
                return;
            }
            selectorMatched = matches;
            if (matches)
            {
                EnsureOutputs();
                Definition.Playback.Enter(this, in context);
                runtime.RaiseEntered(this, in context);
            }
            else
            {
                Definition.Playback.Exit(this, in context);
                runtime.RaiseExited(this, in context);
            }
        }

        internal void TransitionTo(float target, float duration, in Ease easing,
            PlaybackClock clock, bool release)
        {
            playback.TransitionTo(this, target, duration, easing, clock, release);
            if (playback.NeedsTick) runtime.RequestTick(this);
        }

        internal void TriggerPulse(float enterDuration, float exitDuration,
            in Ease easing, PlaybackClock clock)
        {
            playback.TriggerPulse(this, enterDuration, exitDuration, easing, clock);
            if (playback.NeedsTick) runtime.RequestTick(this);
        }

        internal void HoldOneFrame()
        {
            playback.HoldOneFrame(this);
            if (playback.NeedsTick) runtime.RequestTick(this);
        }

        internal void CompleteForReducedMotion()
        {
            playback.CompleteForReducedMotion(this);
            if (playback.NeedsTick) runtime.RequestTick(this);
        }

        internal bool Tick(float deltaTime, PlaybackClock clock)
        {
            if (!targetAlive) return false;
            return playback.Tick(this, deltaTime, clock);
        }

        internal void ReleaseContributions()
        {
            for (var i = outputs.Count - 1; i >= 0; i--) outputs[i].Release();
            outputs.Clear();
        }

        internal void CancelLifetime()
        {
            if (!targetAlive) return;
            Definition.Playback.Cancel(this);
            targetAlive = false;
            selectorMatched = false;
            ReleaseContributions();
            targets.Clear();
        }

        internal void AddOutput(IRangeRuleOutput output)
        {
            boundPayload = targets.Count == 0 ? null : targets[0].Payload.Value;
            outputs.Add(output);
            output.ReconcileTargets(targets);
            output.SetWeight(playback.Weight);
        }

        private void EnsureOutputs()
        {
            if (outputs.Count != 0) return;
            runtime.BindOutputs(this);
        }

    }

    /// <summary>
    /// Per-component runtime of the text's live ranges: parameter ownership over the cascade
    /// (<see cref="Own{TModifier,TValue}"/>), typed signals, rule playbacks and deterministic
    /// contribution composition — without owning feature policy.
    /// </summary>
    public sealed class UniTextRanges : IDisposable
    {
        private static readonly Func<UniTextBase, UniTextRanges> create =
            static owner => new UniTextRanges(owner);

        private sealed class Registration
        {
            public InteractiveModifier owner;
            public IReadOnlyList<RangeStateRule> definitions;
            public int[] definitionOrders;
            public readonly List<RangeRuleInstance> instances = new();
            public readonly List<ModifierRuleBinding> modifierBindings = new();
        }

        private readonly struct ResolvedParameterBinding
        {
            public readonly BaseModifier modifier;
            public readonly ParameterDescriptor parameter;

            public ResolvedParameterBinding(BaseModifier modifier, ParameterDescriptor parameter)
            {
                this.modifier = modifier;
                this.parameter = parameter;
            }
        }

        private readonly struct InstanceKey : IEquatable<InstanceKey>
        {
            private readonly InteractiveModifier owner;
            private readonly RangeStateRule definition;
            private readonly RangeIdentity entity;
            private readonly RangeSegmentId segment;

            public InstanceKey(InteractiveModifier owner, RangeStateRule definition,
                RangeIdentity entity, RangeSegmentId segment)
            {
                this.owner = owner;
                this.definition = definition;
                this.entity = entity;
                this.segment = segment;
            }

            public bool Equals(InstanceKey other)
                => ReferenceEquals(owner, other.owner) && ReferenceEquals(definition, other.definition) &&
                   entity == other.entity && segment == other.segment;

            public override bool Equals(object obj) => obj is InstanceKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(RuntimeHelpers.GetHashCode(owner),
                    RuntimeHelpers.GetHashCode(definition), entity, segment);
        }

        private readonly struct SegmentSignalKey : IEquatable<SegmentSignalKey>
        {
            private readonly RangeIdentity entity;
            private readonly RangeSegmentId segment;

            public RangeIdentity Entity => entity;

            public SegmentSignalKey(RangeIdentity entity, RangeSegmentId segment)
            {
                this.entity = entity;
                this.segment = segment;
            }

            public bool Equals(SegmentSignalKey other)
                => entity == other.entity && segment == other.segment;
            public override bool Equals(object obj)
                => obj is SegmentSignalKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(entity, segment);
        }

        private readonly UniTextBase owner;
        private readonly List<Registration> registrations = new();
        private readonly Dictionary<InstanceKey, RangeRuleInstance> instances = new();
        private readonly Dictionary<RangeIdentity, List<RangeRuleInstance>> instancesByEntity = new();
        private readonly List<RangeIdentity> entityScratch = new();
        private readonly Dictionary<RangeIdentity, RangeSignalState> entitySignals = new();
        private readonly Dictionary<SegmentSignalKey, RangeSignalState> segmentSignals = new();
        private readonly Dictionary<RangeIdentity, List<RangeSignalState>> segmentStatesByEntity = new();
        private readonly List<SegmentSignalKey> staleSignalKeys = new();
        private readonly Dictionary<ParameterSlotKey, object> parameterSlots = new();
        private readonly List<RangeRuleInstance> ticking = new();
        private readonly List<ModifierRuleBinding> dirtyModifierBindings = new();
        private readonly List<IOwnedContribution> ownedHandles = new();
        private readonly List<IOwnedParameterSetLifecycle> ownedSets = new();
        private readonly List<ModifierRange> ownedRangeScratch = new();
        private readonly HashSet<(RangeIdentity, RangeSegmentId)> ownedLiveKeys = new();
        private Action<UniTextCommitChanges> committedCallback;
        private bool committedSubscribed;
        private int ownedParseGeneration = -1;
        private bool suppressOwnedInvalidation;
        private Action frameCallback;
        private bool interactionSubscribed;
        private bool frameSubscribed;
        private bool flushingBindings;
        private int bindingBatchDepth;
        private bool disposed;
        private int declarationOrder;
        private bool prefersReducedMotion;

        internal UniTextBase Owner => owner;

        /// <summary>Text component whose ranges and modifiers this runtime resolves.</summary>
        public UniTextBase Text => owner;
        /// <summary>Whether built-in drivers collapse decorative timing to an immediate result.</summary>
        public bool PrefersReducedMotion
        {
            get => prefersReducedMotion;
            set
            {
                if (prefersReducedMotion == value) return;
                prefersReducedMotion = value;
                if (!value) return;
                BeginBindingBatch();
                try
                {
                    for (var i = 0; i < ticking.Count; i++)
                        ticking[i].CompleteForReducedMotion();
                }
                finally
                {
                    EndBindingBatch();
                }
                RefreshTickSubscription();
            }
        }

        /// <summary>Occurs after a driver's Enter callback for an externally observable playback.</summary>
        public event RangeRuleLifecycleHandler RuleEntered;
        /// <summary>Occurs after a driver's Exit callback for an externally observable playback.</summary>
        public event RangeRuleLifecycleHandler RuleExited;
        /// <summary>Occurs when matched scalar or custom signals change without a selector edge.</summary>
        public event RangeRuleLifecycleHandler RuleUpdated;
        /// <summary>Occurs for an Activated or ContextRequested one-shot playback.</summary>
        public event RangeRuleLifecycleHandler RuleTriggered;

        internal UniTextRanges(UniTextBase owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            owner.Deinitializing += OnHostDeinitialized;
        }

        /// <summary>Returns the range-rule runtime attached to a text component.</summary>
        public static UniTextRanges For(UniTextBase owner)
            => owner.GetOrCreateAttachment(create);

        /// <summary>Returns an existing ranges runtime without creating one.</summary>
        public static bool TryGet(UniTextBase owner, out UniTextRanges ranges)
        {
            if (owner != null) return owner.TryGetAttachment(out ranges);
            ranges = null;
            return false;
        }

        internal static bool HasOwned(UniTextBase owner, BaseModifier modifier,
            ParameterDescriptor parameter, RangeIdentity entity, RangeSegmentId segment)
            => entity.IsValid && segment.IsValid && TryGet(owner, out var ranges) &&
               ranges.parameterSlots.ContainsKey(
                   new ParameterSlotKey(modifier, parameter, entity, segment));

        internal static TValue ResolveFor<TModifier, TValue>(UniTextBase owner,
            TModifier modifier, ParameterDescriptor<TModifier, TValue> parameter,
            RangeIdentity entity, RangeSegmentId segment, TValue baseline)
            where TModifier : BaseModifier
            => TryGet(owner, out var ranges)
                ? ranges.Resolve(modifier, parameter, entity, segment, baseline)
                : baseline;

        /// <summary>Publishes an entity-scoped project signal and reevaluates affected selectors.</summary>
        public void SetSignal<T>(RangeIdentity entity, RangeSignal<T> signal, T value)
        {
            ThrowIfDisposed();
            if (!entity.IsValid) throw new ArgumentException("Entity identity is invalid.", nameof(entity));
            var state = GetOrCreateEntityState(entity);
            if (!state.Set(signal, value)) return;
            EvaluateEntity(entity, default, null, RangeRuleEvent.None, true);
        }

        /// <summary>Publishes a signal for one concrete segment and updates entity aggregates.</summary>
        public void SetSignal<T>(RangeIdentity entity, RangeSegmentId segment,
            RangeSignal<T> signal, T value)
        {
            ThrowIfDisposed();
            if (!entity.IsValid) throw new ArgumentException("Entity identity is invalid.", nameof(entity));
            if (!segment.IsValid) throw new ArgumentException("Segment identity is invalid.", nameof(segment));
            var state = GetOrCreateSegmentState(entity, segment);
            if (!state.Set(signal, value)) return;
            RecomputeEntityBuiltIns(entity);
            EvaluateEntity(entity, segment, null, RangeRuleEvent.None, true);
        }

        /// <summary>Publishes one entity-scoped signal to every current rule entity on a channel.</summary>
        public void SetSignal<T>(RangeChannel channel, RangeSignal<T> signal, T value)
        {
            ThrowIfDisposed();
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (signal == null) throw new ArgumentNullException(nameof(signal));
            CollectEntities(channel);
            for (var i = 0; i < entityScratch.Count; i++)
                SetSignal(entityScratch[i], signal, value);
        }

        /// <summary>Publishes one entity-scoped signal to every current rule entity on this text.</summary>
        public void SetSignalForAll<T>(RangeSignal<T> signal, T value)
        {
            ThrowIfDisposed();
            if (signal == null) throw new ArgumentNullException(nameof(signal));
            entityScratch.Clear();
            foreach (var pair in instancesByEntity) entityScratch.Add(pair.Key);
            for (var i = 0; i < entityScratch.Count; i++)
                SetSignal(entityScratch[i], signal, value);
        }

        /// <summary>Advances only built-in drivers configured with the Manual clock.</summary>
        public void AdvanceManual(float deltaTime)
        {
            ThrowIfDisposed();
            BeginBindingBatch();
            try
            {
                for (var i = ticking.Count - 1; i >= 0; i--)
                {
                    var instance = ticking[i];
                    if (!instance.UsesManualClock) continue;
                    if (!instance.Tick(deltaTime, PlaybackClock.Manual)) ticking.RemoveAt(i);
                }
            }
            finally
            {
                EndBindingBatch();
            }
            RefreshTickSubscription();
        }

        /// <summary>
        /// Takes one parameter of a materialized range into ownership. The owned value composes
        /// on the range's cascade result under <paramref name="composition"/> until the handle is
        /// released or the range's identity retires with a text edit.
        /// </summary>
        public OwnedParameter<TValue> Own<TModifier, TValue>(TModifier modifier,
            ParameterDescriptor<TModifier, TValue> parameter, RangeIdentity entity,
            RangeSegmentId segment,
            ParameterComposition composition = ParameterComposition.Replace, int priority = 0)
            where TModifier : BaseModifier
        {
            ThrowIfDisposed();
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            if (!entity.IsValid) throw new ArgumentException("Entity identity is invalid.", nameof(entity));
            if (!segment.IsValid) throw new ArgumentException("Segment identity is invalid.", nameof(segment));
            var owned = new OwnedContribution<TModifier, TValue>(this, modifier, parameter,
                entity, segment, composition, priority);
            ownedHandles.Add(owned);
            ownedParseGeneration = owner.AttributeParser?.ParseGeneration ?? -1;
            SubscribeCommitted();
            return owned.GetHandle();
        }

        /// <summary>
        /// Takes standing ownership of one parameter across every range the query matches, now
        /// and after every future parse; release the set to return them all to their cascades.
        /// </summary>
        public OwnedParameterSet<TModifier, TValue> Own<TModifier, TValue>(in RangeQuery query,
            ParameterDescriptor<TModifier, TValue> parameter,
            ParameterComposition composition = ParameterComposition.Replace, int priority = 0)
            where TModifier : BaseModifier
        {
            ThrowIfDisposed();
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            if (query.Target is not TModifier typed)
                throw new ArgumentException(
                    $"The query targets {query.Target?.GetType().Name ?? "nothing"}, not " +
                    $"{typeof(TModifier).Name}.", nameof(query));
            var set = new OwnedParameterSet<TModifier, TValue>(this, in query, typed, parameter,
                composition, priority);
            ownedSets.Add(set);
            ownedParseGeneration = owner.AttributeParser?.ParseGeneration ?? -1;
            SubscribeCommitted();
            set.Reconcile();
            return set;
        }

        internal void RemoveOwned(IOwnedContribution owned)
        {
            ownedHandles.Remove(owned);
            if (ownedHandles.Count == 0 && ownedSets.Count == 0) UnsubscribeCommitted();
        }

        internal void RemoveOwnedSet(IOwnedParameterSetLifecycle set)
        {
            ownedSets.Remove(set);
            if (ownedHandles.Count == 0 && ownedSets.Count == 0) UnsubscribeCommitted();
        }

        private void SubscribeCommitted()
        {
            if (committedSubscribed) return;
            committedCallback ??= OnOwnerCommitted;
            owner.Committed += committedCallback;
            committedSubscribed = true;
        }

        private void UnsubscribeCommitted()
        {
            if (!committedSubscribed) return;
            owner.Committed -= committedCallback;
            committedSubscribed = false;
        }

        private void OnOwnerCommitted(UniTextCommitChanges changes)
            => ReconcileOwnedMembership(suppressInvalidation: false);

        /// <summary>
        /// Rematerializes owned membership against the current parse, once per parse generation:
        /// surviving ranges keep their values, new matches receive them, and ownership whose range
        /// identity did not survive is released back to the cascade. When the caller runs before
        /// the parse's spans apply, <paramref name="suppressInvalidation"/> must be true: the
        /// imminent pass consumes every written value itself, and the invalidation chain touches
        /// main-thread-only engine API while that caller may run on a worker.
        /// </summary>
        internal void ReconcileOwnedMembership(bool suppressInvalidation)
        {
            if (ownedHandles.Count == 0 && ownedSets.Count == 0) return;
            var parser = owner.AttributeParser;
            if (parser == null || parser.ParseGeneration == ownedParseGeneration) return;
            ownedParseGeneration = parser.ParseGeneration;
            suppressOwnedInvalidation = suppressInvalidation;
            try
            {
                ReconcileOwnedMembershipCore(parser);
            }
            finally
            {
                suppressOwnedInvalidation = false;
            }
        }

        private void ReconcileOwnedMembershipCore(AttributeParser parser)
        {
            for (var i = ownedSets.Count - 1; i >= 0; i--)
            {
                var set = ownedSets[i];
                if (set.IsAlive) set.Reconcile();
                else ownedSets.RemoveAt(i);
            }
            BaseModifier scanned = null;
            for (var i = ownedHandles.Count - 1; i >= 0; i--)
            {
                var owned = ownedHandles[i];
                if (!owned.IsAlive)
                {
                    ownedHandles.RemoveAt(i);
                    continue;
                }
                if (!ReferenceEquals(owned.Modifier, scanned))
                {
                    scanned = owned.Modifier;
                    ownedRangeScratch.Clear();
                    ownedLiveKeys.Clear();
                    parser.CollectRangesFor(scanned, ownedRangeScratch);
                    for (var r = 0; r < ownedRangeScratch.Count; r++)
                        ownedLiveKeys.Add((ownedRangeScratch[r].Identity,
                            ownedRangeScratch[r].Segment));
                }
                if (ownedLiveKeys.Contains((owned.Entity, owned.Segment))) continue;
                owned.Kill();
                ownedHandles.RemoveAt(i);
            }
            ownedRangeScratch.Clear();
            ownedLiveKeys.Clear();
            if (ownedHandles.Count == 0 && ownedSets.Count == 0) UnsubscribeCommitted();
        }

        internal void AppendDiagnostics(List<RangeRuleDiagnostic> result)
        {
            foreach (var pair in instances)
            {
                var instance = pair.Value;
                var signals = GetSignals(instance);
                result.Add(new RangeRuleDiagnostic(instance, in signals,
                    instance.SelectorMatched, instance.Transitioning));
            }
        }

        internal void Register(InteractiveModifier modifier, IReadOnlyList<RangeStateRule> definitions)
        {
            ThrowIfDisposed();
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (definitions == null || definitions.Count == 0) return;
            if (FindRegistration(modifier) != null)
                throw new InvalidOperationException("The InteractiveModifier rule graph is already registered.");

            var registration = new Registration
            {
                owner = modifier,
                definitions = definitions,
                definitionOrders = new int[definitions.Count],
            };
            try
            {
                for (var i = 0; i < definitions.Count; i++)
                {
                    registration.definitionOrders[i] = declarationOrder++;
                    var definition = definitions[i];
                    if (definition is ModifierRule modifierEffect)
                        registration.modifierBindings.Add(new ModifierRuleBinding(this, modifierEffect));
                }
            }
            catch
            {
                for (var i = 0; i < registration.modifierBindings.Count; i++)
                    registration.modifierBindings[i].Dispose();
                throw;
            }
            registrations.Add(registration);
            try
            {
                SubscribeInteraction();
                Synchronize(modifier);
            }
            catch
            {
                Unregister(modifier);
                throw;
            }
        }

        internal void Unregister(InteractiveModifier modifier)
        {
            var registration = FindRegistration(modifier);
            if (registration == null) return;
            BeginBindingBatch();
            try
            {
                for (var i = registration.instances.Count - 1; i >= 0; i--)
                    RemoveInstance(registration, registration.instances[i]);
                for (var i = 0; i < registration.modifierBindings.Count; i++)
                    registration.modifierBindings[i].Dispose();
                registration.modifierBindings.Clear();
                registrations.Remove(registration);
                if (registrations.Count == 0) UnsubscribeInteraction();
            }
            finally
            {
                EndBindingBatch();
            }
            RefreshTickSubscription();
        }

        internal void Synchronize(InteractiveModifier modifier)
        {
            var registration = FindRegistration(modifier);
            if (registration == null) return;
            BeginBindingBatch();
            try
            {
                for (var i = 0; i < registration.instances.Count; i++)
                    registration.instances[i].ClearTargets();

                var ranges = modifier.InteractiveRanges;
                for (var definitionIndex = 0; definitionIndex < registration.definitions.Count;
                     definitionIndex++)
                {
                    var definition = registration.definitions[definitionIndex];
                    for (var rangeIndex = 0; rangeIndex < ranges.Length; rangeIndex++)
                    {
                        ref readonly var range = ref ranges[rangeIndex];
                        if (!range.Identity.IsValid)
                            throw new InvalidOperationException(
                                "RangeStateRule requires a stable RangeIdentity from its RangeSource.");
                        var segment = definition.Scope == RangeRuleScope.Segment
                            ? range.Segment.Id
                            : default;
                        var key = new InstanceKey(modifier, definition, range.Identity, segment);
                        if (!instances.TryGetValue(key, out var instance))
                        {
                            instance = new RangeRuleInstance(this, modifier, definition,
                                range.Identity, segment,
                                registration.definitionOrders[definitionIndex]);
                            instances.Add(key, instance);
                            registration.instances.Add(instance);
                            if (!instancesByEntity.TryGetValue(range.Identity, out var entityInstances))
                            {
                                entityInstances = new List<RangeRuleInstance>(2);
                                instancesByEntity.Add(range.Identity, entityInstances);
                            }
                            entityInstances.Add(instance);
                        }
                        instance.AddTarget(in range);
                        var segmentState = GetOrCreateSegmentState(range.Identity, range.Segment.Id);
                        segmentState.disabled = !modifier.RouterIsEnabled(in range);
                    }
                }

                for (var i = registration.instances.Count - 1; i >= 0; i--)
                {
                    var instance = registration.instances[i];
                    if (!instance.Seen)
                    {
                        RemoveInstance(registration, instance);
                        continue;
                    }
                    RecomputeEntityBuiltIns(instance.Entity);
                    instance.CommitTargets();
                    instance.Evaluate(null, RangeRuleEvent.None, false);
                }
            }
            finally
            {
                EndBindingBatch();
            }
        }

        internal TValue Resolve<TModifier, TValue>(TModifier modifier,
            ParameterDescriptor<TModifier, TValue> parameter, RangeIdentity entity,
            RangeSegmentId segment, TValue baseline)
            where TModifier : BaseModifier
        {
            if (!entity.IsValid || !segment.IsValid) return baseline;
            var key = new ParameterSlotKey(modifier, parameter, entity, segment);
            return parameterSlots.TryGetValue(key, out var candidate)
                ? ((ParameterSlot<TModifier, TValue>)candidate).Resolve(baseline)
                : baseline;
        }

        internal TModifier ResolveModifier<TModifier>(ModifierKey<TModifier> key,
            InteractiveModifier ruleOwner)
            where TModifier : BaseModifier
        {
            var modifier = owner.AttributeParser?.ResolveModifierNode(ruleOwner, key.NodeId);
            if (modifier == null)
                throw new InvalidOperationException($"Modifier node '{key.NodeId}' was not found.");
            if (modifier is not TModifier typed)
                throw new InvalidOperationException(
                    $"Modifier node '{key.NodeId}' resolves to {modifier.GetType().Name}, not " +
                    $"{typeof(TModifier).Name}.");
            return typed;
        }

        internal void BindOutputs(RangeRuleInstance instance)
        {
            if (instance.Definition is ParameterRule parameterRule)
            {
                BindParameterOutput(instance, parameterRule);
                return;
            }
            if (instance.Definition is ModifierRule modifierEffect)
            {
                var registration = FindRegistration(instance.Owner) ?? throw new InvalidOperationException(
                    "The rule owner is no longer registered.");
                for (var i = 0; i < registration.modifierBindings.Count; i++)
                {
                    var binding = registration.modifierBindings[i];
                    if (!ReferenceEquals(binding.Definition, modifierEffect)) continue;
                    instance.AddOutput(new ModifierRuleOutput(binding));
                    return;
                }
                throw new InvalidOperationException("ModifierRule binding was not prepared.");
            }
            throw new NotSupportedException(instance.Definition.GetType().FullName);
        }

        internal ParameterSlot<TModifier, TValue> GetOrCreateSlot<TModifier, TValue>(
            TModifier modifier, ParameterDescriptor<TModifier, TValue> parameter,
            in InteractiveRange target) where TModifier : BaseModifier
            => GetOrCreateSlot(modifier, parameter, target.Identity, target.Segment.Id);

        internal ParameterSlot<TModifier, TValue> GetOrCreateSlot<TModifier, TValue>(
            TModifier modifier, ParameterDescriptor<TModifier, TValue> parameter,
            RangeIdentity entity, RangeSegmentId segment) where TModifier : BaseModifier
        {
            var key = new ParameterSlotKey(modifier, parameter, entity, segment);
            if (parameterSlots.TryGetValue(key, out var existing))
                return (ParameterSlot<TModifier, TValue>)existing;
            var created = new ParameterSlot<TModifier, TValue>(this, modifier, parameter, in key);
            parameterSlots.Add(key, created);
            return created;
        }

        internal int NextDeclarationOrder() => declarationOrder++;

        internal bool SameSlot<TModifier, TValue>(
            ParameterContribution<TModifier, TValue> contribution, TModifier modifier,
            ParameterDescriptor<TModifier, TValue> parameter, in InteractiveRange target)
            where TModifier : BaseModifier
        {
            var key = new ParameterSlotKey(modifier, parameter, target.Identity,
                target.Segment.Id);
            return parameterSlots.TryGetValue(key, out var slot) &&
                   ReferenceEquals(slot, contribution.Slot);
        }

        internal void ReleaseSlot(in ParameterSlotKey key, object slot)
        {
            if (parameterSlots.TryGetValue(key, out var current) && ReferenceEquals(current, slot))
                parameterSlots.Remove(key);
        }

        internal void Invalidate(BaseModifier modifier, ParameterDescriptor parameter)
        {
            if (suppressOwnedInvalidation) return;
            parameter.Invalidate(modifier);
        }

        internal void RequestTick(RangeRuleInstance instance)
        {
            if (!ticking.Contains(instance)) ticking.Add(instance);
            RefreshTickSubscription();
        }

        internal void QueueModifierBinding(ModifierRuleBinding binding)
        {
            if (!dirtyModifierBindings.Contains(binding)) dirtyModifierBindings.Add(binding);
            if (!flushingBindings && bindingBatchDepth == 0) FlushModifierBindings();
        }

        internal void OnHostDeinitialized()
        {
            for (var i = registrations.Count - 1; i >= 0; i--)
                Unregister(registrations[i].owner);
            entitySignals.Clear();
            segmentSignals.Clear();
            segmentStatesByEntity.Clear();
            instancesByEntity.Clear();
            entityScratch.Clear();
            staleSignalKeys.Clear();
            for (var i = ownedHandles.Count - 1; i >= 0; i--) ownedHandles[i].Kill();
            ownedHandles.Clear();
            for (var i = ownedSets.Count - 1; i >= 0; i--) ownedSets[i].Kill();
            ownedSets.Clear();
            UnsubscribeCommitted();
            parameterSlots.Clear();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed) return;
            owner.Deinitializing -= OnHostDeinitialized;
            OnHostDeinitialized();
            disposed = true;
            UnsubscribeInteraction();
            if (frameSubscribed) owner.FrameUpdated -= frameCallback;
            frameSubscribed = false;
            ticking.Clear();
            dirtyModifierBindings.Clear();
        }

        internal RangeSignalReader GetSignals(RangeRuleInstance instance)
        {
            if (instance.Definition.Scope == RangeRuleScope.Entity)
                return new RangeSignalReader(GetOrCreateEntityState(instance.Entity));
            var segment = instance.Segment;
            return new RangeSignalReader(GetOrCreateSegmentState(instance.Entity, segment));
        }

        internal void RaiseEntered(RangeRuleInstance instance, in RangeRuleContext context)
            => RuleEntered?.Invoke(instance, in context);
        internal void RaiseExited(RangeRuleInstance instance, in RangeRuleContext context)
            => RuleExited?.Invoke(instance, in context);
        internal void RaiseUpdated(RangeRuleInstance instance, in RangeRuleContext context)
            => RuleUpdated?.Invoke(instance, in context);
        internal void RaiseTriggered(RangeRuleInstance instance, in RangeRuleContext context)
            => RuleTriggered?.Invoke(instance, in context);

        internal void RefreshTickSubscription()
        {
            if (disposed) return;
            for (var i = ticking.Count - 1; i >= 0; i--)
                if (!ticking[i].UsesFrameClock && !ticking[i].UsesManualClock)
                    ticking.RemoveAt(i);
            var needsFrame = false;
            for (var i = 0; i < ticking.Count; i++)
                if (ticking[i].UsesFrameClock) { needsFrame = true; break; }
            if (needsFrame == frameSubscribed) return;
            frameCallback ??= OnFrameUpdated;
            if (needsFrame) owner.FrameUpdated += frameCallback;
            else owner.FrameUpdated -= frameCallback;
            frameSubscribed = needsFrame;
        }

        private void BindParameterOutput(RangeRuleInstance instance, ParameterRule definition)
        {
            var binding = ResolveParameterBinding(instance.Owner, definition);
            instance.AddOutput(binding.parameter.Bind(this, instance, binding.modifier, definition,
                definition.TargetValue.Resolve(instance.RepresentativeRange.Payload)));
        }

        private ResolvedParameterBinding ResolveParameterBinding(InteractiveModifier ruleOwner,
            ParameterRule definition)
        {
            var modifier = owner.AttributeParser.ResolveModifierNode(
                ruleOwner, definition.TargetNode);
            var parameter = ParameterDescriptor.Find(modifier, definition.ParameterId) ??
                           throw new InvalidOperationException(
                               modifier.GetType().Name + " declares no parameter '" +
                               definition.ParameterId + "'.");
            return new ResolvedParameterBinding(modifier, parameter);
        }

        private void OnInteraction(RangeInteraction interaction)
        {
            if (!interaction.Entity.IsValid) return;
            var entity = interaction.Entity;
            var segment = interaction.HitSegment.Id;
            if (segment.IsValid)
            {
                var state = GetOrCreateSegmentState(entity, segment);
                if (interaction.Kind == RangeInteractionKind.StateChanged)
                {
                    switch (interaction.Signal)
                    {
                        case RangeInteractionSignal.Hovered:
                            state.hovered = interaction.SignalValue;
                            break;
                        case RangeInteractionSignal.Pressed:
                            state.pressed = interaction.SignalValue;
                            break;
                        case RangeInteractionSignal.Focused:
                            state.focused = interaction.SignalValue;
                            break;
                        case RangeInteractionSignal.Disabled:
                            state.disabled = interaction.SignalValue;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                else if (interaction.Kind == RangeInteractionKind.LongPressProgress)
                {
                    state.longPressProgress = Mathf.Clamp01(interaction.Progress);
                }
                else if (interaction.Kind == RangeInteractionKind.Gesture)
                {
                    state.Set(RangeSignals.DragProgress, Mathf.Clamp01(interaction.Progress));
                }
                else if (interaction.Kind is RangeInteractionKind.Released or
                         RangeInteractionKind.Canceled)
                {
                    state.longPressProgress = 0f;
                    state.Set(RangeSignals.DragProgress, 0f);
                }
                RecomputeEntityBuiltIns(entity);
            }

            var trigger = interaction.Kind switch
            {
                RangeInteractionKind.Activated => RangeRuleEvent.Activated,
                RangeInteractionKind.ContextRequested => RangeRuleEvent.ContextRequested,
                _ => RangeRuleEvent.None,
            };
            var signalChanged = interaction.Kind is RangeInteractionKind.StateChanged or
                RangeInteractionKind.LongPressProgress or RangeInteractionKind.Gesture or
                RangeInteractionKind.Released or
                RangeInteractionKind.Canceled;
            EvaluateEntity(entity, segment, interaction, trigger, signalChanged);
        }

        private void EvaluateEntity(RangeIdentity entity, RangeSegmentId segment,
            RangeInteraction interaction, RangeRuleEvent trigger, bool updateMatched)
        {
            if (!instancesByEntity.TryGetValue(entity, out var affected)) return;
            BeginBindingBatch();
            try
            {
                for (var i = 0; i < affected.Count; i++)
                {
                    var instance = affected[i];
                    if (instance.Definition.Scope == RangeRuleScope.Segment &&
                        segment.IsValid && instance.Segment != segment) continue;
                    instance.Evaluate(interaction, trigger, updateMatched);
                }
            }
            finally
            {
                EndBindingBatch();
            }
        }

        private void RecomputeEntityBuiltIns(RangeIdentity entity)
        {
            var target = GetOrCreateEntityState(entity);
            var hovered = false;
            var pressed = false;
            var focused = false;
            var disabled = true;
            var found = false;
            var progress = 0f;
            var dragProgress = 0f;
            if (segmentStatesByEntity.TryGetValue(entity, out var states))
            {
                found = states.Count > 0;
                for (var i = 0; i < states.Count; i++)
                {
                    var state = states[i];
                    hovered |= state.hovered;
                    pressed |= state.pressed;
                    focused |= state.focused;
                    disabled &= state.disabled;
                    progress = Mathf.Max(progress, state.longPressProgress);
                    dragProgress = Mathf.Max(dragProgress, state.dragProgress);
                }
            }
            target.hovered = hovered;
            target.pressed = pressed;
            target.focused = focused;
            target.disabled = found && disabled;
            target.longPressProgress = progress;
            target.dragProgress = dragProgress;
        }

        private RangeSignalState GetOrCreateEntityState(RangeIdentity entity)
        {
            if (entitySignals.TryGetValue(entity, out var state)) return state;
            state = new RangeSignalState();
            entitySignals.Add(entity, state);
            return state;
        }

        private RangeSignalState GetOrCreateSegmentState(RangeIdentity entity,
            RangeSegmentId segment)
        {
            var key = new SegmentSignalKey(entity, segment);
            if (segmentSignals.TryGetValue(key, out var state)) return state;
            state = new RangeSignalState();
            segmentSignals.Add(key, state);
            if (!segmentStatesByEntity.TryGetValue(entity, out var states))
            {
                states = new List<RangeSignalState>(2);
                segmentStatesByEntity.Add(entity, states);
            }
            states.Add(state);
            return state;
        }

        private Registration FindRegistration(InteractiveModifier modifier)
        {
            for (var i = 0; i < registrations.Count; i++)
                if (ReferenceEquals(registrations[i].owner, modifier)) return registrations[i];
            return null;
        }

        private void CollectEntities(RangeChannel channel)
        {
            entityScratch.Clear();
            foreach (var pair in instancesByEntity)
            {
                var instancesForEntity = pair.Value;
                for (var i = 0; i < instancesForEntity.Count; i++)
                {
                    var instance = instancesForEntity[i];
                    if (instance.TargetCount == 0 ||
                        instance.RepresentativeRange.Channel != channel) continue;
                    entityScratch.Add(pair.Key);
                    break;
                }
            }
        }

        private void RemoveInstance(Registration registration, RangeRuleInstance instance)
        {
            instance.CancelLifetime();
            registration.instances.Remove(instance);
            var key = new InstanceKey(registration.owner, instance.Definition, instance.Entity,
                instance.Segment);
            instances.Remove(key);
            ticking.Remove(instance);
            if (instancesByEntity.TryGetValue(instance.Entity, out var entityInstances))
            {
                entityInstances.Remove(instance);
                if (entityInstances.Count == 0) instancesByEntity.Remove(instance.Entity);
            }
            ReleaseSignalsIfUnreferenced(instance.Entity);
        }

        private void ReleaseSignalsIfUnreferenced(RangeIdentity entity)
        {
            if (instancesByEntity.ContainsKey(entity)) return;
            entitySignals.Remove(entity);
            segmentStatesByEntity.Remove(entity);
            staleSignalKeys.Clear();
            foreach (var pair in segmentSignals)
                if (pair.Key.Entity == entity) staleSignalKeys.Add(pair.Key);
            for (var i = 0; i < staleSignalKeys.Count; i++)
                segmentSignals.Remove(staleSignalKeys[i]);
        }

        private void SubscribeInteraction()
        {
            if (interactionSubscribed) return;
            UniTextInteractions.For(owner).Capturing += OnInteraction;
            interactionSubscribed = true;
        }

        private void UnsubscribeInteraction()
        {
            if (!interactionSubscribed) return;
            if (UniTextInteractions.TryGet(owner, out var interactions))
                interactions.Capturing -= OnInteraction;
            interactionSubscribed = false;
        }

        private void OnFrameUpdated()
        {
            BeginBindingBatch();
            try
            {
                for (var i = ticking.Count - 1; i >= 0; i--)
                {
                    var instance = ticking[i];
                    if (!instance.UsesFrameClock) continue;
                    var clock = instance.TransitionClock;
                    if (!instance.Tick(PlaybackTime.Delta(clock), clock)) ticking.RemoveAt(i);
                }
            }
            finally
            {
                EndBindingBatch();
            }
            RefreshTickSubscription();
        }

        private void BeginBindingBatch() => bindingBatchDepth++;

        private void EndBindingBatch()
        {
            bindingBatchDepth--;
            if (bindingBatchDepth == 0) FlushModifierBindings();
        }

        private void FlushModifierBindings()
        {
            if (flushingBindings || dirtyModifierBindings.Count == 0) return;
            flushingBindings = true;
            try
            {
                while (dirtyModifierBindings.Count > 0)
                {
                    var index = dirtyModifierBindings.Count - 1;
                    var binding = dirtyModifierBindings[index];
                    dirtyModifierBindings.RemoveAt(index);
                    binding.Rebuild();
                }
            }
            finally
            {
                flushingBindings = false;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(UniTextRanges));
        }
    }
}
