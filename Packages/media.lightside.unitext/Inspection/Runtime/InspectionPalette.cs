using UnityEngine;

namespace LightSide.Inspection
{
    /// <summary>Colors for overlay layers. Modifier spans cycle through a distinct palette by index.</summary>
    public struct InspectionPalette
    {
        public Color glyph;
        public Color notdef;
        public Color baseline;
        public Color advance;
        public Color run;
        public Color line;
        public float modifierFillAlpha;

        private static readonly Color[] modifierColors =
        {
            new(0.40f, 0.73f, 1f), new(0.36f, 1f, 0.73f), new(1f, 0.98f, 0.36f),
            new(1f, 0.70f, 0.39f), new(1f, 0.57f, 0.97f), new(1f, 0.45f, 0.42f),
            new(0.59f, 0.35f, 1f), new(0.28f, 0.55f, 1f), new(0.44f, 1f, 0.34f)
        };

        public Color Modifier(int index)
        {
            var n = modifierColors.Length;
            return modifierColors[((index % n) + n) % n];
        }

        public static InspectionPalette Default => new()
        {
            glyph = new Color(0.40f, 0.73f, 1f, 0.9f),
            notdef = new Color(1f, 0.30f, 0.28f, 1f),
            baseline = new Color(1f, 0.85f, 0.30f, 0.8f),
            advance = new Color(0.70f, 0.70f, 0.70f, 0.7f),
            run = new Color(0.44f, 1f, 0.55f, 0.7f),
            line = new Color(0.40f, 0.60f, 1f, 0.5f),
            modifierFillAlpha = 0.22f
        };
    }
}
