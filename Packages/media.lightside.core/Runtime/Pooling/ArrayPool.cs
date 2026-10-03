using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LightSide
{
    /// <summary>
    /// Central registry for pool statistics logging across all <see cref="ArrayPool{T}"/> instances.
    /// </summary>
    /// <remarks>
    /// Each generic instantiation of <see cref="ArrayPool{T}"/> registers its logging function here.
    /// Call <see cref="LogAll"/> to output statistics for all pool types at once.
    /// </remarks>
    public static class PoolStats
    {
        private static readonly List<Action> registeredLogActions = new();
        private static readonly List<Action> registeredResetActions = new();

        /// <summary>
        /// Registers a statistics logging action for a pool type.
        /// </summary>
        /// <param name="logStats">The logging action to register.</param>
        /// <remarks>Called automatically by <see cref="ArrayPool{T}"/> static constructor.</remarks>
        [Conditional("LIGHTSIDE_POOL_DEBUG")]
        internal static void Register(Action logStats)
        {
            registeredLogActions.Add(logStats);
        }

        /// <summary>
        /// Registers a statistics reset action for a pool type.
        /// </summary>
        /// <param name="resetStats">The reset action to register.</param>
        [Conditional("LIGHTSIDE_POOL_DEBUG")]
        internal static void RegisterReset(Action resetStats)
        {
            registeredResetActions.Add(resetStats);
        }

        /// <summary>
        /// Logs statistics for all registered pool types.
        /// </summary>
        [Conditional("LIGHTSIDE_POOL_DEBUG")]
        public static void LogAll()
        {
            foreach (var logAction in registeredLogActions)
                logAction();
        }

        /// <summary>
        /// Resets statistics for all registered pool types.
        /// </summary>
        [Conditional("LIGHTSIDE_POOL_DEBUG")]
        public static void ResetAll()
        {
            foreach (var resetAction in registeredResetActions)
                resetAction();
        }

        /// <summary>Thread-safely increments a statistics counter.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("LIGHTSIDE_POOL_DEBUG")]
        internal static void Increment(ref int counter)
        {
            Interlocked.Increment(ref counter);
        }

        /// <summary>Thread-safely raises a counter to <paramref name="value"/> when it exceeds the current maximum.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("LIGHTSIDE_POOL_DEBUG")]
        internal static void TrackLargest(ref int current, int value)
        {
            int snapshot;
            do
            {
                snapshot = current;
                if (value <= snapshot) return;
            } while (Interlocked.CompareExchange(ref current, value, snapshot) != snapshot);
        }
    }

    /// <summary>
    /// High-performance array pool with thread-local caching and shared overflow storage.
    /// </summary>
    /// <typeparam name="T">The element type of arrays managed by this pool.</typeparam>
    /// <remarks>
    /// <para>
    /// This pool uses a two-tier caching strategy:
    /// <list type="number">
    /// <item><b>Thread-local cache:</b> One array per bucket per thread for instant access</item>
    /// <item><b>Shared buckets:</b> Concurrent queues for cross-thread array sharing</item>
    /// </list>
    /// </para>
    /// <para>
    /// Arrays are organized into power-of-two buckets from 4 through 65536.
    /// Arrays larger than 65536 are allocated directly without pooling.
    /// </para>
    /// <para>
    /// Usage pattern:
    /// <code>
    /// var array = ArrayPool&lt;int&gt;.Rent(100); // Gets array of size 128
    /// try {
    ///     // Use array...
    /// } finally {
    ///     ArrayPool&lt;int&gt;.Return(array);
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <seealso cref="PooledBuffer{T}"/>
    /// <seealso cref="PooledList{T}"/>
    public static class ArrayPool<T>
    {
        private const int BucketCount = 15;
        private const int MinBucketSize = 4;
        private const int FirstEagerBucket = 3;
        private const int MaxBucketSize = 65536;
        private const int MaxArraysPerShared = 1024;
        private static readonly bool containsReferences = RuntimeHelpers.IsReferenceOrContainsReferences<T>();

        [ThreadStatic] private static T[][] threadLocalArrays;
        [ThreadStatic] private static int[] threadLocalCounts;

        private static readonly ConcurrentQueue<T[]>[] sharedBuckets;
        private static readonly int[] sharedCounts;

        /// <summary>Total rent operations in current statistics period.</summary>
        private static int totalRents;

        /// <summary>Rents satisfied from thread-local cache.</summary>
        private static int poolHits;

        /// <summary>Rents that required new allocation.</summary>
        private static int poolMisses;

        /// <summary>Rents satisfied from shared buckets.</summary>
        private static int sharedHits;

        /// <summary>Total return operations in current statistics period.</summary>
        private static int totalReturns;

        /// <summary>Returns rejected because array was larger than max bucket size.</summary>
        private static int returnRejectedTooLarge;

        /// <summary>Returns rejected because array size didn't match bucket size.</summary>
        private static int returnRejectedWrongSize;

        /// <summary>Returns rejected because shared bucket was full.</summary>
        private static int returnRejectedPoolFull;

        /// <summary>Cumulative rent count since application start.</summary>
        private static int cumulativeRents;

        /// <summary>Cumulative return count since application start.</summary>
        private static int cumulativeReturns;

        /// <summary>Cumulative new array allocations since application start.</summary>
        private static int cumulativeAllocations;

        /// <summary>Largest array size ever requested from this pool.</summary>
        private static int largestRentRequested;

        static ArrayPool()
        {
            sharedBuckets = new ConcurrentQueue<T[]>[BucketCount];
            sharedCounts = new int[BucketCount];
            for (var i = FirstEagerBucket; i < BucketCount; i++)
                sharedBuckets[i] = new ConcurrentQueue<T[]>();

            PoolStats.Register(LogStats);
            PoolStats.RegisterReset(ResetStats);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureThreadLocalInitialized()
        {
            if (threadLocalArrays != null) return;
            var arrays = new T[BucketCount][];
            var counts = new int[BucketCount];
            threadLocalArrays = arrays;
            threadLocalCounts = counts;
        }

        /// <summary>
        /// Rents an array from the pool with at least the specified length.
        /// </summary>
        /// <param name="minimumLength">The minimum required array length.</param>
        /// <returns>
        /// An array with length greater than or equal to <paramref name="minimumLength"/>.
        /// The actual length will be the next power of 2 bucket size.
        /// </returns>
        /// <remarks>
        /// The returned array may contain data from previous use. Clear it if needed.
        /// Always call <see cref="Return"/> when done to avoid memory leaks.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="minimumLength"/> is negative.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T[] Rent(int minimumLength)
        {
            RequireNonNegative(minimumLength, nameof(minimumLength));
            PoolStats.TrackLargest(ref largestRentRequested, minimumLength);

            var bucketIndex = GetBucketIndex(minimumLength);
            if (bucketIndex < 0)
            {
                PoolStats.Increment(ref poolMisses);
                PoolStats.Increment(ref cumulativeRents);
                PoolStats.Increment(ref cumulativeAllocations);
                return new T[minimumLength];
            }

            PoolStats.Increment(ref totalRents);
            PoolStats.Increment(ref cumulativeRents);
            var bucketSize = MinBucketSize << bucketIndex;

            EnsureThreadLocalInitialized();
            if (threadLocalCounts[bucketIndex] > 0)
            {
                threadLocalCounts[bucketIndex] = 0;
                var arr = threadLocalArrays[bucketIndex];
                threadLocalArrays[bucketIndex] = null;
                PoolStats.Increment(ref poolHits);
                return arr;
            }

            var sharedBucket = sharedBuckets[bucketIndex];
            if (sharedBucket != null && sharedBucket.TryDequeue(out var sharedArr))
            {
                Interlocked.Decrement(ref sharedCounts[bucketIndex]);
                PoolStats.Increment(ref sharedHits);
                return sharedArr;
            }

            PoolStats.Increment(ref poolMisses);
            PoolStats.Increment(ref cumulativeAllocations);
            return new T[bucketSize];
        }

        /// <summary>
        /// Returns an array to the pool for reuse.
        /// </summary>
        /// <param name="array">The array to return. Can be <see langword="null"/> (no-op).</param>
        /// <remarks>
        /// <para>
        /// Only arrays with sizes matching power-of-two buckets from 4 through 65536 are accepted.
        /// Arrays larger than max bucket size or with non-matching sizes are silently discarded.
        /// </para>
        /// <para>
        /// Arrays whose element type contains references are cleared before storage.
        /// </para>
        /// <para>
        /// An array the optional cache cannot retain is not cached and does not cause the operation to fail.
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(T[] array)
        {
            if (array == null) return;

            PoolStats.Increment(ref cumulativeReturns);

            var bucketIndex = GetBucketIndex(array.Length);
            if (bucketIndex < 0)
            {
                PoolStats.Increment(ref returnRejectedTooLarge);
                return;
            }

            var bucketSize = MinBucketSize << bucketIndex;
            if (array.Length != bucketSize)
            {
                PoolStats.Increment(ref returnRejectedWrongSize);
                return;
            }

            PoolStats.Increment(ref totalReturns);

            try
            {
                EnsureThreadLocalInitialized();
            }
            catch (OutOfMemoryException)
            {
                return;
            }
            if (threadLocalCounts[bucketIndex] == 0)
            {
                if (containsReferences) Array.Clear(array, 0, array.Length);
                threadLocalArrays[bucketIndex] = array;
                threadLocalCounts[bucketIndex] = 1;
                return;
            }

            if (sharedCounts[bucketIndex] < MaxArraysPerShared)
            {
                ConcurrentQueue<T[]> sharedBucket;
                try
                {
                    sharedBucket = GetOrCreateSharedBucket(bucketIndex);
                }
                catch (OutOfMemoryException)
                {
                    return;
                }
                if (containsReferences) Array.Clear(array, 0, array.Length);
                try
                {
                    sharedBucket.Enqueue(array);
                }
                catch (OutOfMemoryException)
                {
                    return;
                }
                Interlocked.Increment(ref sharedCounts[bucketIndex]);
            }
            else
            {
                PoolStats.Increment(ref returnRejectedPoolFull);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBucketIndex(int size)
        {
            if (size <= MinBucketSize) return 0;
            if (size <= 8) return 1;
            if (size <= 16) return 2;
            if (size > MaxBucketSize) return -1;

            var shifted = (size - 1) / 32;
            var index = FirstEagerBucket;
            while (shifted > 0)
            {
                shifted >>= 1;
                index++;
            }

            return index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void RequireNonNegative(int value, string parameterName)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(parameterName, value, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ConcurrentQueue<T[]> GetOrCreateSharedBucket(int bucketIndex)
        {
            var bucket = sharedBuckets[bucketIndex];
            if (bucket != null) return bucket;

            var created = new ConcurrentQueue<T[]>();
            return Interlocked.CompareExchange(ref sharedBuckets[bucketIndex], created, null) ?? created;
        }


        /// <summary>
        /// Doubles the capacity of a pooled array when count has reached the current capacity.
        /// Copies existing elements, returns the old array to the pool, and updates the reference.
        /// </summary>
        public static void GrowDouble(ref T[] array, ref int capacity, int count)
        {
            if (count < capacity) return;
            var newCap = capacity * 2;
            var newArray = Rent(newCap);
            array.AsSpan(0, count).CopyTo(newArray);
            Return(array);
            array = newArray;
            capacity = newCap;
        }

        /// <summary>
        /// Clears all pooled arrays from both thread-local and shared storage.
        /// </summary>
        /// <remarks>
        /// Call this to release memory when the pool is no longer needed,
        /// such as during scene transitions or application shutdown.
        /// </remarks>
        public static void Clear()
        {
            if (threadLocalArrays != null)
            {
                Array.Clear(threadLocalArrays, 0, BucketCount);
                Array.Clear(threadLocalCounts, 0, BucketCount);
            }

            for (var i = 0; i < BucketCount; i++)
            {
                var bucket = sharedBuckets[i];
                if (bucket != null)
                    while (bucket.TryDequeue(out _)) { }
                sharedCounts[i] = 0;
            }
        }

        /// <summary>
        /// Resets all statistics counters for this pool type.
        /// </summary>
        public static void ResetStats()
        {
            totalRents = 0;
            poolHits = 0;
            poolMisses = 0;
            sharedHits = 0;
            totalReturns = 0;
            returnRejectedTooLarge = 0;
            returnRejectedWrongSize = 0;
            returnRejectedPoolFull = 0;
            cumulativeRents = 0;
            cumulativeReturns = 0;
            cumulativeAllocations = 0;
            largestRentRequested = 0;
        }

        /// <summary>
        /// Logs pool statistics to the Unity console.
        /// </summary>
        /// <remarks>
        /// Shows rent/return counts, hit rates, and any rejected returns.
        /// Called automatically by <see cref="PoolStats.LogAll"/>.
        /// </remarks>
        public static void LogStats()
        {
            var rents = totalRents;
            var hits = poolHits;
            var shared = sharedHits;
            var misses = poolMisses;
            var returns = totalReturns;
            var rejTooLarge = returnRejectedTooLarge;
            var rejWrongSize = returnRejectedWrongSize;
            var rejPoolFull = returnRejectedPoolFull;

            if (rents == 0 && misses == 0) return;

            var activeRents = cumulativeRents - cumulativeReturns;
            var totalRejected = rejTooLarge + rejWrongSize + rejPoolFull;

            var msg = $"[Pool<{typeof(T).Name}>] Rents:{rents} Hits:{hits} SharedHits:{shared} Misses:{misses} | Returns:{returns} Active:{activeRents}";
            if (totalRejected > 0)
                msg += $" | Rejected: TooLarge:{rejTooLarge} WrongSize:{rejWrongSize} PoolFull:{rejPoolFull}";
            if (largestRentRequested > 8192)
                msg += $" | LargestRequest:{largestRentRequested}";

            Cat.Meow(msg);
        }

        /// <summary>
        /// Gets a detailed string report of pool state including per-bucket counts.
        /// </summary>
        /// <returns>A formatted string showing thread-local and shared counts per bucket size.</returns>
        public static string GetStats()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"ArrayPool<{typeof(T).Name}>:");

            for (var i = 0; i < BucketCount; i++)
            {
                var size = MinBucketSize << i;
                var threadLocal = threadLocalArrays != null && threadLocalCounts[i] > 0 ? 1 : 0;
                var shared = sharedCounts[i];
                sb.AppendLine($"  [{size}]: ThreadLocal={threadLocal} Shared={shared}");
            }

            return sb.ToString();
        }

    }

    /// <summary>
    /// A value-type wrapper for managing a pooled array with count tracking.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <remarks>
    /// <para>
    /// Provides a List-like interface over a pooled array without heap allocations for the container itself.
    /// Use <see cref="Rent"/> to acquire an array and <see cref="Return"/> to release it back to the pool.
    /// </para>
    /// <para>
    /// The <see cref="count"/> field tracks how many elements are in use, while <see cref="Capacity"/>
    /// reflects the actual array length. Operations like <see cref="Add"/> automatically grow the buffer.
    /// </para>
    /// </remarks>
    /// <seealso cref="ArrayPool{T}"/>
    /// <seealso cref="PooledList{T}"/>
    public struct PooledBuffer<T>
    {
        /// <summary>The underlying array. May be <see langword="null"/> if not rented.</summary>
        public T[] data;

        /// <summary>The number of elements currently in use.</summary>
        public int count;

        /// <summary>Gets the capacity of the underlying array (0 if not rented).</summary>
        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => data?.Length ?? 0;
        }

        /// <summary>Gets a reference to the element at the specified index.</summary>
        /// <param name="i">The zero-based index.</param>
        /// <returns>A reference to the element.</returns>
        public ref T this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref data[i];
        }

        /// <summary>Gets a span over the used portion of the buffer (0 to count).</summary>
        public Span<T> Span
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => data.AsSpan(0, count);
        }

        /// <summary>Replaces the backing storage and resets the count, preserving the current storage if acquisition fails.</summary>
        /// <param name="capacity">The minimum required capacity.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rent(int capacity)
        {
            ArrayPool<T>.RequireNonNegative(capacity, nameof(capacity));
            var replacement = capacity > 0 ? ArrayPool<T>.Rent(capacity) : null;
            var previous = data;
            data = replacement;
            count = 0;
            if (previous != null && previous.Length > 0)
                ArrayPool<T>.Return(previous);
        }

        /// <summary>
        /// Returns the array to the pool and resets the buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return()
        {
            if (data != null && data.Length > 0)
            {
                ArrayPool<T>.Return(data);
                data = null;
            }
            count = 0;
        }

        /// <summary>Replaces rented storage with at least <paramref name="newCapacity"/> elements while preserving the requested occupied prefix; an unrented buffer remains unchanged.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="newCapacity"/> or <paramref name="count"/> is negative, or the prefix omits used elements or exceeds the source or requested destination capacity.</exception>
        public void Grow(int newCapacity, int count)
        {
            ArrayPool<T>.RequireNonNegative(newCapacity, nameof(newCapacity));
            ArrayPool<T>.RequireNonNegative(count, nameof(count));
            if (data == null) return;
            if (count < this.count || count > Capacity || count > newCapacity)
                throw new ArgumentOutOfRangeException(nameof(count), count, null);

            var replacement = newCapacity > 0 ? ArrayPool<T>.Rent(newCapacity) : null;
            if (count > 0) data.AsSpan(0, count).CopyTo(replacement);
            var previous = data;
            data = replacement;
            ArrayPool<T>.Return(previous);
        }

        /// <summary>Replaces rented storage while preserving an occupied prefix and clearing every remaining element; an unrented buffer remains unchanged.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="newCapacity"/> or <paramref name="count"/> is negative, or the prefix omits used elements or exceeds the source or requested destination capacity.</exception>
        public void GrowCleared(int newCapacity, int count)
        {
            Grow(newCapacity, count);
            if (data != null)
                Array.Clear(data, count, data.Length - count);
        }

        /// <summary>Lazily rents and zero-clears positive-capacity storage, returning whether this call allocated.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool EnsureCleared(int capacity)
        {
            ArrayPool<T>.RequireNonNegative(capacity, nameof(capacity));
            if (data != null || capacity == 0) return false;
            Rent(capacity);
            Array.Clear(data, 0, data.Length);
            return true;
        }

        /// <summary>
        /// Resets count to zero without clearing data or returning to pool.
        /// </summary>
        /// <remarks>Fast reset for reuse when data clearing is not needed.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FakeClear()
        {
            count = 0;
        }

        /// <summary>
        /// Clears the used portion of the array and resets count to zero.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            if (count > 0)
            {
                data.AsSpan(0, count).Clear();
                count = 0;
            }
        }

        /// <summary>
        /// Clears the used portion of the array without resetting count.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearData()
        {
            if (count > 0)
            {
                data.AsSpan(0, count).Clear();
            }
        }

        /// <summary>
        /// Clears the entire backing array (not just <c>[0..count)</c>) and resets count to zero.
        /// </summary>
        /// <remarks>
        /// Use this when elements are written via direct <see cref="data"/> indexing without
        /// updating <see cref="count"/> — a pattern used by side buffers read at arbitrary
        /// absolute indices. <see cref="Clear"/> and <see cref="ClearData"/> only scrub
        /// <c>[0..count)</c> which is a no-op for such buffers when count is zero.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearAll()
        {
            if (data != null && data.Length > 0)
                Array.Clear(data, 0, data.Length);
            count = 0;
        }

        /// <summary>
        /// Ensures the buffer has at least the specified capacity, growing if needed.
        /// </summary>
        /// <param name="required">The minimum required capacity.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="required"/> is negative.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCapacity(int required)
        {
            ArrayPool<T>.RequireNonNegative(required, nameof(required));
            EnsureCapacityUnchecked(required);
        }

        /// <summary>
        /// Ensures both capacity and count are at least the specified value.
        /// </summary>
        /// <param name="required">The minimum required count.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="required"/> is negative.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCount(int required)
        {
            ArrayPool<T>.RequireNonNegative(required, nameof(required));
            EnsureCapacityUnchecked(required);
            if (count < required) count = required;
        }

        /// <summary>
        /// Sets <see cref="count"/> to exactly <paramref name="n"/>, growing capacity if needed. Unlike
        /// <see cref="EnsureCount"/> this also shrinks, dropping stale trailing entries from a prior pass.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is negative.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetCount(int n)
        {
            ArrayPool<T>.RequireNonNegative(n, nameof(n));
            EnsureCapacityUnchecked(n);
            count = n;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacityUnchecked(int required)
        {
            if (Capacity < required) Grow(required);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow(int required)
        {
            var oldLen = data?.Length ?? 0;
            var newSize = oldLen == 0 ? Math.Max(required, 4) : Math.Max(required, oldLen * 2);
            var newData = ArrayPool<T>.Rent(newSize);
            if (oldLen > 0)
            {
                data.AsSpan(0, count).CopyTo(newData);
                ArrayPool<T>.Return(data);
            }
            data = newData;
        }

        /// <summary>
        /// Adds an item to the end of the buffer, growing if necessary.
        /// </summary>
        /// <param name="item">The item to add.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            if (count >= Capacity)
                EnsureCapacity(checked(count + 1));
            data[count++] = item;
        }

        /// <summary>
        /// Removes the available portion of a range, or leaves the buffer unchanged when the range cannot start in its used portion.
        /// </summary>
        /// <param name="index">The starting index of elements to remove.</param>
        /// <param name="removeCount">The number of elements to remove.</param>
        public void RemoveRange(int index, int removeCount)
        {
            if (removeCount <= 0 || index < 0 || index >= count) return;
            if (removeCount > count - index)
                removeCount = count - index;
            count -= removeCount;
            if (index < count)
                Array.Copy(data, index + removeCount, data, index, count - index);
        }

        /// <summary>
        /// Removes the element at the specified index and shifts subsequent elements, or leaves the buffer unchanged when the index is unused.
        /// </summary>
        /// <param name="index">The index of the element to remove.</param>
        public void RemoveAt(int index)
        {
            if (count == 0 || index < 0 || index >= count) return;
            count--;
            if (index < count)
                Array.Copy(data, index + 1, data, index, count - index);
        }

        /// <summary>
        /// Inserts an item at the specified index, shifting subsequent elements.
        /// </summary>
        /// <param name="index">The index at which to insert.</param>
        /// <param name="item">The item to insert.</param>
        public void Insert(int index, T item)
        {
            if (index < 0 || index > count)
                throw new ArgumentOutOfRangeException(nameof(index));
            EnsureCapacity(checked(count + 1));
            if (index < count)
                Array.Copy(data, index, data, index + 1, count - index);
            data[index] = item;
            count++;
        }

        /// <summary>
        /// Removes the element at the specified index by swapping with the last element, or leaves the buffer unchanged when the index is unused.
        /// </summary>
        /// <param name="index">The index of the element to remove.</param>
        /// <remarks>O(1) removal but does not preserve order.</remarks>
        public void SwapRemoveAt(int index)
        {
            if (count == 0 || index < 0 || index >= count) return;
            count--;
            if (index < count)
                data[index] = data[count];
            data[count] = default;
        }

        /// <summary>
        /// Sorts the used portion when it contains at least two elements.
        /// </summary>
        /// <param name="comparison">The comparison delegate.</param>
        public void Sort(Comparison<T> comparison)
        {
            if (count > 1)
                Array.Sort(data, 0, count, Comparer<T>.Create(comparison));
        }

        /// <summary>
        /// Sorts a range containing at least two elements; shorter requests leave the buffer unchanged without accessing storage.
        /// </summary>
        /// <param name="index">The starting index of the range to sort.</param>
        /// <param name="length">The number of elements to sort.</param>
        /// <param name="comparer">The comparer to use.</param>
        public void Sort(int index, int length, IComparer<T> comparer)
        {
            if (length > 1)
                Array.Sort(data, index, length, comparer);
        }
    }

    /// <summary>
    /// A reference-type wrapper around <see cref="PooledBuffer{T}"/> for scenarios requiring heap allocation.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <remarks>
    /// <para>
    /// Use this when you need to store a pooled collection in a field or pass it by reference
    /// without boxing. The underlying <see cref="buffer"/> is a struct, so modifications are in-place.
    /// </para>
    /// <para>
    /// Provides the same API as <see cref="PooledBuffer{T}"/> but as a class for scenarios
    /// where value-type semantics are not suitable.
    /// </para>
    /// </remarks>
    /// <seealso cref="PooledBuffer{T}"/>
    /// <seealso cref="ArrayPool{T}"/>
    public sealed class PooledList<T> : IList<T>
    {
        /// <summary>The underlying buffer storage.</summary>
        public PooledBuffer<T> buffer;

        /// <summary>
        /// Initializes a new instance without renting an array.
        /// </summary>
        public PooledList()
        {
            buffer = default;
        }

        /// <summary>
        /// Initializes a new instance and rents an array with the specified capacity.
        /// </summary>
        /// <param name="capacity">The initial capacity.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
        public PooledList(int capacity)
        {
            buffer = default;
            buffer.Rent(capacity);
        }

        /// <summary>Gets the number of elements in the list.</summary>
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.count;
        }

        /// <summary>Gets the capacity of the underlying array.</summary>
        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => buffer.Capacity;
        }

        /// <summary>Gets a reference to the element at the specified index.</summary>
        /// <param name="i">The zero-based index.</param>
        /// <returns>A reference to the element.</returns>
        public ref T this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref buffer[i];
        }

        /// <summary>IList interface indexer.</summary>
        T IList<T>.this[int index]
        {
            get => buffer[index];
            set => buffer[index] = value;
        }

        /// <inheritdoc />
        bool ICollection<T>.IsReadOnly => false;

        /// <inheritdoc cref="PooledBuffer{T}.Add"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item) => buffer.Add(item);

        /// <inheritdoc cref="PooledBuffer{T}.Clear"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() => buffer.Clear();

        /// <inheritdoc cref="PooledBuffer{T}.FakeClear"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FakeClear() => buffer.FakeClear();

        /// <inheritdoc cref="PooledBuffer{T}.ClearAll"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearAll() => buffer.ClearAll();

        /// <inheritdoc cref="PooledBuffer{T}.Return"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return() => buffer.Return();

        /// <inheritdoc cref="PooledBuffer{T}.EnsureCapacity"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCapacity(int required) => buffer.EnsureCapacity(required);

        /// <inheritdoc cref="PooledBuffer{T}.EnsureCount"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCount(int required) => buffer.EnsureCount(required);

        /// <inheritdoc cref="PooledBuffer{T}.RemoveRange"/>
        public void RemoveRange(int index, int removeCount) => buffer.RemoveRange(index, removeCount);

        /// <inheritdoc cref="PooledBuffer{T}.RemoveAt"/>
        public void RemoveAt(int index) => buffer.RemoveAt(index);

        /// <inheritdoc cref="PooledBuffer{T}.Sort(Comparison{T})"/>
        public void Sort(Comparison<T> comparison) => buffer.Sort(comparison);

        /// <inheritdoc cref="PooledBuffer{T}.Sort(int, int, IComparer{T})"/>
        public void Sort(int index, int length, IComparer<T> comparer) => buffer.Sort(index, length, comparer);

        /// <inheritdoc />
        public bool Contains(T item)
        {
            var data = buffer.data;
            var count = buffer.count;
            if (data == null) return false;
            for (var i = 0; i < count; i++)
                if (ValueEquality<T>.Same(data[i], item))
                    return true;
            return false;
        }

        /// <inheritdoc />
        public void CopyTo(T[] array, int arrayIndex)
        {
            if (buffer.data != null && buffer.count > 0)
                Array.Copy(buffer.data, 0, array, arrayIndex, buffer.count);
        }

        /// <inheritdoc />
        public int IndexOf(T item)
        {
            var data = buffer.data;
            var count = buffer.count;
            if (data == null) return -1;
            for (var i = 0; i < count; i++)
                if (ValueEquality<T>.Same(data[i], item))
                    return i;
            return -1;
        }

        /// <inheritdoc />
        public void Insert(int index, T item) => buffer.Insert(index, item);

        /// <inheritdoc />
        public bool Remove(T item)
        {
            var index = IndexOf(item);
            if (index < 0) return false;
            RemoveAt(index);
            return true;
        }

        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator()
        {
            var data = buffer.data;
            var count = buffer.count;
            for (var i = 0; i < count; i++)
                yield return data[i];
        }

        /// <inheritdoc />
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

}
