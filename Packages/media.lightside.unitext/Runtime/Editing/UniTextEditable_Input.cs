using System;
using System.Threading.Tasks;

namespace LightSide
{
    public partial class UniTextEditable
    {
        internal Task HandleKeyDown(NativeKeyCode key, NativeModifiers mods)
        {
            if (!AcceptsNewlines && (key == NativeKeyCode.Return || key == NativeKeyCode.KeypadEnter))
                return ExecuteWithCompletion(EditAction.Submit);

            var resolve = new KeyResolve { key = key, modifiers = mods };
            keyResolver?.Invoke(ref resolve);
            var action = resolve.action != EditAction.None
                ? resolve.action
                : InputPlatformKeyMap.Instance.Resolve(key, mods);
            return action != EditAction.None
                ? ExecuteWithCompletion(action)
                : Task.CompletedTask;
        }

        internal void HandleSelectionInput(int charStart, int charEnd)
        {
            if (isComposing) return;
            charStart = ViewText.SnapToPairBoundary(charStart);
            charEnd = ViewText.SnapToPairBoundary(charEnd);
            var cpStart = CharToCodepointIndex(charStart);
            var cpEnd = charStart == charEnd ? cpStart : CharToCodepointIndex(charEnd);

            Selectable.SetSelectionInternal(new TextSelection(cpStart, cpEnd, CaretAffinity.Downstream), SelectionChangeReason.Input);
            selectionDirty = true;
        }

        internal bool HandleTextInput(string text) => HandleTextInput(text, 0, 0, false);

        internal bool HandleTextInput(string text, int charStart, int charLength)
            => HandleTextInput(text, charStart, charLength, true);

        private bool HandleTextInput(string text, int charStart, int charLength, bool hasReplacement)
        {
            if (readOnly) return false;

            bool wasComposing = isComposing;
            var compositionReplacement = compositionInputSelection;
            if (isComposing)
            {
                if (!RestoreCompositionReplacement() || !isComposing)
                {
                    FinishCompositionState();
                    SetComposing(false);
                    return false;
                }
                isComposing = false;
            }

            TextSelection? replacement = hasReplacement
                ? CharRangeToSelection(charStart, charLength)
                : wasComposing ? compositionReplacement : null;

            if (!hasReplacement && !wasComposing && (text == "\n" || text == "\r"))
            {
                _ = HandleKeyDown(NativeKeyCode.Return, NativeModifiers.None);
                return false;
            }

            var reason = wasComposing ? TextChangeReason.TypeCompose : TextChangeReason.Type;
            bool exact;
            try
            {
                if (replacement.HasValue)
                {
                    text ??= string.Empty;
                    exact = InsertTextCore(text.AsSpan(), text, reason, replacement.Value);
                }
                else
                {
                    text ??= string.Empty;
                    if (text.Length == 0 && Selection.IsCollapsed) return false;
                    exact = InsertTextCore(text.AsSpan(), text, reason);
                }
            }
            catch
            {
                if (wasComposing)
                {
                    FinishCompositionState();
                    try
                    {
                        NotifyCompositionStateChanged(false);
                    }
                    catch (Exception cleanupError)
                    {
                        UnityEngine.Debug.LogException(cleanupError, this);
                    }
                }
                throw;
            }
            if (!wasComposing) return exact;
            FinishCompositionState();
            NotifyCompositionStateChanged(false);
            return exact;
        }

        internal void HandleDeleteBack()
        {
            if (readOnly || isComposing) return;
            DeletePrevious();
        }

        internal void Execute(EditAction action) => _ = ExecuteWithCompletion(action);

        internal Task ExecuteWithCompletion(EditAction action)
        {
            if (isComposing || (readOnly && !IsNonMutatingAction(action)))
                return Task.CompletedTask;

            switch (action)
            {
                case EditAction.MoveLeft:      MoveCaret(CaretMoveDirection.Left, false, byWord: false); break;
                case EditAction.MoveRight:     MoveCaret(CaretMoveDirection.Right, false, byWord: false); break;
                case EditAction.MoveUp:        MoveCaretVertical(-1, false); break;
                case EditAction.MoveDown:      MoveCaretVertical(+1, false); break;
                case EditAction.MovePageUp:    MoveCaretVertical(-PageLineCount(), false); break;
                case EditAction.MovePageDown:  MoveCaretVertical(+PageLineCount(), false); break;
                case EditAction.MoveWordLeft:  MoveCaret(CaretMoveDirection.Left, false, byWord: true); break;
                case EditAction.MoveWordRight: MoveCaret(CaretMoveDirection.Right, false, byWord: true); break;
                case EditAction.MoveLineStart: MoveCaretToLineEdge(false, false); break;
                case EditAction.MoveLineEnd:   MoveCaretToLineEdge(true, false); break;
                case EditAction.MoveDocStart:  MoveCaretToDocEdge(false, false); break;
                case EditAction.MoveDocEnd:    MoveCaretToDocEdge(true, false); break;

                case EditAction.SelectLeft:      MoveCaret(CaretMoveDirection.Left, true, byWord: false); break;
                case EditAction.SelectRight:     MoveCaret(CaretMoveDirection.Right, true, byWord: false); break;
                case EditAction.SelectUp:        MoveCaretVertical(-1, true); break;
                case EditAction.SelectDown:      MoveCaretVertical(+1, true); break;
                case EditAction.SelectPageUp:    MoveCaretVertical(-PageLineCount(), true); break;
                case EditAction.SelectPageDown:  MoveCaretVertical(+PageLineCount(), true); break;
                case EditAction.SelectWordLeft:  MoveCaret(CaretMoveDirection.Left, true, byWord: true); break;
                case EditAction.SelectWordRight: MoveCaret(CaretMoveDirection.Right, true, byWord: true); break;
                case EditAction.SelectLineStart: MoveCaretToLineEdge(false, true); break;
                case EditAction.SelectLineEnd:   MoveCaretToLineEdge(true, true); break;
                case EditAction.SelectDocStart:  MoveCaretToDocEdge(false, true); break;
                case EditAction.SelectDocEnd:    MoveCaretToDocEdge(true, true); break;
                case EditAction.SelectAll:       SelectAll(); break;

                case EditAction.InsertNewline:   InsertText("\n", TextChangeReason.Type); break;
                case EditAction.InsertTab:       InsertText("\t", TextChangeReason.Type); break;
                case EditAction.DeletePrev:      DeletePrevious(); break;
                case EditAction.DeleteNext:      DeleteNext(); break;
                case EditAction.DeleteWordPrev:  DeleteWordPrevious(); break;
                case EditAction.DeleteWordNext:  DeleteWordNext(); break;
                case EditAction.DeleteLineStart: DeleteToLineStart(); break;
                case EditAction.DeleteLineEnd:   DeleteToLineEnd(); break;
                case EditAction.TransposeChars:  TransposeCharacters(); break;

                case EditAction.Copy:  Copy(); break;
                case EditAction.Cut:   Cut(); break;
                case EditAction.Paste: return PasteAsync();
                case EditAction.PasteAsPlain: return PastePlainAsync();

                case EditAction.Undo: Undo(); break;
                case EditAction.Redo: Redo(); break;

                case EditAction.Submit: SubmitField(); break;
                case EditAction.Cancel: CancelField(); break;
            }
            return Task.CompletedTask;
        }

        internal static bool IsNonMutatingAction(EditAction action) => action switch
        {
            >= EditAction.FirstMove and <= EditAction.LastMove => true,
            >= EditAction.FirstSelect and <= EditAction.LastSelect => true,
            EditAction.Copy => true,
            EditAction.Submit => true,
            EditAction.Cancel => true,
            _ => false,
        };

        private bool defocusAfterSubmit;
        private EditingEndReason pendingEndReason;

        /// <summary>
        /// Called by a submit-defining behavior during <see cref="Submitted"/> dispatch to release
        /// focus once the dispatch completes — deferred so every subscriber observes a still-focused
        /// editor regardless of subscription order.
        /// </summary>
        public void RequestDefocusAfterSubmit() => defocusAfterSubmit = true;

        private void SubmitField()
        {
            if (Submitted != null)
                Submitted.Invoke(Text);

            if (defocusAfterSubmit)
            {
                defocusAfterSubmit = false;
                EndEditingWithReason(EditingEndReason.Committed, Defocus);
            }
        }

        private void CancelField()
        {
            if (Cancelled != null)
                EndEditingWithReason(EditingEndReason.Cancelled, Cancelled.Invoke);
        }

        /// <summary>
        /// Runs a dispatch during which the session may end, so that the end reports
        /// <paramref name="reason"/> instead of the default. The reason is restored afterwards:
        /// a dispatch that leaves the editor focused says nothing about how editing will end.
        /// </summary>
        private void EndEditingWithReason(EditingEndReason reason, Action dispatch)
        {
            var previous = pendingEndReason;
            pendingEndReason = reason;
            try { dispatch(); }
            finally { pendingEndReason = previous; }
        }
    }
}
