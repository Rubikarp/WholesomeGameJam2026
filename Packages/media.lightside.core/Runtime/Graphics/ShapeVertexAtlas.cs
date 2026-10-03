using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>
    /// Shared ref-counted data-texture atlas that stores one row of RGBAFloat texels per distinct
    /// content — flattened polygon outlines and composite-element streams. Equal content shares a row,
    /// referenced rows stay stable until every holder releases them, capacity grows on demand, and GPU
    /// uploads are deferred until <see cref="Flush"/>. A universal path (a plain <c>Texture2D</c> read
    /// with <c>tex2Dlod</c>), unlike a <c>StructuredBuffer</c>, which WebGL and GLES 2/3.0 cannot sample.
    /// </summary>
    /// <remarks>
    /// Rows are referenced during a resolve through <see cref="CollectRows"/>: every
    /// <see cref="Reference(IReadOnlyList{Color})"/> inside an open scope retains its row into the
    /// scope's list, and the consumer that owns the rendered mesh releases the previous list only
    /// after the new mesh replaces it — so a row can never be re-baked while a live mesh still reads
    /// it. Outside a scope, <see cref="Reference(IReadOnlyList{Color})"/> answers without retaining.
    /// The shader derives each row's V from its index and the published row-count global.
    /// </remarks>
    public sealed class ShapeVertexAtlas
    {
        /// <summary>The process-wide shape vertex atlas shared by rendering systems.</summary>
        public static readonly ShapeVertexAtlas Instance = new();

        /// <summary>Maximum vertices one polygon row can carry; longer contours are truncated when referenced.</summary>
        public const int MaxVertices = 64;

        /// <summary>RGBA texels one row holds — the budget a packed row layout divides.</summary>
        public const int RowTexels = MaxVertices / 2;

        private const int InitialCapacity = 16;
        private const int MaintenanceInterval = 300;

        private readonly object sync = new();
        private readonly Dictionary<int, List<int>> hashBuckets = new();
        private readonly List<Color[]> rows = new();
        private readonly List<int> rowHashes = new();
        private readonly List<int> refCounts = new();
        private readonly List<int> generations = new();
        private readonly Stack<int> freeSlots = new();
        private readonly List<int> pending = new();
        private readonly Stack<List<int>> bucketPool = new();

        private Texture2D texture;
        private Texture2D staging;
        private int capacity;
        private int lastMaintenanceFrame = -1;
        private int generationStamp;
        private List<int> collector;

        private ShapeVertexAtlas()
        {
        }

        static ShapeVertexAtlas()
        {
            CoreLoop.Maintaining += Instance.MaintenanceTick;
#if UNITY_EDITOR
            EditorLifecycle.UnmanagedCleaning += Instance.DestroyTextures;
#endif
        }

        /// <summary>
        /// Opens a row-reference scope: until <see cref="RowScope.Dispose"/>, every referenced row is
        /// retained once into <paramref name="rows"/>. The caller owns releasing those references after
        /// its previous mesh stops being rendered. Scopes are main-thread only and do not nest.
        /// </summary>
        public RowScope CollectRows(List<int> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (collector != null)
                throw new InvalidOperationException("A shape vertex row scope is already open.");
            collector = rows;
            return new RowScope(this);
        }

        /// <summary>An open row-reference scope; dispose to stop collecting.</summary>
        public readonly struct RowScope : IDisposable
        {
            private readonly ShapeVertexAtlas owner;

            internal RowScope(ShapeVertexAtlas owner) => this.owner = owner;

            /// <inheritdoc/>
            public void Dispose() => owner.collector = null;
        }

        /// <summary>
        /// Returns the row holding <paramref name="texels"/>, sharing an existing row of equal content
        /// or baking a new one. Texels past <see cref="RowTexels"/> are ignored; the remainder of the
        /// row is zero-filled, so the caller's layout owns what an over-read of zeroes means.
        /// Inside an open scope the row is retained into it.
        /// </summary>
        public int Reference(IReadOnlyList<Color> texels)
            => Reference(texels, out _);

        /// <inheritdoc cref="Reference(IReadOnlyList{Color})"/>
        /// <param name="texels">The row content.</param>
        /// <param name="generation">The row's generation stamp, for callers caching a (row, generation) pair.</param>
        public int Reference(IReadOnlyList<Color> texels, out int generation)
        {
            if (texels == null) throw new ArgumentNullException(nameof(texels));
            var count = Mathf.Min(texels.Count, RowTexels);
            var hash = Hash(texels, count);

            lock (sync)
            {
                if (!FindRow(hash, texels, count, out var row))
                {
                    row = Allocate();
                    StoreContent(row, texels, count, hash);
                    pending.Add(row);
                }
                RetainIntoScope(row);
                generation = generations[row];
                return row;
            }
        }

        /// <summary>
        /// Re-references a row a caller cached from an earlier
        /// <see cref="Reference(IReadOnlyList{Color}, out int)"/> without re-hashing its content.
        /// Succeeds only while (<paramref name="row"/>, <paramref name="generation"/>) still identify
        /// the same baked content; on success the row is retained into an open scope.
        /// </summary>
        public bool TryReference(int row, int generation)
        {
            lock (sync)
            {
                if ((uint)row >= (uint)refCounts.Count || refCounts[row] < 0 ||
                    generations[row] != generation)
                    return false;
                RetainIntoScope(row);
                return true;
            }
        }

        /// <summary>Retains one additional reference to a live row.</summary>
        public void Retain(int row)
        {
            lock (sync)
            {
                if ((uint)row >= (uint)refCounts.Count || refCounts[row] < 0)
                    throw new ArgumentOutOfRangeException(nameof(row), row,
                        "The shape vertex row is not allocated.");
                refCounts[row]++;
            }
        }

        /// <summary>
        /// Releases one reference. A row whose last reference is released is reclaimed immediately —
        /// release only after no rendered mesh reads it any longer.
        /// </summary>
        public void Release(int row)
        {
            lock (sync)
            {
                if ((uint)row >= (uint)refCounts.Count || refCounts[row] <= 0)
                    throw new ArgumentOutOfRangeException(nameof(row), row,
                        "The shape vertex row is not referenced.");
                if (--refCounts[row] == 0) Reclaim(row);
            }
        }

        /// <summary>Runs the periodic reclaim and shrink policy; work is performed every 300 frames.</summary>
        public void MaintenanceTick(int frame)
        {
            if (lastMaintenanceFrame == frame) return;
            lastMaintenanceFrame = frame;
            if (frame % MaintenanceInterval != 0) return;
            Sweep();
            TryShrink();
            Flush();
        }

        /// <summary>
        /// Reclaims rows nothing references — the strays a scope-less geometry query can bake. Live row
        /// indices never move. Main thread only.
        /// </summary>
        public void Sweep()
        {
            lock (sync)
            {
                for (var row = 0; row < rows.Count; row++)
                    if (refCounts[row] == 0)
                        Reclaim(row);
            }
        }

        /// <summary>
        /// Releases excess tail capacity when the highest live row occupies at most one quarter of the
        /// current texture. Surviving rows retain their indices and are re-uploaded by the next
        /// <see cref="Flush"/>. Main thread only.
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
                    rowHashes.RemoveAt(row);
                    refCounts.RemoveAt(row);
                    generations.RemoveAt(row);
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
        /// Bakes every queued row and publishes the atlas through shared shader globals. Uses
        /// row-granular GPU copies when supported and a full CPU-backed upload otherwise; growth
        /// re-uploads all live rows. Main thread only.
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
                        UploadRow(row);
                    }
                    texture.Apply(false, false);
                }
                else if (UseStagedCopy)
                {
                    for (var i = 0; i < pending.Count; i++)
                    {
                        var row = pending[i];
                        if (refCounts[row] < 0) continue;
                        UploadRowStaged(row);
                    }
                }
                else
                {
                    for (var i = 0; i < pending.Count; i++)
                    {
                        var row = pending[i];
                        if (refCounts[row] < 0) continue;
                        UploadRow(row);
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

        private void RetainIntoScope(int row)
        {
            if (collector == null) return;
            refCounts[row]++;
            collector.Add(row);
        }

        private bool FindRow(int hash, IReadOnlyList<Color> texels, int count, out int row)
        {
            if (hashBuckets.TryGetValue(hash, out var bucket))
                for (var i = 0; i < bucket.Count; i++)
                {
                    var candidate = bucket[i];
                    if (ContentEquals(rows[candidate], texels, count)) { row = candidate; return true; }
                }
            row = -1;
            return false;
        }

        private static bool ContentEquals(Color[] stored, IReadOnlyList<Color> texels, int count)
        {
            for (var i = 0; i < count; i++)
                if (stored[i] != texels[i])
                    return false;
            for (var i = count; i < RowTexels; i++)
                if (stored[i] != default(Color))
                    return false;
            return true;
        }

        private int Allocate()
        {
            if (freeSlots.Count > 0)
            {
                var reused = freeSlots.Pop();
                refCounts[reused] = 0;
                return reused;
            }

            var row = rows.Count;
            rows.Add(new Color[RowTexels]);
            rowHashes.Add(0);
            refCounts.Add(0);
            generations.Add(0);
            return row;
        }

        private void StoreContent(int row, IReadOnlyList<Color> texels, int count, int hash)
        {
            var storage = rows[row] ??= new Color[RowTexels];
            for (var i = 0; i < count; i++) storage[i] = texels[i];
            for (var i = count; i < RowTexels; i++) storage[i] = default;
            rowHashes[row] = hash;
            generations[row] = ++generationStamp;

            if (!hashBuckets.TryGetValue(hash, out var bucket))
                hashBuckets[hash] = bucket = bucketPool.Count > 0 ? bucketPool.Pop() : new List<int>();
            bucket.Add(row);
        }

        private void UnbindContent(int row)
        {
            var hash = rowHashes[row];
            if (hashBuckets.TryGetValue(hash, out var bucket))
            {
                bucket.Remove(row);
                if (bucket.Count == 0)
                {
                    hashBuckets.Remove(hash);
                    bucketPool.Push(bucket);
                }
            }
        }

        private void Reclaim(int row)
        {
            UnbindContent(row);
            refCounts[row] = -1;
            freeSlots.Push(row);
        }

        private bool EnsureCapacity(int rowCount, out Texture2D retired)
        {
            retired = null;
            if (texture != null && capacity >= rowCount) return false;

            var newCapacity = Mathf.Max(InitialCapacity, capacity);
            while (newCapacity < rowCount) newCapacity *= 2;

            var replacement = new Texture2D(RowTexels, newCapacity, TextureFormat.RGBAFloat, false, true)
            {
                name = "Shape Vertex Atlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };

            retired = texture;
            texture = replacement;
            capacity = newCapacity;
            return true;
        }

        private void UploadRow(int row)
            => texture.SetPixels(0, row, RowTexels, 1, rows[row]);

        private void UploadRowStaged(int row)
        {
            if (staging == null)
                staging = new Texture2D(RowTexels, 1, TextureFormat.RGBAFloat, false, true)
                {
                    name = "Shape Vertex Atlas Staging",
                    hideFlags = HideFlags.HideAndDontSave,
                };

            staging.SetPixels(rows[row]);
            staging.Apply(false, false);
            Graphics.CopyTexture(staging, 0, 0, 0, 0, RowTexels, 1, texture, 0, 0, 0, row);
        }

        private void BindGlobals()
        {
            Shader.SetGlobalTexture(LightSideShaderIds.ShapeVertices, texture);
            Shader.SetGlobalFloat(LightSideShaderIds.ShapeVertexRows, capacity);
        }

        private static int Hash(IReadOnlyList<Color> texels, int count)
        {
            unchecked
            {
                var hash = 17;
                for (var i = 0; i < count; i++)
                {
                    var c = texels[i];
                    hash = hash * 31 + c.r.GetHashCode();
                    hash = hash * 31 + c.g.GetHashCode();
                    hash = hash * 31 + c.b.GetHashCode();
                    hash = hash * 31 + c.a.GetHashCode();
                }
                return hash;
            }
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
                for (var row = 0; row < rows.Count; row++)
                    if (refCounts[row] >= 0)
                        pending.Add(row);
                BindGlobals();
            }

            ObjectUtils.SafeDestroy(retired);
            ObjectUtils.SafeDestroy(retiredStaging);
        }
    }
}
