using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>Visible paste text and its resolved destination formatting.</summary>
    public sealed class ClipboardPasteContent
    {
        /// <summary>The exact visible text to insert.</summary>
        public string Text { get; }

        /// <summary>Destination formatting in UTF-16 coordinates relative to <see cref="Text"/>.</summary>
        public ReadOnlyMemory<ClipboardSpan> Spans { get; }

        /// <summary>
        /// Creates attributed paste content. Every span must fit inside <paramref name="text"/>
        /// and start and end on Unicode scalar boundaries.
        /// </summary>
        public ClipboardPasteContent(string text, ReadOnlyMemory<ClipboardSpan> spans = default)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            var values = spans.Span;
            for (var i = 0; i < values.Length; i++)
            {
                var span = values[i];
                if (span.Length <= 0 || span.Modifier == null || span.Rule == null)
                    throw new ArgumentException("A formatting span is not initialized.", nameof(spans));
                var end = span.Offset + (long)span.Length;
                if (end > text.Length)
                    throw new ArgumentException("A formatting span exceeds the text.", nameof(spans));
                if (!IsScalarBoundary(text, span.Offset) || !IsScalarBoundary(text, (int)end))
                    throw new ArgumentException("A formatting span splits a surrogate pair.", nameof(spans));
            }
            Spans = spans;
        }

        internal static bool IsScalarBoundary(string text, int index)
            => index <= 0 || index >= text.Length
                          || !char.IsHighSurrogate(text[index - 1])
                          || !char.IsLowSurrogate(text[index]);
    }

    /// <summary>
    /// Read-only payload handed to <see cref="IClipboardAdapter.DeserializePaste"/>.
    /// Carries the editor so adapters can resolve external formatting against the destination's styles.
    /// </summary>
    public sealed class ClipboardPasteContext
    {
        /// <summary>The editor that initiated the paste.</summary>
        public UniTextEditable Editable { get; }

        private List<(StyleBinding binding, ModifierClipboardSchema schema)> styleSchemas;

        /// <summary>
        /// The destination's clipboard-capable styles paired with their registered schemas,
        /// collected once per paste operation and shared by every adapter so each builds its
        /// own format map without re-walking the style set.
        /// </summary>
        internal IReadOnlyList<(StyleBinding binding, ModifierClipboardSchema schema)> StyleSchemas
        {
            get
            {
                if (styleSchemas != null) return styleSchemas;
                styleSchemas = new List<(StyleBinding, ModifierClipboardSchema)>(8);
                var bindings = new List<StyleBinding>(8);
                SourceMarkup.CollectStyleBindings(Editable?.TextComponent, bindings);
                for (int i = 0; i < bindings.Count; i++)
                {
                    var schema = ClipboardModifierBindMap.GetSchema(bindings[i].modifier);
                    if (schema != null) styleSchemas.Add((bindings[i], schema));
                }
                return styleSchemas;
            }
        }

        internal ClipboardPasteContext(UniTextEditable editable)
        {
            Editable = editable;
        }
    }
}
