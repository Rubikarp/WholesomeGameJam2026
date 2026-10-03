using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace LightSide
{
    public partial class UniTextEditable
    {
        /// <summary>
        /// Currently active editor across the application — THE "current editor" authority.
        /// Selection UI (<see cref="ISelectionHandles"/> / <see cref="IInsertionHandle"/>,
        /// <see cref="IMagnifier"/>, <see cref="ITextContextMenu"/>) pulls geometry and document
        /// state from here at call time — implementations are never bound to a specific field, so
        /// they can never render against a stale one. Set on <see cref="Activate"/>, cleared when
        /// <see cref="Deactivate"/> completes.
        /// </summary>
        public static UniTextEditable ActiveEditor => activeEditor;

        private static UniTextEditable activeEditor;
        private static UniTextEditable pendingActivation;
        private static bool pendingActivationShowKeyboard;
#if UNITY_EDITOR
        private static bool pendingEditorSession;
        private static Action pendingEditorSessionRelease;
        private static Action pendingEditorSessionStarted;
#endif
        private static int activationDeferralDepth;
        private Action sessionRelease;
        private int activationGeneration;
        private bool releasePending;
        private bool disablePending;
        private bool compositionCommitPending;
        private int compositionCommitGeneration;
        private List<Action> compositionCommitCompletions;
        private List<Action> cellDetachCompletions;
        private bool cellDetached;

        internal static event Action<UniTextEditable, bool> EditingSessionRequested;
        internal static Action<UniTextEditable, Action> EditingSessionReleaseRequested;
        internal static Action<UniTextEditable> EditingSessionAbortRequested;
        internal static Action<UniTextEditable, Action> CompositionCommitRequested;

        /// <summary>
        /// Activates the editor, optionally requests a soft keyboard, and makes the caret visible.
        /// Queues the request while another editor is still releasing its input source.
        /// </summary>
        public void Activate(bool showKeyboard = true)
        {
            if (cellDetached) return;
            if (isActive)
            {
                if (releasePending)
                {
                    QueueActivation(this, showKeyboard);
                    return;
                }
                if (sessionRelease != null)
                {
                    QueueActivation(this, showKeyboard);
                    Deactivate();
                    return;
                }
                EditingSessionRequested?.Invoke(this, showKeyboard);
                return;
            }
            if (activationDeferralDepth != 0)
            {
                QueueActivation(this, showKeyboard);
                return;
            }
            if (!isActiveAndEnabled) return;
            EnsureInitialized();
            if (TextComponent == null) return;

            if (activeEditor != null && activeEditor != this)
            {
                QueueActivation(this, showKeyboard);
                TransferFocus(activeEditor);
                return;
            }
            ClearPendingActivation();
            activeEditor = this;
            isActive = true;
            releasePending = false;
            var activation = ++activationGeneration;

            try
            {
                Enroll();
                EnsureTouchUI();
                EnsureCaretRenderer();
                caretRenderer.ResetBlink();
                caretRenderer.enabled = true;

                selectionDirty = true;
                UpdateCaretAndSelection(forceCaretReveal: true);
                undoStack?.BreakCoalescing();

                EditingSessionRequested?.Invoke(this, showKeyboard);
                if (activationGeneration != activation || !isActive || releasePending || activeEditor != this)
                    return;
                Focused?.Invoke();
            }
            catch
            {
                if (activationGeneration == activation && isActive && activeEditor == this)
                {
                    try
                    {
                        BeginDeactivation();
                    }
                    catch (Exception cleanupError)
                    {
                        Debug.LogException(cleanupError, this);
                    }
                }
                throw;
            }
        }

        /// <summary>
        /// Begins releasing the current editing session and completes after its input source has
        /// quiesced. The editor remains active until completion; repeated requests are idempotent.
        /// </summary>
        public void Deactivate()
        {
            if (!isActive || releasePending) return;
            if (sessionRelease != null)
            {
                var release = sessionRelease;
                sessionRelease = null;
                releasePending = true;
                InvokeReleaseRequest(release, true);
                return;
            }

            BeginDeactivation();
        }

        private void OnDestroy()
        {
            CancelPendingActivation(this);
            if (!isActive) return;
            try
            {
                EditingSessionAbortRequested?.Invoke(this);
            }
            finally
            {
                if (isActive)
                {
                    releasePending = true;
                    CompleteDeactivation();
                }
            }
        }

        private void BeginDeactivation()
        {
            if (!isActive || releasePending) return;
            releasePending = true;
            var release = EditingSessionReleaseRequested;
            if (release == null)
            {
                CompleteDeactivation();
                return;
            }
            InvokeReleaseRequest(() => release(this, CompleteDeactivation), false);
        }

        private void InvokeReleaseRequest(Action request, bool completeIfPending)
        {
            ExceptionDispatchInfo failure = null;
            activationDeferralDepth++;
            try
            {
                request();
                if (completeIfPending && releasePending) CompleteDeactivation();
            }
            catch (Exception error)
            {
                failure = ExceptionDispatchInfo.Capture(error);
                if (releasePending)
                {
                    try
                    {
                        CompleteDeactivation();
                    }
                    catch (Exception cleanupError)
                    {
                        Debug.LogException(cleanupError, this);
                    }
                }
            }
            finally
            {
                activationDeferralDepth--;
            }

            if (failure == null)
            {
                TryActivatePending();
                return;
            }
            try
            {
                TryActivatePending();
            }
            catch (Exception activationError)
            {
                Debug.LogException(activationError, this);
            }
            failure.Throw();
        }

        private void CompleteDeactivation()
        {
            if (!releasePending) return;
            releasePending = false;
            sessionRelease = null;
            isActive = false;
            if (activeEditor == this) activeEditor = null;

            ExceptionDispatchInfo failure = null;
            activationDeferralDepth++;
            try
            {
                CaptureLifecycleFailure(CompleteCompositionCommitLocally, ref failure);
                CaptureLifecycleFailure(() =>
                {
                    if (caretRenderer != null) caretRenderer.enabled = false;
                }, ref failure);
                CaptureLifecycleFailure(() => Selectable?.ClearSelection(SelectionChangeReason.Defocus), ref failure);
                CaptureLifecycleFailure(ResetScrollOnDefocus, ref failure);
                CaptureLifecycleFailure(ResetInteractionState, ref failure);
                CaptureLifecycleFailure(OnTouchDefocused, ref failure);
                CaptureLifecycleFailure(HideContextMenu, ref failure);
                var endReason = pendingEndReason;
                CaptureLifecycleFailure(() => Defocused?.Invoke(endReason), ref failure);
                if (disablePending)
                {
                    disablePending = false;
                    CaptureLifecycleFailure(CompleteDisable, ref failure);
                }
                CaptureLifecycleFailure(CompleteCellDetachment, ref failure);
            }
            finally
            {
                activationDeferralDepth--;
            }

            if (failure == null)
            {
                TryActivatePending(true);
                return;
            }
            try
            {
                TryActivatePending(true);
            }
            catch (Exception activationError)
            {
                Debug.LogException(activationError, this);
            }
            failure.Throw();
        }

        private void CaptureLifecycleFailure(Action action, ref ExceptionDispatchInfo failure)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                if (failure == null) failure = ExceptionDispatchInfo.Capture(error);
                else Debug.LogException(error, this);
            }
        }

        private static void QueueActivation(UniTextEditable target, bool showKeyboard)
        {
#if UNITY_EDITOR
            CancelPendingEditorSession();
#endif
            pendingActivation = target;
            pendingActivationShowKeyboard = showKeyboard;
        }

#if UNITY_EDITOR
        private static void QueueEditorSession(UniTextEditable target, Action release, Action started)
        {
            CancelPendingEditorSession();
            pendingActivation = target;
            pendingActivationShowKeyboard = false;
            pendingEditorSession = true;
            pendingEditorSessionRelease = release;
            pendingEditorSessionStarted = started;
        }

        private static void CancelPendingEditorSession()
        {
            if (!pendingEditorSession) return;
            var cancel = pendingEditorSessionRelease;
            ClearPendingActivation();
            cancel?.Invoke();
        }
#endif

        private static void ClearPendingActivation()
        {
            pendingActivation = null;
            pendingActivationShowKeyboard = false;
#if UNITY_EDITOR
            pendingEditorSession = false;
            pendingEditorSessionRelease = null;
            pendingEditorSessionStarted = null;
#endif
        }

        private static void CancelPendingActivation(UniTextEditable target)
        {
            if (pendingActivation != target) return;
#if UNITY_EDITOR
            if (pendingEditorSession)
            {
                CancelPendingEditorSession();
                return;
            }
#endif
            ClearPendingActivation();
        }

        private static void TryActivatePending(bool releaseCompleted = false)
        {
            if ((!releaseCompleted && activationDeferralDepth != 0) || activeEditor != null)
                return;
            var target = pendingActivation;
            var showKeyboard = pendingActivationShowKeyboard;
#if UNITY_EDITOR
            var editorSession = pendingEditorSession;
            var release = pendingEditorSessionRelease;
            var started = pendingEditorSessionStarted;
#endif
            ClearPendingActivation();
#if UNITY_EDITOR
            if (editorSession)
            {
                if (target != null) target.BeginEditorSession(release, started);
                else release?.Invoke();
                return;
            }
#endif
            if (target != null) target.Activate(showKeyboard);
        }

        private static void TransferFocus(UniTextEditable current)
        {
            ExceptionDispatchInfo failure = null;
            try
            {
                current.Deactivate();
            }
            catch (Exception error)
            {
                failure = ExceptionDispatchInfo.Capture(error);
            }

            if (failure == null)
            {
                TryActivatePending();
                return;
            }
            try
            {
                TryActivatePending();
            }
            catch (Exception activationError)
            {
                Debug.LogException(activationError, current);
            }
            failure.Throw();
        }

        /// <summary>
        /// Releases input focus the canonical way: clears the EventSystem selection when this editor
        /// holds it (which deactivates via <c>OnDeselect</c>), otherwise deactivates directly.
        /// </summary>
        public void Defocus()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem != null && eventSystem.currentSelectedGameObject == gameObject)
                eventSystem.SetSelectedGameObject(null);
            else
                Deactivate();
        }

        private void OnApplicationPause(bool pause)
        {
            if (!isActive || releasePending) return;
            if (pause) RequestCompositionCommit();
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!isActive || releasePending) return;
            if (!focus) RequestCompositionCommit();
        }

        /// <summary>
        /// Invoked by a virtualised list host (Android RecyclerView, iOS UITableView,
        /// Unity scroll-pool, custom) when this editor's GameObject is recycled into a
        /// new cell position. Re-arms input lazily on the next focus. The owning
        /// component is expected to call <see cref="ISavedStateProvider.RestoreState"/>
        /// first when state needs to be preserved across reuse.
        /// </summary>
        public void OnAttachedToCell()
        {
            cellDetached = false;
            textDirty = true;
            selectionDirty = true;
        }

        /// <summary>
        /// Releases editing state and invokes <paramref name="completion"/> when the object may be
        /// safely recycled by its virtualised-list host.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="completion"/> is null.</exception>
        public void OnDetachedFromCell(Action completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            cellDetached = true;
            CancelPendingActivation(this);
            if (isActive)
            {
                cellDetachCompletions ??= new List<Action>(1);
                cellDetachCompletions.Add(completion);
                Deactivate();
                return;
            }

            RequestCompositionCommit(() =>
            {
                undoStack?.BreakCoalescing();
                completion();
            });
        }

        private void OnLayoutInvalidated()
        {
            selectionDirty = true;
            InvalidateCaretRectCache();
            InputGeometryChanged?.Invoke();
        }

        void ISavedStateProvider.SaveState(IDictionary<string, object> bundle)
        {
            if (bundle == null) return;
            bundle["unitext.text"] = Text;
            var sel = Selection;
            bundle["unitext.anchor"] = sel.Anchor;
            bundle["unitext.focus"]  = sel.Focus;
            bundle["unitext.affinity"] = (int)sel.Affinity;
            bundle["unitext.scrollX"] = scrollOffset.x;
            bundle["unitext.scrollY"] = scrollOffset.y;
        }

        void ISavedStateProvider.RestoreState(IDictionary<string, object> bundle)
        {
            if (bundle == null) return;
            if (bundle.TryGetValue("unitext.text", out var t) && t is string text)
                SetTextProgrammatic(text, reason: TextChangeReason.Restore);

            int anchor = 0, focus = 0;
            var affinity = CaretAffinity.Downstream;
            bool haveSelection = false;
            if (bundle.TryGetValue("unitext.anchor", out var a) && a is int ai) { anchor = ai; haveSelection = true; }
            if (bundle.TryGetValue("unitext.focus",  out var f) && f is int fi) { focus = fi; haveSelection = true; }
            if (bundle.TryGetValue("unitext.affinity", out var af) && af is int afi
                && afi >= 0 && afi <= (int)CaretAffinity.Upstream)
                affinity = (CaretAffinity)afi;
            if (haveSelection)
            {
                int max = codepointCount;
                if (anchor < 0) anchor = 0; else if (anchor > max) anchor = max;
                if (focus  < 0) focus  = 0; else if (focus  > max) focus  = max;
                Selectable?.SetSelectionInternal(new TextSelection(anchor, focus, affinity), SelectionChangeReason.Restore);
            }

            if (bundle.TryGetValue("unitext.scrollX", out var sx) && sx is float sxf
             && bundle.TryGetValue("unitext.scrollY", out var sy) && sy is float syf)
                scrollOffset = new Vector2(sxf, syf);
        }

        private void RequestCompositionCommit(Action completion = null)
        {
            if (completion != null)
            {
                compositionCommitCompletions ??= new List<Action>(1);
                compositionCommitCompletions.Add(completion);
            }
            if (compositionCommitPending) return;

            compositionCommitPending = true;
            var token = ++compositionCommitGeneration;
            if (!isComposing)
            {
                CompleteCompositionCommit(token);
                return;
            }
            var request = CompositionCommitRequested;
            if (request == null)
            {
                CompleteCompositionCommit(token);
                return;
            }

            try
            {
                request(this, () => CompleteCompositionCommit(token));
            }
            catch
            {
                if (compositionCommitPending && compositionCommitGeneration == token)
                {
                    compositionCommitPending = false;
                    compositionCommitGeneration++;
                    compositionCommitCompletions?.Clear();
                }
                throw;
            }
        }

        private void CompleteCompositionCommit(int token)
        {
            if (!compositionCommitPending || compositionCommitGeneration != token) return;
            CompleteCompositionCommitLocally();
        }

        private void CompleteCompositionCommitLocally()
        {
            compositionCommitPending = false;
            compositionCommitGeneration++;
            ExceptionDispatchInfo failure = null;
            CaptureLifecycleFailure(CommitCompositionAfterSourceResolution, ref failure);
            CaptureLifecycleFailure(CompleteCompositionCommitCallbacks, ref failure);
            failure?.Throw();
        }

        private void CompleteCompositionCommitCallbacks()
        {
            var completions = compositionCommitCompletions;
            compositionCommitCompletions = null;
            if (completions == null) return;

            ExceptionDispatchInfo failure = null;
            for (int i = 0; i < completions.Count; i++)
                CaptureLifecycleFailure(completions[i], ref failure);
            failure?.Throw();
        }

        private void CompleteCellDetachment()
        {
            var completions = cellDetachCompletions;
            cellDetachCompletions = null;
            if (completions == null) return;

            ExceptionDispatchInfo failure = null;
            for (int i = 0; i < completions.Count; i++)
                CaptureLifecycleFailure(completions[i], ref failure);
            failure?.Throw();
        }

        private void CommitCompositionAfterSourceResolution()
        {
            if (!isComposing)
            {
                undoStack?.BreakCoalescing();
                return;
            }

            var text = compositionOverlay.textLength == 0
                ? string.Empty
                : new string(compositionOverlay.textBuffer, 0, compositionOverlay.textLength);
            var selection = compositionInputSelection;
            if (!RestoreCompositionReplacement() || !isComposing)
            {
                FinishCompositionState();
                SetComposing(false);
                return;
            }
            isComposing = false;
            try
            {
                InsertTextCore(text.AsSpan(), text, TextChangeReason.TypeCompose, selection);
            }
            catch
            {
                FinishCompositionState();
                try
                {
                    NotifyCompositionStateChanged(false);
                }
                catch (Exception cleanupError)
                {
                    Debug.LogException(cleanupError, this);
                }
                throw;
            }
            FinishCompositionState();
            NotifyCompositionStateChanged(false);
        }

        private void FinishCompositionState()
        {
            compositionOverlay.Clear();
            compositionInputSelection = default;
            textDirty = true;
            undoStack?.BreakCoalescing();
        }
    }
}
