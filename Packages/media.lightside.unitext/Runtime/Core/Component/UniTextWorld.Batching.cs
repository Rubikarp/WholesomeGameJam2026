using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    public partial class UniTextWorld : IWorldBatchSource
    {
        private readonly List<MaterialCloneRef<SurfaceMaterialKey>> blendMaterialRefs = new(4);

        Transform IWorldBatchSource.Transform => transform;
        GameObject IWorldBatchSource.GameObject => gameObject;
        int IWorldBatchSource.BatchGroupId => standalone ? ObjectUtils.GetInstanceIdCompat(this) : 0;
        bool IWorldBatchSource.RenderSuppressed => RenderSuppressed;

        [NonSerialized] private WorldRangeDecorationRenderer decorationsBelow;
        [NonSerialized] private WorldRangeDecorationRenderer decorationsAbove;

        /// <summary>Registers, or clears with <see langword="null"/>, the range-decoration surface collected under or over this component's glyph segments.</summary>
        internal void SetDecorationRenderer(bool above, WorldRangeDecorationRenderer renderer)
        {
            if (above) decorationsAbove = renderer;
            else decorationsBelow = renderer;
        }

        /// <summary>Rebuilds pending decoration geometry and reports whether any is staged.</summary>
        private bool PrepareDecorations()
        {
            var any = decorationsBelow != null && decorationsBelow.PrepareSegments();
            if (decorationsAbove != null && decorationsAbove.PrepareSegments()) any = true;
            return any;
        }

        /// <summary>Re-runs the render decision after a decoration surface changed without the text itself changing.</summary>
        internal void RefreshBatchSegments() => UpdateRendering();

        /// <summary>Maps this component's full draw stack — decorations below, glyph segments, decorations above — to neutral <see cref="BatchSegment"/>s for the Core batcher. One renderer draws them all, so the stack orders within the component and nothing external can sort between its parts. Borrowed arrays — valid only for this call.</summary>
        void IWorldBatchSource.Collect(List<BatchSegment> buffer)
        {
            decorationsBelow?.CollectSegments(buffer);
            CollectTextSegments(buffer);
            decorationsAbove?.CollectSegments(buffer);
        }

        private void CollectTextSegments(List<BatchSegment> buffer)
        {
            var gen = MeshGenerator;
            if (gen == null || !gen.HasGeneratedData) return;

            var defaultMat = UniTextMaterialCache.TextWorld(lit);
            var segments = gen.CollectRenderData();
            ValidateBlendUse(defaultMat, segments);

            EnsureBlendMaterialRefs(segments.Count);
            for (var i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                var mat = s.materialOverride != null ? s.materialOverride : defaultMat;
                mat = ResolveBlendMaterial(i, mat, s.blend);
                buffer.Add(new BatchSegment
                {
                    material = mat,
                    lit = mat.HasProperty(ShaderIds.Lit.LightInfluence) && mat.GetFloat(ShaderIds.Lit.LightInfluence) > 0f,
                    sequence = s.sequence,
                    sortIndex = s.sortIndex,
                    vertices = s.vertices,
                    uvs0 = s.uvs0,
                    uvs1 = s.uvs1,
                    uvs2 = s.uvs2,
                    uvs3 = s.uvs3,
                    colors = s.colors,
                    triangles = s.triangles,
                    vertexOffset = s.vertexOffset,
                    vertexCount = s.vertexCount,
                    triangleOffset = s.triangleOffset,
                    triangleCount = s.triangleCount,
                    hasUv1 = s.hasUv1,
                    hasUv2 = s.hasUv2,
                    hasUv3 = s.hasUv3,
                });
            }
            ReleaseUnusedBlendMaterials(segments.Count);
        }

        private static void ValidateBlendUse(Material defaultMaterial, IReadOnlyList<UniTextRenderData> segments)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                SurfaceMaterialPool.ValidateUse(
                    segment.materialOverride != null ? segment.materialOverride : defaultMaterial,
                    segment.blend);
            }
        }

        protected override void OnDeInit()
        {
            base.OnDeInit();
            ReleaseBlendMaterials();
        }

        private void EnsureBlendMaterialRefs(int count)
        {
            while (blendMaterialRefs.Count < count) blendMaterialRefs.Add(default);
        }

        private Material ResolveBlendMaterial(int index, Material source, LayerBlend blend)
        {
            var materialRef = blendMaterialRefs[index];
            Material result;
            if (blend == LayerBlend.Normal)
            {
                materialRef.Release();
                result = source;
            }
            else
            {
                var key = new SurfaceMaterialKey(source, blend);
                result = materialRef.Bind(SurfaceMaterialPool.Instance, key, source);
            }
            blendMaterialRefs[index] = materialRef;
            return result;
        }

        private void ReleaseUnusedBlendMaterials(int used)
        {
            for (var i = used; i < blendMaterialRefs.Count; i++)
            {
                var materialRef = blendMaterialRefs[i];
                materialRef.Release();
                blendMaterialRefs[i] = materialRef;
            }
        }

        private void ReleaseBlendMaterials()
        {
            ReleaseUnusedBlendMaterials(0);
        }
    }
}
