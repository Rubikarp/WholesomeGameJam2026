using Unity.Burst;

namespace LightSide
{
    /// <summary>
    /// Shared contracts and scratch sizing for SDF and MSDF tile generation.
    /// </summary>
    [BurstCompile]
    internal static class SdfCore
    {
        internal static int SdfScratchFloatsPerWorker(int tileSize)
        {
            int pixels = checked(tileSize * tileSize);
            return checked(pixels * 4 + (pixels + 3) / 4);
        }

        internal static int MsdfScratchFloatsPerWorker(int tileSize)
        {
            int pixels = checked(tileSize * tileSize);
            return checked(pixels * 7 + (pixels + 3) / 4);
        }

        internal struct GlyphTask
        {
            public int segmentOffset;
            public int segmentCount;
            public int tileSize;
            public float aspect;
            public float glyphH;
            public int pageIndex;
            public int tileX;
            public int tileY;
            /// <summary>Reserved rim (glyph-height units) for this glyph's pad tier — sets the glyph scale in the tile and how far the distance field is computed beyond the glyph.</summary>
            public float padNorm;

            /// <summary>Start of this task's alpha bitmap in the job's alpha array; a task whose <see cref="alphaWidth"/> is positive is a bitmap silhouette and carries no segments.</summary>
            public int alphaOffset;

            /// <summary>Alpha bitmap width in pixels; 0 for a contour task.</summary>
            public int alphaWidth;

            /// <summary>Alpha bitmap height in pixels, rows stored top-down.</summary>
            public int alphaHeight;
        }

    }
}
