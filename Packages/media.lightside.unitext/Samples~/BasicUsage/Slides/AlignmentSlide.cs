namespace LightSide.Samples
{
    internal sealed class AlignmentSlide : BasicUsageSlide
    {
        public override string Text =>
            "⇔ <b>Alignment · justify · last-line</b>\n\n" +
            "<align=Start>Start-aligned paragraph line — left here, right in an RTL paragraph.</align>\n" +
            "<align=Center>Centered paragraph line.</align>\n" +
            "<align=End>End-aligned paragraph line — right here, left in an RTL paragraph.</align>\n" +
            "<align=Left>سلام دنیا — physical Left pins an RTL paragraph to the left edge.</align>\n" +
            "<align=Justify>Justified: this longer paragraph stretches every full line to both edges by expanding the spaces between words, leaving the final short line at the start.</align>\n" +
            "<align=Justify,InterCharacter,Center>Inter-character justify with a centered last line — the model for spaced CJK setting.</align>";
    }
}
