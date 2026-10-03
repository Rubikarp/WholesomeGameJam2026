namespace LightSide.Samples
{
    internal sealed class BoldSlide : BasicUsageSlide
    {
        public override string Text =>
            "🅱 <b>Bold — CSS weights & realization modes</b>\n\n" +
            "Weight axis: <b=100>100</b> <b=300>300</b> <b=400>400</b> <b=500>500</b> <b=700>700</b> <b=900>900</b>\n" +
            "Modes: bare <b>auto</b> · <b=700,r>real face only</b> · <b=700,f>forced synthetic</b>\n" +
            "Nested: <b>bold <i>+ italic</i> <color=#FF6B6B>+ color</color></b>\n\n" +
            "RTL: <b>عريض بالعربية</b> · <b>מודגש בעברית</b>    Emoji unaffected: <b>😀🎉👍</b>";
    }
}
