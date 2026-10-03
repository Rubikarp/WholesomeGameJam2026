using System.Diagnostics;

namespace LightSide
{
    /// <summary>
    /// Zero-cost phased stopwatch. <see cref="Mark"/> is
    /// <c>[Conditional("LIGHTSIDE_DEBUG")]</c> — stripped in release.
    /// <see cref="Phase"/> and <see cref="Total"/> always compile but return 0 when no marks recorded.
    /// Use with <see cref="Cat.Meow"/> which also strips — no <c>#if</c> needed at call sites.
    /// </summary>
    public unsafe struct DebugTimer
    {
        private const int MaxMarks = 8;
        private fixed long marks[MaxMarks];
        private int count;

        /// <summary>Records a timestamp. First call = start, subsequent calls = phase boundaries.</summary>
        [Conditional("LIGHTSIDE_DEBUG")]
        public void Mark()
        {
            if (count < MaxMarks)
                marks[count++] = Stopwatch.GetTimestamp();
        }

        /// <summary>Returns the duration of phase <paramref name="index"/> in milliseconds.
        /// Phase 0 = Mark(0)→Mark(1), last phase = last mark→now.</summary>
        public double Phase(int index)
        {
            if ((uint)index >= (uint)count) return 0;
            long from = marks[index];
            long to = index + 1 < count ? marks[index + 1] : Stopwatch.GetTimestamp();
            return (to - from) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Returns total elapsed time from first <see cref="Mark"/> to now.</summary>
        public double Total => count < 1 ? 0 : (Stopwatch.GetTimestamp() - marks[0]) * 1000.0 / Stopwatch.Frequency;
    }
}
