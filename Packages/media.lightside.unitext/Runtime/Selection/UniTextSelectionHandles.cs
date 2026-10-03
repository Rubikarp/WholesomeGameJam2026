using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    /// <summary>
    /// Shipped Unity-UI presentation used by <see cref="PrefabSelectionHandles"/>. It instantiates
    /// a user-authored prefab for the draggable selection
    /// handles (one prefab used for both the anchor and focus carets) and for the collapsed-caret
    /// insertion handle, and places each at its caret point. The split of ownership: the <b>prefab</b>
    /// owns the look — sprite, colour, material, and the <b>pivot</b>, which alone decides how the handle
    /// sits relative to the caret (a teardrop pivots at its tip); the <b>code</b> owns the <b>size</b>,
    /// forcing each handle to a square of <see cref="handleSize"/> dp so the touch target stays a constant
    /// physical size across canvases, resolutions, and densities (a fingertip is device-independent). Put
    /// <c>Preserve Aspect</c> on the prefab's Image so its sprite fits that square undistorted. Assign
    /// its prefab inside a <see cref="PrefabSelectionHandles"/> entity; geometry is pulled from
    /// <see cref="UniTextEditable.ActiveEditor"/> at call time, the instances live in a canvas-level
    /// overlay so the field's RectMask2D never clips them, and a handle whose caret scrolls outside the
    /// field box hides until it returns. A <see cref="SelectionHandleDragger"/> (drag routing) and a
    /// <see cref="FocusGuard"/> (so grabbing a handle never defocuses the field) are ensured on each
    /// instance, so the prefab need only carry a raycastable Graphic.
    /// </summary>
    [AddComponentMenu(UniTextMenu.AddComponent.UniTextSelectionHandles)]
    public sealed partial class UniTextSelectionHandles : MonoBehaviour
    {
        private const float ClipMargin = 4f;

        /// <summary>Square handle size in density-independent pixels.</summary>
        [SerializeField, NumberStateProperty(nameof(ApplyHandleSizes), Min = 1)]
        [Tooltip("Handle width/height in dp (density-independent pixels): the code sizes every handle to " +
                 "this square, so the physical size stays constant across canvas scale, resolution, and " +
                 "screen density. Put Preserve Aspect on the prefab's Image to fit the sprite. ~44 dp is a " +
                 "comfortable touch target.")]
        private float handleSize = 44;

        /// <summary>Prefab used for both selection endpoints.</summary>
        [SerializeField, StateProperty(nameof(RebuildHandles))]
        [Tooltip("Prefab instantiated for both selection handles (anchor + focus). Owns sprite/colour/" +
                 "material and the pivot (which places it relative to the caret — no code offset). Its " +
                 "size is overridden to the Handle Size square. Must carry a raycastable Graphic. " +
                 "Unassigned = no selection handles.")]
        private RectTransform selectionHandlePrefab;

        /// <summary>Prefab used for the collapsed-caret insertion handle.</summary>
        [SerializeField, StateProperty(nameof(RebuildHandles))]
        [Tooltip("Prefab instantiated for the collapsed-caret insertion handle. Same contract as the " +
                 "selection prefab. Unassigned = no insertion handle.")]
        private RectTransform insertionHandlePrefab;

        private RectTransform overlay;
        private Canvas rootCanvas;
        private RectTransform anchorHandle;
        private RectTransform focusHandle;
        private RectTransform insertionHandle;
        private SelectionHandleDragger anchorDragger;
        private SelectionHandleDragger focusDragger;
        private SelectionHandleDragger insertionDragger;
        private bool isVisible;
        private bool insertionVisible;

        internal object PresentationOwner { get; set; }

        public event Action<Vector2> AnchorDragged;
        public event Action<Vector2> FocusDragged;
        public event Action SelectionHandleDragStarted;
        public event Action SelectionHandleDragEnded;
        public event Action<Vector2> InsertionHandleDragged;
        public event Action InsertionHandleTapped;
        public event Action InsertionHandleDragStarted;
        public event Action InsertionHandleDragEnded;

        private void Awake()
        {
            overlay = CanvasUtil.GetOverlayContainer(this, "UniTextTouchOverlay");
            var canvas = overlay.GetComponentInParent<Canvas>();
            rootCanvas = canvas != null ? canvas.rootCanvas : null;

            RebuildHandles();
        }

        private void OnDestroy()
        {
            DestroyHandles();
        }

        private void RebuildHandles()
        {
            if (overlay == null) return;
            DestroyHandles();
            anchorHandle = InstantiateHandle(selectionHandlePrefab, "SelectionHandle_Anchor", out anchorDragger);
            focusHandle = InstantiateHandle(selectionHandlePrefab, "SelectionHandle_Focus", out focusDragger);
            insertionHandle = InstantiateHandle(insertionHandlePrefab, "SelectionHandle_Insertion", out insertionDragger);
            if (anchorDragger != null) WireSelection(anchorDragger, pos => AnchorDragged?.Invoke(pos));
            if (focusDragger != null) WireSelection(focusDragger, pos => FocusDragged?.Invoke(pos));
            if (insertionDragger != null) WireInsertion(insertionDragger);
            ApplyHandleSizes();
            SetHandleActive(anchorHandle, false);
            SetHandleActive(focusHandle, false);
            SetHandleActive(insertionHandle, false);
            if (isVisible) UpdateSelectionPositions();
            if (insertionVisible) UpdateInsertionPosition();
        }

        private void DestroyHandles()
        {
            if (anchorHandle != null) Destroy(anchorHandle.gameObject);
            if (focusHandle != null) Destroy(focusHandle.gameObject);
            if (insertionHandle != null) Destroy(insertionHandle.gameObject);
            anchorHandle = null;
            focusHandle = null;
            insertionHandle = null;
            anchorDragger = null;
            focusDragger = null;
            insertionDragger = null;
        }

        internal void ShowSelection()
        {
            var editor = UniTextEditable.ActiveEditor;
            if (editor == null || editor.Selection.IsCollapsed) { HideSelection(); return; }
            isVisible = true;
            UpdateSelectionPositions();
        }

        internal void UpdateSelectionPositions()
        {
            if (!isVisible) return;
            var editor = UniTextEditable.ActiveEditor;
            if (editor == null) { HideSelection(); return; }

            var sel = editor.Selection;
            var clipRect = editor.GetViewportScreenRect();
            var anchorPos = editor.GetCaretScreenPosition(sel.Anchor, true);
            var focusPos = editor.GetCaretScreenPosition(sel.Focus, true);
            Place(anchorHandle, anchorDragger, anchorPos, clipRect);
            Place(focusHandle, focusDragger, focusPos, clipRect);

            bool anchorOnRight = anchorPos.x > focusPos.x;
            MirrorHandle(anchorHandle, anchorOnRight);
            MirrorHandle(focusHandle, !anchorOnRight);
        }

        /// <summary>
        /// Mirrors the handle horizontally when it sits on the visual right of the selection, so the two
        /// handles face each other (iOS/Android convention). Author the prefab as the left / start handle;
        /// the code flips whichever instance is currently on the right. Uses <c>localScale.x</c>, so the
        /// code-set square <c>sizeDelta</c> is unaffected.
        /// </summary>
        private static void MirrorHandle(RectTransform handle, bool mirrored)
        {
            if (handle == null) return;
            var s = handle.localScale;
            float x = mirrored ? -Mathf.Abs(s.x) : Mathf.Abs(s.x);
            if (s.x == x) return;
            s.x = x;
            handle.localScale = s;
        }

        internal void ShowInsertion()
        {
            var editor = UniTextEditable.ActiveEditor;
            if (editor == null || !editor.Selection.IsCollapsed) { HideInsertion(); return; }
            insertionVisible = true;
            UpdateInsertionPosition();
        }

        internal void UpdateInsertionPosition()
        {
            if (!insertionVisible) return;
            var editor = UniTextEditable.ActiveEditor;
            if (editor == null) { HideInsertion(); return; }

            var clipRect = editor.GetViewportScreenRect();
            Place(insertionHandle, insertionDragger,
                editor.GetCaretScreenPosition(editor.Selection.Focus, true), clipRect);
        }

        internal void HideSelection()
        {
            isVisible = false;
            SetHandleActive(anchorHandle, false);
            SetHandleActive(focusHandle, false);
            if (!insertionVisible) PresentationOwner = null;
        }

        internal void HideInsertion()
        {
            insertionVisible = false;
            SetHandleActive(insertionHandle, false);
            if (!isVisible) PresentationOwner = null;
        }

        internal void HideAll()
        {
            HideSelection();
            HideInsertion();
        }

        /// <summary>
        /// Places the handle so its pivot lands on the caret's screen point (converted to the overlay's
        /// world space via the root-canvas camera). No offset is applied — the prefab's pivot alone
        /// decides how the handle hangs relative to the caret. A caret outside the visible viewport
        /// (<paramref name="clipRect"/>) hides the handle — EXCEPT the handle the user is actively dragging,
        /// which stays alive and controllable, pinned to the viewport edge: deactivating it mid-drag would
        /// destroy its own drag receiver and abort the gesture (the endpoint auto-scrolls back into view).
        /// </summary>
        private void Place(RectTransform handle, SelectionHandleDragger dragger,
            Vector2 screenPos, Rect clipRect)
        {
            if (handle == null) return;

            bool dragging = dragger != null && dragger.IsDragging;
            bool inside = dragging
                       || (screenPos.x >= clipRect.xMin - ClipMargin && screenPos.x <= clipRect.xMax + ClipMargin
                        && screenPos.y >= clipRect.yMin - ClipMargin && screenPos.y <= clipRect.yMax + ClipMargin);
            SetHandleActive(handle, inside);
            if (!inside) return;

            if (dragging)
                screenPos = new Vector2(
                    Mathf.Clamp(screenPos.x, clipRect.xMin, clipRect.xMax),
                    Mathf.Clamp(screenPos.y, clipRect.yMin, clipRect.yMax));

            var cam = CanvasUtil.GetCanvasCamera(rootCanvas);
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(overlay, screenPos, cam, out var world))
            {
                var current = handle.position;
                if (current.x != world.x || current.y != world.y || current.z != world.z)
                    handle.position = world;
            }
        }

        private void ApplyHandleSizes()
        {
            var size = new Vector2(handleSize, handleSize);
            if (anchorHandle != null) anchorHandle.sizeDelta = size;
            if (focusHandle != null) focusHandle.sizeDelta = size;
            if (insertionHandle != null) insertionHandle.sizeDelta = size;
        }

        /// <summary>
        /// Instantiates a handle prefab under the overlay, centres its anchors so the code-set square size
        /// is absolute (the prefab's pivot is left untouched), and ensures the drag router and focus guard
        /// are present. A <see langword="null"/> prefab yields no handle.
        /// </summary>
        private RectTransform InstantiateHandle(RectTransform prefab, string handleName,
            out SelectionHandleDragger dragger)
        {
            dragger = null;
            if (prefab == null) return null;

            var rt = Instantiate(prefab, overlay, false);
            rt.name = handleName;
            rt.gameObject.hideFlags = HideFlags.DontSave;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

            if (!rt.TryGetComponent(out dragger))
                dragger = rt.gameObject.AddComponent<SelectionHandleDragger>();
            if (!rt.TryGetComponent<FocusGuard>(out _))
                rt.gameObject.AddComponent<FocusGuard>();

            return rt;
        }

        /// <summary>
        /// Selection-handle drags also drive the active editor's magnifier (iOS loupe-over-handle
        /// convention); the editable feeds it positions via UpdatePosition during the drag.
        /// </summary>
        private void WireSelection(SelectionHandleDragger dragger, Action<Vector2> onDragged)
        {
            dragger.onDragged = onDragged;
            dragger.onDragStarted = pos =>
            {
                UniTextEditable.ActiveEditor?.MagnifierImpl?.Show(pos);
                SelectionHandleDragStarted?.Invoke();
            };
            dragger.onDragEnded = () =>
            {
                UniTextEditable.ActiveEditor?.MagnifierImpl?.Hide();
                SelectionHandleDragEnded?.Invoke();
            };
        }

        private void WireInsertion(SelectionHandleDragger dragger)
        {
            dragger.onDragged = pos => InsertionHandleDragged?.Invoke(pos);
            dragger.onDragStarted = pos =>
            {
                UniTextEditable.ActiveEditor?.MagnifierImpl?.Show(pos);
                InsertionHandleDragStarted?.Invoke();
            };
            dragger.onDragEnded = () =>
            {
                UniTextEditable.ActiveEditor?.MagnifierImpl?.Hide();
                InsertionHandleDragEnded?.Invoke();
            };
            dragger.onTapped = () => InsertionHandleTapped?.Invoke();
        }

        private static void SetHandleActive(RectTransform handle, bool active)
        {
            if (handle != null && handle.gameObject.activeSelf != active)
                handle.gameObject.SetActive(active);
        }
    }

    /// <summary>
    /// Drag handler ensured on each handle instance, routing drag screen positions to
    /// <see cref="UniTextSelectionHandles"/> callbacks. A pointer-up with no drag is a tap. Drag events
    /// bubble up from whatever raycastable Graphic the prefab carries, so no specific Graphic is required.
    /// </summary>
    internal sealed class SelectionHandleDragger : MonoBehaviour,
        IDragHandler, IBeginDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public Action<Vector2> onDragged;
        public Action<Vector2> onDragStarted;
        public Action onDragEnded;
        public Action onTapped;

        /// <summary>True between begin- and end-drag: the presenter must keep this handle active so the
        /// in-flight uGUI drag is never interrupted by the handle deactivating when it crosses the viewport.</summary>
        public bool IsDragging { get; private set; }

        void IBeginDragHandler.OnBeginDrag(PointerEventData eventData)
        {
            IsDragging = true;
            onDragStarted?.Invoke(eventData.position);
        }

        void IDragHandler.OnDrag(PointerEventData eventData)
        {
            onDragged?.Invoke(eventData.position);
        }

        void IEndDragHandler.OnEndDrag(PointerEventData eventData)
        {
            IsDragging = false;
            onDragEnded?.Invoke();
        }

        private void OnDisable() => IsDragging = false;

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging) return;
            onTapped?.Invoke();
        }
    }
}
