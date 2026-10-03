namespace LightSide
{
    /// <summary>
    /// All possible editing actions for an input field.
    /// Decouples input (key presses) from execution (text operations).
    /// </summary>
    /// <remarks>
    /// Values are ordered so that range checks can be used for permission filtering — use the
    /// explicit range constants (<see cref="FirstMove"/> / <see cref="LastMove"/>,
    /// <see cref="FirstSelect"/> / <see cref="LastSelect"/>), never member ordering directly:
    /// a member inserted mid-enum must extend the constants or it silently changes
    /// classification. Text characters do not appear here — they arrive via a separate text
    /// input channel.
    /// </remarks>
    public enum EditAction
    {
        None,

        /// <summary>Key consumed with no action — blocks the platform key map's default binding.</summary>
        Ignore,

        MoveLeft,
        MoveRight,
        MoveUp,
        MoveDown,
        MovePageUp,
        MovePageDown,
        MoveWordLeft,
        MoveWordRight,
        MoveLineStart,
        MoveLineEnd,
        MoveDocStart,
        MoveDocEnd,

        SelectLeft,
        SelectRight,
        SelectUp,
        SelectDown,
        SelectPageUp,
        SelectPageDown,
        SelectWordLeft,
        SelectWordRight,
        SelectLineStart,
        SelectLineEnd,
        SelectDocStart,
        SelectDocEnd,
        SelectAll,

        InsertNewline,

        /// <summary>
        /// Not produced by the platform key map — Tab is reserved for focus traversal (the web/WPF
        /// convention). A behavior may bind it through the <c>KeyResolver</c> hook for multiline fields.
        /// </summary>
        InsertTab,
        DeletePrev,
        DeleteNext,
        DeleteWordPrev,
        DeleteWordNext,
        DeleteLineStart,
        DeleteLineEnd,
        TransposeChars,

        Copy,
        Cut,
        Paste,
        PasteAsPlain,

        Undo,
        Redo,

        Submit,
        Cancel,

        /// <summary>First caret-movement action — range constant for permission filtering.</summary>
        FirstMove = MoveLeft,

        /// <summary>Last caret-movement action — range constant for permission filtering.</summary>
        LastMove = MoveDocEnd,

        /// <summary>First selection action — range constant for permission filtering.</summary>
        FirstSelect = SelectLeft,

        /// <summary>Last selection action — range constant for permission filtering.</summary>
        LastSelect = SelectAll,
    }
}
