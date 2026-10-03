namespace LightSide.Samples
{
    internal sealed class AssetBackedModifiersSlide : BasicUsageSlide
    {
        public override string Text =>
            "🧰 <b>Asset-backed modifiers (setup required)</b>\n\n" +
            "These need project assets, so they're shown as literal tag syntax:\n\n" +
            "<b>Variable fonts</b> \\<var> — positional axes wght,wdth,ital,slnt,opsz:\n" +
            "   \\<var=700> · \\<var=700,80> · \\<var=150%> · \\<var=+200> · \\<var=~,~,~,-12>\n\n" +
            "<b>Inline sprite</b> \\<sprite> from a UniTextSprites provider:\n" +
            "   \\<sprite=heart> · \\<sprite=heart,i> (currentColor) · \\<sprite=heart,\\#FF0000>\n\n" +
            "<b>Inline prefab</b> \\<obj> — \\<obj=icon/> instantiates a RectTransform inline.\n\n" +
            "<b>Custom material</b> \\<mat> — \\<mat> or \\<mat=\\#FF8800> routes glyphs to a sub-mesh material.\n\n" +
            "<size=72%><color=#888>See GettingStarted §3.5 & §6 for provider and material setup.</color></size>";
    }
}
