using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Native UniText round-trip adapter. Carries the selection as a structured
    /// <see cref="UniTextClipboardFragment"/> (visible text + markup spans keyed by modifier
    /// <see cref="BaseModifier.Signature"/>) under <see cref="ClipboardFormat.UniTextSource"/>
    /// (<c>application/vnd.lightside.unitext</c>, RFC 6838 vendor tree).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fidelity is semantic, not byte-for-byte: copy records the attributed document's visible text and
    /// persistent spans; paste resolves each span directly to the destination modifier and rule,
    /// dropping it to plain text when the destination has no such modifier and never leaking raw
    /// tags. Inline objects round-trip as a one-character Object Replacement Character span.
    /// </para>
    /// <para>
    /// Priority is <c>100</c>, above HTML (50) and Markdown (40): UniText → UniText keeps the native fragment.
    /// External apps that do not recognise the format fall back to <see cref="PlainTextClipboardAdapter"/>.
    /// </para>
    /// </remarks>
    [HideFromTypeSelector]
    public sealed class UniTextSourceClipboardAdapter : IClipboardAdapter
    {
        /// <summary>Shared stateless instance. Map building reads the editor passed in via context.</summary>
        public static readonly UniTextSourceClipboardAdapter Instance = new();

        private UniTextSourceClipboardAdapter() { }

        public ClipboardFormat Format => ClipboardFormat.UniTextSource;
        public int Priority => 100;

        public string SerializeCopy(ClipboardCopyContext context)
        {
            if (string.IsNullOrEmpty(context?.VisibleText)) return null;

            var spans = context.Spans.Span;
            var carried = new List<UniTextClipboardFragment.Span>(spans.Length);
            for (var i = 0; i < spans.Length; i++)
            {
                var s = spans[i];
                if (string.IsNullOrEmpty(s.Signature)) continue;
                carried.Add(new UniTextClipboardFragment.Span
                {
                    offset = s.Offset, length = s.Length,
                    signature = s.Signature, parameter = s.Parameter, selfClosing = s.IsAtomic,
                });
            }

            return JsonUtility.ToJson(new UniTextClipboardFragment
            {
                text = context.VisibleText,
                spans = carried.ToArray(),
            });
        }

        public ClipboardPasteContent DeserializePaste(string payload, ClipboardPasteContext context)
        {
            if (string.IsNullOrEmpty(payload) || payload.Length > ClipboardBudget.MaxInputChars) return null;
            var component = context?.Editable?.TextComponent;
            if (component == null) return null;

            UniTextClipboardFragment fragment;
            try { fragment = JsonUtility.FromJson<UniTextClipboardFragment>(payload); }
            catch { return null; }
            if (fragment?.text == null || fragment.text.Length > ClipboardBudget.MaxInputChars) return null;

            var text = fragment.text;
            if (text.Length > ClipboardBudget.MaxOutputChars)
            {
                var cut = Utf16.SafePrefixLength(text.AsSpan(), ClipboardBudget.MaxOutputChars);
                text = text.Substring(0, cut);
            }

            var spans = SanitizeSpans(fragment.spans, text, component);
            return new ClipboardPasteContent(text, spans);
        }

        /// <summary>
        /// The payload is hostile cross-app input: every span's geometry is validated against
        /// the actual text before any of it is applied. Out-of-range or overflowing offsets,
        /// non-positive lengths, and missing signatures are dropped; ends are clamped to the
        /// text. Survivors come back sorted outer-first, ready for attributed insertion.
        /// </summary>
        private static ClipboardSpan[] SanitizeSpans(UniTextClipboardFragment.Span[] spans, string text,
            UniTextBase component)
        {
            if (spans == null || spans.Length == 0) return Array.Empty<ClipboardSpan>();

            var textLength = text.Length;
            var valid = new List<ClipboardSpan>(spans.Length);
            for (var i = 0; i < spans.Length; i++)
            {
                var s = spans[i];
                if (string.IsNullOrEmpty(s.signature)) continue;
                if (s.offset < 0 || s.offset >= textLength) continue;
                if (!component.TryGetStyleBySignature(s.signature, out var style)
                    || style.Source is not ParseRule rule) continue;
                int length;
                if (s.selfClosing)
                {
                    length = 1;
                }
                else
                {
                    var spanEnd = s.offset + (long)s.length;
                    if (spanEnd > textLength) spanEnd = textLength;
                    length = (int)(spanEnd - s.offset);
                    if (length <= 0) continue;
                }
                if (!ClipboardPasteContent.IsScalarBoundary(text, s.offset)
                    || !ClipboardPasteContent.IsScalarBoundary(text, s.offset + length)) continue;
                valid.Add(new ClipboardSpan(s.offset, length, style.Modifier, rule,
                    SourceMarkup.SanitizeParameter(s.parameter), s.selfClosing));
            }

            var result = valid.ToArray();
            Array.Sort(result, static (a, b) =>
                a.Offset != b.Offset ? a.Offset.CompareTo(b.Offset) : (b.Offset + b.Length).CompareTo(a.Offset + a.Length));
            return result;
        }
    }
}
