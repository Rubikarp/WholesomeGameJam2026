using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// Text context menu you build yourself: lay out and style the panel, buttons, and toggles in the
    /// scene however you like, then list the <see cref="ContextMenuItem"/> bindings here — each maps one
    /// of your controls to a command. <see cref="PrefabTextContextMenu"/> drives it with the applicable
    /// capabilities, and standard commands route
    /// back to the presenter it passed. One menu is shareable across many fields — actions always reach
    /// the field that last showed it. The component only wires control events, shows / hides controls per
    /// applicability, positions the panel, and dismisses on an outside click — it draws nothing itself.
    /// Prefabs assigned to <see cref="PrefabTextContextMenu"/> use density-independent layout units.
    /// </summary>
    /// <remarks>
    /// Visibility is driven through a <see cref="CanvasGroup"/>, never by toggling the GameObject active:
    /// the panel stays active so a sibling <see cref="FocusGuard"/> keeps the editor focused while
    /// the user presses a control. Add the guard to this object to preserve focus during interaction.
    /// </remarks>
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    [AddComponentMenu(UniTextMenu.AddComponent.ContextMenu)]
    public sealed partial class UniTextContextMenu : MonoBehaviour
    {
        /// <summary>The ordered control-to-command bindings.</summary>
        [SerializeField, StateList(nameof(ApplyItemsChange))]
        [Tooltip("Bindings between the controls you built (Button / Toggle) and their commands.")]
        private TypedList<ContextMenuItem> items = new(
            new CutContextMenuItem(),
            new CopyContextMenuItem(),
            new PasteContextMenuItem(),
            new SelectAllContextMenuItem());

        private CanvasGroup canvasGroup;
        private bool shown;
        private readonly HashSet<ContextMenuItem> wiredItems = new();
        private GameObject blocker;
        private Action<ContextMenuAction> presenter;

        internal object PresentationOwner { get; set; }

        /// <summary>Whether the menu is currently shown.</summary>
        public bool IsVisible => shown;

        private void Awake()
        {
            if (items == null) SetItemsState(new TypedList<ContextMenuItem>());
            canvasGroup = GetComponent<CanvasGroup>();
            SetShown(false);
        }

        private void OnDestroy()
        {
            UnwireItems();
            if (blocker != null) Destroy(blocker);
        }

        private void ApplyItemsChange()
        {
            UnwireItems();
            if (shown) EnsureWired();
        }

        private void UnwireItems()
        {
            foreach (var item in wiredItems) item?.Unwire();
            wiredItems.Clear();
        }

        /// <summary>
        /// Shows the menu at <paramref name="screenPosition"/>, hiding the controls whose item is not
        /// applicable (or has no control assigned), and records <paramref name="presenter"/> as the
        /// single receiver of invoked actions until the next Show / <see cref="Hide"/>. No-op
        /// (hides) when no item applies.
        /// </summary>
        public void Show(Vector2 screenPosition, in ContextMenuCapabilities capabilities,
            Action<ContextMenuAction> presenter)
        {
            this.presenter = presenter;
            EnsureWired();

            bool any = false;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null) continue;
                bool applicable = item.HasControl && item.IsApplicable(in capabilities);
                item.SetVisible(applicable);
                any |= applicable;
            }
            if (!any) { Hide(); return; }

            gameObject.SetActive(true);
            shown = true;
            SetShown(true);
            PositionAt(screenPosition);
            if (!CanvasUtil.IsMobilePlatform())
                ShowBlocker();
            transform.SetAsLastSibling();
        }

        /// <summary>Hides the menu and clears the recorded presenter.</summary>
        public void Hide()
        {
            shown = false;
            PresentationOwner = null;
            presenter = null;
            if (blocker != null) blocker.SetActive(false);
            SetShown(false);
        }

        /// <summary>Routes the invoked command to the presenter recorded by the last <see cref="Show"/> — how command items reach it.</summary>
        public void RequestAction(ContextMenuAction action) => presenter?.Invoke(action);

        private void SetShown(bool value)
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = value ? 1f : 0f;
            canvasGroup.interactable = value;
            canvasGroup.blocksRaycasts = value;
        }

        /// <summary>Wires each not-yet-wired item on every <see cref="Show"/>, so items added to <see cref="Items"/> at runtime bind too; per-item tracking keeps control listeners single.</summary>
        private void EnsureWired()
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || !wiredItems.Add(item)) continue;
                item.Wire(this);
            }
        }

        private void PositionAt(Vector2 screenPosition)
        {
            var rt = (RectTransform)transform;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRt = (RectTransform)canvas.transform;
            var camera = CanvasUtil.GetCanvasCamera(canvas.rootCanvas);

            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            var size = rt.rect.size;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenPosition, camera, out var local))
                return;

            if (CanvasUtil.IsMobilePlatform())
            {
                rt.pivot = new Vector2(0.5f, 0f);
            }
            else
            {
                var bounds = canvasRt.rect;
                float pivotX = local.x + size.x <= bounds.xMax ? 0f : 1f;
                float pivotY = local.y - size.y >= bounds.yMin ? 1f : 0f;
                rt.pivot = new Vector2(pivotX, pivotY);
            }

            rt.position = canvasRt.TransformPoint(local);
            ClampToCanvas(rt, canvasRt, camera);
        }

        /// <summary>
        /// Keeps the menu inside the canvas and, when a soft keyboard is up, above it: the lower bound is
        /// the canvas bottom raised to the keyboard's top edge (<see cref="UniTextNativeInput.KeyboardArea"/>,
        /// screen pixels). Shifts by the minimum needed per axis.
        /// </summary>
        private static void ClampToCanvas(RectTransform menu, RectTransform canvas, Camera camera)
        {
            var c = new Vector3[4]; canvas.GetWorldCorners(c);
            var m = new Vector3[4]; menu.GetWorldCorners(m);

            float bottomY = c[0].y;
            var keyboard = UniTextNativeInput.KeyboardArea;
            if (keyboard.height > 0f
                && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    canvas, new Vector2(Screen.width * 0.5f, keyboard.yMax), camera, out var kbTop))
                bottomY = Mathf.Max(bottomY, kbTop.y);

            var shift = Vector3.zero;
            if (m[0].x < c[0].x) shift.x = c[0].x - m[0].x;
            else if (m[2].x > c[2].x) shift.x = c[2].x - m[2].x;
            if (m[0].y < bottomY) shift.y = bottomY - m[0].y;
            else if (m[1].y > c[1].y) shift.y = c[1].y - m[1].y;

            menu.position += shift;
        }

        private void ShowBlocker()
        {
            var parent = transform.parent;
            if (parent == null) return;

            if (blocker == null)
            {
                blocker = new GameObject("ContextMenuBlocker", typeof(RectTransform), typeof(Image), typeof(ContextMenuBlocker));
                blocker.hideFlags = HideFlags.DontSave;

                var image = blocker.GetComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0f);
                image.raycastTarget = true;

                blocker.GetComponent<ContextMenuBlocker>().menu = this;
            }

            var rt = (RectTransform)blocker.transform;
            rt.SetParent(parent, false);
            rt.StretchToParent();
            rt.SetAsLastSibling();
            blocker.SetActive(true);
        }

        private sealed class ContextMenuBlocker : MonoBehaviour, IPointerDownHandler
        {
            internal UniTextContextMenu menu;

            void IPointerDownHandler.OnPointerDown(PointerEventData eventData) => menu?.Hide();
        }
    }
}
