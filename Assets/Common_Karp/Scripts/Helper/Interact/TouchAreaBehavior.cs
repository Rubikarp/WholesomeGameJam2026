using Alchemy.Inspector;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// UiBehavior cliquable qui émet la position sur une grille locale au RectTransform.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class TouchAreaBehavior : MonoBehaviour, 
    IPointerClickHandler, IBeginDragHandler, IEndDragHandler, IDragHandler
{
    [field: Header("Interaction")]
    [field: SerializeField] public bool IsInteractable { get; set; } = true;
 
    [Header("Signal")]
    public UnityEvent<Vector2> OnAreaTouched;
 
    [Header("Debug")]
    [SerializeField , ReadOnly] private Vector2 CurrentTouchPos = new Vector2(-1, -1);
 
    private RectTransform _rectTransform;
    
    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }
 
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!IsInteractable) { return; }
 
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return;
        }
 
        OnAreaTouched.Invoke(NormalizePos(localPoint));
    }
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!IsInteractable) { return; }
 
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return;
        }
 
        OnAreaTouched.Invoke(NormalizePos(localPoint));
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!IsInteractable) { return; }
 
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return;
        }
 
        OnAreaTouched.Invoke(NormalizePos(localPoint));
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        CurrentTouchPos = new Vector2(-1, -1);
    }
    public void OnEndDrag(PointerEventData eventData)
    {
        if (!IsInteractable) { return; }
 
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return;
        }
 
        OnAreaTouched.Invoke(NormalizePos(localPoint));
        CurrentTouchPos = new Vector2(-1, -1);
    }
 
    private Vector2 NormalizePos(Vector2 localPoint)
    {
        Rect rect = _rectTransform.rect;
 
        float normalizedX = (localPoint.x - rect.xMin) / rect.width;
        float normalizedY = (localPoint.y - rect.yMin) / rect.height;
        
        CurrentTouchPos = new Vector2(normalizedX, normalizedY);
        return CurrentTouchPos;
    }
}