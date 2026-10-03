using System.Collections.Generic;
using Unity.Profiling;

namespace LightSide
{
    /// <summary>
    /// Samples Unity's built-in profiler counters (memory + rendering) into per-frame series and attaches them
    /// to a capture as counter tracks. Unlike markers, these counters read in RELEASE players too — gated by
    /// <see cref="ProfilerRecorder.Valid"/> so unavailable stats are simply skipped. Drive <see cref="Sample"/>
    /// once per frame (alongside <see cref="Prof.SampleFrame"/>) so the series align with the frame timeline.
    /// </summary>
    public static class ProfCounters
    {
        private sealed class Rec
        {
            public string name;
            public ProfilerRecorder recorder;
            public readonly List<long> series = new();
        }

        private static readonly (ProfilerCategory category, string name)[] Defaults =
        {
            (ProfilerCategory.Memory, "GC Allocated In Frame"),
            (ProfilerCategory.Memory, "Total Used Memory"),
            (ProfilerCategory.Memory, "Total Reserved Memory"),
            (ProfilerCategory.Memory, "GC Used Memory"),
            (ProfilerCategory.Memory, "GC Reserved Memory"),
            (ProfilerCategory.Memory, "System Used Memory"),
            (ProfilerCategory.Render, "Draw Calls Count"),
            (ProfilerCategory.Render, "SetPass Calls Count"),
            (ProfilerCategory.Render, "Vertices Count"),
            (ProfilerCategory.Render, "Used Buffers Bytes"),
        };

        private static readonly List<Rec> recs = new();

        public static bool Armed { get; private set; }

        /// <summary>Starts recorders for every available default counter (skips ones the current player doesn't expose). Discards any previous series.</summary>
        public static void Arm()
        {
            Clear();
            foreach (var d in Defaults)
            {
                var r = ProfilerRecorder.StartNew(d.category, d.name);
                if (r.Valid) recs.Add(new Rec { name = d.name, recorder = r });
                else r.Dispose();
            }
            Armed = true;
        }

        /// <summary>Appends this frame's value for each live counter. Cheap — call once per frame during capture.</summary>
        public static void Sample()
        {
            if (!Armed) return;
            foreach (var r in recs) r.series.Add(r.recorder.CurrentValue);
        }

        /// <summary>Reads the current value of an available profiler counter without requiring an active capture.</summary>
        public static bool TryGetCurrentValue(ProfilerCategory category, string name, out long value)
        {
            if (Armed)
            {
                foreach (var r in recs)
                {
                    if (r.name != name) continue;
                    value = r.recorder.CurrentValue;
                    return true;
                }
            }

            using var recorder = ProfilerRecorder.StartNew(category, name);
            value = recorder.Valid ? recorder.CurrentValue : 0;
            return recorder.Valid;
        }

        /// <summary>Stops the recorders but keeps the sampled series for <see cref="AttachTo"/>.</summary>
        public static void Disarm()
        {
            if (!Armed) return;
            foreach (var r in recs) r.recorder.Dispose();
            Armed = false;
        }

        /// <summary>Copies the sampled series onto <paramref name="capture"/> as counter tracks, exported as Perfetto counter tracks by <see cref="ProfCapture.ToChromeTrace"/>.</summary>
        public static void AttachTo(ProfCapture capture)
        {
            if (capture == null) return;
            foreach (var r in recs)
                if (r.series.Count > 0)
                    capture.counterTracks.Add(new ProfCapture.CounterTrack { name = r.name, values = new List<long>(r.series) });
        }

        /// <summary>Releases sampled series after they have been attached and exported.</summary>
        public static void Release() => Clear();

        private static void Clear()
        {
            if (Armed) Disarm();
            recs.Clear();
        }
    }
}
