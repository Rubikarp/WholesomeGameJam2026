using Alchemy.Inspector;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine;

[System.Serializable]
public class DragElementEvent : UnityEvent<DragElement, PointerEventData> { }

[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(RectTransform))]
public class DragElement : MonoBehaviour,
    IDragHandler, IBeginDragHandler, IEndDragHandler,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    public CanvasGroup CanvasGroup => canvasGroup ?? (canvasGroup = GetComponent<CanvasGroup>());
    private CanvasGroup canvasGroup;
    public RectTransform RectTransform => rectTransform ?? (rectTransform = GetComponent<RectTransform>());
    private RectTransform rectTransform;
    public bool IsInteractable 
    { 
        get => CanvasGroup.interactable;
        set => CanvasGroup.interactable = value;
    }

    [field: Header("Info")]
    [field: SerializeField, ReadOnly] public bool IsHovering { get; private set; }
    [field: SerializeField, ReadOnly] public bool isPressed { get; private set; }
    [field: SerializeField, ReadOnly] public bool isDragging { get; private set; }
    [SerializeField, ReadOnly] private Vector3 dragOffset;
    [SerializeField, ReadOnly] private Vector3 targetPosition;

    [Header("Events")]
    public DragElementEvent onHoverEnter = new DragElementEvent();
    public DragElementEvent onHoverExit = new DragElementEvent();
    [Space]
    public DragElementEvent onPressDown = new DragElementEvent();
    public DragElementEvent onPressUp = new DragElementEvent();
    [Space]
    public DragElementEvent onDragBegin = new DragElementEvent();
    public DragElementEvent onDrag = new DragElementEvent();
    public DragElementEvent onDragEnd = new DragElementEvent();

    protected virtual void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        rectTransform = GetComponent<RectTransform>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!IsInteractable) return;

        isDragging = true;
        onDragBegin.Invoke(this, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!IsInteractable) return;

        // Convert pointer position to local point in parent rect
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out var localPointerPos
        );
        targetPosition = (Vector3)localPointerPos + dragOffset;
        rectTransform.localPosition = targetPosition;

        onDrag.Invoke(this, eventData);
    }
    public void OnEndDrag(PointerEventData eventData)
    {
        if (!IsInteractable) return;

        isDragging = false;
        onDragEnd.Invoke(this, eventData);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!IsInteractable) return;

        IsHovering = true;
        onHoverEnter.Invoke(this, eventData);
    }
    public void OnPointerExit(PointerEventData eventData)
    {
        if (!IsInteractable) return;

        IsHovering = false;
        onHoverExit.Invoke(this, eventData);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!IsInteractable) return;

        // Convert pointer position to local point in parent rect
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out var localPointerPos
        );
        dragOffset = rectTransform.localPosition - (Vector3)localPointerPos;

        isPressed = true;
        onPressDown.Invoke(this, eventData);
    }
    public void OnPointerUp(PointerEventData eventData)
    {
        if (!IsInteractable) return;

        isPressed = false;
        onPressUp.Invoke(this, eventData);
    }
}
