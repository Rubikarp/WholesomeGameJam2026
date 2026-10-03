using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightSide
{
    /// <summary>
    /// Declares semantic interaction for ranges selected by a Style source. One per-component
    /// <see cref="UniTextInteractions"/> router owns input, geometry, overlap and pointer state;
    /// this modifier owns only authored policy, target events and optional default actions.
    /// </summary>
    [Serializable]
    [TypeGroup("Interactive", 3)]
    [TypeDescription("Makes ranges interactive through stable channels and final-geometry hit testing.")]
    public partial class InteractiveModifier : BaseModifier, IModifierCommitChanges
    {
        UniTextCommitChanges IModifierCommitChanges.CommitChanges
            => UniTextCommitChanges.Content;

        /// <summary>Stable event channel, or null to inherit the source channel.</summary>
        [SerializeField]
        [Tooltip("Stable event channel. When unset, the channel emitted by the range source is used.")]
        [StateProperty(nameof(MarkMeshDirty), Validator = nameof(ValidateRangeChannel))]
        private RangeChannel channel;

        /// <summary>Hardware cursor shown while a mouse or pen hovers this range.</summary>
        [SerializeField]
        [Tooltip("Hardware cursor while a mouse or pen rests over this modifier's entities.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private CursorType hoverCursor = CursorType.Link;

        /// <summary>Primary value used when the source and markup provide none.</summary>
        [SerializeField, FormerlySerializedAs("defaultData")]
        [Tooltip("Primary value used when neither the source nor markup provides one.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private string defaultValue;

        /// <summary>Priority used to resolve overlapping interactive ranges.</summary>
        [SerializeField, Parameter]
        [Tooltip("Explicit overlap priority. Higher values win before Style order and specificity.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private int interactionPriority;

        /// <summary>Target scope that must match the press origin for activation.</summary>
        [SerializeField, Parameter]
        [Tooltip("Which target must match the press origin when activation resolves.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private RangeInteractionScope activationScope = RangeInteractionScope.Entity;

        /// <summary>Symmetric expansion applied to hit geometry.</summary>
        [SerializeField, Parameter, Unit("px|em")]
        [Tooltip("Symmetric expansion of hit geometry, independent from visual highlight padding.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private UnitVector2 hitPadding = new(Vector2.zero, UnitKind.Em);

        /// <summary>Minimum width and height of every hit fragment.</summary>
        [SerializeField, Parameter, Unit("px|em")]
        [Tooltip("Minimum width and height of every hit fragment.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private UnitVector2 minimumHitSize = new(Vector2.zero, UnitKind.Absolute);

        /// <summary>Whether wrapped and BiDi fragments remain separate or bridge gaps.</summary>
        [SerializeField, Parameter]
        [Tooltip("Whether wrapped and BiDi fragments remain separate or intentionally bridge gaps.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private WrappedGapPolicy wrappedGapPolicy = WrappedGapPolicy.Separate;

        /// <summary>Whitespace included in the interactive target.</summary>
        [SerializeField, Parameter]
        [Tooltip("Whether meaningful layout whitespace or only rendered glyph faces are targetable.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private RangeWhitespacePolicy whitespacePolicy = RangeWhitespacePolicy.Layout;

        /// <summary>Whether inline objects participate in the interactive target.</summary>
        [SerializeField, Parameter]
        [Tooltip("Whether inline object replacement clusters participate in the target.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private InlineObjectPolicy inlineObjectPolicy = InlineObjectPolicy.Include;

        /// <summary>Whether events leave the underlying UI gesture unblocked.</summary>
        [SerializeField, Parameter]
        [Tooltip("Emit interaction events without capturing the gesture or blocking underlying UI.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private bool passThrough;

        /// <summary>Custom recognizers evaluated by priority and then authored order.</summary>
        [SerializeReference, TypeSelector]
        [Tooltip("Optional custom recognizers. Claims resolve by priority, then this Inspector order.")]
        [StateList(nameof(ApplyGestureRecognizersChange))]
        private RangeGestureRecognizer[] gestureRecognizers = Array.Empty<RangeGestureRecognizer>();

        /// <summary>Rules driven by this range's interaction signals.</summary>
        [SerializeReference, TypeSelector, FormerlySerializedAs("effects")]
        [Tooltip("State/event rules driven by typed range signals without mutating this Style's authored modifiers.")]
        [StateList(nameof(ApplyRulesChange))]
        private RangeStateRule[] rules = Array.Empty<RangeStateRule>();

        /// <summary>Cancelable default actions executed after routed handlers.</summary>
        [SerializeReference, TypeSelector]
        [Tooltip("Cancelable default actions executed in this Inspector order after routed handlers.")]
        [StateList(nameof(ApplyActionsChange))]
        private RangeAction[] actions = Array.Empty<RangeAction>();

        /// <summary>Order used for keyboard and gamepad navigation.</summary>
        [SerializeField]
        [Tooltip("Logical uses source order; Visual follows final geometry for arrow/gamepad movement.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private RangeNavigationOrder navigationOrder = RangeNavigationOrder.Logical;

        /// <summary>Boundary used when navigating between interactive ranges.</summary>
        [SerializeField]
        [Tooltip("Whether internal navigation crosses channel boundaries.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private RangeNavigationGroup navigationGroup = RangeNavigationGroup.Host;

        /// <summary>Whether navigation wraps at the first and last eligible entity.</summary>
        [SerializeField]
        [Tooltip("Wrap internal navigation at the first/last eligible entity.")]
        [StateProperty(nameof(MarkMeshDirty))]
        private bool wrapNavigation;

        [NonSerialized] private PooledArrayAttribute<InteractiveRange> rangesAttribute;
        [NonSerialized] private string attributeKey;
        [NonSerialized] private Action disabledCommitCallback;
        [NonSerialized] private RangeStateRule[] subscribedRules;

        private void ApplyRulesChange()
        {
            if (!IsInitialized) return;
            ModifierGraphValidator.ThrowIfInvalid(ModifierGraphValidator.FindRoot(this));
            var runtime = UniTextRanges.For(uniText);
            runtime.Unregister(this);
            BindRuleChanges();
            runtime.Register(this, rules);
        }

        private void ApplyGestureRecognizersChange() { }

        private void ApplyActionsChange()
        {
            if (IsInitialized)
                ModifierGraphValidator.ThrowIfInvalid(ModifierGraphValidator.FindRoot(this));
        }

        internal RangeNavigationOrder RouterNavigationOrder => navigationOrder;
        internal RangeNavigationGroup RouterNavigationGroup => navigationGroup;
        internal bool RouterWrapNavigation => wrapNavigation;

        internal void RemapRuleTargets(IReadOnlyDictionary<ModifierNodeId, ModifierNodeId> identities)
        {
            for (var i = 0; i < rules.Length; i++)
                if (rules[i] is ParameterRule property)
                    property.RemapTargetNode(identities);
        }

        /// <summary>The interactive ranges produced by the last modifier application cycle.</summary>
        public ReadOnlySpan<InteractiveRange> InteractiveRanges
            => rangesAttribute != null && rangesAttribute.Count > 0
                ? (ReadOnlySpan<InteractiveRange>)rangesAttribute.buffer.Span
                : default;

        /// <summary>Occurs for every event delivered to this modifier's concrete target.</summary>
        public event RangeInteractionHandler Interaction;

        /// <summary>Per-instance attribute key. Override only for an intentional shared range buffer.</summary>
        protected virtual string AttributeKey =>
            attributeKey ??= $"interactive#{RuntimeHelpers.GetHashCode(this):x8}";

        protected override void OnEnable()
        {
            rangesAttribute ??= buffers.GetOrCreateAttributeData<PooledArrayAttribute<InteractiveRange>>(AttributeKey);
            UniTextInteractions.For(uniText).Register(this);
            BindRuleChanges();
            UniTextRanges.For(uniText).Register(this, rules);
        }

        protected internal override void PrepareForParallel()
            => BindRuleChanges();

        protected override void OnDisable()
        {
            UniTextRanges.For(uniText).Unregister(this);
            UnbindRuleChanges();
            UniTextInteractions.For(uniText).Unregister(this);
            UniTextCursor.Release(this);
            disabledCommitCallback ??= OnDisabledCommit;
            uniText.QueuePostCommit(disabledCommitCallback);
        }

        protected override void OnDestroy()
        {
            if (uniText != null && UniTextRanges.TryGet(uniText, out var rangesRuntime))
                rangesRuntime.Unregister(this);
            UnbindRuleChanges();
            if (uniText != null && UniTextInteractions.TryGet(uniText, out var interactions))
                interactions.Unregister(this);
            UniTextCursor.Release(this);
            buffers?.ReleaseAttributeData(AttributeKey);
            rangesAttribute = null;
        }

        protected override void BeforeApply()
        {
            rangesAttribute?.buffer.FakeClear();
            if (uniText != null && UniTextInteractions.TryGet(uniText, out var interactions))
                interactions.MarkRangesDirty();
        }

        /// <summary>
        /// Resolves the primary value and all optional hit-policy slots. Subclasses that own the
        /// primary value should consume it, then call <see cref="AddRange(in RangeApplyContext,string,ref ParameterReader)"/>.
        /// </summary>
        protected override void OnApply(in RangeApplyContext context)
        {
            var reader = context.Parameters.GetReader();
            var resolvedValue = context.PrimaryValue;
            if (resolvedValue == null)
                resolvedValue = reader.NextString(out var supplied) ? supplied : defaultValue;
            AddRange(in context, resolvedValue, ref reader);
        }

        /// <summary>Adds an attributed range after consuming the primary value slot.</summary>
        protected void AddRange(in RangeApplyContext context, string primaryValue)
        {
            var reader = context.Parameters.GetReader();
            if (context.PrimaryValue == null) reader.Next(out _);
            AddRange(in context, primaryValue, ref reader);
        }

        /// <summary>Adds an attributed range and consumes optional interaction policy slots from the reader.</summary>
        protected void AddRange(in RangeApplyContext context, string primaryValue, ref ParameterReader reader)
        {
            reader.NextInt(out var resolvedPriority, interactionPriority);
            reader.NextEnum(out RangeInteractionScope resolvedScope, activationScope);
            reader.NextUnitVector2(out var resolvedPadding, out var paddingUnit, hitPadding);
            reader.NextUnitVector2(out var resolvedMinimum, out var minimumUnit, minimumHitSize);
            reader.NextEnum(out WrappedGapPolicy resolvedGap, wrappedGapPolicy);
            reader.NextEnum(out RangeWhitespacePolicy resolvedWhitespace, whitespacePolicy);
            reader.NextEnum(out InlineObjectPolicy resolvedInline, inlineObjectPolicy);
            var resolvedPassThrough = passThrough;
            if (reader.Next(out var passThroughToken) && !passThroughToken.IsEmpty)
                resolvedPassThrough = passThroughToken.AsBool(passThrough);

            var resolvedChannel = channel != null ? channel : context.Channel;
            resolvedChannel?.ValidatePayload(context.Payload.Value);
            if (resolvedChannel == null && context.Payload.HasValue)
                throw new InvalidOperationException("An interactive payload requires a RangeChannel.");

            var settings = new InteractiveRangeSettings(resolvedPriority, resolvedScope,
                new UnitVector2(resolvedPadding, paddingUnit),
                new UnitVector2(resolvedMinimum, minimumUnit), resolvedGap, resolvedWhitespace,
                resolvedInline, resolvedPassThrough);
            rangesAttribute.Add(new InteractiveRange(in context, primaryValue, resolvedChannel,
                in settings));
        }

        /// <summary>Finds the first owned range containing the rendered cluster.</summary>
        public virtual bool TryGetRange(int cluster, out InteractiveRange range)
        {
            if (rangesAttribute != null)
            {
                for (var i = 0; i < rangesAttribute.Count; i++)
                {
                    if (!rangesAttribute[i].Contains(cluster)) continue;
                    range = rangesAttribute[i];
                    return true;
                }
            }
            range = default;
            return false;
        }

        /// <summary>Whether the entity currently participates in focus, hit testing and events.</summary>
        protected virtual bool IsRangeEnabled(in InteractiveRange range) => true;

        /// <summary>Current aggregate state across all pointer leases for this range.</summary>
        public RangeState GetRangeState(in InteractiveRange range)
            => uniText == null
                ? RangeState.Normal
                : UniTextInteractions.For(uniText).GetState(this, in range);

        /// <summary>Reconciles modifier-owned entity state after the router rebuilds final geometry.</summary>
        protected virtual void HandleRangesRebuilt() { }
        /// <summary>Receives every routed target event after public handlers.</summary>
        protected virtual void HandleInteraction(RangeInteraction interaction) { }
        /// <summary>
        /// Performs built-in behavior after all capture, channel, modifier and bubble handlers.
        /// It is skipped when a handler calls <see cref="RangeInteraction.PreventDefault"/>.
        /// </summary>
        protected virtual void HandleDefaultAction(RangeInteraction interaction) { }

        internal bool RouterIsEnabled(in InteractiveRange range) => IsRangeEnabled(in range);

        internal void RouterRangesRebuilt()
        {
            HandleRangesRebuilt();
            if (uniText != null && UniTextRanges.TryGet(uniText, out var rangesRuntime))
                rangesRuntime.Synchronize(this);
        }

        internal void RouterDispatch(RangeInteraction interaction)
        {
            Interaction?.Invoke(interaction);
            if (!interaction.Handled) HandleInteraction(interaction);
        }

        internal void RouterDefaultAction(RangeInteraction interaction)
        {
            HandleDefaultAction(interaction);
            if (interaction.DefaultPrevented || actions == null) return;
            var context = new RangeActionContext(uniText, interaction);
            for (var i = 0; i < actions.Length; i++)
            {
                var action = actions[i] ?? throw new InvalidOperationException(
                    $"InteractiveModifier action entry {i} is null.");
                if (action.TryExecute(in context) == RangeActionFlow.Stop) return;
            }
        }

        internal void RouterRefreshCursor(bool engaged)
        {
            if (hoverCursor == CursorType.Default)
            {
                UniTextCursor.Release(this);
                return;
            }
            if (engaged) UniTextCursor.Claim(this, hoverCursor);
            else UniTextCursor.Release(this);
        }

        private void OnDisabledCommit()
        {
            if (IsInitialized) return;
            rangesAttribute?.buffer.FakeClear();
            RouterRangesRebuilt();
        }

        private void BindRuleChanges()
        {
            if (subscribedRules == rules) return;
            UnbindRuleChanges();
            subscribedRules = rules;
            if (subscribedRules == null) return;
            for (var i = 0; i < subscribedRules.Length; i++)
                if (subscribedRules[i] is IRangeConfigurationSource source)
                    source.AddConfigurationListener(OnRuleConfigurationChanged);
        }

        private void UnbindRuleChanges()
        {
            if (subscribedRules == null) return;
            for (var i = 0; i < subscribedRules.Length; i++)
                if (subscribedRules[i] is IRangeConfigurationSource source)
                    source.RemoveConfigurationListener(OnRuleConfigurationChanged);
            subscribedRules = null;
        }

        private void OnRuleConfigurationChanged()
            => ApplyRulesChange();
    }
}
