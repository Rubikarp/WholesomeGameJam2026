using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// uGUI integration: the editor implements only the focus family (select / deselect /
    /// submit / cancel) and scroll — pointer and drag input arrives through
    /// <see cref="UniTextBase"/>'s event surface, the same single path the selection layer
    /// consumes. Whether used directly or inside a clipped field box, the editor's GameObject is the raycast
    /// target and the navigable control; sizing is measured through the
    /// <see cref="UniTextBase"/> component's own <see cref="ILayoutElement"/> implementation.
    /// </summary>
    public partial class UniTextEditable :
        IScrollHandler,
        ISelectHandler,
        IDeselectHandler,
        ISubmitHandler,
        ICancelHandler,
        IUpdateSelectedHandler
    {
        private RectTransform clipViewportCache;
        private bool clipCacheValid;

        /// <summary>
        /// True when the clipping mask is the editor's DIRECT parent — the field layout,
        /// where this editor owns scrolling and content sizing. Under a deeper hierarchy
        /// (ScrollRect Content between mask and text) an outer layout owns both, and the
        /// editor's own scroll / fit mutators stand down.
        /// </summary>
        private bool OwnsScrollViewport
        {
            get
            {
                EnsureClipCache();
                return clipViewportCache != null && ReferenceEquals(transform.parent, clipViewportCache);
            }
        }

        private void EnsureClipCache()
        {
            if (clipCacheValid) return;
            clipCacheValid = true;
            clipViewportCache = null;
            for (var p = transform.parent; p != null; p = p.parent)
            {
                if (p.GetComponent<RectMask2D>() == null) continue;
                clipViewportCache = p as RectTransform;
                break;
            }
        }

        private bool reclaimFocusPending;

        private void OnTransformParentChanged() => clipCacheValid = false;

        /// <summary>Engine-driven <see cref="OnLayoutInvalidated"/> trigger: window resize,
        /// rotation, split-screen, layout-group reflow all land here.</summary>
        private void OnRectTransformDimensionsChange()
        {
            if (ViewText == null || !isActiveAndEnabled) return;
            OnLayoutInvalidated();
        }

        /// <summary>
        /// Pointer events come from <see cref="UniTextBase"/>'s click surface; drags come from
        /// <see cref="UniTextSelectable"/>'s own drag surface (the base component no longer
        /// captures drags — plain labels must not steal them from a ScrollRect). The selectable
        /// re-raises every drag it receives while the editing layer is active; this layer routes
        /// them to the touch gesture recogniser or the desktop autoscroll tracker.
        /// </summary>
        private void SubscribeTextEvents()
        {
            var text = TextComponent;
            text.PointerPressed.Subscribe(OnTextPointerPressed, UniTextBase.ComponentDefaultEventOrder);
            text.PointerReleased += OnTextPointerReleased;
            text.PointerEntered += OnTextPointerEntered;
            text.PointerExited += OnTextPointerExited;
            text.ContextRequested.Subscribe(HandleContextRequested, UniTextBase.ComponentDefaultEventOrder);
            Selectable.SelectionDragStarted += OnTextDragStarted;
            Selectable.SelectionDragUpdated += OnTextDragUpdated;
            Selectable.SelectionDragEnded += OnTextDragEnded;
            Selectable.touchDragScrolls = HasScrollableOverflow;
            Selectable.onDragForwardedToParent = () => touchGesture?.Reset();
        }

        private void UnsubscribeTextEvents()
        {
            if (Selectable != null)
            {
                Selectable.touchDragScrolls = null;
                Selectable.onDragForwardedToParent = null;
            }
            var text = Selectable != null ? Selectable.TextComponent : null;
            if (text == null) return;
            text.PointerPressed.Unsubscribe(OnTextPointerPressed);
            text.PointerReleased -= OnTextPointerReleased;
            text.PointerEntered -= OnTextPointerEntered;
            text.PointerExited -= OnTextPointerExited;
            text.ContextRequested.Unsubscribe(HandleContextRequested);
            Selectable.SelectionDragStarted -= OnTextDragStarted;
            Selectable.SelectionDragUpdated -= OnTextDragUpdated;
            Selectable.SelectionDragEnded -= OnTextDragEnded;
        }

        /// <summary>
        /// Press: routes touch to the gesture recogniser; on desktop activates the editor and
        /// runs the shared press gesture (single → caret with hit affinity, double → word +
        /// word-drag, triple → line; Shift extends). Any press breaks undo coalescing and
        /// resets the vertical-movement column anchor.
        /// </summary>
        private void OnTextPointerPressed(TextPointerEvent evt)
        {
            if (evt.Consumed) return;
            if (touchActive)
            {
                touchPointerActive = true;
                Enroll();
                touchGesture.OnPointerDown(evt.ScreenPosition, Time.unscaledTime);
                return;
            }

            var cluster = HitTestCaretSource(evt.ScreenPosition, evt.EventCamera, out var upstream);
            undoStack.BreakCoalescing();
            desiredX = float.NaN;
            Selectable.HandlePressGesture(cluster, upstream, evt.ScreenPosition,
                (evt.Modifiers & PointerModifiers.Shift) != 0);
            MarkSelectionDirty();
            Activate();
        }

        private void OnTextPointerReleased(TextPointerEvent evt)
        {
            if (!touchActive)
                return;
            touchPointerActive = false;
            touchGesture.OnPointerUp(evt.ScreenPosition, Time.unscaledTime);
        }

        /// <summary>
        /// Drag start: feeds the touch gesture recogniser, or arms the desktop autoscroll
        /// tracker AND drives the selection — while the editing layer is active the selectable
        /// only emits the drag events, it does not drive <see cref="UniTextSelectable.BeginDrag"/>
        /// itself (the editing layer owns caret placement end-to-end). The press-anchored
        /// <see cref="TextPointerEvent.ScreenPosition"/> is exactly the right drag anchor.
        /// </summary>
        private void OnTextDragStarted(TextPointerEvent evt)
        {
            if (touchActive)
            {
                touchGesture.OnPointerMove(evt.ScreenPosition, Time.unscaledTime);
                return;
            }

            dragMode = DragMode.Text;
            isDragging = true;
            lastDragScreenPosition = evt.ScreenPosition;
            lastDragCamera = evt.EventCamera;
            var cp = HitTestCaretSource(evt.ScreenPosition, evt.EventCamera, out var upstream);
            Selectable.BeginDrag(cp, (evt.Modifiers & PointerModifiers.Shift) != 0,
                upstream ? CaretAffinity.Upstream : CaretAffinity.Downstream);
        }

        private void OnTextDragUpdated(TextPointerEvent evt)
        {
            if (touchActive)
            {
                touchGesture.OnPointerMove(evt.ScreenPosition, Time.unscaledTime);
                return;
            }
            if (!isDragging) return;

            UpdatePointerDrag(DragMode.Text, evt.ScreenPosition, evt.EventCamera);
        }

        private void OnTextDragEnded(TextPointerEvent evt)
        {
            if (touchActive) return;
            isDragging = false;
            dragMode = DragMode.None;
            Selectable.EndDrag();
        }

        private void OnTextPointerEntered(TextPointerEvent evt)
        {
            UniTextCursor.Set(CursorType.Text);
        }

        private void OnTextPointerExited(TextPointerEvent evt)
        {
            UniTextCursor.Set(CursorType.Default);
        }

        void IScrollHandler.OnScroll(PointerEventData eventData) => HandleScroll(eventData);

        /// <summary>
        /// EventSystem focus — pointer-select, navigation, or programmatic select — activates the editor.
        /// On touch this is suppressed: the EventSystem selects on pointer-DOWN, but focus must wait for the
        /// gesture recogniser's tap (press and release in place) so a press that turns into a scroll or a
        /// pass-through drag never focuses. Programmatic focus activates directly through <see cref="Activate"/>.
        /// </summary>
        void ISelectHandler.OnSelect(BaseEventData eventData)
        {
            if (touchActive) return;
            Activate();
        }

        /// <summary>
        /// EventSystem defocus deactivates the editor, committing any in-flight IME
        /// composition — unless the press landed on a <see cref="FocusGuard"/>
        /// hierarchy (toolbar), which keeps the editor active and reclaims the selection.
        /// </summary>
        void IDeselectHandler.OnDeselect(BaseEventData eventData)
        {
            if (!isActive) return;
            if (FocusGuard.PointerIsOverGuarded())
            {
                reclaimFocusPending = true;
                Enroll();
                return;
            }
            Deactivate();
        }

        /// <summary>EventSystem submit (gamepad / external keyboard): activates an unfocused editor, submits an active one.</summary>
        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (!isActive) { Activate(); return; }
            SubmitField();
        }

        /// <summary>EventSystem cancel (gamepad / Escape): raises Cancelled while active; the defocus policy is the integrator's.</summary>
        void ICancelHandler.OnCancel(BaseEventData eventData)
        {
            if (isActive) CancelField();
        }

        /// <summary>
        /// While editing on desktop, drains the frame's keyboard input at the selection-update stage and
        /// claims Enter / Escape so they reach the input session rather than the EventSystem submit /
        /// cancel handlers. Mobile leaves the path intact for gamepad / external-keyboard navigation.
        /// </summary>
        void IUpdateSelectedHandler.OnUpdateSelected(BaseEventData eventData)
        {
            if (!isActive || CanvasUtil.IsMobilePlatform()) return;
            UniTextNativeInput.FlushPendingInput();
            eventData.Use();
        }
    }
}
