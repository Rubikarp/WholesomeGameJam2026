using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Magnifier entity that appears during long-press cursor placement and selection-handle dragging.
    /// The implementation owns its presentation mechanism.
    /// </summary>
    /// <remarks>
    /// The magnifier is shown during long-press (for precise cursor placement) and during
    /// selection handle drag. It hides on pointer up / drag end.
    /// </remarks>
    [TypeMenuSuffix("Magnifier")]
    public interface IMagnifier : ISelectableEntity
    {
        /// <summary>
        /// Shows the magnifier centered on the specified position.
        /// </summary>
        /// <param name="position">
        /// The focal point in screen coordinates — the text position being magnified.
        /// The magnifier renders above this point to avoid being occluded by the finger.
        /// </param>
        void Show(Vector2 position);

        /// <summary>
        /// Hides the magnifier.
        /// </summary>
        void Hide();

        /// <summary>
        /// Updates the magnifier's focal position while it is visible.
        /// </summary>
        /// <param name="position">New focal point in screen coordinates.</param>
        void UpdatePosition(Vector2 position);
    }

    /// <summary>
    /// Presents the shipped Unity-UI magnifier from a prefab shared through the touch overlay.
    /// The prefab instance is created only when the owner first shows the magnifier.
    /// </summary>
    [Serializable]
    [TypeDescription("Unity-UI magnifier backed by a touch-overlay prefab")]
    public sealed partial class PrefabMagnifier : SelectableEntity, IMagnifier
    {
        /// <summary>Unity-UI presentation prefab resolved through the shared touch overlay.</summary>
        [SerializeField, StateProperty(nameof(ReleaseInstance))]
        [Tooltip("UniTextMagnifier prefab presented through the shared touch overlay.")]
        private UniTextMagnifier prefab;

        [NonSerialized] private UniTextMagnifier instance;

        /// <inheritdoc />
        public void Show(Vector2 position)
        {
            var current = Resolve();
            if (current == null) return;
            current.PresentationOwner = this;
            current.Show(position);
        }

        /// <inheritdoc />
        public void Hide()
        {
            if (OwnsPresentation) instance.Hide();
        }

        /// <inheritdoc />
        public void UpdatePosition(Vector2 position)
        {
            if (OwnsPresentation) instance.UpdatePosition(position);
        }

        protected override void OnDetach() => ReleaseInstance();

        private UniTextMagnifier Resolve()
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
