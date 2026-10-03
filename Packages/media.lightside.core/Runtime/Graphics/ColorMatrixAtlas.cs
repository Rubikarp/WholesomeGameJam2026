using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Shared ref-counted data-texture atlas that stores one row of three RGBAFloat texels per distinct
    /// <see cref="ColorMatrix"/> — texel <c>i</c> carries output row <c>i</c> as
    /// <c>(m_i0, m_i1, m_i2, offset_i)</c>, sampled with <c>tex2Dlod</c> by quads whose colour the
    /// shader must transform (colour glyphs, texture paints). Equal matrices share a row, acquired
    /// rows remain stable until released and reclaimed, and GPU uploads are deferred until
    /// <see cref="Flush"/> so callers can register matrices from worker threads.
    /// </summary>
    public sealed class ColorMatrixAtlas
    {
        /// <summary>The process-wide colour matrix atlas shared by rendering systems.</summary>
        public static readonly ColorMatrixAtlas Instance = new();

        /// <summary>Returned by <see cref="Acquire"/> for an identity matrix, which needs no row.</summary>
        public const int InvalidRow = -1;

        /// <summary>RGBAFloat texels one matrix row occupies.</summary>
        public const int RowTexels = 3;

        private static readonly int textureId = Shader.PropertyToID("_LightSideColorMatrixAtlas");
        private static readonly int rowCountId = Shader.PropertyToID("_LightSideColorMatrixRows");

        private const int InitialCapacity = 8;
        private const int MaintenanceInterval = 300;

        private readonly object sync = new();
        private readonly Dictionary<ColorMatrix, int> rowMap = new();
        private readonly List<ColorMatrix> rows = new();
        private readonly List<int> refCounts = new();
        private readonly List<int> idleFrame = new();
        private readonly Stack<int> freeSlots = new();
        private readonly List<int> pending = new();
        private readonly Color[] rowBuffer = new Color[RowTexels];

        private Texture2D texture;
        private int capacity;
        private int lastMaintenanceFrame = -1;

        private ColorMatrixAtlas()
        {
        }

        static ColorMatrixAtlas()
        {
            CoreLoop.Maintaining += Instance.MaintenanceTick;
#if UNITY_EDITOR
            EditorLifecycle.UnmanagedCleaning += Instance.DestroyTextures;
#endif
        }

        /// <summary>
        /// Acquires a reference to a row for <paramref name="matrix"/>. Equal matrices share a row.
        /// Pair every successful call with <see cref="Release"/>. Thread-safe and allocation-free for
        /// already registered content; returns <see cref="InvalidRow"/> for an identity matrix.
        /// </summary>
        public int Acquire(in ColorMatrix matrix)
        {
            if (matrix.IsIdentity) return InvalidRow;

            lock (sync)
            {
                if (rowMap.TryGetValue(matrix, out var row))
                {
                    refCounts[row]++;
                    return row;
                }

                if (freeSlots.Count > 0)
                {
                    row = freeSlots.Pop();
                    rows[row] = matrix;
                    refCounts[row] = 1;
                    idleFrame[row] = -1;
                }
                else
                {
                    row = rows.Count;
                    rows.Add(matrix);
                    refCounts.Add(1);
                    idleFrame.Add(-1);
                }

                rowMap[matrix] = row;
                pending.Add(row);
                return row;
            }
        }

        /// <summary>
        /// Releases one reference acquired through <see cref="Acquire"/>. The row remains stable and
        /// available for reacquisition until a later maintenance sweep reclaims it. Releasing
        /// <see cref="InvalidRow"/> is the defined no-op for an identity matrix; other invalid or
        /// unbalanced releases fail immediately.
        /// </summary>
        public void Release(int row)
        {
            if (row == InvalidRow) return;

            lock (sync)
            {
                if ((uint)row >= (uint)refCounts.Count || refCounts[row] < 0)
                    throw new ArgumentOutOfRangeException(nameof(row), row, "The colour matrix row is not allocated.");
                if (refCounts[row] == 0)
                    throw new InvalidOperationException($"Colour matrix row {row} has already been released.");
                if (--refCounts[row] == 0) idleFrame[row] = -1;
            }
        }

        /// <summary>
        /// Runs the atlas's periodic reclaim policy. Call once per frame with a monotonically
        /// increasing frame number; work is performed every 300 frames.
        /// </summary>
        public void MaintenanceTick(int frame)
        {
            if (lastMaintenanceFrame == frame) return;
            lastMaintenanceFrame = frame;
            if (frame % MaintenanceInterval != 0) return;
            Sweep(frame, MaintenanceInterval);
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

                    rowMap.Remove(rows[row]);
                    rows[row] = default;
                    refCounts[row] = -1;
                    idleFrame[row] = -1;
                    freeSlots.Push(row);
                }
            }
        }

        /// <summary>
        /// Bakes every queued row and publishes the atlas through shared shader globals. Growth
        /// re-bakes all live rows. Main thread only.
        /// </summary>
        public void Flush()
        {
            Texture2D retired = null;

            lock (sync)
            {
                if (pending.Count == 0) return;

                if (EnsureCapacity(rows.Count, out retired))
                {
                    for (var row = 0; row < rows.Count; row++)
                    {
                        if (refCounts[row] < 0) continue;
                        BakeRow(row);
                    }
                }
                else
                {
                    for (var i = 0; i < pending.Count; i++)
                    {
                        var row = pending[i];
                        if (refCounts[row] < 0) continue;
                        BakeRow(row);
                    }
                }

                texture.Apply(false, false);
                pending.Clear();
                BindGlobals();
            }

            ObjectUtils.SafeDestroy(retired);
        }

        private bool EnsureCapacity(int rowCount, out Texture2D retired)
        {
            retired = null;
            if (texture != null && capacity >= rowCount) return false;

            var newCapacity = Mathf.Max(InitialCapacity, capacity);
            while (newCapacity < rowCount) newCapacity *= 2;

            var replacement = new Texture2D(RowTexels, newCapacity, TextureFormat.RGBAFloat, false, true)
            {
                name = "Color Matrix Atlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };

            retired = texture;
            texture = replacement;
            capacity = newCapacity;
            return true;
        }

        private void BakeRow(int row)
        {
            var m = rows[row];
            rowBuffer[0] = new Color(m.r.x, m.r.y, m.r.z, m.r.w);
            rowBuffer[1] = new Color(m.g.x, m.g.y, m.g.z, m.g.w);
            rowBuffer[2] = new Color(m.b.x, m.b.y, m.b.z, m.b.w);
            texture.SetPixels(0, row, RowTexels, 1, rowBuffer);
        }

        private void BindGlobals()
        {
            Shader.SetGlobalTexture(textureId, texture);
            Shader.SetGlobalFloat(rowCountId, capacity);
        }

        /// <summary>Destroys native texture state without invalidating rows still held during reload teardown.</summary>
        internal void DestroyTextures()
        {
            Texture2D retired;

            lock (sync)
            {
                retired = texture;
                texture = null;
                capacity = 0;
                lastMaintenanceFrame = -1;
                pending.Clear();
                for (var row = 0; row < rows.Count; row++)
                    if (refCounts[row] >= 0)
                        pending.Add(row);
                BindGlobals();
            }

            ObjectUtils.SafeDestroy(retired);
        }
    }
}
