using Alchemy.Inspector;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class ScrollViewFocusPoint : UIBehaviour, IPointerClickHandler
{
    [Header("References")]
    [SerializeField] private ScrollView _scrollView;

    [Header("Settings")]
    [SerializeField] private bool IsInteractible = true;

    [Header("Events")]
    public UnityEvent<Vector2> OnFocus;

    [Button]
    public void Focus()
    {
        if (_scrollView == null) return;
        Vector2 normalizedPosition = _scrollView.GetNormalizedPositionOf((RectTransform)transform);
        OnFocus?.Invoke(normalizedPosition);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!IsInteractible) return;
        Focus();
    }
}