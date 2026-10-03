using System.Runtime.CompilerServices;
using Unity.Burst;

namespace LightSide
{
    /// <summary>
    /// Burst kernel for the default UAX #29 word-boundary rules.
    /// Property lookup is O(1) in the BMP and logarithmic in the supplementary planes; the
    /// rule pass is linear and uses one caller-owned byte of scratch space per codepoint.
    /// </summary>
    [BurstCompile]
    internal static unsafe class UniTextWordBurst
    {
        internal static void Resolve(int* codePoints, int length,
            byte* bmpWordBreak, WordBreakRangeEntry* wordBreakRanges, int wordBreakRangesLength,
            byte* bmpExtendedPictographic, ExtendedPictographicRangeEntry* extendedPictographicRanges,
            int extendedPictographicRangesLength, byte* propertyScratch, byte* outBreaks)
            => ResolveEntry(codePoints, length, bmpWordBreak, wordBreakRanges, wordBreakRangesLength,
                bmpExtendedPictographic, extendedPictographicRanges, extendedPictographicRangesLength,
                propertyScratch, outBreaks);

        private const int BmpSize = 65536;

        [BurstCompile(CompileSynchronously = true)]
        internal static void ResolveEntry(int* codePoints, int length,
            byte* bmpWordBreak, WordBreakRangeEntry* wordBreakRanges, int wordBreakRangesLength,
            byte* bmpExtendedPictographic, ExtendedPictographicRangeEntry* extendedPictographicRanges,
            int extendedPictographicRangesLength, byte* propertyScratch, byte* outBreaks)
        {
            outBreaks[0] = 1;
            outBreaks[length] = 1;

            for (var i = 0; i < length; i++)
                propertyScratch[i] = (byte)LookupWordBreak(
                    codePoints[i], bmpWordBreak, wordBreakRanges, wordBreakRangesLength);

            var left1 = WordBreakProperty.Other;
            var left2 = WordBreakProperty.Other;
            var hasLeft1 = false;
            var hasLeft2 = false;
            var regionalIndicatorCount = 0;

            for (var boundary = 1; boundary < length; boundary++)
            {
                var consumed = (WordBreakProperty)propertyScratch[boundary - 1];
                if (!IsIgnored(consumed))
                {
                    left2 = left1;
                    hasLeft2 = hasLeft1;
                    left1 = consumed;
                    hasLeft1 = true;
                    regionalIndicatorCount = consumed == WordBreakProperty.Regional_Indicator
                        ? regionalIndicatorCount + 1
                        : 0;
                }

                outBreaks[boundary] = ShouldBreak(
                    codePoints, length, boundary, propertyScratch,
                    bmpExtendedPictographic, extendedPictographicRanges,
                    extendedPictographicRangesLength,
                    left1, left2, hasLeft1, hasLeft2, regionalIndicatorCount)
                    ? (byte)1
                    : (byte)0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldBreak(
            int* codePoints, int length, int boundary, byte* properties,
            byte* bmpExtendedPictographic, ExtendedPictographicRangeEntry* extendedPictographicRanges,
            int extendedPictographicRangesLength,
            WordBreakProperty left1, WordBreakProperty left2,
            bool hasLeft1, bool hasLeft2, int regionalIndicatorCount)
        {
            var rawLeft = (WordBreakProperty)properties[boundary - 1];
            var rawRight = (WordBreakProperty)properties[boundary];

            if (rawLeft == WordBreakProperty.CR && rawRight == WordBreakProperty.LF)
                return false;

            if (IsNewline(rawLeft) || IsNewline(rawRight))
                return true;

            if (rawLeft == WordBreakProperty.ZWJ &&
                LookupExtendedPictographic(codePoints[boundary], bmpExtendedPictographic,
                    extendedPictographicRanges, extendedPictographicRangesLength))
                return false;

            if (rawLeft == WordBreakProperty.WSegSpace && rawRight == WordBreakProperty.WSegSpace)
                return false;

            if (IsIgnored(rawRight))
                return false;

            if (!hasLeft1)
                return true;

            var right1 = rawRight;

            if (IsAhLetter(left1) && IsAhLetter(right1))
                return false;

            if (IsAhLetter(left1) && IsMidLetterOrQuote(right1))
            {
                var right2 = FindNextSignificant(properties, boundary + 1, length);
                if (IsAhLetter(right2))
                    return false;
            }

            if (hasLeft2 && IsAhLetter(left2) && IsMidLetterOrQuote(left1) && IsAhLetter(right1))
                return false;

            if (left1 == WordBreakProperty.Hebrew_Letter && right1 == WordBreakProperty.Single_Quote)
                return false;

            if (left1 == WordBreakProperty.Hebrew_Letter && right1 == WordBreakProperty.Double_Quote)
            {
                var right2 = FindNextSignificant(properties, boundary + 1, length);
                if (right2 == WordBreakProperty.Hebrew_Letter)
                    return false;
            }

            if (hasLeft2 && left2 == WordBreakProperty.Hebrew_Letter &&
                left1 == WordBreakProperty.Double_Quote && right1 == WordBreakProperty.Hebrew_Letter)
                return false;

            if (left1 == WordBreakProperty.Numeric && right1 == WordBreakProperty.Numeric)
                return false;

            if (IsAhLetter(left1) && right1 == WordBreakProperty.Numeric)
                return false;

            if (left1 == WordBreakProperty.Numeric && IsAhLetter(right1))
                return false;

            if (hasLeft2 && left2 == WordBreakProperty.Numeric &&
                IsMidNumberOrQuote(left1) && right1 == WordBreakProperty.Numeric)
                return false;

            if (left1 == WordBreakProperty.Numeric && IsMidNumberOrQuote(right1))
            {
                var right2 = FindNextSignificant(properties, boundary + 1, length);
                if (right2 == WordBreakProperty.Numeric)
                    return false;
            }

            if (left1 == WordBreakProperty.Katakana && right1 == WordBreakProperty.Katakana)
                return false;

            if (IsExtendNumLetLeft(left1) && right1 == WordBreakProperty.ExtendNumLet)
                return false;

            if (left1 == WordBreakProperty.ExtendNumLet && IsExtendNumLetRight(right1))
                return false;

            if (left1 == WordBreakProperty.Regional_Indicator &&
                right1 == WordBreakProperty.Regional_Indicator &&
                (regionalIndicatorCount & 1) != 0)
                return false;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIgnored(WordBreakProperty value)
            => value == WordBreakProperty.Extend ||
               value == WordBreakProperty.Format ||
               value == WordBreakProperty.ZWJ;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNewline(WordBreakProperty value)
            => value == WordBreakProperty.Newline ||
               value == WordBreakProperty.CR ||
               value == WordBreakProperty.LF;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAhLetter(WordBreakProperty value)
            => value == WordBreakProperty.ALetter || value == WordBreakProperty.Hebrew_Letter;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsMidLetterOrQuote(WordBreakProperty value)
            => value == WordBreakProperty.MidLetter ||
               value == WordBreakProperty.MidNumLet ||
               value == WordBreakProperty.Single_Quote;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsMidNumberOrQuote(WordBreakProperty value)
            => value == WordBreakProperty.MidNum ||
               value == WordBreakProperty.MidNumLet ||
               value == WordBreakProperty.Single_Quote;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsExtendNumLetLeft(WordBreakProperty value)
            => IsAhLetter(value) ||
               value == WordBreakProperty.Numeric ||
               value == WordBreakProperty.Katakana ||
               value == WordBreakProperty.ExtendNumLet;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsExtendNumLetRight(WordBreakProperty value)
            => IsAhLetter(value) ||
               value == WordBreakProperty.Numeric ||
               value == WordBreakProperty.Katakana;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static WordBreakProperty FindNextSignificant(byte* properties, int start, int length)
        {
            for (var i = start; i < length; i++)
            {
                var value = (WordBreakProperty)properties[i];
                if (!IsIgnored(value))
                    return value;
            }

            return WordBreakProperty.Other;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static WordBreakProperty LookupWordBreak(
            int codePoint, byte* bmp, WordBreakRangeEntry* ranges, int rangesLength)
        {
            if ((uint)codePoint < BmpSize)
                return (WordBreakProperty)bmp[codePoint];

            var index = FindRange(ranges, rangesLength, codePoint);
            return index >= 0 ? ranges[index].wordBreak : WordBreakProperty.Other;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool LookupExtendedPictographic(
            int codePoint, byte* bmp, ExtendedPictographicRangeEntry* ranges, int rangesLength)
        {
            if ((uint)codePoint < BmpSize)
                return bmp[codePoint] != 0;

            return FindRange(ranges, rangesLength, codePoint) >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(WordBreakRangeEntry* entries, int length, int codePoint)
        {
            var low = 0;
            var high = length - 1;
            while (low <= high)
            {
                var middle = (low + high) >> 1;
                var entry = entries[middle];
                if (codePoint < entry.startCodePoint) high = middle - 1;
                else if (codePoint > entry.endCodePoint) low = middle + 1;
                else return middle;
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(ExtendedPictographicRangeEntry* entries, int length, int codePoint)
        {
            var low = 0;
            var high = length - 1;
            while (low <= high)
            {
                var middle = (low + high) >> 1;
                var entry = entries[middle];
                if (codePoint < entry.startCodePoint) high = middle - 1;
                else if (codePoint > entry.endCodePoint) low = middle + 1;
                else return middle;
            }

            return -1;
        }
    }
}
