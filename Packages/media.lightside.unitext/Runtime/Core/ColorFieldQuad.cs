using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Writes the SDF quad an effect samples over a colour glyph's silhouette field: the tile-padded
    /// glyph box of the field, placed over the colour face's final corners so every per-glyph transform
    /// the face received (scale, compression, wobble) carries over. The written quad obeys the outline
    /// quad contract — glyph-local UV0 with the default SDF padding, the field handle and glyph height,
    /// SDF mode in UV1 — so growth, coverage and paint treat it like any text quad.
    /// </summary>
    internal static class ColorFieldQuad
    {
        /// <summary>
        /// Builds the field quad of the colour face at <paramref name="src"/> into vertex slot
        /// <paramref name="dst"/>. Source and destination arrays may alias.
        /// </summary>
        public static void Write(Vector3[] srcVerts, float cluster,
            Vector3[] dstVerts, Vector4[] dstUv0, Vector4[] dstUv1, int src, int dst,
            in UniTextMeshGenerator.ColorFaceField field)
        {
            var bl = srcVerts[src];
            var tl = srcVerts[src + 1];
            var br = srcVerts[src + 3];

            var hx = br.x - bl.x;
            var hy = br.y - bl.y;
            var vx = tl.x - bl.x;
            var vy = tl.y - bl.y;

            var inkX = bl.x + hx * field.padFracX + vx * field.padFracY;
            var inkY = bl.y + hy * field.padFracX + vy * field.padFracY;
            var ihx = hx * (1f - 2f * field.padFracX);
            var ihy = hy * (1f - 2f * field.padFracX);
            var ivx = vx * (1f - 2f * field.padFracY);
            var ivy = vy * (1f - 2f * field.padFracY);

            var aspect = field.aspect;
            var maxDim = aspect > 1f ? aspect : 1f;
            var ex = (maxDim - aspect) * 0.5f + UniTextMeshGenerator.DefaultSdfPadding;
            var ey = (maxDim - 1f) * 0.5f + UniTextMeshGenerator.DefaultSdfPadding;
            var kx = ex / aspect;
            var spanX = (aspect + 2f * ex) / aspect;
            var spanY = 1f + 2f * ey;

            var qx = inkX - ihx * kx - ivx * ey;
            var qy = inkY - ihy * kx - ivy * ey;
            var z = bl.z;

            dstVerts[dst] = new Vector3(qx, qy, z);
            dstVerts[dst + 1] = new Vector3(qx + ivx * spanY, qy + ivy * spanY, z);
            dstVerts[dst + 2] = new Vector3(qx + ivx * spanY + ihx * spanX, qy + ivy * spanY + ihy * spanX, z);
            dstVerts[dst + 3] = new Vector3(qx + ihx * spanX, qy + ihy * spanX, z);

            var handle = (float)field.handle;
            var glyphH = field.glyphH;
            dstUv0[dst] = new Vector4(-ex, -ey, handle, glyphH);
            dstUv0[dst + 1] = new Vector4(-ex, 1f + ey, handle, glyphH);
            dstUv0[dst + 2] = new Vector4(aspect + ex, 1f + ey, handle, glyphH);
            dstUv0[dst + 3] = new Vector4(aspect + ex, -ey, handle, glyphH);

            dstUv1[dst] = new Vector4(aspect, 0f, cluster, 0f);
            dstUv1[dst + 1] = new Vector4(aspect, 0f, cluster, 0f);
            dstUv1[dst + 2] = new Vector4(aspect, 0f, cluster, 1f);
            dstUv1[dst + 3] = new Vector4(aspect, 0f, cluster, 1f);
        }
    }
}
