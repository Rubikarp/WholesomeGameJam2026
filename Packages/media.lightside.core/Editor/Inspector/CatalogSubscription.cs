using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// One live subscription to a named catalog on behalf of a panel-attached element.
    /// <see cref="Track"/> swaps the subscription with identity balance and refuses to hold one while
    /// the element is off a panel; detaching from the panel releases it automatically, so a drawer
    /// re-Tracks from its attach-driven rebuild and never leaks a handler.
    /// </summary>
    public sealed class CatalogSubscription<TEntry>
    {
        private readonly VisualElement owner;
        private readonly NamedCatalogChangedHandler<TEntry> handler;
        private INamedCatalog<TEntry> subscribed;

        /// <summary>Creates the subscription holder for <paramref name="owner"/>, delivering changes to <paramref name="handler"/>.</summary>
        public CatalogSubscription(VisualElement owner, NamedCatalogChangedHandler<TEntry> handler)
        {
            this.owner = owner;
            this.handler = handler;
            owner.RegisterCallback<DetachFromPanelEvent>(_ => Track(null));
        }

        /// <summary>Moves the subscription to <paramref name="catalog"/>; null releases it.</summary>
        public void Track(INamedCatalog<TEntry> catalog)
        {
            var next = owner.panel == null ? null : catalog;
            if (ReferenceEquals(subscribed, next)) return;
            if (subscribed != null) subscribed.Changed -= handler;
            subscribed = next;
            if (subscribed != null) subscribed.Changed += handler;
        }
    }
}
