namespace LightSide.Samples
{
    internal sealed class ItalicSlide : BasicUsageSlide
    {
        public override string Text =>
            "🅸 <b>Italic — modes & synthetic slant</b>\n\n" +
            "Modes: bare <i>auto</i> · <i=r>real face only</i> · <i=f>font's own slant</i>\n" +
            "Synthetic shear: <i=10>10%</i> <i=20>20%</i> <i=30>30%</i> <i=45>45%</i> <i=-20>−20% back-slant</i>\n\n" +
            "RTL: <i>مائل</i> · <i>נטוי</i>    Emoji stays upright: <i>rocket 🚀 unslanted</i>";
    }
}
