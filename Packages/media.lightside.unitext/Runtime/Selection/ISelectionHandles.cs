using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Touch handle entity assigned to a selectable. Implementations may provide selection handles,
    /// an insertion handle, or both through the corresponding capability interfaces.
    /// </summary>
    [StateHierarchy, TypeMenuSuffix("SelectionHandles", "InsertionHandle", "Handles")]
    public interface ITouchHandles : ISelectableEntity { }

    /// <summary>
    /// Selection-handle capability on touch platforms: two draggable endpoints the user moves
    /// to adjust selection boundaries. The entity owns its presentation; the editable only drives it.
    /// </summary>
    /// <remarks>
    /// <see cref="Show"/> is called when a selection is established, <see cref="UpdatePositions"/>
    /// while it remains visible, and <see cref="Hide"/> when the selection collapses or focus is lost.
    /// </remarks>
    public interface ISelectionHandles : ITouchHandles
    {
        /// <summary>Shows both selection endpoints.</summary>
        void Show();

        /// <summary>Hides both selection endpoints.</summary>
        void Hide();

        /// <summary>Updates both endpoints from the current owner state.</summary>
        void UpdatePositions();

        /// <summary>
        /// Occurs when the user is dragging the anchor handle. The parameter is the drag position
        /// in screen coordinates, to be hit-tested into the new anchor codepoint.
        /// </summary>
        event Action<Vector2> AnchorDragged;

        /// <summary>
        /// Occurs when the user is dragging the focus handle. The parameter is the drag position
        /// in screen coordinates, to be hit-tested into the new focus codepoint.
        /// </summary>
        event Action<Vector2> FocusDragged;

        /// <summary>Occurs when a selection-handle drag begins (either handle) — a cue to hide transient UI such as the context menu for the duration of the drag.</summary>
        event Action SelectionHandleDragStarted;

        /// <summary>Occurs when a selection-handle drag ends (either handle).</summary>
        event Action SelectionHandleDragEnded;
    }

    /// <summary>
    /// Presents the shipped Unity-UI selection and insertion handles from a prefab shared through
    /// the touch overlay. The managed entity remains owned by one selectable while the prefab instance
    /// is resolved lazily and can be shared by multiple fields.
    /// </summary>
    [Serializable]
    [TypeDescription("Unity-UI selection and insertion handles backed by a touch-overlay prefab")]
    public sealed partial class PrefabSelectionHandles : SelectableEntity, ISelectionHandles, IInsertionHandle
    {
        /// <summary>Unity-UI presentation prefab resolved through the shared touch overlay.</summary>
        [SerializeField, StateProperty(nameof(ReleaseInstance))]
        [Tooltip("UniTextSelectionHandles prefab presented through the shared touch overlay.")]
        private UniTextSelectionHandles prefab;

        [NonSerialized] private UniTextSelectionHandles instance;
        [NonSerialized] private Action<Vector2> anchorDragged;
        [NonSerialized] private Action<Vector2> focusDragged;
        [NonSerialized] private Action selectionHandleDragStarted;
        [NonSerialized] private Action selectionHandleDragEnded;
        [NonSerialized] private Action<Vector2> insertionHandleDragged;
        [NonSerialized] private Action insertionHandleTapped;
        [NonSerialized] private Action insertionHandleDragStarted;
        [NonSerialized] private Action insertionHandleDragEnded;

        /// <inheritdoc />
        public event Action<Vector2> AnchorDragged
        {
            add => anchorDragged += value;
            remove => anchorDragged -= value;
        }

        /// <inheritdoc />
        public event Action<Vector2> FocusDragged
        {
            add => focusDragged += value;
            remove => focusDragged -= value;
        }

        /// <inheritdoc />
        public event Action SelectionHandleDragStarted
        {
            add => selectionHandleDragStarted += value;
            remove => selectionHandleDragStarted -= value;
        }

        /// <inheritdoc />
        public event Action SelectionHandleDragEnded
        {
            add => selectionHandleDragEnded += value;
            remove => selectionHandleDragEnded -= value;
        }

        /// <inheritdoc />
        public event Action<Vector2> InsertionHandleDragged
        {
            add => insertionHandleDragged += value;
            remove => insertionHandleDragged -= value;
        }

        /// <inheritdoc />
        public event Action InsertionHandleTapped
        {
            add => insertionHandleTapped += value;
            remove => insertionHandleTapped -= value;
        }

        /// <inheritdoc />
        public event Action InsertionHandleDragStarted
        {
            add => insertionHandleDragStarted += value;
            remove => insertionHandleDragStarted -= value;
        }

        /// <inheritdoc />
        public event Action InsertionHandleDragEnded
        {
            add => insertionHandleDragEnded += value;
            remove => insertionHandleDragEnded -= value;
        }

        void ISelectionHandles.Show()
        {
            var current = Resolve();
            if (current == null) return;
            current.PresentationOwner = this;
            current.ShowSelection();
        }

        void ISelectionHandles.Hide()
        {
            if (OwnsPresentation) instance.HideSelection();
        }

        void ISelectionHandles.UpdatePositions()
        {
            if (OwnsPresentation) instance.UpdateSelectionPositions();
        }

        void IInsertionHandle.Show()
        {
            var current = Resolve();
            if (current == null) return;
            current.PresentationOwner = this;
            current.ShowInsertion();
        }

        void IInsertionHandle.Hide()
        {
            if (OwnsPresentation) instance.HideInsertion();
        }

        void IInsertionHandle.UpdatePosition()
        {
            if (OwnsPresentation) instance.UpdateInsertionPosition();
        }

        protected override void OnDetach() => ReleaseInstance();

        private UniTextSelectionHandles Resolve()
        {
            if (instance != null) return instance;
            if (prefab == null) return null;

            instance = TouchOverlayRegistry.GetOrCreate(prefab, RequireOwner().TextComponent.canvas);
            instance.AnchorDragged += RelayAnchorDragged;
            instance.FocusDragged += RelayFocusDragged;
            instance.SelectionHandleDragStarted += RelaySelectionHandleDragStarted;
            instance.SelectionHandleDragEnded += RelaySelectionHandleDragEnded;
            instance.InsertionHandleDragged += RelayInsertionHandleDragged;
            instance.InsertionHandleTapped += RelayInsertionHandleTapped;
            instance.InsertionHandleDragStarted += RelayInsertionHandleDragStarted;
            instance.InsertionHandleDragEnded += RelayInsertionHandleDragEnded;
            return instance;
        }

        private bool OwnsPresentation
            => instance != null && ReferenceEquals(instance.PresentationOwner, this);

        private void ReleaseInstance()
        {
            if (instance == null) return;

            instance.AnchorDragged -= RelayAnchorDragged;
            instance.FocusDragged -= RelayFocusDragged;
            instance.SelectionHandleDragStarted -= RelaySelectionHandleDragStarted;
            instance.SelectionHandleDragEnded -= RelaySelectionHandleDragEnded;
            instance.InsertionHandleDragged -= RelayInsertionHandleDragged;
            instance.InsertionHandleTapped -= RelayInsertionHandleTapped;
            instance.InsertionHandleDragStarted -= RelayInsertionHandleDragStarted;
            instance.InsertionHandleDragEnded -= RelayInsertionHandleDragEnded;
            if (OwnsPresentation) instance.HideAll();
            instance = null;
        }

        private void RelayAnchorDragged(Vector2 position) => anchorDragged?.Invoke(position);
        private void RelayFocusDragged(Vector2 position) => focusDragged?.Invoke(position);
        private void RelaySelectionHandleDragStarted() => selectionHandleDragStarted?.Invoke();
        private void RelaySelectionHandleDragEnded() => selectionHandleDragEnded?.Invoke();
        private void RelayInsertionHandleDragged(Vector2 position) => insertionHandleDragged?.Invoke(position);
        private void RelayInsertionHandleTapped() => insertionHandleTapped?.Invoke();
        private void RelayInsertionHandleDragStarted() => insertionHandleDragStarted?.Invoke();
        private void RelayInsertionHandleDragEnded() => insertionHandleDragEnded?.Invoke();
    }
}
