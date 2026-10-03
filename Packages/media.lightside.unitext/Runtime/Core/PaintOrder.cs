namespace LightSide
{
    /// <summary>
    /// Painter-order policy for the paint layers of a glyph range (see
    /// <see cref="UniTextBuffers.paintOrders"/>). Applies to same-material quads of the base mesh;
    /// separate-material output (texture paints, other segments) always stacks layer-major.
    /// </summary>
    public enum PaintOrder : byte
    {
        /// <summary>Layer-major: each layer completes across every glyph before the next layer draws, so every stroke sits under every fill.</summary>
        Layer = 0,

        /// <summary>Glyph-major: each glyph composites all of its layers before the next glyph draws, so a later glyph's outer layers overlap earlier glyphs.</summary>
        Glyph = 1
    }
}
