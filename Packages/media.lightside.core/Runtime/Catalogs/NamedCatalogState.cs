using System;
using System.Collections.Generic;
using System.Threading;

namespace LightSide
{
    internal sealed class NamedCatalogState<TEntry>
    {
        private readonly Func<TEntry, string> nameOf;
        private readonly Action<NamedCatalogChange<TEntry>> publish;
        private readonly Action<TEntry> connectObservedEntry;
        private readonly Action<TEntry> disconnectObservedEntry;
        private Dictionary<string, TEntry> lookup;
        private ReferenceBinding<IStateChangeSource> boundEntries;
        private Dictionary<IStateChangeSource, string> boundNames;
        private Dictionary<IStateChangeSource, StateChangedHandler> boundHandlers;
        private IReadOnlyList<TEntry> requestedEntries;
        private bool binding;
        private bool bindRequested;

        public NamedCatalogState(Func<TEntry, string> nameOf,
            Action<NamedCatalogChange<TEntry>> publish,
            Action<TEntry> connectObservedEntry = null,
            Action<TEntry> disconnectObservedEntry = null)
        {
            this.nameOf = nameOf ?? throw new ArgumentNullException(nameof(nameOf));
            this.publish = publish ?? throw new ArgumentNullException(nameof(publish));
            this.connectObservedEntry = connectObservedEntry;
            this.disconnectObservedEntry = disconnectObservedEntry;
        }

        public IEnumerable<string> Names(IReadOnlyList<TEntry> entries) =>
            GetOrBuildLookup(entries).Keys;

        public bool TryGet(IReadOnlyList<TEntry> entries, string name, out TEntry entry)
            => GetOrBuildLookup(entries).TryGetValue(name ?? string.Empty, out entry);

        public string EntryName(TEntry entry) => entry is null ? null : nameOf(entry);

        public void Invalidate() => lookup = null;

        public void Bind(IReadOnlyList<TEntry> entries)
        {
            requestedEntries = entries;
            if (binding)
            {
                bindRequested = true;
                return;
            }

            boundEntries ??= new ReferenceBinding<IStateChangeSource>(Connect, Disconnect);
            binding = true;
            try
            {
                do
                {
                    bindRequested = false;
                    boundEntries.Reconcile(requestedEntries);
                } while (bindRequested);
            }
            finally
            {
                binding = false;
                requestedEntries = null;
            }
        }

        public void Unbind()
        {
            boundEntries?.Clear();
            boundNames?.Clear();
            boundHandlers?.Clear();
        }

        public NamedCatalogChange<TEntry> StructuralChange(
            in StateListMutation<TEntry> mutation)
        {
            mutation.TryGetCurrentItem(out var entry);
            return new NamedCatalogChange<TEntry>(mutation.ChangeKind, entry,
                mutation.PreviousItem, affectsResolution: true,
                name: EntryName(entry), previousName: EntryName(mutation.PreviousItem),
                index: mutation.AffectedIndex,
                destinationIndex: mutation.DestinationIndex);
        }

        public static void FillLookup(Dictionary<string, TEntry> target,
            IEnumerable<TEntry> entries, Func<TEntry, string> nameOf)
        {
            target.Clear();
            if (entries == null) return;
            foreach (var entry in entries)
            {
                if (entry is null) continue;
                target[nameOf(entry) ?? string.Empty] = entry;
            }
        }

        private Dictionary<string, TEntry> GetOrBuildLookup(IReadOnlyList<TEntry> entries)
        {
            var current = lookup;
            if (current != null) return current;

            var built = new Dictionary<string, TEntry>(StringComparer.OrdinalIgnoreCase);
            FillLookup(built, entries, nameOf);
            return Interlocked.CompareExchange(ref lookup, built, null) ?? built;
        }

        private void Connect(IStateChangeSource source)
        {
            boundNames ??= new Dictionary<IStateChangeSource, string>(
                ReferenceIdentityComparer<IStateChangeSource>.Instance);
            boundHandlers ??= new Dictionary<IStateChangeSource, StateChangedHandler>(
                ReferenceIdentityComparer<IStateChangeSource>.Instance);
            var entry = (TEntry)(object)source;
            StateChangedHandler handler = (IStateChangeSource nestedSource,
                in StateChange stateChange) =>
                OnEntryChanged(entry, source, nestedSource, in stateChange);
            boundNames[source] = nameOf(entry);
            boundHandlers[source] = handler;
            source.Changed += handler;
            try
            {
                connectObservedEntry?.Invoke(entry);
            }
            catch
            {
                source.Changed -= handler;
                boundHandlers.Remove(source);
                boundNames.Remove(source);
                throw;
            }
        }

        private void Disconnect(IStateChangeSource source)
        {
            if (boundHandlers != null && boundHandlers.TryGetValue(source, out var handler))
            {
                source.Changed -= handler;
                boundHandlers.Remove(source);
            }
            boundNames?.Remove(source);
            disconnectObservedEntry?.Invoke((TEntry)(object)source);
        }

        private void OnEntryChanged(TEntry entry, IStateChangeSource entrySource,
            IStateChangeSource source, in StateChange stateChange)
        {
            var name = nameOf(entry);
            var affectsResolution = !boundNames.TryGetValue(entrySource, out var previousName) ||
                                    !string.Equals(previousName, name, StringComparison.Ordinal);
            boundNames[entrySource] = name;
            if (affectsResolution) Invalidate();
            publish(new NamedCatalogChange<TEntry>(StateChangeKind.Value, entry,
                source: source, sourceChange: stateChange,
                affectsResolution: affectsResolution, name: name,
                previousName: previousName));
        }
    }
}
