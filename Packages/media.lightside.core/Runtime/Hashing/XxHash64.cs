using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LightSide
{
    /// <summary>
    /// Allocation-free xxHash64 over raw spans — a fast non-cryptographic 64-bit content fingerprint.
    /// </summary>
    public static class XxHash64
    {
        private const ulong Prime1 = 11400714785074694791UL;
        private const ulong Prime2 = 14029467366897019727UL;
        private const ulong Prime3 = 1609587929392839161UL;
        private const ulong Prime4 = 9650029242287828579UL;
        private const ulong Prime5 = 2870177450012600261UL;

        /// <summary>Hashes the raw bytes of an unmanaged-element span.</summary>
        public static ulong Hash<T>(ReadOnlySpan<T> span, ulong seed) where T : unmanaged
            => HashBytes(MemoryMarshal.AsBytes(span), seed);

        /// <summary>Mixes a 64-bit value into an existing hash.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Combine(ulong hash, ulong value)
        {
            hash ^= Round(0, value);
            return RotL(hash, 27) * Prime1 + Prime4;
        }

        /// <summary>Computes the xxHash64 of a byte span with the given seed.</summary>
        public static ulong HashBytes(ReadOnlySpan<byte> data, ulong seed)
        {
            var len = data.Length;
            ulong h;
            var i = 0;

            if (len >= 32)
            {
                var v1 = seed + Prime1 + Prime2;
                var v2 = seed + Prime2;
                var v3 = seed;
                var v4 = seed - Prime1;

                for (; i <= len - 32; i += 32)
                {
                    v1 = Round(v1, ReadU64(data, i));
                    v2 = Round(v2, ReadU64(data, i + 8));
                    v3 = Round(v3, ReadU64(data, i + 16));
                    v4 = Round(v4, ReadU64(data, i + 24));
                }

                h = RotL(v1, 1) + RotL(v2, 7) + RotL(v3, 12) + RotL(v4, 18);
                h = MergeRound(h, v1);
                h = MergeRound(h, v2);
                h = MergeRound(h, v3);
                h = MergeRound(h, v4);
            }
            else
            {
                h = seed + Prime5;
            }

            h += (ulong)len;

            for (; i <= len - 8; i += 8)
            {
                h ^= Round(0, ReadU64(data, i));
                h = RotL(h, 27) * Prime1 + Prime4;
            }

            if (i <= len - 4)
            {
                h ^= ReadU32(data, i) * Prime1;
                h = RotL(h, 23) * Prime2 + Prime3;
                i += 4;
            }

            for (; i < len; i++)
            {
                h ^= data[i] * Prime5;
                h = RotL(h, 11) * Prime1;
            }

            h ^= h >> 33;
            h *= Prime2;
            h ^= h >> 29;
            h *= Prime3;
            h ^= h >> 32;
            return h;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Round(ulong acc, ulong input) => RotL(acc + input * Prime2, 31) * Prime1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MergeRound(ulong acc, ulong val)
        {
            acc ^= Round(0, val);
            return acc * Prime1 + Prime4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotL(ulong x, int r) => (x << r) | (x >> (64 - r));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ReadU64(ReadOnlySpan<byte> data, int offset)
            => MemoryMarshal.Read<ulong>(data.Slice(offset, 8));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ReadU32(ReadOnlySpan<byte> data, int offset)
            => MemoryMarshal.Read<uint>(data.Slice(offset, 4));
    }
}
