using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Formats input live against a pattern — phone <c>(###) ###-####</c>, card <c>#### #### #### ####</c>,
    /// date <c>##/##/####</c>. Slot characters accept input — <c>#</c> a digit, <c>A</c> a letter,
    /// <c>*</c> a letter or digit — and every other character is a literal placed automatically. The whole
    /// field is re-formatted on every edit — typing, mid-field edits, paste, and deletion all stay aligned;
    /// a caret deletion landing on a literal removes the nearest entered character instead, and deleting
    /// the last entered character leaves the field empty (no stranded leading literals). The stored text
    /// is the formatted value; <see cref="RawText"/> returns the entered characters with literals stripped.
    /// Slots match single UTF-16 units (BMP): a supplementary-plane character never fills a slot —
    /// mask domains (phone, card, date) are BMP by nature, and the one-char-per-slot invariant is
    /// what keeps the caret math exact.
    /// </summary>
    [Serializable]
    [TypeDescription("Format input against a pattern (phone, card, date)")]
    [TypeGroup("Formatting", 1)]
    public sealed partial class InputMaskBehavior : InputBehavior
    {
        private const int StackCharLimit = 512;

        /// <summary>Mask pattern whose slots accept digits, letters, or either.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Mask pattern: '#' digit, 'A' letter, '*' letter or digit; other characters are literals. E.g. (###) ###-####")]
        private string pattern = "(###) ###-####";

        /// <summary>The entered characters with literals removed — the raw value behind the mask.</summary>
        public string RawText
        {
            get
            {
                if (editable == null || string.IsNullOrEmpty(pattern)) return string.Empty;
                var text = editable.Text;
                Span<char> raw = text.Length <= StackCharLimit ? stackalloc char[StackCharLimit] : new char[text.Length];
                int rawLen = ExtractRaw(text.AsSpan(), raw, 0, 0, out _, out _);
                return new string(raw.Slice(0, rawLen));
            }
        }

        protected override void OnEnable() => editable.InputFilter.Subscribe(Filter);
        protected override void OnDisable() => editable.InputFilter.Unsubscribe(Filter);

        /// <summary>
        /// Rebuilds the whole field from raw (literal-stripped) content in span passes over one buffer.
        /// A caret deletion that removed only literals instead drops the nearest entered character in the
        /// deletion's direction — otherwise reformatting would restore the literal and the deletion would
        /// stall. When no entered characters remain, the result is the empty string, never bare literals.
        /// </summary>
        private void Filter(ref InputEdit edit)
        {
            if (edit.Rejected) return;
            if (string.IsNullOrEmpty(pattern)) return;

            var doc = edit.document;
            int count = doc.CodepointCount;
            int start = Mathf.Clamp(edit.insertCodepointIndex, 0, count);
            int removedCp = Mathf.Clamp(edit.replacedCodepoints, 0, count - start);
            var inserted = (edit.text ?? string.Empty).AsSpan();

            int docCap = count * 2;
            int combinedCap = docCap + inserted.Length;
            int needed = docCap * 2 + combinedCap + pattern.Length;
            char[] rented = null;
            Span<char> buffer = needed <= StackCharLimit
                ? stackalloc char[StackCharLimit]
                : (rented = ArrayPool<char>.Rent(needed));

            var docChars = buffer.Slice(0, docCap);
            int docLen = 0, prefixCharEnd = -1, removedCharEnd = -1;
            for (int i = 0; i < count; i++)
            {
                if (i == start) prefixCharEnd = docLen;
                if (i == start + removedCp) removedCharEnd = docLen;
                docLen += UnicodeData.EncodeUtf16(doc.GetCodepointAt(i), docChars, docLen);
            }
            if (prefixCharEnd < 0) prefixCharEnd = docLen;
            if (removedCharEnd < 0) removedCharEnd = docLen;

            var raw = buffer.Slice(docCap, docCap);
            int rawLen = ExtractRaw(docChars.Slice(0, docLen), raw,
                prefixCharEnd, removedCharEnd, out int rawPrefixLen, out int suffixStart);

            if (inserted.IsEmpty && removedCp > 0 && suffixStart - rawPrefixLen == 0)
            {
                if (edit.Reason == TextChangeReason.DeleteBackward && rawPrefixLen > 0)
                    rawPrefixLen--;
                else if (edit.Reason == TextChangeReason.DeleteForward && suffixStart < rawLen)
                    suffixStart++;
                else if (rawPrefixLen + (rawLen - suffixStart) > 0)
                {
                    if (rented != null) ArrayPool<char>.Return(rented);
                    edit.Rejected = true;
                    return;
                }
            }

            var combined = buffer.Slice(docCap * 2, combinedCap);
            raw.Slice(0, rawPrefixLen).CopyTo(combined);
            inserted.CopyTo(combined.Slice(rawPrefixLen));
            int suffixLen = rawLen - suffixStart;
            raw.Slice(suffixStart, suffixLen).CopyTo(combined.Slice(rawPrefixLen + inserted.Length));
            int combinedLen = rawPrefixLen + inserted.Length + suffixLen;

            var formatted = buffer.Slice(docCap * 2 + combinedCap, pattern.Length);
            int formattedLen = Format(combined.Slice(0, combinedLen), rawPrefixLen + inserted.Length, formatted, out int caret);

            edit.text = formattedLen > 0 ? new string(formatted.Slice(0, formattedLen)) : string.Empty;
            edit.insertCodepointIndex = 0;
            edit.replacedCodepoints = count;
            edit.caret = caret;

            if (rented != null) ArrayPool<char>.Return(rented);
        }

        /// <summary>
        /// One streaming walk of the pattern over <paramref name="text"/>, collecting entered characters
        /// into <paramref name="raw"/> and capturing the raw counts at the two char boundaries
        /// (the state at a boundary equals a separate walk over that prefix, so one pass replaces three).
        /// </summary>
        private int ExtractRaw(ReadOnlySpan<char> text, Span<char> raw,
            int prefixCharEnd, int removedCharEnd, out int rawPrefixLen, out int rawRemovedEnd)
        {
            int t = 0, rawLen = 0;
            rawPrefixLen = -1;
            rawRemovedEnd = -1;
            for (int p = 0; p < pattern.Length && t < text.Length; p++)
            {
                if (rawPrefixLen < 0 && t >= prefixCharEnd) rawPrefixLen = rawLen;
                if (rawRemovedEnd < 0 && t >= removedCharEnd) rawRemovedEnd = rawLen;
                char pc = pattern[p];
                if (IsSlot(pc))
                {
                    if (!Matches(text[t], pc)) break;
                    raw[rawLen++] = text[t++];
                }
                else if (text[t] == pc) t++;
                else break;
            }
            if (rawPrefixLen < 0) rawPrefixLen = rawLen;
            if (rawRemovedEnd < 0) rawRemovedEnd = rawLen;
            return rawLen;
        }

        /// <summary>
        /// Formats raw input against the pattern into <paramref name="output"/>. Output index equals
        /// pattern index (both branches emit exactly one char per step), so literal detection for the
        /// caret walk reads the pattern directly. Returns 0 when no slot was filled — an empty raw
        /// value renders an empty field, not stranded literals.
        /// </summary>
        private int Format(ReadOnlySpan<char> input, int inputCaret, Span<char> output, out int outCaret)
        {
            int len = 0, inPos = 0;
            bool anySlotFilled = false;
            outCaret = -1;
            for (int p = 0; p < pattern.Length; p++)
            {
                if (outCaret < 0 && inPos >= inputCaret) outCaret = len;
                char pc = pattern[p];
                if (IsSlot(pc))
                {
                    while (inPos < input.Length && !Matches(input[inPos], pc))
                    {
                        inPos++;
                        if (outCaret < 0 && inPos >= inputCaret) outCaret = len;
                    }
                    if (inPos >= input.Length) break;
                    output[len++] = input[inPos++];
                    anySlotFilled = true;
                    if (outCaret < 0 && inPos >= inputCaret) outCaret = len;
                }
                else
                {
                    output[len++] = pc;
                    if (inPos < input.Length && input[inPos] == pc)
                    {
                        inPos++;
                        if (outCaret < 0 && inPos >= inputCaret) outCaret = len;
                    }
                }
            }
            if (!anySlotFilled)
            {
                outCaret = 0;
                return 0;
            }
            if (outCaret < 0) outCaret = len;
            while (outCaret < len && !IsSlot(pattern[outCaret])) outCaret++;
            return len;
        }

        private static bool IsSlot(char p) => p == '#' || p == 'A' || p == '*';

        private static bool Matches(char c, char slot) => slot switch
        {
            '#' => char.IsDigit(c),
            'A' => char.IsLetter(c),
            '*' => char.IsLetterOrDigit(c),
            _ => false,
        };
    }
}
