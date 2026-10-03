using UnityEngine;
using UnityEngine.EventSystems;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoMenuTrigger : MonoBehaviour, IPointerEnterHandler
{
    public GlassDemoMenuBar menuBar;
    public GlassDesktopMenu menu;
    public void Open() => menuBar.ShowMenu(menu, (RectTransform)transform);
    public void OnPointerEnter(PointerEventData eventData) => menuBar.ShowMenu(menu, (RectTransform)transform, true);
}
}
