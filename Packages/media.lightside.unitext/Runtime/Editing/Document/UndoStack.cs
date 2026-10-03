using System;
using System.Collections.Generic;

namespace LightSide
{
    internal delegate bool UndoSplice(int index, ReadOnlySpan<char> remove, ReadOnlySpan<char> add, out EditShape shape);
    internal delegate bool UndoAttributedSplice(int index, ReadOnlySpan<char> remove, ReadOnlySpan<char> add,
        AttributedDocumentState state, out EditShape shape);

    /// <summary>
    /// Specifies the type of text editing operation recorded in the undo stack.
    /// </summary>
    internal enum EditOpType : byte
    {
        /// <summary>Text was inserted at a position.</summary>
        Insert,

        /// <summary>Text was deleted from a position.</summary>
        Delete,

        /// <summary>Selected text was replaced with new text (atomic delete + insert).</summary>
        Replace,

        Attributed,

    }


    /// <summary>
    /// Command-pattern undo/redo stack for text editing operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All recorded text is stored in a shared contiguous <c>char[]</c> buffer;
    /// entries reference <c>(offset, length)</c> ranges in it instead of allocating strings. Entries that
    /// change attribution additionally retain immutable span states, so replay never depends on reparsing.
    /// Coalescing relies on the invariant that a coalesce target's slice always ends exactly
    /// at the buffer's used mark — any append that does not become (part of) the last entry
    /// breaks it. Total stored text is capped by <see cref="UniTextSettings.UndoMemoryLimitBytes"/>
    /// (oldest entries drop first); stranded buffer space left by redo truncation and
    /// backward-delete relocation is reclaimed by compaction once it exceeds half the buffer.
    /// </para>
    /// <para>
    /// Sequential operations of the same type and token class within the coalesce timeout
    /// (<see cref="UniTextSettings.UndoCoalesceTimeout"/>) are merged into a single undo step.
    /// Replace operations never coalesce. Multi-entry commands wrap their edits in
    /// <see cref="BeginGroup"/> / <see cref="EndGroup"/> so undo/redo replays them as one step.
    /// </para>
    /// </remarks>
    internal sealed class UndoStack
    {
        /// <summary>
        /// Coalescing classification of an entry's text, decided per CODEPOINT (astral punctuation
        /// classifies whole, not by surrogate halves): all word codepoints, all separators
        /// (whitespace / punctuation), or a mix. Same-class runs merge — typing inside a word
        /// extends one entry, holding the spacebar extends one entry — while a word↔separator
        /// transition starts a new group (the VS Code / CodeMirror 6 boundary rule).
        /// </summary>
        private readonly struct CoalescingEdit
        {
            public readonly EditOpType operation;
            public readonly int index;
            public readonly int caretAfter;
            public readonly int removedCodepoints;
            public readonly Utf16TokenClass tokenClass;

            public CoalescingEdit(int index, int caretAfter, ReadOnlySpan<char> removed, ReadOnlySpan<char> added)
            {
                operation = OperationOf(removed.Length, added.Length);
                this.index = index;
                this.caretAfter = caretAfter;
                removedCodepoints = UnicodeData.CountCodepoints(removed);
                tokenClass = operation == EditOpType.Insert
                    ? Utf16.ClassifyToken(added)
                    : operation == EditOpType.Delete
                        ? Utf16.ClassifyToken(removed)
                        : Utf16TokenClass.Mixed;
            }
        }

        /// <summary>
        /// A single undo/redo record referencing text stored in the shared char buffer.
        /// </summary>
        private struct UndoEntry
        {
            /// <summary>The type of edit operation.</summary>
            public EditOpType type;

            /// <summary>Codepoint index where the operation occurred.</summary>
            public int codepointIndex;

            /// <summary>Buffer slice of the text this edit ADDED to the document (insert / replace's new text; empty for delete).</summary>
            public int addedOffset;
            public int addedLength;

            /// <summary>Buffer slice of the text this edit REMOVED from the document (delete / replace's old text; empty for insert).</summary>
            public int removedOffset;
            public int removedLength;

            public TextSelection selectionBefore;
            public int caretAfter;

            /// <summary>Timestamp from the stack's clock when recorded.</summary>
            public float timestamp;

            /// <summary>Coalescing class of the entry's text.</summary>
            public Utf16TokenClass tokenClass;

            /// <summary>Transaction id shared by entries recorded inside one <see cref="BeginGroup"/> /
            /// <see cref="EndGroup"/> pair; 0 = ungrouped. A whole group replays as one undo/redo step.</summary>
            public int groupId;

            public AttributedDocumentState stateBefore;
            public AttributedDocumentState stateAfter;
            public int stateBytes;
            public bool hasPresentationRestore;
            public TextSelection presentationSelectionBefore;
            public int presentationCaretAfter;
            public EditOpType coalescingOperation;
            public int coalescingIndex;
            public int coalescingCaretAfter;
            public int coalescingRemovedCodepoints;
            public int coalescingEpoch;
            public bool groupCoalesces;
        }

        private const int InitialCharBufferCapacity = 256;
        private const int InitialEntryCapacity = 32;
        private const int CompactionMinChars = 1024;

        /// <summary>Backward-delete coalescing rewrites the whole accumulated slice per keystroke
        /// (document order must be kept); past this size a new entry starts, keeping the per-keystroke
        /// copy bounded instead of O(run length).</summary>
        private const int BackwardDeleteCoalesceCapChars = 4096;

        private static readonly CatZone log = Cat.Zone("Editing");
        private static readonly Func<float> defaultClock = static () => UnityEngine.Time.realtimeSinceStartup;

        private readonly List<UndoEntry> entries;
        private readonly Func<float> clock;
        private readonly int memoryLimitBytesOverride;
        private int undoPointer;
        private char[] charBuffer;
        private int charBufferUsed;
        private int liveChars;
        private int liveStateBytes;
        private int groupDepth;
        private int activeGroupId;
        private int lastGroupId;
        private float activeGroupTimestamp;
        private bool activeGroupCoalesces;
        private int coalescingEpoch;

        /// <summary>
        /// Creates a new <see cref="UndoStack"/>. <paramref name="clock"/> supplies timestamps
        /// for coalescing (seconds, monotonic); <see langword="null"/> uses
        /// <c>Time.realtimeSinceStartup</c> — inject a fake for headless tests.
        /// </summary>
        /// <param name="memoryLimitBytesOverride">Non-negative test override; negative uses the project setting.</param>
        public UndoStack(Func<float> clock = null, int memoryLimitBytesOverride = -1)
        {
            this.clock = clock ?? defaultClock;
            this.memoryLimitBytesOverride = memoryLimitBytesOverride;
            entries = new List<UndoEntry>(InitialEntryCapacity);
            charBuffer = new char[InitialCharBufferCapacity];
        }

        /// <summary>Gets whether an undo operation is available.</summary>
        public bool CanUndo => undoPointer > 0;

        /// <summary>Gets whether a redo operation is available.</summary>
        public bool CanRedo => undoPointer < entries.Count;

        /// <summary>
        /// Opens an undo transaction: entries recorded until the matching <see cref="EndGroup"/>
        /// share a group id and <see cref="Undo"/> / <see cref="Redo"/> replay the whole group as
        /// one step. Nest-counted — only the outermost pair delimits the group. Used for
        /// insert + pending-typing-style wrap and IME compose-over-selection.
        /// </summary>
        public void BeginGroup()
        {
            BeginGroup(false);
        }

        /// <summary>When <paramref name="coalesces"/> is true, adjacent compatible transactions share one undo step without merging their replay data.</summary>
        public void BeginGroup(bool coalesces)
        {
            if (groupDepth++ == 0)
            {
                activeGroupId = ++lastGroupId;
                activeGroupTimestamp = clock();
                activeGroupCoalesces = coalesces;
            }
        }

        /// <summary>Closes the innermost <see cref="BeginGroup"/>; the outermost close seals the group.</summary>
        public void EndGroup()
        {
            if (groupDepth == 0 || --groupDepth != 0) return;
            if (activeGroupCoalesces) CoalesceCompletedGroup();
            activeGroupId = 0;
            activeGroupCoalesces = false;
        }

        /// <summary>
        /// Appends text to the shared char buffer, growing geometrically if needed.
        /// </summary>
        /// <param name="text">The text to append.</param>
        /// <returns>The offset in <see cref="charBuffer"/> where the text was written.</returns>
        private int AppendToBuffer(ReadOnlySpan<char> text)
        {
            var offset = charBufferUsed;
            var required = charBufferUsed + text.Length;

            if (required > charBuffer.Length)
            {
                var newSize = Math.Max(charBuffer.Length * 2, required);
                Array.Resize(ref charBuffer, newSize);
            }

            text.CopyTo(charBuffer.AsSpan(charBufferUsed));
            charBufferUsed += text.Length;
            liveChars += text.Length;
            return offset;
        }

        /// <summary>
        /// Gets a read-only view of text stored in the shared buffer.
        /// </summary>
        /// <param name="offset">Start offset in <see cref="charBuffer"/>.</param>
        /// <param name="length">Number of chars to read.</param>
        /// <returns>A span over the requested buffer region.</returns>
        private ReadOnlySpan<char> GetBufferSlice(int offset, int length)
            => charBuffer.AsSpan(offset, length);

        /// <summary>
        /// Discards the redo branch and rolls the buffer's used mark back to the surviving
        /// entries' end, so the truncated entries' text is reclaimed instead of stranded.
        /// Must run before the new edit's text is appended.
        /// </summary>
        private void TruncateRedo()
        {
            if (undoPointer >= entries.Count) return;

            DropEntries(undoPointer, entries.Count - undoPointer);

            var used = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                used = Math.Max(used, Math.Max(e.addedOffset + e.addedLength, e.removedOffset + e.removedLength));
            }
            charBufferUsed = used;
        }

        /// <summary>Removes an entry range, releasing its chars from the live-memory accounting.</summary>
        private void DropEntries(int start, int count)
        {
            for (var i = start; i < start + count; i++)
            {
                liveChars -= entries[i].addedLength + entries[i].removedLength;
                liveStateBytes -= entries[i].stateBytes;
            }
            entries.RemoveRange(start, count);
        }

        private void Push(UndoEntry entry)
        {
            entries.Add(entry);
            liveStateBytes += entry.stateBytes;
            undoPointer = entries.Count;
        }

        /// <summary>
        /// Whether the newest entry can absorb the next edit: storage kind, effective operation,
        /// transaction, presentation space, token class, and timeout must all match.
        /// </summary>
        private bool TryGetCoalesceTarget(in UndoEntry next, out UndoEntry last)
        {
            if (undoPointer > 0 && undoPointer == entries.Count)
            {
                last = entries[undoPointer - 1];
                if (last.type == next.type
                    && last.coalescingOperation == next.coalescingOperation
                    && next.timestamp - last.timestamp <= UniTextSettings.UndoCoalesceTimeout
                    && last.groupId == next.groupId
                    && last.hasPresentationRestore == next.hasPresentationRestore
                    && last.coalescingEpoch == next.coalescingEpoch
                    && next.tokenClass != Utf16TokenClass.Mixed && last.tokenClass == next.tokenClass)
                    return true;
            }
            last = default;
            return false;
        }

        private static EditOpType OperationOf(int removedLength, int addedLength)
        {
            if (removedLength == 0 && addedLength > 0) return EditOpType.Insert;
            if (addedLength == 0 && removedLength > 0) return EditOpType.Delete;
            return EditOpType.Replace;
        }

        private void Record(UndoEntry entry, ReadOnlySpan<char> removed, ReadOnlySpan<char> added,
            CoalescingEdit? authoredEdit = null)
        {
            entry.removedLength = removed.Length;
            entry.addedLength = added.Length;
            entry.timestamp = groupDepth > 0 ? activeGroupTimestamp : clock();
            entry.groupId = activeGroupId;
            entry.groupCoalesces = activeGroupCoalesces;
            entry.stateBytes = (entry.stateBefore?.EstimatedBytes ?? 0) + (entry.stateAfter?.EstimatedBytes ?? 0);

            var edit = authoredEdit ?? new CoalescingEdit(entry.codepointIndex, entry.caretAfter, removed, added);
            entry.coalescingOperation = edit.operation;
            entry.coalescingIndex = edit.index;
            entry.coalescingCaretAfter = edit.caretAfter;
            entry.coalescingRemovedCodepoints = edit.removedCodepoints;
            entry.coalescingEpoch = coalescingEpoch;
            entry.tokenClass = edit.tokenClass;

            if (!authoredEdit.HasValue && TryCoalesce(in entry, removed, added)) return;

            TruncateRedo();
            entry.removedOffset = AppendToBuffer(removed);
            entry.addedOffset = AppendToBuffer(added);
            Push(entry);
            EnforceMemoryLimit();
        }

        private bool TryCoalesce(in UndoEntry next, ReadOnlySpan<char> removed, ReadOnlySpan<char> added)
        {
            if (!TryGetCoalesceTarget(in next, out var last)) return false;

            if (next.coalescingOperation == EditOpType.Insert)
            {
                if (next.codepointIndex != last.caretAfter) return false;
                var offset = AppendToBuffer(added);
                System.Diagnostics.Debug.Assert(offset == last.addedOffset + last.addedLength,
                    "UndoStack: coalesce target's added slice must end at the buffer's used mark");
                last.addedLength += added.Length;
            }
            else if (next.coalescingOperation == EditOpType.Delete)
            {
                var removedCodepoints = UnicodeData.CountCodepoints(removed);
                if (next.codepointIndex == last.codepointIndex)
                {
                    var offset = AppendToBuffer(removed);
                    System.Diagnostics.Debug.Assert(offset == last.removedOffset + last.removedLength,
                        "UndoStack: coalesce target's removed slice must end at the buffer's used mark");
                    last.removedLength += removed.Length;
                }
                else if (next.codepointIndex == last.codepointIndex - removedCodepoints
                         && last.removedLength + removed.Length <= BackwardDeleteCoalesceCapChars)
                {
                    CoalesceBackwardDelete(ref last, removed, next.codepointIndex, next.timestamp);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            last.caretAfter = next.caretAfter;
            last.coalescingCaretAfter = next.coalescingCaretAfter;
            last.timestamp = next.timestamp;
            if (last.type == EditOpType.Attributed)
            {
                liveStateBytes -= last.stateBytes;
                last.stateAfter = next.stateAfter;
                last.stateBytes = (last.stateBefore?.EstimatedBytes ?? 0) + (last.stateAfter?.EstimatedBytes ?? 0);
                liveStateBytes += last.stateBytes;
                if (last.hasPresentationRestore)
                    last.presentationCaretAfter = next.presentationCaretAfter;
            }

            entries[undoPointer - 1] = last;
            EnforceMemoryLimit();
            return true;
        }

        private void CoalesceCompletedGroup()
        {
            if (entries.Count == 0 || entries[entries.Count - 1].groupId != activeGroupId) return;
            var currentStart = entries.Count - 1;
            while (currentStart > 0 && entries[currentStart - 1].groupId == activeGroupId)
                currentStart--;
            if (currentStart <= 0 || currentStart >= entries.Count) return;
            var currentAnchor = FindCoalescingAnchor(currentStart, entries.Count);
            if (currentAnchor < 0) return;

            var previousEnd = currentStart;
            var previousGroupId = entries[previousEnd - 1].groupId;
            var previousStart = previousEnd - 1;
            if (previousGroupId != 0)
                while (previousStart > 0 && entries[previousStart - 1].groupId == previousGroupId)
                    previousStart--;

            var previousAnchor = FindCoalescingAnchor(previousStart, previousEnd, fromEnd: true);
            if (previousAnchor < 0 || !CanCoalesce(entries[previousAnchor], entries[currentAnchor])) return;

            var targetGroupId = previousGroupId;
            if (targetGroupId == 0)
            {
                targetGroupId = ++lastGroupId;
                var previous = entries[previousStart];
                previous.groupId = targetGroupId;
                previous.groupCoalesces = true;
                entries[previousStart] = previous;
            }

            for (var i = currentStart; i < entries.Count; i++)
            {
                var entry = entries[i];
                entry.groupId = targetGroupId;
                entry.groupCoalesces = true;
                entries[i] = entry;
            }
        }

        private int FindCoalescingAnchor(int start, int end, bool fromEnd = false)
        {
            if (fromEnd)
            {
                for (var i = end - 1; i >= start; i--)
                    if (entries[i].coalescingOperation != EditOpType.Replace
                        && entries[i].tokenClass != Utf16TokenClass.Mixed)
                        return i;
                return -1;
            }

            for (var i = start; i < end; i++)
                if (entries[i].coalescingOperation != EditOpType.Replace
                    && entries[i].tokenClass != Utf16TokenClass.Mixed)
                    return i;
            return -1;
        }

        private static bool CanCoalesce(in UndoEntry previous, in UndoEntry current)
        {
            if (previous.groupId != 0 && !previous.groupCoalesces
                || previous.coalescingOperation != current.coalescingOperation
                || previous.coalescingEpoch != current.coalescingEpoch
                || current.timestamp - previous.timestamp > UniTextSettings.UndoCoalesceTimeout
                || previous.tokenClass != current.tokenClass)
                return false;

            if (current.coalescingOperation == EditOpType.Insert)
                return current.coalescingIndex == previous.coalescingCaretAfter;
            if (current.coalescingOperation != EditOpType.Delete) return false;
            return current.coalescingIndex == previous.coalescingIndex
                   || current.coalescingIndex == previous.coalescingIndex - current.coalescingRemovedCodepoints;
        }

        public void RecordInsert(int codepointIndex, ReadOnlySpan<char> text, TextSelection selectionBefore)
        {
            Record(new UndoEntry
            {
                type = EditOpType.Insert,
                codepointIndex = codepointIndex,
                selectionBefore = selectionBefore,
                caretAfter = codepointIndex + UnicodeData.CountCodepoints(text),
            }, ReadOnlySpan<char>.Empty, text);
        }

        /// <summary>
        /// Classifies text for the coalescing heuristic per CODEPOINT: whitespace and punctuation
        /// are separators, everything else (letters, digits, CJK ideographs, emoji, etc.) is word
        /// content. Astral codepoints classify whole via their Unicode category, never by surrogate
        /// halves.
        /// </summary>
        public void RecordDelete(int codepointIndex, ReadOnlySpan<char> deletedText, TextSelection selectionBefore)
        {
            Record(new UndoEntry
            {
                type = EditOpType.Delete,
                codepointIndex = codepointIndex,
                selectionBefore = selectionBefore,
                caretAfter = codepointIndex,
            }, deletedText, ReadOnlySpan<char>.Empty);
        }

        public void RecordAttributed(ReadOnlySpan<char> before, ReadOnlySpan<char> after,
            AttributedDocumentState stateBefore, AttributedDocumentState stateAfter,
            TextSelection selectionBefore, int caretAfter, TextSelection? presentationSelectionBefore = null,
            int presentationCaretAfter = -1)
            => RecordAttributedDiff(before, after, stateBefore, stateAfter, selectionBefore, caretAfter,
                presentationSelectionBefore, presentationCaretAfter, null);

        private void RecordAttributedDiff(ReadOnlySpan<char> before, ReadOnlySpan<char> after,
            AttributedDocumentState stateBefore, AttributedDocumentState stateAfter,
            TextSelection selectionBefore, int caretAfter, TextSelection? presentationSelectionBefore,
            int presentationCaretAfter, CoalescingEdit? authoredEdit)
        {
            Utf16.GetChangedRange(before, after, out var prefix,
                out var removedLength, out var addedLength);
            var removed = before.Slice(prefix, removedLength);
            var added = after.Slice(prefix, addedLength);
            RecordAttributed(UnicodeData.CountCodepoints(before.Slice(0, prefix)), removed, added,
                stateBefore, stateAfter, selectionBefore, caretAfter,
                presentationSelectionBefore, presentationCaretAfter, authoredEdit);
        }

        public void RecordAttributed(int codepointIndex, ReadOnlySpan<char> removed, ReadOnlySpan<char> added,
            AttributedDocumentState stateBefore, AttributedDocumentState stateAfter,
            TextSelection selectionBefore, int caretAfter, TextSelection? presentationSelectionBefore = null,
            int presentationCaretAfter = -1)
            => RecordAttributed(codepointIndex, removed, added, stateBefore, stateAfter, selectionBefore, caretAfter,
                presentationSelectionBefore, presentationCaretAfter, null);

        private void RecordAttributed(int codepointIndex, ReadOnlySpan<char> removed, ReadOnlySpan<char> added,
            AttributedDocumentState stateBefore, AttributedDocumentState stateAfter,
            TextSelection selectionBefore, int caretAfter, TextSelection? presentationSelectionBefore,
            int presentationCaretAfter, CoalescingEdit? authoredEdit)
        {
            Record(new UndoEntry
            {
                type = EditOpType.Attributed,
                codepointIndex = codepointIndex,
                selectionBefore = selectionBefore,
                caretAfter = caretAfter,
                stateBefore = stateBefore,
                stateAfter = stateAfter,
                hasPresentationRestore = presentationSelectionBefore.HasValue,
                presentationSelectionBefore = presentationSelectionBefore.GetValueOrDefault(),
                presentationCaretAfter = presentationCaretAfter,
            }, removed, added, authoredEdit);
        }

        public void RecordAttributedPresentation(ReadOnlySpan<char> before, ReadOnlySpan<char> after,
            AttributedDocumentState stateBefore, AttributedDocumentState stateAfter,
            TextSelection selectionBefore, int caretAfter, TextSelection presentationSelectionBefore,
            int presentationCaretAfter, int presentationEditIndex, ReadOnlySpan<char> presentationRemoved,
            ReadOnlySpan<char> presentationAdded)
        {
            var authoredEdit = new CoalescingEdit(presentationEditIndex, presentationCaretAfter,
                presentationRemoved, presentationAdded);
            RecordAttributedDiff(before, after, stateBefore, stateAfter, selectionBefore, caretAfter,
                presentationSelectionBefore, presentationCaretAfter, authoredEdit);
        }

        /// <summary>
        /// Coalesces a backward delete (backspace) with the previous delete entry.
        /// The newly deleted text precedes the existing deleted text in document order,
        /// so the combined region is rewritten at the buffer tail to keep document order
        /// (the old region is stranded until compaction). The group keeps the FIRST
        /// backspace's <c>selectionBefore</c> — undo restores the caret to where the run
        /// started — while position and caret track the newest deletion.
        /// </summary>
        private void CoalesceBackwardDelete(
            ref UndoEntry last,
            ReadOnlySpan<char> newText,
            int newCodepointIndex,
            float now)
        {
            var existingLength = last.removedLength;

            var newOffset = AppendToBuffer(newText);
            AppendToBuffer(GetBufferSlice(last.removedOffset, existingLength));
            liveChars -= existingLength;

            last.removedOffset = newOffset;
            last.removedLength = newText.Length + existingLength;
            last.codepointIndex = newCodepointIndex;
            last.caretAfter = newCodepointIndex;
            last.coalescingIndex = newCodepointIndex;
            last.coalescingCaretAfter = newCodepointIndex;
            last.timestamp = now;
        }

        /// <summary>Replace never coalesces.</summary>
        public void RecordReplace(
            int codepointIndex,
            ReadOnlySpan<char> deletedText,
            ReadOnlySpan<char> insertedText,
            TextSelection selectionBefore)
        {
            Record(new UndoEntry
            {
                type = EditOpType.Replace,
                codepointIndex = codepointIndex,
                selectionBefore = selectionBefore,
                caretAfter = codepointIndex + UnicodeData.CountCodepoints(insertedText),
            }, deletedText, insertedText);
        }

        /// <summary>
        /// Drops oldest entries while stored text plus attributed states exceed <see cref="UniTextSettings.UndoMemoryLimitBytes"/>
        /// (the newest entry always survives, even oversized), then compacts the shared buffer
        /// when stranded space passes half of it.
        /// </summary>
        private void EnforceMemoryLimit()
        {
            var limitBytes = memoryLimitBytesOverride >= 0
                ? memoryLimitBytesOverride
                : UniTextSettings.UndoMemoryLimitBytes;
            if (limitBytes > 0)
            {
                var dropCount = 0;
                var remainingBytes = liveChars * sizeof(char) + liveStateBytes;
                while (dropCount < entries.Count - 1 && remainingBytes > limitBytes)
                {
                    var e = entries[dropCount];
                    remainingBytes -= (e.addedLength + e.removedLength) * sizeof(char) + e.stateBytes;
                    dropCount++;
                }

                if (dropCount > 0)
                {
                    DropEntries(0, dropCount);
                    undoPointer = Math.Max(0, undoPointer - dropCount);
                }
            }

            if (charBufferUsed >= CompactionMinChars && charBufferUsed - liveChars > charBufferUsed / 2)
                Compact();
        }

        /// <summary>
        /// Rewrites all live slices into a right-sized buffer, reclaiming space stranded by
        /// redo truncation, backward-delete relocation, and dropped entries. Preserves the
        /// coalescing invariant: the last entry's slice ends at the used mark.
        /// </summary>
        private void Compact()
        {
            var capacity = InitialCharBufferCapacity;
            while (capacity < liveChars) capacity *= 2;

            var target = new char[capacity];
            var used = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.removedLength > 0)
                {
                    Array.Copy(charBuffer, e.removedOffset, target, used, e.removedLength);
                    e.removedOffset = used;
                    used += e.removedLength;
                }
                else e.removedOffset = 0;

                if (e.addedLength > 0)
                {
                    Array.Copy(charBuffer, e.addedOffset, target, used, e.addedLength);
                    e.addedOffset = used;
                    used += e.addedLength;
                }
                else e.addedOffset = 0;

                entries[i] = e;
            }

            charBuffer = target;
            charBufferUsed = used;
        }

        /// <summary>
        /// Forces the next edit to start a new undo group, preventing coalescing
        /// with any previous entry. Call this on cursor movement, paste, focus change,
        /// or IME composition boundary.
        /// </summary>
        public void BreakCoalescing()
        {
            coalescingEpoch++;
        }

        private ReadOnlySpan<char> Added(in UndoEntry e) => GetBufferSlice(e.addedOffset, e.addedLength);
        private ReadOnlySpan<char> Removed(in UndoEntry e) => GetBufferSlice(e.removedOffset, e.removedLength);

        /// <summary>
        /// The one validated mutation entry both directions reduce to: removes <paramref name="remove"/>'s worth
        /// of codepoints at <paramref name="index"/>, then inserts <paramref name="add"/>. Undo passes
        /// (added, removed), redo passes (removed, added) — insert and delete are this with one side empty.
        /// Throw-free: an entry whose range no longer fits the document is refused instead of splicing
        /// wrong text or throwing mid-keystroke.
        /// </summary>
        private static bool TrySplice(GapBuffer buffer, int index, ReadOnlySpan<char> remove, ReadOnlySpan<char> add, out EditShape shape)
        {
            shape = default;
            var docCp = buffer.CodepointCount;
            var removedCp = UnicodeData.CountCodepoints(remove);
            if (index < 0 || index > docCp || index + removedCp > docCp)
                return false;

            if (removedCp > 0) buffer.DeleteAtCodepoint(index, removedCp);
            if (add.Length > 0) buffer.InsertAtCodepoint(index, add);
            shape = new EditShape(index, removedCp, UnicodeData.CountCodepoints(add));
            return true;
        }

        /// <summary>
        /// A failed splice means the entry's indices were computed against a document state that
        /// no longer exists (history not cleared or rebased around an external mutation) — and its
        /// sequential-replay neighbours on the same side of the undo pointer are exactly as
        /// invalid, even when they happen to pass the bounds check. Both drops therefore discard
        /// the ENTIRE far side of the chain, mirroring <see cref="Rebase"/>'s semantics: the undo
        /// side drops the failed entry and everything older; the redo side drops the failed entry
        /// and everything newer (which also rolls the buffer's used mark back, preserving the
        /// coalescing invariant).
        /// </summary>
        private void DropInvalidUndoSide(int failedIndex)
        {
            DropEntries(0, failedIndex + 1);
            undoPointer = 0;
            log.MeowWarnFormat("[UndoStack] Dropped {0} out-of-bounds undo entries", failedIndex + 1);
        }

        private void DropInvalidRedoSide(int failedIndex)
        {
            var dropped = entries.Count - failedIndex;
            undoPointer = failedIndex;
            TruncateRedo();
            log.MeowWarnFormat("[UndoStack] Dropped {0} out-of-bounds redo entries", dropped);
        }

        /// <summary>
        /// Undoes the most recent edit — or, when it belongs to a transaction group, the whole
        /// group as one step — by removing what each entry added and restoring what it removed.
        /// Every splice's <see cref="EditShape"/> is reported through <paramref name="onShape"/>
        /// in application order so the caller patches derived indexed state per mutation.
        /// Returns the pre-edit selection of the earliest replayed entry. An entry that no longer
        /// fits the document is dropped together with everything older (see
        /// <see cref="DropInvalidUndoSide"/>); the return value still reflects any entries already
        /// replayed before the failure.
        /// </summary>
        public bool Undo(GapBuffer buffer, Action<EditShape> onShape, out TextSelection selectionAfter)
            => Undo((int index, ReadOnlySpan<char> remove, ReadOnlySpan<char> add, out EditShape shape)
                => TrySplice(buffer, index, remove, add, out shape), null, onShape, out selectionAfter);

        public bool Undo(UndoSplice splice, Action<EditShape> onShape, out TextSelection selectionAfter)
            => Undo(splice, null, onShape, out selectionAfter, out _);

        public bool Undo(UndoSplice splice, UndoAttributedSplice spliceAttributed,
            Action<EditShape> onShape, out TextSelection selectionAfter)
            => Undo(splice, spliceAttributed, onShape, out selectionAfter, out _);

        /// <summary>
        /// Attributed replay additionally returns an exact synthesized-view selection when the edit was
        /// authored in Raw or Reveal; the document selection remains the coordinate source of truth.
        /// </summary>
        public bool Undo(UndoSplice splice, UndoAttributedSplice spliceAttributed,
            Action<EditShape> onShape, out TextSelection selectionAfter,
            out TextSelection? presentationSelectionAfter)
        {
            selectionAfter = default;
            presentationSelectionAfter = null;
            if (undoPointer <= 0) return false;

            var groupId = entries[undoPointer - 1].groupId;
            var any = false;
            do
            {
                undoPointer--;
                var entry = entries[undoPointer];
                EditShape shape = default;
                var restored = entry.type == EditOpType.Attributed
                    ? spliceAttributed != null && spliceAttributed(entry.codepointIndex, Added(entry), Removed(entry),
                        entry.stateBefore, out shape)
                    : splice(entry.codepointIndex, Added(entry), Removed(entry), out shape);
                if (!restored)
                {
                    DropInvalidUndoSide(undoPointer);
                    return any;
                }
                onShape(shape);
                selectionAfter = entry.selectionBefore;
                if (entry.hasPresentationRestore)
                    presentationSelectionAfter = entry.presentationSelectionBefore;
                any = true;
            } while (groupId != 0 && undoPointer > 0 && entries[undoPointer - 1].groupId == groupId);
            return true;
        }

        /// <summary>
        /// Redoes the most recently undone edit — or its whole transaction group — by re-applying
        /// it to the gap buffer, reporting each splice's <see cref="EditShape"/> through
        /// <paramref name="onShape"/>. Returns the caret codepoint index of the last replayed
        /// entry. An entry that no longer fits the document is dropped together with everything
        /// newer (see <see cref="DropInvalidRedoSide"/>).
        /// </summary>
        public bool Redo(GapBuffer buffer, Action<EditShape> onShape, out int caretAfter)
            => Redo((int index, ReadOnlySpan<char> remove, ReadOnlySpan<char> add, out EditShape shape)
                => TrySplice(buffer, index, remove, add, out shape), null, onShape, out caretAfter);

        public bool Redo(UndoSplice splice, Action<EditShape> onShape, out int caretAfter)
            => Redo(splice, null, onShape, out caretAfter, out _);

        public bool Redo(UndoSplice splice, UndoAttributedSplice spliceAttributed,
            Action<EditShape> onShape, out int caretAfter)
            => Redo(splice, spliceAttributed, onShape, out caretAfter, out _);

        /// <summary>
        /// Attributed replay additionally returns the exact synthesized-view caret recorded after the edit.
        /// </summary>
        public bool Redo(UndoSplice splice, UndoAttributedSplice spliceAttributed,
            Action<EditShape> onShape, out int caretAfter, out int? presentationCaretAfter)
        {
            caretAfter = -1;
            presentationCaretAfter = null;
            if (undoPointer >= entries.Count) return false;

            var groupId = entries[undoPointer].groupId;
            var any = false;
            do
            {
                var entry = entries[undoPointer];
                EditShape shape = default;
                var restored = entry.type == EditOpType.Attributed
                    ? spliceAttributed != null && spliceAttributed(entry.codepointIndex, Removed(entry), Added(entry),
                        entry.stateAfter, out shape)
                    : splice(entry.codepointIndex, Removed(entry), Added(entry), out shape);
                if (!restored)
                {
                    DropInvalidRedoSide(undoPointer);
                    return any;
                }
                onShape(shape);
                undoPointer++;
                caretAfter = entry.caretAfter;
                if (entry.hasPresentationRestore)
                    presentationCaretAfter = entry.presentationCaretAfter;
                any = true;
            } while (groupId != 0 && undoPointer < entries.Count && entries[undoPointer].groupId == groupId);
            return true;
        }

        /// <summary>
        /// Rebases history positions through an edit that was NOT recorded here (network sync,
        /// programmatic replace with preserved history). Entries whose recorded text overlaps the
        /// replaced region cannot rebase and are dropped — together with everything on their far
        /// side of the undo pointer, keeping the replay chain contiguous. Ends the current
        /// coalescing group.
        /// </summary>
        public void Rebase(in EditShape shape)
        {
            if (entries.Count == 0) return;

            var dropOlderThan = 0;
            for (var i = undoPointer - 1; i >= 0; i--)
            {
                if (!TryRebaseEntry(i, shape, inDocument: true))
                {
                    dropOlderThan = i + 1;
                    break;
                }
            }

            var dropNewerFrom = entries.Count;
            for (var i = undoPointer; i < entries.Count; i++)
            {
                if (!TryRebaseEntry(i, shape, inDocument: false))
                {
                    dropNewerFrom = i;
                    break;
                }
            }

            if (dropNewerFrom < entries.Count)
                DropEntries(dropNewerFrom, entries.Count - dropNewerFrom);

            if (dropOlderThan > 0)
            {
                DropEntries(0, dropOlderThan);
                undoPointer -= dropOlderThan;
            }

            BreakCoalescing();
        }

        /// <summary>
        /// Rebases one entry through <paramref name="shape"/>. The span the entry occupies in the
        /// current document is its added text on the undo side (<paramref name="inDocument"/>) and
        /// its removed text on the redo side; an entry survives only when that span lies entirely
        /// outside the replaced region.
        /// </summary>
        private bool TryRebaseEntry(int index, in EditShape shape, bool inDocument)
        {
            var e = entries[index];
            if (e.type == EditOpType.Attributed) return false;
            var spanCp = inDocument
                ? UnicodeData.CountCodepoints(Added(e))
                : UnicodeData.CountCodepoints(Removed(e));

            var spanStart = e.codepointIndex;
            var spanEnd = spanStart + spanCp;
            var editEnd = shape.Start + shape.Removed;
            if (spanEnd > shape.Start && spanStart < editEnd)
                return false;

            e.codepointIndex = shape.MapIndex(e.codepointIndex);
            e.caretAfter = shape.MapIndex(e.caretAfter);
            e.selectionBefore = new TextSelection(
                shape.MapIndex(e.selectionBefore.Anchor),
                shape.MapIndex(e.selectionBefore.Focus),
                e.selectionBefore.Affinity);
            entries[index] = e;
            return true;
        }

        /// <summary>
        /// Resets the undo stack, discarding all history and freeing buffer space.
        /// </summary>
        public void Clear()
        {
            entries.Clear();
            undoPointer = 0;
            charBufferUsed = 0;
            liveChars = 0;
            liveStateBytes = 0;
        }
    }
}
