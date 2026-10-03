using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Stages world-space range decoration geometry as batch segments of the owning text, so a decoration
    /// draws inside that component's own mesh — immediately under or over its glyph segments — instead of
    /// through a renderer of its own.
    /// </summary>
    internal sealed class WorldRangeDecorationRenderer : RangeDecorationRenderer
    {
        /// <summary>
        /// Collect-list identity claimed by decoration segments. Both values sit outside every layer
        /// sequence the mesh generator can emit, which keeps the owner's collect list sorted by
        /// (sequence, sortIndex) — the order <c>WorldBatcher</c> merges against to reuse entries.
        /// </summary>
        private const int BehindSequence = int.MinValue;
        /// <inheritdoc cref="BehindSequence"/>
        private const int AboveSequence = int.MaxValue;

        private struct StagedSegment
        {
            public Material material;
            public int vertexOffset;
            public int vertexCount;
            public int triangleOffset;
            public int triangleCount;
        }

        private readonly UniTextWorld owner;
        private readonly bool above;

        private PooledBuffer<Vector3> positions;
        private PooledBuffer<Color32> colors;
        private PooledBuffer<Vector4> shape;
        private PooledBuffer<Vector4> rounded;
        private PooledBuffer<Vector4> paint;
        private PooledBuffer<Vector4> draw;
        private PooledBuffer<Vector4> tangents;
        private PooledBuffer<int> triangles;

        private readonly List<StagedSegment> staged = new(2);
        private readonly List<MaterialCloneRef<SurfaceMaterialKey>> textureMaterialRefs = new(2);
        private readonly List<MaterialCloneRef<SurfaceMaterialKey>> blendMaterialRefs = new(2);

        public WorldRangeDecorationRenderer(UniTextWorld owner, RangeDecorationOrder order)
        {
            this.owner = owner;
            above = order == RangeDecorationOrder.Above;
            owner.SetDecorationRenderer(above, this);
        }

        protected override void OnDirty() => UniTextWorldBatcher.RequestRecollect(owner);

        /// <summary>Rebuilds pending geometry and reports whether anything is staged — the owner's cue to re-collect rather than drop its batch entries.</summary>
        internal bool PrepareSegments()
        {
            Flush();
            return staged.Count > 0;
        }

        /// <summary>Appends the staged geometry to the owner's capture buffer. The arrays stay owned here; the batcher copies out within the call.</summary>
        internal void CollectSegments(List<BatchSegment> buffer)
        {
            Flush();
            var sequence = above ? AboveSequence : BehindSequence;
            for (var i = 0; i < staged.Count; i++)
            {
                var segment = staged[i];
                buffer.Add(new BatchSegment
                {
                    material = segment.material,
                    lit = false,
                    sequence = sequence,
                    sortIndex = i,
                    vertices = positions.data,
                    uvs0 = shape.data,
                    uvs1 = rounded.data,
                    uvs2 = paint.data,
                    uvs3 = draw.data,
                    tangents = tangents.data,
                    colors = colors.data,
                    triangles = triangles.data,
                    vertexOffset = segment.vertexOffset,
                    vertexCount = segment.vertexCount,
                    triangleOffset = segment.triangleOffset,
                    triangleCount = segment.triangleCount,
                    hasUv1 = true,
                    hasUv2 = true,
                    hasUv3 = true,
                });
            }
        }

        protected override void Rebuild()
        {
            positions.FakeClear();
            colors.FakeClear();
            shape.FakeClear();
            rounded.FakeClear();
            paint.FakeClear();
            draw.FakeClear();
            tangents.FakeClear();
            triangles.FakeClear();
            staged.Clear();

            BuildDrawBatches();
            var batches = DrawBatches;
            var baseMaterial = LightSideMaterials.World(false);
            ValidateBlendMaterials(baseMaterial, batches);
            EnsureBatchStorage(batches.Length);

            var slices = DrawSlices;
            for (var bi = 0; bi < batches.Length; bi++)
            {
                ref readonly var batch = ref batches[bi];
                var vertexStart = positions.count;
                var triangleStart = triangles.count;

                var sliceEnd = batch.sliceStart + batch.sliceCount;
                for (var si = batch.sliceStart; si < sliceEnd; si++)
                {
                    ref readonly var slice = ref slices[si];
                    AddMesh(slice.group.ExistingDecorationMesh, slice.meshBatch, vertexStart);
                }

                if (triangles.count == triangleStart) continue;

                staged.Add(new StagedSegment
                {
                    material = ResolveMaterial(bi, in batch, baseMaterial),
                    vertexOffset = vertexStart,
                    vertexCount = positions.count - vertexStart,
                    triangleOffset = triangleStart,
                    triangleCount = triangles.count - triangleStart,
                });
            }

            ReleaseUnusedMaterials(batches.Length);
        }

        private void EnsureBatchStorage(int count)
        {
            while (textureMaterialRefs.Count < count)
                textureMaterialRefs.Add(default);
            while (blendMaterialRefs.Count < count)
                blendMaterialRefs.Add(default);
        }

        private Material ResolveMaterial(int index, in RangeDecorationDrawBatch batch,
            Material baseMaterial)
        {
            var textureMaterialRef = textureMaterialRefs[index];
            Material source;
            if (batch.texture == null)
            {
                textureMaterialRef.Release();
                source = baseMaterial;
            }
            else
            {
                var textureKey = new SurfaceMaterialKey(baseMaterial, LayerBlend.Normal, batch.texture);
                source = textureMaterialRef.Bind(
                    SurfaceMaterialPool.Instance, textureKey, baseMaterial);
            }
            textureMaterialRefs[index] = textureMaterialRef;

            var blendMaterialRef = blendMaterialRefs[index];
            Material result;
            if (batch.blend == LayerBlend.Normal)
            {
                blendMaterialRef.Release();
                result = source;
            }
            else
            {
                var blendKey = new SurfaceMaterialKey(source, batch.blend);
                result = blendMaterialRef.Bind(SurfaceMaterialPool.Instance, blendKey, source);
            }
            blendMaterialRefs[index] = blendMaterialRef;
            return result;
        }

        private void ReleaseUnusedMaterials(int used)
        {
            for (var i = used; i < blendMaterialRefs.Count; i++)
            {
                var blendMaterialRef = blendMaterialRefs[i];
                blendMaterialRef.Release();
                blendMaterialRefs[i] = blendMaterialRef;
            }
            for (var i = used; i < textureMaterialRefs.Count; i++)
            {
                var textureMaterialRef = textureMaterialRefs[i];
                textureMaterialRef.Release();
                textureMaterialRefs[i] = textureMaterialRef;
            }
        }

        /// <summary>Appends one source batch, rebasing its indices onto the segment that starts at <paramref name="segmentVertexStart"/>.</summary>
        private void AddMesh(RangeDecorationMesh source, int batchIndex, int segmentVertexStart)
        {
            if (source == null) return;
            ref readonly var batch = ref source.Batches[batchIndex];
            var sourceVertices = source.Vertices;
            var end = batch.vertexStart + batch.vertexCount;
            var baseVertex = positions.count - segmentVertexStart;
            for (var i = batch.vertexStart; i < end; i++)
            {
                ref readonly var vertex = ref sourceVertices[i];
                positions.Add(vertex.position);
                colors.Add(vertex.color);
                shape.Add(vertex.uv0);
                rounded.Add(vertex.uv1);
                paint.Add(vertex.uv2);
                draw.Add(vertex.uv3);
                tangents.Add(vertex.tangent);
                
                
            }

            var sourceIndices = source.Indices;
            var indexEnd = batch.indexStart + batch.indexCount;
            for (var i = batch.indexStart; i < indexEnd; i++)
                triangles.Add(baseVertex + sourceIndices[i] - batch.vertexStart);
        }

        internal override void Destroy()
        {
            if (owner != null)
            {
                owner.SetDecorationRenderer(above, null);
                UniTextWorldBatcher.RequestRecollect(owner);
            }

            ReleaseUnusedMaterials(0);
            textureMaterialRefs.Clear();
            blendMaterialRefs.Clear();
            staged.Clear();

            ReturnDrawBatches();
            positions.Return();
            colors.Return();
            shape.Return();
            rounded.Return();
            paint.Return();
            draw.Return();
            tangents.Return();
            triangles.Return();
        }
    }
}
