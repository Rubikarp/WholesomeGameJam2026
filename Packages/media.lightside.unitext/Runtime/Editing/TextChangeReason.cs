namespace LightSide
{
    /// <summary>
    /// Hierarchical reason string carried by <see cref="UniTextEditable.DocumentChanged"/>.
    /// Integrators switch on these to react differently to user typing vs paste vs
    /// programmatic mutation. Format is dotted-prefix so consumers can match by prefix:
    /// <c>input.*</c> is exclusively user-originated (so <c>reason.StartsWith("input.")</c>
    /// means "the user did this"), <c>program.*</c> is integrator code, <c>sync.*</c> is
    /// network / collaborative sync. Narrower prefixes refine further — e.g.
    /// <c>reason.StartsWith("input.type")</c> covers <see cref="Type"/> and
    /// <see cref="TypeCompose"/>.
    /// </summary>
    public static class TextChangeReason
    {
        /// <summary>User-driven character input (keyboard, mobile keyboard, on-screen).</summary>
        public const string Type = "input.type";

        /// <summary>IME composition commit. Distinguish from plain <see cref="Type"/> for CJK/emoji autocorrect handling.</summary>
        public const string TypeCompose = "input.type.compose";

        /// <summary>Paste from system clipboard (Ctrl+V, context-menu, programmatic Paste).</summary>
        public const string Paste = "input.paste";

        /// <summary>Deletion of an explicit range (selection) — no direction.</summary>
        public const string Delete = "input.delete";

        /// <summary>Backward deletion from the caret — Backspace, word / line-start variants.</summary>
        public const string DeleteBackward = "input.delete.backward";

        /// <summary>Forward deletion from the caret — Delete key, word / line-end variants.</summary>
        public const string DeleteForward = "input.delete.forward";

        /// <summary>Cut to clipboard — copy followed by selection deletion.</summary>
        public const string Cut = "input.cut";

        /// <summary>Drag-and-drop text insertion. Reserved; emitted when drop pipeline lands.</summary>
        public const string Drop = "input.drop";

        /// <summary>Integrator-driven mutation via <c>SetText</c> / <c>SetTextProgrammatic</c> — not user input, hence outside <c>input.*</c>.</summary>
        public const string Programmatic = "program.set";

        /// <summary>Autoformat conversion — typed markup replaced by its styling.</summary>
        public const string Format = "input.format";

        /// <summary>Undo / Redo replay. User-visible restoration of a prior document state.</summary>
        public const string Restore = "input.restore";

        /// <summary>Network / collaborative-sync inbound — not user input, hence outside <c>input.*</c>. Reserved for P2.S.</summary>
        public const string Network = "sync.network";
    }
}
