using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace LightSide
{
    /// <summary>
    /// Burst-compiled Job for generating single-channel SDF tiles (RHalf format).
    /// Output is a per-task tile-local block (<c>taskPointers[taskIndex]</c>, row stride = tileSize)
    /// that the atlas uploads as a region afterwards — the job never touches texture memory.
    /// The scheduled range is the number of logical scratch slots; each slot owns its matching
    /// scratch slice and pulls glyph tasks atomically until the shared queue is empty.
    /// A task may describe a contour (segments) or a bitmap silhouette (an alpha plane): both seed the
    /// same vector grid and share the propagation and encoding that follow.
    /// Algorithms remain inlined for stable Burst codegen.
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    internal unsafe struct SdfJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<GlyphCurveCache.Segment> segments;
        [ReadOnly] public NativeArray<byte> alpha;
        [ReadOnly] public NativeArray<SdfCore.GlyphTask> tasks;
        [ReadOnly] public NativeArray<long> taskPointers;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> nextTask;

        [NativeDisableParallelForRestriction]
        public NativeArray<float> scratchBuffer;

        public int maxScratchFloatsPerWorker;

        private const float INF = 1e20f;

        private struct MonoSegment
        {
            public float p0x, p0y, p1x, p1y, p2x, p2y;
            public float yMin, yMax;
            public int windingDir;
            public bool isLinear;
        }

        public void Execute(int workerSlot)
        {
            ref int taskCursor = ref UnsafeUtility.AsRef<int>(nextTask.GetUnsafePtr());
            int taskIndex = Interlocked.Increment(ref taskCursor) - 1;
            if ((uint)taskIndex >= (uint)tasks.Length) return;

            float* workerScratch = (float*)scratchBuffer.GetUnsafePtr()
                                   + workerSlot * maxScratchFloatsPerWorker;
            const int StackMonoCapacity = 2048;
            MonoSegment* monoSegs = stackalloc MonoSegment[StackMonoCapacity];

            do
            {
                RasterizeTask(taskIndex, workerScratch, monoSegs, StackMonoCapacity);
                taskIndex = Interlocked.Increment(ref taskCursor) - 1;
            }
            while ((uint)taskIndex < (uint)tasks.Length);
        }

        private void RasterizeTask(int taskIndex, float* workerScratch,
            MonoSegment* monoSegs, int monoCapacity)
        {
            SdfCore.GlyphTask task = tasks[taskIndex];
            int tileSize = task.tileSize;
            int pixelCount = tileSize * tileSize;

            float* vecGrid = workerScratch;
            byte* signGrid = (byte*)(vecGrid + pixelCount * 4);

            ushort* tileBase = (ushort*)taskPointers[taskIndex];

            if (task.alphaWidth > 0)
            {
                RasterizeAlphaTask(in task, tileSize, vecGrid, signGrid, tileBase);
                return;
            }

            if (task.segmentCount == 0)
            {
                ClearTile(tileBase, tileSize);
                return;
            }

            ComputeTileTransform(in task, out float scale, out float offsetX, out float offsetY);
            ComputeBand(in task, tileSize, scale, offsetX, offsetY,
                out int rxMin, out int ryMin, out int rxMax, out int ryMax);
            ResetBand(vecGrid, signGrid, tileSize, rxMin, ryMin, rxMax, ryMax);

            int monoCount = YMonotoneSplit(task.segmentOffset, task.segmentCount,
                monoSegs, monoCapacity);
            ComputeWinding(monoSegs, monoCount, tileSize, scale, offsetX, offsetY,
                signGrid, rxMin, ryMin, rxMax, ryMax);

            bool resolved =
                (segments[task.segmentOffset].rasterFlags & GlyphCurveCache.Segment.FlagResolved) != 0;
            SeedContour(task.segmentOffset, task.segmentCount, tileSize, scale, offsetX, offsetY, vecGrid, signGrid, resolved);

            PropagateVectors(vecGrid, tileSize, rxMin, ryMin, rxMax, ryMax);

            if (resolved)
                RefineToSegments(task.segmentOffset, tileSize, scale, offsetX, offsetY, vecGrid,
                    rxMin, ryMin, rxMax, ryMax);

            float invSpread = task.glyphH / scale;
            EncodeToHalf16(vecGrid, signGrid, invSpread, tileSize, tileBase,
                rxMin, ryMin, rxMax, ryMax);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ComputeTileTransform(in SdfCore.GlyphTask task,
            out float scale, out float offsetX, out float offsetY)
        {
            float padGlyph = task.padNorm;
            float maxDim = math.max(task.aspect, 1f);
            float baseExtent = maxDim + 2f * padGlyph;
            float gutter = baseExtent / task.tileSize;
            float totalExtent = baseExtent + 2f * gutter;
            scale = task.tileSize / totalExtent;
            offsetX = (maxDim - task.aspect) * 0.5f + padGlyph + gutter;
            offsetY = (maxDim - 1f) * 0.5f + padGlyph + gutter;
        }

        /// <summary>The tile rect the field is computed over: the glyph box grown by the pad-tier rim, clamped to the tile.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ComputeBand(in SdfCore.GlyphTask task, int tileSize, float scale,
            float offsetX, float offsetY, out int rxMin, out int ryMin, out int rxMax, out int ryMax)
        {
            int gxMin = (int)math.floor(offsetX * scale);
            int gyMin = (int)math.floor(offsetY * scale);
            int gxMax = (int)math.ceil((offsetX + task.aspect) * scale);
            int gyMax = (int)math.ceil((offsetY + 1f) * scale);
            int band = (int)math.ceil(task.padNorm * scale);
            rxMin = math.max(0, gxMin - band);
            ryMin = math.max(0, gyMin - band);
            rxMax = math.min(tileSize - 1, gxMax + band);
            ryMax = math.min(tileSize - 1, gyMax + band);
        }

        private static void ResetBand(float* vecGrid, byte* signGrid, int tileSize,
            int rxMin, int ryMin, int rxMax, int ryMax)
        {
            for (int y = ryMin; y <= ryMax; y++)
                for (int x = rxMin; x <= rxMax; x++)
                {
                    int idx = (y * tileSize + x) * 4;
                    vecGrid[idx] = INF;
                    vecGrid[idx + 1] = INF;
                    vecGrid[idx + 2] = 0f;
                    vecGrid[idx + 3] = 0f;
                }

            UnsafeUtility.MemClear(signGrid + ryMin * tileSize, (ryMax - ryMin + 1) * tileSize);
        }

        /// <summary>
        /// Bitmap silhouette: the alpha plane is resampled onto the tile band, the 50% iso-line seeds
        /// the vector grid at the sub-texel crossings between neighbouring texels, and the contour
        /// path's propagation and encoding finish the field. Alpha rows are top-down; the glyph box
        /// maps the bitmap to <c>[0, aspect] × [0, 1]</c> with row 0 at the top edge.
        /// </summary>
        private void RasterizeAlphaTask(in SdfCore.GlyphTask task, int tileSize,
            float* vecGrid, byte* signGrid, ushort* tileBase)
        {
            ComputeTileTransform(in task, out float scale, out float offsetX, out float offsetY);
            ComputeBand(in task, tileSize, scale, offsetX, offsetY,
                out int rxMin, out int ryMin, out int rxMax, out int ryMax);
            ResetBand(vecGrid, signGrid, tileSize, rxMin, ryMin, rxMax, ryMax);

            float invScale = 1f / scale;
            float texelsX = task.alphaWidth / task.aspect;
            float texelsY = task.alphaHeight;
            byte* plane = (byte*)alpha.GetUnsafeReadOnlyPtr() + task.alphaOffset;

            for (int y = ryMin; y <= ryMax; y++)
            {
                float gy = (y + 0.5f) * invScale - offsetY;
                float py = gy * texelsY - 0.5f;
                for (int x = rxMin; x <= rxMax; x++)
                {
                    float gx = (x + 0.5f) * invScale - offsetX;
                    float px = gx * texelsX - 0.5f;
                    float a = SampleAlpha(plane, task.alphaWidth, task.alphaHeight, px, py);
                    int pi = y * tileSize + x;
                    vecGrid[pi * 4 + 2] = a;
                    signGrid[pi] = a >= 0.5f ? (byte)1 : (byte)0;
                }
            }

            for (int y = ryMin; y <= ryMax; y++)
            {
                int row = y * tileSize;
                for (int x = rxMin; x <= rxMax; x++)
                {
                    int pi = row + x;
                    float a0 = vecGrid[pi * 4 + 2];
                    if (x < rxMax) SeedCrossing(vecGrid, pi, pi + 1, a0, vecGrid[(pi + 1) * 4 + 2], 1f, 0f);
                    if (y < ryMax) SeedCrossing(vecGrid, pi, pi + tileSize, a0, vecGrid[(pi + tileSize) * 4 + 2], 0f, 1f);
                }
            }

            PropagateVectors(vecGrid, tileSize, rxMin, ryMin, rxMax, ryMax);

            float invSpread = task.glyphH / scale;
            EncodeToHalf16(vecGrid, signGrid, invSpread, tileSize, tileBase,
                rxMin, ryMin, rxMax, ryMax);
        }

        /// <summary>Bilinear alpha at bitmap-pixel coordinates (pixel centres at integers, y up from the bottom row); outside the bitmap reads as transparent.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SampleAlpha(byte* plane, int width, int height, float px, float py)
        {
            int x0 = (int)math.floor(px);
            int y0 = (int)math.floor(py);
            float fx = px - x0;
            float fy = py - y0;
            float a00 = AlphaAt(plane, width, height, x0, y0);
            float a10 = AlphaAt(plane, width, height, x0 + 1, y0);
            float a01 = AlphaAt(plane, width, height, x0, y0 + 1);
            float a11 = AlphaAt(plane, width, height, x0 + 1, y0 + 1);
            return math.lerp(math.lerp(a00, a10, fx), math.lerp(a01, a11, fx), fy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float AlphaAt(byte* plane, int width, int height, int x, int y)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) return 0f;
            return plane[(height - 1 - y) * width + x] * (1f / 255f);
        }

        /// <summary>
        /// Seeds two adjacent texels whose alphas straddle the 50% iso-line with the vector to the
        /// crossing between their centres, keeping the shorter vector where a texel is seeded twice.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SeedCrossing(float* vecGrid, int pi0, int pi1, float a0, float a1, float dx, float dy)
        {
            float d0 = a0 - 0.5f;
            float d1 = a1 - 0.5f;
            if (d0 * d1 > 0f) return;
            float span = a1 - a0;
            if (math.abs(span) < 1e-6f) return;
            float t = math.saturate((0.5f - a0) / span);

            int i0 = pi0 * 4;
            float v0x = dx * t, v0y = dy * t;
            if (v0x * v0x + v0y * v0y < vecGrid[i0] * vecGrid[i0] + vecGrid[i0 + 1] * vecGrid[i0 + 1])
            {
                vecGrid[i0] = v0x;
                vecGrid[i0 + 1] = v0y;
            }

            int i1 = pi1 * 4;
            float v1x = -dx * (1f - t), v1y = -dy * (1f - t);
            if (v1x * v1x + v1y * v1y < vecGrid[i1] * vecGrid[i1] + vecGrid[i1 + 1] * vecGrid[i1 + 1])
            {
                vecGrid[i1] = v1x;
                vecGrid[i1 + 1] = v1y;
            }
        }

        private int YMonotoneSplit(int segOffset, int segCount, MonoSegment* output,
            int capacity)
        {
            int count = 0;
            for (int i = 0; i < segCount; i++)
            {
                GlyphCurveCache.Segment s = segments[segOffset + i];

                float denom = s.p0y - 2f * s.p1y + s.p2y;
                float tSplit = (math.abs(denom) > 1e-10f) ? (s.p0y - s.p1y) / denom : -1f;

                if (tSplit > 1e-6f && tSplit < 1f - 1e-6f)
                {
                    float t = tSplit, mt = 1f - t;
                    float m01x = mt * s.p0x + t * s.p1x;
                    float m01y = mt * s.p0y + t * s.p1y;
                    float m12x = mt * s.p1x + t * s.p2x;
                    float m12y = mt * s.p1y + t * s.p2y;
                    float mx = mt * m01x + t * m12x;
                    float my = mt * m01y + t * m12y;
                    if (count < capacity)
                        AddMonoSegment(output, ref count, s.p0x, s.p0y, m01x, m01y, mx, my);
                    if (count < capacity)
                        AddMonoSegment(output, ref count, mx, my, m12x, m12y, s.p2x, s.p2y);
                }
                else
                {
                    if (count < capacity)
                        AddMonoSegment(output, ref count, s.p0x, s.p0y, s.p1x, s.p1y, s.p2x, s.p2y);
                }
            }
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddMonoSegment(MonoSegment* output, ref int count,
            float p0x, float p0y, float p1x, float p1y, float p2x, float p2y)
        {
            ref var m = ref output[count];
            m.p0x = p0x; m.p0y = p0y; m.p1x = p1x; m.p1y = p1y; m.p2x = p2x; m.p2y = p2y;
            m.yMin = math.min(p0y, p2y); m.yMax = math.max(p0y, p2y);
            m.windingDir = (p2y > p0y) ? 1 : -1;

            float d01x = p1x - p0x, d01y = p1y - p0y;
            float d02x = p2x - p0x, d02y = p2y - p0y;
            m.isLinear = math.abs(d01x * d02y - d01y * d02x) < 1e-5f;
            count++;
        }

        private static void ComputeWinding(MonoSegment* monoSegs, int monoCount,
            int tileSize, float scale, float offsetX, float offsetY, byte* signGrid,
            int rxMin, int ryMin, int rxMax, int ryMax)
        {
            for (int i = 1; i < monoCount; i++)
            {
                var key = monoSegs[i];
                float keyYMin = key.yMin;
                int j = i - 1;
                while (j >= 0 && monoSegs[j].yMin > keyYMin)
                {
                    monoSegs[j + 1] = monoSegs[j];
                    j--;
                }
                monoSegs[j + 1] = key;
            }

            int windingRowLen = rxMax + 1;
            int* windingRow = stackalloc int[windingRowLen];
            int startIdx = 0;

            float yGlyphAtRyMin = (ryMin + 0.5f) / scale - offsetY;
            while (startIdx < monoCount && monoSegs[startIdx].yMax <= yGlyphAtRyMin)
                startIdx++;

            for (int y = ryMin; y <= ryMax; y++)
            {
                UnsafeUtility.MemClear(windingRow, windingRowLen * sizeof(int));
                float yGlyph = (y + 0.5f) / scale - offsetY;

                while (startIdx < monoCount && monoSegs[startIdx].yMax <= yGlyph)
                    startIdx++;

                for (int si = startIdx; si < monoCount; si++)
                {
                    ref var seg = ref monoSegs[si];
                    if (seg.yMin > yGlyph) break;
                    if (yGlyph >= seg.yMax) continue;

                    float xPx;
                    if (seg.isLinear)
                    {
                        float dySeg = seg.p2y - seg.p0y;
                        if (math.abs(dySeg) < 1e-9f) continue;
                        float t = (yGlyph - seg.p0y) / dySeg;
                        xPx = (seg.p0x + t * (seg.p2x - seg.p0x) + offsetX) * scale;
                    }
                    else
                    {
                        float a = seg.p0y - 2f * seg.p1y + seg.p2y;
                        float b = 2f * (seg.p1y - seg.p0y);
                        float c = seg.p0y - yGlyph;
                        int roots = SolveQuadratic(a, b, c, out float t0, out _);
                        if (roots == 0 || t0 < 0f || t0 > 1f) continue;
                        float mt = 1f - t0;
                        xPx = ((mt * mt * seg.p0x + 2f * mt * t0 * seg.p1x + t0 * t0 * seg.p2x) + offsetX) * scale;
                    }

                    int ixWind = (int)(xPx + 0.5f);
                    if (ixWind >= 0 && ixWind <= rxMax) windingRow[ixWind] += seg.windingDir;
                }

                int winding = 0;
                int rowOffset = y * tileSize;
                for (int x = 0; x <= rxMax; x++)
                {
                    winding += windingRow[x];
                    if (x >= rxMin)
                        signGrid[rowOffset + x] = (winding != 0) ? (byte)1 : (byte)0;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SolveQuadratic(float a, float b, float c, out float t0, out float t1)
        {
            t0 = t1 = -1f;
            if (math.abs(a) < 1e-8f)
            {
                if (math.abs(b) < 1e-8f) return 0;
                t0 = -c / b;
                return (t0 >= 0f && t0 <= 1f) ? 1 : 0;
            }
            float disc = b * b - 4f * a * c;
            if (disc < -1e-7f) return 0;
            if (disc < 0f) disc = 0f;
            float sqrtDisc = math.sqrt(disc);

            float q = -0.5f * (b + math.select(-sqrtDisc, sqrtDisc, b >= 0f));
            if (math.abs(q) < 1e-12f)
            {
                t0 = 0f;
                t1 = -b / a;
            }
            else
            {
                t0 = q / a;
                t1 = c / q;
            }
            bool v0 = t0 >= 0f && t0 <= 1f;
            bool v1 = t1 >= 0f && t1 <= 1f;
            if (v0 && v1) return 2;
            if (v0) return 1;
            if (v1) { t0 = t1; return 1; }
            return 0;
        }

        private void SeedContour(int segOffset, int segCount, int tileSize, float scale, float offsetX, float offsetY, float* vecGrid, byte* signGrid, bool resolved)
        {
            float invScale = 1f / scale;

            for (int i = 0; i < segCount; i++)
            {
                GlyphCurveCache.Segment s = segments[segOffset + i];
                SeedQuadratic(i, s.p0x, s.p0y, s.p1x, s.p1y, s.p2x, s.p2y, tileSize, scale, invScale, offsetX, offsetY, vecGrid, signGrid, resolved);
            }
        }

        private void SeedQuadratic(int segIndex, float ax, float ay, float bx, float by, float cx, float cy,
            int tileSize, float scale, float invScale, float offsetX, float offsetY, float* vecGrid, byte* signGrid, bool resolved)
        {
            float mx = 0.25f * ax + 0.5f * bx + 0.25f * cx;
            float my = 0.25f * ay + 0.5f * by + 0.25f * cy;
            float h1x = mx - ax, h1y = my - ay;
            float h2x = cx - mx, h2y = cy - my;
            float pixelLen = (math.sqrt(h1x * h1x + h1y * h1y) + math.sqrt(h2x * h2x + h2y * h2y)) * scale;

            int steps = (int)math.ceil(pixelLen);
            if (steps < 1) steps = 1;
            float dt = 1f / steps;

            for (int j = 0; j <= steps; j++)
            {
                float t = j * dt;
                float mt = 1f - t;

                float gx = mt * mt * ax + 2f * mt * t * bx + t * t * cx;
                float gy = mt * mt * ay + 2f * mt * t * by + t * t * cy;
                float px = (gx + offsetX) * scale;
                float py = (gy + offsetY) * scale;

                if (!resolved && IsInternalSample(px, py, t, mt, ax, ay, bx, by, cx, cy, tileSize, signGrid))
                    continue;

                int ix0 = (int)math.floor(px - 0.5f);
                int iy0 = (int)math.floor(py - 0.5f);

                for (int dy2 = 0; dy2 <= 1; dy2++)
                {
                    int iy = iy0 + dy2;
                    if ((uint)iy >= (uint)tileSize) continue;
                    for (int dx2 = 0; dx2 <= 1; dx2++)
                    {
                        int ix = ix0 + dx2;
                        if ((uint)ix >= (uint)tileSize) continue;

                        int idx = (iy * tileSize + ix) * 4;
                        float curD2 = vecGrid[idx] * vecGrid[idx] + vecGrid[idx + 1] * vecGrid[idx + 1];
                        if (curD2 < 0.01f) continue;

                        float pxG = (ix + 0.5f) * invScale - offsetX;
                        float pyG = (iy + 0.5f) * invScale - offsetY;

                        float tn = NewtonStep(pxG, pyG, ax, ay, bx, by, cx, cy, t);

                        float mtn = 1f - tn;
                        float vxG = mtn * mtn * ax + 2f * mtn * tn * bx + tn * tn * cx - pxG;
                        float vyG = mtn * mtn * ay + 2f * mtn * tn * by + tn * tn * cy - pyG;

                        float vxPx = vxG * scale;
                        float vyPx = vyG * scale;
                        float d2Px = vxPx * vxPx + vyPx * vyPx;

                        if (d2Px < curD2)
                        {
                            vecGrid[idx] = vxPx;
                            vecGrid[idx + 1] = vyPx;
                            vecGrid[idx + 2] = tn;
                            vecGrid[idx + 3] = segIndex;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// True when the sample lies on an overlap-internal portion of a contour: both
        /// perpendicular grid neighbours at ±1 AND ±2 pixels fall inside the shape, so
        /// the point is not part of the actual glyph silhouette. Seeding here would create
        /// false near-edge distances inside the glyph and produce visible "ghost" lines
        /// along the seams where overlapping contours intersect. The double-distance check
        /// guards against false positives at sharp corners where the ±1 sample can land
        /// on a doubly-inside diagonal neighbour even though one side of the boundary
        /// curve is in fact outside the shape — extending to ±2 confirms the interior.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsInternalSample(float px, float py, float t, float mt,
            float ax, float ay, float bx, float by, float cx, float cy,
            int tileSize, byte* signGrid)
        {
            float tdx = 2f * (mt * (bx - ax) + t * (cx - bx));
            float tdy = 2f * (mt * (by - ay) + t * (cy - by));
            float tlen = math.sqrt(tdx * tdx + tdy * tdy);
            if (tlen < 1e-10f) return false;

            float perpX = -tdy / tlen;
            float perpY = tdx / tlen;

            int dx = perpX > 0.4f ? 1 : (perpX < -0.4f ? -1 : 0);
            int dy = perpY > 0.4f ? 1 : (perpY < -0.4f ? -1 : 0);
            if (dx == 0 && dy == 0) return false;

            int sx = (int)math.floor(px);
            int sy = (int)math.floor(py);

            int aX1 = sx + dx, aY1 = sy + dy;
            int bX1 = sx - dx, bY1 = sy - dy;
            if ((uint)aX1 >= (uint)tileSize || (uint)aY1 >= (uint)tileSize ||
                (uint)bX1 >= (uint)tileSize || (uint)bY1 >= (uint)tileSize)
                return false;
            if (signGrid[aY1 * tileSize + aX1] == 0 || signGrid[bY1 * tileSize + bX1] == 0)
                return false;

            int aX2 = sx + 2 * dx, aY2 = sy + 2 * dy;
            int bX2 = sx - 2 * dx, bY2 = sy - 2 * dy;
            if ((uint)aX2 >= (uint)tileSize || (uint)aY2 >= (uint)tileSize ||
                (uint)bX2 >= (uint)tileSize || (uint)bY2 >= (uint)tileSize)
                return true;
            return signGrid[aY2 * tileSize + aX2] != 0
                && signGrid[bY2 * tileSize + bX2] != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float NewtonStep(float px, float py,
            float ax, float ay, float bx, float by, float cx, float cy, float t)
        {
            float mt = 1f - t;
            float dpx = 2f * ((bx - ax) + (ax - 2f * bx + cx) * t);
            float dpy = 2f * ((by - ay) + (ay - 2f * by + cy) * t);
            float ddpx = 2f * (ax - 2f * bx + cx);
            float ddpy = 2f * (ay - 2f * by + cy);
            float btx = mt * mt * ax + 2f * mt * t * bx + t * t * cx;
            float bty = mt * mt * ay + 2f * mt * t * by + t * t * cy;
            float diffx = btx - px, diffy = bty - py;

            float dpSq = dpx * dpx + dpy * dpy;
            if (dpSq < 1e-6f)
            {
                float ddSq = ddpx * ddpx + ddpy * ddpy;
                if (ddSq < 1e-12f) return t;
                float dot = diffx * ddpx + diffy * ddpy;
                if (dot >= 0f) return t;
                float s = math.sqrt(-2f * dot / ddSq);
                float t1 = math.clamp(t + s, 0f, 1f);
                float t2 = math.clamp(t - s, 0f, 1f);
                float m1 = 1f - t1;
                float d1x = m1 * m1 * ax + 2f * m1 * t1 * bx + t1 * t1 * cx - px;
                float d1y = m1 * m1 * ay + 2f * m1 * t1 * by + t1 * t1 * cy - py;
                float m2 = 1f - t2;
                float d2x = m2 * m2 * ax + 2f * m2 * t2 * bx + t2 * t2 * cx - px;
                float d2y = m2 * m2 * ay + 2f * m2 * t2 * by + t2 * t2 * cy - py;
                return (d1x * d1x + d1y * d1y <= d2x * d2x + d2y * d2y) ? t1 : t2;
            }

            float f = diffx * dpx + diffy * dpy;
            float fp = dpSq + diffx * ddpx + diffy * ddpy;
            if (math.abs(fp) < 1e-12f) return t;
            float tn = t - f / fp;
            return tn < 0f ? 0f : (tn > 1f ? 1f : tn);
        }

        /// <summary>
        /// Canonical two-pass 8SSEDT over the band rect: each pass runs its main row scan, then a
        /// single-neighbour counter-scan of the same row. The counter-scans are load-bearing — they
        /// are the only propagation route for offsets whose horizontal component opposes the main
        /// scan and exceeds the vertical one; without them the far field overestimates by up to ~5%
        /// of the distance, scalloping effect isolines along steep edges.
        /// </summary>
        private static void PropagateVectors(float* vecGrid, int size,
            int rxMin, int ryMin, int rxMax, int ryMax)
        {
            for (int y = ryMin; y <= ryMax; y++)
            {
                int rowOffset = y * size * 4;
                int rowUp = (y - 1) * size * 4;

                for (int x = rxMin; x <= rxMax; x++)
                {
                    int idx = rowOffset + x * 4;
                    float curVx = vecGrid[idx];
                    float curVy = vecGrid[idx + 1];
                    float curD2 = curVx * curVx + curVy * curVy;

                    if (x > rxMin) CheckProp(idx - 4, ref curVx, ref curVy, ref curD2, -1f, 0f, vecGrid, idx);
                    if (y > ryMin)
                    {
                        int upX = rowUp + x * 4;
                        CheckProp(upX, ref curVx, ref curVy, ref curD2, 0f, -1f, vecGrid, idx);
                        if (x > rxMin) CheckProp(upX - 4, ref curVx, ref curVy, ref curD2, -1f, -1f, vecGrid, idx);
                        if (x < rxMax) CheckProp(upX + 4, ref curVx, ref curVy, ref curD2, 1f, -1f, vecGrid, idx);
                    }
                    vecGrid[idx] = curVx;
                    vecGrid[idx + 1] = curVy;
                }

                for (int x = rxMax - 1; x >= rxMin; x--)
                {
                    int idx = rowOffset + x * 4;
                    float curVx = vecGrid[idx];
                    float curVy = vecGrid[idx + 1];
                    float curD2 = curVx * curVx + curVy * curVy;
                    CheckProp(idx + 4, ref curVx, ref curVy, ref curD2, 1f, 0f, vecGrid, idx);
                    vecGrid[idx] = curVx;
                    vecGrid[idx + 1] = curVy;
                }
            }

            for (int y = ryMax; y >= ryMin; y--)
            {
                int rowOffset = y * size * 4;
                int rowDown = (y + 1) * size * 4;

                for (int x = rxMax; x >= rxMin; x--)
                {
                    int idx = rowOffset + x * 4;
                    float curVx = vecGrid[idx];
                    float curVy = vecGrid[idx + 1];
                    float curD2 = curVx * curVx + curVy * curVy;

                    if (x < rxMax) CheckProp(idx + 4, ref curVx, ref curVy, ref curD2, 1f, 0f, vecGrid, idx);
                    if (y < ryMax)
                    {
                        int downX = rowDown + x * 4;
                        CheckProp(downX, ref curVx, ref curVy, ref curD2, 0f, 1f, vecGrid, idx);
                        if (x < rxMax) CheckProp(downX + 4, ref curVx, ref curVy, ref curD2, 1f, 1f, vecGrid, idx);
                        if (x > rxMin) CheckProp(downX - 4, ref curVx, ref curVy, ref curD2, -1f, 1f, vecGrid, idx);
                    }
                    vecGrid[idx] = curVx;
                    vecGrid[idx + 1] = curVy;
                }

                for (int x = rxMin + 1; x <= rxMax; x++)
                {
                    int idx = rowOffset + x * 4;
                    float curVx = vecGrid[idx];
                    float curVy = vecGrid[idx + 1];
                    float curD2 = curVx * curVx + curVy * curVy;
                    CheckProp(idx - 4, ref curVx, ref curVy, ref curD2, -1f, 0f, vecGrid, idx);
                    vecGrid[idx] = curVx;
                    vecGrid[idx + 1] = curVy;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CheckProp(int nIdx, ref float curVx, ref float curVy, ref float curD2,
            float dx, float dy, float* vecGrid, int curIdx)
        {
            float nVx = vecGrid[nIdx] + dx;
            float nVy = vecGrid[nIdx + 1] + dy;
            float nD2 = nVx * nVx + nVy * nVy;
            if (nD2 < curD2)
            {
                curD2 = nD2;
                curVx = nVx;
                curVy = nVy;
                vecGrid[curIdx + 2] = vecGrid[nIdx + 2];
                vecGrid[curIdx + 3] = vecGrid[nIdx + 3];
            }
        }

        /// <summary>
        /// Rewrites each texel's propagated vector with the exact vector to the nearest point of its
        /// inherited segment when that is nearer, turning distance-to-propagated-foot into
        /// distance-to-curve. Runs only on resolved silhouettes: on unresolved overlaps the
        /// projection can slide onto a buried stretch of a contour and undercut the seam-suppressed
        /// interior field.
        /// </summary>
        private void RefineToSegments(int segOffset, int tileSize, float scale,
            float offsetX, float offsetY, float* vecGrid,
            int rxMin, int ryMin, int rxMax, int ryMax)
        {
            float invScale = 1f / scale;
            for (int y = ryMin; y <= ryMax; y++)
            {
                float pyG = (y + 0.5f) * invScale - offsetY;
                int rowOffset = y * tileSize;
                for (int x = rxMin; x <= rxMax; x++)
                {
                    int idx = (rowOffset + x) * 4;
                    GlyphCurveCache.Segment s = segments[segOffset + (int)vecGrid[idx + 3]];
                    float pxG = (x + 0.5f) * invScale - offsetX;
                    float tn = NewtonStep(pxG, pyG, s.p0x, s.p0y, s.p1x, s.p1y, s.p2x, s.p2y, vecGrid[idx + 2]);
                    float mtn = 1f - tn;
                    float vxPx = (mtn * mtn * s.p0x + 2f * mtn * tn * s.p1x + tn * tn * s.p2x - pxG) * scale;
                    float vyPx = (mtn * mtn * s.p0y + 2f * mtn * tn * s.p1y + tn * tn * s.p2y - pyG) * scale;
                    float curVx = vecGrid[idx];
                    float curVy = vecGrid[idx + 1];
                    if (vxPx * vxPx + vyPx * vyPx < curVx * curVx + curVy * curVy)
                    {
                        vecGrid[idx] = vxPx;
                        vecGrid[idx + 1] = vyPx;
                    }
                }
            }
        }

        private void EncodeToHalf16(float* vecGrid, byte* signGrid, float invSpread, int tileSize,
            ushort* tileBase,
            int rxMin, int ryMin, int rxMax, int ryMax)
        {
            ushort halfOne = (ushort)math.f32tof16(1f);

            for (int y = 0; y < tileSize; y++)
            {
                ushort* dstRow = tileBase + y * tileSize;

                if (y < ryMin || y > ryMax)
                {
                    for (int x = 0; x < tileSize; x++)
                        dstRow[x] = halfOne;
                    continue;
                }

                for (int x = 0; x < rxMin; x++)
                    dstRow[x] = halfOne;

                int srcRow = y * tileSize;
                for (int x = rxMin; x <= rxMax; x++)
                {
                    int idx = (srcRow + x) * 4;
                    float vx = vecGrid[idx], vy = vecGrid[idx + 1];
                    float dist = math.sqrt(vx * vx + vy * vy);
                    if (dist > 1e5f) dist = 1e5f;

                    float sign = (signGrid[srcRow + x] != 0) ? -1f : 1f;
                    float v = sign * dist * invSpread + 0.5f;
                    float encoded = v < 0f ? 0f : (v > 1f ? 1f : v);
                    dstRow[x] = (ushort)math.f32tof16(encoded);
                }

                for (int x = rxMax + 1; x < tileSize; x++)
                    dstRow[x] = halfOne;
            }
        }

        private void ClearTile(ushort* tileBase, int tileSize)
        {
            ushort halfOne = (ushort)math.f32tof16(1f);
            for (int y = 0; y < tileSize; y++)
            {
                ushort* dstRow = tileBase + y * tileSize;
                for (int x = 0; x < tileSize; x++)
                    dstRow[x] = halfOne;
            }
        }
    }
}
