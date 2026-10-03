using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>One slice of an ordered decoration group inside a texture-and-blend-compatible batch.</summary>
    internal struct RangeDecorationDrawSlice
    {
        public RangeDecorationGroup group;
        public int meshBatch;
    }

    internal struct RangeDecorationDrawBatch
    {
        public Texture2D texture;
        public LayerBlend blend;
        public int sliceStart;
        public int sliceCount;
    }

    /// <summary>Relative render order of a range decoration versus its owning text.</summary>
    public enum RangeDecorationOrder
    {
        /// <summary>Render behind the text (selections, hover glow).</summary>
        Behind,
        /// <summary>Render in front of the text (click flashes, cursor).</summary>
        Above
    }

    internal abstract class RangeDecorationRenderer
    {
        private readonly Dictionary<string, RangeDecorationGroup> groups = new();
        private readonly List<RangeDecorationGroup> orderedGroups = new(4);
        private PooledBuffer<RangeDecorationDrawSlice> drawSlices;
        private PooledBuffer<RangeDecorationDrawBatch> drawBatches;

        private bool pending;

        internal RangeDecorationGroup GetOrCreateGroup(string id)
        {
            id ??= string.Empty;
            if (!groups.TryGetValue(id, out var group))
            {
                group = new RangeDecorationGroup(this, id);
                groups[id] = group;
                InsertSorted(group);
            }
            return group;
        }

        internal IReadOnlyList<RangeDecorationGroup> Groups => orderedGroups;

        internal ReadOnlySpan<RangeDecorationDrawSlice> DrawSlices => drawSlices.Span;
        internal ReadOnlySpan<RangeDecorationDrawBatch> DrawBatches => drawBatches.Span;

        internal void BuildDrawBatches()
        {
            drawSlices.FakeClear();
            drawBatches.FakeClear();

            for (var gi = 0; gi < orderedGroups.Count; gi++)
            {
                var group = orderedGroups[gi];
                var mesh = group.ExistingDecorationMesh;
                if (mesh == null || mesh.IsEmpty) continue;
                var batches = mesh.Batches;
                for (var bi = 0; bi < batches.Length; bi++)
                    if (batches[bi].indexCount > 0)
                        AddDrawSlice(group, bi, batches[bi].texture, batches[bi].blend);
            }
        }

        private void AddDrawSlice(RangeDecorationGroup group, int meshBatch, Texture2D texture,
            LayerBlend blend)
        {
            if (drawBatches.count == 0 ||
                !ReferenceEquals(drawBatches[drawBatches.count - 1].texture, texture) ||
                drawBatches[drawBatches.count - 1].blend != blend)
            {
                drawBatches.Add(new RangeDecorationDrawBatch
                {
                    texture = texture,
                    blend = blend,
                    sliceStart = drawSlices.count,
                });
            }

            drawSlices.Add(new RangeDecorationDrawSlice { group = group, meshBatch = meshBatch });
            ref var batch = ref drawBatches[drawBatches.count - 1];
            batch.sliceCount++;
        }

        /// <summary>Validates all authored blend states before a renderer mutates retained material handles.</summary>
        protected static void ValidateBlendMaterials(Material baseMaterial,
            ReadOnlySpan<RangeDecorationDrawBatch> batches)
        {
            if (batches.Length == 0) return;
            if (baseMaterial == null)
                throw new InvalidOperationException("The required range-decoration material is unavailable.");

            var contractValidated = false;
            for (var i = 0; i < batches.Length; i++)
            {
                var blend = batches[i].blend;
                BlendState.Resolve(blend);
                if (blend == LayerBlend.Normal || contractValidated) continue;
                BlendState.Validate(baseMaterial);
                contractValidated = true;
            }
        }

        protected void ReturnDrawBatches()
        {
            drawSlices.Return();
            drawBatches.Return();
        }

        internal void NotifySortChanged(RangeDecorationGroup group)
        {
            orderedGroups.Remove(group);
            InsertSorted(group);
            MarkDirty();
        }

        private void InsertSorted(RangeDecorationGroup group)
        {
            var idx = orderedGroups.Count;
            while (idx > 0)
            {
                var prev = orderedGroups[idx - 1];
                if (prev.SortPriority < group.SortPriority
                    || (prev.SortPriority == group.SortPriority && prev.SortSequence <= group.SortSequence))
                    break;
                idx--;
            }
            orderedGroups.Insert(idx, group);
        }

        internal abstract void Destroy();

        protected internal void MarkDirty()
        {
            if (pending) return;
            pending = true;
            OnDirty();
        }

        /// <summary>Schedules <see cref="Flush"/> for the current frame. Called once per transition to dirty.</summary>
        protected abstract void OnDirty();

        protected abstract void Rebuild();

        internal void Flush()
        {
            if (!pending) return;
            pending = false;
            Rebuild();
        }

        internal void RemoveGroupInternal(RangeDecorationGroup group)
        {
            if (groups.Remove(group.Id))
            {
                orderedGroups.Remove(group);
                MarkDirty();
            }
        }
    }
}
