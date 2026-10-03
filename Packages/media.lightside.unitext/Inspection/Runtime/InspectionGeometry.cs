using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Inspection
{
    /// <summary>Appends hover-overlay primitives (glyph box model, baseline, run/line bounds, modifier ranges) into an <see cref="InspectionDrawList"/> in text-local space.</summary>
    public static class InspectionGeometry
    {
        private static readonly List<Rect> rangeRects = new(8);

        public static void Draw(UniTextBase text, in TextInspectionSnapshot snap,
            IReadOnlyList<ModifierInspection> modifiers, InspectionLayers layers, in InspectionPalette palette, InspectionDrawList list)
        {
            if (text == null || !snap.hit) return;
            var padded = text.GetPaddedRect();

            if ((layers & InspectionLayers.ModifierRanges) != 0 && modifiers != null)
            {
                for (var i = 0; i < modifiers.Count; i++)
                {
                    text.GetRangeBounds(modifiers[i].start, modifiers[i].end, rangeRects);
                    var c = palette.Modifier(i);
                    c.a = palette.modifierFillAlpha;
                    for (var r = 0; r < rangeRects.Count; r++)
                        list.Rect(rangeRects[r], c, true);
                }
            }

            var hasLineBox = false;
            Rect lineBox = default;
            if (snap.line.valid && (layers & (InspectionLayers.LineBounds | InspectionLayers.Baseline)) != 0)
            {
                text.GetRangeBounds(snap.line.range.start, snap.line.range.End, rangeRects);
                hasLineBox = TryUnion(rangeRects, out lineBox);
            }

            if ((layers & InspectionLayers.LineBounds) != 0 && hasLineBox)
                list.Rect(lineBox, palette.line, false);

            if ((layers & InspectionLayers.RunBounds) != 0 && snap.run.valid &&
                TryGlyphRangeBox(text, snap.run.range, out var runBox))
                list.Rect(runBox, palette.run, false);

            if ((layers & InspectionLayers.Baseline) != 0 && hasLineBox)
            {
                var y = padded.yMax - snap.glyph.y;
                list.Line(new Vector2(lineBox.xMin, y), new Vector2(lineBox.xMax, y), palette.baseline);
            }

            if ((layers & InspectionLayers.GlyphBox) != 0)
            {
                var g = snap.glyph;
                list.Rect(new Rect(padded.xMin + g.left, padded.yMax - g.bottom, g.right - g.left, g.bottom - g.top),
                    g.isNotdef ? palette.notdef : palette.glyph, false);
            }

            if ((layers & InspectionLayers.Advance) != 0)
            {
                var g = snap.glyph;
                var y = padded.yMax - g.y;
                var tick = Mathf.Max(2f, (g.bottom - g.top) * 0.5f);
                var x0 = padded.xMin + g.x;
                var x1 = x0 + g.advanceX;
                list.Line(new Vector2(x0, y - tick), new Vector2(x0, y + tick), palette.advance);
                list.Line(new Vector2(x1, y - tick), new Vector2(x1, y + tick), palette.advance);
            }
        }

        /// <summary>Union box (local space) of every positioned glyph whose cluster falls in <paramref name="range"/>, or false if none.</summary>
        public static bool TryGlyphRangeBox(UniTextBase text, in TextRange range, out Rect box)
        {
            box = default;
            if (text == null) return false;

            var padded = text.GetPaddedRect();
            var glyphs = text.ResultGlyphs;
            float minL = float.MaxValue, minT = float.MaxValue, maxR = float.MinValue, maxB = float.MinValue;
            var any = false;

            for (var i = 0; i < glyphs.Length; i++)
            {
                var g = glyphs[i];
                if (g.cluster < range.start || g.cluster >= range.End) continue;
                if (g.left < minL) minL = g.left;
                if (g.top < minT) minT = g.top;
                if (g.right > maxR) maxR = g.right;
                if (g.bottom > maxB) maxB = g.bottom;
                any = true;
            }

            if (!any) return false;
            box = new Rect(padded.xMin + minL, padded.yMax - maxB, maxR - minL, maxB - minT);
            return true;
        }

        private static bool TryUnion(List<Rect> rects, out Rect union)
        {
            union = default;
            if (rects == null || rects.Count == 0) return false;

            union = rects[0];
            for (var i = 1; i < rects.Count; i++)
            {
                var r = rects[i];
                union = Rect.MinMaxRect(
                    Mathf.Min(union.xMin, r.xMin), Mathf.Min(union.yMin, r.yMin),
                    Mathf.Max(union.xMax, r.xMax), Mathf.Max(union.yMax, r.yMax));
            }
            return true;
        }
    }
}
