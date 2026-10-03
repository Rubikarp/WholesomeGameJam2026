using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// Navigation / focus primitive for an interactive UniText surface — the EventSystem tab stop
    /// that lets the GameObject hold focus. Created and owned at runtime (hidden, not serialized)
    /// by <see cref="Sync"/>; never added manually.
    /// </summary>
    public class UniTextFocusable : Selectable, ISubmitHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private UniTextBase text;
        private UniTextInteractions interactions;
        private bool interactionMode;
        private bool dragOwnedBySelectable;
        private bool keySubscribed;
        private bool forwardingDrag;
        private GameObject parentDragReceiver;

        /// <summary>
        /// Reconciles the hidden focusable with the components present on <paramref name="go"/>:
        /// editable → a navigation tab stop (<see cref="Navigation.Mode.Automatic"/>); selection-only →
        /// focusable by pointer but excluded from automatic navigation (<see cref="Navigation.Mode.None"/>,
        /// the web model — you can't Tab to a paragraph); neither → disabled. Call from the
        /// <c>OnEnable</c> / <c>OnDisable</c> of both components; the result depends only on current
        /// component state, so call order doesn't matter.
        /// </summary>
        internal static void Sync(GameObject go)
        {
            var editable = go.GetComponent<UniTextEditable>();
            var selectable = go.GetComponent<UniTextSelectable>();
            var text = go.GetComponent<UniTextBase>();
            bool editing = editable != null && editable.isActiveAndEnabled;
            bool selecting = selectable != null && selectable.isActiveAndEnabled;
            UniTextInteractions interactions = null;
            bool interacting = text != null &&
                               UniTextInteractions.TryGet(text, out interactions) &&
                               interactions.HasTargets;

            var focusable = go.GetComponent<UniTextFocusable>();
            if (!editing && !selecting && !interacting)
            {
                if (focusable != null)
                {
                    focusable.SetInteractionMode(false);
                    focusable.interactions = null;
                    focusable.enabled = false;
                }
                return;
            }

            if (focusable == null)
            {
                if (!go.activeInHierarchy) return;
                focusable = go.AddComponent<UniTextFocusable>();
                focusable.transition = Transition.None;
            }

            focusable.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
            var interactionMode = interacting && !editing;
            if (focusable.interactionMode &&
                (!interactionMode || !ReferenceEquals(focusable.interactions, interactions)))
                focusable.SetInteractionMode(false);
            focusable.text = text;
            focusable.interactions = interactions;
            focusable.dragOwnedBySelectable = selecting;
            focusable.SetInteractionMode(interactionMode);
            focusable.enabled = true;
            focusable.navigation = new Navigation
            {
                mode = editing || interacting ? Navigation.Mode.Automatic : Navigation.Mode.None
            };
        }

        /// <inheritdoc/>
        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            if (!interactionMode || text == null) return;
            interactions.OnHostSelected();
            SubscribeKeys();
        }

        /// <inheritdoc/>
        public override void OnDeselect(BaseEventData eventData)
        {
            if (interactionMode && interactions != null) interactions.OnHostDeselected();
            UnsubscribeKeys();
            base.OnDeselect(eventData);
        }

        /// <inheritdoc/>
        public override void OnMove(AxisEventData eventData)
        {
            if (interactionMode && text != null && interactions.OnHostMove(eventData.moveDir))
            {
                eventData.Use();
                return;
            }
            base.OnMove(eventData);
        }

        /// <inheritdoc/>
        public void OnSubmit(BaseEventData eventData)
        {
            if (!interactionMode || interactions == null || !interactions.OnHostSubmit()) return;
            eventData.Use();
        }

        void IInitializePotentialDragHandler.OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (!CanRouteInteractionDrag()) return;
            forwardingDrag = false;
            parentDragReceiver = null;
            var parent = transform.parent;
            if (parent == null) return;
            parentDragReceiver = ExecuteEvents.GetEventHandler<IBeginDragHandler>(parent.gameObject);
            if (parentDragReceiver != null)
                ExecuteEvents.Execute(parentDragReceiver, eventData,
                    ExecuteEvents.initializePotentialDrag);
        }

        void IBeginDragHandler.OnBeginDrag(PointerEventData eventData)
        {
            if (!CanRouteInteractionDrag()) return;
            var arbitration = interactions.OnHostDrag(eventData);
            forwardingDrag = !arbitration.claimed ||
                             (arbitration.compatibility &
                              RangeGestureCompatibility.ParentScroll) != 0;
            if (forwardingDrag && parentDragReceiver != null)
                ExecuteEvents.Execute(parentDragReceiver, eventData, ExecuteEvents.beginDragHandler);
        }

        void IDragHandler.OnDrag(PointerEventData eventData)
        {
            if (!CanRouteInteractionDrag()) return;
            var arbitration = interactions.OnHostDrag(eventData);
            if (forwardingDrag && arbitration.claimed &&
                (arbitration.compatibility & RangeGestureCompatibility.ParentScroll) == 0)
            {
                if (parentDragReceiver != null)
                    ExecuteEvents.Execute(parentDragReceiver, eventData,
                        ExecuteEvents.endDragHandler);
                forwardingDrag = false;
            }
            if (forwardingDrag && parentDragReceiver != null)
                ExecuteEvents.Execute(parentDragReceiver, eventData, ExecuteEvents.dragHandler);
        }

        void IEndDragHandler.OnEndDrag(PointerEventData eventData)
        {
            if (!CanRouteInteractionDrag()) return;
            interactions.OnHostDragEnded(eventData);
            if (forwardingDrag && parentDragReceiver != null)
                ExecuteEvents.Execute(parentDragReceiver, eventData, ExecuteEvents.endDragHandler);
            forwardingDrag = false;
            parentDragReceiver = null;
        }

        protected override void OnDisable()
        {
            if (interactionMode && interactions != null) interactions.OnHostDeselected();
            UnsubscribeKeys();
            forwardingDrag = false;
            parentDragReceiver = null;
            base.OnDisable();
        }

        private bool CanRouteInteractionDrag()
            => interactionMode && !dragOwnedBySelectable && text != null;

        private void SubscribeKeys()
        {
            if (keySubscribed) return;
            keySubscribed = true;
            NativeKeyInputSession.Subscribe(gameObject, OnKeyDown);
        }

        private void SetInteractionMode(bool value)
        {
            if (interactionMode == value) return;
            if (interactionMode && interactions != null) interactions.OnHostDeselected();
            interactionMode = value;
            if (!value)
            {
                UnsubscribeKeys();
                return;
            }
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
            {
                interactions?.OnHostSelected();
                SubscribeKeys();
            }
        }

        private void UnsubscribeKeys()
        {
            if (!keySubscribed) return;
            keySubscribed = false;
            NativeKeyInputSession.Unsubscribe(gameObject, OnKeyDown);
        }

        private void OnKeyDown(NativeKeyCode key, NativeModifiers modifiers)
        {
            if (!interactionMode || text == null) return;
            switch (key)
            {
                case NativeKeyCode.Tab:
                {
                    var previous = (modifiers & NativeModifiers.Shift) != 0;
                    if (interactions.OnHostMove(previous)) return;
                    var next = previous ? FindSelectableOnUp() : FindSelectableOnDown();
                    next?.Select();
                    break;
                }
                case NativeKeyCode.Return:
                case NativeKeyCode.KeypadEnter:
                    interactions.OnHostSubmit();
                    break;
                case NativeKeyCode.Escape:
                    interactions.Blur();
                    break;
            }
        }
    }
}
