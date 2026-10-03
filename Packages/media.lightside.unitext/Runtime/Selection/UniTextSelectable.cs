using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    /// <summary>
    /// Selection capability for <see cref="UniTextBase"/>: caret + range state with explicit
    /// affinity, gesture interpretation (multi-click promotion, drag-to-select, word-by-word
    /// drag, context-menu coordination), and highlight rendering. Sits on the same GameObject
    /// as <see cref="UniTextBase"/>; requires an existing <see cref="UniText"/> or
    /// <see cref="UniTextWorld"/> — <see cref="RequireComponent"/> validates the dependency
    /// but cannot auto-add the abstract base, so add the text component first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// First-class component, not a modifier. The misuse pattern (selection-as-Style, empty
    /// <c>OnApply</c>) is gone — selection is a component-level capability with its own
    /// lifecycle, distinct from text-range markup. Add this component for read-only selectable
    /// text; combine with <c>UniTextEditable</c> for editable input. See Component Composition
    /// Model in the roadmap (D-008, D-011).
    /// </para>
    /// <para>
    /// This component owns the uGUI drag surface for text. Drag policy: mouse / pen drags
    /// select; touch drags forward to the enclosing scroll container (the iOS / Android
    /// convention — selection on touch starts from long-press or double-tap, never from a
    /// plain drag) unless a selection gesture armed word-drag mode or the editing layer
    /// claimed touch drags for a focused field.
    /// </para>
    /// <para>
    /// Handles, magnifier, and context menu are owner-aware serialized entities. Their implementations
    /// choose the presentation mechanism; the component never instantiates UI itself.
    /// </para>
    /// <para>
    /// Multi-click thresholds default to desktop conventions (<see cref="MultiClickMaxInterval"/>,
    /// <see cref="MultiClickMaxDistance"/>). Single = caret, double = word, triple = paragraph.
    /// Touch-side multi-tap thresholds (different from desktop) are fed in via <c>DispatchTap</c> /
    /// <c>DispatchDoubleTap</c> / <c>DispatchTripleTap</c> by the gesture recogniser, which
    /// applies its own timing.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(UniTextBase))]
    [DisallowMultipleComponent]
    [AddComponentMenu(UniTextMenu.AddComponent.Selectable)]
    public sealed partial class UniTextSelectable : MonoBehaviour,
        IPointerMoveHandler,
        IPointerExitHandler,
        ISelectHandler,
        IDeselectHandler,
        IInitializePotentialDragHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {

        /// <summary>
        /// Selection highlight presentation emitted through the text component's shared range-decoration
        /// host. It uses the same paint, mapping and geometry contract as <see cref="HighlightModifier"/>.
        /// </summary>
        [SerializeField, StateProperty(nameof(ApplySelectionHighlightChange))]
        [Tooltip("Presentation of the live selection highlight.")]
        private HighlightPresentation selectionHighlight = HighlightPresentation.SelectionDefault();

        /// <summary>
        /// Touch handle entity. It may implement <see cref="ISelectionHandles"/>,
        /// <see cref="IInsertionHandle"/>, or both; <see langword="null"/> shows no handles.
        /// The sibling <see cref="UniTextEditable"/>, when present, drives it.
        /// </summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplySelectionHandlesChange), Owned = true)]
        [Tooltip("Selection and/or insertion handle entity for touch interaction. Unassigned = no handles.")]
        private ITouchHandles selectionHandles = new PrefabSelectionHandles();

        /// <summary>
        /// Touch magnifier entity. The implementation owns how it is presented;
        /// <see langword="null"/> shows no magnifier.
        /// </summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyMagnifierChange), Owned = true)]
        [Tooltip("Magnifier entity used during long-press placement and handle dragging. Unassigned = no magnifier.")]
        private IMagnifier magnifier = new PrefabMagnifier();

        /// <summary>Occurs when <see cref="SelectionHandles"/> or <see cref="Magnifier"/> changed, so a
        /// driving <see cref="UniTextEditable"/> can rebind its handle subscriptions.</summary>
        internal event System.Action TouchUISlotsChanged;

        private UniTextBase uniText;
        private SelectionHighlightLayer selectionLayer;
        private bool entitiesAttached;

        /// <summary>
        /// Set while a <see cref="UniTextEditable"/> on the same GameObject is enabled. The editing
        /// layer then owns pointer focus, caret placement, defocus, cursor affordance, and keyboard
        /// handling — this component's own standalone handlers stand down to avoid double dispatch.
        /// </summary>
        internal bool editingLayerActive;

        /// <summary>
        /// Set by the editing layer: queried on a plain touch drag (no word-drag armed) to decide whether
        /// this field captures the drag to scroll its own clipped content — <see langword="true"/> only when
        /// the field owns a viewport AND its content actually overflows it. Focus is irrelevant: a field with
        /// nothing to scroll never captures, so the drag reaches an enclosing pan / scroll container.
        /// Null = never scroll-captures.
        /// </summary>
        internal Func<bool> touchDragScrolls;

        /// <summary>
        /// Set by the editing layer: invoked when a touch drag is routed to the enclosing container instead
        /// of this field. The field's own gesture recogniser must abandon the gesture here — it is not fed
        /// the drag's moves (those flow only on the local route), so without this its long-press timer keeps
        /// ticking and fires a word selection mid-pan while the drag actually belongs to the parent.
        /// </summary>
        internal Action onDragForwardedToParent;

        private bool focusSession;
        private bool reclaimFocusPending;

        private TextSelection state;

        private TapChain clickChain;

        private bool isDragging;

        private bool wordDragMode;
        private int wordDragAnchorStart;
        private int wordDragAnchorEnd;

        private enum DragRoute : byte { None, Local, Parent, Gesture }

        private DragRoute dragRoute;
        private GameObject parentDragReceiver;
        private PointerKind dragPointerKind;
        private readonly TextPointerEvent dragEventScratch = new();

        /// <summary>
        /// Maximum interval between consecutive primary clicks for multi-click detection
        /// (seconds). Defaults to 0.5 s — the Windows (<c>GetDoubleClickTime</c>) and macOS
        /// default. Assign to match a user-configured OS value.
        /// </summary>
        public static float MultiClickMaxInterval
        {
            get => UniTextSettings.MultiClickInterval;
            set => UniTextSettings.MultiClickInterval = value;
        }

        /// <summary>
        /// Maximum distance between consecutive primary clicks for multi-click detection, in
        /// density-independent pixels (Android dp, 160 dpi baseline) — the comparison scales by
        /// the display density at use so the physical slop stays constant on high-density
        /// displays. Defaults to 8.
        /// </summary>
        public static float MultiClickMaxDistance
        {
            get => UniTextSettings.MultiClickSlopDp;
            set => UniTextSettings.MultiClickSlopDp = value;
        }

        /// <summary>
        /// The display component this selection layer operates over. Same GameObject;
        /// lazily resolved via <see cref="GetComponent{T}"/> on first access so layout /
        /// preferred-height queries from the Unity layout system work identically in edit
        /// mode and play mode (the runtime path is the single path — no edit-time branch).
        /// </summary>
        public UniTextBase TextComponent
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (uniText == null) uniText = GetComponent<UniTextBase>();
                return uniText;
            }
        }

        /// <summary>Current selection state (anchor / focus / affinity). Immutable.</summary>
        public TextSelection Selection => state;

        /// <summary>
        /// True while word-by-word drag is active (started by a double-click on text). Drag
        /// extension snaps to whole-word boundaries instead of individual codepoints. Read
        /// by the touch gesture pipeline to decide whether to start a fresh caret-drag or
        /// continue extending the word selection.
        /// </summary>
        public bool IsWordDragMode => wordDragMode;

        private OrderedValueEvent<SelectionChangingArgs> selectionChanging;

        /// <summary>
        /// Occurs when <see cref="Selection"/> is about to change through
        /// any of the public mutators (<see cref="SetCaret"/> / <see cref="SetSelection"/> /
        /// <see cref="ExtendSelection"/> / Select* / Click+drag dispatchers). Subscribers may
        /// set <see cref="SelectionChangingArgs.Cancel"/> to abort, or assign
        /// <see cref="SelectionChangingArgs.Proposed"/> to clamp into a permitted range
        /// (P2.A atomic-token boundary, integrator-defined read-only ranges). Side-effect
        /// movements driven by text mutation (insert / delete / IME commit / undo) bypass
        /// this event by design — they go through an internal non-vetoable path. See
        /// <see cref="SelectionChangingArgs"/>. Callbacks run by ascending order and equal orders
        /// retain subscription order.
        /// </summary>
        public OrderedValueEvent<SelectionChangingArgs> SelectionChanging
            => selectionChanging ??= new OrderedValueEvent<SelectionChangingArgs>();

        /// <summary>
        /// Post-change notification with previous and current state plus a hierarchical
        /// <c>UserEvent</c> string. See <see cref="SelectionChangedArgs"/> for category
        /// strings.
        /// </summary>
        public event Action<SelectionChangedArgs> SelectionChanged;

        /// <summary>
        /// Occurs when a pointer drag has been routed to selection (mouse / pen always; touch only
        /// in an armed selection gesture — see the class drag policy). The event is anchored
        /// at the press position, not the position where the drag threshold was crossed.
        /// The instance is reused across emissions and <see cref="TextPointerEvent.Hit"/> is
        /// not computed — hit-test from <see cref="TextPointerEvent.ScreenPosition"/>.
        /// </summary>
        public event Action<TextPointerEvent> SelectionDragStarted;

        /// <inheritdoc cref="SelectionDragStarted"/>
        public event Action<TextPointerEvent> SelectionDragUpdated;

        /// <inheritdoc cref="SelectionDragStarted"/>
        public event Action<TextPointerEvent> SelectionDragEnded;

        /// <summary>
        /// Menu entity presented on a context-menu request. It owns its presentation and may use
        /// Unity UI, native platform UI, or another mechanism. <see langword="null"/> leaves presentation
        /// to <see cref="ContextMenuRequested"/> subscribers.
        /// </summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyContextMenuChange), Owned = true)]
        [Tooltip("Context-menu entity. Unassigned = handle ContextMenuRequested externally.")]
        private ITextContextMenu contextMenu = new PrefabTextContextMenu();

        /// <summary>
        /// Occurs when the user has requested a context menu (right-click, long-press) at a screen position —
        /// after the assigned <see cref="ContextMenu"/> (if any) is shown. Subscribe to present a custom menu.
        /// </summary>
        public event Action<Vector2> ContextMenuRequested;

        /// <summary>Occurs when an open context menu should hide (drag, scroll, edit, focus loss).</summary>
        public event Action ContextMenuDismissRequested;

        private void Awake()
        {
            uniText = GetComponent<UniTextBase>();
        }

        private void OnEnable()
        {
            ValidateSerializedState();
            if (Application.isPlaying) AttachEntities();
            if (selectionLayer != null && selectionLayer.IsAlive)
                selectionLayer.Presentation = selectionHighlight;
            SubscribeToText();
            UniTextFocusable.Sync(gameObject);
            if (!state.IsCollapsed)
                RenderSelection();
            if (!editingLayerActive && EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject == gameObject)
                BeginFocusSession();
        }

        private void OnDisable()
        {
            var wasFocused = focusSession;
            EndFocusSession();
            UnsubscribeFromText();
            ClearHighlight();
            ResetGestureState();
            if (wasFocused) DismissContextMenu();
            DetachEntities();
            UniTextFocusable.Sync(gameObject);
        }

        private void OnDestroy()
        {
            DetachEntities();
            DisposeSelectionLayer();
        }

        private void ApplySelectionHighlightChange(HighlightPresentation previous,
            ref HighlightPresentation current)
        {
            if (current == null)
            {
                current = previous;
                throw new ArgumentNullException(nameof(SelectionHighlight));
            }
            if (selectionLayer != null && selectionLayer.IsAlive)
                selectionLayer.Presentation = current;
        }

        private void ApplySelectionHandlesChange(ITouchHandles previous, ITouchHandles current)
        {
            ApplyEntityChange(previous, current);
            TouchUISlotsChanged?.Invoke();
        }

        private void ApplyMagnifierChange(IMagnifier previous, IMagnifier current)
        {
            ApplyEntityChange(previous, current);
            TouchUISlotsChanged?.Invoke();
        }

        private void ApplyContextMenuChange(ITextContextMenu previous, ITextContextMenu current)
        {
            ApplyEntityChange(previous, current);
            contextMenuPresentationOwner = null;
            if (entitiesAttached) ContextMenuDismissRequested?.Invoke();
        }

        private void ValidateSerializedState()
        {
            if (selectionHighlight == null)
                throw new InvalidOperationException("UniTextSelectable requires a selection highlight presentation.");
        }

        private void AttachEntities()
        {
            if (entitiesAttached) return;
            try
            {
                selectionHandles?.Attach(this);
                magnifier?.Attach(this);
                contextMenu?.Attach(this);
                entitiesAttached = true;
            }
            catch
            {
                contextMenu?.Detach();
                magnifier?.Detach();
                selectionHandles?.Detach();
                throw;
            }
        }

        private void DetachEntities()
        {
            if (!entitiesAttached) return;
            entitiesAttached = false;
            contextMenu?.Detach();
            magnifier?.Detach();
            selectionHandles?.Detach();
        }

        private void ApplyEntityChange(ISelectableEntity previous, ISelectableEntity current)
        {
            if (!entitiesAttached) return;
            previous?.Detach();
            try
            {
                current?.Attach(this);
            }
            catch
            {
                previous?.Attach(this);
                throw;
            }
        }

        /// <summary>
        /// Inverse of the host's document→rendered mapping (rendered offset + <see cref="MarkupViewStick"/> → document).
        /// Word, line, and paragraph ranges are computed in rendered space and mapped back to document codepoints.
        /// </summary>
        internal System.Func<int, MarkupViewStick, int> visibleToSource;

        internal void RenderSelection()
        {
            if (state.IsCollapsed)
            {
                selectionLayer?.Clear();
                return;
            }
            var layer = EnsureSelectionLayer();
            if (layer == null) return;
            var map = uniText.DocumentToRendered;
            var start = map != null ? map(state.Start) : state.Start;
            var end = map != null ? map(state.End) : state.End;
            layer.SetRange(start, end);
        }

        private SelectionHighlightLayer EnsureSelectionLayer()
        {
            if (selectionLayer != null && selectionLayer.IsAlive) return selectionLayer;
            return selectionLayer = new SelectionHighlightLayer(TextComponent, selectionHighlight);
        }

        /// <summary>
        /// Collapses the selection to a single caret at <paramref name="codepointIndex"/>
        /// with the given <paramref name="affinity"/>. Out-of-range indices are clamped to
        /// <c>[0, codepointCount]</c>.
        /// </summary>
        public bool SetCaret(int codepointIndex, CaretAffinity affinity = CaretAffinity.Downstream,
            string userEvent = SelectionChangeReason.Programmatic)
        {
            codepointIndex = Clamp(codepointIndex);
            return ApplySelection(new TextSelection(codepointIndex, codepointIndex, affinity), userEvent);
        }

        /// <summary>
        /// Sets both endpoints simultaneously. Out-of-range indices are clamped to
        /// <c>[0, codepointCount]</c>.
        /// </summary>
        public bool SetSelection(int anchor, int focus, CaretAffinity affinity = CaretAffinity.Downstream,
            string userEvent = SelectionChangeReason.Programmatic)
        {
            anchor = Clamp(anchor);
            focus = Clamp(focus);
            return ApplySelection(new TextSelection(anchor, focus, affinity), userEvent);
        }

        /// <summary>
        /// Moves the focus while keeping the anchor fixed. Used for shift-click and drag
        /// extension.
        /// </summary>
        public bool ExtendSelection(int newFocus, CaretAffinity affinity = CaretAffinity.Downstream,
            string userEvent = SelectionChangeReason.Extend)
        {
            newFocus = Clamp(newFocus);
            return ApplySelection(new TextSelection(state.Anchor, newFocus, affinity), userEvent);
        }

        /// <summary>
        /// Moves one selection endpoint to <paramref name="codepointIndex"/> with the touch
        /// selection-handle contract (iOS / Android): the selection never collapses — when the
        /// dragged endpoint lands on the fixed one it is clamped one grapheme cluster away —
        /// and dragging past the fixed endpoint is allowed, inverting the anchor / focus
        /// orientation (the handles swap roles). <paramref name="draggingAnchor"/> selects
        /// which endpoint moves; the other stays fixed.
        /// </summary>
        public bool DragSelectionHandle(bool draggingAnchor, int codepointIndex,
            CaretAffinity affinity = CaretAffinity.Downstream)
        {
            codepointIndex = Clamp(codepointIndex);
            var fixedEndpoint = draggingAnchor ? state.Focus : state.Anchor;
            if (codepointIndex == fixedEndpoint)
            {
                var moving = draggingAnchor ? state.Anchor : state.Focus;
                codepointIndex = StepClusterAside(fixedEndpoint, keepBefore: moving < fixedEndpoint);
                if (codepointIndex == fixedEndpoint) return false;
            }

            var proposed = draggingAnchor
                ? new TextSelection(codepointIndex, state.Focus, affinity)
                : new TextSelection(state.Anchor, codepointIndex, affinity);
            return ApplySelection(proposed, SelectionChangeReason.Extend);
        }

        /// <summary>
        /// One grapheme cluster to the side of <paramref name="sourceIndex"/>, computed in
        /// rendered space (where cluster data lives) and mapped back to source codepoints.
        /// Flips direction at the text edges so the result is always a valid non-collapsing
        /// endpoint.
        /// </summary>
        private int StepClusterAside(int sourceIndex, bool keepBefore)
        {
            var buffers = uniText != null ? uniText.Buffers : null;
            var visibleCount = buffers?.codepoints.count ?? 0;
            if (visibleCount == 0) return sourceIndex;

            var breaks = buffers.GraphemeBreaksOrEmpty;

            var v = ToVisiblePos(sourceIndex);
            if (v < 0) v = 0;
            if (v > visibleCount) v = visibleCount;

            if (keepBefore && v == 0) keepBefore = false;
            else if (!keepBefore && v >= visibleCount) keepBefore = true;

            var stepped = keepBefore
                ? (breaks.IsEmpty ? v - 1 : GraphemeNavigator.PreviousGraphemeCluster(breaks, v))
                : (breaks.IsEmpty ? v + 1 : GraphemeNavigator.NextGraphemeCluster(breaks, v));
            if (stepped < 0) stepped = 0;
            if (stepped > visibleCount) stepped = visibleCount;

            return visibleToSource != null
                ? visibleToSource(stepped, keepBefore ? MarkupViewStick.Before : MarkupViewStick.After)
                : stepped;
        }

        private int ToVisiblePos(int source) => uniText.DocumentToRendered is { } map ? map(source) : source;

        private (int start, int end) ToSourceRange(int visibleStart, int visibleEnd)
            => visibleToSource == null
                ? (visibleStart, visibleEnd)
                : (visibleToSource(visibleStart, MarkupViewStick.Before), visibleToSource(visibleEnd, MarkupViewStick.After));

        private int HitToSource(int visibleCluster)
            => visibleToSource != null ? visibleToSource(visibleCluster, MarkupViewStick.Before) : visibleCluster;

        /// <summary>Selects the word containing <paramref name="codepointIndex"/>.</summary>
        public bool SelectWord(int codepointIndex)
            => SelectVisibleRange(SelectionWordBreak.GetWordRange(uniText, ToVisiblePos(codepointIndex)), SelectionChangeReason.Word);

        /// <summary>
        /// Selects the visual (soft-wrapped) line containing <paramref name="codepointIndex"/>.
        /// Gesture pipelines use <see cref="SelectParagraph"/> for triple-click / triple-tap;
        /// this stays for consumers that genuinely want the visual row.
        /// </summary>
        public bool SelectLine(int codepointIndex)
            => SelectVisibleRange(SelectionWordBreak.GetLineRange(uniText, ToVisiblePos(codepointIndex)), SelectionChangeReason.Line);

        /// <summary>
        /// Selects the paragraph (between hard line breaks) containing
        /// <paramref name="codepointIndex"/>.
        /// </summary>
        public bool SelectParagraph(int codepointIndex)
            => SelectVisibleRange(SelectionWordBreak.GetParagraphRange(uniText, ToVisiblePos(codepointIndex)), SelectionChangeReason.Paragraph);

        private bool SelectVisibleRange((int start, int end) visibleRange, string reason)
        {
            var (s, e) = ToSourceRange(visibleRange.start, visibleRange.end);
            return SetSelection(s, e, CaretAffinity.Downstream, reason);
        }

        /// <summary>Selects the entire text.</summary>
        public bool SelectAll()
        {
            return SetSelection(0, CodepointCount, CaretAffinity.Downstream, SelectionChangeReason.All);
        }

        /// <summary>Collapses the selection at the current focus.</summary>
        public bool ClearSelection(string userEvent = SelectionChangeReason.Programmatic)
        {
            if (state.IsCollapsed) return false;
            return ApplySelection(new TextSelection(state.Focus, state.Focus, state.Affinity), userEvent);
        }

        /// <summary>
        /// Returns the selected text as a new string. Empty string when collapsed. Reads the
        /// editing layer's document when present (authoritative during IME composition and
        /// password masking), else the rendered codepoint buffer.
        /// </summary>
        public string GetSelectedText()
        {
            if (state.IsCollapsed) return string.Empty;

            if (document != null)
            {
                var sel = state.Clamp(document.CodepointCount);
                if (sel.Length == 0) return string.Empty;
                var buf = new char[sel.Length * 2];
                var len = document.CopyCodepointRange(sel.Start, sel.Length, buf);
                return len > 0 ? new string(buf, 0, len) : string.Empty;
            }

            var buffers = uniText?.Buffers;
            if (buffers == null || buffers.codepoints.count == 0) return string.Empty;

            var start = state.Start;
            var end = state.End;
            if (start < 0) start = 0;
            if (end > buffers.codepoints.count) end = buffers.codepoints.count;
            if (end <= start) return string.Empty;

            var chars = new char[(end - start) * 2];
            var written = 0;
            for (int i = start; i < end; i++)
                written += UnicodeData.EncodeUtf16(buffers.codepoints[i], chars, written);
            return new string(chars, 0, written);
        }

        /// <summary>
        /// Resets all transient gesture state. Call on focus loss so a stale click count or
        /// drag mode does not bleed into the next focus session.
        /// </summary>
        public void ResetGestureState()
        {
            clickChain.Reset();
            isDragging = false;
            wordDragMode = false;
            wordDragAnchorStart = 0;
            wordDragAnchorEnd = 0;
            dragRoute = DragRoute.None;
            parentDragReceiver = null;
        }

        /// <summary>
        /// Desktop primary-press gesture. Multi-click promotion runs on PRESS, not release:
        /// single press places the caret (with the hit's <paramref name="upstream"/> affinity),
        /// the second press selects the word and arms word-by-word drag, the third selects the
        /// paragraph — the browser / macOS text-system convention (visual-row selection stays
        /// available via <see cref="SelectLine"/>). Promoting on press is what lets a drag begun
        /// from the second press extend by whole words. <paramref name="shift"/> extends the
        /// existing focus instead of counting.
        /// </summary>
        internal void HandlePressGesture(int caretCluster, bool upstream, Vector2 screenPosition, bool shift)
        {
            var affinity = upstream ? CaretAffinity.Upstream : CaretAffinity.Downstream;

            if (shift)
            {
                ExtendSelection(caretCluster, affinity, SelectionChangeReason.Extend);
                clickChain.Reset();
                return;
            }

            var count = clickChain.Advance(screenPosition, Time.unscaledTime,
                MultiClickMaxInterval,
                GestureMetrics.SlopPx(MultiClickMaxDistance, TextComponent != null ? TextComponent.canvas : null));

            if (count >= 3)
            {
                SelectParagraph(caretCluster);
                clickChain.Reset();
            }
            else if (count == 2)
            {
                SelectWord(caretCluster);
                ArmWordDrag();
            }
            else
            {
                wordDragMode = false;
                SetCaret(caretCluster, affinity, SelectionChangeReason.Pointer);
            }
        }

        private void ArmWordDrag()
        {
            wordDragMode = true;
            wordDragAnchorStart = state.Start;
            wordDragAnchorEnd = state.End;
        }

        /// <summary>
        /// Programmatic primary-click driver — runs the same press gesture the pointer
        /// pipeline uses (<see cref="HandlePressGesture"/>): single places the caret, double
        /// selects the word, triple the paragraph; <see cref="PointerModifiers.Shift"/> extends.
        /// Affinity defaults downstream — the bounding-box <see cref="TextHitResult"/> carries none.
        /// </summary>
        public void HandlePrimaryClick(TextHitResult hit, Vector2 screenPosition,
            PointerModifiers modifiers = PointerModifiers.None)
        {
            if (!hit.hit) return;
            HandlePressGesture(hit.cluster, false, screenPosition, (modifiers & PointerModifiers.Shift) != 0);
        }

        /// <summary>
        /// Drives selection from a touch-side single tap (caret placement). Bypasses the
        /// desktop multi-click counter so the touch gesture recogniser can apply its own
        /// thresholds.
        /// </summary>
        public void DispatchTap(int codepointIndex)
            => SetCaret(codepointIndex, CaretAffinity.Downstream, SelectionChangeReason.Pointer);

        /// <summary>
        /// Drives selection from a touch-side double tap (word selection) and arms
        /// <see cref="IsWordDragMode"/>.
        /// </summary>
        public void DispatchDoubleTap(int codepointIndex)
        {
            SelectWord(codepointIndex);
            ArmWordDrag();
        }

        /// <summary>Drives selection from a touch-side triple tap (paragraph / line).</summary>
        public void DispatchTripleTap(int codepointIndex)
        {
            SelectParagraph(codepointIndex);
            wordDragMode = false;
        }

        /// <summary>
        /// Begins a drag-to-select operation at <paramref name="codepointIndex"/>. When
        /// <paramref name="extendOnly"/> is <see langword="true"/>, the existing selection's
        /// anchor is preserved (used by long-press → drag flow).
        /// </summary>
        public void BeginDrag(int codepointIndex, bool extendOnly = false,
            CaretAffinity affinity = CaretAffinity.Downstream)
        {
            isDragging = true;
            if (!extendOnly && !wordDragMode)
                SetCaret(codepointIndex, affinity, SelectionChangeReason.Pointer);
        }

        /// <summary>
        /// Updates a drag-in-progress to <paramref name="codepointIndex"/>. In word-drag
        /// mode, extends by whole-word boundaries; otherwise extends by single codepoints.
        /// </summary>
        public void UpdateDrag(int codepointIndex, CaretAffinity affinity = CaretAffinity.Downstream)
        {
            if (!isDragging) return;

            if (wordDragMode)
                ExtendSelectionByWord(codepointIndex);
            else
                ExtendSelection(codepointIndex, affinity, SelectionChangeReason.Extend);
        }

        /// <summary>Ends an active drag-to-select operation.</summary>
        public void EndDrag()
        {
            isDragging = false;
            wordDragMode = false;
        }

        private Action<ContextMenuAction> contextMenuPresenter;
        private object contextMenuPresentationOwner;

        /// <summary>Shows the assigned <see cref="ContextMenu"/> (if any) with this component as the presenter, then raises <see cref="ContextMenuRequested"/>.</summary>
        public void RequestContextMenu(Vector2 screenPosition)
        {
            var cpCount = CodepointCount;
            PresentContextMenu(screenPosition, new ContextMenuCapabilities(
                    canCut: false,
                    canCopy: !Selection.IsCollapsed,
                    canPaste: false,
                    canSelectAll: cpCount > 0 && !(Selection.Start == 0 && Selection.End == cpCount),
                    hasSelection: !Selection.IsCollapsed),
                contextMenuPresenter ??= OnContextMenuAction, this);
        }

        internal bool PresentContextMenu(Vector2 screenPosition,
            in ContextMenuCapabilities capabilities, Action<ContextMenuAction> presenter,
            object presentationOwner)
        {
            contextMenu?.Show(screenPosition, in capabilities, presenter);
            contextMenuPresentationOwner = contextMenu?.IsVisible == true
                ? presentationOwner
                : null;
            ContextMenuRequested?.Invoke(screenPosition);
            return IsContextMenuVisible(presentationOwner);
        }

        internal bool IsContextMenuVisible(object presentationOwner)
            => ReferenceEquals(contextMenuPresentationOwner, presentationOwner) &&
               contextMenu?.IsVisible == true;

        /// <summary>
        /// Standalone (read-only selectable) menu actions: Copy / SelectAll only. Runs only while
        /// this component is the menu's recorded presenter — a sibling editable presents the menu
        /// itself with its own presenter, so a shared menu never double-runs an action.
        /// </summary>
        private void OnContextMenuAction(ContextMenuAction action)
        {
            switch (action)
            {
                case ContextMenuAction.Copy: CopyToClipboard(); break;
                case ContextMenuAction.SelectAll: SelectAll(); break;
            }
        }

        /// <summary>Hides the assigned <see cref="ContextMenu"/> (if any), then raises <see cref="ContextMenuDismissRequested"/>.</summary>
        public void DismissContextMenu()
        {
            contextMenuPresentationOwner = null;
            contextMenu?.Hide();
            ContextMenuDismissRequested?.Invoke();
        }

        internal void DismissContextMenu(object presentationOwner)
        {
            if (IsContextMenuVisible(presentationOwner)) DismissContextMenu();
        }

        /// <summary>
        /// Repaints the highlight rects backing the selection. Called automatically after
        /// each selection change and after the host commits a relayout; expose for consumers
        /// that reposition the text out-of-band (manual scroll offsets) without changing
        /// <see cref="Selection"/>.
        /// </summary>
        public void RefreshHighlight()
        {
            RenderSelection();
            selectionLayer?.Repaint();
        }

        /// <summary>
        /// Editing-pipeline entry point: applies a new selection without going through the
        /// vetoable <see cref="SelectionChanging"/> chain. Used when the selection moves
        /// as a side-effect of text mutation (insert / delete / IME commit / undo) — the
        /// integrator's veto applies to the edit, not to the resulting caret reposition.
        /// </summary>
        internal void SetSelectionInternal(TextSelection newValue, string userEvent)
        {
            var previous = state;
            if (previous == newValue) return;

            state = newValue;
            EmitChanged(previous, userEvent);
        }

        /// <summary>
        /// Editing-pipeline entry point: clamps the selection to <paramref name="codepointCount"/>
        /// after a buffer mutation. Fires <see cref="SelectionChanged"/> with
        /// <c>UserEvent = SelectionChangeReason.Clamp</c> if the selection actually shifted.
        /// </summary>
        internal void ClampToBuffer(int codepointCount)
        {
            var clamped = state.Clamp(codepointCount);
            if (clamped == state) return;
            SetSelectionInternal(clamped, SelectionChangeReason.Clamp);
        }

        private bool ApplySelection(TextSelection proposed, string userEvent)
        {
            if (proposed == state) return false;

            if (selectionChanging?.HasSubscribers == true)
            {
                var args = new SelectionChangingArgs(state, proposed, userEvent);
                selectionChanging.Invoke(args);
                if (args.Cancel) return false;
                proposed = args.Proposed;
                if (proposed == state) return false;
            }

            var previous = state;
            state = proposed;
            EmitChanged(previous, userEvent);
            return true;
        }

        private void EmitChanged(TextSelection previous, string userEvent)
        {
            RenderSelection();
            if (SelectionChanged != null)
                SelectionChanged.Invoke(new SelectionChangedArgs(previous, state, userEvent));
        }

        private void ClearHighlight()
        {
            DisposeSelectionLayer();
        }

        private void DisposeSelectionLayer()
        {
            selectionLayer?.Dispose();
            selectionLayer = null;
        }

        private int Clamp(int codepointIndex)
        {
            var cpCount = CodepointCount;
            if (codepointIndex < 0) return 0;
            if (codepointIndex > cpCount) return cpCount;
            return codepointIndex;
        }

        /// <summary>
        /// The document the selection indexes into — the editing layer's gap buffer when
        /// present (set by <see cref="UniTextEditable"/> on enable), else the rendered
        /// buffers. Keeps selection clamping in document space during IME composition and
        /// password masking, when rendered and document counts diverge.
        /// </summary>
        internal ITextDocument document;

        internal int CodepointCount => document?.CodepointCount ?? uniText?.Buffers?.codepoints.count ?? 0;

        private void SubscribeToText()
        {
            if (TextComponent == null)
            {
                Debug.LogWarning(
                    "[UniText] UniTextSelectable found no text component — add a UniText or UniTextWorld " +
                    "to this GameObject first (RequireComponent cannot auto-add the abstract UniTextBase).",
                    this);
                return;
            }
            uniText.PointerPressed.Subscribe(OnPointerPressed, UniTextBase.ComponentDefaultEventOrder);
            uniText.ContextRequested.Subscribe(OnContextRequested, UniTextBase.ComponentDefaultEventOrder);
            uniText.Committed += OnCommitted;
        }

        private void UnsubscribeFromText()
        {
            if (uniText == null) return;
            uniText.PointerPressed.Unsubscribe(OnPointerPressed);
            uniText.ContextRequested.Unsubscribe(OnContextRequested);
            uniText.Committed -= OnCommitted;
            uniText.FrameUpdated -= ReclaimFocusIfPending;
            reclaimFocusPending = false;
        }

        /// <summary>
        /// Keeps a standalone selection coherent with the text it indexes: after the host
        /// commits a relayout (resize, rewrap, font swap, programmatic text change) the
        /// selection is clamped to the new length and the highlight rects are re-baked from
        /// fresh glyph geometry. The editing layer owns this path when present.
        /// </summary>
        private void OnCommitted(UniTextCommitChanges changes)
        {
            if (editingLayerActive ||
                (changes & UniTextCommitChanges.GlyphGeometry) == 0) return;
            ClampToBuffer(CodepointCount);
            RenderSelection();
        }

        /// <summary>
        /// Standalone press (the editing layer owns this when present): takes EventSystem focus,
        /// then runs the press gesture — single press collapses to a caret at the press point,
        /// the second selects the word, the third the paragraph (<see cref="HandlePressGesture"/>).
        /// </summary>
        private void OnPointerPressed(TextPointerEvent evt)
        {
            if (editingLayerActive || evt.Consumed) return;

            TakeEventSystemFocus();

            var cluster = HitToSource(uniText.HitTestCaret(evt.ScreenPosition, evt.EventCamera, out var upstream));
            HandlePressGesture(cluster, upstream, evt.ScreenPosition, (evt.Modifiers & PointerModifiers.Shift) != 0);
        }

        private void TakeEventSystemFocus()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null && !eventSystem.alreadySelecting
                && eventSystem.currentSelectedGameObject != gameObject)
                eventSystem.SetSelectedGameObject(gameObject);
        }

        /// <summary>
        /// uGUI resolves the drag receiver once at press time and this component wins it (same GameObject as
        /// the raycast target). The enclosing drag handler to forward to is resolved here — the nearest
        /// ancestor <see cref="IBeginDragHandler"/> (a ScrollRect, a pan controller, anything) — and cached
        /// for gesture-long forwarding, then its potential-drag is primed so a ScrollRect records the press.
        /// Resolving by begin-drag (not initialize-potential-drag) is deliberate: a plain drag handler such
        /// as a pan controller need not implement <see cref="IInitializePotentialDragHandler"/>, yet must
        /// still receive the forwarded drag. Mouse / pen pointers drop the drag threshold — desktop text
        /// selection begins on the first pixel of movement.
        /// </summary>
        void IInitializePotentialDragHandler.OnInitializePotentialDrag(PointerEventData eventData)
        {
            dragRoute = DragRoute.None;
            dragPointerKind = UniTextBase.ResolvePointerKind(eventData);
            parentDragReceiver = null;

            var parent = transform.parent;
            if (parent != null)
            {
                parentDragReceiver = ExecuteEvents.GetEventHandler<IBeginDragHandler>(parent.gameObject);
                if (parentDragReceiver != null)
                    ExecuteEvents.Execute(parentDragReceiver, eventData, ExecuteEvents.initializePotentialDrag);
            }

            if (dragPointerKind != PointerKind.Touch)
                eventData.useDragThreshold = false;
        }

        void IBeginDragHandler.OnBeginDrag(PointerEventData eventData)
        {
            var builtInRoute = ResolveDragRoute(eventData);
            var arbitration = UniTextInteractions.TryGet(uniText, out var interactions) && interactions.HasTargets
                ? interactions.OnHostDrag(eventData)
                : default;
            var compatible = builtInRoute == DragRoute.Local
                ? RangeGestureCompatibility.TextSelection
                : RangeGestureCompatibility.ParentScroll;
            dragRoute = arbitration.claimed &&
                        (arbitration.compatibility & compatible) == 0
                ? DragRoute.Gesture
                : builtInRoute;
            if (dragRoute == DragRoute.Gesture) return;
            if (dragRoute == DragRoute.Parent)
            {
                onDragForwardedToParent?.Invoke();
                if (parentDragReceiver != null)
                    ExecuteEvents.Execute(parentDragReceiver, eventData, ExecuteEvents.beginDragHandler);
                return;
            }

            var evt = BuildDragEvent(eventData.pressPosition, eventData);
            SelectionDragStarted?.Invoke(evt);
            if (editingLayerActive) return;

            var cluster = HitToSource(uniText.HitTestCaret(evt.ScreenPosition, evt.EventCamera, out var upstream));
            BeginDrag(cluster, (evt.Modifiers & PointerModifiers.Shift) != 0,
                upstream ? CaretAffinity.Upstream : CaretAffinity.Downstream);
        }

        void IDragHandler.OnDrag(PointerEventData eventData)
        {
            if (UniTextInteractions.TryGet(uniText, out var interactions) && interactions.HasTargets)
                interactions.OnHostDrag(eventData);
            if (dragRoute == DragRoute.Gesture) return;
            if (dragRoute == DragRoute.Parent)
            {
                if (parentDragReceiver != null)
                    ExecuteEvents.Execute(parentDragReceiver, eventData, ExecuteEvents.dragHandler);
                return;
            }
            if (dragRoute != DragRoute.Local) return;

            var evt = BuildDragEvent(eventData.position, eventData);
            SelectionDragUpdated?.Invoke(evt);
            if (editingLayerActive) return;

            var cluster = HitToSource(uniText.HitTestCaret(evt.ScreenPosition, evt.EventCamera, out var upstream));
            UpdateDrag(cluster, upstream ? CaretAffinity.Upstream : CaretAffinity.Downstream);
        }

        void IEndDragHandler.OnEndDrag(PointerEventData eventData)
        {
            if (UniTextInteractions.TryGet(uniText, out var interactions) && interactions.HasTargets)
                interactions.OnHostDragEnded(eventData);
            var route = dragRoute;
            dragRoute = DragRoute.None;

            if (route == DragRoute.Parent)
            {
                if (parentDragReceiver != null)
                    ExecuteEvents.Execute(parentDragReceiver, eventData, ExecuteEvents.endDragHandler);
                parentDragReceiver = null;
                return;
            }
            parentDragReceiver = null;
            if (route == DragRoute.Gesture) return;
            if (route != DragRoute.Local) return;

            SelectionDragEnded?.Invoke(BuildDragEvent(eventData.position, eventData));
            if (editingLayerActive) return;

            EndDrag();
        }

        /// <summary>
        /// The drag policy. Non-left buttons forward to the enclosing container. Mouse / pen drags are
        /// handled locally (text selection). A touch drag is handled locally ONLY when a selection gesture
        /// armed word-drag mode (long-press / double-tap) or the field actually has scrollable overflow
        /// (<see cref="touchDragScrolls"/>) — there the editing layer's recogniser turns the drag into a
        /// content scroll. Focus alone does NOT capture: a focused field with nothing to scroll leaves the
        /// drag to the enclosing pan / scroll container. Everything else forwards up the hierarchy, so a
        /// field that cannot scroll never swallows a pan — including a masked field whose text fits.
        /// </summary>
        private DragRoute ResolveDragRoute(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return DragRoute.Parent;
            if (dragPointerKind != PointerKind.Touch) return DragRoute.Local;
            if (wordDragMode) return DragRoute.Local;
            if (touchDragScrolls != null && touchDragScrolls()) return DragRoute.Local;
            return DragRoute.Parent;
        }

        private TextPointerEvent BuildDragEvent(Vector2 screenPosition, PointerEventData eventData)
            => dragEventScratch.Set(TextHitResult.None, PointerTrigger.PrimaryClick, screenPosition,
                eventData.pressEventCamera, UniTextBase.ReadCurrentModifiers(), dragPointerKind);

        /// <summary>I-beam only while this is the topmost raycast target and over laid-out text (web line-box model); the editing layer owns the cursor when present (whole-surface I-beam).</summary>
        void IPointerMoveHandler.OnPointerMove(PointerEventData eventData)
        {
            if (editingLayerActive) return;
            bool topmost = eventData.pointerCurrentRaycast.gameObject == gameObject;
            UniTextCursor.Set(topmost && uniText.IsOverText(eventData.position, eventData.enterEventCamera)
                ? CursorType.Text
                : CursorType.Default);
        }

        /// <inheritdoc cref="OnPointerMove"/>
        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            if (editingLayerActive) return;
            UniTextCursor.Set(CursorType.Default);
        }

        /// <summary>EventSystem focus: arms the copy / select-all keyboard session for standalone selectable text.</summary>
        void ISelectHandler.OnSelect(BaseEventData eventData)
        {
            if (editingLayerActive) return;
            BeginFocusSession();
        }

        /// <summary>EventSystem defocus: clears the selection (single-selection-per-document semantics, as on web / Android).</summary>
        void IDeselectHandler.OnDeselect(BaseEventData eventData)
        {
            if (editingLayerActive) return;
            if (FocusGuard.PointerIsOverGuarded())
            {
                reclaimFocusPending = true;
                uniText.FrameUpdated -= ReclaimFocusIfPending;
                uniText.FrameUpdated += ReclaimFocusIfPending;
                return;
            }
            DismissContextMenu();
            EndFocusSession();
            ClearSelection(SelectionChangeReason.Defocus);
            ResetGestureState();
        }

        /// <summary>
        /// Re-takes EventSystem focus after a press on a guarded panel (context menu / toolbar) moved the
        /// selection there, so a selection-only field keeps its selection until a real defocus. Deferred
        /// until the press is physically released because focusing native browser input while uGUI still
        /// owns the press can prevent its release from reaching the current drag receiver. Mirrors the
        /// editor's reclaim (<see cref="UniTextEditable"/>).
        /// </summary>
        private void ReclaimFocusIfPending()
        {
            if (!reclaimFocusPending)
            {
                uniText.FrameUpdated -= ReclaimFocusIfPending;
                return;
            }
            if (InputUtils.GetPointerPressed()) return;
            uniText.FrameUpdated -= ReclaimFocusIfPending;
            reclaimFocusPending = false;
            if (!focusSession) return;
            TakeEventSystemFocus();
            NativeKeyInputSession.Ensure(gameObject);
        }

        private void BeginFocusSession()
        {
            if (focusSession) return;
            focusSession = true;
            NativeKeyInputSession.Subscribe(gameObject, HandleKeyDown);
        }

        private void EndFocusSession()
        {
            if (!focusSession) return;
            focusSession = false;
            NativeKeyInputSession.Unsubscribe(gameObject, HandleKeyDown);
        }

        private void HandleKeyDown(NativeKeyCode key, NativeModifiers mods)
        {
            var action = InputPlatformKeyMap.Instance.Resolve(key, mods);
            if (action == EditAction.Copy) CopyToClipboard();
            else if (action == EditAction.SelectAll) SelectAll();
        }

        /// <summary>
        /// Context request policy for standalone selectable text: on a glyph, a collapsed
        /// selection promotes to the word under the pointer (the platform convention) — and a
        /// long-press additionally arms word-drag mode so a drag continuing from the same touch
        /// extends by whole words (iOS / Android convention); off-glyph (the event carries a
        /// no-hit result anywhere on the surface) the menu still shows when a selection exists —
        /// there is nothing to act on otherwise.
        /// </summary>
        private void OnContextRequested(TextPointerEvent evt)
        {
            if (evt.Consumed || editingLayerActive) return;

            var hit = evt.Hit;
            if (!hit.hit && state.IsCollapsed) return;

            TakeEventSystemFocus();

            evt.Consumed = true;

            if (hit.hit && state.IsCollapsed)
            {
                SelectWord(hit.cluster);
                if (evt.Trigger == PointerTrigger.LongPress && !state.IsCollapsed)
                    ArmWordDrag();
            }

            RequestContextMenu(evt.ScreenPosition);
        }

        private void ExtendSelectionByWord(int codepointIndex)
        {
            int v = ToVisiblePos(codepointIndex);
            var (wordStart, wordEnd) = SelectionWordBreak.GetWordRange(uniText, v);
            var (dragWordStart, dragWordEnd) = ToSourceRange(wordStart, wordEnd);

            int newAnchor, newFocus;
            if (codepointIndex < wordDragAnchorStart)
            {
                newAnchor = wordDragAnchorEnd;
                newFocus = dragWordStart;
            }
            else if (codepointIndex >= wordDragAnchorEnd)
            {
                newAnchor = wordDragAnchorStart;
                newFocus = dragWordEnd;
            }
            else
            {
                newAnchor = wordDragAnchorStart;
                newFocus = wordDragAnchorEnd;
            }

            SetSelection(newAnchor, newFocus, CaretAffinity.Downstream, SelectionChangeReason.Extend);
        }

        /// <summary>
        /// Routes the copy command through the editing layer's adapter pipeline when present
        /// (multi-format write, password / copy-block policy); set by <see cref="UniTextEditable"/>
        /// on enable. Standalone selectable text falls back to a plain-text clipboard write.
        /// </summary>
        internal Action copyOverride;

        internal void CopyToClipboard()
        {
            if (state.IsCollapsed) return;
            if (copyOverride != null)
            {
                copyOverride();
                return;
            }
            var text = GetSelectedText();
            if (!string.IsNullOrEmpty(text))
                UniTextClipboard.SetText(text);
        }
    }
}
