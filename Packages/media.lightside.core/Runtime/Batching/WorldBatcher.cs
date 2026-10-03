using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>
    /// Instance-based, domain-agnostic world-space mesh-combining batcher. One instance per consumer
    /// (UniText world text, UniLottie world quads). The host drives it imperatively through the receptors
    /// <see cref="Activate"/> / <see cref="Deactivate"/> / <see cref="Capture"/> / <see cref="ClearRenderData"/>
    /// / <see cref="SuppressionChanged"/> / <see cref="Flush"/>, wiring its own component events and per-frame
    /// tick to them; the engine names no domain concept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Grouping and sharding.</b> A <see cref="BatchGroup"/> is one sorting context, keyed by
    /// <see cref="BatchKey"/> = (sortingLayer, sortingOrder, sortingGroup, <see cref="IWorldBatchSource.BatchGroupId"/>,
    /// layer, scene, castShadows) — material is NOT in the key. Inside a group, entries pack into size-bounded <see cref="BatchShard"/>s
    /// (<see cref="shardTargetVertexCount"/> vertex target, one mesh per shard), so a structural change
    /// re-bakes and re-uploads only the owning shard. All segments of one source co-locate in one shard.
    /// </para>
    /// <para>
    /// <b>Lifecycle.</b> A single static coordinator owns the global bootstrap and orphan sweep, shared by
    /// ALL instances: Play Mode entry and assembly reload reset every editor consumer together, while player
    /// startup does the same through its runtime hook. The owner-agnostic <see cref="BatchMarker"/> sweep is
    /// therefore correct; steady-state destruction remains instance-scoped.
    /// </para>
    /// <para>
    /// <b>Change detection.</b> Movement and destruction are detected by a Burst-compiled parallel job
    /// comparing every source's <c>localToWorldMatrix</c> against its cached value — no per-source main-thread
    /// calls. Grouping-context changes arrive through <see cref="ContextChanged"/> where the host has an
    /// event, and through a budgeted round-robin sweep for the inputs that raise none (layer, scene,
    /// <see cref="SortingGroup"/> state), so a static population costs O(population) SIMD compares on worker
    /// threads and O(budget) main-thread work per frame.
    /// </para>
    /// <para>
    /// Split across partial files: this file (state, coordinator, drive receptors, change detection),
    /// <c>.Capture</c> (source segments → batch entries), <c>.Upload</c> (entries → meshes),
    /// <c>.Types</c> (the data structures).
    /// </para>
    /// </remarks>
    public sealed partial class WorldBatcher
    {
        private readonly Dictionary<IWorldBatchSource, ComponentSlot> slots = new();
        private readonly Dictionary<BatchKey, BatchGroup> groups = new();
        private readonly Dictionary<Scene, Transform> sceneRoots = new();
        private readonly List<IWorldBatchSource> sourceScratch = new();
        private readonly List<BatchSegment> captureBuffer = new(4);
        private readonly List<ComponentSlot> pollList = new();
        private TransformAccessArray pollTransforms;
        private NativeArray<Matrix4x4> pollMatrices;
        private NativeArray<int> pollResults;
        private int sweepCursor;
        private int shardTargetVertexCount;
        private readonly string debugLabel;

        /// <summary>Slots whose grouping context is re-validated per flush. Bounds the cost of catching
        /// the context inputs that raise no event — GameObject layer, scene membership, and
        /// <see cref="SortingGroup"/> presence/enabled state — at a fixed per-frame budget independent
        /// of population; a change lands within population/budget flushes. Evented inputs (the source's
        /// own sorting fields, re-parenting) go through <see cref="ContextChanged"/> instead and never
        /// wait for the sweep.</summary>
        private const int ContextSweepBudget = 64;

        /// <summary>Per-shard vertex packing target. A source's segments co-locate in one shard, so a single oversized source inflates its own shard rather than splitting; the budget only bounds how much unrelated geometry a structural rebuild drags along. Clamped to a floor of 64.</summary>
        public int ShardTargetVertexCount
        {
            get => shardTargetVertexCount;
            set => shardTargetVertexCount = Mathf.Max(64, value);
        }

        private const string BatchLayerNamePrefix = "-_LSWB_";
        private const string SceneRootName = "-_LSWBRoot_-";

        public WorldBatcher(int shardTargetVertexCount = 16384, string debugLabel = null)
        {
            ShardTargetVertexCount = shardTargetVertexCount;
            this.debugLabel = string.IsNullOrEmpty(debugLabel) ? "LSWB" : debugLabel;
            instances.Add(this);
        }

        #region Coordinator

        private static readonly List<WorldBatcher> instances = new();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void EditorInit()
        {
            EditorLifecycle.PlayModeEntering -= ResetAndRestoreAllInstances;
            EditorLifecycle.PlayModeEntering += ResetAndRestoreAllInstances;
            EditorLifecycle.ManagedCleaning -= ResetAllInstances;
            EditorLifecycle.ManagedCleaning += ResetAllInstances;
            SweepOrphans();
        }
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RuntimeInit() => ResetAllInstances();
#endif

        /// <summary>
        /// Full reset shared by every instance. Leftover batch objects must die immediately because deferred
        /// destruction does not execute after domain teardown.
        /// </summary>
        private static void ResetAllInstances() => ResetAllInstances(false);

#if UNITY_EDITOR
        private static void ResetAndRestoreAllInstances() => ResetAllInstances(true);
#endif

        private static void ResetAllInstances(bool restore)
        {
            SweepOrphans();
            for (var i = 0; i < instances.Count; i++)
                instances[i].ResetManagedState(restore);
        }

        private static void SweepOrphans()
        {
            foreach (var marker in Resources.FindObjectsOfTypeAll<BatchMarker>())
                if (marker != null) DestroyBatchObject(marker.gameObject);
        }

        /// <summary>
        /// Destroys a leftover batch GameObject and the dynamic mesh it holds. The mesh is a non-GameObject
        /// <see cref="HideFlags.HideAndDontSave"/> asset that the marker scan cannot reach and that destroying
        /// the GameObject does not free — so it must be released here or it leaks one mesh per layer on every
        /// domain reload.
        /// </summary>
        private static void DestroyBatchObject(GameObject go)
        {
            if (go == null) return;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(go)) return;
            if (go.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                Object.DestroyImmediate(filter.sharedMesh);
            Object.DestroyImmediate(go);
#else
            if (go.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                Object.Destroy(filter.sharedMesh);
            Object.Destroy(go);
#endif
        }

        private void ResetManagedState(bool restore)
        {
            sourceScratch.Clear();
            foreach (var pair in slots)
            {
                pair.Value.ReturnBuffers();
                if (restore && Alive(pair.Key)) sourceScratch.Add(pair.Key);
            }
            slots.Clear();
            groups.Clear();
            sceneRoots.Clear();
            DisposePollState();
            if (!restore) return;
            for (var i = 0; i < sourceScratch.Count; i++)
            {
                var source = sourceScratch[i];
                Activate(source);
                Capture(source);
            }
            sourceScratch.Clear();
            Flush();
        }

        /// <summary>
        /// Generic/interface <c>source == null</c> is REFERENCE equality and bypasses Unity's fake-null; this
        /// forces the <see cref="UnityEngine.Object"/> operator so a destroyed source reads as dead.
        /// </summary>
        private static bool Alive(IWorldBatchSource s) => s is Object o && o != null;

        #endregion

        #region Drive receptors

        /// <summary>Registers a slot for <paramref name="source"/>. The host calls this from its own activation event, before the first <see cref="Capture"/>.</summary>
        public void Activate(IWorldBatchSource source)
        {
            if (!Alive(source) || slots.ContainsKey(source)) return;
            var slot = new ComponentSlot
            {
                source = source,
                cachedTransform = source.Transform,
                cachedGameObject = source.GameObject,
            };
            slots[source] = slot;
            AddToPoll(slot);
        }

        private void AddToPoll(ComponentSlot slot)
        {
            EnsurePollCapacity(pollList.Count + 1);
            slot.pollIndex = pollList.Count;
            pollList.Add(slot);
            pollTransforms.Add(slot.cachedTransform);
            pollMatrices[slot.pollIndex] = slot.cachedTransform.localToWorldMatrix;
        }

        private void RemoveFromPoll(ComponentSlot slot)
        {
            var index = slot.pollIndex;
            if (index < 0) return;
            slot.pollIndex = -1;
            var last = pollList.Count - 1;
            if (index != last)
            {
                var moved = pollList[last];
                pollList[index] = moved;
                moved.pollIndex = index;
                pollMatrices[index] = pollMatrices[last];
            }
            pollList.RemoveAt(last);
            pollTransforms.RemoveAtSwapBack(index);
            if (sweepCursor > last) sweepCursor = 0;
        }

        private void EnsurePollCapacity(int required)
        {
            if (!pollMatrices.IsCreated)
            {
                var capacity = Mathf.Max(64, Mathf.NextPowerOfTwo(required));
                pollTransforms = new TransformAccessArray(capacity);
                pollMatrices = new NativeArray<Matrix4x4>(capacity, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                pollResults = new NativeArray<int>(capacity, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                return;
            }
            if (required <= pollMatrices.Length) return;

            var grown = Mathf.NextPowerOfTwo(required);
            var newMatrices = new NativeArray<Matrix4x4>(grown, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<Matrix4x4>.Copy(pollMatrices, newMatrices, pollList.Count);
            pollMatrices.Dispose();
            pollMatrices = newMatrices;
            pollResults.Dispose();
            pollResults = new NativeArray<int>(grown, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            if (pollTransforms.capacity < grown) pollTransforms.capacity = grown;
        }

        private void DisposePollState()
        {
            if (pollTransforms.isCreated) pollTransforms.Dispose();
            if (pollMatrices.IsCreated) pollMatrices.Dispose();
            if (pollResults.IsCreated) pollResults.Dispose();
            pollList.Clear();
            sweepCursor = 0;
        }

        /// <summary>Drops <paramref name="source"/>'s slot and frees its geometry.</summary>
        public void Deactivate(IWorldBatchSource source)
        {
            if (source != null) DropSlot(source);
        }

        /// <summary>Pulls fresh segments from <paramref name="source"/> and merges/places them. The host calls this when the source raises new render data — the borrowed arrays are valid only for this call.</summary>
        public void Capture(IWorldBatchSource source)
        {
            if (Alive(source) && slots.TryGetValue(source, out var slot))
                CaptureComponent(source, slot);
        }

        /// <summary>Frees <paramref name="source"/>'s entries without dropping its slot (the source cleared its render data but stays active).</summary>
        public void ClearRenderData(IWorldBatchSource source)
        {
            if (!Alive(source) || !slots.TryGetValue(source, out var slot)) return;
            if (slot.entries.Count == 0) return;
            FreeAllEntries(slot);
            slot.ClearEntries();
        }

        /// <summary>Hide/show toggled: flips this source's entries to/from degenerate indices in place — an index-only re-upload, no structural rebuild and no GameObject churn.</summary>
        public void SuppressionChanged(IWorldBatchSource source)
        {
            if (!Alive(source) || !slots.TryGetValue(source, out var slot)) return;
            var suppressed = source.RenderSuppressed;
            if (slot.suppressed == suppressed) return;
            slot.suppressed = suppressed;
            for (var i = 0; i < slot.entries.Count; i++)
            {
                var entry = slot.entries[i];
                entry.suppressed = suppressed;
                MarkEntryIndexDirty(entry);
            }
        }

        /// <summary>Per-frame tick: detects moved and destroyed sources through a parallel transform job,
        /// runs the amortized context sweep, and uploads all dirty shards. The host calls this late in the
        /// frame, after the capture pass, before rendering. A fully static population costs the job's
        /// matrix compare and nothing on the sources themselves.</summary>
        public void Flush()
        {
            PollTransformDeltas();
            SweepContexts();
            FlushGroups();
        }

        [BurstCompile]
        private struct PollTransformsJob : IJobParallelForTransform
        {
            public NativeArray<Matrix4x4> matrices;
            public NativeArray<int> results;

            public void Execute(int index, TransformAccess transform)
            {
                if (!transform.isValid)
                {
                    results[index] = 2;
                    return;
                }
                var matrix = transform.localToWorldMatrix;
                if (matrix == matrices[index]) return;
                matrices[index] = matrix;
                results[index] = 1;
            }
        }

        private unsafe void PollTransformDeltas()
        {
            var count = pollList.Count;
            if (count == 0) return;

            UnsafeUtility.MemClear(pollResults.GetUnsafePtr(), (long)count * sizeof(int));
            new PollTransformsJob { matrices = pollMatrices, results = pollResults }
                .ScheduleReadOnly(pollTransforms, 128).Complete();

            sourceScratch.Clear();
            for (var i = 0; i < count; i++)
            {
                var result = pollResults[i];
                if (result == 0) continue;
                var slot = pollList[i];
                if (result == 2)
                {
                    sourceScratch.Add(slot.source);
                    continue;
                }
                for (var e = 0; e < slot.entries.Count; e++)
                    MarkEntryPositionalDirty(slot.entries[e]);
            }

            for (var i = 0; i < sourceScratch.Count; i++)
                DropSlot(sourceScratch[i]);
            sourceScratch.Clear();
        }

        private void SweepContexts()
        {
            var checks = Mathf.Min(ContextSweepBudget, pollList.Count);
            for (var k = 0; k < checks; k++)
            {
                if (pollList.Count == 0) return;
                if (sweepCursor >= pollList.Count) sweepCursor = 0;
                var slot = pollList[sweepCursor];
                var source = slot.source;
                if (!Alive(source))
                {
                    DropSlot(source);
                    continue;
                }
                if (slot.entries.Count > 0)
                {
                    var ctx = ResolveContext(source, slot);
                    if (!ctx.Matches(slot.lastContext)) RemapEntriesToCurrentKey(slot, ctx);
                }
                sweepCursor++;
            }
        }

        /// <summary>Re-resolves <paramref name="source"/>'s grouping context now and remaps its entries
        /// if it changed. The host calls this from its own sorting and re-parenting events; context
        /// inputs that raise no event are caught by the amortized per-flush sweep instead.</summary>
        public void ContextChanged(IWorldBatchSource source)
        {
            if (!Alive(source) || !slots.TryGetValue(source, out var slot)) return;
            if (slot.entries.Count == 0) return;
            var ctx = ResolveContext(source, slot);
            if (!ctx.Matches(slot.lastContext)) RemapEntriesToCurrentKey(slot, ctx);
        }

        private void DropSlot(IWorldBatchSource source)
        {
            if (!slots.TryGetValue(source, out var slot)) return;
            RemoveFromPoll(slot);
            FreeAllEntries(slot);
            slot.ReturnBuffers();
            slots.Remove(source);
        }

        private static KeyContext ResolveContext(IWorldBatchSource source, ComponentSlot slot)
        {
            var sortingGroup = FindSortingGroup(source.Transform);
            var go = slot.cachedGameObject;
            return new KeyContext
            {
                scene = go.scene,
                sortingGroup = sortingGroup,
                sortingGroupID = sortingGroup != null ? ObjectUtils.GetInstanceIdCompat(sortingGroup) : 0,
                batchGroupID = source.BatchGroupId,
                sortingLayerID = source.SortingLayerID,
                sortingOrder = source.SortingOrder,
                unityLayer = go.layer,
                castShadows = source.CastShadows,
            };
        }

        /// <summary>
        /// The nearest ENABLED <see cref="SortingGroup"/> at or above <paramref name="tr"/> — the one Unity
        /// actually sorts by. Resolved on every context check rather than cached: a group added to or removed
        /// from the hierarchy raises no event, and its <c>enabled</c> state can flip at any time.
        /// </summary>
        private static SortingGroup FindSortingGroup(Transform tr)
        {
            for (var t = tr; t != null; t = t.parent)
                if (t.TryGetComponent<SortingGroup>(out var group) && group.enabled)
                    return group;
            return null;
        }

        #endregion
    }
}
