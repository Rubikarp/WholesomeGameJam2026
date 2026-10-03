using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Builds and queries allocation-free projection frames for gradients and textures.</summary>
    public static class PaintProjectionMath
    {
        /// <summary>
        /// Builds a frame over <paramref name="bounds"/> using the projection's rotation, scale, and
        /// offset. The frame geometry is independent of gradient kind and texture fitting.
        /// </summary>
        public static PaintFrame BuildFrame(in PaintProjection projection, Rect bounds)
        {
            switch (projection.kind)
            {
                case PaintProjectionKind.Linear:
                case PaintProjectionKind.Radial:
                case PaintProjectionKind.Angular:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(projection), projection.kind,
                        "Unknown paint projection kind.");
            }

            var rad = projection.angle * Mathf.Deg2Rad;
            var cos = Mathf.Cos(rad);
            var sin = Mathf.Sin(rad);

            var hx = bounds.width * 0.5f;
            var hy = bounds.height * 0.5f;
            var halfU = Mathf.Abs(hx * cos) + Mathf.Abs(hy * sin);
            var halfV = Mathf.Abs(hx * sin) + Mathf.Abs(hy * cos);

            var scale = projection.scale <= 1e-6f ? 1f : projection.scale;
            return new PaintFrame
            {
                centerX = bounds.center.x,
                centerY = bounds.center.y,
                cosA = cos,
                sinA = sin,
                halfU = (halfU <= 1e-6f ? 1f : halfU) / scale,
                halfV = (halfV <= 1e-6f ? 1f : halfV) / scale,
                offsetX = projection.offset.x,
                offsetY = projection.offset.y,
            };
        }

        /// <summary>
        /// Builds a frame and applies <see cref="PaintProjection.fit"/> for a texture with positive
        /// pixel dimensions. Stretch does not inspect the dimensions; all aspect-preserving modes
        /// reject invalid dimensions at this boundary.
        /// </summary>
        public static PaintFrame BuildTextureFrame(in PaintProjection projection, Rect bounds,
            int textureWidth, int textureHeight)
        {
            var frame = BuildFrame(in projection, bounds);
            ApplyTextureFit(ref frame, projection.fit, textureWidth, textureHeight);
            return frame;
        }

        /// <summary>
        /// Builds a frame for a gradient, applying <see cref="PaintProjection.fit"/> against the square
        /// source that <see cref="PaintProjectionKind.Radial"/> and <see cref="PaintProjectionKind.Angular"/>
        /// sample: anything but <see cref="PaintFit.Stretch"/> equalizes the axes, so a ring stays circular
        /// and a sweep keeps true angles on a non-square frame. <see cref="PaintProjectionKind.Linear"/>
        /// derives its parameter from one axis and is returned unfitted, since equalizing would change its span.
        /// </summary>
        public static PaintFrame BuildGradientFrame(in PaintProjection projection, Rect bounds)
        {
            var frame = BuildFrame(in projection, bounds);
            if (projection.kind.Resolved() != PaintProjectionKind.Linear)
                ApplyFit(ref frame, projection.fit, 1f);
            return frame;
        }

        /// <summary>
        /// Applies <paramref name="fit"/> for a texture, deriving the source aspect from positive pixel
        /// dimensions. Stretch does not inspect the dimensions; every other mode rejects invalid
        /// dimensions at this boundary.
        /// </summary>
        public static void ApplyTextureFit(ref PaintFrame frame, PaintFit fit,
            int textureWidth, int textureHeight)
        {
            if (fit.Resolved() == PaintFit.Stretch) return;
            if (textureWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(textureWidth), textureWidth,
                    "Texture width must be positive for aspect-preserving projection.");
            if (textureHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(textureHeight), textureHeight,
                    "Texture height must be positive for aspect-preserving projection.");

            ApplyFit(ref frame, fit, (float)textureWidth / textureHeight);
        }

        /// <summary>
        /// Fits a frame returned by <see cref="BuildFrame"/> to a source of the given width-over-height
        /// aspect: <see cref="PaintFit.Contain"/> and <see cref="PaintFit.Cover"/> equalize the axes
        /// through the shorter or longer extent, <see cref="PaintFit.Tile"/> sizes aspect-correct cells
        /// from the U axis, and <see cref="PaintFit.Stretch"/> leaves the frame untouched.
        /// </summary>
        public static void ApplyFit(ref PaintFrame frame, PaintFit fit, float sourceAspect)
        {
            fit = fit.Resolved();
            switch (fit)
            {
                case PaintFit.Stretch:
                    return;
                case PaintFit.Contain:
                case PaintFit.Cover:
                case PaintFit.Tile:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fit), fit, "Unknown paint fit.");
            }

            if (!(sourceAspect > 0f) || float.IsInfinity(sourceAspect))
                throw new ArgumentOutOfRangeException(nameof(sourceAspect), sourceAspect,
                    "The source aspect must be positive and finite.");

            if (fit == PaintFit.Tile)
            {
                frame.halfV = frame.halfU / sourceAspect;
                return;
            }

            var ratio = (frame.halfU / frame.halfV) / sourceAspect;
            float multiplierU, multiplierV;
            if (fit == PaintFit.Cover)
            {
                multiplierU = Mathf.Min(1f, ratio);
                multiplierV = Mathf.Min(1f, 1f / ratio);
            }
            else
            {
                multiplierU = Mathf.Max(1f, ratio);
                multiplierV = Mathf.Max(1f, 1f / ratio);
            }

            frame.halfU /= multiplierU;
            frame.halfV /= multiplierV;
        }

        /// <summary>
        /// Maps a source position to a normalized paint coordinate centred on [0,1] squared. Shaders
        /// derive linear, radial, or angular progress—or texture UVs—from the returned coordinate.
        /// </summary>
        public static Vector2 Coord(in PaintFrame frame, float x, float y)
        {
            var dx = x - frame.centerX;
            var dy = y - frame.centerY;
            var u = (dx * frame.cosA + dy * frame.sinA) / (2f * frame.halfU) + 0.5f - frame.offsetX;
            var v = (-dx * frame.sinA + dy * frame.cosA) / (2f * frame.halfV) + 0.5f - frame.offsetY;
            return new Vector2(u, v);
        }
    }
}
