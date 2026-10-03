using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Drives a text target with helper text, replacing it with the validation message and an error color while
    /// the field is invalid. The supporting-text slot of a form field.
    /// </summary>
    [Serializable]
    [TypeGroup("Support", 0)]
    [TypeDescription("Helper text below the field that becomes the validation message on error")]
    public sealed partial class SupportingTextDecorator : FieldDecorator
    {
        /// <summary>Text component that displays helper or validation text.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))] private UniTextBase target;

        /// <summary>Guidance shown while the field has no validation error.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Guidance shown while the field is valid.")]
        private string helper;

        /// <summary>Colour used while the field has a validation error.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Text color while the field is invalid.")]
        private Color errorColor = Color.red;

        [NonSerialized] private Color restingColor;

        protected override void OnAttach()
        {
            if (target != null) restingColor = target.color;
        }

        protected override void OnDetach()
        {
            if (target == null) return;
            target.color = restingColor;
            target.Text = helper;
        }

        protected override void OnFieldState(in FieldState state)
        {
            if (target == null) return;
            bool hasError = !state.Validation.IsValid && !string.IsNullOrEmpty(state.Validation.Message);
            target.Text = hasError ? state.Validation.Message : helper;
            target.color = hasError ? errorColor : restingColor;
        }
    }
}
