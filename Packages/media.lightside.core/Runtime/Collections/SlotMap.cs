using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LightSide
{
    /// <summary>
    /// Identity of one live slot in a <see cref="SlotMap"/>. A handle whose slot has been released never
    /// validates again, so a stale handle is detected rather than silently addressing a successor.
    /// </summary>
    public readonly struct SlotHandle : IEquatable<SlotHandle>
    {
        internal readonly int slot;
        internal readonly uint identityLow;
        internal readonly uint identityHigh;

        internal SlotHandle(int slot, uint identityLow, uint identityHigh)
        {
            this.slot = slot;
            this.identityLow = identityLow;
            this.identityHigh = identityHigh;
        }

        /// <summary>Whether this is the default handle, which no map ever issues.</summary>
        public bool IsDefault => identityLow == 0 && identityHigh == 0;

        /// <summary>Stable 64-bit form, for logging and dictionary keys.</summary>
        public long Id => unchecked((long)((ulong)identityHigh << 32 | identityLow));

        /// <inheritdoc/>
        public bool Equals(SlotHandle other) =>
            slot == other.slot && identityLow == other.identityLow && identityHigh == other.identityHigh;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SlotHandle other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(slot, identityLow, identityHigh);

        /// <inheritdoc/>
        public override string ToString() => IsDefault ? "<none>" : $"{slot}:{Id}";

        /// <summary>Whether two handles identify the same issued slot.</summary>
        public static bool operator ==(SlotHandle a, SlotHandle b) => a.Equals(b);

        /// <summary>Whether two handles identify different issued slots.</summary>
        public static bool operator !=(SlotHandle a, SlotHandle b) => !a.Equals(b);
    }

    /// <summary>
    /// Maps stable handles onto a caller-owned dense range <c>[0, Count)</c>, so parallel payload arrays stay
    /// packed for iteration while their owners keep an identity that survives every other element moving.
    /// </summary>
    /// <remarks>
    /// The map stores no payload. <see cref="Add"/> appends at <see cref="Count"/> and <see cref="Remove"/>
    /// reports the swap the caller must mirror in every payload array — the two must be applied together or
    /// the mapping and the payload diverge.
    /// <para>
    /// Copies share one identity map rather than duplicating ownership of its pooled arrays. No member is safe
    /// against concurrent use. <see cref="Dispose"/> invalidates the shared map and releases its backing arrays;
    /// every copy becomes an empty map that is valid again after the next <see cref="Add"/>.
    /// </para>
    /// </remarks>
    public struct SlotMap : IDisposable
    {
        private struct Slot
        {
            public int index;
            public uint identityLow;
            public uint identityHigh;
            public int nextFree;
        }

        private const int NoFree = -1;
        private static long nextIdentity;

        private sealed class Storage
        {
            public PooledBuffer<Slot> slots;
            public PooledBuffer<int> dense;
            public int freeHead = NoFree;
            public bool initialized;
        }

        private Storage storage;

        /// <summary>Number of live handles, and the exclusive upper bound of the dense range.</summary>
        public int Count => storage?.dense.count ?? 0;

        /// <summary>Slot index of the handle occupying <paramref name="index"/> of the dense range.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the dense range.</exception>
        public SlotHandle HandleAt(int index)
        {
            var state = storage;
            if (state == null || (uint)index >= (uint)state.dense.count)
                throw new ArgumentOutOfRangeException(nameof(index), index, null);
            var slot = state.dense[index];
            ref var entry = ref state.slots[slot];
            return new SlotHandle(slot, entry.identityLow, entry.identityHigh);
        }

        /// <summary>Grows the storage so the next <paramref name="count"/> additions allocate nothing.</summary>
        public void Reserve(int count)
        {
            if (count <= 0) return;
            var state = storage ??= new Storage();
            state.dense.EnsureCapacity(count);
            state.slots.EnsureCapacity(count);
        }

        /// <summary>
        /// Issues a handle for a new element appended at the current <see cref="Count"/>. The caller appends the
        /// element to every payload array in the same step.
        /// </summary>
        public SlotHandle Add()
        {
            var state = storage ??= new Storage();
            state.dense.EnsureCapacity(state.dense.count + 1);
            if (state.freeHead == NoFree)
                state.slots.EnsureCapacity(state.slots.count + 1);

            if (!state.initialized)
            {
                state.freeHead = NoFree;
                state.initialized = true;
            }

            var identity = NextIdentity();
            int slot;
            if (state.freeHead != NoFree)
            {
                slot = state.freeHead;
                state.freeHead = state.slots[slot].nextFree;
            }
            else
            {
                slot = state.slots.count;
                state.slots.Add(new Slot { nextFree = NoFree });
            }

            ref var entry = ref state.slots[slot];
            entry.identityLow = (uint)identity;
            entry.identityHigh = (uint)(identity >> 32);
            entry.index = state.dense.count;
            entry.nextFree = NoFree;
            state.dense.Add(slot);
            return new SlotHandle(slot, entry.identityLow, entry.identityHigh);
        }

        /// <summary>Whether <paramref name="handle"/> still addresses a live element.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(in SlotHandle handle) =>
            !handle.IsDefault && storage != null && (uint)handle.slot < (uint)storage.slots.count &&
            storage.slots.data[handle.slot].identityLow == handle.identityLow &&
            storage.slots.data[handle.slot].identityHigh == handle.identityHigh;

        /// <summary>Resolves <paramref name="handle"/> to its dense index, or returns false when it is stale.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetIndex(in SlotHandle handle, out int index)
        {
            var state = storage;
            if (!handle.IsDefault && state != null && (uint)handle.slot < (uint)state.slots.count)
            {
                ref var entry = ref state.slots.data[handle.slot];
                if (entry.identityLow == handle.identityLow && entry.identityHigh == handle.identityHigh)
                {
                    index = entry.index;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        /// <summary>
        /// Releases <paramref name="handle"/> and reports the swap that packs the dense range: the element at
        /// <paramref name="movedFrom"/> now lives at <paramref name="removed"/>, and the caller must perform the
        /// same move in every payload array. <paramref name="movedFrom"/> is -1 when the removed element was last.
        /// Returns false when the handle was already stale, leaving both outputs at -1.
        /// </summary>
        public bool Remove(in SlotHandle handle, out int removed, out int movedFrom)
        {
            if (!TryGetIndex(handle, out removed))
            {
                movedFrom = -1;
                return false;
            }

            var state = storage;
            var last = state.dense.count - 1;
            if (removed != last)
            {
                var moved = state.dense[last];
                state.dense[removed] = moved;
                state.slots[moved].index = removed;
                movedFrom = last;
            }
            else movedFrom = -1;

            state.dense.RemoveAt(last);

            ref var entry = ref state.slots[handle.slot];
            entry.identityLow = 0;
            entry.identityHigh = 0;
            entry.index = -1;
            entry.nextFree = state.freeHead;
            state.freeHead = handle.slot;
            return true;
        }

        /// <summary>Invalidates every handle and empties the dense range, keeping the backing arrays.</summary>
        public void Clear()
        {
            var state = storage;
            if (state == null) return;
            for (var i = 0; i < state.dense.count; i++)
            {
                ref var entry = ref state.slots[state.dense[i]];
                entry.identityLow = 0;
                entry.identityHigh = 0;
                entry.index = -1;
                entry.nextFree = state.freeHead;
                state.freeHead = state.dense[i];
            }
            state.dense.FakeClear();
        }

        /// <summary>Invalidates every handle and returns the backing arrays to the pool.</summary>
        public void Dispose()
        {
            var state = storage;
            if (state == null) return;
            state.slots.Return();
            state.dense.Return();
            state.slots = default;
            state.dense = default;
            state.freeHead = NoFree;
            state.initialized = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong NextIdentity()
        {
            var identity = unchecked((ulong)Interlocked.Increment(ref nextIdentity));
            return identity != 0
                ? identity
                : unchecked((ulong)Interlocked.Increment(ref nextIdentity));
        }
    }
}
