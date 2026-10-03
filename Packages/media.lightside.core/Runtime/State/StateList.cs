using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;

namespace LightSide
{
    /// <summary>Applies declarative reference-item invariants generated for state lists.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class StateListRules
    {
        /// <summary>Validates null and duplicate-reference rules without boxing a projected mutation.</summary>
        public static void ValidateReferences<T>(in StateListMutation<T> items,
            bool allowNullItems, bool allowDuplicateReferences, string fieldName)
            where T : class
            => ValidateReferencesCore<StateListMutation<T>, T>(in items,
                allowNullItems, allowDuplicateReferences, fieldName);

        /// <summary>Validates null and duplicate-reference rules on materialized serialized contents.</summary>
        public static void ValidateReferences<T>(IReadOnlyList<T> items,
            bool allowNullItems, bool allowDuplicateReferences, string fieldName)
            where T : class
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            ValidateReferencesCore<IReadOnlyList<T>, T>(in items,
                allowNullItems, allowDuplicateReferences, fieldName);
        }

        private static void ValidateReferencesCore<TList, T>(in TList items,
            bool allowNullItems, bool allowDuplicateReferences, string fieldName)
            where TList : IReadOnlyList<T>
            where T : class
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null)
                {
                    if (!allowNullItems)
                        throw new ArgumentException($"State list '{fieldName}' cannot contain null items.");
                    continue;
                }
                if (allowDuplicateReferences) continue;
                for (var previous = 0; previous < i; previous++)
                    if (ReferenceEquals(items[previous], item))
                        throw new ArgumentException(
                            $"State list '{fieldName}' cannot contain the same instance more than once.");
            }
        }
    }

    /// <summary>Identifies one structural list mutation.</summary>
    public enum StateListMutationKind : byte
    {
        /// <summary>Appends one item.</summary>
        Add,
        /// <summary>Inserts one item.</summary>
        Insert,
        /// <summary>Replaces one item.</summary>
        Replace,
        /// <summary>Removes one item.</summary>
        Remove,
        /// <summary>Moves one item.</summary>
        Move,
        /// <summary>Removes every item.</summary>
        Clear,
        /// <summary>Replaces the complete ordered contents.</summary>
        ReplaceAll
    }

    /// <summary>Provides generated access to one serialized collection without exposing its storage.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IStateListAccess<T>
    {
        /// <summary>Gets the current item count from an owning object.</summary>
        int Count(object owner);
        /// <summary>Gets one current item from an owning object.</summary>
        T Get(object owner, int index);
        /// <summary>Validates, applies, and publishes one mutation on an owning object.</summary>
        void Mutate(object owner, in StateListMutation<T> mutation);
    }

    /// <summary>Receives a complete mutation so serializable wrappers can publish one transition.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IStateListMutationTarget<T>
    {
        /// <summary>Applies one mutation and publishes one structural transition.</summary>
        void Apply(in StateListMutation<T> mutation);
    }

    /// <summary>Describes both the current and projected contents of one structural mutation.</summary>
    public readonly struct StateListMutation<T> : IReadOnlyList<T>
    {
        private readonly StateList<T> source;
        private readonly IReadOnlyList<T> replacement;
        private readonly IReadOnlyList<T> committedItems;
        private readonly int committedPreviousCount;
        private readonly T committedPreviousItem;
        private readonly bool committed;

        internal StateListMutation(StateList<T> source, StateListMutationKind kind,
            int index = -1, int destinationIndex = -1, T item = default,
            IReadOnlyList<T> replacement = null, int committedPreviousCount = 0,
            T committedPreviousItem = default, bool committed = false,
            IReadOnlyList<T> committedItems = null)
        {
            this.source = source;
            this.replacement = replacement;
            this.committedItems = committedItems;
            this.committedPreviousCount = committedPreviousCount;
            this.committedPreviousItem = committedPreviousItem;
            this.committed = committed;
            Kind = kind;
            Index = index;
            DestinationIndex = destinationIndex;
            Value = item;
        }

        /// <summary>Creates the complete-replacement transition used by generated deserialization validation.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static StateListMutation<T> CreateReplacement(IReadOnlyList<T> replacement)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            return new StateListMutation<T>(default, StateListMutationKind.ReplaceAll,
                replacement: replacement);
        }

        /// <summary>Creates a committed mutation published by a serializable list wrapper.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static StateListMutation<T> CreateCommitted(IReadOnlyList<T> current,
            StateListMutationKind kind, int previousCount, int index = -1,
            int destinationIndex = -1, T item = default, T previousItem = default)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            return new StateListMutation<T>(default, kind, index, destinationIndex, item,
                committedPreviousCount: previousCount, committedPreviousItem: previousItem,
                committed: true, committedItems: current);
        }

        /// <summary>Gets the structural operation being applied.</summary>
        public StateListMutationKind Kind { get; }
        /// <summary>Gets the same operation in the universal state-change vocabulary.</summary>
        public StateChangeKind ChangeKind => Kind switch
        {
            StateListMutationKind.Add => StateChangeKind.Add,
            StateListMutationKind.Insert => StateChangeKind.Insert,
            StateListMutationKind.Replace => StateChangeKind.Replace,
            StateListMutationKind.Remove => StateChangeKind.Remove,
            StateListMutationKind.Move => StateChangeKind.Move,
            StateListMutationKind.Clear => StateChangeKind.Clear,
            StateListMutationKind.ReplaceAll => StateChangeKind.ReplaceAll,
            _ => throw new ArgumentOutOfRangeException()
        };
        /// <summary>Gets the affected source index, or -1 when the operation has none.</summary>
        public int Index { get; }
        /// <summary>Gets the committed affected index, including the appended index of an add.</summary>
        public int AffectedIndex => Kind == StateListMutationKind.Add ? PreviousCount : Index;
        /// <summary>Gets the destination index of a move, or -1 for other operations.</summary>
        public int DestinationIndex { get; }
        /// <summary>Gets the added or replacement item.</summary>
        public T Value { get; }
        /// <summary>Projects this mutation into the universal change header for <paramref name="member"/>.</summary>
        public StateChange ToStateChange(StateMember member)
            => new(member, ChangeKind, AffectedIndex, DestinationIndex);

        /// <summary>Gets the item count before mutation.</summary>
        public int PreviousCount => committed ? committedPreviousCount : source.Count;
        /// <summary>Gets the replaced or removed item.</summary>
        public T PreviousItem => committed
            ? committedPreviousItem
            : Kind is StateListMutationKind.Replace or StateListMutationKind.Remove
                ? source[Index]
                : default;

        /// <summary>
        /// Returns the affected item as it exists after the mutation. Removal, clear, and complete
        /// replacement do not identify one current item and return <see langword="false"/>.
        /// </summary>
        public bool TryGetCurrentItem(out T item)
        {
            switch (Kind)
            {
                case StateListMutationKind.Add:
                case StateListMutationKind.Insert:
                case StateListMutationKind.Replace:
                    item = Value;
                    return true;
                case StateListMutationKind.Move:
                    item = this[DestinationIndex];
                    return true;
                default:
                    item = default;
                    return false;
            }
        }

        /// <summary>Gets the projected item count after mutation.</summary>
        public int Count => committed
            ? committedItems?.Count ?? source.Count
            : Kind switch
            {
                StateListMutationKind.Add or StateListMutationKind.Insert => source.Count + 1,
                StateListMutationKind.Remove => source.Count - 1,
                StateListMutationKind.Clear => 0,
                StateListMutationKind.ReplaceAll => replacement.Count,
                _ => source.Count
            };

        /// <summary>Gets an item from the projected collection after mutation.</summary>
        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                if (committed) return committedItems != null ? committedItems[index] : source[index];
                return Kind switch
                {
                    StateListMutationKind.Add => index == source.Count ? Value : source[index],
                    StateListMutationKind.Insert => index == Index
                        ? Value
                        : source[index < Index ? index : index - 1],
                    StateListMutationKind.Replace => index == Index ? Value : source[index],
                    StateListMutationKind.Remove => source[index < Index ? index : index + 1],
                    StateListMutationKind.Move => GetMoved(index),
                    StateListMutationKind.Clear => throw new ArgumentOutOfRangeException(nameof(index)),
                    StateListMutationKind.ReplaceAll => replacement[index],
                    _ => throw new ArgumentOutOfRangeException(nameof(index))
                };
            }
        }

        /// <summary>Applies the mutation to compatible mutable storage.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void ApplyTo(IList<T> target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target is IStateListMutationTarget<T> specialized)
            {
                specialized.Apply(in this);
                return;
            }

            switch (Kind)
            {
                case StateListMutationKind.Add:
                    target.Add(Value);
                    break;
                case StateListMutationKind.Insert:
                    target.Insert(Index, Value);
                    break;
                case StateListMutationKind.Replace:
                    target[Index] = Value;
                    break;
                case StateListMutationKind.Remove:
                    target.RemoveAt(Index);
                    break;
                case StateListMutationKind.Move:
                    var moved = target[Index];
                    target.RemoveAt(Index);
                    target.Insert(DestinationIndex, moved);
                    break;
                case StateListMutationKind.Clear:
                    target.Clear();
                    break;
                case StateListMutationKind.ReplaceAll:
                    var values = ToArray();
                    target.Clear();
                    for (var i = 0; i < values.Length; i++) target.Add(values[i]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>Materializes the projected contents as a detached array.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public T[] ToArray()
        {
            if (Count == 0) return Array.Empty<T>();
            var result = new T[Count];
            for (var i = 0; i < result.Length; i++) result[i] = this[i];
            return result;
        }

        /// <summary>Captures the inverse operation before this mutation changes its source.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public StateListMutation<T> CaptureRollback()
        {
            return Kind switch
            {
                StateListMutationKind.Add => new StateListMutation<T>(source,
                    StateListMutationKind.Remove, source.Count),
                StateListMutationKind.Insert => new StateListMutation<T>(source,
                    StateListMutationKind.Remove, Index),
                StateListMutationKind.Replace => new StateListMutation<T>(source,
                    StateListMutationKind.Replace, Index, item: source[Index]),
                StateListMutationKind.Remove => new StateListMutation<T>(source,
                    StateListMutationKind.Insert, Index, item: source[Index]),
                StateListMutationKind.Move => new StateListMutation<T>(source,
                    StateListMutationKind.Move, DestinationIndex, Index),
                StateListMutationKind.Clear or StateListMutationKind.ReplaceAll =>
                    new StateListMutation<T>(source, StateListMutationKind.ReplaceAll,
                        replacement: CaptureSource()),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        /// <summary>Captures pre-mutation facts for publication after storage commits.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public StateListMutation<T> CaptureCommitted()
            => new(source, Kind, Index, DestinationIndex, Value, replacement, source.Count,
                Kind is StateListMutationKind.Replace or StateListMutationKind.Remove
                    ? source[Index]
                    : default,
                true);

        /// <summary>Detaches mutable value-type element storage before it reaches the owner.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public StateListMutation<T> Capture<TElementSnapshot>()
            where TElementSnapshot : struct, IStateCollectionElementSnapshot<T>
        {
            var snapshot = default(TElementSnapshot);
            if (Kind is StateListMutationKind.Add or StateListMutationKind.Insert or StateListMutationKind.Replace)
                return new StateListMutation<T>(source, Kind, Index, DestinationIndex,
                    snapshot.Capture(Value), replacement);
            if (Kind != StateListMutationKind.ReplaceAll) return this;
            var captured = new T[replacement.Count];
            for (var i = 0; i < captured.Length; i++) captured[i] = snapshot.Capture(replacement[i]);
            return new StateListMutation<T>(source, Kind, Index, DestinationIndex, Value, captured);
        }

        private T[] CaptureSource()
        {
            if (source.Count == 0) return Array.Empty<T>();
            var result = new T[source.Count];
            for (var i = 0; i < result.Length; i++) result[i] = source[i];
            return result;
        }

        /// <summary>Returns an allocation-free enumerator over the projected collection.</summary>
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private T GetMoved(int index)
        {
            if (index == DestinationIndex) return source[Index];
            if (Index < DestinationIndex && index >= Index && index < DestinationIndex)
                return source[index + 1];
            if (Index > DestinationIndex && index > DestinationIndex && index <= Index)
                return source[index - 1];
            return source[index];
        }

        /// <summary>Enumerates projected items without allocating.</summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly StateListMutation<T> owner;
            private int index;

            internal Enumerator(StateListMutation<T> owner)
            {
                this.owner = owner;
                index = -1;
            }

            /// <summary>Gets the current projected item.</summary>
            public T Current => owner[index];
            object IEnumerator.Current => Current;
            /// <summary>Advances to the next projected item.</summary>
            public bool MoveNext() => ++index < owner.Count;
            /// <summary>Moves before the first projected item.</summary>
            public void Reset() => index = -1;
            /// <summary>Releases no resources.</summary>
            public void Dispose() { }
        }
    }

    /// <summary>Mutable public view over serialized collection state.</summary>
    public readonly struct StateList<T> : IReadOnlyList<T>
    {
        private readonly object owner;
        private readonly IStateListAccess<T> access;

        /// <summary>Creates a generated list view bound to one owner and field accessor.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public StateList(object owner, IStateListAccess<T> access)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.access = access ?? throw new ArgumentNullException(nameof(access));
        }

        /// <summary>Gets the number of serialized items.</summary>
        public int Count => access != null
            ? access.Count(owner)
            : throw new InvalidOperationException("This state list is not bound to an owner.");
        /// <summary>Gets one serialized item.</summary>
        public T this[int index] => access != null
            ? access.Get(owner, index)
            : throw new InvalidOperationException("This state list is not bound to an owner.");

        /// <summary>Appends an item and publishes one structural transition.</summary>
        public void Add(T item) => Mutate(new StateListMutation<T>(this, StateListMutationKind.Add, item: item));

        /// <summary>Inserts an item at the requested index and publishes one structural transition.</summary>
        public void Insert(int index, T item)
        {
            if ((uint)index > (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            Mutate(new StateListMutation<T>(this, StateListMutationKind.Insert, index, item: item));
        }

        /// <summary>Replaces one item when its state value actually differs.</summary>
        public void Replace(int index, T item)
        {
            RequireIndex(index);
            if (StateArray.Same(this[index], item)) return;
            Mutate(new StateListMutation<T>(this, StateListMutationKind.Replace, index, item: item));
        }

        /// <summary>Removes the first matching item and returns whether it existed.</summary>
        public bool Remove(T item)
        {
            var index = IndexOf(item);
            if (index < 0) return false;
            RemoveAt(index);
            return true;
        }

        /// <summary>Removes the item at the requested index.</summary>
        public void RemoveAt(int index)
        {
            RequireIndex(index);
            Mutate(new StateListMutation<T>(this, StateListMutationKind.Remove, index));
        }

        /// <summary>Moves one item while preserving all remaining item order.</summary>
        public void Move(int fromIndex, int toIndex)
        {
            RequireIndex(fromIndex);
            RequireIndex(toIndex);
            if (fromIndex == toIndex) return;
            Mutate(new StateListMutation<T>(this, StateListMutationKind.Move, fromIndex, toIndex));
        }

        /// <summary>Removes every item when the collection is non-empty.</summary>
        public void Clear()
        {
            if (Count == 0) return;
            Mutate(new StateListMutation<T>(this, StateListMutationKind.Clear));
        }

        /// <summary>Replaces the complete ordered contents when they actually differ.</summary>
        public void ReplaceAll(IReadOnlyList<T> replacement)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            if (ContentsEqual(replacement)) return;
            Mutate(new StateListMutation<T>(this, StateListMutationKind.ReplaceAll, replacement: replacement));
        }

        /// <summary>Materializes and replaces the complete ordered contents of an enumerable source.</summary>
        public void ReplaceAll(IEnumerable<T> replacement)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            if (replacement is IReadOnlyList<T> list)
            {
                ReplaceAll(list);
                return;
            }
            ReplaceAll((IReadOnlyList<T>)new List<T>(replacement));
        }

        /// <summary>Returns whether a matching item exists.</summary>
        public bool Contains(T item) => IndexOf(item) >= 0;

        /// <summary>Returns the first matching item index, or -1 when absent.</summary>
        public int IndexOf(T item)
        {
            for (var i = 0; i < Count; i++)
                if (StateArray.Same(this[i], item)) return i;
            return -1;
        }

        /// <summary>Returns an allocation-free enumerator over serialized items.</summary>
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void Mutate(StateListMutation<T> mutation)
        {
            if (access == null) throw new InvalidOperationException("This state list is not bound to an owner.");
            access.Mutate(owner, in mutation);
        }

        private void RequireIndex(int index)
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        }

        private bool ContentsEqual(IReadOnlyList<T> candidate)
        {
            if (candidate.Count != Count) return false;
            for (var i = 0; i < candidate.Count; i++)
                if (!StateArray.Same(this[i], candidate[i])) return false;
            return true;
        }

        /// <summary>Enumerates serialized items without allocating.</summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly StateList<T> owner;
            private int index;

            internal Enumerator(StateList<T> owner)
            {
                this.owner = owner;
                index = -1;
            }

            /// <summary>Gets the current serialized item.</summary>
            public T Current => owner[index];
            object IEnumerator.Current => Current;
            /// <summary>Advances to the next serialized item.</summary>
            public bool MoveNext() => ++index < owner.Count;
            /// <summary>Moves before the first serialized item.</summary>
            public void Reset() => index = -1;
            /// <summary>Releases no resources.</summary>
            public void Dispose() { }
        }
    }
}
