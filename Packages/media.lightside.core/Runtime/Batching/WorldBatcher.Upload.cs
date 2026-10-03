using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LightSide
{
    public sealed partial class WorldBatcher
    {
        private static readonly List<BatchKey> deadGroupKeys = new();
        private static readonly List<Scene> deadRootKeys = new();

        private static readonly VertexAttributeDescriptor[] vertexLayout =
        {
            new(VertexAttribute.Position,  VertexAttributeFormat.Float32, 3, stream: 0),
            new(VertexAttribute.Normal,    VertexAttributeFormat.Float32, 3, stream: 0),
            new(VertexAttribute.Color,     VertexAttributeFormat.UNorm8,  4, stream: 1),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4, stream: 2),
            // Tangent precedes the UVs: Unity lays a stream out in VertexAttribute order.
            new(VertexAttribute.Tangent,   VertexAttributeFormat.Float32, 4, stream: 3),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4, stream: 3),
            new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4, stream: 3),
            new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4, stream: 3),
        };

        private const MeshUpdateFlags MeshUploadFlags =
            MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;

        private BatchGroup GetOrCreateGroup(BatchKey key, KeyContext ctx)
        {
            if (groups.TryGetValue(key, out var group))
            {
                if (group.IsValid) return group;
                group.Destroy();
                groups.Remove(key);
            }

            var parent = ctx.sortingGroup != null && ctx.sortingGroup.transform != null
                ? ctx.sortingGroup.transform
                : GetOrCreateSceneRoot(ctx.scene);

            if (parent == null) return null;

            group = new BatchGroup(key, parent);
            groups[key] = group;
            return group;
        }

        private Transform GetOrCreateSceneRoot(Scene scene)
        {
            if (sceneRoots.TryGetValue(scene, out var existing) && existing != null)
                return existing;

            var go = new GameObject(SceneRootName) { hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<BatchMarker>();

            if (scene.IsValid() && go.scene != scene)
            {
                if (Application.isPlaying && scene.buildIndex == -1 && scene.name == "DontDestroyOnLoad")
                    UnityEngine.Object.DontDestroyOnLoad(go);
                else if (scene.isLoaded)
                    SceneManager.MoveGameObjectToScene(go, scene);
            }

            sceneRoots[scene] = go.transform;
            return go.transform;
        }

        private void FlushGroups()
        {
            deadGroupKeys.Clear();
            foreach (var kvp in groups)
                if (!kvp.Value.IsValid) { kvp.Value.Destroy(); deadGroupKeys.Add(kvp.Key); }
            for (var i = 0; i < deadGroupKeys.Count; i++) groups.Remove(deadGroupKeys[i]);

            deadGroupKeys.Clear();
            foreach (var kvp in groups)
            {
                var group = kvp.Value;
                var shards = group.shards;
                for (var i = shards.Count - 1; i >= 0; i--)
                {
                    if (shards[i].entries.Count == 0)
                    {
                        shards[i].Destroy();
                        shards.RemoveAt(i);
                    }
                }

                if (shards.Count == 0)
                {
                    group.Destroy();
                    deadGroupKeys.Add(kvp.Key);
                    continue;
                }

                for (var i = 0; i < shards.Count; i++)
                    ProcessShard(shards[i]);
            }
            for (var i = 0; i < deadGroupKeys.Count; i++) groups.Remove(deadGroupKeys[i]);

            if (sceneRoots.Count > 0) ReclaimSceneRoots();
        }

        private void ReclaimSceneRoots()
        {
            deadRootKeys.Clear();
            foreach (var kvp in sceneRoots)
            {
                var root = kvp.Value;
                if (root == null) { deadRootKeys.Add(kvp.Key); continue; }
                if (root.childCount == 0)
                {
                    ObjectUtils.SafeDestroy(root.gameObject);
                    deadRootKeys.Add(kvp.Key);
                }
            }
            for (var i = 0; i < deadRootKeys.Count; i++) sceneRoots.Remove(deadRootKeys[i]);
        }

        private static void ProcessShard(BatchShard shard)
        {
            if (shard.structuralDirty)
            {
                RebuildShardStructural(shard);
                shard.structuralDirty = false;
                ClearPositionalDirty(shard);
                ClearAttributiveDirty(shard);
                ClearIndexDirty(shard);
                return;
            }

            if (shard.positionalDirty.Count > 0)
                FlushPositionalDirty(shard);

            if (shard.attributiveDirty.Count > 0)
            {
                var list = shard.attributiveDirty;
                for (var i = 0; i < list.Count; i++)
                {
                    var entry = list[i];
                    entry.inAttributiveDirty = false;
                    if (entry.colorsChanged) { WriteEntryColorStream(shard, entry); entry.colorsChanged = false; }
                    if (entry.uv0Changed) { WriteEntryUv0Stream(shard, entry); entry.uv0Changed = false; }
                    if (entry.uv123Changed) { WriteEntryUv123Stream(shard, entry); entry.uv123Changed = false; }
                }
                list.Clear();
            }

            if (shard.indexDirty.Count > 0)
            {
                var list = shard.indexDirty;
                for (var i = 0; i < list.Count; i++)
                {
                    var entry = list[i];
                    entry.inIndexDirty = false;
                    WriteEntryIndexStream(shard, entry);
                }
                list.Clear();

                var mesh = shard.layer != null ? shard.layer.mesh : null;
                if (mesh != null) ApplyShardBounds(shard, mesh);
            }
        }

        /// <summary>
        /// Re-bakes all positionally-dirty entries into the position stream, then uploads either the
        /// single merged span they cover (many movers close in mesh order — one <c>SetVertexBufferData</c>)
        /// or one range per entry when the merged span would mostly cover untouched vertices (sparse
        /// movers at opposite ends of the shard). Untouched staging data inside a merged span is still
        /// valid, so the merged upload is always correct — the split is purely a bandwidth heuristic.
        /// </summary>
        private static void FlushPositionalDirty(BatchShard shard)
        {
            var list = shard.positionalDirty;
            shard.stream0.EnsureCount(shard.usedVertexCount);

            var minV = int.MaxValue;
            var maxV = 0;
            var dirtyVerts = 0;
            for (var i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                entry.inPositionalDirty = false;
                if (entry.shard != shard || !Alive(entry.source)) continue;

                BakeEntryPositions(shard, entry);
                var off = entry.vertexOffsetInMesh;
                if (off < minV) minV = off;
                var end = off + entry.vertexCount;
                if (end > maxV) maxV = end;
                dirtyVerts += entry.vertexCount;
            }

            if (maxV <= minV) { list.Clear(); return; }
            var mesh = shard.layer != null ? shard.layer.mesh : null;
            if (mesh == null) { list.Clear(); return; }

            if (maxV - minV <= dirtyVerts * 4)
            {
                mesh.SetVertexBufferData(shard.stream0.data, minV, minV, maxV - minV, stream: 0, MeshUploadFlags);
            }
            else
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var entry = list[i];
                    if (entry.shard != shard || !Alive(entry.source)) continue;
                    var off = entry.vertexOffsetInMesh;
                    mesh.SetVertexBufferData(shard.stream0.data, off, off, entry.vertexCount, stream: 0, MeshUploadFlags);
                }
            }
            list.Clear();

            ApplyShardBounds(shard, mesh);
        }

        private static void RebuildShardStructural(BatchShard shard)
        {
            var totalV = shard.usedVertexCount;
            var totalT = shard.usedIndexCount;

            if (totalV == 0 || totalT == 0)
            {
                shard.ClearMesh();
                return;
            }

            shard.entries.Sort(EntryOrderComparer.Instance);
            shard.anyLit = AnyLit(shard.entries);

            var format = totalV > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            if (format != shard.indexFormat)
            {
                if (format == IndexFormat.UInt16) shard.indices32.Return();
                else shard.indices16.Return();
                shard.indexFormat = format;
            }

            shard.stream0.EnsureCount(totalV);
            shard.stream1.EnsureCount(totalV);
            shard.stream2.EnsureCount(totalV);
            shard.stream3.EnsureCount(totalV);
            if (format == IndexFormat.UInt32) shard.indices32.EnsureCount(totalT);
            else shard.indices16.EnsureCount(totalT);

            var batchMatrix = shard.group.WorldToBatchMatrix;

            shard.subMeshMaterials.Clear();
            shard.subMeshRanges.Clear();

            var vOff = 0;
            var tOff = 0;
            var runStart = 0;
            Material runMat = null;

            for (var i = 0; i < shard.entries.Count; i++)
            {
                var entry = shard.entries[i];

                if (i == 0 || !ReferenceEquals(entry.material, runMat))
                {
                    if (i > 0)
                        shard.subMeshRanges.Add(new SubMeshDescriptor(runStart, tOff - runStart));
                    runMat = entry.material;
                    runStart = tOff;
                    shard.subMeshMaterials.Add(runMat);
                }

                entry.vertexOffsetInMesh = vOff;
                entry.indexOffsetInMesh = tOff;

                FillEntryIntoStagingStreams(shard, entry, batchMatrix, vOff, tOff);

                vOff += entry.vertexCount;
                tOff += entry.triangleCount;
            }
            shard.subMeshRanges.Add(new SubMeshDescriptor(runStart, tOff - runStart));

            shard.EnsureMeshAllocated(totalV, totalT, format);

            var mesh = shard.layer.mesh;
            mesh.SetVertexBufferData(shard.stream0.data, 0, 0, totalV, stream: 0, MeshUploadFlags);
            mesh.SetVertexBufferData(shard.stream1.data, 0, 0, totalV, stream: 1, MeshUploadFlags);
            mesh.SetVertexBufferData(shard.stream2.data, 0, 0, totalV, stream: 2, MeshUploadFlags);
            mesh.SetVertexBufferData(shard.stream3.data, 0, 0, totalV, stream: 3, MeshUploadFlags);
            UploadIndexRange(shard, mesh, 0, totalT);

            mesh.SetSubMeshes(shard.subMeshRanges, MeshUploadFlags);

            ApplyShardBounds(shard, mesh);

            shard.layer.go.SetActive(true);
            shard.AssignSharedMaterials(shard.layer.renderer);
            shard.layer.renderer.sortingLayerID = shard.group.sortingLayerID;
            shard.layer.renderer.sortingOrder = shard.group.sortingOrder;

            shard.layer.renderer.shadowCastingMode = shard.group.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            shard.layer.renderer.receiveShadows = shard.anyLit;
        }

        /// <summary>True if any entry carries the source's per-segment lit flag; drives per-vertex normal computation and the renderer's receive-shadows.</summary>
        private static bool AnyLit(List<SubMeshEntry> entries)
        {
            for (var i = 0; i < entries.Count; i++)
                if (entries[i].lit) return true;
            return false;
        }

        /// <summary>Suppressed entries are excluded: their indices are degenerate and draw nothing, so a
        /// hidden far-away text must not inflate the shard's bounds and defeat frustum culling for the
        /// visible rest.</summary>
        private static void RecomputeShardBounds(BatchShard shard)
        {
            var entries = shard.entries;
            var min = Vector3.zero;
            var max = Vector3.zero;
            var hasAny = false;

            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.suppressed) continue;
                if (!hasAny)
                {
                    min = e.localBoundsMin;
                    max = e.localBoundsMax;
                    hasAny = true;
                    continue;
                }
                if (e.localBoundsMin.x < min.x) min.x = e.localBoundsMin.x;
                if (e.localBoundsMin.y < min.y) min.y = e.localBoundsMin.y;
                if (e.localBoundsMin.z < min.z) min.z = e.localBoundsMin.z;
                if (e.localBoundsMax.x > max.x) max.x = e.localBoundsMax.x;
                if (e.localBoundsMax.y > max.y) max.y = e.localBoundsMax.y;
                if (e.localBoundsMax.z > max.z) max.z = e.localBoundsMax.z;
            }

            shard.boundsMin = min;
            shard.boundsMax = max;
        }

        private static void ApplyShardBounds(BatchShard shard, Mesh mesh)
        {
            RecomputeShardBounds(shard);
            var center = (shard.boundsMin + shard.boundsMax) * 0.5f;
            var size = shard.boundsMax - shard.boundsMin;
            mesh.bounds = new Bounds(center, size);
        }

        private static void FillEntryIntoStagingStreams(
            BatchShard shard, SubMeshEntry entry, Matrix4x4 batchMatrix, int vOff, int tOff)
        {
            var worldMatrix = Alive(entry.source) ? entry.source.Transform.localToWorldMatrix : Matrix4x4.identity;
            var vc = entry.vertexCount;

            var srcVerts = entry.vertices.data;
            var s0 = shard.stream0.data;

            WritePositionsAndNormals(
                s0, vOff, srcVerts, vc,
                in worldMatrix, in batchMatrix, shard.anyLit,
                out var entryMin, out var entryMax);

            entry.localBoundsMin = entryMin;
            entry.localBoundsMax = entryMax;

            Array.Copy(entry.colors.data, 0, shard.stream1.data, vOff, vc);
            Array.Copy(entry.uvs0.data, 0, shard.stream2.data, vOff, vc);

            WriteUv123Block(shard.stream3.data, vOff, vc, entry);

            WriteEntryIndicesToStaging(shard, entry, tOff, vOff);

            entry.positionsChanged = false;
            entry.indicesChanged = false;
            entry.colorsChanged = false;
            entry.uv0Changed = false;
            entry.uv123Changed = false;
        }

        private sealed class EntryOrderComparer : IComparer<SubMeshEntry>
        {
            public static readonly EntryOrderComparer Instance = new();

            public int Compare(SubMeshEntry a, SubMeshEntry b)
            {
                if (a.componentId != b.componentId) return a.componentId < b.componentId ? -1 : 1;
                return a.collectIndex.CompareTo(b.collectIndex);
            }
        }

        /// <summary>Writes one entry's rebased (or degenerate, when suppressed) indices into the shard's
        /// format-native staging buffer — ushort for UInt16 shards, int for UInt32 — so uploads send the
        /// staging bytes directly with no narrowing pass.</summary>
        private static void WriteEntryIndicesToStaging(BatchShard shard, SubMeshEntry entry, int tOff, int vOff)
        {
            var tc = entry.triangleCount;
            var src = entry.triangles.data;

            if (shard.indexFormat == IndexFormat.UInt32)
            {
                var dst = shard.indices32.data;
                if (entry.suppressed)
                    for (var i = 0; i < tc; i++)
                        dst[tOff + i] = vOff;
                else
                    for (var i = 0; i < tc; i++)
                        dst[tOff + i] = src[i] + vOff;
            }
            else
            {
                var dst = shard.indices16.data;
                if (entry.suppressed)
                    for (var i = 0; i < tc; i++)
                        dst[tOff + i] = (ushort)vOff;
                else
                    for (var i = 0; i < tc; i++)
                        dst[tOff + i] = (ushort)(src[i] + vOff);
            }
        }

        /// <summary>Uploads a sub-range of the shard's format-native index staging.</summary>
        private static void UploadIndexRange(BatchShard shard, Mesh mesh, int start, int count)
        {
            if (shard.indexFormat == IndexFormat.UInt32)
                mesh.SetIndexBufferData(shard.indices32.data, start, start, count, MeshUploadFlags);
            else
                mesh.SetIndexBufferData(shard.indices16.data, start, start, count, MeshUploadFlags);
        }

        /// <summary>
        /// Re-writes one entry's index sub-range into the shard buffer and uploads it — for a same-count
        /// re-collect that permuted the triangle order (paint-layer reorder), or a visibility toggle
        /// that flips the entry to/from degenerate (suppressed) indices. Offsets stay valid because the
        /// mesh layout is unchanged.
        /// </summary>
        private static void WriteEntryIndexStream(BatchShard shard, SubMeshEntry entry)
        {
            entry.indicesChanged = false;
            if (entry.shard != shard) return;
            if (shard.layer == null || shard.layer.mesh == null) return;

            if (shard.indexFormat == IndexFormat.UInt32) shard.indices32.EnsureCount(shard.usedIndexCount);
            else shard.indices16.EnsureCount(shard.usedIndexCount);

            var baseOff = entry.indexOffsetInMesh;
            WriteEntryIndicesToStaging(shard, entry, baseOff, entry.vertexOffsetInMesh);
            UploadIndexRange(shard, shard.layer.mesh, baseOff, entry.triangleCount);
        }

        private static void BakeEntryPositions(BatchShard shard, SubMeshEntry entry)
        {
            var worldMatrix = entry.source.Transform.localToWorldMatrix;
            var batchMatrix = shard.group.WorldToBatchMatrix;

            WritePositionsAndNormals(
                shard.stream0.data, entry.vertexOffsetInMesh, entry.vertices.data, entry.vertexCount,
                in worldMatrix, in batchMatrix, shard.anyLit,
                out var entryMin, out var entryMax);

            entry.localBoundsMin = entryMin;
            entry.localBoundsMax = entryMax;
        }

        private static readonly Vector3 unlitNormal = new(0f, 0f, -1f);

        private static void WritePositionsAndNormals(
            PositionNormal[] s0, int baseOff,
            Vector3[] srcVerts, int vc,
            in Matrix4x4 worldMatrix, in Matrix4x4 batchMatrix, bool computeNormals,
            out Vector3 entryMin, out Vector3 entryMax)
        {
            var matrix = batchMatrix * worldMatrix;
            var quadCount = vc >> 2;

            entryMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            entryMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (var q = 0; q < quadCount; q++)
            {
                var baseIdx = q * 4;
                Vector3 worldNormal;
                if (computeNormals)
                {
                    var v0 = srcVerts[baseIdx];
                    var v1 = srcVerts[baseIdx + 1];
                    var v3 = srcVerts[baseIdx + 3];
                    var localNormal = Vector3.Cross(v1 - v0, v3 - v0);
                    if (localNormal.sqrMagnitude > 1e-12f) localNormal.Normalize();
                    worldNormal = worldMatrix.MultiplyVector(localNormal);
                    if (worldNormal.sqrMagnitude > 1e-12f) worldNormal.Normalize();
                }
                else
                {
                    worldNormal = unlitNormal;
                }

                for (var i = 0; i < 4; i++)
                {
                    ref var p = ref s0[baseOff + baseIdx + i];
                    var pos = matrix.MultiplyPoint3x4(srcVerts[baseIdx + i]);
                    p.position = pos;
                    p.normal = worldNormal;

                    if (pos.x < entryMin.x) entryMin.x = pos.x;
                    if (pos.y < entryMin.y) entryMin.y = pos.y;
                    if (pos.z < entryMin.z) entryMin.z = pos.z;
                    if (pos.x > entryMax.x) entryMax.x = pos.x;
                    if (pos.y > entryMax.y) entryMax.y = pos.y;
                    if (pos.z > entryMax.z) entryMax.z = pos.z;
                }
            }
        }

        private static void WriteEntryColorStream(BatchShard shard, SubMeshEntry entry)
        {
            if (entry.shard != shard) return;
            if (shard.layer == null || shard.layer.mesh == null) return;

            var vc = entry.vertexCount;
            var baseOff = entry.vertexOffsetInMesh;

            shard.stream1.EnsureCount(shard.usedVertexCount);
            Array.Copy(entry.colors.data, 0, shard.stream1.data, baseOff, vc);

            shard.layer.mesh.SetVertexBufferData(shard.stream1.data, baseOff, baseOff, vc, stream: 1,
                MeshUploadFlags);
        }

        private static void WriteEntryUv0Stream(BatchShard shard, SubMeshEntry entry)
        {
            if (entry.shard != shard) return;
            if (shard.layer == null || shard.layer.mesh == null) return;

            var vc = entry.vertexCount;
            var baseOff = entry.vertexOffsetInMesh;

            shard.stream2.EnsureCount(shard.usedVertexCount);
            Array.Copy(entry.uvs0.data, 0, shard.stream2.data, baseOff, vc);

            shard.layer.mesh.SetVertexBufferData(shard.stream2.data, baseOff, baseOff, vc, stream: 2,
                MeshUploadFlags);
        }

        private static void WriteEntryUv123Stream(BatchShard shard, SubMeshEntry entry)
        {
            if (entry.shard != shard) return;
            if (shard.layer == null || shard.layer.mesh == null) return;

            var vc = entry.vertexCount;
            var baseOff = entry.vertexOffsetInMesh;

            shard.stream3.EnsureCount(shard.usedVertexCount);
            var s3 = shard.stream3.data;

            WriteUv123Block(s3, baseOff, vc, entry);

            shard.layer.mesh.SetVertexBufferData(s3, baseOff, baseOff, vc, stream: 3,
                MeshUploadFlags);
        }

        private static void WriteUv123Block(TangentUv123[] s3, int baseOff, int vc, SubMeshEntry entry)
        {
            if (entry.hasTangents)
            {
                var srcT = entry.tangents.data;
                for (var i = 0; i < vc; i++) s3[baseOff + i].tangent = srcT[i];
            }
            else
            {
                for (var i = 0; i < vc; i++) s3[baseOff + i].tangent = default;
            }
            if (entry.hasUv1)
            {
                var src = entry.uvs1.data;
                for (var i = 0; i < vc; i++) s3[baseOff + i].uv1 = src[i];
            }
            else
            {
                for (var i = 0; i < vc; i++) s3[baseOff + i].uv1 = default;
            }
            if (entry.hasUv2)
            {
                var src = entry.uvs2.data;
                for (var i = 0; i < vc; i++) s3[baseOff + i].uv2 = src[i];
            }
            else
            {
                for (var i = 0; i < vc; i++) s3[baseOff + i].uv2 = default;
            }
            if (entry.hasUv3)
            {
                var src = entry.uvs3.data;
                for (var i = 0; i < vc; i++) s3[baseOff + i].uv3 = src[i];
            }
            else
            {
                for (var i = 0; i < vc; i++) s3[baseOff + i].uv3 = default;
            }
        }
    }
}
