using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>
    /// Shared ref-counted texture atlas that stores one 256-sample row per distinct
    /// (<see cref="Gradient"/>, <see cref="ColorMatrix"/>) pair — the matrix transforms each baked
    /// sample after interpolation, so a filtered row is exact in every interpolation space. Equal
    /// content shares storage, acquired rows remain stable until released and reclaimed, and GPU
    /// uploads are deferred until <see cref="Flush"/> so callers can register gradients from worker threads.
    /// </summary>
    public sealed class GradientRampAtlas
    {
        /// <summary>The process-wide gradient ramp atlas shared by rendering systems.</summary>
        public static readonly GradientRampAtlas Instance = new();

        /// <summary>Returned by <see cref="Acquire"/> when the supplied gradient has no sampleable stops.</summary>
        public const int InvalidRow = -1;

        /// <summary>Number of horizontal samples baked into every gradient row.</summary>
        public const int RampWidth = 256;

        private static readonly int textureId = Shader.PropertyToID("_LightSideGradientRamp");
        private static readonly int rowCountId = Shader.PropertyToID("_LightSideGradientRampRows");

        private const int InitialCapacity = 64;
        private const int MaintenanceInterval = 300;

        private readonly struct RampKey : IEquatable<RampKey>
        {
            public readonly Gradient gradient;
            public readonly ColorMatrix filter;

            public RampKey(in Gradient gradient, in ColorMatrix filter)
            {
                this.gradient = gradient;
                this.filter = filter;
            }

            public bool Equals(RampKey other)
                => gradient.Equals(other.gradient) && filter.Equals(other.filter);

            public override int GetHashCode() => HashCode.Combine(gradient, filter);
        }

        private readonly object sync = new();
        private readonly Dictionary<RampKey, int> rowMap = new();
        private readonly Dictionary<ReadOnlyArray<GradientStop>, int> sourceRows = new();
        private readonly List<Gradient> rows = new();
        private readonly List<ColorMatrix> rowFilters = new();
        private readonly List<ReadOnlyArray<GradientStop>> rowSource = new();

        /// <summary>
        /// Next row baked from the same stop storage, or −1 — the rows one source produced, one per
        /// distinct filter, linked through <see cref="sourceRows"/> as the head of each chain.
        /// </summary>
        private readonly List<int> rowSourceChain = new();
        private readonly List<int> refCounts = new();
        private readonly List<int> idleFrame = new();
        private readonly Stack<int> freeSlots = new();
        private readonly List<int> pending = new();
        private readonly Color32[] rowBuffer = new Color32[RampWidth];

        private Texture2D texture;
        private Texture2D staging;
        private int capacity;
        private int lastMaintenanceFrame = -1;

        private GradientRampAtlas()
        {
        }

        /// <summary>
        /// Acquires a reference to a ramp row for <paramref name="gradient"/>. Equal gradient content
        /// shares a row. Reacquiring an edited source immediately after its final release reuses and
        /// re-bakes the same row, keeping animated parameters stable without accumulating slots.
        /// Pair every successful call with <see cref="Release"/>. Thread-safe and allocation-free for
        /// already registered content; returns <see cref="InvalidRow"/> for an empty gradient.
        /// </summary>
        public int Acquire(in Gradient gradient) => Acquire(in gradient, ColorMatrix.Identity);

        /// <summary>
        /// Acquires a ramp row whose baked samples pass through <paramref name="filter"/> after
        /// interpolation — the row a colour-filtered gradient renders with. Same sharing, reacquire
        /// and release contract as <see cref="Acquire(in Gradient)"/>; rows of one gradient under
        /// different filters are distinct, and an animated filter re-acquiring one source reuses and
        /// re-bakes its row exactly like an animated gradient does.
        /// </summary>
        public int Acquire(in Gradient gradient, in ColorMatrix filter)
        {
            if (!gradient.IsValid) return InvalidRow;

            lock (sync)
            {
                var probe = new RampKey(in gradient, in filter);
                if (rowMap.TryGetValue(probe, out var row))
                {
                    refCounts[row]++;
                    BindSource(gradient.Stops, row);
                    return row;
                }

                row = FindReusableRow(gradient.Stops);
                if (row >= 0)
                {
                    rowMap.Remove(new RampKey(rows[row], rowFilters[row]));
                    var rebaked = gradient.Clone();
                    rows[row] = rebaked;
                    rowFilters[row] = filter;
                    rowMap[new RampKey(in rebaked, in filter)] = row;
                    refCounts[row] = 1;
                    idleFrame[row] = -1;
                    BindSource(gradient.Stops, row);
                    pending.Add(row);
                    return row;
                }

                var snapshot = gradient.Clone();
                if (freeSlots.Count > 0)
                {
                    row = freeSlots.Pop();
                    rows[row] = snapshot;
                    rowFilters[row] = filter;
                    refCounts[row] = 1;
                    idleFrame[row] = -1;
                }
                else
                {
                    row = rows.Count;
                    rows.Add(snapshot);
                    rowFilters.Add(filter);
                    rowSource.Add(default);
                    rowSourceChain.Add(-1);
                    refCounts.Add(1);
                    idleFrame.Add(-1);
                }

                rowMap[new RampKey(in snapshot, in filter)] = row;
                BindSource(gradient.Stops, row);
                pending.Add(row);
                return row;
            }
        }

        /// <summary>
        /// Releases one reference acquired through <see cref="Acquire"/>. The row remains stable and
        /// available for reacquisition until a later maintenance sweep reclaims it. Releasing
        /// <see cref="InvalidRow"/> is the defined no-op for an empty gradient; other invalid or
        /// unbalanced releases fail immediately.
        /// </summary>
        public void Release(int row)
        {
            if (row == InvalidRow) return;

            lock (sync)
            {
                if ((uint)row >= (uint)refCounts.Count || refCounts[row] < 0)
                    throw new ArgumentOutOfRangeException(nameof(row), row, "The gradient ramp row is not allocated.");
                if (refCounts[row] == 0)
                    throw new InvalidOperationException($"Gradient ramp row {row} has already been released.");
                if (--refCounts[row] == 0) idleFrame[row] = -1;
            }
        }

        /// <summary>
        /// Runs the atlas's periodic reclaim and shrink policy. Call once per frame with a monotonically
        /// increasing frame number; work is performed every 300 frames.
        /// </summary>
        public void MaintenanceTick(int frame)
        {
            if (lastMaintenanceFrame == frame) return;
            lastMaintenanceFrame = frame;
            if (frame % MaintenanceInterval != 0) return;
            Sweep(frame, MaintenanceInterval);
            TryShrink();
            Flush();
        }

        /// <summary>
        /// Reclaims rows that have remained unreferenced for at least <paramref name="graceFrames"/>.
        /// Live row indices never move. Call on the main thread with the current frame number.
        /// </summary>
        public void Sweep(int frame, int graceFrames)
        {
            if (graceFrames < 0) throw new ArgumentOutOfRangeException(nameof(graceFrames));

            lock (sync)
            {
                for (var row = 0; row < rows.Count; row++)
                {
                    if (refCounts[row] != 0) continue;
                    if (idleFrame[row] < 0)
                    {
                        idleFrame[row] = frame;
                        continue;
                    }
                    if (frame - idleFrame[row] < graceFrames) continue;

                    rowMap.Remove(new RampKey(rows[row], rowFilters[row]));
                    UnbindSource(row);
                    rows[row] = default;
                    rowFilters[row] = default;
                    refCounts[row] = -1;
                    idleFrame[row] = -1;
                    freeSlots.Push(row);
                }
            }
        }

        /// <summary>
        /// Releases excess tail capacity after a burst when the highest live row occupies at most one
        /// quarter of the current texture. Surviving rows retain their indices and are re-uploaded by the
        /// next <see cref="Flush"/>. Main thread only.
        /// </summary>
        public void TryShrink()
        {
            Texture2D retired;

            lock (sync)
            {
                if (texture == null || capacity <= InitialCapacity) return;

                var high = rows.Count - 1;
                while (high >= 0 && refCounts[high] < 0) high--;
                var needed = high + 1;
                if (needed > capacity / 4) return;

                for (var row = rows.Count - 1; row > high; row--)
                {
                    rows.RemoveAt(row);
                    rowFilters.RemoveAt(row);
                    rowSource.RemoveAt(row);
                    rowSourceChain.RemoveAt(row);
                    refCounts.RemoveAt(row);
                    idleFrame.RemoveAt(row);
                }

                freeSlots.Clear();
                for (var row = high; row >= 0; row--)
                    if (refCounts[row] < 0)
                        freeSlots.Push(row);

                retired = texture;
                texture = null;
                capacity = 0;

                pending.Clear();
                for (var row = 0; row <= high; row++)
                    if (refCounts[row] >= 0)
                        pending.Add(row);
                BindGlobals();
            }

            ObjectUtils.SafeDestroy(retired);
        }

        /// <summary>
        /// Bakes every queued row and publishes the atlas through shared shader globals. Uses row-granular
        /// GPU copies when supported and a full CPU-backed upload otherwise; growth re-bakes all live rows.
        /// Main thread only.
        /// </summary>
        public void Flush()
        {
            Texture2D retired = null;

            lock (sync)
            {
                if (pending.Count == 0) return;

                var grew = EnsureCapacity(rows.Count, out retired);
                if (grew)
                {
                    for (var row = 0; row < rows.Count; row++)
                    {
                        if (refCounts[row] < 0) continue;
                        BakeRowToTexture(row);
                    }
                    texture.Apply(false, false);
                }
                else if (UseStagedCopy)
                {
                    for (var i = 0; i < pending.Count; i++)
                    {
                        var row = pending[i];
                        if (refCounts[row] < 0) continue;
                        BakeRowStaged(row);
                    }
                }
                else
                {
                    for (var i = 0; i < pending.Count; i++)
                    {
                        var row = pending[i];
                        if (refCounts[row] < 0) continue;
                        BakeRowToTexture(row);
                    }
                    texture.Apply(false, false);
                }

                pending.Clear();
                BindGlobals();
            }

            ObjectUtils.SafeDestroy(retired);
        }

        private static bool UseStagedCopy =>
            (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

        private bool EnsureCapacity(int rowCount, out Texture2D retired)
        {
            retired = null;
            if (texture != null && capacity >= rowCount) return false;

            var newCapacity = Mathf.Max(InitialCapacity, capacity);
            while (newCapacity < rowCount) newCapacity *= 2;

            var replacement = new Texture2D(RampWidth, newCapacity, TextureFormat.RGBA32, false, false)
            {
                name = "Gradient Ramp Atlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            retired = texture;
            texture = replacement;
            capacity = newCapacity;
            return true;
        }

        private void BakeRow(int row)
        {
            var gradient = rows[row];
            var filter = rowFilters[row];
            if (filter.IsIdentity)
                for (var i = 0; i < RampWidth; i++)
                    rowBuffer[i] = gradient.Evaluate(i / (float)(RampWidth - 1));
            else
                for (var i = 0; i < RampWidth; i++)
                    rowBuffer[i] = filter.Transform(gradient.Evaluate(i / (float)(RampWidth - 1)));
        }

        private void BakeRowToTexture(int row)
        {
            BakeRow(row);
            texture.SetPixels32(0, row, RampWidth, 1, rowBuffer);
        }

        private void BakeRowStaged(int row)
        {
            if (staging == null)
                staging = new Texture2D(RampWidth, 1, TextureFormat.RGBA32, false, false)
                {
                    name = "Gradient Ramp Atlas Staging",
                    hideFlags = HideFlags.HideAndDontSave,
                };

            BakeRow(row);
            staging.SetPixels32(rowBuffer);
            staging.Apply(false, false);
            Graphics.CopyTexture(staging, 0, 0, 0, 0, RampWidth, 1, texture, 0, 0, 0, row);
        }

        /// <summary>
        /// A released row baked from this same stop storage, or −1. Reuse is restricted to the
        /// storage that produced the row because rebaking is immediate and a released row can still
        /// be read by a mesh on screen: only the source re-acquiring it is guaranteed to be
        /// replacing that mesh in the same pass. Reuse candidates are all the rows one source
        /// produced — one per distinct filter over it — chained through
        /// <see cref="rowSourceChain"/>.
        /// </summary>
        private int FindReusableRow(ReadOnlyArray<GradientStop> stops)
        {
            if (!stops.HasStorage || !sourceRows.TryGetValue(stops, out var row)) return -1;
            while (row >= 0)
            {
                if ((uint)row >= (uint)refCounts.Count) return -1;
                if (refCounts[row] == 0) return row;
                row = rowSourceChain[row];
            }
            return -1;
        }

        private void BindSource(ReadOnlyArray<GradientStop> stops, int row)
        {
            if (!stops.HasStorage || rowSource[row].HasSameStorage(stops)) return;
            UnbindSource(row);
            rowSource[row] = stops;
            rowSourceChain[row] = sourceRows.TryGetValue(stops, out var head) ? head : -1;
            sourceRows[stops] = row;
        }

        private void UnbindSource(int row)
        {
            var source = rowSource[row];
            if (!source.HasStorage) return;

            if (sourceRows.TryGetValue(source, out var head))
            {
                if (head == row)
                {
                    var next = rowSourceChain[row];
                    if (next < 0) sourceRows.Remove(source);
                    else sourceRows[source] = next;
                }
                else
                {
                    var previous = head;
                    while (previous >= 0 && rowSourceChain[previous] != row)
                        previous = rowSourceChain[previous];
                    if (previous >= 0) rowSourceChain[previous] = rowSourceChain[row];
                }
            }

            rowSource[row] = default;
            rowSourceChain[row] = -1;
        }

        private void BindGlobals()
        {
            Shader.SetGlobalTexture(textureId, texture);
            Shader.SetGlobalFloat(rowCountId, capacity);
        }

        static GradientRampAtlas()
        {
            CoreLoop.Maintaining += Instance.MaintenanceTick;
#if UNITY_EDITOR
            EditorLifecycle.UnmanagedCleaning += Instance.DestroyTextures;
#endif
        }

        /// <summary>Destroys native texture state without invalidating rows still held during reload teardown.</summary>
        internal void DestroyTextures()
        {
            Texture2D retired;
            Texture2D retiredStaging;

            lock (sync)
            {
                retired = texture;
                retiredStaging = staging;
                texture = null;
                staging = null;
                capacity = 0;
                lastMaintenanceFrame = -1;
                pending.Clear();
                BindGlobals();
            }

            ObjectUtils.SafeDestroy(retired);
            ObjectUtils.SafeDestroy(retiredStaging);
        }
    }
}
