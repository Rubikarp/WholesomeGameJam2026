using System;

namespace LightSide
{
    /// <summary>Observable result categories changed by one completed text-pipeline pass.</summary>
    [Flags]
    public enum UniTextCommitChanges : byte
    {
        /// <summary>No observable output changed.</summary>
        None = 0,
        /// <summary>Parsed text, shaping, ranges, or their associated data changed.</summary>
        Content = 1 << 0,
        /// <summary>Line breaking or positioned glyph placement changed.</summary>
        Layout = 1 << 1,
        /// <summary>Final glyph faces changed after mesh modifiers.</summary>
        GlyphGeometry = 1 << 2,
        /// <summary>Mesh paint, material inputs, or auxiliary presentation changed without moving glyph faces.</summary>
        Appearance = 1 << 3,
        /// <summary>Every observable output category may have changed.</summary>
        All = Content | Layout | GlyphGeometry | Appearance,
    }

    /// <summary>Which stage of the text pipeline a change invalidates. Each flag names the coarsest work that
    /// must rerun; higher stages subsume every cheaper one below. <c>SetDirty</c> re-enters the pipeline at
    /// the coarsest flag set, so pick the least expensive stage that still captures the change.</summary>
    [Flags]
    public enum UniTextDirty
    {
        /// <summary>No rebuild needed.</summary>
        None = 0,
        /// <summary>Rebuild the mesh (vertices, UVs, colours, indices) from cached glyph positions. The cheapest stage.</summary>
        Mesh = 1 << 0,
        /// <summary>Re-place glyphs within their existing line breaks — alignment, justification, vertical metrics.</summary>
        Positions = 1 << 1,
        /// <summary>Re-break lines and reflow. Font-size changes live here: they alter advances, so lines recompute.</summary>
        Layout = 1 << 2,
        /// <summary>Re-parse and re-shape: the text content changed.</summary>
        Text = 1 << 3,
        /// <summary>Font asset changed. Resets the font provider and mesh generator, then fully rebuilds.</summary>
        Font = 1 << 4,
        /// <summary>Base text direction changed.</summary>
        Direction = 1 << 5,
        /// <summary>Text, font, or direction changed — a full re-parse and reshape.</summary>
        FullRebuild = Text | Font | Direction,
        /// <summary>Everything needs rebuilding.</summary>
        All = Mesh | Positions | Layout | FullRebuild
    }
}
