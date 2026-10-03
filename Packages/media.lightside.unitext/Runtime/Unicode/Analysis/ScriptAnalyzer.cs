using System;

namespace LightSide
{
    /// <summary>
    /// Implements Unicode Standard Annex #24 (UAX #24) script property analysis.
    /// </summary>
    /// <remarks>
    /// Assigns a script (Latin, Arabic, Han, etc.) to each codepoint in the text.
    /// Resolves inherited and common scripts by propagating from neighboring characters.
    /// Script information is used to select appropriate fonts and shaping engines.
    ///
    /// Passes 100% of Unicode conformance tests.
    /// </remarks>
    /// <seealso cref="UnicodeScript"/>
    internal sealed class ScriptAnalyzer
    {
        private readonly UnicodeDataProvider dataProvider;

        public ScriptAnalyzer(UnicodeDataProvider dataProvider)
        {
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        }

        public ScriptAnalyzer()
        {
            dataProvider = UnicodeData.Provider;
        }

        /// <summary>
        /// True for scripts whose words can be split on whitespace and cached per-word. Cursive/reordering
        /// shaping (Arabic, Hebrew, Devanagari…) is INTRA-word — U+0020 breaks the join, so a whitespace word
        /// shaped alone equals it in context; the only residual risk is a font whose space glyph itself
        /// participates in shaping (space kerning, contextual forms), which the caller's per-word safe-to-break
        /// check catches — those boundaries come back unsafe, so such words are simply not cached. RTL scripts are
        /// cacheable (the caller emits their words in visual order). Excluded: <see cref="IsSpacelessComplex"/>
        /// South-East Asian scripts (no whitespace word boundaries) and the Common/Inherited/Unknown pseudo-scripts.
        /// CJK is cacheable but only ever forms a single whole-run token (no spaces); per-character caching is a
        /// future refinement.
        /// </summary>
        internal static bool IsWordCacheable(UnicodeScript script)
            => script != UnicodeScript.Unknown
               && script != UnicodeScript.Common
               && script != UnicodeScript.Inherited
               && !IsSpacelessComplex(script);

        /// <summary>
        /// Dictionary-segmented South-East Asian scripts that do not use whitespace to separate words, so a
        /// whitespace tokenizer cannot form reusable word units for them.
        /// </summary>
        internal static bool IsSpacelessComplex(UnicodeScript script)
        {
            switch (script)
            {
                case UnicodeScript.Thai:
                case UnicodeScript.Lao:
                case UnicodeScript.Khmer:
                case UnicodeScript.Myanmar:
                case UnicodeScript.Tibetan:
                case UnicodeScript.NewTaiLue:
                case UnicodeScript.TaiViet:
                    return true;
                default:
                    return false;
            }
        }


        /// <summary>Analyzes text and assigns scripts to each codepoint.</summary>
        /// <param name="codepoints">Input codepoints to analyze.</param>
        /// <param name="result">Output buffer to receive script assignments (must be at least codepoints.Length).</param>
        /// <remarks>
        /// Classification and Common/Inherited resolution run through <see cref="UniTextScriptBurst"/>.
        /// </remarks>
        public unsafe void Analyze(ReadOnlySpan<int> codepoints, UnicodeScript[] result)
        {
            if (result.Length < codepoints.Length)
                throw new ArgumentException("Result buffer too small", nameof(result));

            var length = codepoints.Length;
            if (length == 0) return;

            fixed (int* cp = codepoints)
            fixed (UnicodeScript* rp = result)
                UniTextScriptBurst.Resolve(cp, length, dataProvider.BmpScriptPtr,
                    dataProvider.ScriptRangesPtr, dataProvider.ScriptRangesLength, (byte*)rp);
        }
    }
}
