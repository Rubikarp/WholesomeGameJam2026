using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Replaces the void separator tag (default <c>&lt;sep&gt;</c>) with the configured separator
    /// string and marks the inserted range for a paired <see cref="SeparatorModifier"/>.
    /// </summary>
    /// <remarks>
    /// Matches <c>&lt;sep&gt;</c>, <c>&lt;sep/&gt;</c> and <c>&lt;sep=value&gt;</c>, where
    /// <c>value</c> overrides <see cref="Separator"/> for that occurrence — quote it to keep
    /// surrounding spaces: <c>&lt;sep=" ● "&gt;</c>. A stray <c>&lt;/sep&gt;</c> is consumed and
    /// renders nothing.
    /// </remarks>
    /// <seealso cref="SeparatorModifier"/>
    [Serializable]
    [TypeGroup("Tags", 1)]
    [TypeDescription("Void tag replaced with a configurable separator string. Pair with the Separator modifier.")]
    public sealed partial class SeparatorParseRule : ParseRule
    {
        /// <summary>Configured separator tag name without angle brackets.</summary>
        [StateProperty(nameof(NotifyChanged))]
        [SerializeField] private string tagName = "sep";
        /// <summary>Separator string inserted in place of the tag.</summary>
        [StateProperty(nameof(NotifyChanged))]
        [SerializeField] private string separator = " | ";

        private string identityKey;
        private string identityValue;

        public SeparatorParseRule() { }

        public SeparatorParseRule(string tagName, string separator = " | ")
        {
            this.tagName = tagName;
            this.separator = separator;
        }

        public override string Identity => CachedIdentity("tag:", tagName, ref identityKey, ref identityValue);

        public override string MarkupTriggers => "<";
        public override string TypingTriggers => ">";

        public override int Priority => 1;

        public override int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results)
        {
            if (text[index] != '<' || string.IsNullOrEmpty(tagName))
                return index;

            var nameStart = index + 1;
            var isClose = nameStart < text.Length && text[nameStart] == '/';
            if (isClose) nameStart++;

            if (nameStart + tagName.Length >= text.Length)
                return index;
            if (!AsciiCaseInsensitive.StartsWith(text, nameStart, tagName))
                return index;

            var afterName = nameStart + tagName.Length;
            var c = text[afterName];

            if (isClose)
            {
                if (c != '>') return index;
                results.Add(ParsedRange.SelfClosing(index, afterName + 1, string.Empty));
                return afterName + 1;
            }

            string parameter = null;
            int openEnd;

            if (c == '=')
            {
                var paramStart = afterName + 1;
                var closePos = TagParseRule.FindTagClose(text, paramStart);
                if (closePos < 0) return index;
                var selfClose = closePos > paramStart && text[closePos - 1] == '/';
                parameter = TagParseRule.ExtractParameter(text, paramStart, selfClose ? closePos - 1 : closePos);
                openEnd = closePos + 1;
            }
            else if (c == '>')
            {
                openEnd = afterName + 1;
            }
            else if (c == '/' && afterName + 1 < text.Length && text[afterName + 1] == '>')
            {
                openEnd = afterName + 2;
            }
            else
            {
                return index;
            }

            var insert = !string.IsNullOrEmpty(parameter) ? parameter : separator ?? string.Empty;
            var range = ParsedRange.SelfClosing(index, openEnd, insert);
            range.end = openEnd;
            results.Add(range);
            return openEnd;
        }
    }
}
