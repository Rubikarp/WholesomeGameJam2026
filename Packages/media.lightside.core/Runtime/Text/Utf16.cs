using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>Describes whether a UTF-16 token contains word content, separators, or both.</summary>
    public enum Utf16TokenClass : byte
    {
        /// <summary>The token is empty or contains both word content and separators.</summary>
        Mixed,

        /// <summary>The token contains only non-separator code points.</summary>
        Word,

        /// <summary>The token contains only whitespace or punctuation code points.</summary>
        Separator,
    }

    /// <summary>UTF-16 and Unicode scalar primitives shared across text consumers.</summary>
    public static class Utf16
    {
        /// <summary>Returns whether a value is a Unicode scalar rather than a surrogate code point.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsUnicodeScalar(int value)
            => (uint)value <= 0x10FFFF && (uint)(value - 0xD800) > 0x7FF;

        /// <summary>
        /// Decodes the code point at <paramref name="index"/> and returns its UTF-16 size.
        /// An unpaired surrogate is returned as its own value with size one.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DecodeAt(ReadOnlySpan<char> text, int index, out int size)
        {
            var high = text[index];
            if (char.IsHighSurrogate(high) && index + 1 < text.Length &&
                char.IsLowSurrogate(text[index + 1]))
            {
                size = 2;
                return 0x10000 + ((high - 0xD800) << 10) + (text[index + 1] - 0xDC00);
            }

            size = 1;
            return high;
        }

        /// <summary>
        /// Decodes one Unicode scalar and reports its UTF-16 size, substituting U+FFFD for an
        /// unpaired surrogate.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DecodeScalarAt(ReadOnlySpan<char> text, int index, out int size)
        {
            var first = text[index];
            var surrogateOffset = (uint)(first - 0xD800);
            if (surrogateOffset > 0x7FF)
            {
                size = 1;
                return first;
            }

            if (surrogateOffset <= 0x3FF && index + 1 < text.Length)
            {
                var lowOffset = (uint)(text[index + 1] - 0xDC00);
                if (lowOffset <= 0x3FF)
                {
                    size = 2;
                    return 0x10000 + ((int)surrogateOffset << 10) + (int)lowOffset;
                }
            }

            size = 1;
            return 0xFFFD;
        }

        /// <summary>
        /// Returns the number of UTF-16 code units occupied by the code point at
        /// <paramref name="index"/>. An unpaired surrogate occupies one unit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeAt(ReadOnlySpan<char> text, int index)
            => char.IsHighSurrogate(text[index]) && index + 1 < text.Length &&
               char.IsLowSurrogate(text[index + 1]) ? 2 : 1;

        /// <summary>
        /// Counts code points in a UTF-16 sequence, treating each unpaired surrogate as one code point.
        /// </summary>
        public static int CountCodepoints(ReadOnlySpan<char> text)
        {
            var count = 0;
            for (var index = 0; index < text.Length; index += SizeAt(text, index)) count++;
            return count;
        }

        /// <summary>
        /// Finds the minimal changed ranges of two UTF-16 sequences without splitting a valid
        /// surrogate pair. The ranges start at <paramref name="start"/> and have the returned lengths.
        /// </summary>
        public static void GetChangedRange(ReadOnlySpan<char> before, ReadOnlySpan<char> after,
            out int start, out int removedLength, out int addedLength)
        {
            start = 0;
            var prefixLimit = Math.Min(before.Length, after.Length);
            while (start < prefixLimit && before[start] == after[start]) start++;
            if (start > 0 &&
                (start < before.Length && char.IsHighSurrogate(before[start - 1]) &&
                 char.IsLowSurrogate(before[start]) ||
                 start < after.Length && char.IsHighSurrogate(after[start - 1]) &&
                 char.IsLowSurrogate(after[start])))
                start--;

            var suffix = 0;
            var suffixLimit = Math.Min(before.Length - start, after.Length - start);
            while (suffix < suffixLimit &&
                   before[before.Length - suffix - 1] == after[after.Length - suffix - 1])
                suffix++;
            var beforeSuffixStart = before.Length - suffix;
            var afterSuffixStart = after.Length - suffix;
            if (suffix > 0 &&
                (beforeSuffixStart > 0 && beforeSuffixStart < before.Length &&
                 char.IsHighSurrogate(before[beforeSuffixStart - 1]) &&
                 char.IsLowSurrogate(before[beforeSuffixStart]) ||
                 afterSuffixStart > 0 && afterSuffixStart < after.Length &&
                 char.IsHighSurrogate(after[afterSuffixStart - 1]) &&
                 char.IsLowSurrogate(after[afterSuffixStart])))
                suffix--;

            removedLength = before.Length - start - suffix;
            addedLength = after.Length - start - suffix;
        }

        /// <summary>
        /// Classifies a token for edit coalescing using .NET whitespace and punctuation categories.
        /// </summary>
        public static Utf16TokenClass ClassifyToken(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty) return Utf16TokenClass.Mixed;

            var anyWord = false;
            var anySeparator = false;
            for (var index = 0; index < text.Length;)
            {
                var codepoint = DecodeAt(text, index, out var size);
                index += size;
                if (IsSeparator(codepoint)) anySeparator = true;
                else anyWord = true;
                if (anyWord && anySeparator) return Utf16TokenClass.Mixed;
            }

            return anySeparator ? Utf16TokenClass.Separator : Utf16TokenClass.Word;
        }

        /// <summary>
        /// Returns the largest prefix no longer than <paramref name="maximumLength"/> that does not
        /// end with a high surrogate when the source is truncated.
        /// </summary>
        public static int SafePrefixLength(ReadOnlySpan<char> text, int maximumLength)
        {
            if (maximumLength < 0) throw new ArgumentOutOfRangeException(nameof(maximumLength));
            if (maximumLength >= text.Length) return text.Length;
            return maximumLength > 0 && char.IsHighSurrogate(text[maximumLength - 1])
                ? maximumLength - 1
                : maximumLength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSeparator(int codepoint)
        {
            if (codepoint <= char.MaxValue)
            {
                var character = (char)codepoint;
                return char.IsWhiteSpace(character) || char.IsPunctuation(character);
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(codepoint);
            return category is UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation
                or UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator;
        }
    }
}
