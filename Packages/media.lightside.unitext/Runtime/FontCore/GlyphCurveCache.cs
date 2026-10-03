using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LightSide
{
    internal unsafe interface IGlyphOutlineSource : IDisposable
    {
        FaceInfo FaceInfo { get; }
        IGlyphOutlineSource Retain();
        IntPtr RentFace(int[] axisTags, int[] coordinates);
        void ReturnFace(IntPtr face);
        int Decompose(IntPtr face, uint glyphIndex,
            float* curves, int* types, int* curveCount, int maxCurves,
            int* contours, int* contourCount, int maxContours,
            out int bearingX, out int bearingY, out int advanceX,
            out int width, out int height);
    }

    /// <summary>
    /// Per-font extraction of glyph outlines as quadratic Bézier segments.
    /// Curves are extracted through the font's outline source, normalized to [0,1] glyph space
    /// (height-based), and stored directly (no flattening) for GPU upload.
    /// Includes a face pool for parallel extraction across threads.
    /// </summary>
    internal sealed unsafe class GlyphCurveCache : IDisposable
    {
        private const int MaxCurvesPerGlyph = 2048;
        private const int MaxContoursPerGlyph = 256;

        /// <summary>
        /// One quadratic Bézier segment: p0 (start), p1 (control), p2 (end).
        /// Degenerate lines have p1 = midpoint(p0, p2).
        /// channelMask: R=1, G=2, B=4. Set by EdgeColoring for MSDF; ignored by SdfJob.
        /// </summary>
        public struct Segment
        {
            public float p0x, p0y, p1x, p1y, p2x, p2y;
            public byte channelMask;
            public byte contourIndex;
            /// <summary>Bit 0: endpoint A (p0) is a corner. Bit 1: endpoint B (p2) is a corner.
            /// Bits 2-4: channels exclusive to this segment at A. Bits 5-7: exclusive at B.</summary>
            public byte cornerFlags;
            /// <summary>Bit 1 (<see cref="FlagResolved"/>): glyph geometry was resolved by <see cref="ContourUnionBurst"/> (no buried
            /// edges remain), so rasterizers skip the per-sample internal-silhouette heuristic. Set on every segment of a resolved glyph.</summary>
            public byte rasterFlags;

            public const byte FlagResolved = 2;
        }

        /// <summary>
        /// Glyph metrics extracted with the outline.
        /// </summary>
        public struct GlyphCurveData
        {
            public float bboxMinX, bboxMinY, bboxMaxX, bboxMaxY;
            public float bearingX, bearingY;
            public float advanceX;
            public int designWidth, designHeight;
            public bool isEmpty;
        }

        private readonly FontSource fontSource;
        private readonly int faceIndex;
        private readonly IGlyphOutlineSource outlineSource;
        private readonly int[] axisTags;
        private readonly ConcurrentBag<IntPtr> availableFaces = new();
        private readonly List<FreeTypeFace> createdFaces = new();
        private readonly object poolLock = new();
        private readonly int maxPoolSize;
        private int activeRents;
        private bool disposed;

        public GlyphCurveCache(IntPtr primaryFace, FontSource fontSource, int faceIndex)
        {
            this.fontSource = fontSource;
            this.faceIndex = faceIndex;
            maxPoolSize = Environment.ProcessorCount;

            availableFaces.Add(primaryFace);
        }

        public GlyphCurveCache(IGlyphOutlineSource outlineSource, int[] axisTags)
        {
            this.outlineSource = outlineSource ?? throw new ArgumentNullException(nameof(outlineSource));
            this.axisTags = axisTags;
            maxPoolSize = Environment.ProcessorCount;
        }

        #region Face Pool

        /// <summary>
        /// Rents a face configured for the requested variation. FreeType faces are pooled;
        /// immutable platform faces may be shared across workers.
        /// </summary>
        public IntPtr RentFace(int[] coordinates)
        {
            lock (poolLock)
            {
                if (disposed) throw new ObjectDisposedException(nameof(GlyphCurveCache));
                activeRents++;
            }

            try
            {
                if (outlineSource != null)
                    return outlineSource.RentFace(axisTags, coordinates);

                if (availableFaces.TryTake(out var face))
                {
                    SetVariationCoordinates(face, coordinates);
                    return face;
                }

                lock (poolLock)
                {
                    if (availableFaces.TryTake(out face))
                    {
                        SetVariationCoordinates(face, coordinates);
                        return face;
                    }

                    if (createdFaces.Count < maxPoolSize - 1)
                    {
                        var owned = FreeTypeFace.TryCreate(fontSource, faceIndex);
                        if (owned != null)
                        {
                            face = owned.Pointer;
                            createdFaces.Add(owned);
                            SetVariationCoordinates(face, coordinates);
                            return face;
                        }
                    }
                }

                SpinWait spin = default;
                while (!availableFaces.TryTake(out face))
                    spin.SpinOnce();
                SetVariationCoordinates(face, coordinates);
                return face;
            }
            catch
            {
                CompleteRent();
                throw;
            }
        }

        /// <summary>
        /// Return a rented face handle to the pool.
        /// </summary>
        public void ReturnFace(IntPtr face)
        {
            try
            {
                if (outlineSource != null)
                    outlineSource.ReturnFace(face);
                else if (face != IntPtr.Zero)
                    availableFaces.Add(face);
            }
            finally { CompleteRent(); }
        }

        private void CompleteRent()
        {
            lock (poolLock)
            {
                activeRents--;
                if (disposed && activeRents == 0) Monitor.PulseAll(poolLock);
            }
        }

        private static void SetVariationCoordinates(IntPtr face, int[] coordinates)
        {
            if (face == IntPtr.Zero || coordinates == null || coordinates.Length == 0) return;
            fixed (int* pointer = coordinates)
                FT.SetVarDesignCoordinates(face, pointer, coordinates.Length);
        }

        #endregion

        #region Thread-safe extraction

        /// <summary>
        /// Thread-safe extraction: uses the provided face and output buffer (no shared state).
        /// Caller must rent face via <see cref="RentFace"/> and provide a per-thread buffer.
        /// </summary>
        public GlyphCurveData ExtractWithFace(IntPtr face, uint glyphIndex, ref PooledBuffer<Segment> output)
        {
            return ExtractCore(face, glyphIndex, ref output);
        }

        #endregion

        internal static long ftTicks;
        internal static long normalizeTicks;
        internal static long edgeColorTicks;
        internal static long unionTicks;

        internal static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        [System.Diagnostics.Conditional("UNITEXT_DEBUG")]
        internal static void ResetTimers()
        {
            Interlocked.Exchange(ref ftTicks, 0);
            Interlocked.Exchange(ref normalizeTicks, 0);
            Interlocked.Exchange(ref edgeColorTicks, 0);
            Interlocked.Exchange(ref unionTicks, 0);
            Interlocked.Exchange(ref ContourUnionBurst.statBailed, 0);
            Interlocked.Exchange(ref ContourUnionBurst.statChanged, 0);
            Interlocked.Exchange(ref ContourUnionBurst.statPromoted, 0);
            Interlocked.Exchange(ref ContourUnionBurst.statBailInput, 0);
            Interlocked.Exchange(ref ContourUnionBurst.statBailBudget, 0);
            Interlocked.Exchange(ref ContourUnionBurst.statBailCaps, 0);
            Interlocked.Exchange(ref ContourUnionBurst.statBailClassify, 0);
            Interlocked.Exchange(ref ContourUnionBurst.statBailAssembly, 0);
            Interlocked.Exchange(ref ContourUnionBurst.statThrew, 0);
        }

        [System.Diagnostics.Conditional("UNITEXT_DEBUG")]
        private static void BeginTicks(ref long t0) { t0 = Stopwatch.GetTimestamp(); }

        [System.Diagnostics.Conditional("UNITEXT_DEBUG")]
        private static void AddTicks(ref long counter, long t0) { Interlocked.Add(ref counter, Stopwatch.GetTimestamp() - t0); }

        private GlyphCurveData ExtractCore(IntPtr face, uint glyphIndex, ref PooledBuffer<Segment> output)
        {
            var rawCurves = stackalloc float[MaxCurvesPerGlyph * 8];
            var rawTypes = stackalloc int[MaxCurvesPerGlyph];
            var rawContours = stackalloc int[MaxContoursPerGlyph];
            int curveCount, contourCount;
            int bearingX, bearingY, advanceX, width, height;
            long t0 = 0;
            BeginTicks(ref t0);
            int err = outlineSource != null
                ? outlineSource.Decompose(face, glyphIndex,
                    rawCurves, rawTypes, &curveCount, MaxCurvesPerGlyph,
                    rawContours, &contourCount, MaxContoursPerGlyph,
                    out bearingX, out bearingY, out advanceX, out width, out height)
                : FT.OutlineDecompose(face, glyphIndex,
                    rawCurves, rawTypes, &curveCount, MaxCurvesPerGlyph,
                    rawContours, &contourCount, MaxContoursPerGlyph,
                    out bearingX, out bearingY, out advanceX, out width, out height);
            AddTicks(ref ftTicks, t0);

            if (err != 0)
            {
                if (outlineSource != null)
                    throw new InvalidOperationException(
                        $"System-font outline extraction failed for glyph {glyphIndex} ({err}).");
                return new GlyphCurveData
                {
                    isEmpty = true,
                    bearingX = bearingX,
                    bearingY = bearingY,
                    advanceX = advanceX,
                    designWidth = width,
                    designHeight = height
                };
            }

            if (curveCount == 0)
            {
                return new GlyphCurveData
                {
                    isEmpty = true,
                    bearingX = bearingX,
                    bearingY = bearingY,
                    advanceX = advanceX,
                    designWidth = width,
                    designHeight = height
                };
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < curveCount; i++)
            {
                float* c = rawCurves + i * 8;
                for (int j = 0; j < 3; j++)
                {
                    float x = c[j * 2];
                    float y = c[j * 2 + 1];
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            float bboxH = maxY - minY;
            if (bboxH < 1e-6f) bboxH = 1f;
            float invScale = 1f / bboxH;

            output.EnsureCapacity(output.count + curveCount);
            int segStart = output.count;

            for (int i = 0; i < curveCount; i++)
            {
                float* c = rawCurves + i * 8;
                var seg = new Segment
                {
                    p0x = (c[0] - minX) * invScale, p0y = (c[1] - minY) * invScale,
                    p1x = (c[2] - minX) * invScale, p1y = (c[3] - minY) * invScale,
                    p2x = (c[4] - minX) * invScale, p2y = (c[5] - minY) * invScale
                };
                output.Add(seg);
            }


            BeginTicks(ref t0);
            bool resolved;
            try
            {
                resolved = ContourUnionBurst.TryResolve(ref output, segStart, ref curveCount, rawContours, ref contourCount);
            }
            catch (Exception e)
            {
                resolved = false;
                Interlocked.Increment(ref ContourUnionBurst.statThrew);
                CatZones.raster.MeowWarnOnce($"union:{glyphIndex}",
                    "[GlyphCurveCache] ContourUnion threw for glyph {0}: {1} — rendering from raw contours",
                    glyphIndex, e.Message);
            }
            AddTicks(ref unionTicks, t0);

            BeginTicks(ref t0);
            curveCount = NormalizeContours(ref output, segStart, curveCount, rawContours, contourCount);
            AddTicks(ref normalizeTicks, t0);

            BeginTicks(ref t0);
            EdgeColoring.ColorAllContours(output.data, segStart, curveCount, rawContours, contourCount);
            AddTicks(ref edgeColorTicks, t0);

            int cStart = 0;
            for (int c = 0; c < contourCount; c++)
            {
                int cEnd = rawContours[c];
                for (int i = cStart; i <= cEnd; i++)
                    output[segStart + i].contourIndex = (byte)c;
                cStart = cEnd + 1;
            }

            if (resolved)
            {
                for (int i = 0; i < curveCount; i++)
                    output[segStart + i].rasterFlags |= Segment.FlagResolved;
            }

            return new GlyphCurveData
            {
                bboxMinX = minX, bboxMinY = minY,
                bboxMaxX = maxX, bboxMaxY = maxY,
                bearingX = bearingX,
                bearingY = bearingY,
                advanceX = advanceX,
                designWidth = width,
                designHeight = height,
                isEmpty = false
            };
        }

        /// <summary>
        /// Port of msdfgen's Shape::normalize(): splits single-edge contours into 3 parts
        /// so EdgeColoring can assign distinct channel masks (instead of WHITE = all identical).
        /// Processes back-to-front to expand in-place without overwriting unprocessed data.
        /// </summary>
        private static int NormalizeContours(ref PooledBuffer<Segment> output, int segStart, int segCount,
            int* rawContours, int contourCount)
        {
            int singleCount = 0;
            int cStart = 0;
            for (int c = 0; c < contourCount; c++)
            {
                int cEnd = rawContours[c];
                if (cEnd == cStart) singleCount++;
                cStart = cEnd + 1;
            }
            if (singleCount == 0) return segCount;

            int extra = singleCount * 2;
            int newSegCount = segCount + extra;
            output.EnsureCapacity(segStart + newSegCount);

            int writePos = newSegCount - 1;
            for (int c = contourCount - 1; c >= 0; c--)
            {
                int cEnd = rawContours[c];
                int cStartSeg = c > 0 ? rawContours[c - 1] + 1 : 0;
                int edgeCount = cEnd - cStartSeg + 1;

                if (edgeCount == 1)
                {
                    Segment seg = output[segStart + cStartSeg];
                    SplitSegmentInThirds(in seg, out var p0, out var p1, out var p2);
                    output[segStart + writePos] = p2;
                    output[segStart + writePos - 1] = p1;
                    output[segStart + writePos - 2] = p0;
                    rawContours[c] = writePos;
                    writePos -= 3;
                }
                else
                {
                    for (int i = edgeCount - 1; i >= 0; i--)
                        output[segStart + writePos - (edgeCount - 1 - i)] = output[segStart + cStartSeg + i];
                    rawContours[c] = writePos;
                    writePos -= edgeCount;
                }
            }

            output.count = segStart + newSegCount;
            return newSegCount;
        }

        /// <summary>
        /// Exact port of msdfgen's splitInThirds for quadratic Bézier: subdivides at t=1/3 and t=2/3
        /// using de Casteljau algorithm, producing 3 sub-segments.
        /// </summary>
        private static void SplitSegmentInThirds(in Segment seg, out Segment part0, out Segment part1, out Segment part2)
        {
            part0 = default;
            part1 = default;
            part2 = default;

            float p0x = seg.p0x, p0y = seg.p0y;
            float p1x = seg.p1x, p1y = seg.p1y;
            float p2x = seg.p2x, p2y = seg.p2y;

            float m01x = Mix(p0x, p1x, 1f / 3f), m01y = Mix(p0y, p1y, 1f / 3f);
            float m12x = Mix(p1x, p2x, 1f / 3f), m12y = Mix(p1y, p2y, 1f / 3f);
            float pt13x = Mix(m01x, m12x, 1f / 3f), pt13y = Mix(m01y, m12y, 1f / 3f);

            float n01x = Mix(p0x, p1x, 2f / 3f), n01y = Mix(p0y, p1y, 2f / 3f);
            float n12x = Mix(p1x, p2x, 2f / 3f), n12y = Mix(p1y, p2y, 2f / 3f);
            float pt23x = Mix(n01x, n12x, 2f / 3f), pt23y = Mix(n01y, n12y, 2f / 3f);

            part0.p0x = p0x; part0.p0y = p0y;
            part0.p1x = m01x; part0.p1y = m01y;
            part0.p2x = pt13x; part0.p2y = pt13y;

            float a59x = Mix(p0x, p1x, 5f / 9f), a59y = Mix(p0y, p1y, 5f / 9f);
            float b49x = Mix(p1x, p2x, 4f / 9f), b49y = Mix(p1y, p2y, 4f / 9f);
            part1.p0x = pt13x; part1.p0y = pt13y;
            part1.p1x = Mix(a59x, b49x, 0.5f); part1.p1y = Mix(a59y, b49y, 0.5f);
            part1.p2x = pt23x; part1.p2y = pt23y;

            part2.p0x = pt23x; part2.p0y = pt23y;
            part2.p1x = n12x; part2.p1y = n12y;
            part2.p2x = p2x; part2.p2y = p2y;
        }

        private static float Mix(float a, float b, float t) => a + (b - a) * t;

        public void Dispose()
        {
            lock (poolLock)
            {
                if (disposed) return;
                disposed = true;
                while (activeRents != 0) Monitor.Wait(poolLock);
                foreach (var owned in createdFaces) owned.Dispose();
                createdFaces.Clear();
            }

            while (availableFaces.TryTake(out _)) { }
        }
    }
}
