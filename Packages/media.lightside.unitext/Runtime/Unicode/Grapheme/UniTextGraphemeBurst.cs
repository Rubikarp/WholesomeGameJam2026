using System.Runtime.CompilerServices;
using Unity.Burst;

namespace LightSide
{
    /// <summary>
    /// Burst kernel for UAX #29 grapheme boundaries, including Indic conjuncts, emoji ZWJ sequences,
    /// regional-indicator pairing, and the simple-text fast path.
    /// </summary>
    [BurstCompile]
    internal static unsafe class UniTextGraphemeBurst
    {
        /// <summary>Computes grapheme-cluster boundaries for <paramref name="length"/> codepoints into the pinned <paramref name="outBreaks"/> buffer (0/1 per position, length+1 slots); <paramref name="gcbScratch"/> is caller-pinned working space of length ≥ <paramref name="length"/>. Callable from any thread.</summary>
        internal static void Resolve(int* codePoints, int length,
            byte* bmpGcb, GraphemeBreakRangeEntry* gcbRanges, int gcbRangesLen,
            byte* bmpIncb, IndicConjunctBreakRangeEntry* incbRanges, int incbRangesLen,
            byte* bmpExtPict, ExtendedPictographicRangeEntry* extPictRanges, int extPictRangesLen,
            byte* gcbScratch, byte* outBreaks)
            => ResolveEntry(codePoints, length, bmpGcb, gcbRanges, gcbRangesLen,
                bmpIncb, incbRanges, incbRangesLen, bmpExtPict, extPictRanges, extPictRangesLen,
                gcbScratch, outBreaks);

        private const int BmpSize = 65536;

        /// <summary>Mirror of <see cref="GraphemeBreaker"/>'s complexity mask: GCB classes whose presence can create a NON-boundary. Absent, every codepoint is its own cluster and the pass takes the all-breaks fast-path.</summary>
        private const uint GraphemeComplexityMask =
            (1u << (int)GraphemeClusterBreak.CR) | (1u << (int)GraphemeClusterBreak.LF) |
            (1u << (int)GraphemeClusterBreak.Extend) | (1u << (int)GraphemeClusterBreak.ZWJ) |
            (1u << (int)GraphemeClusterBreak.Regional_Indicator) | (1u << (int)GraphemeClusterBreak.Prepend) |
            (1u << (int)GraphemeClusterBreak.SpacingMark) |
            (1u << (int)GraphemeClusterBreak.L) | (1u << (int)GraphemeClusterBreak.V) | (1u << (int)GraphemeClusterBreak.T) |
            (1u << (int)GraphemeClusterBreak.LV) | (1u << (int)GraphemeClusterBreak.LVT);

        [BurstCompile(CompileSynchronously = true)]
        internal static void ResolveEntry(int* codePoints, int length,
            byte* bmpGcb, GraphemeBreakRangeEntry* gcbRanges, int gcbRangesLen,
            byte* bmpIncb, IndicConjunctBreakRangeEntry* incbRanges, int incbRangesLen,
            byte* bmpExtPict, ExtendedPictographicRangeEntry* extPictRanges, int extPictRangesLen,
            byte* gcbScratch, byte* outBreaks)
        {
            outBreaks[0] = 1;
            outBreaks[length] = 1;

            uint seenMask = 0;
            for (var i = 0; i < length; i++)
            {
                var g = LookupGcb(codePoints[i], bmpGcb, gcbRanges, gcbRangesLen);
                gcbScratch[i] = (byte)g;
                seenMask |= 1u << (int)g;
            }

            if ((seenMask & GraphemeComplexityMask) == 0)
            {
                for (var i = 1; i < length; i++)
                    outBreaks[i] = 1;
                return;
            }

            var riCount = 0;
            var scan = new ContextScan();
            var icbCurrent = LookupIncb(codePoints[0], bmpIncb, incbRanges, incbRangesLen);
            var extPictCurrent = LookupExtPict(codePoints[0], bmpExtPict, extPictRanges, extPictRangesLen);
            for (var i = 0; i < length - 1; i++)
            {
                var gcb1 = (GraphemeClusterBreak)gcbScratch[i];

                if (gcb1 == GraphemeClusterBreak.Regional_Indicator)
                    riCount++;
                else
                    riCount = 0;

                var nextCp = codePoints[i + 1];
                var icbNext = LookupIncb(nextCp, bmpIncb, incbRanges, incbRangesLen);
                var extPictNext = LookupExtPict(nextCp, bmpExtPict, extPictRanges, extPictRangesLen);

                scan.AdvanceConjunct(icbCurrent);
                var emojiPrefix = scan.EmojiPrefix;

                outBreaks[i + 1] = ShouldBreak(
                    gcb1, (GraphemeClusterBreak)gcbScratch[i + 1], riCount,
                    icbNext == IndicConjunctBreak.Consonant, extPictNext,
                    scan.ConjunctActive, emojiPrefix)
                    ? (byte)1
                    : (byte)0;

                scan.AdvanceEmoji(gcb1, extPictCurrent);

                icbCurrent = icbNext;
                extPictCurrent = extPictNext;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static GraphemeClusterBreak LookupGcb(int codePoint, byte* bmp, GraphemeBreakRangeEntry* ranges, int rangesLen)
        {
            if ((uint)codePoint < BmpSize)
                return (GraphemeClusterBreak)bmp[codePoint];

            var idx = FindRange(ranges, rangesLen, codePoint);
            return idx >= 0 ? ranges[idx].graphemeBreak : GraphemeClusterBreak.Other;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IndicConjunctBreak LookupIncb(int codePoint, byte* bmp, IndicConjunctBreakRangeEntry* ranges, int rangesLen)
        {
            if ((uint)codePoint < BmpSize)
                return (IndicConjunctBreak)bmp[codePoint];

            var idx = FindRange(ranges, rangesLen, codePoint);
            return idx >= 0 ? ranges[idx].indicConjunctBreak : IndicConjunctBreak.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool LookupExtPict(int codePoint, byte* bmp, ExtendedPictographicRangeEntry* ranges, int rangesLen)
        {
            if ((uint)codePoint < BmpSize)
                return bmp[codePoint] != 0;

            return FindRange(ranges, rangesLen, codePoint) >= 0;
        }

        /// <summary>
        /// The UAX #29 GCB pair table (GB3–GB999) — the single source shared by the Burst bulk pass
        /// (<see cref="ResolveEntry"/>) and the managed <see cref="GraphemeBreaker"/> wrappers. The three
        /// context-sensitive rules are fed pre-computed inputs: GB12/13 via <paramref name="riCount"/>
        /// parity, GB9c via <paramref name="conjunctActive"/>, GB11 via <paramref name="emojiPrefix"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool ShouldBreak(
            GraphemeClusterBreak gcb1,
            GraphemeClusterBreak gcb2,
            int riCount,
            bool nextIsConjunctConsonant,
            bool nextIsExtendedPictographic,
            bool conjunctActive,
            bool emojiPrefix)
        {
            if (gcb1 == GraphemeClusterBreak.CR && gcb2 == GraphemeClusterBreak.LF)
                return false;

            if (gcb1 == GraphemeClusterBreak.Control ||
                gcb1 == GraphemeClusterBreak.CR ||
                gcb1 == GraphemeClusterBreak.LF)
                return true;

            if (gcb2 == GraphemeClusterBreak.Control ||
                gcb2 == GraphemeClusterBreak.CR ||
                gcb2 == GraphemeClusterBreak.LF)
                return true;

            if (gcb1 == GraphemeClusterBreak.L &&
                (gcb2 == GraphemeClusterBreak.L ||
                 gcb2 == GraphemeClusterBreak.V ||
                 gcb2 == GraphemeClusterBreak.LV ||
                 gcb2 == GraphemeClusterBreak.LVT))
                return false;

            if ((gcb1 == GraphemeClusterBreak.LV || gcb1 == GraphemeClusterBreak.V) &&
                (gcb2 == GraphemeClusterBreak.V || gcb2 == GraphemeClusterBreak.T))
                return false;

            if ((gcb1 == GraphemeClusterBreak.LVT || gcb1 == GraphemeClusterBreak.T) &&
                gcb2 == GraphemeClusterBreak.T)
                return false;

            if (gcb2 == GraphemeClusterBreak.Extend || gcb2 == GraphemeClusterBreak.ZWJ)
                return false;

            if (gcb2 == GraphemeClusterBreak.SpacingMark)
                return false;

            if (gcb1 == GraphemeClusterBreak.Prepend)
                return false;

            if (nextIsConjunctConsonant && conjunctActive)
                return false;

            if (gcb1 == GraphemeClusterBreak.ZWJ && nextIsExtendedPictographic && emojiPrefix)
                return false;

            if (gcb1 == GraphemeClusterBreak.Regional_Indicator && gcb2 == GraphemeClusterBreak.Regional_Indicator)
                if (riCount % 2 == 1)
                    return false;

            return true;
        }

        /// <summary>
        /// Forward-carried lookbehind for GB9c and GB11, replacing their naive backscans with O(1)-per-position
        /// updates so the pass stays linear on long Indic and emoji-ZWJ runs. The canonical implementation for
        /// both the Burst and managed-IL paths.
        /// </summary>
        private struct ContextScan
        {
            private bool conjunctConsonant;
            private bool conjunctLinker;
            private bool emojiPrefix;

            public void AdvanceConjunct(IndicConjunctBreak icb)
            {
                switch (icb)
                {
                    case IndicConjunctBreak.Consonant:
                        conjunctConsonant = true;
                        conjunctLinker = false;
                        break;
                    case IndicConjunctBreak.Linker:
                        if (conjunctConsonant)
                            conjunctLinker = true;
                        break;
                    case IndicConjunctBreak.Extend:
                        break;
                    default:
                        conjunctConsonant = false;
                        conjunctLinker = false;
                        break;
                }
            }

            public bool ConjunctActive => conjunctConsonant && conjunctLinker;

            public bool EmojiPrefix => emojiPrefix;

            public void AdvanceEmoji(GraphemeClusterBreak gcb, bool isExtendedPictographic)
            {
                if (isExtendedPictographic)
                    emojiPrefix = true;
                else if (gcb != GraphemeClusterBreak.Extend && gcb != GraphemeClusterBreak.ZWJ)
                    emojiPrefix = false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(GraphemeBreakRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(IndicConjunctBreakRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(ExtendedPictographicRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }
    }
}
