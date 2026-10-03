using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Serializable base for <see cref="INamedCatalog{TEntry}"/> implementations that keep
    /// their entries inline on the owning object — edited in the inspector or mutated through
    /// <see cref="Entries"/>.
    /// </summary>
    /// <remarks>
    /// Concrete subclasses supply <see cref="GetEntryName"/>. The
    /// <see cref="INamedCatalog{TEntry}.Changed"/> event fires automatically on every mutation;
    /// subscribers stay alive only through their own delegate, no static channels.
    /// </remarks>
    /// <typeparam name="TEntry">The entry type stored in the catalog.</typeparam>
    [Serializable]
    public abstract partial class InlineNamedCatalog<TEntry> :
        INamedCatalog<TEntry>, IStateChangeSource
    {
        /// <summary>Entries available for name resolution.</summary>
        [SerializeField, StateList(nameof(ApplyEntriesChange))]
        [Tooltip("Inline named entries available to the owner. Names are case-insensitive.")]
        protected StyledList<TEntry> entries = new();

        [NonSerialized] private NamedCatalogState<TEntry> state;
        [NonSerialized] private bool entriesBound;
        [NonSerialized] private bool bindQueued;
        [NonSerialized] private NamedCatalogChangedHandler<TEntry> changed;
        [NonSerialized] private StateChangedHandler stateChanged;

        private NamedCatalogState<TEntry> State =>
            state ??= new NamedCatalogState<TEntry>(GetEntryName, PublishNestedChange,
                OnObservedEntryBound, OnObservedEntryUnbound);

        /// <inheritdoc/>
        public event NamedCatalogChangedHandler<TEntry> Changed
        {
            add
            {
                if (changed == null)
                {
                    EnsureBound();
                }
                changed += value;
            }
            remove => changed -= value;
        }

        /// <inheritdoc/>
        event StateChangedHandler IStateChangeSource.Changed
        {
            add
            {
                EnsureBound();
                stateChanged += value;
            }
            remove => stateChanged -= value;
        }

        /// <inheritdoc/>
        public bool TryGet(string name, out TEntry entry)
        {
            EnsureBound();
            return State.TryGet(entries, name, out entry);
        }

        /// <inheritdoc/>
        public IEnumerable<TEntry> Enumerate()
        {
            EnsureBound();
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e is not null) yield return e;
            }
        }

        /// <summary>Removes the first entry with the given name. Returns <see langword="true"/> on success.</summary>
        public bool Remove(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var mutableEntries = Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                if (string.Equals(State.EntryName(entries[i]), name, StringComparison.OrdinalIgnoreCase))
                {
                    mutableEntries.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Extracts the lookup name from an entry. An empty name marks the catalog's default entry.</summary>
        protected abstract string GetEntryName(TEntry entry);

        /// <summary>Activates dependencies of an entry that entered observation.</summary>
        protected virtual void OnObservedEntryBound(TEntry entry) { }

        /// <summary>Deactivates dependencies previously activated by <see cref="OnObservedEntryBound"/>.</summary>
        protected virtual void OnObservedEntryUnbound(TEntry entry) { }

        /// <summary>Raises one exact catalog change.</summary>
        protected void RaiseChanged(in NamedCatalogChange<TEntry> change)
            => changed?.Invoke(this, in change);

        /// <summary>Raises an exact transition for serialized state owned by this provider.</summary>
        protected void RaiseStateChanged(IStateChangeSource source,
            in StateChange change)
            => stateChanged?.Invoke(source, in change);

        private void ApplyEntriesChange(in StateListMutation<TEntry> mutation)
        {
            State.Invalidate();
            BindEntries();
            var change = State.StructuralChange(in mutation);
            RaiseChanged(in change);
            var stateChange = new StateChange(Members.Entries, change.Kind,
                change.Index, change.DestinationIndex);
            RaiseStateChanged(this, in stateChange);
        }

        private void BindEntries()
        {
            State.Bind(entries);
            entriesBound = true;
        }

        private void UnbindEntries()
        {
            state?.Unbind();
            entriesBound = false;
        }

        private void EnsureBound()
        {
            if (entriesBound) return;
            if (MainThread.IsCurrent)
            {
                BindEntries();
                return;
            }
            if (bindQueued) return;
            bindQueued = true;
            MainThread.Post(CompleteQueuedBind);
        }

        private void CompleteQueuedBind()
        {
            bindQueued = false;
            EnsureBound();
        }

        private void PublishNestedChange(NamedCatalogChange<TEntry> change)
        {
            RaiseChanged(in change);
            if (change.Source is not null)
                RaiseStateChanged(change.Source, change.SourceChange);
        }
    }
}
