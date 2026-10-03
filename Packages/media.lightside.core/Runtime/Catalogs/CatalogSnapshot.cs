using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Main-thread name→entry mirror of an <see cref="INamedCatalog{TEntry}"/> for worker-thread
    /// resolution. Owns ALL freshness rules — including the edit-mode-always-recapture rule
    /// (outside play mode assets mutate without raising events, so <see cref="Prepare"/> recaptures
    /// every sweep) — so no consumer can forget one.
    /// </summary>
    public sealed class CatalogSnapshot<TEntry>
    {
        private readonly Dictionary<string, TEntry> map = new(StringComparer.OrdinalIgnoreCase);
        private object capturedSource;
        private bool dirty = true;

        /// <summary>Number of entries in the captured map.</summary>
        public int Count => map.Count;

        /// <summary>Forces the next <see cref="Prepare"/> to recapture.</summary>
        public void MarkDirty() => dirty = true;

        /// <summary>
        /// Main-thread refresh before parallel layout. Returns <see langword="true"/> when the map
        /// was recaptured (so consumers can rebuild derived caches), <see langword="false"/> when it
        /// was already fresh. Entries whose <paramref name="nameOf"/> yields a null or empty key are
        /// skipped.
        /// </summary>
        public bool Prepare(INamedCatalog<TEntry> source, Func<TEntry, string> nameOf)
        {
            if (!Application.isPlaying) dirty = true;
            if (!ReferenceEquals(capturedSource, source)) dirty = true;
            if (!dirty) return false;
            dirty = false;
            capturedSource = source;
            NamedCatalogState<TEntry>.FillLookup(
                map, source?.Enumerate(), nameOf);
            return true;
        }

        /// <summary>Resolves a name against the captured map (case-insensitive). Safe on worker threads between <see cref="Prepare"/> calls.</summary>
        public bool TryGet(string name, out TEntry entry) => map.TryGetValue(name, out entry);

        /// <summary>Whether the captured map has an entry with the given name.</summary>
        public bool ContainsKey(string name) => map.ContainsKey(name);

        /// <summary>All captured entries, allocation-free.</summary>
        public Dictionary<string, TEntry>.ValueCollection Values => map.Values;
    }
}
