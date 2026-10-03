using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Applies registered dictionary tailoring to default UAX #29 word boundaries and,
    /// for complex-context scripts, UAX #14 line-break opportunities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scans contiguous runs handled by the same segmenter and dispatches them to the
    /// registered <see cref="IWordSegmenter"/>.
    /// </para>
    /// <para>
    /// Call <see cref="Process"/> after default line, grapheme, word, and script analysis.
    /// </para>
    /// </remarks>
    internal sealed class WordSegmentationProcessor
    {
        private readonly IWordSegmenter[] segmenters = new IWordSegmenter[256];
        private int registeredCount;

        /// <summary>Returns true if any segmenters are registered.</summary>
        public bool HasSegmenters
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => registeredCount > 0;
        }

        /// <summary>Registers a segmenter for its target script. Replaces any existing one.</summary>
        public void Register(IWordSegmenter segmenter)
        {
            if (segmenter == null) throw new ArgumentNullException(nameof(segmenter));
            var idx = (int)segmenter.Script;
            if (segmenters[idx] == null) registeredCount++;
            segmenters[idx] = segmenter;
        }

        /// <summary>Unregisters the segmenter for a specific script.</summary>
        public void Unregister(UnicodeScript script)
        {
            var idx = (int)script;
            if (segmenters[idx] != null)
            {
                segmenters[idx] = null;
                registeredCount--;
            }
        }

        /// <summary>Removes all registered segmenters.</summary>
        public void Clear()
        {
            Array.Clear(segmenters, 0, segmenters.Length);
            registeredCount = 0;
        }

        /// <summary>
        /// Refines contextual-script runs without creating boundaries inside grapheme clusters.
        /// </summary>
        /// <param name="codepoints">Codepoint array.</param>
        /// <param name="scripts">Per-codepoint script array (from ScriptAnalyzer).</param>
        /// <param name="breaks">Break opportunities array (length = codepoints.Length + 1).</param>
        public void Process(
            ReadOnlySpan<int> codepoints,
            ReadOnlySpan<UnicodeScript> scripts,
            Span<LineBreakType> breaks,
            Span<bool> wordBoundaries,
            ReadOnlySpan<bool> graphemeBoundaries)
        {
            var length = codepoints.Length;
            if (length == 0) return;

            var i = 0;
            while (i < length)
            {
                var segmenter = ResolveSegmenter(scripts[i]);

                if (segmenter == null || !IsWordCharacter(codepoints[i]))
                {
                    i++;
                    continue;
                }

                var runStart = i;
                i++;
                while (i < length && ReferenceEquals(ResolveSegmenter(scripts[i]), segmenter) &&
                       IsWordCharacter(codepoints[i]))
                    i++;

                var runLength = i - runStart;
                if (runLength <= 1) continue;

                if (segmenter is IWordBoundarySegmenter boundarySegmenter)
                    boundarySegmenter.Segment(codepoints, runStart, runLength, breaks,
                        wordBoundaries, graphemeBoundaries);
                else
                    segmenter.Segment(codepoints, runStart, runLength, breaks);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IWordSegmenter ResolveSegmenter(UnicodeScript script)
        {
            var exact = segmenters[(int)script];
            if (exact != null) return exact;

            var canonical = CanonicalizeDictionaryScript(script);
            return canonical != script ? segmenters[(int)canonical] : null;
        }

        internal bool HasSegmenter(UnicodeScript script) => ResolveSegmenter(script) != null;

        internal static UnicodeScript CanonicalizeDictionaryScript(UnicodeScript script)
            => script == UnicodeScript.Hiragana || script == UnicodeScript.Katakana ||
               script == UnicodeScript.Bopomofo
                ? UnicodeScript.Han
                : script;

        internal static bool IsContextualDictionaryScript(UnicodeScript script)
            => CanonicalizeDictionaryScript(script) == UnicodeScript.Han ||
               ScriptAnalyzer.IsSpacelessComplex(script);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsWordCharacter(int codepoint)
        {
            switch (UnicodeData.Provider.GetGeneralCategory(codepoint))
            {
                case GeneralCategory.Lu:
                case GeneralCategory.Ll:
                case GeneralCategory.Lt:
                case GeneralCategory.Lm:
                case GeneralCategory.Lo:
                case GeneralCategory.Mn:
                case GeneralCategory.Mc:
                case GeneralCategory.Me:
                case GeneralCategory.Nd:
                case GeneralCategory.Nl:
                case GeneralCategory.No:
                case GeneralCategory.Pc:
                    return true;
                default:
                    return false;
            }
        }
    }
}
