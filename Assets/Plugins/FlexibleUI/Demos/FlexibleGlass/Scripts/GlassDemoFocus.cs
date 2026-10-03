using UnityEngine;
using UnityEngine.EventSystems;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoFocus : MonoBehaviour, IPointerDownHandler
{
    public GlassDemoWindow window;
    public void OnPointerDown(PointerEventData eventData) => window.Focus();
}
}
