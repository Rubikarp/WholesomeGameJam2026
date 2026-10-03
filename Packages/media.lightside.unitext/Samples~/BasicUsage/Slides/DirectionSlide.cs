namespace LightSide.Samples
{
    internal sealed class DirectionSlide : BasicUsageSlide
    {
        public override string Text =>
            "🔃 <b>Base writing direction (whole-text)</b>\n\n" +
            "Three mixed lines, each starting with a different strong character — Auto detects each line's own base; LTR/RTL force one for all:\n" +
            "Order 123 مرحبا abc שלום 456 end\n" +
            "مرحبا world 123 שלום abc نهاية\n" +
            "שלום 456 world مرحبا abc סוף\n\n" +
            "Set base direction (persists until cleared):\n" +
            "<link=dir:auto><color=#4ECDC4><u>Auto</u></color></link>  " +
            "<link=dir:ltr><color=#4ECDC4><u>LTR</u></color></link>  " +
            "<link=dir:rtl><color=#4ECDC4><u>RTL</u></color></link>  " +
            "<link=dir:clear><color=#4ECDC4><u>clear</u></color></link>\n\n" +
            "<size=72%><color=#888>Applied via SetWholeText\\<DirectionModifier> — the CSS direction analogue (UAX \\#9).</color></size>";

        public override bool HandleLink(BasicUsageExampleBase example, string url)
        {
            if (!url.StartsWith("dir:")) return false;
            var mode = url.Substring(4);
            if (mode == "clear")
            {
                example.DemoText.ClearWholeText<DirectionModifier>();
                example.UpdateStatus("<color=#2ECC71>Direction:</color> cleared (component default)");
                return true;
            }

            var value = mode switch
            {
                "ltr" => "LeftToRight",
                "rtl" => "RightToLeft",
                _ => "Auto"
            };
            example.DemoText.SetWholeText<DirectionModifier>(value);
            example.UpdateStatus($"<color=#2ECC71>Direction:</color> {value}");
            return true;
        }
    }
}
