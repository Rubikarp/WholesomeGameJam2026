using System;
using System.Collections.Generic;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// A clipboard-relevant style: its modifier, the rule that writes its source syntax, and
    /// the rule's external source token.
    /// </summary>
    internal readonly struct StyleBinding
    {
        public readonly BaseModifier modifier;
        public readonly BaseModifier appliedModifier;
        public readonly ParseRule rule;
        public readonly string sourceToken;

        /// <summary>
        /// The <c>;</c>-parameter slot this leaf consumes: its index within the owning top-level
        /// composite, 0 for a plain style. A writer targeting the leaf with a value places it at
        /// this slot (<c>";;value"</c> for slot 2) so the composite's positional split routes it.
        /// </summary>
        public readonly int childIndex;

        public StyleBinding(BaseModifier modifier, BaseModifier appliedModifier, ParseRule rule,
            string sourceToken, int childIndex)
        {
            this.modifier = modifier;
            this.appliedModifier = appliedModifier;
            this.rule = rule;
            this.sourceToken = sourceToken;
            this.childIndex = childIndex;
        }
    }

    /// <summary>
    /// One persistent formatting annotation clipped to UTF-16 coordinates within
    /// <see cref="ClipboardCopyContext.VisibleText"/>.
    /// </summary>
    public readonly struct ClipboardSpan
    {
        /// <summary>UTF-16 offset from the start of the copied visible text.</summary>
        public int Offset { get; }

        /// <summary>Length in UTF-16 code units.</summary>
        public int Length { get; }

        /// <summary>The modifier applied to this attributed range.</summary>
        public BaseModifier Modifier { get; }

        /// <summary>Stable modifier identity used by the native UniText clipboard format.</summary>
        public string Signature => Modifier.Signature;

        /// <summary>The parse rule that owns this attributed range.</summary>
        public ParseRule Rule { get; }

        /// <summary>The source rule's externally known syntax token, when it has one.</summary>
        public string SourceToken { get; }

        /// <summary>The formatting parameter carried by the source annotation.</summary>
        public string Parameter { get; }

        /// <summary>Whether the span represents one indivisible replacement rather than a styled range.</summary>
        public bool IsAtomic { get; }

        /// <summary>
        /// Creates one formatting range in UTF-16 coordinates. The range must be non-empty;
        /// <paramref name="modifier"/> and <paramref name="rule"/> identify the destination style.
        /// </summary>
        public ClipboardSpan(int offset, int length, BaseModifier modifier, ParseRule rule,
            string parameter = null, bool isAtomic = false, string sourceToken = null)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
            Offset = offset;
            Length = length;
            Modifier = modifier ?? throw new ArgumentNullException(nameof(modifier));
            Rule = rule ?? throw new ArgumentNullException(nameof(rule));
            SourceToken = sourceToken ?? rule.SourceToken;
            Parameter = parameter;
            IsAtomic = isAtomic;
        }
    }

    /// <summary>
    /// Per-format emission hooks for <see cref="SourceMarkup.RenderSpans"/>. The walker owns the structure
    /// (literal runs, span nesting, depth flattening, overlap dropping); the renderer contributes only the
    /// format-specific pieces: literal escaping, inline-object emission, and how a styled span wraps its
    /// already-rendered inner content.
    /// </summary>
    internal interface IClipboardSpanRenderer
    {
        void Literal(StringBuilder sb, string text, int start, int end);
        void Object(StringBuilder sb, in ClipboardSpan span);
        void Styled(StringBuilder sb, in ClipboardSpan span, string inner);
    }

    /// <summary>
    /// Shared rendering and source-escaping primitives for clipboard adapters. Copy renderers receive the
    /// attributed document's visible text and persistent spans directly.
    /// </summary>
    internal static class SourceMarkup
    {
        internal static bool IsTagNameChar(char c)
            => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_';

        /// <summary>
        /// Places a leaf-targeted value at the binding's <c>;</c>-slot (<c>";;value"</c> for slot 2)
        /// so a composite's positional parameter split routes it to that child. Slot 0 and empty
        /// values pass through.
        /// </summary>
        internal static string SlotParameter(int slot, string value)
            => slot > 0 && !string.IsNullOrEmpty(value) ? new string(';', slot) + value : value;

        private static readonly char[] parameterBreakers = { '<', '>', '\\', '\n', '\r' };

        /// <summary>
        /// The one neutralization gate for parameters arriving from untrusted payloads (HTML
        /// attributes, markdown URLs, vendor-fragment JSON). Markup structure characters, the
        /// escape prefix, and line breaks are stripped before the parameter enters the attributed
        /// document, so its eventual source serialization cannot open or terminate syntax.
        /// </summary>
        internal static string SanitizeParameter(string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return parameter;
            if (parameter.IndexOfAny(parameterBreakers) < 0) return parameter;

            var sb = new StringBuilder(parameter.Length);
            for (var i = 0; i < parameter.Length; i++)
            {
                var c = parameter[i];
                if (c is not ('<' or '>' or '\\' or '\n' or '\r'))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Collects one binding per active style LEAF — modifier plus the rule that writes its source — the
        /// single source for both clipboard directions. A composite style binds each child under the style's
        /// own rule, so format lookups (HTML tag, markdown marker, linkify) see the capable leaf while
        /// application through the rule styles the range with the whole composite. Copy resolves each
        /// match's schema by modifier identity; paste resolves external syntax directly to the
        /// destination modifier and rule. Both honour only the formats the field has a style for.
        /// </summary>
        internal static void CollectStyleBindings(UniTextBase text, List<StyleBinding> into)
        {
            if (text == null) return;
            var styles = text.Styles;
            for (int i = 0; i < styles.Count; i++)
            {
                var style = styles[i];
                if (style?.Modifier == null || style.Source is not ParseRule rule) continue;
                AddLeafBindings(style.Modifier, style.Modifier, rule, rule.SourceToken,
                    into, slot: -1);
            }
        }

        /// <summary>
        /// <paramref name="slot"/> = the <c>;</c>-parameter slot this subtree consumes; -1 = the style
        /// root, whose direct children claim slots by position. Deeper nesting keeps the top-level
        /// slot — a flat parameter string cannot address inside a nested composite's segment.
        /// </summary>
        private static void AddLeafBindings(BaseModifier modifier, BaseModifier appliedModifier,
            ParseRule rule, string sourceToken, List<StyleBinding> into, int slot)
        {
            if (modifier.Children is { } children)
            {
                for (int i = 0; i < children.Count; i++)
                    if (children[i] != null) AddLeafBindings(children[i], appliedModifier, rule,
                        sourceToken, into, slot < 0 ? i : slot);
            }
            else into.Add(new StyleBinding(modifier, appliedModifier, rule, sourceToken,
                Math.Max(slot, 0)));
        }

        /// <summary>
        /// Renders (visible text + sorted spans) through <paramref name="renderer"/> — the one nesting walk
        /// shared by every clipboard channel. A span crossing its parent's end is clamped to it; overlapping
        /// (non-nested) spans keep the first and drop the rest; nesting past
        /// <see cref="ClipboardBudget.MaxDepth"/> flattens the tail to literals so recursion depth stays
        /// bounded on hostile input.
        /// </summary>
        internal static void RenderSpans(StringBuilder sb, string text, ReadOnlySpan<ClipboardSpan> spans,
            IClipboardSpanRenderer renderer)
        {
            var idx = 0;
            RenderRange(sb, text, 0, text.Length, spans, ref idx, renderer, 0);
        }

        private static void RenderRange(StringBuilder sb, string text, int lo, int hi,
            ReadOnlySpan<ClipboardSpan> spans, ref int idx, IClipboardSpanRenderer renderer, int depth)
        {
            if (depth >= ClipboardBudget.MaxDepth)
            {
                renderer.Literal(sb, text, lo, hi);
                while (idx < spans.Length && spans[idx].Offset < hi) idx++;
                return;
            }

            var p = lo;
            while (idx < spans.Length && spans[idx].Offset < hi)
            {
                var s = spans[idx];
                if (s.Offset < p) { idx++; continue; }

                if (s.Offset > p) renderer.Literal(sb, text, p, s.Offset);
                idx++;
                var sEnd = Math.Min(s.Offset + s.Length, hi);

                if (s.IsAtomic)
                {
                    renderer.Object(sb, in s);
                    while (idx < spans.Length && spans[idx].Offset < sEnd) idx++;
                }
                else
                {
                    var inner = new StringBuilder();
                    RenderRange(inner, text, s.Offset, sEnd, spans, ref idx, renderer, depth + 1);
                    renderer.Styled(sb, in s, inner.ToString());
                }
                p = sEnd;
            }
            if (p < hi) renderer.Literal(sb, text, p, hi);
        }
    }
}
