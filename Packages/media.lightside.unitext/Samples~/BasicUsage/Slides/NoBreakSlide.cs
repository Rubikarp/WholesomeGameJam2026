namespace LightSide.Samples
{
    internal sealed class NoBreakSlide : WidthAnimatedSlide
    {
        public override string Text =>
            "⛓ <b>No-break</b>\n\n" +
            "Narrow the box: words wrap one by one, but <nobr><b><color=#FFD447>four five six</color></b></nobr> never splits.\n\n" +
            "one two three <nobr><b><color=#FFD447>four five six</color></b></nobr> seven eight nine ten eleven twelve";
    }
}
