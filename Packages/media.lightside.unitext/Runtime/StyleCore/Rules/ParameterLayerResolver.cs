using System;

namespace LightSide
{
    /// <summary>
    /// Materializes explicit and source-default parameter layers for APIs that require one string.
    /// Runtime modifiers consume the layers directly through <see cref="ParameterReader"/>.
    /// </summary>
    internal static class ParameterLayerResolver
    {
        public static string Apply(ReadOnlySpan<char> parameter, ReadOnlySpan<char> defaults)
        {
            if (defaults.IsEmpty) return parameter.IsEmpty ? null : SpanIntern.Get(parameter);
            if (parameter.IsEmpty) return SpanIntern.Get(defaults);
            return Merge(parameter, defaults);
        }

        /// <summary>
        /// Merge variant for values that legally contain <c>,</c>/<c>;</c> (URLs — RFC 3986 allows both):
        /// <paramref name="first"/> is kept verbatim as the entire first <c>;</c>-group, and the default's
        /// remaining groups append after it. The default's own first group is discarded — the opaque value
        /// owns that slot. Interned, so re-parsing unchanged text allocates nothing.
        /// </summary>
        public static string ApplyOpaqueFirst(ReadOnlySpan<char> first, ReadOnlySpan<char> defaults)
        {
            if (defaults.IsEmpty) return first.IsEmpty ? null : SpanIntern.Get(first);
            if (first.IsEmpty) return SpanIntern.Get(defaults);

            var defSpan = defaults.Slice(ParameterTokenizer.Next(defaults, ';', out _));
            if (defSpan.IsEmpty) return SpanIntern.Get(first);

            var len = first.Length + 1 + defSpan.Length;
            Span<char> buf = len <= 256 ? stackalloc char[len] : new char[len];
            first.CopyTo(buf);
            buf[first.Length] = ';';
            defSpan.CopyTo(buf.Slice(first.Length + 1));
            return SpanIntern.Get(buf.Slice(0, len));
        }

        private static string Merge(ReadOnlySpan<char> fromText, ReadOnlySpan<char> defaults)
        {
            var maxLen = fromText.Length + defaults.Length;
            Span<char> buf = maxLen <= 256 ? stackalloc char[maxLen] : new char[maxLen];
            var pos = 0;

            var textSpan = fromText;
            var defSpan = defaults;
            var firstGroup = true;

            while (textSpan.Length > 0 || defSpan.Length > 0)
            {
                var textConsumed = ParameterTokenizer.Next(textSpan, ';', out var textGroup);
                textSpan = textSpan.Slice(textConsumed);
                var defaultConsumed = ParameterTokenizer.Next(defSpan, ';', out var defGroup);
                defSpan = defSpan.Slice(defaultConsumed);

                if (!firstGroup) buf[pos++] = ';';
                firstGroup = false;

                if (textGroup.IsEmpty)
                {
                    defGroup.CopyTo(buf.Slice(pos));
                    pos += defGroup.Length;
                }
                else
                {
                    pos = MergeTokensInto(buf, pos, textGroup, defGroup);
                }
            }

            while (pos > 0 && buf[pos - 1] == ';') pos--;

            return SpanIntern.Get(buf.Slice(0, pos));
        }

        private static int MergeTokensInto(Span<char> buf, int pos,
            ReadOnlySpan<char> text, ReadOnlySpan<char> defaults)
        {
            var firstToken = true;

            while (text.Length > 0 || defaults.Length > 0)
            {
                var textConsumed = ParameterTokenizer.Next(text, ',', out var textToken);
                text = text.Slice(textConsumed);
                textToken = textToken.Trim();
                var defaultConsumed = ParameterTokenizer.Next(defaults, ',', out var defToken);
                defaults = defaults.Slice(defaultConsumed);
                defToken = defToken.Trim();
                var chosen = textToken.Length > 0 ? textToken : defToken;

                if (!firstToken) buf[pos++] = ',';
                firstToken = false;

                chosen.CopyTo(buf.Slice(pos));
                pos += chosen.Length;
            }

            while (pos > 0 && buf[pos - 1] == ',') pos--;

            return pos;
        }
    }
}
