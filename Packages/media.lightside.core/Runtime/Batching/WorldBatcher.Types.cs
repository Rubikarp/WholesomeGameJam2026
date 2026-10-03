using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LightSide
{
    public sealed partial class WorldBatcher
    {
        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            public readonly bool isValid;
            public readonly int sortingLayerID;
            public readonly int sortingOrder;
            public readonly int sortingGroupID;
            public readonly int batchGroupID;
            public readonly int unityLayer;
            public readonly ulong sceneHandle;
            public readonly bool castShadows;

            public bool IsValid => isValid;

            public BatchKey(int sortingLayerID, int sortingOrder, int sortingGroupID,
                int batchGroupID, int unityLayer, ulong sceneHandle, bool castShadows)
            {
                isValid = true;
                this.sortingLayerID = sortingLayerID;
                this.sortingOrder = sortingOrder;
                this.sortingGroupID = sortingGroupID;
                this.batchGroupID = batchGroupID;
                this.unityLayer = unityLayer;
                this.sceneHandle = sceneHandle;
                this.castShadows = castShadows;
            }

            public bool Equals(BatchKey other) =>
                isValid == other.isValid
                && sortingLayerID == other.sortingLayerID
                && sortingOrder == other.sortingOrder
                && sortingGroupID == other.sortingGroupID
                && batchGroupID == other.batchGroupID
                && unityLayer == other.unityLayer
                && sceneHandle == other.sceneHandle
                && castShadows == other.castShadows;

            public override bool Equals(object obj) => obj is BatchKey other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(sortingLayerID, sortingOrder, sortingGroupID, batchGroupID,
                    unityLayer, sceneHandle, castShadows);
        }

        /// <summary>Resolved per-component grouping context for one tick: the live <see cref="SortingGroup"/>
        /// and scene plus the scalar keys. Cached in the slot to detect sorting/scene changes between frames.</summary>
        private struct KeyContext
        {
            public Scene scene;
            public SortingGroup sortingGroup;
            public int sortingGroupID;
            public int batchGroupID;
            public int sortingLayerID;
            public int sortingOrder;
            public int unityLayer;
            public bool castShadows;

            public BatchKey ToKey() =>
                new BatchKey(sortingLayerID, sortingOrder, sortingGroupID, batchGroupID, unityLayer,
                    ObjectUtils.GetSceneHandleCompat(scene), castShadows);

            public bool Matches(KeyContext o) =>
                sortingGroupID == o.sortingGroupID
                && batchGroupID == o.batchGroupID
                && sortingLayerID == o.sortingLayerID
                && sortingOrder == o.sortingOrder
                && unityLayer == o.unityLayer
                && scene.handle == o.scene.handle
                && castShadows == o.castShadows;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PositionNormal
        {
            public Vector3 position;
            public Vector3 normal;
        }

        /// <summary>
        /// The interleaved attribute stream. Tangent comes first because Unity lays attributes out inside
        /// a stream in <see cref="VertexAttribute"/> order — Position, Normal, Tangent, Color, TexCoord0..7 —
        /// so appending it after the UVs would silently mis-map the buffer.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct TangentUv123
        {
            public Vector4 tangent;
            public Vector4 uv1;
            public Vector4 uv2;
            public Vector4 uv3;
        }

        private sealed class ComponentSlot
        {
            public IWorldBatchSource source;
            public Transform cachedTransform;
            public GameObject cachedGameObject;

            /// <summary>Index into the dense poll structures (<c>pollList</c> / <c>pollTransforms</c> /
            /// <c>pollMatrices</c>); -1 while not pooled. Maintained by swap-back removal.</summary>
            public int pollIndex = -1;

            public readonly List<SubMeshEntry> entries = new(2);
            private readonly Stack<SubMeshEntry> entryPool = new();
            private readonly Stack<List<SubMeshEntry>> workListPool = new();

            public bool suppressed;

            /// <summary>Sticky shard assignment: every entry of this component is placed into the same
            /// shard so the component's layer stack stays contiguous in one mesh (per-component draw
            /// atomicity). Revalidated against the current group key on each placement; a released
            /// shard or a context change picks a new shard for the whole component.</summary>
            public BatchShard shard;

            public KeyContext lastContext;

            public SubMeshEntry AcquireEntry()
            {
                return entryPool.Count > 0 ? entryPool.Pop() : new SubMeshEntry();
            }

            public void ReleaseEntry(SubMeshEntry e)
            {
                e.groupKey = default;
                e.material = null;
                e.materialInstanceId = 0;
                e.vertexCount = 0;
                e.triangleCount = 0;
                e.hasUv1 = e.hasUv2 = e.hasUv3 = e.hasTangents = false;
                e.sequence = 0;
                e.sortIndex = 0;
                e.componentId = 0;
                e.collectIndex = 0;
                e.source = null;
                e.lit = false;
                e.shard = null;
                e.vertexOffsetInMesh = 0;
                e.indexOffsetInMesh = 0;
                e.positionsChanged = false;
                e.indicesChanged = false;
                e.colorsChanged = false;
                e.uv0Changed = false;
                e.uv123Changed = false;
                e.inPositionalDirty = false;
                e.inAttributiveDirty = false;
                e.inIndexDirty = false;
                e.suppressed = false;
                e.vertices.FakeClear();
                e.uvs0.FakeClear();
                e.uvs1.FakeClear();
                e.uvs2.FakeClear();
                e.uvs3.FakeClear();
                e.tangents.FakeClear();
                e.colors.FakeClear();
                e.triangles.FakeClear();
                entryPool.Push(e);
            }

            public List<SubMeshEntry> AcquireWorkList()
            {
                return workListPool.Count > 0 ? workListPool.Pop() : new List<SubMeshEntry>(2);
            }

            public void ReleaseWorkList(List<SubMeshEntry> list)
            {
                list.Clear();
                workListPool.Push(list);
            }

            public void ClearEntries()
            {
                for (var i = 0; i < entries.Count; i++)
                    ReleaseEntry(entries[i]);
                entries.Clear();
            }

            public void ReturnBuffers()
            {
                for (var i = 0; i < entries.Count; i++)
                    entries[i].ReturnBuffers();
                entries.Clear();

                while (entryPool.Count > 0)
                    entryPool.Pop().ReturnBuffers();
            }
        }

        private sealed class SubMeshEntry
        {
            public Material material;
            public int materialInstanceId;
            public BatchKey groupKey;
            public int vertexCount;
            public int triangleCount;
            public bool hasUv1;
            public bool hasUv2;
            public bool hasUv3;
            public bool hasTangents;

            /// <summary>Segment identity used to pair this entry with the matching segment of the next
            /// re-collect (merge matching in <c>CaptureComponent</c>), so a mid-list segment insertion
            /// does not cascade structural rebuilds through the entries after it.</summary>
            public int sequence;
            public int sortIndex;

            /// <summary>Draw-order keys within a shard. Entries sort by (<see cref="componentId"/>,
            /// <see cref="collectIndex"/>): all of a component's segments stay contiguous and keep the
            /// exact order <c>CollectRenderData</c> produced (its list is already sequence-sorted per
            /// component). Cross-component layer interleaving is intentionally NOT supported — each
            /// component draws atomically; relative order of components in one shard is stable but
            /// unspecified. The pair is unique per entry, so the unstable <c>List.Sort</c> is total
            /// and cannot flicker.</summary>
            public int componentId;
            public int collectIndex;

            /// <summary>Owning source. Read during structural rebuild and positional updates for its current <c>localToWorldMatrix</c>.</summary>
            public IWorldBatchSource source;

            /// <summary>Per-segment lighting flag from the source; OR-ed into the shard's lit state at structural rebuild.</summary>
            public bool lit;

            public PooledBuffer<Vector3> vertices;
            public PooledBuffer<Vector4> uvs0;
            public PooledBuffer<Vector4> uvs1;
            public PooledBuffer<Vector4> uvs2;
            public PooledBuffer<Vector4> uvs3;
            public PooledBuffer<Vector4> tangents;
            public PooledBuffer<Color32> colors;
            public PooledBuffer<int> triangles;

            public BatchShard shard;
            public int vertexOffsetInMesh;
            public int indexOffsetInMesh;

            /// <summary>Set when a same-count re-collect changed the corresponding source stream — e.g. a
            /// paint-layer reorder permutes triangles without changing counts. Each flag drives a
            /// sub-range re-upload of just that stream; unchanged streams are neither copied nor uploaded.</summary>
            public bool positionsChanged;
            public bool indicesChanged;
            public bool colorsChanged;
            public bool uv0Changed;
            public bool uv123Changed;

            public bool inPositionalDirty;
            public bool inAttributiveDirty;
            public bool inIndexDirty;

            /// <summary>Hidden via Hide/scene-visibility: the entry keeps its mesh slot but its index range is written degenerate (zero-area), so a visibility toggle is an index-only re-upload, never a structural rebuild.</summary>
            public bool suppressed;

            public Vector3 localBoundsMin;
            public Vector3 localBoundsMax;

            public void ReturnBuffers()
            {
                vertices.Return();
                uvs0.Return();
                uvs1.Return();
                uvs2.Return();
                uvs3.Return();
                tangents.Return();
                colors.Return();
                triangles.Return();
            }
        }

        /// <summary>One sorting context (see <see cref="BatchKey"/>): a thin container of size-bounded
        /// <see cref="BatchShard"/>s. All mesh state lives per shard so a structural change re-bakes and
        /// re-uploads only the owning shard, never the whole context.</summary>
        private sealed class BatchGroup
        {
            public readonly BatchKey key;
            public readonly int sortingLayerID;
            public readonly int sortingOrder;
            public readonly int unityLayer;
            public readonly bool castShadows;
            public readonly Transform batcherTransform;

            public readonly List<BatchShard> shards = new(1);

            public bool IsValid => batcherTransform != null;
            public Matrix4x4 WorldToBatchMatrix => batcherTransform.worldToLocalMatrix;

            public BatchGroup(BatchKey key, Transform parent)
            {
                this.key = key;
                sortingLayerID = key.sortingLayerID;
                sortingOrder = key.sortingOrder;
                unityLayer = key.unityLayer;
                castShadows = key.castShadows;
                batcherTransform = parent;
            }

            public void Destroy()
            {
                for (var i = 0; i < shards.Count; i++)
                    shards[i].Destroy();
                shards.Clear();
            }
        }

        /// <summary>
        /// One combined mesh: a vertex-budgeted slice of a <see cref="BatchGroup"/>. Components are
        /// packed whole (sticky <see cref="ComponentSlot.shard"/>), so a component growing past the
        /// budget inflates its own shard instead of migrating — the budget bounds how much unrelated
        /// text a structural rebuild can touch, it is a packing target, not a hard cap.
        /// </summary>
        private sealed class BatchShard
        {
            public readonly BatchGroup group;

            public readonly List<SubMeshEntry> entries = new(4);

            public int usedVertexCount;
            public int usedIndexCount;

            public bool structuralDirty;

            /// <summary>Set by <see cref="Destroy"/>; sticky slot references revalidate against this
            /// before reusing the shard for a new placement.</summary>
            public bool released;

            /// <summary>True if any sub-mesh material in the shard is lit. When false, per-vertex normals are
            /// written as a constant rather than computed (cross + two normalizes per quad), since unlit shaders
            /// ignore them — saves that math on every structural rebuild and positional update.</summary>
            public bool anyLit;

            public readonly List<SubMeshEntry> positionalDirty = new();
            public readonly List<SubMeshEntry> attributiveDirty = new();
            public readonly List<SubMeshEntry> indexDirty = new();

            public PooledBuffer<PositionNormal> stream0;
            public PooledBuffer<Color32> stream1;
            public PooledBuffer<Vector4> stream2;
            public PooledBuffer<TangentUv123> stream3;

            /// <summary>Index staging in the mesh's actual format — one buffer active per
            /// <see cref="indexFormat"/>, so the common UInt16 shard uploads with no narrowing pass
            /// or second staging copy. The inactive buffer is returned on a format flip.</summary>
            public PooledBuffer<ushort> indices16;
            /// <inheritdoc cref="indices16"/>
            public PooledBuffer<int> indices32;

            /// <summary>Chosen per structural rebuild: UInt16 while the shard fits (halves index bandwidth,
            /// avoids the GLES2/WebGL1 UInt32 extension dependency), UInt32 only when a single oversized
            /// component pushes the shard past 65535 vertices.</summary>
            public IndexFormat indexFormat = IndexFormat.UInt16;

            /// <summary>Materials per sub-mesh, aligned with <see cref="subMeshRanges"/>; assigned to the
            /// renderer in entry order so each component's layers draw in Styles order.</summary>
            public readonly List<Material> subMeshMaterials = new(2);
            /// <summary>Sub-mesh descriptors, aligned with <see cref="subMeshMaterials"/>. Applied in one
            /// atomic <c>Mesh.SetSubMeshes</c> call: per-index <c>SetSubMesh</c> would leave a transient range
            /// overlapping the previous rebuild's layout whenever the sub-mesh count shrinks, which Unity treats
            /// as undefined behavior and corrupts the draw until the next clean rebuild.</summary>
            public readonly List<SubMeshDescriptor> subMeshRanges = new(2);

            /// <summary>Reused backing for <c>renderer.sharedMaterials</c>; regrown only when the sub-mesh count changes, so a same-shape structural rebuild assigns materials without allocating.</summary>
            private Material[] sharedMaterialsCache;

            public void AssignSharedMaterials(MeshRenderer renderer)
            {
                var count = subMeshMaterials.Count;
                if (sharedMaterialsCache == null || sharedMaterialsCache.Length != count)
                    sharedMaterialsCache = new Material[count];
                for (var i = 0; i < count; i++) sharedMaterialsCache[i] = subMeshMaterials[i];
                renderer.sharedMaterials = sharedMaterialsCache;
            }

            public Vector3 boundsMin;
            public Vector3 boundsMax;

            public BatchLayer layer;
            private int allocatedVertexCount;
            private int allocatedIndexCount;
            private IndexFormat allocatedIndexFormat = IndexFormat.UInt16;

            public BatchShard(BatchGroup group)
            {
                this.group = group;
            }

            public void EnsureMeshAllocated(int vertexCount, int indexCount, IndexFormat format)
            {
                if (layer == null || !layer.IsAlive)
                {
                    if (layer != null) DestroyLayerStatic(layer);
                    layer = CreateLayer();
                    allocatedVertexCount = 0;
                    allocatedIndexCount = 0;
                    allocatedIndexFormat = IndexFormat.UInt16;
                }

                var mesh = layer.mesh;
                if (vertexCount != allocatedVertexCount)
                {
                    mesh.SetVertexBufferParams(vertexCount, vertexLayout);
                    allocatedVertexCount = vertexCount;
                }
                if (indexCount != allocatedIndexCount || format != allocatedIndexFormat)
                {
                    mesh.SetIndexBufferParams(indexCount, format);
                    allocatedIndexCount = indexCount;
                    allocatedIndexFormat = format;
                }
                indexFormat = format;
            }

            public void ClearMesh()
            {
                if (layer == null) return;
                if (!layer.IsAlive)
                {
                    DestroyLayerStatic(layer);
                    layer = null;
                    allocatedVertexCount = 0;
                    allocatedIndexCount = 0;
                    return;
                }
                layer.mesh.Clear();
                layer.go.SetActive(false);
                allocatedVertexCount = 0;
                allocatedIndexCount = 0;
            }

            public void Destroy()
            {
                released = true;

                stream0.Return();
                stream1.Return();
                stream2.Return();
                stream3.Return();
                indices16.Return();
                indices32.Return();

                if (layer != null) DestroyLayerStatic(layer);
                layer = null;

                for (var i = 0; i < entries.Count; i++)
                    entries[i].shard = null;
                entries.Clear();
                ClearPositionalDirty(this);
                ClearAttributiveDirty(this);
                ClearIndexDirty(this);
                usedVertexCount = 0;
                usedIndexCount = 0;
            }

            private BatchLayer CreateLayer()
            {
                var go = new GameObject($"{BatchLayerNamePrefix}{group.sortingLayerID}_{group.sortingOrder}_-")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = group.unityLayer
                };
                go.transform.SetParent(group.batcherTransform, false);

                go.AddComponent<BatchMarker>();
                var filter = go.AddComponent<MeshFilter>();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sortingLayerID = group.sortingLayerID;
                renderer.sortingOrder = group.sortingOrder;

                var mesh = new Mesh
                {
                    name = $"WorldBatch_{group.sortingLayerID}_{group.sortingOrder}",
                    hideFlags = HideFlags.HideAndDontSave
                };
                mesh.MarkDynamic();
                filter.sharedMesh = mesh;

                return new BatchLayer { go = go, renderer = renderer, mesh = mesh };
            }

            private static void DestroyLayerStatic(BatchLayer layer)
            {
                if (layer == null) return;
                ObjectUtils.SafeDestroy(layer.mesh);
                ObjectUtils.SafeDestroy(layer.go);
            }
        }

        private sealed class BatchLayer
        {
            public GameObject go;
            public MeshRenderer renderer;
            public Mesh mesh;

            public bool IsAlive => go != null && renderer != null && mesh != null;
        }
    }
}
