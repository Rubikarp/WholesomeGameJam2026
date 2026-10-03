using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace LightSide
{
    /// <summary>Defines compile-time capture and comparison semantics for collection elements.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IStateCollectionElementSnapshot<T>
    {
        /// <summary>Returns the value stored in the detached collection snapshot.</summary>
        T Capture(T value);

        /// <summary>Compares a current value with its detached snapshot.</summary>
        bool StateEquals(T value, in T snapshot);

        /// <summary>Compares two live values without creating a detached snapshot.</summary>
        bool ValuesEqual(T previous, T current);
    }

    /// <summary>Separates a live collection element from its reusable detached snapshot storage.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IStateCollectionElementSnapshot<T, TSnapshot>
    {
        /// <summary>Captures a live value into reusable detached snapshot storage.</summary>
        void Capture(T value, ref TSnapshot snapshot);

        /// <summary>Copies detached state without sharing mutable snapshot storage.</summary>
        void CopySnapshot(in TSnapshot source, ref TSnapshot destination);

        /// <summary>Compares a current value with its detached snapshot.</summary>
        bool StateEquals(T value, in TSnapshot snapshot);

        /// <summary>Compares two detached snapshots.</summary>
        bool SnapshotEquals(in TSnapshot previous, in TSnapshot current);

        /// <summary>Restores a live element from its detached snapshot.</summary>
        T Restore(in TSnapshot snapshot);

        /// <summary>Compares two live values without creating a detached snapshot.</summary>
        bool ValuesEqual(T previous, T current);
    }

    /// <summary>Exposes live item identity/value separately from detached nested state.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IStateCollectionValueSnapshot<T, TSnapshot>
    {
        /// <summary>Compares the live value identity or value represented by two detached snapshots.</summary>
        bool SnapshotValuesEqual(in TSnapshot previous, in TSnapshot current);

        /// <summary>Returns the live value represented by a detached snapshot without restoring nested state.</summary>
        T SnapshotValue(in TSnapshot snapshot);
    }

    /// <summary>Preserves value equality for value types and identity equality for reference types.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly struct IdentityStateCollectionElement<T> : IStateCollectionElementSnapshot<T>
    {
        private static readonly bool compareByValue = typeof(T).IsValueType || typeof(T) == typeof(string);

        /// <inheritdoc/>
        public T Capture(T value) => value;

        /// <inheritdoc/>
        public bool StateEquals(T value, in T snapshot) => compareByValue
            ? ValueEquality<T>.Same(value, snapshot)
            : ReferenceEquals(value, snapshot);

        /// <inheritdoc/>
        public bool ValuesEqual(T previous, T current) => compareByValue
            ? ValueEquality<T>.Same(previous, current)
            : ReferenceEquals(previous, current);
    }

    /// <summary>Uses an element's detached state contract without boxing value types.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly struct DetachedStateCollectionElement<T> : IStateCollectionElementSnapshot<T>
        where T : struct, IStateSnapshot<T>
    {
        /// <inheritdoc/>
        public T Capture(T value) => value.CaptureStateSnapshot();

        /// <inheritdoc/>
        public bool StateEquals(T value, in T snapshot) => value.StateEquals(in snapshot);

        /// <inheritdoc/>
        public bool ValuesEqual(T previous, T current) => current.StateEquals(in previous);
    }

    /// <summary>
    /// Resolves detached capture at runtime when a generic collection's element type is unknown to
    /// source generation. Closed value types implementing <see cref="IStateSnapshot{T}"/> use that
    /// contract; reference types retain identity semantics. Concrete element types use generated
    /// constrained strategies instead, avoiding value-type boxing.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly struct AdaptiveStateCollectionElement<T> : IStateCollectionElementSnapshot<T>
    {
        private static readonly bool compareByValue = typeof(T).IsValueType || typeof(T) == typeof(string);

        /// <inheritdoc/>
        public T Capture(T value)
            => compareByValue && value is IStateSnapshot<T> snapshot
                ? snapshot.CaptureStateSnapshot()
                : value;

        /// <inheritdoc/>
        public bool StateEquals(T value, in T snapshot)
        {
            if (compareByValue && value is IStateSnapshot<T> state) return state.StateEquals(in snapshot);
            return compareByValue
                ? ValueEquality<T>.Same(value, snapshot)
                : ReferenceEquals(value, snapshot);
        }

        /// <inheritdoc/>
        public bool ValuesEqual(T previous, T current)
        {
            if (compareByValue && current is IStateSnapshot<T> state) return state.StateEquals(in previous);
            return compareByValue
                ? ValueEquality<T>.Same(previous, current)
                : ReferenceEquals(previous, current);
        }
    }

    /// <summary>Generated ordered collection comparison primitive.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class StateCollection
    {
        /// <summary>Compares ordered collection contents using the generated element strategy.</summary>
        public static bool ContentEquals<T, TElementSnapshot>(IReadOnlyList<T> previous, IReadOnlyList<T> current)
            where TElementSnapshot : struct, IStateCollectionElementSnapshot<T>
        {
            if (ReferenceEquals(previous, current)) return true;
            if (previous == null || current == null || previous.Count != current.Count) return false;
            var elementSnapshot = default(TElementSnapshot);
            for (var i = 0; i < previous.Count; i++)
                if (!elementSnapshot.ValuesEqual(previous[i], current[i])) return false;
            return true;
        }

        /// <summary>Compares ordered collection contents using a detached element representation.</summary>
        public static bool ContentEquals<T, TSnapshot, TElementSnapshot>(
            IReadOnlyList<T> previous, IReadOnlyList<T> current)
            where TElementSnapshot : struct, IStateCollectionElementSnapshot<T, TSnapshot>
        {
            if (ReferenceEquals(previous, current)) return true;
            if (previous == null || current == null || previous.Count != current.Count) return false;
            var elementSnapshot = default(TElementSnapshot);
            for (var i = 0; i < previous.Count; i++)
                if (!elementSnapshot.ValuesEqual(previous[i], current[i])) return false;
            return true;
        }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal readonly struct SameTypeStateCollectionElement<T, TElementSnapshot> :
        IStateCollectionElementSnapshot<T, T>, IStateCollectionValueSnapshot<T, T>
        where TElementSnapshot : struct, IStateCollectionElementSnapshot<T>
    {
        private static readonly TElementSnapshot elementSnapshot = default;

        public void Capture(T value, ref T snapshot)
            => snapshot = elementSnapshot.Capture(value);

        public void CopySnapshot(in T source, ref T destination) => destination = source;

        public bool StateEquals(T value, in T snapshot)
            => elementSnapshot.StateEquals(value, in snapshot);

        public bool SnapshotEquals(in T previous, in T current)
            => elementSnapshot.StateEquals(current, in previous);

        public T Restore(in T snapshot) => elementSnapshot.Capture(snapshot);

        public bool ValuesEqual(T previous, T current)
            => elementSnapshot.ValuesEqual(previous, current);

        public bool SnapshotValuesEqual(in T previous, in T current)
            => elementSnapshot.ValuesEqual(previous, current);

        public T SnapshotValue(in T snapshot) => snapshot;
    }

    /// <summary>Detached ordered snapshot with compile-time element capture semantics.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public struct StateCollectionSnapshot<T, TElementSnapshot>
        where TElementSnapshot : struct, IStateCollectionElementSnapshot<T>
    {
        private StateCollectionSnapshot<T, T,
            SameTypeStateCollectionElement<T, TElementSnapshot>> snapshot;

        /// <summary>Whether the captured collection reference was null.</summary>
        public bool IsNull => snapshot.IsNull;
        /// <summary>Gets the captured item count, or zero for a null collection.</summary>
        public int Count => snapshot.Count;

        /// <summary>Copies an ordered collection without retaining its mutable storage.</summary>
        public void Capture(IReadOnlyList<T> source) => snapshot.Capture(source);

        /// <summary>Copies another detached snapshot into independently owned storage.</summary>
        public void CopyFrom(in StateCollectionSnapshot<T, TElementSnapshot> source)
            => snapshot.CopyFrom(in source.snapshot);

        /// <summary>Compares element order and identity/value semantics without allocating.</summary>
        public bool ContentEquals(in StateCollectionSnapshot<T, TElementSnapshot> other)
            => snapshot.ContentEquals(in other.snapshot);

        /// <summary>Compares only ordered item identity/value, excluding nested detached state.</summary>
        public bool ValueContentEquals(in StateCollectionSnapshot<T, TElementSnapshot> other)
            => snapshot.ValueContentEquals(in other.snapshot);

        /// <summary>Compares one captured item with an item in another snapshot.</summary>
        public bool ItemEquals(int index,
            in StateCollectionSnapshot<T, TElementSnapshot> other, int otherIndex)
            => snapshot.ItemEquals(index, in other.snapshot, otherIndex);

        /// <summary>Compares one captured item's identity/value without nested detached state.</summary>
        public bool ItemValueEquals(int index,
            in StateCollectionSnapshot<T, TElementSnapshot> other, int otherIndex)
            => snapshot.ItemValueEquals(index, in other.snapshot, otherIndex);

        /// <summary>Restores one captured item.</summary>
        public T Get(int index) => snapshot.Get(index);

        /// <summary>Returns one captured item without applying detached nested state.</summary>
        public T GetValue(int index) => snapshot.GetValue(index);

        /// <summary>Compares live ordered contents with this detached snapshot.</summary>
        public bool ContentEquals(IReadOnlyList<T> current)
            => snapshot.ContentEquals(current);

        /// <summary>Replaces a mutable collection with the captured ordered elements.</summary>
        public void CopyTo(IList<T> destination) => snapshot.CopyTo(destination);

        /// <summary>Restores only ordered item values, excluding nested detached state.</summary>
        public void CopyValuesTo(IList<T> destination)
            => snapshot.CopyValuesTo(destination);

        /// <summary>Creates an array containing the captured ordered elements.</summary>
        public T[] ToArray() => snapshot.ToArray();

        /// <summary>Creates an array of captured item values without applying nested detached state.</summary>
        public T[] ToValueArray() => snapshot.ToValueArray();
    }

    /// <summary>Detached ordered snapshot whose storage type is independent of the live element type.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public struct StateCollectionSnapshot<T, TSnapshot, TElementSnapshot>
        where TElementSnapshot : struct, IStateCollectionElementSnapshot<T, TSnapshot>
    {
        private static readonly TElementSnapshot elementSnapshot = default;
        private static readonly IStateCollectionValueSnapshot<T, TSnapshot> valueSnapshot =
            elementSnapshot is IStateCollectionValueSnapshot<T, TSnapshot> values ? values : null;
        private TSnapshot[] items;
        private int count;

        /// <summary>Whether the captured collection reference was null.</summary>
        public bool IsNull => count < 0;
        /// <summary>Gets the captured item count, or zero for a null collection.</summary>
        public int Count => Math.Max(0, count);

        /// <summary>Copies an ordered collection without retaining its mutable storage.</summary>
        public void Capture(IReadOnlyList<T> source)
        {
            if (source == null)
            {
                ClearItems();
                count = -1;
                return;
            }
            EnsureCapacity(source.Count);
            count = source.Count;
            for (var i = 0; i < count; i++) elementSnapshot.Capture(source[i], ref items[i]);
            if (items != null && count < items.Length) Array.Clear(items, count, items.Length - count);
        }

        /// <summary>Copies another detached snapshot into independently owned storage.</summary>
        public void CopyFrom(in StateCollectionSnapshot<T, TSnapshot, TElementSnapshot> source)
        {
            if (source.count < 0)
            {
                ClearItems();
                count = -1;
                return;
            }
            EnsureCapacity(source.count);
            count = source.count;
            for (var i = 0; i < count; i++)
                elementSnapshot.CopySnapshot(in source.items[i], ref items[i]);
            if (items != null && count < items.Length) Array.Clear(items, count, items.Length - count);
        }

        /// <summary>Compares element order and identity/value semantics without allocating.</summary>
        public bool ContentEquals(in StateCollectionSnapshot<T, TSnapshot, TElementSnapshot> other)
        {
            if (count != other.count) return false;
            for (var i = 0; i < count; i++)
            {
                if (!elementSnapshot.SnapshotEquals(in items[i], in other.items[i])) return false;
            }
            return true;
        }

        /// <summary>Compares only ordered item identity/value, excluding nested detached state.</summary>
        public bool ValueContentEquals(in StateCollectionSnapshot<T, TSnapshot, TElementSnapshot> other)
        {
            if (count != other.count) return false;
            for (var i = 0; i < count; i++)
                if (!SnapshotValuesEqual(in items[i], in other.items[i])) return false;
            return true;
        }

        /// <summary>Compares one captured item with an item in another snapshot.</summary>
        public bool ItemEquals(int index,
            in StateCollectionSnapshot<T, TSnapshot, TElementSnapshot> other,
            int otherIndex)
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            if ((uint)otherIndex >= (uint)other.Count)
                throw new ArgumentOutOfRangeException(nameof(otherIndex));
            return elementSnapshot.SnapshotEquals(in items[index],
                in other.items[otherIndex]);
        }

        /// <summary>Compares one captured item's identity/value without nested detached state.</summary>
        public bool ItemValueEquals(int index,
            in StateCollectionSnapshot<T, TSnapshot, TElementSnapshot> other,
            int otherIndex)
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            if ((uint)otherIndex >= (uint)other.Count)
                throw new ArgumentOutOfRangeException(nameof(otherIndex));
            return SnapshotValuesEqual(in items[index], in other.items[otherIndex]);
        }

        /// <summary>Restores one captured item.</summary>
        public T Get(int index)
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return elementSnapshot.Restore(in items[index]);
        }

        /// <summary>Returns one captured item without applying detached nested state.</summary>
        public T GetValue(int index)
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return SnapshotValue(in items[index]);
        }

        /// <summary>Compares live ordered contents with this detached snapshot.</summary>
        public bool ContentEquals(IReadOnlyList<T> current)
        {
            if (current == null) return IsNull;
            if (count < 0 || count != current.Count) return false;
            for (var i = 0; i < count; i++)
                if (!elementSnapshot.StateEquals(current[i], in items[i])) return false;
            return true;
        }

        /// <summary>Replaces a mutable collection with the captured ordered elements.</summary>
        public void CopyTo(IList<T> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (destination is IReadOnlyList<T> current && ContentEquals(current)) return;
            if (destination is IStateCollectionMutationSink<T> sink)
            {
                if (count <= 0)
                {
                    sink.ReplaceStateContents(null, count);
                    return;
                }
                var restored = ArrayPool<T>.Rent(count);
                try
                {
                    for (var i = 0; i < count; i++)
                        restored[i] = elementSnapshot.Restore(in items[i]);
                    sink.ReplaceStateContents(restored, count);
                }
                finally
                {
                    ArrayPool<T>.Return(restored);
                }
                return;
            }
            destination.Clear();
            if (count < 0) return;
            for (var i = 0; i < count; i++) destination.Add(elementSnapshot.Restore(in items[i]));
        }

        /// <summary>Restores only ordered item values, excluding nested detached state.</summary>
        public void CopyValuesTo(IList<T> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (destination is IStateCollectionMutationSink<T> sink)
            {
                if (count <= 0)
                {
                    sink.ReplaceStateContents(null, count);
                    return;
                }
                var values = ArrayPool<T>.Rent(count);
                try
                {
                    for (var i = 0; i < count; i++)
                        values[i] = SnapshotValue(in items[i]);
                    sink.ReplaceStateContents(values, count);
                }
                finally
                {
                    ArrayPool<T>.Return(values);
                }
                return;
            }
            destination.Clear();
            if (count < 0) return;
            for (var i = 0; i < count; i++)
                destination.Add(SnapshotValue(in items[i]));
        }

        /// <summary>Creates an array containing the captured ordered elements.</summary>
        public T[] ToArray()
        {
            if (count < 0) return null;
            if (count == 0) return Array.Empty<T>();
            var result = new T[count];
            for (var i = 0; i < count; i++) result[i] = elementSnapshot.Restore(in items[i]);
            return result;
        }

        /// <summary>Creates an array of captured item values without applying nested detached state.</summary>
        public T[] ToValueArray()
        {
            if (count < 0) return null;
            if (count == 0) return Array.Empty<T>();
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = SnapshotValue(in items[i]);
            return result;
        }

        private static bool SnapshotValuesEqual(in TSnapshot previous, in TSnapshot current)
            => valueSnapshot != null
                ? valueSnapshot.SnapshotValuesEqual(in previous, in current)
                : elementSnapshot.SnapshotEquals(in previous, in current);

        private static T SnapshotValue(in TSnapshot snapshot)
            => valueSnapshot != null
                ? valueSnapshot.SnapshotValue(in snapshot)
                : elementSnapshot.Restore(in snapshot);

        private void EnsureCapacity(int capacity)
        {
            if (capacity == 0) return;
            if (items != null && items.Length >= capacity) return;
            var next = 4;
            while (next < capacity) next <<= 1;
            items = new TSnapshot[next];
        }

        private void ClearItems()
        {
            if (items != null && count > 0) Array.Clear(items, 0, count);
        }
    }
}
