using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightSide
{
    internal interface ISharedMaterialClonePool
    {
        void MaintenanceTick(int frame);
#if UNITY_EDITOR
        void DestroyAll();
#endif
    }

    internal static class SharedMaterialClonePools
    {
        private static readonly List<ISharedMaterialClonePool> pools = new();

        static SharedMaterialClonePools()
        {
            CoreLoop.Maintaining += MaintenanceTick;
#if UNITY_EDITOR
            EditorLifecycle.UnmanagedCleaning += DestroyAll;
#endif
        }

        internal static void Register(ISharedMaterialClonePool pool)
        {
            for (var i = 0; i < pools.Count; i++)
                if (ReferenceEquals(pools[i], pool))
                    return;
            pools.Add(pool);
        }

        internal static void MaintenanceTick(int frame)
        {
            for (var i = 0; i < pools.Count; i++) pools[i].MaintenanceTick(frame);
        }

#if UNITY_EDITOR
        private static void DestroyAll()
        {
            for (var i = 0; i < pools.Count; i++) pools[i].DestroyAll();
        }
#endif
    }

    /// <summary>Shares reference-counted runtime material variants by logical key and source identity.</summary>
    internal abstract class SharedMaterialClonePool<TKey> : ISharedMaterialClonePool
        where TKey : struct, IEquatable<TKey>
    {
        protected sealed class Entry
        {
            public Material runtime;
            public Object keySource;
            public int refCount;
            public int idleSinceFrame;
        }

        private readonly Dictionary<TKey, Entry> entries = new();
        private List<TKey> sweepScratch;
        private List<Entry> orphans;
        private int lastMaintenanceFrame = -1;

        protected SharedMaterialClonePool() => SharedMaterialClonePools.Register(this);

        protected abstract Material CreateClone(in TKey key, Object source);

        protected virtual void OnRebind(Material runtime, in TKey key, Object source) { }

        public Material Acquire(in TKey key, Object source)
        {
            if (source == null) return null;

            if (entries.TryGetValue(key, out var entry) && entry.runtime != null &&
                ReferenceEquals(entry.keySource, source))
            {
                OnRebind(entry.runtime, in key, source);
                entry.refCount++;
                return entry.runtime;
            }

            var runtime = CreateClone(in key, source);
            if (runtime == null) return null;
            try
            {
                OnRebind(runtime, in key, source);
            }
            catch
            {
                ObjectUtils.SafeDestroy(runtime);
                throw;
            }

            var replacement = new Entry
            {
                runtime = runtime,
                keySource = source,
                refCount = 1,
            };

            if (entry != null)
            {
                if (entry.refCount > 0)
                {
                    orphans ??= new List<Entry>();
                    orphans.Add(entry);
                }
                else
                {
                    Destroy(entry);
                }
            }

            entries[key] = replacement;
            return runtime;
        }

        public bool TryRebind(in TKey key, Object source, Material held)
        {
            if (source == null || !entries.TryGetValue(key, out var entry) || entry.runtime == null ||
                !ReferenceEquals(entry.keySource, source) || !ReferenceEquals(entry.runtime, held))
                return false;

            OnRebind(entry.runtime, in key, source);
            return true;
        }

        public void Release(in TKey key, Material held)
        {
            if (entries.TryGetValue(key, out var entry) && ReferenceEquals(entry.runtime, held))
            {
                if (entry.refCount == 0) return;
                if (--entry.refCount == 0)
                    entry.idleSinceFrame = CoreLoop.Frame;
                return;
            }

            if (orphans == null) return;
            for (var i = 0; i < orphans.Count; i++)
            {
                var orphan = orphans[i];
                if (!ReferenceEquals(orphan.runtime, held)) continue;
                if (--orphan.refCount <= 0)
                {
                    orphans.RemoveAt(i);
                    Destroy(orphan);
                }
                return;
            }
        }

        public virtual void MaintenanceTick(int frame)
        {
            if (lastMaintenanceFrame == frame) return;
            lastMaintenanceFrame = frame;
            if (frame % 300 == 0) Sweep(frame, 300);
        }

        public void Sweep(int frame, int graceFrames)
        {
            if (orphans != null)
            {
                for (var i = orphans.Count - 1; i >= 0; i--)
                {
                    var orphan = orphans[i];
                    if (orphan.refCount > 0 && orphan.runtime != null) continue;
                    orphans.RemoveAt(i);
                    Destroy(orphan);
                }
            }

            if (entries.Count == 0) return;
            sweepScratch ??= new List<TKey>();
            sweepScratch.Clear();

            foreach (var pair in entries)
            {
                var entry = pair.Value;
                if (entry.refCount != 0) continue;
                if (entry.keySource == null || frame - entry.idleSinceFrame >= graceFrames)
                    sweepScratch.Add(pair.Key);
            }

            for (var i = 0; i < sweepScratch.Count; i++)
            {
                var key = sweepScratch[i];
                if (!entries.TryGetValue(key, out var entry)) continue;
                entries.Remove(key);
                Destroy(entry);
            }
        }

        protected void InvalidateBySource(Object source)
        {
            if (source == null) return;
            sweepScratch ??= new List<TKey>();
            sweepScratch.Clear();
            foreach (var pair in entries)
            {
                if (ReferenceEquals(pair.Value.keySource, source)) sweepScratch.Add(pair.Key);
            }

            for (var i = 0; i < sweepScratch.Count; i++)
            {
                var key = sweepScratch[i];
                var entry = entries[key];
                entries.Remove(key);
                if (entry.refCount > 0)
                {
                    orphans ??= new List<Entry>();
                    orphans.Add(entry);
                }
                else
                {
                    Destroy(entry);
                }
            }
        }

        protected void ForEachClone(Action<Material> action)
        {
            foreach (var entry in entries.Values)
                if (entry.runtime != null)
                    action(entry.runtime);

            if (orphans == null) return;
            for (var i = 0; i < orphans.Count; i++)
                if (orphans[i].runtime != null)
                    action(orphans[i].runtime);
        }

        private static void Destroy(Entry entry)
        {
            ObjectUtils.SafeDestroy(entry.runtime);
            entry.runtime = null;
        }

#if UNITY_EDITOR
        public void DestroyAll()
        {
            foreach (var entry in entries.Values) Destroy(entry);
            entries.Clear();
            if (orphans == null) return;
            for (var i = 0; i < orphans.Count; i++) Destroy(orphans[i]);
            orphans.Clear();
        }
#endif
    }

    /// <summary>Holds one reference to a shared material variant across rebuilds.</summary>
    internal struct MaterialCloneRef<TKey> where TKey : struct, IEquatable<TKey>
    {
        private SharedMaterialClonePool<TKey> pool;
        private TKey key;
        private Material current;
        private bool held;

        public Material Current => held ? current : null;

        public Material Bind(SharedMaterialClonePool<TKey> targetPool, in TKey newKey, Object source)
        {
            if (held && ReferenceEquals(pool, targetPool) && key.Equals(newKey) && current != null &&
                targetPool.TryRebind(in key, source, current))
            {
                return current;
            }

            var replacement = targetPool.Acquire(in newKey, source);
            if (replacement == null)
            {
                Release();
                return null;
            }

            if (held) pool.Release(in key, current);
            pool = targetPool;
            key = newKey;
            current = replacement;
            held = true;
            return replacement;
        }

        public void Release()
        {
            if (held) pool.Release(in key, current);
            held = false;
            current = null;
            key = default;
            pool = null;
        }
    }
}
