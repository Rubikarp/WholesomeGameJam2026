using System;

namespace LightSide
{
    /// <summary>
    /// Dictionary segmenter that minimizes dictionary cost across the whole run and treats a
    /// contiguous unknown span as one word for Southeast Asian scripts. CJK dictionaries retain
    /// per-character unknown boundaries and may supply frequency-derived word costs.
    /// </summary>
    internal sealed class BestPathSegmenter : IWordBoundarySegmenter
    {
        private const int UnknownCost = 255;

        private readonly DoubleArrayTrie trie;
        private readonly UnicodeScript script;
        private readonly bool groupUnknown;

        public UnicodeScript Script => script;

        public BestPathSegmenter(DoubleArrayTrie trie, UnicodeScript script)
        {
            this.trie = trie ?? throw new ArgumentNullException(nameof(trie));
            this.script = script;
            groupUnknown = script != UnicodeScript.Han;
        }

        public void Segment(ReadOnlySpan<int> codepoints, int start, int length, Span<LineBreakType> breaks)
        {
            var words = ArrayPool<bool>.Rent(codepoints.Length + 1);
            try
            {
                var wordBoundaries = words.AsSpan(0, codepoints.Length + 1);
                wordBoundaries.Fill(true);
                SegmentCore(codepoints, start, length, breaks, wordBoundaries,
                    ReadOnlySpan<bool>.Empty);
            }
            finally
            {
                ArrayPool<bool>.Return(words);
            }
        }

        public void Segment(ReadOnlySpan<int> codepoints, int start, int length,
            Span<LineBreakType> lineBreaks, Span<bool> wordBoundaries,
            ReadOnlySpan<bool> graphemeBoundaries)
            => SegmentCore(codepoints, start, length, lineBreaks, wordBoundaries, graphemeBoundaries);

        private void SegmentCore(ReadOnlySpan<int> codepoints, int start, int length,
            Span<LineBreakType> lineBreaks, Span<bool> wordBoundaries,
            ReadOnlySpan<bool> graphemeBoundaries)
        {
            var bestCost = ArrayPool<int>.Rent(length + 1);
            var bestNext = ArrayPool<int>.Rent(length + 1);

            try
            {
                ResolveBestPath(codepoints, start, length, lineBreaks, wordBoundaries,
                    graphemeBoundaries, bestCost, bestNext);
            }
            finally
            {
                ArrayPool<int>.Return(bestCost);
                ArrayPool<int>.Return(bestNext);
            }
        }

        /// <summary>Chooses the minimum-cost dictionary path; unmatched spans retain only their default UAX #29 endpoints.</summary>
        private void ResolveBestPath(ReadOnlySpan<int> codepoints, int start, int length,
            Span<LineBreakType> lineBreaks, Span<bool> wordBoundaries,
            ReadOnlySpan<bool> graphemeBoundaries,
            int[] bestCost, int[] bestNext)
        {
            const int infinity = int.MaxValue / 4;

            bestCost[length] = 0;
            bestNext[length] = length;

            for (var i = length - 1; i >= 0; i--)
            {
                var chosenCost = infinity;
                var chosenEnd = -1;
                var state = 0;

                for (var j = i; j < length; j++)
                {
                    state = trie.Traverse(state, codepoints[start + j]);
                    if (state < 0) break;
                    if (!trie.IsWordEnd(state)) continue;

                    var suffixCost = bestCost[j + 1];
                    var wordCost = trie.GetWordCost(state);
                    var candidateCost = suffixCost >= infinity - wordCost
                        ? infinity
                        : suffixCost + wordCost;
                    if (candidateCost < chosenCost ||
                        candidateCost == chosenCost && j + 1 > chosenEnd)
                    {
                        chosenCost = candidateCost;
                        chosenEnd = j + 1;
                    }
                }

                if (chosenEnd >= 0)
                {
                    bestCost[i] = chosenCost;
                    bestNext[i] = chosenEnd;
                    continue;
                }

                if (groupUnknown && i + 1 < length && bestNext[i + 1] < 0)
                {
                    bestCost[i] = bestCost[i + 1];
                    bestNext[i] = bestNext[i + 1];
                    continue;
                }

                bestCost[i] = bestCost[i + 1] >= infinity - UnknownCost
                    ? infinity
                    : bestCost[i + 1] + UnknownCost;
                bestNext[i] = ~(i + 1);
            }

            for (var boundary = 1; boundary < length; boundary++)
                bestCost[boundary] = 0;

            var position = 0;
            while (position < length)
            {
                var encodedEnd = bestNext[position];
                var end = encodedEnd < 0 ? ~encodedEnd : encodedEnd;
                if (end <= position || end > length)
                    throw new InvalidOperationException("Dictionary segmentation produced an invalid path.");

                if (end < length)
                    bestCost[end] = encodedEnd < 0 ? 1 : 2;

                position = end;
            }

            for (var localBoundary = 1; localBoundary < length; localBoundary++)
            {
                var boundary = start + localBoundary;
                var selected = bestCost[localBoundary];
                var isWordBoundary = selected != 0 &&
                    (selected == 2 || wordBoundaries[boundary]) &&
                    (graphemeBoundaries.IsEmpty || graphemeBoundaries[boundary]);
                wordBoundaries[boundary] = isWordBoundary;
                if (isWordBoundary && script != UnicodeScript.Han &&
                    lineBreaks[boundary] == LineBreakType.None)
                    lineBreaks[boundary] = LineBreakType.Optional;
            }
        }
    }
}
