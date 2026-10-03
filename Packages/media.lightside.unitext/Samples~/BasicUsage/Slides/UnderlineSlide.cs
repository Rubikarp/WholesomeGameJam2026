namespace LightSide.Samples
{
    internal sealed class UnderlineSlide : BasicUsageSlide
    {
        public override string Text =>
            "﹍ <b>Underline — style · color · thickness · offset · skip-ink</b>\n\n" +
            "Styles: <u>solid</u> <u=,double>double</u> <u=,dotted>dotted</u> <u=,dashed>dashed</u> <u=,wavy>wavy</u>\n" +
            "Paint: <u=#FF6B6B>red</u> <u=#4ECDC4,wavy>teal wavy</u> <u=#FFD700,double>gold double</u>\n" +
            "Metrics: <u=,solid,3px>3px thick</u> <u=,solid,1px,-4px>offset down</u>\n" +
            "Skip-ink jumps descenders: <u=#A06CD5,solid,2px,,true>puppy paging gently</u>\n\n" +
            "RTL: <u=#4ECDC4>تسطير</u> · <u>קו תחתון</u>";
    }
}
