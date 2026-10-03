using System;

namespace LightSide
{
    /// <summary>
    /// How text length is measured. The user-facing default is <see cref="Graphemes"/> — what a person counts
    /// as one character (a family emoji, an accented letter) — unlike codepoints or UTF-16 units, which can be
    /// higher than expected.
    /// </summary>
    public enum TextLengthUnit
    {
        /// <summary>User-perceived characters (UAX #29 grapheme clusters). A ZWJ emoji or base + combining mark counts as one.</summary>
        Graphemes,

        /// <summary>UTF-16 code units — matches <c>string.Length</c> in C#/JS/Java, for parity with such a backend.</summary>
        Utf16Units,

        /// <summary>UTF-8 bytes — for a field backed by a byte-sized DB column or wire field.</summary>
        Utf8Bytes,

        /// <summary>Unicode codepoints (scalar values). Surrogate-safe, but still splits emoji/ZWJ sequences.</summary>
        Codepoints
    }

    /// <summary>A length cap and the unit it is measured in. <see cref="Max"/> of 0 means no cap.</summary>
    public readonly struct LengthLimit
    {
        public readonly int Max;
        public readonly TextLengthUnit Unit;

        public LengthLimit(int max, TextLengthUnit unit)
        {
            Max = max;
            Unit = unit;
        }
    }

    /// <summary>
    /// Measures and truncates text by <see cref="TextLengthUnit"/>. Truncation cuts only on a codepoint or grapheme
    /// boundary, so it never splits a surrogate pair, a multi-byte codepoint, or a grapheme cluster.
    /// </summary>
    public static class TextMeasure
    {
        private const int StackCharLimit = 512;
        private const int StackCodepointLimit = 128;

        /// <summary>Length of <paramref name="text"/> in <paramref name="unit"/>.</summary>
        public static int Count(ReadOnlySpan<char> text, TextLengthUnit unit)
        {
            switch (unit)
            {
                case TextLengthUnit.Utf16Units: return text.Length;
                case TextLengthUnit.Codepoints: return UnicodeData.CountCodepoints(text);
                case TextLengthUnit.Utf8Bytes:  return CountUtf8(text);
                default:                    return CountGraphemes(text);
            }
        }

        /// <summary>
        /// Length of the whole document in <paramref name="unit"/>. Measures the document SOURCE —
        /// in a markup-bearing field hidden tag characters count too; keep limits and counters on
        /// plain fields, or accept that they measure source, not the visible projection.
        /// For the per-keystroke grapheme path prefer <see cref="GraphemeCountCache"/>.
        /// </summary>
        public static int Count(ITextDocument document, TextLengthUnit unit)
        {
            switch (unit)
            {
                case TextLengthUnit.Utf16Units: return document.CharCount;
                case TextLengthUnit.Codepoints: return document.CodepointCount;
                default:                    return CountSpan(document, 0, document.CodepointCount, unit);
            }
        }

        /// <summary>Length of the document's codepoint range <c>[startCodepoint, startCodepoint + codepointCount)</c> in <paramref name="unit"/>.</summary>
        public static int CountRange(ITextDocument document, int startCodepoint, int codepointCount, TextLengthUnit unit)
        {
            if (codepointCount <= 0) return 0;
            if (unit == TextLengthUnit.Codepoints) return codepointCount;
            return CountSpan(document, startCodepoint, codepointCount, unit);
        }

        /// <summary>
        /// Char count of the longest prefix of <paramref name="text"/> whose length in <paramref name="unit"/>
        /// is at most <paramref name="budget"/>, cut on a boundary. Pass the result to <c>Substring</c>.
        /// </summary>
        public static int TruncatedCharLength(ReadOnlySpan<char> text, int budget, TextLengthUnit unit)
        {
            if (budget <= 0) return 0;
            if (unit == TextLengthUnit.Graphemes) return TruncateByGrapheme(text, budget);

            int used = 0, offset = 0;
            while (offset < text.Length)
            {
                int cp = (int)UnicodeData.DecodeAt(text, offset, out int charSize);
                int cost = unit == TextLengthUnit.Utf16Units ? charSize
                         : unit == TextLengthUnit.Utf8Bytes ? Utf8Length(cp)
                         : 1;
                if (used + cost > budget) break;
                used += cost;
                offset += charSize;
            }
            return offset;
        }

        private static int CountSpan(ITextDocument document, int startCodepoint, int codepointCount, TextLengthUnit unit)
        {
            var charCapacity = codepointCount * 2;
            char[] rented = null;
            Span<char> buffer = charCapacity <= StackCharLimit
                ? stackalloc char[StackCharLimit]
                : (rented = ArrayPool<char>.Rent(charCapacity));

            int written = document.CopyCodepointRange(startCodepoint, codepointCount, buffer);
            var result = Count(buffer.Slice(0, written), unit);

            if (rented != null) ArrayPool<char>.Return(rented);
            return result;
        }

        private static int CountUtf8(ReadOnlySpan<char> text)
        {
            int bytes = 0, offset = 0;
            while (offset < text.Length)
            {
                int cp = (int)UnicodeData.DecodeAt(text, offset, out int charSize);
                bytes += Utf8Length(cp);
                offset += charSize;
            }
            return bytes;
        }

        private static int CountGraphemes(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty) return 0;

            var count = UnicodeData.CountCodepoints(text);
            int[] rented = null;
            Span<int> codepoints = count <= StackCodepointLimit
                ? stackalloc int[StackCodepointLimit]
                : (rented = ArrayPool<int>.Rent(count));
            codepoints = codepoints.Slice(0, count);

            DecodeToCodepoints(text, codepoints);
            var result = SharedPipelineComponents.GraphemeBreaker.CountGraphemeClusters(codepoints);

            if (rented != null) ArrayPool<int>.Return(rented);
            return result;
        }

        private static int TruncateByGrapheme(ReadOnlySpan<char> text, int budget)
        {
            if (text.IsEmpty) return 0;
            int count = UnicodeData.CountCodepoints(text);

            int[] rentedInts = null;
            bool[] rentedBools = null;
            Span<int> ints = count * 2 + 1 <= StackCodepointLimit * 2 + 1
                ? stackalloc int[StackCodepointLimit * 2 + 1]
                : (rentedInts = ArrayPool<int>.Rent(count * 2 + 1));
            Span<bool> breaks = count + 1 <= StackCodepointLimit + 1
                ? stackalloc bool[StackCodepointLimit + 1]
                : (rentedBools = ArrayPool<bool>.Rent(count + 1));
            var codepoints = ints.Slice(0, count);
            var charAt = ints.Slice(count, count + 1);
            breaks = breaks.Slice(0, count + 1);

            int offset = 0, k = 0;
            while (offset < text.Length)
            {
                charAt[k] = offset;
                codepoints[k] = (int)UnicodeData.DecodeAt(text, offset, out int charSize);
                offset += charSize;
                k++;
            }
            charAt[count] = text.Length;

            SharedPipelineComponents.GraphemeBreaker.GetBreakOpportunities(codepoints, breaks);

            int result = text.Length;
            int clusters = 0;
            for (int i = 1; i <= count; i++)
            {
                if (!breaks[i]) continue;
                clusters++;
                if (clusters == budget) { result = charAt[i]; break; }
            }

            if (rentedInts != null) ArrayPool<int>.Return(rentedInts);
            if (rentedBools != null) ArrayPool<bool>.Return(rentedBools);
            return result;
        }

        private static void DecodeToCodepoints(ReadOnlySpan<char> text, Span<int> destination)
        {
            int offset = 0, k = 0;
            while (offset < text.Length)
            {
                destination[k++] = (int)UnicodeData.DecodeAt(text, offset, out int charSize);
                offset += charSize;
            }
        }

        private static int Utf8Length(int codepoint)
            => codepoint <= 0x7F ? 1 : codepoint <= 0x7FF ? 2 : codepoint <= 0xFFFF ? 3 : 4;
    }
}
