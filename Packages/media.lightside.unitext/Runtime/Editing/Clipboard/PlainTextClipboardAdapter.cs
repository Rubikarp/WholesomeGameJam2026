namespace LightSide
{
    /// <summary>
    /// Universal-floor clipboard adapter. Writes the selection as
    /// <see cref="ClipboardFormat.PlainText"/> on copy and accepts plain text on paste.
    /// Always registered on every <see cref="UniTextEditable"/>; integrators rarely need
    /// to remove or replace it — it is the format every external clipboard consumer
    /// understands, the guaranteed last-resort path for both directions.
    /// </summary>
    /// <remarks>
    /// Priority is <c>0</c>: every richer adapter wins on paste when its format is
    /// present, and falls back to this one when nothing better is available. On copy the
    /// payload is exactly the text visible inside the selection. Rich channels independently
    /// carry the canonical visible text and formatting spans.
    /// </remarks>
    [HideFromTypeSelector]
    public sealed class PlainTextClipboardAdapter : IClipboardAdapter
    {
        /// <summary>Shared stateless instance. The adapter holds no per-editor state.</summary>
        public static readonly PlainTextClipboardAdapter Instance = new();

        private PlainTextClipboardAdapter() { }

        public ClipboardFormat Format => ClipboardFormat.PlainText;
        public int Priority => 0;

        public string SerializeCopy(ClipboardCopyContext context)
            => context?.SelectedPlainText ?? string.Empty;

        public ClipboardPasteContent DeserializePaste(string payload, ClipboardPasteContext context)
            => string.IsNullOrEmpty(payload) ? null : new ClipboardPasteContent(payload);
    }
}
