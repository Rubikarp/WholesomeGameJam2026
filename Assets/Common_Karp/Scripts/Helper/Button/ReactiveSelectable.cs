using Alchemy.Inspector;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WorldGame.UI
{
    public abstract class ReactiveSelectable : Selectable, IPointerClickHandler, ISubmitHandler
    {
        [ShowInInspector] private EReactState State => AsReactState(currentSelectionState);
        
        [FoldoutGroup("Events")]
        public UnityEvent onClick;
        
        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            OnClick();
        }
        public void OnPointerClick(PointerEventData eventData) {}
        public void OnSubmit(BaseEventData eventData)
        {
            if (!IsActive() || !IsInteractable())
                return;
            OnClick();
            
            // if we get set disabled during the click don't run the transition.
            if (!IsActive() || !IsInteractable())
                return;
            DoStateTransition(SelectionState.Pressed, false);
        }

        protected virtual void OnClick()
        {
            onClick.Invoke();
        }
        
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            ApplyState(AsReactState(state), instant);
        }
        protected abstract void ApplyState(EReactState state, bool isInstant);
        
        /// <summary>
        /// Normal => Default State |
        /// Focused => Whenever the button is Selected/Highlighted/Hover... |
        /// Pressed => Whenever the button is pressed |
        /// Disabled => Disabled State 
        /// </summary>
        private static EReactState AsReactState(SelectionState state) => state switch
        {
            SelectionState.Normal => EReactState.Normal,
            SelectionState.Highlighted => EReactState.Focused,
            SelectionState.Pressed => EReactState.Pressed,
            SelectionState.Selected => EReactState.Pressed,
            SelectionState.Disabled => EReactState.Disabled,
            _ => EReactState.Normal,
        };


#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            transition = Transition.None; 
        }
        
        protected override void OnValidate()
        {
            base.OnValidate();
            transition = Transition.None;

            // Différé : OnValidate interdit de toucher à d'autres objets pendant la sérialisation.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null)
                {
                    return;
                }
                ApplyState(AsReactState(currentSelectionState), true);
            };
        }
#endif
    }
}
