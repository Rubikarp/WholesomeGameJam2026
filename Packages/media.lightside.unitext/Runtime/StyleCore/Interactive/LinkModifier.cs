using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Marks text ranges as clickable hyperlinks and dispatches their click/hover events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Carries no base visual styling — colour and underline are separate modifiers. Combine it
    /// with <see cref="ColorModifier"/> and <see cref="UnderlineModifier"/> in a
    /// <see cref="CompositeModifier"/> (as the built-in Link preset does) so a link's appearance is
    /// fully author-controlled. Parameter: the URL. State feedback follows web convention: no
    /// hover background (underline / colour composition already reads as hover affordance), a
    /// subtle grey rounded pressed/activation flash is composed through the same
    /// <see cref="ModifierRule"/> graph available to every interactive range.
    /// </para>
    /// <para>
    /// Subscribe to <see cref="LinkClicked"/> to handle activation (a confirmed click / tap —
    /// raised from the interaction machine's Activated event), or let the modifier open the URL
    /// via <see cref="Application.OpenURL"/> when <see cref="AutoOpenUrl"/> is set.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var linkMod = uniText.GetModifier&lt;LinkModifier&gt;();
    /// linkMod.LinkClicked += url => Debug.Log($"Link clicked: {url}");
    /// </code>
    /// </example>
    /// <seealso cref="ParseRule"/>
    /// <seealso cref="MarkdownLinkParseRule"/>
    /// <seealso cref="RawUrlParseRule"/>
    /// <seealso cref="InteractiveModifier"/>
    [Serializable]
    [TypeGroup("Interactive", 3)]
    [TypeDescription("Makes text interactive as a clickable link.")]
    public partial class LinkModifier : InteractiveModifier
    {
        /// <summary>URL used when the source does not supply a primary value.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        private string url = "";

        /// <summary>Whether activation automatically opens the resolved URL.</summary>
        [SerializeField, StateProperty(nameof(ApplyAutoOpenUrlChange))]
        [Tooltip("Whether to automatically open URLs when clicked.")]
        private bool autoOpenUrl = true;

        public LinkModifier()
        {
            Rules.ReplaceAll(new RangeStateRule[]
            {
                new ModifierRule
                {
                    Selector = new InteractionRangeStateSelector
                    {
                        Required = InteractionSignalMask.Pressed,
                    },
                    Playback = new TransitionPlayback
                    {
                        EnterDuration = 0f,
                        ExitDuration = 0.15f,
                    },
                    ModifierTemplate = CreateFeedbackHighlight(),
                },
                new ModifierRule
                {
                    Selector = null,
                    Trigger = RangeRuleEvent.Activated,
                    Playback = new TransitionPlayback
                    {
                        EnterDuration = 0.04f,
                        ExitDuration = 0.18f,
                    },
                    ModifierTemplate = CreateFeedbackHighlight(),
                },
            });
        }

        /// <summary>Occurs when a link has been clicked, providing the URL.</summary>
        public event Action<string> LinkClicked;

        /// <summary>Occurs when the pointer has entered a link, providing the URL.</summary>
        public event Action<string> LinkEntered;

        /// <summary>Occurs when the pointer has exited a link.</summary>
        public event Action LinkExited;

        private void ApplyAutoOpenUrlChange() { }

        /// <inheritdoc/>
        protected override void OnApply(in RangeApplyContext context)
        {
            var reader = context.Parameters.GetReader();
            var resolved = context.PrimaryValue;
            if (string.IsNullOrEmpty(resolved))
                resolved = reader.NextString(out var parameter) && !string.IsNullOrEmpty(parameter)
                    ? parameter
                    : url;
            if (string.IsNullOrEmpty(resolved)) return;
            AddRange(in context, resolved, ref reader);
        }

        /// <inheritdoc/>
        protected override void HandleInteraction(RangeInteraction interaction)
        {
            var urlValue = interaction.Range.PrimaryValue;
            switch (interaction.Kind)
            {
                case RangeInteractionKind.Activated:
                    CatZones.component.MeowFormat("[LinkModifier] Link clicked: {0}", urlValue);
                    LinkClicked?.Invoke(urlValue);
                    break;
                case RangeInteractionKind.Entered:
                    LinkEntered?.Invoke(urlValue);
                    break;
                case RangeInteractionKind.Exited:
                    LinkExited?.Invoke();
                    break;
            }
        }

        /// <inheritdoc/>
        protected override void HandleDefaultAction(RangeInteraction interaction)
        {
            if (interaction.Kind == RangeInteractionKind.Activated && autoOpenUrl &&
                !string.IsNullOrEmpty(interaction.Range.PrimaryValue))
                Application.OpenURL(interaction.Range.PrimaryValue);
        }

        private static HighlightModifier CreateFeedbackHighlight() => new()
        {
            Paint = PaintRef.Solid(new Color32(128, 128, 128, 64)),
            GeometryMapping = GeometryMapping.Range,
            Height = RangeHeight.Content,
            Padding = new UnitVector2(new Vector2(0.1f, 0.06f), UnitKind.Em),
            CornerRadius = UnitValue.Em(0.15f),
            Priority = RangeDecorationPriorities.Interactive,
        };
    }
}
