using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace WG.Common
{
    public class SimpleDragElement : UIBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private Canvas canvas;
        private RectTransform rect;

        protected override void Start()
        {
            base.Start();
            canvas = GetComponentInParent<Canvas>();
            rect = transform.GetComponent<RectTransform>();
        }

        public void OnBeginDrag(PointerEventData eventData) { }
        public void OnEndDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData)
        {
            if (!Pointer.current.IsPressed()) return;
            rect.anchoredPosition += eventData.delta / canvas.scaleFactor;
        }
    }
}
