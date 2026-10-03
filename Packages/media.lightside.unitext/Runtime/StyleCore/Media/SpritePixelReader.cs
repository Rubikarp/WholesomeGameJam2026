using System;
using Unity.Collections;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Reads a sprite's pixels through the GPU — a blit of its texture region into a scratch render
    /// target and a synchronous read-back — so a silhouette can be derived from any sprite whatever its
    /// texture's compression or readability. Rows come back top-down, straight alpha; a tightly packed
    /// sprite is masked to its own mesh so neighbouring sprites' pixels never enter its silhouette.
    /// Main thread only.
    /// </summary>
    internal static class SpritePixelReader
    {
        private static Texture2D scratch;

#if UNITY_EDITOR
        static SpritePixelReader() => EditorLifecycle.UnmanagedCleaning += ReleaseScratch;
#endif

        /// <summary>
        /// Reads the sprite's rect region into a pooled RGBA buffer no larger than <paramref name="maxSize"/>
        /// on either side (a larger region is downscaled on the GPU, aspect kept). The caller returns the
        /// buffer to <see cref="ArrayPool{T}"/>.
        /// </summary>
        public static byte[] Read(in SpriteFont.SpriteGlyph glyph, int maxSize, out int width, out int height)
        {
            var texture = glyph.texture;
            var span = glyph.uvMax - glyph.uvMin;
            var sourceWidth = Mathf.Max(1f, span.x * texture.width);
            var sourceHeight = Mathf.Max(1f, span.y * texture.height);
            var fit = Mathf.Min(1f, maxSize / Mathf.Max(sourceWidth, sourceHeight));
            width = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * fit));
            height = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * fit));

            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, target, span, glyph.uvMin);
                RenderTexture.active = target;
                if (scratch == null || scratch.width != width || scratch.height != height)
                {
                    ReleaseScratch();
                    scratch = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                    {
                        name = "UniText Sprite Readback",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }
                scratch.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }

            var raw = scratch.GetRawTextureData<byte>();
            var rowBytes = width * 4;
            var pixels = ArrayPool<byte>.Rent(rowBytes * height);
            for (var y = 0; y < height; y++)
                NativeArray<byte>.Copy(raw, y * rowBytes, pixels, (height - 1 - y) * rowBytes, rowBytes);

            if (glyph.meshPositions != null)
                MaskToMesh(glyph.meshPositions, glyph.meshTriangles, pixels, width, height);

            return pixels;
        }

        /// <summary>Clears every pixel outside the sprite's own mesh, given in coordinates normalized over the read region.</summary>
        private static void MaskToMesh(Vector2[] positions, ushort[] triangles, byte[] pixels, int width, int height)
        {
            var count = width * height;
            var mask = ArrayPool<byte>.Rent(count);
            try
            {
                Array.Clear(mask, 0, count);
                for (var t = 0; t + 2 < triangles.Length; t += 3)
                {
                    var a = positions[triangles[t]];
                    var b = positions[triangles[t + 1]];
                    var c = positions[triangles[t + 2]];
                    FillTriangle(mask, width, height,
                        a.x * width, a.y * height,
                        b.x * width, b.y * height,
                        c.x * width, c.y * height);
                }

                var rowBytes = width * 4;
                for (var y = 0; y < height; y++)
                {
                    var row = (height - 1 - y) * rowBytes;
                    for (var x = 0; x < width; x++)
                    {
                        if (mask[y * width + x] != 0) continue;
                        var p = row + x * 4;
                        pixels[p] = pixels[p + 1] = pixels[p + 2] = pixels[p + 3] = 0;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Return(mask);
            }
        }

        private static void FillTriangle(byte[] mask, int width, int height,
            float ax, float ay, float bx, float by, float cx, float cy)
        {
            var area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (Mathf.Abs(area) < 1e-6f) return;
            var sign = area > 0f ? 1f : -1f;

            var x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx))));
            var x1 = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx))));
            var y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, cy))));
            var y1 = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, cy))));

            for (var y = y0; y <= y1; y++)
            {
                var py = y + 0.5f;
                for (var x = x0; x <= x1; x++)
                {
                    var px = x + 0.5f;
                    var e0 = ((bx - ax) * (py - ay) - (by - ay) * (px - ax)) * sign;
                    var e1 = ((cx - bx) * (py - by) - (cy - by) * (px - bx)) * sign;
                    var e2 = ((ax - cx) * (py - cy) - (ay - cy) * (px - cx)) * sign;
                    if (e0 >= -0.5f && e1 >= -0.5f && e2 >= -0.5f)
                        mask[y * width + x] = 1;
                }
            }
        }

        private static void ReleaseScratch()
        {
            if (scratch == null) return;
            ObjectUtils.SafeDestroy(scratch);
            scratch = null;
        }
    }
}
