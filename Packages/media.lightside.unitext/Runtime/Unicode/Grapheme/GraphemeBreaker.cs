using System;

namespace LightSide
{
    /// <summary>
    /// Implements Unicode Standard Annex #29 (UAX #29) grapheme cluster boundary detection.
    /// </summary>
    /// <remarks>
    /// A grapheme cluster is a user-perceived character which may consist of multiple codepoints
    /// (e.g., base character + combining marks, emoji sequences). This class determines where
    /// text can be safely segmented for cursor movement, selection, and editing.
    ///
    /// Boundary passes run through <see cref="UniTextGraphemeBurst"/>.
    ///
    /// Passes 100% of Unicode conformance tests.
    /// </remarks>
    /// <seealso cref="LineBreakAlgorithm"/>
    internal sealed unsafe class GraphemeBreaker
    {
        private readonly UnicodeDataProvider dataProvider;

        public GraphemeBreaker(UnicodeDataProvider dataProvider)
        {
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        }

        /// <summary>Computes grapheme cluster boundaries for the given codepoints.</summary>
        /// <param name="codePoints">Input codepoints to analyze.</param>
        /// <param name="breaks">Output buffer for boundary flags (must be at least codePoints.Length + 1).</param>
        public void GetBreakOpportunities(ReadOnlySpan<int> codePoints, Span<bool> breaks)
        {
            var length = codePoints.Length;

            if (breaks.Length < length + 1)
                throw new ArgumentException($"breaks array must have length at least {length + 1}");

            if (length == 0)
            {
                breaks[0] = true;
                return;
            }

            var scratch = ArrayPool<byte>.Rent(length);
            fixed (int* cp = codePoints)
            fixed (bool* bp = breaks)
            fixed (byte* gs = scratch)
                UniTextGraphemeBurst.Resolve(cp, length,
                    dataProvider.BmpGraphemeBreakPtr, dataProvider.GraphemeBreakRangesPtr, dataProvider.GraphemeBreakRangesLength,
                    dataProvider.BmpIndicConjunctBreakPtr, dataProvider.IndicConjunctBreakRangesPtr, dataProvider.IndicConjunctBreakRangesLength,
                    dataProvider.BmpExtendedPictographicPtr, dataProvider.ExtendedPictographicRangesPtr, dataProvider.ExtendedPictographicRangesLength,
                    gs, (byte*)bp);
            ArrayPool<byte>.Return(scratch);
        }

        /// <summary>Computes grapheme cluster boundaries, allocating a new result array.</summary>
        /// <param name="codePoints">Input codepoints to analyze.</param>
        /// <returns>Array of boundary flags with length codePoints.Length + 1.</returns>
        public bool[] GetBreakOpportunities(ReadOnlySpan<int> codePoints)
        {
            var breaks = new bool[codePoints.Length + 1];
            GetBreakOpportunities(codePoints, breaks);
            return breaks;
        }

        /// <summary>Counts the number of grapheme clusters in the text.</summary>
        /// <param name="codePoints">Input codepoints to analyze.</param>
        /// <returns>Number of user-perceived characters.</returns>
        public int CountGraphemeClusters(ReadOnlySpan<int> codePoints)
        {
            var length = codePoints.Length;
            if (length == 0)
                return 0;

            var breaks = ArrayPool<byte>.Rent(length + 1);
            var scratch = ArrayPool<byte>.Rent(length);
            fixed (int* cp = codePoints)
            fixed (byte* bp = breaks)
            fixed (byte* gs = scratch)
                UniTextGraphemeBurst.Resolve(cp, length,
                    dataProvider.BmpGraphemeBreakPtr, dataProvider.GraphemeBreakRangesPtr, dataProvider.GraphemeBreakRangesLength,
                    dataProvider.BmpIndicConjunctBreakPtr, dataProvider.IndicConjunctBreakRangesPtr, dataProvider.IndicConjunctBreakRangesLength,
                    dataProvider.BmpExtendedPictographicPtr, dataProvider.ExtendedPictographicRangesPtr, dataProvider.ExtendedPictographicRangesLength,
                    gs, bp);

            var count = 0;
            for (var i = 1; i <= length; i++)
                if (breaks[i] != 0)
                    count++;

            ArrayPool<byte>.Return(breaks);
            ArrayPool<byte>.Return(scratch);
            return count;
        }

        /// <summary>
        /// Whether UAX #29 mandates a cluster boundary between <paramref name="a"/> and
        /// <paramref name="b"/> INDEPENDENT of any surrounding context — the "safe point" test
        /// windowed re-segmentation anchors on (<see cref="GraphemeCountCache"/>).
        /// Context-sensitive pairs (RI parity, ZWJ + pictographic, Indic conjunct chains) report
        /// unsafe even when a break might exist, keeping anchors conservative. Rather than re-encode
        /// the pair table, it feeds <see cref="UniTextGraphemeBurst.ShouldBreak"/> the context inputs
        /// that force its three context-sensitive rules into their conservative (non-boundary) branch:
        /// odd RI parity (GB12/13 → keep), <c>emojiPrefix</c> = true (GB11 fires on any ZWJ +
        /// pictographic), and <c>conjunctActive</c> = the local Linker/Extend test (GB9c fires on any
        /// Consonant preceded by Linker/Extend). Every context-free row is then shared verbatim.
        /// </summary>
        public bool IsContextFreeBoundary(int a, int b)
        {
            var icbA = dataProvider.GetIndicConjunctBreak(a);
            return UniTextGraphemeBurst.ShouldBreak(
                dataProvider.GetGraphemeClusterBreak(a),
                dataProvider.GetGraphemeClusterBreak(b),
                riCount: 1,
                dataProvider.GetIndicConjunctBreak(b) == IndicConjunctBreak.Consonant,
                dataProvider.IsExtendedPictographic(b),
                conjunctActive: icbA == IndicConjunctBreak.Linker || icbA == IndicConjunctBreak.Extend,
                emojiPrefix: true);
        }
    }
}
