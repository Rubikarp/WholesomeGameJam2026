using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Serializable <see cref="BoundNamedCatalog{TEntry,TAsset}"/> that resolves through an explicit
    /// ScriptableObject asset reference — useful when multiple components should share the same
    /// catalog managed as a project asset.
    /// </summary>
    /// <typeparam name="TEntry">The entry type stored in the catalog.</typeparam>
    /// <typeparam name="TAsset">The asset type that implements <see cref="INamedCatalog{TEntry}"/> for this domain.</typeparam>
    [Serializable]
    [StateSource]
    public abstract partial class AssetNamedCatalog<TEntry, TAsset> :
        BoundNamedCatalog<TEntry, TAsset>
        where TAsset : ScriptableObject, INamedCatalog<TEntry>
    {
        /// <summary>The asset used for name resolution; null makes every lookup fail.</summary>
        [SerializeField, StateProperty(nameof(ApplyAssetChange))]
        [Tooltip("Asset to resolve entry names from.")]
        protected TAsset asset;

        protected sealed override TAsset ResolveAsset() => asset;

        private void ApplyAssetChange(StateMember member)
        {
            RebindResolvedAsset();
            PublishStateChange(member);
        }
    }
}
