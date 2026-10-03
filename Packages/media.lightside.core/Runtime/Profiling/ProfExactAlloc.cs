#if ENABLE_IL2CPP && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEngine.Scripting;
#endif

namespace LightSide
{
    /// <summary>
    /// Allocation source for IL2CPP players, where the default per-thread probe is a not-implemented icall
    /// (always 0). <see cref="Begin"/> points <see cref="ProfAlloc"/> at the GC used-size
    /// (<c>il2cpp_gc_get_used_size</c>) and disables automatic GC for the capture window, so the counter only
    /// grows and each zone's Enter/Exit delta is gross bytes; <see cref="End"/> restores both. Wrap a whole
    /// capture — <see cref="Begin"/> before BeginCapture, <see cref="End"/> after EndCapture; ending mid-capture
    /// silently zeroes the remaining alloc data (the restored per-thread probe reads 0 on IL2CPP). The counter is
    /// PROCESS-WIDE: with concurrent allocating threads, zone deltas absorb other threads' bytes — trust exact
    /// numbers only for single-threaded windows. Keep the window short — nothing is collected inside it (on WebGL
    /// the GC mode is unsupported and collections can still run between frames). On Mono and in the editor this
    /// is a no-op: the per-thread probe is already exact gross bytes there.
    /// </summary>
    public static class ProfExactAlloc
    {
#if ENABLE_IL2CPP && !UNITY_EDITOR
        private static bool active;
        private static Func<long> prevProbe;
        private static GarbageCollector.Mode prevMode;

        public static void Begin()
        {
            if (active) return;
            prevProbe = ProfAlloc.Probe;
            ProfAlloc.Probe = UsedSize;
            prevMode = GarbageCollector.GCMode;
            try { GarbageCollector.GCMode = GarbageCollector.Mode.Manual; } catch { }
            active = true;
        }

        public static void End()
        {
            if (!active) return;
            try { GarbageCollector.GCMode = prevMode; } catch { }
            ProfAlloc.Probe = prevProbe;
            active = false;
        }

        [DllImport("__Internal")] private static extern ulong il2cpp_gc_get_used_size();
        private static long UsedSize() => (long)il2cpp_gc_get_used_size();
#else
        public static void Begin() { }
        public static void End() { }
#endif
    }
}
