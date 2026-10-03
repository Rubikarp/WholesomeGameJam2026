using UnityEngine;

namespace LightSide
{
    /// <summary>Adapts resolved text paint state to the shared projection-frame primitive.</summary>
    public static class PaintMappingMath
    {
        /// <summary>
        /// Builds the shared projection frame from a flattened runtime paint and text-selected bounds.
        /// Texture dimensions captured before parallel layout are applied only to texture sources.
        /// </summary>
        public static PaintFrame BuildFrame(in TextPaint paint, Rect bounds)
        {
            var projection = new PaintProjection
            {
                kind = paint.shape,
                fit = paint.fit,
                spread = paint.spread,
                angle = paint.angle,
                scale = paint.scale,
                offset = paint.offset,
            };
            return paint.kind == PaintSourceKind.Texture
                ? PaintProjectionMath.BuildTextureFrame(in projection, bounds,
                    paint.texWidth, paint.texHeight)
                : PaintProjectionMath.BuildGradientFrame(in projection, bounds);
        }
    }
}
