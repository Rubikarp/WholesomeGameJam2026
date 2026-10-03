using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Seg = LightSide.GlyphCurveCache.Segment;

namespace LightSide
{
    internal struct SpanPair { public double s0, s1, t0, t1; }

    /// <summary>Outcome + folded debug counters returned across the Burst boundary (no managed access inside Burst).</summary>
    internal struct UnionResult
    {
        public int outcome;
        public int total;
        public int contourCount;
        public int promoted, bailCaps, bailBudget;
    }

    /// <summary>
    /// Unmanaged mirror of <c>ContourUnion.Workspace</c>: all scratch is raw pointers so the whole union
    /// runs inside Burst. Allocated once per worker thread, grown on demand, freed on domain reload / quit.
    /// Sizes reproduce <c>Ensure</c>/<c>EnsurePieces</c> exactly — no bounds checks in Burst, so a wrong
    /// multiplier is silent corruption.
    /// </summary>
    internal unsafe struct UnionWorkspace
    {
        public double* S;
        public double* bb;
        public int* segContour;
        public int* contourFirst;
        public int* contourLast;
        public int* segStartVert;
        public int* segEndVert;
        public int* segCx0;
        public int* segCy0;
        public int* cellStart;
        public int* cellEntries;

        public double* mono;
        public int* monoDir;
        public int* bandStart;
        public int* bandEntries;

        public int* recSeg;
        public double* recT;
        public int* recVert;

        public double* vertX;
        public double* vertY;
        public int* vertParent;
        public int* vertNext;
        public int* hashTable;

        public int* segRecStart;
        public int* segRecCount;
        public int* recOrder;

        public double* pieceCtrl;
        public double* pieceT0;
        public double* pieceT1;
        public int* pieceSeg;
        public int* pieceV0;
        public int* pieceV1;
        public byte* pieceKeep;
        public byte* pieceInLeft;

        public int* twinTable;
        public int* twinNext;
        public int* outHead;
        public int* outNext;
        public byte* used;
        public int* chainPiece;
        public int* chainEnd;
        public SpanPair* isectStack;
        public double* isectFound;

        public int capN, vcap, capHash, capCells, capBands, capTwin, capPieces;
        public int hashMask, epoch, monoCount, recCount, vertCount, junctionVertCount, pieceCount;
        public int promoted, bailCaps, bailBudget;
    }

    [BurstCompile]
    internal static unsafe class ContourUnionBurst
    {
        private const int MaxSegs = 2048;
        private const int MaxRecords = 1024;
        private const int MaxSplitsPerSeg = 32;
        private const int MaxPieces = 4096;
        private const int MaxContours = 256;
        private const int SubdivBudget = 640;
        private const int NewtonIters = 10;
        private const int Bands = 64;
        private const int GridDim = 32;
        private const int GridMinSegs = 128;

        private const double TEnd = 1e-4;
        private const double TDedupe = 1e-4;
        private const double TClip = 2e-3;
        private const double VertexTol = 1e-5;
        private const double ProbeEps = 1e-4;
        private const double FlatTol = 1e-4;
        private const double LineTol = 1e-9;
        private const double RayTol = 1e-9;
        private const double TangentTol = 1e-6;

        private const int OkUnchanged = 0;
        private const int OkChanged = 1;
        private const int BailSilent = 2;
        private const int BailIntersect = 3;
        private const int BailBuild = 4;
        private const int BailClassify = 5;
        private const int BailAssembly = 6;

        internal static int statBailed;
        internal static int statChanged;
        internal static int statPromoted;
        internal static int statBailInput;
        internal static int statBailBudget;
        internal static int statBailCaps;
        internal static int statBailClassify;
        internal static int statBailAssembly;
        internal static int statThrew;

        #region Dispatch (managed)

        [ThreadStatic] private static IntPtr wsPtr;
        private static readonly ConcurrentBag<IntPtr> allWs = new();

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InitializeEditor() => EditorLifecycle.UnmanagedCleaning += FreeAllWorkspaces;
#endif

        private static UnionWorkspace* Acquire()
        {
            if (wsPtr == IntPtr.Zero)
            {
                var p = (UnionWorkspace*)UnsafeUtility.Malloc(sizeof(UnionWorkspace), 16, Allocator.Persistent);
                UnsafeUtility.MemClear(p, sizeof(UnionWorkspace));
                wsPtr = (IntPtr)p;
                allWs.Add(wsPtr);
            }
            return (UnionWorkspace*)wsPtr;
        }

        internal static void FreeAllWorkspaces()
        {
            while (allWs.TryTake(out var ip))
            {
                var w = (UnionWorkspace*)ip;
                FreeAll(w);
                UnsafeUtility.Free(w, Allocator.Persistent);
            }
            wsPtr = IntPtr.Zero;
        }

        /// <summary>Managed entry, signature-identical to <c>ContourUnion.TryResolve</c>. Pins the pooled output, pre-grows to the piece ceiling (the kernel cannot grow a managed buffer), invokes the union kernel, folds stats.</summary>
        public static bool TryResolve(ref PooledBuffer<Seg> output, int segStart,
            ref int segCount, int* rawContours, ref int contourCount)
        {
            int n = segCount;
            if (n <= 0 || n > MaxSegs || contourCount <= 0 || contourCount > MaxContours)
            {
                Bump(ref statBailInput);
                return false;
            }

            int upper = Math.Min(MaxPieces, n + MaxRecords + 8);
            output.EnsureCapacity(segStart + upper);

            var res = default(UnionResult);
            int cc = contourCount;
            fixed (Seg* segBase = output.data)
            {
                ResolveEntry(Acquire(), segBase, segStart, n, output.data.Length, rawContours, cc, &res);
            }

            FoldStats(ref res);

            switch (res.outcome)
            {
                case OkChanged:
                    output.count = segStart + res.total;
                    segCount = res.total;
                    contourCount = res.contourCount;
                    Bump(ref statChanged);
                    return true;
                case OkUnchanged:
                    return true;
                case BailIntersect:
                case BailBuild:
                    Bump(ref statBailed);
                    return false;
                case BailClassify:
                    Bump(ref statBailClassify); Bump(ref statBailed);
                    return false;
                case BailAssembly:
                    Bump(ref statBailAssembly); Bump(ref statBailed);
                    return false;
                default:
                    return false;
            }
        }

        [System.Diagnostics.Conditional("UNITEXT_DEBUG")]
        private static void Bump(ref int c) => Interlocked.Increment(ref c);

        [System.Diagnostics.Conditional("UNITEXT_DEBUG")]
        private static void FoldStats(ref UnionResult r)
        {
            Interlocked.Add(ref statPromoted, r.promoted);
            Interlocked.Add(ref statBailCaps, r.bailCaps);
            Interlocked.Add(ref statBailBudget, r.bailBudget);
        }

        #endregion

        #region Allocation (unmanaged)

        private static void* Re(void* p, long bytes)
        {
            if (p != null) UnsafeUtility.Free(p, Allocator.Persistent);
            return UnsafeUtility.Malloc(bytes, 16, Allocator.Persistent);
        }

        private static int NextPow2(int v) { int p = 64; while (p < v) p <<= 1; return p; }

        private static void EnsureConst(UnionWorkspace* w)
        {
            if (w->recSeg != null) return;
            w->contourFirst = (int*)Re(null, MaxContours * 4L); w->contourLast = (int*)Re(null, MaxContours * 4L);
            w->cellStart = (int*)Re(null, (GridDim * GridDim + 2) * 4L); w->bandStart = (int*)Re(null, (Bands + 2) * 4L);
            w->recSeg = (int*)Re(null, MaxRecords * 4L); w->recT = (double*)Re(null, MaxRecords * 8L);
            w->recVert = (int*)Re(null, MaxRecords * 4L); w->recOrder = (int*)Re(null, MaxRecords * 4L);
            w->chainEnd = (int*)Re(null, (MaxContours + 1) * 4L);
            w->isectStack = (SpanPair*)Re(null, 64L * sizeof(SpanPair)); w->isectFound = (double*)Re(null, 16 * 8L);
        }

        private static void EnsureN(UnionWorkspace* w, int n)
        {
            if (n <= w->capN) return;
            w->S = (double*)Re(w->S, (long)n * 6 * 8); w->bb = (double*)Re(w->bb, (long)n * 4 * 8);
            w->segContour = (int*)Re(w->segContour, (long)n * 4);
            w->segStartVert = (int*)Re(w->segStartVert, (long)n * 4);
            w->segEndVert = (int*)Re(w->segEndVert, (long)n * 4);
            w->segCx0 = (int*)Re(w->segCx0, (long)n * 4); w->segCy0 = (int*)Re(w->segCy0, (long)n * 4);
            w->segRecStart = (int*)Re(w->segRecStart, (long)n * 4); w->segRecCount = (int*)Re(w->segRecCount, (long)n * 4);
            w->mono = (double*)Re(w->mono, (long)n * 16 * 8); w->monoDir = (int*)Re(w->monoDir, (long)n * 2 * 4);
            int vcap = n * 2 + MaxRecords + 16;
            w->vertX = (double*)Re(w->vertX, (long)vcap * 8); w->vertY = (double*)Re(w->vertY, (long)vcap * 8);
            w->vertParent = (int*)Re(w->vertParent, (long)vcap * 4); w->vertNext = (int*)Re(w->vertNext, (long)vcap * 4);
            w->outHead = (int*)Re(w->outHead, (long)vcap * 4);
            w->capN = n; w->vcap = vcap;
            int hcap = NextPow2(vcap * 2);
            if (w->capHash < hcap)
            {
                w->hashTable = (int*)Re(w->hashTable, (long)hcap * 4);
                UnsafeUtility.MemClear(w->hashTable, (long)hcap * 4);
                w->capHash = hcap;
            }
            w->hashMask = w->capHash - 1;
        }

        private static void EnsurePieces(UnionWorkspace* w, int cap)
        {
            if (cap <= w->capPieces) return;
            w->pieceCtrl = (double*)Re(w->pieceCtrl, (long)cap * 2 * 8);
            w->pieceT0 = (double*)Re(w->pieceT0, (long)cap * 8); w->pieceT1 = (double*)Re(w->pieceT1, (long)cap * 8);
            w->pieceSeg = (int*)Re(w->pieceSeg, (long)cap * 4); w->pieceV0 = (int*)Re(w->pieceV0, (long)cap * 4);
            w->pieceV1 = (int*)Re(w->pieceV1, (long)cap * 4); w->twinNext = (int*)Re(w->twinNext, (long)cap * 4);
            w->outNext = (int*)Re(w->outNext, (long)cap * 4); w->chainPiece = (int*)Re(w->chainPiece, (long)cap * 4);
            w->pieceKeep = (byte*)Re(w->pieceKeep, cap); w->pieceInLeft = (byte*)Re(w->pieceInLeft, cap);
            w->used = (byte*)Re(w->used, cap);
            w->capPieces = cap;
        }

        private static void FreeAll(UnionWorkspace* w)
        {
            F(w->S); F(w->bb); F(w->segContour); F(w->contourFirst); F(w->contourLast);
            F(w->segStartVert); F(w->segEndVert); F(w->segCx0); F(w->segCy0); F(w->cellStart); F(w->cellEntries);
            F(w->mono); F(w->monoDir); F(w->bandStart); F(w->bandEntries);
            F(w->recSeg); F(w->recT); F(w->recVert);
            F(w->vertX); F(w->vertY); F(w->vertParent); F(w->vertNext); F(w->hashTable);
            F(w->segRecStart); F(w->segRecCount); F(w->recOrder);
            F(w->pieceCtrl); F(w->pieceT0); F(w->pieceT1); F(w->pieceSeg); F(w->pieceV0); F(w->pieceV1);
            F(w->pieceKeep); F(w->pieceInLeft);
            F(w->twinTable); F(w->twinNext); F(w->outHead); F(w->outNext); F(w->used); F(w->chainPiece); F(w->chainEnd);
            F(w->isectStack); F(w->isectFound);
        }

        private static void F(void* p) { if (p != null) UnsafeUtility.Free(p, Allocator.Persistent); }

        private static void BeginEpoch(UnionWorkspace* w)
        {
            w->epoch++;
            if (w->epoch > 0x7FFF)
            {
                w->epoch = 1;
                UnsafeUtility.MemClear(w->hashTable, (long)w->capHash * 4);
            }
        }

        #endregion

        #region Entry

        [BurstCompile(FloatPrecision.High, FloatMode.Strict, CompileSynchronously = true)]
        public static void ResolveEntry(UnionWorkspace* w, Seg* segBase, int segStart, int n, int capacity,
            int* rawContours, int contourCount, UnionResult* res)
        {
            EnsureConst(w);
            EnsureN(w, n);
            w->promoted = 0; w->bailCaps = 0; w->bailBudget = 0;

            for (int i = 0; i < n; i++)
            {
                Seg* s = segBase + segStart + i;
                int b = i * 6;
                w->S[b] = s->p0x; w->S[b + 1] = s->p0y;
                w->S[b + 2] = s->p1x; w->S[b + 3] = s->p1y;
                w->S[b + 4] = s->p2x; w->S[b + 5] = s->p2y;
            }

            int cStart = 0;
            for (int c = 0; c < contourCount; c++)
            {
                int cEnd = rawContours[c];
                if (cEnd < cStart || cEnd >= n) { res->outcome = BailSilent; return; }
                for (int i = cStart; i <= cEnd; i++) w->segContour[i] = c;
                w->contourFirst[c] = cStart;
                w->contourLast[c] = cEnd;
                cStart = cEnd + 1;
            }
            if (cStart != n) { res->outcome = BailSilent; return; }

            for (int i = 0; i < n; i++)
            {
                int b = i * 6;
                double p0x = w->S[b], p0y = w->S[b + 1], p1x = w->S[b + 2], p1y = w->S[b + 3], p2x = w->S[b + 4], p2y = w->S[b + 5];
                w->bb[i * 4] = Math.Min(p0x, Math.Min(p1x, p2x));
                w->bb[i * 4 + 1] = Math.Min(p0y, Math.Min(p1y, p2y));
                w->bb[i * 4 + 2] = Math.Max(p0x, Math.Max(p1x, p2x));
                w->bb[i * 4 + 3] = Math.Max(p0y, Math.Max(p1y, p2y));
            }

            w->vertCount = 0;
            w->recCount = 0;
            BeginEpoch(w);

            bool anyDup = false;
            if (!FindIntersections(w, n, ref anyDup)) { res->outcome = BailIntersect; goto fold; }
            w->junctionVertCount = w->vertCount;

            if (w->recCount == 0 && w->vertCount == 0 && !anyDup && !AnySameWindingNesting(w, n, contourCount))
            { res->outcome = OkUnchanged; goto fold; }

            EnsurePieces(w, Math.Min(MaxPieces, n + w->recCount + 8));
            BuildMonoPieces(w, n);
            if (!BuildVerticesAndPieces(w, n)) { res->outcome = BailBuild; goto fold; }

            if (!ClassifyPieces(w)) { res->outcome = BailClassify; goto fold; }
            DropCoincidentTwins(w);

            if (w->recCount == 0 && w->pieceCount == n)
            {
                bool allKept = true;
                for (int i = 0; i < w->pieceCount && allKept; i++) allKept = w->pieceKeep[i] != 0;
                if (allKept) { res->outcome = OkUnchanged; goto fold; }
            }

            int chainCount = AssembleChains(w);
            if (chainCount <= 0 || chainCount > MaxContours) { res->outcome = BailAssembly; goto fold; }

            int total = w->chainEnd[chainCount];
            if (total <= 0 || total > MaxPieces || segStart + total > capacity) { res->outcome = BailAssembly; goto fold; }

            for (int k = 0; k < total; k++)
            {
                int p = w->chainPiece[k];
                int v0 = Find(w, w->pieceV0[p]);
                int v1 = Find(w, w->pieceV1[p]);
                Seg* s = segBase + segStart + k;
                s->p0x = (float)w->vertX[v0]; s->p0y = (float)w->vertY[v0];
                s->p1x = (float)w->pieceCtrl[p * 2]; s->p1y = (float)w->pieceCtrl[p * 2 + 1];
                s->p2x = (float)w->vertX[v1]; s->p2y = (float)w->vertY[v1];
                s->channelMask = 0; s->contourIndex = 0; s->cornerFlags = 0; s->rasterFlags = 0;
            }
            for (int c = 0; c < chainCount; c++)
                rawContours[c] = w->chainEnd[c + 1] - 1;

            res->total = total;
            res->contourCount = chainCount;
            res->outcome = OkChanged;

        fold:
            res->promoted = w->promoted;
            res->bailCaps = w->bailCaps;
            res->bailBudget = w->bailBudget;
        }

        private static bool AnySameWindingNesting(UnionWorkspace* w, int n, int contourCount)
        {
            if (contourCount <= 1) return false;
            double* cb = stackalloc double[MaxContours * 4];
            double* area = stackalloc double[MaxContours];
            for (int c = 0; c < contourCount; c++)
            {
                cb[c * 4] = double.MaxValue; cb[c * 4 + 1] = double.MaxValue;
                cb[c * 4 + 2] = double.MinValue; cb[c * 4 + 3] = double.MinValue;
                area[c] = 0;
            }
            for (int i = 0; i < n; i++)
            {
                int c = w->segContour[i];
                cb[c * 4] = Math.Min(cb[c * 4], w->bb[i * 4]);
                cb[c * 4 + 1] = Math.Min(cb[c * 4 + 1], w->bb[i * 4 + 1]);
                cb[c * 4 + 2] = Math.Max(cb[c * 4 + 2], w->bb[i * 4 + 2]);
                cb[c * 4 + 3] = Math.Max(cb[c * 4 + 3], w->bb[i * 4 + 3]);
                int b = i * 6;
                area[c] += w->S[b] * w->S[b + 5] - w->S[b + 4] * w->S[b + 1];
            }
            const double slack = 1e-4;
            for (int a = 0; a < contourCount; a++)
                for (int b = a + 1; b < contourCount; b++)
                {
                    bool aInB = cb[b * 4] <= cb[a * 4] + slack && cb[b * 4 + 2] >= cb[a * 4 + 2] - slack &&
                                cb[b * 4 + 1] <= cb[a * 4 + 1] + slack && cb[b * 4 + 3] >= cb[a * 4 + 3] - slack;
                    bool bInA = cb[a * 4] <= cb[b * 4] + slack && cb[a * 4 + 2] >= cb[b * 4 + 2] - slack &&
                                cb[a * 4 + 1] <= cb[b * 4 + 1] + slack && cb[a * 4 + 3] >= cb[b * 4 + 3] - slack;
                    if ((aInB || bInA) && area[a] * area[b] > 0)
                        return true;
                }
            return false;
        }

        #endregion

        #region Intersections

        private static bool FindIntersections(UnionWorkspace* w, int n, ref bool anyDup)
        {
            if (n < GridMinSegs)
            {
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        if (w->bb[i * 4] > w->bb[j * 4 + 2] + VertexTol ||
                            w->bb[j * 4] > w->bb[i * 4 + 2] + VertexTol ||
                            w->bb[i * 4 + 1] > w->bb[j * 4 + 3] + VertexTol ||
                            w->bb[j * 4 + 1] > w->bb[i * 4 + 3] + VertexTol)
                            continue;
                        if (IsDuplicatePair(w, i, j)) { anyDup = true; continue; }
                        if (!IntersectPair(w, i, j)) return false;
                    }
                return true;
            }

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (w->bb[i * 4] < minX) minX = w->bb[i * 4];
                if (w->bb[i * 4 + 1] < minY) minY = w->bb[i * 4 + 1];
                if (w->bb[i * 4 + 2] > maxX) maxX = w->bb[i * 4 + 2];
                if (w->bb[i * 4 + 3] > maxY) maxY = w->bb[i * 4 + 3];
            }
            double invW = GridDim / (maxX - minX + 1e-9);
            double invH = GridDim / (maxY - minY + 1e-9);

            int total = 0;
            for (int i = 0; i < n; i++)
            {
                CellRange(w, i, minX, minY, invW, invH, out int cx0, out int cy0, out int cx1, out int cy1);
                w->segCx0[i] = cx0;
                w->segCy0[i] = cy0;
                total += (cx1 - cx0 + 1) * (cy1 - cy0 + 1);
            }
            int cellCap = Math.Max(total, 256);
            if (cellCap > w->capCells) { w->cellEntries = (int*)Re(w->cellEntries, (long)cellCap * 4); w->capCells = cellCap; }

            int cells = GridDim * GridDim;
            UnsafeUtility.MemClear(w->cellStart, (long)(cells + 1) * 4);
            for (int i = 0; i < n; i++)
            {
                CellRange(w, i, minX, minY, invW, invH, out int cx0, out int cy0, out int cx1, out int cy1);
                for (int cy = cy0; cy <= cy1; cy++)
                    for (int cx = cx0; cx <= cx1; cx++)
                        w->cellStart[cy * GridDim + cx + 1]++;
            }
            for (int c = 0; c < cells; c++) w->cellStart[c + 1] += w->cellStart[c];
            int* cursor = stackalloc int[GridDim * GridDim];
            for (int c = 0; c < cells; c++) cursor[c] = w->cellStart[c];
            for (int i = 0; i < n; i++)
            {
                CellRange(w, i, minX, minY, invW, invH, out int cx0, out int cy0, out int cx1, out int cy1);
                for (int cy = cy0; cy <= cy1; cy++)
                    for (int cx = cx0; cx <= cx1; cx++)
                        w->cellEntries[cursor[cy * GridDim + cx]++] = i;
            }

            for (int c = 0; c < cells; c++)
            {
                int s = w->cellStart[c], e = w->cellStart[c + 1];
                if (e - s < 2) continue;
                int cellX = c % GridDim, cellY = c / GridDim;

                for (int a = s; a < e; a++)
                {
                    int i = w->cellEntries[a];
                    for (int bb = a + 1; bb < e; bb++)
                    {
                        int j = w->cellEntries[bb];
                        int lo = i < j ? i : j;
                        int hi = i < j ? j : i;
                        if (Math.Max(w->segCx0[lo], w->segCx0[hi]) != cellX ||
                            Math.Max(w->segCy0[lo], w->segCy0[hi]) != cellY)
                            continue;

                        if (w->bb[lo * 4] > w->bb[hi * 4 + 2] + VertexTol ||
                            w->bb[hi * 4] > w->bb[lo * 4 + 2] + VertexTol ||
                            w->bb[lo * 4 + 1] > w->bb[hi * 4 + 3] + VertexTol ||
                            w->bb[hi * 4 + 1] > w->bb[lo * 4 + 3] + VertexTol)
                            continue;

                        if (IsDuplicatePair(w, lo, hi)) { anyDup = true; continue; }
                        if (!IntersectPair(w, lo, hi)) return false;
                    }
                }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CellRange(UnionWorkspace* w, int i, double minX, double minY, double invW, double invH,
            out int cx0, out int cy0, out int cx1, out int cy1)
        {
            cx0 = (int)((w->bb[i * 4] - VertexTol - minX) * invW);
            cy0 = (int)((w->bb[i * 4 + 1] - VertexTol - minY) * invH);
            cx1 = (int)((w->bb[i * 4 + 2] + VertexTol - minX) * invW);
            cy1 = (int)((w->bb[i * 4 + 3] + VertexTol - minY) * invH);
            if (cx0 < 0) cx0 = 0; else if (cx0 > GridDim - 1) cx0 = GridDim - 1;
            if (cy0 < 0) cy0 = 0; else if (cy0 > GridDim - 1) cy0 = GridDim - 1;
            if (cx1 < cx0) cx1 = cx0; else if (cx1 > GridDim - 1) cx1 = GridDim - 1;
            if (cy1 < cy0) cy1 = cy0; else if (cy1 > GridDim - 1) cy1 = GridDim - 1;
        }

        private static bool IsDuplicatePair(UnionWorkspace* w, int i, int j)
        {
            int a = i * 6, b = j * 6;
            const double tol = 1e-6;
            bool fwd = Math.Abs(w->S[a] - w->S[b]) < tol && Math.Abs(w->S[a + 1] - w->S[b + 1]) < tol
                    && Math.Abs(w->S[a + 2] - w->S[b + 2]) < tol && Math.Abs(w->S[a + 3] - w->S[b + 3]) < tol
                    && Math.Abs(w->S[a + 4] - w->S[b + 4]) < tol && Math.Abs(w->S[a + 5] - w->S[b + 5]) < tol;
            if (fwd) return true;
            return Math.Abs(w->S[a] - w->S[b + 4]) < tol && Math.Abs(w->S[a + 1] - w->S[b + 5]) < tol
                && Math.Abs(w->S[a + 2] - w->S[b + 2]) < tol && Math.Abs(w->S[a + 3] - w->S[b + 3]) < tol
                && Math.Abs(w->S[a + 4] - w->S[b]) < tol && Math.Abs(w->S[a + 5] - w->S[b + 1]) < tol;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AreAdjacent(UnionWorkspace* w, int i, int j)
        {
            var c = w->segContour[i];
            if (c != w->segContour[j]) return false;
            if (j == i + 1) return true;
            return i == w->contourFirst[c] && j == w->contourLast[c];
        }

        private static int NextInContour(UnionWorkspace* w, int s)
        {
            int c = w->segContour[s];
            return s == w->contourLast[c] ? w->contourFirst[c] : s + 1;
        }

        private static int PrevInContour(UnionWorkspace* w, int s)
        {
            int c = w->segContour[s];
            return s == w->contourFirst[c] ? w->contourLast[c] : s - 1;
        }

        private static bool ChainNeighborTouches(UnionWorkspace* w, int s, bool atStart, double px, double py)
        {
            int b = s * 6;
            return atStart
                ? NearPt(w->S[b], w->S[b + 1], px, py)
                : NearPt(w->S[b + 4], w->S[b + 5], px, py);
        }

        private static bool TryGetRay(UnionWorkspace* w, int seg, bool atStart, double px, double py,
            out double rx, out double ry)
        {
            int s = seg;
            for (int step = 0; step < 4; step++)
            {
                int b = s * 6;
                double cx = w->S[b + 2] - px, cy = w->S[b + 3] - py;
                if (cx * cx + cy * cy > VertexTol * VertexTol) { rx = cx; ry = cy; return true; }
                double fx = (atStart ? w->S[b + 4] : w->S[b]) - px;
                double fy = (atStart ? w->S[b + 5] : w->S[b + 1]) - py;
                if (fx * fx + fy * fy > VertexTol * VertexTol) { rx = fx; ry = fy; return true; }
                s = atStart ? NextInContour(w, s) : PrevInContour(w, s);
            }
            rx = 0; ry = 0;
            return false;
        }

        private static bool NearParallel(double cross, double lenSqU, double lenSqV)
            => cross * cross <= TangentTol * TangentTol * lenSqU * lenSqV;

        private static bool SameDir(double cross, double dot, double lenSqU, double lenSqV)
            => dot > 0 && NearParallel(cross, lenSqU, lenSqV);

        private static bool IsTransversalContact(UnionWorkspace* w, int i, bool iAtStart, int j, bool jAtStart,
            double px, double py)
        {
            int iIn = iAtStart ? PrevInContour(w, i) : i;
            int iOut = iAtStart ? i : NextInContour(w, i);
            int jIn = jAtStart ? PrevInContour(w, j) : j;
            int jOut = jAtStart ? j : NextInContour(w, j);

            if (!ChainNeighborTouches(w, iAtStart ? iIn : iOut, !iAtStart, px, py) ||
                !ChainNeighborTouches(w, jAtStart ? jIn : jOut, !jAtStart, px, py))
                return true;

            if (!TryGetRay(w, iIn, false, px, py, out double a1x, out double a1y) ||
                !TryGetRay(w, iOut, true, px, py, out double a2x, out double a2y))
                return false;
            if (!TryGetRay(w, jIn, false, px, py, out double b1x, out double b1y) ||
                !TryGetRay(w, jOut, true, px, py, out double b2x, out double b2y))
                return false;

            double la1 = a1x * a1x + a1y * a1y, la2 = a2x * a2x + a2y * a2y;
            double lb1 = b1x * b1x + b1y * b1y, lb2 = b2x * b2x + b2y * b2y;
            double cA = a1x * a2y - a1y * a2x;
            double cB = b1x * b2y - b1y * b2x;
            double c1b1 = a1x * b1y - a1y * b1x, d1b1 = a1x * b1x + a1y * b1y;
            double c1b2 = a1x * b2y - a1y * b2x, d1b2 = a1x * b2x + a1y * b2y;
            double c2b1 = a2x * b1y - a2y * b1x, d2b1 = a2x * b1x + a2y * b1y;
            double c2b2 = a2x * b2y - a2y * b2x, d2b2 = a2x * b2x + a2y * b2y;

            if (SameDir(c1b1, d1b1, la1, lb1) && SameDir(c2b2, d2b2, la2, lb2)) return false;
            if (SameDir(c1b2, d1b2, la1, lb2) && SameDir(c2b1, d2b1, la2, lb1)) return false;

            if (SameDir(cA, a1x * a2x + a1y * a2y, la1, la2) ||
                SameDir(cB, b1x * b2x + b1y * b2y, lb1, lb2))
                return true;
            if (NearParallel(c1b1, la1, lb1) || NearParallel(c1b2, la1, lb2) ||
                NearParallel(c2b1, la2, lb1) || NearParallel(c2b2, la2, lb2))
                return true;

            bool s1 = cA >= 0 ? c1b1 >= 0 && -c2b1 >= 0 : c1b1 >= 0 || -c2b1 >= 0;
            bool s2 = cA >= 0 ? c1b2 >= 0 && -c2b2 >= 0 : c1b2 >= 0 || -c2b2 >= 0;
            return s1 != s2;
        }

        private static bool IntersectPair(UnionWorkspace* w, int i, int j)
        {
            SpanPair* stack = w->isectStack;
            double* found = w->isectFound;
            int foundCount = 0;

            double* S = w->S;
            double* A = S + i * 6;
            double* B = S + j * 6;

            double is0 = 0, is1 = 1, it0 = 0, it1 = 1;
            bool adjacent = AreAdjacent(w, i, j);
            if (NearPt(A[0], A[1], B[0], B[1]))
            {
                is0 = TClip; it0 = TClip;
                if (!adjacent && IsTransversalContact(w, i, true, j, true, A[0], A[1]))
                {
                    w->promoted++;
                    if (AddVertex(w, A[0], A[1]) < 0) { w->bailCaps++; return false; }
                }
            }
            if (NearPt(A[0], A[1], B[4], B[5]))
            {
                is0 = TClip; it1 = 1 - TClip;
                if (!adjacent && IsTransversalContact(w, i, true, j, false, A[0], A[1]))
                {
                    w->promoted++;
                    if (AddVertex(w, A[0], A[1]) < 0) { w->bailCaps++; return false; }
                }
            }
            if (NearPt(A[4], A[5], B[0], B[1]))
            {
                is1 = 1 - TClip; it0 = TClip;
                if (!adjacent && IsTransversalContact(w, i, false, j, true, A[4], A[5]))
                {
                    w->promoted++;
                    if (AddVertex(w, A[4], A[5]) < 0) { w->bailCaps++; return false; }
                }
            }
            if (NearPt(A[4], A[5], B[4], B[5]))
            {
                is1 = 1 - TClip; it1 = 1 - TClip;
                if (!adjacent && IsTransversalContact(w, i, false, j, false, A[4], A[5]))
                {
                    w->promoted++;
                    if (AddVertex(w, A[4], A[5]) < 0) { w->bailCaps++; return false; }
                }
            }
            if (is0 >= is1 || it0 >= it1) return true;

            int top = 0;
            stack[top++] = new SpanPair { s0 = is0, s1 = is1, t0 = it0, t1 = it1 };
            int budget = SubdivBudget;

            while (top > 0)
            {
                if (--budget < 0) { w->bailBudget++; return false; }
                var sp = stack[--top];

                SubBounds(A, sp.s0, sp.s1, out double aMinX, out double aMinY, out double aMaxX, out double aMaxY);
                SubBounds(B, sp.t0, sp.t1, out double bMinX, out double bMinY, out double bMaxX, out double bMaxY);
                if (aMinX > bMaxX + LineTol || bMinX > aMaxX + LineTol ||
                    aMinY > bMaxY + LineTol || bMinY > aMaxY + LineTol)
                    continue;

                double aSize = Math.Max(aMaxX - aMinX, aMaxY - aMinY);
                double bSize = Math.Max(bMaxX - bMinX, bMaxY - bMinY);
                bool aFlat = aSize < FlatTol || SubFlat(A, sp.s0, sp.s1);
                bool bFlat = bSize < FlatTol || SubFlat(B, sp.t0, sp.t1);

                if (aFlat && bFlat)
                {
                    if (ChordIntersect(A, sp.s0, sp.s1, B, sp.t0, sp.t1, out double s, out double t))
                    {
                        if (!RefineAndRecord(w, i, j, A, B, s, t, found, ref foundCount))
                            return false;
                    }
                    continue;
                }

                if (top + 2 > 64) { w->bailBudget++; return false; }
                if (aSize >= bSize)
                {
                    double sm = 0.5 * (sp.s0 + sp.s1);
                    stack[top++] = new SpanPair { s0 = sp.s0, s1 = sm, t0 = sp.t0, t1 = sp.t1 };
                    stack[top++] = new SpanPair { s0 = sm, s1 = sp.s1, t0 = sp.t0, t1 = sp.t1 };
                }
                else
                {
                    double tm = 0.5 * (sp.t0 + sp.t1);
                    stack[top++] = new SpanPair { s0 = sp.s0, s1 = sp.s1, t0 = sp.t0, t1 = tm };
                    stack[top++] = new SpanPair { s0 = sp.s0, s1 = sp.s1, t0 = tm, t1 = sp.t1 };
                }
            }
            return true;
        }

        private static bool RefineAndRecord(UnionWorkspace* w, int i, int j, double* A, double* B,
            double s, double t, double* found, ref int foundCount)
        {
            bool converged = false;
            for (int it = 0; it < NewtonIters; it++)
            {
                Eval(A, s, out double ax, out double ay);
                Eval(B, t, out double bx, out double by);
                double fx = ax - bx, fy = ay - by;
                if (fx * fx + fy * fy < 1e-20) { converged = true; break; }
                Tangent(A, s, out double dax, out double day);
                Tangent(B, t, out double dbx, out double dby);
                double det = dax * -dby - day * -dbx;
                if (Math.Abs(det) < 1e-14) break;
                double ds = (fx * -dby - fy * -dbx) / det;
                double dt = (dax * fy - day * fx) / det;
                s -= ds; t -= dt;
                if (s < -0.05 || s > 1.05 || t < -0.05 || t > 1.05) return true;
                if (Math.Abs(ds) < 1e-14 && Math.Abs(dt) < 1e-14) { converged = true; break; }
            }
            if (!converged)
            {
                Eval(A, s, out double ax, out double ay);
                Eval(B, t, out double bx, out double by);
                double fx = ax - bx, fy = ay - by;
                if (fx * fx + fy * fy > VertexTol * VertexTol) return true;
                Tangent(B, t, out double tbx, out double tby);
                double d = 1e-3;
                Eval(A, Math.Max(0, s - d), out double px0, out double py0);
                Eval(A, Math.Min(1, s + d), out double px1, out double py1);
                double c0 = tbx * (py0 - by) - tby * (px0 - bx);
                double c1 = tbx * (py1 - by) - tby * (px1 - bx);
                if (c0 * c1 >= 0) return true;
            }

            Eval(A, s, out double sx, out double sy);
            if (NearPt(sx, sy, A[0], A[1])) s = 0;
            else if (NearPt(sx, sy, A[4], A[5])) s = 1;
            if (NearPt(sx, sy, B[0], B[1])) t = 0;
            else if (NearPt(sx, sy, B[4], B[5])) t = 1;

            for (int k = 0; k < foundCount; k++)
                if (Math.Abs(found[k * 2] - s) < 1e-5 && Math.Abs(found[k * 2 + 1] - t) < 1e-5)
                    return true;
            if (foundCount >= 8) { w->bailCaps++; return false; }
            found[foundCount * 2] = s;
            found[foundCount * 2 + 1] = t;
            foundCount++;

            bool sInterior = s > TEnd && s < 1 - TEnd;
            bool tInterior = t > TEnd && t < 1 - TEnd;
            if (!sInterior && !tInterior) return true;

            Eval(A, s, out double hx, out double hy);
            int vert = AddVertex(w, hx, hy);
            if (vert < 0) { w->bailCaps++; return false; }

            if (sInterior && !AddRecord(w, i, s, vert)) { w->bailCaps++; return false; }
            if (tInterior && !AddRecord(w, j, t, vert)) { w->bailCaps++; return false; }
            return true;
        }

        private static bool AddRecord(UnionWorkspace* w, int seg, double t, int vert)
        {
            if (w->recCount >= MaxRecords) return false;
            w->recSeg[w->recCount] = seg;
            w->recT[w->recCount] = t;
            w->recVert[w->recCount] = vert;
            w->recCount++;
            return true;
        }

        private static bool ChordIntersect(double* A, double s0, double s1, double* B, double t0, double t1,
            out double s, out double t)
        {
            s = t = 0;
            Eval(A, s0, out double a0x, out double a0y);
            Eval(A, s1, out double a1x, out double a1y);
            Eval(B, t0, out double b0x, out double b0y);
            Eval(B, t1, out double b1x, out double b1y);

            double rx = a1x - a0x, ry = a1y - a0y;
            double qx = b1x - b0x, qy = b1y - b0y;
            double det = rx * qy - ry * qx;
            double dx = b0x - a0x, dy = b0y - a0y;
            if (Math.Abs(det) < 1e-18)
            {
                s = 0.5 * (s0 + s1);
                t = 0.5 * (t0 + t1);
                return true;
            }
            double u = (dx * qy - dy * qx) / det;
            double v = (dx * ry - dy * rx) / det;
            if (u < -0.1 || u > 1.1 || v < -0.1 || v > 1.1) return false;
            s = s0 + (s1 - s0) * Clamp01(u);
            t = t0 + (t1 - t0) * Clamp01(v);
            return true;
        }

        private static void SubBounds(double* Q, double t0, double t1,
            out double minX, out double minY, out double maxX, out double maxY)
        {
            Eval(Q, t0, out double p0x, out double p0y);
            Eval(Q, t1, out double p2x, out double p2y);
            Blossom(Q, t0, t1, out double p1x, out double p1y);
            minX = Math.Min(p0x, Math.Min(p1x, p2x));
            minY = Math.Min(p0y, Math.Min(p1y, p2y));
            maxX = Math.Max(p0x, Math.Max(p1x, p2x));
            maxY = Math.Max(p0y, Math.Max(p1y, p2y));
        }

        private static bool SubFlat(double* Q, double t0, double t1)
        {
            Eval(Q, t0, out double p0x, out double p0y);
            Eval(Q, t1, out double p2x, out double p2y);
            Blossom(Q, t0, t1, out double p1x, out double p1y);
            double dx = p2x - p0x, dy = p2y - p0y;
            double cross = (p1x - p0x) * dy - (p1y - p0y) * dx;
            double len2 = dx * dx + dy * dy;
            return cross * cross <= FlatTol * FlatTol * Math.Max(len2, 1e-30);
        }

        #endregion

        #region Vertices

        private static int AddVertex(UnionWorkspace* w, double x, double y)
        {
            const double cell = VertexTol * 4;
            int cx = (int)Math.Floor(x / cell);
            int cy = (int)Math.Floor(y / cell);
            int epoch = w->epoch;

            for (int ny = cy - 1; ny <= cy + 1; ny++)
                for (int nx = cx - 1; nx <= cx + 1; nx++)
                {
                    int bucket = Hash(nx, ny) & w->hashMask;
                    int cellValue = w->hashTable[bucket];
                    if (cellValue >> 16 != epoch) continue;
                    for (int v = (cellValue & 0xFFFF) - 1; v >= 0; v = w->vertNext[v])
                    {
                        double ddx = w->vertX[v] - x, ddy = w->vertY[v] - y;
                        if (ddx * ddx + ddy * ddy <= VertexTol * VertexTol)
                            return v;
                    }
                }

            int idx = w->vertCount;
            if (idx >= w->vcap) return -1;
            w->vertX[idx] = x; w->vertY[idx] = y;
            w->vertParent[idx] = idx;
            int homeBucket = Hash(cx, cy) & w->hashMask;
            int homeCell = w->hashTable[homeBucket];
            w->vertNext[idx] = homeCell >> 16 == epoch ? (homeCell & 0xFFFF) - 1 : -1;
            w->hashTable[homeBucket] = (epoch << 16) | (idx + 1);
            w->vertCount = idx + 1;
            return idx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Hash(int x, int y)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
            return (int)(h & 0x7FFFFFFF);
        }

        private static int Find(UnionWorkspace* w, int v)
        {
            while (w->vertParent[v] != v)
            {
                w->vertParent[v] = w->vertParent[w->vertParent[v]];
                v = w->vertParent[v];
            }
            return v;
        }

        private static void Union(UnionWorkspace* w, int a, int b)
        {
            a = Find(w, a); b = Find(w, b);
            if (a != b) w->vertParent[b] = a;
        }

        #endregion

        #region Pieces

        private static bool BuildVerticesAndPieces(UnionWorkspace* w, int n)
        {
            double* S = w->S;
            for (int i = 0; i < n; i++)
            {
                double* Q = S + i * 6;
                int v0 = AddVertex(w, Q[0], Q[1]);
                int v1 = AddVertex(w, Q[4], Q[5]);
                if (v0 < 0 || v1 < 0) return false;
                w->segStartVert[i] = v0;
                w->segEndVert[i] = v1;
            }

            for (int i = 0; i < n; i++) { w->segRecStart[i] = 0; w->segRecCount[i] = 0; }
            for (int r = 0; r < w->recCount; r++) w->segRecCount[w->recSeg[r]]++;
            int acc = 0;
            for (int i = 0; i < n; i++) { w->segRecStart[i] = acc; acc += w->segRecCount[i]; w->segRecCount[i] = 0; }
            for (int r = 0; r < w->recCount; r++)
            {
                int seg = w->recSeg[r];
                w->recOrder[w->segRecStart[seg] + w->segRecCount[seg]++] = r;
            }

            w->pieceCount = 0;
            for (int i = 0; i < n; i++)
            {
                int rs = w->segRecStart[i], rc = w->segRecCount[i];
                if (rc > MaxSplitsPerSeg) { w->bailCaps++; return false; }

                for (int a = rs + 1; a < rs + rc; a++)
                {
                    int ra = w->recOrder[a];
                    int b = a - 1;
                    while (b >= rs && w->recT[w->recOrder[b]] > w->recT[ra])
                    {
                        w->recOrder[b + 1] = w->recOrder[b];
                        b--;
                    }
                    w->recOrder[b + 1] = ra;
                }

                int outCount = 0;
                for (int k = rs; k < rs + rc; k++)
                {
                    int r = w->recOrder[k];
                    if (outCount > 0)
                    {
                        int prev = w->recOrder[rs + outCount - 1];
                        if (w->recT[r] - w->recT[prev] < TDedupe)
                        {
                            Union(w, w->recVert[prev], w->recVert[r]);
                            continue;
                        }
                    }
                    w->recOrder[rs + outCount++] = r;
                }

                double* Q = S + i * 6;
                double prevT = 0;
                int prevVert = w->segStartVert[i];
                for (int k = 0; k <= outCount; k++)
                {
                    double t1;
                    int endVert;
                    if (k < outCount)
                    {
                        int r = w->recOrder[rs + k];
                        t1 = w->recT[r];
                        endVert = w->recVert[r];
                    }
                    else
                    {
                        t1 = 1;
                        endVert = w->segEndVert[i];
                    }

                    if (w->pieceCount >= MaxPieces || w->pieceCount >= w->capPieces) { w->bailCaps++; return false; }
                    int p = w->pieceCount++;
                    Blossom(Q, prevT, t1, out double c1x, out double c1y);
                    w->pieceCtrl[p * 2] = c1x;
                    w->pieceCtrl[p * 2 + 1] = c1y;
                    w->pieceT0[p] = prevT;
                    w->pieceT1[p] = t1;
                    w->pieceSeg[p] = i;
                    w->pieceV0[p] = prevVert;
                    w->pieceV1[p] = endVert;

                    prevT = t1;
                    prevVert = endVert;
                }
            }
            return true;
        }

        #endregion

        #region Classification

        private static void BuildMonoPieces(UnionWorkspace* w, int n)
        {
            w->monoCount = 0;
            double* S = w->S;
            for (int i = 0; i < n; i++)
            {
                double* Q = S + i * 6;
                double denom = Q[1] - 2 * Q[3] + Q[5];
                double tSplit = Math.Abs(denom) > 1e-14 ? (Q[1] - Q[3]) / denom : -1;
                if (tSplit > 1e-6 && tSplit < 1 - 1e-6)
                {
                    AddMono(w, Q, 0, tSplit);
                    AddMono(w, Q, tSplit, 1);
                }
                else
                {
                    AddMono(w, Q, 0, 1);
                }
            }

            for (int b = 0; b <= Bands + 1; b++) w->bandStart[b] = 0;
            int mc = w->monoCount;
            int total = 0;
            for (int m = 0; m < mc; m++)
            {
                if (w->monoDir[m] == 0) continue;
                BandRange(w, m, out int b0, out int b1);
                total += b1 - b0 + 1;
            }
            int bandCap = Math.Max(total, 256);
            if (bandCap > w->capBands) { w->bandEntries = (int*)Re(w->bandEntries, (long)bandCap * 4); w->capBands = bandCap; }

            for (int m = 0; m < mc; m++)
            {
                if (w->monoDir[m] == 0) continue;
                BandRange(w, m, out int b0, out int b1);
                for (int b = b0; b <= b1; b++) w->bandStart[b + 1]++;
            }
            for (int b = 0; b <= Bands; b++) w->bandStart[b + 1] += w->bandStart[b];
            int* cursor = stackalloc int[Bands];
            for (int b = 0; b < Bands; b++) cursor[b] = w->bandStart[b];
            for (int m = 0; m < mc; m++)
            {
                if (w->monoDir[m] == 0) continue;
                BandRange(w, m, out int b0, out int b1);
                for (int b = b0; b <= b1; b++) w->bandEntries[cursor[b]++] = m;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BandRange(UnionWorkspace* w, int m, out int b0, out int b1)
        {
            int bIdx = m * 8;
            double y0 = w->mono[bIdx + 1], y2 = w->mono[bIdx + 5];
            double yMin = Math.Min(y0, y2), yMax = Math.Max(y0, y2);
            b0 = (int)(yMin * Bands); if (b0 < 0) b0 = 0; else if (b0 > Bands - 1) b0 = Bands - 1;
            b1 = (int)(yMax * Bands); if (b1 < 0) b1 = 0; else if (b1 > Bands - 1) b1 = Bands - 1;
        }

        private static void AddMono(UnionWorkspace* w, double* Q, double t0, double t1)
        {
            int m = w->monoCount++;
            Eval(Q, t0, out double p0x, out double p0y);
            Eval(Q, t1, out double p2x, out double p2y);
            Blossom(Q, t0, t1, out double p1x, out double p1y);
            int b = m * 8;
            w->mono[b] = p0x; w->mono[b + 1] = p0y;
            w->mono[b + 2] = p1x; w->mono[b + 3] = p1y;
            w->mono[b + 4] = p2x; w->mono[b + 5] = p2y;
            w->mono[b + 6] = Math.Max(p0x, Math.Max(p1x, p2x));
            w->mono[b + 7] = Math.Min(p0x, Math.Min(p1x, p2x));
            w->monoDir[m] = p2y > p0y ? 1 : (p2y < p0y ? -1 : 0);
        }

        private static int Winding(UnionWorkspace* w, double px, double py, out bool reliable)
        {
            reliable = true;
            int wind = 0;
            int band = (int)(py * Bands);
            if (band < 0) band = 0; else if (band > Bands - 1) band = Bands - 1;
            int e0 = w->bandStart[band], e1 = w->bandStart[band + 1];

            for (int e = e0; e < e1; e++)
            {
                int m = w->bandEntries[e];
                int dir = w->monoDir[m];
                int b = m * 8;
                double y0 = w->mono[b + 1], y2 = w->mono[b + 5];
                double yMin = dir > 0 ? y0 : y2;
                double yMax = dir > 0 ? y2 : y0;
                if (py < yMin || py >= yMax) continue;
                if (w->mono[b + 6] <= px) continue;
                if (w->mono[b + 7] > px) { wind += dir; continue; }
                wind += SolveCrossing(w, m, px, py, ref reliable);
            }
            return wind;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SolveCrossing(UnionWorkspace* w, int m, double px, double py, ref bool reliable)
        {
            int b = m * 8;
            double y0 = w->mono[b + 1], p1y = w->mono[b + 3], y2 = w->mono[b + 5];
            double a = y0 - 2 * p1y + y2;
            double t;
            if (Math.Abs(a) < 1e-14)
            {
                double dy = y2 - y0;
                t = (py - y0) / dy;
            }
            else
            {
                double bq = 2 * (p1y - y0);
                double cq = y0 - py;
                double disc = bq * bq - 4 * a * cq;
                if (disc < 0) disc = 0;
                double sq = Math.Sqrt(disc);
                double q = -0.5 * (bq + (bq >= 0 ? sq : -sq));
                double r0 = q / a;
                double r1 = Math.Abs(q) > 1e-300 ? cq / q : -1;
                t = r0 >= -1e-9 && r0 <= 1 + 1e-9 ? r0 : r1;
                if (t < -1e-9 || t > 1 + 1e-9) return 0;
                t = Clamp01(t);
            }

            double mt = 1 - t;
            double xHit = mt * mt * w->mono[b] + 2 * mt * t * w->mono[b + 2] + t * t * w->mono[b + 4];
            if (Math.Abs(xHit - px) < RayTol) reliable = false;
            return xHit > px ? w->monoDir[m] : 0;
        }

        private static bool ClassifyPieces(UnionWorkspace* w)
        {
            for (int p = 0; p < w->pieceCount; p++)
            {
                w->pieceKeep[p] = 1;
                w->pieceInLeft[p] = 0;
            }

            double* S = w->S;
            int contourStart = 0;
            while (contourStart < w->pieceCount)
            {
                int seg0 = w->pieceSeg[contourStart];
                int contour = w->segContour[seg0];
                int contourEnd = contourStart;
                while (contourEnd + 1 < w->pieceCount && w->segContour[w->pieceSeg[contourEnd + 1]] == contour)
                    contourEnd++;

                if (!ClassifyContourRuns(w, S, contourStart, contourEnd))
                    return false;
                contourStart = contourEnd + 1;
            }
            return true;
        }

        private static bool ClassifyContourRuns(UnionWorkspace* w, double* S, int pStart, int pEnd)
        {
            int count = pEnd - pStart + 1;
            int firstJunction = -1;
            for (int p = pStart; p <= pEnd; p++)
            {
                if (Find(w, w->pieceV0[p]) < w->junctionVertCount) { firstJunction = p; break; }
            }

            if (firstJunction < 0)
                return ClassifyRun(w, S, pStart, pEnd, pStart, count);

            int runStart = firstJunction;
            int walked = 0;
            while (walked < count)
            {
                int runLen = 1;
                while (runLen < count)
                {
                    int next = pStart + (runStart - pStart + runLen) % count;
                    if (Find(w, w->pieceV0[next]) < w->junctionVertCount) break;
                    runLen++;
                }
                if (!ClassifyRun(w, S, pStart, pEnd, runStart, runLen))
                    return false;
                walked += runLen;
                runStart = pStart + (runStart - pStart + runLen) % count;
            }
            return true;
        }

        private static bool ClassifyRun(UnionWorkspace* w, double* S, int pStart, int pEnd, int runStart, int runLen)
        {
            int count = pEnd - pStart + 1;
            bool done = false, anyProbeable = false;
            bool keep = false, inLeft = false;

            for (int k = 0; k < runLen && !done; k++)
            {
                int p = pStart + (runStart - pStart + k) % count;
                double* Q = S + w->pieceSeg[p] * 6;
                for (int fi = 0; fi < 3 && !done; fi++)
                {
                    double frac = fi == 0 ? 0.5 : (fi == 1 ? 0.35 : 0.65);
                    double tm = w->pieceT0[p] + (w->pieceT1[p] - w->pieceT0[p]) * frac;
                    Eval(Q, tm, out double mx, out double my);
                    Tangent(Q, tm, out double tx, out double ty);
                    double len = Math.Sqrt(tx * tx + ty * ty);
                    if (len < 1e-12) continue;
                    anyProbeable = true;
                    double nx = -ty / len, ny = tx / len;

                    int wl = Winding(w, mx + nx * ProbeEps, my + ny * ProbeEps, out bool relL);
                    if (!relL) continue;
                    int wr = Winding(w, mx - nx * ProbeEps, my - ny * ProbeEps, out bool relR);
                    if (!relR) continue;

                    keep = (wl == 0) != (wr == 0);
                    inLeft = wl != 0;
                    done = true;
                }
            }
            if (!done)
            {
                if (anyProbeable) return false;
                for (int k = 0; k < runLen; k++)
                {
                    int p = pStart + (runStart - pStart + k) % count;
                    w->pieceKeep[p] = 0;
                    w->pieceInLeft[p] = 0;
                }
                return true;
            }

            for (int k = 0; k < runLen; k++)
            {
                int p = pStart + (runStart - pStart + k) % count;
                int v0 = Find(w, w->pieceV0[p]);
                int v1 = Find(w, w->pieceV1[p]);
                if (v0 == v1)
                {
                    double t0 = w->pieceT0[p], t1 = w->pieceT1[p];
                    double* Qd = S + w->pieceSeg[p] * 6;
                    Eval(Qd, t0, out double e0x, out double e0y);
                    Eval(Qd, 0.5 * (t0 + t1), out double emx, out double emy);
                    double ex = emx - e0x, ey = emy - e0y;
                    if (ex * ex + ey * ey < 9 * VertexTol * VertexTol)
                    {
                        w->pieceKeep[p] = 0;
                        w->pieceInLeft[p] = 0;
                        continue;
                    }
                }
                w->pieceKeep[p] = (byte)(keep ? 1 : 0);
                w->pieceInLeft[p] = (byte)(inLeft ? 1 : 0);
            }
            return true;
        }

        private static void DropCoincidentTwins(UnionWorkspace* w)
        {
            int count = w->pieceCount;
            int size = 64;
            while (size < count * 2) size <<= 1;
            if (size > w->capTwin) { w->twinTable = (int*)Re(w->twinTable, (long)size * 4); w->capTwin = size; }
            UnsafeUtility.MemClear(w->twinTable, (long)size * 4);
            int mask = size - 1;

            for (int p = 0; p < count; p++)
            {
                if (w->pieceKeep[p] == 0) continue;
                int v0 = Find(w, w->pieceV0[p]);
                int v1 = Find(w, w->pieceV1[p]);
                int h = (v0 * 73856093 ^ v1 * 19349663) & mask;

                bool dropped = false;
                for (int q = w->twinTable[h] - 1; q >= 0; q = w->twinNext[q])
                {
                    if (Find(w, w->pieceV0[q]) != v0 || Find(w, w->pieceV1[q]) != v1) continue;
                    double dx = w->pieceCtrl[p * 2] - w->pieceCtrl[q * 2];
                    double dy = w->pieceCtrl[p * 2 + 1] - w->pieceCtrl[q * 2 + 1];
                    if (dx * dx + dy * dy < 16 * VertexTol * VertexTol)
                    {
                        w->pieceKeep[p] = 0;
                        dropped = true;
                        break;
                    }
                }
                if (!dropped)
                {
                    w->twinNext[p] = w->twinTable[h] - 1;
                    w->twinTable[h] = p + 1;
                }
            }
        }

        #endregion

        #region Assembly

        private static int AssembleChains(UnionWorkspace* w)
        {
            for (int v = 0; v < w->vertCount; v++) w->outHead[v] = -1;
            for (int p = w->pieceCount - 1; p >= 0; p--)
            {
                w->used[p] = 0;
                if (w->pieceKeep[p] == 0) continue;
                int v0 = Find(w, w->pieceV0[p]);
                w->outNext[p] = w->outHead[v0];
                w->outHead[v0] = p;
            }

            int chainCount = 0;
            int written = 0;
            w->chainEnd[0] = 0;

            double* S = w->S;
            for (int start = 0; start < w->pieceCount; start++)
            {
                if (w->pieceKeep[start] == 0 || w->used[start] != 0) continue;
                if (chainCount >= MaxContours) return -1;

                int startVert = Find(w, w->pieceV0[start]);
                int cur = start;
                int guard = w->pieceCount + 1;

                while (true)
                {
                    if (--guard < 0) return -1;
                    w->used[cur] = 1;
                    w->chainPiece[written++] = cur;

                    int endVert = Find(w, w->pieceV1[cur]);
                    if (endVert == startVert) break;

                    int next = PickNext(w, S, cur, endVert);
                    if (next < 0)
                    {
                        if (NearPt(w->vertX[endVert], w->vertY[endVert],
                                w->vertX[startVert], w->vertY[startVert], 4 * VertexTol))
                            break;
                        next = FindNearbyStart(w, endVert);
                        if (next < 0) return -1;
                    }
                    cur = next;
                }

                chainCount++;
                w->chainEnd[chainCount] = written;
            }
            return chainCount;
        }

        private static int PickNext(UnionWorkspace* w, double* S, int cur, int vert)
        {
            int first = -1, count = 0;
            for (int p = w->outHead[vert]; p >= 0; p = w->outNext[p])
            {
                if (w->used[p] != 0) continue;
                if (first < 0) first = p;
                count++;
            }
            if (count == 0) return -1;
            if (count == 1) return first;

            int curSeg = w->pieceSeg[cur];
            for (int p = w->outHead[vert]; p >= 0; p = w->outNext[p])
            {
                if (w->used[p] != 0) continue;
                if (w->pieceSeg[p] == curSeg && w->pieceT0[p] >= w->pieceT1[cur] - 1e-12)
                    return p;
            }

            double* Qc = S + curSeg * 6;
            Tangent(Qc, w->pieceT1[cur], out double inX, out double inY);
            double revX = -inX, revY = -inY;
            double best = double.MaxValue;
            int bestP = -1;
            for (int p = w->outHead[vert]; p >= 0; p = w->outNext[p])
            {
                if (w->used[p] != 0) continue;
                if (w->pieceInLeft[p] != w->pieceInLeft[cur]) continue;
                double* Qp = S + w->pieceSeg[p] * 6;
                Tangent(Qp, w->pieceT0[p], out double outX, out double outY);
                double cw = -Math.Atan2(revX * outY - revY * outX, revX * outX + revY * outY);
                if (cw <= 1e-9) cw += 2 * Math.PI;
                if (cw < best) { best = cw; bestP = p; }
            }
            return bestP >= 0 ? bestP : first;
        }

        private static int FindNearbyStart(UnionWorkspace* w, int vert)
        {
            double vx = w->vertX[vert], vy = w->vertY[vert];
            double bestD = 16 * VertexTol * VertexTol;
            int best = -1;
            for (int p = 0; p < w->pieceCount; p++)
            {
                if (w->pieceKeep[p] == 0 || w->used[p] != 0) continue;
                int v0 = Find(w, w->pieceV0[p]);
                double dx = w->vertX[v0] - vx, dy = w->vertY[v0] - vy;
                double d2 = dx * dx + dy * dy;
                if (d2 < bestD) { bestD = d2; best = p; }
            }
            return best;
        }

        #endregion

        #region Curve math

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Eval(double* Q, double t, out double x, out double y)
        {
            double mt = 1 - t;
            double w0 = mt * mt, w1 = 2 * mt * t, w2 = t * t;
            x = w0 * Q[0] + w1 * Q[2] + w2 * Q[4];
            y = w0 * Q[1] + w1 * Q[3] + w2 * Q[5];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Tangent(double* Q, double t, out double x, out double y)
        {
            double mt = 1 - t;
            x = 2 * (mt * (Q[2] - Q[0]) + t * (Q[4] - Q[2]));
            y = 2 * (mt * (Q[3] - Q[1]) + t * (Q[5] - Q[3]));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Blossom(double* Q, double a, double b, out double x, out double y)
        {
            double w0 = (1 - a) * (1 - b);
            double w1 = a * (1 - b) + b * (1 - a);
            double w2 = a * b;
            x = w0 * Q[0] + w1 * Q[2] + w2 * Q[4];
            y = w0 * Q[1] + w1 * Q[3] + w2 * Q[5];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool NearPt(double ax, double ay, double bx, double by, double tol = VertexTol)
        {
            double dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy <= tol * tol;
        }

        #endregion
    }
}
