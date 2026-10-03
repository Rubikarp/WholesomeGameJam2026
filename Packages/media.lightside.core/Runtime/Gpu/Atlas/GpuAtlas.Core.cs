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
    public abstract partial class GpuAtlas<TEntry> : IGpuAtlasTextureSource where TEntry : struct, IGpuAtlasEntry
    {
        private protected readonly GpuAtlasConfig config;
        private readonly TextureFormat textureFormat;
        private readonly bool hasMipmaps;
        private readonly bool isLinear;
        private readonly FilterMode atlasFilterMode;
        private readonly float atlasMipMapBias;
        protected readonly int tileGutter;
        private protected readonly bool pixelTiles;
        private readonly int maxLodShaderId;
        private protected readonly CatZone logZone;

        /// <summary>Diagnostic label baked into every log line (from <see cref="GpuAtlasConfig.Label"/>).</summary>
        public string Label => config.Label;

        protected GpuAtlas(GpuAtlasConfig config)
        {
            if (config?.TileSizes == null || config.TileSizes.Length == 0)
                throw new ArgumentException("GpuAtlasConfig.TileSizes must hold at least one tile size.");

            this.config = config;
            textureFormat = config.Format;
            isLinear = config.Linear;
            atlasFilterMode = config.Filter;
            atlasMipMapBias = config.MipMapBias;
            tileGutter = config.TileGutter;
            pixelTiles = config.PixelTiles;
            hasMipmaps = config.Mips == GpuAtlasMips.TileAlignedWindow && SystemInfo.hasMipMaxLevel;
            logZone = config.LogZone ?? Cat.Zone("GpuAtlas");

            tileSizes = config.TileSizes;
            gridUnit = tileSizes[0];
            colSlots = PageSize / gridUnit;
            numSizeClasses = tileSizes.Length;
            if ((PageSize / gridUnit - 1) * colSlots + (colSlots - 1) >= ClassStride)
                throw new InvalidOperationException("GpuAtlas tile placement exceeds the encoded tile class stride.");
            maxLodShaderId = config.MaxLodShaderProperty != null
                ? Shader.PropertyToID(config.MaxLodShaderProperty)
                : -1;
            InitCollections();
            liveInstances.Add(this);
        }

#if UNITY_EDITOR
        static GpuAtlas()
        {
            EditorLifecycle.UnmanagedCleaned += ReleaseRetiredTextures;
        }
#endif

        private static readonly List<GpuAtlas<TEntry>> liveInstances = new();
        private static readonly CatZone staticLogZone = Cat.Zone("GpuAtlas");

        private static void ForEachInstance(Action<GpuAtlas<TEntry>> action)
        {
            for (int i = liveInstances.Count - 1; i >= 0; i--)
                action(liveInstances[i]);
        }

        protected int SizeClassIndex(int tileSize)
        {
            for (int i = 0; i < numSizeClasses; i++)
                if (tileSizes[i] == tileSize) return i;
            return 0;
        }

        protected int GetSizeClassFromEncoded(int encodedTile) => encodedTile / ClassStride;

        public int TileSizeFromEncoded(int encodedTile) => tileSizes[encodedTile / ClassStride];


        /// <summary>The registered upload target of the current storage generation (null until registration).</summary>
        protected GpuUploadTarget UploadTarget => gpuUploadTarget;

        /// <summary>Inserts a fresh entry and counts it on its page. The caller owns slot allocation and pending-work queuing.</summary>
        protected void InsertEntry(long key, in TEntry entry)
        {
            entries[key] = entry;
            pageEntryCount[entry.PageIndex]++;
        }

        /// <summary>Rewrites an existing entry in place (payload or placement already adjusted by the caller).</summary>
        protected void UpdateEntry(long key, in TEntry entry) => entries[key] = entry;

        /// <summary>Adds a submitted chunk to the delivery statistics.</summary>
        protected void NoteRegionsUploaded(int regions, long bytes)
        {
            uploadedRegions += regions;
            uploadedBytes += bytes;
        }

        /// <summary>Tears the atlas down: storage, tickets, index and consumer state. The published texture is dropped and subscribers are notified.</summary>
        public virtual void Dispose()
        {
            ClearGpuUploadTickets();
            DestroyAtlasTexture();
            publishedAtlasTexture = null;
            try
            {
                NotifyLocalAtlasTextureChanged(null);
            }
            finally
            {
                CompleteDeferredTextureRetirement();
            }
            DisposeNative(ref tilePixelScratch);
            ClearAtlasState();
            liveInstances.Remove(this);
        }

        /// <summary>Encoded-tile span reserved per size-class: placement ids (<c>tileCol + shelfRow·colSlots</c>) stay below it — asserted in the constructor; <see cref="GetSizeClassFromEncoded"/> and <see cref="DecodeTileXY"/> rely on it.</summary>
        protected const int ClassStride = 4096;

        protected readonly int[] tileSizes;
        protected readonly int gridUnit;
        protected readonly int colSlots;
        private readonly int numSizeClasses;
        public const int PageSize = 2048;
        private RenderTexture atlasRT;
        private Texture publishedAtlasTexture;
        private Texture deferredRetirementTexture;
        private GpuUploadTarget deferredRetirementTarget;
        private bool publicationRequiresPresentationCommit;
        private static readonly List<RetiredTexture> retiredTextures = new();
        private static bool retiredTexturePolling;
        private static bool lowMemorySubscribed;
        private bool retirementCompletionUnavailableLogged;
        private bool ceilingOverflowLogged;
        private bool storageDescriptionLogged;
        private bool uploadTargetRegistrationFailureLogged;
        private GpuUploadError gpuUploadTargetRegistrationError;
        private ulong gpuUploadTargetRegistrationEpoch;
        private bool gpuUploadDeliveryFailureLogged;
        private ulong gpuUploadSupportEpoch;
        private GpuUploadError gpuUploadDeliveryError;
        private GpuUploadTarget gpuUploadTarget;
        private NativeArray<byte> tilePixelScratch;

        protected int sliceCount;

        /// <summary>Reclamation flag: set on ANY refCount 1→0 transition (and by consumer removal sweeps) — orphans scatter across mixed pages, so waiting for a whole page to die misses most reclaimable states. Consumed by one recycle+compact+shrink evaluation at the first <see cref="CommitPresentationAfterPublication"/> of a frame in which the atlas allocated NOTHING; re-armed while in-flight tickets or deferred publication block the sweep, so the evaluation is deferred, never lost. The no-allocation gate separates a pause from the end of work, and the evaluation's own arithmetic filters make an idle pass free. Compaction is part of the evaluation because dead pages can be LEADING — trailing shrink cannot reclaim them, only a repack can. Event-driven — no maintenance cadence, no timers.</summary>
        private bool reclaimPending;

        /// <summary>Whether <see cref="AllocateTile"/> ran since this frame's <see cref="BeginFrame"/> — the reclamation gate's "the atlas was touched this frame" signal.</summary>
        private bool allocatedThisFrame;

        private int evictedThisFrame;

        private int gpuUploadBatches;
        private int uploadedRegions;
        private long uploadedBytes;
        private int flushYields;
        private bool hasLastGpuUploadError;
        private GpuUploadError lastGpuUploadError;

        /// <summary>Ticket ring bound: a flush of any size fits because <see cref="EnsureUploadTicketSlot"/> drains a full ring before each new batch, and tickets are drained by <see cref="PollGpuUploadTickets"/> every frame and inside every backpressure round. Retained statuses are never evicted while a ticket awaits them — GpuUpload pins each entry until its terminal status is observed and grows the shared history to outstanding-ticket demand — so the fail-closed audit never reads expired history.</summary>
        private const int GpuUploadTicketCapacity = 256;
        private readonly GpuUploadTicket[] gpuUploadTickets = new GpuUploadTicket[GpuUploadTicketCapacity];
        private int gpuUploadTicketCount;

        /// <summary>Occurs when GPU-authoritative atlas contents are lost and the index is wiped. Subscribers must drop refs and re-collect after this event. Static per <typeparamref name="TEntry"/> family: each closed generic has its own subscriber list.</summary>
        public static event Action AnyAtlasContentLost;
        private readonly FastLongDictionary<TEntry> entries = new();

        private LinkedList<long>[] evictableLists;
        private FastLongDictionary<LinkedListNode<long>>[] evictableNodeMaps;

        private readonly List<int> pageEntryCount = new();
        private readonly List<int> pageLiveCount = new();



        private struct Shelf
        {
            public int PageIndex;
            public int y;
            public int height;
            public int nextX;
        }

        private readonly List<Shelf> shelves = new();
        private readonly List<int> pageUsedHeight = new();

        protected struct TileSlot
        {
            public int PageIndex;
            public int EncodedTile;
        }

        private struct DeferredTileRetirement
        {
            public TileSlot slot;
            public bool live;
        }

        private readonly List<DeferredTileRetirement> deferredTileRetirements = new();

        private struct RetiredTexture
        {
            public Texture texture;
            public GpuUploadTarget uploadTarget;
            public AsyncGPUReadbackRequest readback;
            public bool hasReadback;
            public bool readbackPassed;
        }

        private struct PendingTile
        {
            public long key;
            public int PageIndex;
            public int EncodedTile;
            public byte[] rgbaPixels;
            public int pixelWidth;
            public int pixelHeight;
            public bool isBGRA;
        }

        private readonly List<PendingTile> pendingTiles = new();

        protected struct FlushTransaction
        {
            internal bool atlasWriteMayHaveStarted;
            internal bool graphicsStorageLost;
            internal GpuUploadError failure;
            internal uint recoveryVersion;
        }


        private uint recoveryVersion;

        private List<TileSlot>[] freeTiles;

        /// <summary>Out-of-cadence reclamation on OS memory pressure (iOS/Android/UWP foreground; the event does not exist on WebGL/desktop). Dead content only — never a live wipe: recycle dead pages, pack live tiles, trim storage.</summary>
        private static void EnsureLowMemorySubscription()
        {
            if (lowMemorySubscribed) return;
            lowMemorySubscribed = true;
            Application.lowMemory += static () => ForEachInstance(static a => a.SweepOnLowMemory());
        }

        private void SweepOnLowMemory()
        {
            RecycleDeadPages();
            Compact();
            TryShrinkAtlas();
        }

        private void InitCollections()
        {
            evictableLists = new LinkedList<long>[numSizeClasses];
            evictableNodeMaps = new FastLongDictionary<LinkedListNode<long>>[numSizeClasses];
            freeTiles = new List<TileSlot>[numSizeClasses];
            for (int i = 0; i < numSizeClasses; i++)
            {
                evictableLists[i] = new LinkedList<long>();
                evictableNodeMaps[i] = new FastLongDictionary<LinkedListNode<long>>();
                freeTiles[i] = new List<TileSlot>();
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetEntry(long key, out TEntry entry)
        {
            return entries.TryGetValue(key, out entry);
        }

        /// <summary>Ref-returning <see cref="TryGetEntry(long, out TEntry)"/>: avoids copying the multi-word entry per lookup on the consumer's hot path. The reference aliases the backing array and is invalidated by the next atlas insert/eviction/compaction — read it immediately, never hold it.</summary>
        public ref TEntry TryGetEntryRef(long key, out bool found)
        {
            return ref entries.TryGetValueRef(key, out found);
        }

        /// <summary>Passive per-atlas delivery counters since the last reset — the slim introspection surface for benchmarks and diagnostics.</summary>
        public readonly struct Stats
        {
            public string Label { get; }
            public int UploadBatches { get; }
            public int UploadedRegions { get; }
            public long UploadedBytes { get; }
            public int FlushYields { get; }
            public GpuUploadError? LastUploadError { get; }

            public Stats(string label, int uploadBatches, int uploadedRegions,
                long uploadedBytes, int flushYields, GpuUploadError? lastUploadError)
            {
                Label = label;
                UploadBatches = uploadBatches;
                UploadedRegions = uploadedRegions;
                UploadedBytes = uploadedBytes;
                FlushYields = flushYields;
                LastUploadError = lastUploadError;
            }
        }

        public Stats GetStats() => new(Label, gpuUploadBatches, uploadedRegions,
            uploadedBytes, flushYields, hasLastGpuUploadError ? lastGpuUploadError : null);

        public void ResetStats()
        {
            gpuUploadBatches = 0;
            uploadedRegions = 0;
            uploadedBytes = 0;
            flushYields = 0;
            hasLastGpuUploadError = false;
            lastGpuUploadError = default;
        }

        /// <summary>
        /// Keys pinned by one temporary ref from the moment a batch touches them (fresh content,
        /// relocation, or LRU revival on a cache hit) until the consumer's presentation COMMITS its
        /// refs at <see cref="CommitPresentationAfterPublication"/>. Never released at a frame boundary or at pixel
        /// flush: a flush may span frames under the yield contract, and component refs do not exist
        /// until the consumer's presentation commit runs — releasing at pixel flush left committed-but-unpublished tiles
        /// refCount-0, and BeginFrame reclamation evicted them into an endless flush→evict→re-raster
        /// treadmill (observed on Android/Vulkan, where big bursts always yield). The commit-time 1→0
        /// drop parks never-referenced entries in the evictable list.
        /// </summary>
        private readonly HashSet<long> batchProtected = new();

        public void ProtectForBatch(long key)
        {
            if (!batchProtected.Add(key)) return;
            AddRef(key);
        }

        private void ReleaseBatchProtection()
        {
            if (batchProtected.Count == 0) return;
            foreach (var key in batchProtected)
                Release(key);
            batchProtected.Clear();
        }

        public void AddRef(long key)
        {
            if (entries.TryGetValue(key, out var e))
            {
                if (e.RefCount == 0)
                    pageLiveCount[e.PageIndex]++;

                e.RefCount++;
                entries[key] = e;

                int ci = GetSizeClassFromEncoded(e.EncodedTile);
                UnlinkEvictable(ci, key);
            }
        }

        public void Release(long key)
        {
            if (entries.TryGetValue(key, out var e))
            {
                if (e.RefCount <= 0)
                {
                    logZone.MeowWarnFormat("[" + Label + "] Release: key 0x{0:X} already has refCount={1}", key, e.RefCount);
                    return;
                }
                e.RefCount--;
                entries[key] = e;

                if (e.RefCount == 0)
                {
                    pageLiveCount[e.PageIndex]--;
                    reclaimPending = true;

                    int ci = GetSizeClassFromEncoded(e.EncodedTile);
                    if (!evictableNodeMaps[ci].ContainsKey(key))
                    {
                        var node = evictableLists[ci].AddLast(key);
                        evictableNodeMaps[ci][key] = node;
                    }
                }
            }
        }

        public Texture AtlasTexture => publishedAtlasTexture;
        public int TileGutter => tileGutter;
        public int MaxContentSize => tileSizes[0] - 2 * tileGutter;
        public event Action<Texture> AtlasTextureChanged;

        /// <summary>Static per <typeparamref name="TEntry"/> family, like <see cref="AnyAtlasContentLost"/>.</summary>
        public static event Action<Texture> AnyAtlasTextureChanged;

        /// <summary>Static per <typeparamref name="TEntry"/> family, like <see cref="AnyAtlasContentLost"/>.</summary>
        public static event Action<GpuAtlas<TEntry>> AnyAtlasCompacted;

        private void NotifyAtlasTextureChanged(Texture texture)
        {
            InvokeSafely(AtlasTextureChanged, texture, nameof(AtlasTextureChanged));
            InvokeSafely(AnyAtlasTextureChanged, texture, nameof(AnyAtlasTextureChanged));
        }

        private void NotifyLocalAtlasTextureChanged(Texture texture) =>
            InvokeSafely(AtlasTextureChanged, texture, nameof(AtlasTextureChanged));

        private static void NotifyAtlasContentLost() =>
            InvokeSafely(AnyAtlasContentLost, nameof(AnyAtlasContentLost));

        private static void NotifyAtlasCompacted(GpuAtlas<TEntry> atlas) =>
            InvokeSafely(AnyAtlasCompacted, atlas, nameof(AnyAtlasCompacted));

        private static void InvokeSafely(Action handlers, string eventName)
        {
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch (Exception exception)
                {
                    staticLogZone.MeowError(
                        $"[GpuAtlas] {eventName} subscriber raised {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        private static void InvokeSafely<T>(Action<T> handlers, T value, string eventName)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(value);
                }
                catch (Exception exception)
                {
                    staticLogZone.MeowError(
                        $"[GpuAtlas] {eventName} subscriber raised {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        public int PageCount => sliceCount;
        public int EntryCount => entries.Count;

    }
}
