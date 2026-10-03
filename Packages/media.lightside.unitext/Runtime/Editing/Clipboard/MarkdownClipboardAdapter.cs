using System;
using System.Collections.Generic;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// Markdown ↔ attributed UniText content via modifier-declared markers. Priority 40 — beats plain
    /// text, loses to HTML and UniText source. Covers inline emphases — bold <c>**...**</c>,
    /// italic <c>*...*</c>, strikethrough <c>~~...~~</c>, plus any paste-side alias markers a
    /// schema declares (<c>_</c> / <c>__</c>) — and inline links
    /// <c>[text](url)</c> (the <see cref="LinkModifier"/> parameter is the URL; angle-bracket
    /// URLs <c>(&lt;url&gt;)</c> are read, titles are dropped). Inline code, headings, lists,
    /// and blockquotes stay deferred — they need a backing code / block modifier the engine
    /// does not yet model.
    /// </summary>
    [HideFromTypeSelector]
    public sealed class MarkdownClipboardAdapter : IClipboardAdapter
    {
        /// <summary>Shared stateless instance. Map building reads the editor passed in via context.</summary>
        public static readonly MarkdownClipboardAdapter Instance = new();

        private MarkdownClipboardAdapter() { }

        public ClipboardFormat Format => ClipboardFormat.Markdown;
        public int Priority => 40;

        public string SerializeCopy(ClipboardCopyContext context)
        {
            if (string.IsNullOrEmpty(context?.VisibleText)) return null;

            var text = context.VisibleText;
            var sb = new StringBuilder(text.Length + 16);
            SourceMarkup.RenderSpans(sb, text, context.Spans.Span, MarkdownRenderer.Instance);
            return sb.ToString();
        }

        public ClipboardPasteContent DeserializePaste(string payload, ClipboardPasteContext context)
        {
            if (string.IsNullOrEmpty(payload)) return null;

            if (payload.Length > ClipboardBudget.MaxInputChars)
            {
                var length = Utf16.SafePrefixLength(payload.AsSpan(), ClipboardBudget.MaxOutputChars);
                return new ClipboardPasteContent(payload.Substring(0, length));
            }

            var sb = new StringBuilder(Math.Min(payload.Length, ClipboardBudget.MaxOutputChars));
            var spans = new List<ClipboardSpan>();
            var markerMap = BuildMarkerMap(context, out var link, out var maxMarkerLength);
            ParseRange(payload, 0, payload.Length, markerMap, link, maxMarkerLength, sb, spans, 0);
            return sb.Length == 0 ? null : new ClipboardPasteContent(sb.ToString(), spans.ToArray());
        }

        private struct MarkerEntry
        {
            public StyleBinding binding;
            public ModifierMarkdownSchema schema;
        }

        private static Dictionary<string, MarkerEntry> BuildMarkerMap(ClipboardPasteContext context,
            out MarkerEntry link, out int maxMarkerLength)
        {
            var map = new Dictionary<string, MarkerEntry>(StringComparer.Ordinal);
            link = default;
            maxMarkerLength = 0;
            var schemas = context?.StyleSchemas;
            if (schemas == null) return map;
            for (int i = 0; i < schemas.Count; i++)
            {
                var (binding, schema) = schemas[i];
                if (binding.rule == null) continue;
                var markdown = schema.Markdown;
                if (markdown == null) continue;
                if (markdown.Kind == MarkdownSyntaxKind.Link)
                {
                    if (link.binding.rule == null)
                        link = new MarkerEntry { binding = binding, schema = markdown };
                    continue;
                }
                var entry = new MarkerEntry { binding = binding, schema = markdown };
                AddMarker(map, markdown.OpenMarker, entry, ref maxMarkerLength);
                for (int a = 0; a < markdown.AliasMarkers.Count; a++)
                    AddMarker(map, markdown.AliasMarkers[a], entry, ref maxMarkerLength);
            }
            return map;
        }

        private static void AddMarker(Dictionary<string, MarkerEntry> map, string marker, in MarkerEntry entry, ref int maxMarkerLength)
        {
            if (string.IsNullOrEmpty(marker) || map.ContainsKey(marker)) return;
            map[marker] = entry;
            if (marker.Length > maxMarkerLength) maxMarkerLength = marker.Length;
        }

        /// <summary>
        /// Copy-side renderer over the shared span model. Spans without a Markdown schema degrade
        /// to their inner content; inline objects have no Markdown form and drop.
        /// </summary>
        private sealed class MarkdownRenderer : IClipboardSpanRenderer
        {
            public static readonly MarkdownRenderer Instance = new();

            public void Literal(StringBuilder sb, string text, int start, int end)
            {
                for (int i = start; i < end; i++)
                    AppendEscapedMarkdownChar(sb, text[i]);
            }

            public void Object(StringBuilder sb, in ClipboardSpan span) { }

            public void Styled(StringBuilder sb, in ClipboardSpan span, string inner)
            {
                var markdown = ClipboardModifierBindMap.GetSchema(span.Modifier)?.Markdown;
                if (markdown == null)
                {
                    sb.Append(inner);
                    return;
                }
                if (markdown.Kind == MarkdownSyntaxKind.Link)
                {
                    sb.Append('[').Append(inner).Append(']').Append('(');
                    AppendMarkdownUrl(sb, span.Parameter);
                    sb.Append(')');
                    return;
                }
                sb.Append(markdown.OpenMarker).Append(inner).Append(markdown.CloseMarker);
            }
        }

        /// <summary>
        /// Per-parse-level scan memo keeping the paste parser linear on hostile input (paste payloads are
        /// attacker-controlled): a forward scan that reached the level's end without finding a closer records
        /// the position it started from — every later scan of the same kind starting at or past it fails
        /// immediately instead of re-walking to the end. Valid because all closer criteria are positional
        /// (and a scan start can never fall inside a <c>\x</c> escape pair: the char before a scan start is
        /// always a delimiter or <c>(</c>, never <c>\</c>). Allocated only when a full scan first fails, so
        /// ordinary payloads pay nothing.
        /// </summary>
        private sealed class ScanMemo
        {
            public int urlPlainFailFrom = int.MaxValue;
            public int urlAngleFailFrom = int.MaxValue;
            public int parenFailFrom = int.MaxValue;
            private int[] emphasisFailFrom;
            private int maxTrial;

            public int GetEmphasisFail(char delim, int trial)
            {
                if (emphasisFailFrom == null) return int.MaxValue;
                var idx = EmphasisIndex(delim, trial);
                return idx < 0 ? int.MaxValue : emphasisFailFrom[idx];
            }

            public void SetEmphasisFail(char delim, int trial, int maxMarkerLength, int from)
            {
                if (emphasisFailFrom == null)
                {
                    maxTrial = maxMarkerLength;
                    emphasisFailFrom = new int[4 * maxMarkerLength];
                    for (int i = 0; i < emphasisFailFrom.Length; i++) emphasisFailFrom[i] = int.MaxValue;
                }
                var idx = EmphasisIndex(delim, trial);
                if (idx >= 0 && from < emphasisFailFrom[idx]) emphasisFailFrom[idx] = from;
            }

            private int EmphasisIndex(char delim, int trial)
            {
                if (trial < 1 || trial > maxTrial) return -1;
                var d = DelimIndex(delim);
                return d < 0 ? -1 : d * maxTrial + trial - 1;
            }

            private static int DelimIndex(char delim) => delim switch
            {
                '*' => 0,
                '_' => 1,
                '~' => 2,
                '`' => 3,
                _ => -1,
            };
        }

        private static void ParseRange(string md, int start, int end, Dictionary<string, MarkerEntry> markers,
            MarkerEntry link, int maxMarkerLength, StringBuilder sb, List<ClipboardSpan> spans, int depth)
        {
            ScanMemo memo = null;
            int i = start;
            while (i < end)
            {
                if (sb.Length >= ClipboardBudget.MaxOutputChars) return;
                char c = md[i];
                if (c == '\\' && i + 1 < end)
                {
                    char next = md[i + 1];
                    if (IsAsciiPunctuation(next))
                    {
                        sb.Append(next);
                        i += 2;
                        continue;
                    }
                    sb.Append(c);
                    i++;
                    continue;
                }
                if (depth < ClipboardBudget.MaxDepth)
                {
                    if (c == '[' && link.binding.rule != null
                        && TryConsumeLink(md, i, end, ref memo, out var textStart, out var textEnd, out var url, out var afterLink))
                    {
                        var spanStart = sb.Length;
                        ParseRange(md, textStart, textEnd, markers, link, maxMarkerLength,
                            sb, spans, depth + 1);
                        AddSpan(spans, spanStart, sb.Length, in link,
                            SourceMarkup.SlotParameter(link.binding.childIndex,
                                SourceMarkup.SanitizeParameter(url)));
                        i = afterLink;
                        continue;
                    }
                    if (IsMarkdownDelimiter(c)
                        && TryConsumeEmphasis(md, i, end, markers, maxMarkerLength, ref memo,
                            out var outer, out var innerMarker, out var hasInner, out var innerStart, out var innerEnd, out var afterClose))
                    {
                        var spanStart = sb.Length;
                        ParseRange(md, innerStart, innerEnd, markers, link, maxMarkerLength,
                            sb, spans, depth + 1);
                        if (hasInner) AddSpan(spans, spanStart, sb.Length, in innerMarker, null);
                        AddSpan(spans, spanStart, sb.Length, in outer, null);
                        i = afterClose;
                        continue;
                    }
                }
                var scalarSize = Utf16.SizeAt(md.AsSpan(0, end), i);
                if (sb.Length + scalarSize > ClipboardBudget.MaxOutputChars) return;
                sb.Append(md, i, scalarSize);
                i += scalarSize;
            }
        }

        private static void AddSpan(List<ClipboardSpan> spans, int start, int end,
            in MarkerEntry entry, string parameter)
        {
            if (end <= start || entry.binding.appliedModifier == null || entry.binding.rule == null) return;
            spans.Add(new ClipboardSpan(start, end - start, entry.binding.appliedModifier,
                entry.binding.rule, parameter, sourceToken: entry.binding.sourceToken));
        }

        /// <summary>
        /// Parses a CommonMark inline link <c>[text](url)</c> at <paramref name="from"/> (which
        /// must be <c>[</c>). Accepts an angle-bracket URL <c>(&lt;url&gt;)</c> and drops an
        /// optional title. Fails (caller keeps <c>[</c> literal) on a nested <c>[</c>, a missing
        /// <c>](</c>, an empty URL, or a URL carrying <c>&lt;</c>/<c>&gt;</c> (which cannot live
        /// in a <c>&lt;link=…&gt;</c> source tag). <paramref name="textStart"/>..<paramref name="textEnd"/>
        /// is the link-text slice; <paramref name="afterLink"/> is just past the closing <c>)</c>.
        /// </summary>
        private static bool TryConsumeLink(string md, int from, int end, ref ScanMemo memo,
            out int textStart, out int textEnd, out string url, out int afterLink)
        {
            textStart = from + 1;
            textEnd = -1;
            url = null;
            afterLink = -1;

            int i = textStart;
            while (i < end && md[i] != ']')
            {
                if (md[i] == '\\' && i + 1 < end) { i += 2; continue; }
                if (md[i] == '[') return false;
                i++;
            }
            if (i >= end) return false;
            textEnd = i;

            int j = i + 1;
            if (j >= end || md[j] != '(') return false;
            j++;

            bool angle = j < end && md[j] == '<';
            if (angle) j++;

            if (memo != null && j >= (angle ? memo.urlAngleFailFrom : memo.urlPlainFailFrom)) return false;

            int urlStart = j;
            var urlSb = new StringBuilder();
            while (j < end)
            {
                char ch = md[j];
                if (ch == '\\' && j + 1 < end) { urlSb.Append(md[j + 1]); j += 2; continue; }
                if (angle ? ch == '>' : ch == ')' || Ascii.IsWhitespace(ch)) break;
                urlSb.Append(ch);
                j++;
            }
            if (j >= end)
            {
                memo ??= new ScanMemo();
                if (angle) { if (urlStart < memo.urlAngleFailFrom) memo.urlAngleFailFrom = urlStart; }
                else if (urlStart < memo.urlPlainFailFrom) memo.urlPlainFailFrom = urlStart;
                return false;
            }

            if (angle)
            {
                j++;
                while (j < end && (md[j] == ' ' || md[j] == '\t')) j++;
                if (j >= end || md[j] != ')') return false;
            }
            else
            {
                if (memo != null && j >= memo.parenFailFrom) return false;
                int parenScanStart = j;
                while (j < end && md[j] != ')') j++;
                if (j >= end)
                {
                    memo ??= new ScanMemo();
                    if (parenScanStart < memo.parenFailFrom) memo.parenFailFrom = parenScanStart;
                    return false;
                }
            }

            url = urlSb.ToString();
            if (url.Length == 0 || url.IndexOf('<') >= 0 || url.IndexOf('>') >= 0) return false;
            afterLink = j + 1;
            return true;
        }

        /// <summary>
        /// Emits a link URL into <c>(...)</c>. Wraps in angle brackets when the URL carries a
        /// space or parenthesis (CommonMark requires it); otherwise emits it plain, backslash-
        /// escaping a literal <c>)</c> or <c>\</c>.
        /// </summary>
        private static void AppendMarkdownUrl(StringBuilder sb, string url)
        {
            url ??= string.Empty;
            bool angle = false;
            for (int k = 0; k < url.Length; k++)
            {
                char c = url[k];
                if (c == ' ' || c == '\t' || c == '(' || c == ')') { angle = true; break; }
            }
            if (angle && url.IndexOf('>') < 0)
            {
                sb.Append('<').Append(url).Append('>');
                return;
            }
            for (int k = 0; k < url.Length; k++)
            {
                char c = url[k];
                if (c == ')' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
        }

        /// <summary>
        /// Matches a delimiter run at <paramref name="from"/> against the registered markers,
        /// longest first, capped at <paramref name="maxMarkerLength"/> (so a pasted line of
        /// 100k asterisks probes two dictionary keys, not 100k allocated strings). When the
        /// run splits into two registered markers with a symmetric closer — the CommonMark
        /// <c>***bold italic***</c> case — both are reported (<paramref name="hasInner"/>)
        /// and the full runs are consumed, instead of leaving stray delimiter chars. A closer
        /// scan that reaches <paramref name="end"/> records itself in <paramref name="memo"/>
        /// so no later opener of the same shape re-walks the tail.
        /// </summary>
        private static bool TryConsumeEmphasis(string md, int from, int end, Dictionary<string, MarkerEntry> markers,
            int maxMarkerLength, ref ScanMemo memo, out MarkerEntry outer, out MarkerEntry innerMarker, out bool hasInner,
            out int innerStart, out int innerEnd, out int afterClose)
        {
            outer = default;
            innerMarker = default;
            hasInner = false;
            innerStart = -1;
            innerEnd = -1;
            afterClose = -1;
            if (maxMarkerLength <= 0) return false;

            int runLen = 1;
            char delim = md[from];
            int runCap = maxMarkerLength * 2 + 1;
            while (runLen < runCap && from + runLen < end && md[from + runLen] == delim) runLen++;

            for (int trial = Math.Min(runLen, maxMarkerLength); trial >= 1; trial--)
            {
                var key = new string(delim, trial);
                if (!markers.TryGetValue(key, out var value)) continue;

                int leftover = runLen - trial;
                MarkerEntry leftoverEntry = default;
                bool nested = leftover > 0 && leftover <= maxMarkerLength
                    && markers.TryGetValue(new string(delim, leftover), out leftoverEntry);

                int opened = nested ? runLen : trial;
                if (from + opened >= end) continue;

                char afterOpen = md[from + opened];
                if (Ascii.IsWhitespace(afterOpen)) continue;

                int scanStart = from + opened;
                if (memo != null && scanStart >= memo.GetEmphasisFail(delim, trial)) continue;

                int scan = scanStart;
                while (scan < end)
                {
                    if (md[scan] == '\\' && scan + 1 < end) { scan += 2; continue; }
                    if (md[scan] != delim) { scan++; continue; }
                    int closeRun = 1;
                    while (scan + closeRun < end && md[scan + closeRun] == delim) closeRun++;
                    if (closeRun < trial) { scan += closeRun; continue; }
                    int beforeClose = scan - 1;
                    if (beforeClose < from + opened) { scan += closeRun; continue; }
                    char prev = md[beforeClose];
                    if (Ascii.IsWhitespace(prev)) { scan += closeRun; continue; }

                    outer = value;
                    innerStart = from + opened;
                    innerEnd = scan;
                    if (nested && closeRun >= runLen)
                    {
                        innerMarker = leftoverEntry;
                        hasInner = true;
                        afterClose = scan + runLen;
                    }
                    else
                    {
                        if (nested)
                        {
                            innerStart = from + trial;
                        }
                        afterClose = scan + trial;
                    }
                    return true;
                }

                memo ??= new ScanMemo();
                memo.SetEmphasisFail(delim, trial, maxMarkerLength, scanStart);
            }
            return false;
        }

        internal static bool IsMarkdownDelimiter(char c) => c == '*' || c == '_' || c == '~' || c == '`';

        private static bool IsAsciiPunctuation(char c)
            => c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';

        /// <summary>
        /// CommonMark-minimal escaping: delimiter and escape chars always, <c>[</c> always (no
        /// link can start without it, which keeps <c>]</c> / parens literal), <c>#</c> only at
        /// line start. Prose punctuation stays readable when the payload is consumed as
        /// markdown source (chat composers, plain editors).
        /// </summary>
        private static void AppendEscapedMarkdownChar(StringBuilder sb, char c)
        {
            switch (c)
            {
                case '\\':
                case '*':
                case '_':
                case '~':
                case '`':
                case '[':
                    sb.Append('\\').Append(c);
                    break;
                case '#':
                    if (sb.Length == 0 || sb[sb.Length - 1] == '\n') sb.Append('\\');
                    sb.Append(c);
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
    }
}
