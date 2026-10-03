#if UNITY_EDITOR
using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// SceneView session lifecycle. Editor events reuse the runtime editing funnel.
    /// </summary>
    public partial class UniTextEditable
    {
        /// <summary>
        /// Opens an editor-driven editing session, invoking <paramref name="started"/> once it is live.
        /// Every outcome is reported: a request that cannot start — because the editor is unusable, is
        /// already running one, or is queued behind another editor and then superseded — invokes
        /// <paramref name="release"/> instead, so the caller never has to time out waiting for a session
        /// that will not arrive. <paramref name="release"/> also reports a session the engine ends on its own.
        /// </summary>
        internal void BeginEditorSession(Action release, Action started = null)
        {
            if (cellDetached)
            {
                release?.Invoke();
                return;
            }
            if (isActive)
            {
                if (sessionRelease != null)
                {
                    release?.Invoke();
                    return;
                }
                QueueEditorSession(this, release, started);
                Deactivate();
                return;
            }
            if (activationDeferralDepth != 0)
            {
                QueueEditorSession(this, release, started);
                return;
            }
            EnsureInitialized();
            if (TextComponent == null)
            {
                release?.Invoke();
                return;
            }

            Canvas.ForceUpdateCanvases();

            if (activeEditor != null && activeEditor != this)
            {
                QueueEditorSession(this, release, started);
                TransferFocus(activeEditor);
                return;
            }

            ClearPendingActivation();
            sessionRelease = release;
            activeEditor = this;
            isActive = true;
            releasePending = false;
            var activation = ++activationGeneration;

            try
            {
                EnsureCaretRenderer();
                caretRenderer.ResetBlink();
                caretRenderer.enabled = true;

                selectionDirty = true;
                UpdateCaretAndSelection(forceCaretReveal: true);
                undoStack?.BreakCoalescing();

                Focused?.Invoke();
                if (activationGeneration != activation || !isActive || releasePending || activeEditor != this)
                    return;
                started?.Invoke();
            }
            catch
            {
                if (activationGeneration == activation && isActive && activeEditor == this)
                {
                    try
                    {
                        Deactivate();
                    }
                    catch (Exception cleanupError)
                    {
                        Debug.LogException(cleanupError, this);
                    }
                }
                throw;
            }
        }

        internal void EndEditorSession()
        {
            if (!isActive) return;
            sessionRelease = null;
            if (!releasePending) releasePending = true;
            CompleteDeactivation();
        }

        /// <summary>The caret geometry committed together with the current text layout.</summary>
        internal Rect CurrentCaretLocalRect => cachedCaretRect;

        /// <summary>Which standard clipboard/select-all actions apply right now — the same computation the runtime context menu uses, so the SceneView menu greys items from one owner.</summary>
        internal ContextMenuCapabilities EditorContextCapabilities => BuildContextMenuCapabilities();

        internal void EditorPointerPress(Vector2 screenPosition, Camera camera, bool shift)
        {
            var cluster = HitTestCaretSource(screenPosition, camera, out var upstream);
            undoStack.BreakCoalescing();
            desiredX = float.NaN;
            Selectable.HandlePressGesture(cluster, upstream, screenPosition, shift);
            MarkSelectionDirty();
        }

        internal void EditorDragBegin(Vector2 screenPosition, Camera camera, bool shift)
        {
            var cp = HitTestCaretSource(screenPosition, camera, out var upstream);
            Selectable.BeginDrag(cp, shift, upstream ? CaretAffinity.Upstream : CaretAffinity.Downstream);
        }

        internal void EditorDragUpdate(Vector2 screenPosition, Camera camera)
        {
            var cp = HitTestCaretSource(screenPosition, camera, out var upstream);
            Selectable.UpdateDrag(cp, upstream ? CaretAffinity.Upstream : CaretAffinity.Downstream);
            MarkSelectionDirty();
        }

        internal void EditorDragEnd() => Selectable.EndDrag();
    }
}
#endif
