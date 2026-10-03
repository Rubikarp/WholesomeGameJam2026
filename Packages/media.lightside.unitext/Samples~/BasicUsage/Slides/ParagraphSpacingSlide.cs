namespace LightSide.Samples
{
    internal sealed class ParagraphSpacingSlide : BasicUsageSlide
    {
        public override string Text =>
            "¶ <b>Paragraph spacing (between line breaks)</b>\n\n" +
            "<pspace=18>Paragraph one.\nParagraph two — 18px opens after each.\nParagraph three.</pspace>\n\n" +
            "<pspace=1em,0.5em>After 1em + before 0.5em; where two paragraphs meet the gaps add.\nSecond line here.</pspace>";
    }
}
