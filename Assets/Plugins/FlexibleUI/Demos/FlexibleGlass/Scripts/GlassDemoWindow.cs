using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoWindow : MonoBehaviour, IPointerDownHandler
{
    public string appName;
    public bool startOpen;
    public float animationDuration = 0.24f;
    public GlassDemoDockIcon launcher;
    public Text titleLabel;
    public Image[] trafficLights;
    public GlassDemoDesktop desktop;
    public Canvas windowCanvas;
    public Vector2 initialPosition;
    public Vector2 initialSize;
    public bool canMaximize = true;
    public bool IsOpen { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsMinimized { get; private set; }
    public bool IsFullScreen { get; private set; }
    public bool IsMaximized => maximized;

    private CanvasGroup group;
    private RectTransform rect;
    private GlassImage[] glass;
    private Coroutine transition;
    private Vector2 restoredPosition;
    private Vector2 restoredSize;
    private bool maximized;
    private Vector2 beforeFullPosition;
    private Vector2 beforeFullSize;
    private Vector2 restingPosition;
    private bool transitionOpens;
    private static readonly Color[] TrafficColors = { new(1f, 0.36f, 0.31f), new(1f, 0.73f, 0.21f), new(0.25f, 0.79f, 0.34f) };

    private void Awake()
    {
        Cache();
        if (!desktop) Initialize(startOpen);
    }

    private void Cache()
    {
        if (rect) return;
        rect = (RectTransform)transform;
        group = GetComponent<CanvasGroup>();
        glass = GetComponentsInChildren<GlassImage>(true);
    }

    public void Initialize(bool open)
    {
        Cache();
        IsOpen = open;
        IsRunning = open;
        IsMinimized = false;
        SetImmediate(open);
    }

    public void Open()
    {
        Cache();
        var alreadyOpen = IsOpen && group.interactable;
        var fromDock = IsMinimized;
        CompleteTransition();
        IsOpen = true;
        IsRunning = true;
        IsMinimized = false;
        SetGlassEnabled(true);
        Focus();
        if (alreadyOpen) { SetImmediate(true); return; }
        if (launcher) launcher.Launch();
        Animate(true, fromDock);
    }

    public void Focus()
    {
        if (desktop) desktop.Focus(this);
        else transform.SetAsLastSibling();
    }

    public void Close() { CompleteTransition(); ExitFullScreen(); IsRunning = false; IsMinimized = false; Animate(false, false); }
    public void Minimize() { CompleteTransition(); ExitFullScreen(); IsMinimized = true; Animate(false, true); }
    public void Quit() => Close();
    public void OnPointerDown(PointerEventData eventData) => Focus();

    internal void ReleaseInputFocus()
    {
        var events = EventSystem.current;
        var selected = events ? events.currentSelectedGameObject : null;
        if (selected && selected.transform.IsChildOf(transform)) events.SetSelectedGameObject(null);
    }

    public void RestoreLayout()
    {
        Cache();
        CompleteTransition();
        ExitFullScreen();
        maximized = false;
        rect.anchoredPosition = initialPosition;
        rect.sizeDelta = initialSize;
    }

    public void ToggleMaximize()
    {
        Cache();
        CompleteTransition();
        if (!canMaximize || rect.parent is not RectTransform parent) return;
        ExitFullScreen();
        if (!maximized)
        {
            restoredPosition = rect.anchoredPosition;
            restoredSize = rect.sizeDelta;
            var bottom = desktop && desktop.dockController && !desktop.dockController.autoHide ? desktop.dockController.VisibleHeight : 12f;
            rect.anchoredPosition = new Vector2(0f, (bottom - 40f) * 0.5f);
            rect.sizeDelta = parent.rect.size - new Vector2(24f, bottom + 40f);
        }
        else
        {
            rect.anchoredPosition = restoredPosition;
            rect.sizeDelta = restoredSize;
        }
        maximized = !maximized;
        Focus();
    }

    public void ToggleFullScreen()
    {
        Cache();
        CompleteTransition();
        if (!canMaximize || rect.parent is not RectTransform parent) return;
        if (IsFullScreen) ExitFullScreen();
        else
        {
            beforeFullPosition = rect.anchoredPosition;
            beforeFullSize = rect.sizeDelta;
            IsFullScreen = true;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = parent.rect.size;
        }
        Focus();
    }

    public void ExitFullScreen()
    {
        if (!IsFullScreen) return;
        IsFullScreen = false;
        rect.anchoredPosition = beforeFullPosition;
        rect.sizeDelta = beforeFullSize;
    }

    public void SetBackdrop(Camera source)
    {
        Cache();
        foreach (var image in glass)
            if (image) GlassDemoDesktop.SetSource(image, source);
    }

    public void RefreshGlass()
    {
        Cache();
        glass = GetComponentsInChildren<GlassImage>(true);
        var source = GetComponent<GlassImage>().cameraReference;
        foreach (var image in glass)
        {
            GlassDemoDesktop.SetSource(image, source);
            image.enabled = Application.isPlaying ? IsOpen : startOpen;
        }
    }

    internal static void SetGlassSelection(Button button, bool selected, Color accent)
    {
        if (button.image is not GlassImage image)
        {
            button.image.color = new Color(accent.r, accent.g, accent.b, selected ? 0.2f : 0.04f);
            return;
        }
        image.color = selected ? new Color(accent.r, accent.g, accent.b, 1f) : new Color(0.17f, 0.22f, 0.3f, 1f);
        image.appearance.colorMix = selected ? 0.62f : 0.38f;
        image.SetVerticesDirty();
    }

    public void SetFocused(bool focused)
    {
        if (titleLabel) titleLabel.color = new Color(0.97f, 0.98f, 1f, focused ? 1f : 0.6f);
        if (trafficLights == null) return;
        for (var i = 0; i < trafficLights.Length; i++)
            if (trafficLights[i]) trafficLights[i].color = focused ? TrafficColors[i] : new Color(0.65f, 0.69f, 0.75f, 0.6f);
    }

    internal void CompleteTransition()
    {
        if (transition == null) return;
        StopCoroutine(transition);
        transition = null;
        rect.anchoredPosition = restingPosition;
        rect.localScale = Vector3.one;
        IsOpen = transitionOpens;
        SetImmediate(IsOpen);
    }

    private void Animate(bool open, bool throughDock)
    {
        Cache();
        CompleteTransition();
        if (!open) ReleaseInputFocus();
        restingPosition = rect.anchoredPosition;
        transitionOpens = open;
        group.interactable = open;
        group.blocksRaycasts = open;
        var endpoint = restingPosition + Vector2.down * 18f;
        if (throughDock && launcher && rect.parent is RectTransform parent)
        {
            var screen = RectTransformUtility.WorldToScreenPoint(null, launcher.transform.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, windowCanvas.worldCamera, out endpoint);
        }
        transition = StartCoroutine(AnimateRoutine(open, endpoint, throughDock ? 0.12f : 0.96f));
    }

    private IEnumerator AnimateRoutine(bool open, Vector2 endpoint, float endScale)
    {
        var elapsed = 0f;
        while (elapsed < animationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / Mathf.Max(animationDuration, 0.01f));
            var eased = open ? 1f - Mathf.Pow(1f - t, 3f) : t * t * (3f - 2f * t);
            var visibility = open ? eased : 1f - eased;
            group.alpha = Mathf.SmoothStep(0f, 1f, visibility * 2f);
            rect.anchoredPosition = Vector2.Lerp(endpoint, restingPosition, visibility);
            rect.localScale = Vector3.one * Mathf.Lerp(endScale, 1f, visibility);
            yield return null;
        }
        rect.anchoredPosition = restingPosition;
        rect.localScale = Vector3.one;
        IsOpen = open;
        SetImmediate(open);
        transition = null;
        if (desktop) desktop.RefreshStack();
    }

    private void SetImmediate(bool open)
    {
        group.alpha = open ? 1f : 0f;
        group.interactable = open;
        group.blocksRaycasts = open;
        SetGlassEnabled(open);
        if (!desktop && windowCanvas && windowCanvas.worldCamera) windowCanvas.worldCamera.enabled = open;
        if (windowCanvas) windowCanvas.enabled = open;
    }

    private void SetGlassEnabled(bool value)
    {
        foreach (var image in glass)
            if (image) image.enabled = value;
        foreach (var shape in GetComponentsInChildren<UIGlass>(true)) shape.enabled = value;
    }
}
}
