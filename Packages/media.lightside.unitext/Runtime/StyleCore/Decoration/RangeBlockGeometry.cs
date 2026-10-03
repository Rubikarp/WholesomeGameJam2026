using System;
using UnityEngine;

namespace LightSide
{
    internal struct RangeBlockBand
    {
        public int lineIndex;
        public Rect bounds;
    }

    /// <summary>
    /// Builds and triangulates the rounded, connected staircase contour of one rectangle per visual
    /// line. Overlapping bands retain their padded union; disjoint bands receive a narrow bridge so
    /// the result remains one connected component.
    /// </summary>
    internal sealed class RangeBlockGeometry
    {
        private PooledBuffer<Vector2> rawContour;
        private PooledBuffer<Vector2> contour;
        private PooledBuffer<Vector2> outerContour;
        private PooledBuffer<int> polygon;
        private PooledBuffer<int> triangles;

        public ReadOnlySpan<Vector2> Contour => contour.Span;
        public ReadOnlySpan<Vector2> OuterContour => outerContour.Span;
        public ReadOnlySpan<int> Triangles => triangles.Span;

        public void Build(Span<RangeBlockBand> bands, float radius, float bridgeHalfWidth,
            float fringeWidth)
        {
            rawContour.FakeClear();
            contour.FakeClear();
            outerContour.FakeClear();
            polygon.FakeClear();
            triangles.FakeClear();
            if (bands.Length == 0) return;

            ConnectBands(bands, Mathf.Max(bridgeHalfWidth, 0.01f));
            BuildRawContour(bands);
            SimplifyRawContour();
            BuildRoundedContour(Mathf.Max(0f, radius));
            Triangulate();
            BuildOuterContour(Mathf.Max(0.01f, fringeWidth));
        }

        public void Return()
        {
            rawContour.Return();
            contour.Return();
            outerContour.Return();
            polygon.Return();
            triangles.Return();
        }

        /// <summary>
        /// Closes the seams between consecutive bands so the contour is one connected component.
        /// </summary>
        /// <remarks>
        /// Bands that share no horizontal span are drawn together to the midpoint of the gap and overlapped by
        /// <paramref name="bridgeHalfWidth"/>. That widening is over glyphs outside the range, so the bridge must
        /// stay a hairline: it is sized from the type, never from the corner radius, which an author sets for looks
        /// and cannot relate to a wrap position they do not control.
        /// </remarks>
        private static void ConnectBands(Span<RangeBlockBand> bands, float bridgeHalfWidth)
        {
            for (var i = 1; i < bands.Length; i++)
            {
                ref var previous = ref bands[i - 1];
                ref var current = ref bands[i];
                if (previous.bounds.yMin > current.bounds.yMax)
                {
                    var boundary = (previous.bounds.yMin + current.bounds.yMax) * 0.5f;
                    previous.bounds.yMin = boundary;
                    current.bounds.yMax = boundary;
                }

                if (previous.bounds.xMax < current.bounds.xMin)
                {
                    var center = (previous.bounds.xMax + current.bounds.xMin) * 0.5f;
                    previous.bounds.xMax = center + bridgeHalfWidth;
                    current.bounds.xMin = center - bridgeHalfWidth;
                }
                else if (current.bounds.xMax < previous.bounds.xMin)
                {
                    var center = (current.bounds.xMax + previous.bounds.xMin) * 0.5f;
                    current.bounds.xMax = center + bridgeHalfWidth;
                    previous.bounds.xMin = center - bridgeHalfWidth;
                }
            }
        }

        private void BuildRawContour(ReadOnlySpan<RangeBlockBand> bands)
        {
            ref readonly var first = ref bands[0];
            rawContour.Add(new Vector2(first.bounds.xMin, first.bounds.yMax));
            rawContour.Add(new Vector2(first.bounds.xMax, first.bounds.yMax));

            for (var i = 0; i < bands.Length; i++)
            {
                ref readonly var band = ref bands[i];
                if (i + 1 < bands.Length)
                {
                    ref readonly var next = ref bands[i + 1];
                    var transition = RightTransition(in band, in next);
                    rawContour.Add(new Vector2(band.bounds.xMax, transition));
                    rawContour.Add(new Vector2(next.bounds.xMax, transition));
                }
                else
                {
                    rawContour.Add(new Vector2(band.bounds.xMax, band.bounds.yMin));
                }
            }

            ref readonly var last = ref bands[bands.Length - 1];
            rawContour.Add(new Vector2(last.bounds.xMin, last.bounds.yMin));
            for (var i = bands.Length - 1; i >= 0; i--)
            {
                ref readonly var band = ref bands[i];
                if (i > 0)
                {
                    ref readonly var previous = ref bands[i - 1];
                    var transition = LeftTransition(in previous, in band);
                    rawContour.Add(new Vector2(band.bounds.xMin, transition));
                    rawContour.Add(new Vector2(previous.bounds.xMin, transition));
                }
                else
                {
                    rawContour.Add(new Vector2(band.bounds.xMin, band.bounds.yMax));
                }
            }
        }

        private static float RightTransition(in RangeBlockBand upper, in RangeBlockBand lower)
        {
            if (upper.bounds.xMax > lower.bounds.xMax) return upper.bounds.yMin;
            if (lower.bounds.xMax > upper.bounds.xMax) return lower.bounds.yMax;
            return (upper.bounds.yMin + lower.bounds.yMax) * 0.5f;
        }

        private static float LeftTransition(in RangeBlockBand upper, in RangeBlockBand lower)
        {
            if (upper.bounds.xMin < lower.bounds.xMin) return upper.bounds.yMin;
            if (lower.bounds.xMin < upper.bounds.xMin) return lower.bounds.yMax;
            return (upper.bounds.yMin + lower.bounds.yMax) * 0.5f;
        }

        private void SimplifyRawContour()
        {
            if (rawContour.count > 1 && Approximately(rawContour[0], rawContour[rawContour.count - 1]))
                rawContour.count--;

            var changed = true;
            while (changed && rawContour.count >= 3)
            {
                changed = false;
                for (var i = 0; i < rawContour.count; i++)
                {
                    var previous = rawContour[(i + rawContour.count - 1) % rawContour.count];
                    var current = rawContour[i];
                    var next = rawContour[(i + 1) % rawContour.count];
                    if (Approximately(current, previous) || Approximately(current, next) ||
                        Mathf.Abs(Cross(previous, current, next)) < 1e-5f)
                    {
                        RemoveAt(ref rawContour, i);
                        changed = true;
                        break;
                    }
                }
            }

            if (SignedArea(rawContour.Span) < 0f)
                Reverse(rawContour.Span);
        }

        private void BuildRoundedContour(float radius)
        {
            if (rawContour.count < 3)
                throw new InvalidOperationException("A connected range block requires at least three contour vertices.");

            for (var i = 0; i < rawContour.count; i++)
            {
                var previous = rawContour[(i + rawContour.count - 1) % rawContour.count];
                var current = rawContour[i];
                var next = rawContour[(i + 1) % rawContour.count];
                var incomingVector = current - previous;
                var outgoingVector = next - current;
                var incomingLength = incomingVector.magnitude;
                var outgoingLength = outgoingVector.magnitude;
                var turn = Cross(previous, current, next);
                var cornerRadius = Mathf.Min(radius, Mathf.Min(incomingLength, outgoingLength) * 0.5f);
                if (cornerRadius <= 1e-4f || Mathf.Abs(turn) < 1e-5f)
                {
                    AddContourPoint(current);
                    continue;
                }

                var incoming = incomingVector / incomingLength;
                var outgoing = outgoingVector / outgoingLength;
                var tangentIn = current - incoming * cornerRadius;
                var tangentOut = current + outgoing * cornerRadius;
                var center = current - incoming * cornerRadius + outgoing * cornerRadius;
                var startAngle = Mathf.Atan2(tangentIn.y - center.y, tangentIn.x - center.x);
                var endAngle = Mathf.Atan2(tangentOut.y - center.y, tangentOut.x - center.x);
                if (turn > 0f)
                    while (endAngle <= startAngle) endAngle += Mathf.PI * 2f;
                else
                    while (endAngle >= startAngle) endAngle -= Mathf.PI * 2f;

                var delta = endAngle - startAngle;
                var segments = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(delta) * cornerRadius / 3f), 1, 16);
                AddContourPoint(tangentIn);
                for (var segment = 1; segment <= segments; segment++)
                {
                    var angle = startAngle + delta * segment / segments;
                    AddContourPoint(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * cornerRadius);
                }
            }

            SimplifyRoundedContour();
        }

        private void AddContourPoint(Vector2 point)
        {
            if (contour.count == 0 || !Approximately(contour[contour.count - 1], point))
                contour.Add(point);
        }

        private static bool Approximately(Vector2 a, Vector2 b)
            => (a - b).sqrMagnitude <= 1e-8f;

        private void SimplifyRoundedContour()
        {
            if (contour.count > 1 && Approximately(contour[0], contour[contour.count - 1]))
                contour.count--;

            var changed = true;
            while (changed && contour.count >= 3)
            {
                changed = false;
                for (var i = 0; i < contour.count; i++)
                {
                    var previous = contour[(i + contour.count - 1) % contour.count];
                    var current = contour[i];
                    var next = contour[(i + 1) % contour.count];
                    if (!Approximately(current, previous) && !Approximately(current, next) &&
                        Mathf.Abs(Cross(previous, current, next)) > 1e-6f) continue;
                    RemoveAt(ref contour, i);
                    changed = true;
                    break;
                }
            }
        }

        private void Triangulate()
        {
            if (contour.count < 3)
                throw new InvalidOperationException("A connected range block requires at least three rounded contour vertices.");
            if (SignedArea(contour.Span) < 0f)
                Reverse(contour.Span);

            polygon.EnsureCapacity(contour.count);
            for (var i = 0; i < contour.count; i++) polygon.Add(i);

            var guard = contour.count * contour.count;
            while (polygon.count > 3 && guard-- > 0)
            {
                var clipped = false;
                for (var i = 0; i < polygon.count; i++)
                {
                    var previous = polygon[(i + polygon.count - 1) % polygon.count];
                    var current = polygon[i];
                    var next = polygon[(i + 1) % polygon.count];
                    if (Cross(contour[previous], contour[current], contour[next]) <= 1e-6f) continue;
                    if (ContainsVertex(previous, current, next)) continue;

                    triangles.Add(previous);
                    triangles.Add(current);
                    triangles.Add(next);
                    RemoveAt(ref polygon, i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                    throw new NotSupportedException(
                        "The connected range contour is self-intersecting or numerically degenerate and cannot be triangulated.");
            }

            if (polygon.count == 3)
            {
                triangles.Add(polygon[0]);
                triangles.Add(polygon[1]);
                triangles.Add(polygon[2]);
            }
        }

        private void BuildOuterContour(float width)
        {
            outerContour.EnsureCapacity(contour.count);
            for (var i = 0; i < contour.count; i++)
            {
                var previous = contour[(i + contour.count - 1) % contour.count];
                var current = contour[i];
                var next = contour[(i + 1) % contour.count];
                var incoming = (current - previous).normalized;
                var outgoing = (next - current).normalized;
                var firstNormal = new Vector2(incoming.y, -incoming.x);
                var secondNormal = new Vector2(outgoing.y, -outgoing.x);
                var miter = firstNormal + secondNormal;
                if (miter.sqrMagnitude < 1e-6f) miter = secondNormal;
                else miter.Normalize();
                var denominator = Mathf.Abs(Vector2.Dot(miter, secondNormal));
                var distance = Mathf.Min(width / Mathf.Max(denominator, 0.25f), width * 2f);
                outerContour.Add(current + miter * distance);
            }
        }

        private bool ContainsVertex(int aIndex, int bIndex, int cIndex)
        {
            var a = contour[aIndex];
            var b = contour[bIndex];
            var c = contour[cIndex];
            for (var i = 0; i < polygon.count; i++)
            {
                var index = polygon[i];
                if (index == aIndex || index == bIndex || index == cIndex) continue;
                var point = contour[index];
                if (point == a || point == b || point == c) continue;
                if (PointInTriangle(point, a, b, c)) return true;
            }
            return false;
        }

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            var ab = Cross(a, b, point);
            var bc = Cross(b, c, point);
            var ca = Cross(c, a, point);
            return ab >= -1e-6f && bc >= -1e-6f && ca >= -1e-6f;
        }

        private static float SignedArea(ReadOnlySpan<Vector2> points)
        {
            var area = 0f;
            for (var i = 0; i < points.Length; i++)
            {
                var next = points[(i + 1) % points.Length];
                area += points[i].x * next.y - next.x * points[i].y;
            }
            return area * 0.5f;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c)
            => (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);

        private static void Reverse(Span<Vector2> values)
        {
            for (var left = 0; left < values.Length / 2; left++)
            {
                var right = values.Length - 1 - left;
                var value = values[left];
                values[left] = values[right];
                values[right] = value;
            }
        }

        private static void RemoveAt<T>(ref PooledBuffer<T> buffer, int index)
        {
            var remaining = buffer.count - index - 1;
            if (remaining > 0)
                Array.Copy(buffer.data, index + 1, buffer.data, index, remaining);
            buffer.count--;
        }
    }
}
