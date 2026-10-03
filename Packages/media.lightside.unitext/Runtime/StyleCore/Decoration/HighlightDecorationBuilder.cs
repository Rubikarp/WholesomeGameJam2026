using System;
using UnityEngine;
using TextPaintMapping = LightSide.PaintMapping;

namespace LightSide
{
    internal sealed class HighlightDecorationBuilder : IDisposable
    {
        private const float CornerSafeInsetFactor = 0.292893218f;

        private struct Piece
        {
            public RangeVisualFragment fragment;
            public Rect layoutBounds;
            public Rect bounds;
            public RangeDecorationCorners corners;
            public RangeVisualTransformKind transformKind;
            public RangeVisualTransform transform;
        }

        private readonly struct BlockPaintVertex
        {
            public readonly Vector2 position;
            public readonly float coverage;

            public BlockPaintVertex(Vector2 position, float coverage)
            {
                this.position = position;
                this.coverage = coverage;
            }
        }

        private PooledBuffer<Piece> pieces;
        private PooledBuffer<RangeVisualFragment> glyphs;
        private PooledBuffer<RangeBlockBand> lineFrames;
        private PooledBuffer<int> blockVertexIndices;
        private PooledBuffer<int> blockOuterVertexIndices;
        private readonly RangeBlockGeometry blockGeometry = new();

        public bool Build(RangeGeometryIndex geometryIndex, int start, int end, float fontSize,
            in ResolvedHighlightPresentation style, int rampRow, ref RangeMeshWriter writer)
        {
            var source = style.geometry == GeometryMapping.Glyph
                ? geometryIndex.GetGlyphFragments(start, end, style.height)
                : geometryIndex.GetLineFragments(start, end, style.height);
            BuildPieces(source, style.geometry, style.paddingX, style.paddingY,
                style.radius, style.merge);
            if (pieces.count == 0) return false;

            if (style.paint.mapping == TextPaintMapping.Glyph && style.geometry != GeometryMapping.Glyph)
            {
                var glyphSource = geometryIndex.GetGlyphFragments(start, end, style.height);
                glyphs.FakeClear();
                glyphs.EnsureCapacity(glyphSource.Length);
                for (var i = 0; i < glyphSource.Length; i++) glyphs.Add(glyphSource[i]);
            }

            BuildLineFrames();
            if (style.geometry == GeometryMapping.Block)
            {
                var blockRadius = CompensateBlockFrames(style.radius);
                IncludeBlockVisualEnds(style.paddingX, style.paddingY, style.radius);
                return EmitBlock(geometryIndex, fontSize, in style, blockRadius,
                    rampRow, ref writer);
            }

            var rangeBounds = UnionPieces();
            var textBounds = style.paint.mapping == TextPaintMapping.Block
                ? geometryIndex.GetTextBlockBounds(style.height)
                : default;
            var decorationPaint = new RangeDecorationPaint(in style.paint, rampRow);

            for (var i = 0; i < pieces.count; i++)
            {
                ref readonly var piece = ref pieces[i];
                if (style.paint.mapping == TextPaintMapping.Glyph && style.geometry != GeometryMapping.Glyph)
                {
                    EmitGlyphPaintSections(in piece, in decorationPaint, style.order,
                        style.tint, style.radius, ref writer);
                    continue;
                }

                var frameBounds = style.paint.mapping switch
                {
                    TextPaintMapping.Glyph => piece.bounds,
                    TextPaintMapping.Line => FindLineFrame(piece.fragment.LineIndex),
                    TextPaintMapping.Range => rangeBounds,
                    TextPaintMapping.Block => textBounds,
                    _ => rangeBounds,
                };
                writer.SetPaint(in decorationPaint, frameBounds, style.order, style.tint);
                AddPieceRoundedRect(in piece, style.radius, ref writer);
            }
            return true;
        }

        public void Dispose()
        {
            pieces.Return();
            glyphs.Return();
            lineFrames.Return();
            blockVertexIndices.Return();
            blockOuterVertexIndices.Return();
            blockGeometry.Return();
        }

        private void BuildPieces(ReadOnlySpan<RangeVisualFragment> source, GeometryMapping geometry,
            float paddingX, float paddingY, float radius, float merge)
        {
            pieces.FakeClear();
            pieces.EnsureCapacity(source.Length);
            for (var i = 0; i < source.Length; i++)
            {
                var fragment = source[i];
                var corners = geometry == GeometryMapping.Range
                    ? EndpointCorners(in fragment)
                    : RangeDecorationCorners.All;
                var compensatedRadius = corners == RangeDecorationCorners.None ||
                                        geometry == GeometryMapping.Block
                    ? 0f
                    : radius;
                var layoutBounds = Expand(fragment.LayoutBounds, paddingX, paddingY,
                    compensatedRadius);
                var transformKind = geometry == GeometryMapping.Block
                    ? RangeVisualTransformKind.Identity
                    : fragment.TransformKind;
                var bounds = transformKind == RangeVisualTransformKind.Identity
                    ? layoutBounds
                    : ResolveVisualBounds(in fragment, layoutBounds, paddingX, paddingY,
                        compensatedRadius);
                pieces.Add(new Piece
                {
                    fragment = fragment,
                    layoutBounds = layoutBounds,
                    bounds = bounds,
                    corners = corners,
                    transformKind = transformKind,
                    transform = transformKind == RangeVisualTransformKind.Uniform
                        ? fragment.Transform
                        : default,
                });
            }

            if (merge <= 0f || geometry == GeometryMapping.Glyph) return;
            for (var i = 0; i < pieces.count; i++)
                if (pieces[i].transformKind != RangeVisualTransformKind.Identity)
                    pieces[i].transformKind = RangeVisualTransformKind.NonUniform;
            for (var i = 1; i < pieces.count; i++)
            {
                ref var previous = ref pieces[i - 1];
                ref var current = ref pieces[i];
                if (previous.fragment.LineIndex == current.fragment.LineIndex) continue;
                if (Mathf.Abs(previous.bounds.xMin - current.bounds.xMin) < merge)
                {
                    var x = Mathf.Min(previous.bounds.xMin, current.bounds.xMin);
                    previous.bounds.xMin = x;
                    current.bounds.xMin = x;
                }
                if (Mathf.Abs(previous.bounds.xMax - current.bounds.xMax) < merge)
                {
                    var x = Mathf.Max(previous.bounds.xMax, current.bounds.xMax);
                    previous.bounds.xMax = x;
                    current.bounds.xMax = x;
                }
            }
        }

        private static Rect Expand(Rect bounds, float paddingX, float paddingY, float radius)
        {
            bounds.xMin -= paddingX;
            bounds.xMax += paddingX;
            bounds.yMin -= paddingY;
            bounds.yMax += paddingY;

            var halfExtent = Mathf.Max(0f, Mathf.Min(bounds.width, bounds.height) * 0.5f);
            var effectiveRadius = Mathf.Min(radius, halfExtent / (1f - CornerSafeInsetFactor));
            var compensation = effectiveRadius * CornerSafeInsetFactor;
            bounds.xMin -= compensation;
            bounds.xMax += compensation;
            bounds.yMin -= compensation;
            bounds.yMax += compensation;
            return bounds;
        }

        private static Rect ResolveVisualBounds(in RangeVisualFragment fragment, Rect layoutBounds,
            float paddingX, float paddingY, float radius)
        {
            if (fragment.TransformKind == RangeVisualTransformKind.Uniform)
                return fragment.Transform.TransformBounds(layoutBounds);
            return Expand(fragment.Bounds, paddingX, paddingY, radius);
        }

        private void BuildLineFrames()
        {
            lineFrames.FakeClear();
            for (var i = 0; i < pieces.count; i++)
            {
                ref readonly var piece = ref pieces[i];
                if (lineFrames.count == 0 || lineFrames[lineFrames.count - 1].lineIndex != piece.fragment.LineIndex)
                {
                    lineFrames.Add(new RangeBlockBand
                    {
                        lineIndex = piece.fragment.LineIndex,
                        bounds = piece.bounds,
                    });
                    continue;
                }

                ref var line = ref lineFrames[lineFrames.count - 1];
                Encapsulate(ref line.bounds, piece.bounds);
            }
        }

        private float CompensateBlockFrames(float radius)
        {
            if (radius <= 0f) return 0f;

            var halfExtent = Mathf.Max(0f,
                Mathf.Min(lineFrames[0].bounds.width, lineFrames[0].bounds.height) * 0.5f);
            for (var i = 1; i < lineFrames.count; i++)
            {
                var bounds = lineFrames[i].bounds;
                halfExtent = Mathf.Min(halfExtent,
                    Mathf.Max(0f, Mathf.Min(bounds.width, bounds.height) * 0.5f));
            }

            var effectiveRadius = Mathf.Min(radius, halfExtent / (1f - CornerSafeInsetFactor));
            var compensation = effectiveRadius * CornerSafeInsetFactor;
            for (var i = 0; i < lineFrames.count; i++)
            {
                ref var bounds = ref lineFrames[i].bounds;
                bounds.xMin -= compensation;
                bounds.xMax += compensation;
                bounds.yMin -= compensation;
                bounds.yMax += compensation;
            }
            return effectiveRadius;
        }

        private void IncludeBlockVisualEnds(float paddingX, float paddingY, float radius)
        {
            var lineIndex = int.MinValue;
            var lineFrameIndex = -1;
            for (var i = 0; i < pieces.count; i++)
            {
                ref readonly var piece = ref pieces[i];
                var fragment = piece.fragment;
                if (fragment.LineIndex != lineIndex)
                {
                    lineIndex = fragment.LineIndex;
                    lineFrameIndex++;
                }

                var layoutBounds = Expand(fragment.LayoutBounds, paddingX, paddingY, radius);
                var visualBounds = fragment.TransformKind == RangeVisualTransformKind.Identity
                    ? layoutBounds
                    : ResolveVisualBounds(in fragment, layoutBounds, paddingX, paddingY, radius);
                ref var bounds = ref lineFrames[lineFrameIndex].bounds;
                if (fragment.IsRightToLeft)
                    bounds.xMin = Mathf.Min(bounds.xMin, visualBounds.xMin);
                else
                    bounds.xMax = Mathf.Max(bounds.xMax, visualBounds.xMax);
            }
        }

        private void EmitGlyphPaintSections(in Piece piece, in RangeDecorationPaint decorationPaint,
            RangeDecorationOrder order, Color32 tint, float radius, ref RangeMeshWriter writer)
        {
            var first = -1;
            var last = -1;
            for (var i = 0; i < glyphs.count; i++)
            {
                ref readonly var glyph = ref glyphs[i];
                if (glyph.LineIndex != piece.fragment.LineIndex ||
                    glyph.Bounds.xMax <= piece.bounds.xMin || glyph.Bounds.xMin >= piece.bounds.xMax)
                    continue;
                if (first < 0) first = i;
                last = i;
            }

            if (first < 0)
            {
                writer.SetPaint(in decorationPaint, piece.bounds, order, tint);
                AddPieceRoundedRect(in piece, radius, ref writer);
                return;
            }

            for (var i = first; i <= last; i++)
            {
                ref readonly var glyph = ref glyphs[i];
                if (glyph.LineIndex != piece.fragment.LineIndex ||
                    glyph.Bounds.xMax <= piece.bounds.xMin || glyph.Bounds.xMin >= piece.bounds.xMax)
                    continue;

                var previous = PreviousGlyphOnPiece(i, first, in piece);
                var next = NextGlyphOnPiece(i, last, in piece);
                var left = previous < 0
                    ? piece.bounds.xMin
                    : (glyphs[previous].Bounds.xMax + glyph.Bounds.xMin) * 0.5f;
                var right = next < 0
                    ? piece.bounds.xMax
                    : (glyph.Bounds.xMax + glyphs[next].Bounds.xMin) * 0.5f;
                left = Mathf.Clamp(left, piece.bounds.xMin, piece.bounds.xMax);
                right = Mathf.Clamp(right, piece.bounds.xMin, piece.bounds.xMax);
                if (right <= left) continue;

                var section = Rect.MinMaxRect(left, piece.bounds.yMin, right, piece.bounds.yMax);
                writer.SetPaint(in decorationPaint, glyph.Bounds, order, tint);
                writer.AddRoundedRectSection(piece.bounds, section, radius, piece.corners);
            }
        }

        private static void AddPieceRoundedRect(in Piece piece, float radius,
            ref RangeMeshWriter writer)
        {
            if (piece.transformKind == RangeVisualTransformKind.Uniform)
                writer.AddRoundedRect(piece.layoutBounds, in piece.transform, radius, piece.corners);
            else
                writer.AddRoundedRect(piece.bounds, radius, piece.corners);
        }

        private int PreviousGlyphOnPiece(int index, int first, in Piece piece)
        {
            for (var i = index - 1; i >= first; i--)
                if (glyphs[i].LineIndex == piece.fragment.LineIndex &&
                    glyphs[i].Bounds.xMax > piece.bounds.xMin && glyphs[i].Bounds.xMin < piece.bounds.xMax)
                    return i;
            return -1;
        }

        private int NextGlyphOnPiece(int index, int last, in Piece piece)
        {
            for (var i = index + 1; i <= last; i++)
                if (glyphs[i].LineIndex == piece.fragment.LineIndex &&
                    glyphs[i].Bounds.xMax > piece.bounds.xMin && glyphs[i].Bounds.xMin < piece.bounds.xMax)
                    return i;
            return -1;
        }

        private Rect FindLineFrame(int lineIndex)
        {
            for (var i = 0; i < lineFrames.count; i++)
                if (lineFrames[i].lineIndex == lineIndex)
                    return lineFrames[i].bounds;
            throw new InvalidOperationException("A decoration piece has no line paint frame.");
        }

        /// <summary>
        /// Whether two line bands share enough horizontal span to be drawn as one surface.
        /// </summary>
        /// <remarks>
        /// At least <paramref name="seam"/> of real overlap, not merely touching edges: a pair meeting at a point
        /// pinches the contour to zero width there, and the triangulator has no surface to build across it.
        /// </remarks>
        private static bool Overlaps(in Rect a, in Rect b, float seam) =>
            a.xMax - b.xMin >= seam && b.xMax - a.xMin >= seam;

        /// <summary>
        /// Emits one run of bands that genuinely overlap, as a single rounded staircase.
        /// </summary>
        /// <remarks>
        /// Runs are emitted separately rather than bridged into one component. Two wrapped fragments that share no
        /// horizontal span have no corridor between them free of other words — every path from one to the other
        /// crosses glyphs outside the range — so joining them can only be done by covering text the range does not
        /// contain. Two surfaces is what the reader is looking at; one is a claim the geometry cannot honour.
        /// <para>
        /// The paint frame is set once for the whole range before the runs, so a gradient still spans the range
        /// rather than restarting on each surface.
        /// </para>
        /// </remarks>
        private bool EmitBlockRun(Span<RangeBlockBand> bands, float radius, float seam, float fringeWidth,
            ref RangeMeshWriter writer)
        {
            blockGeometry.Build(bands, radius, seam, fringeWidth);
            var contour = blockGeometry.Contour;
            var outerContour = blockGeometry.OuterContour;
            var triangles = blockGeometry.Triangles;
            if (contour.Length < 3 || triangles.Length < 3) return false;

            blockVertexIndices.FakeClear();
            blockOuterVertexIndices.FakeClear();
            blockVertexIndices.EnsureCapacity(contour.Length);
            blockOuterVertexIndices.EnsureCapacity(outerContour.Length);
            for (var i = 0; i < contour.Length; i++)
            {
                blockVertexIndices.Add(writer.AddCoverageVertex(contour[i], 1f));
                blockOuterVertexIndices.Add(writer.AddCoverageVertex(outerContour[i], 0f));
            }

            for (var i = 0; i < triangles.Length; i += 3)
                writer.AddTriangle(blockVertexIndices[triangles[i]], blockVertexIndices[triangles[i + 1]],
                    blockVertexIndices[triangles[i + 2]]);

            for (var i = 0; i < contour.Length; i++)
            {
                var next = (i + 1) % contour.Length;
                writer.AddTriangle(blockVertexIndices[i], blockVertexIndices[next],
                    blockOuterVertexIndices[next]);
                writer.AddTriangle(blockOuterVertexIndices[next], blockOuterVertexIndices[i],
                    blockVertexIndices[i]);
            }

            return true;
        }

        private bool EmitBlock(RangeGeometryIndex geometryIndex, float fontSize,
            in ResolvedHighlightPresentation style, float radius, int rampRow,
            ref RangeMeshWriter writer)
        {
            var fringeWidth = Mathf.Max(0.5f, fontSize / 64f);
            var seam = Mathf.Max(0.5f, fontSize / 24f);

            var decorationPaint = new RangeDecorationPaint(in style.paint, rampRow);
            if (style.paint.mapping == TextPaintMapping.Range || style.paint.mapping == TextPaintMapping.Block)
            {
                var frame = style.paint.mapping == TextPaintMapping.Block
                    ? geometryIndex.GetTextBlockBounds(style.height)
                    : UnionLineFrames();
                writer.SetPaint(in decorationPaint, frame, style.order, style.tint);

                var emitted = false;
                var start = 0;
                for (var end = 1; end <= lineFrames.count; end++)
                {
                    if (end < lineFrames.count &&
                        Overlaps(lineFrames[end - 1].bounds, lineFrames[end].bounds, seam)) continue;

                    emitted |= EmitBlockRun(lineFrames.Span.Slice(start, end - start), radius, seam,
                        fringeWidth, ref writer);
                    start = end;
                }
                return emitted;
            }

            if (style.paint.mapping == TextPaintMapping.Line)
            {
                for (var i = 0; i < lineFrames.count; i++)
                {
                    var bounds = lineFrames[i].bounds;
                    var clip = ExpandedBlockCell(bounds, i, fringeWidth);
                    EmitBlockCell(clip, bounds, in decorationPaint, style.order,
                        style.tint, ref writer);
                }
                return true;
            }

            EmitBlockGlyphCells(in decorationPaint, style.order, style.tint,
                fringeWidth, ref writer);
            return true;
        }

        private void EmitBlockGlyphCells(in RangeDecorationPaint decorationPaint,
            RangeDecorationOrder order, Color32 tint, float fringeWidth,
            ref RangeMeshWriter writer)
        {
            for (var lineIndex = 0; lineIndex < lineFrames.count; lineIndex++)
            {
                ref readonly var line = ref lineFrames[lineIndex];
                var lineClip = ExpandedBlockCell(line.bounds, lineIndex, fringeWidth);
                var first = -1;
                var last = -1;
                for (var i = 0; i < glyphs.count; i++)
                {
                    if (glyphs[i].LineIndex != line.lineIndex) continue;
                    if (first < 0) first = i;
                    last = i;
                }

                if (first < 0)
                {
                    EmitBlockCell(lineClip, line.bounds, in decorationPaint, order,
                        tint, ref writer);
                    continue;
                }

                for (var i = first; i <= last; i++)
                {
                    ref readonly var glyph = ref glyphs[i];
                    if (glyph.LineIndex != line.lineIndex) continue;
                    var previous = PreviousGlyphOnLine(i, first, line.lineIndex);
                    var next = NextGlyphOnLine(i, last, line.lineIndex);
                    var left = previous < 0
                        ? line.bounds.xMin
                        : (glyphs[previous].Bounds.xMax + glyph.Bounds.xMin) * 0.5f;
                    var right = next < 0
                        ? line.bounds.xMax
                        : (glyph.Bounds.xMax + glyphs[next].Bounds.xMin) * 0.5f;
                    left = Mathf.Clamp(left, line.bounds.xMin, line.bounds.xMax);
                    right = Mathf.Clamp(right, line.bounds.xMin, line.bounds.xMax);
                    if (right <= left) continue;
                    var cell = Rect.MinMaxRect(
                        previous < 0 ? left - fringeWidth * 2f : left,
                        lineClip.yMin,
                        next < 0 ? right + fringeWidth * 2f : right,
                        lineClip.yMax);
                    EmitBlockCell(cell, glyph.Bounds, in decorationPaint, order,
                        tint, ref writer);
                }
            }
        }

        private Rect ExpandedBlockCell(Rect bounds, int lineIndex, float fringeWidth)
        {
            bounds.xMin -= fringeWidth * 2f;
            bounds.xMax += fringeWidth * 2f;
            bounds.yMax = lineIndex == 0
                ? bounds.yMax + fringeWidth * 2f
                : SharedBandBoundary(lineFrames[lineIndex - 1].bounds, bounds);
            bounds.yMin = lineIndex == lineFrames.count - 1
                ? bounds.yMin - fringeWidth * 2f
                : SharedBandBoundary(bounds, lineFrames[lineIndex + 1].bounds);
            return bounds;
        }

        private static float SharedBandBoundary(Rect upper, Rect lower)
            => (upper.center.y + lower.center.y) * 0.5f;

        private void EmitBlockCell(Rect clip, Rect paintFrame, in RangeDecorationPaint decorationPaint,
            RangeDecorationOrder order, Color32 tint, ref RangeMeshWriter writer)
        {
            writer.SetPaint(in decorationPaint, paintFrame, order, tint);
            var contour = blockGeometry.Contour;
            var outer = blockGeometry.OuterContour;
            var triangles = blockGeometry.Triangles;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                EmitClippedTriangle(
                    new BlockPaintVertex(contour[triangles[i]], 1f),
                    new BlockPaintVertex(contour[triangles[i + 1]], 1f),
                    new BlockPaintVertex(contour[triangles[i + 2]], 1f),
                    clip, ref writer);
            }

            for (var i = 0; i < contour.Length; i++)
            {
                var next = (i + 1) % contour.Length;
                var inner = new BlockPaintVertex(contour[i], 1f);
                var innerNext = new BlockPaintVertex(contour[next], 1f);
                var outerNext = new BlockPaintVertex(outer[next], 0f);
                var outerCurrent = new BlockPaintVertex(outer[i], 0f);
                EmitClippedTriangle(inner, innerNext, outerNext, clip, ref writer);
                EmitClippedTriangle(outerNext, outerCurrent, inner, clip, ref writer);
            }
        }

        private static void EmitClippedTriangle(BlockPaintVertex a, BlockPaintVertex b,
            BlockPaintVertex c, Rect clip, ref RangeMeshWriter writer)
        {
            if (Mathf.Max(a.position.x, Mathf.Max(b.position.x, c.position.x)) <= clip.xMin ||
                Mathf.Min(a.position.x, Mathf.Min(b.position.x, c.position.x)) >= clip.xMax ||
                Mathf.Max(a.position.y, Mathf.Max(b.position.y, c.position.y)) <= clip.yMin ||
                Mathf.Min(a.position.y, Mathf.Min(b.position.y, c.position.y)) >= clip.yMax)
                return;

            Span<BlockPaintVertex> clipped = stackalloc BlockPaintVertex[8];
            var count = ClipTriangle(a, b, c, clip, clipped);
            if (count < 3) return;
            var first = writer.AddCoverageVertex(clipped[0].position, clipped[0].coverage);
            var previous = writer.AddCoverageVertex(clipped[1].position, clipped[1].coverage);
            for (var vertex = 2; vertex < count; vertex++)
            {
                var current = writer.AddCoverageVertex(clipped[vertex].position, clipped[vertex].coverage);
                writer.AddTriangle(first, previous, current);
                previous = current;
            }
        }

        private static int ClipTriangle(BlockPaintVertex a, BlockPaintVertex b,
            BlockPaintVertex c, Rect clip, Span<BlockPaintVertex> result)
        {
            Span<BlockPaintVertex> first = stackalloc BlockPaintVertex[8];
            Span<BlockPaintVertex> second = stackalloc BlockPaintVertex[8];
            first[0] = a;
            first[1] = b;
            first[2] = c;
            var count = 3;
            var input = first;
            var output = second;
            for (var edge = 0; edge < 4 && count > 0; edge++)
            {
                count = ClipEdge(input[..count], output, clip, edge);
                var swap = input;
                input = output;
                output = swap;
            }
            input[..count].CopyTo(result);
            return count;
        }

        private static int ClipEdge(ReadOnlySpan<BlockPaintVertex> input,
            Span<BlockPaintVertex> output, Rect clip, int edge)
        {
            if (input.Length == 0) return 0;
            var count = 0;
            var previous = input[input.Length - 1];
            var previousInside = IsInside(previous.position, clip, edge);
            for (var i = 0; i < input.Length; i++)
            {
                var current = input[i];
                var currentInside = IsInside(current.position, clip, edge);
                if (currentInside != previousInside)
                    output[count++] = Intersect(previous, current, clip, edge);
                if (currentInside) output[count++] = current;
                previous = current;
                previousInside = currentInside;
            }
            return count;
        }

        private static bool IsInside(Vector2 point, Rect clip, int edge) => edge switch
        {
            0 => point.x >= clip.xMin,
            1 => point.x <= clip.xMax,
            2 => point.y >= clip.yMin,
            _ => point.y <= clip.yMax,
        };

        private static BlockPaintVertex Intersect(BlockPaintVertex a, BlockPaintVertex b,
            Rect clip, int edge)
        {
            if (edge < 2)
            {
                var x = edge == 0 ? clip.xMin : clip.xMax;
                var delta = b.position.x - a.position.x;
                var t = Mathf.Abs(delta) > 1e-6f ? (x - a.position.x) / delta : 0f;
                return new BlockPaintVertex(
                    new Vector2(x, Mathf.LerpUnclamped(a.position.y, b.position.y, t)),
                    Mathf.LerpUnclamped(a.coverage, b.coverage, t));
            }

            var y = edge == 2 ? clip.yMin : clip.yMax;
            var verticalDelta = b.position.y - a.position.y;
            var verticalT = Mathf.Abs(verticalDelta) > 1e-6f
                ? (y - a.position.y) / verticalDelta
                : 0f;
            return new BlockPaintVertex(
                new Vector2(Mathf.LerpUnclamped(a.position.x, b.position.x, verticalT), y),
                Mathf.LerpUnclamped(a.coverage, b.coverage, verticalT));
        }

        private int PreviousGlyphOnLine(int index, int first, int lineIndex)
        {
            for (var i = index - 1; i >= first; i--)
                if (glyphs[i].LineIndex == lineIndex) return i;
            return -1;
        }

        private int NextGlyphOnLine(int index, int last, int lineIndex)
        {
            for (var i = index + 1; i <= last; i++)
                if (glyphs[i].LineIndex == lineIndex) return i;
            return -1;
        }

        private Rect UnionLineFrames()
        {
            var result = lineFrames[0].bounds;
            for (var i = 1; i < lineFrames.count; i++) Encapsulate(ref result, lineFrames[i].bounds);
            return result;
        }

        private Rect UnionPieces()
        {
            var result = pieces[0].bounds;
            for (var i = 1; i < pieces.count; i++) Encapsulate(ref result, pieces[i].bounds);
            return result;
        }

        private static void Encapsulate(ref Rect target, Rect value)
        {
            target.xMin = Mathf.Min(target.xMin, value.xMin);
            target.yMin = Mathf.Min(target.yMin, value.yMin);
            target.xMax = Mathf.Max(target.xMax, value.xMax);
            target.yMax = Mathf.Max(target.yMax, value.yMax);
        }

        private static RangeDecorationCorners EndpointCorners(in RangeVisualFragment fragment)
        {
            var result = RangeDecorationCorners.None;
            if (fragment.ContainsRangeStart)
                result |= fragment.RangeStartOnRight ? RangeDecorationCorners.Right : RangeDecorationCorners.Left;
            if (fragment.ContainsRangeEnd)
                result |= fragment.RangeEndOnRight ? RangeDecorationCorners.Right : RangeDecorationCorners.Left;
            return result;
        }
    }
}
