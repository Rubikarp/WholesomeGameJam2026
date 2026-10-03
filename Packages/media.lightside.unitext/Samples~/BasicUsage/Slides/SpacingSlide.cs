namespace LightSide.Samples
{
    internal sealed class SpacingSlide : BasicUsageSlide
    {
        public override string Text =>
            "↔ <b>Letter-spacing (cspace) & word-spacing</b>\n\n" +
            "Tracking: <cspace=8>S P A C E D  O U T</cspace>\n" +
            "Em units: <cspace=0.25em>quarter-em tracking</cspace> · tight <cspace=-1>squeezed</cspace>\n" +
            "Equal cells: <cwidth=auto>iIWmll — one advance</cwidth> · full-width: <cwidth=1em>P</cwidth>级别谱面\n" +
            "Word gaps: normal spacing here <wspace=0.8em>wide gaps only between words</wspace>\n\n" +
            "Arabic kashida grows at joins: <cspace=6>مرحبا بالعالم</cspace>";
    }
}
