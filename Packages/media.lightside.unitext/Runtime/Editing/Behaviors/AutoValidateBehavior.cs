using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Re-runs the editor's <see cref="InputValidatorBase"/>s on a schedule and writes the result to
    /// <see cref="UniTextEditable.Validation"/> for decorators to show. Without it, validation runs only when
    /// app code calls <see cref="UniTextEditable.SetValidation"/>.
    /// </summary>
    [Serializable]
    [TypeGroup("Validation", 1)]
    [TypeDescription("Re-run validators automatically (on change, blur, or submit) to drive the error state")]
    public sealed partial class AutoValidateBehavior : InputBehavior
    {
        /// <summary>Schedule that triggers validation for this editor.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("When the validators re-run.")]
        private AutoValidateMode mode = AutoValidateMode.OnUnfocus;

        [NonSerialized] private readonly List<InputValidatorBase> validators = new();

        protected override void OnEnable()
        {
            if (mode == AutoValidateMode.OnValueChanged || mode == AutoValidateMode.Always)
                editable.ValueChanged += OnValueChanged;
            if (mode == AutoValidateMode.OnUnfocus)
                editable.Defocused += OnDefocused;
            if (mode == AutoValidateMode.OnSubmit)
                editable.Submitted += OnSubmit;
            if (mode == AutoValidateMode.Always)
                editable.FrameTicked += InitialValidate;
        }

        protected override void OnDisable()
        {
            editable.ValueChanged -= OnValueChanged;
            editable.Defocused -= OnDefocused;
            editable.Submitted -= OnSubmit;
            editable.FrameTicked -= InitialValidate;
        }

        /// <summary>
        /// Always-mode initial validation is deferred one frame: enabling runs locals before preset
        /// copies exist, so an immediate pass would see a partial validator set.
        /// </summary>
        private void InitialValidate(float _)
        {
            editable.FrameTicked -= InitialValidate;
            Validate();
        }

        private void OnValueChanged(string _) => Validate();
        private void OnDefocused(EditingEndReason _) => Validate();
        private void OnSubmit(string _) => Validate();

        private void Validate()
        {
            validators.Clear();
            editable.GetBehaviors(validators);
            foreach (var validator in validators)
            {
                var state = validator.Validate(editable);
                if (!state.IsValid)
                {
                    editable.SetValidation(state);
                    return;
                }
            }
            editable.SetValidation(ValidationState.Valid());
        }
    }
}
