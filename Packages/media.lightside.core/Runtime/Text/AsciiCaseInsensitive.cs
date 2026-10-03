using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Branchless ASCII case-insensitive matching for parse-rule hot paths.
    /// </summary>
    /// <remarks>
    /// Compares ASCII letters by folding the case bit (<c>0x20</c>) on the input character; non-letter
    /// bytes are required to match exactly, so symbols and digits are case-sensitive as expected.
    /// Intended for fixed lower-case markup tokens (tag names, literal markers) where pulling in the
    /// full <see cref="StringComparison"/> machinery would dwarf the comparison itself.
    /// </remarks>
    public static class AsciiCaseInsensitive
    {
        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="text"/> contains
        /// <paramref name="pattern"/> at <paramref name="index"/>, comparing ASCII letters
        /// without regard to case.
        /// </summary>
        /// <param name="text">The text to scan.</param>
        /// <param name="index">Position in <paramref name="text"/> to compare against.</param>
        /// <param name="pattern">Lower-case pattern. Non-letter characters are matched exactly.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool StartsWith(ReadOnlySpan<char> text, int index, string pattern)
        {
            if (index < 0 || index + pattern.Length > text.Length)
                return false;

            for (var i = 0; i < pattern.Length; i++)
            {
                var c = text[index + i];
                var p = pattern[i];
                if (c == p) continue;
                var cLower = (uint)((c | 0x20) - 'a');
                if (cLower >= 26 || (c | 0x20) != p) return false;
            }

            return true;
        }
    }
}
