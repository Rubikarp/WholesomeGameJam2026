using System;

namespace LightSide
{
    /// <summary>
    /// Implements Unicode Standard Annex #14 (UAX #14) line breaking algorithm.
    /// </summary>
    /// <remarks>
    /// Determines valid line break opportunities in text according to Unicode rules.
    /// Handles complex cases including CJK characters, punctuation, spaces, and various
    /// script-specific rules.
    ///
    /// Passes 100% of Unicode conformance tests.
    ///
    /// Bulk analysis runs through <see cref="UniTextLineBreakBurst"/>. Per-position queries share its rule core.
    /// </remarks>
    /// <seealso cref="LineBreaker"/>
    /// <seealso cref="GraphemeBreaker"/>
    internal sealed unsafe class LineBreakAlgorithm
    {
        private readonly UnicodeDataProvider dataProvider;

        public LineBreakAlgorithm(UnicodeDataProvider dataProvider)
        {
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        }

        public LineBreakAlgorithm()
        {
            dataProvider = UnicodeData.Provider;
        }

        /// <summary>Computes break opportunities for the given codepoints.</summary>
        /// <param name="codePoints">Input codepoints to analyze.</param>
        /// <param name="breaks">Output buffer for break types (must be at least codePoints.Length + 1).</param>
        public void GetBreakOpportunities(ReadOnlySpan<int> codePoints, Span<LineBreakType> breaks)
        {
            var length = codePoints.Length;

            if (breaks.Length < length + 1)
                throw new ArgumentException($"breaks array must have length at least {length + 1}");

            if (length == 0)
            {
                breaks[0] = LineBreakType.Mandatory;
                return;
            }

            fixed (int* cp = codePoints)
            fixed (LineBreakType* bp = breaks)
                UniTextLineBreakBurst.Resolve(cp, length,
                    dataProvider.BmpLineBreakPtr, dataProvider.LineBreakRangesPtr, dataProvider.LineBreakRangesLength,
                    dataProvider.BmpGeneralCategoryPtr, dataProvider.GeneralCategoryRangesPtr, dataProvider.GeneralCategoryRangesLength,
                    dataProvider.BmpEastAsianWidthPtr, dataProvider.EastAsianWidthRangesPtr, dataProvider.EastAsianWidthRangesLength,
                    dataProvider.BmpExtendedPictographicPtr, dataProvider.ExtendedPictographicRangesPtr,
                    dataProvider.ExtendedPictographicRangesLength,
                    dataProvider.BmpScriptPtr, dataProvider.ScriptRangesPtr, dataProvider.ScriptRangesLength,
                    (byte*)bp);
        }

        /// <summary>Computes break opportunities, allocating a new result array.</summary>
        /// <param name="codePoints">Input codepoints to analyze.</param>
        /// <returns>Array of break types with length codePoints.Length + 1.</returns>
        public LineBreakType[] GetBreakOpportunities(ReadOnlySpan<int> codePoints)
        {
            var breaks = new LineBreakType[codePoints.Length + 1];
            GetBreakOpportunities(codePoints, breaks);
            return breaks;
        }

        /// <summary>Checks if a line break is allowed at a specific position.</summary>
        /// <param name="codePoints">Input codepoints to analyze.</param>
        /// <param name="index">Position to check (0 = before first character).</param>
        /// <returns>The break type at this position.</returns>
        public LineBreakType GetBreakTypeAt(ReadOnlySpan<int> codePoints, int index)
        {
            if (index <= 0) return LineBreakType.None;
            if (index >= codePoints.Length) return LineBreakType.Mandatory;
            return GetBreakType(codePoints, index - 1);
        }

        /// <summary>Checks if a line break is allowed at a specific position (legacy bool API).</summary>
        public bool CanBreakAt(ReadOnlySpan<int> codePoints, int index)
        {
            return GetBreakTypeAt(codePoints, index) != LineBreakType.None;
        }

        private LineBreakType GetBreakType(ReadOnlySpan<int> codePoints, int index)
        {
            var t = MakeTables();
            fixed (int* cp = codePoints)
            {
                var beforeRaw = UniTextLineBreakBurst.GetLineBreakClass(in t, cp[index]);
                var afterRaw = UniTextLineBreakBurst.GetLineBreakClass(in t, cp[index + 1]);
                return UniTextLineBreakBurst.GetBreakTypeCore(in t, cp, codePoints.Length, index, beforeRaw, afterRaw);
            }
        }

        /// <summary>Binds the provider's cached read-only table pointers into the kernel's <see cref="UniTextLineBreakBurst.Tables"/> so the shared rule methods run over the same memory the Burst path reads.</summary>
        private UniTextLineBreakBurst.Tables MakeTables()
        {
            return new UniTextLineBreakBurst.Tables
            {
                bmpLb = dataProvider.BmpLineBreakPtr, lbRanges = dataProvider.LineBreakRangesPtr, lbLen = dataProvider.LineBreakRangesLength,
                bmpGc = dataProvider.BmpGeneralCategoryPtr, gcRanges = dataProvider.GeneralCategoryRangesPtr, gcLen = dataProvider.GeneralCategoryRangesLength,
                bmpEaw = dataProvider.BmpEastAsianWidthPtr, eawRanges = dataProvider.EastAsianWidthRangesPtr, eawLen = dataProvider.EastAsianWidthRangesLength,
                bmpExtPict = dataProvider.BmpExtendedPictographicPtr, extPictRanges = dataProvider.ExtendedPictographicRangesPtr, extPictLen = dataProvider.ExtendedPictographicRangesLength,
                bmpScript = dataProvider.BmpScriptPtr, scriptRanges = dataProvider.ScriptRangesPtr, scriptLen = dataProvider.ScriptRangesLength
            };
        }
    }
}
