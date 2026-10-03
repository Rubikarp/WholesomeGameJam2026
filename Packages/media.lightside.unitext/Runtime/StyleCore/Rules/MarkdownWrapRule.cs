using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>Parses symmetric open/close markers in text (e.g., **text**, ~~text~~).</summary>
    /// <remarks>
    /// <para>
    /// The marker string is configurable — any string can be used as a marker with any modifier.
    /// When <see cref="DefaultParameter"/> is set, matched ranges use it as the parameter value.
    /// </para>
    /// <para>
    /// Matching follows CommonMark §6.2 flanking rules (simplified to the marker occurrence's
    /// neighbours): a marker opens only when left-flanking (next char is not whitespace, and not
    /// punctuation unless preceded by whitespace/punctuation) and closes only when right-flanking
    /// (mirrored). Markers made of <c>_</c> additionally never open or close inside a word, so
    /// <c>snake_case_name</c> stays literal. Stray markers in arithmetic (<c>2 * 3</c>) never open.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Markdown", 1)]
    [TypeDescription("Activates the modifier using symmetric wrap markers, e.g. **text**.")]
    public sealed partial class MarkdownWrapRule : ParseRule
    {
        /// <summary>The symmetric marker string that wraps the affected text range.</summary>
        [UnityEngine.Tooltip("The symmetric marker string that wraps the affected text range (e.g. **, ~~, ++, or any custom string).")]
        [StateProperty(nameof(NotifyChanged))]
        [UnityEngine.SerializeField] private string marker = "**";

        /// <summary>Parameter given to matched ranges — the marker syntax itself carries no value.</summary>
        [StateProperty(nameof(NotifyChanged))]
        [UnityEngine.SerializeField, DefaultParameter] private string defaultParameter;

        private readonly Stack<(int openStart, int openEnd)> openMarkers = new(8);

        private string identityKey;
        private string identityValue;
        private string triggersKey;
        private string triggersValue;

        public override int Priority => marker != null ? marker.Length : 0;

        public override string Identity => CachedIdentity("marker:", marker, ref identityKey, ref identityValue);

        public override string MarkupTriggers
        {
            get
            {
                if (string.IsNullOrEmpty(marker)) return "";
                if (!ReferenceEquals(triggersKey, marker))
                {
                    triggersKey = marker;
                    triggersValue = marker.Substring(0, 1);
                }
                return triggersValue;
            }
        }

        public override void Reset()
        {
            openMarkers.Clear();
        }

        public override string TypingTriggers => marker ?? "";

        public override bool CanWrap => !string.IsNullOrEmpty(marker);

        public override string Apply(ReadOnlySpan<char> content, string parameter)
            => string.IsNullOrEmpty(marker) ? null : marker + content.ToString() + marker;

        public override int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results)
        {
            if (string.IsNullOrEmpty(marker))
                return index;

            var len = marker.Length;
            if (index + len > text.Length)
                return index;

            for (var i = 0; i < len; i++)
            {
                if (text[index + i] != marker[i])
                    return index;
            }

            var afterMarker = index + len;
            Classify(text, index, afterMarker, out var canOpen, out var canClose);

            if (canClose && openMarkers.Count > 0)
            {
                var open = openMarkers.Pop();
                var range = new ParsedRange(open.openStart, open.openEnd, index, afterMarker);
                range.SetDefaultParameter(DefaultParameter);
                results.Add(range);
                return afterMarker;
            }

            if (canOpen)
            {
                openMarkers.Push((index, afterMarker));
                return afterMarker;
            }

            return index;
        }

        /// <summary>
        /// CommonMark §6.2 delimiter-run classification against the occurrence's neighbour characters.
        /// Text boundaries count as whitespace; <c>_</c> markers get the intraword exception so
        /// underscores inside identifiers never toggle emphasis.
        /// </summary>
        private void Classify(ReadOnlySpan<char> text, int index, int afterMarker, out bool canOpen, out bool canClose)
        {
            var prevIsWhite = index == 0 || char.IsWhiteSpace(text[index - 1]);
            var nextIsWhite = afterMarker >= text.Length || char.IsWhiteSpace(text[afterMarker]);
            var prevIsPunct = !prevIsWhite && IsPunct(text[index - 1]);
            var nextIsPunct = !nextIsWhite && IsPunct(text[afterMarker]);

            var leftFlanking = !nextIsWhite && (!nextIsPunct || prevIsWhite || prevIsPunct);
            var rightFlanking = !prevIsWhite && (!prevIsPunct || nextIsWhite || nextIsPunct);

            if (marker[0] == '_')
            {
                canOpen = leftFlanking && (!rightFlanking || prevIsPunct);
                canClose = rightFlanking && (!leftFlanking || nextIsPunct);
            }
            else
            {
                canOpen = leftFlanking;
                canClose = rightFlanking;
            }
        }

        private static bool IsPunct(char c) => char.IsPunctuation(c) || char.IsSymbol(c);

        public override void Finalize(ReadOnlySpan<char> text, PooledList<ParsedRange> results)
        {
            openMarkers.Clear();
        }
    }
}
