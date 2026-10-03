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
        /// <summary>
        /// Second phase of atlas allocation: tile reservation (<see cref="GrowAtlas"/>) only advances
        /// bookkeeping, so a batch reserves all its pages virtually and storage is (re)created here exactly
        /// once per flush, sized by EXACT demand (owner ruling: no headroom, no doubling — growth is a rare
        /// event, memory precision outranks recreation amortization, and slice-blit migration makes growth
        /// wipe-free). When growth is needed and packing live tiles would free enough pages, compaction runs
        /// instead. Returns false only in the logged-once inert ceiling-overflow state — the pending batch
        /// stays queued until live demand drops; the real escape is consumer-level tile classification.
        /// </summary>
        private bool MaterializeAtlasCapacity()
        {
            if (sliceCount == 0) return true;
            if (atlasRT != null && atlasRT.volumeDepth >= sliceCount) return true;
            EnsureLowMemorySubscription();
            Compact();
            if (atlasRT != null && atlasRT.volumeDepth >= sliceCount)
            {
                ceilingOverflowLogged = false;
                return true;
            }
            int ceiling = GpuAtlasShared.TextureArraySliceLimit;
            if (sliceCount > ceiling)
            {
                if (!ceilingOverflowLogged)
                {
                    ceilingOverflowLogged = true;
                    logZone.MeowError(
                        $"[{Label}] Demand of {sliceCount} pages exceeds the device slice ceiling ({ceiling}); pending tiles stay queued until live demand drops. Reduce unique tile pressure at the source.");
                }
                return false;
            }
            ceilingOverflowLogged = false;
            logZone.MeowFormat("[{0}] Materializing storage: {1}→{2} pages",
                Label, atlasRT != null ? atlasRT.volumeDepth : 0, sliceCount);
            ReallocateStorage(sliceCount);
            if (maxLodShaderId != -1)
                Shader.SetGlobalFloat(maxLodShaderId, TileMipCount - 1);
            if (!storageDescriptionLogged)
            {
                storageDescriptionLogged = true;
                logZone.MeowFormat(
                    "[{0}] Storage: RenderTexture array {1}, mips={2} (realized {3}, hasMipMaxLevel={4}), delivery: GpuUpload regions",
                    Label, atlasRT.graphicsFormat, hasMipmaps ? TileMipCount : 1,
                    atlasRT.mipmapCount, SystemInfo.hasMipMaxLevel);
            }
            return true;
        }

        /// <summary>
        /// The one texture-(re)allocation path — grow and shrink. Content migrates by whole-slice blits
        /// old→new (the same primitive compaction uses for tile rects); the superseded generation is
        /// retired after GPU-use completion, deferred when it is the published texture so publication
        /// stays atomic at <see cref="PublishCommittedTexture"/>. Creation failure throws — on the 2021
        /// floor a texture-allocation failure has no catchable recovery, and pretending otherwise is
        /// try/catch theater.
        /// </summary>
        private void ReallocateStorage(int newDepth)
        {
            var newRT = CreateAtlasTexture(newDepth);
            var oldRT = atlasRT;
            if (oldRT != null)
            {
                try
                {
                    BlitSlices(oldRT, newRT, Math.Min(Math.Min(sliceCount, oldRT.volumeDepth), newDepth));
                }
                catch
                {
                    ReleaseTexture(newRT);
                    throw;
                }
            }
            atlasRT = newRT;
            var oldTarget = gpuUploadTarget;
            gpuUploadTarget = null;
            if (oldRT != null)
            {
                if (ReferenceEquals(oldRT, publishedAtlasTexture))
                    DeferTextureRetirement(oldRT, oldTarget);
                else
                    RetireTexture(oldRT, oldTarget);
            }
            else oldTarget?.Close();
        }

        private RenderTexture CreateAtlasTexture(int depth)
        {
            var format = GraphicsFormatUtility.GetGraphicsFormat(textureFormat, !isLinear);
            int mipCount = hasMipmaps ? TileMipCount : 1;
            var descriptor = new RenderTextureDescriptor(PageSize, PageSize, format, 0, mipCount)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = depth,
                useMipMap = mipCount > 1,
                autoGenerateMips = false
            };
            var rt = new RenderTexture(descriptor)
            {
                name = $"LsAtlas_{Label}_{textureFormat}",
                filterMode = atlasFilterMode,
                wrapMode = TextureWrapMode.Clamp,
                mipMapBias = atlasMipMapBias,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!rt.Create() || rt.graphicsFormat != format || rt.volumeDepth != depth
                || rt.mipmapCount != mipCount)
            {
                string realized = $"{rt.graphicsFormat}, {rt.volumeDepth} layers, {rt.mipmapCount} mips";
                ReleaseTexture(rt);
                throw new InvalidOperationException(
                    $"[{Label}] Atlas storage realization failed: requested {format}, {depth} layers, {mipCount} mips; realized {realized}.");
            }
            return rt;
        }

        /// <summary>Color tile mip chain length, clamped at the last level where 1&lt;&lt;mip divides the tile size exactly: per-mip tile regions (origin&gt;&gt;m, size&gt;&gt;m) tile the plane only while the division is exact — a misaligned level would leave never-written texels at tile boundaries for bilinear to tap (e.g. a 136px tile → 4 mips: 136/68/34/17). 1 when <see cref="SystemInfo.hasMipMaxLevel"/> is false: Unity cannot clamp sampling to a user mip count there, so the never-written deep levels of the realized chain would be sampled.</summary>
        private int TileMipCount
        {
            get
            {
                if (!hasMipmaps) return 1;
                int size = tileSizes[0];
                int mips = 1;
                while ((1 << mips) <= size && size % (1 << mips) == 0) mips++;
                return mips;
            }
        }

        /// <summary>Copies whole slices (every mip) old→new during storage migration. Aligned texel-center sampling at explicit LOD is an exact copy; sRGB round-trips through hardware decode/encode.</summary>
        private void BlitSlices(RenderTexture source, RenderTexture destination, int slices)
        {
            if (slices <= 0) return;
            var material = GpuAtlasShared.AtlasBlitMaterial;
            material.SetTexture(GpuAtlasShared.BlitSourceId, source);
            var previousActive = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            GL.sRGBWrite = !isLinear;
            try
            {
                int mips = Math.Min(source.mipmapCount, destination.mipmapCount);
                for (int slice = 0; slice < slices; slice++)
                {
                    material.SetFloat(GpuAtlasShared.BlitSliceId, slice);
                    for (int mip = 0; mip < mips; mip++)
                    {
                        material.SetFloat(GpuAtlasShared.BlitLodId, mip);
                        Graphics.SetRenderTarget(destination, mip, CubemapFace.Unknown, slice);
                        material.SetPass(0);
                        GL.PushMatrix();
                        GL.LoadOrtho();
                        DrawBlitQuad(0f, 0f, 1f, 1f, 0f, 0f, 1f, 1f);
                        GL.PopMatrix();
                    }
                }
            }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previousActive;
            }
        }

        private static void DrawBlitQuad(float dstX0, float dstY0, float dstX1, float dstY1,
            float srcU0, float srcV0, float srcU1, float srcV1)
        {
            GL.Begin(GL.QUADS);
            GL.TexCoord2(srcU0, srcV0);
            GL.Vertex3(dstX0, dstY0, 0f);
            GL.TexCoord2(srcU1, srcV0);
            GL.Vertex3(dstX1, dstY0, 0f);
            GL.TexCoord2(srcU1, srcV1);
            GL.Vertex3(dstX1, dstY1, 0f);
            GL.TexCoord2(srcU0, srcV1);
            GL.Vertex3(dstX0, dstY1, 0f);
            GL.End();
        }

        /// <summary>
        /// Reserves a fresh page in bookkeeping only — the texture is materialized once per
        /// flush by <see cref="MaterializeAtlasCapacity"/>, so a batch never triggers
        /// per-page reallocations.
        /// </summary>
        private TileSlot GrowAtlas(int tileSize)
        {
            int newSliceIndex = sliceCount;
            sliceCount++;

            int ci = SizeClassIndex(tileSize);
            while (pageUsedHeight.Count < sliceCount) pageUsedHeight.Add(0);
            while (pageLiveCount.Count < sliceCount) pageLiveCount.Add(0);
            while (pageEntryCount.Count < sliceCount) pageEntryCount.Add(0);
            pageUsedHeight[newSliceIndex] = tileSize;

            shelves.Add(new Shelf { PageIndex = newSliceIndex, y = 0, height = tileSize, nextX = tileSize });

            return new TileSlot
            {
                PageIndex = newSliceIndex,
                EncodedTile = ci * ClassStride
            };
        }

        /// <summary>
        /// Evicts every entry on pages whose live count is zero and resets their shelves, making whole
        /// pages available to any size class. Bookkeeping only — no pixels move; pending/protected tiles
        /// hold a ref, so their pages are never dead. Runs pre-growth, on the page-went-dead trigger, and
        /// in the lowMemory sweep.
        /// </summary>
        private void RecycleDeadPages()
        {
            if (deferredTileRetirements.Count != 0) return;
            bool anyDead = false;
            for (int p = 0; p < sliceCount; p++)
            {
                if (pageLiveCount[p] == 0 && pageEntryCount[p] > 0)
                {
                    anyDead = true;
                    break;
                }
            }
            if (!anyDead) return;

            var keysToRemove = removeScratchKeys ??= new List<long>();
            keysToRemove.Clear();
            foreach (var kvp in entries)
            {
                int p = kvp.Value.PageIndex;
                if (pageLiveCount[p] == 0 && pageEntryCount[p] > 0 && kvp.Value.RefCount == 0)
                    keysToRemove.Add(kvp.Key);
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                var key = keysToRemove[i];
                var e = entries[key];
                entries.Remove(key);
                OnEntryDropped(in e);
                pageEntryCount[e.PageIndex]--;

                int ci = GetSizeClassFromEncoded(e.EncodedTile);
                UnlinkEvictable(ci, key);
            }

            if (keysToRemove.Count > 0)
                logZone.MeowFormat("[{0}] RecycleDeadPages: evicted {1} dead entries, remainingEntries={2}",
                    Label, keysToRemove.Count, entries.Count);

            CleanupEmptyPages();
        }

        private void CleanupEmptyPages()
        {
            for (int p = 0; p < sliceCount; p++)
            {
                if (pageEntryCount[p] > 0 || pageUsedHeight[p] == 0) continue;

                for (int i = shelves.Count - 1; i >= 0; i--)
                {
                    if (shelves[i].PageIndex == p)
                    {
                        shelves[i] = shelves[^1];
                        shelves.RemoveAt(shelves.Count - 1);
                    }
                }

                for (int ci = 0; ci < numSizeClasses; ci++)
                {
                    var list = freeTiles[ci];
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (list[i].PageIndex == p)
                        {
                            list[i] = list[^1];
                            list.RemoveAt(list.Count - 1);
                        }
                    }
                }

                pageUsedHeight[p] = 0;
            }
        }

        /// <summary>Trims trailing empty pages, destroys storage at zero demand, and halves over-allocated depth through the blit reallocation path. Optional operation: failures retain the current texture.</summary>
        private bool TryShrinkAtlas()
        {
            if (gpuUploadTicketCount != 0 || publicationRequiresPresentationCommit
                                          || deferredRetirementTexture != null
                                          || deferredTileRetirements.Count != 0
                                          || HasConsumerPendingWork || pendingTiles.Count != 0)
                return false;

            int sliceCountBefore = sliceCount;
            while (sliceCount > 0)
            {
                int last = sliceCount - 1;
                if (pageEntryCount[last] > 0 || pageUsedHeight[last] > 0) break;
                sliceCount--;
            }

            if (sliceCount < sliceCountBefore)
                logZone.MeowFormat("[" + Label + "] TryShrinkAtlas: trimmed trailing empty pages {0}→{1}",
                    sliceCountBefore, sliceCount);

            if (sliceCount == 0)
            {
                if (atlasRT != null && CanObserveTextureRetirement(atlasRT))
                {
                    try
                    {
                        logZone.MeowFormat("[" + Label + "] TryShrinkAtlas: RETIRING atlas storage (0 active pages, entries={0})",
                            entries.Count);
                        var oldRT = atlasRT;
                        var oldTarget = gpuUploadTarget;
                        atlasRT = null;
                        gpuUploadTarget = null;
                        if (ReferenceEquals(oldRT, publishedAtlasTexture))
                        {
                            DeferTextureRetirement(oldRT, oldTarget);
                            publicationRequiresPresentationCommit = true;
                        }
                        else
                        {
                            RetireTexture(oldRT, oldTarget);
                            publishedAtlasTexture = null;
                            NotifyAtlasTextureChanged(null);
                        }
                        return true;
                    }
                    catch (Exception exception)
                    {
                        logZone.MeowWarnFormat(
                            "[{0}] Optional empty-atlas release failed ({1})",
                            Label, exception.GetType().Name);
                        return atlasRT == null;
                    }
                }
                return false;
            }

            if (atlasRT != null && atlasRT.volumeDepth > 1 && sliceCount <= atlasRT.volumeDepth / 2)
            {
                if (!CanObserveTextureRetirement(atlasRT)) return false;
                logZone.MeowFormat("[" + Label + "] TryShrinkAtlas: SHRINKING atlas depth {0}→{1}, activePages={2}, entries={3}",
                    atlasRT.volumeDepth, sliceCount, sliceCount, entries.Count);

                try
                {
                    ReallocateStorage(sliceCount);
                    PublishCommittedTexture();
                    return true;
                }
                catch (Exception exception)
                {
                    logZone.MeowWarnFormat(
                        "[{0}] Optional atlas shrink failed ({1}); retaining the current texture",
                        Label, exception.GetType().Name);
                }
            }
            return false;
        }

        private void DestroyAtlasTexture(bool releaseImmediately = false)
        {
            if (atlasRT != null)
            {
                var uploadTarget = gpuUploadTarget;
                gpuUploadTarget = null;
                RetireOrReleaseTexture(atlasRT, releaseImmediately, uploadTarget);
                atlasRT = null;
            }
            else if (gpuUploadTarget != null)
            {
                gpuUploadTarget.Close();
                gpuUploadTarget = null;
            }
        }

        private void RetireOrReleaseTexture(Texture texture, bool releaseImmediately,
            GpuUploadTarget uploadTarget = null)
        {
            if (releaseImmediately)
            {
                uploadTarget?.Close();
                if (uploadTarget == null || uploadTarget.IsReleased)
                {
                    ReleaseTexture(texture);
                    return;
                }
            }
            RetireTexture(texture, uploadTarget);
        }

        /// <summary>
        /// Re-packs live tiles into a fresh dense storage generation via tile blits (the migration
        /// primitive). Entries keep their stable handles; the transform table is flushed here so the
        /// texture swap publishes atomically with the relocated rows — meshes are untouched. Pending
        /// rasters are retargeted to the relocated slots (their entries are live via pixel protection).
        /// The color atlas never compacts: its quads carry atlas rects directly, and single-class pages
        /// reclaim fully through recycling. Triggers are event-driven: pre-growth (compact instead of
        /// grow when packing frees enough pages — the exact-arithmetic filter below) and the lowMemory
        /// sweep. Failures retain the old texture and rebuild the index from consumer sources.
        /// </summary>
        private bool Compact()
        {
            if (pixelTiles || atlasRT == null || sliceCount < 2) return false;
            if (gpuUploadTicketCount != 0 || publicationRequiresPresentationCommit
                                          || deferredRetirementTexture != null
                                          || deferredTileRetirements.Count != 0)
                return false;
            if (!CanObserveTextureRetirement(atlasRT)) return false;

            long liveArea = 0;
            int liveCount = 0;
            foreach (var kvp in entries)
            {
                if (kvp.Value.RefCount <= 0) continue;
                long tileSize = tileSizes[GetSizeClassFromEncoded(kvp.Value.EncodedTile)];
                liveArea += tileSize * tileSize;
                liveCount++;
            }
            if (liveCount == 0) return false;
            const long pageArea = (long)PageSize * PageSize;
            int floorPages = (int)((liveArea + pageArea - 1) / pageArea);
            if (floorPages >= sliceCount) return false;

            var compList = compactList ??= new List<CompactRecord>();
            compList.Clear();
            foreach (var kvp in entries)
            {
                if (kvp.Value.RefCount > 0)
                {
                    compList.Add(new CompactRecord
                    {
                        key = kvp.Key,
                        oldPage = kvp.Value.PageIndex,
                        oldEncodedTile = kvp.Value.EncodedTile,
                        entry = kvp.Value
                    });
                }
                else
                {
                    var dead = kvp.Value;
                    OnEntryDropped(in dead);
                }
            }

            int oldSliceCount = sliceCount;
            var oldRT = atlasRT;
            var oldTarget = gpuUploadTarget;

            logZone.MeowFormat("[" + Label + "] Compact: RELOCATING {0} live entries, slices={1}",
                compList.Count, sliceCount);

            try
            {
                ResetIndexState();
                sliceCount = 0;
                atlasRT = null;
                gpuUploadTarget = null;

                for (int i = 0; i < compList.Count; i++)
                {
                    var rec = compList[i];
                    int ci = GetSizeClassFromEncoded(rec.oldEncodedTile);
                    int tileSize = tileSizes[ci];

                    var newSlot = AllocateTile(tileSize);

                    var relocated = rec.entry;
                    relocated.EncodedTile = newSlot.EncodedTile;
                    relocated.PageIndex = newSlot.PageIndex;
                    entries[rec.key] = relocated;
                    pageEntryCount[newSlot.PageIndex]++;
                    pageLiveCount[newSlot.PageIndex]++;
                    OnTilePlaced(rec.key, in relocated);

                    rec.newPage = newSlot.PageIndex;
                    rec.newEncodedTile = newSlot.EncodedTile;
                    compList[i] = rec;
                }

                atlasRT = CreateAtlasTexture(sliceCount);
                BlitCompactedTiles(oldRT, atlasRT);
            }
            catch (Exception exception)
            {
                var failedRT = atlasRT;
                atlasRT = oldRT;
                gpuUploadTarget = oldTarget;
                if (failedRT != null && !ReferenceEquals(failedRT, oldRT))
                    RetireTexture(failedRT);
                RecoverAfterFailedMutation(exception);
                logZone.MeowWarnFormat(
                    "[{0}] Optional compaction failed ({1}); retaining the previous texture and rebuilding the index",
                    Label, exception.GetType().Name);
                return false;
            }

            if (ReferenceEquals(oldRT, publishedAtlasTexture))
                DeferTextureRetirement(oldRT, oldTarget);
            else
                RetireTexture(oldRT, oldTarget);

            OnCompactionRelocated();
            PublishCommittedTexture();

            logZone.MeowFormat("[" + Label + "] Compact: done — slices {0}→{1}",
                oldSliceCount, sliceCount);

            NotifyAtlasCompacted(this);
            return true;
        }

        /// <summary>Moves each live tile old→new at mip 0 via the blit primitive. Mip-less atlases only — mip-window (pixel-tile) atlases do not compact.</summary>
        private void BlitCompactedTiles(RenderTexture source, RenderTexture destination)
        {
            var material = GpuAtlasShared.AtlasBlitMaterial;
            material.SetTexture(GpuAtlasShared.BlitSourceId, source);
            material.SetFloat(GpuAtlasShared.BlitLodId, 0f);
            var previousActive = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            GL.sRGBWrite = !isLinear;
            try
            {
                const float invPage = 1f / PageSize;
                int currentDstPage = -1;
                int currentSrcPage = -1;
                for (int i = 0; i < compactList.Count; i++)
                {
                    var rec = compactList[i];
                    int tileSize = tileSizes[GetSizeClassFromEncoded(rec.oldEncodedTile)];
                    DecodeTileXY(rec.oldEncodedTile, tileSize, out int srcX, out int srcY);
                    DecodeTileXY(rec.newEncodedTile, tileSize, out int dstX, out int dstY);

                    if (rec.newPage != currentDstPage)
                    {
                        Graphics.SetRenderTarget(destination, 0, CubemapFace.Unknown, rec.newPage);
                        currentDstPage = rec.newPage;
                        currentSrcPage = -1;
                    }
                    if (rec.oldPage != currentSrcPage)
                    {
                        material.SetFloat(GpuAtlasShared.BlitSliceId, rec.oldPage);
                        material.SetPass(0);
                        currentSrcPage = rec.oldPage;
                    }
                    GL.PushMatrix();
                    GL.LoadOrtho();
                    DrawBlitQuad(dstX * invPage, dstY * invPage,
                        (dstX + tileSize) * invPage, (dstY + tileSize) * invPage,
                        srcX * invPage, srcY * invPage,
                        (srcX + tileSize) * invPage, (srcY + tileSize) * invPage);
                    GL.PopMatrix();
                }
            }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previousActive;
            }
        }

        private struct CompactRecord
        {
            public long key;
            public int oldPage, oldEncodedTile;
            public int newPage, newEncodedTile;
            public TEntry entry;
        }

        private static List<CompactRecord> compactList;

        private void DeferTextureRetirement(Texture texture,
            GpuUploadTarget uploadTarget = null)
        {
            if (texture == null)
            {
                uploadTarget?.Close();
                return;
            }
            if (deferredRetirementTexture != null
                && !ReferenceEquals(deferredRetirementTexture, texture))
                throw new InvalidOperationException("A texture replacement is already awaiting publication.");
            deferredRetirementTexture = texture;
            deferredRetirementTarget = uploadTarget;
        }

        private void PreservePublishedStorageForRecovery()
        {
            if (gpuUploadTicketCount != 0) return;
            if (atlasRT == null || publishedAtlasTexture == null
                                || !ReferenceEquals(atlasRT, publishedAtlasTexture))
                return;
            DeferTextureRetirement(atlasRT, gpuUploadTarget);
        }

        private void CompleteDeferredTextureRetirement(bool releaseImmediately = false)
        {
            var texture = deferredRetirementTexture;
            var uploadTarget = deferredRetirementTarget;
            deferredRetirementTexture = null;
            deferredRetirementTarget = null;
            if (texture != null)
                RetireOrReleaseTexture(texture, releaseImmediately, uploadTarget);
            else uploadTarget?.Close();
        }

        private static bool CanObserveTextureRetirement(Texture texture)
        {
            if (texture == null) return false;
            return !GpuAtlasShared.retirementReadbackPollingUnavailable
                   && SystemInfo.supportsAsyncGPUReadback
                   && SupportsRetirementReadback(texture);
        }

        private void RetireTexture(Texture texture, GpuUploadTarget uploadTarget = null)
        {
            if (texture == null) return;
            uploadTarget?.Close();
            var retired = new RetiredTexture { texture = texture, uploadTarget = uploadTarget };
            retired.hasReadback = TryCreateRetirementReadback(texture, out retired.readback);
            retiredTextures.Add(retired);
            if ((retired.hasReadback || uploadTarget != null && !uploadTarget.IsReleased)
                && !retiredTexturePolling)
            {
                Application.onBeforeRender += PollRetiredTextures;
                retiredTexturePolling = true;
            }
            if (!retired.hasReadback && !retirementCompletionUnavailableLogged)
            {
                retirementCompletionUnavailableLogged = true;
                logZone.MeowWarnFormat("[{0}] GPU retirement completion unavailable; retired textures will be retained until device reset or Unity shutdown",
                    Label);
            }
        }

        /// <summary>
        /// Queues a minimal all-layer readback whose completion proves the GPU passed this point of
        /// the graphics timeline — every previously submitted command, including draws sampling ANY
        /// mip, has executed — so mipmapped textures are observable too; native GpuUpload use is
        /// gated separately by the target's IsReleased. Readback is the SOLE completion marker:
        /// GraphicsFence is unusable here — the fence insert is recorded into the render thread's
        /// current command buffer, which is null outside active rendering and faults the Vulkan
        /// device worker (vk::CommandBuffer::QueueEvent), and on D3D11 <c>passed</c> throws
        /// NotSupportedException despite <c>supportsGraphicsFence</c>.
        /// </summary>
        private static bool TryCreateRetirementReadback(Texture texture, out AsyncGPUReadbackRequest request)
        {
            request = default;
            if (GpuAtlasShared.retirementReadbackPollingUnavailable
                || !SystemInfo.supportsAsyncGPUReadback
                || !SupportsRetirementReadback(texture))
                return false;
            try
            {
                int depth = texture is RenderTexture rt
                    ? rt.volumeDepth
                    : texture is Texture2DArray array ? array.depth : 1;
                request = AsyncGPUReadback.Request(texture, 0, 0, 1, 0, 1, 0, Math.Max(depth, 1));
                if (!request.hasError) return true;
            }
            catch (Exception exception) when (exception is NotSupportedException
                                              || exception is InvalidOperationException
                                              || exception is ArgumentException
                                              || exception is UnityException
                                              || exception is NullReferenceException)
            {
            }
            GpuAtlasShared.retirementReadbackPollingUnavailable = true;
            return false;
        }

        private static bool SupportsRetirementReadback(Texture texture)
        {
#if UNITY_2023_1_OR_NEWER
            return SystemInfo.IsFormatSupported(texture.graphicsFormat, GraphicsFormatUsage.ReadPixels);
#else
            return SystemInfo.IsFormatSupported(texture.graphicsFormat, FormatUsage.ReadPixels);
#endif
        }

        private static void PollRetiredTextures()
        {
            bool hasPending = false;
            for (int i = retiredTextures.Count - 1; i >= 0; i--)
            {
                var retired = retiredTextures[i];
                bool uploadPending = retired.uploadTarget != null && !retired.uploadTarget.IsReleased;
                bool markerPending = false;
                bool markerPassed = retired.readbackPassed;
                if (retired.hasReadback)
                {
                    if (!retired.readback.done)
                        markerPending = true;
                    else if (retired.readback.hasError)
                        DisableRetirementReadback(ref retired, i);
                    else
                    {
                        retired.hasReadback = false;
                        retired.readbackPassed = true;
                        retiredTextures[i] = retired;
                        markerPassed = true;
                    }
                }
                if (uploadPending || markerPending)
                {
                    hasPending = true;
                    continue;
                }
                if (!markerPassed) continue;
                ReleaseTexture(retired.texture);
                retiredTextures.RemoveAt(i);
            }
            if (!hasPending && retiredTexturePolling)
            {
                Application.onBeforeRender -= PollRetiredTextures;
                retiredTexturePolling = false;
            }
        }

        private static void DisableRetirementReadback(ref RetiredTexture retired, int index)
        {
            retired.hasReadback = false;
            retiredTextures[index] = retired;
            if (GpuAtlasShared.retirementReadbackPollingUnavailable) return;
            GpuAtlasShared.retirementReadbackPollingUnavailable = true;
            staticLogZone.MeowWarn(
                "[GpuAtlas] GPU retirement readback failed; retired GPU textures will be retained until device reset or Unity shutdown");
        }

        private static void ReleaseRetiredTextures()
        {
            bool hasPendingTarget = false;
            for (int i = retiredTextures.Count - 1; i >= 0; i--)
            {
                var retired = retiredTextures[i];
                retired.uploadTarget?.Close();
                if (retired.uploadTarget != null && !retired.uploadTarget.IsReleased)
                {
                    retired.hasReadback = false;
                    retired.readbackPassed = true;
                    retiredTextures[i] = retired;
                    hasPendingTarget = true;
                    continue;
                }
                ReleaseTexture(retired.texture);
                retiredTextures.RemoveAt(i);
            }
            GpuAtlasShared.retirementReadbackPollingUnavailable = false;
            if (hasPendingTarget && !retiredTexturePolling)
            {
                Application.onBeforeRender += PollRetiredTextures;
                retiredTexturePolling = true;
            }
            else if (!hasPendingTarget && retiredTexturePolling)
            {
                Application.onBeforeRender -= PollRetiredTextures;
                retiredTexturePolling = false;
            }
        }

    }
}
