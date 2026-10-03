using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LightSide
{
    /// <summary>
    /// Abstract ScriptableObject base for named-entry catalogs. Subclasses give the catalog a
    /// concrete entry type, a <c>CreateAssetMenu</c> path, and (optionally) a payload validity
    /// filter for editor enumeration.
    /// </summary>
    /// <typeparam name="TEntry">The entry type stored in the catalog.</typeparam>
    public abstract partial class NamedCatalogAsset<TEntry> : ScriptableObject, INamedCatalog<TEntry>
    {
        /// <summary>Entries available for name resolution.</summary>
        [SerializeField, FormerlySerializedAs("gradients"), StateList(nameof(ApplyEntriesChange),
            Validator = nameof(ValidateEntriesMutation))]
        [Tooltip("List of named entries available for name resolution.")]
        protected StyledList<TEntry> entries = new();

        [NonSerialized] private NamedCatalogState<TEntry> state;
        [NonSerialized] private NamedCatalogChangedHandler<TEntry> changed;

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
                    BindEntries();
                }
                changed += value;
            }
            remove => changed -= value;
        }

        /// <summary>Number of entries in the catalog.</summary>
        public int Count => entries.Count;

        /// <summary>All entry names exposed by this catalog.</summary>
        public IEnumerable<string> Names => State.Names(entries);

        /// <inheritdoc/>
        public bool TryGet(string name, out TEntry entry)
        {
            return State.TryGet(entries, name, out entry);
        }

        /// <inheritdoc/>
        public IEnumerable<TEntry> Enumerate()
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e is not null) yield return e;
            }
        }

        /// <summary>Extracts the lookup name from an entry. An empty name marks the catalog's default entry.</summary>
        protected abstract string GetEntryName(TEntry entry);

        /// <summary>Activates dependencies while Core observes a state-bearing entry.</summary>
        protected virtual void OnObservedEntryBound(TEntry entry) { }

        /// <summary>Deactivates dependencies previously activated by <see cref="OnObservedEntryBound"/>.</summary>
        protected virtual void OnObservedEntryUnbound(TEntry entry) { }

        /// <summary>Validates the projected catalog contents before one structural mutation commits.</summary>
        protected virtual void ValidateEntriesMutation(in StateListMutation<TEntry> mutation) { }

        /// <summary>Raises one exact catalog change.</summary>
        protected void RaiseChanged(in NamedCatalogChange<TEntry> change)
            => changed?.Invoke(this, in change);

        private void ApplyEntriesChange(in StateListMutation<TEntry> mutation)
        {
            State.Invalidate();
            BindEntries();
            var change = State.StructuralChange(in mutation);
            RaiseChanged(in change);
            MarkDirty();
        }

        private void BindEntries() => State.Bind(entries);

        private void UnbindEntries() => state?.Unbind();

        private void PublishNestedChange(NamedCatalogChange<TEntry> change) =>
            RaiseChanged(in change);

        private void MarkDirty()
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        private void OnEnable()
        {
            State.Invalidate();
            BindEntries();
        }

        private void OnDisable() => UnbindEntries();
    }
}
