using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Sets the paragraph base writing direction (UAX #9). Apply whole-text.
    /// </summary>
    [Serializable]
    [TypeGroup("Layout", 0)]
    [TypeDescription("Base writing direction of the text (auto / left-to-right / right-to-left).")]
    [GenerateParameters]
    public sealed partial class DirectionModifier : BaseModifier
    {
        /// <summary>Base writing direction. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkTextDirty))] private TextDirection direction = TextDirection.Auto;

        private TextDirection resolved = TextDirection.Auto;
        private bool applied;

        private OrderedEventHandler<TextProcessSettings> configureCallback;

        protected override void OnEnable()
        {
            applied = false;
            configureCallback ??= OnConfigure;
            uniText.TextProcessor.ConfigureSettings.Subscribe(configureCallback);
        }

        protected override void OnDisable()
            => uniText.TextProcessor.ConfigureSettings.Unsubscribe(configureCallback);

        protected override void OnApply(in RangeApplyContext context)
        {
            resolved = Param.Direction.Resolve(this, in context);
            applied = true;
        }

        private void OnConfigure(ref TextProcessSettings settings)
        {
            settings.baseDirection = applied ? resolved : direction;
            applied = false;
        }
    }
}
