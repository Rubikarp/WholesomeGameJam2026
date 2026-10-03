using System;
using System.Collections.Generic;

namespace LightSide
{
    internal sealed class UniTextAttachments : IDisposable
    {
        private readonly Dictionary<Type, IDisposable> byType = new();
        private readonly List<IDisposable> ordered = new();
        private bool disposed;

        public T GetOrCreate<T>(UniTextBase owner, Func<UniTextBase, T> factory)
            where T : class, IDisposable
        {
            if (disposed) throw new ObjectDisposedException(nameof(UniTextAttachments));
            if (byType.TryGetValue(typeof(T), out var existing)) return (T)existing;
            var created = factory(owner) ?? throw new InvalidOperationException(
                $"Attachment factory for {typeof(T).Name} returned null.");
            byType.Add(typeof(T), created);
            ordered.Add(created);
            return created;
        }

        public bool TryGet<T>(out T attachment) where T : class, IDisposable
        {
            if (!disposed && byType.TryGetValue(typeof(T), out var existing))
            {
                attachment = (T)existing;
                return true;
            }
            attachment = null;
            return false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (var i = ordered.Count - 1; i >= 0; i--) ordered[i].Dispose();
            ordered.Clear();
            byType.Clear();
        }
    }
}
