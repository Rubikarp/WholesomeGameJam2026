using UnityEngine;

namespace LightSide.Inspection
{
    /// <summary>Whole-text highlight filter: marks every glyph matching a criterion, independent of hover.</summary>
    public enum InspectionFilter
    {
        None,
        Notdef,
        Fallback,
        Rtl
    }

    /// <summary>Appends a box around every glyph that matches the active <see cref="InspectionFilter"/> into an <see cref="InspectionDrawList"/>.</summary>
    public static class InspectionFilterDraw
    {
        public static void Draw(UniTextBase text, InspectionFilter filter, InspectionDrawList list)
        {
            if (text == null || filter == InspectionFilter.None) return;

            var processor = text.TextProcessor;
            if (processor == null) return;

            var padded = text.GetPaddedRect();
            var glyphs = text.ResultGlyphs;
            var buf = processor.buf;
            var levels = buf.bidiLevels.data;
            var cpCount = buf.codepoints.count;
            var primaryId = text.FontProvider != null ? text.FontProvider.PrimaryFontId : 0;
            var color = ColorFor(filter);

            for (var i = 0; i < glyphs.Length; i++)
            {
                var g = glyphs[i];
                if (!Matches(filter, g, primaryId, levels, cpCount)) continue;
                list.Rect(new Rect(padded.xMin + g.left, padded.yMax - g.bottom, g.right - g.left, g.bottom - g.top), color, false);
            }
        }

        private static bool Matches(InspectionFilter filter, in PositionedGlyph g, int primaryId, byte[] levels, int cpCount)
        {
            return filter switch
            {
                InspectionFilter.Notdef => g.glyphId == 0,
                InspectionFilter.Fallback => primaryId != 0 && g.fontId != primaryId,
                InspectionFilter.Rtl => levels != null && (uint)g.cluster < (uint)cpCount &&
                                        g.cluster < levels.Length && (levels[g.cluster] & 1) != 0,
                _ => false
            };
        }

        private static Color ColorFor(InspectionFilter filter)
        {
            return filter switch
            {
                InspectionFilter.Notdef => new Color(1f, 0.30f, 0.28f),
                InspectionFilter.Fallback => new Color(1f, 0.60f, 0.20f),
                InspectionFilter.Rtl => new Color(0.40f, 0.80f, 1f),
                _ => Color.white
            };
        }
    }
}
