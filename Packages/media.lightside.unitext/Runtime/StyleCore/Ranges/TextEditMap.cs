using System;

namespace LightSide
{
    internal readonly struct TextEditMap
    {
        public readonly int start;
        public readonly int removed;
        public readonly int inserted;

        public int OldEnd => start + removed;
        public int Delta => inserted - removed;

        private TextEditMap(int start, int removed, int inserted)
        {
            this.start = start;
            this.removed = removed;
            this.inserted = inserted;
        }

        public int MapPosition(int position, RangeBoundaryAffinity affinity)
        {
            if (removed == 0)
            {
                if (position < start) return position;
                if (position > start) return position + inserted;
                return affinity == RangeBoundaryAffinity.After ? position + inserted : position;
            }

            if (position < start) return position;
            if (position > OldEnd) return position + Delta;
            if (position == OldEnd) return start + inserted;
            return affinity == RangeBoundaryAffinity.After ? start + inserted : start;
        }

        public bool TryMapUnchangedRange(int rangeStart, int rangeEnd, out int mappedStart, out int mappedEnd)
        {
            if (rangeEnd <= start)
            {
                mappedStart = rangeStart;
                mappedEnd = rangeEnd;
                return true;
            }

            if (rangeStart >= OldEnd && !(removed == 0 && rangeStart == start))
            {
                mappedStart = rangeStart + Delta;
                mappedEnd = rangeEnd + Delta;
                return true;
            }

            mappedStart = default;
            mappedEnd = default;
            return false;
        }

        public static TextEditMap Detect(ReadOnlySpan<char> previous, ReadOnlySpan<char> current)
        {
            var prefix = 0;
            var prefixCodepoints = 0;
            while (prefix < previous.Length && prefix < current.Length)
            {
                var left = Utf16.DecodeAt(previous, prefix, out var leftSize);
                var right = Utf16.DecodeAt(current, prefix, out var rightSize);
                if (left != right || leftSize != rightSize) break;
                prefix += leftSize;
                prefixCodepoints++;
            }

            var previousEnd = previous.Length;
            var currentEnd = current.Length;
            while (previousEnd > prefix && currentEnd > prefix)
            {
                var previousStart = PreviousCodepointStart(previous, previousEnd);
                var currentStart = PreviousCodepointStart(current, currentEnd);
                var left = Utf16.DecodeAt(previous, previousStart, out _);
                var right = Utf16.DecodeAt(current, currentStart, out _);
                if (left != right) break;
                previousEnd = previousStart;
                currentEnd = currentStart;
            }

            return new TextEditMap(prefixCodepoints,
                UnicodeData.CountCodepoints(previous.Slice(prefix, previousEnd - prefix)),
                UnicodeData.CountCodepoints(current.Slice(prefix, currentEnd - prefix)));
        }

        private static int PreviousCodepointStart(ReadOnlySpan<char> text, int end)
        {
            var index = end - 1;
            if (index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]))
                index--;
            return index;
        }
    }
}
