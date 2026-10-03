using System;
using System.Runtime.CompilerServices;
using Unity.Burst;

namespace LightSide
{
    /// <summary>One level run inside <see cref="UniTextBidiBurst"/> (mirror of the managed engine's <c>LevelRun</c> value tuple); caller-provided scratch.</summary>
    internal struct BidiLevelRun
    {
        public int start;
        public int end;
        public byte level;
    }

    /// <summary>One isolating run sequence (X10) inside <see cref="UniTextBidiBurst"/>: a window <c>[indexStart, indexStart+indexLength)</c> into the shared sequence-index scratch plus its level and sos/eos boundary classes; caller-provided scratch.</summary>
    internal struct BidiIsoSeq
    {
        public int indexStart;
        public int indexLength;
        public byte level;
        public byte sos;
        public byte eos;
    }

    /// <summary>One resolved paired-bracket pair (N0) inside <see cref="UniTextBidiBurst"/>; caller-provided scratch.</summary>
    internal struct BidiBracketPair
    {
        public int openIndex;
        public int closeIndex;
    }

    /// <summary>
    /// Per-thread reusable working buffers for one bidi resolve — grown once and kept, so the hot
    /// per-paragraph path pays no pool traffic. The Burst-compatible <see cref="UniTextBidiBurst"/> kernel
    /// cannot read managed thread-static state, so the caller pins these and passes raw pointers; the same
    /// holder feeds both text-pipeline and <see cref="BidiEngine"/> calls into the same Burst kernel.
    /// One paragraph resolves at a time per thread and never nests, so a single
    /// thread-local instance is safe for both.
    /// </summary>
    internal sealed class BidiScratch
    {
        public byte[] bidiClasses;
        public byte[] originalClasses;
        public int[] isolateToPdi;
        public int[] pdiToIsolate;
        public int[] isolateStack;
        public BidiLevelRun[] levelRuns;
        public int[] runIndexByPosition;
        public int[] seqBuffer;
        public int[] sequenceIndices;
        public BidiIsoSeq[] sequences;
        public BidiBracketPair[] bracketPairs;
        public int[] openStack;
        public BidiParagraph[] paragraphsOut;

        private int capacity;

        [ThreadStatic] private static BidiScratch tls;

        /// <summary>The calling thread's scratch, sized to at least <paramref name="length"/> codepoints.</summary>
        public static BidiScratch Get(int length)
        {
            var s = tls ??= new BidiScratch();
            if (s.capacity < length) s.Grow(length);
            return s;
        }

        private void Grow(int length)
        {
            var n = Math.Max(length, capacity == 0 ? 64 : capacity * 2);
            bidiClasses = new byte[n];
            originalClasses = new byte[n];
            isolateToPdi = new int[n];
            pdiToIsolate = new int[n];
            isolateStack = new int[n];
            levelRuns = new BidiLevelRun[n];
            runIndexByPosition = new int[n];
            seqBuffer = new int[n];
            sequenceIndices = new int[n];
            sequences = new BidiIsoSeq[n];
            bracketPairs = new BidiBracketPair[n];
            openStack = new int[n];
            paragraphsOut = new BidiParagraph[n];
            capacity = n;
        }
    }

    /// <summary>
    /// Burst kernel for the UAX #9 bidirectional algorithm through rule L1.
    /// </summary>
    [BurstCompile]
    internal static unsafe class UniTextBidiBurst
    {
        /// <summary>Resolves the UAX #9 embedding levels for <paramref name="length"/> codepoints into the pinned <paramref name="outLevels"/> (one byte per codepoint) and the paragraphs into <paramref name="outParagraphs"/>/<paramref name="outParagraphCount"/>; <paramref name="paragraphDirection"/> is 0=LTR, 1=RTL, 2=Auto. All other pointer arguments are caller-pinned scratch (see the class summary). Callable from any thread.</summary>
        internal static void Resolve(int* codePoints, int length, int paragraphDirection,
            byte* bmpBidiClass, RangeEntry* bidiRanges, int bidiRangesLen,
            BracketEntry* brackets, int bracketsLen,
            byte* bidiClasses, byte* originalClasses,
            int* isolateToPdi, int* pdiToIsolate, int* isolateStack,
            BidiLevelRun* levelRuns, int* runIndexByPosition,
            int* seqBuffer, int* sequenceIndices, BidiIsoSeq* sequences,
            BidiBracketPair* bracketPairs, int* openStack,
            byte* outLevels, BidiParagraph* outParagraphs, int* outParagraphCount)
            => ResolveEntry(codePoints, length, paragraphDirection,
                bmpBidiClass, bidiRanges, bidiRangesLen, brackets, bracketsLen,
                bidiClasses, originalClasses, isolateToPdi, pdiToIsolate, isolateStack,
                levelRuns, runIndexByPosition, seqBuffer, sequenceIndices, sequences,
                bracketPairs, openStack, outLevels, outParagraphs, outParagraphCount);

        private const int BmpSize = 65536;
        private const int MaxExplicitLevel = 125;
        private const int EmbeddingStackSize = MaxExplicitLevel + 2;
        private const int MaxPairingDepth = 63;

        /// <summary>Bidi classes whose presence forces full UAX#9 resolution (implicit RTL + every explicit control). Mirror of <see cref="BidiEngine"/>'s <c>BidiComplexityMask</c>.</summary>
        private const uint BidiComplexityMask =
            (1u << (int)BidiClass.RightToLeft) | (1u << (int)BidiClass.ArabicLetter) | (1u << (int)BidiClass.ArabicNumber) |
            (1u << (int)BidiClass.LeftToRightEmbedding) | (1u << (int)BidiClass.RightToLeftEmbedding) |
            (1u << (int)BidiClass.LeftToRightOverride) | (1u << (int)BidiClass.RightToLeftOverride) |
            (1u << (int)BidiClass.PopDirectionalFormat) |
            (1u << (int)BidiClass.LeftToRightIsolate) | (1u << (int)BidiClass.RightToLeftIsolate) |
            (1u << (int)BidiClass.FirstStrongIsolate) | (1u << (int)BidiClass.PopDirectionalIsolate);

        /// <summary>The two read-only bidi tables (BMP dense byte array + supplementary <see cref="RangeEntry"/> array) plus the paired-bracket table. Bundled so the ~25 transcribed helpers thread one value instead of five loose pointers — same raw-pointer lookups, no logic change.</summary>
        private struct Tables
        {
            public byte* bmpBidi;
            public RangeEntry* bidiRanges;
            public int bidiRangesLen;
            public BracketEntry* brackets;
            public int bracketsLen;
        }

        /// <summary>All caller-provided length-bounded scratch pointers for one <see cref="ResolveEntry"/> invocation. Bundled to keep the sub-pass helper signatures readable — same raw pointers, no logic change.</summary>
        private struct Scratch
        {
            public byte* bidiClasses;
            public byte* originalClasses;
            public int* isolateToPdi;
            public int* pdiToIsolate;
            public int* isolateStack;
            public BidiLevelRun* levelRuns;
            public int* runIndexByPosition;
            public int* seqBuffer;
            public int* sequenceIndices;
            public BidiIsoSeq* sequences;
            public BidiBracketPair* bracketPairs;
            public int* openStack;
        }

        private struct EmbeddingState
        {
            public byte level;
            public sbyte overrideStatus;
            public bool isIsolate;
        }

        /// <summary>
        /// Full UAX #9 pipeline shared by Burst and managed execution.
        /// </summary>
        [BurstCompile(CompileSynchronously = true)]
        internal static void ResolveEntry(int* codePoints, int length, int paragraphDirection,
            byte* bmpBidiClass, RangeEntry* bidiRanges, int bidiRangesLen,
            BracketEntry* brackets, int bracketsLen,
            byte* bidiClasses, byte* originalClasses,
            int* isolateToPdi, int* pdiToIsolate, int* isolateStack,
            BidiLevelRun* levelRuns, int* runIndexByPosition,
            int* seqBuffer, int* sequenceIndices, BidiIsoSeq* sequences,
            BidiBracketPair* bracketPairs, int* openStack,
            byte* outLevels, BidiParagraph* outParagraphs, int* outParagraphCount)
        {
            outParagraphCount[0] = 0;
            if (length == 0) return;

            var t = new Tables
            {
                bmpBidi = bmpBidiClass, bidiRanges = bidiRanges, bidiRangesLen = bidiRangesLen,
                brackets = brackets, bracketsLen = bracketsLen
            };
            var s = new Scratch
            {
                bidiClasses = bidiClasses, originalClasses = originalClasses,
                isolateToPdi = isolateToPdi, pdiToIsolate = pdiToIsolate, isolateStack = isolateStack,
                levelRuns = levelRuns, runIndexByPosition = runIndexByPosition,
                seqBuffer = seqBuffer, sequenceIndices = sequenceIndices, sequences = sequences,
                bracketPairs = bracketPairs, openStack = openStack
            };

            uint seenMask = 0;
            for (var i = 0; i < length; i++)
            {
                var cp = codePoints[i];
                if ((uint)cp > UnicodeData.MaxCodepoint)
                    cp = UnicodeData.ReplacementCharacter;

                var bc = GetBidiClass(in t, cp);
                bidiClasses[i] = (byte)bc;
                originalClasses[i] = (byte)bc;
                seenMask |= 1u << (int)bc;
            }

            int paragraphCount;
            var forced = paragraphDirection != 2;
            if (forced)
                paragraphCount = BuildParagraphsWithExplicitBaseLevel(bidiClasses, length, (byte)paragraphDirection, outParagraphs);
            else
                paragraphCount = BuildParagraphs(bidiClasses, length, outParagraphs);

            for (var i = 0; i < length; i++)
                outLevels[i] = 0;

            var pureLtr = (seenMask & BidiComplexityMask) == 0;
            if (pureLtr)
                for (var p = 0; p < paragraphCount; p++)
                    if ((outParagraphs[p].baseLevel & 1) != 0) { pureLtr = false; break; }

            if (pureLtr)
            {
                outParagraphCount[0] = paragraphCount;
                return;
            }

            for (var p = 0; p < paragraphCount; p++)
            {
                var para = outParagraphs[p];
                ResolveExplicitLevelsForParagraph(para.startIndex, para.endIndex, para.baseLevel, bidiClasses, outLevels);
            }

            for (var p = 0; p < paragraphCount; p++)
            {
                var para = outParagraphs[p];
                var seqCount = BuildIsolatingRunSequences(para.startIndex, para.endIndex, para.baseLevel, in t, in s, outLevels);
                ResolveWeakTypesWithSequences(seqCount, in s);
                ResolvePairedBracketsForParagraph(seqCount, in t, in s, codePoints);
                ResolveNeutralTypesWithSequences(seqCount, in s);
            }

            for (var p = 0; p < paragraphCount; p++)
            {
                var para = outParagraphs[p];
                ResolveImplicitLevelsForParagraph(para.startIndex, para.endIndex, bidiClasses, outLevels);
            }

            for (var p = 0; p < paragraphCount; p++)
            {
                var para = outParagraphs[p];
                ApplyLineBreakRuleL1ForParagraph(originalClasses, para.startIndex, para.endIndex, para.baseLevel, outLevels);
            }

            outParagraphCount[0] = paragraphCount;
        }

        #region Paragraph splitting (P2/P3)

        private static int BuildParagraphs(byte* bidiClasses, int length, BidiParagraph* paragraphs)
        {
            var count = 0;
            var paraStart = 0;
            for (var i = 0; i < length; i++)
                if ((BidiClass)bidiClasses[i] == BidiClass.ParagraphSeparator)
                {
                    var baseLevel = ComputeParagraphBaseLevel(bidiClasses, paraStart, i - 1);
                    paragraphs[count++] = new BidiParagraph(paraStart, i, baseLevel);
                    paraStart = i + 1;
                }

            if (paraStart < length)
            {
                var baseLevel = ComputeParagraphBaseLevel(bidiClasses, paraStart, length - 1);
                paragraphs[count++] = new BidiParagraph(paraStart, length - 1, baseLevel);
            }

            return count;
        }

        private static int BuildParagraphsWithExplicitBaseLevel(byte* bidiClasses, int length, byte givenBaseLevel, BidiParagraph* paragraphs)
        {
            var count = 0;
            var paraStart = 0;
            for (var i = 0; i < length; i++)
                if ((BidiClass)bidiClasses[i] == BidiClass.ParagraphSeparator)
                {
                    paragraphs[count++] = new BidiParagraph(paraStart, i, givenBaseLevel);
                    paraStart = i + 1;
                }

            if (paraStart < length)
                paragraphs[count++] = new BidiParagraph(paraStart, length - 1, givenBaseLevel);

            return count;
        }

        private static byte ComputeParagraphBaseLevel(byte* bidiClasses, int start, int end)
        {
            var isolateDepth = 0;

            for (var i = start; i <= end; i++)
            {
                var bc = (BidiClass)bidiClasses[i];

                if (bc == BidiClass.LeftToRightIsolate ||
                    bc == BidiClass.RightToLeftIsolate ||
                    bc == BidiClass.FirstStrongIsolate)
                {
                    isolateDepth++;
                    continue;
                }

                if (bc == BidiClass.PopDirectionalIsolate)
                {
                    if (isolateDepth > 0)
                        isolateDepth--;
                    continue;
                }

                if (isolateDepth > 0)
                    continue;

                switch (bc)
                {
                    case BidiClass.LeftToRight:
                        return 0;
                    case BidiClass.RightToLeft:
                    case BidiClass.ArabicLetter:
                        return 1;
                }
            }

            return 0;
        }

        #endregion

        #region Explicit levels (X1–X8)

        private static void ResolveExplicitLevelsForParagraph(int start, int end, byte baseLevel, byte* bidiClasses, byte* levels)
        {
            var stack = stackalloc EmbeddingState[EmbeddingStackSize];

            var stackDepth = 1;
            stack[0].level = baseLevel;
            stack[0].overrideStatus = 0;
            stack[0].isIsolate = false;

            var currentLevel = baseLevel;
            sbyte overrideStatus = 0;
            var overflowIsolateCount = 0;
            var overflowEmbeddingCount = 0;

            for (var i = start; i <= end; i++)
            {
                var bc = (BidiClass)bidiClasses[i];

                levels[i] = currentLevel;

                switch (bc)
                {
                    case BidiClass.LeftToRightEmbedding:
                        PushEmbedding(stack, ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, false, 0);
                        bidiClasses[i] = (byte)BidiClass.BoundaryNeutral;
                        break;

                    case BidiClass.RightToLeftEmbedding:
                        PushEmbedding(stack, ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, true, 0);
                        bidiClasses[i] = (byte)BidiClass.BoundaryNeutral;
                        break;

                    case BidiClass.LeftToRightOverride:
                        PushEmbedding(stack, ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, false, 1);
                        bidiClasses[i] = (byte)BidiClass.BoundaryNeutral;
                        break;

                    case BidiClass.RightToLeftOverride:
                        PushEmbedding(stack, ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, true, 2);
                        bidiClasses[i] = (byte)BidiClass.BoundaryNeutral;
                        break;

                    case BidiClass.PopDirectionalFormat:
                        PopEmbedding(stack, ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount);
                        levels[i] = currentLevel;
                        bidiClasses[i] = (byte)BidiClass.BoundaryNeutral;
                        break;

                    case BidiClass.LeftToRightIsolate:
                        PushIsolate(stack, ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, false);
                        break;

                    case BidiClass.RightToLeftIsolate:
                        PushIsolate(stack, ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, true);
                        break;

                    case BidiClass.FirstStrongIsolate:
                    {
                        var isRtl = ResolveFirstStrongIsolateDirection(i, end, bidiClasses, baseLevel);
                        PushIsolate(stack, ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount, isRtl);
                        break;
                    }

                    case BidiClass.PopDirectionalIsolate:
                        PopIsolate(stack, ref stackDepth, ref currentLevel, ref overrideStatus,
                            ref overflowIsolateCount, ref overflowEmbeddingCount);
                        levels[i] = currentLevel;
                        break;

                    default:
                        if (overrideStatus == 1)
                        {
                            if (bc != BidiClass.BoundaryNeutral &&
                                bc != BidiClass.ParagraphSeparator &&
                                bc != BidiClass.SegmentSeparator)
                                bidiClasses[i] = (byte)BidiClass.LeftToRight;
                        }
                        else if (overrideStatus == 2)
                        {
                            if (bc != BidiClass.BoundaryNeutral &&
                                bc != BidiClass.ParagraphSeparator &&
                                bc != BidiClass.SegmentSeparator)
                                bidiClasses[i] = (byte)BidiClass.RightToLeft;
                        }

                        break;
                }
            }
        }

        private static void PushEmbedding(EmbeddingState* stack, ref int stackDepth, ref byte currentLevel,
            ref sbyte overrideStatus, ref int overflowIsolateCount, ref int overflowEmbeddingCount, bool isRtl, sbyte overrideClass)
        {
            if (overflowIsolateCount > 0 || overflowEmbeddingCount > 0)
            {
                if (overflowIsolateCount == 0)
                    overflowEmbeddingCount++;
                return;
            }

            byte newLevel;
            if (isRtl)
                newLevel = (byte)((currentLevel + 1) | 1);
            else
                newLevel = (byte)((currentLevel + 2) & ~1);

            if (newLevel > MaxExplicitLevel || stackDepth >= EmbeddingStackSize)
            {
                overflowEmbeddingCount++;
                return;
            }

            stack[stackDepth].level = newLevel;
            stack[stackDepth].overrideStatus = overrideClass;
            stack[stackDepth].isIsolate = false;
            stackDepth++;

            currentLevel = newLevel;
            overrideStatus = overrideClass;
        }

        private static void PopEmbedding(EmbeddingState* stack, ref int stackDepth, ref byte currentLevel,
            ref sbyte overrideStatus, ref int overflowIsolateCount, ref int overflowEmbeddingCount)
        {
            if (overflowIsolateCount > 0)
                return;

            if (overflowEmbeddingCount > 0)
            {
                overflowEmbeddingCount--;
                return;
            }

            if (stackDepth <= 1)
                return;

            var topIndex = stackDepth - 1;
            if (stack[topIndex].isIsolate)
                return;

            stackDepth--;
            var state = stack[stackDepth - 1];
            currentLevel = state.level;
            overrideStatus = state.overrideStatus;
        }

        private static void PushIsolate(EmbeddingState* stack, ref int stackDepth, ref byte currentLevel,
            ref sbyte overrideStatus, ref int overflowIsolateCount, ref int overflowEmbeddingCount, bool isRtl)
        {
            if (overflowIsolateCount > 0)
            {
                overflowIsolateCount++;
                return;
            }

            byte newLevel;
            if (isRtl)
                newLevel = (byte)((currentLevel + 1) | 1);
            else
                newLevel = (byte)((currentLevel + 2) & ~1);

            if (newLevel > MaxExplicitLevel || stackDepth >= EmbeddingStackSize)
            {
                overflowIsolateCount++;
                return;
            }

            stack[stackDepth].level = newLevel;
            stack[stackDepth].overrideStatus = 0;
            stack[stackDepth].isIsolate = true;
            stackDepth++;

            currentLevel = newLevel;
            overrideStatus = 0;
        }

        private static void PopIsolate(EmbeddingState* stack, ref int stackDepth, ref byte currentLevel,
            ref sbyte overrideStatus, ref int overflowIsolateCount, ref int overflowEmbeddingCount)
        {
            if (overflowIsolateCount > 0)
            {
                overflowIsolateCount--;
                return;
            }

            if (stackDepth <= 1) return;

            var isolateIndex = -1;
            for (var i = stackDepth - 1; i >= 1; i--)
                if (stack[i].isIsolate)
                {
                    isolateIndex = i;
                    break;
                }

            if (isolateIndex == -1) return;

            stackDepth = isolateIndex;
            overflowEmbeddingCount = 0;
            var state = stack[stackDepth - 1];
            currentLevel = state.level;
            overrideStatus = state.overrideStatus;
        }

        private static bool ResolveFirstStrongIsolateDirection(int fsiIndex, int paragraphEnd, byte* bidiClasses, byte paragraphBaseLevel)
        {
            var depth = 1;

            for (var i = fsiIndex + 1; i <= paragraphEnd; i++)
            {
                var bc = (BidiClass)bidiClasses[i];

                switch (bc)
                {
                    case BidiClass.LeftToRightIsolate:
                    case BidiClass.RightToLeftIsolate:
                    case BidiClass.FirstStrongIsolate:
                        depth++;
                        break;

                    case BidiClass.PopDirectionalIsolate:
                        depth--;
                        if (depth == 0) return false;
                        break;
                }

                if (depth < 1)
                    break;

                if (depth == 1)
                {
                    if (bc == BidiClass.LeftToRight)
                        return false;
                    if (bc == BidiClass.RightToLeft || bc == BidiClass.ArabicLetter)
                        return true;
                }
            }

            return false;
        }

        #endregion

        #region Isolating run sequences (X10)

        private static int BuildIsolatingRunSequences(int start, int end, byte paragraphBaseLevel, in Tables t, in Scratch s, byte* levels)
        {
            if (start > end)
                return 0;

            var length = end - start + 1;
            var bidiClasses = s.bidiClasses;
            var isolateToPdi = s.isolateToPdi;
            var pdiToIsolate = s.pdiToIsolate;

            for (var i = 0; i < length; i++)
            {
                isolateToPdi[i] = -1;
                pdiToIsolate[i] = -1;
            }

            var isolateStack = s.isolateStack;
            var isoTop = 0;

            for (var index = start; index <= end; index++)
            {
                var bc = (BidiClass)bidiClasses[index];

                if (bc == BidiClass.LeftToRightIsolate ||
                    bc == BidiClass.RightToLeftIsolate ||
                    bc == BidiClass.FirstStrongIsolate)
                    isolateStack[isoTop++] = index;
                else if (bc == BidiClass.PopDirectionalIsolate)
                    if (isoTop > 0)
                    {
                        var open = isolateStack[--isoTop];
                        isolateToPdi[open - start] = index;
                        pdiToIsolate[index - start] = open;
                    }
            }

            var levelRuns = s.levelRuns;
            var levelRunsCount = 0;

            {
                var runStart = -1;
                byte runLevel = 0;

                for (var i = start; i <= end; i++)
                {
                    if ((BidiClass)bidiClasses[i] == BidiClass.BoundaryNeutral)
                        continue;

                    var l = levels[i];

                    if (runStart == -1)
                    {
                        runStart = i;
                        runLevel = l;
                    }
                    else if (l != runLevel)
                    {
                        levelRuns[levelRunsCount++] = new BidiLevelRun
                            { start = runStart, end = FindLastNonBnBefore(i, start, bidiClasses), level = runLevel };
                        runStart = i;
                        runLevel = l;
                    }
                }

                if (runStart != -1)
                    levelRuns[levelRunsCount++] = new BidiLevelRun
                        { start = runStart, end = FindLastNonBnBefore(end + 1, start, bidiClasses), level = runLevel };
            }

            var runIndexByPosition = s.runIndexByPosition;
            for (var i = start; i <= end; i++)
                runIndexByPosition[i] = -1;
            for (var r = 0; r < levelRunsCount; r++)
            {
                var run = levelRuns[r];
                for (var i = run.start; i <= run.end; i++)
                    runIndexByPosition[i] = r;
            }

            var seqBuffer = s.seqBuffer;
            var sequenceIndices = s.sequenceIndices;
            var sequences = s.sequences;
            var seqIndicesCount = 0;
            var seqCount = 0;

            for (var r = 0; r < levelRunsCount; r++)
            {
                var run = levelRuns[r];
                var firstIndex = run.start;
                var firstBc = (BidiClass)bidiClasses[firstIndex];

                if (firstBc == BidiClass.PopDirectionalIsolate &&
                    pdiToIsolate[firstIndex - start] != -1)
                    continue;

                var seqBufCount = 0;
                var currentRunIndex = r;

                while (true)
                {
                    var currentRun = levelRuns[currentRunIndex];

                    for (var i = currentRun.start; i <= currentRun.end; i++)
                        seqBuffer[seqBufCount++] = i;

                    var lastIndex = currentRun.end;
                    var lastBc = (BidiClass)bidiClasses[lastIndex];

                    var isIsolateInitiator =
                        lastBc == BidiClass.LeftToRightIsolate ||
                        lastBc == BidiClass.RightToLeftIsolate ||
                        lastBc == BidiClass.FirstStrongIsolate;

                    if (!isIsolateInitiator) break;

                    var pdiIndex = isolateToPdi[lastIndex - start];
                    if (pdiIndex < 0) break;

                    var nextRunIndex = runIndexByPosition[pdiIndex];

                    if (nextRunIndex < 0 || nextRunIndex == currentRunIndex) break;

                    currentRunIndex = nextRunIndex;
                }

                if (seqBufCount == 0)
                    continue;

                var seqStart = seqIndicesCount;
                for (var i = 0; i < seqBufCount; i++)
                    sequenceIndices[seqIndicesCount++] = seqBuffer[i];

                var firstIdx = sequenceIndices[seqStart];
                var lastIdx = sequenceIndices[seqStart + seqBufCount - 1];
                var seqLevel = levels[firstIdx];

                var sos = ComputeSequenceBoundaryType(start, end, paragraphBaseLevel, firstIdx, true, bidiClasses, levels, isolateToPdi);
                var eos = ComputeSequenceBoundaryType(start, end, paragraphBaseLevel, lastIdx, false, bidiClasses, levels, isolateToPdi);

                sequences[seqCount++] = new BidiIsoSeq
                    { indexStart = seqStart, indexLength = seqBufCount, level = seqLevel, sos = (byte)sos, eos = (byte)eos };
            }

            return seqCount;
        }

        private static BidiClass ComputeSequenceBoundaryType(int start, int end, byte paragraphBaseLevel,
            int boundaryIndex, bool isStart, byte* bidiClasses, byte* levels, int* isolateToPdiRelative)
        {
            var levelHere = levels[boundaryIndex];
            byte otherLevel = paragraphBaseLevel;

            if (isStart)
            {
                for (var i = boundaryIndex - 1; i >= start; i--)
                    if ((BidiClass)bidiClasses[i] != BidiClass.BoundaryNeutral)
                    {
                        otherLevel = levels[i];
                        break;
                    }
            }
            else
            {
                var unmatchedIsolate =
                    IsIsolateInitiator((BidiClass)bidiClasses[boundaryIndex]) &&
                    isolateToPdiRelative[boundaryIndex - start] < 0;

                if (!unmatchedIsolate)
                    for (var i = boundaryIndex + 1; i <= end; i++)
                    {
                        var bc = (BidiClass)bidiClasses[i];
                        if (bc != BidiClass.BoundaryNeutral)
                        {
                            otherLevel = bc == BidiClass.ParagraphSeparator ? paragraphBaseLevel : levels[i];
                            break;
                        }
                    }
            }

            var higher = levelHere >= otherLevel ? levelHere : otherLevel;
            return (higher & 1) == 0 ? BidiClass.LeftToRight : BidiClass.RightToLeft;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindLastNonBnBefore(int beforeIndex, int start, byte* bidiClasses)
        {
            for (var i = beforeIndex - 1; i >= start; i--)
                if ((BidiClass)bidiClasses[i] != BidiClass.BoundaryNeutral)
                    return i;

            return start;
        }

        #endregion

        #region Weak types (W1–W7)

        private static void ResolveWeakTypesWithSequences(int seqCount, in Scratch s)
        {
            for (var i = 0; i < seqCount; i++)
                ResolveWeakTypesForSequence(s.sequences[i], s);
        }

        private static void ResolveWeakTypesForSequence(BidiIsoSeq seq, in Scratch s)
        {
            var indices = s.sequenceIndices + seq.indexStart;
            var length = seq.indexLength;
            if (length == 0)
                return;

            var bidiClasses = s.bidiClasses;
            var sos = (BidiClass)seq.sos;
            var eos = (BidiClass)seq.eos;

            {
                var prevType = sos;

                for (var i = 0; i < length; i++)
                {
                    var idx = indices[i];
                    var tt = (BidiClass)bidiClasses[idx];

                    if (tt == BidiClass.BoundaryNeutral)
                        continue;

                    if (tt == BidiClass.NonspacingMark)
                    {
                        if (IsIsolateInitiator(prevType) ||
                            prevType == BidiClass.PopDirectionalIsolate)
                            bidiClasses[idx] = (byte)BidiClass.OtherNeutral;
                        else
                            bidiClasses[idx] = (byte)prevType;

                        tt = (BidiClass)bidiClasses[idx];
                    }

                    prevType = tt;
                }
            }

            {
                for (var i = 0; i < length; i++)
                {
                    var idx = indices[i];
                    if ((BidiClass)bidiClasses[idx] != BidiClass.EuropeanNumber)
                        continue;

                    var strong = FindPrevStrongTypeForW2(indices, bidiClasses, i, sos);

                    if (strong == BidiClass.ArabicLetter) bidiClasses[idx] = (byte)BidiClass.ArabicNumber;
                }
            }

            {
                for (var i = 0; i < length; i++)
                {
                    var idx = indices[i];
                    if ((BidiClass)bidiClasses[idx] == BidiClass.ArabicLetter) bidiClasses[idx] = (byte)BidiClass.RightToLeft;
                }
            }

            {
                for (var i = 0; i < length; i++)
                {
                    var idx = indices[i];
                    var tt = (BidiClass)bidiClasses[idx];

                    if (tt != BidiClass.EuropeanSeparator &&
                        tt != BidiClass.CommonSeparator)
                        continue;

                    var before = GetTypeBeforeInSequence(indices, bidiClasses, i, sos);
                    var after = GetTypeAfterInSequence(indices, length, bidiClasses, i, eos);

                    if (tt == BidiClass.EuropeanSeparator)
                    {
                        if (before == BidiClass.EuropeanNumber &&
                            after == BidiClass.EuropeanNumber)
                            bidiClasses[idx] = (byte)BidiClass.EuropeanNumber;
                    }
                    else
                    {
                        if (before == BidiClass.EuropeanNumber &&
                            after == BidiClass.EuropeanNumber)
                            bidiClasses[idx] = (byte)BidiClass.EuropeanNumber;
                        else if (before == BidiClass.ArabicNumber &&
                                 after == BidiClass.ArabicNumber)
                            bidiClasses[idx] = (byte)BidiClass.ArabicNumber;
                    }
                }
            }

            {
                var i = 0;
                while (i < length)
                {
                    var idx = indices[i];
                    var bc = (BidiClass)bidiClasses[idx];

                    if (bc != BidiClass.EuropeanTerminator)
                    {
                        i++;
                        continue;
                    }

                    var runStart = i;
                    var runEnd = i;

                    while (runEnd + 1 < length)
                    {
                        var nextBc = (BidiClass)bidiClasses[indices[runEnd + 1]];
                        if (nextBc == BidiClass.EuropeanTerminator || nextBc == BidiClass.BoundaryNeutral)
                            runEnd++;
                        else
                            break;
                    }

                    var before = GetTypeBeforeInSequence(indices, bidiClasses, runStart, sos);
                    var after = GetTypeAfterInSequence(indices, length, bidiClasses, runEnd, eos);

                    var beforeIsEn = before == BidiClass.EuropeanNumber;
                    var afterIsEn = after == BidiClass.EuropeanNumber;

                    if (beforeIsEn || afterIsEn)
                        for (var p = runStart; p <= runEnd; p++)
                            if ((BidiClass)bidiClasses[indices[p]] == BidiClass.EuropeanTerminator)
                                bidiClasses[indices[p]] = (byte)BidiClass.EuropeanNumber;

                    i = runEnd + 1;
                }
            }

            {
                for (var i = 0; i < length; i++)
                {
                    var idx = indices[i];
                    var tt = (BidiClass)bidiClasses[idx];

                    if (tt == BidiClass.EuropeanSeparator ||
                        tt == BidiClass.CommonSeparator ||
                        tt == BidiClass.EuropeanTerminator)
                        bidiClasses[idx] = (byte)BidiClass.OtherNeutral;
                }
            }

            {
                for (var i = 0; i < length; i++)
                {
                    var idx = indices[i];
                    if ((BidiClass)bidiClasses[idx] != BidiClass.EuropeanNumber)
                        continue;

                    var strong = FindPrevStrongTypeForW7(indices, bidiClasses, i, sos);

                    if (strong == BidiClass.LeftToRight) bidiClasses[idx] = (byte)BidiClass.LeftToRight;
                }
            }
        }

        private static BidiClass GetTypeBeforeInSequence(int* indices, byte* classes, int position, BidiClass sos)
        {
            for (var i = position - 1; i >= 0; i--)
            {
                var tt = (BidiClass)classes[indices[i]];
                if (tt == BidiClass.BoundaryNeutral)
                    continue;

                return tt;
            }

            return sos;
        }

        private static BidiClass GetTypeAfterInSequence(int* indices, int length, byte* classes, int position, BidiClass eos)
        {
            for (var i = position + 1; i < length; i++)
            {
                var tt = (BidiClass)classes[indices[i]];
                if (tt == BidiClass.BoundaryNeutral)
                    continue;

                return tt;
            }

            return eos;
        }

        private static BidiClass FindPrevStrongTypeForW2(int* indices, byte* classes, int position, BidiClass sos)
        {
            for (var i = position - 1; i >= 0; i--)
            {
                var tt = (BidiClass)classes[indices[i]];

                if (tt == BidiClass.BoundaryNeutral)
                    continue;

                if (tt == BidiClass.LeftToRight ||
                    tt == BidiClass.RightToLeft ||
                    tt == BidiClass.ArabicLetter)
                    return tt;
            }

            return sos;
        }

        private static BidiClass FindPrevStrongTypeForW7(int* indices, byte* classes, int position, BidiClass sos)
        {
            for (var i = position - 1; i >= 0; i--)
            {
                var tt = (BidiClass)classes[indices[i]];

                if (IsNeutralType(tt))
                    continue;

                if (tt == BidiClass.LeftToRight ||
                    tt == BidiClass.RightToLeft)
                    return tt;
            }

            return sos;
        }

        #endregion

        #region Paired brackets (N0)

        private static void ResolvePairedBracketsForParagraph(int seqCount, in Tables t, in Scratch s, int* codePoints)
        {
            for (var i = 0; i < seqCount; i++)
                ResolvePairedBracketsForSequence(s.sequences[i], in t, in s, codePoints);
        }

        private static void ResolvePairedBracketsForSequence(BidiIsoSeq sequence, in Tables t, in Scratch s, int* codePoints)
        {
            var bidiClasses = s.bidiClasses;
            var openStack = s.openStack;
            var bracketPairs = s.bracketPairs;
            var openStackCount = 0;
            var bracketPairsCount = 0;

            var indices = s.sequenceIndices + sequence.indexStart;
            var seqLen = sequence.indexLength;

            for (var k = 0; k < seqLen; k++)
            {
                var index = indices[k];

                if ((BidiClass)bidiClasses[index] != BidiClass.OtherNeutral)
                    continue;

                var cp = codePoints[index];
                var bt = GetBidiPairedBracketType(in t, cp);

                if (bt == BidiPairedBracketType.Open)
                {
                    if (openStackCount >= MaxPairingDepth)
                    {
                        bracketPairsCount = 0;
                        openStackCount = 0;
                        break;
                    }

                    openStack[openStackCount++] = index;
                }
                else if (bt == BidiPairedBracketType.Close)
                {
                    for (var si = openStackCount - 1; si >= 0; si--)
                    {
                        var openIndex = openStack[si];
                        var openCp = codePoints[openIndex];

                        if (BracketsMatch(in t, openCp, cp))
                        {
                            bracketPairs[bracketPairsCount++] = new BidiBracketPair { openIndex = openIndex, closeIndex = index };
                            openStackCount = si;
                            break;
                        }
                    }
                }
            }

            if (bracketPairsCount == 0)
                return;

            for (var j = 1; j < bracketPairsCount; j++)
            {
                var key = bracketPairs[j];
                var k = j - 1;
                while (k >= 0 && bracketPairs[k].openIndex > key.openIndex)
                {
                    bracketPairs[k + 1] = bracketPairs[k];
                    k--;
                }
                bracketPairs[k + 1] = key;
            }

            var embeddingDir = GetStrongTypeFromLevel(sequence.level);
            var sos = (BidiClass)sequence.sos;

            for (var p = 0; p < bracketPairsCount; p++)
            {
                var openIndex = bracketPairs[p].openIndex;
                var closeIndex = bracketPairs[p].closeIndex;

                var innerMatch = BidiClass.OtherNeutral;
                var innerOpposite = BidiClass.OtherNeutral;

                for (var k = 0; k < seqLen; k++)
                {
                    var idx = indices[k];

                    if (idx <= openIndex || idx >= closeIndex)
                        continue;

                    var strong = MapToStrongTypeForN0((BidiClass)bidiClasses[idx]);

                    if (strong != BidiClass.LeftToRight && strong != BidiClass.RightToLeft)
                        continue;

                    if (strong == embeddingDir)
                    {
                        innerMatch = embeddingDir;
                        break;
                    }

                    innerOpposite = strong;
                }

                if (innerMatch == embeddingDir)
                {
                    bidiClasses[openIndex] = (byte)embeddingDir;
                    bidiClasses[closeIndex] = (byte)embeddingDir;
                    continue;
                }

                if (innerOpposite == BidiClass.LeftToRight || innerOpposite == BidiClass.RightToLeft)
                {
                    var preceding = BidiClass.OtherNeutral;

                    var openSeqPos = 0;
                    for (; openSeqPos < seqLen; openSeqPos++)
                        if (indices[openSeqPos] == openIndex)
                            break;

                    for (var k = openSeqPos - 1; k >= 0; k--)
                    {
                        var idx = indices[k];

                        if ((BidiClass)bidiClasses[idx] == BidiClass.BoundaryNeutral)
                            continue;

                        var strong = MapToStrongTypeForN0((BidiClass)bidiClasses[idx]);
                        if (strong == BidiClass.LeftToRight || strong == BidiClass.RightToLeft)
                        {
                            preceding = strong;
                            break;
                        }
                    }

                    if (preceding != BidiClass.LeftToRight && preceding != BidiClass.RightToLeft) preceding = sos;

                    bidiClasses[openIndex] = (byte)preceding;
                    bidiClasses[closeIndex] = (byte)preceding;
                    continue;
                }
            }

            for (var p = 0; p < bracketPairsCount; p++)
            {
                var openIndex = bracketPairs[p].openIndex;
                var closeIndex = bracketPairs[p].closeIndex;

                var pairType = (BidiClass)bidiClasses[openIndex];
                if (pairType != BidiClass.LeftToRight && pairType != BidiClass.RightToLeft)
                    continue;

                var openSeqPos = 0;
                for (; openSeqPos < seqLen; openSeqPos++)
                    if (indices[openSeqPos] == openIndex)
                        break;

                var closeSeqPos = 0;
                for (; closeSeqPos < seqLen; closeSeqPos++)
                    if (indices[closeSeqPos] == closeIndex)
                        break;

                var kPos = openSeqPos + 1;
                while (kPos < seqLen)
                {
                    var idx = indices[kPos];
                    var original = GetBidiClass(in t, codePoints[idx]);

                    if (original == BidiClass.BoundaryNeutral)
                    {
                        kPos++;
                        continue;
                    }

                    if (original != BidiClass.NonspacingMark)
                        break;

                    bidiClasses[idx] = (byte)pairType;
                    kPos++;
                }

                kPos = closeSeqPos + 1;
                while (kPos < seqLen)
                {
                    var idx = indices[kPos];
                    var original = GetBidiClass(in t, codePoints[idx]);

                    if (original == BidiClass.BoundaryNeutral)
                    {
                        kPos++;
                        continue;
                    }

                    if (original != BidiClass.NonspacingMark)
                        break;

                    bidiClasses[idx] = (byte)pairType;
                    kPos++;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool BracketsMatch(in Tables t, int openCp, int closeCp)
        {
            var openType = GetBidiPairedBracketType(in t, openCp);
            if (openType != BidiPairedBracketType.None)
            {
                var paired = GetBidiPairedBracket(in t, openCp);
                if (paired == closeCp)
                    return true;

                if (openCp == UnicodeData.LeftPointingAngleBracket && (closeCp == UnicodeData.RightPointingAngleBracket ||
                                                                       closeCp == UnicodeData.RightAngleBracket))
                    return true;
                if (openCp == UnicodeData.LeftAngleBracket && (closeCp == UnicodeData.RightPointingAngleBracket ||
                                                               closeCp == UnicodeData.RightAngleBracket))
                    return true;

                return false;
            }

            return openCp == UnicodeData.LeftParenthesis && closeCp == UnicodeData.RightParenthesis;
        }

        #endregion

        #region Neutral types (N1–N2)

        private static void ResolveNeutralTypesWithSequences(int seqCount, in Scratch s)
        {
            var bidiClasses = s.bidiClasses;

            for (var i = 0; i < seqCount; i++)
            {
                var seq = s.sequences[i];
                var indices = s.sequenceIndices + seq.indexStart;
                var seqLen = seq.indexLength;
                if (seqLen == 0)
                    continue;

                var k = 0;
                while (k < seqLen)
                {
                    var idx = indices[k];
                    var bc = (BidiClass)bidiClasses[idx];

                    if (!IsNeutralType(bc))
                    {
                        k++;
                        continue;
                    }

                    var neutralStartPos = k;
                    var neutralEndPos = k;

                    while (neutralEndPos + 1 < seqLen &&
                           IsNeutralType((BidiClass)bidiClasses[indices[neutralEndPos + 1]]))
                        neutralEndPos++;

                    var sGroup = BidiClass.OtherNeutral;
                    var sGroupFromActualChar = false;

                    for (var pos = neutralStartPos - 1; pos >= 0; pos--)
                    {
                        var j = indices[pos];
                        var tt = (BidiClass)bidiClasses[j];
                        if (tt == BidiClass.BoundaryNeutral) continue;
                        var strong = MapToStrongTypeForNeutrals(tt);
                        if (strong != BidiClass.OtherNeutral)
                        {
                            sGroup = strong;
                            sGroupFromActualChar = true;
                            break;
                        }
                    }

                    if (sGroup == BidiClass.OtherNeutral)
                        sGroup = MapToStrongTypeForNeutrals((BidiClass)seq.sos);

                    var eGroup = BidiClass.OtherNeutral;
                    var eGroupFromActualChar = false;

                    for (var pos = neutralEndPos + 1; pos < seqLen; pos++)
                    {
                        var j = indices[pos];
                        var tt = (BidiClass)bidiClasses[j];
                        if (tt == BidiClass.BoundaryNeutral) continue;
                        var strong = MapToStrongTypeForNeutrals(tt);
                        if (strong != BidiClass.OtherNeutral)
                        {
                            eGroup = strong;
                            eGroupFromActualChar = true;
                            break;
                        }
                    }

                    if (eGroup == BidiClass.OtherNeutral)
                        eGroup = MapToStrongTypeForNeutrals((BidiClass)seq.eos);

                    BidiClass resolvedType;
                    if (sGroup != BidiClass.OtherNeutral && sGroup == eGroup)
                        resolvedType = sGroup;
                    else
                        resolvedType = (seq.level & 1) == 0 ? BidiClass.LeftToRight : BidiClass.RightToLeft;

                    var skipIsolateControls = !sGroupFromActualChar && !eGroupFromActualChar;

                    for (var pos = neutralStartPos; pos <= neutralEndPos; pos++)
                    {
                        var j = indices[pos];
                        var tt = (BidiClass)bidiClasses[j];
                        if (tt == BidiClass.BoundaryNeutral) continue;
                        if (skipIsolateControls && IsIsolateControl(tt)) continue;
                        if (IsNeutralType(tt))
                            bidiClasses[j] = (byte)resolvedType;
                    }

                    k = neutralEndPos + 1;
                }
            }
        }

        #endregion

        #region Implicit levels (I1–I2) and L1

        private static void ResolveImplicitLevelsForParagraph(int start, int end, byte* bidiClasses, byte* levels)
        {
            var i = start;

            while (i <= end)
            {
                var runLevel = levels[i];
                var runStart = i;
                var runEnd = i;

                while (runEnd + 1 <= end && levels[runEnd + 1] == runLevel) runEnd++;

                var isEvenLevel = (runLevel & 1) == 0;

                if (isEvenLevel)
                    for (var j = runStart; j <= runEnd; j++)
                        switch ((BidiClass)bidiClasses[j])
                        {
                            case BidiClass.RightToLeft:
                            case BidiClass.ArabicLetter:
                                levels[j] = (byte)(runLevel + 1);
                                break;
                            case BidiClass.EuropeanNumber:
                            case BidiClass.ArabicNumber:
                                levels[j] = (byte)(runLevel + 2);
                                break;
                        }
                else
                    for (var j = runStart; j <= runEnd; j++)
                        switch ((BidiClass)bidiClasses[j])
                        {
                            case BidiClass.LeftToRight:
                            case BidiClass.EuropeanNumber:
                            case BidiClass.ArabicNumber:
                                levels[j] = (byte)(runLevel + 1);
                                break;
                        }

                i = runEnd + 1;
            }
        }

        private static void ApplyLineBreakRuleL1ForParagraph(byte* originalClasses, int start, int end, byte paragraphBaseLevel, byte* levels)
        {
            if (start > end)
                return;

            var lineStart = start;
            var lineEnd = end;

            for (var i = lineStart; i <= lineEnd; i++)
            {
                var cls = (BidiClass)originalClasses[i];

                if (cls == BidiClass.SegmentSeparator || cls == BidiClass.ParagraphSeparator)
                {
                    levels[i] = paragraphBaseLevel;

                    var j = i - 1;
                    while (j >= lineStart)
                    {
                        var prev = (BidiClass)originalClasses[j];

                        if (IsL1Resettable(prev))
                        {
                            levels[j] = paragraphBaseLevel;
                            j--;
                            continue;
                        }

                        break;
                    }
                }
            }

            var k = lineEnd;
            while (k >= lineStart)
            {
                var cls = (BidiClass)originalClasses[k];

                if (IsL1Resettable(cls))
                {
                    levels[k] = paragraphBaseLevel;
                    k--;
                    continue;
                }

                break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsL1Resettable(BidiClass bc)
        {
            return bc == BidiClass.WhiteSpace ||
                   bc == BidiClass.LeftToRightIsolate ||
                   bc == BidiClass.RightToLeftIsolate ||
                   bc == BidiClass.FirstStrongIsolate ||
                   bc == BidiClass.PopDirectionalIsolate ||
                   bc == BidiClass.LeftToRightEmbedding ||
                   bc == BidiClass.RightToLeftEmbedding ||
                   bc == BidiClass.LeftToRightOverride ||
                   bc == BidiClass.RightToLeftOverride ||
                   bc == BidiClass.PopDirectionalFormat ||
                   bc == BidiClass.BoundaryNeutral;
        }

        #endregion

        #region Shared classifiers (ports of BidiEngine's static helpers)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIsolateInitiator(BidiClass bc)
        {
            return bc == BidiClass.LeftToRightIsolate ||
                   bc == BidiClass.RightToLeftIsolate ||
                   bc == BidiClass.FirstStrongIsolate;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIsolateControl(BidiClass bc)
        {
            return bc == BidiClass.LeftToRightIsolate ||
                   bc == BidiClass.RightToLeftIsolate ||
                   bc == BidiClass.FirstStrongIsolate ||
                   bc == BidiClass.PopDirectionalIsolate;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNeutralType(BidiClass bc)
        {
            switch (bc)
            {
                case BidiClass.WhiteSpace:
                case BidiClass.SegmentSeparator:
                case BidiClass.OtherNeutral:
                case BidiClass.BoundaryNeutral:
                case BidiClass.LeftToRightIsolate:
                case BidiClass.RightToLeftIsolate:
                case BidiClass.FirstStrongIsolate:
                case BidiClass.PopDirectionalIsolate:
                    return true;
                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BidiClass MapToStrongTypeForNeutrals(BidiClass bc)
        {
            switch (bc)
            {
                case BidiClass.LeftToRight:
                    return BidiClass.LeftToRight;
                case BidiClass.RightToLeft:
                    return BidiClass.RightToLeft;
                case BidiClass.EuropeanNumber:
                case BidiClass.ArabicNumber:
                    return BidiClass.RightToLeft;
                default:
                    return BidiClass.OtherNeutral;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BidiClass MapToStrongTypeForN0(BidiClass tt)
        {
            switch (tt)
            {
                case BidiClass.LeftToRight:
                    return BidiClass.LeftToRight;
                case BidiClass.RightToLeft:
                case BidiClass.ArabicLetter:
                case BidiClass.EuropeanNumber:
                case BidiClass.ArabicNumber:
                    return BidiClass.RightToLeft;
                default:
                    return BidiClass.OtherNeutral;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BidiClass GetStrongTypeFromLevel(byte level)
        {
            return (level & 1) == 0 ? BidiClass.LeftToRight : BidiClass.RightToLeft;
        }

        #endregion

        #region Table lookups (bmp fast-path else binary search — mirrors UnicodeDataProvider)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BidiClass GetBidiClass(in Tables t, int cp)
        {
            if ((uint)cp < BmpSize)
                return (BidiClass)t.bmpBidi[cp];

            var idx = FindRange(t.bidiRanges, t.bidiRangesLen, cp);
            return idx >= 0 ? t.bidiRanges[idx].bidiClass : BidiClass.LeftToRight;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BidiPairedBracketType GetBidiPairedBracketType(in Tables t, int cp)
        {
            var idx = FindPoint(t.brackets, t.bracketsLen, cp);
            return idx >= 0 ? t.brackets[idx].bracketType : BidiPairedBracketType.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBidiPairedBracket(in Tables t, int cp)
        {
            var idx = FindPoint(t.brackets, t.bracketsLen, cp);
            return idx >= 0 ? t.brackets[idx].pairedCodePoint : cp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(RangeEntry* entries, int length, int codePoint)
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
        private static int FindPoint(BracketEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var cp = entries[mid].codePoint;
                if (codePoint < cp) hi = mid - 1;
                else if (codePoint > cp) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        #endregion
    }
}
