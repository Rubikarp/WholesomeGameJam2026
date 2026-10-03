namespace LightSide.Samples
{
    internal sealed class SeparatorSlide : WidthAnimatedSlide
    {
        public override string Text =>
            "⑆ <b>Separator segments (\\<sep> void tag)</b>\n\n" +
            "Home<sep>Products<sep>Docs<sep>Pricing<sep>About<sep>Contact\n\n" +
            "Custom string: A<sep=\" ● \">B<sep=\" ● \">C\n\n" +
            "<size=72%><color=#888>Each segment lays out as a whole unit; the separator collapses when its segment wraps.</color></size>";
    }
}
