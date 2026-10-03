namespace LightSide.Samples
{
    internal sealed class ColorSlide : BasicUsageSlide
    {
        public override string Text =>
            "🎨 <b>Color — hex · named · alpha compositing</b>\n\n" +
            "Named: <color=red>red</color> <color=orange>orange</color> <color=gold>gold</color> <color=lime>lime</color> <color=teal>teal</color> <color=navy>navy</color> <color=purple>purple</color> <color=pink>pink</color>\n" +
            "Hex: <color=#F00>#F00</color> <color=#00B4D8>#00B4D8</color> <color=#A06CD5>#A06CD5</color>\n" +
            "Alpha: <color=#FF000040>25%</color> <color=#FF000080>50%</color> <color=#FF0000C0>75%</color> <color=#FF0000FF>100%</color>\n\n" +
            "Emoji keep their own colors: <color=#4ECDC4>tint applies to text only 😀🌈🎉</color>";
    }
}
