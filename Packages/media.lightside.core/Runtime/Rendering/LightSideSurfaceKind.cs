namespace LightSide
{
    /// <summary>
    /// What a quad in a LightSide mesh draws. Rides the vertex stream in <c>TEXCOORD1.w</c> (packed as
    /// <c>fraction + 2 * kind</c>), never a shader keyword — a keyword would split the material and with
    /// it the Canvas batch that one shared surface shader exists to keep whole.
    /// </summary>
    /// <remarks>C# mirror of <c>LightSideSurface.hlsl</c>; the two must agree.</remarks>
    public enum LightSideSurfaceKind
    {
        /// <summary>Single-channel signed-distance glyph.</summary>
        GlyphSdf = 0,

        /// <summary>Multi-channel signed-distance glyph.</summary>
        GlyphMsdf = 1,

        /// <summary>Bitmap colour glyph (emoji).</summary>
        GlyphColor = 2,

        /// <summary>Analytic vector shape — rounded rect, polygon, and the range decorations built on them.</summary>
        Shape = 3,

        /// <summary>Plain textured quad from an array atlas.</summary>
        AtlasQuad = 4,
    }

    /// <summary>Packs and unpacks the <c>TEXCOORD1.w</c> discriminator word shared by every LightSide surface.</summary>
    public static class LightSideSurface
    {
        /// <summary>Packs a surface kind with a free 0..1 per-surface value.</summary>
        public static float Pack(LightSideSurfaceKind kind, float fraction = 0f)
            => fraction + 2f * (int)kind;

        /// <summary>The surface kind encoded in <paramref name="packed"/>.</summary>
        public static LightSideSurfaceKind KindOf(float packed)
            => (LightSideSurfaceKind)(int)(packed * 0.5f);
    }
}
