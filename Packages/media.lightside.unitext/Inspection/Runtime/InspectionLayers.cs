using System;

namespace LightSide.Inspection
{
    /// <summary>Toggleable overlay visualizations. Combine as flags.</summary>
    [Flags]
    public enum InspectionLayers
    {
        None = 0,
        GlyphBox = 1 << 0,
        Baseline = 1 << 1,
        Advance = 1 << 2,
        RunBounds = 1 << 3,
        LineBounds = 1 << 4,
        ModifierRanges = 1 << 5,

        Default = GlyphBox | Baseline | RunBounds | ModifierRanges
    }
}
