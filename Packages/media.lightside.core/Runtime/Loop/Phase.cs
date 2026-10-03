using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Subscription slot in a <see cref="Phase"/>. The default value represents no subscription
    /// and is always safe to pass to <see cref="Phase.Remove"/>.
    /// </summary>
    public struct TickHandle
    {
        internal Phase owner;
        internal int slot;
        internal int version;

        /// <summary>Gets whether this handle currently represents a live subscription.</summary>
        public bool IsActive => slot != 0;
    }

    /// <summary>
    /// One moment of the shared frame loop: a set of per-frame callbacks with O(1) allocation-free
    /// subscription and removal, safe to mutate from inside its own tick. A callback added during a
    /// tick first runs on the next tick; a callback removed during a tick no longer runs in it. A
    /// subscriber's exception is logged and does not stop the others. Callback order is not a
    /// contract. Main thread only.
    /// </summary>
    public sealed class Phase
    {
        private Action[] callbacks = Array.Empty<Action>();
        private int[] callbackSlots = Array.Empty<int>();
        private int count;
        private int[] slotIndex = Array.Empty<int>();
        private int[] slotVersion = Array.Empty<int>();
        private int[] freeSlots = Array.Empty<int>();
        private int freeCount;
        private int slotCount;
        private int ticking = -1;
        private int holes;

        /// <summary>Gets the number of live subscriptions.</summary>
        public int Count => count - holes;

        /// <summary>
        /// Adds a callback that runs once per tick of this phase until removed, and returns the
        /// handle that removes it. Cache the delegate in a field when subscribing repeatedly —
        /// every method-group conversion allocates a new one.
        /// </summary>
        public TickHandle Add(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            int slot;
            if (freeCount > 0) slot = freeSlots[--freeCount];
            else
            {
                if (slotCount == slotIndex.Length)
                {
                    var grown = Math.Max(8, slotCount * 2);
                    Array.Resize(ref slotIndex, grown);
                    Array.Resize(ref slotVersion, grown);
                    Array.Resize(ref freeSlots, grown);
                }
                slot = slotCount++;
            }

            if (count == callbacks.Length)
            {
                var grown = Math.Max(8, count * 2);
                Array.Resize(ref callbacks, grown);
                Array.Resize(ref callbackSlots, grown);
            }

            callbacks[count] = callback;
            callbackSlots[count] = slot;
            slotIndex[slot] = count;
            count++;
            return new TickHandle { owner = this, slot = slot + 1, version = slotVersion[slot] };
        }

        /// <summary>
        /// Removes the subscription behind <paramref name="handle"/> and resets it to default.
        /// A default or already-removed handle is ignored.
        /// </summary>
        /// <exception cref="ArgumentException">The handle belongs to a different phase.</exception>
        public void Remove(ref TickHandle handle)
        {
            var owner = handle.owner;
            var slot = handle.slot - 1;
            var version = handle.version;
            handle = default;
            if (slot < 0) return;
            if (owner != this) throw new ArgumentException("The handle belongs to a different phase.", nameof(handle));
            if (slotVersion[slot] != version) return;

            var index = slotIndex[slot];
            slotVersion[slot]++;
            freeSlots[freeCount++] = slot;

            if (ticking >= 0)
            {
                callbacks[index] = null;
                holes++;
                return;
            }

            var last = count - 1;
            if (index != last)
            {
                callbacks[index] = callbacks[last];
                callbackSlots[index] = callbackSlots[last];
                slotIndex[callbackSlots[last]] = index;
            }
            callbacks[last] = null;
            count = last;
        }

        /// <summary>
        /// Aligns the subscription with <paramref name="active"/>: adds <paramref name="callback"/>
        /// when it should run and <paramref name="handle"/> is inactive, removes it when it should
        /// not and the handle is active. No-op when the state already matches.
        /// </summary>
        public void Toggle(ref TickHandle handle, Action callback, bool active)
        {
            if (active == handle.IsActive) return;
            if (active) handle = Add(callback);
            else Remove(ref handle);
        }

        internal void Tick()
        {
            Debug.Assert(ticking < 0, "Phase.Tick reentered.");
            var end = count;
            ticking = end;
            for (var i = 0; i < end; i++)
            {
                var callback = callbacks[i];
                if (callback == null) continue;
                try { callback(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            ticking = -1;
            if (holes > 0) Compact();
        }

        private void Compact()
        {
            var write = 0;
            for (var read = 0; read < count; read++)
            {
                var callback = callbacks[read];
                if (callback == null) continue;
                if (write != read)
                {
                    callbacks[write] = callback;
                    callbackSlots[write] = callbackSlots[read];
                    slotIndex[callbackSlots[read]] = write;
                }
                write++;
            }
            for (var i = write; i < count; i++) callbacks[i] = null;
            count = write;
            holes = 0;
        }
    }
}
