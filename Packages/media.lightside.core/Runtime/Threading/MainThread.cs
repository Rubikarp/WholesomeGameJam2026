using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace LightSide
{
    /// <summary>Main-thread identity and deferred dispatch through Unity's synchronization context.</summary>
    public static class MainThread
    {
        private static int threadId = -1;
        private static readonly object pendingGate = new();
        private static readonly Queue<Action> pending = new();
        private static SynchronizationContext context;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Capture()
        {
            var captured = SynchronizationContext.Current ?? throw new InvalidOperationException(
                "Unity did not install its main-thread synchronization context.");
            threadId = Thread.CurrentThread.ManagedThreadId;
            lock (pendingGate)
            {
                while (pending.Count != 0) captured.Post(Invoke, pending.Dequeue());
                Volatile.Write(ref context, captured);
            }
        }

        /// <summary>Whether the calling thread is the main thread. Generic types cannot host
        /// [RuntimeInitializeOnLoadMethod], so they read the captured identity from here.</summary>
        public static bool IsCurrent => Thread.CurrentThread.ManagedThreadId == threadId;

        /// <summary>Defers <paramref name="action"/> to the main thread. Calls made before Unity's
        /// context is captured remain queued until runtime initialization.</summary>
        public static void Post(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var captured = Volatile.Read(ref context);
            if (captured != null)
            {
                captured.Post(Invoke, action);
                return;
            }

            lock (pendingGate)
            {
                captured = Volatile.Read(ref context);
                if (captured == null)
                {
                    pending.Enqueue(action);
                    return;
                }
            }
            captured.Post(Invoke, action);
        }

        private static void Invoke(object state) => ((Action)state)();
    }
}
