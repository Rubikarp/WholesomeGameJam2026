using System;
using System.Reflection;

namespace LightSide
{
    /// <summary>
    /// Per-thread managed-allocation probe for the profiler. Resolves <c>GC.GetAllocatedBytesForCurrentThread</c>
    /// by reflection (its Unity support is version-dependent, and reflection avoids a hard compile dependency),
    /// then verifies at runtime that the counter actually advances — if it never moves, allocation capture
    /// degrades to off (time only) rather than reporting garbage. The probe is a receptor: assign
    /// <see cref="Probe"/> to swap the source (e.g. a native counter) without touching the sink.
    /// </summary>
    public static class ProfAlloc
    {
        /// <summary>Returns cumulative managed bytes allocated on the current thread. Replace to swap the source.</summary>
        public static Func<long> Probe = ResolveDefault();

        /// <summary>True once <see cref="Verify"/> has seen the probe advance on a real allocation.</summary>
        public static bool Available { get; private set; }

        private static Func<long> ResolveDefault()
        {
            var m = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (m != null && m.ReturnType == typeof(long))
            {
                try { return (Func<long>)m.CreateDelegate(typeof(Func<long>)); }
                catch { return static () => 0L; }
            }
            return static () => 0L;
        }

        /// <summary>Current probe value, or 0 when the probe is unavailable.</summary>
        public static long Bytes() => Available ? Probe() : 0;

        /// <summary>
        /// Confirms the probe advances across a known allocation. Call once before a capture; sets
        /// <see cref="Available"/>. Re-runs the check each time. The touched byte keeps the optimizer from
        /// eliminating the probe allocation as a dead store.
        /// </summary>
        public static bool Verify()
        {
            long before;
            try { before = Probe(); } catch { return Available = false; }
            var sink = new byte[4096];
            sink[Environment.TickCount & 4095] = 1;
            long after;
            try { after = Probe(); } catch { return Available = false; }
            return Available = after > before;
        }
    }
}
