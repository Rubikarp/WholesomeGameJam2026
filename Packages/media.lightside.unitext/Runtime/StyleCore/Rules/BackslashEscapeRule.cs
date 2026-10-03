using System;

namespace LightSide
{
    /// <summary>
    /// Escapes a single ASCII punctuation character preceded by <c>\</c>.
    /// Example: <c>\*</c> becomes a literal <c>*</c> and is protected from any other parse rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Standalone rule — register via <see cref="UniTextBase.AddRule"/> without a modifier.
    /// </para>
    /// <para>
    /// The escapable set follows CommonMark §2.4 — all ASCII punctuation:
    /// <c>!"#$%&amp;'()*+,-./:;&lt;=&gt;?@[\]^_`{|}~</c>
    /// </para>
    /// <para>
    /// Only isolated occurrences are protected. A paired inline matcher (e.g. <c>**bold**</c>) that
    /// started scanning before an inner <c>\*</c> will not respect the escape — that case requires
    /// matchers themselves to skip escaped characters when seeking their closing marker.
    /// </para>
    /// </remarks>
    /// <seealso cref="ParseRule"/>
    [Serializable]
    [TypeGroup("Protection", 3)]
    [TypeDescription("Escapes a single character after '\\' (e.g. \\* becomes a literal *).")]
    public sealed class BackslashEscapeRule : ParseRule
    {
        public override int Priority => int.MaxValue;
        public override bool IsStandalone => true;
        public override string MarkupTriggers => "\\";
        public override string TypingTriggers => "\\";
        public override char EscapePrefix => '\\';

        public override int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results)
        {
            if (text[index] != '\\') return index;
            if (index + 1 >= text.Length) return index;

            var next = text[index + 1];
            if (!IsEscapable(next)) return index;

            results.Add(ParsedRange.SelfClosing(index, index + 2, GetSingleCharString(next)));
            return index + 2;
        }

        public override bool IsEscapable(char c)
        {
            return c switch
            {
                '!' or '"' or '#' or '$' or '%' or '&' or '\'' or '(' or ')' or '*' or '+' or ','
                    or '-' or '.' or '/' or ':' or ';' or '<' or '=' or '>' or '?' or '@' or '['
                    or '\\' or ']' or '^' or '_' or '`' or '{' or '|' or '}' or '~' => true,
                _ => false,
            };
        }

        private static readonly string[] asciiStrings = BuildAsciiStringCache();

        private static string[] BuildAsciiStringCache()
        {
            var arr = new string[128];
            for (var i = 0; i < arr.Length; i++) arr[i] = ((char)i).ToString();
            return arr;
        }

        private static string GetSingleCharString(char c)
        {
            return c < 128 ? asciiStrings[c] : c.ToString();
        }
    }
}
