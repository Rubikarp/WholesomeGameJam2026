namespace LightSide.Samples
{
    internal sealed class LineHeightSlide : BasicUsageSlide
    {
        public override string Text =>
            "↕ <b>Line height — modes · units · leading</b>\n\n" +
            "<line-height=160%>Multiplier 1.6×\nrow two\nrow three</line-height>\n\n" +
            "<line-height=52>Absolute 52px rows\nfixed regardless of glyphs</line-height>\n\n" +
            "<line-height=,primary>Primary mode: tall fallback 🎉😀 glyphs\ndo not stretch the line box</line-height>";
    }
}
