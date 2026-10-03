using System;

namespace LightSide
{
    /// <summary>
    /// The per-codepoint silhouette-field request colour-glyph effects leave for the atlas pipeline
    /// (<see cref="AttributeKeys.ColorGlyphField"/>): the largest outward reach in em any applying layer
    /// needs on that codepoint, quantized to a byte where 0 means no field. The pad tier the field is
    /// rasterized at follows from the request and the glyph's own height, so the first raster already
    /// holds the rim the effect samples.
    /// </summary>
    internal static class ColorGlyphField
    {
        private const float Step = GlyphAtlas.Pad / 254f;

        /// <summary>Encodes an outward reach in em, clamped to the field spread, as a non-zero request.</summary>
        internal static byte Encode(float extentEm)
        {
            if (!(extentEm > 0f)) return 1;
            if (extentEm > GlyphAtlas.Pad) extentEm = GlyphAtlas.Pad;
            return (byte)(1 + (int)Math.Ceiling(extentEm / Step));
        }

        /// <summary>The outward reach in em a request encodes; 0 for no request.</summary>
        internal static float Decode(byte request) => request == 0 ? 0f : (request - 1) * Step;

        /// <summary>Raises the request over <c>[start, end)</c>; the larger reach wins on every codepoint it covers.</summary>
        internal static void Request(PooledArrayAttribute<byte> attribute, int start, int end, float extentEm)
        {
            if (attribute == null) return;
            var count = attribute.Count;
            if (count == 0) count = attribute.buffer.data?.Length ?? 0;
            if (start < 0) start = 0;
            if (end > count) end = count;
            if (start >= end) return;
            var code = Encode(extentEm);
            var span = attribute.WritableSpan(new TextRange(start, end - start));
            for (var i = 0; i < span.Length; i++)
                if (span[i] < code) span[i] = code;
        }

        /// <summary>Pad tier a request needs on a glyph <paramref name="glyphH"/> em tall, seam margin included.</summary>
        internal static byte TierFor(byte request, float glyphH)
        {
            if (glyphH < 1e-6f) return 0;
            var padGlyph = GlyphAtlas.Pad / glyphH;
            var norm = Decode(request) / glyphH;
            if (norm > padGlyph) norm = padGlyph;
            if (norm <= UniTextMeshGenerator.DefaultSdfPadding) return 0;
            return (byte)GlyphAtlas.PadTierForExtent(norm + GlyphAtlas.TierSeamMarginNorm, glyphH);
        }
    }
}
