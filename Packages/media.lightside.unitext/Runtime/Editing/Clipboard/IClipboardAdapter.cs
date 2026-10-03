namespace LightSide
{
    /// <summary>
    /// Per-format clipboard pipeline stage. Copy adapters translate the canonical attributed
    /// selection into an external format; paste adapters translate that format into attributed
    /// destination content. The editor's <see cref="UniTextEditable.Copy"/>
    /// walks every registered adapter to build a multi-format clipboard write; on
    /// <see cref="UniTextEditable.Paste"/> the highest-<see cref="Priority"/> adapter
    /// whose <see cref="Format"/> is present on the clipboard wins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Format mapping (semantic ↔ external markup) is declared per modifier type in
    /// <see cref="ClipboardModifierBindMap"/>; adapters are the orchestration layer
    /// that walks the active modifier set + a serializer / parser for the format. This
    /// matches the design used by ProseMirror (MarkType.toDOM/parseDOM), Lexical
    /// (LexicalNode.exportDOM/importDOM), and CKEditor 5 (model ↔ view converters):
    /// the syntactic shape is per-format, the semantic effect is per-modifier, and the
    /// adapter is the bridge.
    /// </para>
    /// <para>
    /// Built-in adapters registered by default on every <see cref="UniTextEditable"/>:
    /// <list type="bullet">
    ///   <item><description><see cref="PlainTextClipboardAdapter"/> — universal floor; always wins when nothing richer is present.</description></item>
    ///   <item><description><see cref="UniTextSourceClipboardAdapter"/> — perfect-fidelity round-trip via the vendor-tree format <see cref="ClipboardFormat.UniTextSource"/>.</description></item>
    ///   <item><description><see cref="TagHtmlClipboardAdapter"/> — HTML channel (paste from browsers / Word / Notion; copy as HTML).</description></item>
    ///   <item><description><see cref="MarkdownClipboardAdapter"/> — Markdown channel.</description></item>
    /// </list>
    /// The built-in set above is fixed per field.
    /// </para>
    /// </remarks>
    public interface IClipboardAdapter
    {
        /// <summary>
        /// Clipboard format this adapter writes on copy and accepts on paste. The
        /// editor uses this to populate the multi-format write and to pick the
        /// appropriate adapter on paste.
        /// </summary>
        ClipboardFormat Format { get; }

        /// <summary>
        /// Selection priority on paste. Higher wins. Built-in priorities:
        /// <see cref="UniTextSourceClipboardAdapter"/>=100 (lossless),
        /// HTML=50, Markdown=40, <see cref="PlainTextClipboardAdapter"/>=0 (floor).
        /// Custom adapters that should preempt the built-ins use values above 100.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Serialize the current selection into this adapter's clipboard format.
        /// Returns the text payload to attach under <see cref="Format"/>'s identifier,
        /// or <see langword="null"/> / empty to skip this format in the multi-format
        /// write (e.g. an HTML adapter with no applicable modifiers in the selection).
        /// </summary>
        string SerializeCopy(ClipboardCopyContext context);

        /// <summary>
        /// Deserialize external clipboard payload into visible text and resolved destination spans. Returns
        /// <see langword="null"/> to abort and let the pipeline fall through to the
        /// next-best format on the clipboard.
        /// </summary>
        ClipboardPasteContent DeserializePaste(string payload, ClipboardPasteContext context);
    }

    /// <summary>The fixed built-in adapter set used by every editable.</summary>
    internal static class ClipboardAdapterDefaults
    {
        internal static readonly IClipboardAdapter[] All =
        {
            PlainTextClipboardAdapter.Instance,
            UniTextSourceClipboardAdapter.Instance,
            TagHtmlClipboardAdapter.Instance,
            MarkdownClipboardAdapter.Instance,
        };
    }
}
