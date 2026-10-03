using UnityEngine;

namespace LightSide.Samples
{
    internal sealed class InteractiveRangesSlide : BasicUsageSlide
    {
        public override string Text =>
            "💬 <b>Mentions & Hashtags (one modifier graph)</b>\n\n" +
            "Morning @alice and @bob_42 — the shaping pass by @charlie landed! " +
            "Tag reviews with #unicode, #typography or #rtl.\n\n" +
            "Word-start guard keeps e-mail plain: support@lightside.media\n\n" +
            "<size=72%><color=#888>A mention Style is CompositeModifier: persistent " +
            "HighlightModifier + FillModifier + InteractiveModifier. State Rules add transient " +
            "modifiers or animate graph properties. Subscribe once to Interaction to receive " +
            "activation, hover, long press, pointer coordinates, entity identity and anchor bounds.</color></size>";

        private InteractiveModifier mentionModifier;
        private InteractiveModifier hashtagModifier;
        private BasicUsageExampleBase owner;

        public override void Register(BasicUsageExampleBase example)
        {
            owner = example;
            mentionModifier = new InteractiveModifier();
            mentionModifier.Rules.ReplaceAll(InteractionRules(new Color32(88, 101, 242, 72)));
            var mentionGraph = new CompositeModifier();
            mentionGraph.Modifiers.ReplaceAll(new BaseModifier[]
            {
                new HighlightModifier
                {
                    Paint = PaintRef.Solid(new Color32(88, 101, 242, 77)),
                    GeometryMapping = GeometryMapping.Range,
                    Height = RangeHeight.Content,
                    Padding = new UnitVector2(new Vector2(0.15f, 0.05f), UnitKind.Em),
                    CornerRadius = UnitValue.Em(0.25f),
                    Priority = RangeDecorationPriorities.Interactive,
                },
                new FillModifier
                {
                    Paint = PaintRef.Solid(new Color32(224, 226, 255, 255)),
                },
                mentionModifier,
            });
            example.AddStyle(mentionGraph, new TriggerWordParseRule("@"));

            var hashtagFill = new FillModifier
            {
                Paint = PaintRef.Solid(new Color32(255, 255, 255, 255)),
            };
            hashtagModifier = new InteractiveModifier();
            hashtagModifier.Rules.ReplaceAll(HashtagRules(new AssetPaintProvider
            {
                Asset = example.DemoPaints,
            }, hashtagFill));
            var hashtagGraph = new CompositeModifier();
            hashtagGraph.Modifiers.ReplaceAll(new BaseModifier[] { hashtagFill, hashtagModifier });
            example.AddStyle(hashtagGraph, new TriggerWordParseRule("#"));

            mentionModifier.Interaction += OnMentionInteraction;
            hashtagModifier.Interaction += OnHashtagInteraction;
        }

        public override bool HasRangeAt(int cluster)
            => Contains(mentionModifier, cluster) || Contains(hashtagModifier, cluster);

        public override void Dispose(BasicUsageExampleBase example)
        {
            if (mentionModifier != null) mentionModifier.Interaction -= OnMentionInteraction;
            if (hashtagModifier != null) hashtagModifier.Interaction -= OnHashtagInteraction;
        }

        private void OnMentionInteraction(RangeInteraction interaction) => OnInteraction(interaction, "@");

        private void OnHashtagInteraction(RangeInteraction interaction) => OnInteraction(interaction, "#");

        private void OnInteraction(RangeInteraction interaction, string prefix)
        {
            var value = interaction.Range.PrimaryValue;
            switch (interaction.Kind)
            {
                case RangeInteractionKind.Activated:
                    owner?.UpdateStatus(
                        $"<color=#2ECC71>Activated:</color> {prefix}{value} · entity {interaction.Entity}");
                    break;
                case RangeInteractionKind.ContextRequested:
                    owner?.UpdateStatus(
                        $"<color=#E67E22>Long press / context:</color> {prefix}{value} · anchor {interaction.AnchorRect}");
                    break;
                case RangeInteractionKind.Entered:
                    owner?.UpdateStatus(
                        $"<color=#3498DB>Hover:</color> {prefix}{value} · local {interaction.LocalPosition}");
                    break;
                case RangeInteractionKind.LongPressProgress:
                    owner?.UpdateStatus(
                        $"<color=#9B59B6>Hold:</color> {prefix}{value} · {interaction.Progress:P0}");
                    break;
            }
        }

        private static RangeStateRule[] InteractionRules(Color32 color)
            => new RangeStateRule[]
            {
                HighlightRule(InteractionSignalMask.Hovered, color, 0.14f, 0.18f),
                HighlightRule(InteractionSignalMask.Pressed,
                    new Color32(color.r, color.g, color.b, 118), 0f, 0.2f),
                new ModifierRule
                {
                    Selector = new InteractionRangeStateSelector
                    {
                        Required = InteractionSignalMask.Focused,
                    },
                    Playback = new TransitionPlayback
                    {
                        EnterDuration = 0.12f,
                        ExitDuration = 0.12f,
                    },
                    ModifierTemplate = new StrokeModifier
                    {
                        Paint = PaintRef.Solid(new Color32(255, 255, 255, 180)),
                        Width = UnitValue.Em(0.06f),
                    },
                },
            };

        private static RangeStateRule[] HashtagRules(IPaintProvider paints,
            FillModifier hashtagFill)
        {
            var hoverText = new ParameterRule
            {
                Selector = new InteractionRangeStateSelector
                {
                    Required = InteractionSignalMask.Hovered,
                },
                Playback = new TransitionPlayback
                {
                    EnterDuration = 0.2f,
                    ExitDuration = 0.28f,
                },
                TargetValue = new ColorRuleValue
                {
                    Value = new Color32(18, 20, 42, 255),
                },
            };
            hoverText.SetTarget(hashtagFill, PaintLayerModifier.Param.Tint);
            return new RangeStateRule[]
            {
                new ModifierRule
                {
                    Selector = new InteractionRangeStateSelector
                    {
                        Required = InteractionSignalMask.Hovered,
                    },
                    Playback = new TransitionPlayback
                    {
                        EnterDuration = 0.2f,
                        ExitDuration = 0.28f,
                    },
                    ModifierTemplate = new HighlightModifier
                    {
                        Provider = paints,
                        Paint = PaintRef.Named("neon"),
                        GeometryMapping = GeometryMapping.Glyph,
                        PaintMapping = RangePaintMapping.Range,
                        Height = RangeHeight.Content,
                        Padding = new UnitVector2(new Vector2(0.05f, 0.03f), UnitKind.Em),
                        CornerRadius = UnitValue.Em(0.16f),
                        Priority = RangeDecorationPriorities.Interactive,
                    },
                },
                hoverText,
                HighlightRule(InteractionSignalMask.Pressed,
                    new Color32(78, 205, 196, 125), 0f, 0.2f),
            };
        }

        private static ModifierRule HighlightRule(InteractionSignalMask signal, Color32 color,
            float enter, float exit)
            => new()
            {
                Selector = new InteractionRangeStateSelector { Required = signal },
                Playback = new TransitionPlayback
                {
                    EnterDuration = enter,
                    ExitDuration = exit,
                },
                ModifierTemplate = new HighlightModifier
                {
                    Paint = PaintRef.Solid(color),
                    GeometryMapping = GeometryMapping.Range,
                    Height = RangeHeight.Content,
                    Padding = new UnitVector2(new Vector2(0.15f, 0.05f), UnitKind.Em),
                    CornerRadius = UnitValue.Em(0.25f),
                    Priority = RangeDecorationPriorities.Interactive,
                },
            };

        private static bool Contains(InteractiveModifier modifier, int cluster)
            => modifier != null && modifier.TryGetRange(cluster, out _);
    }
}
