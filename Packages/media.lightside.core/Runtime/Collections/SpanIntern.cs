using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Per-thread span→string intern cache for hot parse paths (tag parameters, paint swatch names,
    /// URLs, trigger words, merged link parameters). Repeated content resolves to the same cached
    /// instance, so steady-state parsing of unchanged text allocates no strings. FNV-1a keyed; a hash
    /// collision falls back to a fresh allocation that replaces the slot — correctness never depends
    /// on the cache.
    /// </summary>
    /// <remarks>
    /// Bounded by a generation swap: when the hot table fills past <see cref="MaxEntries"/>, it
    /// becomes the cold table and the previous cold table is dropped wholesale. Live tokens keep
    /// hitting through the cold probe (and are re-promoted to hot on access); tokens unseen for a
    /// full generation are released. High-cardinality feeds (distinct URLs, mention words in a
    /// long-running chat) therefore cannot grow the cache without bound — misses are tolerated by
    /// design.
    /// </remarks>
    public static class SpanIntern
    {
        private const int MaxEntries = 2048;

        [ThreadStatic] private static FastIntDictionary<string> hot;
        [ThreadStatic] private static FastIntDictionary<string> cold;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Get(ReadOnlySpan<char> span)
        {
            if (span.IsEmpty) return string.Empty;

            var map = hot ??= new FastIntDictionary<string>(128);
            var hash = ComputeHash(span);

            if (map.TryGetValue(hash, out var cached) && Matches(cached, span))
                return cached;

            if (cold != null && cold.TryGetValue(hash, out cached) && Matches(cached, span))
            {
                Store(map, hash, cached);
                return cached;
            }

            var result = span.ToString();
            Store(map, hash, result);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Matches(string cached, ReadOnlySpan<char> span)
            => cached.Length == span.Length && span.SequenceEqual(cached.AsSpan());

        private static void Store(FastIntDictionary<string> map, int hash, string value)
        {
            if (map.Count >= MaxEntries)
            {
                var drained = cold;
                cold = map;
                if (drained != null)
                {
                    drained.Clear();
                    map = drained;
                }
                else
                {
                    map = new FastIntDictionary<string>(128);
                }
                hot = map;
            }
            map[hash] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeHash(ReadOnlySpan<char> span)
        {
            unchecked
            {
                var hash = -2128831035;
                for (var i = 0; i < span.Length; i++)
                {
                    hash ^= span[i];
                    hash *= 16777619;
                }

                return hash == -1 ? 1 : hash;
            }
        }
    }
}
