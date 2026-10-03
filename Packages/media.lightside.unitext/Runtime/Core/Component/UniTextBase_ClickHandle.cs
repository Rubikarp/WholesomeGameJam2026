using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    /// <summary>
    /// UniTextBase partial class implementing pointer interaction and hit testing
    /// shared between Canvas and world-space variants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements Unity <see cref="EventSystem"/> interfaces on the base class so both
    /// <see cref="UniText"/> (driven by <c>GraphicRaycaster</c>) and <see cref="UniTextWorld"/>
    /// (driven by <c>PhysicsRaycaster</c>/<c>Physics2DRaycaster</c> with a collider) receive the
    /// same events through the same code path.
    /// </para>
    /// <para>
    /// <b>Event surface.</b>
    /// <list type="bullet">
    /// <item><see cref="TextClicked"/> — single-tap activation. Consumable: setting
    ///   <see cref="TextPointerEvent.Consumed"/> suppresses propagation of the Unity click to
    ///   the parent UI hierarchy. Multi-tap detection (double = word, triple = line) is the
    ///   consumer's responsibility — the host emits one event per click without aggregating.</item>
    /// <item><see cref="ContextRequested"/> — unified context-menu request. Triggered by
    ///   right-click on desktop or long-press on touch / pen (matches HTML <c>contextmenu</c>,
    ///   WinUI <c>ContextRequested</c>, SwiftUI <c>.contextMenu</c>). Consumable.</item>
    /// <item><see cref="TextLongPressProgress"/> — fires every frame while a primary press
    ///   from a touch / pen pointer is held in place, with progress in <c>[0, 1]</c>.
    ///   Notification only. Mouse holds do not emit progress.</item>
    /// <item><see cref="HoverChanged"/> — hover position changed; <see cref="TextHitResult.None"/>
    ///   when the pointer leaves. Notification only.</item>
    /// </list>
    /// </para>
    /// <para>
    /// The base class deliberately does NOT implement the uGUI drag interfaces — a plain label
    /// must never capture drags from an enclosing ScrollRect. Drag-to-select lives on
    /// <see cref="UniTextSelectable"/>, which implements the drag handlers on the same
    /// GameObject and applies the touch-scroll-vs-select policy.
    /// </para>
    /// <para>
    /// It equally does NOT implement <see cref="IPointerClickHandler"/>. An input module resolves a
    /// click against the click handler found under the pointer at press and again at release, so a
    /// label holding that role becomes the click target of every press over it and an enclosing
    /// <c>Selectable</c> is activated only when press and release land on the label itself. The
    /// release is resolved here instead: this component's own click first, then the enclosing
    /// hierarchy's click by the same rule and independent of this component's gesture state.
    /// </para>
    /// <para>
    /// Which side dispatches that hierarchy click depends on the module. One that records
    /// <c>PointerEventData.pointerClick</c> already targets the host and dispatches it itself; this
    /// component only suppresses it, by clearing that field. One that resolves the click against
    /// <c>pointerPress</c> — where this component sits, as the press handler — can never reach the
    /// host, so the click is dispatched from here. Clearing <c>eligibleForClick</c> is not a
    /// suppression lever: <c>InputSystemUIInputModule</c> reads it into its click decision before it
    /// sends the pointer-up, and would dispatch a second click regardless.
    /// </para>
    /// <para>
    /// Emitted <see cref="TextPointerEvent"/> instances are REUSED across emissions — consumers
    /// must not retain them beyond the callback.
    /// </para>
    /// </remarks>
    public abstract partial class UniTextBase :
        IPointerDownHandler, IPointerUpHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        private const float DefaultMaxClickDistance = 20f;

        /// <summary>Built-in range-routing order; lower values intercept it.</summary>
        public const int RangeInteractionEventOrder = -1000;

        /// <summary>Built-in selection/editing order; default subscribers run first.</summary>
        public const int ComponentDefaultEventOrder = 1000;

        /// <summary>
        /// Set by a gesture layer that runs its own touch gesture recognition over this text's
        /// pointer stream (<see cref="PointerPressed"/> / <see cref="PointerReleased"/> — the
        /// editable's <see cref="TouchGestureRecognizer"/> session): the built-in long-press
        /// promotion stands down so one hold never raises two competing context-menu requests,
        /// while the hold is still tracked so its release does not resolve into a click.
        /// Right-click <see cref="ContextRequested"/> is unaffected.
        /// </summary>
        internal bool longPressClaimed;

        private TextHitResult lastHoverResult;

        private sealed class HostPointerSession
        {
            public int pointerId;
            public bool held;
            public bool longPressFired;
            public bool suppressClick;
            public bool suppressContext;
            public bool pressConsumed;
            public Vector2 downScreenPosition;
            public Camera downCamera;
            public float downTimestamp;
            public PointerEventData.InputButton downButton;
            public PointerKind kind;
            public TextHitResult longPressHit;
            public bool longPressHitValid;
            public int releaseFrame = -1;
            public GameObject hostClickTarget;
        }

        private readonly Dictionary<int, HostPointerSession> pointerSessions = new();
        private readonly List<HostPointerSession> pointerSessionScratch = new();
        private readonly HashSet<int> pointersOverTopmost = new();

        private readonly TextPointerEvent pointerEventScratch = new();

        #region Events

        /// <summary>
        /// Occurs when a primary-button click has been confirmed anywhere on this component's
        /// raycast surface — <see cref="TextPointerEvent.Hit"/> is a no-hit result when no glyph
        /// is under the pointer. Subscribers set <see cref="TextPointerEvent.Consumed"/> to
        /// suppress propagation of the underlying Unity click to the parent UI hierarchy.
        /// </summary>
        public event Action<TextPointerEvent> TextClicked;

        private OrderedValueEvent<TextPointerEvent> contextRequested;

        /// <summary>
        /// Occurs when the user has requested a context menu. Triggered by either the secondary
        /// pointer button (right-click on desktop) or by holding a touch / pen press in place
        /// past <see cref="UniTextSettings.LongPressDuration"/>. Mirrors the HTML <c>contextmenu</c> /
        /// WinUI <c>ContextRequested</c> / SwiftUI <c>.contextMenu</c> pattern of one event
        /// with multiple platform-appropriate triggers. Fires anywhere on the component's
        /// raycast surface (a field's menu must open over its empty area too) —
        /// <see cref="TextPointerEvent.Hit"/> is a no-hit result off-glyph. Consumable. Callbacks
        /// run by ascending order and all receive the event even after it is consumed. The range
        /// router uses <see cref="RangeInteractionEventOrder"/>, ordinary subscriptions default
        /// to zero, and selection / editing defaults use <see cref="ComponentDefaultEventOrder"/>.
        /// </summary>
        public OrderedValueEvent<TextPointerEvent> ContextRequested
            => contextRequested ??= new OrderedValueEvent<TextPointerEvent>();

        /// <summary>
        /// Occurs every frame while a touch / pen pointer is being held in place after the initial
        /// press, with <c>progress</c> climbing from <c>0</c> to <c>1</c> over
        /// <see cref="UniTextSettings.LongPressDuration"/>. Stops once the press resolves into a
        /// <see cref="ContextRequested"/> event or is cancelled by movement / release. Mouse
        /// holds do not emit. Notification only.
        /// </summary>
        public event Action<TextHitResult, float> TextLongPressProgress;

        /// <summary>
        /// Pointer-aware long-press progress carrying contact identity and coordinates.
        /// <see cref="TextLongPressProgress"/> remains the hit-only compatibility projection.
        /// </summary>
        public event Action<TextPointerEvent, float> PointerLongPressProgress;

        /// <summary>
        /// Occurs when the hover position has changed. Fires with <see cref="TextHitResult.None"/>
        /// when the pointer leaves the text entirely. Notification only.
        /// </summary>
        public event Action<TextHitResult> HoverChanged;

        private OrderedValueEvent<TextPointerEvent> pointerPressed;

        /// <summary>
        /// Occurs when the primary button has been pressed, before any click resolution. The press
        /// anchor for gesture pipelines: caret placement and focus acquisition happen here
        /// (click events fire only after release). Resolve the caret cluster from
        /// <see cref="TextPointerEvent.ScreenPosition"/> via <see cref="HitTestCaret(Vector2, Camera)"/>;
        /// the bundled <see cref="TextPointerEvent.Hit"/> is the bounding-box hit. Callbacks run
        /// by ascending order and all receive the event even after it is consumed. The range
        /// router uses <see cref="RangeInteractionEventOrder"/>, ordinary subscriptions default
        /// to zero, and selection / editing defaults use <see cref="ComponentDefaultEventOrder"/>.
        /// </summary>
        public OrderedValueEvent<TextPointerEvent> PointerPressed
            => pointerPressed ??= new OrderedValueEvent<TextPointerEvent>();

        /// <summary>
        /// Occurs when the primary button has been released, paired with <see cref="PointerPressed"/>.
        /// Touch gesture recognisers resolve tap / long-press release here.
        /// </summary>
        public event Action<TextPointerEvent> PointerReleased;

        /// <summary>
        /// Occurs when this surface has become the topmost raycast target under the pointer (paired with
        /// <see cref="PointerExited"/> when it ceases to be). Tracks the actual hovered element, not mere
        /// rect containment, so an occluding child / overlapping graphic with a raycast target suppresses
        /// it. <see cref="TextPointerEvent.Hit"/> is not computed — consumers needing a hit re-test from
        /// the position.
        /// </summary>
        public event Action<TextPointerEvent> PointerEntered;

        /// <inheritdoc cref="PointerEntered"/>
        public event Action<TextPointerEvent> PointerExited;

        /// <summary>
        /// Occurs for pointer movement over this event surface and carries an exact pointer identity.
        /// Unlike <see cref="HoverChanged"/>, it is not limited to mouse-style semantic hover.
        /// </summary>
        public event Action<TextPointerEvent> PointerMoved;

        #endregion

        #region Public API

        /// <summary>
        /// Last hover hit test result. Tracked only while <see cref="HoverChanged"/> has subscribers —
        /// without them the per-move hit test is skipped and this stays <see cref="TextHitResult.None"/>.
        /// </summary>
        public TextHitResult LastHoverResult => lastHoverResult;

        #endregion

        #region Pointer Handlers

        /// <inheritdoc/>
        void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
        {
            var session = GetPointerSession(eventData.pointerId);
            session.held = true;
            session.longPressFired = false;
            session.suppressClick = false;
            session.suppressContext = false;
            session.pressConsumed = false;
            session.longPressHitValid = false;
            session.releaseFrame = -1;
            session.downScreenPosition = eventData.position;
            session.downCamera = ResolveEventCamera(eventData);
            session.downTimestamp = Time.unscaledTime;
            session.downButton = eventData.button;
            session.kind = ResolvePointerKind(eventData);
            session.hostClickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(gameObject);

            if (pointerPressed?.HasSubscribers == true &&
                eventData.button == PointerEventData.InputButton.Left)
            {
                var evt = pointerEventScratch.Set(
                    HitTestRange(eventData.position, session.downCamera), PointerTrigger.PrimaryClick,
                    eventData.position, session.downCamera, ReadCurrentModifiers(), session.kind,
                    eventData.pointerId);
                pointerPressed.Invoke(evt);
                session.pressConsumed = evt.Consumed;
            }

            PropagateToParent(eventData, ExecuteEvents.pointerDownHandler);
        }

        /// <inheritdoc/>
        void IPointerUpHandler.OnPointerUp(PointerEventData eventData)
        {
            pointerSessions.TryGetValue(eventData.pointerId, out var session);
            if (session != null)
            {
                session.held = false;
                session.releaseFrame = Time.frameCount;
            }

            var consumed = false;

            if (PointerReleased != null && eventData.button == PointerEventData.InputButton.Left)
            {
                var camera = ResolveEventCamera(eventData);
                var evt = pointerEventScratch.Set(
                    HitTestRange(eventData.position, camera), PointerTrigger.PrimaryClick,
                    eventData.position, camera, ReadCurrentModifiers(), ResolvePointerKind(eventData),
                    eventData.pointerId);
                PointerReleased.Invoke(evt);
                consumed = evt.Consumed;
            }

            PropagateToParent(eventData, ExecuteEvents.pointerUpHandler);
            ResolveClick(eventData, session, consumed);
        }

        /// <summary>
        /// Resolves the release into this component's click and then into the enclosing hierarchy's,
        /// the latter by the module's own rule — same click handler under the pointer at press and at
        /// release, still eligible — so an enclosing <c>Selectable</c> behaves exactly as it does
        /// under a plain graphic child. A drag on this pointer, or a long press already promoted into
        /// a context request, cancels both clicks. Whenever the host click must not happen,
        /// <c>pointerClick</c> is cleared so a module that dispatches it itself sends it nowhere.
        /// </summary>
        private void ResolveClick(PointerEventData eventData, HostPointerSession session, bool consumed)
        {
            if (session == null) return;

            if (eventData.eligibleForClick && !eventData.dragging && !session.longPressFired)
            {
                if (!consumed && !session.suppressClick &&
                    eventData.pointerCurrentRaycast.gameObject == gameObject)
                    consumed = EmitClick(eventData);

                var host = session.hostClickTarget;
                if (!consumed && host != null &&
                    host == ExecuteEvents.GetEventHandler<IPointerClickHandler>(
                        eventData.pointerCurrentRaycast.gameObject))
                {
                    if (eventData.pointerClick == null)
                        ExecuteEvents.Execute(host, eventData, ExecuteEvents.pointerClickHandler);
                    return;
                }
            }

            eventData.pointerClick = null;
        }

        /// <summary>
        /// Emits the click of the resolving button and reports whether a subscriber consumed it.
        /// Fires for any click on the component's raycast surface — a no-hit
        /// <see cref="TextHitResult"/> is carried when no glyph is under the pointer (empty field
        /// area, blank space past short text), matching the platform convention that a field's
        /// context menu opens anywhere inside the field rect. Consumers decide what a no-hit
        /// click means.
        /// </summary>
        private bool EmitClick(PointerEventData eventData)
        {
            var secondary = eventData.button == PointerEventData.InputButton.Right;
            if (secondary)
            {
                if (contextRequested?.HasSubscribers != true) return false;
            }
            else if (eventData.button != PointerEventData.InputButton.Left || TextClicked == null)
                return false;

            var camera = ResolveEventCamera(eventData);
            var evt = pointerEventScratch.Set(HitTestRange(eventData.position, camera),
                secondary ? PointerTrigger.SecondaryClick : PointerTrigger.PrimaryClick,
                eventData.position, camera, ReadCurrentModifiers(), ResolvePointerKind(eventData),
                eventData.pointerId);

            if (secondary) contextRequested.Invoke(evt);
            else TextClicked.Invoke(evt);
            return evt.Consumed;
        }

        /// <inheritdoc/>
        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            UpdateTopmostHover(eventData);
            UpdateHover(eventData);
            EmitPointerMoved(eventData);
        }

        /// <inheritdoc/>
        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            if (pointersOverTopmost.Remove(eventData.pointerId))
            {
                PointerExited?.Invoke(MakeHoverEvent(eventData));
            }

            if (lastHoverResult.hit)
            {
                lastHoverResult = TextHitResult.None;
                HoverChanged?.Invoke(TextHitResult.None);
            }
        }

        /// <inheritdoc/>
        void IPointerMoveHandler.OnPointerMove(PointerEventData eventData)
        {
            if (pointerSessions.TryGetValue(eventData.pointerId, out var session) &&
                session.held && !session.longPressFired &&
                Vector2.Distance(eventData.position, session.downScreenPosition)
                    >= GestureMetrics.SlopPx(UniTextSettings.DragSlopDp, canvas))
            {
                session.held = false;
                session.suppressClick = true;
            }

            UpdateTopmostHover(eventData);
            UpdateHover(eventData);
            EmitPointerMoved(eventData);
        }

        /// <summary>
        /// Fires <see cref="PointerEntered"/> / <see cref="PointerExited"/> on transitions of "this surface
        /// is the topmost raycast target". uGUI delivers pointer events to the whole hover hierarchy (the
        /// topmost target plus its ancestors), so an occluding child or overlapping graphic with a raycast
        /// target would otherwise leave the surface "entered"; the hover cursor must follow the actual
        /// topmost element, the web model.
        /// </summary>
        private void UpdateTopmostHover(PointerEventData eventData)
        {
            bool topmost = eventData.pointerCurrentRaycast.gameObject == gameObject;
            var wasTopmost = pointersOverTopmost.Contains(eventData.pointerId);
            if (topmost == wasTopmost) return;
            if (topmost)
            {
                pointersOverTopmost.Add(eventData.pointerId);
                PointerEntered?.Invoke(MakeHoverEvent(eventData));
            }
            else
            {
                pointersOverTopmost.Remove(eventData.pointerId);
                PointerExited?.Invoke(MakeHoverEvent(eventData));
            }
        }

        private TextPointerEvent MakeHoverEvent(PointerEventData eventData) =>
            pointerEventScratch.Set(TextHitResult.None, PointerTrigger.Hover,
                eventData.position, ResolveEventCamera(eventData), ReadCurrentModifiers(),
                ResolvePointerKind(eventData), eventData.pointerId);

        private HostPointerSession GetPointerSession(int pointerId)
        {
            if (pointerSessions.TryGetValue(pointerId, out var existing)) return existing;
            var created = new HostPointerSession { pointerId = pointerId };
            pointerSessions.Add(pointerId, created);
            if (pointerSessions.Count == 1) RefreshFrameTick();
            return created;
        }

        private void ResetPointerSessions()
        {
            pointerSessions.Clear();
            pointerSessionScratch.Clear();
            pointersOverTopmost.Clear();
            RefreshFrameTick();
            lastHoverResult = TextHitResult.None;
        }

        internal static PointerModifiers ReadCurrentModifiers()
        {
            var m = PointerModifiers.None;
            if (InputUtils.GetKey(KeyCode.LeftShift)   || InputUtils.GetKey(KeyCode.RightShift))   m |= PointerModifiers.Shift;
            if (InputUtils.GetKey(KeyCode.LeftControl) || InputUtils.GetKey(KeyCode.RightControl)) m |= PointerModifiers.Control;
            if (InputUtils.GetKey(KeyCode.LeftAlt)     || InputUtils.GetKey(KeyCode.RightAlt))     m |= PointerModifiers.Alt;
            if (InputUtils.GetKey(KeyCode.LeftCommand) || InputUtils.GetKey(KeyCode.RightCommand) ||
                InputUtils.GetKey(KeyCode.LeftWindows) || InputUtils.GetKey(KeyCode.RightWindows)) m |= PointerModifiers.Meta;
            return m;
        }

        /// <summary>
        /// Called every frame from the component's frame tick while pointer sessions exist. Emits per-frame long-press
        /// progress while a touch / pen primary press is held, and promotes the hold into a
        /// <see cref="ContextRequested"/> event once <see cref="UniTextSettings.LongPressDuration"/>
        /// elapses. Mouse holds are ignored — context-menu on desktop comes from right-click.
        /// While <see cref="longPressClaimed"/> the editable's own selection gesture suppresses
        /// promotion, except when a pointer subscriber consumed the original press and therefore
        /// owns its independent progress/context lifecycle. The press
        /// point is immobile by definition (movement cancels the hold), so the hit is resolved
        /// once per press and cached.
        /// </summary>
        private void TickLongPress()
        {
            pointerSessionScratch.Clear();
            foreach (var pair in pointerSessions)
                if (!pair.Value.held && pair.Value.releaseFrame >= 0 &&
                    Time.frameCount > pair.Value.releaseFrame)
                    pointerSessionScratch.Add(pair.Value);
            for (var i = 0; i < pointerSessionScratch.Count; i++)
                pointerSessions.Remove(pointerSessionScratch[i].pointerId);
            if (pointerSessionScratch.Count > 0 && pointerSessions.Count == 0) RefreshFrameTick();

            if (!longPressClaimed && TextLongPressProgress == null && PointerLongPressProgress == null &&
                contextRequested?.HasSubscribers != true) return;

            pointerSessionScratch.Clear();
            foreach (var pair in pointerSessions)
                if (pair.Value.held && !pair.Value.longPressFired)
                    pointerSessionScratch.Add(pair.Value);

            for (var i = 0; i < pointerSessionScratch.Count; i++)
            {
                var session = pointerSessionScratch[i];
                var claimed = longPressClaimed &&
                              !session.pressConsumed;
                TickLongPress(session, claimed);
            }
        }

        private void TickLongPress(HostPointerSession session, bool claimed)
        {
            if (session.downButton != PointerEventData.InputButton.Left) return;
            if (session.kind == PointerKind.Mouse) return;

            if (!ReferenceEquals(session.downCamera, null) && session.downCamera == null)
            {
                session.held = false;
                session.suppressClick = true;
                return;
            }

            var elapsed = Time.unscaledTime - session.downTimestamp;
            var progress = Mathf.Clamp01(elapsed / UniTextSettings.LongPressDuration);

            if (progress < 1f)
            {
                if (claimed || (TextLongPressProgress == null && PointerLongPressProgress == null)) return;
                EnsureLongPressHit(session);
                TextLongPressProgress?.Invoke(session.longPressHit, progress);
                var evt = pointerEventScratch.Set(session.longPressHit,
                    PointerTrigger.LongPress, session.downScreenPosition, session.downCamera,
                    ReadCurrentModifiers(), session.kind, session.pointerId);
                PointerLongPressProgress?.Invoke(evt, progress);
                if (evt.Consumed) session.suppressContext = true;
                return;
            }

            session.longPressFired = true;
            if (claimed) return;

            EnsureLongPressHit(session);
            TextLongPressProgress?.Invoke(session.longPressHit, 1f);
            var finalEvent = pointerEventScratch.Set(session.longPressHit,
                PointerTrigger.LongPress, session.downScreenPosition, session.downCamera,
                ReadCurrentModifiers(), session.kind, session.pointerId);
            PointerLongPressProgress?.Invoke(finalEvent, 1f);
            if (finalEvent.Consumed) session.suppressContext = true;
            if (session.suppressContext || contextRequested?.HasSubscribers != true) return;

            var context = pointerEventScratch.Set(session.longPressHit, PointerTrigger.LongPress,
                session.downScreenPosition, session.downCamera, ReadCurrentModifiers(), session.kind,
                session.pointerId);
            contextRequested.Invoke(context);
        }

        private void EnsureLongPressHit(HostPointerSession session)
        {
            if (session.longPressHitValid) return;
            session.longPressHit = HitTestRange(session.downScreenPosition, session.downCamera);
            session.longPressHitValid = true;
        }

        #endregion

        #region Hover

        /// <summary>
        /// Per-pointer-move hit testing runs only while <see cref="HoverChanged"/> has subscribers
        /// (or a previous hit still needs to decay to <see cref="TextHitResult.None"/>), so idle
        /// labels pay nothing for hover; <see cref="LastHoverResult"/> is fresh under the same condition.
        /// </summary>
        private void UpdateHover(PointerEventData eventData)
        {
            if (HoverChanged == null && !lastHoverResult.hit) return;

            var result = HitTestRange(eventData.position, ResolveEventCamera(eventData), 0f);

            if (result.cluster != lastHoverResult.cluster || result.hit != lastHoverResult.hit)
                HoverChanged?.Invoke(result);

            lastHoverResult = result;
        }

        private void EmitPointerMoved(PointerEventData eventData)
        {
            if (PointerMoved == null) return;
            var camera = ResolveEventCamera(eventData);
            PointerMoved.Invoke(pointerEventScratch.Set(
                HitTestRange(eventData.position, camera, 0f), PointerTrigger.Hover,
                eventData.position, camera, ReadCurrentModifiers(), ResolvePointerKind(eventData),
                eventData.pointerId));
        }

        private void PropagateToParent<T>(PointerEventData eventData, ExecuteEvents.EventFunction<T> functor)
            where T : IEventSystemHandler
        {
            var parent = transform.parent;
            if (parent != null)
                ExecuteEvents.ExecuteHierarchy(parent.gameObject, eventData, functor);
        }

        /// <summary>
        /// Resolves the physical device class once per event. New Input System: authoritative
        /// pointer type from <c>ExtendedPointerEventData</c> (a mouse there uses positive device
        /// ids, so the legacy id-sign convention would misclassify it). Legacy input: negative
        /// pointerId = mouse buttons, non-negative = touch.
        /// </summary>
        internal static PointerKind ResolvePointerKind(PointerEventData eventData)
        {
#if UNITEXT_INPUTSYSTEM
            if (eventData is UnityEngine.InputSystem.UI.ExtendedPointerEventData ep)
            {
                switch (ep.pointerType)
                {
                    case UnityEngine.InputSystem.UI.UIPointerType.Touch:
                        return PointerKind.Touch;
                    case UnityEngine.InputSystem.UI.UIPointerType.MouseOrPen:
                        return ep.device is UnityEngine.InputSystem.Pen ? PointerKind.Pen : PointerKind.Mouse;
                    default:
                        return PointerKind.Mouse;
                }
            }
#endif
            return eventData.pointerId < 0 ? PointerKind.Mouse : PointerKind.Touch;
        }

        #endregion

        #region Hit Testing

        /// <summary>
        /// Range hit test: returns the glyph (and its cluster) under the point. Inclusive of the
        /// whole glyph bounding box — clicks anywhere on glyph N return cluster N. Use for
        /// <em>entity</em> queries: links, hashtags, mentions, hover-style ranges where left-half
        /// vs. right-half of a glyph is irrelevant. <b>Not for caret placement</b> — use
        /// <see cref="HitTestCaret(Vector2, Camera)"/> for that, since caret semantics need the
        /// edge-snap (left half → before-glyph, right half → after-glyph).
        /// </summary>
        /// <param name="localPosition">Position in local <see cref="RectTransform"/> space.</param>
        /// <param name="maxDistance">Closest-glyph fallback distance when the point is outside every
        /// glyph's bounding box. Pass <c>0</c> to disable the fallback.</param>
        public TextHitResult HitTestRange(Vector2 localPosition, float maxDistance = DefaultMaxClickDistance)
        {
            if (textProcessor == null) return TextHitResult.None;

            var glyphs = textProcessor.PositionedGlyphs;
            var glyphCount = glyphs.Length;
            if (glyphCount == 0) return TextHitResult.None;

            var rect = GetPaddedRect();
            var textX = localPosition.x - rect.xMin;
            var textY = rect.yMax - localPosition.y;

            var buffersLocal = Buffers;
            int scanStart = 0, scanEnd = glyphCount;
            int hitLine = -1;
            if (buffersLocal != null && buffersLocal.lines.count > 0)
            {
                hitLine = SelectionHitTest.FindLineAtTextY(textY, glyphs, buffersLocal);
                if (!TryGetLineGlyphSpan(buffersLocal.lines, hitLine, glyphCount, out scanStart, out scanEnd))
                {
                    scanStart = 0;
                    scanEnd = glyphCount;
                    hitLine = -1;
                }
            }

            for (var i = scanStart; i < scanEnd; i++)
            {
                ref readonly var glyph = ref glyphs[i];

                if (textX >= glyph.left && textX <= glyph.right &&
                    textY >= glyph.top && textY <= glyph.bottom)
                    return new TextHitResult(i, glyph.cluster, new Vector2(glyph.x, glyph.y), 0f);
            }

            if (maxDistance <= 0)
                return TextHitResult.None;

            if (hitLine >= 0)
            {
                if (hitLine > 0 && TryGetLineGlyphSpan(buffersLocal.lines, hitLine - 1, glyphCount, out var prevStart, out _))
                    scanStart = prevStart;
                if (hitLine + 1 < buffersLocal.lines.count &&
                    TryGetLineGlyphSpan(buffersLocal.lines, hitLine + 1, glyphCount, out _, out var nextEnd))
                    scanEnd = nextEnd;
            }

            var closestDistSq = float.MaxValue;
            var closestIndex = -1;

            for (var i = scanStart; i < scanEnd; i++)
            {
                ref readonly var glyph = ref glyphs[i];

                var centerX = (glyph.left + glyph.right) * 0.5f;
                var centerY = (glyph.top + glyph.bottom) * 0.5f;
                var dx = textX - centerX;
                var dy = textY - centerY;
                var distSq = dx * dx + dy * dy;

                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closestIndex = i;
                }
            }

            if (closestIndex < 0)
                return TextHitResult.None;

            var distance = Mathf.Sqrt(closestDistSq);
            if (distance > maxDistance)
                return TextHitResult.None;

            ref readonly var closestGlyph = ref glyphs[closestIndex];
            return new TextHitResult(closestIndex, closestGlyph.cluster, new Vector2(closestGlyph.x, closestGlyph.y),
                distance);
        }

        private static bool TryGetLineGlyphSpan(in PooledBuffer<TextLine> lines, int lineIndex, int glyphCount,
            out int start, out int end)
        {
            start = 0;
            end = 0;
            if (lineIndex < 0 || lineIndex >= lines.count) return false;
            var line = lines[lineIndex];
            if (line.glyphCount <= 0 || line.glyphStart < 0 || line.glyphStart + line.glyphCount > glyphCount)
                return false;
            start = line.glyphStart;
            end = line.glyphStart + line.glyphCount;
            return true;
        }

        /// <summary>
        /// Range hit test from screen coordinates. Overload around
        /// <see cref="HitTestRange(Vector2, float)"/> that performs the screen-to-local conversion.
        /// </summary>
        public TextHitResult HitTestRange(Vector2 screenPosition, Camera eventCamera, float maxDistance = DefaultMaxClickDistance)
        {
            return TryScreenToLocal(screenPosition, eventCamera, out var localPos)
                ? HitTestRange(localPos, maxDistance)
                : TextHitResult.None;
        }

        /// <summary>
        /// Caret hit test: returns the codepoint cluster where a caret should be placed for the
        /// given screen point. Snaps to the nearest glyph <em>edge</em> on the line determined
        /// by the point's vertical coordinate — left half of glyph N → cluster N (caret before
        /// the glyph), right half → cluster N+1 (caret after the glyph). Use for caret placement,
        /// drag-extend selection, click-to-position. <b>Not for entity detection</b> — clicks at
        /// the right edge of a link's last glyph would snap past the link's range; use
        /// <see cref="HitTestRange(Vector2, Camera, float)"/> for that.
        /// </summary>
        /// <param name="screenPosition">Pointer position in screen space.</param>
        /// <param name="eventCamera">Camera for screen-to-local conversion (<see langword="null"/>
        /// for screen-space-overlay canvases).</param>
        public int HitTestCaret(Vector2 screenPosition, Camera eventCamera)
        {
            return HitTestCaret(screenPosition, eventCamera, out _);
        }

        /// <summary>
        /// Caret hit test that also reports the affinity bit. Affinity matters at soft-wrap
        /// boundaries: a codepoint shared by two lines may render at the right edge of the
        /// previous line (upstream) or the left edge of the next line (downstream).
        /// </summary>
        public int HitTestCaret(Vector2 screenPosition, Camera eventCamera, out bool upstream)
        {
            upstream = false;

            if (textProcessor == null) return 0;
            var glyphs = textProcessor.PositionedGlyphs;
            if (glyphs.Length == 0) return 0;

            if (!TryScreenToLocal(screenPosition, eventCamera, out var localPos))
                return 0;

            var buffersLocal = Buffers;
            if (buffersLocal == null) return 0;

            var lines = buffersLocal.lines;
            if (lines.count == 0) return 0;

            var rectLocal = GetPaddedRect();
            var lineIndex = SelectionHitTest.FindLineAtLocalY(localPos, rectLocal, glyphs, buffersLocal);
            var textX = localPos.x - rectLocal.xMin;
            var cluster = SelectionHitTest.FindCodepointAtX(this, lineIndex, textX, lines);

            if (lineIndex + 1 < lines.count
                && cluster == lines[lineIndex].range.End
                && cluster == lines[lineIndex + 1].range.start
                && !lines[lineIndex].endedByMandatoryBreak)
            {
                upstream = true;
            }

            return cluster;
        }

        /// <summary>
        /// Whether the point lies over laid-out text: inside a line's vertical band and within
        /// that line's content extent — the web line-box model (gaps between words on a line
        /// count as text, the empty area past the line's end does not). Drives hover affordance
        /// (I-beam cursor). For caret placement use <see cref="HitTestCaret(Vector2, Camera)"/>,
        /// which snaps from any distance instead of rejecting.
        /// </summary>
        public bool IsOverText(Vector2 screenPosition, Camera eventCamera)
        {
            if (textProcessor == null) return false;
            var glyphs = textProcessor.PositionedGlyphs;
            if (glyphs.Length == 0) return false;
            var buffersLocal = Buffers;
            if (buffersLocal == null || buffersLocal.lines.count == 0) return false;
            if (!TryScreenToLocal(screenPosition, eventCamera, out var localPos)) return false;

            var rect = GetPaddedRect();
            var textX = localPos.x - rect.xMin;
            var textY = rect.yMax - localPos.y;

            var lines = buffersLocal.lines;
            var lineIndex = SelectionHitTest.FindLineAtTextY(textY, glyphs, buffersLocal);

            var line = lines[lineIndex];
            if (line.glyphCount <= 0) return false;
            var glyphEnd = line.glyphStart + line.glyphCount;
            if (line.glyphStart < 0 || glyphEnd > glyphs.Length) return false;

            ref readonly var firstGlyph = ref glyphs[line.glyphStart];
            ref readonly var lastGlyph = ref glyphs[glyphEnd - 1];

            float contentLeft, contentRight;
            if (line.IsRtl)
            {
                contentRight = lastGlyph.right;
                contentLeft = contentRight - line.widthPx;
            }
            else
            {
                contentLeft = firstGlyph.left;
                contentRight = contentLeft + line.widthPx;
            }

            if (lineIndex == 0 && textY < textProcessor.FirstLineTop) return false;
            if (lineIndex == lines.count - 1 && textY > textProcessor.LastLineBottom) return false;
            return textX >= contentLeft && textX <= contentRight;
        }

        private bool TryScreenToLocal(Vector2 screenPosition, Camera eventCamera, out Vector2 localPos)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, screenPosition, eventCamera, out localPos);
        }

        internal bool TryPointerScreenToLocal(Vector2 screenPosition, Camera eventCamera,
            out Vector2 localPosition)
            => TryScreenToLocal(screenPosition, eventCamera, out localPosition);

        #endregion

        #region Variant Hooks

        /// <summary>
        /// Resolves the camera to use for screen-to-local conversion based on the event source.
        /// </summary>
        /// <remarks>
        /// Canvas variant returns <c>null</c> for <c>ScreenSpaceOverlay</c> and <c>canvas.worldCamera</c>
        /// otherwise; world-space variant returns the raycaster camera from the event data.
        /// </remarks>
        protected abstract Camera ResolveEventCamera(PointerEventData eventData);

        #endregion
    }
}
