using UnityEngine;
using UnityEngine.EventSystems;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoShapeDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public GlassDemoShapeMotion motion;
    public int shapeIndex;
    private Vector2 lastPointer;

    private void Awake()
    {
        var hit = GetComponent<UnityEngine.UI.Image>();
        if (hit && hit.sprite && hit.sprite.texture.isReadable)
            hit.alphaHitTestMinimumThreshold = 0.5f;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        motion.BeginShapeDrag(shapeIndex);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(motion.stage, eventData.position, eventData.pressEventCamera, out lastPointer);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(motion.stage, eventData.position, eventData.pressEventCamera, out var point)) return;
        var rect = (RectTransform)motion.shapes[shapeIndex].glass.transform;
        var next = motion.ClampToScreen(rect.anchoredPosition + point - lastPointer);
        motion.MoveShape(shapeIndex, next - rect.anchoredPosition);
        lastPointer = point;
    }

    public void OnEndDrag(PointerEventData eventData) => motion.EndShapeDrag(shapeIndex);

    private void OnDisable()
    {
        if (motion && motion.shapes != null && shapeIndex < motion.shapes.Length)
            motion.EndShapeDrag(shapeIndex);
    }
}
}
