using System;

namespace LightSide
{
    public abstract partial class UniTextBase
    {
        /// <summary>
        /// Occurs after Unity's <c>Animator</c> applied animated property values to this component.
        /// The Animator writes serialized fields directly, bypassing the property setters that raise
        /// <see cref="UniTextDirty"/>, so nothing invalidates on its own — attach a
        /// <see cref="UniTextAnimationBridge"/> with the handlers for the fields you animate, or
        /// subscribe here and run your own diff, calling <see cref="SetDirty"/> with the matching flags.
        /// </summary>
        public event Action Animated;

        /// <inheritdoc/>
        protected override void OnDidApplyAnimationProperties() => Animated?.Invoke();
    }
}
