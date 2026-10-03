namespace LightSide
{
    /// <summary>
    /// Payload of <see cref="UniTextSelectable.SelectionChanged"/>. Carries previous and
    /// current selection state plus a hierarchical <see cref="UserEvent"/> string that
    /// classifies the change (per revised D-002, CodeMirror 6 convention).
    /// </summary>
    /// <remarks>
    /// Standard values are the <see cref="SelectionChangeReason"/> constants. New categories are just new
    /// strings — the struct is durable and never breaks subscribers, which can match a family by prefix
    /// (e.g. <c>UserEvent.StartsWith("select.")</c>).
    /// </remarks>
    public readonly struct SelectionChangedArgs
    {
        /// <summary>Selection state before the change.</summary>
        public TextSelection Previous { get; }

        /// <summary>Selection state after the change.</summary>
        public TextSelection Current { get; }

        /// <summary>Hierarchical category string. See remarks for standard values.</summary>
        public string UserEvent { get; }

        public SelectionChangedArgs(TextSelection previous, TextSelection current, string userEvent)
        {
            Previous = previous;
            Current = current;
            UserEvent = userEvent;
        }
    }
}
