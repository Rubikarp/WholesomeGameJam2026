using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Gap buffer storage backing <see cref="UniTextEditable"/>. Internal to the engine;
    /// integrators read document state through <see cref="ITextDocument"/> instead.
    /// All edits at the cursor position are O(1) amortized.
    /// </summary>
    /// <remarks>
    /// The buffer maintains a contiguous gap at the edit position — sequential typing
    /// never moves the gap. Codepoint-level methods handle UTF-16 surrogate pairs
    /// transparently. Growth is geometric, gap is restored to <c>max(64, newCapacity/8)</c>
    /// on every grow, and the buffer never shrinks.
    /// Char-level positions must not split a surrogate pair (<see cref="Insert"/>/<see cref="Delete"/>
    /// throw); lone surrogates inserted as separate halves around the gap yield gap-position-dependent
    /// codepoint counts — sanitize upstream instead of storing them.
    /// </remarks>
    internal sealed class GapBuffer
    {
        private const int DefaultCapacity = 64;
        private const int MinGapSize = 64;

        private char[] buffer;
        private int gapStart;
        private int gapEnd;
        private int version;

        private int cachedCodepointCount;
        private int cachedCodepointVersion;

        private int cpCacheCpIdx;
        private int cpCacheCharIdx;
        private int cpCacheVersion = -1;

        private int gapCpBeforeGap;

        /// <summary>
        /// Initializes a new gap buffer with the default capacity of 64 chars.
        /// </summary>
        internal GapBuffer() : this(DefaultCapacity) { }

        /// <summary>
        /// Initializes a new gap buffer with the specified capacity.
        /// </summary>
        /// <param name="capacity">Initial buffer capacity in chars. Clamped to a minimum of 64.</param>
        internal GapBuffer(int capacity)
        {
            capacity = Math.Max(capacity, DefaultCapacity);
            buffer = new char[capacity];
            gapStart = 0;
            gapEnd = capacity;
            version = 0;
            cachedCodepointVersion = -1;
        }

        /// <summary>Logical length in chars (excluding the gap).</summary>
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.Length - GapSize;
        }

        /// <summary>Current size of the gap.</summary>
        private int GapSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => gapEnd - gapStart;
        }

        /// <summary>Monotonically increasing version counter, incremented on every mutation.</summary>
        public int Version
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => version;
        }

        /// <summary>
        /// Gets the char at the specified logical index, skipping over the gap.
        /// </summary>
        /// <param name="index">Logical char index in [0, Length).</param>
        public char this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)index >= (uint)Length)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return buffer[index < gapStart ? index : index + GapSize];
            }
        }

        /// <summary>Span over the text before the gap: buffer[0..gapStart].</summary>
        public ReadOnlySpan<char> BeforeGap
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.AsSpan(0, gapStart);
        }

        /// <summary>Span over the text after the gap: buffer[gapEnd..end].</summary>
        public ReadOnlySpan<char> AfterGap
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.AsSpan(gapEnd, buffer.Length - gapEnd);
        }

        /// <summary>
        /// Creates a string from the logical contents (excluding the gap).
        /// </summary>
        /// <remarks>
        /// Allocates a new string. Use <see cref="BeforeGap"/> and <see cref="AfterGap"/>
        /// for the zero-allocation hot path.
        /// </remarks>
        public override string ToString()
        {
            var len = Length;
            if (len == 0) return string.Empty;

            return string.Create(len, this, static (span, self) =>
            {
                self.BeforeGap.CopyTo(span);
                self.AfterGap.CopyTo(span.Slice(self.gapStart));
            });
        }

        /// <summary>
        /// Copies a range of logical chars to the destination span.
        /// </summary>
        /// <param name="start">Starting logical char index.</param>
        /// <param name="count">Number of chars to copy.</param>
        /// <param name="destination">Destination span. Must have at least <paramref name="count"/> capacity.</param>
        /// <returns>Number of chars actually copied.</returns>
        public int CopyTo(int start, int count, Span<char> destination)
        {
            var len = Length;
            if (start < 0 || start > len)
                throw new ArgumentOutOfRangeException(nameof(start));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (start + count > len)
                count = len - start;
            if (count == 0)
                return 0;

            var end = start + count;
            var written = 0;

            if (start < gapStart)
            {
                var beforeCount = Math.Min(gapStart - start, count);
                buffer.AsSpan(start, beforeCount).CopyTo(destination);
                written += beforeCount;
            }

            if (end > gapStart)
            {
                var afterLogicalStart = Math.Max(start, gapStart);
                var afterPhysicalStart = afterLogicalStart + GapSize;
                var afterCount = end - afterLogicalStart;
                buffer.AsSpan(afterPhysicalStart, afterCount).CopyTo(destination.Slice(written));
                written += afterCount;
            }

            return written;
        }

        /// <summary>
        /// Copies the entire logical content into <paramref name="destination"/>.
        /// </summary>
        public int CopyTo(Span<char> destination) => CopyTo(0, Length, destination);

        /// <summary>
        /// Inserts text at the specified logical char position.
        /// </summary>
        /// <param name="charPosition">Logical char index where text will be inserted.</param>
        /// <param name="text">The text to insert.</param>
        internal void Insert(int charPosition, ReadOnlySpan<char> text)
        {
            if (text.Length == 0) return;
            var len = Length;
            if ((uint)charPosition > (uint)len)
                throw new ArgumentOutOfRangeException(nameof(charPosition));
            if (IsMidPair(charPosition))
                throw new ArgumentException("GapBuffer.Insert: char position splits a surrogate pair", nameof(charPosition));

            EnsureCapacity(len + text.Length);
            MoveGap(charPosition);

            text.CopyTo(buffer.AsSpan(gapStart));
            gapStart += text.Length;
            gapCpBeforeGap += UnicodeData.CountCodepoints(text);
            version++;
        }

        /// <summary>
        /// Deletes chars starting at the specified logical char position.
        /// </summary>
        /// <param name="charPosition">Logical char index where deletion begins.</param>
        /// <param name="charCount">Number of chars to delete.</param>
        internal void Delete(int charPosition, int charCount)
        {
            if (charCount == 0) return;
            var len = Length;
            if ((uint)charPosition > (uint)len)
                throw new ArgumentOutOfRangeException(nameof(charPosition));
            if (charCount < 0 || charPosition + charCount > len)
                throw new ArgumentOutOfRangeException(nameof(charCount));
            if (IsMidPair(charPosition) || IsMidPair(charPosition + charCount))
                throw new ArgumentException("GapBuffer.Delete: range boundary splits a surrogate pair", nameof(charPosition));

            MoveGap(charPosition);

            gapEnd += charCount;
            version++;
        }

        /// <summary>
        /// Replaces the entire buffer contents with the given text — the single wholesale-replace
        /// implementation. Returns the minimal <see cref="EditShape"/> of the change (common prefix
        /// and suffix excluded, never cutting a surrogate pair), so callers can rebase indexed
        /// state instead of treating the swap as replace-everything.
        /// </summary>
        /// <param name="text">New text content.</param>
        internal EditShape SetText(ReadOnlySpan<char> text)
        {
            var shape = ComputeReplaceShape(text);

            if (buffer.Length - text.Length < MinGapSize)
                buffer = new char[ChooseCapacity(buffer.Length, text.Length)];

            text.CopyTo(buffer.AsSpan());
            gapStart = text.Length;
            gapEnd = buffer.Length;
            gapCpBeforeGap = UnicodeData.CountCodepoints(text);
            version++;
            return shape;
        }

        /// <summary>
        /// Codepoint-space shape of replacing the current content with <paramref name="text"/>,
        /// with the common prefix and suffix factored out. Prefix/suffix ends are pulled back off
        /// surrogate-pair interiors so the shape always covers whole codepoints.
        /// </summary>
        private EditShape ComputeReplaceShape(ReadOnlySpan<char> text)
        {
            var oldLen = Length;

            var prefix = 0;
            var prefixBound = Math.Min(oldLen, text.Length);
            while (prefix < prefixBound && this[prefix] == text[prefix])
                prefix++;
            if (prefix > 0 && char.IsHighSurrogate(text[prefix - 1]))
                prefix--;

            var suffix = 0;
            var suffixBound = Math.Min(oldLen, text.Length) - prefix;
            while (suffix < suffixBound && this[oldLen - 1 - suffix] == text[text.Length - 1 - suffix])
                suffix++;
            if (suffix > 0 && char.IsLowSurrogate(text[text.Length - suffix]))
                suffix--;

            var removedChars = oldLen - prefix - suffix;
            var insertedChars = text.Length - prefix - suffix;
            var startCp = CharToCodepointIndex(prefix);
            var removedCp = removedChars > 0 ? CountCodepoints(prefix, removedChars) : 0;
            var insertedCp = insertedChars > 0 ? UnicodeData.CountCodepoints(text.Slice(prefix, insertedChars)) : 0;
            return new EditShape(startCp, removedCp, insertedCp);
        }

        /// <summary>
        /// Clears all text, resetting the gap to span the entire buffer.
        /// </summary>
        internal void Clear()
        {
            gapStart = 0;
            gapEnd = buffer.Length;
            gapCpBeforeGap = 0;
            version++;
        }

        /// <summary>
        /// Inserts text at the specified codepoint index.
        /// </summary>
        /// <param name="codepointIndex">Codepoint index (0-based). Surrogate pairs count as 1 codepoint.</param>
        /// <param name="text">The text to insert.</param>
        internal void InsertAtCodepoint(int codepointIndex, ReadOnlySpan<char> text)
        {
            var charIndex = CodepointToCharIndex(codepointIndex);
            Insert(charIndex, text);
        }

        /// <summary>
        /// Deletes codepoints starting at the specified codepoint index.
        /// </summary>
        /// <param name="codepointIndex">Starting codepoint index.</param>
        /// <param name="codepointCount">Number of codepoints to delete.</param>
        internal void DeleteAtCodepoint(int codepointIndex, int codepointCount)
        {
            if (codepointCount == 0) return;

            var charStart = CodepointToCharIndex(codepointIndex);
            var charEnd = CodepointToCharIndex(codepointIndex + codepointCount);
            Delete(charStart, charEnd - charStart);
        }

        /// <summary>
        /// Extracts the text corresponding to a range of codepoints as a string.
        /// </summary>
        /// <param name="codepointIndex">Starting codepoint index.</param>
        /// <param name="codepointCount">Number of codepoints to extract.</param>
        /// <returns>A new string containing the specified codepoint range.</returns>
        /// <remarks>
        /// Allocates a string. For hot paths (delete, transpose), prefer
        /// <see cref="CopyCodepointRange"/> with a stack-allocated destination.
        /// </remarks>
        internal string GetCodepointRange(int codepointIndex, int codepointCount)
        {
            if (codepointCount == 0) return string.Empty;

            var charStart = CodepointToCharIndex(codepointIndex);
            var charEnd = CodepointToCharIndex(codepointIndex + codepointCount);
            var charCount = charEnd - charStart;

            return string.Create(charCount, (self: this, charStart, charCount), static (span, state) =>
            {
                state.self.CopyTo(state.charStart, state.charCount, span);
            });
        }

        /// <summary>
        /// Copies the text of a codepoint range into a destination span without allocation.
        /// </summary>
        /// <param name="codepointIndex">Starting codepoint index.</param>
        /// <param name="codepointCount">Number of codepoints to copy.</param>
        /// <param name="destination">
        /// Destination span. Must have capacity for at least
        /// <paramref name="codepointCount"/> * 2 chars (worst case: all surrogate pairs).
        /// </param>
        /// <returns>The number of chars actually written to <paramref name="destination"/>.</returns>
        internal int CopyCodepointRange(int codepointIndex, int codepointCount, Span<char> destination)
        {
            if (codepointCount == 0) return 0;

            var charStart = CodepointToCharIndex(codepointIndex);
            var charEnd = CodepointToCharIndex(codepointIndex + codepointCount);
            var charCount = charEnd - charStart;

            return CopyTo(charStart, charCount, destination);
        }

        /// <summary>
        /// Total number of codepoints in the buffer. Surrogate pairs count as 1 codepoint.
        /// </summary>
        /// <remarks>
        /// Cached — O(n) on first call after mutation, O(1) on subsequent calls.
        /// </remarks>
        public int CodepointCount
        {
            get
            {
                if (cachedCodepointVersion == version)
                    return cachedCodepointCount;

                cachedCodepointCount = CountCodepointsInBuffer();
                cachedCodepointVersion = version;
                return cachedCodepointCount;
            }
        }

        /// <summary>
        /// Converts a codepoint index to a logical char index.
        /// </summary>
        /// <param name="codepointIndex">
        /// Codepoint index in [0, CodepointCount]. Passing CodepointCount returns Length.
        /// </param>
        /// <returns>The corresponding logical char index.</returns>
        /// <remarks>
        /// Resumes from the nearest of the last conversion, the gap, or index 0, keeping
        /// conversion cost proportional to the distance from the nearest valid anchor.
        /// </remarks>
        public int CodepointToCharIndex(int codepointIndex)
        {
            if (codepointIndex == 0) return 0;
            if (codepointIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(codepointIndex));

            var anchorCp = 0;
            var anchorChar = 0;
            if (gapCpBeforeGap <= codepointIndex)
            {
                anchorCp = gapCpBeforeGap;
                anchorChar = gapStart;
            }
            if (cpCacheVersion == version && cpCacheCpIdx <= codepointIndex && cpCacheCpIdx > anchorCp)
            {
                anchorCp = cpCacheCpIdx;
                anchorChar = cpCacheCharIdx;
            }

            if (codepointIndex < gapCpBeforeGap && gapCpBeforeGap - codepointIndex < codepointIndex - anchorCp)
            {
                var charIdx = gapStart;
                for (var cpIdx = gapCpBeforeGap; cpIdx > codepointIndex; cpIdx--)
                {
                    charIdx--;
                    if (charIdx > 0 && char.IsLowSurrogate(buffer[charIdx]) && char.IsHighSurrogate(buffer[charIdx - 1]))
                        charIdx--;
                }
                UpdateCpCache(codepointIndex, charIdx);
                return charIdx;
            }

            var remaining = codepointIndex - anchorCp;
            var forwardChar = anchorChar;
            if (remaining == 0) return forwardChar;

            while (forwardChar < gapStart && remaining > 0)
            {
                forwardChar += IsPairAt(forwardChar, gapStart) ? 2 : 1;
                remaining--;
            }

            if (remaining == 0)
            {
                UpdateCpCache(codepointIndex, forwardChar);
                return forwardChar;
            }

            var physIdx = forwardChar >= gapStart
                ? forwardChar + GapSize
                : gapEnd;
            while (physIdx < buffer.Length && remaining > 0)
            {
                physIdx += IsPairAt(physIdx, buffer.Length) ? 2 : 1;
                remaining--;
            }

            if (remaining > 0)
                throw new ArgumentOutOfRangeException(nameof(codepointIndex));

            var logicalResult = physIdx - GapSize;
            UpdateCpCache(codepointIndex, logicalResult);
            return logicalResult;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateCpCache(int cpIdx, int charIdx)
        {
            cpCacheCpIdx = cpIdx;
            cpCacheCharIdx = charIdx;
            cpCacheVersion = version;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsPairAt(int index, int bound)
            => char.IsHighSurrogate(buffer[index]) && index + 1 < bound && char.IsLowSurrogate(buffer[index + 1]);

        private bool IsMidPair(int charPosition)
            => charPosition > 0 && charPosition < Length
               && char.IsHighSurrogate(this[charPosition - 1]) && char.IsLowSurrogate(this[charPosition]);

        /// <summary>Clamps to <c>[0, Length]</c> and snaps an interior surrogate boundary backward.</summary>
        internal int SnapToPairBoundary(int charPosition)
        {
            if (charPosition <= 0) return 0;
            var len = Length;
            if (charPosition >= len) return len;
            return IsMidPair(charPosition) ? charPosition - 1 : charPosition;
        }

        /// <summary>
        /// Converts a logical char index to a codepoint index.
        /// </summary>
        /// <param name="charIndex">Logical char index in [0, Length].</param>
        /// <returns>The corresponding codepoint index.</returns>
        /// <remarks>
        /// Shares the resume anchors with <see cref="CodepointToCharIndex"/>: the last conversion,
        /// the gap (backward-capable), and index 0 — whichever is nearest. Sequential access
        /// (ascending tag ranges during a coordinate-map rebuild) and caret-adjacent IME queries
        /// both stay O(distance).
        /// </remarks>
        public int CharToCodepointIndex(int charIndex)
        {
            var len = Length;
            if ((uint)charIndex > (uint)len)
                throw new ArgumentOutOfRangeException(nameof(charIndex));
            if (charIndex == 0) return 0;
            if (charIndex == gapStart) return gapCpBeforeGap;

            var anchorCp = 0;
            var anchorChar = 0;
            if (gapStart <= charIndex)
            {
                anchorCp = gapCpBeforeGap;
                anchorChar = gapStart;
            }
            if (cpCacheVersion == version && cpCacheCharIdx <= charIndex && cpCacheCharIdx > anchorChar)
            {
                if (charIndex == cpCacheCharIdx) return cpCacheCpIdx;
                anchorCp = cpCacheCpIdx;
                anchorChar = cpCacheCharIdx;
            }

            if (charIndex < gapStart && gapStart - charIndex < charIndex - anchorChar)
            {
                var cpCount = gapCpBeforeGap - CountCodepoints(charIndex, gapStart - charIndex);
                UpdateCpCache(cpCount, charIndex);
                return cpCount;
            }

            var count = anchorCp;
            var charIdx = anchorChar;

            var beforeTarget = Math.Min(charIndex, gapStart);
            while (charIdx < beforeTarget)
            {
                charIdx += IsPairAt(charIdx, gapStart) ? 2 : 1;
                count++;
            }

            if (charIdx >= charIndex)
            {
                UpdateCpCache(count, charIdx);
                return count;
            }

            var physIdx = charIdx + GapSize;
            var physTarget = charIndex + GapSize;
            while (physIdx < physTarget)
            {
                physIdx += IsPairAt(physIdx, buffer.Length) ? 2 : 1;
                count++;
            }

            UpdateCpCache(count, physIdx - GapSize);
            return count;
        }

        /// <summary>
        /// Counts codepoints in a logical char range.
        /// </summary>
        /// <param name="charStart">Starting logical char index.</param>
        /// <param name="charCount">Number of chars in the range.</param>
        /// <returns>Number of codepoints (surrogate pair = 1).</returns>
        public int CountCodepoints(int charStart, int charCount)
        {
            if (charCount == 0) return 0;

            var len = Length;
            if (charStart < 0 || charStart >= len)
                throw new ArgumentOutOfRangeException(nameof(charStart));
            if (charCount < 0 || charStart + charCount > len)
                throw new ArgumentOutOfRangeException(nameof(charCount));

            var cpCount = 0;
            var endLogical = charStart + charCount;
            var logicalIdx = charStart;

            if (logicalIdx < gapStart)
            {
                var beforeEnd = Math.Min(endLogical, gapStart);
                while (logicalIdx < beforeEnd)
                {
                    logicalIdx += IsPairAt(logicalIdx, gapStart) ? 2 : 1;
                    cpCount++;
                }
            }

            if (logicalIdx < endLogical)
            {
                var physIdx = logicalIdx + GapSize;
                var physEnd = endLogical + GapSize;
                while (physIdx < physEnd)
                {
                    physIdx += IsPairAt(physIdx, buffer.Length) ? 2 : 1;
                    cpCount++;
                }
            }

            return cpCount;
        }

        /// <summary>
        /// Moves the gap to the specified logical char position.
        /// </summary>
        /// <param name="charPosition">Target logical char position for the gap start.</param>
        /// <remarks>
        /// O(|charPosition - gapStart|). Surrogate-safe: never splits a surrogate pair.
        /// </remarks>
        internal void MoveGap(int charPosition)
        {
            if (charPosition == gapStart) return;

            if (charPosition < gapStart)
            {
                if (charPosition > 0 &&
                    char.IsHighSurrogate(buffer[charPosition - 1]) &&
                    char.IsLowSurrogate(buffer[charPosition]))
                {
                    charPosition--;
                }
            }
            else
            {
                var newGapEndPhys = gapEnd + (charPosition - gapStart);
                if (newGapEndPhys > gapEnd && newGapEndPhys < buffer.Length &&
                    char.IsHighSurrogate(buffer[newGapEndPhys - 1]) &&
                    char.IsLowSurrogate(buffer[newGapEndPhys]))
                {
                    charPosition++;
                }
            }

            if (charPosition == gapStart) return;

            if (charPosition < gapStart)
            {
                var delta = gapStart - charPosition;
                gapCpBeforeGap -= CountCodepoints(charPosition, delta);
                Array.Copy(buffer, charPosition, buffer, gapEnd - delta, delta);
                gapStart = charPosition;
                gapEnd -= delta;
            }
            else
            {
                var delta = charPosition - gapStart;
                gapCpBeforeGap += CountCodepoints(gapStart, delta);
                Array.Copy(buffer, gapEnd, buffer, gapStart, delta);
                gapStart += delta;
                gapEnd += delta;
            }
        }

        /// <summary>
        /// Ensures the buffer can hold at least <paramref name="needed"/> logical chars.
        /// </summary>
        /// <param name="needed">Required logical capacity (chars excluding gap).</param>
        /// <remarks>
        /// If growth is needed, the buffer doubles in size. The gap after growth is
        /// max(64, newCapacity / 8).
        /// </remarks>
        internal void EnsureCapacity(int needed)
        {
            var gapSize = GapSize;
            var currentLogical = buffer.Length - gapSize;

            var additionalNeeded = needed - currentLogical;
            if (additionalNeeded <= gapSize) return;

            Grow(ChooseCapacity(buffer.Length, needed));
        }

        /// <summary>
        /// The one capacity rule for every grow path (mutation grow and wholesale replace):
        /// doubles from <paramref name="current"/> until <paramref name="needed"/> logical chars
        /// fit alongside the post-grow gap floor <c>max(64, capacity/8)</c>.
        /// </summary>
        private static int ChooseCapacity(int current, int needed)
        {
            var newCapacity = Math.Max(current, DefaultCapacity);
            while (true)
            {
                newCapacity *= 2;
                var minGap = Math.Max(MinGapSize, newCapacity / 8);
                if (newCapacity - minGap >= needed) return newCapacity;
            }
        }

        private void Grow(int newCapacity)
        {
            var newBuffer = new char[newCapacity];
            var afterGapLength = buffer.Length - gapEnd;

            if (gapStart > 0)
                Array.Copy(buffer, 0, newBuffer, 0, gapStart);

            var newGapEnd = newCapacity - afterGapLength;
            if (afterGapLength > 0)
                Array.Copy(buffer, gapEnd, newBuffer, newGapEnd, afterGapLength);

            buffer = newBuffer;
            gapEnd = newGapEnd;
        }

        /// <summary>
        /// Counts all codepoints across both sides of the gap.
        /// </summary>
        private int CountCodepointsInBuffer()
        {
            var count = 0;

            for (var i = 0; i < gapStart; i++)
            {
                if (IsPairAt(i, gapStart)) i++;
                count++;
            }

            for (var i = gapEnd; i < buffer.Length; i++)
            {
                if (IsPairAt(i, buffer.Length)) i++;
                count++;
            }

            return count;
        }
    }
}
