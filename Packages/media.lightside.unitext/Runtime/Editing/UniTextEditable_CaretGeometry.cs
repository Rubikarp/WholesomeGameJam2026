using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Per-layout cluster→glyph index acceleration for caret geometry. Positioned glyphs are
    /// in visual order (clusters non-monotonic under BiDi), so every caret query used to scan
    /// the whole glyph array; selection handles and menu placement issue several such queries
    /// per frame. This cache makes exact-cluster lookup O(1) and predecessor / successor
    /// cluster lookup O(log n), rebuilt lazily once per applied layout.
    /// </summary>
    public partial class UniTextEditable
    {
        private int[] clusterFirstGlyph = Array.Empty<int>();
        private int[] presentClusters = Array.Empty<int>();
        private int presentClusterCount;

        /// <summary>
        /// Per-cluster advance extent (min <see cref="PositionedGlyph.left"/> / max
        /// <see cref="PositionedGlyph.right"/> over every glyph of the cluster). A cluster can
        /// map to several glyphs — a base plus zero-advance marks (e.g. the detached dots of an
        /// Arabic letter), or a multi-codepoint ligature — so caret boundaries must come from the
        /// cluster's advance span, not from one member glyph's box, whose edge for a mark is
        /// degenerate and would collapse onto the neighbour.
        /// </summary>
        private float[] clusterLeft = Array.Empty<float>();
        private float[] clusterRight = Array.Empty<float>();
        private int caretGeometryCpCount;
        private bool caretGeometryValid;

        private void InvalidateCaretGeometry() => caretGeometryValid = false;

        private void EnsureCaretGeometry(ReadOnlySpan<PositionedGlyph> glyphs)
        {
            if (caretGeometryValid) return;
            caretGeometryValid = true;

            caretGeometryCpCount = RenderedCodepointCount;
            var mapLen = caretGeometryCpCount + 1;
            if (clusterFirstGlyph.Length < mapLen)
            {
                var size = Mathf.NextPowerOfTwo(Mathf.Max(mapLen, 64));
                clusterFirstGlyph = new int[size];
                presentClusters = new int[size];
                clusterLeft = new float[size];
                clusterRight = new float[size];
            }

            clusterFirstGlyph.AsSpan(0, mapLen).Fill(-1);
            clusterLeft.AsSpan(0, mapLen).Fill(float.PositiveInfinity);
            clusterRight.AsSpan(0, mapLen).Fill(float.NegativeInfinity);
            for (var i = 0; i < glyphs.Length; i++)
            {
                var c = glyphs[i].cluster;
                if ((uint)c >= (uint)mapLen) continue;
                if (clusterFirstGlyph[c] < 0)
                    clusterFirstGlyph[c] = i;
                if (glyphs[i].left < clusterLeft[c]) clusterLeft[c] = glyphs[i].left;
                if (glyphs[i].right > clusterRight[c]) clusterRight[c] = glyphs[i].right;
            }

            presentClusterCount = 0;
            for (var cp = 0; cp < mapLen; cp++)
                if (clusterFirstGlyph[cp] >= 0)
                    presentClusters[presentClusterCount++] = cp;
        }

        /// <summary>First visual glyph whose cluster equals <paramref name="cluster"/>, or -1.</summary>
        private int GlyphAtCluster(int cluster)
            => (uint)cluster <= (uint)caretGeometryCpCount ? clusterFirstGlyph[cluster] : -1;

        /// <summary>
        /// First visual glyph of the largest present cluster strictly below
        /// <paramref name="cluster"/>, or -1 when no glyph precedes it logically.
        /// </summary>
        private int GlyphBeforeCluster(int cluster)
        {
            int lo = 0, hi = presentClusterCount - 1, best = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                if (presentClusters[mid] < cluster) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best < 0 ? -1 : clusterFirstGlyph[presentClusters[best]];
        }

        /// <summary>
        /// Codepoint index where the cluster after <paramref name="cluster"/> begins: the smallest
        /// present cluster strictly above it, or the rendered codepoint count when none exists (the
        /// last cluster runs to the end of the text). An upper bound on the cluster's extent, not the
        /// extent itself — the gap also holds every codepoint shaping or layout dropped;
        /// <see cref="PositionedClusterSpan"/> narrows it.
        /// </summary>
        private int NextClusterAfter(int cluster)
        {
            int lo = 0, hi = presentClusterCount - 1, best = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                if (presentClusters[mid] > cluster) { best = mid; hi = mid - 1; }
                else lo = mid + 1;
            }
            return best < 0 ? caretGeometryCpCount : presentClusters[best];
        }

        /// <summary>
        /// Codepoints over [<paramref name="cluster"/>, <paramref name="end"/>) that the cluster's glyph
        /// actually stands for, and through <paramref name="caretOffset"/> how many of them precede
        /// <paramref name="caret"/> — denominator and numerator of the intra-cluster caret fraction.
        /// The range between two glyph-bearing clusters holds more than one cluster's codepoints: shaping
        /// strips default-ignorables (<see cref="HB.BUFFER_FLAG_REMOVE_DEFAULT_IGNORABLES"/>) and layout
        /// drops hidden clusters, and neither kind reaches a glyph or occupies advance. A span of one
        /// leaves the caret on the cluster's trailing edge instead of inside its glyph.
        /// </summary>
        private int PositionedClusterSpan(int cluster, int end, int caret, out int caretOffset)
        {
            var codepoints = TextComponent.Buffers.codepoints;
            var processor = TextComponent.TextProcessor;
            var span = 0;
            caretOffset = 0;
            for (var cp = cluster; cp < end; cp++)
            {
                if (UnicodeData.IsDefaultIgnorable(codepoints[cp])) continue;
                if (processor.IsClusterHiddenFromLayout(cp)) continue;
                if (cp < caret) caretOffset++;
                span++;
            }
            return span;
        }
    }
}
