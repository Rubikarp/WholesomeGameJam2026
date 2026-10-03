using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Read-only Unicode trie used by contextual word segmenters. The current sparse format
    /// supports the full scalar range and optional word costs; legacy double-array assets remain readable.
    /// </summary>
    internal sealed class DoubleArrayTrie
    {
        private const int SparseMagic = 0x32445357;

        private int[] baseArray;
        private int[] checkArray;
        private int codepointBase;
        private int codepointRange;

        private int[] firstEdges;
        private int[] edgeCounts;
        private int[] wordCosts;
        private int[] edgeCodepoints;
        private int[] edgeTargets;
        private bool sparse;
        private int stateCount;

        public int StateCount => stateCount;

        public void Load(byte[] data)
        {
            if (data == null || data.Length < 12)
                throw new ArgumentException("Invalid trie data: too short.", nameof(data));

            var span = data.AsSpan();
            if (ReadInt32(span, 0) == SparseMagic)
                LoadSparse(span);
            else
                LoadLegacy(data, span);
        }

        private void LoadSparse(ReadOnlySpan<byte> data)
        {
            stateCount = ReadInt32(data, 4);
            var edgeCount = ReadInt32(data, 8);
            var expectedSize = 12L + stateCount * 12L + edgeCount * 8L;
            if (stateCount <= 0 || edgeCount < 0 || expectedSize != data.Length)
                throw new ArgumentException("Invalid sparse trie data.", nameof(data));

            firstEdges = new int[stateCount];
            edgeCounts = new int[stateCount];
            wordCosts = new int[stateCount];
            edgeCodepoints = new int[edgeCount];
            edgeTargets = new int[edgeCount];
            var parentCounts = new int[stateCount];

            var offset = 12;
            var expectedFirstEdge = 0;
            for (var state = 0; state < stateCount; state++)
            {
                firstEdges[state] = ReadInt32(data, offset);
                edgeCounts[state] = ReadInt32(data, offset + 4);
                wordCosts[state] = ReadInt32(data, offset + 8);
                offset += 12;

                if (firstEdges[state] != expectedFirstEdge || edgeCounts[state] < 0 ||
                    (long)firstEdges[state] + edgeCounts[state] > edgeCount ||
                    wordCosts[state] < -1)
                    throw new ArgumentException("Invalid sparse trie state.", nameof(data));
                expectedFirstEdge += edgeCounts[state];

                var previousCodepoint = -1;
                for (var edge = firstEdges[state]; edge < firstEdges[state] + edgeCounts[state]; edge++)
                {
                    var codepoint = ReadInt32(data, 12 + stateCount * 12 + edge * 8);
                    if ((uint)codepoint > 0x10FFFF || codepoint <= previousCodepoint)
                        throw new ArgumentException("Unsorted sparse trie edges.", nameof(data));
                    previousCodepoint = codepoint;
                }
            }
            if (expectedFirstEdge != edgeCount)
                throw new ArgumentException("Unowned sparse trie edges.", nameof(data));
            if (wordCosts[0] != -1)
                throw new ArgumentException("Invalid sparse trie root.", nameof(data));

            for (var edge = 0; edge < edgeCount; edge++)
            {
                edgeCodepoints[edge] = ReadInt32(data, offset);
                edgeTargets[edge] = ReadInt32(data, offset + 4);
                offset += 8;
                if (!Utf16.IsUnicodeScalar(edgeCodepoints[edge]) ||
                    (uint)edgeTargets[edge] >= (uint)stateCount ||
                    edgeTargets[edge] == 0 || ++parentCounts[edgeTargets[edge]] != 1)
                    throw new ArgumentException("Invalid sparse trie edge.", nameof(data));
            }

            for (var state = 1; state < stateCount; state++)
                if (parentCounts[state] != 1)
                    throw new ArgumentException("Disconnected sparse trie state.", nameof(data));

            var reachable = new bool[stateCount];
            var stack = new int[stateCount];
            var stackCount = 1;
            var reachableCount = 0;
            stack[0] = 0;
            while (stackCount > 0)
            {
                var state = stack[--stackCount];
                if (reachable[state]) continue;
                reachable[state] = true;
                reachableCount++;
                for (var edge = firstEdges[state]; edge < firstEdges[state] + edgeCounts[state]; edge++)
                    stack[stackCount++] = edgeTargets[edge];
            }
            if (reachableCount != stateCount)
                throw new ArgumentException("Disconnected sparse trie state.", nameof(data));

            baseArray = null;
            checkArray = null;
            sparse = true;
        }

        private void LoadLegacy(byte[] data, ReadOnlySpan<byte> span)
        {
            stateCount = ReadInt32(span, 0);
            codepointBase = ReadInt32(span, 4);
            codepointRange = ReadInt32(span, 8);

            var expectedSize = 12L + stateCount * 8L;
            if (stateCount <= 0 || codepointRange <= 0 || codepointBase < 0 ||
                (long)codepointBase + codepointRange > 0x110000 || expectedSize != data.Length)
                throw new ArgumentException("Invalid legacy trie data.", nameof(data));

            baseArray = new int[stateCount];
            checkArray = new int[stateCount];
            Buffer.BlockCopy(data, 12, baseArray, 0, stateCount * 4);
            Buffer.BlockCopy(data, 12 + stateCount * 4, checkArray, 0, stateCount * 4);

            firstEdges = null;
            edgeCounts = null;
            wordCosts = null;
            edgeCodepoints = null;
            edgeTargets = null;
            sparse = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Traverse(int state, int codepoint)
        {
            if (sparse)
            {
                var low = firstEdges[state];
                var high = low + edgeCounts[state] - 1;
                while (low <= high)
                {
                    var middle = (low + high) >> 1;
                    var value = edgeCodepoints[middle];
                    if (codepoint < value) high = middle - 1;
                    else if (codepoint > value) low = middle + 1;
                    else return edgeTargets[middle];
                }

                return -1;
            }

            var index = codepoint - codepointBase;
            if ((uint)index >= (uint)codepointRange) return -1;

            var baseValue = baseArray[state];
            if (baseValue < 0) baseValue = ~baseValue;

            var next = baseValue + index;
            if ((uint)next >= (uint)stateCount || checkArray[next] != state) return -1;
            return next;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsWordEnd(int state) => sparse ? wordCosts[state] >= 0 : baseArray[state] < 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetWordCost(int state) => sparse ? wordCosts[state] : 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadInt32(ReadOnlySpan<byte> data, int offset)
        {
            return data[offset]
                 | data[offset + 1] << 8
                 | data[offset + 2] << 16
                 | data[offset + 3] << 24;
        }
    }
}
