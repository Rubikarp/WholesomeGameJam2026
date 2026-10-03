using System;

namespace LightSide
{
    /// <summary>
    /// Base class for state-driven field visuals: placeholder, floating label, supporting text,
    /// character counter, or validation feedback. It participates in the ordinary
    /// <see cref="InputBehavior"/> lifecycle and appears in the editor's behavior type picker.
    /// </summary>
    [Serializable]
    [TypeMenuSuffix("Decorator")]
    public abstract class FieldDecorator : InputBehavior
    {
        [NonSerialized] private bool ticking;

        /// <summary>Whether this decorator is currently attached to an enabled editable.</summary>
        protected bool IsAttached => IsEnabled;

        protected override void OnEnable()
        {
            OnAttach();
            editable.TextChanged += Dispatch;
            editable.Focused += Dispatch;
            editable.Defocused += OnEditingEnded;
            editable.CompositionStateChanged += OnCompositionStateChanged;
            editable.ValidationChanged += OnValidationChanged;
            Dispatch();
        }

        protected override void OnDisable()
        {
            editable.TextChanged -= Dispatch;
            editable.Focused -= Dispatch;
            editable.Defocused -= OnEditingEnded;
            editable.CompositionStateChanged -= OnCompositionStateChanged;
            editable.ValidationChanged -= OnValidationChanged;
            StopTick();
            OnDetach();
        }

        private void OnCompositionStateChanged(bool _) => Dispatch();

        private void OnValidationChanged(ValidationState _) => Dispatch();

        private void OnEditingEnded(EditingEndReason _) => Dispatch();

        private void Dispatch()
        {
            var state = new FieldState(
                isEmpty: editable.CharCount == 0 && !editable.IsComposing,
                isFocused: editable.IsActive,
                isComposing: editable.IsComposing,
                lengthLimit: editable.LengthLimit,
                validation: editable.Validation);
            OnFieldState(in state);
        }

        /// <summary>Starts per-frame <see cref="OnTick"/> delivery. Idempotent; idle decorators receive no ticks.</summary>
        protected void RequestTick()
        {
            if (ticking) return;
            ticking = true;
            editable.FrameTicked += Tick;
        }

        /// <summary>Stops per-frame <see cref="OnTick"/> delivery. Safe to call from inside <see cref="OnTick"/>.</summary>
        protected void StopTick()
        {
            if (!ticking) return;
            ticking = false;
            editable.FrameTicked -= Tick;
        }

        private void Tick(float deltaTime) => OnTick(deltaTime);

        /// <summary>Cache target references and capture any original state to restore in <see cref="OnDetach"/>.</summary>
        protected virtual void OnAttach() { }

        /// <summary>Revert anything applied to targets so the editable returns to its undecorated state.</summary>
        protected virtual void OnDetach() { }

        /// <summary>Called whenever focus, text, composition, or validation changes. Drive visuals from <paramref name="state"/>.</summary>
        protected abstract void OnFieldState(in FieldState state);

        /// <summary>
        /// Per-frame hook delivered only between <see cref="RequestTick"/> and <see cref="StopTick"/>.
        /// <paramref name="deltaTime"/> is unscaled.
        /// </summary>
        protected virtual void OnTick(float deltaTime) { }
    }
}
