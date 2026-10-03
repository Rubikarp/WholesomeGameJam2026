using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LightSide
{
    /// <summary>
    /// The profiler sink: an <see cref="Enter"/>/<see cref="Exit"/> contract fed by manual <see cref="Scope"/>s.
    /// The hot path only appends a raw begin/end event with a timestamp into a per-thread log (single-writer,
    /// no locks, no inline tree). The
    /// call tree — and any real-timeline export — is reconstructed offline in <see cref="EndCapture"/>. Ticks are
    /// <see cref="Stopwatch"/> timestamps; allocation comes from the swappable <see cref="ProfAlloc"/> probe.
    /// Capture boundaries handshake with recording threads (<see cref="ThreadState.suppressed"/> + full fence)
    /// so Begin/EndCapture never read or reset a log mid-append; a thread that fails to quiesce within the spin
    /// budget is quarantined out of the capture instead of being read racily.
    /// </summary>
    public static class Prof
    {
        internal static readonly int RootId = ProfRegistry.Id("<root>");

        private static volatile bool capturing;
        private static bool sampleAllocActive;
        private static double overheadTicksPerCall;

        /// <summary>True between <see cref="BeginCapture"/> and <see cref="EndCapture"/>. Enter/Exit are near-free when false (one volatile read).</summary>
        public static bool Capturing => capturing;

        /// <summary>When false, Enter/Exit skip the allocation probe. The probe is a native call; per-method on a million-call hot path it dominates capture cost, so time-only captures are far cheaper. Latched at <see cref="BeginCapture"/> — mid-capture writes take effect on the next capture.</summary>
        public static bool SampleAlloc = true;

        [ThreadStatic] private static ThreadState ts;
        private static ConcurrentBag<ThreadState> threads = new();

        private static readonly List<long> frameTicks = new();
        private static readonly List<long> frameBytes = new();
        private static long frameBaseTicks, frameBaseBytes;

        /// <summary>Records one frame boundary during capture — wall time and allocation since the previous call. Drive it from the editor update loop (or a per-frame yield) to get a per-frame timeline. Bytes cover the calling thread only (per-thread probe); worker-thread allocation shows in the per-zone tree, not here.</summary>
        public static void SampleFrame()
        {
            if (!capturing) return;
            long now = Stopwatch.GetTimestamp();
            long bytes = ProfAlloc.Bytes() - (ts?.log.overheadBytes ?? 0);
            frameTicks.Add(now - frameBaseTicks);
            frameBytes.Add(bytes - frameBaseBytes);
            frameBaseTicks = now;
            frameBaseBytes = bytes;
        }

        internal sealed class ThreadState
        {
            public readonly Thread thread;
            public readonly int threadId;
            public readonly string threadName;
            public readonly ProfThreadLog log = new();
            public long[] counter = Array.Empty<long>();

            /// <summary>Set (with a full fence) while this thread mutates its log or counters. Capture boundaries spin on it before reading/resetting.</summary>
            public bool suppressed;

            /// <summary>Set at a capture boundary when the thread failed to quiesce — its log is neither reset nor read until a later boundary succeeds.</summary>
            public bool quarantined;

            public ThreadState(Thread t)
            {
                thread = t;
                threadId = t.ManagedThreadId;
                threadName = t.Name ?? ("Thread " + t.ManagedThreadId);
            }

            public void Reset()
            {
                log.Reset();
                if (counter.Length != 0) Array.Clear(counter, 0, counter.Length);
            }
        }

        /// <summary>Offline reconstruction node — the working representation the tree is rebuilt into from the event log; never touched on the hot path.</summary>
        internal struct Node
        {
            public int id;
            public int parent, firstChild, nextSibling;
            public long calls;
            public long totalTicks, childTicks;
            public long totalAlloc, childAlloc, firstAlloc;
        }

        private static ThreadState Local()
        {
            var t = ts;
            if (t == null)
            {
                t = ts = new ThreadState(Thread.CurrentThread);
                threads.Add(t);
            }
            return t;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Enter(int id)
        {
            if (!capturing) return;
            var t = ts ?? Local();
            Volatile.Write(ref t.suppressed, true);
            Thread.MemoryBarrier();
            try
            {
                if (!capturing) return;
                long alloc = sampleAllocActive ? ProfAlloc.Bytes() - t.log.overheadBytes : 0;
                long now = Stopwatch.GetTimestamp();
                t.log.Append(now, alloc, id, ProfEvType.ZoneBegin);
            }
            finally { Volatile.Write(ref t.suppressed, false); }
        }

        /// <summary>Enter by name, resolved to its runtime id. First-sight registration allocations are booked as profiler overhead, not to the open zone.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Enter(string name)
        {
            if (!capturing) return;
            if (!ProfRegistry.TryGet(name, out var id))
            {
                var t = ts ?? Local();
                long before = ProfAlloc.Bytes();
                id = ProfRegistry.Id(name);
                t.log.overheadBytes += ProfAlloc.Bytes() - before;
            }
            Enter(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Exit()
        {
            if (!capturing) return;
            long now = Stopwatch.GetTimestamp();
            var t = ts;
            if (t == null) return;
            Volatile.Write(ref t.suppressed, true);
            Thread.MemoryBarrier();
            try
            {
                if (!capturing) return;
                long alloc = sampleAllocActive ? ProfAlloc.Bytes() - t.log.overheadBytes : 0;
                t.log.Append(now, alloc, 0, ProfEvType.ZoneEnd);
            }
            finally { Volatile.Write(ref t.suppressed, false); }
        }

        /// <summary>Adds <paramref name="value"/> to a named counter for this thread (e.g. glyphs rasterized, bytes uploaded).</summary>
        public static void Count(int id, long value = 1)
        {
            if (!capturing) return;
            var t = ts ?? Local();
            Volatile.Write(ref t.suppressed, true);
            Thread.MemoryBarrier();
            try
            {
                if (!capturing) return;
                if (id >= t.counter.Length)
                {
                    int n = t.counter.Length == 0 ? 16 : t.counter.Length;
                    while (id >= n) n *= 2;
                    long before = ProfAlloc.Bytes();
                    Array.Resize(ref t.counter, n);
                    t.log.overheadBytes += ProfAlloc.Bytes() - before;
                }
                t.counter[id] += value;
            }
            finally { Volatile.Write(ref t.suppressed, false); }
        }

        /// <summary>Manual scope: <c>using (Prof.Scope(id))</c>. Cache the id via <see cref="ProfRegistry.Id"/> for hot paths.</summary>
        public static Handle Scope(int id) { Enter(id); return default; }

        /// <summary>Manual scope by name (registers on first use — avoid in hot loops; cache the id instead).</summary>
        public static Handle Scope(string name) { Enter(name); return default; }

        public readonly struct Handle : IDisposable
        {
            public void Dispose() => Exit();
        }

        /// <summary>Clears every thread's log and starts recording. Verifies the allocation probe, waits out in-flight appends from a previous capture (quarantining threads that never quiesce), prunes states of dead threads, and calibrates per-call overhead for the reconstruction to subtract.</summary>
        public static void BeginCapture()
        {
            capturing = false;
            Thread.MemoryBarrier();
            foreach (var t in threads) t.quarantined = !Quiesce(t);
            PruneDeadThreads();
            ProfAlloc.Verify();
            sampleAllocActive = SampleAlloc && ProfAlloc.Available;
            overheadTicksPerCall = Calibrate();
            Thread.MemoryBarrier();
            foreach (var t in threads) if (!t.quarantined && !Quiesce(t)) t.quarantined = true;
            foreach (var t in threads) if (!t.quarantined) t.Reset();
            frameTicks.Clear();
            frameBytes.Clear();
            frameBaseTicks = Stopwatch.GetTimestamp();
            frameBaseBytes = ProfAlloc.Bytes() - (ts?.log.overheadBytes ?? 0);
            capturing = true;
        }

        /// <summary>Releases pooled capture capacity after export; call only when no capture is active.</summary>
        public static void ReleaseCaptureBuffers()
        {
            if (capturing) return;
            Thread.MemoryBarrier();
            foreach (var t in threads)
            {
                if (!Quiesce(t)) continue;
                t.log.ReleaseBlocks();
                t.counter = Array.Empty<long>();
            }
            frameTicks.Clear();
            frameTicks.TrimExcess();
            frameBytes.Clear();
            frameBytes.TrimExcess();
        }

        /// <summary>Stops recording, waits out in-flight appends, and returns the capture (per-thread trees reconstructed from the event logs + counters + names). Threads that fail to quiesce are excluded rather than read racily.</summary>
        public static ProfCapture EndCapture()
        {
            capturing = false;
            Thread.MemoryBarrier();
            var live = new List<ThreadState>();
            foreach (var t in threads)
            {
                if (!Quiesce(t)) t.quarantined = true;
                if (!t.quarantined && (t.log.Count > 0 || HasCounters(t))) live.Add(t);
            }
            return ProfCapture.Build(live, ProfRegistry.Snapshot(), ProfAlloc.Available, frameTicks, frameBytes, overheadTicksPerCall);
        }

        private static bool Quiesce(ThreadState t) =>
            !Volatile.Read(ref t.suppressed) || SpinWait.SpinUntil(() => !Volatile.Read(ref t.suppressed), 100);

        private static void PruneDeadThreads()
        {
            bool anyDead = false;
            foreach (var t in threads) if (!t.thread.IsAlive) { anyDead = true; break; }
            if (!anyDead) return;
            var keep = new ConcurrentBag<ThreadState>();
            foreach (var t in threads) if (t.thread.IsAlive) keep.Add(t);
            threads = keep;
        }

        /// <summary>Measures what one instrumented call costs its PARENT zone (probe + timestamp + append outside the child's own window) so the reconstruction can subtract it — otherwise dense woven trees misrank parents of hot small methods. Minimum over batches, so a preemption or GC pause inflates one batch, not the constant. Runs on this thread's log; the events are wiped by the Reset that follows.</summary>
        private static double Calibrate()
        {
            var t = ts ?? Local();
            t.Reset();
            capturing = true;
            for (int i = 0; i < 8; i++) { Enter("<calibration>"); Exit(); }

            double best = double.MaxValue;
            for (int b = 0; b < 4; b++)
            {
                long mark = t.log.Count;
                long w0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < 16; i++) { Enter("<calibration>"); Exit(); }
                long w1 = Stopwatch.GetTimestamp();

                long inner = 0;
                int pairs = 0;
                for (long i = mark; i + 1 < t.log.Count; i += 2)
                {
                    inner += t.log.At(i + 1).ticks - t.log.At(i).ticks;
                    pairs++;
                }
                if (pairs > 0)
                {
                    double a = (w1 - w0 - inner) / (double)pairs;
                    if (a < best) best = a;
                }
            }
            capturing = false;
            return best > 0 && best < double.MaxValue ? best : 0;
        }

        private static bool HasCounters(ThreadState t)
        {
            for (int i = 0; i < t.counter.Length; i++) if (t.counter[i] != 0) return true;
            return false;
        }
    }
}
