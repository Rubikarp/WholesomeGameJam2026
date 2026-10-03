using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Auto-detects <c>&lt;trigger&gt;word</c> tokens in plain text (mentions, hashtags) and
    /// produces a style-only range per token whose parameter is the word without the trigger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trigger character is serialized — nothing is hardcoded. The word charset is Unicode
    /// letters, digits and underscore, extendable through <see cref="ExtraWordChars"/> (e.g.
    /// <c>"-."</c> for handles that allow them). A match requires the trigger at a word start:
    /// the preceding character must not be a word character or another trigger, and at least one
    /// word character must follow — so e-mail addresses (<c>user@host</c>) and doubled triggers
    /// stay plain text.
    /// </para>
    /// <para>
    /// Style-only: the trigger and word remain visible, nothing is consumed. Runs at negative
    /// priority so explicit markup (tags, Markdown) wins where they overlap.
    /// </para>
    /// </remarks>
    /// <seealso cref="RawUrlParseRule"/>
    [Serializable]
    [TypeGroup("Auto-detect", 2)]
    [TypeDescription("Automatically detects <trigger>word tokens in plain text (@mentions, #hashtags).")]
    public sealed partial class TriggerWordParseRule : ParseRule
    {
        /// <summary>Trigger prefix; only the first character starts a match. Null or empty disables the rule.</summary>
        [StateProperty(nameof(NotifyChanged))]
        [SerializeField]
        [Tooltip("Character that starts a match (only the first character is used).")]
        private string trigger;

        /// <summary>Characters allowed in the word besides Unicode letters, digits and underscore.</summary>
        [StateProperty(nameof(ApplyExtraWordCharsChange))]
        [SerializeField]
        [Tooltip("Characters allowed in the word besides Unicode letters, digits and underscore.")]
        private string extraWordChars = "";

        /// <summary>Merged behind the detected word (the word itself stays the opaque first value).</summary>
        [StateProperty(nameof(NotifyChanged))]
        [SerializeField, DefaultParameter] private string defaultParameter;

        [NonSerialized] private string identityKey;
        [NonSerialized] private string identityValue;

        public TriggerWordParseRule() : this("@") { }

        public TriggerWordParseRule(string trigger) => this.trigger = trigger;

        private void ApplyExtraWordCharsChange(string previous, ref string current)
        {
            current ??= "";
            if (!string.Equals(previous, current, StringComparison.Ordinal)) NotifyChanged();
        }

        /// <summary>Low priority ensures this runs after explicit markup rules.</summary>
        public override int Priority => -100;

        /// <summary>Style-only matches — the trigger and word stay on screen, nothing is consumed.</summary>
        public override string MarkupTriggers => "";
        public override string TypingTriggers => "";

        /// <inheritdoc/>
        public override string ScanTriggers => trigger;

        /// <inheritdoc/>
        public override string Identity => CachedIdentity("trigger-word:", trigger, ref identityKey, ref identityValue);

        /// <inheritdoc/>
        public override int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results)
        {
            if (string.IsNullOrEmpty(trigger) || text[index] != trigger[0]) return index;
            if (index > 0 && (IsWordChar(text[index - 1]) || text[index - 1] == trigger[0])) return index;

            var end = index + 1;
            while (end < text.Length && IsWordChar(text[end])) end++;
            if (end == index + 1) return index;

            var word = SpanIntern.Get(text.Slice(index + 1, end - index - 1));
            var range = new ParsedRange(index, end, word);
            range.SetOpaquePrimary(DefaultParameter);
            results.Add(range);
            return end;
        }

        private bool IsWordChar(char c)
            => char.IsLetterOrDigit(c) || c == '_' || (extraWordChars != null && extraWordChars.IndexOf(c) >= 0);
    }
}
