using UnityEngine;

namespace LightSide.Samples
{
    internal sealed class RevealSlide : BasicUsageSlide
    {
        public override string Text =>
            "⌨ <b>Reveal — one modifier, many appearances</b>\n\n" +
            "<reveal>The default (unnamed) entry types this line in.</reveal>\n" +
            "<reveal=fade>Fade: soft opacity rise.</reveal>\n" +
            "<reveal=drop>Drop: glyphs fall into place.</reveal>\n" +
            "<reveal=pop>Pop: springy scale-in.</reveal>\n" +
            "<reveal=spin>Spin: rotating entrance.</reveal>\n" +
            "<reveal=rain>Rain: drifting down like droplets.</reveal>\n" +
            "<reveal=glitch>Glitch: corrupted materialization.</reveal>\n" +
            "<reveal=composite>Composite: fade + slide + spin + tint stacked in one entry.</reveal>\n\n" +
            "<size=72%><color=#888>One RevealModifier: the tag parameter picks a named handler " +
            "from its provider (inline here; a shared UniTextRevealHandlers asset works the same). " +
            "A bare tag uses the empty-name entry. A single Front sweeps one sequential frontier " +
            "across every range, while each handler keeps its own duration.</color></size>";

        private RevealModifier revealModifier;
        private float time;

        public override void Register(BasicUsageExampleBase example)
        {
            var composite = new CompositeRevealHandler { Duration = 0.5f };
            composite.Handlers.ReplaceAll(new RevealHandler[]
            {
                new FadeRevealHandler(),
                new SlideRevealHandler(),
                new SpinRevealHandler(),
                new TintRevealHandler
                {
                    StartTint = new Color32(255, 96, 220, 255),
                    Duration = 1f,
                },
            });

            var provider = new InlineRevealHandlerProvider();
            provider.Entries.ReplaceAll(new[]
            {
                Entry("", new PopRevealHandler { Duration = 0.35f }),
                Entry("fade", new FadeRevealHandler { Duration = 0.35f }),
                Entry("drop", new DropRevealHandler { Duration = 0.45f }),
                Entry("pop", new PopRevealHandler { Duration = 0.3f }),
                Entry("spin", new SpinRevealHandler { Duration = 0.4f }),
                Entry("rain", new RainRevealHandler { Duration = 0.5f }),
                Entry("glitch", new GlitchRevealHandler { Duration = 0.35f }),
                Entry("composite", composite),
            });

            revealModifier = new RevealModifier { Provider = provider };
            example.AddStyle(revealModifier, new TagRule("reveal"));
        }

        public override void Enter(BasicUsageExampleBase example)
        {
            time = 0f;
            if (revealModifier != null) revealModifier.Front = UnitValue.Percent(0f);
        }

        public override void Exit(BasicUsageExampleBase example)
        {
            if (revealModifier != null) revealModifier.Front = UnitValue.Percent(100f);
        }

        public override void Update(BasicUsageExampleBase example)
        {
            if (revealModifier == null) return;
            time += Time.deltaTime * 0.16f;
            revealModifier.Front =
                UnitValue.Percent(Mathf.Clamp01(Mathf.Repeat(time, 1.25f)) * 100f);
        }

        private static RevealHandlerEntry Entry(string name, RevealHandler handler)
            => new() { Name = name, Handler = handler };
    }
}
