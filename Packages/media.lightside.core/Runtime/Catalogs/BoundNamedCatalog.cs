using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Base for <see cref="INamedCatalog{TEntry}"/> implementations that delegate resolution to a
    /// catalog asset resolved through <see cref="ResolveAsset"/> — an explicit serialized reference
    /// (<see cref="AssetNamedCatalog{TEntry,TAsset}"/>) or an external source such as project
    /// settings. Owns the lazy bind/unbind subscription choreography so every asset-backed
    /// provider shares one implementation.
    /// </summary>
    /// <remarks>
    /// Subscription to the resolved asset's <see cref="INamedCatalog{TEntry}.Changed"/> is lazy:
    /// the provider attaches only while it has at least one subscriber and detaches when the last
    /// unsubscribes, so an unused provider doesn't keep itself alive through the asset's delegate
    /// list. Subclasses whose asset source can swap externally hook the source's change event in
    /// <see cref="OnFirstSubscriber"/>/<see cref="OnLastSubscriberRemoved"/> and call
    /// <see cref="RebindResolvedAsset"/> when it fires.
    /// </remarks>
    /// <typeparam name="TEntry">The entry type stored in the catalog.</typeparam>
    /// <typeparam name="TAsset">The asset type that implements <see cref="INamedCatalog{TEntry}"/> for this domain.</typeparam>
    [Serializable]
    public abstract class BoundNamedCatalog<TEntry, TAsset> : INamedCatalog<TEntry>
        where TAsset : ScriptableObject, INamedCatalog<TEntry>
    {
        [NonSerialized] private TAsset boundAsset;
        [NonSerialized] private NamedCatalogChangedHandler<TEntry> changed;
        [NonSerialized] private bool bindQueued;

        /// <summary>The asset names resolve against right now. <see langword="null"/> makes <see cref="TryGet"/> return <see langword="false"/> for every name.</summary>
        protected abstract TAsset ResolveAsset();

        /// <summary>First live subscriber attached — hook external source-change events here.</summary>
        protected virtual void OnFirstSubscriber() { }

        /// <summary>Last subscriber detached — release what <see cref="OnFirstSubscriber"/> hooked.</summary>
        protected virtual void OnLastSubscriberRemoved() { }

        /// <inheritdoc/>
        public event NamedCatalogChangedHandler<TEntry> Changed
        {
            add
            {
                var wasEmpty = changed == null;
                changed += value;
                if (!wasEmpty) return;
                OnFirstSubscriber();
                QueueBind();
            }
            remove
            {
                changed -= value;
                if (changed != null) return;
                UnbindAsset();
                OnLastSubscriberRemoved();
            }
        }

        /// <inheritdoc/>
        public bool TryGet(string name, out TEntry entry)
        {
            var asset = ResolveAsset();
            if (asset == null)
            {
                entry = default;
                return false;
            }
            return asset.TryGet(name, out entry);
        }

        /// <inheritdoc/>
        public IEnumerable<TEntry> Enumerate()
        {
            var asset = ResolveAsset();
            return asset == null ? Enumerable.Empty<TEntry>() : asset.Enumerate();
        }

        /// <summary>
        /// Re-binds to the current <see cref="ResolveAsset"/> result and notifies subscribers —
        /// call when the external source swapped the asset reference (or on a deserialize that
        /// changed a serialized reference).
        /// </summary>
        protected void RebindResolvedAsset()
        {
            UnbindAsset();
            BindAssetIfNeeded();
            var change = new NamedCatalogChange<TEntry>(StateChangeKind.Reset,
                affectsResolution: true);
            changed?.Invoke(this, in change);
        }

        /// <summary>Raises one exact change without rebinding.</summary>
        protected void RaiseChanged(in NamedCatalogChange<TEntry> change)
            => changed?.Invoke(this, in change);

        /// <summary>
        /// Binds immediately on the main thread; defers through <see cref="MainThread.Post"/> when
        /// the first subscriber attaches on a worker thread, where <see cref="ResolveAsset"/> may
        /// reach Unity API (settings load). The main-thread synchronization context delivers the deferred bind;
        /// the pass that subscribed still resolves through its own main-thread capture.
        /// </summary>
        private void QueueBind()
        {
            if (MainThread.IsCurrent)
            {
                BindAssetIfNeeded();
                return;
            }
            if (bindQueued) return;
            bindQueued = true;
            MainThread.Post(FlushQueuedBind);
        }

        private void FlushQueuedBind()
        {
            bindQueued = false;
            BindAssetIfNeeded();
        }

        private void BindAssetIfNeeded()
        {
            if (changed == null) return;
            var asset = ResolveAsset();
            if (asset == null) return;
            boundAsset = asset;
            boundAsset.Changed += OnAssetChanged;
        }

        /// <summary>Runs on worker threads too (owner teardown → last unsubscribe), so the check is a reference-null test — never Unity's <c>==</c>. Detaching from a destroyed asset's managed event is harmless.</summary>
        private void UnbindAsset()
        {
            if (boundAsset is null) return;
            boundAsset.Changed -= OnAssetChanged;
            boundAsset = null;
        }

        /// <summary>Forwards the underlying asset's own verdict verbatim.</summary>
        private void OnAssetChanged(INamedCatalog<TEntry> _,
            in NamedCatalogChange<TEntry> change) => changed?.Invoke(this, in change);
    }
}
