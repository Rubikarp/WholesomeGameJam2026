namespace LightSide.Samples
{
    internal sealed class FontSlide : BasicUsageSlide
    {
        public override string Text =>
            "🔤 <b>Font override with \\<font=name></b>\n\n" +
            "Add unique names to families in your FontStack inspector, then reference\n" +
            "them here. Unknown names fall back and log a warning.\n\n" +
            "Per-range \\<font=…>:\n" +
            "<font=header>HEADER FAMILY SAMPLE</font>\n" +
            "<font=body>Body family — paragraph sample with more words.</font>\n" +
            "<font=mono>monospace_function(arg1, arg2)</font>\n\n" +
            "Set whole-text family (click to apply):\n" +
            "<link=font:><color=#4ECDC4><u>clear</u></color></link>  " +
            "<link=font:header><color=#4ECDC4><u>header</u></color></link>  " +
            "<link=font:body><color=#4ECDC4><u>body</u></color></link>  " +
            "<link=font:mono><color=#4ECDC4><u>mono</u></color></link>\n\n" +
            "<color=#888>Per-range tags always win over the whole-text default.</color>";

        public override bool HandleLink(BasicUsageExampleBase example, string url)
        {
            if (!url.StartsWith("font:")) return false;
            var familyName = url.Substring(5);
            if (string.IsNullOrEmpty(familyName))
            {
                example.DemoText.ClearWholeText<FontModifier>();
                example.UpdateStatus("<color=#2ECC71>Font:</color> cleared");
            }
            else
            {
                example.DemoText.SetWholeText<FontModifier>(familyName);
                example.UpdateStatus($"<color=#2ECC71>Font:</color> {familyName}");
            }
            return true;
        }
    }
}
