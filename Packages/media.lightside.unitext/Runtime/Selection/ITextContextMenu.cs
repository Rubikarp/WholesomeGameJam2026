using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Standard edit actions a text context menu can request.</summary>
    public enum ContextMenuAction
    {
        Cut,
        Copy,
        Paste,
        SelectAll
    }

    /// <summary>Which standard actions are applicable for the current selection / document state.</summary>
    public readonly struct ContextMenuCapabilities
    {
        public readonly bool CanCut;
        public readonly bool CanCopy;
        public readonly bool CanPaste;
        public readonly bool CanSelectAll;

        /// <summary>Whether a non-collapsed selection exists — distinct from <see cref="CanCopy"/>,
        /// which is additionally gated by copy policy (a password field selects without copying).</summary>
        public readonly bool HasSelection;

        public ContextMenuCapabilities(bool canCut, bool canCopy, bool canPaste, bool canSelectAll, bool hasSelection)
        {
            CanCut = canCut;
            CanCopy = canCopy;
            CanPaste = canPaste;
            CanSelectAll = canSelectAll;
            HasSelection = hasSelection;
        }
    }

    /// <summary>
    /// Text context menu entity owned by a selectable. Implementations receive the complete owner
    /// for their attached lifetime and may present Unity UI, native platform UI, or another menu.
    /// </summary>
    [TypeMenuSuffix("TextContextMenu", "ContextMenu")]
    public interface ITextContextMenu : ISelectableEntity
    {
        /// <summary>
        /// Shows the menu with the given applicability and records <paramref name="presenter"/>
        /// as the single receiver of invoked actions until the next <see cref="Show"/> replaces
        /// it or <see cref="Hide"/> clears it — a menu shared across fields always routes to
        /// the field that last showed it.
        /// </summary>
        /// <param name="screenPosition">Anchor point in screen coordinates, typically above the selection midpoint.</param>
        void Show(Vector2 screenPosition, in ContextMenuCapabilities capabilities, Action<ContextMenuAction> presenter);

        /// <summary>Hides the menu and releases any presenter retained by the current presentation.</summary>
        void Hide();

        /// <summary>Whether this entity is currently presenting a context menu.</summary>
        bool IsVisible { get; }
    }

    /// <summary>
    /// Presents the shipped Unity-UI context menu from a prefab shared through the touch overlay.
    /// Native and other non-prefab menus can implement <see cref="ITextContextMenu"/> directly.
    /// </summary>
    [Serializable]
    [TypeDescription("Unity-UI context menu backed by a touch-overlay prefab")]
    public sealed partial class PrefabTextContextMenu : SelectableEntity, ITextContextMenu
    {
        /// <summary>Unity-UI presentation prefab resolved through the shared touch overlay.</summary>
        [SerializeField, StateProperty(nameof(ReleaseInstance))]
        [Tooltip("UniTextContextMenu prefab presented through the shared touch overlay.")]
        private UniTextContextMenu prefab;

        [NonSerialized] private UniTextContextMenu instance;

        /// <inheritdoc />
        public bool IsVisible => OwnsPresentation && instance.IsVisible;

        /// <inheritdoc />
        public void Show(Vector2 screenPosition, in ContextMenuCapabilities capabilities,
            Action<ContextMenuAction> presenter)
        {
            var current = Resolve();
            if (current == null) return;
            current.PresentationOwner = this;
            current.Show(screenPosition, in capabilities, presenter);
        }

        /// <inheritdoc />
        public void Hide()
        {
            if (OwnsPresentation) instance.Hide();
        }

        protected override void OnDetach() => ReleaseInstance();

        private UniTextContextMenu Resolve()
        {
            if (instance != null) return instance;
            if (prefab == null) return null;
            return instance = TouchOverlayRegistry.GetOrCreate(prefab, RequireOwner().TextComponent.canvas);
        }

        private bool OwnsPresentation
            => instance != null && ReferenceEquals(instance.PresentationOwner, this);

        private void ReleaseInstance()
        {
            if (instance == null) return;
            if (OwnsPresentation) instance.Hide();
            instance = null;
        }
    }
}
