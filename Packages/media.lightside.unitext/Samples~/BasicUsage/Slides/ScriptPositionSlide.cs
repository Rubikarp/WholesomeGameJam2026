namespace LightSide.Samples
{
    internal sealed class ScriptPositionSlide : BasicUsageSlide
    {
        public override string Text =>
            "² <b>Superscript & subscript</b>\n\n" +
            "Chemistry: H<sub>2</sub>O · CO<sub>2</sub> · H<sub>2</sub>SO<sub>4</sub> · C<sub>6</sub>H<sub>12</sub>O<sub>6</sub>\n" +
            "Math: E = mc<sup>2</sup> · a<sup>2</sup> + b<sup>2</sup> = c<sup>2</sup> · 6.02×10<sup>23</sup>\n" +
            "Ordinals & notes: 1<sup>st</sup> 2<sup>nd</sup> 3<sup>rd</sup> · claim<sup>[1]</sup>\n\n" +
            "<size=72%><color=#888>OpenType sups/subs when available, otherwise scaled + shifted.</color></size>";
    }
}
