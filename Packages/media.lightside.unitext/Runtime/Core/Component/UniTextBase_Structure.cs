namespace LightSide
{
    public abstract partial class UniTextBase
    {
        #region Text Structure

        /// <summary>
        /// Codepoint span of the whole processed text — the rendered text after parsing, which is
        /// what every structure and range API addresses.
        /// </summary>
        public TextRange TextSpan => new(0, buffers?.codepoints.count ?? 0);

        /// <summary>
        /// The text's units of <paramref name="unit"/> granularity, in logical order. Enumerating
        /// allocates nothing; nothing is computed until it is.
        /// </summary>
        public TextUnitSequence Units(TextUnit unit) => new(this, unit, TextSpan);

        /// <summary>
        /// The units of <paramref name="unit"/> granularity inside <paramref name="range"/>, in
        /// logical order, each clipped to the range. Out-of-bounds parts of the range are dropped.
        /// </summary>
        public TextUnitSequence Units(TextUnit unit, TextRange range) => new(this, unit, range);

        /// <summary>How many units of <paramref name="unit"/> granularity the text holds.</summary>
        public int CountUnits(TextUnit unit) => Units(unit).Count;

        /// <summary>
        /// How many units of <paramref name="unit"/> granularity <paramref name="range"/> holds. A
        /// unit the range only partly covers counts as one.
        /// </summary>
        public int CountUnits(TextUnit unit, TextRange range) => Units(unit, range).Count;

        /// <summary>
        /// Codepoint span of the range the author anchored with <c>#<paramref name="label"/></c>,
        /// whatever modifier's tag carries it — the whole-text counterpart of a modifier's own
        /// <c>WhereLabel</c> query, for a caller that knows the name but not the owner. The first
        /// such range in text order answers when a name was used more than once.
        /// </summary>
        public bool TryGetLabeled(string label, out TextRange span)
        {
            var parser = AttributeParser;
            if (parser != null) return parser.TryGetLabeledSpan(label, out span);
            span = default;
            return false;
        }

        /// <summary>
        /// The unit of <paramref name="unit"/> granularity holding <paramref name="codepointIndex"/>,
        /// or an empty range at that index when the text does not reach it.
        /// </summary>
        public TextRange UnitAt(TextUnit unit, int codepointIndex)
        {
            foreach (var found in Units(unit))
                if (codepointIndex < found.End)
                    return codepointIndex >= found.start ? found : new TextRange(codepointIndex, 0);

            return new TextRange(codepointIndex, 0);
        }

        #endregion
    }
}
