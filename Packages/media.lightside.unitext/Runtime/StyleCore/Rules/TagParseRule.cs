using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Base class for parsing XML-style markup tags (e.g., &lt;b&gt;, &lt;color=#FF0000&gt;).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived classes override <see cref="TagName"/> to specify the tag name to match.
    /// Parameters are always optional: &lt;tag&gt;, &lt;tag=value&gt;, &lt;tag/&gt;, &lt;tag=value/&gt;.
    /// Self-closing is purely syntax-driven via /&gt;.
    /// </para>
    /// </remarks>
    /// <seealso cref="ParseRule"/>
    [Serializable]
    public abstract class TagParseRule : ParseRule
    {
        private readonly Stack<OpenTag> openTags = new(8);

        private string identityKey;
        private string identityValue;

        private struct OpenTag
        {
            public int openStart;
            public int openEnd;
            public string parameter;
            public string label;
        }

        public abstract string TagName { get; }

        public sealed override string SourceToken => TagName;

        /// <summary>The self-closing occurrence of this tag (object inserts): <c>&lt;tag/&gt;</c> or <c>&lt;tag=param/&gt;</c>; null when the rule has no tag name.</summary>
        public string SelfClosing(string parameter) => string.IsNullOrEmpty(TagName)
            ? null
            : string.IsNullOrEmpty(parameter) ? $"<{TagName}/>" : $"<{TagName}={parameter}/>";

        public override string Identity => CachedIdentity("tag:", TagName, ref identityKey, ref identityValue);

        public override string MarkupTriggers => "<";
        public override string TypingTriggers => "<>";

        /// <summary>
        /// When <see langword="true"/>, the tag has HTML void-element semantics: every
        /// <c>&lt;tag&gt;</c> or <c>&lt;tag=value&gt;</c> form is treated as self-closing
        /// (produces a single <c>￼</c> placeholder), and a stray <c>&lt;/tag&gt;</c>
        /// is silently consumed. Use for inline-media tags (<c>obj</c>, <c>sprite</c>)
        /// where range semantics don't apply.
        /// </summary>
        protected virtual bool IsVoid => false;

        public override void Reset()
        {
            openTags.Clear();
        }

        /// <inheritdoc/>
        public override bool CanWrap => !IsVoid && !string.IsNullOrEmpty(TagName);

        /// <summary>Void tags have no wrapping form (return null); otherwise wraps content in the open/close tag pair.</summary>
        public override string Apply(ReadOnlySpan<char> content, string parameter)
        {
            if (IsVoid || string.IsNullOrEmpty(TagName)) return null;
            return string.IsNullOrEmpty(parameter)
                ? $"<{TagName}>{content.ToString()}</{TagName}>"
                : $"<{TagName}={parameter}>{content.ToString()}</{TagName}>";
        }

        public override void Finalize(ReadOnlySpan<char> text,PooledList<ParsedRange> results)
        {
            while (openTags.Count > 0)
            {
                var open = openTags.Pop();
                var range = new ParsedRange(
                    open.openStart,
                    open.openEnd,
                    text.Length, text.Length, open.parameter
                );
                range.label = open.label;
                results.Add(range);
            }
        }

        public override int TryMatch(ReadOnlySpan<char> text,int index, PooledList<ParsedRange> results)
        {
            if (text[index] != '<')
                return index;

            var openResult = TryMatchOpenTag(text, index, results);
            if (openResult > index)
                return openResult;

            var closeResult = TryMatchCloseTag(text, index, results);
            if (closeResult > index)
                return closeResult;

            return index;
        }

        private int TryMatchOpenTag(ReadOnlySpan<char> text,int index, PooledList<ParsedRange> results)
        {
            var tagNameLen = TagName.Length;
            if (index + tagNameLen + 2 > text.Length)
                return index;

            if (!AsciiCaseInsensitive.StartsWith(text, index + 1, TagName))
                return index;

            var afterName = index + 1 + tagNameLen;
            if (afterName >= text.Length)
                return index;

            string parameter = null;
            int openEnd;

            string label = null;
            var c = text[afterName];
            if (c == ' ' && afterName + 2 < text.Length && text[afterName + 1] == '#')
            {
                var labelStart = afterName + 2;
                var labelEnd = labelStart;
                while (labelEnd < text.Length && IsLabelChar(text[labelEnd])) labelEnd++;
                if (labelEnd == labelStart) return index;
                label = SpanIntern.Get(text.Slice(labelStart, labelEnd - labelStart));
                afterName = labelEnd;
                if (afterName >= text.Length) return index;
                c = text[afterName];
            }
            if (c == '=')
            {
                var paramStart = afterName + 1;
                var closePos = FindTagClose(text, paramStart);
                if (closePos < 0)
                    return index;

                var selfClose = closePos > paramStart && text[closePos - 1] == '/';
                var paramEnd = selfClose ? closePos - 1 : closePos;
                parameter = ExtractParameter(text, paramStart, paramEnd);
                openEnd = closePos + 1;

                if (selfClose || IsVoid)
                {
                    results.Add(ParsedRange.SelfClosing(index, openEnd,
                        UnicodeData.ObjectReplacementCharacterString, parameter, label));
                    return openEnd;
                }
            }
            else if (c == '/')
            {
                if (afterName + 1 >= text.Length || text[afterName + 1] != '>')
                    return index;
                openEnd = afterName + 2;
                results.Add(ParsedRange.SelfClosing(index, openEnd,
                    UnicodeData.ObjectReplacementCharacterString, null, label));
                return openEnd;
            }
            else if (c == '>')
            {
                openEnd = afterName + 1;
                if (IsVoid)
                {
                    results.Add(ParsedRange.SelfClosing(index, openEnd,
                        UnicodeData.ObjectReplacementCharacterString, null, label));
                    return openEnd;
                }
            }
            else
            {
                return index;
            }

            openTags.Push(new OpenTag { openStart = index, openEnd = openEnd, parameter = parameter, label = label });
            return openEnd;
        }

        private static bool IsLabelChar(char value)
            => char.IsLetterOrDigit(value) || value == '_' || value == '-';

        /// <summary>
        /// Scans for the tag-closing <c>&gt;</c> starting at <paramref name="start"/>, treating
        /// the contents of a balanced <c>"…"</c> or <c>'…'</c> pair as opaque. This lets
        /// attribute values themselves contain <c>&lt;</c> and <c>&gt;</c> — e.g. Unity Input
        /// System binding paths like <c>&lt;sprite="&lt;Keyboard&gt;/a"&gt;</c> — without
        /// requiring HTML-style entity escaping.
        /// </summary>
        /// <remarks>
        /// Mirrors HTML5 attribute-value tokenisation: a quote character entered outside an
        /// existing quote opens a quoted region; the same character closes it. Unmatched quote
        /// pairs fall back to ordinary scanning (return -1 if no <c>&gt;</c> follows).
        /// </remarks>
        internal static int FindTagClose(ReadOnlySpan<char> text, int start)
        {
            char quote = '\0';
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    quote = c;
                    continue;
                }
                if (c == '>')
                    return i;
            }
            return -1;
        }

        private int TryMatchCloseTag(ReadOnlySpan<char> text,int index, PooledList<ParsedRange> results)
        {
            var tagNameLen = TagName.Length;

            var closeLen = 3 + tagNameLen;
            if (index + closeLen > text.Length)
                return index;

            if (text[index + 1] != '/')
                return index;

            if (!AsciiCaseInsensitive.StartsWith(text, index + 2, TagName))
                return index;

            if (text[index + 2 + tagNameLen] != '>')
                return index;

            var closeEnd = index + closeLen;

            if (IsVoid)
            {
                results.Add(ParsedRange.SelfClosing(index, closeEnd, string.Empty, null));
                return closeEnd;
            }

            if (openTags.Count == 0)
                return index;

            var open = openTags.Pop();

            var range = new ParsedRange(
                open.openStart,
                open.openEnd,
                index,
                closeEnd,
                open.parameter
            );
            range.label = open.label;
            results.Add(range);

            return closeEnd;
        }

        internal static string ExtractParameter(ReadOnlySpan<char> text,int start, int end)
        {
            var span = text.Slice(start, end - start);

            span = span.Trim();

            if (span.Length >= 2)
            {
                var first = span[0];
                var last = span[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\'')) span = span.Slice(1, span.Length - 2);
            }

            return SpanIntern.Get(span);
        }
    }
}
