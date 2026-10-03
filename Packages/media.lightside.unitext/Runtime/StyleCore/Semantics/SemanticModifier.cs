using System;
using UnityEngine;

namespace LightSide
{
    internal struct SemanticApplication
    {
        public RangeIdentity identity;
        public RangeSegment segment;
        public RangeChannel channel;
        public object payload;
        public TextRevision revision;
        public TextSemanticRole role;
        public string label;
        public string value;
        public string hint;
        public string language;
        public TextSemanticStates states;
        public TextSemanticActions actions;
        public int priority;
        public bool deriveLabel;
    }

    /// <summary>
    /// Publishes platform-neutral meaning, spoken text, states, actions and final bounds for range
    /// entities. Native accessibility integrations consume <see cref="UniTextSemantics.For"/>
    /// instead of being named or invoked by this modifier.
    /// </summary>
    [Serializable]
    [TypeGroup("Semantics", 4)]
    [TypeDescription("Adds accessibility, TTS and semantic-navigation data to ranges.")]
    [GenerateParameters]
    public partial class SemanticModifier : BaseModifier, IModifierCommitChanges
    {
        UniTextCommitChanges IModifierCommitChanges.CommitChanges
            => UniTextCommitChanges.Content;

        /// <summary>Accessible name; empty may derive from covered text.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        [Tooltip("Accessible name. Empty derives the covered rendered text when enabled below.")]
        private string label;

        /// <summary>Current represented value.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        [Tooltip("Current semantic value, such as a mention identifier rendered differently from its name.")]
        private string value;

        /// <summary>Additional usage guidance.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        [Tooltip("Additional usage guidance announced after the name and value.")]
        private string hint;

        /// <summary>BCP 47 language tag, or empty to inherit.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        [Tooltip("BCP 47 language tag for pronunciation. Empty inherits the surrounding language.")]
        private string language;

        /// <summary>Platform-neutral meaning of the entity.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        private TextSemanticRole role = TextSemanticRole.Text;

        /// <summary>Authored semantic states.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        private TextSemanticStates states;

        /// <summary>Actions exposed to accessibility and project adapters.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        private TextSemanticActions actions;

        /// <summary>Explicit conflict priority for duplicate semantic descriptions.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        [Tooltip("Wins when more than one semantic modifier describes the same entity at one Style order.")]
        private int priority;

        /// <summary>Whether empty labels derive from covered rendered text.</summary>
        [SerializeField, StateProperty(nameof(MarkMeshDirty))]
        [Tooltip("Use the entity's covered rendered text when Label resolves empty.")]
        private bool deriveLabelFromText = true;

        /// <summary>Explicit semantic channel, or null to inherit the source channel.</summary>
        [SerializeField, StateProperty(nameof(MarkMeshDirty),
            Validator = nameof(ValidateRangeChannel))]
        [Tooltip("Stable semantic channel. When unset, the source channel is retained.")]
        private RangeChannel channel;

        /// <summary>Optional payload member used as the accessible name.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyBindingChange))]
        [Tooltip("Optional named payload binding that replaces Label after parameter resolution.")]
        private RangePayloadStringBinding labelBinding;

        /// <summary>Optional payload member used as the represented value.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyBindingChange))]
        [Tooltip("Optional named payload binding that replaces Value after parameter resolution.")]
        private RangePayloadStringBinding valueBinding;

        /// <summary>Optional payload member used as additional guidance.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyBindingChange))]
        [Tooltip("Optional named payload binding that replaces Hint after parameter resolution.")]
        private RangePayloadStringBinding hintBinding;

        /// <summary>Optional payload member used as the BCP 47 language tag.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyBindingChange))]
        [Tooltip("Optional named payload binding that replaces Language after parameter resolution.")]
        private RangePayloadStringBinding languageBinding;

        [NonSerialized] private PooledBuffer<SemanticApplication> applications;
        [NonSerialized] private bool bindingsBound;

        internal ReadOnlySpan<SemanticApplication> Applications => applications.Span;

        protected override void OnEnable()
        {
            BindBindings();
            UniTextSemantics.For(uniText).Register(this);
        }

        protected internal override void PrepareForParallel()
            => BindBindings();

        protected override void OnDisable()
        {
            UnbindBindings();
            UniTextSemantics.For(uniText).Unregister(this);
        }

        protected override void OnDestroy()
        {
            if (uniText != null && UniTextSemantics.TryGet(uniText, out var semantics))
                semantics.Unregister(this);
            applications.Return();
        }

        protected override void BeforeApply()
        {
            applications.FakeClear();
            if (uniText != null && UniTextSemantics.TryGet(uniText, out var semantics))
                semantics.MarkApplicationsDirty();
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var resolvedLabel = Param.Label.Resolve(this, in context);
            var resolvedValue = Param.Value.Resolve(this, in context);
            var resolvedHint = Param.Hint.Resolve(this, in context);
            var resolvedLanguage = Param.Language.Resolve(this, in context);
            var resolvedRole = Param.Role.Resolve(this, in context);
            var resolvedStates = Param.States.Resolve(this, in context);
            var resolvedActions = Param.Actions.Resolve(this, in context);
            var resolvedPriority = Param.Priority.Resolve(this, in context);

            if (labelBinding != null) resolvedLabel = labelBinding.Resolve(context.Payload);
            if (valueBinding != null) resolvedValue = valueBinding.Resolve(context.Payload);
            if (hintBinding != null) resolvedHint = hintBinding.Resolve(context.Payload);
            if (languageBinding != null) resolvedLanguage = languageBinding.Resolve(context.Payload);

            var resolvedChannel = channel != null ? channel : context.Channel;
            resolvedChannel?.ValidatePayload(context.Payload.Value);
            applications.Add(new SemanticApplication
            {
                identity = context.Identity,
                segment = context.Segment,
                channel = resolvedChannel,
                payload = context.Payload.Value,
                revision = context.Revision,
                role = resolvedRole,
                label = resolvedLabel,
                value = resolvedValue,
                hint = resolvedHint,
                language = resolvedLanguage,
                states = resolvedStates,
                actions = resolvedActions,
                priority = resolvedPriority,
                deriveLabel = deriveLabelFromText && string.IsNullOrEmpty(resolvedLabel),
            });
        }

        private void ApplyBindingChange(RangePayloadStringBinding previous, RangePayloadStringBinding current)
        {
            if (IsInitialized)
            {
                UnbindBinding(previous);
                BindBinding(current);
            }
            MarkMeshDirty();
        }

        private void BindBindings()
        {
            if (bindingsBound) return;
            bindingsBound = true;
            BindBinding(labelBinding);
            BindBinding(valueBinding);
            BindBinding(hintBinding);
            BindBinding(languageBinding);
        }

        private void UnbindBindings()
        {
            if (!bindingsBound) return;
            bindingsBound = false;
            UnbindBinding(labelBinding);
            UnbindBinding(valueBinding);
            UnbindBinding(hintBinding);
            UnbindBinding(languageBinding);
        }

        private void BindBinding(RangePayloadStringBinding binding)
            => (binding as IRangeConfigurationSource)?.AddConfigurationListener(OnBindingConfigurationChanged);

        private void UnbindBinding(RangePayloadStringBinding binding)
            => (binding as IRangeConfigurationSource)?.RemoveConfigurationListener(OnBindingConfigurationChanged);

        private void OnBindingConfigurationChanged() => MarkMeshDirty();
    }
}
