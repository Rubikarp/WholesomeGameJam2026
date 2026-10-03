namespace LightSide.Samples
{
    internal sealed class SizeSlide : BasicUsageSlide
    {
        public override string Text =>
            "🔎 <b>Size — absolute px · percent · relative delta</b>\n\n" +
            "px: <size=16>16</size> <size=24>24</size> <size=36>36</size> <size=52>52</size>\n" +
            "%: <size=60%>60%</size> <size=100%>100%</size> <size=140%>140%</size> <size=180%>180%</size>\n" +
            "delta: base <size=+16>+16</size> <size=-8>−8</size>\n\n" +
            "Inline mix keeps the baseline: H<size=60%>2</size>O · big <size=200%>😀</size> emoji";
    }
}
