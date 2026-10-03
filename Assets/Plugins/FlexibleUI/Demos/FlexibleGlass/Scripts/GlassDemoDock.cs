using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoDock : MonoBehaviour
{
    public const float DefaultScale = 1.25f;
    public GlassDemoDesktop desktop;
    public RectTransform dockRect;
    public GlassDemoDockIcon[] icons;
    public UIGlass selection;
    public bool autoHide;
    [Range(0.7f, 1.25f)] public float size = 1f;
    public bool IsRevealed { get; private set; } = true;
    public float VisibleHeight => dockRect.rect.height * size * DefaultScale + 30f;
    private float lastHover;
    private Canvas canvas;
    private float selectionAmount;
    private float selectionX;

    private void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        if (selection && desktop.dock) selection.appearance = desktop.dock.appearance;
    }
    private void Update()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var pointer = UnityEngine.InputSystem.Pointer.current?.position.ReadValue() ?? Vector2.negativeInfinity;
#else
        Vector2 pointer = Input.mousePosition;
#endif
        UpdatePointer(pointer, Time.unscaledDeltaTime);
    }

    public void UpdatePointer(Vector2 pointer, float deltaTime)
    {
        if (!canvas) canvas = GetComponentInParent<Canvas>();
        var hide = autoHide || desktop.IsFullScreen;
        var scale = size * DefaultScale;
        var width = dockRect.rect.width * scale * canvas.scaleFactor;
        var atBottom = pointer.y >= 0f && pointer.y <= 3f && Mathf.Abs(pointer.x - Screen.width * 0.5f) < width * 0.5f;
        var overDock = IsRevealed && RectTransformUtility.RectangleContainsScreenPoint(dockRect, pointer, null);
        var contextOpen = desktop.menuBar && desktop.menuBar.DockMenuOpen;
        if (atBottom || overDock || contextOpen) lastHover = Time.unscaledTime;
        IsRevealed = !hide || atBottom || overDock || contextOpen || Time.unscaledTime - lastHover < 0.35f;
        var y = IsRevealed ? 15f + dockRect.rect.height * scale * 0.5f : -dockRect.rect.height * scale * 0.5f - 20f;
        var current = dockRect.anchoredPosition;
        var next = Mathf.MoveTowards(current.y, y, deltaTime * 800f);
        if (current.y != next) dockRect.anchoredPosition = new Vector2(current.x, next);
        if (dockRect.localScale.x != scale) dockRect.localScale = Vector3.one * scale;
        UpdateIcons(pointer, deltaTime, overDock);
    }

    private void UpdateIcons(Vector2 pointer, float deltaTime, bool overDock)
    {
        var eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(dockRect, pointer, eventCamera, out var local);
        var pointerInside = pointer.x >= 0 && pointer.x <= Screen.width && pointer.y >= 0 && pointer.y <= Screen.height;
        var hovering = pointerInside && IsRevealed && (overDock || local.y >= 0 && local.y < dockRect.rect.yMax + 38f);
        var nearest = -1;
        var nearestDistance = 52f;
        var emphasis = 0f;
        for (var i = 0; i < icons.Length; i++)
        {
            var icon = icons[i];
            if (!icon) continue;
            var center = ((RectTransform)icon.transform.parent).anchoredPosition.x;
            var distance = Mathf.Abs(local.x - center);
            if (hovering && distance < nearestDistance) { nearest = i; nearestDistance = distance; }
            var influence = hovering ? 1f - Mathf.SmoothStep(0f, 1f, distance / 100f) : 0f;
            icon.UpdateEmphasis(influence, deltaTime);
            emphasis = Mathf.Max(emphasis, icon.Emphasis);
        }
        if (!selection) return;
        var blend = 1f - Mathf.Exp(-18f * deltaTime);
        if (hovering && nearest >= 0)
        {
            var x = ((RectTransform)icons[nearest].transform.parent).anchoredPosition.x;
            selectionX = selectionAmount <= 0.001f ? x : Mathf.Lerp(selectionX, x, blend);
        }
        selectionAmount = emphasis;
        var rect = (RectTransform)selection.transform;
        var scale = selectionAmount > 0f ? Mathf.Lerp(0.4f, 1f, selectionAmount) : 0f;
        rect.anchoredPosition = new Vector2(selectionX, -14f + rect.rect.height * scale * 0.5f);
        rect.localScale = Vector3.one * scale;
        selection.enabled = selectionAmount > 0f;
    }

    public void SetAutoHide(bool value) => autoHide = value;
    public void SetSize(float value) => size = Mathf.Clamp(value, 0.7f, 1.25f);
    public void RefreshIndicators()
    {
        if (icons == null) return;
        foreach (var icon in icons) if (icon) icon.RefreshIndicator();
    }
}
}
