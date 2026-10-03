using System;
using System.Collections.Concurrent;
using System.Threading;

namespace LightSide
{
    /// <summary>
    /// Process-global cache of pristine per-word shaping results in font units (<see cref="RawShapedGlyph"/>) —
    /// the finer-grained sibling of <see cref="ParagraphShapeCache"/>. It accelerates paragraphs that MISS the
    /// paragraph cache yet reuse individual words (changing labels, counters, chat logs that share vocabulary):
    /// a fully-cached run is spliced with no shaping call at all. A word is a whitespace-delimited token of a
    /// run whose script is whitespace-segmentable (any script except spaceless-complex ones such as Thai) and
    /// carries one OpenType feature set throughout; only tokens HarfBuzz marks safe-to-break on BOTH boundaries are stored —
    /// the exact per-font condition under which the isolated word shapes identically to its in-context form —
    /// so stored glyphs are context-free and size-independent.
    /// </summary>
    /// <remarks>
    /// Keys are <see cref="XxHash64"/> of (word codepoints, font id, variation hash, script, direction,
    /// language, feature set); equal hash is treated as identity — 64-bit collision odds are negligible and a false hit
    /// self-heals on the next content change. Thread-safe by design: <see cref="Store"/>/<see cref="TryGet"/>
    /// run on the shape worker threads, while <see cref="Prune"/> runs in the pass-setup phase (itself possibly
    /// off the main thread); a <c>WorkerPool</c> join barrier separates that phase from shaping, so Prune never
    /// overlaps a store. Entries hold font-unit glyphs, so they survive font-size changes; the readback loop
    /// re-applies scale/tracking/overrides on splice.
    /// </remarks>
    internal static class WordShapeCache
    {
        private struct Entry
        {
            public RawShapedGlyph[] glyphs;
            public int count;
        }

        private const int MaxEntries = 8192;
        private const int MaxWordLength = 32;

        private static readonly ConcurrentDictionary<long, Entry> map = new();
        private static int approxCount;
        private static int lastGeneration = -1;

        /// <summary>Longest word (codepoints) worth caching; longer tokens rarely repeat and only add memory.</summary>
        internal static bool IsCacheableLength(int wordLength) => wordLength > 0 && wordLength <= MaxWordLength;

        /// <summary>
        /// Drops the whole cache when the font fallback generation changed (stale glyph ids) or the entry cap
        /// is exceeded (pathological all-unique text). Called from the pass-setup phase, which joins before any
        /// shape job dispatches, so it never overlaps a concurrent <see cref="Store"/>; concurrent Prune calls
        /// from separate components are benign (idempotent clear + same-valued generation write).
        /// </summary>
        internal static void Prune(int generation)
        {
            if (generation != lastGeneration)
            {
                Clear();
                lastGeneration = generation;
            }
            else if (approxCount > MaxEntries)
            {
                Clear();
            }
        }

        internal static long ComputeKey(ReadOnlySpan<int> word, int fontId, int variationHash,
            byte script, byte direction, byte language, byte featureSet)
        {
            ulong h = XxHash64.Combine((ulong)(uint)fontId, (ulong)(uint)variationHash);
            h = XxHash64.Combine(h,
                ((ulong)featureSet << 24) | ((ulong)script << 16) | ((ulong)direction << 8) | language);
            return (long)XxHash64.Hash<int>(word, h);
        }

        internal static bool TryGet(long key, out RawShapedGlyph[] glyphs, out int count)
        {
            if (map.TryGetValue(key, out var e))
            {
                glyphs = e.glyphs;
                count = e.count;
                return true;
            }
            glyphs = null;
            count = 0;
            return false;
        }

        internal static void Store(long key, RawShapedGlyph[] glyphs, int count)
        {
            if (map.TryAdd(key, new Entry { glyphs = glyphs, count = count }))
                Interlocked.Increment(ref approxCount);
        }

        internal static void Clear()
        {
            map.Clear();
            Interlocked.Exchange(ref approxCount, 0);
        }
    }
}
