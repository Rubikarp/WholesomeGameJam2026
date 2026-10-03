using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Performs word wrapping by breaking shaped text into lines based on available width.
    /// </summary>
    /// <remarks>
    /// Uses break opportunities from <see cref="LineBreakAlgorithm"/> to determine where
    /// lines can be split. Handles BiDi reordering of runs within each line according to
    /// the Unicode Bidirectional Algorithm (UAX #9).
    /// </remarks>
    /// <seealso cref="LineBreakAlgorithm"/>
    /// <seealso cref="TextLine"/>
    internal sealed class LineBreaker
    {
        private const float FitEpsilon = 0.05f;

        private TextLine[] tempLines;
        private int tempLineCount;
        private ShapedRun[] tempOrderedRuns;
        private int tempOrderedRunCount;
        private int searchStartRunIdx;

        /// <summary>
        /// Breaks shaped text into lines. <paramref name="measureOnly"/> skips the per-line UAX #9 bidi reorder
        /// (<see cref="ReorderRunsPerLine"/>) and paragraph run-slice fill: line ranges, widths and per-line font
        /// sets are complete (enough for height and preferred-width queries) but <c>orderedRuns</c> stay in logical
        /// order and paragraph <c>orderedRun</c> slices are left stale — never render from a measure-only break.
        /// Used by the autosize search, which re-runs a full break at the chosen size.
        /// </summary>
        public void BreakLines(
            ReadOnlySpan<int> codepoints,
            ReadOnlySpan<ShapedRun> runs,
            ReadOnlySpan<ShapedGlyph> glyphs,
            ReadOnlySpan<float> cpWidths,
            ReadOnlySpan<LineBreakType> breakTypes,
            ReadOnlySpan<TextRange> segmentBreaks,
            float maxWidth,
            Span<Paragraph> paragraphs,
            ref TextLine[] linesOut,
            ref int lineCount,
            ref ShapedRun[] orderedRunsOut,
            ref int orderedRunCount,
            ReadOnlySpan<float> startMargins,
            ReadOnlySpan<byte> hiddenClusters,
            byte hiddenMask,
            bool measureOnly = false)
        {
            tempLines = linesOut;
            tempLineCount = 0;
            tempOrderedRuns = orderedRunsOut;
            tempOrderedRunCount = 0;

            if (runs.IsEmpty)
            {
                for (var p = 0; p < paragraphs.Length; p++)
                {
                    ref var para = ref paragraphs[p];
                    para.lineStart = 0;
                    para.lineCount = 0;
                    para.orderedRunStart = 0;
                    para.orderedRunCount = 0;
                }
                lineCount = 0;
                orderedRunCount = 0;
                return;
            }

            searchStartRunIdx = 0;
            var state = SeedWrapState(codepoints, maxWidth, startMargins);

            for (var p = 0; p < paragraphs.Length; p++)
            {
                ref var para = ref paragraphs[p];
                para.lineStart = tempLineCount;
                WrapParagraph(codepoints, runs, glyphs, hiddenClusters, hiddenMask,
                    cpWidths, breakTypes, segmentBreaks,
                    maxWidth, startMargins, para.cpStart, para.CpEnd, p == paragraphs.Length - 1, ref state);
                para.lineCount = tempLineCount - para.lineStart;
            }

            if (!measureOnly)
            {
                ReorderRunsPerLine(codepoints, glyphs, hiddenClusters, hiddenMask, paragraphs);
                FillParagraphOrderedRunSlices(paragraphs);
            }

            linesOut = tempLines;
            orderedRunsOut = tempOrderedRuns;
            lineCount = tempLineCount;
            orderedRunCount = tempOrderedRunCount;
        }

        /// <summary>
        /// The wrap fold's live state, threaded through consecutive <see cref="WrapParagraph"/> calls.
        /// A paragraph whose trailing hard break survived (modifiers can suppress it via
        /// <c>breakOpportunities</c>) hands over a freshly-reset state; a suppressed break hands the
        /// open line across the boundary, preserving the whole-document wrap semantics.
        /// </summary>
        private struct WrapState
        {
            public int lineStartCp;
            public float lineWidth;
            public int lastBreakCp;
            public float widthAtLastBreak;
            public bool lineIsClean;
            public float rawMargin;
            public float effectiveMaxWidth;
            public bool leftIsHangul;
            public int segIdx;

            /// <summary>First codepoint the fold has not consumed yet; a separator delimiter spanning a hard break advances it past the next paragraph's start.</summary>
            public int resumeCp;
        }

        private static WrapState SeedWrapState(ReadOnlySpan<int> codepoints, float maxWidth, ReadOnlySpan<float> startMargins)
        {
            var rawMargin = startMargins.Length > 0 ? startMargins[0] : 0f;
            return new WrapState
            {
                lineStartCp = 0,
                lastBreakCp = -1,
                lineIsClean = true,
                rawMargin = rawMargin,
                effectiveMaxWidth = maxWidth - rawMargin,
                leftIsHangul = codepoints.Length > 0 &&
                               IsHangulBreakClass(UnicodeData.Provider.GetLineBreakClass(codepoints[0]))
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsHangingWhitespace(int codepoint) => codepoint == UnicodeData.Space || codepoint == UnicodeData.Tab;

        /// <summary>
        /// Sum of <paramref name="cpWidths"/> contributions of trailing hanging whitespace
        /// (UAX #14 hangable spaces / tabs) over <c>[start, end]</c> inclusive. Walks backwards
        /// from <c>end</c> until the first non-hanging codepoint and returns the accumulated
        /// width.
        /// </summary>
        /// <remarks>
        /// Single source of truth for "right-side whitespace that doesn't count toward visible
        /// line width" — used both by the per-line construction in
        /// <see cref="CreateLineFromCodepoints"/> and by the no-wrap measurement path in
        /// <see cref="ComputeMaxLineWidthAtMandatoryBreaks"/>.
        /// </remarks>
        internal static float TrailingHangingWidth(
            ReadOnlySpan<int> codepoints, ReadOnlySpan<float> cpWidths, int start, int end,
            ReadOnlySpan<byte> hiddenClusters, byte hiddenMask)
        {
            float total = 0f;
            for (int j = end; j >= start; j--)
            {
                if (HiddenClusterBits.IsHidden(hiddenClusters, j, hiddenMask)) continue;
                if (!IsHangingWhitespace(codepoints[j])) break;
                total += cpWidths[j];
            }
            return total;
        }

        /// <summary>
        /// Computes the widest line if the text were broken only at UAX #14 mandatory line
        /// breaks (BK / CR / LF / NL) — i.e. the no-wrap width at the shaping font size.
        /// </summary>
        /// <remarks>
        /// Measurement-only path: reuses the same break-classification and trailing-whitespace
        /// rules as <see cref="WrapParagraph"/> with <c>maxWidth = float.PositiveInfinity</c>, but
        /// produces only the scalar maximum and does not allocate <see cref="TextLine"/> /
        /// <see cref="ShapedRun"/> output. Intended for Unity's <c>ILayoutElement</c> preferred-
        /// width queries (see <see cref="TextProcessor.GetMaxLineWidth"/>) where the full
        /// line-break + bidi-reorder pipeline is unnecessary.
        /// </remarks>
        internal static float ComputeMaxLineWidthAtMandatoryBreaks(
            ReadOnlySpan<int> codepoints,
            ReadOnlySpan<float> cpWidths,
            ReadOnlySpan<LineBreakType> breakTypes,
            ReadOnlySpan<float> startMargins,
            bool measureTrailingWhitespace,
            ReadOnlySpan<byte> hiddenClusters,
            byte hiddenMask,
            ReadOnlySpan<int> suppressedBreaks)
        {
            var cpCount = codepoints.Length;
            if (cpCount == 0) return 0f;

            float maxWidth = 0f;
            int lineStart = 0;
            float lineWidth = 0f;
            var suppressedCursor = suppressedBreaks.Length - 1;

            for (int i = 0; i < cpCount; i++)
            {
                lineWidth += cpWidths[i];
                var breakIndex = i + 1;
                while (suppressedCursor >= 0 && suppressedBreaks[suppressedCursor] < breakIndex)
                    suppressedCursor--;
                var suppressed = suppressedCursor >= 0
                                 && suppressedBreaks[suppressedCursor] == breakIndex;
                if (suppressed) suppressedCursor--;
                if (suppressed || GetBreakTypeAfter(breakTypes, i) != LineBreakType.Mandatory)
                    continue;

                float w = lineWidth;
                if (!measureTrailingWhitespace)
                    w -= TrailingHangingWidth(codepoints, cpWidths, lineStart, i,
                        hiddenClusters, hiddenMask);
                if ((uint)lineStart < (uint)startMargins.Length) w += startMargins[lineStart];
                if (w > maxWidth) maxWidth = w;

                lineStart = i + 1;
                lineWidth = 0f;
            }

            if (lineStart < cpCount)
            {
                float w = lineWidth;
                if (!measureTrailingWhitespace)
                    w -= TrailingHangingWidth(codepoints, cpWidths, lineStart, cpCount - 1,
                        hiddenClusters, hiddenMask);
                if ((uint)lineStart < (uint)startMargins.Length) w += startMargins[lineStart];
                if (w > maxWidth) maxWidth = w;
            }

            return maxWidth;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static LineBreakType GetBreakTypeAfter(ReadOnlySpan<LineBreakType> breakTypes, int index)
        {
            var breakIndex = index + 1;
            return (uint)breakIndex < (uint)breakTypes.Length ? breakTypes[breakIndex] : LineBreakType.None;
        }

        /// <summary>
        /// True if the line break class identifies a Hangul syllable or jamo.
        /// </summary>
        /// <remarks>
        /// Test is on <see cref="LineBreakClass"/> rather than <see cref="UnicodeScript"/>
        /// because script analysis propagates Hangul through adjacent whitespace (Common
        /// script), which would collapse all breaks in Korean text including after spaces.
        /// Line break classes are assigned per Unicode codepoint and never propagate, so
        /// <c>SP</c> (space) remains distinct and only real Hangul syllables/jamo pair up.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHangulBreakClass(LineBreakClass cls)
        {
            return cls == LineBreakClass.H2 || cls == LineBreakClass.H3 ||
                   cls == LineBreakClass.JL || cls == LineBreakClass.JV ||
                   cls == LineBreakClass.JT;
        }

        private void WrapParagraph(
            ReadOnlySpan<int> codepoints,
            ReadOnlySpan<ShapedRun> runs,
            ReadOnlySpan<ShapedGlyph> glyphs,
            ReadOnlySpan<byte> hiddenClusters,
            byte hiddenMask,
            ReadOnlySpan<float> cpWidths,
            ReadOnlySpan<LineBreakType> breakTypes,
            ReadOnlySpan<TextRange> segmentBreaks,
            float maxWidth,
            ReadOnlySpan<float> startMargins,
            int paraStart, int paraEnd, bool isLastParagraph,
            ref WrapState state)
        {
            var cpCount = codepoints.Length;
            var unicodeData = UnicodeData.Provider;

            for (var cpIdx = Math.Max(paraStart, state.resumeCp); cpIdx < paraEnd; cpIdx++)
            {
                if (state.segIdx < segmentBreaks.Length)
                {
                    while (state.segIdx < segmentBreaks.Length && segmentBreaks[state.segIdx].start < cpIdx)
                        state.segIdx++;

                    if (state.segIdx < segmentBreaks.Length && segmentBreaks[state.segIdx].start == cpIdx)
                    {
                        var delimEnd = Math.Min(segmentBreaks[state.segIdx].End, cpCount);
                        if (delimEnd < cpIdx) delimEnd = cpIdx;
                        state.segIdx++;

                        bool collapse;
                        var attachToPrevious = false;

                        if (cpIdx == state.lineStartCp)
                        {
                            collapse = attachToPrevious = tempLineCount > 0;
                        }
                        else
                        {
                            collapse = !state.lineIsClean || !SegmentFits(codepoints, cpWidths, breakTypes,
                                segmentBreaks, hiddenClusters, hiddenMask, state.segIdx, cpIdx, delimEnd,
                                state.effectiveMaxWidth - state.lineWidth);
                        }

                        if (collapse)
                        {
                            if (attachToPrevious)
                            {
                                ref var prev = ref tempLines[tempLineCount - 1];
                                prev.range = new TextRange(prev.range.start, prev.range.length + delimEnd - cpIdx);
                            }
                            else
                            {
                                CreateLineFromCodepoints(codepoints, cpWidths, runs, glyphs,
                                    hiddenClusters, hiddenMask,
                                    state.lineStartCp, delimEnd - 1, state.rawMargin, cpIdx);
                            }

                            state.lineStartCp = delimEnd;
                            state.lineWidth = 0;
                            state.lastBreakCp = -1;
                            state.widthAtLastBreak = 0;
                            state.lineIsClean = true;
                            state.rawMargin = (uint)state.lineStartCp < (uint)startMargins.Length ? startMargins[state.lineStartCp] : 0f;
                            state.effectiveMaxWidth = maxWidth - state.rawMargin;

                            cpIdx = delimEnd - 1;
                            state.resumeCp = delimEnd;
                            if (delimEnd < cpCount)
                                state.leftIsHangul = IsHangulBreakClass(unicodeData.GetLineBreakClass(codepoints[delimEnd]));
                            continue;
                        }
                    }
                }

                state.lineWidth += cpWidths[cpIdx];

                var breakType = GetBreakTypeAfter(breakTypes, cpIdx);
                var rightIsHangul = false;
                if (cpIdx + 1 < cpCount)
                {
                    rightIsHangul = IsHangulBreakClass(unicodeData.GetLineBreakClass(codepoints[cpIdx + 1]));
                    if (breakType == LineBreakType.Optional && state.leftIsHangul && rightIsHangul)
                        breakType = LineBreakType.None;
                }
                state.leftIsHangul = rightIsHangul;

                while (state.lineWidth > state.effectiveMaxWidth)
                {
                    var trailingSpaceWidth = TrailingHangingWidth(codepoints, cpWidths,
                        state.lineStartCp, cpIdx, hiddenClusters, hiddenMask);

                    if (state.lineWidth - trailingSpaceWidth <= state.effectiveMaxWidth)
                        break;

                    if (state.lastBreakCp >= 0 && state.lastBreakCp >= state.lineStartCp)
                    {
                        CreateLineFromCodepoints(codepoints, cpWidths, runs, glyphs,
                            hiddenClusters, hiddenMask, state.lineStartCp, state.lastBreakCp, state.rawMargin);
                        state.lineStartCp = state.lastBreakCp + 1;
                        state.lineWidth -= state.widthAtLastBreak;
                        state.lastBreakCp = -1;
                        state.widthAtLastBreak = 0;
                        state.lineIsClean = false;
                        state.rawMargin = (uint)state.lineStartCp < (uint)startMargins.Length ? startMargins[state.lineStartCp] : 0f;
                        state.effectiveMaxWidth = maxWidth - state.rawMargin;
                    }
                    else if (cpIdx > state.lineStartCp)
                    {
                        CreateLineFromCodepoints(codepoints, cpWidths, runs, glyphs,
                            hiddenClusters, hiddenMask, state.lineStartCp, cpIdx - 1, state.rawMargin);
                        state.lineStartCp = cpIdx;
                        state.lineWidth = cpWidths[cpIdx];
                        state.lastBreakCp = -1;
                        state.widthAtLastBreak = 0;
                        state.lineIsClean = false;
                        state.rawMargin = (uint)state.lineStartCp < (uint)startMargins.Length ? startMargins[state.lineStartCp] : 0f;
                        state.effectiveMaxWidth = maxWidth - state.rawMargin;
                    }
                    else
                    {
                        break;
                    }
                }

                if (breakType == LineBreakType.Mandatory)
                {
                    var previousLineCount = tempLineCount;
                    CreateLineFromCodepoints(codepoints, cpWidths, runs, glyphs,
                        hiddenClusters, hiddenMask, state.lineStartCp, cpIdx, state.rawMargin);
                    if (tempLineCount > previousLineCount)
                        tempLines[tempLineCount - 1].endedByMandatoryBreak = true;
                    state.lineStartCp = cpIdx + 1;
                    state.lineWidth = 0;
                    state.lastBreakCp = -1;
                    state.widthAtLastBreak = 0;
                    state.lineIsClean = true;
                    state.rawMargin = (uint)state.lineStartCp < (uint)startMargins.Length ? startMargins[state.lineStartCp] : 0f;
                    state.effectiveMaxWidth = maxWidth - state.rawMargin;
                    continue;
                }

                if (breakType == LineBreakType.Optional)
                {
                    state.lastBreakCp = cpIdx;
                    state.widthAtLastBreak = state.lineWidth;
                }
            }

            if (!isLastParagraph) return;

            if (state.lineStartCp < cpCount)
                CreateLineFromCodepoints(codepoints, cpWidths, runs, glyphs,
                    hiddenClusters, hiddenMask, state.lineStartCp, cpCount - 1, state.rawMargin);

            if (state.lineStartCp == cpCount && cpCount > 0)
            {
                var lastCpClass = unicodeData.GetLineBreakClass(codepoints[cpCount - 1]);
                if ((lastCpClass == LineBreakClass.BK || lastCpClass == LineBreakClass.CR ||
                     lastCpClass == LineBreakClass.LF || lastCpClass == LineBreakClass.NL)
                    && (hiddenMask == 0 || tempLineCount > 0
                        && tempLines[tempLineCount - 1].range.End == cpCount))
                {
                    EnsureLineCapacity(tempLineCount + 1);
                    tempLines[tempLineCount++] = new TextLine
                    {
                        range = new TextRange(cpCount, 0),
                        runStart = tempOrderedRunCount,
                        runCount = 0,
                        width = 0,
                        trailingWhitespace = 0,
                        startMargin = 0
                    };
                }
            }
        }

        /// <summary>
        /// Lookahead for the segment-break decision in <see cref="WrapParagraph"/>: true when the
        /// delimiter at <c>[delimStart, delimEnd)</c> plus its entire following segment (up to
        /// the next segment break or the first mandatory break, minus trailing hangable
        /// whitespace) fits into <paramref name="remainingWidth"/>.
        /// </summary>
        private static bool SegmentFits(
            ReadOnlySpan<int> codepoints,
            ReadOnlySpan<float> cpWidths,
            ReadOnlySpan<LineBreakType> breakTypes,
            ReadOnlySpan<TextRange> segmentBreaks,
            ReadOnlySpan<byte> hiddenClusters,
            byte hiddenMask,
            int nextBreakIdx, int delimStart, int delimEnd,
            float remainingWidth)
        {
            var cpCount = codepoints.Length;
            var segmentEnd = nextBreakIdx < segmentBreaks.Length
                ? Math.Min(segmentBreaks[nextBreakIdx].start, cpCount)
                : cpCount;
            if (segmentEnd < delimEnd) segmentEnd = delimEnd;

            float width = 0;
            for (var i = delimStart; i < delimEnd; i++) width += cpWidths[i];

            var contentEnd = delimEnd;
            for (var i = delimEnd; i < segmentEnd; i++)
            {
                width += cpWidths[i];
                contentEnd = i + 1;
                if (GetBreakTypeAfter(breakTypes, i) == LineBreakType.Mandatory) break;
            }

            width -= TrailingHangingWidth(codepoints, cpWidths, delimEnd, contentEnd - 1,
                hiddenClusters, hiddenMask);
            return width <= remainingWidth + FitEpsilon;
        }

        private void CreateLineFromCodepoints(
            ReadOnlySpan<int> codepoints,
            ReadOnlySpan<float> cpWidths,
            ReadOnlySpan<ShapedRun> runs,
            ReadOnlySpan<ShapedGlyph> glyphs,
            ReadOnlySpan<byte> hiddenClusters,
            byte hiddenMask,
            int startCp, int endCp, float startMargin = 0f, int hiddenFromCp = int.MaxValue)
        {
            if (startCp > endCp) return;
            if (hiddenMask != 0 && !HasLayoutContent(codepoints, hiddenClusters, hiddenMask,
                    startCp, endCp))
                return;

            var lineRunStart = tempOrderedRunCount;
            var lineRunCount = 0;

            for (var runIdx = searchStartRunIdx; runIdx < runs.Length; runIdx++)
            {
                var run = runs[runIdx];
                var runStart = run.range.start;
                var runEnd = run.range.End - 1;

                if (runEnd < startCp)
                {
                    searchStartRunIdx = runIdx + 1;
                    continue;
                }

                if (runStart > endCp)
                    break;

                var effEnd = hiddenFromCp - 1 < endCp ? hiddenFromCp - 1 : endCp;
                if (!LocateGlyphSpan(glyphs, run.glyphStart, run.glyphCount, startCp, effEnd,
                        run.direction != TextDirection.RightToLeft, out var glyphFirst, out var glyphLast))
                    continue;

                if (hiddenMask != 0)
                {
                    while (glyphFirst <= glyphLast &&
                           HiddenClusterBits.IsHidden(hiddenClusters, glyphs[run.glyphStart + glyphFirst].cluster, hiddenMask))
                        glyphFirst++;
                    while (glyphLast >= glyphFirst &&
                           HiddenClusterBits.IsHidden(hiddenClusters, glyphs[run.glyphStart + glyphLast].cluster, hiddenMask))
                        glyphLast--;
                    if (glyphFirst > glyphLast) continue;
                }

                var glyphCount = glyphLast - glyphFirst + 1;

                float partialWidth = 0;
                for (var g = run.glyphStart + glyphFirst; g <= run.glyphStart + glyphLast; g++)
                {
                    if (hiddenMask != 0 &&
                        HiddenClusterBits.IsHidden(hiddenClusters, glyphs[g].cluster, hiddenMask))
                        continue;
                    partialWidth += glyphs[g].advanceX;
                }

                EnsureOrderedRunCapacity(tempOrderedRunCount + 1);
                tempOrderedRuns[tempOrderedRunCount++] = new ShapedRun
                {
                    range = run.range,
                    glyphStart = run.glyphStart + glyphFirst,
                    glyphCount = glyphCount,
                    width = partialWidth,
                    direction = run.direction,
                    bidiLevel = run.bidiLevel,
                    language = run.language,
                    fontId = run.fontId
                };
                lineRunCount++;
            }

            float actualLineWidth = 0;
            for (var i = lineRunStart; i < tempOrderedRunCount; i++) actualLineWidth += tempOrderedRuns[i].width;

            float trailingWsWidth = TrailingHangingWidth(codepoints, cpWidths, startCp,
                Math.Min(endCp, hiddenFromCp - 1), hiddenClusters, hiddenMask);

            EnsureLineCapacity(tempLineCount + 1);
            tempLines[tempLineCount++] = new TextLine
            {
                range = new TextRange(startCp, endCp - startCp + 1),
                runStart = lineRunStart,
                runCount = lineRunCount,
                width = actualLineWidth - trailingWsWidth,
                trailingWhitespace = trailingWsWidth,
                startMargin = startMargin
            };
        }

        private static bool HasLayoutContent(ReadOnlySpan<int> codepoints,
            ReadOnlySpan<byte> hiddenClusters, byte hiddenMask, int start, int end)
        {
            var containsHidden = false;
            for (var cp = start; cp <= end; cp++)
            {
                if (HiddenClusterBits.IsHidden(hiddenClusters, cp, hiddenMask))
                {
                    containsHidden = true;
                    continue;
                }

                if (!UnicodeData.IsMandatoryBreakChar(codepoints[cp])) return true;
            }

            return !containsHidden;
        }

        /// <summary>
        /// Locates the contiguous glyph sub-range of a run whose clusters fall in <c>[lo, hi]</c>, returned as
        /// run-relative indices. HarfBuzz emits monotonic clusters within a run — ascending for LTR, descending
        /// for RTL — so the in-range glyphs are one contiguous block found by two boundary binary searches
        /// instead of scanning the run's full glyph count per line (this is what keeps line construction from
        /// being O(lines × glyphs) when one run spans many lines).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool LocateGlyphSpan(ReadOnlySpan<ShapedGlyph> glyphs, int gStart, int gCount,
            int lo, int hi, bool ascending, out int first, out int last)
        {
            first = 0;
            last = -1;
            if (gCount <= 0 || lo > hi) return false;

            int a = 0, b = gCount, c = 0, d = gCount;
            if (ascending)
            {
                while (a < b)
                {
                    var m = (a + b) >> 1;
                    if (glyphs[gStart + m].cluster >= lo) b = m; else a = m + 1;
                }
                while (c < d)
                {
                    var m = (c + d) >> 1;
                    if (glyphs[gStart + m].cluster > hi) d = m; else c = m + 1;
                }
            }
            else
            {
                while (a < b)
                {
                    var m = (a + b) >> 1;
                    if (glyphs[gStart + m].cluster <= hi) b = m; else a = m + 1;
                }
                while (c < d)
                {
                    var m = (c + d) >> 1;
                    if (glyphs[gStart + m].cluster < lo) d = m; else c = m + 1;
                }
            }

            first = a;
            last = c - 1;
            return first <= last;
        }

        /// <summary>
        /// Per-line UAX #9 L1 + L2 with the paragraph's base level from a tandem cursor. The synthetic
        /// trailing empty line (after a final hard break) adopts the LAST paragraph's level — the caret
        /// there continues the adjacent paragraph's direction, matching mainstream editors (owner-approved
        /// change from the pre-paragraph behavior, which used the first paragraph's level).
        /// </summary>
        private void ReorderRunsPerLine(ReadOnlySpan<int> codepoints, ReadOnlySpan<ShapedGlyph> glyphs,
            ReadOnlySpan<byte> hiddenClusters, byte hiddenMask, ReadOnlySpan<Paragraph> paragraphs)
        {
            var pIdx = 0;
            for (var i = 0; i < tempLineCount; i++)
            {
                ref var line = ref tempLines[i];
                while (pIdx + 1 < paragraphs.Length && line.range.start >= paragraphs[pIdx].CpEnd)
                    pIdx++;
                var paragraphBaseLevel = paragraphs.IsEmpty ? (byte)0 : paragraphs[pIdx].baseLevel;

                ApplyL1ForLine(codepoints, glyphs, hiddenClusters, hiddenMask, ref line, paragraphBaseLevel);
                ReorderRunsInLine(line.runStart, line.runCount, paragraphBaseLevel);

                line.paragraphBaseLevel = paragraphBaseLevel;
            }
        }

        /// <summary>
        /// Derives each paragraph's orderedRuns slice from its (final, post-L1) line slice. Runs after
        /// <see cref="ApplyL1ForLine"/> because whitespace-run splits shift <c>runStart</c> values.
        /// </summary>
        private void FillParagraphOrderedRunSlices(Span<Paragraph> paragraphs)
        {
            var runningEnd = 0;
            for (var p = 0; p < paragraphs.Length; p++)
            {
                ref var para = ref paragraphs[p];
                if (para.lineCount > 0)
                {
                    ref readonly var first = ref tempLines[para.lineStart];
                    ref readonly var last = ref tempLines[para.lineStart + para.lineCount - 1];
                    para.orderedRunStart = first.runStart;
                    para.orderedRunCount = last.runStart + last.runCount - first.runStart;
                    runningEnd = para.orderedRunStart + para.orderedRunCount;
                }
                else
                {
                    para.orderedRunStart = runningEnd;
                    para.orderedRunCount = 0;
                }
            }
        }

        /// <summary>
        /// UAX #9 Rule L1 sub-rule 4: at the end of each line, any sequence of whitespace,
        /// isolate formatting, embedding formatting, or boundary neutral characters is reset
        /// to the paragraph embedding level. Applied per-line after wrapping, before L2 reorder.
        /// </summary>
        private void ApplyL1ForLine(ReadOnlySpan<int> codepoints, ReadOnlySpan<ShapedGlyph> glyphs,
            ReadOnlySpan<byte> hiddenClusters, byte hiddenMask, ref TextLine line, byte paragraphBaseLevel)
        {
            var lineEnd = line.range.start + line.range.length - 1;

            var firstWsCp = lineEnd + 1;
            var unicodeData = UnicodeData.Provider;
            for (var cp = lineEnd; cp >= line.range.start; cp--)
            {
                if (HiddenClusterBits.IsHidden(hiddenClusters, cp, hiddenMask)) continue;
                if (!IsL1Trailing(unicodeData.GetBidiClass(codepoints[cp]))) break;
                firstWsCp = cp;
            }

            if (firstWsCp > lineEnd) return;

            var runEnd = line.runStart + line.runCount;
            for (var r = line.runStart; r < runEnd; r++)
            {
                ref var run = ref tempOrderedRuns[r];
                if (run.bidiLevel == paragraphBaseLevel) continue;

                var gStart = run.glyphStart;
                var gEnd = gStart + run.glyphCount;
                int wsCount = 0, contentCount = 0;

                for (var g = gStart; g < gEnd; g++)
                {
                    if (glyphs[g].cluster >= firstWsCp) wsCount++;
                    else contentCount++;
                }

                if (wsCount == 0) continue;

                if (contentCount == 0)
                {
                    run.bidiLevel = paragraphBaseLevel;
                    continue;
                }

                int contentStart, wsStart;
                if (run.direction == TextDirection.RightToLeft)
                {
                    wsStart = gStart;
                    contentStart = gStart + wsCount;
                }
                else
                {
                    contentStart = gStart;
                    wsStart = gStart + contentCount;
                }

                float wsWidth = 0;
                for (var g = wsStart; g < wsStart + wsCount; g++)
                {
                    if (hiddenMask != 0 &&
                        HiddenClusterBits.IsHidden(hiddenClusters, glyphs[g].cluster, hiddenMask))
                        continue;
                    wsWidth += glyphs[g].advanceX;
                }

                var wsRun = new ShapedRun
                {
                    range = run.range,
                    glyphStart = wsStart,
                    glyphCount = wsCount,
                    width = wsWidth,
                    direction = run.direction,
                    bidiLevel = paragraphBaseLevel,
                    language = run.language,
                    fontId = run.fontId
                };

                run.glyphStart = contentStart;
                run.glyphCount = contentCount;
                run.width -= wsWidth;

                EnsureOrderedRunCapacity(tempOrderedRunCount + 1);
                for (var j = tempOrderedRunCount; j > r + 1; j--)
                    tempOrderedRuns[j] = tempOrderedRuns[j - 1];
                tempOrderedRuns[r + 1] = wsRun;
                tempOrderedRunCount++;
                line.runCount++;

                for (var li = 0; li < tempLineCount; li++)
                    if (tempLines[li].runStart > r)
                        tempLines[li].runStart++;

                runEnd++;
                r++;
            }
        }

        /// <summary>
        /// Returns true if the given original bidi class is eligible for L1 trailing reset.
        /// Per UAX #9 L1: WS, isolate formatting (FSI/LRI/RLI/PDI), embedding formatting
        /// (LRE/RLE/LRO/RLO/PDF), and boundary neutral (BN).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsL1Trailing(BidiClass cls) => cls switch
        {
            BidiClass.WhiteSpace => true,
            BidiClass.BoundaryNeutral => true,
            BidiClass.LeftToRightIsolate => true,
            BidiClass.RightToLeftIsolate => true,
            BidiClass.FirstStrongIsolate => true,
            BidiClass.PopDirectionalIsolate => true,
            BidiClass.LeftToRightEmbedding => true,
            BidiClass.RightToLeftEmbedding => true,
            BidiClass.LeftToRightOverride => true,
            BidiClass.RightToLeftOverride => true,
            BidiClass.PopDirectionalFormat => true,
            _ => false
        };

        private void ReorderRunsInLine(int start, int count, byte paragraphBaseLevel)
        {
            if (count <= 1) return;

            var maxLevel = paragraphBaseLevel;
            var minLevel = paragraphBaseLevel;

            for (var i = 0; i < count; i++)
            {
                var level = tempOrderedRuns[start + i].bidiLevel;
                if (level > maxLevel) maxLevel = level;
                if (level < minLevel) minLevel = level;
            }

            var lowestOddLevel = (minLevel & 1) == 1 ? minLevel : (byte)(minLevel + 1);
            if (lowestOddLevel > maxLevel) return;

            for (var level = maxLevel; level >= lowestOddLevel; level--)
            {
                var runStart = -1;

                for (var i = 0; i <= count; i++)
                {
                    var inSequence = i < count && tempOrderedRuns[start + i].bidiLevel >= level;

                    if (inSequence && runStart < 0)
                    {
                        runStart = i;
                    }
                    else if (!inSequence && runStart >= 0)
                    {
                        ReverseRuns(start + runStart, i - runStart);
                        runStart = -1;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReverseRuns(int start, int count)
        {
            var arr = tempOrderedRuns;
            var end = start + count - 1;
            while (start < end)
            {
                (arr[start], arr[end]) = (arr[end], arr[start]);
                start++;
                end--;
            }
        }

        private void EnsureLineCapacity(int required)
        {
            if (tempLines != null && tempLines.Length >= required) return;

            var newSize = Math.Max(required, tempLines?.Length * 2 ?? 128);
            var newBuffer = ArrayPool<TextLine>.Rent(newSize);

            if (tempLines != null)
            {
                tempLines.AsSpan(0, tempLineCount).CopyTo(newBuffer);
                ArrayPool<TextLine>.Return(tempLines);
            }

            tempLines = newBuffer;
        }

        private void EnsureOrderedRunCapacity(int required)
        {
            if (tempOrderedRuns != null && tempOrderedRuns.Length >= required) return;

            var newSize = Math.Max(required, tempOrderedRuns?.Length * 2 ?? 512);
            var newBuffer = ArrayPool<ShapedRun>.Rent(newSize);

            if (tempOrderedRuns != null)
            {
                tempOrderedRuns.AsSpan(0, tempOrderedRunCount).CopyTo(newBuffer);
                ArrayPool<ShapedRun>.Return(tempOrderedRuns);
            }

            tempOrderedRuns = newBuffer;
        }
    }

}
