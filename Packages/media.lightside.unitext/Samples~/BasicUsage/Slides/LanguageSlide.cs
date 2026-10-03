namespace LightSide.Samples
{
    internal sealed class LanguageSlide : BasicUsageSlide
    {
        public override string Text =>
            "🌏 <b>Language & OpenType 'locl'</b>\n\n" +
            "Identical code points rendered four times with different language tags:\n" +
            "<lang=zh-Hans>zh-Hans  直 骨 雪 今 家 字 漢 社 海 神 真 食</lang>\n" +
            "<lang=zh-Hant>zh-Hant  直 骨 雪 今 家 字 漢 社 海 神 真 食</lang>\n" +
            "<lang=ja>ja       直 骨 雪 今 家 字 漢 社 海 神 真 食</lang>\n" +
            "<lang=ko>ko       直 骨 雪 今 家 字 漢 社 海 神 真 食</lang>\n\n" +
            "Component-level <b>Language</b> property (click to switch):\n" +
            "<link=lang:><color=#4ECDC4><u>none</u></color></link>  " +
            "<link=lang:zh-Hans><color=#4ECDC4><u>zh-Hans</u></color></link>  " +
            "<link=lang:zh-Hant><color=#4ECDC4><u>zh-Hant</u></color></link>  " +
            "<link=lang:ja><color=#4ECDC4><u>ja</u></color></link>  " +
            "<link=lang:ko><color=#4ECDC4><u>ko</u></color></link>\n\n" +
            "<color=#888>Use the bundled Fonts/SourceHanSans-Demo.otf (96 KB subset of Adobe\n" +
            "Source Han Sans with full locl coverage). Region-specific cuts like Noto Serif SC\n" +
            "only contain one set of glyphs and look identical across rows.\n\n" +
            "Per-range tags always win over the component-level default.</color>";

        private Style languageTagStyle;

        public override void Register(BasicUsageExampleBase example)
        {
            languageTagStyle = example.AddStyle(new LanguageModifier(), new TagRule("lang"));
        }

        public override bool HandleLink(BasicUsageExampleBase example, string url)
        {
            if (!url.StartsWith("lang:")) return false;
            var language = url.Substring(5);
            if (string.IsNullOrEmpty(language))
            {
                example.DemoText.Language = null;
                if (languageTagStyle != null && !ContainsStyle(example, languageTagStyle))
                    example.DemoText.Styles.Add(languageTagStyle);
                example.UpdateStatus("<color=#2ECC71>Language:</color> none (per-range <lang> tags re-enabled)");
            }
            else
            {
                if (languageTagStyle != null && ContainsStyle(example, languageTagStyle))
                    example.DemoText.Styles.Remove(languageTagStyle);
                example.DemoText.Language = language;
                example.UpdateStatus($"<color=#2ECC71>Language:</color> {language} (per-range <lang> tags suspended)");
            }
            return true;
        }

        private static bool ContainsStyle(BasicUsageExampleBase example, Style style)
        {
            var styles = example.DemoText.Styles;
            for (var i = 0; i < styles.Count; i++)
                if (styles[i] == style) return true;
            return false;
        }
    }
}
