using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Line and codepoint navigation helpers shared by the caret hit-test path
    /// (<see cref="UniTextBase.HitTestCaret(Vector2, Camera)"/>), the editing layer
    /// (vertical caret movement, line edges), and selection-extension code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Top-level screen-to-codepoint resolution lives on <see cref="UniTextBase"/> as the
    /// <c>HitTestRange</c> / <c>HitTestCaret</c> instance method pair (range vs. caret are
    /// genuinely different operations — the bounding-box hit suits link / range detection
    /// while edge-snapping suits caret placement). Helpers in this class compose into both.
    /// </para>
    /// <para>
    /// All methods are pure with respect to layout state — none mutate UniText buffers.
    /// </para>
    /// </remarks>
    public static class SelectionHitTest
    {
        /// <summary>
        /// Determines which text line corresponds to the given local-space point.
        /// <paramref name="textRect"/> must be the rect the glyph coordinates are relative to
        /// (the padded rect).
        /// </summary>
        public static int FindLineAtLocalY(
            Vector2 localPos, Rect textRect,
            ReadOnlySpan<PositionedGlyph> glyphs, UniTextBuffers buffers)
            => FindLineAtTextY(textRect.yMax - localPos.y, glyphs, buffers);

        /// <summary>
        /// Resolves the line whose vertical band contains <paramref name="textY"/> (measured
        /// down from the text rect top). Bands accumulate per-line advances from
        /// <see cref="FirstBandTop"/>; the first band extends upward and the last band downward
        /// without bound, so callers needing rejection outside the text apply their own extent
        /// checks. Binary-searches <see cref="TextLine.advancePrefix"/>, O(log lines) per pointer
        /// event, and answers the last line while the layout carries no advances yet.
        /// </summary>
        internal static int FindLineAtTextY(float textY, ReadOnlySpan<PositionedGlyph> glyphs, UniTextBuffers buffers)
        {
            var lineCount = buffers.lines.count;
            if (lineCount <= 1)
                return 0;

            var y = textY - FirstBandTop(glyphs, buffers);

            var lines = buffers.lines;
            var lo = 0;
            var hi = lineCount - 2;
            var result = lineCount - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                if (y < lines[mid].advancePrefix)
                {
                    result = mid;
                    hi = mid - 1;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            return result;
        }

        /// <summary>
        /// Top of the first line's vertical band: the first positioned glyph's top pulled back
        /// up by the advances of any leading empty lines, which contribute bands but own no
        /// glyphs.
        /// </summary>
        internal static float FirstBandTop(ReadOnlySpan<PositionedGlyph> glyphs, UniTextBuffers buffers)
        {
            if (glyphs.Length == 0) return 0f;

            var top = glyphs[0].top;
            var lines = buffers.lines;

            for (int i = 0; i < lines.count && lines[i].glyphCount <= 0; i++)
                top -= lines[i].advance;

            return top;
        }

        /// <summary>
        /// Caret position at the end of a line's visible content: a trailing mandatory break is
        /// excluded so the caret stays on this line rather than the start of the next.
        /// </summary>
        internal static int LineCaretEnd(in TextLine line, UniTextBuffers buffers)
        {
            if (line.range.length == 0)
                return line.range.start;

            var end = line.range.End;
            var lastCp = end - 1;
            if (buffers != null && lastCp < buffers.codepoints.count
                && UnicodeData.IsMandatoryBreakChar(buffers.codepoints[lastCp]))
            {
                return lastCp;
            }

            return end;
        }

        /// <summary>
        /// Finds the line containing a given codepoint via binary search. With
        /// <paramref name="upstream"/> set, codepoints that sit at a soft-wrap boundary
        /// resolve to the upstream line (useful for caret affinity).
        /// </summary>
        public static int FindLineAtCodepoint(int codepointIndex, PooledBuffer<TextLine> lines, bool upstream = false)
        {
            var lo = 0;
            var hi = lines.count - 1;
            var result = lines.count - 1;

            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                ref var line = ref lines[mid];

                if (codepointIndex < line.range.start)
                    hi = mid - 1;
                else if (codepointIndex >= line.range.End)
                    lo = mid + 1;
                else
                {
                    result = mid;
                    break;
                }
            }

            if (upstream && result > 0 && codepointIndex == lines[result].range.start)
                result--;

            return result;
        }

        /// <summary>
        /// Finds the codepoint index on <paramref name="lineIndex"/> closest to
        /// <paramref name="targetX"/> in text-local coordinates. Snaps to grapheme cluster
        /// boundaries to keep multi-codepoint sequences atomic. Never returns a position past a
        /// trailing mandatory break (see <see cref="LineCaretEnd"/>). Scans only the line's own
        /// glyph span (<see cref="TextLine.glyphStart"/>/<see cref="TextLine.glyphCount"/>) —
        /// the per-call cost is bounded by the line, not the document. Clicks past a visual line
        /// edge resolve to the logical line start / end when the edge run follows the paragraph
        /// direction, otherwise to the edge glyph's BiDi codepoint (the visual edge — Android
        /// <c>getOffsetForHorizontal</c> / Chromium <c>PositionForPoint</c> behavior on
        /// mixed-direction lines).
        /// </summary>
        public static int FindCodepointAtX(UniTextBase uniText, int lineIndex, float targetX, PooledBuffer<TextLine> lines)
        {
            ref var line = ref lines[lineIndex];
            var glyphs = uniText.ResultGlyphs;
            if (glyphs.Length == 0)
                return line.range.start;

            var lineStart = line.range.start;
            var caretEnd = LineCaretEnd(in line, uniText.Buffers);

            var glyphFrom = line.glyphStart;
            var glyphTo = line.glyphStart + line.glyphCount;
            if (line.glyphCount <= 0 || glyphFrom < 0 || glyphTo > glyphs.Length)
                return lineStart;

            var graphemeBreaks = uniText.Buffers != null
                ? uniText.Buffers.GraphemeBreaksOrEmpty
                : ReadOnlySpan<bool>.Empty;

            var buffers = uniText.Buffers;

            var bestIndex = lineStart;
            var bestDistance = float.MaxValue;
            var bestContains = false;
            var minLeft = float.MaxValue;
            var maxRight = float.MinValue;
            var leftEdgeCp = lineStart;
            var rightEdgeCp = lineStart;
            var leftEdgeRtl = false;
            var rightEdgeRtl = false;

            for (var i = glyphFrom; i < glyphTo; i++)
            {
                ref readonly var glyph = ref glyphs[i];

                var afterCp = graphemeBreaks.Length > 0
                    ? GraphemeNavigator.NextGraphemeCluster(graphemeBreaks, glyph.cluster)
                    : glyph.cluster + 1;

                bool rtl = buffers != null && buffers.IsRtlLevelAt(glyph.cluster);
                int leftCp = rtl ? afterCp : glyph.cluster;
                int rightCp = rtl ? glyph.cluster : afterCp;

                if (glyph.left < minLeft)
                {
                    minLeft = glyph.left;
                    leftEdgeCp = leftCp;
                    leftEdgeRtl = rtl;
                }
                if (glyph.right > maxRight)
                {
                    maxRight = glyph.right;
                    rightEdgeCp = rightCp;
                    rightEdgeRtl = rtl;
                }

                bool contains = targetX >= glyph.left && targetX <= glyph.right;

                var distToLeft = Math.Abs(glyph.left - targetX);
                if ((contains && !bestContains) || (contains == bestContains && distToLeft < bestDistance))
                {
                    bestContains = contains;
                    bestDistance = distToLeft;
                    bestIndex = leftCp;
                }

                var distToRight = Math.Abs(glyph.right - targetX);
                if ((contains && !bestContains) || (contains == bestContains && distToRight < bestDistance))
                {
                    bestContains = contains;
                    bestDistance = distToRight;
                    bestIndex = rightCp;
                }
            }

            if (targetX > maxRight)
                bestIndex = rightEdgeRtl == line.IsRtl ? (line.IsRtl ? lineStart : caretEnd) : rightEdgeCp;
            else if (targetX < minLeft)
                bestIndex = leftEdgeRtl == line.IsRtl ? (line.IsRtl ? caretEnd : lineStart) : leftEdgeCp;

            if (bestIndex < lineStart) bestIndex = lineStart;
            if (bestIndex > caretEnd) bestIndex = caretEnd;

            if (graphemeBreaks.Length > 0)
                bestIndex = GraphemeNavigator.SnapToClusterBoundary(graphemeBreaks, bestIndex);

            return bestIndex;
        }
    }
}
