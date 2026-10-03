namespace LightSide.Samples
{
    internal sealed class StrikethroughSlide : BasicUsageSlide
    {
        public override string Text =>
            "─ <b>Strikethrough — same knobs as underline</b>\n\n" +
            "<s>solid</s> <s=,double>double</s> <s=,wavy>wavy</s> <s=#FF6B6B>red</s> <s=#888,dashed>dashed gray</s>\n" +
            "Sale: <s=#FF0000,solid,2px>$99.00</s>  now <color=#2ECC71><b>$49.00</b></color>\n\n" +
            "RTL: <s>مشطوب</s> · <s>קו חוצה</s>";
    }
}
