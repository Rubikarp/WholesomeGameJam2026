using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightSide
{
    /// <summary>Links a range source with the modifier graph applied to its entities.</summary>
    [Serializable]
    public sealed partial class Style : IModifierChangeSink, IRangeSourceChangeSink
    {
        /// <summary>Gets or sets the modifier, with proper lifecycle management on hot-swap.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyModifierChange),
            Validator = nameof(ValidateModifier), Owned = true)]
        private BaseModifier modifier;

        /// <summary>Gets or sets the range producer. Null means one implicit whole-text entity.</summary>
        [FormerlySerializedAs("rule")]
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplySourceChange),
            Validator = nameof(ValidateSource), Owned = true)]
        private RangeSource source;

        /// <summary>
        /// Source-specific default used by the implicit whole-text entity when no source exists.
        /// </summary>
        [SerializeField, DefaultParameter, StateProperty(nameof(ApplyDefaultParameterChange))]
        private string defaultParameter;

        /// <summary>Stored inverted so the default (and any pre-existing serialized style that lacks the field) reads as enabled.</summary>
        [SerializeField, StateField(nameof(ApplyDisabledChange))]
        private bool disabled;

        [NonSerialized] private WholeTextRangeSource wholeTextSource;
        [NonSerialized] private IStyleChangeSink changeSink;

        /// <summary>
        /// Whether this style takes part in text processing. A disabled style is skipped entirely —
        /// not registered with the parser, never applied or rendered, and ignored by style queries —
        /// as if removed, while its configuration is preserved. Re-enabling restores its authored
        /// position and precedence.
        /// </summary>
        public bool Enabled
        {
            get => !disabled;
            set => SetDisabledState(!value);
        }

        internal IStyleChangeSink ChangeSink => changeSink;

        internal RangeSource EffectiveSource => source ?? (wholeTextSource ??=
            new WholeTextRangeSource($"whole:{GetHashCode():X8}"));

        internal bool IsValid => modifier != null || (source is ParseRule { IsStandalone: true });

        internal void SetChangeSink(IStyleChangeSink sink)
        {
            if (ReferenceEquals(changeSink, sink)) return;
            if (sink != null)
            {
                BaseModifier.ValidateGraph(modifier);
                RangeSource.ValidateGraph(source);
            }
            modifier?.SetChangeSink(null);
            DetachSource(source);
            changeSink = sink;
            if (sink == null) return;
            modifier?.SetChangeSink(this);
            AttachSource(source, true);
        }

        private void ApplyModifierChange(StateMember member, BaseModifier previous,
            ref BaseModifier current)
        {
            if (ReferenceEquals(previous, current)) return;
            if (ReferenceEquals(previous?.ChangeSink, this)) previous.SetChangeSink(null);
            current?.SetChangeSink(changeSink == null ? null : this);
            PublishChange(current, stateSource: this, member: member, structural: true);
        }

        private void ApplySourceChange(StateMember member, RangeSource previous,
            ref RangeSource current)
        {
            if (ReferenceEquals(previous, current)) return;
            DetachSource(previous);
            AttachSource(current, changeSink != null);
            PublishChange(rangeSource: current, stateSource: this, member: member,
                structural: true);
        }

        private void ValidateModifier(BaseModifier candidate)
            => BaseModifier.ValidateGraph(candidate);

        private void ValidateSource(RangeSource candidate)
            => RangeSource.ValidateGraph(candidate);

        private void AttachSource(RangeSource value, bool notify)
        {
            if (notify) value?.SetChangeSink(this);
        }

        private void DetachSource(RangeSource value)
        {
            if (value == null) return;
            if (ReferenceEquals(value.ChangeSink, this)) value.SetChangeSink(null);
        }

        private void ApplyDefaultParameterChange(StateMember member, string previous,
            string current)
        {
            if (string.Equals(previous, current, StringComparison.Ordinal)) return;
            PublishChange(stateSource: this, member: member);
        }

        private void ApplyDisabledChange(StateMember member, bool previous, bool current)
        {
            if (previous == current) return;
            PublishChange(stateSource: this, member: member, structural: true);
        }

        void IModifierChangeSink.MarkModifierChanged(BaseModifier changedModifier,
            UniTextDirty flags, IStateMemberReplay stateSource, StateMember member,
            bool structural)
        {
            PublishChange(changedModifier, flags: flags, stateSource: stateSource,
                member: member, structural: structural);
        }

        void IRangeSourceChangeSink.MarkRangeSourceChanged(RangeSource changedSource)
        {
            PublishChange(rangeSource: changedSource, structural: true);
        }

        private void PublishChange(BaseModifier changedModifier = null,
            RangeSource rangeSource = null,
            UniTextDirty flags = UniTextDirty.Text, IStateMemberReplay stateSource = null,
            StateMember member = default, bool structural = false)
        {
            if (changeSink == null) return;
            var change = new StyleChange(this, changedModifier, rangeSource,
                flags, stateSource, member, structural);
            changeSink.MarkStyleChanged(in change);
        }

        internal static void ValidateCollectionGraphs(in StateListMutation<Style> styles)
        {
            var modifiers = new List<BaseModifier>();
            var sources = new List<RangeSource>();
            for (var styleIndex = 0; styleIndex < styles.Count; styleIndex++)
            {
                var style = styles[styleIndex];
                var previousModifierCount = modifiers.Count;
                BaseModifier.CollectGraphNodes(style.Modifier, modifiers, ownedOnly: true);
                for (var i = previousModifierCount; i < modifiers.Count; i++)
                {
                    var candidate = modifiers[i];
                    for (var j = 0; j < previousModifierCount; j++)
                        if (ReferenceEquals(modifiers[j], candidate) || modifiers[j].NodeId == candidate.NodeId)
                            throw new InvalidOperationException(
                                "A style collection cannot share modifier nodes or modifier node identities between styles.");
                }

                var previousSourceCount = sources.Count;
                RangeSource.CollectGraphNodes(style.Source, sources);
                for (var i = previousSourceCount; i < sources.Count; i++)
                    for (var j = 0; j < previousSourceCount; j++)
                        if (ReferenceEquals(sources[j], sources[i]))
                            throw new InvalidOperationException(
                                "A style collection cannot share range-source nodes between styles.");
            }
        }

        /// <summary>
        /// Builds a style that applies <paramref name="modifier"/> to the entire text. No
        /// <see cref="Source"/> is created — the absence of a source is interpreted as "no
        /// constraint, apply everywhere", and <paramref name="parameter"/> is forwarded to the
        /// modifier on every cycle via <see cref="defaultParameter"/>.
        /// </summary>
        /// <param name="modifier">The modifier to apply. Must not be null.</param>
        /// <param name="parameter">Optional parameter forwarded to the modifier's <c>OnApply</c>.</param>
        public static Style WholeText(BaseModifier modifier, string parameter = null)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));

            return new Style
            {
                Modifier = modifier,
                DefaultParameter = parameter,
            };
        }

        /// <summary>
        /// Builds a style that applies <paramref name="modifier"/> to a fixed codepoint range
        /// via a <see cref="FixedRangeSource"/>.
        /// </summary>
        /// <param name="modifier">The modifier to apply. Must not be null.</param>
        /// <param name="start">Inclusive start codepoint index.</param>
        /// <param name="end">Exclusive end codepoint index.</param>
        /// <param name="parameter">Optional parameter forwarded to the modifier's <c>OnApply</c>.</param>
        public static Style Range(BaseModifier modifier, int start, int end, string parameter = null)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));

            return new Style
            {
                Modifier = modifier,
                Source = new FixedRangeSource(start, end, parameter)
            };
        }

        /// <summary>Builds a style that applies a modifier graph to entities emitted by a range source.</summary>
        /// <param name="source">Source that owns entity identity, geometry and lifetime.</param>
        /// <param name="modifier">Modifier graph applied once per emitted segment.</param>
        public static Style FromSource(RangeSource source, BaseModifier modifier)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            return new Style { Source = source, Modifier = modifier };
        }

        /// <summary>
        /// Builds a style that activates <paramref name="modifier"/> on ranges matched by a
        /// rich-text tag <c>&lt;tagName&gt;...&lt;/tagName&gt;</c>.
        /// </summary>
        /// <param name="modifier">The modifier to apply. Must not be null.</param>
        /// <param name="tagName">Tag name without angle brackets (e.g. <c>"color"</c>, <c>"lang"</c>).</param>
        /// <param name="defaultParameter">
        /// Optional default parameter used when the tag is written without <c>=value</c>.
        /// </param>
        public static Style Tag(BaseModifier modifier, string tagName, string defaultParameter = null)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (string.IsNullOrEmpty(tagName)) throw new ArgumentException("tagName must be non-empty", nameof(tagName));

            return new Style
            {
                Modifier = modifier,
                Source = new TagRule(tagName) { DefaultParameter = defaultParameter }
            };
        }
    }

}
