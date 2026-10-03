using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace LightSide
{
    public abstract partial class GpuAtlas<TEntry> where TEntry : struct, IGpuAtlasEntry
    {
        private int EvictableParkedCount()
        {
            if (evictableLists == null) return 0;
            int n = 0;
            for (int i = 0; i < evictableLists.Length; i++)
                n += evictableLists[i].Count;
            return n;
        }

        private static List<long> removeScratchKeys;

        /// <summary>
        /// Removes every entry matching <paramref name="match"/>: the consumer purges its own pending
        /// work via <see cref="OnRemoveSweep"/>, queued pixel tiles are dropped, still-referenced tiles
        /// retire deferred, dead tiles free immediately. Returns the number of removed entries.
        /// </summary>
        public int RemoveWhere(Func<TEntry, bool> match)
        {
            OnRemoveSweep(match);

            for (int i = pendingTiles.Count - 1; i >= 0; i--)
            {
                if (entries.TryGetValue(pendingTiles[i].key, out var pe) && match(pe))
                {
                    if (pendingTiles[i].rgbaPixels != null)
                        ArrayPool<byte>.Return(pendingTiles[i].rgbaPixels);
                    pendingTiles[i] = pendingTiles[^1];
                    pendingTiles.RemoveAt(pendingTiles.Count - 1);
                }
            }

            var keysToRemove = removeScratchKeys ??= new List<long>();
            keysToRemove.Clear();
            foreach (var kvp in entries)
            {
                if (match(kvp.Value))
                    keysToRemove.Add(kvp.Key);
            }

            foreach (var key in keysToRemove)
            {
                var e = entries[key];
                entries.Remove(key);
                OnEntryDropped(in e);

                int ci = GetSizeClassFromEncoded(e.EncodedTile);
                UnlinkEvictable(ci, key);

                bool wasLive = e.RefCount > 0;
                if (batchProtected.Remove(key))
                    e.RefCount--;

                var slot = new TileSlot { PageIndex = e.PageIndex, EncodedTile = e.EncodedTile };
                if (e.RefCount > 0)
                {
                    deferredTileRetirements.Add(new DeferredTileRetirement
                    {
                        slot = slot,
                        live = true
                    });
                }
                else
                {
                    pageEntryCount[e.PageIndex]--;
                    if (wasLive)
                        pageLiveCount[e.PageIndex]--;
                    if (e.EncodedTile >= 0)
                        freeTiles[ci].Add(slot);
                }
            }

            CleanupEmptyPages();
            reclaimPending = true;

            logZone.MeowFormat("[{0}] RemoveWhere: removed {1} entries, totalEntries={2}, pages={3}",
                Label, keysToRemove.Count, entries.Count, sliceCount);
            return keysToRemove.Count;
        }

        /// <summary>Consumer hook: purge consumer-owned pending work whose entries match the sweep predicate. Runs before the entries are removed.</summary>
        protected virtual void OnRemoveSweep(Func<TEntry, bool> match) { }

        /// <summary>Consumer hook: an entry left the index (removal, eviction, wipe). Free consumer-side resources it owns (e.g. a transform-table row).</summary>
        protected virtual void OnEntryDropped(in TEntry entry) { }

        /// <summary>Consumer hook: a live entry's placement was rewritten (compaction relocate). Republish consumer-side placement data for it.</summary>
        protected virtual void OnTilePlaced(long key, in TEntry entry) { }

        protected TileSlot AllocateTile(int tileSize)
        {
            allocatedThisFrame = true;
            int ci = SizeClassIndex(tileSize);

            var free = freeTiles[ci];
            if (free.Count > 0)
            {
                var slot = free[^1];
                free.RemoveAt(free.Count - 1);
                return slot;
            }

            for (int i = 0; i < shelves.Count; i++)
            {
                var shelf = shelves[i];
                if (shelf.height == tileSize && shelf.nextX + tileSize <= PageSize)
                {
                    int tileCol = shelf.nextX / tileSize;
                    int shelfRow = shelf.y / gridUnit;
                    shelf.nextX += tileSize;
                    shelves[i] = shelf;
                    return new TileSlot
                    {
                        PageIndex = shelf.PageIndex,
                        EncodedTile = tileCol + shelfRow * colSlots + ci * ClassStride
                    };
                }
            }

            if (TryOpenShelf(ci, tileSize, out var opened)) return opened;

            if (evictableLists[ci].Count > 0)
            {
                var key = evictableLists[ci].First.Value;
                evictableLists[ci].RemoveFirst();
                evictableNodeMaps[ci].Remove(key);

                var e = entries[key];
                entries.Remove(key);
                OnEntryDropped(in e);

                pageEntryCount[e.PageIndex]--;

                evictedThisFrame++;
                logZone.MeowFormat("[{0}] silent evict key=0x{1:X} (refCount=0) to serve a size-{2} allocation",
                    Label, key, tileSize);

                return new TileSlot { PageIndex = e.PageIndex, EncodedTile = e.EncodedTile };
            }

            RecycleDeadPages();
            if (TryOpenShelf(ci, tileSize, out opened)) return opened;

            return GrowAtlas(tileSize);
        }

        /// <summary>Opens a new shelf on the first page with vertical headroom. Pre-growth page recycling re-runs this scan: per-tile LRU reuse cannot cross size classes, but a recycled page serves any class.</summary>
        private bool TryOpenShelf(int ci, int tileSize, out TileSlot slot)
        {
            for (int p = 0; p < sliceCount; p++)
            {
                int usedH = pageUsedHeight[p];
                if (usedH + tileSize <= PageSize)
                {
                    int y = usedH;
                    shelves.Add(new Shelf { PageIndex = p, y = y, height = tileSize, nextX = tileSize });
                    pageUsedHeight[p] = y + tileSize;
                    int shelfRow = y / gridUnit;
                    slot = new TileSlot
                    {
                        PageIndex = p,
                        EncodedTile = shelfRow * colSlots + ci * ClassStride
                    };
                    return true;
                }
            }
            slot = default;
            return false;
        }

        /// <summary>Removes a key from the size class's evictable LRU (list + node map), if linked.</summary>
        private void UnlinkEvictable(int ci, long key)
        {
            if (evictableNodeMaps[ci].TryGetValue(key, out var node))
            {
                evictableLists[ci].Remove(node);
                evictableNodeMaps[ci].Remove(key);
            }
        }

        /// <summary>Clears every entry/tile index structure (entries, evictable lists, free tiles, page counters, shelves) — the shared prologue of recover, compact, and dispose.</summary>
        private void ResetIndexState()
        {
            entries.Clear();
            for (int i = 0; i < numSizeClasses; i++)
            {
                evictableLists[i].Clear();
                evictableNodeMaps[i].Clear();
                freeTiles[i].Clear();
            }
            pageEntryCount.Clear();
            pageLiveCount.Clear();
            shelves.Clear();
            pageUsedHeight.Clear();
        }

        public void DecodeTileXY(int encodedTile, int tileSize, out int x, out int y)
        {
            int remainder = encodedTile % ClassStride;
            int shelfRow = remainder / colSlots;
            int tileCol = remainder % colSlots;
            x = tileCol * tileSize;
            y = shelfRow * gridUnit;
        }

        private static void ReleaseTexture(Texture texture)
        {
            if (texture == null) return;
            if (texture is RenderTexture rt)
                rt.Release();
            ObjectUtils.SafeDestroy(texture);
        }

    }
}
