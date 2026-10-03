using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;

namespace LightSide
{
    /// <summary>Receives one committed structural list mutation.</summary>
    public delegate void StateListChangedHandler<T>(in StateListMutation<T> mutation);

    /// <summary>Publishes exact structural mutations made through a serializable collection wrapper.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IStateCollectionMutationSource<T>
    {
        /// <summary>Occurs after one exact structural mutation commits.</summary>
        event StateListChangedHandler<T> Changed;
    }

    /// <summary>Receives generated snapshot restoration without publishing intermediate mutations.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IStateCollectionMutationSink<T>
    {
        /// <summary>Replaces all contents without raising the structural mutation receptor.</summary>
        void ReplaceStateContents(T[] source, int count);
    }

    internal static class SerializableListMutation
    {
        public static void Set<T>(List<T> items, int index, T value,
            StateListChangedHandler<T> changed)
        {
            if (StateArray.Same(items[index], value)) return;
            var previous = items[index];
            var previousCount = items.Count;
            items[index] = value;
            Publish(items, changed, StateListMutationKind.Replace, previousCount, index,
                item: value, previousItem: previous);
        }

        public static void Add<T>(List<T> items, T item, StateListChangedHandler<T> changed)
        {
            var previousCount = items.Count;
            items.Add(item);
            Publish(items, changed, StateListMutationKind.Add, previousCount, item: item);
        }

        public static bool Remove<T>(List<T> items, T item, StateListChangedHandler<T> changed)
        {
            var index = items.IndexOf(item);
            if (index < 0) return false;
            RemoveAt(items, index, changed);
            return true;
        }

        public static void RemoveAt<T>(List<T> items, int index,
            StateListChangedHandler<T> changed)
        {
            var previousCount = items.Count;
            var previous = items[index];
            items.RemoveAt(index);
            Publish(items, changed, StateListMutationKind.Remove, previousCount, index,
                previousItem: previous);
        }

        public static void Clear<T>(List<T> items, StateListChangedHandler<T> changed)
        {
            if (items.Count == 0) return;
            var previousCount = items.Count;
            items.Clear();
            Publish(items, changed, StateListMutationKind.Clear, previousCount);
        }

        public static void Insert<T>(List<T> items, int index, T item,
            StateListChangedHandler<T> changed)
        {
            var previousCount = items.Count;
            items.Insert(index, item);
            Publish(items, changed, StateListMutationKind.Insert, previousCount, index,
                item: item);
        }

        public static void Move<T>(List<T> items, int fromIndex, int toIndex,
            StateListChangedHandler<T> changed)
        {
            if ((uint)fromIndex >= (uint)items.Count)
                throw new ArgumentOutOfRangeException(nameof(fromIndex));
            if ((uint)toIndex >= (uint)items.Count)
                throw new ArgumentOutOfRangeException(nameof(toIndex));
            if (fromIndex == toIndex) return;
            var previousCount = items.Count;
            var item = items[fromIndex];
            items.RemoveAt(fromIndex);
            items.Insert(toIndex, item);
            Publish(items, changed, StateListMutationKind.Move, previousCount,
                fromIndex, toIndex);
        }

        public static void ReplaceAll<T>(List<T> items, IEnumerable<T> source,
            StateListChangedHandler<T> changed)
        {
            var replacement = source == null ? null : new List<T>(source);
            if (replacement == null && items.Count == 0 ||
                replacement != null && Same(items, replacement))
                return;
            var previousCount = items.Count;
            items.Clear();
            if (replacement != null) items.AddRange(replacement);
            Publish(items, changed, StateListMutationKind.ReplaceAll, previousCount);
        }

        public static void Apply<T>(ref List<T> items, in StateListMutation<T> mutation,
            StateListChangedHandler<T> changed)
        {
            var committed = mutation.CaptureCommitted();
            switch (mutation.Kind)
            {
                case StateListMutationKind.Add:
                    items.Add(mutation.Value);
                    break;
                case StateListMutationKind.Insert:
                    items.Insert(mutation.Index, mutation.Value);
                    break;
                case StateListMutationKind.Replace:
                    items[mutation.Index] = mutation.Value;
                    break;
                case StateListMutationKind.Remove:
                    items.RemoveAt(mutation.Index);
                    break;
                case StateListMutationKind.Move:
                    var moved = items[mutation.Index];
                    items.RemoveAt(mutation.Index);
                    items.Insert(mutation.DestinationIndex, moved);
                    break;
                case StateListMutationKind.Clear:
                    items.Clear();
                    break;
                case StateListMutationKind.ReplaceAll:
                    var replacement = new List<T>(mutation.Count);
                    for (var i = 0; i < mutation.Count; i++) replacement.Add(mutation[i]);
                    items = replacement;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            changed?.Invoke(in committed);
        }

        public static void Restore<T>(List<T> items, T[] source, int count)
        {
            items.Clear();
            if (source == null || count <= 0) return;
            if (items.Capacity < count) items.Capacity = count;
            for (var i = 0; i < count; i++) items.Add(source[i]);
        }

        private static void Publish<T>(IReadOnlyList<T> items,
            StateListChangedHandler<T> changed, StateListMutationKind kind,
            int previousCount, int index = -1, int destinationIndex = -1,
            T item = default, T previousItem = default)
        {
            var mutation = StateListMutation<T>.CreateCommitted(items, kind, previousCount,
                index, destinationIndex, item, previousItem);
            changed?.Invoke(in mutation);
        }

        private static bool Same<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
        {
            if (left.Count != right.Count) return false;
            for (var i = 0; i < left.Count; i++)
                if (!StateArray.Same(left[i], right[i])) return false;
            return true;
        }
    }

    /// <summary>
    /// Serializable polymorphic list whose public mutations publish one structural change. Predates the
    /// generated state lists: a new field prefers a plain serialized <c>List&lt;T&gt;</c> under
    /// <c>[StateList]</c> — this wrapper earns its place only for <c>[SerializeReference]</c> element
    /// storage, for code that mutates the collection through its own API and must still publish, or
    /// where its serialized layout already ships.
    /// </summary>
    [Serializable]
    [StateCollectionElementReference]
    public class TypedList<T> : IList<T>, IReadOnlyList<T>, IStateCollectionMutationSource<T>,
        IStateListMutationTarget<T>, IStateCollectionMutationSink<T>
    {
        [SerializeReference, TypeSelector]
        private List<T> items = new();
        [NonSerialized] private IReadOnlyList<T> readOnlyItems;
        /// <inheritdoc/>
        [field: NonSerialized] public event StateListChangedHandler<T> Changed;

        /// <summary>Creates an empty serializable polymorphic list.</summary>
        public TypedList() { }
        /// <summary>Creates a serializable polymorphic list initialized from the supplied items.</summary>
        public TypedList(params T[] initialItems) => items = new List<T>(initialItems);

        /// <summary>Read-only view of the serialized items.</summary>
        public IReadOnlyList<T> Items => readOnlyItems ??= new ReadOnlyItems(this);

        /// <inheritdoc/>
        public int Count => items.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public T this[int index]
        {
            get => items[index];
            set => SerializableListMutation.Set(items, index, value, Changed);
        }

        /// <inheritdoc/>
        public void Add(T item) => SerializableListMutation.Add(items, item, Changed);
        /// <inheritdoc/>
        public bool Remove(T item) => SerializableListMutation.Remove(items, item, Changed);
        /// <inheritdoc/>
        public void RemoveAt(int index) => SerializableListMutation.RemoveAt(items, index, Changed);
        /// <inheritdoc/>
        public void Clear() => SerializableListMutation.Clear(items, Changed);
        /// <inheritdoc/>
        public void Insert(int index, T item) =>
            SerializableListMutation.Insert(items, index, item, Changed);
        /// <inheritdoc/>
        public int IndexOf(T item) => items.IndexOf(item);
        /// <inheritdoc/>
        public bool Contains(T item) => items.Contains(item);
        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);
        /// <summary>Replaces the item at <paramref name="index"/>.</summary>
        public void Replace(int index, T item) => this[index] = item;
        /// <summary>Moves one item while preserving the order of all remaining items.</summary>
        public void Move(int fromIndex, int toIndex) =>
            SerializableListMutation.Move(items, fromIndex, toIndex, Changed);
        /// <summary>Replaces all items and publishes at most one structural transition.</summary>
        public void ReplaceAll(IEnumerable<T> source) =>
            SerializableListMutation.ReplaceAll(items, source, Changed);
        /// <summary>Returns the allocation-free enumerator of the serialized storage.</summary>
        public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

        void IStateListMutationTarget<T>.Apply(in StateListMutation<T> mutation)
            => SerializableListMutation.Apply(ref items, in mutation, Changed);

        void IStateCollectionMutationSink<T>.ReplaceStateContents(T[] source, int count)
            => SerializableListMutation.Restore(items, source, count);

        private sealed class ReadOnlyItems : IReadOnlyList<T>
        {
            private readonly TypedList<T> owner;

            internal ReadOnlyItems(TypedList<T> owner) => this.owner = owner;

            public int Count => owner.items.Count;
            public T this[int index] => owner.items[index];
            public List<T>.Enumerator GetEnumerator() => owner.items.GetEnumerator();
            IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }

    /// <summary>
    /// Serializable value list whose public mutations publish one structural change. Predates the
    /// generated state lists: a new field prefers a plain serialized <c>List&lt;T&gt;</c> under
    /// <c>[StateList]</c> — this wrapper earns its place only for code that mutates the collection
    /// through its own API and must still publish, or where its serialized layout already ships.
    /// </summary>
    [Serializable]
    public class StyledList<T> : IList<T>, IReadOnlyList<T>, IStateCollectionMutationSource<T>,
        IStateListMutationTarget<T>, IStateCollectionMutationSink<T>
    {
        [SerializeField] private List<T> items = new();
        [NonSerialized] private IReadOnlyList<T> readOnlyItems;
        /// <inheritdoc/>
        [field: NonSerialized] public event StateListChangedHandler<T> Changed;

        /// <summary>Creates an empty serializable value list.</summary>
        public StyledList() { }
        /// <summary>Creates a serializable value list initialized from the supplied items.</summary>
        public StyledList(params T[] initialItems) => items = new List<T>(initialItems);

        /// <summary>Read-only view of the serialized items.</summary>
        public IReadOnlyList<T> Items => readOnlyItems ??= new ReadOnlyItems(this);

        /// <inheritdoc/>
        public int Count => items.Count;
        /// <summary>Number of serialized items, provided for array-shaped consumers.</summary>
        public int Length => items.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public T this[int index]
        {
            get => items[index];
            set => SerializableListMutation.Set(items, index, value, Changed);
        }

        /// <inheritdoc/>
        public void Add(T item) => SerializableListMutation.Add(items, item, Changed);
        /// <inheritdoc/>
        public bool Remove(T item) => SerializableListMutation.Remove(items, item, Changed);
        /// <inheritdoc/>
        public void RemoveAt(int index) => SerializableListMutation.RemoveAt(items, index, Changed);
        /// <inheritdoc/>
        public void Clear() => SerializableListMutation.Clear(items, Changed);
        /// <inheritdoc/>
        public void Insert(int index, T item) =>
            SerializableListMutation.Insert(items, index, item, Changed);
        /// <inheritdoc/>
        public int IndexOf(T item) => items.IndexOf(item);
        /// <inheritdoc/>
        public bool Contains(T item) => items.Contains(item);
        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);
        /// <summary>Replaces the item at <paramref name="index"/>.</summary>
        public void Replace(int index, T item) => this[index] = item;
        /// <summary>Moves one item while preserving the order of all remaining items.</summary>
        public void Move(int fromIndex, int toIndex) =>
            SerializableListMutation.Move(items, fromIndex, toIndex, Changed);
        /// <summary>Replaces all items and publishes at most one structural transition.</summary>
        public void ReplaceAll(IEnumerable<T> source) =>
            SerializableListMutation.ReplaceAll(items, source, Changed);
        /// <summary>Returns the allocation-free enumerator of the serialized storage.</summary>
        public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
        /// <summary>Creates a detached array containing the current ordered items.</summary>
        public T[] ToArray() => items.ToArray();

        void IStateListMutationTarget<T>.Apply(in StateListMutation<T> mutation)
            => SerializableListMutation.Apply(ref items, in mutation, Changed);

        void IStateCollectionMutationSink<T>.ReplaceStateContents(T[] source, int count)
            => SerializableListMutation.Restore(items, source, count);

        private sealed class ReadOnlyItems : IReadOnlyList<T>
        {
            private readonly StyledList<T> owner;

            internal ReadOnlyItems(StyledList<T> owner) => this.owner = owner;

            public int Count => owner.items.Count;
            public T this[int index] => owner.items[index];
            public List<T>.Enumerator GetEnumerator() => owner.items.GetEnumerator();
            IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
