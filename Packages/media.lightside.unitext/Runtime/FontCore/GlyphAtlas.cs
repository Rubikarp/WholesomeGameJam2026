using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace LightSide
{
    /// <summary>
    /// GPU-resident glyph tile cache over the <see cref="GpuAtlas{TEntry}"/> engine: Burst CPU
    /// rasterization directly into acquired <see cref="GpuUploadSlot"/> memory for SDF/MSDF
    /// glyphs, ready color pixels for emoji/color fonts. This class owns everything glyph-shaped —
    /// pad tiers, tile-size classification, the <see cref="GlyphTransformTable"/> rows, raster jobs —
    /// while storage, delivery, slots, eviction, compaction and recovery live in the engine.
    /// The atlas is a disposable cache: on <see cref="GpuAtlas{TEntry}.AnyAtlasContentLost"/> consumers
    /// re-collect from font data.
    /// </summary>
    public sealed class GlyphAtlas : GpuAtlas<GlyphAtlas.GlyphEntry>
    {
        /// <summary>SDF distance scale: the atlas stores signed distance in em, clamped to ±this, and effect widths convert em→field units through it (the shader's DILATE_SCALE). NOT the tile rim — see <see cref="TileFitPad"/>.</summary>
        public const float Pad = 0.5f;

        /// <summary>Highest pad tier: tiers 0..2 are the fixed rims below, the top tier reserves the full SDF spread.</summary>
        private const int PadTierMax = 3;

        /// <summary>Tight-fit rim for an effect-free glyph, in glyph-height units — pad tier 0 (see <see cref="PadTierToNorm"/>). Small enough that the glyph nearly fills its tile, large enough for edge AA (must exceed the face SDF padding). Effects grow the tier per-glyph via re-rasterization, up to the full <c>Pad/glyphH</c> spread.</summary>
        public const float TileFitPad = 0.06f;

        /// <summary>Headroom (glyph-height units) added to an effect's outward reach when picking its pad tier, so the reserved rim exceeds the reach by an AA-ramp margin. The glyph fills its tile up to the rim, so the distance field is valid only to ~the rim; an effect whose outer edge lands exactly at the rim would sample the field→flat-fill seam (a 1px rim of effect colour). This pushes such an effect to a looser tier where its edge falls inside computed field.</summary>
        internal const float TierSeamMarginNorm = 0.05f;

        /// <summary>Reserved rim (glyph-height units) at pad tiers 1 and 2. Fixed breakpoints, NOT a linear ramp: with only 4 tier slots a linear tier-0→maxRim ramp made the first non-zero tier huge (≈ 0.28 for cap-height glyphs), over-reserving for the common small effect. A default 0.12 em stroke needs ≈ 0.09 rim, so tier 1 sits just above it.</summary>
        internal const float TierRim1 = 0.15f;
        internal const float TierRim2 = 0.30f;

        /// <summary>Per-side rim (glyph-height units) reserved for a glyph at the given pad tier. Tier 0 = tight fit (<see cref="TileFitPad"/>), tiers 1/2 = <see cref="TierRim1"/>/<see cref="TierRim2"/> breakpoints, top tier = the full SDF spread <c>Pad/glyphH</c>. Every rim is capped at <c>Pad/glyphH</c> (the physical SDF limit); the raster receives the already-normalized padNorm.</summary>
        internal static float PadTierToNorm(int tier, float glyphH)
        {
            float maxRim = Pad / (glyphH < 1e-6f ? 1e-6f : glyphH);
            float rim = tier <= 0 ? TileFitPad
                : tier == 1 ? TierRim1
                : tier == 2 ? TierRim2
                : maxRim;
            return rim < maxRim ? rim : maxRim;
        }

        /// <summary>Smallest pad tier whose reserved rim (<see cref="PadTierToNorm"/>) covers <paramref name="requiredNorm"/> for this glyph height.</summary>
        internal static int PadTierForExtent(float requiredNorm, float glyphH)
        {
            for (int t = 0; t < PadTierMax; t++)
                if (PadTierToNorm(t, glyphH) >= requiredNorm) return t;
            return PadTierMax;
        }

        /// <summary>
        /// Publishes the entry's atlas transform into its <see cref="GlyphTransformTable"/> row:
        /// atlasUV = glyphUV * scale + offset on page <see cref="GlyphEntry.pageIndex"/>. The isotropic
        /// placement math (tile fit, rim, ~1-texel gutter). SYNC: mirrored by the raster tile transforms —
        /// this is the single producer of the mapping the shaders consume.
        /// INVARIANT: a row is written only when the tile it maps holds valid pixels for this glyph —
        /// at pixel flush for pending rasters, and inside compaction where the blit moves the pixels in
        /// the same breath. A relocated live glyph therefore keeps rendering from its retained old tile
        /// (deferred tile retirement) for however long its re-raster stays queued — relocation is never
        /// visible as a blank tile.
        /// </summary>
        private void WriteTransformRow(in GlyphEntry entry)
        {
            if (entry.handle < 0 || entry.encodedTile < 0) return;

            int ci = GetSizeClassFromEncoded(entry.encodedTile);
            int tileSize = tileSizes[ci];
            DecodeTileXY(entry.encodedTile, tileSize, out int tilePxX, out int tilePxY);

            float aspect = entry.metrics.height > 1e-6f ? entry.metrics.width / entry.metrics.height : 1f;
            float maxDim = aspect > 1f ? aspect : 1f;
            float baseExtent = maxDim + 2f * entry.padNorm;
            float gutter = baseExtent / tileSize;
            float totalExtent = baseExtent + 2f * gutter;

            const float invPage = 1f / PageSize;
            float s = tileSize * invPage / totalExtent;
            float offsetX = tilePxX * invPage + ((maxDim - aspect) * 0.5f + entry.padNorm + gutter) * s;
            float offsetY = tilePxY * invPage + ((maxDim - 1f) * 0.5f + entry.padNorm + gutter) * s;

            GlyphTransformTable.Set(entry.handle, s, offsetX, offsetY, entry.pageIndex);
        }

        /// <summary>
        /// When true, SDF/MSDF jobs run single-threaded via job.Run() instead of Schedule().
        /// Useful for benchmarking without parallelism (e.g. WebGL parity).
        /// </summary>
        internal static bool forceSingleThreaded;

        private static readonly int[] defaultTileSizes = { 64, 128, 256 };

        private readonly UniTextRenderMode mode;
        private NativeArray<long> cpuRasterTaskPointers;
        private readonly List<PendingGlyph> pending = new();
        private PooledBuffer<GlyphCurveCache.Segment> pendingSegments;
        private PooledBuffer<byte> pendingAlpha;

        public struct GlyphEntry : IGpuAtlasEntry
        {
            public int encodedTile;
            public int pageIndex;
            internal int refCount;
            internal int baseFontHash;
            /// <summary>Current pad tier the atlas tile is rasterized at (<see cref="PadTierToNorm"/>); grows when an effect needs more rim than the current tier reserves.</summary>
            internal byte padTier;
            public GlyphMetrics metrics;
            /// <summary>Rendered pixel width (color only, 0 for SDF).</summary>
            public int pixelWidth;
            /// <summary>Rendered pixel height (color only, 0 for SDF).</summary>
            public int pixelHeight;

            /// <summary>
            /// Stable glyph handle written to the vertex (UV0.z) and indexing the entry's row in
            /// <see cref="GlyphTransformTable"/>. Allocated once per entry, unchanged across pad-tier
            /// upgrades, tile-size upgrades and compaction — relocation rewrites the table row, never
            /// the meshes. -1 for color entries (their quads carry the atlas rect directly).
            /// </summary>
            internal int handle;

            /// <summary>Rasterized per-side rim in glyph-height units (<see cref="PadTierToNorm"/> of <see cref="padTier"/>) — a table-row input, stored because it depends on the glyph height the atlas does not otherwise retain.</summary>
            internal float padNorm;

            int IGpuAtlasEntry.EncodedTile { get => encodedTile; set => encodedTile = value; }
            int IGpuAtlasEntry.PageIndex { get => pageIndex; set => pageIndex = value; }
            int IGpuAtlasEntry.RefCount { get => refCount; set => refCount = value; }
        }

        private struct PendingGlyph
        {
            public long key;
            public int pageIndex;
            public int encodedTile;
            public float aspect;
            public float glyphH;
            public int segmentOffset;
            public int segmentCount;
            public float padNorm;
            public int alphaOffset;
            public int alphaWidth;
            public int alphaHeight;
        }

        private static GlyphAtlas sdfInstance;
        private static GlyphAtlas msdfInstance;
        private static GlyphAtlas colorInstance;
        private static readonly object instanceGate = new();

        /// <summary>Returns the process-wide atlas; GPU initialization remains deferred to the main thread.</summary>
        public static GlyphAtlas GetInstance(UniTextRenderMode mode) => mode switch
        {
            UniTextRenderMode.SDF => GetOrCreateInstance(ref sdfInstance, UniTextRenderMode.SDF),
            UniTextRenderMode.MSDF => GetOrCreateInstance(ref msdfInstance, UniTextRenderMode.MSDF),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        internal static bool TryGetExistingInstance(UniTextRenderMode mode, out GlyphAtlas atlas)
        {
            atlas = mode switch
            {
                UniTextRenderMode.SDF => System.Threading.Volatile.Read(ref sdfInstance),
                UniTextRenderMode.MSDF => System.Threading.Volatile.Read(ref msdfInstance),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            return atlas != null;
        }

        internal static GlyphAtlas Color => System.Threading.Volatile.Read(ref colorInstance);

        private static GlyphAtlas GetOrCreateInstance(ref GlyphAtlas instance,
            UniTextRenderMode mode)
        {
            var current = System.Threading.Volatile.Read(ref instance);
            if (current != null) return current;
            lock (instanceGate)
            {
                current = instance;
                if (current != null) return current;
                current = new GlyphAtlas(mode);
                System.Threading.Volatile.Write(ref instance, current);
                return current;
            }
        }

        /// <summary>Gets or creates the single shared color-glyph atlas. All color fonts co-reside here, keyed by varHash48 + baseFontHash; the first color font to load (normally the system emoji font, pre-warmed on the main thread) fixes the tile pixel size for the rest.</summary>
        internal static GlyphAtlas CreateColorInstance(int colorPixelSize)
        {
            var current = System.Threading.Volatile.Read(ref colorInstance);
            if (current != null)
            {
                WarnIfColorAtlasTooSmall(colorPixelSize, current);
                return current;
            }
            lock (instanceGate)
            {
                current = colorInstance;
                if (current != null)
                {
                    WarnIfColorAtlasTooSmall(colorPixelSize, current);
                    return current;
                }
                current = new GlyphAtlas(colorPixelSize);
                System.Threading.Volatile.Write(ref colorInstance, current);
                return current;
            }
        }

        private static void WarnIfColorAtlasTooSmall(int requested, GlyphAtlas existing)
        {
            if (requested > existing.ColorPixelSize)
                CatZones.glyphAtlas.MeowWarnOnce($"coloratlas:{requested}",
                    "[GlyphAtlas] Color font requests {0}px but the shared color atlas is {1}px; its glyphs render at the smaller size. Set matching Color Pixel Sizes to avoid under-sampling.",
                    requested, existing.ColorPixelSize);
        }

        internal static void ForEachInstance(Action<GlyphAtlas> action)
        {
            var sdf = System.Threading.Volatile.Read(ref sdfInstance);
            var msdf = System.Threading.Volatile.Read(ref msdfInstance);
            var colorAtlas = System.Threading.Volatile.Read(ref colorInstance);
            if (sdf != null) action(sdf);
            if (msdf != null) action(msdf);
            if (colorAtlas != null) action(colorAtlas);
        }

        internal static bool FlushPendingInstances()
        {
            var sdf = System.Threading.Volatile.Read(ref sdfInstance);
            var msdf = System.Threading.Volatile.Read(ref msdfInstance);
            var colorAtlas = System.Threading.Volatile.Read(ref colorInstance);
            bool flushed = (sdf == null || sdf.FlushPending())
                           && (msdf == null || msdf.FlushPending())
                           && (colorAtlas == null || colorAtlas.FlushPending());
            GlyphTransformTable.FlushIfDirty();
            return flushed;
        }

        /// <summary>Publishes deferred atlas storage and releases superseded tiles only after every affected renderer mesh has been uploaded.</summary>
        internal static void CommitMeshChanges()
        {
            ForEachInstance(static atlas => atlas.CommitPresentationAfterPublication());
            GlyphTransformTable.CommitDeferredFrees();
        }

#if UNITY_EDITOR
        static GlyphAtlas()
        {
            EditorLifecycle.UnmanagedCleaning += DisposeInstances;
        }
#endif

        private static void DisposeInstances()
        {
            DisposeTextInstances();
            System.Threading.Volatile.Read(ref colorInstance)?.Dispose();
        }

        internal static void DisposeTextInstances()
        {
            System.Threading.Volatile.Read(ref sdfInstance)?.Dispose();
            System.Threading.Volatile.Read(ref msdfInstance)?.Dispose();
        }

        internal const int ColorTileGutter = 4;

        /// <summary>Pixel size the color atlas was built for (tile side − 2·gutter).</summary>
        internal int ColorPixelSize => gridUnit - 2 * tileGutter;

        private GlyphAtlas(UniTextRenderMode mode) : base(TextConfig(mode))
        {
            this.mode = mode;
            pendingSegments = default;
            pendingSegments.EnsureCapacity(4096);
        }

        private static GpuAtlasConfig TextConfig(UniTextRenderMode mode) => new()
        {
            Format = mode == UniTextRenderMode.MSDF ? TextureFormat.RGBAHalf : TextureFormat.RHalf,
            Linear = true,
            Filter = FilterMode.Bilinear,
            Mips = GpuAtlasMips.None,
            PixelTiles = false,
            TileSizes = defaultTileSizes,
            Label = mode == UniTextRenderMode.MSDF ? "GlyphAtlas:MSDF" : "GlyphAtlas:SDF",
            LogZone = CatZones.glyphAtlas,
        };

        private GlyphAtlas(int colorPixelSize) : base(ColorConfig(colorPixelSize))
        {
        }

        private static GpuAtlasConfig ColorConfig(int colorPixelSize)
        {
            if (colorPixelSize < 8 || colorPixelSize > PageSize - 2 * ColorTileGutter)
                throw new ArgumentOutOfRangeException(nameof(colorPixelSize), colorPixelSize,
                    $"Color pixel size must be in [8, {PageSize - 2 * ColorTileGutter}] so atlas tiles remain addressable.");
            bool mips = SystemInfo.hasMipMaxLevel;
            return new GpuAtlasConfig
            {
                Format = TextureFormat.RGBA32,
                Linear = false,
                Filter = mips ? FilterMode.Trilinear : FilterMode.Bilinear,
                MipMapBias = mips ? -0.5f : 0f,
                Mips = GpuAtlasMips.TileAlignedWindow,
                PixelTiles = true,
                TileSizes = new[] { colorPixelSize + 2 * ColorTileGutter },
                TileGutter = ColorTileGutter,
                Label = "GlyphAtlas:Color",
                LogZone = CatZones.glyphAtlas,
                MaxLodShaderProperty = "_UniTextColorMaxLod",
            };
        }

        internal static int OffsetTileSize(int tileSize, int offset)
        {
            int idx = 0;
            while (idx < defaultTileSizes.Length - 1 && tileSize > defaultTileSizes[idx]) idx++;
            int newIdx = Math.Clamp(idx + offset, 0, defaultTileSizes.Length - 1);
            return defaultTileSizes[newIdx];
        }

        public static long MakeKey(long varHash48, uint glyphIndex) =>
            (varHash48 << 16) | (glyphIndex & 0xFFFF);

        /// <summary>Computes default varHash48 for a non-variable font.</summary>
        public static long DefaultVarHash(int fontDataHash) =>
            (long)fontDataHash & 0xFFFF_FFFFFFFF;

        /// <summary>
        /// Computes varHash48 for a variable font with specific axis values.
        /// Uses FNV-1a to mix fontDataHash with axis values.
        /// Returns DefaultVarHash if axisValues is empty.
        /// </summary>
        public static long ComputeVarHash48(int fontDataHash, ReadOnlySpan<float> axisValues)
        {
            if (axisValues.Length == 0)
                return DefaultVarHash(fontDataHash);

            unchecked
            {
                const long fnvOffset = unchecked((long)0xCBF29CE484222325);
                const long fnvPrime = 0x100000001B3;

                long h = fnvOffset;
                h = (h ^ fontDataHash) * fnvPrime;
                for (int i = 0; i < axisValues.Length; i++)
                {
                    int bits = BitConverter.SingleToInt32Bits(axisValues[i]);
                    h = (h ^ bits) * fnvPrime;
                }
                return h & 0xFFFF_FFFFFFFF;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetEntry(long varHash48, uint glyphIndex, out GlyphEntry entry) =>
            TryGetEntry(MakeKey(varHash48, glyphIndex), out entry);

        internal static int ClassifyTileSize(ReadOnlySpan<GlyphCurveCache.Segment> segments, float aspect, float glyphH, float detailMultiplier = 1f)
        {
            int n = segments.Length;
            int idx;

            if (n <= 8)
            {
                idx = 0;
            }
            else
            {
                float totalChordLen2 = 0f;
                for (int i = 0; i < n; i++)
                {
                    float ex = segments[i].p2x;
                    float ey = segments[i].p2y;
                    float dx = ex - segments[i].p0x;
                    float dy = ey - segments[i].p0y;
                    totalChordLen2 += dx * dx + dy * dy;
                }

                float area = Math.Max(aspect, 0.01f);
                float detail = totalChordLen2 * n / area * detailMultiplier;

                idx = detail < 100f ? 0 : detail < 500f ? 1 : 2;
            }

            const float minTexels = 3f;
            float minDim = Math.Min(aspect, 1f);
            float padGlyph = Pad / Math.Max(glyphH, 1e-6f);
            float totalExtent = Math.Max(aspect, 1f) + 2f * padGlyph;

            while (idx < defaultTileSizes.Length - 1 &&
                   minDim / totalExtent * defaultTileSizes[idx] < minTexels)
                idx++;

            return defaultTileSizes[idx];
        }

        internal void ReservePendingSegments(int additionalCount)
        {
            pendingSegments.EnsureCapacity(pendingSegments.count + additionalCount);
        }

        internal GlyphEntry EnsureGlyph(long varHash48, uint glyphIndex, int baseFontHash,
            in GlyphCurveCache.GlyphCurveData curveData, ReadOnlySpan<GlyphCurveCache.Segment> segments,
            int tileSize, float glyphH, float aspect, in GlyphMetrics glyphMetrics)
        {
            long key = MakeKey(varHash48, glyphIndex);
            if (TryGetEntry(key, out var existing))
                return existing;

            if (curveData.isEmpty || segments.Length == 0)
                return new GlyphEntry { encodedTile = -1, pageIndex = -1 };

            var slot = AllocateTile(tileSize);

            int segOffset = pendingSegments.count;
            int segCount = segments.Length;
            pendingSegments.EnsureCapacity(segOffset + segCount);
            segments.CopyTo(pendingSegments.data.AsSpan(segOffset));
            pendingSegments.count += segCount;

            float padNorm = PadTierToNorm(0, glyphH);
            pending.Add(new PendingGlyph
            {
                key = key,
                pageIndex = slot.PageIndex,
                encodedTile = slot.EncodedTile,
                aspect = aspect,
                glyphH = glyphH,
                segmentOffset = segOffset,
                segmentCount = segCount,
                padNorm = padNorm
            });

            var entry = new GlyphEntry
            {
                encodedTile = slot.EncodedTile,
                pageIndex = slot.PageIndex,
                refCount = 0,
                baseFontHash = baseFontHash,
                padTier = 0,
                metrics = glyphMetrics,
                handle = GlyphTransformTable.Allocate(),
                padNorm = padNorm
            };
            InsertEntry(key, in entry);
            ProtectForBatch(key);

            return entry;
        }

        /// <summary>
        /// Key space of the silhouette fields derived from a colour font's bitmaps: the font's identity
        /// mixed with a sentinel no variation axis can take, so a field never aliases an outline glyph
        /// of the same font in the SDF atlas.
        /// </summary>
        internal static long FieldVarHash48(int fontDataHash)
        {
            Span<float> sentinel = stackalloc float[1];
            sentinel[0] = float.PositiveInfinity;
            return ComputeVarHash48(fontDataHash, sentinel);
        }

        /// <summary>Side of the largest tile class — the most resolution a silhouette field can hold, so a bitmap read only for its field need not exceed it.</summary>
        internal static int LargestTileSize => defaultTileSizes[defaultTileSizes.Length - 1];

        /// <summary>Smallest tile class that holds a bitmap silhouette at its own resolution; the pad rim then scales it down slightly. The largest class when the bitmap overflows every class.</summary>
        internal static int ClassifyFieldTileSize(int width, int height)
        {
            int major = Math.Max(width, height);
            for (int i = 0; i < defaultTileSizes.Length; i++)
                if (defaultTileSizes[i] >= major) return defaultTileSizes[i];
            return defaultTileSizes[defaultTileSizes.Length - 1];
        }

        /// <summary>
        /// Ensures the SDF atlas holds the silhouette field of a colour glyph's alpha bitmap at least at
        /// <paramref name="tier"/>: a missing field is queued for rasterization in a fresh tile; an
        /// existing one below the tier is re-rasterized through <see cref="UpgradeFieldTier"/>. Only the
        /// SDF instance hosts fields — every colour-glyph effect quad samples them in SDF mode.
        /// </summary>
        /// <exception cref="InvalidOperationException">This is not the SDF atlas.</exception>
        internal GlyphEntry EnsureFieldGlyph(long fieldVarHash48, uint glyphIndex, int baseFontHash,
            ReadOnlySpan<byte> alpha, int width, int height, float glyphH, float aspect, byte tier,
            in GlyphMetrics glyphMetrics)
        {
            RequireFieldHost();
            long key = MakeKey(fieldVarHash48, glyphIndex);
            if (TryGetEntry(key, out var existing))
            {
                if (tier > existing.padTier)
                {
                    UpgradeFieldTier(key, alpha, width, height, glyphH, aspect, tier);
                    TryGetEntry(key, out existing);
                }
                else if (existing.refCount == 0)
                    ProtectForBatch(key);
                return existing;
            }

            if (width <= 0 || height <= 0 || alpha.Length < width * height || glyphH < 1e-6f)
                return new GlyphEntry { encodedTile = -1, pageIndex = -1 };

            var slot = AllocateTile(ClassifyFieldTileSize(width, height));
            float padNorm = PadTierToNorm(tier, glyphH);
            QueueAlphaRaster(key, slot.PageIndex, slot.EncodedTile, alpha, width, height, glyphH, aspect, padNorm);

            var entry = new GlyphEntry
            {
                encodedTile = slot.EncodedTile,
                pageIndex = slot.PageIndex,
                refCount = 0,
                baseFontHash = baseFontHash,
                padTier = tier,
                metrics = glyphMetrics,
                handle = GlyphTransformTable.Allocate(),
                padNorm = padNorm
            };
            InsertEntry(key, in entry);
            ProtectForBatch(key);
            return entry;
        }

        /// <summary>Re-rasterizes a published silhouette field at a looser pad tier in a fresh tile (see <see cref="UpgradeGlyphTier"/>).</summary>
        internal void UpgradeFieldTier(long key, ReadOnlySpan<byte> alpha, int width, int height,
            float glyphH, float aspect, byte tier)
        {
            RequireFieldHost();
            if (!TryGetEntry(key, out var entry)) return;
            if (tier <= entry.padTier) return;
            if (width <= 0 || height <= 0 || alpha.Length < width * height) return;

            RelocateEntry(key, tileSizes[GetSizeClassFromEncoded(entry.encodedTile)], ref entry);
            float padNorm = PadTierToNorm(tier, glyphH);
            QueueAlphaRaster(key, entry.pageIndex, entry.encodedTile, alpha, width, height, glyphH, aspect, padNorm);

            entry.padTier = tier;
            entry.padNorm = padNorm;
            UpdateEntry(key, in entry);
            ProtectForBatch(key);
        }

        /// <summary>Grow-only relocation of a silhouette field to a larger tile class (see <see cref="UpgradeGlyphTileSize"/>). Returns whether the tile actually grew.</summary>
        internal bool UpgradeFieldTileSize(long key, ReadOnlySpan<byte> alpha, int width, int height,
            float glyphH, float aspect, int targetTileSize)
        {
            RequireFieldHost();
            if (!TryGetEntry(key, out var entry)) return false;
            int curClass = GetSizeClassFromEncoded(entry.encodedTile);
            int targetClass = SizeClassIndex(targetTileSize);
            if (targetClass <= curClass) return false;
            if (width <= 0 || height <= 0 || alpha.Length < width * height) return false;

            RelocateEntry(key, targetTileSize, ref entry);

            int pendingIdx = -1;
            for (int i = pending.Count - 1; i >= 0; i--)
                if (pending[i].key == key) { pendingIdx = i; break; }

            if (pendingIdx >= 0)
            {
                var pg = pending[pendingIdx];
                pg.pageIndex = entry.pageIndex;
                pg.encodedTile = entry.encodedTile;
                pending[pendingIdx] = pg;
            }
            else
                QueueAlphaRaster(key, entry.pageIndex, entry.encodedTile, alpha, width, height, glyphH, aspect,
                    PadTierToNorm(entry.padTier, glyphH));

            UpdateEntry(key, in entry);
            ProtectForBatch(key);
            return true;
        }

        private void QueueAlphaRaster(long key, int pageIndex, int encodedTile, ReadOnlySpan<byte> alpha,
            int width, int height, float glyphH, float aspect, float padNorm)
        {
            int count = width * height;
            int offset = pendingAlpha.count;
            pendingAlpha.EnsureCapacity(offset + count);
            alpha.Slice(0, count).CopyTo(pendingAlpha.data.AsSpan(offset, count));
            pendingAlpha.count = offset + count;

            pending.Add(new PendingGlyph
            {
                key = key,
                pageIndex = pageIndex,
                encodedTile = encodedTile,
                aspect = aspect,
                glyphH = glyphH,
                segmentOffset = 0,
                segmentCount = 0,
                padNorm = padNorm,
                alphaOffset = offset,
                alphaWidth = width,
                alphaHeight = height
            });
        }

        private void RequireFieldHost()
        {
            if (mode != UniTextRenderMode.SDF || ReferenceEquals(this, System.Threading.Volatile.Read(ref colorInstance)))
                throw new InvalidOperationException("Silhouette fields are hosted by the SDF glyph atlas only.");
        }

        internal GlyphEntry EnsureColorGlyph(long varHash48, uint glyphIndex, int baseFontHash,
            byte[] pixels, int w, int h, bool isBGRA, in GlyphMetrics glyphMetrics)
        {
            int storedWidth = Math.Min(w, MaxContentSize);
            int storedHeight = Math.Min(h, MaxContentSize);
            var template = new GlyphEntry
            {
                encodedTile = -1,
                pageIndex = -1,
                baseFontHash = baseFontHash,
                metrics = glyphMetrics,
                pixelWidth = storedWidth,
                pixelHeight = storedHeight,
                handle = -1,
            };
            return EnsureTilePixels(MakeKey(varHash48, glyphIndex), in template, pixels, w, h, isBGRA);
        }

        /// <summary>
        /// Raises the pad tier of a glyph still sitting in the un-flushed pending batch, reusing the
        /// already-extracted segments — one rasterization instead of raster→re-extract→re-raster.
        /// Returns false when the glyph is not pending (already flushed in an earlier frame) and the
        /// caller must fall back to <see cref="UpgradeGlyphTier"/>.
        /// </summary>
        internal bool TryUpgradePendingTier(long key, byte tier)
        {
            if (!TryGetEntry(key, out var entry)) return false;
            if (tier <= entry.padTier) return true;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].key != key) continue;
                var pg = pending[i];
                pg.padNorm = PadTierToNorm(tier, pg.glyphH);
                pending[i] = pg;
                entry.padTier = tier;
                entry.padNorm = pg.padNorm;
                UpdateEntry(key, in entry);
                return true;
            }
            return false;
        }

        /// <summary>Re-rasterizes a published glyph in a fresh tile so the currently displayed tile remains immutable until mesh publication.</summary>
        internal void UpgradeGlyphTier(long key,
            ReadOnlySpan<GlyphCurveCache.Segment> segments,
            float glyphH, float aspect, byte tier)
        {
            if (!TryGetEntry(key, out var entry)) return;
            if (tier <= entry.padTier) return;

            RelocateEntry(key, tileSizes[GetSizeClassFromEncoded(entry.encodedTile)],
                ref entry);

            int segOffset = pendingSegments.count;
            pendingSegments.EnsureCapacity(segOffset + segments.Length);
            segments.CopyTo(pendingSegments.data.AsSpan(segOffset));
            pendingSegments.count += segments.Length;

            float padNorm = PadTierToNorm(tier, glyphH);
            pending.Add(new PendingGlyph
            {
                key = key,
                pageIndex = entry.pageIndex,
                encodedTile = entry.encodedTile,
                aspect = aspect,
                glyphH = glyphH,
                segmentOffset = segOffset,
                segmentCount = segments.Length,
                padNorm = padNorm
            });

            entry.padTier = tier;
            entry.padNorm = padNorm;
            UpdateEntry(key, in entry);
            ProtectForBatch(key);
        }

        /// <summary>
        /// Grow-only relocation of a glyph to a larger tile-size class: allocates a slot in
        /// <paramref name="targetTileSize"/>'s class, re-rasterizes there at the current pad tier (an
        /// un-flushed pending raster is retargeted in place; a flushed glyph queues a fresh one), retains the
        /// old slot until renderer mesh publication, and rewrites the entry's tile id. No-op — returns false — when the target class is not
        /// larger than the current one. Meshes are untouched: the relocation publishes through the
        /// glyph's <see cref="GlyphTransformTable"/> row (the glyph's atlas key ignores tile size, so
        /// one tile is shared at the max requested resolution).
        /// </summary>
        internal bool UpgradeGlyphTileSize(long key,
            ReadOnlySpan<GlyphCurveCache.Segment> segments,
            float glyphH, float aspect, int targetTileSize)
        {
            if (!TryGetEntry(key, out var entry)) return false;
            int curClass = GetSizeClassFromEncoded(entry.encodedTile);
            int targetClass = SizeClassIndex(targetTileSize);
            if (targetClass <= curClass) return false;

            RelocateEntry(key, targetTileSize, ref entry);

            int pendingIdx = -1;
            for (int i = pending.Count - 1; i >= 0; i--)
                if (pending[i].key == key) { pendingIdx = i; break; }

            if (pendingIdx >= 0)
            {
                var pg = pending[pendingIdx];
                pg.pageIndex = entry.pageIndex;
                pg.encodedTile = entry.encodedTile;
                pending[pendingIdx] = pg;
            }
            else
            {
                int segOffset = pendingSegments.count;
                pendingSegments.EnsureCapacity(segOffset + segments.Length);
                segments.CopyTo(pendingSegments.data.AsSpan(segOffset));
                pendingSegments.count += segments.Length;
                pending.Add(new PendingGlyph
                {
                    key = key,
                    pageIndex = entry.pageIndex,
                    encodedTile = entry.encodedTile,
                    aspect = aspect,
                    glyphH = glyphH,
                    segmentOffset = segOffset,
                    segmentCount = segments.Length,
                    padNorm = PadTierToNorm(entry.padTier, glyphH)
                });
            }

            UpdateEntry(key, in entry);
            ProtectForBatch(key);
            return true;
        }

        internal void ClearForFont(int fontHash) =>
            RemoveWhere(e => e.baseFontHash == fontHash);

        internal static float ComputeAspect(in GlyphCurveCache.GlyphCurveData metrics)
        {
            float h = metrics.bboxMaxY - metrics.bboxMinY;
            if (h < 1e-6f) return 1f;
            float w = metrics.bboxMaxX - metrics.bboxMinX;
            return w / h;
        }

        protected override void OnEntryDropped(in GlyphEntry entry) =>
            GlyphTransformTable.Free(entry.handle);

        protected override void OnTilePlaced(long key, in GlyphEntry entry) =>
            WriteTransformRow(in entry);

        protected override bool HasConsumerPendingWork => pending.Count > 0;

        protected override void OnRemoveSweep(Func<GlyphEntry, bool> match)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (TryGetEntry(pending[i].key, out var pe) && match(pe))
                {
                    pending[i] = pending[^1];
                    pending.RemoveAt(pending.Count - 1);
                }
            }
        }

        protected override void OnAtlasStateCleared()
        {
            pending.Clear();
            pendingSegments.FakeClear();
            pendingAlpha.FakeClear();
        }

        protected override void OnCompactionRelocated()
        {
            for (int i = 0; i < pending.Count; i++)
            {
                if (!TryGetEntry(pending[i].key, out var e)) continue;
                var pg = pending[i];
                pg.pageIndex = e.pageIndex;
                pg.encodedTile = e.encodedTile;
                pending[i] = pg;
            }
            GlyphTransformTable.FlushIfDirty();
        }

        public override void Dispose()
        {
            base.Dispose();
            pendingSegments.Return();
            pendingAlpha.Return();
            DisposeNative(ref cpuRasterTaskPointers);

            lock (instanceGate)
            {
                if (ReferenceEquals(sdfInstance, this))
                    System.Threading.Volatile.Write(ref sdfInstance, null);
                else if (ReferenceEquals(msdfInstance, this))
                    System.Threading.Volatile.Write(ref msdfInstance, null);
                else if (ReferenceEquals(colorInstance, this))
                    System.Threading.Volatile.Write(ref colorInstance, null);
            }
        }

        protected override unsafe bool FlushPendingWork(ref FlushTransaction transaction)
        {
            var timer = new DebugTimer();
            timer.Mark();

            int count = pending.Count;
            bool msdf = mode == UniTextRenderMode.MSDF;
            int bpp = msdf ? sizeof(ulong) : sizeof(ushort);
            int count64 = 0, count128 = 0, count256 = 0;
            bool ok = true;
            int flushed = 0;

            NativeArray<GlyphCurveCache.Segment> segmentsNative = default;
            NativeArray<byte> alphaNative = default;
            NativeArray<SdfCore.GlyphTask> tasks = default;
            NativeArray<int> nextTask = default;
            NativeArray<float> scratch = default;
            try
            {
                segmentsNative = new NativeArray<GlyphCurveCache.Segment>(pendingSegments.count,
                    Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                if (pendingSegments.count > 0)
                    fixed (void* src = pendingSegments.data)
                        UnsafeUtility.MemCpy(segmentsNative.GetUnsafePtr(), src,
                            pendingSegments.count * sizeof(GlyphCurveCache.Segment));

                alphaNative = new NativeArray<byte>(pendingAlpha.count, Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                if (pendingAlpha.count > 0)
                    fixed (void* src = pendingAlpha.data)
                        UnsafeUtility.MemCpy(alphaNative.GetUnsafePtr(), src, pendingAlpha.count);

                tasks = new NativeArray<SdfCore.GlyphTask>(count, Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                int maxTileSize = 0;
                for (int i = 0; i < count; i++)
                {
                    var pg = pending[i];
                    int sizeClass = GetSizeClassFromEncoded(pg.encodedTile);
                    int tileSize = tileSizes[sizeClass];
                    DecodeTileXY(pg.encodedTile, tileSize, out int tilePxX, out int tilePxY);

                    if (sizeClass == 0) count64++; else if (sizeClass == 1) count128++; else count256++;
                    maxTileSize = Math.Max(maxTileSize, tileSize);

                    tasks[i] = new SdfCore.GlyphTask
                    {
                        segmentOffset = pg.segmentOffset,
                        segmentCount = pg.segmentCount,
                        tileSize = tileSize,
                        aspect = pg.aspect,
                        glyphH = pg.glyphH,
                        pageIndex = pg.pageIndex,
                        tileX = tilePxX,
                        tileY = tilePxY,
                        padNorm = pg.padNorm,
                        alphaOffset = pg.alphaOffset,
                        alphaWidth = pg.alphaWidth,
                        alphaHeight = pg.alphaHeight
                    };
                }

                int workerCapacity = forceSingleThreaded
                    ? 1
                    : Math.Min(count, Math.Max(1, JobsUtility.JobWorkerCount));
                int scratchFloatsPerWorker = msdf
                    ? SdfCore.MsdfScratchFloatsPerWorker(maxTileSize)
                    : SdfCore.SdfScratchFloatsPerWorker(maxTileSize);
                nextTask = new NativeArray<int>(1, Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                scratch = new NativeArray<float>(
                    checked(workerCapacity * scratchFloatsPerWorker), Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                timer.Mark();

                while (flushed < count)
                {
                    if (!TryPlanGlyphUploadChunk(tasks, flushed, bpp,
                            out int end, out int chunkBytes, out var uploadError))
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
                    if (!AcquireUploadSlot(chunkBytes, out var slot, out uploadError))
                    {
                        RecordFlushGpuUploadError(uploadError, ref transaction);
                        ok = false;
                        break;
                    }

                    int chunkCount = end - flushed;
                    var chunkTasks = tasks.GetSubArray(flushed, chunkCount);
                    var uploadBatch = default(GpuUploadBatch);
                    try
                    {
                        var taskPointers = CpuRasterTaskPointers(chunkCount);
                        PopulateRasterTaskPointers(slot.View, chunkTasks, bpp, taskPointers);
                        RunCpuRasterChunk(segmentsNative, alphaNative, chunkTasks, taskPointers,
                            nextTask, scratch, scratchFloatsPerWorker, msdf);

                        if (!BeginUploadBatch(out uploadBatch, out uploadError))
                        {
                            RecordFlushGpuUploadError(uploadError, ref transaction);
                            ok = false;
                            break;
                        }
                        bool valid = true;
                        long slotOffset = 0;
                        int regions = 0;
                        for (int i = 0; valid && i < chunkCount; i++)
                        {
                            var t = chunkTasks[i];
                            int tileBytes = t.tileSize * t.tileSize * bpp;
                            var region = GpuUploadRegion.ForLayers(0, t.pageIndex, 1,
                                t.tileX, t.tileY, t.tileSize, t.tileSize,
                                slotOffset, t.tileSize * bpp, tileBytes);
                            valid = uploadBatch.TryAddRegion(UploadTarget,
                                region, out uploadError);
                            slotOffset += tileBytes;
                            regions++;
                        }
                        if (!SubmitUploadBatch(ref uploadBatch, ref slot, chunkBytes,
                                valid, uploadError, ref transaction))
                        {
                            ok = false;
                            break;
                        }
                        NoteRegionsUploaded(regions, slotOffset);
                        for (int i = flushed; i < end; i++)
                        {
                            if (TryGetEntry(pending[i].key, out var flushedEntry))
                                WriteTransformRow(in flushedEntry);
                        }
                        flushed = end;
                    }
                    finally
                    {
                        uploadBatch.Dispose();
                        GpuUpload.ReleaseSlot(ref slot);
                    }
                }
            }
            finally
            {
                DisposeNative(ref scratch);
                DisposeNative(ref nextTask);
                DisposeNative(ref tasks);
                DisposeNative(ref alphaNative);
                DisposeNative(ref segmentsNative);
            }

            if (ok)
            {
                timer.Mark();
                var modeLabel = msdf ? "MSDF" : "SDF";
                CatZones.glyphAtlas.Meow($"[GlyphAtlas:{modeLabel}] Flushed {count} glyphs " +
                         $"(64px:{count64} 128px:{count128} 256px:{count256}), pages:{sliceCount} | " +
                         $"setup={timer.Phase(0):F1}ms raster+upload={timer.Phase(1):F1}ms total={timer.Total:F1}ms");
                pending.Clear();
                pendingSegments.FakeClear();
                pendingAlpha.FakeClear();
            }
            return ok;
        }

        /// <summary>Accumulates glyph tiles while the chunk fits the per-batch region cap and the backend staging bound — never the slot class: one flush produces the fewest possible submissions, so delivery cannot depend on mid-frame GPU retirement. The slot layer serves the resulting byte size from the resident ring or a transient slot.</summary>
        private bool TryPlanGlyphUploadChunk(NativeArray<SdfCore.GlyphTask> tasks, int start,
            int bpp, out int end, out int chunkBytes, out GpuUploadError error)
        {
            end = start;
            chunkBytes = 0;
            ulong stagingBytes = 0;
            int maxRegions = GpuUpload.MaxRegionsPerBatch;
            ulong maxStaging = GpuUpload.Info.MaxStagingBytes;
            while (end < tasks.Length && end - start < maxRegions)
            {
                var task = tasks[end];
                int tileBytes = checked(task.tileSize * task.tileSize * bpp);
                var region = GpuUploadRegion.ForLayers(0, task.pageIndex, 1,
                    task.tileX, task.tileY, task.tileSize, task.tileSize,
                    chunkBytes, task.tileSize * bpp, tileBytes);
                ulong candidate = stagingBytes;
                if (!GpuUpload.TryAccumulateStagingBytes(UploadTarget, region,
                        ref candidate, out error))
                    return false;
                if (candidate > maxStaging) break;
                stagingBytes = candidate;
                chunkBytes = checked(chunkBytes + tileBytes);
                end++;
            }
            if (end == start)
            {
                error = GpuUploadError.BackendFailed;
                return false;
            }
            error = GpuUploadError.None;
            return true;
        }

        private NativeArray<long> CpuRasterTaskPointers(int count)
        {
            if (!cpuRasterTaskPointers.IsCreated || cpuRasterTaskPointers.Length < count)
            {
                DisposeNative(ref cpuRasterTaskPointers);
                cpuRasterTaskPointers = new NativeArray<long>(count, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }
            return cpuRasterTaskPointers.GetSubArray(0, count);
        }

        private static unsafe void PopulateRasterTaskPointers(NativeArray<byte> bytes,
            NativeArray<SdfCore.GlyphTask> tasks, int bpp, NativeArray<long> pointers)
        {
            byte* basePointer = (byte*)bytes.GetUnsafePtr();
            long offset = 0;
            for (int i = 0; i < tasks.Length; i++)
            {
                pointers[i] = (long)(basePointer + offset);
                int tileSize = tasks[i].tileSize;
                offset += (long)tileSize * tileSize * bpp;
            }
        }

        private static void RunCpuRasterChunk(NativeArray<GlyphCurveCache.Segment> segments,
            NativeArray<byte> alpha,
            NativeArray<SdfCore.GlyphTask> tasks, NativeArray<long> taskPointers,
            NativeArray<int> nextTask, NativeArray<float> scratch,
            int scratchFloatsPerWorker, bool msdf)
        {
            int workerCount = forceSingleThreaded
                ? 1
                : Math.Min(tasks.Length, Math.Max(1, JobsUtility.JobWorkerCount));
            nextTask[0] = 0;
            if (msdf)
            {
                var job = new MsdfJob
                {
                    segments = segments,
                    tasks = tasks,
                    taskPointers = taskPointers,
                    nextTask = nextTask,
                    scratchBuffer = scratch,
                    maxScratchFloatsPerWorker = scratchFloatsPerWorker
                };
                if (forceSingleThreaded) job.Run(workerCount);
                else job.Schedule(workerCount, 1).Complete();
                return;
            }

            var sdfJob = new SdfJob
            {
                segments = segments,
                alpha = alpha,
                tasks = tasks,
                taskPointers = taskPointers,
                nextTask = nextTask,
                scratchBuffer = scratch,
                maxScratchFloatsPerWorker = scratchFloatsPerWorker
            };
            if (forceSingleThreaded) sdfJob.Run(workerCount);
            else sdfJob.Schedule(workerCount, 1).Complete();
        }
    }
}
