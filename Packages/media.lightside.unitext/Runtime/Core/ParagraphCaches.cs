using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Per-paragraph cache of pristine shaping results (runs + glyphs as HarfBuzz produced them,
    /// before Shaped-phase modifiers mutate advances in place). A fingerprint hit replays the
    /// stored slice instead of re-itemizing and re-shaping the paragraph — the Blink shape-cache
    /// model. Fingerprints are <see cref="XxHash64"/> values: equal hash + equal codepoint count is
    /// treated as identity (64-bit collision odds are negligible against the count discriminator,
    /// and a false hit costs one stale paragraph render, self-healing on the next content change).
    /// Entries store paragraph-LOCAL indices (range starts, clusters and glyph offsets
    /// rebased to the paragraph origin) so a reuse at a shifted document position is a copy plus
    /// constant adds.
    /// </summary>
    /// <remarks>
    /// Pass protocol: <see cref="BeginPass"/> → per paragraph either <see cref="TryAppend"/>
    /// (consumes a previous-pass entry, moves it to the next generation) or shape +
    /// <see cref="Store"/> → <see cref="EndPass"/> (returns unconsumed arrays, swaps generations).
    /// Entries are single-use per pass: duplicate-content paragraphs after the first re-shape and
    /// store their own entry, keeping array ownership single-owner. Not thread-safe per instance;
    /// one instance per <see cref="TextProcessor"/>, used from that component's rebuild thread.
    /// </remarks>
    internal sealed class ParagraphShapeCache
    {
        private struct Entry
        {
            public ulong hash;
            public int cpCount;
            public ShapedRun[] runs;
            public int runCount;
            public ShapedGlyph[] glyphs;
            public int glyphCount;
        }

        private PooledBuffer<Entry> entries;
        private PooledBuffer<Entry> nextEntries;
        private readonly Dictionary<ulong, int> index = new();

        public void BeginPass(int paragraphCount)
        {
            for (var i = 0; i < nextEntries.count; i++)
                ReturnEntry(ref nextEntries.data[i]);
            nextEntries.count = 0;

            index.Clear();
            for (var i = 0; i < entries.count; i++)
                index.TryAdd(entries.data[i].hash, i);

            nextEntries.EnsureCapacity(paragraphCount);
        }

        /// <summary>
        /// Fingerprint hit: appends the entry's pristine runs/glyphs rebased to
        /// (<paramref name="cpStart"/>, current buffer tails) and moves the entry into the next
        /// generation. False on miss — caller shapes and <see cref="Store"/>s.
        /// </summary>
        public bool TryAppend(ulong hash, int cpCount, int cpStart,
            ref PooledBuffer<ShapedRun> outRuns, ref PooledBuffer<ShapedGlyph> outGlyphs)
        {
            if (!index.TryGetValue(hash, out var i)) return false;
            ref var entry = ref entries.data[i];
            if (entry.cpCount != cpCount || entry.runs == null) return false;
            index.Remove(hash);

            var glyphBase = outGlyphs.count;
            outGlyphs.EnsureCapacity(glyphBase + entry.glyphCount);
            var dstGlyphs = outGlyphs.data;
            var srcGlyphs = entry.glyphs;
            for (var g = 0; g < entry.glyphCount; g++)
            {
                var glyph = srcGlyphs[g];
                glyph.cluster += cpStart;
                dstGlyphs[glyphBase + g] = glyph;
            }
            outGlyphs.count = glyphBase + entry.glyphCount;

            outRuns.EnsureCapacity(outRuns.count + entry.runCount);
            var dstRuns = outRuns.data;
            var runBase = outRuns.count;
            var srcRuns = entry.runs;
            for (var r = 0; r < entry.runCount; r++)
            {
                var run = srcRuns[r];
                run.range = new TextRange(run.range.start + cpStart, run.range.length);
                run.glyphStart += glyphBase;
                dstRuns[runBase + r] = run;
            }
            outRuns.count = runBase + entry.runCount;

            var moved = entry;
            entry.runs = null;
            entry.glyphs = null;
            nextEntries.Add(moved);
            return true;
        }

        /// <summary>Non-consuming hit test; a later <see cref="TryAppend"/> with the same key may still miss when a duplicate-content paragraph consumed the single-use entry first — callers need an inline re-shape fallback.</summary>
        public bool Peek(ulong hash, int cpCount)
            => index.TryGetValue(hash, out var i) && entries.data[i].cpCount == cpCount && entries.data[i].runs != null;

        /// <summary>
        /// Move-in store: takes ownership of arrays already holding the paragraph-LOCAL snapshot
        /// (clusters/ranges rebased to the paragraph origin, glyphStart 0-based). The parallel shape
        /// jobs produce these directly, so no copy happens here.
        /// </summary>
        public void StoreMoved(ulong hash, int cpCount, ShapedRun[] runs, int runCount, ShapedGlyph[] glyphs, int glyphCount)
        {
            nextEntries.Add(new Entry
            {
                hash = hash,
                cpCount = cpCount,
                runs = runs,
                runCount = runCount,
                glyphs = glyphs,
                glyphCount = glyphCount,
            });
        }

        /// <summary>Snapshots the paragraph's freshly-shaped slice (pristine, pre-Shaped-event) in local indices.</summary>
        public void Store(ulong hash, int cpStart, int cpCount,
            ReadOnlySpan<ShapedRun> runs, int glyphBase, ReadOnlySpan<ShapedGlyph> glyphs)
        {
            var entry = new Entry
            {
                hash = hash,
                cpCount = cpCount,
                runCount = runs.Length,
                glyphCount = glyphs.Length,
                runs = ArrayPool<ShapedRun>.Rent(Math.Max(runs.Length, 1)),
                glyphs = ArrayPool<ShapedGlyph>.Rent(Math.Max(glyphs.Length, 1))
            };

            for (var g = 0; g < glyphs.Length; g++)
            {
                var glyph = glyphs[g];
                glyph.cluster -= cpStart;
                entry.glyphs[g] = glyph;
            }

            for (var r = 0; r < runs.Length; r++)
            {
                var run = runs[r];
                run.range = new TextRange(run.range.start - cpStart, run.range.length);
                run.glyphStart -= glyphBase;
                entry.runs[r] = run;
            }

            nextEntries.Add(entry);
        }

        public void EndPass()
        {
            for (var i = 0; i < entries.count; i++)
                ReturnEntry(ref entries.data[i]);

            (entries, nextEntries) = (nextEntries, entries);
            nextEntries.count = 0;
            index.Clear();
        }

        public void Clear()
        {
            for (var i = 0; i < entries.count; i++) ReturnEntry(ref entries.data[i]);
            for (var i = 0; i < nextEntries.count; i++) ReturnEntry(ref nextEntries.data[i]);
            entries.Return();
            nextEntries.Return();
            index.Clear();
        }

        private static void ReturnEntry(ref Entry entry)
        {
            if (entry.runs != null) ArrayPool<ShapedRun>.Return(entry.runs);
            if (entry.glyphs != null) ArrayPool<ShapedGlyph>.Return(entry.glyphs);
            entry.runs = null;
            entry.glyphs = null;
        }
    }

    /// <summary>
    /// Per-paragraph cache of bidi levels, resolved scripts, and line, grapheme, and word boundaries.
    /// Same content-addressed, generational, single-use model as
    /// <see cref="ParagraphShapeCache"/>: an edit re-runs only the changed paragraphs' analysis, unchanged
    /// paragraphs replay their stored slices. Because analysis is keyed and computed per paragraph, script
    /// resolution (UAX #24 Common/Inherited) no longer leaks across paragraph separators — matching the bidi/
    /// line, grapheme, and word algorithms, all of which are paragraph-scoped, and the isolated-run (ruby) path.
    /// Stores paragraph-LOCAL arrays; the splice back to whole-document buffers lives in the caller
    /// (<see cref="TextProcessor"/>), which owns the +1-slot boundary semantics. Not thread-safe per instance;
    /// one instance per <see cref="TextProcessor"/>, used from that component's rebuild thread.
    /// </summary>
    internal sealed class ParagraphAnalysisCache
    {
        internal struct Entry
        {
            public ulong hash;
            public int cpCount;
            public byte baseLevel;
            public byte[] levels;
            public UnicodeScript[] scripts;
            public LineBreakType[] breaks;
            public bool[] graphemes;
            public bool[] words;
        }

        private PooledBuffer<Entry> entries;
        private PooledBuffer<Entry> nextEntries;
        private readonly Dictionary<ulong, int> index = new();

        public void BeginPass(int paragraphCount)
        {
            for (var i = 0; i < nextEntries.count; i++)
                ReturnEntry(ref nextEntries.data[i]);
            nextEntries.count = 0;

            index.Clear();
            for (var i = 0; i < entries.count; i++)
                index.TryAdd(entries.data[i].hash, i);

            nextEntries.EnsureCapacity(paragraphCount);
        }

        public bool Peek(ulong hash, int cpCount)
            => index.TryGetValue(hash, out var i) && entries.data[i].cpCount == cpCount && entries.data[i].levels != null;

        /// <summary>Hit: moves the matching entry into the next generation and returns it for the caller to splice. Single-use per pass, so duplicate-content paragraphs after the first re-compute and store their own entry.</summary>
        public bool TryConsume(ulong hash, int cpCount, out Entry entry)
        {
            entry = default;
            if (!index.TryGetValue(hash, out var i)) return false;
            ref var src = ref entries.data[i];
            if (src.cpCount != cpCount || src.levels == null) return false;
            index.Remove(hash);

            entry = src;
            nextEntries.Add(src);
            src.levels = null;
            src.scripts = null;
            src.breaks = null;
            src.graphemes = null;
            src.words = null;
            return true;
        }

        /// <summary>Snapshots a freshly analyzed paragraph's local slices. Copies; use <see cref="StoreMoved"/> to take ownership of job arrays.</summary>
        public void Store(ulong hash, int cpCount, byte baseLevel,
            ReadOnlySpan<byte> levels, ReadOnlySpan<UnicodeScript> scripts,
            ReadOnlySpan<LineBreakType> breaks, ReadOnlySpan<bool> graphemes,
            ReadOnlySpan<bool> words)
        {
            var entry = new Entry
            {
                hash = hash,
                cpCount = cpCount,
                baseLevel = baseLevel,
                levels = ArrayPool<byte>.Rent(Math.Max(cpCount, 1)),
                scripts = ArrayPool<UnicodeScript>.Rent(Math.Max(cpCount, 1)),
                breaks = ArrayPool<LineBreakType>.Rent(cpCount + 1),
                graphemes = ArrayPool<bool>.Rent(cpCount + 1),
                words = ArrayPool<bool>.Rent(cpCount + 1)
            };
            levels.CopyTo(entry.levels);
            scripts.CopyTo(entry.scripts);
            breaks.CopyTo(entry.breaks);
            graphemes.CopyTo(entry.graphemes);
            words.CopyTo(entry.words);

            Add(entry);
        }

        /// <summary>Move-in store: takes ownership of arrays already holding the paragraph-LOCAL snapshot (as an analysis job produces them), no copy. The arrays must be ArrayPool-rented; the cache returns them on eviction.</summary>
        public void StoreMoved(ulong hash, int cpCount, byte baseLevel,
            byte[] levels, UnicodeScript[] scripts, LineBreakType[] breaks, bool[] graphemes, bool[] words)
        {
            Add(new Entry
            {
                hash = hash,
                cpCount = cpCount,
                baseLevel = baseLevel,
                levels = levels,
                scripts = scripts,
                breaks = breaks,
                graphemes = graphemes,
                words = words
            });
        }

        private void Add(in Entry entry) => nextEntries.Add(entry);

        public void EndPass()
        {
            for (var i = 0; i < entries.count; i++)
                ReturnEntry(ref entries.data[i]);

            (entries, nextEntries) = (nextEntries, entries);
            nextEntries.count = 0;
            index.Clear();
        }

        public void Clear()
        {
            for (var i = 0; i < entries.count; i++) ReturnEntry(ref entries.data[i]);
            for (var i = 0; i < nextEntries.count; i++) ReturnEntry(ref nextEntries.data[i]);
            entries.Return();
            nextEntries.Return();
            index.Clear();
        }

        private static void ReturnEntry(ref Entry entry)
        {
            if (entry.levels != null) ArrayPool<byte>.Return(entry.levels);
            if (entry.scripts != null) ArrayPool<UnicodeScript>.Return(entry.scripts);
            if (entry.breaks != null) ArrayPool<LineBreakType>.Return(entry.breaks);
            if (entry.graphemes != null) ArrayPool<bool>.Return(entry.graphemes);
            if (entry.words != null) ArrayPool<bool>.Return(entry.words);
            entry.levels = null;
            entry.scripts = null;
            entry.breaks = null;
            entry.graphemes = null;
            entry.words = null;
        }
    }
}
