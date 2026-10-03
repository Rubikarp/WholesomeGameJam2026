using System;
using UnityEngine;

namespace LightSide
{
    internal sealed class RangeGeometryIndex : IDisposable
    {
        private static readonly Func<UniTextBase, RangeGeometryIndex> create =
            static owner => new RangeGeometryIndex(owner);

        private const int IdentityGeometry = -2;
        private const byte Positioned = 1 << 0;
        private const byte Visible = 1 << 1;

        private readonly UniTextBase owner;
        private PooledBuffer<RangeBoundsEntry> entryScratch;
        private PooledBuffer<RangeVisualFragment> fragmentScratch;
        private PooledBuffer<RangeVisualFragment> layoutFragmentScratch;
        private PooledBuffer<RangeVisualGlyph> visualGlyphScratch;
        private PooledBuffer<GlyphVisualGeometry> glyphGeometry;
        private PooledBuffer<int> glyphGeometryByPosition;
        private PooledBuffer<int> geometryByCluster;
        private PooledBuffer<int> clusterOffsets;
        private PooledBuffer<int> identityGlyphsByCluster;
        private PooledBuffer<byte> positionedClusterFlags;
        private PooledBuffer<int> lineByPosition;
        private PooledBuffer<int> layoutClusters;
        private UniTextMeshGenerator capturedGenerator;
        private Action captureStartCallback;
        private Action<GlyphVisualGeometry> glyphCallback;
        private int captureOwners;
        private int exactGlyphOwners;
        private bool indexDirty;
        private bool hasCapture;
        private bool resetExactCapture;
        private bool disposed;
        private int version;
        private byte blockBoundsValid;
        private Rect lineBoxBounds;
        private Rect contentBounds;
        private Rect lineAdvanceBounds;

        public RangeGeometryIndex(UniTextBase owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            owner.MeshGeneratorChanged += SetMeshGenerator;
            owner.CommitFinalizing += Commit;
        }

        public static RangeGeometryIndex For(UniTextBase owner)
            => owner.GetOrCreateAttachment(create);

        public static bool TryGet(UniTextBase owner, out RangeGeometryIndex geometry)
            => owner.TryGetAttachment(out geometry);

        public int Version => version;

        public void Retain(bool exactGlyphs = false)
        {
            ThrowIfDisposed();
            captureOwners++;
            if (exactGlyphs) exactGlyphOwners++;
            if (exactGlyphs && exactGlyphOwners == 1) hasCapture = false;

            if (captureOwners == 1)
            {
                captureStartCallback ??= ResetCapture;
                glyphCallback ??= CaptureGlyph;
                SetMeshGenerator(owner.MeshGenerator);
            }

            if (capturedGenerator != null)
                capturedGenerator.captureIdentityGlyphGeometry = exactGlyphOwners > 0;
            if (!hasCapture) RequestCapture();
        }

        public void Release(bool exactGlyphs = false)
        {
            if (disposed) return;
            if (captureOwners <= 0 || exactGlyphs && exactGlyphOwners <= 0)
                throw new InvalidOperationException("Range geometry capture ownership is unbalanced.");
            captureOwners--;
            if (exactGlyphs) exactGlyphOwners--;
            if (capturedGenerator != null)
                capturedGenerator.captureIdentityGlyphGeometry = exactGlyphOwners > 0;
            if (captureOwners != 0)
            {
                if (exactGlyphs && exactGlyphOwners == 0)
                {
                    resetExactCapture = true;
                    hasCapture = false;
                    RequestCapture();
                }
                return;
            }

            SetMeshGenerator(null);
            exactGlyphOwners = 0;
            ReturnBuffers();
        }

        public void SetMeshGenerator(UniTextMeshGenerator generator)
        {
            ThrowIfDisposed();
            if (captureOwners == 0) generator = null;
            if (ReferenceEquals(capturedGenerator, generator)) return;
            if (capturedGenerator != null)
            {
                capturedGenerator.onGlyphGeometryRebuildStart -= captureStartCallback;
                capturedGenerator.onGlyphComplete -= glyphCallback;
                capturedGenerator.captureIdentityGlyphGeometry = false;
            }
            capturedGenerator = generator;
            hasCapture = false;
            indexDirty = false;
            blockBoundsValid = 0;
            glyphGeometry.FakeClear();
            glyphGeometryByPosition.FakeClear();
            geometryByCluster.FakeClear();
            clusterOffsets.FakeClear();
            identityGlyphsByCluster.FakeClear();
            positionedClusterFlags.FakeClear();
            lineByPosition.FakeClear();
            if (capturedGenerator == null || captureOwners == 0) return;
            capturedGenerator.onGlyphGeometryRebuildStart += captureStartCallback;
            capturedGenerator.onGlyphComplete += glyphCallback;
            capturedGenerator.captureIdentityGlyphGeometry = exactGlyphOwners > 0;
        }

        public void Commit(UniTextCommitChanges changes)
        {
            if (captureOwners == 0 || capturedGenerator == null ||
                (changes & UniTextCommitChanges.GlyphGeometry) == 0) return;
            BuildIndexes();
            hasCapture = true;
            version++;
        }

        public ReadOnlySpan<RangeVisualFragment> GetLineFragments(int start, int end,
            RangeHeight height)
        {
            EnsureIndexes();
            owner.CollectRangeBounds(start, end, height, ref entryScratch);
            EnrichEntries(end);
            fragmentScratch.FakeClear();
            fragmentScratch.EnsureCapacity(entryScratch.count);
            for (var i = 0; i < entryScratch.count; i++)
            {
                ref readonly var entry = ref entryScratch[i];
                var layout = ToVisualFragment(in entry);
                if (entry.firstGlyphIndex < 0)
                {
                    if (IsInsideVisibleWindow(layout.Bounds)) fragmentScratch.Add(layout);
                    continue;
                }
                if (!TryGetVisibleLayoutBounds(entry.firstGlyphIndex, entry.lastGlyphIndex,
                        layout.LayoutBounds, out var visibleLayout))
                {
                    if (TryResolveGeometry(entry.firstGlyphIndex, entry.lastGlyphIndex,
                            layout.LayoutBounds, out var virtualGeometry))
                        fragmentScratch.Add(WithGeometry(in layout, in virtualGeometry));
                    continue;
                }
                if (TryResolveGeometry(entry.firstGlyphIndex, entry.lastGlyphIndex,
                        visibleLayout, out var resolved))
                    fragmentScratch.Add(WithGeometry(in layout, in resolved));
                else if (IsInsideVisibleWindow(visibleLayout))
                    fragmentScratch.Add(WithLayoutBounds(in layout, visibleLayout));
            }
            return fragmentScratch.Span;
        }

        public ReadOnlySpan<RangeVisualFragment> GetGlyphFragments(int start, int end,
            RangeHeight height)
        {
            EnsureIndexes();
            CollectGlyphFragments(start, end, height);
            fragmentScratch.FakeClear();
            fragmentScratch.EnsureCapacity(layoutFragmentScratch.count);
            for (var i = 0; i < layoutFragmentScratch.count; i++)
            {
                ref readonly var layout = ref layoutFragmentScratch[i];
                if (TryResolveClusterGeometry(layout.ClusterStart, layout.LayoutBounds,
                        out var resolved))
                {
                    fragmentScratch.Add(WithGeometry(in layout, in resolved));
                    continue;
                }

                var flags = (uint)layout.ClusterStart < (uint)positionedClusterFlags.count
                    ? positionedClusterFlags[layout.ClusterStart]
                    : (byte)0;
                if ((flags & Visible) != 0 || (flags & Positioned) == 0)
                {
                    if (IsInsideVisibleWindow(layout.Bounds)) fragmentScratch.Add(layout);
                }
            }
            return fragmentScratch.Span;
        }

        private void CollectGlyphFragments(int start, int end, RangeHeight height)
        {
            layoutFragmentScratch.FakeClear();
            owner.CollectRangeBounds(start, end, height, ref entryScratch);
            EnrichEntries(end);
            if (entryScratch.count == 0) return;

            var glyphs = owner.ResultGlyphs;
            layoutClusters.FakeClear();
            for (var i = 0; i < entryScratch.count; i++)
            {
                ref readonly var entry = ref entryScratch[i];
                for (var glyphIndex = entry.firstGlyphIndex;
                     glyphIndex >= 0 && glyphIndex <= entry.lastGlyphIndex;
                     glyphIndex++)
                    layoutClusters.Add(glyphs[glyphIndex].cluster);
            }
            if (layoutClusters.count > 1)
                Array.Sort(layoutClusters.data, 0, layoutClusters.count);
            var uniqueCount = 0;
            for (var i = 0; i < layoutClusters.count; i++)
                if (uniqueCount == 0 || layoutClusters[i] != layoutClusters[uniqueCount - 1])
                    layoutClusters[uniqueCount++] = layoutClusters[i];
            layoutClusters.count = uniqueCount;

            var componentRect = owner.cachedTransformData.rect;
            for (var i = 0; i < entryScratch.count; i++)
            {
                ref readonly var entry = ref entryScratch[i];
                if (entry.firstGlyphIndex < 0)
                {
                    layoutFragmentScratch.Add(ToVisualFragment(in entry));
                    continue;
                }

                var glyphIndex = entry.firstGlyphIndex;
                while (glyphIndex <= entry.lastGlyphIndex)
                {
                    var first = glyphIndex;
                    var cluster = glyphs[glyphIndex].cluster;
                    float minX = glyphs[glyphIndex].left, maxX = glyphs[glyphIndex].right;
                    float minY = glyphs[glyphIndex].top, maxY = glyphs[glyphIndex].bottom;
                    while (++glyphIndex <= entry.lastGlyphIndex &&
                           glyphs[glyphIndex].cluster == cluster)
                    {
                        ref readonly var glyph = ref glyphs[glyphIndex];
                        if (glyph.left < minX) minX = glyph.left;
                        if (glyph.right > maxX) maxX = glyph.right;
                        if (glyph.top < minY) minY = glyph.top;
                        if (glyph.bottom > maxY) maxY = glyph.bottom;
                    }

                    Rect bounds;
                    if (height == RangeHeight.Content)
                    {
                        UniTextBase.ComputeInkExtents(owner, glyphs, first, glyphIndex - 1,
                            ref minY, ref maxY);
                        bounds = new Rect(componentRect.xMin + minX, componentRect.yMax - maxY,
                            maxX - minX, maxY - minY);
                    }
                    else
                    {
                        bounds = new Rect(componentRect.xMin + minX, entry.rect.yMin,
                            maxX - minX, entry.rect.height);
                    }

                    var sorted = BinarySearchLayoutCluster(cluster);
                    var clusterEnd = sorted + 1 < layoutClusters.count
                        ? layoutClusters[sorted + 1]
                        : end;
                    var containsStart = entry.containsRangeStart &&
                                        cluster <= entry.clusterStart && clusterEnd > entry.clusterStart;
                    var containsEnd = entry.containsRangeEnd &&
                                      cluster < entry.clusterEnd && clusterEnd >= entry.clusterEnd;
                    var rtl = owner.Buffers.IsRtlLevelAt(cluster);
                    layoutFragmentScratch.Add(new RangeVisualFragment(bounds, entry.lineIndex, rtl,
                        containsStart, containsEnd, rtl, !rtl, cluster, clusterEnd));
                }
            }
        }

        private int BinarySearchLayoutCluster(int cluster)
        {
            var low = 0;
            var high = layoutClusters.count - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) >> 1);
                var value = layoutClusters[middle];
                if (value == cluster) return middle;
                if (value < cluster) low = middle + 1;
                else high = middle - 1;
            }
            throw new InvalidOperationException("A collected glyph cluster was absent from its cluster index.");
        }

        private void EnrichEntries(int requestedEnd)
        {
            if (entryScratch.count == 0) return;
            var glyphs = owner.ResultGlyphs;
            var buffers = owner.Buffers;
            var lines = buffers.lines;
            for (var i = 0; i < entryScratch.count; i++)
            {
                ref var entry = ref entryScratch[i];
                if (entry.firstGlyphIndex < 0)
                {
                    var cluster = lines[entry.lineIndex].range.start;
                    entry.rtl = buffers.IsRtlLevelAt(cluster);
                    entry.clusterStart = cluster;
                    entry.clusterEnd = Math.Min(cluster + 1, requestedEnd);
                    continue;
                }

                var clusterStart = int.MaxValue;
                var clusterEnd = int.MinValue;
                for (var glyphIndex = entry.firstGlyphIndex;
                     glyphIndex <= entry.lastGlyphIndex;
                     glyphIndex++)
                {
                    var cluster = glyphs[glyphIndex].cluster;
                    if (cluster < clusterStart) clusterStart = cluster;
                    if (cluster > clusterEnd) clusterEnd = cluster;
                }
                entry.rtl = buffers.IsRtlLevelAt(glyphs[entry.firstGlyphIndex].cluster);
                entry.clusterStart = clusterStart;
                entry.clusterEnd = clusterEnd + 1;
            }

            var visibleStart = int.MaxValue;
            var visibleEnd = int.MinValue;
            for (var i = 0; i < entryScratch.count; i++)
            {
                ref readonly var entry = ref entryScratch[i];
                if (entry.clusterStart < visibleStart) visibleStart = entry.clusterStart;
                if (entry.clusterEnd > visibleEnd) visibleEnd = entry.clusterEnd;
            }

            var endCluster = visibleEnd - 1;
            var startOnRight = buffers.IsRtlLevelAt(visibleStart);
            var endOnRight = !buffers.IsRtlLevelAt(endCluster);
            var markedStart = false;
            var markedEnd = false;
            for (var i = 0; i < entryScratch.count; i++)
            {
                ref var entry = ref entryScratch[i];
                if (!markedStart && entry.clusterStart <= visibleStart && entry.clusterEnd > visibleStart)
                {
                    entry.containsRangeStart = true;
                    entry.rangeStartOnRight = startOnRight;
                    markedStart = true;
                }
                if (!markedEnd && entry.clusterStart <= endCluster && entry.clusterEnd > endCluster)
                {
                    entry.containsRangeEnd = true;
                    entry.rangeEndOnRight = endOnRight;
                    markedEnd = true;
                }
            }
        }

        private static RangeVisualFragment ToVisualFragment(in RangeBoundsEntry entry)
            => new(entry.rect, entry.lineIndex, entry.rtl, entry.containsRangeStart,
                entry.containsRangeEnd, entry.rangeStartOnRight, entry.rangeEndOnRight,
                entry.clusterStart, entry.clusterEnd);

        public Rect GetTextBlockBounds(RangeHeight height)
        {
            var mask = (byte)(1 << (int)height);
            if ((blockBoundsValid & mask) != 0) return GetCachedBlockBounds(height);
            var fragments = GetLineFragments(0, owner.Buffers.codepoints.count, height);
            var result = default(Rect);
            if (fragments.Length > 0)
            {
                result = fragments[0].Bounds;
                for (var i = 1; i < fragments.Length; i++)
                    Encapsulate(ref result, fragments[i].Bounds);
            }
            SetCachedBlockBounds(height, result);
            blockBoundsValid |= mask;
            return result;
        }

        public ReadOnlySpan<RangeVisualGlyph> GetVisualGlyphs(int start, int end)
        {
            if (exactGlyphOwners == 0 || !hasCapture)
                throw new InvalidOperationException(
                    "This decoration did not retain exact per-glyph range geometry.");
            EnsureIndexes();
            visualGlyphScratch.FakeClear();
            if (end <= start || clusterOffsets.count == 0) return visualGlyphScratch.Span;
            var from = Mathf.Clamp(start, 0, clusterOffsets.count - 1);
            var to = Mathf.Clamp(end, from, clusterOffsets.count - 1);
            visualGlyphScratch.EnsureCapacity(clusterOffsets[to] - clusterOffsets[from]);
            for (var cluster = from; cluster < to; cluster++)
            {
                var first = clusterOffsets[cluster];
                var last = clusterOffsets[cluster + 1];
                for (var i = first; i < last; i++)
                {
                    ref readonly var geometry = ref glyphGeometry[geometryByCluster[i]];
                    visualGlyphScratch.Add(new RangeVisualGlyph(in geometry,
                        FindLineIndex(geometry.positionedGlyphIndex, geometry.cluster)));
                }
            }
            return visualGlyphScratch.Span;
        }

        public void Dispose()
        {
            if (disposed) return;
            owner.MeshGeneratorChanged -= SetMeshGenerator;
            owner.CommitFinalizing -= Commit;
            SetMeshGenerator(null);
            disposed = true;
            captureOwners = 0;
            exactGlyphOwners = 0;
            ReturnBuffers();
        }

        private readonly struct ResolvedFragmentGeometry
        {
            public readonly Rect bounds;
            public readonly RangeVisualTransformKind kind;
            public readonly RangeVisualTransform transform;

            public ResolvedFragmentGeometry(Rect bounds, RangeVisualTransformKind kind,
                RangeVisualTransform transform)
            {
                this.bounds = bounds;
                this.kind = kind;
                this.transform = transform;
            }
        }

        private void ResetCapture()
        {
            if (resetExactCapture)
            {
                glyphGeometry.Return();
                glyphGeometryByPosition.Return();
                geometryByCluster.Return();
                clusterOffsets.Return();
                identityGlyphsByCluster.Return();
                positionedClusterFlags.Return();
                lineByPosition.Return();
                visualGlyphScratch.Return();
                resetExactCapture = false;
            }
            glyphGeometry.FakeClear();
            var count = owner.ResultGlyphs.Length;
            glyphGeometryByPosition.SetCount(count);
            glyphGeometryByPosition.Span.Fill(IdentityGeometry);
            indexDirty = true;
            hasCapture = false;
            blockBoundsValid = 0;
        }

        private void CaptureGlyph(GlyphVisualGeometry geometry)
        {
            var index = glyphGeometry.count;
            glyphGeometry.Add(geometry);
            if (!geometry.isVirtual &&
                (uint)geometry.positionedGlyphIndex < (uint)glyphGeometryByPosition.count)
                glyphGeometryByPosition[geometry.positionedGlyphIndex] = index;
        }

        private void BuildIndexes()
        {
            if (!indexDirty) return;
            indexDirty = false;
            blockBoundsValid = 0;
            var clusterCount = owner.Buffers.codepoints.count;
            clusterOffsets.SetCount(clusterCount + 1);
            clusterOffsets.Span.Clear();
            var validGeometryCount = 0;
            for (var i = 0; i < glyphGeometry.count; i++)
            {
                var cluster = glyphGeometry[i].cluster;
                if ((uint)cluster >= (uint)clusterCount) continue;
                clusterOffsets[cluster + 1]++;
                validGeometryCount++;
            }
            for (var i = 1; i < clusterOffsets.count; i++)
                clusterOffsets[i] += clusterOffsets[i - 1];

            geometryByCluster.SetCount(validGeometryCount);
            var total = validGeometryCount;
            for (var i = glyphGeometry.count - 1; i >= 0; i--)
            {
                var cluster = glyphGeometry[i].cluster;
                if ((uint)cluster >= (uint)clusterCount) continue;
                geometryByCluster[--clusterOffsets[cluster + 1]] = i;
            }
            for (var i = 1; i < clusterOffsets.count - 1; i++)
                clusterOffsets[i] = clusterOffsets[i + 1];
            if (clusterOffsets.count > 0)
                clusterOffsets[clusterOffsets.count - 1] = total;

            identityGlyphsByCluster.SetCount(clusterCount);
            identityGlyphsByCluster.Span.Clear();
            positionedClusterFlags.SetCount(clusterCount);
            positionedClusterFlags.Span.Clear();
            var glyphs = owner.ResultGlyphs;
            var hidden = owner.Buffers.hiddenClusters;
            for (var i = 0; i < glyphs.Length; i++)
            {
                var cluster = glyphs[i].cluster;
                if ((uint)cluster >= (uint)clusterCount) continue;
                var flags = Positioned;
                if ((uint)cluster >= (uint)hidden.count || hidden[cluster] == 0)
                    flags |= Visible;
                positionedClusterFlags[cluster] |= flags;
                if ((uint)i < (uint)glyphGeometryByPosition.count &&
                    glyphGeometryByPosition[i] == IdentityGeometry)
                    identityGlyphsByCluster[cluster]++;
            }

            if (exactGlyphOwners > 0)
            {
                lineByPosition.SetCount(glyphs.Length);
                lineByPosition.Span.Fill(-1);
                var lines = owner.Buffers.lines;
                for (var lineIndex = 0; lineIndex < lines.count; lineIndex++)
                {
                    ref readonly var line = ref lines[lineIndex];
                    var end = Math.Min(line.glyphStart + line.glyphCount, lineByPosition.count);
                    for (var i = Math.Max(0, line.glyphStart); i < end; i++)
                        lineByPosition[i] = lineIndex;
                }
            }
            else lineByPosition.Return();
        }

        private void EnsureIndexes()
        {
            BuildIndexes();
        }

        private void RequestCapture()
        {
            if ((owner.ScheduledCommitChanges & UniTextCommitChanges.GlyphGeometry) != 0 ||
                !owner.isActiveAndEnabled) return;
            owner.SetDirty(UniTextDirty.Mesh,
                UniTextCommitChanges.GlyphGeometry | UniTextCommitChanges.Appearance);
        }

        private bool TryResolveGeometry(int firstGlyphIndex, int lastGlyphIndex, Rect layoutBounds,
            out ResolvedFragmentGeometry resolved)
        {
            var found = false;
            var hasIdentity = false;
            var identity = true;
            var uniform = true;
            var transform = default(RangeVisualTransform);
            var bounds = default(Rect);
            for (var i = firstGlyphIndex; i <= lastGlyphIndex; i++)
            {
                if ((uint)i >= (uint)glyphGeometryByPosition.count) continue;
                var geometryIndex = glyphGeometryByPosition[i];
                if (geometryIndex == IdentityGeometry)
                {
                    hasIdentity = true;
                    continue;
                }
                if ((uint)geometryIndex >= (uint)glyphGeometry.count) continue;
                ref readonly var geometry = ref glyphGeometry[geometryIndex];
                AccumulateGeometry(in geometry, ref found, ref identity, ref uniform,
                    ref transform, ref bounds);
            }

            var glyphs = owner.ResultGlyphs;
            if (firstGlyphIndex >= 0 && firstGlyphIndex < glyphs.Length)
            {
                var minCluster = int.MaxValue;
                var maxCluster = int.MinValue;
                for (var i = firstGlyphIndex; i <= lastGlyphIndex && i < glyphs.Length; i++)
                {
                    minCluster = Math.Min(minCluster, glyphs[i].cluster);
                    maxCluster = Math.Max(maxCluster, glyphs[i].cluster);
                }
                AccumulateVirtualGeometry(minCluster, maxCluster, ref found, ref identity,
                    ref uniform, ref transform, ref bounds);
            }

            if (hasIdentity && found)
            {
                identity = false;
                uniform = false;
                Encapsulate(ref bounds, layoutBounds);
            }
            return CompleteGeometry(found, identity, uniform, in transform, layoutBounds,
                bounds, out resolved);
        }

        private bool TryResolveClusterGeometry(int cluster, Rect layoutBounds,
            out ResolvedFragmentGeometry resolved)
        {
            var found = false;
            var identity = true;
            var uniform = true;
            var transform = default(RangeVisualTransform);
            var bounds = default(Rect);
            if (cluster >= 0 && cluster + 1 < clusterOffsets.count)
            {
                var first = clusterOffsets[cluster];
                var last = clusterOffsets[cluster + 1];
                for (var i = first; i < last; i++)
                {
                    ref readonly var geometry = ref glyphGeometry[geometryByCluster[i]];
                    AccumulateGeometry(in geometry, ref found, ref identity, ref uniform,
                        ref transform, ref bounds);
                }
            }
            if ((uint)cluster < (uint)identityGlyphsByCluster.count &&
                identityGlyphsByCluster[cluster] > 0 && found)
            {
                identity = false;
                uniform = false;
                Encapsulate(ref bounds, layoutBounds);
            }
            return CompleteGeometry(found, identity, uniform, in transform, layoutBounds,
                bounds, out resolved);
        }

        private void AccumulateVirtualGeometry(int minCluster, int maxCluster, ref bool found,
            ref bool identity, ref bool uniform, ref RangeVisualTransform transform, ref Rect bounds)
        {
            if (minCluster > maxCluster || clusterOffsets.count == 0) return;
            var from = Math.Max(0, minCluster);
            var to = Math.Min(maxCluster, clusterOffsets.count - 2);
            for (var cluster = from; cluster <= to; cluster++)
            {
                var first = clusterOffsets[cluster];
                var last = clusterOffsets[cluster + 1];
                for (var i = first; i < last; i++)
                {
                    ref readonly var geometry = ref glyphGeometry[geometryByCluster[i]];
                    if (!geometry.isVirtual) continue;
                    AccumulateGeometry(in geometry, ref found, ref identity, ref uniform,
                        ref transform, ref bounds);
                }
            }
        }

        private static void AccumulateGeometry(in GlyphVisualGeometry geometry, ref bool found,
            ref bool identity, ref bool uniform, ref RangeVisualTransform transform, ref Rect bounds)
        {
            var current = new RangeVisualTransform(in geometry);
            var currentBounds = geometry.transform.final.Bounds;
            if (!found)
            {
                found = true;
                transform = current;
                bounds = currentBounds;
                identity = current.IsIdentity;
                return;
            }

            identity &= current.IsIdentity;
            uniform &= transform.EquivalentTo(in current);
            Encapsulate(ref bounds, currentBounds);
        }

        private static bool CompleteGeometry(bool found, bool identity, bool uniform,
            in RangeVisualTransform transform, Rect layoutBounds, Rect nonUniformBounds,
            out ResolvedFragmentGeometry resolved)
        {
            if (!found)
            {
                resolved = default;
                return false;
            }
            if (identity)
            {
                resolved = new ResolvedFragmentGeometry(layoutBounds,
                    RangeVisualTransformKind.Identity, default);
                return true;
            }
            if (uniform)
            {
                resolved = new ResolvedFragmentGeometry(transform.TransformBounds(layoutBounds),
                    RangeVisualTransformKind.Uniform, transform);
                return true;
            }
            resolved = new ResolvedFragmentGeometry(nonUniformBounds,
                RangeVisualTransformKind.NonUniform, default);
            return true;
        }

        private bool TryGetVisibleLayoutBounds(int firstGlyphIndex, int lastGlyphIndex,
            Rect original, out Rect visible)
        {
            var glyphs = owner.ResultGlyphs;
            var hidden = owner.Buffers.hiddenClusters;
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            for (var i = firstGlyphIndex; i <= lastGlyphIndex && i < glyphs.Length; i++)
            {
                ref readonly var glyph = ref glyphs[i];
                if ((uint)glyph.cluster < (uint)hidden.count && hidden[glyph.cluster] != 0) continue;
                minX = Mathf.Min(minX, glyph.left);
                maxX = Mathf.Max(maxX, glyph.right);
            }

            if (minX == float.MaxValue || maxX <= minX)
            {
                visible = default;
                return false;
            }

            var offsetX = owner.cachedTransformData.rect.xMin;
            visible = Rect.MinMaxRect(offsetX + minX, original.yMin, offsetX + maxX, original.yMax);
            return IsInsideVisibleWindow(visible);
        }

        private int FindLineIndex(int positionedGlyphIndex, int cluster)
        {
            if ((uint)positionedGlyphIndex < (uint)lineByPosition.count &&
                lineByPosition[positionedGlyphIndex] >= 0)
                return lineByPosition[positionedGlyphIndex];
            var lines = owner.Buffers.lines;
            if (lines.count > 0) return SelectionHitTest.FindLineAtCodepoint(cluster, lines);
            throw new InvalidOperationException("Captured glyph geometry has no owning layout line.");
        }

        private bool IsInsideVisibleWindow(Rect bounds)
            => owner.VisibleWindow is not { } window || window.Overlaps(bounds, true);

        private Rect GetCachedBlockBounds(RangeHeight height)
            => height switch
            {
                RangeHeight.LineBox => lineBoxBounds,
                RangeHeight.Content => contentBounds,
                RangeHeight.LineAdvance => lineAdvanceBounds,
                _ => throw new ArgumentOutOfRangeException(nameof(height)),
            };

        private void SetCachedBlockBounds(RangeHeight height, Rect value)
        {
            switch (height)
            {
                case RangeHeight.LineBox: lineBoxBounds = value; break;
                case RangeHeight.Content: contentBounds = value; break;
                case RangeHeight.LineAdvance: lineAdvanceBounds = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(height));
            }
        }

        private void ReturnBuffers()
        {
            entryScratch.Return();
            fragmentScratch.Return();
            layoutFragmentScratch.Return();
            visualGlyphScratch.Return();
            glyphGeometry.Return();
            glyphGeometryByPosition.Return();
            geometryByCluster.Return();
            clusterOffsets.Return();
            identityGlyphsByCluster.Return();
            positionedClusterFlags.Return();
            lineByPosition.Return();
            layoutClusters.Return();
            indexDirty = false;
            hasCapture = false;
            resetExactCapture = false;
            blockBoundsValid = 0;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(RangeGeometryIndex));
        }

        private static RangeVisualFragment WithGeometry(in RangeVisualFragment source,
            in ResolvedFragmentGeometry geometry)
            => new(source.LayoutBounds, geometry.bounds, geometry.kind, geometry.transform,
                source.LineIndex, source.IsRightToLeft, source.ContainsRangeStart,
                source.ContainsRangeEnd, source.RangeStartOnRight, source.RangeEndOnRight,
                source.ClusterStart, source.ClusterEnd);

        private static RangeVisualFragment WithLayoutBounds(in RangeVisualFragment source, Rect bounds)
            => new(bounds, bounds, RangeVisualTransformKind.Identity, default,
                source.LineIndex, source.IsRightToLeft, source.ContainsRangeStart,
                source.ContainsRangeEnd, source.RangeStartOnRight, source.RangeEndOnRight,
                source.ClusterStart, source.ClusterEnd);

        private static void Encapsulate(ref Rect target, Rect value)
        {
            target.xMin = Mathf.Min(target.xMin, value.xMin);
            target.yMin = Mathf.Min(target.yMin, value.yMin);
            target.xMax = Mathf.Max(target.xMax, value.xMax);
            target.yMax = Mathf.Max(target.yMax, value.yMax);
        }
    }
}
