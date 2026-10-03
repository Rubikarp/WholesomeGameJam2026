using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using ThreadPriority = System.Threading.ThreadPriority;

namespace LightSide
{
    /// <summary>
    /// Thread pool for index-based parallel work. <see cref="Execute"/> hands <c>[0, count)</c> to a
    /// pool of persistent workers plus the calling thread, which all pull the next index off a shared
    /// counter until the range drains — self-balancing, with no size threshold to tune per device.
    /// </summary>
    /// <remarks>
    /// Not reentrant: one <see cref="Execute"/> at a time, from the main thread. A nested call (a user
    /// callback fired inside an item re-entering) runs its range inline on the calling thread instead.
    /// </remarks>
    public static class WorkerPool
    {
        private static readonly int ThreadCount = Math.Max(1, Environment.ProcessorCount - 1);
        private const int JoinTimeoutMs = 5000;
        private static readonly CatZone zone = Cat.Zone("WorkerPool");

        /// <summary>
        /// One dispatch's mutable scheduling state, allocated fresh per <see cref="Execute"/> and never
        /// mutated after publish. A straggler worker from a prior dispatch keeps draining its own (spent)
        /// object and grabs nothing from the next — this object identity is what dissolves the stale-grab
        /// race, so it must not be pooled or reused in place.
        /// </summary>
        private sealed class Dispatch
        {
            public Action<int> action;
            public int count;
            public int nextIndex;
            public int remaining;
            public Exception firstException;
        }

        private static Thread[] workers;
        private static AutoResetEvent[] wake;
        private static ManualResetEventSlim done;

        private static Dispatch current;
        private static int epoch;
        private static bool isShuttingDown;
        private static int inExecute;
        private static bool isInitialized;

    #if UNITY_EDITOR
        static WorkerPool()
        {
            EditorLifecycle.ManagedCleaning += Shutdown;
        }
    #endif

        /// <summary>Returns true if parallel processing is supported (more than 1 CPU core, not WebGL).</summary>
        public static bool IsParallelSupported =>
#if UNITY_WEBGL && !UNITY_EDITOR
            false;
#else
            Environment.ProcessorCount > 1;
#endif

        /// <summary>Ensures the worker pool is initialized.</summary>
        public static void EnsureInitialized()
        {
            if (isInitialized || Volatile.Read(ref isShuttingDown)) return;

            lock (typeof(WorkerPool))
            {
                if (isInitialized || Volatile.Read(ref isShuttingDown)) return;

                workers = new Thread[ThreadCount];
                wake = new AutoResetEvent[ThreadCount];
                done = new ManualResetEventSlim(false);

                for (var i = 0; i < ThreadCount; i++)
                {
                    wake[i] = new AutoResetEvent(false);
                    var threadIdx = i;
                    workers[i] = new Thread(() => WorkerLoop(threadIdx))
                    {
                        IsBackground = true,
                        Name = $"LightSideWorker_{i}",
                        Priority = ThreadPriority.Normal
                    };
                    workers[i].Start();
                }

                isInitialized = true;

                zone.MeowFormat("[WorkerPool] Initialized with {0} threads", ThreadCount);
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> for every index in <c>[0, count)</c>. Workers and the calling
        /// thread pull indices off a shared counter until the range drains; the caller blocks until the
        /// last item ran. The first item exception is rethrown on the caller after every claimed item
        /// has left the dispatch, preserving its original stack trace.
        /// </summary>
        public static void Execute(int count, Action<int> action)
        {
            if (count <= 0) return;

            if (count == 1 || !IsParallelSupported || Volatile.Read(ref isShuttingDown))
            {
                for (var i = 0; i < count; i++) action(i);
                return;
            }

            if (Interlocked.CompareExchange(ref inExecute, 1, 0) != 0)
            {
                for (var i = 0; i < count; i++) action(i);
                return;
            }

            try
            {
                EnsureInitialized();
                if (!isInitialized || Volatile.Read(ref isShuttingDown))
                {
                    for (var i = 0; i < count; i++) action(i);
                    return;
                }

                var d = new Dispatch { action = action, count = count, remaining = count };

                done.Reset();
                Volatile.Write(ref current, d);
                Volatile.Write(ref epoch, unchecked(epoch + 1));

                var toWake = Math.Min(count - 1, ThreadCount);
                for (var i = 0; i < toWake; i++) wake[i].Set();

                RunGrabLoop(d);

                if (!done.Wait(JoinTimeoutMs))
                {
                    zone.MeowFormat("[WorkerPool] Join timed out with {0} item(s) unfinished", Volatile.Read(ref d.remaining));
                    done.Wait();
                }

                if (d.firstException != null)
                    ExceptionDispatchInfo.Capture(d.firstException).Throw();
            }
            finally
            {
                Volatile.Write(ref inExecute, 0);
            }
        }

        /// <summary>
        /// Shared grab loop, identical on workers and the submitter: claim the next index atomically,
        /// run it, and drive <see cref="Dispatch.remaining"/> down — the thread that lands it on zero
        /// signals the join. The per-item try/finally keeps the count exact on a throw so the join can
        /// never hang; the Interlocked full fence + the event's own barrier publish item writes to the
        /// waiter.
        /// </summary>
        private static void RunGrabLoop(Dispatch d)
        {
            while (true)
            {
                var i = Interlocked.Increment(ref d.nextIndex) - 1;
                if (i >= d.count) break;

                try
                {
                    d.action(i);
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref d.firstException, ex, null);
                }
                finally
                {
                    if (Interlocked.Decrement(ref d.remaining) == 0) done.Set();
                }
            }
        }

        private static void WorkerLoop(int idx)
        {
            var last = 0;
            while (!Volatile.Read(ref isShuttingDown))
            {
                var sw = new SpinWait();
                while (Volatile.Read(ref epoch) == last && !Volatile.Read(ref isShuttingDown))
                {
                    if (sw.NextSpinWillYield)
                    {
                        wake[idx].WaitOne();
                        break;
                    }
                    sw.SpinOnce();
                }

                if (Volatile.Read(ref isShuttingDown)) break;

                var e = Volatile.Read(ref epoch);
                if (e == last) continue;
                last = e;

                var d = Volatile.Read(ref current);
                if (d != null) RunGrabLoop(d);
            }
        }

        private static void Shutdown()
        {
            lock (typeof(WorkerPool))
            {
                var spin = new SpinWait();
                while (Volatile.Read(ref inExecute) != 0) spin.SpinOnce();

                Volatile.Write(ref isShuttingDown, true);
                if (!isInitialized) return;

                WakeAllForShutdown();

                for (var i = 0; i < workers.Length; i++) workers[i].Join();
                for (var i = 0; i < wake.Length; i++) wake[i].Dispose();
                done.Dispose();

                workers = null;
                wake = null;
                done = null;
                current = null;
                epoch = 0;
                isInitialized = false;
            }
        }

        /// <summary>Wakes both parked and spinning workers so shutdown's <see cref="isShuttingDown"/> flag is observed promptly.</summary>
        private static void WakeAllForShutdown()
        {
            Volatile.Write(ref epoch, unchecked(epoch + 1));
            if (wake != null)
                for (var i = 0; i < wake.Length; i++)
                    wake[i]?.Set();
        }
    }
}
