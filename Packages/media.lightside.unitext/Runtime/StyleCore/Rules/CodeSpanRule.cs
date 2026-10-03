using System;

namespace LightSide
{
    /// <summary>
    /// Protects content inside backtick-delimited code spans (e.g. <c>`x`</c>, <c>``x``</c>, <c>```x```</c>)
    /// from being processed by other parse rules. Follows CommonMark §6.1 balanced-run semantics:
    /// an N-backtick run is closed by the next N-backtick run of the same length.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Standalone rule — register via <see cref="UniTextBase.AddRule"/> without a modifier.
    /// </para>
    /// <para>
    /// If no matching closing run is found, the rule does not consume the input (the backticks are
    /// treated as literal text). This matches CommonMark's non-forgiving close behavior.
    /// </para>
    /// </remarks>
    /// <seealso cref="ParseRule"/>
    [Serializable]
    [TypeGroup("Protection", 3)]
    [TypeDescription("Prevents parse rules from matching inside `code spans` (balanced backtick runs).")]
    public sealed class CodeSpanRule : ParseRule
    {
        public override int Priority => int.MaxValue;
        public override bool IsStandalone => true;
        public override string MarkupTriggers => "`";
        public override string TypingTriggers => "`";

        public override int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results)
        {
            if (text[index] != '`') return index;

            var openEnd = index;
            while (openEnd < text.Length && text[openEnd] == '`') openEnd++;
            var runLen = openEnd - index;

            var closeStart = FindMatchingCloseRun(text, openEnd, runLen);
            if (closeStart < 0) return index;

            var closeEnd = closeStart + runLen;
            results.Add(new ParsedRange(index, openEnd, closeStart, closeEnd));
            return closeEnd;
        }

        private static int FindMatchingCloseRun(ReadOnlySpan<char> text, int from, int runLen)
        {
            var i = from;
            while (i < text.Length)
            {
                if (text[i] != '`') { i++; continue; }

                var runStart = i;
                while (i < text.Length && text[i] == '`') i++;

                if (i - runStart == runLen) return runStart;
            }
            return -1;
        }
    }
}
