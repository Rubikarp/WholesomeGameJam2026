namespace LightSide
{
    /// <summary>Editable-state snapshot pushed to a <see cref="FieldDecorator"/> whenever it changes.</summary>
    public readonly struct FieldState
    {
        /// <summary>No committed text and no in-progress composition.</summary>
        public readonly bool IsEmpty;

        /// <summary>Whether the editable currently owns input focus.</summary>
        public readonly bool IsFocused;

        /// <summary>Whether an IME composition is in progress.</summary>
        public readonly bool IsComposing;

        /// <summary>Length cap published by <see cref="LengthLimitBehavior"/>; zero means unlimited.</summary>
        public readonly LengthLimit LengthLimit;

        /// <summary>The validation state currently published by the editable's validators.</summary>
        public readonly ValidationState Validation;

        /// <summary>Creates an immutable snapshot delivered to a field decorator.</summary>
        public FieldState(bool isEmpty, bool isFocused, bool isComposing,
            LengthLimit lengthLimit, ValidationState validation)
        {
            IsEmpty = isEmpty;
            IsFocused = isFocused;
            IsComposing = isComposing;
            LengthLimit = lengthLimit;
            Validation = validation;
        }
    }
}
