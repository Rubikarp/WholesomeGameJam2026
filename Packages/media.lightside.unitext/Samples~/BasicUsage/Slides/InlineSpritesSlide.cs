namespace LightSide.Samples
{
    internal sealed class InlineSpritesSlide : BasicUsageSlide
    {
        public override string Text =>
            "<sprite=cat-sulejman> starts the first line beside ordinary text.\n" +
            "The second line flows naturally without inline media.\n" +
            "The third line carries <sprite=unitext-logo> right in its flow.\n" +
            "The fourth line remains completely text-only.\n" +
            "The fifth mixes <sprite=cat-sulejman> text <sprite=unitext-logo> in one run.\n" +
            "The sixth continues normally beneath both sprites.\n" +
            "The final line closes the paragraph without inline media.";

        public override void Register(BasicUsageExampleBase example)
        {
            var modifier = new SpriteModifier
            {
                Provider = new AssetSpriteProvider { Asset = example.DemoSprites }
            };
            example.AddStyle(modifier, new TagRule("sprite"));
        }
    }
}
