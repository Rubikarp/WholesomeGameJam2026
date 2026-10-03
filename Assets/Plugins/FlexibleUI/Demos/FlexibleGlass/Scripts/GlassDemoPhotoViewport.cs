using UnityEngine;
using UnityEngine.EventSystems;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoPhotoViewport : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
{
    public GlassDemoPhotos photos;
    public void OnBeginDrag(PointerEventData eventData) => photos.GetComponent<GlassDemoWindow>().Focus();
    public void OnDrag(PointerEventData eventData) => photos.Pan(eventData.delta / GetComponentInParent<Canvas>().scaleFactor);
    public void OnScroll(PointerEventData eventData) => photos.ZoomBy(eventData.scrollDelta.y * 0.15f);
}
}
