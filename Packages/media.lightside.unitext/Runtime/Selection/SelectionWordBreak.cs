using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Coarse character classes used by touch gestures and whitespace-skipping navigation.
    /// </summary>
    internal enum WordCharClass : byte
    {
        Word,
        Whitespace,
        Punctuation,
        Ideographic,
        Katakana,
        SoutheastAsian
    }

    /// <summary>
    /// Word-boundary helpers used by selection, word drag, and Ctrl/Option+Arrow navigation.
    /// </summary>
    /// <remarks>
    /// Interactive word semantics consume the pipeline's authoritative UAX #29 boundaries,
    /// including dictionary tailoring. Those boundaries preserve grapheme clusters.
    /// </remarks>
    internal static class SelectionWordBreak
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WordCharClass Classify(int codepoint)
        {
            if ((uint)codepoint <= 0x7E)
            {
                if (Ascii.IsWhitespace((char)codepoint))
                    return WordCharClass.Whitespace;

                if ((codepoint >= 'A' && codepoint <= 'Z')
                    || (codepoint >= 'a' && codepoint <= 'z')
                    || (codepoint >= '0' && codepoint <= '9')
                    || codepoint == '_')
                    return WordCharClass.Word;

                return WordCharClass.Punctuation;
            }

            if (codepoint == UnicodeData.ZeroWidthSpace)
                return WordCharClass.Whitespace;

            var provider = UnicodeData.Provider;

            var script = provider.GetScript(codepoint);
            if (script == UnicodeScript.Han || script == UnicodeScript.Hiragana)
                return WordCharClass.Ideographic;
            if (script == UnicodeScript.Katakana)
                return WordCharClass.Katakana;
            if (script == UnicodeScript.Thai || script == UnicodeScript.Lao
                || script == UnicodeScript.Khmer || script == UnicodeScript.Myanmar)
                return WordCharClass.SoutheastAsian;

            var gc = provider.GetGeneralCategory(codepoint);
            switch (gc)
            {
                case GeneralCategory.Lu:
                case GeneralCategory.Ll:
                case GeneralCategory.Lt:
                case GeneralCategory.Lm:
                case GeneralCategory.Lo:
                case GeneralCategory.Nd:
                case GeneralCategory.Nl:
                case GeneralCategory.No:
                case GeneralCategory.Mn:
                case GeneralCategory.Mc:
                case GeneralCategory.Me:
                case GeneralCategory.Pc:
                    return WordCharClass.Word;

                case GeneralCategory.Zs:
                case GeneralCategory.Zl:
                case GeneralCategory.Zp:
                    return WordCharClass.Whitespace;

                default:
                    return WordCharClass.Punctuation;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PreviousBoundary(ReadOnlySpan<bool> boundaries, int position)
        {
            for (var i = Math.Min(position - 1, boundaries.Length - 1); i > 0; i--)
                if (boundaries[i])
                    return i;
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextBoundary(ReadOnlySpan<bool> boundaries, int position, int length)
        {
            for (var i = Math.Max(position + 1, 1); i < boundaries.Length; i++)
                if (boundaries[i])
                    return Math.Min(i, length);
            return length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWhitespace(int codepoint)
            => Classify(codepoint) == WordCharClass.Whitespace;

        private static ReadOnlySpan<bool> GetWordBoundaries(UniTextBuffers buffers, int codepointCount)
        {
            var boundaries = buffers.WordBoundariesOrEmpty;
            if (boundaries.Length != codepointCount + 1)
                throw new InvalidOperationException("Word boundaries are unavailable for processed text.");
            return boundaries;
        }

        /// <summary>
        /// Previous UAX #29 boundary after skipping whitespace segments.
        /// </summary>
        public static int FindWordBoundaryPrevious(
            UniTextBase uniText, int codepointIndex)
        {
            if (codepointIndex <= 0 || uniText == null) return 0;

            var buffers = uniText.Buffers;
            if (buffers == null || buffers.codepoints.count == 0) return 0;

            var codepoints = buffers.codepoints;
            var cpCount = codepoints.count;

            var pos = codepointIndex;
            if (pos > cpCount) pos = cpCount;

            var wordBoundaries = GetWordBoundaries(buffers, cpCount);
            while (pos > 0 && IsWhitespace(codepoints[pos - 1]))
                pos = PreviousBoundary(wordBoundaries, pos);
            return PreviousBoundary(wordBoundaries, pos);
        }

        /// <summary>
        /// Next UAX #29 boundary followed by any adjacent whitespace segments.
        /// </summary>
        public static int FindWordBoundaryNext(UniTextBase uniText, int codepointIndex)
        {
            if (uniText == null) return 0;

            var buffers = uniText.Buffers;
            if (buffers == null || buffers.codepoints.count == 0) return 0;

            var codepoints = buffers.codepoints;
            var cpCount = codepoints.count;

            if (codepointIndex >= cpCount) return cpCount;

            var wordBoundaries = GetWordBoundaries(buffers, cpCount);
            var boundary = NextBoundary(wordBoundaries, Math.Max(codepointIndex, 0), cpCount);
            while (boundary < cpCount && IsWhitespace(codepoints[boundary]))
                boundary = NextBoundary(wordBoundaries, boundary, cpCount);
            return boundary;
        }

        /// <summary>
        /// Resolves the word range that contains <paramref name="codepointIndex"/>. Output
        /// is a half-open <c>[start, end)</c> codepoint range. Empty text yields <c>(0, 0)</c>.
        /// </summary>
        public static (int start, int end) GetWordRange(UniTextBase uniText, int codepointIndex)
        {
            if (uniText == null) return (0, 0);

            var buffers = uniText.Buffers;
            if (buffers == null || buffers.codepoints.count == 0) return (0, 0);

            var codepoints = buffers.codepoints;
            var cpCount = codepoints.count;

            if (codepointIndex >= cpCount) codepointIndex = cpCount > 0 ? cpCount - 1 : 0;
            if (codepointIndex < 0) codepointIndex = 0;
            if (cpCount == 0) return (0, 0);

            var wordBoundaries = GetWordBoundaries(buffers, cpCount);
            var start = wordBoundaries[codepointIndex]
                ? codepointIndex
                : PreviousBoundary(wordBoundaries, codepointIndex + 1);
            var end = NextBoundary(wordBoundaries, codepointIndex, cpCount);
            return (start, end);
        }

        /// <summary>
        /// Range of the line containing <paramref name="codepointIndex"/>, used for
        /// triple-click line selection. Honours hard line breaks; soft-wrap breakpoints
        /// are treated as part of the same paragraph.
        /// </summary>
        public static (int start, int end) GetLineRange(UniTextBase uniText, int codepointIndex)
        {
            if (uniText == null) return (0, 0);
            var buffers = uniText.Buffers;
            if (buffers == null || buffers.lines.count == 0) return (0, 0);

            var lineIndex = SelectionHitTest.FindLineAtCodepoint(codepointIndex, buffers.lines);
            ref var line = ref buffers.lines[lineIndex];
            return (line.range.start, line.range.End);
        }

        /// <summary>
        /// Range of the paragraph (between hard line breaks) containing
        /// <paramref name="codepointIndex"/>. Used for triple-click / triple-tap paragraph
        /// selection (matches the browser / macOS / iOS / Android convention). Breaks match
        /// layout's paragraph segmentation (<see cref="UnicodeData.IsMandatoryBreakChar"/>:
        /// LF, CR, NEL, LS, PS); an index at the very end after a trailing break resolves to
        /// the empty final paragraph, not the previous one.
        /// </summary>
        public static (int start, int end) GetParagraphRange(UniTextBase uniText, int codepointIndex)
        {
            if (uniText == null) return (0, 0);
            var buffers = uniText.Buffers;
            if (buffers == null || buffers.codepoints.count == 0) return (0, 0);

            var codepoints = buffers.codepoints;
            var cpCount = codepoints.count;
            var cp = Math.Clamp(codepointIndex, 0, cpCount);

            int start = cp;
            while (start > 0 && !UnicodeData.IsMandatoryBreakChar(codepoints[start - 1]))
                start--;

            int end = cp;
            while (end < cpCount && !UnicodeData.IsMandatoryBreakChar(codepoints[end]))
                end++;

            return (start, end);
        }

    }
}
