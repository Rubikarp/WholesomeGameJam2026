using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LightSide
{
    internal sealed class NativeFieldReplicaSession : INativeFieldIntentSink
    {
        private readonly UniTextEditable editor;
        private readonly int sessionId;
        private readonly CompositionClause[] compositionClauses = new CompositionClause[1];
        private readonly char[] compareBuffer = new char[256];
        private int authorityRevision = 1;
        private int lastNativeRevision;
        private bool active;
        private bool nativeCompositionActive;
        private int compositionStart;
        private int compositionLength;
        private int compositionBaseCharCount;
        private int compositionBaseVersion;
        private string compositionOriginalText;
        private bool forceProjection;
        private (object identity, int version) snapshotTextStamp;
        private int snapshotSelectionStart;
        private int snapshotSelectionEnd;
        private string presenterId;
        private NativeFieldShape shape;
        private int pendingActionRevision;
        private NativeInputReporter reporter;

        internal int SessionId => sessionId;
        internal bool IsActive => active;

        private NativeFieldReplicaSession(UniTextEditable editor, int sessionId)
        {
            this.editor = editor;
            this.sessionId = sessionId;
        }

        internal static NativeFieldReplicaSession Create(UniTextEditable editor, int sessionId)
            => new(editor, sessionId) { active = true };

        internal void Open(NativeInputReporter reporter,
            NativeKeyboardConfig config, bool passwordMode, in NativeFieldShape shape, bool readOnly,
            bool copyAllowed,
            in NativeFieldPresentationState presentation)
        {
            editor.GetCharSelection(out var selectionStart, out var selectionLength);
            var request = new NativeFieldOpenRequest(sessionId, authorityRevision,
                config, NativeInputSession.CaptureText(editor), selectionStart,
                selectionStart + selectionLength, passwordMode, in shape, readOnly, copyAllowed,
                in presentation);
            RecordSnapshot();
            var previous = this.reporter;
            this.reporter = reporter;
            try { UniTextNativeInput.OpenNativeField(reporter, in request); }
            catch
            {
                this.reporter = previous;
                throw;
            }
            presenterId = presentation.PresenterId;
            this.shape = shape;
        }

        internal bool RequiresRecreation(in NativeFieldShape next,
            in NativeFieldPresentationState presentation)
            => shape.MultiLineControl != next.MultiLineControl ||
               shape.ReturnKey != next.ReturnKey ||
               presenterId != presentation.PresenterId;

        internal void Reopen(NativeInputReporter reporter,
            NativeKeyboardConfig config, bool passwordMode, in NativeFieldShape next, bool readOnly,
            bool copyAllowed, in NativeFieldPresentationState presentation)
        {
            editor.GetCharSelection(out var selectionStart, out var selectionLength);
            var request = new NativeFieldOpenRequest(sessionId, authorityRevision,
                config, NativeInputSession.CaptureText(editor), selectionStart,
                selectionStart + selectionLength, passwordMode, in next, readOnly,
                copyAllowed, in presentation);
            RecordSnapshot();
            var previous = this.reporter;
            this.reporter = reporter;
            try { UniTextNativeInput.OpenNativeField(reporter, in request); }
            catch
            {
                this.reporter = previous;
                throw;
            }
            presenterId = presentation.PresenterId;
            shape = next;
        }

        internal void Focus(NativeInputReporter next)
        {
            var previous = reporter;
            reporter = next;
            try { UniTextNativeInput.FocusNativeField(sessionId, next); }
            catch
            {
                reporter = previous;
                throw;
            }
        }

        internal void Detach()
        {
            if (!active) return;
            active = false;
            pendingActionRevision = 0;
            reporter = null;
            ClearNativeCompositionState();
        }

        internal void Update(NativeKeyboardConfig config, bool passwordMode,
            in NativeFieldShape nextShape, bool readOnly, bool copyAllowed,
            in NativeFieldPresentationState next)
        {
            if (!active) return;
            if (RequiresRecreation(in nextShape, in next))
                throw new InvalidOperationException(
                    "Native field creation shape cannot be changed by an update.");
            var request = new NativeFieldUpdateRequest(sessionId, authorityRevision,
                config, passwordMode, in nextShape, readOnly, copyAllowed, in next);
            UniTextNativeInput.UpdateNativeField(in request);
            shape = nextShape;
        }

        internal bool InvalidateNativeComposition()
        {
            if (!active || !nativeCompositionActive) return false;
            ClearNativeCompositionState();
            AdvanceAuthorityRevision();
            forceProjection = true;
            return true;
        }

        internal void SyncExternal()
        {
            if (!active || pendingActionRevision != 0) return;
            GetSelection(out var selectionStart, out var selectionEnd);
            var textChanged = editor.TextStorageStamp != snapshotTextStamp;
            var selectionChanged = selectionStart != snapshotSelectionStart ||
                                   selectionEnd != snapshotSelectionEnd;
            if (!forceProjection && !textChanged && !selectionChanged) return;

            if (nativeCompositionActive)
            {
                ClearNativeCompositionState();
                if (editor.IsComposing) editor.HandleCompositionEnded();
                if (!active) return;
                GetSelection(out selectionStart, out selectionEnd);
                textChanged = true;
            }

            AdvanceAuthorityRevision();
            UniTextNativeInput.ReconcileNativeField(sessionId, -1, authorityRevision,
                textChanged || forceProjection ? NativeInputSession.CaptureText(editor) : null,
                selectionStart, selectionEnd);
            forceProjection = false;
            RecordSnapshot();
        }

        public void Apply(in NativeFieldEditIntent intent)
        {
            if (!Begin(intent.SessionId, intent.NativeRevision, intent.AuthorityRevision)) return;

            if (nativeCompositionActive)
            {
                if (intent.Start != compositionStart || intent.Length != compositionLength)
                {
                    Reject(intent.NativeRevision);
                    return;
                }

                var compositionExpectedLength = compositionBaseCharCount - compositionLength + intent.Text.Length;
                if (intent.SelectionStart > compositionExpectedLength ||
                    intent.SelectionEnd > compositionExpectedLength ||
                    !IsProposedEditBoundary(compositionStart, 0, intent.Text,
                        intent.SelectionStart, compositionExpectedLength) ||
                    !IsProposedEditBoundary(compositionStart, 0, intent.Text,
                        intent.SelectionEnd, compositionExpectedLength))
                {
                    Reject(intent.NativeRevision);
                    return;
                }
                var compositionSourceExact = editor.HandleTextInput(intent.Text);
                if (!active) return;
                ClearNativeCompositionState();
                var exact = compositionSourceExact && editor.CharCount == compositionExpectedLength &&
                            RangeEquals(compositionStart, intent.Text) &&
                            SelectionEquals(intent.SelectionStart, intent.SelectionEnd);
                Acknowledge(intent.NativeRevision, exact, true);
                return;
            }

            var beforeLength = editor.CharCount;
            if (!RangeIsValid(intent.Start, intent.Length, beforeLength) ||
                !editor.IsCharBoundary(intent.Start) ||
                !editor.IsCharBoundary(intent.Start + intent.Length))
            {
                Reject(intent.NativeRevision);
                return;
            }
            var expectedLength = beforeLength - intent.Length + intent.Text.Length;
            if (intent.SelectionStart > expectedLength || intent.SelectionEnd > expectedLength ||
                !IsProposedEditBoundary(intent.Start, intent.Length, intent.Text,
                    intent.SelectionStart, expectedLength) ||
                !IsProposedEditBoundary(intent.Start, intent.Length, intent.Text,
                    intent.SelectionEnd, expectedLength))
            {
                Reject(intent.NativeRevision);
                return;
            }

            var sourceExact = editor.HandleTextInput(intent.Text, intent.Start, intent.Length);
            if (!active) return;
            var accepted = sourceExact && editor.CharCount == expectedLength &&
                           RangeEquals(intent.Start, intent.Text) &&
                           SelectionEquals(intent.SelectionStart, intent.SelectionEnd);
            Acknowledge(intent.NativeRevision, accepted, true);
        }

        public void Apply(in NativeFieldCompositionIntent intent)
        {
            if (!Begin(intent.SessionId, intent.NativeRevision, intent.AuthorityRevision)) return;
            switch (intent.Phase)
            {
                case NativeFieldCompositionPhase.Update:
                    ApplyCompositionUpdate(in intent);
                    break;
                case NativeFieldCompositionPhase.End:
                    if (nativeCompositionActive)
                    {
                        Reject(intent.NativeRevision);
                        return;
                    }
                    editor.HandleCompositionEnded();
                    if (active) Acknowledge(intent.NativeRevision, true, true);
                    break;
                case NativeFieldCompositionPhase.Cancel:
                    ApplyCompositionCancel(in intent);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void Apply(in NativeFieldSelectionIntent intent)
        {
            if (!Begin(intent.SessionId, intent.NativeRevision, intent.AuthorityRevision)) return;
            if (intent.Start > editor.CharCount || intent.End > editor.CharCount ||
                !editor.IsCharBoundary(intent.Start) || !editor.IsCharBoundary(intent.End))
            {
                Reject(intent.NativeRevision);
                return;
            }

            editor.HandleSelectionInput(intent.Start, intent.End);
            if (!active) return;
            Acknowledge(intent.NativeRevision,
                SelectionEquals(intent.Start, intent.End), true);
        }

        public void Apply(in NativeFieldActionIntent intent)
        {
            if (!Begin(intent.SessionId, intent.NativeRevision, intent.AuthorityRevision)) return;
            if (nativeCompositionActive || editor.IsComposing)
            {
                Reject(intent.NativeRevision);
                return;
            }

            var textStamp = editor.TextStorageStamp;
            var nativeRevision = intent.NativeRevision;
            var source = reporter;
            pendingActionRevision = nativeRevision;
            var operation = NativeInputSession.ExecuteAction(editor, intent.Action, intent.Modifiers);
            if (!active) return;
            if (operation.IsCompleted)
            {
                CompleteAction(operation, source, nativeRevision, textStamp);
                return;
            }
            operation.GetAwaiter().OnCompleted(
                () => CompleteAction(operation, source, nativeRevision, textStamp));
        }

        public void Apply(in NativeInputQuiescedIntent intent)
        {
            if (!active || intent.SessionId != sessionId) return;
            if (!IsNextNativeRevision(intent.NativeRevision))
                throw new InvalidOperationException("A native input barrier must follow the acknowledged intent stream.");
            if (intent.AuthorityRevision != authorityRevision)
                throw new InvalidOperationException("A native input barrier must use the current authority revision.");
            lastNativeRevision = intent.NativeRevision;
            RecordSnapshot();
        }

        public void Fault(int faultSessionId, int nativeRevision)
        {
            if (!active || faultSessionId != sessionId) return;
            pendingActionRevision = 0;
            if (nativeRevision > lastNativeRevision) lastNativeRevision = nativeRevision;
            try
            {
                ClearNativeCompositionState();
                AdvanceAuthorityRevision();
                GetSelection(out var selectionStart, out var selectionEnd);
                UniTextNativeInput.ReconcileNativeField(sessionId, nativeRevision,
                    authorityRevision, NativeInputSession.CaptureText(editor),
                    selectionStart, selectionEnd);
                forceProjection = false;
                RecordSnapshot();
            }
            catch
            {
                try { NativeInputSession.HandleReplicaFault(editor, sessionId); }
                catch (Exception releaseError) { Debug.LogException(releaseError, editor); }
                throw;
            }
            NativeInputSession.HandleReplicaFault(editor, sessionId);
        }

        private void ApplyCompositionUpdate(in NativeFieldCompositionIntent intent)
        {
            var first = !nativeCompositionActive;
            if (first)
            {
                if (!RangeIsValid(intent.Start, intent.Length, editor.CharCount) ||
                    !editor.IsCharBoundary(intent.Start) ||
                    !editor.IsCharBoundary(intent.Start + intent.Length))
                {
                    Reject(intent.NativeRevision);
                    return;
                }
                compositionStart = intent.Start;
                compositionLength = intent.Length;
                compositionBaseCharCount = editor.CharCount;
                compositionBaseVersion = editor.Version;
                compositionOriginalText = NativeInputSession.CaptureRange(
                    editor, compositionStart, compositionLength);
                editor.HandleSelectionInput(compositionStart, compositionStart + compositionLength);
            }
            else if (intent.Start != compositionStart || intent.Length != compositionLength)
            {
                Reject(intent.NativeRevision);
                return;
            }
            if (intent.Cursor >= 0 &&
                (intent.Cursor > int.MaxValue - compositionStart ||
                 !IsStringCharBoundary(intent.Text, intent.Cursor)))
            {
                Reject(intent.NativeRevision);
                return;
            }

            var clause = compositionClauses[0];
            clause.startOffset = 0;
            clause.endOffset = intent.Text.Length;
            clause.style = CompositionClauseStyle.Unconverted;
            compositionClauses[0] = clause;
            var data = new CompositionData
            {
                text = intent.Text.AsSpan(),
                clauses = intent.Text.Length == 0
                    ? ReadOnlySpan<CompositionClause>.Empty
                    : compositionClauses.AsSpan(),
                cursorPosition = intent.Cursor,
            };
            var beforeVersion = editor.Version;
            editor.HandleCompositionChanged(data);
            if (!active) return;

            var expectedVersion = first && compositionLength > 0
                ? compositionBaseVersion + 1
                : beforeVersion;
            var expectedLength = compositionBaseCharCount - compositionLength;
            if (!editor.IsComposing || editor.Version != expectedVersion ||
                editor.CharCount != expectedLength || !SelectionEquals(compositionStart, compositionStart))
            {
                Reject(intent.NativeRevision);
                return;
            }

            nativeCompositionActive = true;
            lastNativeRevision = intent.NativeRevision;
            UniTextNativeInput.ReconcileNativeField(sessionId, intent.NativeRevision,
                authorityRevision, null, compositionStart + Math.Max(intent.Cursor, 0),
                compositionStart + Math.Max(intent.Cursor, 0));
            RecordSnapshot();
        }

        private void ApplyCompositionCancel(in NativeFieldCompositionIntent intent)
        {
            if (!nativeCompositionActive)
            {
                Acknowledge(intent.NativeRevision, true, false);
                return;
            }
            if (intent.Start != compositionStart || intent.Length != compositionLength)
            {
                Reject(intent.NativeRevision);
                return;
            }

            editor.HandleCompositionEnded();
            if (!active) return;
            var exact = editor.CharCount == compositionBaseCharCount &&
                        RangeEquals(compositionStart, compositionOriginalText ?? string.Empty);
            ClearNativeCompositionState();
            Acknowledge(intent.NativeRevision, exact, false);
        }

        private bool Begin(int intentSessionId, int nativeRevision, int baseAuthorityRevision)
        {
            if (!active || intentSessionId != sessionId) return false;
            if (pendingActionRevision != 0)
                throw new InvalidOperationException(
                    "A native field intent arrived before the previous action completed.");
            if (lastNativeRevision == int.MaxValue)
                throw new InvalidOperationException("The native field revision space is exhausted.");
            if (nativeRevision <= lastNativeRevision) return false;
            if (!IsNextNativeRevision(nativeRevision))
            {
                Reject(nativeRevision);
                return false;
            }

            if (baseAuthorityRevision == authorityRevision) return true;
            Reject(nativeRevision);
            return false;
        }

        private void Reject(int nativeRevision)
        {
            if (!active) return;
            if (editor.IsComposing) editor.HandleCompositionEnded();
            if (!active) return;
            ClearNativeCompositionState();
            lastNativeRevision = nativeRevision;
            AdvanceAuthorityRevision();
            GetSelection(out var selectionStart, out var selectionEnd);
            UniTextNativeInput.ReconcileNativeField(sessionId, nativeRevision, authorityRevision,
                NativeInputSession.CaptureText(editor), selectionStart, selectionEnd);
            RecordSnapshot();
        }

        private void Acknowledge(int nativeRevision, bool exact, bool nativeHasText)
        {
            if (!active) return;
            lastNativeRevision = nativeRevision;
            AdvanceAuthorityRevision();
            GetSelection(out var selectionStart, out var selectionEnd);
            UniTextNativeInput.ReconcileNativeField(sessionId, nativeRevision,
                authorityRevision, exact && nativeHasText ? null : NativeInputSession.CaptureText(editor),
                selectionStart, selectionEnd);
            RecordSnapshot();
        }

        private bool SelectionEquals(int start, int end)
        {
            GetSelection(out var actualStart, out var actualEnd);
            return actualStart == start && actualEnd == end;
        }

        private void GetSelection(out int start, out int end)
        {
            editor.GetCharSelection(out start, out var length);
            end = start + length;
        }

        private bool RangeEquals(int start, string text)
        {
            if (text.Length == 0) return true;
            if (start < 0 || start > editor.CharCount - text.Length) return false;
            var offset = 0;
            while (offset < text.Length)
            {
                var length = Math.Min(compareBuffer.Length, text.Length - offset);
                if (editor.CopyCharRangeTo(start + offset, length, compareBuffer) != length ||
                    !text.AsSpan(offset, length).SequenceEqual(compareBuffer.AsSpan(0, length)))
                    return false;
                offset += length;
            }
            return true;
        }

        private static bool RangeIsValid(int start, int length, int total)
            => start >= 0 && length >= 0 && start <= total && length <= total - start;

        private bool IsNextNativeRevision(int value)
        {
            if (lastNativeRevision == int.MaxValue)
                throw new InvalidOperationException("The native field revision space is exhausted.");
            return value == lastNativeRevision + 1;
        }

        private void AdvanceAuthorityRevision()
        {
            if (authorityRevision == int.MaxValue)
                throw new InvalidOperationException("The native field authority revision space is exhausted.");
            authorityRevision++;
        }

        private bool IsProposedEditBoundary(int start, int length, string text,
            int position, int finalLength)
        {
            if (position == 0 || position == finalLength) return true;
            var before = ProposedEditCharAt(start, length, text, position - 1);
            var after = ProposedEditCharAt(start, length, text, position);
            return !char.IsHighSurrogate(before) || !char.IsLowSurrogate(after);
        }

        private char ProposedEditCharAt(int start, int length, string text, int index)
        {
            if (index >= start && index < start + text.Length) return text[index - start];
            var sourceIndex = index < start ? index : index - text.Length + length;
            if (editor.CopyCharRangeTo(sourceIndex, 1, compareBuffer) != 1)
                throw new InvalidOperationException("The proposed edit does not map to the committed text.");
            return compareBuffer[0];
        }

        private static bool IsStringCharBoundary(string text, int position)
            => position == 0 || position == text.Length ||
               !char.IsHighSurrogate(text[position - 1]) ||
               !char.IsLowSurrogate(text[position]);

        private void RecordSnapshot()
        {
            snapshotTextStamp = editor.TextStorageStamp;
            GetSelection(out snapshotSelectionStart, out snapshotSelectionEnd);
        }

        private void ClearNativeCompositionState()
        {
            nativeCompositionActive = false;
            compositionOriginalText = null;
        }

        private void CompleteAction(Task operation, NativeInputReporter source, int nativeRevision,
            (object identity, int version) textStamp)
        {
            var fault = false;
            try
            {
                operation.GetAwaiter().GetResult();
                if (!active || !ReferenceEquals(reporter, source) ||
                    pendingActionRevision != nativeRevision) return;
                pendingActionRevision = 0;
                fault = true;
                var textChanged = editor.TextStorageStamp != textStamp;
                Acknowledge(nativeRevision, true, !textChanged);
            }
            catch (Exception error)
            {
                if (!fault && active && ReferenceEquals(reporter, source) &&
                    pendingActionRevision == nativeRevision)
                {
                    pendingActionRevision = 0;
                    fault = true;
                }
                Debug.LogException(error, editor);
                if (!fault || !active) return;
                try { Fault(sessionId, nativeRevision); }
                catch (Exception cleanupError) { Debug.LogException(cleanupError, editor); }
            }
        }
    }
}
