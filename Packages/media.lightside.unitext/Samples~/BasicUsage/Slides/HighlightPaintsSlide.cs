using UnityEngine;

namespace LightSide.Samples
{
    internal sealed class HighlightPaintsSlide : BasicUsageSlide
    {
        public override string Text =>
            "✨ <b>Painted & Animated Highlights</b>\n\n" +
            "<highlight-neon><b>NEON GRADIENT</b> · one paint across the range</highlight-neon>\n\n" +
            "<highlight-shine><b>SHINING</b> · a bright band crosses the highlight</highlight-shine>\n\n" +
            "<highlight-photo><b>TEXTURE SCROLL</b> · tiled photo paint moves continuously</highlight-photo>\n\n" +
            "<highlight-pulse><b>TINT PULSE</b> · candy gradient changes brightness</highlight-pulse>\n\n" +
            "<size=64%><color=#888>All four are HighlightModifier graphs backed by Demo Paints. " +
            "GeometryMapping controls the surface; PaintMapping independently controls how its " +
            "gradient or texture spans the range. PaintOffset and Tint animate without changing text layout.</color></size>";

        private HighlightModifier shining;
        private HighlightModifier scrollingTexture;
        private HighlightModifier pulsingTint;
        private float time;

        public override void Register(BasicUsageExampleBase example)
        {
            var paints = new AssetPaintProvider { Asset = example.DemoPaints };
            AddHighlight(example, paints, "highlight-neon", "neon", new Color32(18, 20, 42, 255));
            shining = AddHighlight(example, paints, "highlight-shine", "shine",
                new Color32(18, 20, 42, 255));
            scrollingTexture = AddHighlight(example, paints, "highlight-photo", "photo",
                new Color32(255, 255, 255, 255));
            scrollingTexture.Tint = new Color32(70, 90, 120, 230);
            pulsingTint = AddHighlight(example, paints, "highlight-pulse", "candy",
                new Color32(18, 20, 42, 255));
        }

        public override void Enter(BasicUsageExampleBase example)
        {
            time = 0f;
        }

        public override void Update(BasicUsageExampleBase example)
        {
            time += Time.deltaTime;
            if (shining != null)
                shining.PaintOffset = new Vector2(Mathf.Repeat(time * 0.8f, 3.5f) - 1.75f, 0f);
            if (scrollingTexture != null)
                scrollingTexture.PaintOffset = new Vector2(1.26f + Mathf.Repeat(time * 0.18f, 1f), 0.24f);
            if (pulsingTint != null)
            {
                var phase = 0.5f + 0.5f * Mathf.Sin(time * 2.2f);
                var brightness = (byte)Mathf.RoundToInt(Mathf.Lerp(175f, 255f, phase));
                pulsingTint.Tint = new Color32(brightness, brightness, brightness, 255);
            }
        }

        private static HighlightModifier AddHighlight(BasicUsageExampleBase example,
            IPaintProvider paints, string tag, string swatch, Color32 textColor)
        {
            var highlight = new HighlightModifier
            {
                Provider = paints,
                Paint = PaintRef.Named(swatch),
                GeometryMapping = GeometryMapping.Range,
                PaintMapping = RangePaintMapping.Range,
                Height = RangeHeight.Content,
                Padding = new UnitVector2(new Vector2(0.14f, 0.07f), UnitKind.Em),
                CornerRadius = UnitValue.Em(0.24f),
            };
            var graph = new CompositeModifier();
            graph.Modifiers.ReplaceAll(new BaseModifier[]
            {
                highlight,
                new FillModifier { Paint = PaintRef.Solid(textColor) },
            });
            example.AddStyle(graph, new TagRule(tag));
            return highlight;
        }
    }
}
