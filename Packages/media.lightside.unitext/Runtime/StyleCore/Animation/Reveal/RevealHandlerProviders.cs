using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Named-catalog source of <see cref="RevealHandlerEntry"/> appearance effects.</summary>
    [StateHierarchy]
    [TypeMenuSuffix("RevealHandlerProvider", "Provider")]
    public interface IRevealHandlerProvider : INamedCatalog<RevealHandlerEntry>
    {
    }

    /// <summary>Inline list of named reveal handlers edited directly on the modifier.</summary>
    [Serializable]
    [TypeDescription("Inline list of named reveal handlers edited directly on the modifier.")]
    public sealed class InlineRevealHandlerProvider : InlineNamedCatalog<RevealHandlerEntry>,
        IRevealHandlerProvider
    {
        /// <inheritdoc/>
        protected override string GetEntryName(RevealHandlerEntry entry) => entry?.Name;

        /// <inheritdoc/>
        protected override void OnObservedEntryBound(RevealHandlerEntry entry)
            => entry?.OnObserved();

        /// <inheritdoc/>
        protected override void OnObservedEntryUnbound(RevealHandlerEntry entry)
            => entry?.OnUnobserved();
    }

    /// <summary>Resolves names through an explicit <see cref="UniTextRevealHandlers"/> asset reference.</summary>
    [Serializable]
    [TypeDescription("Resolves names through an explicit UniTextRevealHandlers asset reference.")]
    public sealed class AssetRevealHandlerProvider :
        AssetNamedCatalog<RevealHandlerEntry, UniTextRevealHandlers>, IRevealHandlerProvider
    {
    }

    /// <summary>Project asset carrying a shared set of named reveal handlers.</summary>
    [CreateAssetMenu(fileName = "UniTextRevealHandlers",
        menuName = UniTextMenu.CreateAsset.RevealHandlers)]
    public sealed class UniTextRevealHandlers : NamedCatalogAsset<RevealHandlerEntry>
    {
        /// <inheritdoc/>
        protected override string GetEntryName(RevealHandlerEntry entry) => entry?.Name;

        /// <inheritdoc/>
        protected override void OnObservedEntryBound(RevealHandlerEntry entry)
            => entry?.OnObserved();

        /// <inheritdoc/>
        protected override void OnObservedEntryUnbound(RevealHandlerEntry entry)
            => entry?.OnUnobserved();
    }
}
