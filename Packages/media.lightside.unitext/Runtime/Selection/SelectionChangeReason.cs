namespace LightSide
{
    /// <summary>
    /// Standard <see cref="SelectionChangedArgs.UserEvent"/> values (CodeMirror 6 convention). Dotted-prefix
    /// so subscribers can match a whole family with <c>reason.StartsWith("select.")</c>. New categories are
    /// just new strings; this is the canonical set the engine emits.
    /// </summary>
    public static class SelectionChangeReason
    {
        public const string Pointer = "select.pointer";
        public const string Move = "select.move";
        public const string Extend = "select.extend";
        public const string Word = "select.word";
        public const string Line = "select.line";
        public const string Paragraph = "select.paragraph";
        public const string All = "select.all";
        public const string Programmatic = "select.programmatic";

        /// <summary>Selection moved as a side-effect of a text mutation (insert / delete / IME commit).</summary>
        public const string Input = "select.input";

        /// <summary>Selection clamped after the text shrank underneath it.</summary>
        public const string Clamp = "select.clamp";

        /// <summary>Selection remapped to an equivalent canonical position after a projection or view change.</summary>
        public const string Normalize = "select.normalize";

        /// <summary>Selection placed on the result of a style edit (wrap / strip / split).</summary>
        public const string Style = "select.style";

        /// <summary>Selection restored by undo / redo replay.</summary>
        public const string Restore = "select.restore";

        /// <summary>Selection collapsed or cleared on focus loss.</summary>
        public const string Defocus = "select.defocus";
    }
}
