using System;

namespace LightSide
{
    /// <summary>
    /// Replaces the void <c>&lt;br&gt;</c> tag with a soft line break (U+2028 LINE SEPARATOR): the line
    /// wraps but stays in the same paragraph, keeping the surrounding direction and taking no
    /// inter-paragraph spacing — like HTML <c>&lt;br&gt;</c> or a word-processor Shift+Enter. A
    /// paragraph break is a literal newline (Enter) instead. Matches <c>&lt;br&gt;</c>,
    /// <c>&lt;br/&gt;</c> and <c>&lt;br /&gt;</c> case-insensitively with HTML void-element semantics:
    /// no closing tag, no nested content.
    /// </summary>
    /// <seealso cref="ParseRule"/>
    [Serializable]
    [TypeGroup("Tags", 1)]
    [TypeDescription("Replaces the void <br> tag with a soft line break — wraps the line, stays in the paragraph.")]
    public sealed class LineBreakParseRule : ParseRule
    {
        private const string Tag = "br";
        private const string SoftLineBreak = "\u2028";

        public override int Priority => 1;
        public override bool IsStandalone => true;
        public override string MarkupTriggers => "<";
        public override string TypingTriggers => ">";
        public override string SourceToken => Tag;

        public override int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results)
        {
            if (text[index] != '<')
                return index;

            const int minTagLen = 4;
            if (index + minTagLen > text.Length)
                return index;

            if (!AsciiCaseInsensitive.StartsWith(text, index + 1, Tag))
                return index;

            var i = index + 1 + Tag.Length;

            while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
                i++;

            if (i >= text.Length)
                return index;

            if (text[i] == '/')
            {
                i++;
                if (i >= text.Length || text[i] != '>')
                    return index;
            }
            else if (text[i] != '>')
            {
                return index;
            }

            var openEnd = i + 1;
            results.Add(ParsedRange.SelfClosing(index, openEnd, SoftLineBreak));
            return openEnd;
        }
    }
}
