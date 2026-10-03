using System;

namespace LightSide
{
    /// <summary>
    /// Replaces <c>&lt;math&gt;...&lt;/math&gt;</c> with one layout placeholder while preserving the
    /// enclosed formula as the modifier parameter.
    /// </summary>
    [Serializable]
    [TypeGroup("Tags", 0)]
    [TypeDescription("Parses <math>...</math> formulas for MathModifier.")]
    public sealed class MathParseRule : ParseRule
    {
        private const string OpenTag = "<math>";
        private const string CloseTag = "</math>";

        /// <inheritdoc/>
        public override int Priority => 10;

        /// <inheritdoc/>
        public override bool CanWrap => true;

        /// <inheritdoc/>
        public override string SourceToken => "math";

        /// <inheritdoc/>
        public override string MarkupTriggers => "<";

        /// <inheritdoc/>
        public override string TypingTriggers => "<>";

        /// <inheritdoc/>
        public override int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results)
        {
            if (!AsciiCaseInsensitive.StartsWith(text, index, OpenTag))
                return index;

            var formulaStart = index + OpenTag.Length;
            var closeStart = FindClose(text, formulaStart);
            if (closeStart < 0)
                return index;

            var closeEnd = closeStart + CloseTag.Length;
            var formula = text.Slice(formulaStart, closeStart - formulaStart).ToString();
            var range = ParsedRange.SelfClosing(index, closeEnd,
                UnicodeData.ObjectReplacementCharacterString, formula);
            range.SetOpaquePrimary(null);
            results.Add(range);
            return closeEnd;
        }

        /// <inheritdoc/>
        public override string Apply(ReadOnlySpan<char> content, string parameter)
            => string.Concat(OpenTag, content.ToString(), CloseTag);

        private static int FindClose(ReadOnlySpan<char> text, int start)
        {
            var last = text.Length - CloseTag.Length;
            for (var i = start; i <= last; i++)
                if (text[i] == '<' && AsciiCaseInsensitive.StartsWith(text, i, CloseTag))
                    return i;
            return -1;
        }
    }
}
