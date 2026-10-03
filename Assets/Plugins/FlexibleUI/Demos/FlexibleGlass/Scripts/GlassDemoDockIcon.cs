using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoDockIcon : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public Text tooltip;
    public GlassDemoWindow window;
    public GameObject runningIndicator;
    public bool Hovered { get; private set; }
    public float Emphasis => emphasis;
    public float hoverScale = 1.42f;
    private Coroutine launchAnimation;
    private Vector2 restingPosition;
    private float bounce;
    private float emphasis;

    private void Awake()
    {
        restingPosition = ((RectTransform)transform).anchoredPosition;
        if (tooltip)
            tooltip.transform.parent.gameObject.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Hovered = true;
        if (tooltip)
            tooltip.transform.parent.gameObject.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Hovered = false;
        if (tooltip)
            tooltip.transform.parent.gameObject.SetActive(false);
    }

    public void RefreshIndicator()
    {
        if (runningIndicator) runningIndicator.SetActive(Application.isPlaying ? window.IsRunning : window.startOpen);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right && window && window.desktop.menuBar)
            window.desktop.menuBar.ShowDockMenu(this);
    }

    public void Launch()
    {
        if (launchAnimation != null) StopCoroutine(launchAnimation);
        launchAnimation = StartCoroutine(LaunchRoutine());
    }

    private IEnumerator LaunchRoutine()
    {
        var elapsed = 0f;
        const float duration = 0.42f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / duration);
            bounce = Mathf.Sin(t * Mathf.PI) * (1f - t) * 22f;
            UpdatePosition();
            yield return null;
        }
        bounce = 0f;
        UpdatePosition();
        launchAnimation = null;
    }

    public void UpdateEmphasis(float target, float deltaTime)
    {
        emphasis = Mathf.Lerp(emphasis, target, 1f - Mathf.Exp(-22f * deltaTime));
        if (Mathf.Abs(emphasis - target) < 0.001f) emphasis = target;
        transform.localScale = Vector3.one * Mathf.Lerp(1f, hoverScale, emphasis);
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        ((RectTransform)transform).anchoredPosition = restingPosition + Vector2.up * (bounce + (transform.localScale.x - 1f) * 28f);
    }
}
}
