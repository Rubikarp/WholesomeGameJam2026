using UnityEngine;
using UnityEngine.EventSystems;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerClickHandler
{
    public RectTransform target;
    private RectTransform parent;
    private Vector2 pointerOffset;

    public void OnBeginDrag(PointerEventData eventData)
    {
        parent = null;
        if (!target || target.parent is not RectTransform targetParent)
            return;
        if (target.TryGetComponent<GlassDemoWindow>(out var window))
        {
            if (window.IsFullScreen) return;
            window.CompleteTransition();
            window.Focus();
        }
        else
            target.SetAsLastSibling();
        parent = targetParent;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out var point))
            pointerOffset = target.anchoredPosition - point;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!target || !parent || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out var point))
            return;

        var desired = point + pointerOffset;
        var parentRect = parent.rect;
        var targetRect = target.rect;
        var half = Vector2.Scale(targetRect.size, target.localScale) * 0.5f;
        desired.x = Mathf.Clamp(desired.x, parentRect.xMin - half.x + 100f, parentRect.xMax + half.x - 100f);
        desired.y = Mathf.Clamp(desired.y, parentRect.yMin - half.y + 54f, parentRect.yMax - half.y - 28f);
        target.anchoredPosition = desired;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.clickCount == 2 && target && target.TryGetComponent<GlassDemoWindow>(out var window))
            window.ToggleMaximize();
    }
}
}
