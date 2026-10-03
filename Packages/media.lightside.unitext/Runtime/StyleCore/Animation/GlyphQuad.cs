using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Vertex-level operations on one glyph quad (4 vertices at a base index, order BL-TL-TR-BR) for
    /// modifiers that transform glyphs during <see cref="UniTextMeshGenerator.onGlyph"/>.
    /// </summary>
    public static class GlyphQuad
    {
        /// <summary>Geometric center of the quad.</summary>
        public static Vector2 Center(Vector3[] verts, int baseIdx)
        {
            ref readonly var bl = ref verts[baseIdx];
            ref readonly var tr = ref verts[baseIdx + 2];
            return new Vector2((bl.x + tr.x) * 0.5f, (bl.y + tr.y) * 0.5f);
        }

        public static void Offset(Vector3[] verts, int baseIdx, float dx, float dy)
        {
            for (var i = 0; i < 4; i++)
            {
                ref var v = ref verts[baseIdx + i];
                v.x += dx;
                v.y += dy;
            }
        }

        /// <summary>Rotates the quad around a pivot. Positive radians rotate counter-clockwise.</summary>
        public static void Rotate(Vector3[] verts, int baseIdx, float pivotX, float pivotY, float radians)
        {
            var sin = Mathf.Sin(radians);
            var cos = Mathf.Cos(radians);
            for (var i = 0; i < 4; i++)
            {
                ref var v = ref verts[baseIdx + i];
                var dx = v.x - pivotX;
                var dy = v.y - pivotY;
                v.x = pivotX + dx * cos - dy * sin;
                v.y = pivotY + dx * sin + dy * cos;
            }
        }

        public static void Scale(Vector3[] verts, int baseIdx, float pivotX, float pivotY, float scaleX, float scaleY)
        {
            for (var i = 0; i < 4; i++)
            {
                ref var v = ref verts[baseIdx + i];
                v.x = pivotX + (v.x - pivotX) * scaleX;
                v.y = pivotY + (v.y - pivotY) * scaleY;
            }
        }
    }
}
