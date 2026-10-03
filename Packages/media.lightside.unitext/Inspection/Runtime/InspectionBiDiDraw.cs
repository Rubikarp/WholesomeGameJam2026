using UnityEngine;

namespace LightSide.Inspection
{
    /// <summary>
    /// Draws a direction arrow over each visual run so BiDi reordering (logical vs visual order) is visible.
    /// Run boxes are accumulated in a single pass per line (O(line glyphs × runs-in-line)) rather than
    /// re-scanning all glyphs per run.
    /// </summary>
    public static class InspectionBiDiDraw
    {
        private static readonly Color ltrColor = new(0.40f, 1f, 0.55f);
        private static readonly Color rtlColor = new(1f, 0.60f, 0.20f);

        private const float ShaftWidth = 3f;
        private const float HeadWidth = 7f;

        private static float[] minL, minT, maxR, maxB;
        private static bool[] rtl, used;

        public static void Draw(UniTextBase text, InspectionDrawList list)
        {
            if (text == null) return;
            var processor = text.TextProcessor;
            if (processor == null) return;

            var padded = text.GetPaddedRect();
            var glyphs = text.ResultGlyphs;
            var lines = processor.buf.lines.Span;
            var runs = processor.buf.orderedRuns.Span;

            for (var li = 0; li < lines.Length; li++)
            {
                ref readonly var line = ref lines[li];
                var n = line.runCount;
                if (n <= 0) continue;
                EnsureCapacity(n);

                for (var k = 0; k < n; k++)
                {
                    used[k] = false;
                    var ri = line.runStart + k;
                    rtl[k] = (uint)ri < (uint)runs.Length && runs[ri].direction == TextDirection.RightToLeft;
                }

                var gEnd = line.glyphStart + line.glyphCount;
                for (var gi = line.glyphStart; gi < gEnd && (uint)gi < (uint)glyphs.Length; gi++)
                {
                    var g = glyphs[gi];
                    for (var k = 0; k < n; k++)
                    {
                        var ri = line.runStart + k;
                        if ((uint)ri >= (uint)runs.Length) continue;
                        ref readonly var run = ref runs[ri];
                        if (g.cluster < run.range.start || g.cluster >= run.range.End) continue;

                        if (!used[k])
                        {
                            used[k] = true;
                            minL[k] = g.left; minT[k] = g.top; maxR[k] = g.right; maxB[k] = g.bottom;
                        }
                        else
                        {
                            if (g.left < minL[k]) minL[k] = g.left;
                            if (g.top < minT[k]) minT[k] = g.top;
                            if (g.right > maxR[k]) maxR[k] = g.right;
                            if (g.bottom > maxB[k]) maxB[k] = g.bottom;
                        }
                        break;
                    }
                }

                for (var k = 0; k < n; k++)
                {
                    if (!used[k]) continue;
                    var box = new Rect(padded.xMin + minL[k], padded.yMax - maxB[k], maxR[k] - minL[k], maxB[k] - minT[k]);
                    Arrow(list, box, rtl[k], rtl[k] ? rtlColor : ltrColor);
                }
            }
        }

        private static void EnsureCapacity(int n)
        {
            if (minL != null && minL.Length >= n) return;
            minL = new float[n]; minT = new float[n]; maxR = new float[n]; maxB = new float[n];
            rtl = new bool[n]; used = new bool[n];
        }

        private static void Arrow(InspectionDrawList list, Rect box, bool isRtl, Color color)
        {
            var y = box.center.y;
            var pad = Mathf.Min(6f, box.width * 0.2f);
            var x0 = box.xMin + pad;
            var x1 = box.xMax - pad;
            if (x1 <= x0) return;

            list.Line(new Vector2(x0, y), new Vector2(x1, y), color, ShaftWidth);

            var head = Mathf.Min(10f, box.height * 0.35f, (x1 - x0) * 0.5f);
            if (isRtl)
            {
                list.Line(new Vector2(x0, y), new Vector2(x0 + head, y + head), color, HeadWidth);
                list.Line(new Vector2(x0, y), new Vector2(x0 + head, y - head), color, HeadWidth);
            }
            else
            {
                list.Line(new Vector2(x1, y), new Vector2(x1 - head, y + head), color, HeadWidth);
                list.Line(new Vector2(x1, y), new Vector2(x1 - head, y - head), color, HeadWidth);
            }
        }
    }
}
