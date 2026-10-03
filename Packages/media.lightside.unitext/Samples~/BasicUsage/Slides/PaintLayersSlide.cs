using UnityEngine;

namespace LightSide.Samples
{
    internal sealed class PaintLayersSlide : BasicUsageSlide
    {
        public override string Text =>
            "🎨 <b>SDF Paint Layers</b>\n\n" +
            "<font=modak><size=280%><gold>GOLD</gold>  <fire>FIRE</fire>  <ice>ICE</ice></size></font>\n\n" +
            "<font=modak><size=250%><neon>NEON</neon>  <candy>CANDY</candy>  <texture>PHOTO</texture></size></font>\n\n" +
            "<font=modak><size=200%><outline>OUTLINE</outline>  <layers>LAYERS</layers>  <inner-shadow=#000000E0,0.05 -0.07><fill=#EDE6D0>INSET</fill></inner-shadow></size></font>\n\n" +
            "<font=modak><size=280%><glow-pulse>PULSE</glow-pulse>  <rainbow>RAINBOW</rainbow></size></font>\n\n" +
            "<size=64%><color=#888>Swatches come from a UniTextPaints asset; RAINBOW's provider is code. LAYERS stacks two strokes; INSET is an inner shadow. FIRE and PULSE animate glow radius, PHOTO scrolls its texture, RAINBOW its gradient.</color></size>";

        private FillModifier animatedFill;
        private GlowModifier animatedGlow;
        private GlowModifier animatedFireGlow;
        private FillModifier animatedTextureFill;
        private CompositeModifier pulseComposite;
        private CompositeModifier fireComposite;
        private float time;

        public override void Register(BasicUsageExampleBase example)
        {
            IPaintProvider paints = new AssetPaintProvider { Asset = example.DemoPaints };

            AddPaintTag(example, "gold", "#2A1A00CC,0.05;gold", MakeStroke(paints), MakeFill(paints));
            AddPaintTag(example, "ice", "#9EE8FF80,0.30;ice", MakeGlow(paints), MakeFill(paints));
            AddPaintTag(example, "neon", "#FF2BD6,0.40;neon", MakeGlow(paints), MakeFill(paints));
            AddPaintTag(example, "candy", "#FFFFFF,0.05;candy", MakeStroke(paints), MakeFill(paints));
            AddPaintTag(example, "outline", "neon,0.12;#00000000", MakeStroke(paints), MakeFill(paints));
            AddPaintTag(example, "layers", "#101010,0.40;#FFD447,0.22;#E8407A", MakeStroke(paints), MakeStroke(paints), MakeFill(paints));

            animatedGlow = new GlowModifier();
            pulseComposite = new CompositeModifier();
            pulseComposite.Modifiers.ReplaceAll(new BaseModifier[] { animatedGlow, new FillModifier() });
            example.AddStyle(pulseComposite, new TagRule("glow-pulse") { DefaultParameter = "#00F5FF;#FFFFFF" });

            animatedFireGlow = new GlowModifier();
            fireComposite = new CompositeModifier();
            fireComposite.Modifiers.ReplaceAll(new BaseModifier[] { animatedFireGlow, MakeFill(paints) });
            example.AddStyle(fireComposite, new TagRule("fire") { DefaultParameter = "#FF7A00;fire" });

            animatedTextureFill = MakeFill(paints);
            example.AddStyle(animatedTextureFill, new TagRule("texture") { DefaultParameter = "photo" });

            var codePaints = new InlinePaintProvider();
            codePaints.Entries.Add(GradientSwatch("rainbow", 0f,
                ("#FF3B3B", 0f), ("#FF9E00", 0.2f), ("#FFE600", 0.35f),
                ("#37D67A", 0.5f), ("#00C2C7", 0.65f), ("#3B7BFF", 0.8f), ("#9B5CFF", 1f)));
            animatedFill = MakeFill(codePaints);
            example.AddStyle(animatedFill, new TagRule("rainbow") { DefaultParameter = "rainbow" });
        }

        public override void Enter(BasicUsageExampleBase example)
        {
            time = 0f;
        }

        public override void Update(BasicUsageExampleBase example)
        {
            time += Time.deltaTime;
            if (animatedFill != null)
                animatedFill.PaintOffset = new Vector2(0.5f + 0.5f * Mathf.Sin(time * 1.5f), 0f);
            if (animatedTextureFill != null)
                animatedTextureFill.PaintOffset = new Vector2(Mathf.Repeat(time * 0.15f, 1f), 0f);
            if (animatedGlow != null)
            {
                animatedGlow.Radius = UnitValue.Em(0.16f + 0.34f * (0.5f + 0.5f * Mathf.Sin(time * 2.2f)) - 0.05f);
                example.DemoText.MarkModifierDirty(pulseComposite, UniTextDirty.Mesh);
            }
            if (animatedFireGlow != null)
            {
                animatedFireGlow.Radius = UnitValue.Em(0.12f + 0.28f * Mathf.PerlinNoise(time * 7f, 0f));
                example.DemoText.MarkModifierDirty(fireComposite, UniTextDirty.Mesh);
            }
        }

        private static FillModifier MakeFill(IPaintProvider paints) => new() { Provider = paints };
        private static StrokeModifier MakeStroke(IPaintProvider paints) => new() { Provider = paints };
        private static GlowModifier MakeGlow(IPaintProvider paints) => new() { Provider = paints };

        private static void AddPaintTag(BasicUsageExampleBase example, string tag, string parameter, params BaseModifier[] layers)
        {
            var composite = new CompositeModifier();
            composite.Modifiers.ReplaceAll(layers);
            example.AddStyle(composite, new TagRule(tag) { DefaultParameter = parameter });
        }

        private static PaintSwatch GradientSwatch(string name, float angle, params (string hex, float time)[] stops)
        {
            var gradientStops = new GradientStop[stops.Length];
            for (var i = 0; i < stops.Length; i++)
            {
                ColorUtility.TryParseHtmlString(stops[i].hex, out var color);
                gradientStops[i] = new GradientStop(stops[i].time, color);
            }
            return new PaintSwatch
            {
                name = name,
                paint = new Paint
                {
                    source = new PaintSource
                    {
                        kind = PaintSourceKind.Gradient,
                        color = Color.white,
                        gradient = new Gradient(gradientStops, GradientInterpolation.Perceptual),
                    },
                    projection = new PaintProjection
                    {
                        kind = PaintProjectionKind.Linear,
                        fit = PaintFit.Stretch,
                        angle = angle,
                        scale = 1f,
                    },
                    blend = LayerBlend.Normal,
                },
                mapping = PaintMapping.Range,
            };
        }
    }
}
