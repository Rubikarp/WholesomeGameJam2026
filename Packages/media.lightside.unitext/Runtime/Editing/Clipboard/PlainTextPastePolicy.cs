namespace LightSide
{
    /// <summary>
    /// How a <see cref="UniTextEditable"/> interprets plain-text paste — the only channel whose markup-vs-literal
    /// intent is unknown because the format carries no semantic spans.
    /// </summary>
    public enum PlainTextPastePolicy
    {
        /// <summary>Pick by <see cref="UniTextEditable.MarkupVisibility"/>: Raw parses, every other mode is literal.</summary>
        Auto,

        /// <summary>Insert verbatim — markup characters become literal text, never parsed.</summary>
        Literal,

        /// <summary>Reparse with this field's own rules, as if the text were typed.</summary>
        Parse,
    }
}
