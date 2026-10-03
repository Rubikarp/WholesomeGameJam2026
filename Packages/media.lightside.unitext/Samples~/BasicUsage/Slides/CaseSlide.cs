namespace LightSide.Samples
{
    internal sealed class CaseSlide : BasicUsageSlide
    {
        public override string Text =>
            "🔠 <b>Case & small-caps (Unicode-correct)</b>\n\n" +
            "<upper>uppercased from lowercase input</upper>\n" +
            "<lower>LOWERCASED FROM UPPERCASE INPUT</lower>\n" +
            "<smallcaps>Small Caps: Mixed Case Becomes Caps</smallcaps>\n\n" +
            "Greek final sigma: <upper>οδός</upper>   Cyrillic: <upper>привет мир</upper>\n" +
            "<size=72%><color=#888>Bundled UCD case data — OpenType smcp when present, else synthesized.</color></size>";
    }
}
