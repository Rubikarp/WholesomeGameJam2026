using UnityEngine;

namespace LightSide.Samples
{
    internal sealed class HighlightMappingsSlide : WidthAnimatedSlide
    {
        public override string Text =>
            "🧩 <b>Highlight Geometry Mapping</b>\n\n" +
            "<b><color=#B79CFF>Glyph</color></b>  <highlight-glyph>Mapping follows these wrapped words.</highlight-glyph>\n\n" +
            "<b><color=#62D9FF>Line</color></b>  <highlight-line>Mapping follows these wrapped words.</highlight-line>\n\n" +
            "<b><color=#FFB45E>Range</color></b>  <highlight-range>Mapping follows these wrapped words.</highlight-range>\n\n" +
            "<b><color=#70E0A0>Block</color></b>  <highlight-block>Mapping follows these wrapped words.</highlight-block>\n\n" +
            "<size=64%><color=#888>Width changes continuously. Glyph rounds every cluster; Line rounds every visual fragment; Range rounds only logical endpoints; Block joins all lines. PaintMapping stays Range.</color></size>";

        public override void Register(BasicUsageExampleBase example)
        {
            AddMapping(example, "highlight-glyph", GeometryMapping.Glyph,
                new Color32(139, 92, 246, 110));
            AddMapping(example, "highlight-line", GeometryMapping.Line,
                new Color32(14, 165, 233, 110));
            AddMapping(example, "highlight-range", GeometryMapping.Range,
                new Color32(249, 115, 22, 110));
            AddMapping(example, "highlight-block", GeometryMapping.Block,
                new Color32(34, 197, 94, 110));
        }

        private static void AddMapping(BasicUsageExampleBase example, string tag,
            GeometryMapping mapping, Color32 color)
        {
            example.AddStyle(new HighlightModifier
            {
                Paint = PaintRef.Solid(color),
                GeometryMapping = mapping,
                PaintMapping = RangePaintMapping.Range,
                Height = RangeHeight.Content,
                Padding = new UnitVector2(new Vector2(0.1f, 0.06f), UnitKind.Em),
                CornerRadius = UnitValue.Em(0.22f),
            }, new TagRule(tag));
        }
    }
}
