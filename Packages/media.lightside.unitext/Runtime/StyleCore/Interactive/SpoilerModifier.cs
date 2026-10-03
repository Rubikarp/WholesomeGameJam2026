using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>Signature of <see cref="SpoilerModifier.RevealChanged"/> — one range toggled between concealed and revealed.</summary>
    public delegate void SpoilerRevealChangedHandler(in InteractiveRangeHit hit, bool revealed);

    /// <summary>Matches spoiler entities whose <see cref="SpoilerModifier.RevealedSignal"/> is false.</summary>
    [Serializable]
    [TypeDescription("Spoiler: entity is concealed")]
    public sealed class SpoilerConcealedRangeStateSelector : RangeStateSelector
    {
        /// <inheritdoc/>
        protected internal override bool Matches(in RangeSignalReader signals)
            => !signals.Get(SpoilerModifier.RevealedSignal);
    }

    /// <summary>
    /// Hides text ranges behind an opaque decoration until activated and exposes the revealed state
    /// as a typed range signal for arbitrary modifier, property or externally driven rules.
    /// </summary>
    /// <remarks>
    /// The revealed set is keyed by stable source identity, so unchanged entities keep their state
    /// across parsing and layout. The default presentation is an ordinary transient
    /// <see cref="HighlightModifier"/> above the text; replace or extend <see cref="InteractiveModifier.Rules"/>
    /// to use blur, glyph modifiers, custom geometry or a project animation driver.
    /// </remarks>
    /// <example>
    /// <code>
    /// var spoiler = new SpoilerModifier();
    /// uniText.Styles.Add(new Style { Modifier = spoiler, Source = new TagRule("spoiler") });
    /// </code>
    /// </example>
    [Serializable]
    [TypeGroup("Interactive", 3)]
    [TypeDescription("Hides text behind an animated modifier rule until activated.")]
    public class SpoilerModifier : InteractiveModifier
    {
        private readonly HashSet<RangeIdentity> revealed = new();
        private readonly List<RangeIdentity> pruneScratch = new();

        /// <summary>
        /// Typed signal published for every spoiler entity. Project selectors and external drivers
        /// can observe the same state as the built-in cover rule.
        /// </summary>
        public static readonly RangeSignal<bool> RevealedSignal =
            new("lightside.spoiler.revealed", false);

        public SpoilerModifier()
        {
            Rules.ReplaceAll(new RangeStateRule[]
            {
                new ModifierRule
                {
                    Selector = new SpoilerConcealedRangeStateSelector(),
                    Playback = new TransitionPlayback
                    {
                        EnterDuration = 0.25f,
                        ExitDuration = 0.18f,
                    },
                    ModifierTemplate = new HighlightModifier
                    {
                        Paint = PaintRef.Solid(new Color32(32, 34, 42, 255)),
                        GeometryMapping = GeometryMapping.Range,
                        Height = RangeHeight.Content,
                        Padding = new UnitVector2(new Vector2(0.12f, 0.08f), UnitKind.Em),
                        CornerRadius = UnitValue.Em(0.2f),
                        Order = RangeDecorationOrder.Above,
                        Priority = RangeDecorationPriorities.Interactive,
                    },
                },
            });
        }

        /// <summary>Occurs after one current range changes its revealed state.</summary>
        public event SpoilerRevealChangedHandler RevealChanged;

        /// <summary>Returns whether a current spoiler entity is revealed.</summary>
        public bool IsRevealed(in InteractiveRange range)
            => revealed.Contains(RequireOwnedIdentity(in range));

        /// <summary>Changes one current spoiler entity and drives every rule bound to <see cref="RevealedSignal"/>.</summary>
        public void SetRevealed(in InteractiveRange range, bool value)
        {
            var identity = RequireOwnedIdentity(in range);
            var changed = value ? revealed.Add(identity) : revealed.Remove(identity);
            if (!changed) return;
            if (uniText != null)
                UniTextRanges.For(uniText).SetSignal(identity, RevealedSignal, value);
            var rangeHit = new InteractiveRangeHit(range, default);
            RevealChanged?.Invoke(in rangeHit, value);
        }

        /// <summary>Conceals every currently revealed spoiler and animates the configured rules.</summary>
        public void ConcealAll()
        {
            if (revealed.Count == 0) return;
            for (var i = 0; i < InteractiveRanges.Length; i++)
            {
                ref readonly var range = ref InteractiveRanges[i];
                if (revealed.Contains(range.Identity))
                    if (uniText != null)
                        UniTextRanges.For(uniText).SetSignal(range.Identity, RevealedSignal, false);
            }
            revealed.Clear();
        }

        /// <inheritdoc/>
        protected override void OnApply(in RangeApplyContext context)
        {
            AddRange(in context, context.PrimaryValue);
        }

        /// <inheritdoc/>
        protected override void HandleDefaultAction(RangeInteraction interaction)
        {
            if (interaction.Kind != RangeInteractionKind.Activated) return;
            var range = interaction.Range;
            var identity = RequireOwnedIdentity(in range);
            var nowRevealed = revealed.Add(identity);
            if (!nowRevealed) revealed.Remove(identity);
            UniTextRanges.For(uniText).SetSignal(identity, RevealedSignal, nowRevealed);
            var rangeHit = new InteractiveRangeHit(range, interaction.TextHit);
            RevealChanged?.Invoke(in rangeHit, nowRevealed);
        }

        /// <inheritdoc/>
        protected override void HandleRangesRebuilt()
        {
            pruneScratch.Clear();
            foreach (var identity in revealed)
            {
                var found = false;
                var ranges = InteractiveRanges;
                for (var i = 0; i < ranges.Length; i++)
                {
                    if (ranges[i].Identity != identity) continue;
                    found = true;
                    break;
                }
                if (!found) pruneScratch.Add(identity);
            }

            for (var i = 0; i < pruneScratch.Count; i++)
                revealed.Remove(pruneScratch[i]);

            var current = InteractiveRanges;
            for (var i = 0; i < current.Length; i++)
            {
                ref readonly var range = ref current[i];
                if (!range.Identity.IsValid)
                    throw new InvalidOperationException(
                        "SpoilerModifier requires a RangeSource with stable entity identities.");
                UniTextRanges.For(uniText).SetSignal(range.Identity, RevealedSignal,
                    revealed.Contains(range.Identity));
            }
        }

        private RangeIdentity RequireOwnedIdentity(in InteractiveRange range)
        {
            if (!range.Identity.IsValid)
                throw new ArgumentException(
                    "The spoiler range has no stable source identity.", nameof(range));
            var ranges = InteractiveRanges;
            for (var i = 0; i < ranges.Length; i++)
                if (ranges[i].Identity == range.Identity)
                    return range.Identity;
            throw new ArgumentException(
                "The range is not a current entity of this SpoilerModifier.", nameof(range));
        }
    }
}
