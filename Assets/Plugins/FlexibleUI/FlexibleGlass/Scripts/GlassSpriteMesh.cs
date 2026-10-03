using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;

namespace JeffGrawAssets.FlexibleUI
{
internal static class GlassSpriteMesh
{
    internal const int TileCount = 16;

    internal struct Triangle
    {
        public Vector4 origin;
        public Vector4 axisU;
        public Vector4 axisV;

        public bool TryGetUv(Vector2 position, out Vector2 uv)
        {
            var delta = position - new Vector2(origin.x, origin.y);
            var u = delta.x * axisU.x + delta.y * axisU.y;
            var v = delta.x * axisV.x + delta.y * axisV.y;
            uv = new Vector2(origin.z + u * axisU.z + v * axisV.z, origin.w + u * axisU.w + v * axisV.w);
            return u >= -1e-6f && v >= -1e-6f && u + v <= 1f + 1e-6f;
        }
    }

    internal static void Build(Sprite sprite, List<Triangle> triangles, List<Vector4> bounds)
    {
        triangles.Clear();
        bounds.Clear();
        var vertices = sprite.vertices;
        var uvs = sprite.uv;
        var indices = sprite.triangles;
        var size = sprite.rect.size;
        var pivot = sprite.pivot;
        var pixelsPerUnit = sprite.pixelsPerUnit;
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            var a = Normalize(vertices[indices[i]], pixelsPerUnit, pivot, size);
            var b = Normalize(vertices[indices[i + 1]], pixelsPerUnit, pivot, size);
            var c = Normalize(vertices[indices[i + 2]], pixelsPerUnit, pivot, size);
            if (!TryCreate(a, b, c, uvs[indices[i]], uvs[indices[i + 1]], uvs[indices[i + 2]], out var triangle))
                continue;
            triangles.Add(triangle);
            var min = Vector2.Min(a, Vector2.Min(b, c));
            var max = Vector2.Max(a, Vector2.Max(b, c));
            bounds.Add(new Vector4(min.x, min.y, max.x, max.y));
        }
    }

    internal sealed class Sampler
    {
        private Sprite source;
        private Texture texture;
        private Vector4 textureUv;
        private uint updateCount;
        private Vector2 sourceHalfTexel;
        private Vector2 atlasHalfTexel;
        private readonly List<Triangle> triangles = new();
        private readonly List<Vector4> bounds = new();

        internal bool TryGetTextureUv(Sprite sprite, Vector2 position, out Vector2 uv)
        {
            var currentTexture = sprite.texture;
            var currentUv = DataUtility.GetOuterUV(sprite);
            if (source != sprite || texture != currentTexture || textureUv != currentUv || updateCount != currentTexture.updateCount)
            {
                source = sprite;
                texture = currentTexture;
                textureUv = currentUv;
                updateCount = currentTexture.updateCount;
                Build(sprite, triangles, bounds);
                var spritePixels = sprite.rect.size * sprite.spriteAtlasTextureScale;
                sourceHalfTexel = new Vector2(0.5f / spritePixels.x, 0.5f / spritePixels.y);
                atlasHalfTexel = new Vector2(0.5f / currentTexture.width, 0.5f / currentTexture.height);
            }
            // Preserve GetPixelBilinear's texel convention through the mesh UV transform.
            position += sourceHalfTexel;
            foreach (var triangle in triangles)
                if (triangle.TryGetUv(position, out uv))
                {
                    uv -= atlasHalfTexel;
                    return true;
                }
            uv = default;
            return false;
        }
    }

    private static Vector2 Normalize(Vector2 vertex, float pixelsPerUnit, Vector2 pivot, Vector2 size) =>
        new Vector2((vertex.x * pixelsPerUnit + pivot.x) / size.x, (vertex.y * pixelsPerUnit + pivot.y) / size.y);

    private static bool TryCreate(Vector2 a, Vector2 b, Vector2 c, Vector2 uvA, Vector2 uvB, Vector2 uvC, out Triangle triangle)
    {
        var ab = b - a;
        var ac = c - a;
        var determinant = ab.x * ac.y - ab.y * ac.x;
        triangle = default;
        if (Mathf.Abs(determinant) < 1e-12f)
            return false;
        var inverse = 1f / determinant;
        triangle.origin = new Vector4(a.x, a.y, uvA.x, uvA.y);
        triangle.axisU = new Vector4(ac.y * inverse, -ac.x * inverse, uvB.x - uvA.x, uvB.y - uvA.y);
        triangle.axisV = new Vector4(-ab.y * inverse, ab.x * inverse, uvC.x - uvA.x, uvC.y - uvA.y);
        return true;
    }
}
}
