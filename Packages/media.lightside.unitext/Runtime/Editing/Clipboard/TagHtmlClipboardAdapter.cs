using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// HTML clipboard adapter. Converts between the selection's span model and the
    /// <see cref="ClipboardFormat.Html"/> external format using the per-type schemas
    /// registered in <see cref="ClipboardModifierBindMap"/>
    /// (<see cref="ModifierClipboardSchema.Html"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Copy renders the attributed document's shared visible-text-and-spans model —
    /// each span resolves its HTML schema by modifier identity, so whatever syntax authored the
    /// styling (tags or markers), the HTML carries it. On paste every modifier that declares an
    /// HTML schema contributes the mapping <c>HTML element name(s)</c> → the style's rule; the
    /// map covers only the formats the field has a style for; elements with no mapping are
    /// stripped, their inner content survives.
    /// </para>
    /// <para>
    /// Priority is <c>50</c>: <see cref="UniTextSourceClipboardAdapter"/> (100, perfect-fidelity)
    /// wins for UniText → UniText pastes; HTML wins over plain text for paste from external
    /// rich-text apps (Word, Notion, Slack, Chrome) and from UniText into them.
    /// </para>
    /// <para>
    /// Paste input is treated as hostile per <see cref="ClipboardBudget"/>: the parser is a
    /// single-pass tokenizer with an explicit open-element stack (no recursion, no rescans),
    /// oversized payloads degrade to the same pass with no element map (plain text out), and
    /// nesting past the depth cap flattens. Inter-element whitespace collapses to one space the
    /// way CSS renders it (literal only inside <c>&lt;pre&gt;</c>), so pretty-printed Word /
    /// Outlook CF_HTML pastes without spurious blank lines and indent runs.
    /// </para>
    /// </remarks>
    [HideFromTypeSelector]
    public sealed class TagHtmlClipboardAdapter : IClipboardAdapter
    {
        /// <summary>Shared stateless instance. Map building reads the editor passed in via context.</summary>
        public static readonly TagHtmlClipboardAdapter Instance = new();

        private TagHtmlClipboardAdapter() { }

        public ClipboardFormat Format => ClipboardFormat.Html;
        public int Priority => 50;

        public string SerializeCopy(ClipboardCopyContext context)
        {
            if (string.IsNullOrEmpty(context?.VisibleText)) return null;

            var text = context.VisibleText;
            var sb = new StringBuilder(text.Length + 32);
            SourceMarkup.RenderSpans(sb, text, context.Spans.Span, HtmlRenderer.Instance);
            return sb.ToString();
        }

        public ClipboardPasteContent DeserializePaste(string payload, ClipboardPasteContext context)
        {
            if (string.IsNullOrEmpty(payload)) return null;

            var map = payload.Length > ClipboardBudget.MaxInputChars ? null : BuildHtmlElementToTagMap(context);
            var sb = new StringBuilder(Math.Min(payload.Length, ClipboardBudget.MaxOutputChars));
            var spans = new List<ClipboardSpan>();
            ParseHtml(payload, map, sb, spans);
            TrimTrailingNewlines(sb);
            ClipSpans(spans, sb.Length);
            return sb.Length == 0 ? null : new ClipboardPasteContent(sb.ToString(), spans.ToArray());
        }

        private static int ResolveMatches(List<TagSchema> candidates, string element,
            string attributesRaw, List<ResolvedStyle> into)
        {
            into.Clear();
            if (candidates == null) return 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                var entry = candidates[i];
                string parameter = null;
                if (entry.schema.ExtractParameter != null)
                {
                    parameter = entry.schema.ExtractParameter(element, attributesRaw);
                    if (parameter == null) continue;
                }
                var duplicate = false;
                for (int k = 0; k < into.Count; k++)
                {
                    if (!ReferenceEquals(into[k].rule, entry.binding.rule)) continue;
                    duplicate = true;
                    break;
                }
                if (!duplicate) into.Add(new ResolvedStyle(entry.binding.appliedModifier,
                    entry.binding.rule, entry.binding.sourceToken,
                    SourceMarkup.SlotParameter(entry.binding.childIndex,
                        SourceMarkup.SanitizeParameter(parameter))));
            }
            return into.Count;
        }

        private readonly struct ResolvedStyle
        {
            public readonly BaseModifier modifier;
            public readonly ParseRule rule;
            public readonly string sourceToken;
            public readonly string parameter;

            public ResolvedStyle(BaseModifier modifier, ParseRule rule, string sourceToken,
                string parameter)
            {
                this.modifier = modifier;
                this.rule = rule;
                this.sourceToken = sourceToken;
                this.parameter = parameter;
            }
        }

        private struct TagSchema
        {
            public StyleBinding binding;
            public ModifierHtmlSchema schema;
        }

        private static Dictionary<string, List<TagSchema>> BuildHtmlElementToTagMap(ClipboardPasteContext context)
        {
            var map = new Dictionary<string, List<TagSchema>>(StringComparer.OrdinalIgnoreCase);
            var schemas = context?.StyleSchemas;
            if (schemas == null) return map;
            for (int i = 0; i < schemas.Count; i++)
            {
                var (binding, schema) = schemas[i];
                if (binding.rule == null) continue;
                var html = schema.Html;
                if (html == null) continue;

                for (int j = 0; j < html.RecognizedElements.Count; j++)
                {
                    var element = html.RecognizedElements[j];
                    if (string.IsNullOrEmpty(element)) continue;
                    if (schema.MatchesSourceTagName
                        && !element.EqualsIgnoreCase(binding.sourceToken)) continue;
                    AddCandidate(map, element, binding, html);
                }
            }
            return map;
        }

        private static void AddCandidate(Dictionary<string, List<TagSchema>> map, string element,
            StyleBinding binding, ModifierHtmlSchema html)
        {
            if (!map.TryGetValue(element, out var list))
            {
                list = new List<TagSchema>(2);
                map[element] = list;
            }
            for (int k = 0; k < list.Count; k++)
                if (ReferenceEquals(list[k].binding.rule, binding.rule)) return;
            list.Add(new TagSchema { binding = binding, schema = html });
        }

        /// <summary>
        /// Copy-side renderer over the shared span model. A span whose modifier has no HTML schema (or whose
        /// value / tag-name substitution cannot produce valid HTML) degrades to its inner content — never
        /// invalid markup, never a leaked raw tag.
        /// </summary>
        private sealed class HtmlRenderer : IClipboardSpanRenderer
        {
            public static readonly HtmlRenderer Instance = new();

            public void Literal(StringBuilder sb, string text, int start, int end)
            {
                for (int i = start; i < end; i++)
                {
                    var c = text[i];
                    if (c == '\n') sb.Append("<br>");
                    else AppendEscapedHtmlChar(sb, c);
                }
            }

            public void Object(StringBuilder sb, in ClipboardSpan span)
            {
                if (!TryResolveTags(in span, out var open, out var close)) return;
                sb.Append(open).Append(close);
            }

            public void Styled(StringBuilder sb, in ClipboardSpan span, string inner)
            {
                if (!TryResolveTags(in span, out var open, out var close))
                {
                    sb.Append(inner);
                    return;
                }
                sb.Append(open).Append(inner).Append(close);
            }

            private static bool TryResolveTags(in ClipboardSpan span, out string open, out string close)
            {
                open = null;
                close = null;
                var schema = ClipboardModifierBindMap.GetSchema(span.Modifier);
                var html = schema?.Html;
                if (html == null) return false;

                var sourceTagName = span.SourceToken;
                if (schema.MatchesSourceTagName && !RecognizesElement(html, sourceTagName)) return false;

                open = html.OpenTagTemplate;
                close = html.CloseTag;
                if (html.HasValuePlaceholder)
                {
                    var value = html.ToCss != null ? html.ToCss(span.Parameter) : span.Parameter;
                    if (string.IsNullOrEmpty(value)) return false;
                    open = open.Replace("{value}", EscapeHtmlAttribute(value));
                }
                if (!string.IsNullOrEmpty(sourceTagName))
                {
                    open = open.Replace("{tagName}", sourceTagName);
                    close = close.Replace("{tagName}", sourceTagName);
                }
                return true;
            }

            private static bool RecognizesElement(ModifierHtmlSchema html, string element)
            {
                if (string.IsNullOrEmpty(element)) return false;
                for (var i = 0; i < html.RecognizedElements.Count; i++)
                    if (element.EqualsIgnoreCase(html.RecognizedElements[i])) return true;
                return false;
            }
        }

        private struct OpenElement
        {
            public string name;
            public List<ResolvedStyle> matches;
            public int contentStart;
            public bool block;
            public bool pre;
        }

        /// <summary>
        /// Single forward pass with an explicit open-element stack. A mapped open records
        /// where its inner content begins in <paramref name="sb"/>; its close (or the
        /// end-of-input unwind, which keeps unclosed mapped tags styling their tail) records
        /// the resolved style over that visible range. A close
        /// tag with no matching open is ignored; a close deeper in the stack implicitly
        /// closes everything above it (fragment-parser recovery). Opens past
        /// <see cref="ClipboardBudget.MaxDepth"/> only bump a dropped-depth counter their
        /// own close tags consume, so they can never mis-pop a pushed ancestor. A
        /// <see langword="null"/> <paramref name="map"/> is the degrade path: no element
        /// ever matches, plain text with block newlines comes out. HTML whitespace runs
        /// collapse to one space, dropped at output start and after a block boundary,
        /// preserved literally inside <c>&lt;pre&gt;</c>.
        /// </summary>
        private static void ParseHtml(string html, Dictionary<string, List<TagSchema>> map,
            StringBuilder sb, List<ClipboardSpan> spans)
        {
            int end = html.Length;
            var stack = new List<OpenElement>(8);
            int droppedDepth = 0;
            int preDepth = 0;
            bool pendingSpace = false;
            int i = 0;
            while (i < end)
            {
                if (sb.Length >= ClipboardBudget.MaxOutputChars) break;
                char c = html[i];
                if (c == '<')
                {
                    if (i + 3 < end && html[i + 1] == '!' && html[i + 2] == '-' && html[i + 3] == '-')
                    {
                        int ce = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                        if (ce < 0) break;
                        i = ce + 3;
                        continue;
                    }
                    if (i + 1 < end && html[i + 1] == '!')
                    {
                        int gt = html.IndexOf('>', i + 2);
                        if (gt < 0) break;
                        i = gt + 1;
                        continue;
                    }
                    if (TryMatchHtmlElement(html, i, end, out var elementName, out var attributesRaw, out var elementEnd, out var selfClosing, out var isClose))
                    {
                        if (isClose)
                        {
                            if (droppedDepth > 0)
                            {
                                droppedDepth--;
                                i = elementEnd;
                                continue;
                            }
                            int open = FindOpen(stack, elementName);
                            if (open >= 0)
                                while (stack.Count > open)
                                    PopElement(stack, sb, spans, ref pendingSpace, ref preDepth);
                            i = elementEnd;
                            continue;
                        }

                        if (IsRawTextElement(elementName))
                        {
                            i = SkipRawText(html, elementEnd, end, elementName);
                            continue;
                        }

                        if (selfClosing || IsVoidHtmlElement(elementName))
                        {
                            if (elementName.EqualsIgnoreCase("br"))
                            {
                                pendingSpace = false;
                                sb.Append('\n');
                            }
                            i = elementEnd;
                            continue;
                        }

                        List<ResolvedStyle> matches = null;
                        if (map != null && map.TryGetValue(elementName, out var candidates))
                        {
                            var resolved = new List<ResolvedStyle>(2);
                            if (ResolveMatches(candidates, elementName, attributesRaw, resolved) > 0)
                                matches = resolved;
                        }

                        bool block = IsBlockHtmlElement(elementName);
                        if (block)
                        {
                            pendingSpace = false;
                            EnsureNewline(sb);
                        }
                        else
                        {
                            FlushPendingSpace(sb, ref pendingSpace);
                        }
                        if (stack.Count < ClipboardBudget.MaxDepth && droppedDepth == 0)
                        {
                            var pre = elementName.EqualsIgnoreCase("pre");
                            stack.Add(new OpenElement
                            {
                                name = elementName,
                                matches = matches,
                                contentStart = sb.Length,
                                block = block,
                                pre = pre,
                            });
                            if (pre) preDepth++;
                        }
                        else
                        {
                            droppedDepth++;
                        }
                        i = elementEnd;
                        continue;
                    }
                    FlushPendingSpace(sb, ref pendingSpace);
                    sb.Append('<');
                    i++;
                    continue;
                }
                if (c == '&')
                {
                    int semi = i + 1;
                    while (semi < end && semi - i < 12 && html[semi] != ';') semi++;
                    if (semi < end && html[semi] == ';')
                    {
                        var decoded = WebUtility.HtmlDecode(html.Substring(i, semi - i + 1));
                        FlushPendingSpace(sb, ref pendingSpace);
                        var room = ClipboardBudget.MaxOutputChars - sb.Length;
                        var length = Utf16.SafePrefixLength(decoded.AsSpan(), room);
                        if (length > 0) sb.Append(decoded, 0, length);
                        i = semi + 1;
                        continue;
                    }
                    FlushPendingSpace(sb, ref pendingSpace);
                    sb.Append('&');
                    i++;
                    continue;
                }
                if (Ascii.IsWhitespace(c) && preDepth == 0)
                {
                    pendingSpace = true;
                    i++;
                    continue;
                }
                FlushPendingSpace(sb, ref pendingSpace);
                var scalarSize = Utf16.SizeAt(html.AsSpan(0, end), i);
                if (sb.Length + scalarSize > ClipboardBudget.MaxOutputChars) break;
                sb.Append(html, i, scalarSize);
                i += scalarSize;
            }
            while (stack.Count > 0) PopElement(stack, sb, spans, ref pendingSpace, ref preDepth);
        }

        private static void FlushPendingSpace(StringBuilder sb, ref bool pendingSpace)
        {
            if (!pendingSpace) return;
            pendingSpace = false;
            if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append(' ');
        }

        private static int FindOpen(List<OpenElement> stack, string name)
        {
            for (int s = stack.Count - 1; s >= 0; s--)
                if (stack[s].name.EqualsIgnoreCase(name)) return s;
            return -1;
        }

        private static void PopElement(List<OpenElement> stack, StringBuilder sb,
            List<ClipboardSpan> spans, ref bool pendingSpace, ref int preDepth)
        {
            var entry = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            if (entry.pre) preDepth--;
            if (entry.block) pendingSpace = false;
            if (entry.matches != null && sb.Length >= entry.contentStart)
            {
                var length = sb.Length - entry.contentStart;
                if (length > 0)
                {
                    for (var i = 0; i < entry.matches.Count; i++)
                    {
                        var style = entry.matches[i];
                        spans.Add(new ClipboardSpan(entry.contentStart, length, style.modifier,
                            style.rule, style.parameter, sourceToken: style.sourceToken));
                    }
                }
            }
            if (entry.block) EnsureNewline(sb);
        }

        private static void ClipSpans(List<ClipboardSpan> spans, int textLength)
        {
            for (var i = spans.Count - 1; i >= 0; i--)
            {
                var span = spans[i];
                if (span.Offset >= textLength)
                {
                    spans.RemoveAt(i);
                    continue;
                }
                if (span.Offset + span.Length > textLength)
                    spans[i] = new ClipboardSpan(span.Offset, textLength - span.Offset,
                        span.Modifier, span.Rule, span.Parameter, span.IsAtomic, span.SourceToken);
            }
        }

        /// <summary>
        /// Skips a raw-text element's content to its matching close tag (HTML gives these
        /// elements no nesting, so the first close wins). Word / Outlook clipboard HTML
        /// carries <c>&lt;style&gt;</c> blocks whose CSS must never paste as visible text.
        /// </summary>
        private static int SkipRawText(string html, int from, int end, string elementName)
        {
            int i = from;
            while (i < end)
            {
                int lt = html.IndexOf('<', i);
                if (lt < 0 || lt + 1 >= end) return end;
                if (html[lt + 1] == '/'
                    && string.Compare(html, lt + 2, elementName, 0, elementName.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    int gt = html.IndexOf('>', lt + 2);
                    return gt < 0 ? end : gt + 1;
                }
                i = lt + 1;
            }
            return end;
        }

        private static bool IsRawTextElement(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "style":
                case "script":
                case "title":
                case "head":
                case "xml":
                case "noscript":
                case "template":
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryMatchHtmlElement(string html, int from, int end, out string elementName, out string attributesRaw, out int elementEnd, out bool selfClosing, out bool isClose)
        {
            elementName = null;
            attributesRaw = string.Empty;
            elementEnd = from;
            selfClosing = false;
            isClose = false;

            if (from + 2 >= end || html[from] != '<') return false;
            int i = from + 1;
            if (i < end && html[i] == '/') { isClose = true; i++; }

            int nameStart = i;
            while (i < end)
            {
                char c = html[i];
                if (c == '>' || c == '/' || Ascii.IsWhitespace(c)) break;
                if (!IsHtmlNameChar(c)) return false;
                i++;
            }
            if (i == nameStart) return false;
            elementName = html.Substring(nameStart, i - nameStart);
            int attrStart = i;

            while (i < end && html[i] != '>')
            {
                if (html[i] == '"' || html[i] == '\'')
                {
                    char quote = html[i++];
                    while (i < end && html[i] != quote) i++;
                    if (i < end) i++;
                }
                else i++;
            }
            if (i >= end || html[i] != '>') return false;

            int gt = i;
            int attrEnd = gt;
            int probe = gt - 1;
            while (probe > nameStart && Ascii.IsWhitespace(html[probe])) probe--;
            if (probe > nameStart && html[probe] == '/') { selfClosing = true; attrEnd = probe; }

            attributesRaw = attrEnd > attrStart ? html.Substring(attrStart, attrEnd - attrStart) : string.Empty;
            elementEnd = gt + 1;
            return true;
        }

        private static bool IsHtmlNameChar(char c) => SourceMarkup.IsTagNameChar(c);

        private static bool IsVoidHtmlElement(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            switch (name.ToLowerInvariant())
            {
                case "area":
                case "base":
                case "br":
                case "col":
                case "embed":
                case "hr":
                case "img":
                case "input":
                case "link":
                case "meta":
                case "param":
                case "source":
                case "track":
                case "wbr":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsBlockHtmlElement(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            switch (name.ToLowerInvariant())
            {
                case "p":
                case "div":
                case "li":
                case "tr":
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                case "blockquote":
                case "pre":
                    return true;
                default:
                    return false;
            }
        }

        private static void EnsureNewline(StringBuilder sb)
        {
            if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
        }

        private static void TrimTrailingNewlines(StringBuilder sb)
        {
            int len = sb.Length;
            while (len > 0 && (sb[len - 1] == '\n' || sb[len - 1] == '\r')) len--;
            sb.Length = len;
        }

        private static void AppendEscapedHtmlChar(StringBuilder sb, char c)
        {
            switch (c)
            {
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '&': sb.Append("&amp;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(c); break;
            }
        }

        private static string EscapeHtmlAttribute(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            for (int k = 0; k < value.Length; k++)
            {
                char c = value[k];
                if (c == '&' || c == '"' || c == '<' || c == '>' || c == '\'')
                    return EscapeHtmlAttributeSlow(value);
            }
            return value;
        }

        private static string EscapeHtmlAttributeSlow(string value)
        {
            var sb = new StringBuilder(value.Length + 8);
            for (int k = 0; k < value.Length; k++)
                AppendEscapedHtmlChar(sb, value[k]);
            return sb.ToString();
        }
    }
}
