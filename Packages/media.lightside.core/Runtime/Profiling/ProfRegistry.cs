using System;
using System.Collections.Concurrent;

namespace LightSide
{
    /// <summary>
    /// Stable name → int id map for profiler nodes. Ids are dense and never reused, so the runtime indexes
    /// flat arrays by id. Lookup is lock-free (worker threads resolve ids); only first-time registration locks.
    /// </summary>
    public static class ProfRegistry
    {
        private static readonly object sync = new();
        private static readonly ConcurrentDictionary<string, int> map = new(StringComparer.Ordinal);
        private static string[] names = new string[64];
        private static int count;

        /// <summary>Number of registered ids; the width flat per-thread arrays must cover.</summary>
        public static int Count => count;

        /// <summary>Returns the stable id for <paramref name="name"/>, registering it on first sight. Lock-free after the first call for a given name; <see cref="Count"/> is bumped last so a concurrent <see cref="Name"/> never reads an unwritten slot.</summary>
        public static int Id(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "<unnamed>";
            if (map.TryGetValue(name, out var i)) return i;
            lock (sync)
            {
                if (map.TryGetValue(name, out i)) return i;
                if (count == names.Length) Array.Resize(ref names, names.Length * 2);
                i = count;
                names[i] = name;
                map[name] = i;
                count++;
                return i;
            }
        }

        /// <summary>Lock-free lookup without registering. False on first sight — the caller decides whether to pay <see cref="Id"/>'s registration.</summary>
        public static bool TryGet(string name, out int id)
        {
            if (string.IsNullOrEmpty(name)) name = "<unnamed>";
            return map.TryGetValue(name, out id);
        }

        /// <summary>Name for an id, or "?" when out of range. Count is read before the array so a concurrent resize can only leave the array newer (larger) than the bound.</summary>
        public static string Name(int id)
        {
            int c = count;
            var arr = names;
            return (uint)id < (uint)c && id < arr.Length ? arr[id] : "?";
        }

        /// <summary>Snapshot of all registered names by id — used by the exporter/viewer to resolve the tree.</summary>
        public static string[] Snapshot()
        {
            lock (sync)
            {
                var arr = new string[count];
                Array.Copy(names, arr, count);
                return arr;
            }
        }
    }
}
