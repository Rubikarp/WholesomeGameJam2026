using System;

namespace LightSide
{
    /// <summary>
    /// Protects content inside <c>&lt;noparse&gt;...&lt;/noparse&gt;</c> from being processed by any other parse rule.
    /// The markers themselves are stripped; the content between them appears verbatim in the output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Standalone rule — register via <see cref="UniTextBase.AddRule"/> without a modifier.
    /// </para>
    /// <para>
    /// If the closing <c>&lt;/noparse&gt;</c> is missing, the remainder of the text is treated as protected
    /// (CommonMark-style forgiving close).
    /// </para>
    /// </remarks>
    /// <seealso cref="ParseRule"/>
    [Serializable]
    [TypeGroup("Protection", 0)]
    [TypeDescription("Prevents any parse rule from matching inside <noparse>...</noparse>.")]
    public sealed class NoparseTagRule : ParseRule
    {
        private const string OpenTag = "<noparse>";
        private const string CloseTag = "</noparse>";

        public override int Priority => int.MaxValue;
        public override bool IsStandalone => true;
        public override string MarkupTriggers => "<";
        public override string TypingTriggers => ">";

        public override bool IsCompleteMatch(in ParsedRange range) => true;

        public override int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results)
        {
            if (!AsciiCaseInsensitive.StartsWith(text, index, OpenTag))
                return index;

            var openEnd = index + OpenTag.Length;
            var closeStart = FindCloseTagIgnoreCase(text, openEnd);

            int closeEnd;
            if (closeStart < 0)
            {
                closeStart = text.Length;
                closeEnd = text.Length;
            }
            else
            {
                closeEnd = closeStart + CloseTag.Length;
            }

            results.Add(new ParsedRange(index, openEnd, closeStart, closeEnd));

            return closeEnd;
        }

        private static int FindCloseTagIgnoreCase(ReadOnlySpan<char> text, int start)
        {
            var limit = text.Length - CloseTag.Length;
            for (var i = start; i <= limit; i++)
            {
                if (AsciiCaseInsensitive.StartsWith(text, i, CloseTag)) return i;
            }
            return -1;
        }
    }
}
