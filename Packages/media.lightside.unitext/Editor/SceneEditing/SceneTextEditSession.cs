using System;
using UnityEditor;
using UnityEngine;

namespace LightSide.SceneEditing
{
    /// <summary>
    /// Owns the single active SceneView inline-editing session: attaches (or reuses) the runtime
    /// <see cref="UniTextEditable"/> on the target, drives it through the editor-session seam, and
    /// commits the edited source markup back to the serialized <see cref="UniTextBase.Text"/> on exit.
    /// Everything the session adds belongs to a <see cref="SceneEditScaffold"/> on the target rather
    /// than to this class, so the user's component graph is restored whatever ends the session.
    /// </summary>
    internal static class SceneTextEditSession
    {
        private static UniTextBase target;
        private static UniTextEditable editable;
        private static SceneEditScaffold scaffold;
        private static bool running;
        private static bool ending;

        internal static bool Active => target != null && editable != null && editable.IsActive;
        internal static UniTextBase Target => target;
        internal static UniTextEditable Editable => editable;
        internal static event Action Changed;

        /// <summary>Starts (or re-hits) a session from a SceneView click, placing the caret at the hit point.</summary>
        internal static void Begin(UniTextBase newTarget, Vector2 screenPosition, Camera camera)
        {
            if (newTarget == null) return;
            if (Active && ReferenceEquals(target, newTarget))
            {
                editable.EditorPointerPress(screenPosition, camera, false);
                return;
            }
            End();
            Attach(newTarget);
            var requested = editable;
            editable.BeginEditorSession(() =>
            {
                if (ReferenceEquals(editable, requested)) End();
            }, () =>
            {
                if (!ReferenceEquals(editable, requested)) return;
                running = true;
                requested.EditorPointerPress(screenPosition, camera, false);
                SyncSelection();
            });
        }

        /// <summary>Starts a session on the selected <see cref="UniTextBase"/> (menu entry).</summary>
        internal static void BeginForSelected()
        {
            var go = Selection.activeGameObject;
            BeginNoClick(go != null ? go.GetComponent<UniTextBase>() : null);
        }

        /// <summary>Starts a session without a click (inspector button / menu), caret at the end of the text.</summary>
        internal static void BeginNoClick(UniTextBase newTarget)
        {
            if (newTarget == null) return;
            End();
            Attach(newTarget);
            var requested = editable;
            editable.BeginEditorSession(() =>
            {
                if (ReferenceEquals(editable, requested)) End();
            }, () =>
            {
                if (!ReferenceEquals(editable, requested)) return;
                running = true;
                requested.MoveCaretTo(requested.CodepointCount);
                SyncSelection();

                var view = SceneView.lastActiveSceneView;
                if (view != null) { view.Focus(); view.Repaint(); }
            });
        }

        /// <summary>
        /// Ends the running session, committing the edit and reclaiming the scaffold. Reclamation runs
        /// even when releasing the editor or committing the text fails, and the failure still surfaces.
        /// Re-entrant calls — the engine reporting the release this call requested — are absorbed.
        /// </summary>
        internal static void End()
        {
            EditorGUIUtility.editingTextField = false;
            if (ending) return;
            if (target == null && scaffold == null) { Reset(); return; }

            ending = true;
            try
            {
                Release();
                Commit();
            }
            finally
            {
                if (scaffold != null) scaffold.Reclaim();
                ending = false;
                Reset();
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// Ends the running session and reclaims every scaffold left anywhere in the project. The entry
        /// point for editor states no session survives — assembly reload, compilation, play mode, scene
        /// and prefab-stage changes, quitting — where a scaffold may have outlived the session that made it.
        /// </summary>
        internal static void Reclaim()
        {
            End();
            SceneEditScaffold.ReclaimUnclaimed();
        }

        /// <summary>
        /// Reconciles the session against live object state; returns whether an editing session is still
        /// running. Called every editor frame, so a session broken from outside — target or editor
        /// destroyed, editor deactivated by another owner — is torn down within one frame.
        /// </summary>
        internal static bool Tick()
        {
            if (target == null && scaffold == null) return false;
            if (target != null && editable != null && scaffold != null && (!running || editable.IsActive))
                return true;
            End();
            return false;
        }

        /// <summary>Routes an editor Copy/Cut/Paste/SelectAll/Undo/Redo command into the engine via the primary-modifier shortcut.</summary>
        internal static bool HandleCommand(string command)
        {
            if (!Active) return false;
            var primary = PlatformKeySemantics.PrimaryModifier;

            switch (command)
            {
                case "Copy":      editable.HandleKeyDown(NativeKeyCode.C, primary); return true;
                case "Cut":       editable.HandleKeyDown(NativeKeyCode.X, primary); return true;
                case "Paste":     editable.HandleKeyDown(NativeKeyCode.V, primary); return true;
                case "SelectAll": editable.HandleKeyDown(NativeKeyCode.A, primary); return true;
                case "Undo":      editable.HandleKeyDown(NativeKeyCode.Z, primary); return true;
                case "Redo":      editable.HandleKeyDown(NativeKeyCode.Y, primary); return true;
            }
            return false;
        }

        private static void Attach(UniTextBase newTarget)
        {
            target = newTarget;
            scaffold = SceneEditScaffold.Attach(newTarget.gameObject);

            editable = newTarget.GetComponent<UniTextEditable>();
            if (editable == null)
                editable = newTarget.gameObject.AddComponent<UniTextEditable>();
            scaffold.StampOwned();

            scaffold.CaptureMarkupVisibility(editable);
            editable.MarkupVisibility = MarkupVisibility.RevealActiveRange;
            Changed.SafeInvoke();
        }

        private static void Release()
        {
            var released = editable;
            if (released == null) return;
            try
            {
                released.Deactivate();
            }
            catch (Exception error)
            {
                Debug.LogException(error, released);
            }
            if (released != null) released.EndEditorSession();
        }

        private static void Commit()
        {
            if (editable == null || target == null) return;
            var markup = editable.Text;
            if (target.Text == markup) return;

            var state = new SerializedObject(target);
            state.UpdateIfRequiredOrScript();
            var text = InspectorHelpers.RequireProperty(state, "text");
            new SerializedPropertyBinding(text).SetValue(markup, "Edit Text (UniText)");
        }

        private static void SyncSelection()
        {
            if (target != null && Selection.activeGameObject != target.gameObject)
                Selection.activeGameObject = target.gameObject;
        }

        private static void Reset()
        {
            if (ReferenceEquals(target, null) && ReferenceEquals(editable, null)
                                              && ReferenceEquals(scaffold, null)) return;
            target = null;
            editable = null;
            scaffold = null;
            running = false;
            Changed.SafeInvoke();
        }
    }
}
