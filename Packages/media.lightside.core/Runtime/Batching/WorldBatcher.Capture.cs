using System;
using UnityEngine;

namespace LightSide
{
    public sealed partial class WorldBatcher
    {
        /// <summary>Pulls the source's fully-resolved segments and merges them into batch entries — reusing entries that match by (sequence, sortIndex), structurally rebuilding those whose shape/material changed, and freeing the stale tail. An empty <see cref="IWorldBatchSource.Collect"/> is a no-op that leaves the slot untouched (matching the old HasGeneratedData early-return defense).</summary>
        private void CaptureComponent(IWorldBatchSource source, ComponentSlot slot)
        {
            captureBuffer.Clear();
            source.Collect(captureBuffer);
            if (captureBuffer.Count == 0) return;

            var ctx = ResolveContext(source, slot);
            slot.suppressed = source.RenderSuppressed;

            var componentId = ObjectUtils.GetInstanceIdCompat((UnityEngine.Object)source);
            var groupKey = ctx.ToKey();

            var prevEntries = slot.entries;
            var reuseIndex = 0;

            var newEntries = slot.AcquireWorkList();
            newEntries.Clear();

            for (var i = 0; i < captureBuffer.Count; i++)
            {
                var data = captureBuffer[i];
                if (data.vertexCount <= 0 || data.triangleCount <= 0) continue;

                var material = data.material;
                if (material == null) continue;

                var materialInstanceId = ObjectUtils.GetInstanceIdCompat(material);

                SubMeshEntry entry = null;
                while (reuseIndex < prevEntries.Count)
                {
                    var prev = prevEntries[reuseIndex];
                    if (prev == null) { reuseIndex++; continue; }
                    var cmp = prev.sequence != data.sequence
                        ? (prev.sequence < data.sequence ? -1 : 1)
                        : prev.sortIndex.CompareTo(data.sortIndex);
                    if (cmp < 0) { reuseIndex++; continue; }
                    if (cmp == 0)
                    {
                        entry = prev;
                        prevEntries[reuseIndex] = null;
                        reuseIndex++;
                    }
                    break;
                }

                var isNewEntry = entry == null;
                if (isNewEntry)
                    entry = slot.AcquireEntry();

                var newHasUv1 = data.hasUv1 && data.uvs1 != null;
                var newHasUv2 = data.hasUv2 && data.uvs2 != null;
                var newHasUv3 = data.hasUv3 && data.uvs3 != null;
                var newHasTangents = data.tangents != null;

                var structurallyChanged =
                    isNewEntry
                    || entry.vertexCount != data.vertexCount
                    || entry.triangleCount != data.triangleCount
                    || entry.hasUv1 != newHasUv1
                    || entry.hasUv2 != newHasUv2
                    || entry.hasUv3 != newHasUv3
                    || entry.hasTangents != newHasTangents
                    || entry.materialInstanceId != materialInstanceId
                    || entry.sequence != data.sequence
                    || entry.sortIndex != data.sortIndex
                    || entry.shard == null;

                entry.componentId = componentId;
                entry.collectIndex = newEntries.Count;
                entry.lit = data.lit;

                if (structurallyChanged)
                {
                    if (entry.shard != null)
                        FreeEntryFromShard(entry);

                    entry.material = material;
                    entry.materialInstanceId = materialInstanceId;
                    entry.vertexCount = data.vertexCount;
                    entry.triangleCount = data.triangleCount;
                    entry.hasUv1 = newHasUv1;
                    entry.hasUv2 = newHasUv2;
                    entry.hasUv3 = newHasUv3;
                    entry.hasTangents = newHasTangents;
                    entry.sequence = data.sequence;
                    entry.sortIndex = data.sortIndex;
                    entry.groupKey = groupKey;
                    entry.source = source;
                    entry.suppressed = slot.suppressed;

                    CopyEntrySourceData(entry, in data, trackChanges: false);

                    PlaceEntry(slot, groupKey, ctx, entry);
                }
                else
                {
                    entry.source = source;
                    var wasSuppressed = entry.suppressed;
                    entry.suppressed = slot.suppressed;
                    CopyEntrySourceData(entry, in data, trackChanges: true);
                    if (entry.colorsChanged || entry.uv0Changed || entry.uv123Changed)
                        MarkEntryAttributiveDirty(entry);
                    if (entry.positionsChanged) MarkEntryPositionalDirty(entry);
                    if (entry.suppressed != wasSuppressed || (!entry.suppressed && entry.indicesChanged))
                        MarkEntryIndexDirty(entry);
                }

                newEntries.Add(entry);
            }

            for (var k = 0; k < prevEntries.Count; k++)
            {
                var stale = prevEntries[k];
                if (stale == null) continue;
                FreeEntryFromShard(stale);
                slot.ReleaseEntry(stale);
            }

            slot.entries.Clear();
            for (var k = 0; k < newEntries.Count; k++)
                slot.entries.Add(newEntries[k]);
            slot.ReleaseWorkList(newEntries);

            slot.lastContext = ctx;

            var matrix = source.Transform.localToWorldMatrix;
            var moved = matrix != pollMatrices[slot.pollIndex];
            pollMatrices[slot.pollIndex] = matrix;

            if (moved)
                for (var i = 0; i < slot.entries.Count; i++)
                    MarkEntryPositionalDirty(slot.entries[i]);
        }

        private static void CopyEntrySourceData(SubMeshEntry entry, in BatchSegment data, bool trackChanges)
        {
            var vc = data.vertexCount;
            var tc = data.triangleCount;
            var off = data.vertexOffset;

            entry.vertices.EnsureCount(vc);
            entry.uvs0.EnsureCount(vc);
            entry.colors.EnsureCount(vc);
            if (entry.hasUv1) entry.uvs1.EnsureCount(vc);
            if (entry.hasUv2) entry.uvs2.EnsureCount(vc);
            if (entry.hasUv3) entry.uvs3.EnsureCount(vc);
            if (entry.hasTangents) entry.tangents.EnsureCount(vc);
            entry.triangles.EnsureCount(tc);

            if (trackChanges)
            {
                entry.positionsChanged = CopyDetect(data.vertices, off, entry.vertices.data, vc);
                entry.uv0Changed = CopyDetect(data.uvs0, off, entry.uvs0.data, vc);
                entry.colorsChanged = CopyDetect(data.colors, off, entry.colors.data, vc);
                var uvChanged = false;
                if (entry.hasUv1) uvChanged |= CopyDetect(data.uvs1, off, entry.uvs1.data, vc);
                if (entry.hasUv2) uvChanged |= CopyDetect(data.uvs2, off, entry.uvs2.data, vc);
                if (entry.hasUv3) uvChanged |= CopyDetect(data.uvs3, off, entry.uvs3.data, vc);
                if (entry.hasTangents) uvChanged |= CopyDetect(data.tangents, off, entry.tangents.data, vc);
                entry.uv123Changed = uvChanged;
                entry.indicesChanged = CopyDetect(data.triangles, data.triangleOffset, entry.triangles.data, tc);
            }
            else
            {
                Array.Copy(data.vertices, off, entry.vertices.data, 0, vc);
                Array.Copy(data.uvs0, off, entry.uvs0.data, 0, vc);
                Array.Copy(data.colors, off, entry.colors.data, 0, vc);
                if (entry.hasUv1) Array.Copy(data.uvs1, off, entry.uvs1.data, 0, vc);
                if (entry.hasUv2) Array.Copy(data.uvs2, off, entry.uvs2.data, 0, vc);
                if (entry.hasUv3) Array.Copy(data.uvs3, off, entry.uvs3.data, 0, vc);
                if (entry.hasTangents) Array.Copy(data.tangents, off, entry.tangents.data, 0, vc);
                Array.Copy(data.triangles, data.triangleOffset, entry.triangles.data, 0, tc);
                entry.positionsChanged = false;
                entry.indicesChanged = false;
                entry.colorsChanged = false;
                entry.uv0Changed = false;
                entry.uv123Changed = false;
            }
        }

        /// <summary>Componentwise exact float compare — Unity's <c>Vector3/Vector4 ==</c> is approximate (1e-5), which would silently drop sub-epsilon source changes from the change detector.</summary>
        private static bool CopyDetect(Vector3[] src, int srcOff, Vector3[] dst, int count)
        {
            var i = 0;
            while (i < count)
            {
                var a = src[srcOff + i];
                var b = dst[i];
                if (a.x != b.x || a.y != b.y || a.z != b.z) break;
                i++;
            }
            if (i == count) return false;
            Array.Copy(src, srcOff + i, dst, i, count - i);
            return true;
        }

        /// <inheritdoc cref="CopyDetect(Vector3[], int, Vector3[], int)"/>
        private static bool CopyDetect(Vector4[] src, int srcOff, Vector4[] dst, int count)
        {
            var i = 0;
            while (i < count)
            {
                var a = src[srcOff + i];
                var b = dst[i];
                if (a.x != b.x || a.y != b.y || a.z != b.z || a.w != b.w) break;
                i++;
            }
            if (i == count) return false;
            Array.Copy(src, srcOff + i, dst, i, count - i);
            return true;
        }

        private static bool CopyDetect(Color32[] src, int srcOff, Color32[] dst, int count)
        {
            var i = 0;
            while (i < count)
            {
                var a = src[srcOff + i];
                var b = dst[i];
                if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) break;
                i++;
            }
            if (i == count) return false;
            Array.Copy(src, srcOff + i, dst, i, count - i);
            return true;
        }

        private static bool CopyDetect(int[] src, int srcOff, int[] dst, int count)
        {
            var i = 0;
            while (i < count && src[srcOff + i] == dst[i]) i++;
            if (i == count) return false;
            Array.Copy(src, srcOff + i, dst, i, count - i);
            return true;
        }

        private static void FreeAllEntries(ComponentSlot slot)
        {
            for (var i = 0; i < slot.entries.Count; i++)
                FreeEntryFromShard(slot.entries[i]);
        }

        private static void FreeEntryFromShard(SubMeshEntry entry)
        {
            var shard = entry.shard;
            if (shard == null) return;
            shard.entries.Remove(entry);
            shard.usedVertexCount -= entry.vertexCount;
            shard.usedIndexCount -= entry.triangleCount;
            shard.structuralDirty = true;
            if (entry.inPositionalDirty) { shard.positionalDirty.Remove(entry); entry.inPositionalDirty = false; }
            if (entry.inAttributiveDirty) { shard.attributiveDirty.Remove(entry); entry.inAttributiveDirty = false; }
            if (entry.inIndexDirty) { shard.indexDirty.Remove(entry); entry.inIndexDirty = false; }
            entry.shard = null;
            entry.vertexOffsetInMesh = 0;
            entry.indexOffsetInMesh = 0;
        }

        private static void MarkEntryPositionalDirty(SubMeshEntry entry)
        {
            var shard = entry.shard;
            if (shard == null || shard.structuralDirty || entry.inPositionalDirty) return;
            entry.inPositionalDirty = true;
            shard.positionalDirty.Add(entry);
        }

        private static void MarkEntryAttributiveDirty(SubMeshEntry entry)
        {
            var shard = entry.shard;
            if (shard == null || shard.structuralDirty || entry.inAttributiveDirty) return;
            entry.inAttributiveDirty = true;
            shard.attributiveDirty.Add(entry);
        }

        private static void MarkEntryIndexDirty(SubMeshEntry entry)
        {
            var shard = entry.shard;
            if (shard == null || shard.structuralDirty || entry.inIndexDirty) return;
            entry.inIndexDirty = true;
            shard.indexDirty.Add(entry);
        }

        private static void ClearPositionalDirty(BatchShard shard)
        {
            var list = shard.positionalDirty;
            for (var i = 0; i < list.Count; i++) list[i].inPositionalDirty = false;
            list.Clear();
        }

        private static void ClearAttributiveDirty(BatchShard shard)
        {
            var list = shard.attributiveDirty;
            for (var i = 0; i < list.Count; i++) list[i].inAttributiveDirty = false;
            list.Clear();
        }

        private static void ClearIndexDirty(BatchShard shard)
        {
            var list = shard.indexDirty;
            for (var i = 0; i < list.Count; i++) list[i].inIndexDirty = false;
            list.Clear();
        }

        private void PlaceEntry(ComponentSlot slot, BatchKey key, KeyContext ctx, SubMeshEntry entry)
        {
            var group = GetOrCreateGroup(key, ctx);
            if (group == null) return;

            var shard = slot.shard;
            if (shard == null || shard.released || !shard.group.key.Equals(key))
                shard = slot.shard = SelectShard(group, entry.vertexCount);

            entry.shard = shard;
            shard.entries.Add(entry);
            shard.usedVertexCount += entry.vertexCount;
            shard.usedIndexCount += entry.triangleCount;
            shard.structuralDirty = true;
        }

        private BatchShard SelectShard(BatchGroup group, int vertexCount)
        {
            var budget = Mathf.Max(64, shardTargetVertexCount);
            for (var i = 0; i < group.shards.Count; i++)
            {
                var shard = group.shards[i];
                if (!shard.released && shard.usedVertexCount + vertexCount <= budget)
                    return shard;
            }

            var fresh = new BatchShard(group);
            group.shards.Add(fresh);
            return fresh;
        }

        private void RemapEntriesToCurrentKey(ComponentSlot slot, KeyContext ctx)
        {
            var newKey = ctx.ToKey();
            slot.shard = null;

            for (var i = 0; i < slot.entries.Count; i++)
            {
                var entry = slot.entries[i];
                FreeEntryFromShard(entry);
                entry.groupKey = newKey;
                PlaceEntry(slot, newKey, ctx, entry);
            }

            slot.lastContext = ctx;
        }
    }
}
