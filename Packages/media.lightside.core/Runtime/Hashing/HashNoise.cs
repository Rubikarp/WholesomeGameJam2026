using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Deterministic noise for phase-driven modifiers: the same inputs always yield the same output,
    /// so "random" effects stay scrubbable, rewindable, and reproducible across machines.
    /// </summary>
    public static class HashNoise
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7FEB352Du;
            x ^= x >> 15; x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }

        /// <summary>Uniform value in [0, 1) from up to three integer inputs.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Hash01(int a, int b, int c = 0)
            => Hash((uint)a * 0x9E3779B9u ^ (uint)b * 0x85EBCA6Bu ^ (uint)c * 0xC2B2AE35u) * (1f / 4294967296f);

        /// <summary>Signed variant of <see cref="Hash01"/>: uniform value in [-1, 1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float HashSigned(int a, int b, int c = 0) => Hash01(a, b, c) * 2f - 1f;

        /// <summary>Smooth 1D value noise in [-1, 1]: hashes at integer steps of <paramref name="t"/>, smoothstep-blended between them.</summary>
        public static float ValueSigned(float t, int seed)
        {
            var i = Mathf.FloorToInt(t);
            var f = t - i;
            f = f * f * (3f - 2f * f);
            return Mathf.Lerp(HashSigned(i, seed), HashSigned(i + 1, seed), f);
        }
    }
}
