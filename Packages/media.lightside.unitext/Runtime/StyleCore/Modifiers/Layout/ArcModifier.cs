using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Places glyphs on concentric circular arcs while preserving each glyph's shape.
    /// </summary>
    /// <remarks>
    /// Ranges of every arc modifier resolve into one per-cluster axis, so a glyph rides the arc of the
    /// range applied last rather than being bent once per modifier.
    /// </remarks>
    [Serializable]
    [TypeGroup("Layout", 2)]
    [TypeDescription("Places glyphs along a circular baseline arc.")]
    [GenerateParameters]
    public sealed partial class ArcModifier : PooledAttributeModifier<int>
    {
        /// <summary>Arc sweep in degrees: zero is straight, positive arches upward, and negative downward.</summary>
        [SerializeField, Parameter, Range(-360f, 360f), NumberStateProperty(nameof(MarkMeshDirty), Min = -360, Max = 360)] private float angle = 90f;

        private struct ArcRange
        {
            public int start;
            public int end;
            public float angle;
        }

        private struct ArcData
        {
            public float centerX;
            public float radiansPerUnit;
            public float radius;
        }

        private const float TaylorThreshold = 0.01f;

        protected override string AttributeKey => AttributeKeys.Arc;

        [NonSerialized] private Channel pass;

        protected override AttributeChannel CreateChannel() => new Channel();

        protected override void OnEnable()
        {
            base.OnEnable();
            pass = (Channel)SharedChannel;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            pass = null;
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var resolvedAngle = Mathf.Clamp(Param.Angle.Resolve(this, in context), -360f, 360f);
            pass.AddRange(new ArcRange
            {
                start = context.Segment.Range.start,
                end = context.Segment.Range.End,
                angle = resolvedAngle
            });
        }

        private sealed class Channel : AttributeChannel
        {
            private PooledArrayAttribute<int> attribute;
            private PooledList<ArcRange> ranges;
            private PooledList<ArcData> arcs;
            private PooledList<LineRangeEntry> rangeEntries;

            private Action prepareCallback;
            private Action glyphCallback;

            internal void AddRange(in ArcRange range) => ranges.Add(range);

            protected override void OnActivate()
            {
                attribute = buffers.GetAttributeData<PooledArrayAttribute<int>>(Key);

                ranges ??= new PooledList<ArcRange>(8);
                ranges.FakeClear();
                arcs ??= new PooledList<ArcData>(8);
                arcs.FakeClear();
                rangeEntries ??= new PooledList<LineRangeEntry>(8);

                prepareCallback ??= PrepareArcs;
                glyphCallback ??= OnGlyph;
                uniText.BeforeGenerateMesh.Subscribe(prepareCallback);
                uniText.MeshGenerator.onGlyph.Subscribe(glyphCallback);
            }

            protected override void OnDeactivate()
            {
                uniText.BeforeGenerateMesh.Unsubscribe(prepareCallback);
                uniText.MeshGenerator.onGlyph.Unsubscribe(glyphCallback);
                attribute?.buffer.data?.AsSpan().Clear();
            }

            protected override void OnBeginCycle() => ranges?.FakeClear();

            protected override void OnRelease()
            {
                attribute = null;
                ranges?.Return();
                ranges = null;
                arcs?.Return();
                arcs = null;
                rangeEntries?.Return();
                rangeEntries = null;
            }

            private void PrepareArcs()
            {
                var indices = attribute.buffer.data;
                if (indices == null) return;
                var count = buffers.codepoints.count;
                indices.AsSpan(0, count).Clear();
                arcs.FakeClear();

                var positionedGlyphs = buffers.positionedGlyphs.data;
                for (var r = 0; r < ranges.Count; r++)
                {
                    var range = ranges[r];
                    var start = Math.Max(0, range.start);
                    var end = Math.Min(range.end, count);
                    if (end <= start) continue;

                    if (range.angle == 0f)
                    {
                        indices.AsSpan(start, end - start).Clear();
                        continue;
                    }

                    uniText.CollectRangeEntries(start, end, rangeEntries);
                    if (rangeEntries.Count == 0) continue;

                    var minX = rangeEntries[0].minX;
                    var maxX = rangeEntries[0].maxX;
                    var minLineY = rangeEntries[0].minY;
                    var maxLineY = rangeEntries[0].minY;
                    for (var i = 1; i < rangeEntries.Count; i++)
                    {
                        var entry = rangeEntries[i];
                        minX = Math.Min(minX, entry.minX);
                        maxX = Math.Max(maxX, entry.maxX);
                        minLineY = Math.Min(minLineY, entry.minY);
                        maxLineY = Math.Max(maxLineY, entry.minY);
                    }

                    var width = maxX - minX;
                    if (width <= 0f) continue;

                    var centerX = (minX + maxX) * 0.5f;
                    var referenceRadius = width / (range.angle * Mathf.Deg2Rad);
                    var referenceLineY = range.angle > 0f ? maxLineY : minLineY;
                    var currentLineIdx = -1;
                    var arcIndex = 0;

                    for (var i = 0; i < rangeEntries.Count; i++)
                    {
                        var entry = rangeEntries[i];
                        if (entry.lineIdx != currentLineIdx)
                        {
                            var radius = referenceRadius + referenceLineY - entry.minY;
                            arcs.Add(new ArcData
                            {
                                centerX = centerX,
                                radiansPerUnit = 1f / radius,
                                radius = radius
                            });
                            currentLineIdx = entry.lineIdx;
                            arcIndex = arcs.Count;
                        }

                        for (var g = entry.firstGlyphIdx; g <= entry.lastGlyphIdx; g++)
                        {
                            var cluster = positionedGlyphs[g].cluster;
                            if ((uint)cluster < (uint)count)
                                indices[cluster] = arcIndex;
                        }
                    }
                }
            }

            private void OnGlyph()
            {
                var gen = uniText.MeshGenerator;
                var cluster = gen.currentCluster;
                if ((uint)cluster >= (uint)buffers.codepoints.count) return;

                var arcIndex = attribute.buffer.data[cluster] - 1;
                if ((uint)arcIndex >= (uint)arcs.Count) return;
                ref readonly var arc = ref arcs[arcIndex];

                var vertices = gen.Vertices;
                var baseIdx = gen.faceBaseIdx;
                var pivotX = (vertices[baseIdx].x + vertices[baseIdx + 2].x) * 0.5f;
                var pivotY = gen.baselineY;
                var theta = (pivotX - gen.offsetX - arc.centerX) * arc.radiansPerUnit;
                var sin = Mathf.Sin(theta);
                var cos = Mathf.Cos(theta);
                var targetX = gen.offsetX + arc.centerX + sin * arc.radius;
                var thetaSquared = theta * theta;
                var radialY = Mathf.Abs(theta) < TaylorThreshold
                    ? thetaSquared * (-0.5f + thetaSquared / 24f) * arc.radius
                    : (cos - 1f) * arc.radius;
                var targetY = pivotY + radialY;

                for (var i = 0; i < 4; i++)
                {
                    ref var vertex = ref vertices[baseIdx + i];
                    var x = vertex.x - pivotX;
                    var y = vertex.y - pivotY;
                    vertex.x = targetX + x * cos + y * sin;
                    vertex.y = targetY - x * sin + y * cos;
                }
            }
        }
    }
}
