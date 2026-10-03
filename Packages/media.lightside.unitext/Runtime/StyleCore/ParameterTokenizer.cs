using System;

namespace LightSide
{
    internal static class ParameterTokenizer
    {
        public static int Next(ReadOnlySpan<char> remaining, char separator,
            out ReadOnlySpan<char> token)
            => Next(remaining, separator, out token, out _);

        public static int Next(ReadOnlySpan<char> remaining, char separator,
            out ReadOnlySpan<char> token, out bool hadSeparator)
        {
            if (remaining.IsEmpty)
            {
                token = default;
                hadSeparator = false;
                return 0;
            }

            var index = FindSeparator(remaining, separator);
            if (index < 0)
            {
                token = remaining;
                hadSeparator = false;
                return remaining.Length;
            }

            token = remaining.Slice(0, index);
            hadSeparator = true;
            return index + 1;
        }

        public static ReadOnlySpan<char> Decode(ReadOnlySpan<char> raw, out string decoded,
            out bool isSet)
        {
            raw = raw.Trim();
            var quoted = HasOuterQuotes(raw);
            if (quoted) raw = raw.Slice(1, raw.Length - 2);
            isSet = quoted || !raw.IsEmpty;

            var escape = FirstRecognizedEscape(raw);
            if (escape < 0)
            {
                decoded = null;
                return raw;
            }

            Span<char> buffer = raw.Length <= 256 ? stackalloc char[raw.Length] : new char[raw.Length];
            var written = 0;
            for (var i = 0; i < raw.Length; i++)
            {
                var c = raw[i];
                if (c == '\\' && i + 1 < raw.Length && IsEscapable(raw[i + 1]))
                    c = raw[++i];
                buffer[written++] = c;
            }

            decoded = SpanIntern.Get(buffer.Slice(0, written));
            return decoded.AsSpan();
        }

        /// <summary>
        /// Encodes one edited token so delimiters, quotes, escapes, boundary whitespace, and an
        /// explicitly set empty string survive the surrounding parameter-list grammar.
        /// </summary>
        public static string Encode(string value, bool preserveEmpty = false)
        {
            value ??= string.Empty;
            if (value.Length == 0) return preserveEmpty ? "\"\"" : string.Empty;

            var quote = char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]);
            var escaped = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\\' || c == '"') escaped++;
                if (c == ',' || c == ';' || c == '\\' || c == '\'' || c == '"') quote = true;
            }
            if (!quote) return value;

            Span<char> buffer = value.Length + escaped + 2 <= 256
                ? stackalloc char[value.Length + escaped + 2]
                : new char[value.Length + escaped + 2];
            var written = 0;
            buffer[written++] = '"';
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\\' || c == '"') buffer[written++] = '\\';
                buffer[written++] = c;
            }
            buffer[written++] = '"';
            return buffer.Slice(0, written).ToString();
        }

        private static int FindSeparator(ReadOnlySpan<char> value, char separator)
        {
            var first = value.IndexOf(separator);
            if (first < 0) return -1;

            var prefix = value.Slice(0, first);
            if (prefix.IndexOf('\\') < 0 && prefix.IndexOf('\'') < 0 && prefix.IndexOf('"') < 0)
                return first;

            var quote = '\0';
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                {
                    i++;
                    continue;
                }

                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    quote = c;
                    continue;
                }

                if (c == separator) return i;
            }

            return -1;
        }

        private static bool HasOuterQuotes(ReadOnlySpan<char> value)
        {
            if (value.Length < 2 || value[0] != '\'' && value[0] != '"') return false;

            var quote = value[0];
            for (var i = 1; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    i++;
                    continue;
                }

                if (value[i] == quote) return i == value.Length - 1;
            }

            return false;
        }

        private static int FirstRecognizedEscape(ReadOnlySpan<char> value)
        {
            for (var i = 0; i + 1 < value.Length; i++)
                if (value[i] == '\\' && IsEscapable(value[i + 1]))
                    return i;
            return -1;
        }

        private static bool IsEscapable(char value)
            => value == ',' || value == ';' || value == '\\' || value == '\'' || value == '"';
    }
}
