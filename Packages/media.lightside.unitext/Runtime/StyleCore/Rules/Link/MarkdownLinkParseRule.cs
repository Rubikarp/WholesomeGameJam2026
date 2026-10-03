using System;

namespace LightSide
{
    /// <summary>Parses Markdown-style links: [link text](https://example.com).</summary>
    /// <seealso cref="LinkModifier"/>
    /// <seealso cref="ParseRule"/>
    [Serializable]
    [TypeGroup("Markdown", 1)]
    [TypeDescription("Parses Markdown-style links: [text](url).")]
    public sealed partial class MarkdownLinkParseRule : ParseRule
    {
        public override bool CanWrap => true;
        /// <summary>Merged behind the parsed URL (the URL stays the opaque first value).</summary>
        [StateProperty(nameof(NotifyChanged))]
        [UnityEngine.SerializeField, DefaultParameter] private string defaultParameter;

        public override string MarkupTriggers => "[";
        public override string TypingTriggers => "[]()";

        public override string Apply(ReadOnlySpan<char> content, string parameter)
            => string.IsNullOrEmpty(parameter) ? null : "[" + content.ToString() + "](" + parameter + ")";

        public override int TryMatch(ReadOnlySpan<char> text,int index, PooledList<ParsedRange> results)
        {
            if (text[index] != '[') return index;

            var textStart = index + 1;
            var closeBracket = FindCloseBracket(text, textStart);
            if (closeBracket < 0) return index;

            if (closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(')
                return index;

            var urlStart = closeBracket + 2;
            var closeParen = FindCloseParen(text, urlStart);
            if (closeParen < 0) return index;

            var urlSpan = text.Slice(urlStart, closeParen - urlStart).Trim();
            if (urlSpan.IsEmpty) return index;
            var url = SpanIntern.Get(urlSpan);

            var fullEnd = closeParen + 1;

            var range = new ParsedRange(
                index,
                textStart,
                closeBracket,
                fullEnd,
                url);
            range.SetOpaquePrimary(DefaultParameter);
            results.Add(range);

            return fullEnd;
        }

        private static int FindCloseBracket(ReadOnlySpan<char> text,int start)
        {
            var depth = 1;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0) return i;
                }
                else if (c == '\n' || c == '\r') return -1;
            }
            return -1;
        }

        private static int FindCloseParen(ReadOnlySpan<char> text,int start)
        {
            var depth = 1;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
                else if (c == '\n' || c == '\r') return -1;
            }
            return -1;
        }
    }

}
