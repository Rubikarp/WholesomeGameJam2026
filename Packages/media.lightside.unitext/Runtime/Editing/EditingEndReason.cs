namespace LightSide
{
    /// <summary>
    /// How an editing session concluded, carried by <see cref="UniTextEditable.Defocused"/>.
    /// Integrators switch on it to tell an accepted edit from an abandoned one without correlating
    /// <see cref="UniTextEditable.Submitted"/> and <see cref="UniTextEditable.Cancelled"/> against
    /// the end of the session themselves.
    /// </summary>
    public enum EditingEndReason
    {
        /// <summary>
        /// Focus left without a verdict on the content: another field took focus, the pointer
        /// landed elsewhere, the platform dismissed the keyboard, or the session was closed
        /// programmatically. The text stands as edited.
        /// </summary>
        Dismissed,

        /// <summary>The user accepted the content and the session ended on that submission.</summary>
        Committed,

        /// <summary>The user abandoned the edit and the session ended on that cancellation.</summary>
        Cancelled,
    }
}
