using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    internal struct OrderedEventEntry<TDelegate> where TDelegate : Delegate
    {
        public TDelegate callback;
        public int order;
    }

    internal struct OrderedEventStorage<TDelegate> where TDelegate : Delegate
    {
        private OrderedEventEntry<TDelegate>[] entries;

        public bool HasSubscribers => entries != null;

        public void Subscribe(TDelegate callback, int order)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            var current = entries;
            var count = current?.Length ?? 0;
            for (var i = 0; i < count; i++)
                if (current[i].callback.Equals(callback))
                    throw new InvalidOperationException("Callback is already subscribed.");

            var index = FindInsertionIndex(current, count, order);
            var next = new OrderedEventEntry<TDelegate>[count + 1];
            if (index != 0)
                Array.Copy(current, 0, next, 0, index);
            next[index] = new OrderedEventEntry<TDelegate> { callback = callback, order = order };
            if (index != count)
                Array.Copy(current, index, next, index + 1, count - index);
            entries = next;
        }

        public void Unsubscribe(TDelegate callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            var current = entries;
            var count = current?.Length ?? 0;
            for (var i = 0; i < count; i++)
            {
                if (!current[i].callback.Equals(callback)) continue;

                if (count == 1)
                {
                    entries = null;
                    return;
                }

                var next = new OrderedEventEntry<TDelegate>[count - 1];
                if (i != 0)
                    Array.Copy(current, 0, next, 0, i);
                if (i != count - 1)
                    Array.Copy(current, i + 1, next, i, count - i - 1);
                entries = next;
                return;
            }

            throw new InvalidOperationException("Callback is not subscribed.");
        }

        public ReadOnlySpan<OrderedEventEntry<TDelegate>> Snapshot => entries;

        public void Release() => entries = null;

        private static int FindInsertionIndex(OrderedEventEntry<TDelegate>[] current, int count,
            int order)
        {
            var low = 0;
            var high = count;

            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (current[middle].order <= order)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }
    }

    /// <summary>Invokes callbacks by ascending order over a stable per-invocation snapshot.</summary>
    /// <remarks>Equal orders stay stable; mutations affect later invocations and must be serialized.</remarks>
    public sealed class OrderedEvent
    {
        private OrderedEventStorage<Action> storage;

        internal bool HasSubscribers => storage.HasSubscribers;

        /// <summary>Subscribes a callback; lower orders run first and null or duplicates are rejected.</summary>
        public void Subscribe(Action callback, int order = 0) => storage.Subscribe(callback, order);

        /// <summary>Unsubscribes a callback; null and unknown callbacks are rejected.</summary>
        public void Unsubscribe(Action callback) => storage.Unsubscribe(callback);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Invoke()
        {
            var entries = storage.Snapshot;
            for (var i = 0; i < entries.Length; i++)
                entries[i].callback();
        }

        internal void Release() => storage.Release();
    }

    /// <summary>Invokes one-argument callbacks by ascending order over a stable snapshot.</summary>
    /// <remarks>Equal orders stay stable; mutations affect later invocations and must be serialized.</remarks>
    public sealed class OrderedValueEvent<TArgument>
    {
        private OrderedEventStorage<Action<TArgument>> storage;

        internal bool HasSubscribers => storage.HasSubscribers;

        /// <summary>Subscribes a callback; lower orders run first and null or duplicates are rejected.</summary>
        public void Subscribe(Action<TArgument> callback, int order = 0)
            => storage.Subscribe(callback, order);

        /// <summary>Unsubscribes a callback; null and unknown callbacks are rejected.</summary>
        public void Unsubscribe(Action<TArgument> callback) => storage.Unsubscribe(callback);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Invoke(TArgument argument)
        {
            var entries = storage.Snapshot;
            for (var i = 0; i < entries.Length; i++)
                entries[i].callback(argument);
        }

        internal void Release() => storage.Release();
    }

    /// <summary>Handles an ordered event with mutable context borrowed for one invocation.</summary>
    public delegate void OrderedEventHandler<TContext>(ref TContext context);

    /// <summary>Invokes callbacks over one mutable context by ascending order and stable snapshot.</summary>
    /// <remarks>Equal orders stay stable; mutations affect later invocations and must be serialized.</remarks>
    public sealed class OrderedEvent<TContext>
    {
        private OrderedEventStorage<OrderedEventHandler<TContext>> storage;

        internal bool HasSubscribers => storage.HasSubscribers;

        /// <summary>Subscribes a callback; lower orders run first and null or duplicates are rejected.</summary>
        public void Subscribe(OrderedEventHandler<TContext> callback, int order = 0)
            => storage.Subscribe(callback, order);

        /// <summary>Unsubscribes a callback; null and unknown callbacks are rejected.</summary>
        public void Unsubscribe(OrderedEventHandler<TContext> callback) => storage.Unsubscribe(callback);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Invoke(ref TContext context)
        {
            var entries = storage.Snapshot;
            for (var i = 0; i < entries.Length; i++)
                entries[i].callback(ref context);
        }

        internal void Release() => storage.Release();
    }
}
