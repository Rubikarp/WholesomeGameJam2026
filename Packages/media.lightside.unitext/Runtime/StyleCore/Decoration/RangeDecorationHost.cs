using System;
using System.Collections.Generic;

namespace LightSide
{
    internal sealed class RangeDecorationHost : IDisposable
    {
        private static readonly Func<UniTextBase, RangeDecorationHost> create =
            static owner => new RangeDecorationHost(owner);

        private readonly UniTextBase owner;
        private readonly Dictionary<int, RangeDecorationHandle> handles = new();
        private RangeDecorationRenderer behindRenderer;
        private RangeDecorationRenderer aboveRenderer;
        private int nextHandleId;
        private int nextSequence;
        private bool subscribed;
        private bool disposed;

        public RangeDecorationHost(UniTextBase owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            owner.Deinitializing += Hide;
        }

        public static RangeDecorationHost For(UniTextBase owner)
            => owner.GetOrCreateAttachment(create);

        public RangeDecorationHandle Acquire(RangeDecorationOrder order = RangeDecorationOrder.Behind,
            int priority = RangeDecorationPriorities.Custom)
        {
            ThrowIfDisposed();
            var id = ++nextHandleId;
            if (id == 0) id = ++nextHandleId;
            var handle = new RangeDecorationHandle(this, id, order, priority, nextSequence++);
            handles.Add(id, handle);
            if (!subscribed)
            {
                subscribed = true;
                UniTextBase.ProcessingEnded += Flush;
            }
            return handle;
        }

        public void Hide()
        {
            if (disposed) return;
            foreach (var pair in handles)
                pair.Value.ExistingGroup?.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.Deinitializing -= Hide;
            if (subscribed)
            {
                subscribed = false;
                UniTextBase.ProcessingEnded -= Flush;
            }

            foreach (var pair in handles)
                pair.Value.Invalidate();
            handles.Clear();
            behindRenderer?.Destroy();
            aboveRenderer?.Destroy();
            behindRenderer = null;
            aboveRenderer = null;
        }

        internal RangeDecorationGroup GetOrCreateGroup(RangeDecorationHandle handle)
        {
            Validate(handle);
            if (handle.ExistingGroup != null) return handle.ExistingGroup;
            var renderer = RendererFor(handle.Order);
            var group = renderer.GetOrCreateGroup($"decoration:{handle.Id}");
            group.SetSort(handle.Priority, handle.Sequence);
            handle.SetGroup(group);
            return group;
        }

        internal void SetPriority(RangeDecorationHandle handle, int priority)
        {
            Validate(handle);
            if (handle.Priority == priority) return;
            handle.SetPriorityValue(priority);
            handle.ExistingGroup?.SetSort(priority, handle.Sequence);
        }

        internal void Release(RangeDecorationHandle handle)
        {
            if (disposed || handle == null || !handles.TryGetValue(handle.Id, out var owned) ||
                !ReferenceEquals(owned, handle))
            {
                handle?.Invalidate();
                return;
            }

            handles.Remove(handle.Id);
            handle.ExistingGroup?.Destroy();
            handle.Invalidate();
            if (handles.Count != 0) return;

            if (subscribed)
            {
                subscribed = false;
                UniTextBase.ProcessingEnded -= Flush;
            }
            behindRenderer?.Destroy();
            aboveRenderer?.Destroy();
            behindRenderer = null;
            aboveRenderer = null;
        }

        private RangeDecorationRenderer RendererFor(RangeDecorationOrder order)
        {
            var renderer = order == RangeDecorationOrder.Above ? aboveRenderer : behindRenderer;
            if (renderer != null) return renderer;

            renderer = owner switch
            {
                UniText canvasText => new CanvasRangeDecorationRenderer(canvasText,
                    order == RangeDecorationOrder.Above ? "Decorations (Above)" : "Decorations (Behind)",
                    order),
                UniTextWorld worldText => new WorldRangeDecorationRenderer(worldText, order),
                _ => throw new NotSupportedException(
                    $"Range decorations do not support {owner.GetType().FullName}."),
            };
            if (order == RangeDecorationOrder.Above) aboveRenderer = renderer;
            else behindRenderer = renderer;
            return renderer;
        }

        private void Flush()
        {
            behindRenderer?.Flush();
            aboveRenderer?.Flush();
        }

        private void Validate(RangeDecorationHandle handle)
        {
            ThrowIfDisposed();
            if (handle == null || !handles.TryGetValue(handle.Id, out var owned) ||
                !ReferenceEquals(owned, handle))
                throw new ObjectDisposedException(nameof(RangeDecorationHandle));
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(RangeDecorationHost));
        }
    }

    internal sealed class RangeDecorationHandle : IDisposable
    {
        private RangeDecorationHost host;
        private RangeDecorationGroup group;

        public int Id { get; }
        public int Sequence { get; }
        public RangeDecorationOrder Order { get; }
        public int Priority { get; private set; }
        public RangeDecorationGroup ExistingGroup => group;
        public RangeDecorationGroup Group => RequireHost().GetOrCreateGroup(this);
        public RangeDecorationMesh Mesh => Group.DecorationMesh;

        public RangeDecorationHandle(RangeDecorationHost host, int id, RangeDecorationOrder order,
            int priority, int sequence)
        {
            this.host = host;
            Id = id;
            Order = order;
            Priority = priority;
            Sequence = sequence;
        }

        public void SetPriority(int value)
        {
            RequireHost().SetPriority(this, value);
        }

        public void Dispose()
        {
            host?.Release(this);
        }

        public void MarkDirty()
        {
            if (group != null) group.MarkDirty();
        }

        internal void SetGroup(RangeDecorationGroup value) => group = value;
        internal void SetPriorityValue(int value) => Priority = value;

        internal void Invalidate()
        {
            host = null;
            group = null;
        }

        private RangeDecorationHost RequireHost()
            => host ?? throw new ObjectDisposedException(nameof(RangeDecorationHandle));
    }
}
