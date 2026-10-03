using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>Allocation-free read-only view over array storage without exposing the mutable array.</summary>
    public readonly struct ReadOnlyArray<T> : IReadOnlyList<T>, IEquatable<ReadOnlyArray<T>>
    {
        private readonly T[] items;

        /// <summary>Wraps array storage in a read-only public contract.</summary>
        public ReadOnlyArray(T[] items) => this.items = items;

        /// <inheritdoc/>
        public int Count => items?.Length ?? 0;

        /// <summary>Number of elements in the wrapped storage.</summary>
        public int Length => items?.Length ?? 0;

        /// <summary>Whether this view wraps concrete array storage.</summary>
        public bool HasStorage => items != null;

        /// <inheritdoc/>
        public T this[int index] => items[index];

        /// <summary>Whether both views reference the exact same backing storage.</summary>
        public bool HasSameStorage(ReadOnlyArray<T> other) => ReferenceEquals(items, other.items);

        /// <summary>Identity hash of the backing storage, or zero for a null view.</summary>
        public int GetStorageHashCode() => items == null ? 0 : RuntimeHelpers.GetHashCode(items);

        /// <summary>Creates a detached mutable array containing the current values.</summary>
        public T[] ToArray()
        {
            if (items == null || items.Length == 0) return Array.Empty<T>();
            return (T[])items.Clone();
        }

        /// <inheritdoc/>
        public bool Equals(ReadOnlyArray<T> other) => HasSameStorage(other);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ReadOnlyArray<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => GetStorageHashCode();

        /// <summary>Compares two views by backing-storage identity.</summary>
        public static bool operator ==(ReadOnlyArray<T> left, ReadOnlyArray<T> right) => left.Equals(right);

        /// <summary>Compares two views by backing-storage identity.</summary>
        public static bool operator !=(ReadOnlyArray<T> left, ReadOnlyArray<T> right) => !left.Equals(right);

        /// <summary>Returns an allocation-free enumerator over the wrapped storage.</summary>
        public Enumerator GetEnumerator() => new(items);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Allocation-free enumerator for <see cref="ReadOnlyArray{T}"/>.</summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly T[] items;
            private int index;

            internal Enumerator(T[] items)
            {
                this.items = items;
                index = -1;
            }

            /// <inheritdoc/>
            public T Current => items[index];

            object IEnumerator.Current => Current;

            /// <inheritdoc/>
            public bool MoveNext() => ++index < (items?.Length ?? 0);

            /// <inheritdoc/>
            public void Reset() => index = -1;

            /// <inheritdoc/>
            public void Dispose() { }
        }
    }
}
