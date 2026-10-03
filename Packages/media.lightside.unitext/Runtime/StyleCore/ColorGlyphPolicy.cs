namespace LightSide
{
    /// <summary>Whether a layer effect decorates the colour glyphs — emoji and inline sprites — inside its range.</summary>
    public enum ColorGlyphPolicy : sbyte
    {
        /// <summary>No value at this layer (see <see cref="PaintInherit"/>); an unresolved chain means the effect's own default.</summary>
        Inherit = PaintInherit.None,

        /// <summary>The effect leaves colour glyphs untouched.</summary>
        Skip = 0,

        /// <summary>The effect renders on the glyph's silhouette, a distance field the atlas derives from the bitmap's alpha at the 50% threshold.</summary>
        Apply = 1
    }
}
