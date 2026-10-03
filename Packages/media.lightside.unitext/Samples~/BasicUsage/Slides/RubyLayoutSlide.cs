namespace LightSide.Samples
{
    internal sealed class RubyLayoutSlide : BasicUsageSlide
    {
        public override string Text =>
            "🧪 <b>Ruby — wrap, stacking, settings</b>\n\n" +
            "Base+reading wrap as one unit (fill to the edge so it breaks): ねこ ねこ ねこ ねこ ねこ <ruby>大和<rt>やまとなでしこ</rt></ruby> ねこ\n\n" +
            "Dense rows each clear the line above: <ruby>昨日<rt>きのう</rt></ruby><ruby>友達<rt>ともだち</rt></ruby>と<ruby>映画<rt>えいが</rt></ruby>を<ruby>見<rt>み</rt></ruby>て<ruby>料理<rt>りょうり</rt></ruby>を<ruby>食<rt>た</rt></ruby>べた\n\n" +
            "<color=#888>RubyModifier settings: Position = Over / Under, Align = SpaceAround / Center / SpaceBetween / Start.</color>";
    }
}
