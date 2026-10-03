using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Trims vertical space above/below text to chosen metrics (CSS <c>text-box-trim</c>/<c>text-box-edge</c>):
    /// e.g. cap-height + baseline for optically centered UI labels. Apply whole-text.
    /// </summary>
    [Serializable]
    [TypeGroup("Layout", 2)]
    [TypeDescription("Trims space above/below text to chosen metrics (cap-height, baseline) for optical fit.")]
    [GenerateParameters]
    public sealed partial class TextBoxTrimModifier : BaseModifier
    {
        /// <summary>Top trim metric. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkPositionsDirty))] private TextOverEdge overEdge = TextOverEdge.Ascent;
        /// <summary>Bottom trim metric. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkPositionsDirty))] private TextUnderEdge underEdge = TextUnderEdge.Descent;

        private TextOverEdge over = TextOverEdge.Ascent;
        private TextUnderEdge under = TextUnderEdge.Descent;

        private OrderedEventHandler<TextProcessSettings> configureCallback;

        protected override void OnEnable()
        {
            over = overEdge;
            under = underEdge;
            configureCallback ??= OnConfigure;
            uniText.TextProcessor.ConfigureSettings.Subscribe(configureCallback);
        }

        protected override void OnDisable()
            => uniText.TextProcessor.ConfigureSettings.Unsubscribe(configureCallback);

        protected override void OnApply(in RangeApplyContext context)
        {
            over = Param.OverEdge.Resolve(this, in context);
            under = Param.UnderEdge.Resolve(this, in context);
        }

        private void OnConfigure(ref TextProcessSettings settings)
        {
            settings.layout.overEdge = over;
            settings.layout.underEdge = under;
        }
    }
}
