namespace LightSide.Samples
{
    internal sealed class ListsSlide : BasicUsageSlide
    {
        public override string Text =>
            "• <b>Lists — bullets · ordered · nested</b>\n\n" +
            "<li>Bullet at level 0</li>\n<li>Another bullet</li>\n<li=1>Nested dash</li>\n<li=2>Deeper dot</li>\n\n" +
            "<li=0,1>Ordered one</li>\n<li=0,2>Ordered two</li>\n<li=0,3>Ordered three</li>\n<li=1,1>Nested a.</li>\n\n" +
            "RTL marker flips side: <li>بند عربي</li>";
    }
}
