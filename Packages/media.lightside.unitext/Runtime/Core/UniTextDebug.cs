using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine.Profiling;

namespace LightSide
{
    /// <summary>
    /// UniText's zone markers and debug counters. <see cref="BeginSample"/>/<see cref="EndSample"/> feed the
    /// LightSide profiler (<see cref="Prof"/> — captured by the benchmarks) and
    /// optionally forward to Unity Profiler; both compile out entirely without <c>UNITEXT_PROFILE</c>.
    /// </summary>
    /// <remarks>
    /// Pool diagnostics span two defines: <c>LIGHTSIDE_POOL_DEBUG</c> gates the Core pool counters
    /// (<see cref="PoolStats"/>), <c>UNITEXT_POOL_DEBUG</c> gates the domain counters and report here —
    /// a benchmark session wanting the full picture sets both.
    /// </remarks>
    internal static class UniTextDebug
    {
        [ThreadStatic] private static int sampleDepth;

        /// <summary>Gates the Unity Profiler forward only — <see cref="Prof"/> zones are governed by <c>Prof.Capturing</c> instead.</summary>
        public static bool Enabled;

        /// <summary>Opt-in forward to Unity Profiler for timeline eyeballing. Off by default: the forward costs a marker lookup per zone even unattached, which would pollute benchmark windows.</summary>
        public static bool ProfilerEnabled;

        /// <summary>Enable performance counters.</summary>
        public static bool CountersEnabled = true;

        /// <summary>Opens a named zone. Near-free when no capture is running (one volatile read). Worker-thread zones land in per-thread logs; the Unity forward is main-thread only.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("UNITEXT_PROFILE")]
        public static void BeginSample(string name)
        {
            if (Enabled && ProfilerEnabled) Profiler.BeginSample(name);
            Prof.Enter(name);
            sampleDepth++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("UNITEXT_PROFILE")]
        public static void EndSample()
        {
            if (sampleDepth > 0) sampleDepth--;
            Prof.Exit();
            if (Enabled && ProfilerEnabled) Profiler.EndSample();
        }

        internal static int SampleDepth => sampleDepth;

        internal static void RestoreSampleDepth(int depth)
        {
            while (sampleDepth > depth)
                EndSample();
        }


        #region Counters

        public static int TextProcessor_ProcessCount;
        public static int TextProcessor_EnsureShapingCount;
        public static int TextProcessor_DoFullShapingCount;

        public static int Buffers_InstanceCount;
        public static int Buffers_RentCount;

        public static int Bidi_ProcessCount;
        public static int Bidi_BuildIsoRunSeqCount;

        public static int SystemFont_RequestHitCount;
        public static int SystemFont_DeferredRequestCount;
        public static int SystemFont_PlatformBatchCount;
        public static int SystemFont_PlatformSingleCount;
        public static int SystemFont_SourceHitCount;
        public static int SystemFont_SourceLoadCount;

        #endregion


        #region Counter Wrappers

        /// <summary>Thread-safely increments a counter if debug and counters are enabled.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("UNITEXT_POOL_DEBUG")]
        public static void Increment(ref int counter)
        {
            if (Enabled && CountersEnabled)
                Interlocked.Increment(ref counter);
        }

        /// <summary>Thread-safely updates a counter to track the maximum value seen.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("UNITEXT_POOL_DEBUG")]
        public static void TrackLargest(ref int current, int value)
        {
            if (!Enabled || !CountersEnabled) return;

            int snapshot;
            do
            {
                snapshot = current;
                if (value <= snapshot) return;
            } while (Interlocked.CompareExchange(ref current, value, snapshot) != snapshot);
        }

        #endregion


        #region Reset & Reporting

        /// <summary>Resets all performance counters to zero.</summary>
        [Conditional("UNITEXT_POOL_DEBUG")]
        public static void ResetAllCounters()
        {
            TextProcessor_ProcessCount = 0;
            TextProcessor_EnsureShapingCount = 0;
            TextProcessor_DoFullShapingCount = 0;

            Buffers_InstanceCount = 0;
            Buffers_RentCount = 0;

            Bidi_ProcessCount = 0;
            Bidi_BuildIsoRunSeqCount = 0;

            SystemFont_RequestHitCount = 0;
            SystemFont_DeferredRequestCount = 0;
            SystemFont_PlatformBatchCount = 0;
            SystemFont_PlatformSingleCount = 0;
            SystemFont_SourceHitCount = 0;
            SystemFont_SourceLoadCount = 0;
        }

        /// <summary>Generates a formatted performance report with all counter values.</summary>
        public static string GetReport()
        {
            var systemFontMemory = SystemFont.MemoryStats;
            var variationMemory = UniTextFontProvider.VariationMemoryStats;
            return $@"=== UniText Debug Report ===

    TextProcessor:
      Process calls: {TextProcessor_ProcessCount}
      EnsureShaping calls: {TextProcessor_EnsureShapingCount}
      DoFullShaping calls: {TextProcessor_DoFullShapingCount}

    Buffers:
      Instances: {Buffers_InstanceCount}
      Rent calls: {Buffers_RentCount}

    BidiEngine:
      Process calls: {Bidi_ProcessCount}
      BuildIsoRunSeq calls: {Bidi_BuildIsoRunSeqCount}

    SystemFont:
      Request hits: {SystemFont_RequestHitCount}
      Deferred requests: {SystemFont_DeferredRequestCount}
      Platform batches: {SystemFont_PlatformBatchCount}
      Platform single resolves: {SystemFont_PlatformSingleCount}
      Source hits: {SystemFont_SourceHitCount}
      Source loads: {SystemFont_SourceLoadCount}
      Sources: {systemFontMemory.SourceCount} ({systemFontMemory.ActiveSourceCount} active)
      Requests: {systemFontMemory.RequestCount}
      Retained files: {systemFontMemory.ByteEntryCount} ({systemFontMemory.RetainedByteCount} bytes)
      Variations: {variationMemory.RegistrationCount} ({variationMemory.ActiveRegistrationCount} active)
    ";
        }

        /// <summary>Logs the performance report to Unity console via Debug.Log.</summary>
        [Conditional("UNITEXT_POOL_DEBUG")]
        public static void LogReport()
        {
            UnityEngine.Debug.Log(GetReport());
        }

        #endregion
    }

}
