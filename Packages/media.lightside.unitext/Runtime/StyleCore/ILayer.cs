namespace LightSide
{
    /// <summary>A modifier that emits geometry at a layer position — effects, decorations, sub-meshes. The layer-sequence stamp orders only these; text/layout/attribute modifiers don't implement it.</summary>
    internal interface ILayer
    {
        /// <summary>Position in the flattened style order that orders this layer's emitted quads (the generator's single draw-order axis). Stamped once per rebuild.</summary>
        int LayerSequence { get; set; }

        /// <summary>Whether this layer defaults to rendering behind the glyph fill (outward effects: outer stroke, shadow, glow, extrude) instead of above it. Applied only when no explicit fill is present (<see cref="ClaimsFill"/>): the stamp places such layers below the implicit fill so a lone outline/shadow sits behind the text. With an explicit fill the author controls order through the style list. Read once per rebuild; per-range parameter overrides are not consulted.</summary>
        bool RendersBehindFill { get; }

        /// <summary>Whether this layer occupies the fill role (claims the base glyph face) — a FillModifier or a Replace-mode material. When any such layer is present, the stamp uses plain style-list order instead of the default behind-fill banding, so the author controls stacking explicitly.</summary>
        bool ClaimsFill { get; }
    }
}
