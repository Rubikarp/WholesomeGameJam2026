using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace LightSide
{
    public abstract partial class GpuAtlas<TEntry> where TEntry : struct, IGpuAtlasEntry
    {
        /// <summary>
        /// Adds a pixel tile to the atlas. <paramref name="template"/> carries the consumer payload;
        /// placement is written into it before insertion. Takes ownership of the pixel buffer —
        /// it is returned to ArrayPool&lt;byte&gt; after FlushPending copies the data, so the caller
        /// must provide a pooled buffer (ArrayPool&lt;byte&gt;.Rent). Pixels outside the tile's content
        /// area are clipped.
        /// </summary>
        protected TEntry EnsureTilePixels(long key, in TEntry template,
            byte[] pixels, int w, int h, bool isBGRA)
        {
            try
            {
                if (entries.TryGetValue(key, out var existing))
                    return existing;

                if (pixels == null || w == 0 || h == 0)
                    return new TEntry { EncodedTile = -1, PageIndex = -1 };

                var slot = AllocateTile(tileSizes[0]);

                pendingTiles.Add(new PendingTile
                {
                    key = key,
                    PageIndex = slot.PageIndex,
                    EncodedTile = slot.EncodedTile,
                    rgbaPixels = pixels,
                    pixelWidth = w,
                    pixelHeight = h,
                    isBGRA = isBGRA
                });
                pixels = null;

                var entry = template;
                entry.EncodedTile = slot.EncodedTile;
                entry.PageIndex = slot.PageIndex;
                entry.RefCount = 0;
                entries[key] = entry;
                pageEntryCount[slot.PageIndex]++;
                ProtectForBatch(key);

                return entry;
            }
            finally
            {
                if (pixels != null)
                    ArrayPool<byte>.Return(pixels);
            }
        }

        protected void RelocateEntry(long key, int targetTileSize, ref TEntry entry)
        {
            var oldSlot = new TileSlot
            {
                PageIndex = entry.PageIndex,
                EncodedTile = entry.EncodedTile
            };
            bool live = entry.RefCount > 0;
            UnlinkEvictable(GetSizeClassFromEncoded(entry.EncodedTile), key);
            var newSlot = AllocateTile(targetTileSize);
            deferredTileRetirements.Add(new DeferredTileRetirement
            {
                slot = oldSlot,
                live = live
            });
            pageEntryCount[newSlot.PageIndex]++;
            if (live) pageLiveCount[newSlot.PageIndex]++;
            entry.PageIndex = newSlot.PageIndex;
            entry.EncodedTile = newSlot.EncodedTile;
        }

        private void CommitDeferredTileRetirements()
        {
            if (deferredTileRetirements.Count == 0) return;
            for (int i = 0; i < deferredTileRetirements.Count; i++)
            {
                var retirement = deferredTileRetirements[i];
                int sizeClass = GetSizeClassFromEncoded(retirement.slot.EncodedTile);
                freeTiles[sizeClass].Add(retirement.slot);
                pageEntryCount[retirement.slot.PageIndex]--;
                if (retirement.live)
                    pageLiveCount[retirement.slot.PageIndex]--;
            }
            deferredTileRetirements.Clear();
            CleanupEmptyPages();
        }

        /// <summary>
        /// Commits the pending batch through the one delivery path, ENTIRELY within the current frame
        /// (owner axiom: state is never deferred — a heavy frame is long, never stale or half-shown).
        /// Backend backpressure is resolved by deterministic GPU drains inside the flush, never by
        /// yielding to the next frame. <c>false</c> means an inert condition where nothing was
        /// delivered at all — delivery still becoming available, the slice ceiling exceeded, or a
        /// non-terminal failure before any byte crossed the delivery boundary — so the consumer
        /// withholds the batch's presentation, keeps the pending work queued, and the previous
        /// picture remains coherent. A failure after delivery may have begun wipes the affected
        /// transaction fail-closed; terminal failures additionally propagate an exact exception.
        /// </summary>
        public bool FlushPending()
        {
            if (pixelTiles ? pendingTiles.Count == 0 : !HasConsumerPendingWork)
                return true;
            var transaction = new FlushTransaction { recoveryVersion = recoveryVersion };
            try
            {
                if (!MaterializeAtlasCapacity())
                {
                    flushYields++;
                    return false;
                }
                if (!EnsureGpuUploadDelivery()) return NoteDeliveryPending();
                if (!EnsureGpuUploadTarget())
                {
                    flushYields++;
                    return false;
                }

                if (!(pixelTiles
                        ? FlushTilePixels(ref transaction)
                        : FlushPendingWork(ref transaction)))
                    return RejectPendingFlush(in transaction);
                PublishCommittedTexture();
                return true;
            }
            catch (Exception failure)
            {
                try
                {
                    RecoverAfterFlushFailure(in transaction);
                }
                catch (Exception recoveryFailure)
                {
                    throw new AggregateException(
                        $"[{Label}] GPU upload and recovery both failed.",
                        failure, recoveryFailure);
                }
                throw;
            }
        }

        /// <summary>Consumer flush hook for non-pixel atlases: rasterize and deliver the consumer's own pending work inside the open flush transaction. Base implementation is a successful no-op.</summary>
        protected virtual bool FlushPendingWork(ref FlushTransaction transaction) => true;

        /// <summary>Consumer hook: true while the consumer holds pending work of its own (gates flush emptiness and shrink).</summary>
        protected virtual bool HasConsumerPendingWork => false;

        /// <summary>Consumer hook: the whole index was wiped — clear consumer-owned pending work.</summary>
        protected virtual void OnAtlasStateCleared() { }

        /// <summary>Consumer hook: compaction relocated every live entry (index already rewritten, pixels blitted). Refresh consumer-side placement snapshots and flush placement tables.</summary>
        protected virtual void OnCompactionRelocated() { }

        private bool RejectPendingFlush(in FlushTransaction transaction)
        {
            var failure = transaction.failure == GpuUploadError.None
                ? GpuUploadError.InternalError
                : transaction.failure;
            RecordGpuUploadError(failure);
            var terminal = IsTerminalGpuUploadError(failure);
            if (!terminal && !transaction.atlasWriteMayHaveStarted)
            {
                flushYields++;
                return false;
            }
            var uploadFailure = new InvalidOperationException(
                $"[{Label}] GPU upload failed ({GpuUpload.Describe(failure)}).");
            try
            {
                RecoverAfterFlushFailure(in transaction);
            }
            catch (Exception recoveryFailure)
            {
                throw new AggregateException(
                    $"[{Label}] GPU upload and recovery both failed.",
                    uploadFailure, recoveryFailure);
            }
            if (terminal) throw uploadFailure;
            return false;
        }

        private void RecoverAfterFlushFailure(in FlushTransaction transaction)
        {
            if (transaction.recoveryVersion != recoveryVersion) return;
            bool publishedStorageIsDetached = deferredRetirementTexture != null
                                              && ReferenceEquals(deferredRetirementTexture,
                                                  publishedAtlasTexture)
                                              && !ReferenceEquals(atlasRT, publishedAtlasTexture);
            bool preservePublished = publishedStorageIsDetached
                                     || !transaction.atlasWriteMayHaveStarted;
            Exception preservationFailure = null;
            if (preservePublished && !publishedStorageIsDetached)
            {
                try
                {
                    PreservePublishedStorageForRecovery();
                }
                catch (Exception exception)
                {
                    preservationFailure = exception;
                }
            }
            try
            {
                InvalidateAllContent(transaction.graphicsStorageLost, preservePublished);
            }
            catch (Exception invalidationFailure)
            {
                if (preservationFailure == null) throw;
                throw new AggregateException(
                    $"[{Label}] Both GPU recovery operations failed.",
                    preservationFailure, invalidationFailure);
            }
            if (preservationFailure != null)
                ExceptionDispatchInfo.Capture(preservationFailure).Throw();
        }

        private bool NoteDeliveryPending()
        {
            flushYields++;
            if (gpuUploadDeliveryError != GpuUploadError.None
                && !IsTransientGpuUploadConfigurationError(gpuUploadDeliveryError)
                && !IsGraphicsRecoveryError(gpuUploadDeliveryError))
            {
                throw new InvalidOperationException(
                    $"[{Label}] GPU delivery is unavailable ({GpuUpload.Describe(gpuUploadDeliveryError)}).");
            }
            return false;
        }

        private void PublishCommittedTexture()
        {
            if (publicationRequiresPresentationCommit) return;
            var current = atlasRT;
            if (current == null || ReferenceEquals(publishedAtlasTexture, current)) return;
            publishedAtlasTexture = current;
            try
            {
                NotifyAtlasTextureChanged(current);
            }
            finally
            {
                CompleteDeferredTextureRetirement();
            }
        }

        public void CommitPresentationAfterPublication()
        {
            bool delayedPublication = publicationRequiresPresentationCommit;
            publicationRequiresPresentationCommit = false;
            if (delayedPublication && atlasRT == null
                                   && ReferenceEquals(deferredRetirementTexture,
                                       publishedAtlasTexture))
            {
                publishedAtlasTexture = null;
                try
                {
                    NotifyAtlasTextureChanged(null);
                }
                finally
                {
                    CompleteDeferredTextureRetirement();
                }
            }
            else
            {
                PublishCommittedTexture();
            }
            CommitDeferredTileRetirements();
            ReleaseBatchProtection();
            
            if (reclaimPending && !allocatedThisFrame)
            {
                reclaimPending = false;
                RecycleDeadPages();
                Compact();
                TryShrinkAtlas();
                if (gpuUploadTicketCount != 0 || deferredTileRetirements.Count != 0
                                              || publicationRequiresPresentationCommit
                                              || deferredRetirementTexture != null)
                    reclaimPending = true;
            }
        }

        protected static void DisposeNative<T>(ref NativeArray<T> value) where T : struct
        {
            if (!value.IsCreated) return;
            value.Dispose();
            value = default;
        }

        protected bool SubmitUploadBatch(ref GpuUploadBatch uploadBatch, ref GpuUploadSlot slot,
            int writtenBytes, bool valid, GpuUploadError uploadError,
            ref FlushTransaction transaction)
        {
            GpuUploadSubmitResult submit = default;
            if (valid)
            {
                bool writeWasAlreadyPossible = transaction.atlasWriteMayHaveStarted;
                transaction.atlasWriteMayHaveStarted = true;
                submit = uploadBatch.Submit(ref slot, writtenBytes);
                if (submit.ContentState == GpuUploadContentState.Unchanged)
                    transaction.atlasWriteMayHaveStarted = writeWasAlreadyPossible;
                uploadError = submit.Error;
            }
            if (submit.Succeeded)
            {
                if (!TrackGpuUploadTicket(submit.Ticket))
                {
                    RecordFlushGpuUploadError(GpuUploadError.InternalError, ref transaction);
                    return false;
                }
                gpuUploadBatches++;
                return true;
            }
            RecordFlushGpuUploadError(uploadError == GpuUploadError.None
                ? GpuUploadError.BackendFailed
                : uploadError, ref transaction);
            return false;
        }

        /// <summary>
        /// Rasterizes each tile (mip chain included) into the persistent cached tile scratch and
        /// copies the finished tile into the acquired upload slot. The mip downsample reads its own
        /// output, and mapped slot memory may be write-combined, so raster never targets the slot
        /// directly.
        /// </summary>
        private unsafe bool FlushTilePixels(ref FlushTransaction transaction)
        {
            var timer = new DebugTimer();
            timer.Mark();

            int count = pendingTiles.Count;
            int tileSize = tileSizes[0];
            int maxContent = tileSize - 2 * tileGutter;
            int tileMips = TileMipCount;
            long tileBytes = TileBytes(tileSize, tileMips);
            bool ok = true;
            int flushed = 0;

            if (!tilePixelScratch.IsCreated)
                tilePixelScratch = new NativeArray<byte>(checked((int)tileBytes),
                    Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            while (flushed < count)
            {
                if (!TryPlanTilePixelsChunk(flushed, count, tileSize, tileMips, tileBytes,
                        out int tiles, out int sourceBytes, out var uploadError))
                {
                    RecordFlushGpuUploadError(uploadError, ref transaction);
                    ok = false;
                    break;
                }
                if (!EnsureUploadTicketSlot(out uploadError))
                {
                    RecordFlushGpuUploadError(uploadError, ref transaction);
                    ok = false;
                    break;
                }
                if (!AcquireUploadSlot(sourceBytes, out var slot, out uploadError))
                {
                    RecordFlushGpuUploadError(uploadError, ref transaction);
                    ok = false;
                    break;
                }

                var uploadBatch = default(GpuUploadBatch);
                try
                {
                    byte* scratch = (byte*)tilePixelScratch.GetUnsafePtr();
                    byte* destination = (byte*)slot.View.GetUnsafePtr();
                    long offset = 0;
                    for (int i = flushed; i < flushed + tiles; i++)
                    {
                        WriteTilePixels((uint*)scratch, pendingTiles[i],
                            tileSize, maxContent, tileGutter);
                        DownsampleTileMips(scratch, tileSize, tileMips);
                        UnsafeUtility.MemCpy(destination + offset, scratch, tileBytes);
                        offset += tileBytes;
                    }

                    if (!BeginUploadBatch(out uploadBatch, out uploadError))
                    {
                        RecordFlushGpuUploadError(uploadError, ref transaction);
                        ok = false;
                        break;
                    }
                    bool valid = true;
                    offset = 0;
                    int regions = 0;
                    for (int i = flushed; valid && i < flushed + tiles; i++)
                    {
                        var tile = pendingTiles[i];
                        DecodeTileXY(tile.EncodedTile, tileSize, out int tileX, out int tileY);
                        for (int mip = 0; valid && mip < tileMips; mip++)
                        {
                            int size = Math.Max(1, tileSize >> mip);
                            int bytes = size * size * 4;
                            var region = GpuUploadRegion.ForLayers(mip, tile.PageIndex, 1,
                                tileX >> mip, tileY >> mip, size, size,
                                offset, size * 4, bytes);
                            valid = uploadBatch.TryAddRegion(gpuUploadTarget,
                                region, out uploadError);
                            offset += bytes;
                            regions++;
                        }
                    }
                    if (!SubmitUploadBatch(ref uploadBatch, ref slot, sourceBytes,
                            valid, uploadError, ref transaction))
                    {
                        ok = false;
                        break;
                    }
                    uploadedRegions += regions;
                    uploadedBytes += tiles * tileBytes;
                    for (int i = flushed; i < flushed + tiles; i++)
                    {
                        var pg = pendingTiles[i];
                        if (pg.rgbaPixels != null)
                        {
                            ArrayPool<byte>.Return(pg.rgbaPixels);
                            pg.rgbaPixels = null;
                            pendingTiles[i] = pg;
                        }
                    }
                    flushed += tiles;
                }
                finally
                {
                    uploadBatch.Dispose();
                    GpuUpload.ReleaseSlot(ref slot);
                }
            }

            if (ok)
            {
                timer.Mark();
                logZone.Meow($"[{Label}] Flushed {count} tiles, pages:{sliceCount} | " +
                         $"stage+upload={timer.Phase(0):F1}ms total={timer.Total:F1}ms");
                pendingTiles.Clear();
                ClearStreamIndex();
            }
            return ok;
        }

        /// <summary>Accumulates tiles while the chunk fits the standard slot capacity, the per-batch region cap, and the backend staging bound. A first tile larger than the standard slot forms its own chunk — an oversized acquisition is served by a transient slot.</summary>
        private bool TryPlanTilePixelsChunk(int start, int count, int tileSize, int tileMips,
            long tileBytes, out int tiles, out int sourceBytes, out GpuUploadError error)
        {
            tiles = 0;
            sourceBytes = 0;
            ulong stagingBytes = 0;
            int regions = 0;
            int maxRegions = GpuUpload.MaxRegionsPerBatch;
            ulong maxStaging = GpuUpload.Info.MaxStagingBytes;
            while (start + tiles < count && regions + tileMips <= maxRegions)
            {
                var tile = pendingTiles[start + tiles];
                DecodeTileXY(tile.EncodedTile, tileSize, out int tileX, out int tileY);
                ulong candidate = stagingBytes;
                long offset = sourceBytes;
                for (int mip = 0; mip < tileMips; mip++)
                {
                    int size = Math.Max(1, tileSize >> mip);
                    int bytes = checked(size * size * 4);
                    var region = GpuUploadRegion.ForLayers(mip, tile.PageIndex, 1,
                        tileX >> mip, tileY >> mip, size, size,
                        offset, size * 4, bytes);
                    if (!GpuUpload.TryAccumulateStagingBytes(gpuUploadTarget, region,
                            ref candidate, out error))
                        return false;
                    offset += bytes;
                }
                if (candidate > maxStaging) break;
                stagingBytes = candidate;
                sourceBytes = checked(sourceBytes + (int)tileBytes);
                regions += tileMips;
                tiles++;
            }
            if (tiles == 0)
            {
                error = GpuUploadError.BackendFailed;
                return false;
            }
            error = GpuUploadError.None;
            return true;
        }

        private static unsafe void WriteTilePixels(uint* dst, in PendingTile pg,
            int tileSize, int maxContent, int gutter)
        {
            UnsafeUtility.MemClear(dst, (long)tileSize * tileSize * 4);

            int copyW = Math.Min(pg.pixelWidth, maxContent);
            int copyH = Math.Min(pg.pixelHeight, maxContent);
            fixed (byte* src = pg.rgbaPixels)
            {
                for (int y = 0; y < copyH; y++)
                {
                    uint* srcRow = (uint*)(src + (long)(pg.pixelHeight - 1 - y) * pg.pixelWidth * 4);
                    uint* dstRow = dst + (long)(gutter + y) * tileSize + gutter;
                    if (pg.isBGRA)
                    {
                        for (int x = 0; x < copyW; x++)
                        {
                            uint px = srcRow[x];
                            uint rb = ((px & 0x00FF0000u) >> 16) | ((px & 0x000000FFu) << 16);
                            dstRow[x] = (px & 0xFF00FF00u) | rb;
                        }
                    }
                    else
                        UnsafeUtility.MemCpy(dstRow, srcRow, (long)copyW * 4);
                }
            }
        }

        /// <summary>Generates the tile's truncated mip chain in place after mip 0 (2×2 box average in storage space — matching Unity's own CPU mip generation), so color tiles upload without any staging texture. <see cref="TileMipCount"/> guarantees every level divides exactly.</summary>
        private static unsafe void DownsampleTileMips(byte* tile, int tileSize, int mipCount)
        {
            byte* src = tile;
            int srcSize = tileSize;
            for (int m = 1; m < mipCount; m++)
            {
                byte* dst = src + (long)srcSize * srcSize * 4;
                int dstSize = srcSize >> 1;
                for (int y = 0; y < dstSize; y++)
                {
                    byte* r0 = src + (long)(y * 2) * srcSize * 4;
                    byte* r1 = r0 + (long)srcSize * 4;
                    byte* d = dst + (long)y * dstSize * 4;
                    for (int x = 0; x < dstSize; x++)
                    {
                        int o = x * 8;
                        int p = x * 4;
                        d[p] = (byte)((r0[o] + r0[o + 4] + r1[o] + r1[o + 4] + 2) >> 2);
                        d[p + 1] = (byte)((r0[o + 1] + r0[o + 5] + r1[o + 1] + r1[o + 5] + 2) >> 2);
                        d[p + 2] = (byte)((r0[o + 2] + r0[o + 6] + r1[o + 2] + r1[o + 6] + 2) >> 2);
                        d[p + 3] = (byte)((r0[o + 3] + r0[o + 7] + r1[o + 3] + r1[o + 7] + 2) >> 2);
                    }
                }
                src = dst;
                srcSize = dstSize;
            }
        }

        private static long TileBytes(int tileSize, int mipCount)
        {
            long bytes = 0;
            for (int mip = 0; mip < mipCount; mip++)
            {
                int size = Math.Max(1, tileSize >> mip);
                bytes = checked(bytes + (long)size * size * 4);
            }
            return bytes;
        }

        private void ReturnPendingTileBuffers()
        {
            for (int i = 0; i < pendingTiles.Count; i++)
            {
                if (pendingTiles[i].rgbaPixels != null)
                    ArrayPool<byte>.Return(pendingTiles[i].rgbaPixels);
            }
            pendingTiles.Clear();
            ClearStreamIndex();
        }

        private static GpuUploadBatchOptions UploadBatchOptions
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return GpuUploadBatchOptions.ObserveSharedGlErrors;
#else
                return GpuUploadBatchOptions.None;
#endif
            }
        }

    }
}
