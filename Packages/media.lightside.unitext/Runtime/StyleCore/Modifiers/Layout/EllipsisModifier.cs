using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Truncates text that overflows its container and appends an ellipsis (...).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Overflow is detected against the container's width without word wrap, and against its height
    /// with word wrap.
    /// </para>
    /// <para>
    /// Without word wrap the text keeps one line and the parameter (0-1) places the cut inside it:
    /// <c>0</c> takes the marker to the beginning, <c>0.5</c> to the middle, <c>1</c> (the default) to
    /// the end.
    /// </para>
    /// <para>
    /// With word wrap the text is clamped to the lines that fit the box: the tail of the last of them
    /// gives way to the marker and every line below is hidden, so the parameter has no effect there —
    /// a hidden line still occupies its place in the layout, and keeping the lower lines instead would
    /// draw them outside the box.
    /// </para>
    /// </remarks>
    /// <seealso cref="ParseRule"/>
    [Serializable]
    [TypeGroup("Layout", 4)]
    [TypeDescription("Truncates overflowing text with an ellipsis.")]
    [GenerateParameters]
    public partial class EllipsisModifier : BaseModifier
    {
        /// <summary>Where the marker sits inside the truncated line (0 start, 0.5 middle, 1 end); without effect once word wrap clamps whole lines. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, Range(0f, 1f), StateProperty(nameof(MarkTextDirty))] private float position = 1f;

        public struct Range
        {
            public int start;
            public int end;
            public float position;
        }

        private struct Truncation
        {
            public int truncateMinCluster;
            public int truncateMaxCluster;
            public int ellipsisCluster;

            /// <summary>Distance from the anchor glyph's pen origin to the marker, in mesh-local pixels: the anchor's own width when the marker follows visible text, zero when it replaces it.</summary>
            public float markerOffset;
        }

        private const string EllipsisText = "...";
        private const float OverflowEpsilon = 0.5f;

        /// <summary>When <see langword="false"/>, overflow is truncated without rendering the "..." marker and no marker width is reserved.</summary>
        protected virtual bool ShowEllipsis => true;

        private PooledList<Range> ranges;
        private PooledBuffer<float> originalAdvances;
        private bool needsRestore;
        private bool isProcessingRelayout;

        private PooledBuffer<int> glyphToGlobalCluster;
        private PooledBuffer<(int firstGlyph, int lastGlyph, int minCluster, int maxCluster)> rangeGlyphBoundsCache;

        private PooledBuffer<float> clusterWidthsBuffer;
        private PooledList<Truncation> lineTruncations;

        /// <summary>Marker width per range in display pixels, measured in the font that range's own text resolved to.</summary>
        private PooledBuffer<float> rangeEllipsisWidths;

        /// <summary>Per-codepoint widths as the truncation left them, handed to the one re-wrap that closes the pass.</summary>
        private PooledBuffer<float> truncatedCpWidths;

        /// <summary>
        /// Break positions this pass turned from mandatory to optional, put back with the advances.
        /// Dropped when the text is parsed again: break analysis rewrites its own opportunities there,
        /// and positions from the previous text would restore breaks into a document that never had them.
        /// </summary>
        private PooledBuffer<int> suppressedBreaks;

        private Action rectHeightCallback;
        private Action<UniTextDirty> dirtyFlagsCallback;
        private Action shapedCallback;
        private Action layoutCompleteCallback;
        private Action injectDotsCallback;
        private Action rebuildEndCallback;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<Range>(8);
            ranges.FakeClear();
            originalAdvances.Rent(256);
            glyphToGlobalCluster.Rent(256);
            rangeGlyphBoundsCache.Rent(8);
            clusterWidthsBuffer.Rent(256);
            lineTruncations ??= new PooledList<Truncation>(8);
            lineTruncations.FakeClear();
            rangeEllipsisWidths.Rent(8);
            truncatedCpWidths.Rent(256);
            suppressedBreaks.Rent(8);
            needsRestore = false;
            isProcessingRelayout = false;
            
            rectHeightCallback ??= OnRectHeightChanged;
            uniText.RectHeightChanged += rectHeightCallback;
            dirtyFlagsCallback ??= OnDirtyFlagsChanged;
            uniText.DirtyFlagsChanged += dirtyFlagsCallback;
            shapedCallback ??= OnShaped;
            uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
            layoutCompleteCallback ??= OnLayoutComplete;
            uniText.TextProcessor.LayoutComplete.Subscribe(layoutCompleteCallback);
            injectDotsCallback ??= InjectEllipsisDots;
            uniText.BeforeGenerateMesh.Subscribe(injectDotsCallback);
            rebuildEndCallback ??= OnRebuildEnd;
            uniText.MeshGenerator.onRebuildEnd.Subscribe(rebuildEndCallback);
        }

        private void OnRectHeightChanged()
        {
            if ((uniText.CurrentDirtyFlags & UniTextDirty.Layout) == 0)
                uniText.SetDirty(UniTextDirty.Layout);
        }

        private void OnDirtyFlagsChanged(UniTextDirty flags)
        {
            if ((flags & UniTextDirty.Positions) != 0 &&
                (uniText.CurrentDirtyFlags & UniTextDirty.Layout) == 0)
            {
                uniText.SetDirty(UniTextDirty.Layout);
            }
        }

        protected override void OnDisable()
        {
            uniText.RectHeightChanged -= rectHeightCallback;
            uniText.DirtyFlagsChanged -= dirtyFlagsCallback;
            uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
            uniText.TextProcessor.LayoutComplete.Unsubscribe(layoutCompleteCallback);
            uniText.BeforeGenerateMesh.Unsubscribe(injectDotsCallback);
            uniText.MeshGenerator.onRebuildEnd.Unsubscribe(rebuildEndCallback);
        }

        protected override void OnDestroy()
        {
            ranges?.Return();
            ranges = null;
            originalAdvances.Return();
            glyphToGlobalCluster.Return();
            rangeGlyphBoundsCache.Return();
            clusterWidthsBuffer.Return();
            lineTruncations?.Return();
            lineTruncations = null;
            rangeEllipsisWidths.Return();
            truncatedCpWidths.Return();
            suppressedBreaks.Return();
            needsRestore = false;
            isProcessingRelayout = false;
        }
        
        protected override void BeforeApply()
        {
            ranges?.FakeClear();
            suppressedBreaks.FakeClear();
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            ranges.Add(new Range
            {
                start = context.Segment.Range.start,
                end = context.Segment.Range.End,
                position = Math.Clamp(Param.Position.Resolve(this, in context), 0f, 1f)
            });

            if (!ShowEllipsis)
                return;

            for (var i = 0; i < EllipsisText.Length; i++)
                buffers.RequestVirtualCodepoint(EllipsisText[i]);
        }

        private void ClearEllipsisState()
        {
            lineTruncations?.FakeClear();
            ClearOwnHiddenFlags();
        }

        private void OnShaped()
        {
            BuildGlyphToClusterMap();
            BuildRangeGlyphBounds();
        }

        private void BuildGlyphToClusterMap()
        {
            var buf = buffers;
            var glyphCount = buf.shapedGlyphs.count;

            glyphToGlobalCluster.FakeClear();
            if (glyphCount == 0)
                return;

            glyphToGlobalCluster.EnsureCapacity(glyphCount);

            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;
            var glyphs = buf.shapedGlyphs.data;
            var clusterData = glyphToGlobalCluster.data;

            for (var r = 0; r < runCount; r++)
            {
                ref readonly var run = ref runs[r];
                var end = run.glyphStart + run.glyphCount;
                for (var g = run.glyphStart; g < end; g++)
                    clusterData[g] = glyphs[g].cluster;
            }

            glyphToGlobalCluster.count = glyphCount;
        }

        private void BuildRangeGlyphBounds()
        {
            rangeGlyphBoundsCache.FakeClear();
            if (ranges == null || ranges.Count == 0)
                return;

            var rangeCount = ranges.Count;
            rangeGlyphBoundsCache.EnsureCapacity(rangeCount);

            var glyphCount = glyphToGlobalCluster.count;
            var clusterData = glyphToGlobalCluster.data;
            var boundsData = rangeGlyphBoundsCache.data;

            for (var r = 0; r < rangeCount; r++)
                boundsData[r] = (-1, -1, int.MaxValue, int.MinValue);

            for (var g = 0; g < glyphCount; g++)
            {
                var cluster = clusterData[g];
                for (var r = 0; r < rangeCount; r++)
                {
                    var range = ranges[r];
                    if (cluster >= range.start && cluster < range.end)
                    {
                        ref var bounds = ref boundsData[r];
                        if (bounds.Item1 < 0) bounds.Item1 = g;
                        bounds.Item2 = g;
                        if (cluster < bounds.Item3) bounds.Item3 = cluster;
                        if (cluster > bounds.Item4) bounds.Item4 = cluster;
                    }
                }
            }

            for (var r = 0; r < rangeCount; r++)
            {
                ref var bounds = ref boundsData[r];
                if (bounds.Item3 == int.MaxValue) bounds.Item3 = -1;
                if (bounds.Item4 == int.MinValue) bounds.Item4 = -1;
            }

            rangeGlyphBoundsCache.count = rangeCount;
        }

        private void OnLayoutComplete()
        {
            if (isProcessingRelayout)
                return;

            RestoreOriginalAdvances();
            ClearEllipsisState();

            if (ranges == null || ranges.Count == 0)
                return;

            var rect = uniText.cachedTransformData.rect;
            var maxWidth = rect.width;
            var maxHeight = rect.height;
            var resultWidth = uniText.TextProcessor.ResultWidth;
            var resultHeight = uniText.TextProcessor.ResultHeight;

            var hasHeightOverflow = maxHeight > 0 && !float.IsInfinity(maxHeight) && resultHeight > maxHeight + OverflowEpsilon;
            var hasWidthOverflow = !uniText.WordWrap && maxWidth > 0 && resultWidth > maxWidth + OverflowEpsilon;

            if (!hasHeightOverflow && !hasWidthOverflow)
                return;

            BuildRangeEllipsisWidths();

            if (hasWidthOverflow)
                ProcessNonWordWrapOverflow(maxWidth);

            if (hasHeightOverflow)
                ClampToVisibleLines(maxWidth, maxHeight);

            BuildTruncationFlags();
        }

        /// <summary>
        /// Drops this modifier's visual bit from its ranges in <see cref="UniTextBuffers.hiddenClusters"/>.
        /// Must run on every layout pass — including passes that detect no overflow — or flags from a
        /// previous pass would keep glyphs hidden after the text fits again. Other producers' bits in
        /// overlapping ranges are preserved.
        /// </summary>
        private void ClearOwnHiddenFlags()
        {
            var count = buffers.hiddenClusters.count;
            if (count == 0 || ranges == null)
                return;

            var flags = buffers.hiddenClusters.data;
            for (var r = 0; r < ranges.Count; r++)
            {
                var range = ranges[r];
                var min = Math.Max(0, range.start);
                var max = Math.Min(count, range.end);
                for (var c = min; c < max; c++)
                    flags[c] &= unchecked((byte)~HiddenClusterBits.Ellipsis);
            }
        }

        private void BuildTruncationFlags()
        {
            var flags = buffers.PrepareHiddenClusters();
            var clusterCount = flags.Length;
            if (clusterCount == 0 || lineTruncations == null)
                return;

            ClearOwnHiddenFlags();

            for (var i = 0; i < lineTruncations.Count; i++)
            {
                var truncation = lineTruncations[i];
                var min = Math.Max(0, truncation.truncateMinCluster);
                var max = Math.Min(clusterCount - 1, truncation.truncateMaxCluster);
                for (var c = min; c <= max; c++)
                    flags[c] |= HiddenClusterBits.Ellipsis;
            }
        }

        private void ProcessNonWordWrapOverflow(float maxWidth)
        {
            var buf = buffers;
            var glyphs = buf.shapedGlyphs.data;
            var glyphCount = buf.shapedGlyphs.count;
            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;

            if (glyphCount == 0)
                return;

            var glyphScale = buf.GetGlyphScale(uniText.CurrentFontSize);
            var epsilonInShapingUnits = glyphScale > 0 ? OverflowEpsilon / glyphScale : OverflowEpsilon;
            var maxWidthInShapingUnits = glyphScale > 0 ? maxWidth / glyphScale : maxWidth;

            EnsurePristineAdvances(glyphs, glyphCount);

            isProcessingRelayout = true;

            var lines = buf.lines.data;
            var lineCount = buf.lines.count;
            var orderedRuns = buf.orderedRuns.data;

            for (var lineIdx = 0; lineIdx < lineCount; lineIdx++)
            {
                ref readonly var line = ref lines[lineIdx];
                if (line.width <= maxWidthInShapingUnits + epsilonInShapingUnits)
                    continue;

                var lineExcess = line.width - maxWidthInShapingUnits;

                var lineFirstGlyph = int.MaxValue;
                var lineLastGlyph = int.MinValue;
                for (var r = line.runStart; r < line.runStart + line.runCount; r++)
                {
                    ref readonly var run = ref orderedRuns[r];
                    if (run.glyphStart < lineFirstGlyph) lineFirstGlyph = run.glyphStart;
                    var runEnd = run.glyphStart + run.glyphCount - 1;
                    if (runEnd > lineLastGlyph) lineLastGlyph = runEnd;
                }

                var lineRangeWidth = 0f;
                var lineEllipsisWidth = 0f;
                var rangeCount = ranges.Count;
                var boundsData = rangeGlyphBoundsCache.data;
                var origAdvances = originalAdvances.data;
                var clusterData = glyphToGlobalCluster.data;

                for (var r = 0; r < rangeCount; r++)
                {
                    var (firstGlyph, lastGlyph, _, _) = boundsData[r];
                    if (firstGlyph < 0 || lastGlyph < lineFirstGlyph || firstGlyph > lineLastGlyph)
                        continue;

                    lineEllipsisWidth += EllipsisWidth(r, glyphScale);

                    var lineRangeFirst = Math.Max(firstGlyph, lineFirstGlyph);
                    var lineRangeLast = Math.Min(lastGlyph, lineLastGlyph);

                    for (var g = lineRangeFirst; g <= lineRangeLast; g++)
                        lineRangeWidth += origAdvances[g];
                }

                if (lineRangeWidth <= 0)
                    continue;

                var lineWidthToRemove = lineExcess + lineEllipsisWidth;

                for (var r = 0; r < rangeCount; r++)
                {
                    var (firstGlyph, lastGlyph, _, _) = boundsData[r];
                    if (firstGlyph < 0)
                        continue;
                    if (lastGlyph < lineFirstGlyph || firstGlyph > lineLastGlyph)
                        continue;

                    var lineRangeFirst = Math.Max(firstGlyph, lineFirstGlyph);
                    var lineRangeLast = Math.Min(lastGlyph, lineLastGlyph);

                    var lineMinCluster = int.MaxValue;
                    var lineMaxCluster = int.MinValue;
                    for (var g = lineRangeFirst; g <= lineRangeLast; g++)
                    {
                        var cluster = clusterData[g];
                        if (cluster < lineMinCluster) lineMinCluster = cluster;
                        if (cluster > lineMaxCluster) lineMaxCluster = cluster;
                    }

                    if (lineMinCluster > lineMaxCluster)
                        continue;

                    var rangeWidth = 0f;
                    for (var g = lineRangeFirst; g <= lineRangeLast; g++)
                        rangeWidth += origAdvances[g];

                    var rangeWidthToRemove = lineWidthToRemove * (rangeWidth / lineRangeWidth);

                    var clusterCount = lineMaxCluster - lineMinCluster + 1;
                    clusterWidthsBuffer.EnsureCapacity(clusterCount);
                    var clusterWidths = clusterWidthsBuffer.data.AsSpan(0, clusterCount);
                    clusterWidths.Clear();
                    BuildClusterWidths(lineRangeFirst, lineRangeLast, lineMinCluster, clusterWidths);

                    var range = ranges[r];
                    var (truncMin, truncMax, ellipsisClusterTarget) = FindWidthBasedTruncation(
                        range.position, clusterWidths, lineMinCluster, lineMaxCluster, rangeWidthToRemove);

                    if (truncMin > truncMax)
                        continue;

                    var ellipsisGlyph = ApplyTruncationToGlyphs(
                        glyphs, lineRangeFirst, lineRangeLast, truncMin, truncMax, ellipsisClusterTarget,
                        EllipsisWidth(r, glyphScale), origAdvances, clusterData);

                    if (ellipsisGlyph >= 0)
                    {
                        lineTruncations.Add(new Truncation
                        {
                            truncateMinCluster = truncMin,
                            truncateMaxCluster = truncMax,
                            ellipsisCluster = clusterData[ellipsisGlyph]
                        });
                    }
                }
            }

            RecalculateRunWidths(glyphs, runs, runCount);
            RecalculateRunWidths(glyphs, buf.orderedRuns.data, buf.orderedRuns.count);

            uniText.TextProcessor.ForceReposition();

            isProcessingRelayout = false;
        }

        private void BuildClusterWidths(int firstGlyph, int lastGlyph, int minCluster, Span<float> clusterWidths)
        {
            var clusterData = glyphToGlobalCluster.data;
            var origAdvances = originalAdvances.data;

            for (var g = firstGlyph; g <= lastGlyph; g++)
            {
                var cluster = clusterData[g];
                clusterWidths[cluster - minCluster] += origAdvances[g];
            }
        }

        private static (int truncMin, int truncMax, int ellipsisCluster) FindWidthBasedTruncation(
            float position, Span<float> clusterWidths, int minCluster, int maxCluster, float widthToRemove)
        {
            var anchor = minCluster + (int)(position * (maxCluster - minCluster));
            var truncMin = anchor;
            var truncMax = anchor;
            var accumulated = clusterWidths[anchor - minCluster];

            while (accumulated < widthToRemove)
            {
                var canExpandLeft = truncMin > minCluster;
                var canExpandRight = truncMax < maxCluster;

                if (!canExpandLeft && !canExpandRight)
                    break;

                if (canExpandLeft && canExpandRight)
                {
                    var leftWidth = clusterWidths[truncMin - 1 - minCluster];
                    var rightWidth = clusterWidths[truncMax + 1 - minCluster];

                    if (leftWidth <= rightWidth)
                    {
                        truncMin--;
                        accumulated += leftWidth;
                    }
                    else
                    {
                        truncMax++;
                        accumulated += rightWidth;
                    }
                }
                else if (canExpandLeft)
                {
                    truncMin--;
                    accumulated += clusterWidths[truncMin - minCluster];
                }
                else
                {
                    truncMax++;
                    accumulated += clusterWidths[truncMax - minCluster];
                }
            }

            return (truncMin, truncMax, anchor);
        }

        /// <summary>
        /// Gives a range up until the whole block fits the box: everything below the last line that fits
        /// is hidden, and that line takes the marker — following its text where the width allows,
        /// otherwise in place of as much of the tail as the marker needs. The cut also frees room for the
        /// text that follows the range, so a fixed suffix keeps its place beside the marker rather than
        /// being pushed out. The range is the only thing that gives way: when even an empty range leaves
        /// the rest too big, the rest overflows. A line the width pass already marked keeps its own
        /// marker, and on a right-to-left line the marker always replaces the tail, since it is written
        /// from the pen forward.
        /// </summary>
        private void ClampToVisibleLines(float maxWidth, float maxHeight)
        {
            var buf = buffers;
            var glyphs = buf.shapedGlyphs.data;
            var glyphCount = buf.shapedGlyphs.count;

            if (glyphCount == 0 || buf.lines.count == 0 || ranges.Count == 0)
                return;

            var lines = buf.lines.data;
            var glyphScale = buf.GetGlyphScale(uniText.CurrentFontSize);
            var boundsData = rangeGlyphBoundsCache.data;
            var clusterData = glyphToGlobalCluster.data;
            var rangeCount = Math.Min(ranges.Count, rangeGlyphBoundsCache.count);
            var clamped = false;

            for (var r = 0; r < rangeCount; r++)
            {
                var (firstGlyph, lastGlyph, minCluster, maxCluster) = boundsData[r];
                if (firstGlyph < 0 || minCluster > maxCluster)
                    continue;

                var firstLine = LineOfCluster(minCluster);
                if (firstLine < 0)
                    continue;

                var lastLine = buf.lines.count - 1;
                var lastVisible = LastLineWithin(firstLine, lastLine, maxHeight);
                if (lastVisible >= lastLine)
                    continue;

                EnsurePristineAdvances(glyphs, glyphCount);
                var origAdvances = originalAdvances.data;

                ref readonly var line = ref lines[lastVisible];
                var lineStart = Math.Max(line.range.start, minCluster);
                var dropFrom = Math.Min(line.range.End - 1, maxCluster) + 1;
                var ellipsisCluster = -1;
                var markerOffset = 0f;

                if (LineCarriesMarker(in line))
                {
                    ApplyTruncationToGlyphs(glyphs, firstGlyph, lastGlyph, dropFrom, maxCluster, dropFrom,
                        0f, origAdvances, clusterData);
                }
                else
                {
                    var markerWidth = EllipsisWidth(r, glyphScale);
                    var budget = glyphScale > 0 ? (maxWidth - line.startMargin) / glyphScale : maxWidth;
                    var tail = WidthPulledOntoLine(maxCluster + 1);

                    dropFrom = FirstClusterPastBudget(line.range.start, maxCluster, budget - tail) > maxCluster
                        ? maxCluster + 1
                        : Math.Max(lineStart,
                            FirstClusterPastBudget(line.range.start, maxCluster, budget - markerWidth - tail));

                    if (dropFrom <= maxCluster)
                    {
                        var anchor = LastInkCluster(firstGlyph, lastGlyph, lineStart, dropFrom, clusterData, origAdvances);
                        if (line.IsRtl && anchor >= 0)
                        {
                            dropFrom = anchor;
                            anchor = -1;
                        }
                        else if (anchor >= 0)
                        {
                            dropFrom = anchor + 1;
                        }

                        var dropped = ApplyTruncationToGlyphs(glyphs, firstGlyph, lastGlyph, dropFrom, maxCluster,
                            dropFrom, 0f, origAdvances, clusterData);

                        if (anchor >= 0)
                        {
                            var anchorGlyph = FirstGlyphOfCluster(firstGlyph, lastGlyph, anchor, clusterData);
                            markerOffset = GetClusterWidth(firstGlyph, lastGlyph, anchor, clusterData, origAdvances) * glyphScale;
                            glyphs[anchorGlyph].advanceX += markerWidth;
                            ellipsisCluster = anchor;
                        }
                        else if (dropped >= 0)
                        {
                            glyphs[dropped].advanceX = markerWidth;
                            ellipsisCluster = clusterData[dropped];
                        }
                    }
                }

                if (dropFrom > maxCluster)
                    continue;

                SuppressBreaksIn(dropFrom, maxCluster);
                SilenceMarkersIn(dropFrom, maxCluster);

                lineTruncations.Add(new Truncation
                {
                    truncateMinCluster = dropFrom,
                    truncateMaxCluster = maxCluster,
                    ellipsisCluster = ellipsisCluster,
                    markerOffset = markerOffset
                });
                clamped = true;
            }

            if (!clamped)
                return;

            isProcessingRelayout = true;
            RecalculateRunWidths(glyphs, buf.shapedRuns.data, buf.shapedRuns.count);
            RecalculateRunWidths(glyphs, buf.orderedRuns.data, buf.orderedRuns.count);
            BuildTruncatedCpWidths();
            uniText.TextProcessor.ForceRelayout(truncatedCpWidths.Span);
            isProcessingRelayout = false;
        }

        /// <summary>
        /// Per-codepoint widths read back from the advances the truncation left behind — what the closing
        /// re-wrap lays out from. Truncation only takes width away, so the wrap it produces holds no more
        /// text than the one the clamp decided on: emptied lines close up and whatever followed the range
        /// moves in behind the marker.
        /// </summary>
        private void BuildTruncatedCpWidths()
        {
            var buf = buffers;
            var cpCount = buf.codepoints.count;

            truncatedCpWidths.EnsureCount(cpCount);
            var widths = truncatedCpWidths.data;
            Array.Clear(widths, 0, cpCount);

            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;
            var glyphs = buf.shapedGlyphs.data;

            for (var r = 0; r < runCount; r++)
            {
                ref readonly var run = ref runs[r];
                var end = run.glyphStart + run.glyphCount;
                for (var g = run.glyphStart; g < end; g++)
                {
                    var cluster = glyphs[g].cluster;
                    if ((uint)cluster < (uint)cpCount)
                        widths[cluster] += glyphs[g].advanceX;
                }
            }

            truncatedCpWidths.count = cpCount;
        }

        /// <summary>
        /// Takes the hard breaks of cut text out of the wrap: a line break the truncation removed has no
        /// character left behind it, so what follows keeps its place on the line instead of being pushed
        /// down by it. The breaks are put back when the pass's advances are.
        /// </summary>
        private void SuppressBreaksIn(int fromCluster, int toCluster)
        {
            var breaks = buffers.breakOpportunities.data;
            var last = Math.Min(toCluster + 1, buffers.breakOpportunities.count - 1);

            for (var i = Math.Max(1, fromCluster); i <= last; i++)
            {
                if (breaks[i] != LineBreakType.Mandatory) continue;

                suppressedBreaks.Add(i);
                breaks[i] = LineBreakType.Optional;
            }
        }

        private void RestoreSuppressedBreaks()
        {
            if (suppressedBreaks.count == 0) return;

            var breaks = buffers.breakOpportunities.data;
            var count = buffers.breakOpportunities.count;
            for (var i = 0; i < suppressedBreaks.count; i++)
            {
                var index = suppressedBreaks[i];
                if (index < count) breaks[index] = LineBreakType.Mandatory;
            }

            suppressedBreaks.FakeClear();
        }

        /// <summary>
        /// Takes the marker off every truncation anchored inside a span this clamp hides. A marker draws
        /// its dots even where the text is hidden — that is what lets it stand in for the text it replaced
        /// — so one left on a dropped line would float over nothing.
        /// </summary>
        private void SilenceMarkersIn(int fromCluster, int toCluster)
        {
            for (var i = 0; i < lineTruncations.Count; i++)
            {
                var truncation = lineTruncations[i];
                if (truncation.ellipsisCluster < fromCluster || truncation.ellipsisCluster > toCluster)
                    continue;

                truncation.ellipsisCluster = -1;
                lineTruncations[i] = truncation;
            }
        }

        /// <summary>
        /// First cluster of the line the range cannot keep: the one that no longer fits
        /// <paramref name="keep"/> — what the line has left for text once the marker and the text pulled
        /// up behind it are paid for — or the one a hard break inside the range opens, since a line the
        /// author ended cannot go on. Measured from the line's own start, so the answer holds whether the
        /// cut falls inside the text already on the line or in the word after it, which the truncation
        /// shortens and brings onto the line.
        /// </summary>
        private int FirstClusterPastBudget(int lineStart, int maxCluster, float keep)
        {
            var widths = buffers.cpWidths;
            var breaks = buffers.breakOpportunities;
            var used = 0f;

            for (var c = lineStart; c <= maxCluster; c++)
            {
                if (c > lineStart && c < breaks.count && breaks[c] == LineBreakType.Mandatory)
                    return c;

                var width = c < widths.count ? widths[c] : 0f;
                if (used + width > keep) return c;
                used += width;
            }

            return maxCluster + 1;
        }

        /// <summary>
        /// Width of the text that must come up onto the last visible line once the range gives way: what
        /// follows the range up to the next hard break, which has no line of its own left to sit on. The
        /// truncation frees room for it, so a fixed suffix — a file extension, a "more" link — stays
        /// beside the marker instead of being pushed out of the box.
        /// </summary>
        private float WidthPulledOntoLine(int from)
        {
            var widths = buffers.cpWidths;
            var breaks = buffers.breakOpportunities;
            var cpCount = buffers.codepoints.count;

            var total = 0f;
            for (var c = from; c < cpCount; c++)
            {
                if (c > from && c < breaks.count && breaks[c] == LineBreakType.Mandatory)
                    break;
                if (c < widths.count)
                    total += widths[c];
            }

            return total;
        }

        /// <summary>Whether a marker already sits on <paramref name="line"/> — the width pass runs first and may have placed one.</summary>
        private bool LineCarriesMarker(in TextLine line)
        {
            for (var i = 0; i < lineTruncations.Count; i++)
            {
                var cluster = lineTruncations[i].ellipsisCluster;
                if (cluster >= line.range.start && cluster < line.range.End)
                    return true;
            }

            return false;
        }

        private static int FirstGlyphOfCluster(int firstGlyph, int lastGlyph, int cluster, int[] clusterData)
        {
            for (var g = firstGlyph; g <= lastGlyph; g++)
                if (clusterData[g] == cluster)
                    return g;

            return -1;
        }

        /// <summary>
        /// Last cluster before <paramref name="limit"/> that still puts ink on the line — the one the
        /// marker follows. Trailing whitespace is passed over, so the marker sits against the text
        /// rather than after the gap. Returns -1 when the line keeps nothing visible.
        /// </summary>
        private int LastInkCluster(int firstGlyph, int lastGlyph, int lineStart, int limit,
            int[] clusterData, float[] origAdvances)
        {
            var codepoints = buffers.codepoints.data;
            var codepointCount = buffers.codepoints.count;

            for (var c = limit - 1; c >= lineStart; c--)
            {
                if (GetClusterWidth(firstGlyph, lastGlyph, c, clusterData, origAdvances) <= 0f)
                    continue;
                if ((uint)c < (uint)codepointCount && UnicodeData.IsWhiteSpace(codepoints[c]))
                    continue;

                return c;
            }

            return -1;
        }

        /// <summary>Index of the line holding <paramref name="cluster"/>, or -1 when no line covers it.</summary>
        private int LineOfCluster(int cluster)
        {
            var lines = buffers.lines.data;
            for (var l = 0; l < buffers.lines.count; l++)
                if (cluster >= lines[l].range.start && cluster < lines[l].range.End)
                    return l;

            return -1;
        }

        /// <summary>
        /// Last line in <paramref name="firstLine"/>..<paramref name="lastLine"/> whose bottom edge still
        /// fits <paramref name="maxHeight"/>, measured from the top of the text block. Never returns less
        /// than <paramref name="firstLine"/>: one line always survives, however short the box.
        /// </summary>
        private int LastLineWithin(int firstLine, int lastLine, float maxHeight)
        {
            var lines = buffers.lines;
            if (!buffers.HasLineAdvances || lines.count == 0)
                return firstLine;

            var blockExtra = uniText.TextProcessor.ResultHeight - lines[lines.count - 1].advancePrefix;

            var last = firstLine;
            for (var l = firstLine + 1; l <= lastLine; l++)
            {
                if (blockExtra + lines[l - 1].advancePrefix > maxHeight + OverflowEpsilon)
                    break;
                last = l;
            }

            return last;
        }

        private static float GetClusterWidth(int firstGlyph, int lastGlyph, int cluster,
            int[] clusterData, float[] origAdvances)
        {
            var width = 0f;
            for (var g = firstGlyph; g <= lastGlyph; g++)
            {
                if (clusterData[g] == cluster)
                    width += origAdvances[g];
            }
            return width;
        }

        private int ApplyTruncationToGlyphs(
            ShapedGlyph[] glyphs, int firstGlyph, int lastGlyph,
            int truncMin, int truncMax, int ellipsisClusterTarget, float ellipsisWidth,
            float[] origAdvances, int[] clusterData)
        {
            var ellipsisGlyph = -1;

            for (var g = firstGlyph; g <= lastGlyph; g++)
            {
                var cluster = clusterData[g];
                if (cluster < truncMin || cluster > truncMax)
                    continue;

                if (ellipsisGlyph < 0 || cluster == ellipsisClusterTarget)
                    ellipsisGlyph = g;

                glyphs[g].advanceX = 0f;
            }

            if (ellipsisGlyph >= 0)
                glyphs[ellipsisGlyph].advanceX = ellipsisWidth;

            return ellipsisGlyph;
        }

        /// <summary>
        /// Takes the pass's copy of the untouched advances, once: the width and height constraints both
        /// mutate advances, and the second of them to run must measure the text's own widths rather than
        /// what the first one already zeroed.
        /// </summary>
        private void EnsurePristineAdvances(ShapedGlyph[] glyphs, int glyphCount)
        {
            if (needsRestore) return;

            originalAdvances.EnsureCapacity(glyphCount);
            SaveOriginalAdvances(glyphs, glyphCount);
            needsRestore = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SaveOriginalAdvances(ShapedGlyph[] glyphs, int count)
        {
            var advances = originalAdvances.data;
            for (var i = 0; i < count; i++)
                advances[i] = glyphs[i].advanceX;

            originalAdvances.count = count;
        }

        private bool TryResolveDot(int cluster, out InjectedGlyph dot)
            => buffers.TryResolveInjectedGlyph(uniText.FontProvider, '.', cluster,
                uniText.CurrentFontSize, out dot);

        /// <summary>The marker width in display pixels, in the font resolved at <paramref name="cluster"/>.</summary>
        private float MeasureEllipsisWidth(int cluster)
            => ShowEllipsis && TryResolveDot(cluster, out var dot)
                ? dot.Advance * EllipsisText.Length
                : 0f;

        /// <summary>
        /// Measures every range's marker once, at a cluster of that range, so the width the layout frees
        /// is the one the face the marker is drawn in actually takes.
        /// </summary>
        private void BuildRangeEllipsisWidths()
        {
            rangeEllipsisWidths.FakeClear();

            var rangeCount = Math.Min(ranges?.Count ?? 0, rangeGlyphBoundsCache.count);
            if (rangeCount == 0)
                return;

            rangeEllipsisWidths.EnsureCapacity(rangeCount);
            var widths = rangeEllipsisWidths.data;
            var boundsData = rangeGlyphBoundsCache.data;

            for (var r = 0; r < rangeCount; r++)
            {
                var (firstGlyph, _, minCluster, maxCluster) = boundsData[r];
                widths[r] = firstGlyph < 0 || minCluster > maxCluster
                    ? 0f
                    : MeasureEllipsisWidth(minCluster + (int)(ranges[r].position * (maxCluster - minCluster)));
            }

            rangeEllipsisWidths.count = rangeCount;
        }

        /// <summary>The range's marker width in shaping units — the advance the truncation must free.</summary>
        private float EllipsisWidth(int rangeIndex, float glyphScale)
        {
            var width = (uint)rangeIndex < (uint)rangeEllipsisWidths.count
                ? rangeEllipsisWidths[rangeIndex]
                : 0f;
            return glyphScale > 0 ? width / glyphScale : width;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RecalculateRunWidths(ShapedGlyph[] glyphs, ShapedRun[] runs, int runCount)
        {
            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var width = 0f;
                var end = run.glyphStart + run.glyphCount;

                for (var g = run.glyphStart; g < end; g++)
                    width += glyphs[g].advanceX;

                run.width = width;
            }
        }

        private void InjectEllipsisDots()
        {
            if (!ShowEllipsis || lineTruncations == null)
                return;

            for (var i = 0; i < lineTruncations.Count; i++)
            {
                var truncation = lineTruncations[i];
                if (truncation.ellipsisCluster < 0)
                    continue;

                InjectDotsAtCluster(truncation.ellipsisCluster, truncation.markerOffset);
            }
        }

        /// <param name="markerOffset">Pen distance from the anchor glyph's origin to the marker, in mesh-local pixels.</param>
        private void InjectDotsAtCluster(int ellipsisCluster, float markerOffset)
        {
            var positionedGlyphs = buffers.positionedGlyphs.data;
            var positionedCount = buffers.positionedGlyphs.count;

            if (positionedCount == 0)
                return;

            var shapedGlyphs = buffers.shapedGlyphs.data;
            var glyphScale = buffers.GetGlyphScale(uniText.CurrentFontSize);

            for (var i = 0; i < positionedCount; i++)
            {
                if (positionedGlyphs[i].cluster != ellipsisCluster)
                    continue;

                ref readonly var pg = ref positionedGlyphs[i];
                ref readonly var shapedGlyph = ref shapedGlyphs[pg.shapedGlyphIndex];

                var baselineX = pg.x - shapedGlyph.offsetX * glyphScale;
                var baselineY = pg.y + shapedGlyph.offsetY * glyphScale;

                if (!TryResolveDot(ellipsisCluster, out var dot))
                    return;

                var curX = baselineX + markerOffset;
                for (var d = 0; d < EllipsisText.Length; d++)
                {
                    buffers.virtualPositionedGlyphs.Add(new PositionedGlyph
                    {
                        glyphId = (int)dot.GlyphIndex,
                        cluster = ellipsisCluster,
                        x = curX,
                        y = baselineY,
                        fontId = dot.FontId,
                        shapedGlyphIndex = -1,
                        left = curX,
                        right = curX + dot.Advance,
                        top = baselineY,
                        bottom = baselineY
                    });
                    curX += dot.Advance;
                }
                return;
            }
        }

        private void OnRebuildEnd() => RestoreOriginalAdvances();

        /// <summary>
        /// Puts the pristine advances back and drops the pass's claim on them. Every entry point that is
        /// about to measure calls this first: a pass that measured advances another pass had already
        /// zeroed would mistake them for the text's own widths.
        /// </summary>
        private void RestoreOriginalAdvances()
        {
            RestoreSuppressedBreaks();

            if (!needsRestore || originalAdvances.count == 0)
            {
                needsRestore = false;
                return;
            }

            needsRestore = false;

            var glyphs = buffers.shapedGlyphs.data;
            var advances = originalAdvances.data;
            var count = Math.Min(originalAdvances.count, buffers.shapedGlyphs.count);

            for (var i = 0; i < count; i++)
                glyphs[i].advanceX = advances[i];

            RecalculateRunWidths(glyphs, buffers.shapedRuns.data, buffers.shapedRuns.count);
            RecalculateRunWidths(glyphs, buffers.orderedRuns.data, buffers.orderedRuns.count);

            originalAdvances.FakeClear();
        }
    }
}
