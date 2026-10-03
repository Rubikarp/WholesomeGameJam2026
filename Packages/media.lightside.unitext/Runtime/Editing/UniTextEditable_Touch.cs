using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Touch layer of the editable: gesture recognition, selection / insertion handles,
    /// magnifier, mobile keyboard integration, and context-menu presentation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On touch platforms (iOS, Android), this partial class intercepts pointer events and
    /// routes them through <see cref="TouchGestureRecognizer"/> instead of the desktop
    /// click/drag handlers. The recognizer detects single/double/triple taps, long press,
    /// and drag gestures, which map to platform-standard text editing interactions.
    /// </para>
    /// <para>
    /// Selection handles (<see cref="ISelectionHandles"/> / <see cref="IInsertionHandle"/>) and
    /// the magnifier (<see cref="IMagnifier"/>) are entities owned by the sibling selectable.
    /// The editable drives them while each entity owns how its presentation is implemented.
    /// </para>
    /// <para>
    /// On desktop platforms, touch features are inactive. The mouse/keyboard
    /// handlers in <c>UniTextEditable_Selection.cs</c> handle all interaction.
    /// </para>
    /// </remarks>
    public partial class UniTextEditable
    {
        private ISelectionHandles selectionHandlesImpl;
        private IInsertionHandle insertionHandleImpl;
        private IMagnifier magnifierImpl;

        /// <summary>Gesture recognizer for tap/double-tap/long-press/drag detection.</summary>
        private TouchGestureRecognizer touchGesture;

        /// <summary>
        /// Set by the immediate <see cref="HandleTouchTapCaret"/> when a tap landed on the existing
        /// collapsed caret (moved nothing), read by the deferred <see cref="HandleTouchSingleTap"/> to
        /// raise the context menu — the two halves of one tap run a multi-tap window apart.
        /// </summary>
        private bool tapWasReTap;

        /// <summary>Whether touch interaction is active for this session.</summary>
        private bool touchActive;

        /// <summary>Whether the assigned touch-UI slots have been resolved and subscribed — lazily, at first activation.</summary>
        private bool touchUIResolved;

        /// <summary>True from touch pointer-down to pointer-up; keeps the editable enrolled so the gesture recogniser's long-press timing ticks.</summary>
        private bool touchPointerActive;

        /// <summary>Resolved magnifier for the shipped handle components' loupe-over-handle drag; null when no magnifier is assigned.</summary>
        internal IMagnifier MagnifierImpl => magnifierImpl;

        /// <summary>
        /// Initializes touch interaction. Called from OnEnable when on a touch platform
        /// or when touch simulation is active in the editor. Only the gesture recogniser is
        /// created here — the assigned touch-UI slots resolve lazily at first activation
        /// (<see cref="EnsureTouchUI"/>).
        /// </summary>
        private void InitializeTouch()
        {
            if (!ShouldUseTouchInteraction())
                return;

            touchActive = true;
            TextComponent.longPressClaimed = true;

            touchGesture = new TouchGestureRecognizer();
            touchGesture.CanvasSource = GetGestureCanvas;
            touchGesture.ThresholdsSource = GetGestureThresholds;
            touchGesture.OnTapCaret = HandleTouchTapCaret;
            touchGesture.OnSingleTap = HandleTouchSingleTap;
            touchGesture.OnDoubleTap = HandleTouchDoubleTap;
            touchGesture.OnTripleTap = HandleTouchTripleTap;
            touchGesture.OnLongPress = HandleTouchLongPress;
            touchGesture.OnLongPressEnd = HandleTouchLongPressEnd;
            touchGesture.OnDragStart = HandleTouchDragStart;
            touchGesture.OnDragUpdate = HandleTouchDragUpdate;
            touchGesture.OnDragEnd = HandleTouchDragEnd;
            touchGesture.OnScrollDrag = HandleTouchScrollDrag;
            touchGesture.OnScrollDragEnd = HandleTouchScrollDragEnd;
        }

        /// <summary>
        /// Tears down touch interaction. Called from OnDisable. The context menu is dismissed
        /// even without a touch session (the desktop right-click path presents it too), but only
        /// when this editable presented it — disabling a field must not hide a shared menu
        /// another field currently owns.
        /// </summary>
        private void TeardownTouch()
        {
            if (IsContextMenuVisible) HideContextMenu();
            if (!touchActive) return;

            touchActive = false;
            touchPointerActive = false;
            touchUIResolved = false;

            var text = Selectable != null ? Selectable.TextComponent : null;
            if (text != null) text.longPressClaimed = false;

            UnsubscribeTouchUI();
            if (Selectable != null) Selectable.TouchUISlotsChanged -= OnSelectableTouchUISlotsChanged;

            HideAllTouchUI();

            touchGesture.Reset();
            touchGesture = null;
        }

        /// <summary>
        /// Resolves the touch-UI slots at first activation from the sibling
        /// <see cref="UniTextSelectable"/> that owns them: casts its <see cref="UniTextSelectable.SelectionHandles"/>
        /// to <see cref="ISelectionHandles"/> / <see cref="IInsertionHandle"/> and its
        /// <see cref="UniTextSelectable.Magnifier"/> to <see cref="IMagnifier"/>, then subscribes to their
        /// drive events. Unassigned slots leave the corresponding touch UI absent.
        /// </summary>
        private void EnsureTouchUI()
        {
            if (!touchActive || touchUIResolved) return;
            touchUIResolved = true;

            ResolveTouchUISlots();
            if (Selectable != null) Selectable.TouchUISlotsChanged += OnSelectableTouchUISlotsChanged;

            SubscribeTouchUI();
        }

        /// <summary>Resolves the handle/magnifier implementations from the sibling
        /// <see cref="UniTextSelectable"/>, which owns the serialized touch-UI slots.</summary>
        private void ResolveTouchUISlots()
        {
            var handles = Selectable != null ? Selectable.SelectionHandles : null;
            selectionHandlesImpl = handles as ISelectionHandles;
            insertionHandleImpl = handles as IInsertionHandle;
            magnifierImpl = Selectable != null ? Selectable.Magnifier : null;
        }

        /// <summary>Rebinds handle subscriptions when the sibling's touch-UI slots change at runtime.</summary>
        private void OnSelectableTouchUISlotsChanged()
        {
            UnsubscribeHandles();
            UnsubscribeInsertionHandle();
            ResolveTouchUISlots();
            SubscribeHandles();
            SubscribeInsertionHandle();
        }

        /// <summary>
        /// Wires all touch UI events. Runs on every enable; <see cref="UnsubscribeTouchUI"/>
        /// mirrors it on disable, and every subscription is -=/+= paired so a repeat call
        /// can never stack duplicate handlers.
        /// </summary>
        private void SubscribeTouchUI()
        {
            SubscribeHandles();
            SubscribeInsertionHandle();
        }

        private void UnsubscribeTouchUI()
        {
            UnsubscribeHandles();
            UnsubscribeInsertionHandle();
        }

        private void SubscribeHandles()
        {
            if (selectionHandlesImpl == null) return;
            selectionHandlesImpl.AnchorDragged -= HandleAnchorHandleDragged;
            selectionHandlesImpl.FocusDragged -= HandleFocusHandleDragged;
            selectionHandlesImpl.SelectionHandleDragStarted -= HandleHandleDragStarted;
            selectionHandlesImpl.SelectionHandleDragEnded -= HandleHandleDragEnded;
            selectionHandlesImpl.AnchorDragged += HandleAnchorHandleDragged;
            selectionHandlesImpl.FocusDragged += HandleFocusHandleDragged;
            selectionHandlesImpl.SelectionHandleDragStarted += HandleHandleDragStarted;
            selectionHandlesImpl.SelectionHandleDragEnded += HandleHandleDragEnded;
        }

        private void UnsubscribeHandles()
        {
            if (selectionHandlesImpl == null) return;
            selectionHandlesImpl.AnchorDragged -= HandleAnchorHandleDragged;
            selectionHandlesImpl.FocusDragged -= HandleFocusHandleDragged;
            selectionHandlesImpl.SelectionHandleDragStarted -= HandleHandleDragStarted;
            selectionHandlesImpl.SelectionHandleDragEnded -= HandleHandleDragEnded;
        }

        private void SubscribeInsertionHandle()
        {
            if (insertionHandleImpl == null) return;
            insertionHandleImpl.InsertionHandleDragged -= HandleInsertionHandleDragged;
            insertionHandleImpl.InsertionHandleTapped -= HandleInsertionHandleTapped;
            insertionHandleImpl.InsertionHandleDragStarted -= HandleHandleDragStarted;
            insertionHandleImpl.InsertionHandleDragEnded -= HandleHandleDragEnded;
            insertionHandleImpl.InsertionHandleDragged += HandleInsertionHandleDragged;
            insertionHandleImpl.InsertionHandleTapped += HandleInsertionHandleTapped;
            insertionHandleImpl.InsertionHandleDragStarted += HandleHandleDragStarted;
            insertionHandleImpl.InsertionHandleDragEnded += HandleHandleDragEnded;
        }

        private void UnsubscribeInsertionHandle()
        {
            if (insertionHandleImpl == null) return;
            insertionHandleImpl.InsertionHandleDragged -= HandleInsertionHandleDragged;
            insertionHandleImpl.InsertionHandleTapped -= HandleInsertionHandleTapped;
            insertionHandleImpl.InsertionHandleDragStarted -= HandleHandleDragStarted;
            insertionHandleImpl.InsertionHandleDragEnded -= HandleHandleDragEnded;
        }

        /// <summary>
        /// Updates the gesture recognizer each frame for long-press timing.
        /// Called from <see cref="ProcessFrame"/> when touch is active.
        /// </summary>
        private void UpdateTouchGesture()
        {
            if (!touchActive) return;
            touchGesture.Update(Time.unscaledTime);
        }

        /// <summary>
        /// Immediate half of a tap, fired on release before the multi-tap window resolves: activate /
        /// re-show the keyboard and place the caret at the tap, so the caret tracks the finger with no
        /// latency (iOS/Android behavior). A focusing tap activates and still places the caret; a tap on
        /// an active field with a dismissed keyboard re-shows it AND moves the caret. A tap that lands on
        /// the existing collapsed caret moves nothing and only arms the context-menu affordance for the
        /// deferred half. Superseded by a chained tap's word/line selection; neither half toggles focus off.
        /// </summary>
        private void HandleTouchTapCaret(Vector2 screenPosition)
        {
            tapWasReTap = false;

            bool focusingTap = !IsActive;
            if (focusingTap)
            {
                Activate();
                if (!IsActive) return;
            }
            else
            {
                Activate();
            }

            var camera = GetEventCamera();
            var codepointIndex = HitTestCaretSource(screenPosition, camera, out var upstream);
            bool caretMoved = codepointIndex != Selection.Focus || !Selection.IsCollapsed;

            if (!caretMoved && !focusingTap)
            {
                tapWasReTap = true;
                return;
            }

            Selectable.EndDrag();
            PlaceCaret(codepointIndex, upstream, SelectionChangeReason.Pointer);
            selectionHandlesImpl?.Hide();
        }

        /// <summary>
        /// Deferred half of a solitary tap, fired once the multi-tap window closes with no follow-up: shows
        /// the insertion handle, and — when the tap did not move the caret (a re-tap on the existing caret,
        /// or any tap on empty text) — toggles the context menu, the platform "tap the caret to summon or
        /// dismiss the menu" convention. Held back from <see cref="HandleTouchTapCaret"/> so a chained tap —
        /// which never reaches here — does not flash the handle/menu before its word selection replaces them.
        /// </summary>
        private void HandleTouchSingleTap(Vector2 screenPosition)
        {
            if (!IsActive) return;

            ShowInsertionHandleAtCaret();
            if (tapWasReTap)
                ToggleContextMenuForCurrentSelection();
        }

        /// <summary>
        /// Double tap: select word under tap, show selection handles and context menu.
        /// </summary>
        private void HandleTouchDoubleTap(Vector2 screenPosition)
        {
            if (!IsActive) return;

            var camera = GetEventCamera();
            var cluster = HitTestCaretSource(screenPosition, camera);
            ResetForGesture();
            Selectable.DispatchDoubleTap(cluster);

            ResolveTapSelectionUI();
        }

        /// <summary>
        /// Triple tap: select entire line/paragraph, show selection handles and context menu.
        /// </summary>
        private void HandleTouchTripleTap(Vector2 screenPosition)
        {
            if (!IsActive) return;

            var camera = GetEventCamera();
            var cluster = HitTestCaretSource(screenPosition, camera);
            ResetForGesture();
            Selectable.DispatchTripleTap(cluster);

            ResolveTapSelectionUI();
        }

        /// <summary>
        /// Long press: show magnifier for precise cursor placement. On Android, a press on a
        /// word selects it (the platform convention); on iOS long-press only places the caret
        /// under the magnifier — word selection is reserved for the double-tap.
        /// </summary>
        private void HandleTouchLongPress(Vector2 screenPosition)
        {
            if (!IsActive)
                Activate(showKeyboard: false);

            HideInsertionHandle();
            HideContextMenu();

            magnifierImpl?.Show(screenPosition);

            var camera = GetEventCamera();
            var codepointIndex = HitTestCaretSource(screenPosition, camera, out var upstream);

            if (LongPressSelectsWord() && codepointIndex >= 0 && codepointIndex < codepointCount && HasLayout)
            {
                var buffers = TextComponent.Buffers;
                if (buffers.codepoints.count > 0)
                {
                    var cpIndex = Mathf.Min(DocumentToRendered(codepointIndex), buffers.codepoints.count - 1);
                    var charClass = SelectionWordBreak.Classify(buffers.codepoints[cpIndex]);
                    if (charClass != WordCharClass.Whitespace && charClass != WordCharClass.Punctuation)
                    {
                        ResetForGesture();
                        Selectable.DispatchDoubleTap(codepointIndex);
                        return;
                    }
                }
            }

            PlaceCaret(codepointIndex, upstream, SelectionChangeReason.Pointer);
        }

        private static bool LongPressSelectsWord()
            => Application.platform != RuntimePlatform.IPhonePlayer;

        /// <summary>
        /// Long press ended: hide magnifier, show selection handles and toolbar if a word was selected.
        /// Ends the drag session the long-press path may have begun — the recognizer reports a
        /// long-press drag through OnLongPressEnd, never OnDragEnd, so without this the
        /// <c>wordDragMode</c>/<c>isDragging</c> state armed by a double-tap leaks into the next gesture.
        /// </summary>
        private void HandleTouchLongPressEnd(Vector2 screenPosition)
        {
            isDragging = false;
            dragMode = DragMode.None;
            magnifierImpl?.Hide();
            Selectable.EndDrag();

            if (!Selection.IsCollapsed)
            {
                ShowSelectionUI();
            }
            else
            {
                ShowInsertionHandleAtCaret();
                ShowContextMenuForCurrentSelection();
            }
        }

        /// <summary>
        /// Drag start: begin character-by-character or word-by-word selection.
        /// </summary>
        private void HandleTouchDragStart(Vector2 screenPosition)
        {
            if (!IsActive) return;

            HideContextMenu();
            magnifierImpl?.Show(screenPosition);

            var camera = GetEventCamera();
            dragMode = DragMode.Text;
            isDragging = true;
            lastDragScreenPosition = screenPosition;
            lastDragCamera = camera;
            var codepointIndex = HitTestCaretSource(screenPosition, camera, out var upstream);

            if (!Selectable.IsWordDragMode)
            {
                ResetForGesture();
            }
            Selectable.BeginDrag(codepointIndex, Selectable.IsWordDragMode,
                upstream ? CaretAffinity.Upstream : CaretAffinity.Downstream);
        }

        /// <summary>
        /// Drag update: extend selection to current position.
        /// </summary>
        private void HandleTouchDragUpdate(Vector2 screenPosition)
        {
            if (!IsActive) return;

            magnifierImpl?.UpdatePosition(screenPosition);
            UpdatePointerDrag(DragMode.Text, screenPosition, GetEventCamera());
        }

        /// <summary>
        /// Records a live pointer drag (which endpoint it moves, and where) and applies it once. The same
        /// (mode, position) is re-applied by <see cref="DragAutoScroll"/> each frame the pointer is held
        /// past a viewport edge, so every drag — text, either selection handle, the caret handle — extends
        /// through the one smooth auto-scroll path instead of the instant <see cref="EnsureCaretVisible"/>
        /// jump.
        /// </summary>
        private void UpdatePointerDrag(DragMode mode, Vector2 screenPosition, Camera camera)
        {
            dragMode = mode;
            isDragging = true;
            lastDragScreenPosition = screenPosition;
            lastDragCamera = camera;
            ApplyDragTo(screenPosition, camera);
        }

        /// <summary>Applies the active <see cref="dragMode"/> at a screen position: moves that endpoint to the
        /// hit-tested caret and refreshes the affected touch UI. Shared by the live drag handlers and the
        /// per-frame auto-scroll re-application.</summary>
        private void ApplyDragTo(Vector2 screenPosition, Camera camera)
        {
            var cp = HitTestCaretSource(screenPosition, camera, out var upstream);
            var affinity = upstream ? CaretAffinity.Upstream : CaretAffinity.Downstream;
            switch (dragMode)
            {
                case DragMode.AnchorHandle:
                    Selectable.DragSelectionHandle(true, cp, Selection.Affinity);
                    MarkSelectionDirty();
                    UpdateSelectionHandlePositions();
                    break;
                case DragMode.FocusHandle:
                    Selectable.DragSelectionHandle(false, cp, affinity);
                    MarkSelectionDirty();
                    UpdateSelectionHandlePositions();
                    break;
                case DragMode.Caret:
                    PlaceCaret(cp, upstream, SelectionChangeReason.Pointer);
                    insertionHandleImpl?.UpdatePosition();
                    break;
                default:
                    Selectable.UpdateDrag(cp, affinity);
                    MarkSelectionDirty();
                    break;
            }
        }

        /// <summary>
        /// Drag end: finalize selection, hide magnifier, show handles if selection exists.
        /// </summary>
        private void HandleTouchDragEnd()
        {
            isDragging = false;
            dragMode = DragMode.None;
            magnifierImpl?.Hide();
            Selectable.EndDrag();

            if (!Selection.IsCollapsed)
            {
                ShowSelectionUI();
            }
            else
            {
                selectionHandlesImpl?.Hide();
            }
        }

        private void HandleTouchScrollDrag(Vector2 delta)
        {
            if (!CanScroll) return;

            HideInsertionHandle();
            HideContextMenu();
            scrollOffset += new Vector2(delta.x, delta.y);
            ClampScrollOffset();
            ApplyScrollOffset();
            RefreshCaretVisual();
            UpdateSelectionHandlePositions();
        }

        private void HandleTouchScrollDragEnd()
        {
            if (!Selection.IsCollapsed && IsSelectionVisibleInViewport())
                ShowContextMenuForCurrentSelection();
        }

        private bool IsSelectionVisibleInViewport()
        {
            if (Selection.IsCollapsed) return false;
            var vr = GetViewportRect();
            var startVP = TextRectToViewport(CaretRectAtSource(Selection.Start));
            var endVP = TextRectToViewport(CaretRectAtSource(Selection.End));
            float selTop = Mathf.Max(startVP.yMax, endVP.yMax);
            float selBottom = Mathf.Min(startVP.yMin, endVP.yMin);
            return selBottom < vr.yMax && selTop > vr.yMin;
        }

        /// <summary>
        /// Called when the user drags the anchor selection handle.
        /// </summary>
        private void HandleAnchorHandleDragged(Vector2 screenPosition)
        {
            if (!IsActive) return;

            magnifierImpl?.UpdatePosition(screenPosition);
            UpdatePointerDrag(DragMode.AnchorHandle, screenPosition, GetEventCamera());
        }

        /// <summary>
        /// Called when the user drags the focus selection handle.
        /// </summary>
        private void HandleFocusHandleDragged(Vector2 screenPosition)
        {
            if (!IsActive) return;

            magnifierImpl?.UpdatePosition(screenPosition);
            UpdatePointerDrag(DragMode.FocusHandle, screenPosition, GetEventCamera());
        }

        private void HandleInsertionHandleTapped()
        {
            if (!IsActive) return;
            ToggleContextMenuForCurrentSelection();
        }

        /// <summary>Any handle drag started: remember whether the menu was up and hide it for the drag.</summary>
        private void HandleHandleDragStarted()
        {
            if (!IsActive) return;
            menuVisibleBeforeHandleDrag = IsContextMenuVisible;
            if (IsContextMenuVisible)
                HideContextMenu();
        }

        /// <summary>Any handle drag ended: end the auto-scroll drag, then bring the menu back over the new
        /// selection only if it was up when the drag began.</summary>
        private void HandleHandleDragEnded()
        {
            isDragging = false;
            dragMode = DragMode.None;
            if (!IsActive) return;
            if (menuVisibleBeforeHandleDrag)
                ShowContextMenuForCurrentSelection();
            menuVisibleBeforeHandleDrag = false;
        }

        private void ResetForGesture()
        {
            undoStack.BreakCoalescing();
            desiredX = float.NaN;
        }

        private void ShowSelectionUI()
        {
            ShowSelectionHandlesForCurrentSelection();
            ShowContextMenuForCurrentSelection();
        }

        /// <summary>
        /// Resolves touch UI after a tap-driven selection attempt. A range shows the selection handles and
        /// menu; a collapsed result — a word/line tap on empty or word-less text where nothing could be
        /// selected, i.e. the caret did not move — keeps the insertion handle at the caret and TOGGLES the
        /// context menu (the tap summons or dismisses it, the platform "tap the caret" convention).
        /// </summary>
        private void ResolveTapSelectionUI()
        {
            if (Selection.IsCollapsed)
            {
                ShowInsertionHandleAtCaret();
                ToggleContextMenuForCurrentSelection();
            }
            else
            {
                HideInsertionHandle();
                ShowSelectionUI();
            }
        }

        private void ShowSelectionHandlesForCurrentSelection()
        {
            if (selectionHandlesImpl == null || Selection.IsCollapsed) return;
            selectionHandlesImpl.Show();
        }

        private void UpdateSelectionHandlePositions()
        {
            if (!isActive || selectionHandlesImpl == null || Selection.IsCollapsed) return;
            selectionHandlesImpl.UpdatePositions();
        }

        /// <summary>Shows the resolved context menu for the current selection; the capabilities decide which items apply.</summary>
        private void ShowContextMenuForCurrentSelection()
            => ShowContextMenu(GetContextMenuScreenPosition());

        /// <summary>Toggles the context menu for the current selection: hides it if this field is presenting
        /// it, shows it otherwise — so repeating whatever gesture summons the menu also dismisses it.</summary>
        private void ToggleContextMenuForCurrentSelection()
        {
            if (IsContextMenuVisible) HideContextMenu();
            else ShowContextMenuForCurrentSelection();
        }

        private Action<ContextMenuAction> contextMenuPresenter;

        /// <summary>Set by a menu action (Select All) that must keep the menu up: the item click hides the menu right after the action, so the menu is re-shown on the next selection pass instead.</summary>
        private bool reshowContextMenuPending;
        private bool touchSelectionUpdatePending;

        /// <summary>Whether the context menu was up when the current handle drag began; the menu reappears on release only if it was.</summary>
        private bool menuVisibleBeforeHandleDrag;

        /// <summary>Canvas whose density scales the gesture recognizer's dp thresholds on displays reporting no dpi.</summary>
        private Canvas GetGestureCanvas()
            => TextComponent != null ? TextComponent.canvas : null;

        /// <summary>Recognizer thresholds from the settings asset — re-read per event so inspector edits apply immediately.</summary>
        private static GestureThresholds GetGestureThresholds() => new()
        {
            DragSlopDp = UniTextSettings.DragSlopDp,
            MultiTapSlopDp = UniTextSettings.MultiTapSlopDp,
            MultiTapWindow = UniTextSettings.MultiTapWindow,
            LongPressDuration = UniTextSettings.LongPressDuration,
        };

        private bool IsContextMenuVisible => Selectable?.IsContextMenuVisible(this) == true;

        /// <summary>Shows the menu with this editable as the presenter — the menu routes actions to whichever field last showed it, so a shared menu never leaks actions to defocused editors.</summary>
        private void ShowContextMenu(Vector2 screenPosition)
        {
            if (Selectable == null) return;
            var capabilities = BuildContextMenuCapabilities();
            Selectable.PresentContextMenu(screenPosition, in capabilities,
                contextMenuPresenter ??= OnContextMenuAction, this);
        }

        private void HideContextMenu()
        {
            Selectable?.DismissContextMenu(this);
        }

        private void OnContextMenuAction(ContextMenuAction action)
        {
            switch (action)
            {
                case ContextMenuAction.Cut: Cut(); break;
                case ContextMenuAction.Copy: Copy(); break;
                case ContextMenuAction.Paste: DispatchPaste(plain: false); break;
                case ContextMenuAction.SelectAll: SelectAll(); reshowContextMenuPending = true; break;
            }
        }

        private ContextMenuCapabilities BuildContextMenuCapabilities()
        {
            var hasSelection = !Selection.IsCollapsed;
            var canCopy = hasSelection && IsCopyAllowed();
            var canCut = canCopy && !readOnly;
            var canPaste = !readOnly && UniTextClipboard.HasContent();
            var canSelectAll = codepointCount > 0
                && !(Selection.Start == 0 && Selection.End == codepointCount);
            return new ContextMenuCapabilities(canCut, canCopy, canPaste, canSelectAll, hasSelection);
        }

        /// <summary>Screen-space anchor for the context menu: above the selection midpoint, or above the caret.</summary>
        private Vector2 GetContextMenuScreenPosition()
        {
            Vector2 anchor;
            if (!Selection.IsCollapsed)
            {
                var start = GetCaretScreenPosition(Selection.Start, false);
                var end = GetCaretScreenPosition(Selection.End, false);
                anchor = new Vector2((start.x + end.x) * 0.5f, Mathf.Max(start.y, end.y));
            }
            else
            {
                anchor = GetCaretScreenPosition(Selection.Focus, false);
            }

            var view = GetViewportScreenRect();
            anchor.x = Mathf.Clamp(anchor.x, view.xMin, view.xMax);
            anchor.y = Mathf.Clamp(anchor.y, view.yMin, view.yMax);
            return anchor;
        }

        /// <summary>
        /// Hides all touch UI elements (handles, context menu, magnifier).
        /// </summary>
        private void HideAllTouchUI()
        {
            selectionHandlesImpl?.Hide();
            HideContextMenu();
            magnifierImpl?.Hide();
            HideInsertionHandle();
        }

        private void HandleInsertionHandleDragged(Vector2 screenPosition)
        {
            if (!IsActive) return;

            HideContextMenu();
            UpdatePointerDrag(DragMode.Caret, screenPosition, GetEventCamera());
        }

        private bool insertionHandleVisible;

        private void ShowInsertionHandleAtCaret()
        {
            if (insertionHandleImpl == null) return;
            insertionHandleImpl.Show();
            insertionHandleVisible = true;
        }

        private void HideInsertionHandle()
        {
            insertionHandleVisible = false;
            insertionHandleImpl?.Hide();
        }

        private void UpdateInsertionHandleIfVisible()
        {
            if (!isActive || !insertionHandleVisible || insertionHandleImpl == null) return;
            insertionHandleImpl.UpdatePosition();
        }

        /// <summary>
        /// Returns <see langword="true"/> if touch interaction should be used.
        /// Active on iOS and Android at runtime, in mobile WebGL browsers (the soft-keyboard
        /// path already targets them), and when touch simulation is enabled in the editor.
        /// </summary>
        private static bool ShouldUseTouchInteraction()
        {
#if UNITY_IOS || UNITY_ANDROID
            return !Application.isEditor || IsEditorTouchSimulation();
#elif UNITY_WEBGL && !UNITY_EDITOR
            return Application.isMobilePlatform;
#else
            return IsEditorTouchSimulation();
#endif
        }

        /// <summary>
        /// Returns <see langword="true"/> when the editor simulates touch input: the legacy
        /// input manager reports touch support with mouse-to-touch simulation enabled
        /// (the Device Simulator view does both). Always <see langword="false"/> in players
        /// and when the legacy input manager is unavailable.
        /// </summary>
        private static bool IsEditorTouchSimulation()
        {
#if UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            return Input.touchSupported && Input.simulateMouseWithTouches;
#else
            return false;
#endif
        }

        /// <summary>
        /// Context-menu entry from the core surface (<see cref="UniTextBase.ContextRequested"/>):
        /// right-click, plus touch / pen long-press when no touch gesture session owns the pointer
        /// (while the touch layer is active the editable claims the core long-press —
        /// <c>longPressClaimed</c> — and the recognizer's own long-press flow presents instead, so
        /// one hold never raises two menus). Windows convention: a click outside the current
        /// selection moves the caret there first; a click inside keeps the selection.
        /// </summary>
        private void HandleContextRequested(TextPointerEvent evt)
        {
            if (evt.Consumed) return;
            if (ShouldUseTouchInteraction())
            {
                evt.Consumed = true;
                return;
            }

            if (!IsActive) Activate();
            if (!IsActive) return;

            evt.Consumed = true;

            var cp = HitTestCaretSource(evt.ScreenPosition, evt.EventCamera, out var upstream);
            var sel = Selection;
            if (sel.IsCollapsed || cp < sel.Start || cp > sel.End)
                PlaceCaret(cp, upstream, SelectionChangeReason.Pointer);

            ShowContextMenu(evt.ScreenPosition);
        }

        /// <summary>
        /// Called when the field loses focus. Hides all touch UI.
        /// Invoked from <see cref="Deactivate"/>.
        /// </summary>
        private void OnTouchDefocused()
        {
            if (!touchActive) return;
            Selectable.EndDrag();
            HideAllTouchUI();
            touchGesture.Reset();
        }

        /// <summary>
        /// Called after selection changes from non-touch sources (keyboard, programmatic).
        /// Updates or hides touch UI to match the new selection state.
        /// </summary>
        private void OnTouchSelectionChanged()
        {
            if (!touchActive) return;

            if (!Selection.IsCollapsed)
            {
                HideInsertionHandle();
                if (touchGesture != null && (touchGesture.IsDragging || touchGesture.IsLongPressActive))
                    UpdateSelectionHandlePositions();
                else
                    ShowSelectionHandlesForCurrentSelection();

                if (reshowContextMenuPending)
                    ShowContextMenuForCurrentSelection();
            }
            else
            {
                selectionHandlesImpl?.Hide();
                HideContextMenu();
            }

            reshowContextMenuPending = false;
        }
    }
}
