using System;

namespace LightSide
{
    /// <summary>Ordinal case-insensitive equality helpers — the single home for the comparison,
    /// so callers never hand-roll <c>StringComparison</c> boilerplate.</summary>
    public static class StringExtensions
    {
        /// <summary>Ordinal case-insensitive equality for character spans. Allocation-free.</summary>
        public static bool EqualsIgnoreCase(this ReadOnlySpan<char> token, ReadOnlySpan<char> value)
            => token.Equals(value, StringComparison.OrdinalIgnoreCase);

        /// <summary>Ordinal case-insensitive equality for strings. Null-safe on both sides.</summary>
        public static bool EqualsIgnoreCase(this string token, string value)
            => string.Equals(token, value, StringComparison.OrdinalIgnoreCase);
    }
}
