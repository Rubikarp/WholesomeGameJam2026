#nullable enable
using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Specifies the paragraph direction for bidirectional text processing.
    /// </summary>
    /// <seealso cref="BidiEngine"/>
    internal enum BidiParagraphDirection
    {
        /// <summary>Force left-to-right paragraph direction.</summary>
        LeftToRight = 0,
        /// <summary>Force right-to-left paragraph direction.</summary>
        RightToLeft = 1,
        /// <summary>Detect paragraph direction automatically from first strong character.</summary>
        Auto = 2
    }

    /// <summary>
    /// Represents a paragraph within bidirectional text with its resolved base direction.
    /// </summary>
    /// <remarks>
    /// A paragraph is a unit of text separated by paragraph separators (U+2029 or hard line breaks).
    /// The base level determines the default direction for neutral characters.
    /// </remarks>
    internal readonly struct BidiParagraph
    {
        /// <summary>Start index of the paragraph in the codepoint array.</summary>
        public readonly int startIndex;
        /// <summary>End index (inclusive) of the paragraph in the codepoint array.</summary>
        public readonly int endIndex;
        /// <summary>Resolved embedding level (0 = LTR, 1 = RTL).</summary>
        public readonly byte baseLevel;

        /// <summary>
        /// Initializes a new paragraph with the specified range and base level.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BidiParagraph(int startIndex, int endIndex, byte baseLevel)
        {
            this.startIndex = startIndex;
            this.endIndex = endIndex;
            this.baseLevel = baseLevel;
        }

        /// <summary>Gets the resolved direction of this paragraph.</summary>
        public BidiDirection Direction
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (baseLevel & 1) == 0 ? BidiDirection.LeftToRight : BidiDirection.RightToLeft;
        }

        /// <summary>Gets the length of the paragraph in codepoints.</summary>
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => endIndex - startIndex + 1;
        }
    }

    /// <summary>
    /// Contains the results of bidirectional text processing.
    /// </summary>
    /// <remarks>
    /// The <see cref="levels"/> array contains per-character embedding levels (0-125).
    /// Odd levels indicate RTL runs, even levels indicate LTR runs.
    /// Use <see cref="BidiEngine.ReorderLine"/> to convert levels to visual order.
    /// </remarks>
    internal readonly struct BidiResult
    {
        /// <summary>Per-codepoint embedding levels. Odd = RTL, even = LTR.</summary>
        public readonly byte[] levels;
        /// <summary>Number of valid levels in the array (may be less than array length for pooled buffers).</summary>
        public readonly int levelsLength;
        /// <summary>Array of paragraphs found in the text.</summary>
        public readonly BidiParagraph[] paragraphs;
        /// <summary>Number of valid paragraphs in the array.</summary>
        public readonly int paragraphCount;

        /// <summary>
        /// Initializes a new result with the given levels and paragraphs.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BidiResult(byte[] levels, BidiParagraph[] paragraphs)
        {
            this.levels = levels;
            levelsLength = levels?.Length ?? 0;
            this.paragraphs = paragraphs;
            paragraphCount = paragraphs?.Length ?? 0;
        }

        /// <summary>
        /// Initializes a new result with pooled arrays (used internally for zero-allocation processing).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BidiResult(byte[] levels, int levelsLength, BidiParagraph[] paragraphs, int paragraphCount)
        {
            this.levels = levels;
            this.levelsLength = levelsLength;
            this.paragraphs = paragraphs;
            this.paragraphCount = paragraphCount;
        }

        /// <summary>Gets the direction of the first paragraph, or LTR if empty.</summary>
        public BidiDirection Direction
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => paragraphCount > 0 ? paragraphs[0].Direction : BidiDirection.LeftToRight;
        }

        /// <summary>Gets the valid paragraphs as a span.</summary>
        public ReadOnlySpan<BidiParagraph> ParagraphsSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => paragraphs != null ? paragraphs.AsSpan(0, paragraphCount) : ReadOnlySpan<BidiParagraph>.Empty;
        }

        /// <summary>Returns true if any character has an odd (RTL) embedding level.</summary>
        public bool HasRtlContent
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var lvls = levels;
                for (var i = 0; i < lvls.Length; i++)
                    if ((lvls[i] & 1) != 0)
                        return true;

                return false;
            }
        }
    }

    /// <summary>
    /// Implements the Unicode Bidirectional Algorithm (UAX #9) for mixed-direction text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The BiDi algorithm determines the correct visual ordering of text containing
    /// both left-to-right (Latin, Cyrillic) and right-to-left (Arabic, Hebrew) scripts.
    /// </para>
    /// <para>
    /// This implementation passes 100% of the Unicode BiDi conformance tests and supports:
    /// <list type="bullet">
    /// <item>Explicit embedding levels (LRE, RLE, LRO, RLO, PDF)</item>
    /// <item>Isolate controls (LRI, RLI, FSI, PDI)</item>
    /// <item>Paired bracket resolution (rule N0)</item>
    /// <item>Multiple paragraphs with independent base directions</item>
    /// </list>
    /// </para>
    /// <para>
    /// Processing runs through <see cref="UniTextBidiBurst"/>. Rule L2 (<see cref="ReorderLine"/>) remains
    /// here because it is a display-order transform rather than part of paragraph analysis.
    /// </para>
    /// </remarks>
    /// <seealso cref="BidiResult"/>
    /// <seealso cref="BidiParagraph"/>
    internal sealed class BidiEngine
    {
        private readonly UnicodeDataProvider unicodeData;

        [ThreadStatic] private static byte[]? levelsBuffer;
        [ThreadStatic] private static BidiParagraph[]? paragraphsResultBuffer;

        private static void EnsureLevelsCapacity(int length)
        {
            if (levelsBuffer == null || levelsBuffer.Length < length)
                levelsBuffer = new byte[Math.Max(length, 256)];
        }

        private static void EnsureParagraphsResultCapacity(int count)
        {
            if (paragraphsResultBuffer == null || paragraphsResultBuffer.Length < count)
                paragraphsResultBuffer = new BidiParagraph[Math.Max(count, 8)];
        }

        /// <summary>
        /// Initializes a new BidiEngine with a specific Unicode data provider.
        /// </summary>
        public BidiEngine(UnicodeDataProvider unicodeData)
        {
            this.unicodeData = unicodeData ?? throw new ArgumentNullException(nameof(unicodeData));
        }

        /// <summary>
        /// Initializes a new BidiEngine using the global <see cref="UnicodeData.Provider"/>.
        /// </summary>
        public BidiEngine()
        {
            unicodeData = UnicodeData.Provider;
        }

        /// <summary>
        /// Processes codepoints through the BiDi algorithm and returns embedding levels.
        /// Uses pooled arrays internally - zero allocation per call.
        /// </summary>
        /// <param name="codePoints">The text as Unicode codepoints.</param>
        /// <param name="direction">Paragraph direction hint, or Auto to detect.</param>
        /// <returns>BiDi result containing per-character levels and paragraph info.
        /// The returned arrays are pooled and will be reused on the next call.</returns>
        public BidiResult Process(ReadOnlySpan<int> codePoints,
            BidiParagraphDirection direction = BidiParagraphDirection.Auto)
        {
            byte? forcedLevel = direction switch
            {
                BidiParagraphDirection.LeftToRight => 0,
                BidiParagraphDirection.RightToLeft => 1,
                _ => null
            };
            return ProcessInternal(codePoints, forcedLevel);
        }

        /// <summary>
        /// Processes codepoints with an integer direction hint.
        /// Uses pooled arrays internally - zero allocation per call.
        /// </summary>
        /// <param name="codePoints">The text as Unicode codepoints.</param>
        /// <param name="paragraphDirection">0 = LTR, 1 = RTL, 2 = Auto.</param>
        /// <returns>BiDi result containing per-character levels and paragraph info.
        /// The returned arrays are pooled and will be reused on the next call.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BidiResult Process(ReadOnlySpan<int> codePoints, int paragraphDirection)
        {
            byte? forcedLevel = paragraphDirection switch
            {
                0 => 0,
                1 => 1,
                2 => null,
                _ => throw new ArgumentOutOfRangeException(nameof(paragraphDirection))
            };
            return ProcessInternal(codePoints, forcedLevel);
        }

        /// <summary>
        /// Quickly detects the dominant direction without full BiDi processing.
        /// </summary>
        /// <param name="codePoints">The text as Unicode codepoints.</param>
        /// <returns>Direction based on the first strong character found.</returns>
        public BidiDirection DetectDirection(ReadOnlySpan<int> codePoints)
        {
            for (var i = 0; i < codePoints.Length; i++)
            {
                var bc = unicodeData.GetBidiClass(codePoints[i]);
                if (bc == BidiClass.LeftToRight)
                    return BidiDirection.LeftToRight;
                if (bc == BidiClass.RightToLeft || bc == BidiClass.ArabicLetter)
                    return BidiDirection.RightToLeft;
            }

            return BidiDirection.LeftToRight;
        }

        /// <summary>
        /// Computes the visual display order for a span of codepoints.
        /// </summary>
        /// <param name="codePoints">The text as Unicode codepoints.</param>
        /// <param name="direction">Paragraph direction hint.</param>
        /// <returns>
        /// Array where indexMap[visual] = logical. Use to reorder characters for display.
        /// </returns>
        public int[] GetVisualOrder(ReadOnlySpan<int> codePoints,
            BidiParagraphDirection direction = BidiParagraphDirection.Auto)
        {
            if (codePoints.Length == 0)
                return Array.Empty<int>();

            var result = Process(codePoints, direction);
            var order = new int[codePoints.Length];
            ReorderLine(result.levels, 0, codePoints.Length - 1, order);
            return order;
        }

        /// <summary>
        /// Converts embedding levels to visual order indices using the L2 algorithm.
        /// </summary>
        /// <param name="levels">Per-character embedding levels.</param>
        /// <param name="start">Start index in the levels array.</param>
        /// <param name="end">End index (inclusive) in the levels array.</param>
        /// <param name="indexMap">Output array: indexMap[visual] = logical position.</param>
        /// <remarks>
        /// Implements UAX #9 rule L2: from the highest level down to the lowest odd level,
        /// reverse any contiguous sequence of characters at that level or higher.
        /// </remarks>
        public static void ReorderLine(byte[] levels, int start, int end, int[] indexMap)
        {
            var length = end - start + 1;
            if (length <= 0)
                return;

            for (var i = 0; i < length; i++)
                indexMap[i] = start + i;

            byte maxLevel = 0;
            var minOddLevel = byte.MaxValue;

            for (var i = start; i <= end; i++)
            {
                var level = levels[i];
                if (level > maxLevel)
                    maxLevel = level;
                if ((level & 1) != 0 && level < minOddLevel)
                    minOddLevel = level;
            }

            if (minOddLevel == byte.MaxValue)
                return;

            for (var level = maxLevel; level >= minOddLevel; level--)
            {
                var i = 0;
                while (i < length)
                    if (levels[indexMap[i]] >= level)
                    {
                        var runStart = i;
                        var runEnd = i + 1;
                        while (runEnd < length && levels[indexMap[runEnd]] >= level)
                            runEnd++;

                        var left = runStart;
                        var right = runEnd - 1;
                        while (left < right)
                        {
                            (indexMap[left], indexMap[right]) = (indexMap[right], indexMap[left]);
                            left++;
                            right--;
                        }

                        i = runEnd;
                    }
                    else
                    {
                        i++;
                    }

                if (level == 0)
                    break;
            }
        }

        /// <summary>
        /// Results use thread-static storage and remain valid until the next call on the same thread.
        /// </summary>
        private unsafe BidiResult ProcessInternal(ReadOnlySpan<int> codePoints, byte? forcedParagraphLevel)
        {
            UniTextDebug.Increment(ref UniTextDebug.Bidi_ProcessCount);

            var length = codePoints.Length;
            if (length == 0)
                return new BidiResult(Array.Empty<byte>(), Array.Empty<BidiParagraph>());

            var dir = forcedParagraphLevel switch
            {
                0 => 0,
                1 => 1,
                _ => 2
            };

            EnsureLevelsCapacity(length);
            EnsureParagraphsResultCapacity(length);
            var levels = levelsBuffer!;
            var paragraphsResult = paragraphsResultBuffer!;

            var s = BidiScratch.Get(length);
            var paragraphCount = 0;

            fixed (int* cp = codePoints)
            fixed (byte* lv = levels)
            fixed (byte* bc = s.bidiClasses)
            fixed (byte* oc = s.originalClasses)
            fixed (int* i2p = s.isolateToPdi)
            fixed (int* p2i = s.pdiToIsolate)
            fixed (int* ist = s.isolateStack)
            fixed (BidiLevelRun* lr = s.levelRuns)
            fixed (int* rip = s.runIndexByPosition)
            fixed (int* sb = s.seqBuffer)
            fixed (int* si = s.sequenceIndices)
            fixed (BidiIsoSeq* sq = s.sequences)
            fixed (BidiBracketPair* bpr = s.bracketPairs)
            fixed (int* os = s.openStack)
            fixed (BidiParagraph* po = paragraphsResult)
            {
                UniTextBidiBurst.Resolve(cp, length, dir,
                    unicodeData.BmpBidiClassPtr, unicodeData.BidiClassRangesPtr, unicodeData.BidiClassRangesLength,
                    unicodeData.BracketsPtr, unicodeData.BracketsLength,
                    bc, oc, i2p, p2i, ist, lr, rip, sb, si, sq, bpr, os,
                    lv, po, &paragraphCount);
            }

            return new BidiResult(levels, length, paragraphsResult, paragraphCount);
        }
    }
}
