using System;

namespace LightSide
{
    /// <summary>
    /// The compact contracts of the per-codepoint font-style attribute buffers (bold weight, italic
    /// request) plus the synthetic-bold stroke ratio. Owned by the layout core: the face-resolution
    /// and shaping passes decode these; the style modifiers encode into them.
    /// </summary>
    internal static class FontStyleEncoding
    {
        /// <summary>
        /// FreeType's FT_GlyphSlot_Embolden ratio: em/24 total stroke width per unit weight.
        /// Used for both advance correction and dilate (shader applies × DILATE_SCALE = × 0.5).
        /// </summary>
        internal const float EmboldenRatio = 1f / 24f;

        internal const ushort BoldModeMask = 0xC000;
        internal const ushort BoldWeightMask = 0x03FF;
        internal const ushort BoldModeFake = 0x8000;
        internal const ushort BoldModeRealOnly = 0x4000;

        internal static ushort EncodeCssWeight(int cssWeight, ushort mode)
            => (ushort)(Math.Clamp(cssWeight, 1, 1000) | mode);

        internal static int DecodeCssWeight(ushort encoded) => encoded & BoldWeightMask;

        internal static int CssWeightMatchScore(int actual, int target)
        {
            if (target < 400)
                return actual <= target ? target - actual : 2000 + actual - target;
            if (target > 500)
                return actual >= target ? actual - target : 2000 + target - actual;
            if (actual >= target && actual <= 500)
                return actual - target;
            if (actual < target)
                return 2000 + target - actual;
            return 4000 + actual - 500;
        }

        internal const byte ItalicAuto = 1;
        internal const byte ItalicRealOnly = 2;
        internal const byte ItalicFakeUsesFontSlant = 255;

    }
}
