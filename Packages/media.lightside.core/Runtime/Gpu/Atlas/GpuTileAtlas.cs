using UnityEngine;

namespace LightSide
{
    /// <summary>Placement-only atlas entry for consumers without domain payload.</summary>
    public struct GpuTilePlacement : IGpuAtlasEntry
    {
        /// <inheritdoc/>
        public int EncodedTile { get; set; }

        /// <inheritdoc/>
        public int PageIndex { get; set; }

        /// <inheritdoc/>
        public int RefCount { get; set; }
    }

    /// <summary>
    /// Ready-to-use dynamic tile atlas over the <see cref="GpuAtlas{TEntry}"/> engine, for consumers
    /// whose tiles are plain pixel buffers (animated stickers, thumbnails, procedural sprites).
    /// Rent a keyed tile once, write pixels immutably or stream per-frame rewrites
    /// (<see cref="GpuAtlas{TEntry}.TryUpdateTilePixels"/> — latest-wins under backpressure),
    /// sample via <see cref="GetTileUv"/> + <see cref="GpuAtlas{TEntry}.AtlasTexture"/>. Keys are
    /// consumer-defined: identical content rented under one key is stored and uploaded once.
    /// Sources are the truth and the atlas is a cache — re-render on
    /// <see cref="GpuAtlas{TEntry}.AnyAtlasContentLost"/>.
    /// </summary>
    public sealed class GpuTileAtlas : GpuAtlas<GpuTilePlacement>
    {
        public GpuTileAtlas(GpuAtlasConfig config) : base(config) { }

        /// <summary>
        /// Returns the keyed tile, placing it on first rent (+1 reference either way). Pixels arrive
        /// separately: pass them here for the first fill, or stream them later. Release with
        /// <see cref="GpuAtlas{TEntry}.Release"/> when the consumer stops referencing the tile.
        /// </summary>
        public GpuTilePlacement Rent(long key, byte[] pixels = null, int w = 0, int h = 0, bool isBGRA = false)
        {
            if (!TryGetEntry(key, out var entry))
                entry = EnsureTilePixels(key, default, pixels, w, h, isBGRA);
            else if (pixels != null)
                TryUpdateTilePixels(key, pixels, w, h, isBGRA);
            AddRef(key);
            return entry;
        }

        /// <summary>Normalized UV rect of the tile's content area (gutter excluded) on its page layer.</summary>
        public Rect GetTileUv(in GpuTilePlacement placement)
        {
            int tileSize = TileSizeFromEncoded(placement.EncodedTile);
            DecodeTileXY(placement.EncodedTile, tileSize, out int x, out int y);
            const float invPage = 1f / PageSize;
            int gutter = config.TileGutter;
            return new Rect(
                (x + gutter) * invPage,
                (y + gutter) * invPage,
                (tileSize - 2 * gutter) * invPage,
                (tileSize - 2 * gutter) * invPage);
        }
    }
}
