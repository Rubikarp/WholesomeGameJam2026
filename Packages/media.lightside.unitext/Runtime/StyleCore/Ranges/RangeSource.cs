using System;
using System.Collections.Generic;
using System.Threading;

namespace LightSide
{
    /// <summary>Base class for serialized and runtime producers of text range entities.</summary>
    [Serializable]
    [TypeMenuSuffix("RangeSource", "Source")]
    [SelectorNoneLabel("Whole Text")]
    [StateHierarchy]
    public abstract class RangeSource
    {
        private static long nextRuntimeId;

        [NonSerialized] private long runtimeId;
        [NonSerialized] private long nextRangeId;
        [NonSerialized] private int nextSegmentId;
        [NonSerialized] private object boundOwner;
        [NonSerialized] private Action boundChanged;
        [NonSerialized] private SynchronizationContext boundContext;
        [NonSerialized] private Func<TextSnapshot> snapshotProvider;
        [NonSerialized] private int bindingCount;
        [NonSerialized] private int bindingGeneration;
        [NonSerialized] private TextSnapshot snapshot;
        [NonSerialized] private Dictionary<RangeId, RangeSegmentId> primarySegmentIds;
        [NonSerialized] private IRangeSourceChangeSink changeSink;
        [NonSerialized] private RangeSource attachmentParent;
        [NonSerialized] private List<RangeSource> connectedChildren;

        /// <summary>Occurs after this source atomically changes its range set.</summary>
        public event Action Changed;

        /// <summary>Runtime identity used with source-local <see cref="RangeId"/> values.</summary>
        public RangeSourceId RuntimeId
        {
            get
            {
                var current = Interlocked.Read(ref runtimeId);
                if (current != 0) return new RangeSourceId((ulong)current);

                var allocated = Interlocked.Increment(ref nextRuntimeId);
                current = Interlocked.CompareExchange(ref runtimeId, allocated, 0);
                return new RangeSourceId((ulong)(current == 0 ? allocated : current));
            }
        }

        /// <summary>
        /// Stable configuration identity shared by sources that select the same logical input;
        /// null means identity by instance only.
        /// </summary>
        public virtual string Identity => GetType().FullName;

        internal virtual IReadOnlyList<RangeSource> Children => null;

        internal IRangeSourceChangeSink ChangeSink
        {
            get
            {
                lock (this) return changeSink;
            }
        }

        internal bool IsBound
        {
            get
            {
                lock (this) return boundOwner != null;
            }
        }

        internal RangeSource AttachmentParent => attachmentParent;

        private void SetAttachmentParent(RangeSource parent)
        {
            if (ReferenceEquals(attachmentParent, parent)) return;
            attachmentParent = parent;
        }

        internal void SetChangeSink(IRangeSourceChangeSink sink)
        {
            if (sink != null)
                ValidateGraph(this);
            IRangeSourceChangeSink previous;
            lock (this)
            {
                previous = changeSink;
                changeSink = sink;
            }
            if (ReferenceEquals(previous, sink)) return;
            if (connectedChildren != null)
            {
                for (var i = 0; i < connectedChildren.Count; i++)
                {
                    var child = connectedChildren[i];
                    if (!ReferenceEquals(child.AttachmentParent, this)) continue;
                    child.SetChangeSink(sink);
                }
            }
            ReconcileChildren();
        }

        /// <summary>
        /// The rendered-text snapshot against which this source currently resolves positions.
        /// It is invalid until the source has participated in its owner's first parse.
        /// </summary>
        public TextSnapshot Snapshot
        {
            get
            {
                Func<TextSnapshot> provider;
                lock (this)
                {
                    if (snapshot.IsValid) return snapshot;
                    provider = snapshotProvider;
                }
                if (provider != null) provider();
                lock (this) return snapshot;
            }
        }

        internal RangeId AllocateRangeId()
            => new((ulong)Interlocked.Increment(ref nextRangeId));

        internal RangeSegmentId AllocateSegmentId()
            => new((uint)Interlocked.Increment(ref nextSegmentId));

        internal RangeSegmentId GetPrimarySegmentId(RangeId rangeId)
        {
            lock (this)
            {
                primarySegmentIds ??= new Dictionary<RangeId, RangeSegmentId>();
                if (primarySegmentIds.TryGetValue(rangeId, out var existing)) return existing;
                var created = AllocateSegmentId();
                primarySegmentIds.Add(rangeId, created);
                return created;
            }
        }

        internal void ReserveRangeId(RangeId id)
        {
            if (id.Value > long.MaxValue) return;
            var target = (long)id.Value;
            var current = Interlocked.Read(ref nextRangeId);
            while (current < target)
            {
                var observed = Interlocked.CompareExchange(ref nextRangeId, target, current);
                if (observed == current) return;
                current = observed;
            }
        }

        internal void ReserveSegmentId(RangeSegmentId id)
        {
            if (id.Value > int.MaxValue) return;
            var target = (int)id.Value;
            var current = Volatile.Read(ref nextSegmentId);
            while (current < target)
            {
                var observed = Interlocked.CompareExchange(ref nextSegmentId, target, current);
                if (observed == current) return;
                current = observed;
            }
        }

        internal void Bind(object owner, Action changed,
            Func<TextSnapshot> getSnapshot,
            in TextSnapshot currentSnapshot)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (changed == null) throw new ArgumentNullException(nameof(changed));
            if (getSnapshot == null) throw new ArgumentNullException(nameof(getSnapshot));

            bool firstBinding;
            lock (this)
            {
                if (boundOwner != null && !ReferenceEquals(boundOwner, owner))
                    throw new InvalidOperationException(
                        $"Range source {GetType().Name} is already bound to another UniText parser.");

                firstBinding = boundOwner == null;
                if (firstBinding)
                {
                    boundOwner = owner;
                    boundChanged = changed;
                    boundContext = SynchronizationContext.Current;
                    snapshotProvider = getSnapshot;
                    bindingGeneration++;
                }
                bindingCount++;
            }

            if (currentSnapshot.IsValid)
                SetSnapshot(in currentSnapshot);
            if (firstBinding) ReconcileChildren(false);
            BindChildrenOnce(owner, changed, getSnapshot, in currentSnapshot);
        }

        internal void Unbind(object owner)
        {
            List<RangeSource> children;
            bool lastBinding;
            lock (this)
            {
                if (!ReferenceEquals(boundOwner, owner)) return;
                children = connectedChildren;
                lastBinding = --bindingCount == 0;
                if (lastBinding)
                {
                    boundOwner = null;
                    boundChanged = null;
                    boundContext = null;
                    snapshotProvider = null;
                    bindingGeneration++;
                }
            }
            if (children == null) return;
            for (var i = 0; i < children.Count; i++)
                children[i].Unbind(owner);
            if (lastBinding && ChangeSink == null)
                ReconcileChildren();
        }

        internal void SetSnapshot(in TextSnapshot value)
        {
            if (!value.IsValid) throw new ArgumentException("Text snapshot is invalid.", nameof(value));
            lock (this)
            {
                if (snapshot.Revision == value.Revision) return;
                var previous = snapshot;
                snapshot = value;
                OnSnapshotChanged(in previous, in value);
            }
        }

        internal void Collect(in TextSnapshot currentSnapshot, List<RangeSourceMatch> output)
        {
            var writer = new RangeMatchWriter(this, in currentSnapshot, output);
            CollectRanges(in currentSnapshot, writer);
        }

        /// <summary>
        /// Emits this source's immutable range snapshot. Implementations must not retain
        /// <paramref name="writer"/> or mutable references used to construct a match.
        /// </summary>
        protected virtual void CollectRanges(in TextSnapshot currentSnapshot, RangeMatchWriter writer) { }

        /// <summary>
        /// Updates source-owned anchors after rendered text changes. Snapshot-bound sources may
        /// reject or clear old results; tracking sources may remap them through the detected edit.
        /// </summary>
        protected virtual void OnSnapshotChanged(in TextSnapshot previous, in TextSnapshot current) { }

        /// <summary>Publishes one consolidated source-change notification.</summary>
        protected void NotifyChanged() => PublishChange();

        protected void NotifyStructureChanged()
        {
            ValidateGraph(this);
            ReconcileChildren();
            PublishChange();
        }

        internal static void ValidateGraph(RangeSource root)
        {
            if (root == null) return;
            TraverseGraph(root, new List<RangeSource>(), null);
        }

        internal static void ValidateGraphs<T>(in StateListMutation<T> roots)
            where T : RangeSource
            => ValidateGraphsCore<StateListMutation<T>, T>(roots);

        internal static void CollectGraphNodes(RangeSource root, List<RangeSource> result)
        {
            if (root == null) return;
            TraverseGraph(root, new List<RangeSource>(), result);
        }

        private static void ValidateGraphsCore<TList, T>(TList roots)
            where TList : IReadOnlyList<T>
            where T : RangeSource
        {
            var visited = new List<RangeSource>();
            for (var i = 0; i < roots.Count; i++)
                if (roots[i] is { } root)
                    TraverseGraph(root, visited, null);
        }

        private static void TraverseGraph(RangeSource node, List<RangeSource> visited,
            List<RangeSource> result)
        {
            if (ReferenceIdentity.Contains(visited, node))
                throw new InvalidOperationException(
                    $"Range-source graph contains the same '{node.GetType().FullName}' instance more than once or contains a cycle.");
            visited.Add(node);
            result?.Add(node);

            if (node.Children is not { } children) return;
            for (var i = 0; i < children.Count; i++)
                if (children[i] is { } child)
                    TraverseGraph(child, visited, result);
        }

        private void PublishChange()
        {
            Action ownerChanged;
            SynchronizationContext context;
            IRangeSourceChangeSink sink;
            int generation;
            lock (this)
            {
                ownerChanged = boundChanged;
                context = boundContext;
                sink = changeSink;
                generation = bindingGeneration;
            }

            void Publish(object _)
            {
                Action currentOwnerChanged;
                IRangeSourceChangeSink currentSink;
                lock (this)
                {
                    currentOwnerChanged = generation == bindingGeneration ? ownerChanged : null;
                    currentSink = ReferenceEquals(changeSink, sink) ? sink : null;
                }
                currentOwnerChanged?.Invoke();
                currentSink?.MarkRangeSourceChanged(this);
                Changed?.Invoke();
            }

            if (context != null && !ReferenceEquals(SynchronizationContext.Current, context))
                context.Post(Publish, null);
            else
                Publish(null);
        }

        private void ReconcileChildren(bool bindAdded = true)
        {
            connectedChildren ??= new List<RangeSource>();
            var children = Children;
            IRangeSourceChangeSink sink;
            object owner;
            Action ownerChanged;
            Func<TextSnapshot> getSnapshot;
            int count;
            TextSnapshot currentSnapshot;
            lock (this)
            {
                sink = changeSink;
                owner = boundOwner;
                ownerChanged = boundChanged;
                getSnapshot = snapshotProvider;
                count = bindingCount;
                currentSnapshot = snapshot;
            }
            if (sink == null && owner == null)
            {
                for (var i = 0; i < connectedChildren.Count; i++)
                    if (ReferenceEquals(connectedChildren[i].AttachmentParent, this))
                        connectedChildren[i].SetAttachmentParent(null);
                connectedChildren.Clear();
                return;
            }

            ReferenceIdentity.Reconcile(children, connectedChildren, child =>
            {
                child.SetAttachmentParent(this);
                if (sink != null) child.SetChangeSink(sink);
                if (bindAdded && owner != null)
                    for (var binding = 0; binding < count; binding++)
                        child.Bind(owner, ownerChanged, getSnapshot, currentSnapshot);
            }, child =>
            {
                var ownsChild = ReferenceEquals(child.AttachmentParent, this);
                if (sink != null)
                {
                    if (ownsChild && ReferenceEquals(child.ChangeSink, sink))
                        child.SetChangeSink(null);
                }
                if (owner != null)
                    for (var binding = 0; binding < count; binding++) child.Unbind(owner);
                if (ownsChild) child.SetAttachmentParent(null);
            });
        }

        private void BindChildrenOnce(object owner, Action changed,
            Func<TextSnapshot> getSnapshot, in TextSnapshot currentSnapshot)
        {
            if (connectedChildren == null) return;
            for (var i = 0; i < connectedChildren.Count; i++)
                connectedChildren[i].Bind(owner, changed, getSnapshot, in currentSnapshot);
        }

    }
}
