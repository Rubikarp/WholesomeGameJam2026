using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Matches literal string patterns and optionally replaces them.</summary>
    /// <remarks>
    /// Useful for custom shortcodes, emoji aliases, or text substitutions.
    /// Example: pattern ":)" with replacement "😊".
    /// </remarks>
    [Serializable]
    [TypeGroup("Utility", 3)]
    [TypeDescription("Matches literal string patterns in the text, with optional replacement.")]
    public sealed partial class StringParseRule : ParseRule
    {
        /// <summary>String patterns to match (case-sensitive).</summary>
        [Tooltip("String patterns to match (case-sensitive).")]
        [EscapeTextArea(1, 3)]
        [StateList(nameof(NotifyChanged))]
        [SerializeField] private string[] patterns;

        /// <summary>Whether to replace matched patterns with a custom string.</summary>
        [Tooltip("Whether to replace matched patterns with a custom string.")]
        [StateProperty(nameof(NotifyChanged))]
        [SerializeField] private bool hasReplacement;

        /// <summary>Replacement string (used when hasReplacement is true).</summary>
        [Tooltip("Replacement string (used when hasReplacement is true).")]
        [EscapeTextArea(1, 3)]
        [StateProperty(nameof(NotifyChanged))]
        [SerializeField] private string replacement;

        public override string Identity => null;

        /// <summary>Distinct first characters of the configured patterns — every match replaces text, so all of them are consuming.</summary>
        public override string MarkupTriggers => BuildTriggers(false);

        public override string TypingTriggers => BuildTriggers(true);

        private string BuildTriggers(bool allCharacters)
        {
            if (patterns == null || patterns.Length == 0) return "";
            var triggers = "";
            for (var i = 0; i < patterns.Length; i++)
            {
                var pattern = patterns[i];
                if (string.IsNullOrEmpty(pattern)) continue;
                var end = allCharacters ? pattern.Length : 1;
                for (var j = 0; j < end; j++)
                    if (triggers.IndexOf(pattern[j]) < 0) triggers += pattern[j];
            }
            return triggers;
        }

        public override int TryMatch(ReadOnlySpan<char> text,int index, PooledList<ParsedRange> results)
        {
            if (patterns == null || patterns.Length == 0)
                return index;

            for (var p = 0; p < patterns.Length; p++)
            {
                var pattern = patterns[p];
                if (string.IsNullOrEmpty(pattern))
                    continue;

                var patternLen = pattern.Length;
                if (index + patternLen > text.Length)
                    continue;

                var matched = true;
                for (var i = 0; i < patternLen; i++)
                {
                    if (text[index + i] != pattern[i])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    var matchEnd = index + patternLen;
                    results.Add(ParsedRange.SelfClosing(index, matchEnd, hasReplacement ? (replacement ?? string.Empty) : string.Empty));
                    return matchEnd;
                }
            }

            return index;
        }
    }
}
