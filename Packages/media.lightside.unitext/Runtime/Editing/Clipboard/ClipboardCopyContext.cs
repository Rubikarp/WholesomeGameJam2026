using System;

namespace LightSide
{
    /// <summary>
    /// Read-only selection payload handed to <see cref="IClipboardAdapter.SerializeCopy"/>.
    /// </summary>
    public sealed class ClipboardCopyContext
    {
        /// <summary>The editable field that initiated the copy operation.</summary>
        public UniTextEditable Editable { get; }

        /// <summary>Selection serialized in the source field's own markup syntax.</summary>
        public string SelectedSource { get; }

        /// <summary>Text written to the plain-text clipboard channel.</summary>
        public string SelectedPlainText { get; }

        /// <summary>Selection start in the active editing view, measured in codepoints.</summary>
        public int SelectionStart { get; }

        /// <summary>Selection length in the active editing view, measured in codepoints.</summary>
        public int SelectionLength { get; }

        /// <summary>Canonical visible document text covered by the selection.</summary>
        public string VisibleText { get; }

        /// <summary>Persistent formatting clipped to <see cref="VisibleText"/>.</summary>
        public ReadOnlyMemory<ClipboardSpan> Spans { get; }

        internal ClipboardCopyContext(UniTextEditable editable, string selectedSource,
            string selectedPlainText, string visibleText, ClipboardSpan[] spans,
            int selectionStart, int selectionLength)
        {
            Editable = editable;
            SelectedSource = selectedSource ?? string.Empty;
            SelectedPlainText = selectedPlainText ?? string.Empty;
            SelectionStart = selectionStart;
            SelectionLength = selectionLength;
            VisibleText = visibleText ?? string.Empty;
            Spans = spans ?? Array.Empty<ClipboardSpan>();
        }
    }
}
