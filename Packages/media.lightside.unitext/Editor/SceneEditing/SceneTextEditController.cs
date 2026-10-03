using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide.SceneEditing
{
    /// <summary>
    /// SceneView driver for inline text editing — the editor-mode twin of the runtime input/pointer
    /// loop. Registers on <see cref="SceneView.duringSceneGui"/> (the seam the inspection overlay uses),
    /// starts a session on a double-click over a <see cref="UniTextBase"/>, and feeds SceneView pointer
    /// events plus Toolkit keyboard input into the runtime engine. While a session is active it holds keyboard
    /// focus and sets <see cref="EditorGUIUtility.editingTextField"/> so the SceneView's own tool
    /// shortcuts don't swallow the typing. Caret and selection are drawn by the engine's own renderers.
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneTextEditController
    {
        private static bool dragging;
        private static bool sweepRequested = true;
        private static readonly Dictionary<int, SceneKeyboardCapture> keyboardCaptures = new();

        static SceneTextEditController()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.hierarchyChanged += () => sweepRequested = true;

            EditorLifecycle.ManagedCleaning += SceneTextEditSession.Reclaim;
            CompilationPipeline.compilationStarted += _ => SceneTextEditSession.Reclaim();
            EditorApplication.playModeStateChanged += _ => SceneTextEditSession.Reclaim();
            EditorApplication.quitting += SceneTextEditSession.Reclaim;
            EditorSceneManager.sceneClosing += (_, _) => SceneTextEditSession.Reclaim();
            EditorSceneManager.activeSceneChangedInEditMode += (_, _) => SceneTextEditSession.Reclaim();
            PrefabStage.prefabStageClosing += _ => SceneTextEditSession.Reclaim();
        }

        /// <summary>
        /// Sweeps unclaimed scaffolds, reconciles the session, and keeps the engine ticking every editor
        /// frame while editing — the layout/caret sweep runs off the player loop, not off SceneView repaints,
        /// so the caret otherwise freezes between events. Sweeping here coalesces bursts of hierarchy
        /// changes into one pass and keeps destruction out of the notification that reported them.
        /// </summary>
        private static void OnEditorUpdate()
        {
            if (sweepRequested)
            {
                sweepRequested = false;
                SceneEditScaffold.ReclaimUnclaimed();
            }
            if (!SceneTextEditSession.Tick()) return;
            CoreLoop.RequestEditorFrame();
        }

        [MenuItem("Tools/UniText/Edit Text in Scene")]
        internal static void EditSelected() => SceneTextEditSession.BeginForSelected();

        [MenuItem("Tools/UniText/Edit Text in Scene", true)]
        private static bool EditSelectedValidate() => SelectedText() != null;

        private static UniTextBase SelectedText()
        {
            var go = Selection.activeGameObject;
            return go != null ? go.GetComponent<UniTextBase>() : null;
        }

        private static void OnSelectionChanged()
        {
            if (!SceneTextEditSession.Active) return;
            var t = SceneTextEditSession.Target;
            if (t == null || Selection.activeGameObject != t.gameObject)
                SceneTextEditSession.End();
        }

        private static void OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Keyboard);

            if (!SceneTextEditSession.Active)
            {
                HideKeyboardCapture(view);
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F2 && SelectedText() != null)
                {
                    SceneTextEditSession.BeginForSelected();
                    e.Use();
                    return;
                }
                TryBeginOnDoubleClick(e, view);
                return;
            }

            HandleActiveEvent(e, controlId, view, SceneTextEditSession.Target, SceneTextEditSession.Editable);
            if (!SceneTextEditSession.Active)
            {
                HideKeyboardCapture(view);
                return;
            }
            CaptureKeyboard(view, SceneTextEditSession.Target, SceneTextEditSession.Editable);
            DrawEditingChrome(SceneTextEditSession.Target, SceneTextEditSession.Editable);
            view.Repaint();
        }

        private static readonly Vector3[] outlineCorners = new Vector3[4];

        /// <summary>
        /// Draws the Figma-style outline around the text being edited (both render modes), and — only for
        /// non-Canvas <see cref="UniTextWorld"/> text, which has no Canvas caret renderer — a Handles caret,
        /// so world text is editable with a visible insertion point. Canvas text keeps the higher-fidelity
        /// runtime <see cref="InputCaretRenderer"/> and needs no editor-drawn caret.
        /// </summary>
        private static void DrawEditingChrome(UniTextBase target, UniTextEditable editable)
        {
            if (Event.current.type != EventType.Repaint) return;

            target.rectTransform.GetWorldCorners(outlineCorners);
            Handles.color = new Color(0.26f, 0.55f, 0.96f, 0.9f);
            Handles.DrawAAPolyLine(2f, outlineCorners[0], outlineCorners[1], outlineCorners[2], outlineCorners[3], outlineCorners[0]);

            if (target.canvas != null) return;
            var caret = editable.CurrentCaretLocalRect;
            var rt = target.rectTransform;
            Handles.color = Color.white;
            Handles.DrawAAPolyLine(2f,
                rt.TransformPoint(new Vector3(caret.center.x, caret.yMax)),
                rt.TransformPoint(new Vector3(caret.center.x, caret.yMin)));
        }

        private static void CaptureKeyboard(SceneView view, UniTextBase target, UniTextEditable editable)
        {
            if (!view.hasFocus) return;
            var caret = editable.CurrentCaretLocalRect;
            var position = HandleUtility.WorldToGUIPoint(
                target.rectTransform.TransformPoint(caret.center));
            GetKeyboardCapture(view).Activate(position);
            EditorGUIUtility.editingTextField = true;
        }

        private static SceneKeyboardCapture GetKeyboardCapture(SceneView view)
        {
            var id = ObjectUtils.GetInstanceIdCompat(view);
            if (keyboardCaptures.TryGetValue(id, out var capture)) return capture;
            capture = new SceneKeyboardCapture(view.rootVisualElement);
            keyboardCaptures.Add(id, capture);
            view.rootVisualElement.RegisterCallback<DetachFromPanelEvent>(_ =>
                keyboardCaptures.Remove(id));
            return capture;
        }

        private static void HideKeyboardCapture(SceneView view)
        {
            if (keyboardCaptures.TryGetValue(ObjectUtils.GetInstanceIdCompat(view), out var capture))
                capture.Hide();
        }

        private static void TryBeginOnDoubleClick(Event e, SceneView view)
        {
            if (e.type != EventType.MouseDown || e.button != 0 || e.clickCount < 2) return;

            var target = ResolveDoubleClickTarget(e, view);
            if (target == null) return;

            var screen = HandleUtility.GUIPointToScreenPixelCoordinate(e.mousePosition);
            SceneTextEditSession.Begin(target, screen, view.camera);
            e.Use();
        }

        /// <summary>
        /// Resolves native SceneView picks first, then screen-tests Canvas text because Unity does not
        /// expose uGUI graphics through <see cref="HandleUtility.PickGameObject"/>.
        /// </summary>
        private static UniTextBase ResolveDoubleClickTarget(Event e, SceneView view)
        {
            var go = HandleUtility.PickGameObject(e.mousePosition, false);
            var picked = go != null ? go.GetComponent<UniTextBase>() : null;
            if (picked != null) return picked;

            var selected = SelectedText();
            if (selected != null && ClickInsideText(selected, e.mousePosition, view.camera)) return selected;

            return PickByScreenRect(e.mousePosition, view.camera);
        }

        /// <summary>Screen-rect hit test over every active <see cref="UniTextBase"/>, since uGUI graphics aren't pickable in the SceneView. Prefers a text whose glyphs are actually under the cursor.</summary>
        private static UniTextBase PickByScreenRect(Vector2 guiMouse, Camera camera)
        {
            var screen = HandleUtility.GUIPointToScreenPixelCoordinate(guiMouse);
            UniTextBase rectHit = null;
            foreach (var text in ObjectUtils.FindAll<UniTextBase>())
            {
                var rt = text.rectTransform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, camera, out var local)) continue;
                if (!rt.rect.Contains(local)) continue;
                if (text.IsOverText(screen, camera)) return text;
                rectHit ??= text;
            }
            return rectHit;
        }

        private static void HandleActiveEvent(Event e, int controlId, SceneView view, UniTextBase target, UniTextEditable editable)
        {
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                {
                    if (ClickInsideText(target, e.mousePosition, view.camera))
                    {
                        var screen = HandleUtility.GUIPointToScreenPixelCoordinate(e.mousePosition);
                        editable.EditorPointerPress(screen, view.camera, e.shift);
                        GUIUtility.hotControl = controlId;
                        dragging = false;
                        e.Use();
                        view.Repaint();
                    }
                    else
                    {
                        SceneTextEditSession.End();
                    }
                    break;
                }
                case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                {
                    var screen = HandleUtility.GUIPointToScreenPixelCoordinate(e.mousePosition);
                    if (!dragging) { editable.EditorDragBegin(screen, view.camera, e.shift); dragging = true; }
                    else editable.EditorDragUpdate(screen, view.camera);
                    e.Use();
                    view.Repaint();
                    break;
                }
                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                {
                    if (dragging) editable.EditorDragEnd();
                    dragging = false;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                }
                case EventType.ContextClick:
                {
                    if (ClickInsideText(target, e.mousePosition, view.camera))
                    {
                        SceneTextContextMenu.Show(
                            new Rect(e.mousePosition, Vector2.zero), target, editable);
                        e.Use();
                    }
                    break;
                }
            }
        }

        private static bool IsEditCommand(string command) =>
            command == "Copy" || command == "Cut" || command == "Paste" ||
            command == "SelectAll" || command == "Undo" || command == "Redo";

        private static bool ClickInsideText(UniTextBase target, Vector2 guiMouse, Camera camera)
        {
            var screen = HandleUtility.GUIPointToScreenPixelCoordinate(guiMouse);
            var rt = target.rectTransform;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, camera, out var local)
                   && rt.rect.Contains(local);
        }

        private sealed class SceneKeyboardCapture
        {
            private readonly TextField field;

            public SceneKeyboardCapture(VisualElement parent)
            {
                field = new TextField
                {
                    multiline = false,
                    pickingMode = PickingMode.Ignore,
                };
                field.style.position = Position.Absolute;
                field.style.width = 2f;
                field.style.height = 16f;
                field.style.opacity = 0f;
                field.RegisterValueChangedCallback(evt =>
                {
                    if (string.IsNullOrEmpty(evt.newValue)) return;
                    SceneTextEditSession.Editable?.HandleTextInput(evt.newValue);
                    field.SetValueWithoutNotify(string.Empty);
                });
                field.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
                field.RegisterCallback<ValidateCommandEvent>(OnValidateCommand);
                field.RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand);
                parent.Add(field);
                Hide();
            }

            public void Activate(Vector2 position)
            {
                field.style.display = DisplayStyle.Flex;
                field.style.left = position.x;
                field.style.top = position.y;
                var focused = field.panel?.focusController?.focusedElement as VisualElement;
                if (focused != field && (focused == null || !field.Contains(focused)))
                    field.Focus();
            }

            public void Hide()
            {
                field.style.display = DisplayStyle.None;
            }

            private static void OnKeyDown(KeyDownEvent evt)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    SceneTextEditSession.End();
                    Consume(evt);
                    return;
                }
                var editable = SceneTextEditSession.Editable;
                if (editable == null || !SceneTextInputTranslator.ProcessKeyDown(editable, evt)) return;
                Consume(evt);
                SceneView.RepaintAll();
            }

            private static void OnValidateCommand(ValidateCommandEvent evt)
            {
                if (IsEditCommand(evt.commandName)) Consume(evt);
            }

            private static void OnExecuteCommand(ExecuteCommandEvent evt)
            {
                if (!SceneTextEditSession.HandleCommand(evt.commandName)) return;
                Consume(evt);
                SceneView.RepaintAll();
            }

            private static void Consume(EventBase evt)
            {
#if !UNITY_6000_0_OR_NEWER
                evt.PreventDefault();
#endif
                evt.StopImmediatePropagation();
            }
        }
    }
}
