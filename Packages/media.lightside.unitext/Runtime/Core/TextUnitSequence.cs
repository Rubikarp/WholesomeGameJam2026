using System;

namespace LightSide
{
    /// <summary>
    /// The units of one stretch of processed text, in logical order. Enumerating allocates nothing
    /// and reads the pass's own analysis, so a query costs one walk of the range and no text work.
    /// </summary>
    /// <remarks>
    /// A snapshot of nothing: the sequence holds the component, not its buffers, and resolves them
    /// on <see cref="GetEnumerator"/>. Enumerate it inside the frame you obtained it in — a rebuild
    /// between the two gives the next enumeration the new text, and a rebuild during one is a
    /// programming error the enumerator does not guard against.
    /// </remarks>
    public readonly struct TextUnitSequence
    {
        private readonly UniTextBase text;
        private readonly TextRange range;
        private readonly TextUnit unit;

        internal TextUnitSequence(UniTextBase text, TextUnit unit, TextRange range)
        {
            this.text = text;
            this.unit = unit;
            this.range = range;
        }

        /// <summary>Number of units the range holds; a partial unit at either edge counts as one.</summary>
        public int Count
        {
            get
            {
                var total = 0;
                var walk = GetEnumerator();
                while (walk.MoveNext()) total++;
                return total;
            }
        }

        /// <summary>Returns a struct enumerator over the range's units.</summary>
        public TextUnitEnumerator GetEnumerator() => new(text, unit, range);
    }

    /// <summary>
    /// Walks the units of a range, yielding each one's codepoint span. Clipped to the range: the
    /// first and last unit carry only the part the range covers.
    /// </summary>
    public ref struct TextUnitEnumerator
    {
        private readonly ReadOnlySpan<int> codepoints;
        private readonly ReadOnlySpan<bool> graphemeBreaks;
        private readonly int start;
        private readonly int end;
        private TextUnitWalk units;
        private int cursor;
        private int unitStart;
        private TextRange current;

        internal TextUnitEnumerator(UniTextBase text, TextUnit unit, TextRange range)
        {
            var buffers = text != null ? text.Buffers : null;
            var count = buffers?.codepoints.count ?? 0;

            start = Math.Max(0, range.start);
            end = Math.Min(range.End, count);
            if (end < start) end = start;

            if (count == 0)
            {
                codepoints = default;
                graphemeBreaks = default;
                units = default;
                cursor = 0;
                unitStart = -1;
                current = default;
                return;
            }

            var length = end - start;
            codepoints = buffers.codepoints.data.AsSpan(start, length);
            var breaks = buffers.GraphemeBreaksOrEmpty;
            graphemeBreaks = breaks.Length >= end ? breaks.Slice(start, length) : default;

            var words = buffers.WordBoundariesOrEmpty;
            units = new TextUnitWalk(unit, codepoints,
                words.Length >= end ? words.Slice(start, length) : default,
                buffers.lines.Span, start);

            cursor = 0;
            unitStart = -1;
            current = default;
        }

        /// <summary>Codepoint span of the unit the walk stands on.</summary>
        public TextRange Current => current;

        /// <summary>Advances to the next unit; false once the range is spent.</summary>
        public bool MoveNext()
        {
            while (cursor < codepoints.Length)
            {
                var index = cursor++;
                if (index != 0 && graphemeBreaks.Length != 0 && !graphemeBreaks[index]) continue;
                if (!units.Starts(index)) continue;

                if (unitStart < 0)
                {
                    unitStart = index;
                    continue;
                }

                current = new TextRange(start + unitStart, index - unitStart);
                unitStart = index;
                return true;
            }

            if (unitStart < 0) return false;

            current = new TextRange(start + unitStart, codepoints.Length - unitStart);
            unitStart = -1;
            return true;
        }
    }
}
