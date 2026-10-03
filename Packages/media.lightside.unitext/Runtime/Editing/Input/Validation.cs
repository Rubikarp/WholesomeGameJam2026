namespace LightSide
{
    /// <summary>
    /// Well-known <see cref="ValidationState.Status"/> tokens. The status is an open string — a project may use
    /// its own (e.g. "warning", "available") and have decorators react to it; these are just the common ones.
    /// </summary>
    public static class ValidationStatus
    {
        public const string Invalid = "invalid";

        /// <summary>Asynchronous validation is in flight; the result is not yet known.</summary>
        public const string Pending = "pending";
    }

    /// <summary>
    /// Validation state of a field — an open <see cref="Status"/> token (empty = valid) plus a human-readable
    /// <see cref="Message"/>. Held on <see cref="UniTextEditable"/>, set by validators or app code, read by decorators.
    /// </summary>
    public readonly struct ValidationState
    {
        private readonly string status;
        private readonly string message;

        /// <summary>Open status token; empty when valid. See <see cref="ValidationStatus"/> for common values.</summary>
        public string Status => status ?? string.Empty;

        /// <summary>Human-readable reason a supporting-text decorator shows.</summary>
        public string Message => message ?? string.Empty;

        /// <summary>True when <see cref="Status"/> is empty.</summary>
        public bool IsValid => string.IsNullOrEmpty(status);

        public ValidationState(string status, string message = null)
        {
            this.status = status;
            this.message = message;
        }

        public static ValidationState Valid() => new(null, null);
        public static ValidationState Invalid(string message) => new(ValidationStatus.Invalid, message);
        public static ValidationState Pending() => new(ValidationStatus.Pending, null);
    }

    /// <summary>When an <see cref="AutoValidateBehavior"/> re-runs the editor's validators.</summary>
    public enum AutoValidateMode
    {
        OnValueChanged,

        /// <summary>When the field loses focus.</summary>
        OnUnfocus,
        OnSubmit,

        /// <summary>On every value change, including before the user has interacted.</summary>
        Always
    }
}
