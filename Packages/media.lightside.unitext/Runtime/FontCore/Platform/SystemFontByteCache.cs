using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Process-wide cache of system-font sources keyed by a path or opaque platform backing identity, so a font the OS
    /// matches for several clusters, styles, or faces of one collection is materialized once and the same source
    /// is shared by every companion built from it. A per-key load lock collapses concurrent first-touch access
    /// to the same file into one materialization (parallel layout can hit one .ttc from several workers at once).
    /// Inactive entries are budgeted by <see cref="SystemFont.InactiveByteBudget"/> and all entries are cleared by <see cref="SystemFont.Reset"/>.
    /// </summary>
    internal static class SystemFontByteCache
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static readonly StringComparer keyComparer = StringComparer.OrdinalIgnoreCase;
#else
        private static readonly StringComparer keyComparer = StringComparer.Ordinal;
#endif
        private sealed class Entry
        {
            internal FontSource source;
            internal int references;
            internal long lastUse;
        }

        private sealed class LoadGate
        {
            internal readonly object sync = new();
            internal int users;
        }

        internal readonly struct Stats
        {
            internal readonly int entryCount;
            internal readonly long byteCount;

            internal Stats(int entryCount, long byteCount)
            {
                this.entryCount = entryCount;
                this.byteCount = byteCount;
            }
        }

        private static readonly Dictionary<string, Entry> cache = new(keyComparer);
        private static readonly Dictionary<string, LoadGate> loadLocks = new(keyComparer);
        private static readonly object gate = new();
        private static int generation;
        private static long useClock;
        private static long retainedBytes;
        private static long inactiveByteBudget = 64L * 1024 * 1024;
        private static long retentionTarget = 64L * 1024 * 1024;

        internal static long InactiveByteBudget
        {
            get { lock (gate) return inactiveByteBudget; }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                lock (gate)
                {
                    inactiveByteBudget = value;
                    retentionTarget = value;
                }
                Trim(false);
            }
        }

        public static FontSource Read(string path) => GetOrAdd(path, OpenPathSource);

        private static FontSource OpenPathSource(string path)
        {
#if UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)
            return FontFileCache.OpenSnapshot(path);
#else
            return FileFontSource.OpenFile(path);
#endif
        }

        public static FontSource GetOrAdd(string key, Func<string, FontSource> loader)
        {
            lock (gate)
                if (cache.TryGetValue(key, out var hit))
                {
                    hit.lastUse = ++useClock;
                    return hit.source;
                }

            var loadGate = AcquireLoadGate(key);
            try
            {
                lock (loadGate.sync)
                {
                    int loadGeneration;
                    lock (gate)
                    {
                        if (cache.TryGetValue(key, out var cached))
                        {
                            cached.lastUse = ++useClock;
                            return cached.source;
                        }
                        loadGeneration = generation;
                    }

                    var source = loader(key);
                    if (source == null || source.Length == 0) return null;

                    var added = false;
                    lock (gate)
                        if (loadGeneration == generation)
                        {
                            cache[key] = new Entry { source = source, lastUse = ++useClock };
                            retainedBytes += source.OwnedByteCount;
                            added = true;
                        }
                    if (added && !SystemFont.NotifyByteCacheGrowth()) Trim(false);
                    return source;
                }
            }
            finally { ReleaseLoadGate(key, loadGate); }
        }

        internal static void Retain(string key, FontSource source)
        {
            if (string.IsNullOrEmpty(key) || source == null || source.Length == 0) return;
            var added = false;
            lock (gate)
            {
                if (!cache.TryGetValue(key, out var entry))
                {
                    entry = new Entry { source = source };
                    cache[key] = entry;
                    retainedBytes += source.OwnedByteCount;
                    added = true;
                }
                entry.references++;
                entry.lastUse = ++useClock;
            }
            if (added && !SystemFont.NotifyByteCacheGrowth()) Trim(false);
        }

        internal static void Release(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (gate)
                if (cache.TryGetValue(key, out var entry) && entry.references > 0)
                {
                    entry.references--;
                    entry.lastUse = ++useClock;
                }
        }

        internal static void Trim(bool aggressive)
        {
            long target;
            lock (gate) target = aggressive ? 0 : retentionTarget;
            Trim(aggressive, target);
        }

        internal static void Trim(bool aggressive, long target)
        {
            lock (gate)
            {
                long inactiveBytes = 0;
                foreach (var entry in cache.Values)
                    if (entry.references == 0) inactiveBytes += entry.source.OwnedByteCount;
                if (aggressive) target = 0;
                retentionTarget = Math.Min(target, inactiveByteBudget);
                target = retentionTarget;
                if (aggressive || inactiveBytes > target
                               || HasUnownedFileSources())
                {
                    var candidates = new List<KeyValuePair<string, Entry>>();
                    foreach (var pair in cache)
                        if (pair.Value.references == 0) candidates.Add(pair);
                    candidates.Sort((left, right) => left.Value.lastUse.CompareTo(right.Value.lastUse));
                    for (var i = 0; i < candidates.Count; i++)
                    {
                        var oldest = candidates[i];
                        var ownedBytes = oldest.Value.source.OwnedByteCount;
                        if (!aggressive && ownedBytes != 0 && inactiveBytes <= target) continue;
                        cache.Remove(oldest.Key);
                        retainedBytes -= ownedBytes;
                        inactiveBytes -= ownedBytes;
                    }
                }
                if (aggressive)
                {
                    cache.TrimExcess();
                    loadLocks.TrimExcess();
                }
            }
        }

        private static bool HasUnownedFileSources()
        {
            foreach (var entry in cache.Values)
                if (entry.references == 0 && entry.source.OwnedByteCount == 0) return true;
            return false;
        }

        internal static Stats GetStats()
        {
            lock (gate) return new Stats(cache.Count, retainedBytes);
        }

        private static LoadGate AcquireLoadGate(string key)
        {
            lock (gate)
            {
                if (!loadLocks.TryGetValue(key, out var result))
                    loadLocks[key] = result = new LoadGate();
                result.users++;
                return result;
            }
        }

        private static void ReleaseLoadGate(string key, LoadGate loadGate)
        {
            lock (gate)
            {
                loadGate.users--;
                if (loadGate.users == 0 && loadLocks.TryGetValue(key, out var current)
                                        && ReferenceEquals(current, loadGate))
                    loadLocks.Remove(key);
            }
        }

        public static void Clear()
        {
            lock (gate)
            {
                cache.Clear();
                loadLocks.Clear();
                cache.TrimExcess();
                loadLocks.TrimExcess();
                retainedBytes = 0;
                retentionTarget = inactiveByteBudget;
                generation++;
                FileFontSource.ResetRegistry();
            }
        }
    }
}
