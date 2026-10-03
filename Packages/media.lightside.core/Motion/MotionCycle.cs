using UnityEngine;

namespace LightSide
{
    /// <summary>What each cycle after the first does.</summary>
    /// <remarks>The values are a serialization contract and never change; the declaration order is the order the
    /// inspector dropdown lists them in.</remarks>
    public enum MotionCycle : byte
    {
        /// <summary>Jumps back to the start value and plays forward again.</summary>
        Restart = 0,

        /// <summary>
        /// Runs the whole cycle backwards, so the value retraces its exact path — the same shape read right to
        /// left. Matches <see cref="Mathf.PingPong"/> and CSS <c>animation-direction: alternate</c>.
        /// </summary>
        PingPong = 3,

        /// <summary>
        /// Returns to the start value, but eases the return from its own beginning, so a curve that starts fast
        /// starts fast both ways.
        /// </summary>
        Yoyo = 1,

        /// <summary>Adds the previous cycle's travel to the start value, so the value keeps advancing.</summary>
        Incremental = 2,
    }

    /// <summary>How many times a timeline repeats: the count that never ends, and the rule every authored
    /// count passes through.</summary>
    public static class MotionCycles
    {
        /// <summary>Cycle count that repeats until stopped.</summary>
        public const int Infinite = -1;

        /// <summary>
        /// Maps an authored count onto the counts a timeline runs: zero stands for a single pass, every
        /// negative count collapses onto <see cref="Infinite"/>, and any other count stands for itself.
        /// </summary>
        public static int Normalize(int count) => count == 0 ? 1 : (count < 0 ? Infinite : count);
    }
}
