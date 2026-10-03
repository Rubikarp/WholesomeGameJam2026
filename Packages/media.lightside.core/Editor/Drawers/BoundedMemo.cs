using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Bounded memo cache for editor hot paths: a plain dictionary that clears itself when adding
    /// past capacity. Misses are always tolerated (the value is recomputed), so wholesale clearing
    /// is safe and cheaper than eviction bookkeeping. NOT for entries that own resources needing
    /// disposal — those need explicit eviction (see GradientDrawer's texture cache).
    /// </summary>
    public sealed class BoundedMemo<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> map = new();
        private readonly int capacity;

        public BoundedMemo(int capacity) => this.capacity = capacity;

        public bool TryGetValue(TKey key, out TValue value) => map.TryGetValue(key, out value);

        public TValue this[TKey key]
        {
            set
            {
                if (map.Count >= capacity && !map.ContainsKey(key))
                    map.Clear();
                map[key] = value;
            }
        }
    }
}
