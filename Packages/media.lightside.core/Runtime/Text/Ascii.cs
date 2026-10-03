using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>Provides allocation-free primitives for ASCII-only grammars.</summary>
    public static class Ascii
    {
        /// <summary>Whether the value is SP, TAB, CR, or LF.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWhitespace(char value) =>
            value == ' ' || value == '\t' || value == '\r' || value == '\n';

        /// <summary>Decodes one hexadecimal digit.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryHexDigit(char value, out byte digit)
        {
            if (value >= '0' && value <= '9')
            {
                digit = (byte)(value - '0');
                return true;
            }

            if (value >= 'a' && value <= 'f')
            {
                digit = (byte)(value - 'a' + 10);
                return true;
            }

            if (value >= 'A' && value <= 'F')
            {
                digit = (byte)(value - 'A' + 10);
                return true;
            }

            digit = 0;
            return false;
        }
    }
}
