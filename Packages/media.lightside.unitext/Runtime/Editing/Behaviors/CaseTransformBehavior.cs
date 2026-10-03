using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightSide
{
    /// <summary>
    /// Forces typed and pasted text to a fixed letter case as it is entered — uppercase for codes and
    /// serials, lowercase for emails / usernames / tags, title case for names. Changes the stored value,
    /// not just the rendering.
    /// </summary>
    [Serializable]
    [TypeDescription("Force input to upper / lower / title case as you type")]
    [TypeGroup("Formatting", 1)]
    public sealed partial class CaseTransformBehavior : InputBehavior
    {
        /// <summary>Case applied to inserted text.</summary>
        [FormerlySerializedAs("letterCase")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Case applied to every inserted character.")]
        private LetterCase @case = LetterCase.Upper;

        protected override void OnEnable() => editable.InputFilter.Subscribe(Filter);
        protected override void OnDisable() => editable.InputFilter.Unsubscribe(Filter);

        private void Filter(ref InputEdit edit)
        {
            if (edit.Rejected || string.IsNullOrEmpty(edit.text)) return;
            edit.text = @case == LetterCase.Title
                ? ToTitle(edit.text, edit.document, edit.insertCodepointIndex)
                : MapEach(edit.text, @case == LetterCase.Upper);
        }

        private const int StackCharLimit = 256;

        private static string MapEach(string text, bool upper)
        {
            var span = text.AsSpan();
            char[] rented = null;
            Span<char> output = span.Length * 2 <= StackCharLimit
                ? stackalloc char[StackCharLimit]
                : (rented = ArrayPool<char>.Rent(span.Length * 2));
            int len = 0;
            bool changed = false;
            for (int i = 0; i < span.Length;)
            {
                int cp = (int)UnicodeData.DecodeAt(span, i, out int size);
                int mapped = upper ? UnicodeData.GetSimpleUppercase(cp) : UnicodeData.GetSimpleLowercase(cp);
                changed |= mapped != cp;
                len += UnicodeData.EncodeUtf16(mapped, output, len);
                i += size;
            }
            var result = changed ? new string(output.Slice(0, len)) : text;
            if (rented != null) ArrayPool<char>.Return(rented);
            return result;
        }

        /// <summary>
        /// Word boundary = any non-alphanumeric codepoint, matching iOS "words" autocapitalize —
        /// capitalizes after hyphens, apostrophes, and opening brackets, not just whitespace.
        /// </summary>
        private static string ToTitle(string text, ITextDocument document, int insertCodepointIndex)
        {
            bool boundary = insertCodepointIndex == 0
                || IsWordBoundary(document.GetCodepointAt(insertCodepointIndex - 1));
            var span = text.AsSpan();
            char[] rented = null;
            Span<char> output = span.Length * 2 <= StackCharLimit
                ? stackalloc char[StackCharLimit]
                : (rented = ArrayPool<char>.Rent(span.Length * 2));
            int len = 0;
            bool changed = false;
            for (int i = 0; i < span.Length;)
            {
                int cp = (int)UnicodeData.DecodeAt(span, i, out int size);
                int mapped = boundary ? UnicodeData.GetSimpleTitlecase(cp) : cp;
                changed |= mapped != cp;
                len += UnicodeData.EncodeUtf16(mapped, output, len);
                boundary = IsWordBoundary(cp);
                i += size;
            }
            var result = changed ? new string(output.Slice(0, len)) : text;
            if (rented != null) ArrayPool<char>.Return(rented);
            return result;
        }

        private static bool IsWordBoundary(int cp)
            => !UnicodeData.IsLetter(cp) && !(cp >= '0' && cp <= '9');
    }

    /// <summary>Letter case enforced by <see cref="CaseTransformBehavior"/>.</summary>
    public enum LetterCase
    {
        Upper,
        Lower,

        /// <summary>Uppercase the first letter of each word; leave the rest as typed (iOS "words" autocapitalize).</summary>
        Title,
    }
}
