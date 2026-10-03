using System;

namespace LightSide
{
    /// <summary>
    /// One materialized range of a modifier: the working unit whose parameters resolve through the
    /// cascade and can be taken into ownership.
    /// </summary>
    /// <remarks>
    /// Obtained from <see cref="BaseModifier.GetRanges"/>. The range stays addressable across text
    /// edits for as long as the parser preserves its <see cref="Identity"/>; an edit inside the
    /// range retires the identity, and ownership taken on it is released.
    /// </remarks>
    public readonly struct ModifierRange
    {
        /// <summary>The modifier whose application produced this range.</summary>
        public BaseModifier Modifier { get; }

        /// <summary>Stable source-scoped identity of the entity.</summary>
        public RangeIdentity Identity { get; }

        /// <summary>The concrete segment of the entity this range covers.</summary>
        public RangeSegmentId Segment { get; }

        /// <summary>Covered codepoint range in rendered text space.</summary>
        public TextRange Range { get; }

        /// <summary>Opaque semantic value emitted by the source, or null.</summary>
        public string PrimaryValue { get; }

        /// <summary>The <c>#label</c> anchor authored on the range's tag, or null.</summary>
        public string Label => Parameters.Label;

        /// <summary>Optional project asset used for semantic routing.</summary>
        public RangeChannel Channel { get; }

        /// <summary>Whether this instance addresses a parsed range.</summary>
        public bool IsValid => Modifier != null && Identity.IsValid && Segment.IsValid;

        internal ModifierParameters Parameters { get; }

        internal ModifierRange(BaseModifier modifier, RangeIdentity identity, RangeSegmentId segment,
            TextRange range, string primaryValue, RangeChannel channel,
            in ModifierParameters parameters)
        {
            Modifier = modifier;
            Identity = identity;
            Segment = segment;
            Range = range;
            PrimaryValue = primaryValue;
            Channel = channel;
            Parameters = parameters;
        }

        /// <summary>
        /// This range's units of <paramref name="unit"/> granularity, in logical order, each clipped
        /// to <see cref="Range"/>. Enumerating allocates nothing.
        /// </summary>
        /// <exception cref="InvalidOperationException">The range's modifier is not attached to a text component.</exception>
        public TextUnitSequence Units(TextUnit unit)
        {
            var owner = Modifier?.Owner ?? throw new InvalidOperationException(
                "The range's modifier is not attached to a text component.");
            return owner.Units(unit, Range);
        }

        /// <summary>
        /// How many units of <paramref name="unit"/> granularity this range holds. A unit the range
        /// only partly covers counts as one.
        /// </summary>
        /// <exception cref="InvalidOperationException">The range's modifier is not attached to a text component.</exception>
        public int CountUnits(TextUnit unit) => Units(unit).Count;

        /// <summary>
        /// Takes one parameter of this range into ownership. The owned value composes on the
        /// cascade result under <paramref name="composition"/> until the handle is released or the
        /// range's identity retires.
        /// </summary>
        public OwnedParameter<TValue> Own<TModifier, TValue>(
            ParameterDescriptor<TModifier, TValue> parameter,
            ParameterComposition composition = ParameterComposition.Replace, int priority = 0)
            where TModifier : BaseModifier
        {
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            if (!IsValid) throw new InvalidOperationException("The range is not valid.");
            if (Modifier is not TModifier typed)
                throw new ArgumentException(
                    $"The range belongs to {Modifier.GetType().Name}, not {typeof(TModifier).Name}.",
                    nameof(parameter));
            var owner = Modifier.Owner ?? throw new InvalidOperationException(
                "The range's modifier is not attached to a text component.");
            return UniTextRanges.For(owner)
                .Own(typed, parameter, Identity, Segment, composition, priority);
        }

        /// <summary>Resolves one parameter of this range: cascade plus owned values.</summary>
        public TValue Resolve<TModifier, TValue>(ParameterDescriptor<TModifier, TValue> parameter)
            where TModifier : BaseModifier
        {
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            if (!IsValid) throw new InvalidOperationException("The range is not valid.");
            if (Modifier is not TModifier typed)
                throw new ArgumentException(
                    $"The range belongs to {Modifier.GetType().Name}, not {typeof(TModifier).Name}.",
                    nameof(parameter));
            return parameter.ApplyOwned(typed, Identity, Segment,
                parameter.CascadeFrom(typed, Parameters));
        }

        /// <inheritdoc/>
        public override string ToString()
            => IsValid ? $"{Modifier.GetType().Name} [{Range.start}..{Range.End})" : "Invalid";
    }
}
