using System;
using System.Threading;

namespace LightSide
{
    /// <summary>
    /// Thread-safe cache for font-fallback lookup results to avoid repeated fallback searches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Performance:</b> Uses a fixed-size hash table with O(1) lookup. Thread-local storage
    /// avoids locking overhead.
    /// </para>
    /// <para>
    /// Call <see cref="InvalidateAll"/> when fonts are added or removed to ensure
    /// correct cache behavior.
    /// </para>
    /// </remarks>
    internal static class SharedFontCache
    {
        private readonly struct FontCacheEntry
        {
            public readonly int codepoint;
            public readonly int preferredFontId;
            public readonly int contextId;
            public readonly int styleKey;
            public readonly int explicitFontId;
            public readonly int resultFontId;
            public readonly int version;
            public readonly byte language;

            public FontCacheEntry(int codepoint, int preferredFontId, int contextId,
                int styleKey, int explicitFontId, int resultFontId, int version, byte language)
            {
                this.codepoint = codepoint;
                this.preferredFontId = preferredFontId;
                this.contextId = contextId;
                this.styleKey = styleKey;
                this.explicitFontId = explicitFontId;
                this.resultFontId = resultFontId;
                this.version = version;
                this.language = language;
            }
        }

        private const int CacheSize = 16384;
        private const int CacheMask = CacheSize - 1;
        [ThreadStatic] private static FontCacheEntry[] cache;
        private static int fontStateVersion;

        /// <summary>Global fallback generation. Bumps whenever fonts are added/removed or coverage changes, so downstream caches keyed on shaping results can drop stale entries wholesale.</summary>
        internal static int Version => Volatile.Read(ref fontStateVersion);

        private static void EnsureInitialized()
        {
            if (cache != null) return;
            cache = new FontCacheEntry[CacheSize];
        }

        /// <summary>Attempts to get a cached font lookup result for an exact resolution context.</summary>
        public static bool TryGet(int codepoint, int preferredFontId, int contextId,
            byte language, int styleKey, int explicitFontId, out int resultFontId)
        {
            EnsureInitialized();
            var index = GetIndex(codepoint, preferredFontId, contextId, language, styleKey, explicitFontId);
            ref var entry = ref cache[index];
            var version = Volatile.Read(ref fontStateVersion);

            if (entry.codepoint == codepoint &&
                entry.preferredFontId == preferredFontId &&
                entry.contextId == contextId &&
                entry.language == language &&
                entry.styleKey == styleKey &&
                entry.explicitFontId == explicitFontId &&
                entry.version == version &&
                version == Volatile.Read(ref fontStateVersion))
            {
                resultFontId = entry.resultFontId;
                return true;
            }

            resultFontId = -1;
            return false;
        }

        /// <summary>Caches a font lookup result for an exact resolution context.</summary>
        public static void Set(int codepoint, int preferredFontId, int contextId,
            byte language, int styleKey, int explicitFontId, int resultFontId)
        {
            EnsureInitialized();
            var index = GetIndex(codepoint, preferredFontId, contextId, language, styleKey, explicitFontId);
            cache[index] = new FontCacheEntry(codepoint, preferredFontId, contextId,
                styleKey, explicitFontId, resultFontId, Volatile.Read(ref fontStateVersion), language);
        }

        private static int GetIndex(int codepoint, int preferredFontId, int contextId,
            byte language, int styleKey, int explicitFontId)
        {
            unchecked
            {
                var hash = codepoint;
                hash = (hash * 397) ^ preferredFontId;
                hash = (hash * 397) ^ contextId;
                hash = (hash * 397) ^ language;
                hash = (hash * 397) ^ styleKey;
                hash = (hash * 397) ^ explicitFontId;
                return hash & CacheMask;
            }
        }

        /// <summary>
        /// Invalidates all cached entries by incrementing the version number.
        /// </summary>
        /// <remarks>
        /// Call this method when fonts are added, removed, or their glyph coverage changes.
        /// </remarks>
        public static void InvalidateAll()
        {
            Interlocked.Increment(ref fontStateVersion);
        }

        /// <summary>
        /// Clears all cached entries.
        /// </summary>
        public static void Clear()
        {
            if (cache != null)
                for (var i = 0; i < CacheSize; i++)
                    cache[i] = default;
            InvalidateAll();
        }
    }
}
