using System;

namespace LightSide
{
    public partial class UniTextEditable
    {
        internal void GetCharSelection(out int charStart, out int charLength)
        {
            charStart = 0;
            charLength = 0;
            if (ViewText == null) return;

            var selection = Selection;
            charStart = ViewText.CodepointToCharIndex(selection.Start);
            if (selection.IsCollapsed) return;

            var charEnd = ViewText.CodepointToCharIndex(selection.End);
            charLength = charEnd - charStart;
        }

        internal int CopyCharRangeTo(int charStart, int charLength, Span<char> destination)
        {
            if (ViewText == null || charLength <= 0) return 0;
            if (charStart < 0) charStart = 0;
            var available = ViewText.Length - charStart;
            if (available <= 0) return 0;
            if (charLength > available) charLength = available;
            if (charLength > destination.Length) charLength = destination.Length;
            return charLength > 0 ? ViewText.CopyTo(charStart, charLength, destination) : 0;
        }

        internal void SelectCharRange(int charStart, int charLength)
        {
            if (ViewText == null || isComposing) return;
            Selectable.SetSelectionInternal(CharRangeToSelection(charStart, charLength),
                SelectionChangeReason.Input);
        }

        private TextSelection CharRangeToSelection(int charStart, int charLength)
        {
            if (ViewText == null) return TextSelection.Caret(0);
            charStart = ViewText.SnapToPairBoundary(charStart);
            var available = ViewText.Length - charStart;
            var charEnd = charLength <= 0
                ? charStart
                : charLength >= available ? ViewText.Length : charStart + charLength;
            charEnd = ViewText.SnapToPairBoundary(charEnd);

            var codepointStart = CharToCodepointIndex(charStart);
            var codepointEnd = charEnd == charStart
                ? codepointStart
                : CharToCodepointIndex(charEnd);
            return new TextSelection(codepointStart, codepointEnd, CaretAffinity.Downstream);
        }

        internal int CharToCodepointIndex(int charIndex)
        {
            if (ViewText == null || charIndex <= 0) return 0;
            var length = ViewText.Length;
            return charIndex >= length ? ViewText.CodepointCount : ViewText.CharToCodepointIndex(charIndex);
        }
    }
}
