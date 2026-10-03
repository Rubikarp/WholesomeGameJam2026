using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// High-performance dictionary optimized for integer keys using open addressing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses Struct-of-Arrays layout: keys and values are stored in separate arrays.
    /// Probing only touches the compact key array (4 bytes per slot), loading the value
    /// array only on key match. This dramatically reduces cache pressure for large value types.
    /// </para>
    /// <para>
    /// Key <c>-1</c> (0xFFFFFFFF) is reserved as the empty-slot sentinel and cannot be stored.
    /// Internally keys are stored as <c>key + 1</c> so that key <c>0</c> maps to stored value <c>1</c>
    /// and the stored value <c>0</c> unambiguously marks an empty slot.
    /// </para>
    /// <para>
    /// Writes are not thread-safe and require external synchronization. Reads (TryGetValue, ContainsKey)
    /// are safe against a concurrent <c>Grow</c>: keys, values and mask live in one immutable <c>Table</c>
    /// published through a single volatile reference, so a reader always observes a consistent generation.
    /// Grows automatically at 75% load factor.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The value type.</typeparam>
    public sealed class FastIntDictionary<T>
    {
        private sealed class Table
        {
            public readonly int[] keys;
            public readonly T[] values;
            public readonly int mask;
            public Table(int size) { keys = new int[size]; values = new T[size]; mask = size - 1; }
        }

        private volatile Table table;
        private int count;
        private int growThreshold;

        public FastIntDictionary(int capacity = 16)
        {
            var size = NextPowerOfTwo(capacity * 4 / 3 + 1);
            table = new Table(size);
            growThreshold = size * 3 / 4;
        }

        public int Count => count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(int key, out T value)
        {
            var t = table;
            var k = t.keys;
            var m = t.mask;
            var idx = key & m;
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    value = t.values[idx];
                    return true;
                }
                idx = (idx + 1) & m;
            }

            value = default;
            return false;
        }

        public T this[int key]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (TryGetValue(key, out var val))
                    return val;
                throw new KeyNotFoundException();
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => AddOrUpdate(key, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddOrUpdate(int key, T value)
        {
            if (key == -1)
                throw new ArgumentException("Key -1 is reserved as the empty-slot sentinel and cannot be stored.", nameof(key));

            if (count >= growThreshold)
                Grow();

            var t = table;
            var k = t.keys;
            var m = t.mask;
            var idx = key & m;
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    t.values[idx] = value;
                    return;
                }
                idx = (idx + 1) & m;
            }

            k[idx] = sk;
            t.values[idx] = value;
            count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(int key)
        {
            var t = table;
            var k = t.keys;
            var m = t.mask;
            var idx = key & m;
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                    return true;
                idx = (idx + 1) & m;
            }

            return false;
        }

        public bool Remove(int key)
        {
            var t = table;
            var k = t.keys;
            var v = t.values;
            var m = t.mask;
            var idx = key & m;
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    count--;
                    var empty = idx;

                    while (true)
                    {
                        idx = (idx + 1) & m;

                        if (k[idx] == 0)
                        {
                            k[empty] = 0;
                            v[empty] = default;
                            return true;
                        }

                        var ideal = (k[idx] - 1) & m;

                        if ((empty <= idx) ? (ideal <= empty || ideal > idx) : (ideal <= empty && ideal > idx))
                        {
                            k[empty] = k[idx];
                            v[empty] = v[idx];
                            empty = idx;
                        }
                    }
                }
                idx = (idx + 1) & m;
            }

            return false;
        }

        public void Clear()
        {
            var t = table;
            Array.Clear(t.keys, 0, t.keys.Length);
            Array.Clear(t.values, 0, t.values.Length);
            count = 0;
        }

        public void ClearFast()
        {
            if (count == 0) return;
            Array.Clear(table.keys, 0, table.keys.Length);
            count = 0;
        }

        private void Grow()
        {
            var old = table;
            var oldKeys = old.keys;
            var oldValues = old.values;
            var next = new Table(oldKeys.Length * 2);
            var newKeys = next.keys;
            var newValues = next.values;
            var newMask = next.mask;

            for (var i = 0; i < oldKeys.Length; i++)
            {
                if (oldKeys[i] != 0)
                {
                    var idx = (oldKeys[i] - 1) & newMask;
                    while (newKeys[idx] != 0)
                        idx = (idx + 1) & newMask;
                    newKeys[idx] = oldKeys[i];
                    newValues[idx] = oldValues[i];
                }
            }

            table = next;
            growThreshold = next.keys.Length * 3 / 4;
        }

        private static int NextPowerOfTwo(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return v + 1;
        }

        public Enumerator GetEnumerator() => new(this);

        public struct Enumerator
        {
            private readonly int[] storedKeys;
            private readonly T[] values;
            private int index;
            private int remaining;

            internal Enumerator(FastIntDictionary<T> dict)
            {
                var t = dict.table;
                storedKeys = t.keys;
                values = t.values;
                index = -1;
                remaining = dict.count;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (remaining <= 0) return false;
                while (++index < storedKeys.Length)
                {
                    if (storedKeys[index] != 0)
                    {
                        remaining--;
                        return true;
                    }
                }
                return false;
            }

            public KeyValuePair<int, T> Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => new(storedKeys[index] - 1, values[index]);
            }
        }
    }

    /// <summary>
    /// High-performance dictionary optimized for long keys using open addressing with Fibonacci hashing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses Struct-of-Arrays layout: keys and values are stored in separate arrays.
    /// Probing only touches the compact key array (8 bytes per slot), loading the value
    /// array only on key match. This dramatically reduces cache pressure for large value types.
    /// </para>
    /// <para>
    /// Key <c>-1</c> (0xFFFFFFFFFFFFFFFF) is reserved as the empty-slot sentinel and cannot be stored.
    /// Internally keys are stored as <c>key + 1</c> so that key <c>0</c> maps to stored value <c>1</c>
    /// and the stored value <c>0</c> unambiguously marks an empty slot.
    /// </para>
    /// <inheritdoc cref="FastIntDictionary{T}"/>
    /// </remarks>
    public sealed class FastLongDictionary<T>
    {
        private sealed class Table
        {
            public readonly long[] keys;
            public readonly T[] values;
            public readonly int mask;
            public Table(int size) { keys = new long[size]; values = new T[size]; mask = size - 1; }
        }

        private volatile Table table;
        private int count;
        private int growThreshold;

        public FastLongDictionary(int capacity = 16)
        {
            var size = NextPowerOfTwo(capacity * 4 / 3 + 1);
            table = new Table(size);
            growThreshold = size * 3 / 4;
        }

        public int Count => count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Hash(long key, int mask) =>
            (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> 32) & mask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(long key, out T value)
        {
            var t = table;
            var k = t.keys;
            var m = t.mask;
            var idx = Hash(key, m);
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    value = t.values[idx];
                    return true;
                }
                idx = (idx + 1) & m;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Returns a reference to the stored value for <paramref name="key"/>, avoiding a copy of large
        /// value types. The reference aliases the current backing array and is invalidated by any insert
        /// (<see cref="AddOrUpdate"/>, <see cref="TryAdd"/>) or the <c>Grow</c> it may trigger, and by
        /// <see cref="Remove"/>; read it immediately and never hold it across a mutation of this dictionary.
        /// On a miss <paramref name="found"/> is <c>false</c> and the returned reference is a placeholder
        /// that must not be dereferenced.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T TryGetValueRef(long key, out bool found)
        {
            var t = table;
            var k = t.keys;
            var m = t.mask;
            var idx = Hash(key, m);
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    found = true;
                    return ref t.values[idx];
                }
                idx = (idx + 1) & m;
            }

            found = false;
            return ref t.values[0];
        }

        public T this[long key]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (TryGetValue(key, out var val))
                    return val;
                throw new KeyNotFoundException();
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => AddOrUpdate(key, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddOrUpdate(long key, T value)
        {
            if (key == -1L)
                throw new ArgumentException("Key -1 is reserved as the empty-slot sentinel and cannot be stored.", nameof(key));

            if (count >= growThreshold)
                Grow();

            var t = table;
            var k = t.keys;
            var m = t.mask;
            var idx = Hash(key, m);
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    t.values[idx] = value;
                    return;
                }
                idx = (idx + 1) & m;
            }

            k[idx] = sk;
            t.values[idx] = value;
            count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAdd(long key, T value)
        {
            if (key == -1L)
                throw new ArgumentException("Key -1 is reserved as the empty-slot sentinel and cannot be stored.", nameof(key));

            if (count >= growThreshold)
                Grow();

            var t = table;
            var k = t.keys;
            var m = t.mask;
            var idx = Hash(key, m);
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                    return false;
                idx = (idx + 1) & m;
            }

            k[idx] = sk;
            t.values[idx] = value;
            count++;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(long key)
        {
            var t = table;
            var k = t.keys;
            var m = t.mask;
            var idx = Hash(key, m);
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                    return true;
                idx = (idx + 1) & m;
            }

            return false;
        }

        public bool Remove(long key)
        {
            var t = table;
            var k = t.keys;
            var v = t.values;
            var m = t.mask;
            var idx = Hash(key, m);
            var sk = key + 1;

            while (k[idx] != 0)
            {
                if (k[idx] == sk)
                {
                    count--;
                    var empty = idx;

                    while (true)
                    {
                        idx = (idx + 1) & m;

                        if (k[idx] == 0)
                        {
                            k[empty] = 0;
                            v[empty] = default;
                            return true;
                        }

                        var ideal = Hash(k[idx] - 1, m);

                        if ((empty <= idx) ? (ideal <= empty || ideal > idx) : (ideal <= empty && ideal > idx))
                        {
                            k[empty] = k[idx];
                            v[empty] = v[idx];
                            empty = idx;
                        }
                    }
                }
                idx = (idx + 1) & m;
            }

            return false;
        }

        public void Clear()
        {
            var t = table;
            Array.Clear(t.keys, 0, t.keys.Length);
            Array.Clear(t.values, 0, t.values.Length);
            count = 0;
        }

        public void ClearFast()
        {
            if (count == 0) return;
            Array.Clear(table.keys, 0, table.keys.Length);
            count = 0;
        }

        private void Grow()
        {
            var old = table;
            var oldKeys = old.keys;
            var oldValues = old.values;
            var next = new Table(oldKeys.Length * 2);
            var newKeys = next.keys;
            var newValues = next.values;
            var newMask = next.mask;

            for (var i = 0; i < oldKeys.Length; i++)
            {
                if (oldKeys[i] != 0)
                {
                    var idx = Hash(oldKeys[i] - 1, newMask);
                    while (newKeys[idx] != 0)
                        idx = (idx + 1) & newMask;
                    newKeys[idx] = oldKeys[i];
                    newValues[idx] = oldValues[i];
                }
            }

            table = next;
            growThreshold = next.keys.Length * 3 / 4;
        }

        private static int NextPowerOfTwo(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return v + 1;
        }

        public Enumerator GetEnumerator() => new(this);

        public struct Enumerator
        {
            private readonly long[] storedKeys;
            private readonly T[] values;
            private int index;
            private int remaining;

            internal Enumerator(FastLongDictionary<T> dict)
            {
                var t = dict.table;
                storedKeys = t.keys;
                values = t.values;
                index = -1;
                remaining = dict.count;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (remaining <= 0) return false;
                while (++index < storedKeys.Length)
                {
                    if (storedKeys[index] != 0)
                    {
                        remaining--;
                        return true;
                    }
                }
                return false;
            }

            public KeyValuePair<long, T> Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => new(storedKeys[index] - 1, values[index]);
            }
        }
    }
}
