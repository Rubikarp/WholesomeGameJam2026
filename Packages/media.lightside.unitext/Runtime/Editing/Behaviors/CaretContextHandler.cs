using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Composable caret-context consumer hosted by <see cref="CaretContextBehavior"/>. A
    /// handler carries its own serialized settings, receives every frame-coalesced context
    /// change, and can query the source editor through
    /// <see cref="CaretContext.Editable"/> (<see cref="UniTextEditable.IsStyleActive(Type)"/>,
    /// <see cref="UniTextEditable.TryGetStyleParameter(Type, out string)"/>, caret geometry).
    /// Subclass for toolbar state, floating formatting bubbles, context-sensitive hints.
    /// </summary>
    [Serializable]
    [StateHierarchy]
    public abstract class CaretContextHandler
    {
        [NonSerialized] private CaretContextBehavior owner;
        [NonSerialized] private UniTextEditable attachedEditable;

        protected bool HasOwner => owner != null;

        internal CaretContextBehavior Owner => owner;

        public abstract void OnContextChanged(in CaretContext change);

        internal virtual void SetOwner(CaretContextBehavior behavior)
        {
            if (ReferenceEquals(owner, behavior))
                return;
            Detach();
            owner = behavior;
        }

        internal void Attach(UniTextEditable editable)
        {
            if (editable == null) throw new ArgumentNullException(nameof(editable));
            if (ReferenceEquals(attachedEditable, editable)) return;
            Detach();
            attachedEditable = editable;
            OnAttach(editable);
        }

        internal void Detach()
        {
            if (attachedEditable == null) return;
            var current = attachedEditable;
            attachedEditable = null;
            OnDetach(current);
        }

        protected void NotifyStructureChanged() => owner?.OnHandlerStructureChanged();

        /// <summary>Called when the hosting behavior enables on an editor.</summary>
        protected internal virtual void OnAttach(UniTextEditable editable) { }

        /// <summary>Called when the hosting behavior disables.</summary>
        protected internal virtual void OnDetach(UniTextEditable editable) { }
    }

    /// <summary>
    /// Edge-triggered toolbar-state handler: tracks whether typing now produces the configured
    /// style (<see cref="UniTextEditable.IsStyleActive(Type)"/> — pending typing styles win)
    /// and raises <see cref="Changed"/> only when the value flips, plus once with the initial
    /// state on attach.
    /// </summary>
    [Serializable]
    [TypeDescription("Style state: fires a bool when the configured style turns on/off at the caret")]
    public sealed partial class StyleStateHandler : CaretContextHandler, IModifierChangeSink
    {
        /// <summary>Modifier signature whose active caret state is observed.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyStyleChange),
            Validator = nameof(ValidateStyle), Owned = true)]
        [Tooltip("The modifier whose caret state drives the events.")]
        private BaseModifier style;

        [NonSerialized] private bool lastState;
        [NonSerialized] private bool hasState;

        /// <summary>Occurs when the configured style's caret state flips and once on attach.</summary>
        public event Action<bool> Changed;

        internal override void SetOwner(CaretContextBehavior behavior)
        {
            if (ReferenceEquals(Owner, behavior))
            {
                base.SetOwner(behavior);
                return;
            }
            if (behavior != null)
                BaseModifier.ValidateGraph(style);
            base.SetOwner(behavior);
            style?.SetChangeSink(behavior == null ? null : this);
        }

        protected internal override void OnAttach(UniTextEditable editable)
        {
            hasState = false;
            Dispatch(editable);
        }

        public override void OnContextChanged(in CaretContext change) => Dispatch(change.Editable);

        private void Dispatch(UniTextEditable editable)
        {
            if (style == null || editable == null) return;
            var state = editable.IsStyleActive(style);
            if (hasState && state == lastState) return;
            hasState = true;
            lastState = state;
            Changed?.Invoke(state);
        }

        private void ApplyStyleChange(BaseModifier previous, ref BaseModifier current)
        {
            if (ReferenceEquals(previous, current)) return;
            if (ReferenceEquals(previous?.ChangeSink, this)) previous.SetChangeSink(null);
            if (HasOwner) current?.SetChangeSink(this);
            NotifyStructureChanged();
        }

        private void ValidateStyle(BaseModifier candidate)
            => BaseModifier.ValidateGraph(candidate);

        void IModifierChangeSink.MarkModifierChanged(BaseModifier modifier, UniTextDirty flags,
            IStateMemberReplay source, StateMember member, bool structural)
            => NotifyStructureChanged();
    }
}
