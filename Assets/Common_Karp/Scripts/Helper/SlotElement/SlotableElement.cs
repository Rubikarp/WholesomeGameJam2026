using Alchemy.Inspector;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

[RequireComponent(typeof(DragElement))]
public class SlotElement : MonoBehaviour
{
    [SerializeField] private DragElement dragElement;

    public UnityEvent<SlotElement, SlotSpace> onPlacedInSlot = new UnityEvent<SlotElement, SlotSpace>();
    public UnityEvent<SlotElement> onRemovedFromSlot = new UnityEvent<SlotElement>();
    public UnityEvent<SlotElement> onReturnToPreviousSlot = new UnityEvent<SlotElement>();

    private void Awake()
    {
        if (dragElement == null) dragElement = GetComponent<DragElement>();
        dragElement.onDragBegin.AddListener(OnBeginDrag);
        dragElement.onDragEnd.AddListener(OnEndDrag);
    }

    private void OnBeginDrag(DragElement dragElement, PointerEventData eventData)
    {
        onRemovedFromSlot.Invoke(this);
    }
    private void OnEndDrag(DragElement dragElement, PointerEventData eventData)
    {
        SlotSpace slotSpace = null;
        if (eventData.pointerEnter != null)
        {
            slotSpace = eventData.pointerEnter.GetComponent<SlotSpace>();
        }
        if (slotSpace != null)
        {
            // If the slot space already has an element, do nothing
            if (slotSpace.currentElement != null) return;
        }
    }
    public void GetSlotedIn(SlotSpace slotSpace)
    {
        dragElement.RectTransform.SetParent(slotSpace.transform, false);
        dragElement.RectTransform.localPosition = Vector3.zero;
    }
}

[RequireComponent(typeof(RectTransform))]
public class SlotSpace : MonoBehaviour
{
    [field: SerializeField, ReadOnly]
    public SlotElement currentElement { get; private set; } = null;

    public UnityEvent<SlotSpace, SlotElement> onElementPlaced = new UnityEvent<SlotSpace, SlotElement>();
    public UnityEvent<SlotSpace, SlotElement> onElementRemoved = new UnityEvent<SlotSpace, SlotElement>();


    protected virtual bool IsOccupied() => currentElement != null;
    public virtual void PlaceElement(SlotElement element)
    {
        currentElement = element;
        currentElement.onRemovedFromSlot.AddListener(OnElementRemoved);
        element.GetSlotedIn(this);

        onElementPlaced.Invoke(this, element);
    }
    private void OnElementRemoved(SlotElement element)
    {
        if (currentElement != element) return;

        currentElement.onRemovedFromSlot.RemoveListener(OnElementRemoved);
        currentElement = null;
        onElementRemoved.Invoke(this, element);
    }

}