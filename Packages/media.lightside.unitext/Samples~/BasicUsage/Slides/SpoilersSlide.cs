namespace LightSide.Samples
{
    internal sealed class SpoilersSlide : BasicUsageSlide
    {
        public override string Text =>
            "🫥 <b>Spoilers — tap to reveal</b>\n\n" +
            "The killer is <spoiler>the butler 🔪</spoiler>, and the twist: <spoiler>it was all a dream</spoiler>\n\n" +
            "Covers follow wrapping — fill the line so this one breaks: <spoiler>a spoiler spanning " +
            "multiple lines keeps its rounded cover on every fragment, and one tap reveals the whole range</spoiler>\n\n" +
            "<size=72%><color=#888>Tap a cover to reveal, tap again to conceal. Reveal state keys on range identity: " +
            "it survives re-parses and resets when the entity disappears. The cover is an ordinary HighlightModifier " +
            "inside a signal-driven ModifierRule, so its Paint, geometry and animation can be replaced in the Inspector.</color></size>";

        private SpoilerModifier spoilerModifier;

        public override void Register(BasicUsageExampleBase example)
        {
            spoilerModifier = new SpoilerModifier();
            example.AddStyle(spoilerModifier, new TagRule("spoiler"));
        }

        public override bool HasRangeAt(int cluster)
            => spoilerModifier != null && spoilerModifier.TryGetRange(cluster, out _);
    }
}
