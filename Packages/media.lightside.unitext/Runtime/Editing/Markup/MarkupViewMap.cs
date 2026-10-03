using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Chooses a side when hidden syntax in the synthesized markup view collapses to one rendered offset.
    /// </summary>
    internal enum MarkupViewStick { Before, After }

    /// <summary>
    /// Bidirectional codepoint map owned exclusively by the synthesized Raw/Reveal markup view.
    /// </summary>
    /// <remarks>
    /// <para>Sits above BiDi: <c>source view → rendered logical → visual</c>. Rebuilt per view parse.</para>
    /// <para>
    /// Editing-layer coordinate spaces — all bare <c>int</c>, told apart only by name, so mixing them is a silent bug:
    /// <b>source codepoint</b> (gap-buffer logical position; what caret/selection store) ↔ <b>source char</b> (UTF-16;
    /// <c>GapBuffer.CodepointToCharIndex</c> / <c>CharToCodepointIndex</c>); <b>source codepoint</b> ↔ <b>visible
    /// codepoint</b> (markup hidden; this map's <see cref="SourceToRendered"/> / <see cref="RenderedToSource(int,MarkupViewStick)"/>);
    /// <b>visible codepoint</b> → glyph cluster / line index / screen px (layout + hit-test).
    /// </para>
    /// <para>
    /// Two rules keep edits safe at hidden-tag boundaries: (1) every edit driven by a visible selection takes its source
    /// range from the markup view's document-range resolver, never raw selection offsets — else neighbouring hidden syntax
    /// is swept in; (2) <see cref="RenderedToSource(int,MarkupViewStick)"/> follows explicit boundary affinity.
    /// </para>
    /// <para>
    /// Invariant: a range with <c>VisibleWidth &gt; 1</c> must project as ONE atomic grapheme cluster.
    /// Visible offsets strictly inside such a range do not round-trip — <see cref="RenderedToSource(int,MarkupViewStick)"/>
    /// lands inside the tag's source and <see cref="SourceToRendered"/> collapses back to the range's
    /// visible start — so navigation must step over the projection, never into it.
    /// </para>
    /// </remarks>
    internal sealed class MarkupViewMap
    {
        private readonly List<ProjectedRange> regions = new();
        private readonly List<int> visibleStarts = new();
        private readonly List<int> hiddenPrefix = new();

        private int sourceLength;
        private int visibleLength;

        public int SourceLength => sourceLength;
        public int VisibleLength => visibleLength;
        public IReadOnlyList<ProjectedRange> Regions => regions;

        /// <summary>
        /// Rebuilds from the projected tag ranges. They must be sorted by <see cref="ProjectedRange.start"/>,
        /// non-overlapping, and within <c>[0, sourceLength]</c>; adjacent zero-width ranges (one ending where
        /// the next begins) are allowed and form a single cluster at the shared visible offset.
        /// </summary>
        public void Rebuild(int sourceLength, IReadOnlyList<ProjectedRange> projectedRanges)
        {
            this.sourceLength = sourceLength;
            regions.Clear();
            for (var i = 0; i < projectedRanges.Count; i++)
                regions.Add(projectedRanges[i]);
            RebuildIndex();
        }

        private void RebuildIndex()
        {
            visibleStarts.Clear();
            hiddenPrefix.Clear();

            var hidden = 0;
            hiddenPrefix.Add(0);
            for (var i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                visibleStarts.Add(r.start - hidden);
                hidden += r.length - r.VisibleWidth;
                hiddenPrefix.Add(hidden);
            }
            visibleLength = sourceLength - hidden;
        }

        /// <summary>
        /// Patches the map by one document mutation so queries stay in-bounds and exact outside
        /// the edited range until the next parse rebuilds it (C10 time coherence). Regions after
        /// the edit shift by the codepoint delta; a region strictly containing the edit absorbs
        /// the delta; a region partially overlapping the edit is damaged by it (the tag text
        /// itself changed) and is dropped — its span reads as visible content, which is the
        /// conservative clamp: no query can land out of range, and the next parse restores truth.
        /// </summary>
        public void ApplyEditShape(in EditShape shape)
        {
            var editStart = shape.Start;
            var editEnd = shape.Start + shape.Removed;
            var delta = shape.Inserted - shape.Removed;

            sourceLength += delta;
            if (sourceLength < 0) sourceLength = 0;

            if (regions.Count == 0)
            {
                visibleLength = sourceLength;
                if (visibleStarts.Count > 0) RebuildIndex();
                return;
            }

            var write = 0;
            for (var i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                if (r.End <= editStart)
                {
                    regions[write++] = r;
                }
                else if (r.start >= editEnd)
                {
                    regions[write++] = new ProjectedRange(r.start + delta, r.length, r.visible);
                }
                else if (r.start <= editStart && r.End >= editEnd)
                {
                    var grown = r.length + delta;
                    if (grown > 0)
                        regions[write++] = new ProjectedRange(r.start, grown, r.visible);
                }
            }
            regions.RemoveRange(write, regions.Count - write);
            RebuildIndex();
        }

        /// <summary>
        /// Visible offset for a source offset. A source offset inside a tag range collapses to that range's
        /// visible start; the range's own glyph (an object/escape with non-zero width) sits at that start, so
        /// the source position just after the range maps one visible unit further on per width.
        /// </summary>
        public int SourceToRendered(int source)
        {
            if (source <= 0) return 0;
            if (source >= sourceLength) return visibleLength;
            if (regions.Count == 0) return source;

            var idx = LastStartAtMost(source);
            if (idx < 0) return source;
            if (source < regions[idx].End) return visibleStarts[idx];
            return source - hiddenPrefix[idx + 1];
        }

        /// <summary>
        /// Source offset for a visible offset. At a zero-width tag cluster, <paramref name="stick"/> chooses
        /// the source side (see <see cref="MarkupViewStick"/>); a range with visible width (an inline object/escape)
        /// is real content, not a cluster point, so it never sticks.
        /// </summary>
        public int RenderedToSource(int visible, MarkupViewStick stick)
        {
            if (visible <= 0) visible = 0;
            else if (visible >= visibleLength) visible = visibleLength;
            if (regions.Count == 0) return visible;

            var lo = FirstVisibleStartAtLeast(visible);
            var baseSource = visible + hiddenPrefix[lo];

            if (lo < regions.Count && visibleStarts[lo] == visible && regions[lo].VisibleWidth == 0)
            {
                if (stick == MarkupViewStick.Before) return baseSource;
                var hi = lo;
                while (hi < regions.Count && visibleStarts[hi] == visible && regions[hi].VisibleWidth == 0) hi++;
                return regions[hi - 1].End;
            }
            return baseSource;
        }

        public int RenderedToSource(int visible) => RenderedToSource(visible, MarkupViewStick.Before);

        /// <summary>Whether the source offset falls strictly inside a tag range.</summary>
        public bool IsInsideHiddenSyntax(int source, out ProjectedRange region)
        {
            region = default;
            if (regions.Count == 0) return false;

            var idx = LastStartAtMost(source);
            if (idx < 0) return false;

            var r = regions[idx];
            if (source >= r.start && source < r.End)
            {
                region = r;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Snaps a source position stranded strictly inside a tag range outward:
        /// <paramref name="backward"/> to the tag's start, forward to its exclusive end; a position
        /// not inside any tag returns unchanged. The atomic-range invariant — an edit that reaches
        /// into a tag always takes the whole tag, never a slice of it.
        /// </summary>
        public int SnapOutOfHiddenSyntax(int pos, bool backward)
            => IsInsideHiddenSyntax(pos, out var region) ? (backward ? region.start : region.End) : pos;

        private int LastStartAtMost(int source)
        {
            int lo = 0, hi = regions.Count - 1, result = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                if (regions[mid].start <= source)
                {
                    result = mid;
                    lo = mid + 1;
                }
                else hi = mid - 1;
            }
            return result;
        }

        private int FirstVisibleStartAtLeast(int visible)
        {
            int lo = 0, hi = visibleStarts.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (visibleStarts[mid] < visible) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }
}
