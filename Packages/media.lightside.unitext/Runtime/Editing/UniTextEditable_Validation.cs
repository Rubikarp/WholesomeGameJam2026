using System;

namespace LightSide
{
    public partial class UniTextEditable
    {
        [NonSerialized] private ValidationState validation = ValidationState.Valid();

        /// <summary>Current content-validity state — written by validators (via <see cref="AutoValidateBehavior"/>) or app code through <see cref="SetValidation"/>, read by field decorators.</summary>
        public ValidationState Validation => validation;

        /// <summary>Occurs when <see cref="Validation"/> has changed.</summary>
        public event Action<ValidationState> ValidationChanged;

        /// <summary>
        /// Sets the validation state and fires <see cref="ValidationChanged"/>. The editor only holds the state;
        /// what to validate and when lives in an <see cref="AutoValidateBehavior"/> or app code (set
        /// <see cref="ValidationState.Pending"/> while an async check runs, then the result).
        /// </summary>
        public void SetValidation(ValidationState state)
        {
            if (validation.Status == state.Status && validation.Message == state.Message) return;
            validation = state;
            ValidationChanged?.Invoke(validation);
        }
    }
}
