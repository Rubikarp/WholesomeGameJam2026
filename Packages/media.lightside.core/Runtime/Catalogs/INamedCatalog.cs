using System.Collections.Generic;

namespace LightSide
{
    /// <summary>Describes one committed catalog structure, resolution, or payload change.</summary>
    public readonly struct NamedCatalogChange<TEntry>
    {
        /// <summary>Creates one catalog structure, resolution, or nested-payload transition.</summary>
        public NamedCatalogChange(StateChangeKind kind, TEntry entry = default,
            TEntry previousEntry = default, IStateChangeSource source = null,
            StateChange sourceChange = default, bool affectsResolution = false,
            string name = null, string previousName = null,
            int index = -1, int destinationIndex = -1)
        {
            Kind = kind;
            Entry = entry;
            PreviousEntry = previousEntry;
            Source = source;
            SourceChange = sourceChange;
            AffectsResolution = affectsResolution;
            Name = name;
            PreviousName = previousName;
            Index = index;
            DestinationIndex = destinationIndex;
        }

        /// <summary>Gets the scalar or structural operation that committed.</summary>
        public StateChangeKind Kind { get; }
        /// <summary>Gets the current affected entry, when one is identifiable.</summary>
        public TEntry Entry { get; }
        /// <summary>Gets the replaced or removed entry, when one existed.</summary>
        public TEntry PreviousEntry { get; }
        /// <summary>Gets the nested source for an entry payload change.</summary>
        public IStateChangeSource Source { get; }
        /// <summary>Gets the nested semantic change for an entry payload change.</summary>
        public StateChange SourceChange { get; }
        /// <summary>Whether name resolution may now produce a different result.</summary>
        public bool AffectsResolution { get; }
        /// <summary>Gets the entry's current lookup name.</summary>
        public string Name { get; }
        /// <summary>Gets the entry's previous lookup name.</summary>
        public string PreviousName { get; }
        /// <summary>Gets the affected source index, or -1 when no index applies.</summary>
        public int Index { get; }
        /// <summary>Gets the destination index of a move, or -1 for other operations.</summary>
        public int DestinationIndex { get; }
        /// <summary>Whether the ordered entry collection changed.</summary>
        public bool IsStructural => Kind != StateChangeKind.Value;
    }

    /// <summary>Receives one committed catalog transition with affected-entry context.</summary>
    public delegate void NamedCatalogChangedHandler<TEntry>(
        INamedCatalog<TEntry> catalog, in NamedCatalogChange<TEntry> change);

    /// <summary>
    /// Resolves named entries by case-insensitive name. The shared contract behind every
    /// "named catalog" feature, so the same set of providers — inline list, shared asset,
    /// custom code — composes across all of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Concrete implementations decide where the catalog lives — an explicit asset reference,
    /// an inline list edited on the owner, or a custom runtime resolver — and raise
    /// <see cref="Changed"/> whenever their resolution result changes. The consumer that
    /// consults the provider subscribes during its active lifecycle and decides from the
    /// exact transition whether any selected resolution or payload is affected.
    /// </para>
    /// <para>
    /// Per-domain marker interfaces extend this with no additional members — they exist purely
    /// to scope <c>[SerializeReference, TypeSelector]</c> dropdowns and to give domain-specific
    /// reading.
    /// </para>
    /// </remarks>
    /// <typeparam name="TEntry">The entry type stored in the catalog.</typeparam>
    public interface INamedCatalog<TEntry>
    {
        /// <summary>
        /// Tries to resolve a name to a catalog entry. Name matching is case-insensitive.
        /// </summary>
        /// <param name="name">The lookup name.</param>
        /// <param name="entry">The resolved entry when this call returns <see langword="true"/>; otherwise <see langword="default"/>.</param>
        /// <returns><see langword="true"/> when an entry with the given name was found.</returns>
        bool TryGet(string name, out TEntry entry);

        /// <summary>
        /// Enumerates every entry exposed by this provider in catalog order. Used by editor
        /// tooling to populate entry-name dropdowns. Lazy or runtime-resolved providers
        /// may legitimately return an empty sequence.
        /// </summary>
        IEnumerable<TEntry> Enumerate();

        /// <summary>Raised after one exact catalog transition commits.</summary>
        event NamedCatalogChangedHandler<TEntry> Changed;
    }
}
