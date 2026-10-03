using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Parses ruby (furigana) markup into base + reading ranges for <see cref="RubyModifier"/>.
    /// </summary>
    /// <remarks>
    /// Two equivalent forms:
    /// <list type="bullet">
    /// <item>Standard HTML — <c>&lt;ruby&gt;漢字&lt;rt&gt;かんじ&lt;/rt&gt;&lt;/ruby&gt;</c>: base is the
    /// element content, the reading is in <c>&lt;rt&gt;</c>. Several <c>&lt;rt&gt;</c> give mono-ruby
    /// (one reading per base segment): <c>&lt;ruby&gt;東&lt;rt&gt;とう&lt;/rt&gt;京&lt;rt&gt;きょう&lt;/rt&gt;&lt;/ruby&gt;</c>.
    /// <c>&lt;rp&gt;</c> fallback parentheses are stripped.</item>
    /// <item>Shorthand — <c>&lt;ruby=かんじ&gt;漢字&lt;/ruby&gt;</c>: the reading is the tag value.</item>
    /// </list>
    /// Other inline tags around a ruby work; tags inside the base text are not parsed (the base is plain text).
    /// </remarks>
    /// <seealso cref="RubyModifier"/>
    [Serializable]
    [TypeGroup("Tags", 0)]
    [TypeDescription("Parses ruby/furigana markup: <ruby>base<rt>reading</rt></ruby>.")]
    public sealed partial class RubyParseRule : ParseRule
    {
        /// <summary>Outer ruby element name.</summary>
        [StateProperty(nameof(NotifyChanged))]
        [SerializeField] private string rubyTag = "ruby";
        /// <summary>Ruby-text element name.</summary>
        [StateProperty(nameof(NotifyChanged))]
        [SerializeField] private string textTag = "rt";
        /// <summary>Fallback-parentheses element name.</summary>
        [StateProperty(nameof(NotifyChanged))]
        [SerializeField] private string parenTag = "rp";

        private string identityKey;
        private string identityValue;

        public override string Identity => CachedIdentity("ruby:", rubyTag, ref identityKey, ref identityValue);

        public override string MarkupTriggers => "<";
        public override string TypingTriggers => ">";

        public override int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results)
        {
            if (text[index] != '<') return index;
            if (!AsciiCaseInsensitive.StartsWith(text, index + 1, rubyTag)) return index;

            var after = index + 1 + rubyTag.Length;
            if (after >= text.Length) return index;

            return text[after] switch
            {
                '=' => ParseAttributeForm(text, index, after + 1, results),
                '>' => ParseElementForm(text, index, after + 1, results),
                _ => index
            };
        }

        private int ParseAttributeForm(ReadOnlySpan<char> text, int openStart, int paramStart, PooledList<ParsedRange> results)
        {
            var gt = FindClosingBracket(text, paramStart);
            if (gt < 0) return openStart;

            var selfClose = gt > paramStart && text[gt - 1] == '/';
            var reading = ExtractParam(text, paramStart, selfClose ? gt - 1 : gt);
            var contentStart = gt + 1;
            if (selfClose) return contentStart;

            var closeStart = FindCloseTag(text, contentStart, rubyTag, text.Length);
            if (closeStart < 0) return openStart;
            var closeEnd = closeStart + rubyTag.Length + 3;

            results.Add(new ParsedRange(openStart, contentStart, closeStart, closeEnd, reading));
            return closeEnd;
        }

        /// <summary>
        /// Walks <c>&lt;ruby&gt;…&lt;/ruby&gt;</c> emitting one range per base segment. Each range strips the
        /// markup before its base (the opening tag, or the previous segment's annotation) and the markup
        /// after it (its <c>&lt;rt&gt;</c>/<c>&lt;rp&gt;</c> group, plus the closing tag on the last segment),
        /// so the strip regions tile the block exactly.
        /// </summary>
        private int ParseElementForm(ReadOnlySpan<char> text, int openStart, int contentStart, PooledList<ParsedRange> results)
        {
            var closeRubyStart = FindCloseTag(text, contentStart, rubyTag, text.Length);
            if (closeRubyStart < 0) return openStart;
            var closeRubyEnd = closeRubyStart + rubyTag.Length + 3;

            var openStripStart = openStart;
            var cursor = contentStart;

            while (cursor < closeRubyStart)
            {
                var baseStart = cursor;
                var baseEnd = NextAnnotationMarker(text, cursor, closeRubyStart);

                string reading = null;
                var annoEnd = baseEnd < closeRubyStart
                    ? ConsumeAnnotation(text, baseEnd, closeRubyStart, ref reading)
                    : closeRubyStart;

                var closeStripEnd = annoEnd >= closeRubyStart ? closeRubyEnd : annoEnd;
                results.Add(new ParsedRange(openStripStart, baseStart, baseEnd, closeStripEnd, reading));

                openStripStart = annoEnd;
                cursor = annoEnd;
            }

            return closeRubyEnd;
        }

        private int NextAnnotationMarker(ReadOnlySpan<char> text, int from, int limit)
        {
            for (var i = from; i < limit; i++)
            {
                if (text[i] != '<') continue;
                if (IsOpenTag(text, i, textTag) || IsOpenTag(text, i, parenTag))
                    return i;
            }
            return limit;
        }

        private int ConsumeAnnotation(ReadOnlySpan<char> text, int pos, int limit, ref string reading)
        {
            while (pos < limit && text[pos] == '<')
            {
                if (IsOpenTag(text, pos, textTag))
                {
                    var readingStart = pos + textTag.Length + 2;
                    var close = FindCloseTag(text, readingStart, textTag, limit);
                    if (close < 0) break;
                    if (reading == null && close > readingStart)
                        reading = text.Slice(readingStart, close - readingStart).ToString();
                    pos = close + textTag.Length + 3;
                }
                else if (IsOpenTag(text, pos, parenTag))
                {
                    var inner = pos + parenTag.Length + 2;
                    var close = FindCloseTag(text, inner, parenTag, limit);
                    if (close < 0) break;
                    pos = close + parenTag.Length + 3;
                }
                else
                {
                    break;
                }
            }

            return pos;
        }

        private static bool IsOpenTag(ReadOnlySpan<char> text, int pos, string tag)
        {
            if (pos + tag.Length + 2 > text.Length) return false;
            if (text[pos + 1] == '/') return false;
            if (!AsciiCaseInsensitive.StartsWith(text, pos + 1, tag)) return false;
            return text[pos + 1 + tag.Length] == '>';
        }

        private static int FindCloseTag(ReadOnlySpan<char> text, int from, string tag, int limit)
        {
            var last = Math.Min(limit, text.Length) - (tag.Length + 3);
            for (var i = from; i <= last; i++)
            {
                if (text[i] != '<' || text[i + 1] != '/') continue;
                if (!AsciiCaseInsensitive.StartsWith(text, i + 2, tag)) continue;
                if (text[i + 2 + tag.Length] == '>') return i;
            }

            return -1;
        }

        private static int FindClosingBracket(ReadOnlySpan<char> text, int from)
        {
            var quote = '\0';
            for (var i = from; i < text.Length; i++)
            {
                var c = text[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }

                if (c == '"' || c == '\'') quote = c;
                else if (c == '>') return i;
            }

            return -1;
        }

        private static string ExtractParam(ReadOnlySpan<char> text, int start, int end)
        {
            var span = text.Slice(start, Math.Max(0, end - start)).Trim();
            if (span.Length >= 2)
            {
                var f = span[0];
                var l = span[^1];
                if ((f == '"' && l == '"') || (f == '\'' && l == '\''))
                    span = span.Slice(1, span.Length - 2);
            }

            return span.IsEmpty ? string.Empty : span.ToString();
        }
    }
}
