using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Placement contract every <see cref="GpuAtlas{TEntry}"/> entry struct implements. The atlas
    /// engine reads and writes only these members; everything else in the entry is consumer payload
    /// carried at zero cost (the generic is monomorphized — no boxing, direct field access).
    /// </summary>
    public interface IGpuAtlasEntry
    {
        /// <summary>Packed tile id: size-class stride + shelf placement (+ consumer lanes above the placement span). Negative while the entry has no tile.</summary>
        int EncodedTile { get; set; }

        /// <summary>Atlas array layer the tile lives on.</summary>
        int PageIndex { get; set; }

        /// <summary>Live references from committed presentation. 0 = evictable (parked in the LRU).</summary>
        int RefCount { get; set; }
    }

    /// <summary>Mip policy of an atlas: none, or the truncated tile-aligned window (per-tile mips down to the last level where the tile size divides exactly; sampling must clamp to the window — see the max-LOD shader global).</summary>
    public enum GpuAtlasMips : byte
    {
        None,
        TileAlignedWindow,
    }

    /// <summary>
    /// Immutable construction-time configuration of a <see cref="GpuAtlas{TEntry}"/>.
    /// The format is explicit and never converted: source bytes must already carry the storage
    /// encoding (sRGB vs linear included) — the delivery layer performs no color-space conversion.
    /// </summary>
    public sealed class GpuAtlasConfig
    {
        /// <summary>Storage format of the atlas pages.</summary>
        public TextureFormat Format = TextureFormat.RGBA32;

        /// <summary>True to realize a linear (non-sRGB) texture.</summary>
        public bool Linear = true;

        /// <summary>Sampler filter of the pages.</summary>
        public FilterMode Filter = FilterMode.Bilinear;

        /// <summary>Sampler mip bias (no effect when <see cref="Mips"/> is None or the consumer samples with an explicit LOD).</summary>
        public float MipMapBias;

        /// <summary>Mip policy. TileAlignedWindow requires a 4-bytes-per-pixel format (the box downsampler works per RGBA texel).</summary>
        public GpuAtlasMips Mips = GpuAtlasMips.None;

        /// <summary>True when tiles arrive as ready pixel buffers (<c>EnsureTilePixels</c>/streaming); false when a consumer subclass rasterizes pending work itself in <c>FlushPendingWork</c>.</summary>
        public bool PixelTiles = true;

        /// <summary>Tile side per size class, ascending. A single entry makes a uniform grid.</summary>
        public int[] TileSizes;

        /// <summary>Transparent rim reserved inside each tile so bilinear taps never cross into a neighbour.</summary>
        public int TileGutter;

        /// <summary>Diagnostic label baked into every log line ("[MyAtlas:SDF] …") — keep it stable, downstream tooling may match on it.</summary>
        public string Label;

        /// <summary>Log zone the atlas meows into. Null uses the shared "GpuAtlas" zone.</summary>
        public CatZone? LogZone;

        /// <summary>Shader global receiving the written-mip-window max LOD (mip count − 1) whenever storage materializes, so samplers can clamp away the unwritten physical tail. Null skips the global.</summary>
        public string MaxLodShaderProperty;
    }
}
