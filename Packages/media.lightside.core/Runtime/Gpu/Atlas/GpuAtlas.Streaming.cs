using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    public abstract partial class GpuAtlas<TEntry> where TEntry : struct, IGpuAtlasEntry
    {
        private FastLongDictionary<int> streamPendingIndex;

        /// <summary>
        /// Queues a pixel rewrite of an existing tile — the streaming write path. Latest wins: a
        /// second update of the same tile before the flush replaces the queued pixels (the stale
        /// buffer returns to the pool), and a failed or yielded flush keeps the newest queued frame,
        /// so backpressure drops animation frames instead of stalling presentation. Takes ownership
        /// of <paramref name="pixels"/> (ArrayPool&lt;byte&gt;-rented). Returns false when the key has
        /// no placed tile (the buffer is returned to the pool). Pixels outside the tile's content
        /// area are clipped.
        /// </summary>
        public bool TryUpdateTilePixels(long key, byte[] pixels, int w, int h, bool isBGRA)
        {
            if (!entries.TryGetValue(key, out var e) || e.EncodedTile < 0 || pixels == null)
            {
                if (pixels != null)
                    ArrayPool<byte>.Return(pixels);
                return false;
            }

            streamPendingIndex ??= new FastLongDictionary<int>();
            if (streamPendingIndex.TryGetValue(key, out int at) && at < pendingTiles.Count
                && pendingTiles[at].key == key)
            {
                var pt = pendingTiles[at];
                if (pt.rgbaPixels != null)
                    ArrayPool<byte>.Return(pt.rgbaPixels);
                pt.rgbaPixels = pixels;
                pt.pixelWidth = w;
                pt.pixelHeight = h;
                pt.isBGRA = isBGRA;
                pendingTiles[at] = pt;
                return true;
            }

            streamPendingIndex[key] = pendingTiles.Count;
            pendingTiles.Add(new PendingTile
            {
                key = key,
                PageIndex = e.PageIndex,
                EncodedTile = e.EncodedTile,
                rgbaPixels = pixels,
                pixelWidth = w,
                pixelHeight = h,
                isBGRA = isBGRA
            });
            return true;
        }

        private void ClearStreamIndex() => streamPendingIndex?.Clear();
    }
}
