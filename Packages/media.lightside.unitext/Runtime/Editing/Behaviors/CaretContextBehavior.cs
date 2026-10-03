using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Hosts <see cref="CaretContextHandler"/>s — composable consumers of
    /// <see cref="UniTextEditable.CaretContextChanged"/>, each carrying its own serialized
    /// settings (<see cref="StyleStateHandler"/> for toolbar button state; subclass the
    /// handler for floating bubbles or custom context UI). Code-first consumers subscribe
    /// <see cref="ContextChanged"/> directly.
    /// </summary>
    [Serializable]
    [TypeDescription("Caret context: composable handlers reacting to the styles at the caret (toolbar state)")]
    [TypeGroup("Field", 4)]
    public sealed partial class CaretContextBehavior : InputBehavior
    {
        /// <summary>Handlers receiving every caret-context change.</summary>
        [SerializeField, StateList(nameof(ApplyHandlersChange), Owned = true,
            AllowNullItems = false, AllowDuplicateReferences = false)]
        [Tooltip("Handlers receiving every caret-context change, each with its own settings.")]
        private TypedList<CaretContextHandler> handlers = new();

        [NonSerialized] private ReferenceBinding<CaretContextHandler> boundHandlers;

        /// <summary>Occurs when the caret context has changed, once per change with the full payload.</summary>
        public event Action<CaretContext> ContextChanged;

        internal override void OnChangeSinkChanged(
            IInputBehaviorChangeSink previous, IInputBehaviorChangeSink current)
        {
            if (current != null) BindHandlers();
            else UnbindHandlers();
        }

        protected override void OnEnable()
        {
            editable.CaretContextChanged += OnCaretContextChanged;
            for (var i = 0; i < (boundHandlers?.Count ?? 0); i++)
                boundHandlers[i].Attach(editable);
        }

        protected override void OnDisable()
        {
            editable.CaretContextChanged -= OnCaretContextChanged;
            for (var i = 0; i < (boundHandlers?.Count ?? 0); i++)
                boundHandlers[i].Detach();
        }

        internal void OnHandlerStructureChanged()
            => NotifyStructureChanged(Members.Handlers);

        private void ApplyHandlersChange()
        {
            if (HasChangeSink) BindHandlers();
            NotifyStructureChanged(Members.Handlers);
        }

        private void BindHandlers()
        {
            boundHandlers ??= new ReferenceBinding<CaretContextHandler>(ConnectHandler, DisconnectHandler);
            boundHandlers.Reconcile(handlers);
        }

        private void UnbindHandlers() => boundHandlers?.Clear();

        private void ConnectHandler(CaretContextHandler handler)
        {
            handler.SetOwner(this);
            if (IsEnabled) handler.Attach(editable);
        }

        private void DisconnectHandler(CaretContextHandler handler)
        {
            if (!ReferenceEquals(handler.Owner, this)) return;
            handler.Detach();
            handler.SetOwner(null);
        }

        private void OnCaretContextChanged(CaretContext change)
        {
            for (var i = 0; i < (boundHandlers?.Count ?? 0); i++)
                boundHandlers[i].OnContextChanged(in change);
            ContextChanged?.Invoke(change);
        }
    }
}
