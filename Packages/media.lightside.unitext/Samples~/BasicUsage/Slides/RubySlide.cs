namespace LightSide.Samples
{
    internal sealed class RubySlide : BasicUsageSlide
    {
        public override string Text =>
            "<ruby>漢字<rt>かんじ</rt></ruby> 📖 <b>Ruby</b> — first line tests top clearance\n\n" +
            "Group, over, JIS 1:2:1: <ruby>漢字<rt>かんじ</rt></ruby> <ruby>学校<rt>がっこう</rt></ruby> <ruby>東京<rt>とうきょう</rt></ruby>\n" +
            "Mono (per char): <ruby>東<rt>とう</rt>京<rt>きょう</rt></ruby>タワー · shorthand <ruby=Tōkyō>東京</ruby> · any reading <ruby>拼音<rt>pīnyīn</rt></ruby> · arabic shapes <ruby>salam<rt>سلام</rt></ruby>\n" +
            "Wide reading spreads base <ruby>力<rt>ちからもち</rt></ruby>; narrow centers <ruby>図書館<rt>と</rt></ruby>; adjacent never collide <ruby>北<rt>きた</rt></ruby><ruby>東<rt>ひがし</rt></ruby><ruby>風<rt>かぜ</rt></ruby>";
    }
}
