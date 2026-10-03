using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Base class for modifiers that emit a separate, owner-controlled sub-mesh rendered by its own
    /// <c>CanvasRenderer</c> with a user-supplied material (see <see cref="MaterialModifier"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lifecycle per frame:
    /// <list type="number">
    /// <item><c>onRebuildStart</c> — own buffers are cleared; any persistent state (e.g. Replace-mode
    /// claim of the base face) will be re-applied in the next <c>onGlyph</c> pass.</item>
    /// <item>The geometry-consumer tail of <c>onGlyph</c> — subclass overrides
    /// <see cref="ShouldIncludeCurrentGlyph"/> and, when true, the base class copies the completed face
    /// quad (4 verts) from the generator into its own buffer. SDF, MSDF and emoji glyphs share it — the
    /// per-glyph mode rides the vertex stream (UV1.w), so one material renders them all.</item>
    /// <item><c>onCollectSubMeshes</c> — the base class appends one entry to the render-data list,
    /// with the material, order and sort index chosen by the subclass.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Compared to <see cref="EffectModifier"/>, which bakes its duplicates into the same mesh as the
    /// face pass, <see cref="SubMeshModifier"/> keeps its vertices in isolated local buffers because
    /// each sub-mesh is bound to a distinct material and CanvasRenderer.
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract class SubMeshModifier : BaseModifier, ILayer
    {
        public int LayerSequence { get; set; }
        public virtual bool RendersBehindFill => false;
        public virtual bool ClaimsFill => false;

        /// <summary>Accumulated raw geometry. Emitted as <see cref="UniTextRenderData"/>
        /// per frame; consumers (canvas / world batcher) read directly from these pooled buffers.</summary>
        private struct SubMeshBuffers
        {
            public PooledBuffer<Vector3> vertices;
            public PooledBuffer<Vector4> uvs0;
            public PooledBuffer<Vector4> uvs1;
            public PooledBuffer<Vector4> uvs2;
            public PooledBuffer<Vector4> uvs3;
            public PooledBuffer<Color32> colors;
            public PooledBuffer<int> triangles;
            public bool hasUvs2;
            public bool hasUvs3;

            public int VertexCount => vertices.count;

            public void Clear()
            {
                vertices.FakeClear();
                uvs0.FakeClear();
                uvs1.FakeClear();
                uvs2.FakeClear();
                uvs3.FakeClear();
                colors.FakeClear();
                triangles.FakeClear();
                hasUvs2 = false;
                hasUvs3 = false;
            }

            public void Return()
            {
                vertices.Return();
                uvs0.Return();
                uvs1.Return();
                uvs2.Return();
                uvs3.Return();
                colors.Return();
                triangles.Return();
            }
        }

        private SubMeshBuffers subMeshBuffers;

        private Action onGlyphCallback;
        private Action onRebuildStartCallback;
        private OrderedEventHandler<SubMeshCollectionContext> onCollectCallback;

        /// <summary>
        /// Tells the base class whether the glyph currently being emitted by the generator should be
        /// copied into this modifier's sub-mesh. Inspect <c>uniText.MeshGenerator</c> fields
        /// (<c>currentCluster</c>, <c>faceBaseIdx</c>, <c>font.IsColor</c>, etc.) for the decision.
        /// </summary>
        protected abstract bool ShouldIncludeCurrentGlyph();

        /// <summary>
        /// Resolves the sub-mesh material. Return <see langword="null"/> to skip emitting.
        /// </summary>
        protected abstract Material GetMaterial();

        /// <summary>Stable sort key within the same layer sequence (lower renders first).</summary>
        protected virtual int GetSortIndex() => 0;

        /// <summary>
        /// Called after a glyph's quad is copied into the sub-mesh buffer. Override to write custom
        /// UV2/UV3 data for the just-appended four vertices, or to modify colors/positions. The four
        /// vertices occupy indices <c>[vertexStart, vertexStart + 4)</c>.
        /// </summary>
        /// <param name="vertexStart">Index of the first of the four appended vertices in the buffer.</param>
        /// <param name="gen">The current mesh generator (same as <c>uniText.MeshGenerator</c>, passed for convenience).</param>
        protected virtual void OnQuadAppended(int vertexStart, UniTextMeshGenerator gen) { }

        /// <summary>Writes a value into UV2 channel for a vertex in the buffer (auto-allocates channel).</summary>
        protected void SetSubMeshUv2(int vertexIndex, Vector4 value)
        {
            EnsureUvChannel(ref subMeshBuffers, 2, subMeshBuffers.vertices.count);
            subMeshBuffers.uvs2[vertexIndex] = value;
            subMeshBuffers.hasUvs2 = true;
        }

        /// <summary>Writes a value into UV3 channel for a vertex in the buffer (auto-allocates channel).</summary>
        protected void SetSubMeshUv3(int vertexIndex, Vector4 value)
        {
            EnsureUvChannel(ref subMeshBuffers, 3, subMeshBuffers.vertices.count);
            subMeshBuffers.uvs3[vertexIndex] = value;
            subMeshBuffers.hasUvs3 = true;
        }

        /// <summary>Writes the same value to UV2 for all 4 vertices of a quad (indices <c>[vertexStart..+4)</c>).</summary>
        protected void SetSubMeshUv2Quad(int vertexStart, Vector4 value)
        {
            EnsureUvChannel(ref subMeshBuffers, 2, subMeshBuffers.vertices.count);
            var data = subMeshBuffers.uvs2.data;
            data[vertexStart]     = value;
            data[vertexStart + 1] = value;
            data[vertexStart + 2] = value;
            data[vertexStart + 3] = value;
            subMeshBuffers.hasUvs2 = true;
        }

        /// <summary>Writes the same value to UV3 for all 4 vertices of a quad (indices <c>[vertexStart..+4)</c>).</summary>
        protected void SetSubMeshUv3Quad(int vertexStart, Vector4 value)
        {
            EnsureUvChannel(ref subMeshBuffers, 3, subMeshBuffers.vertices.count);
            var data = subMeshBuffers.uvs3.data;
            data[vertexStart]     = value;
            data[vertexStart + 1] = value;
            data[vertexStart + 2] = value;
            data[vertexStart + 3] = value;
            subMeshBuffers.hasUvs3 = true;
        }

        /// <summary>Direct access to the sub-mesh color buffer for read/write on already-appended vertices.</summary>
        protected Color32[] GetSubMeshColors() => subMeshBuffers.colors.data;

        /// <summary>Direct access to the sub-mesh vertex buffer for read/write on already-appended vertices.</summary>
        protected Vector3[] GetSubMeshVertices() => subMeshBuffers.vertices.data;

        /// <summary>
        /// Expands a 4-vertex quad outward on all sides by <paramref name="delta"/> UV0 units, moving the
        /// positions through the quad's own UV0→pixel scale. Uses the same deformation-aware position and
        /// UV0 expansion as the main mesh.
        /// </summary>
        /// <param name="vertexStart">Index of the first of the four quad vertices in the buffer.</param>
        /// <param name="delta">Padding to add on each side, in UV0 units (glyph heights on a glyph quad).</param>
        protected void ExpandSubMeshQuad(int vertexStart, float delta)
        {
            if (delta <= 0f) return;
            ref var s = ref subMeshBuffers;
            if (s.uvs0.data == null) return;

            UniTextMeshGenerator.ExpandQuad(s.vertices.data, s.uvs0.data, vertexStart, delta);
        }

        protected override void OnEnable()
        {
            subMeshBuffers.Clear();

            onGlyphCallback ??= OnGlyph;
            onRebuildStartCallback ??= OnRebuildStart;
            onCollectCallback ??= OnCollectSubMeshes;

            var gen = uniText.MeshGenerator;
            gen.onGlyph.Subscribe(onGlyphCallback, UniTextMeshGenerator.GlyphGeometryConsumerOrder);
            gen.onRebuildStart.Subscribe(onRebuildStartCallback);
            gen.onCollectSubMeshes.Subscribe(onCollectCallback);
        }

        protected override void OnDisable()
        {
            var gen = uniText.MeshGenerator;
            gen.onGlyph.Unsubscribe(onGlyphCallback);
            gen.onRebuildStart.Unsubscribe(onRebuildStartCallback);
            gen.onCollectSubMeshes.Unsubscribe(onCollectCallback);
        }

        protected override void OnDestroy()
        {
            subMeshBuffers.Return();
            onGlyphCallback = null;
            onRebuildStartCallback = null;
            onCollectCallback = null;
        }

        private void OnRebuildStart()
        {
            subMeshBuffers.Clear();
            OnBeforeRebuild();
        }

        /// <summary>Called at the start of every rebuild (before any <c>onGlyph</c>). Override to refresh
        /// derived state (e.g. runtime material clones) that depends on the generator's current mode.</summary>
        protected virtual void OnBeforeRebuild() { }

        private void OnGlyph()
        {
            if (!ShouldIncludeCurrentGlyph()) return;

            var gen = uniText.MeshGenerator;
            var srcBase = gen.faceBaseIdx;
            if (srcBase < 0) return;

            var dstBase = subMeshBuffers.vertices.count;
            AppendQuadFromGenerator(ref subMeshBuffers, gen, srcBase);
            OnQuadAppended(dstBase, gen);
        }

        private static void AppendQuadFromGenerator(ref SubMeshBuffers s, UniTextMeshGenerator gen, int srcBase)
        {
            EnsureBufferCapacity(ref s, 4, 6);

            var dstBase = s.vertices.count;

            var srcVerts = gen.Vertices;
            var srcUv0 = gen.Uvs0;
            var srcUv1 = gen.Uvs1;
            var srcCols = gen.Colors;

            Array.Copy(srcVerts, srcBase, s.vertices.data, dstBase, 4);
            Array.Copy(srcUv0, srcBase, s.uvs0.data, dstBase, 4);
            Array.Copy(srcUv1, srcBase, s.uvs1.data, dstBase, 4);
            Array.Copy(srcCols, srcBase, s.colors.data, dstBase, 4);

            if (s.hasUvs2 && s.uvs2.data != null)
            {
                s.uvs2[dstBase]     = default;
                s.uvs2[dstBase + 1] = default;
                s.uvs2[dstBase + 2] = default;
                s.uvs2[dstBase + 3] = default;
            }
            if (s.hasUvs3 && s.uvs3.data != null)
            {
                s.uvs3[dstBase]     = default;
                s.uvs3[dstBase + 1] = default;
                s.uvs3[dstBase + 2] = default;
                s.uvs3[dstBase + 3] = default;
            }

            var tri = s.triangles.count;
            s.triangles[tri]     = dstBase;
            s.triangles[tri + 1] = dstBase + 1;
            s.triangles[tri + 2] = dstBase + 2;
            s.triangles[tri + 3] = dstBase + 2;
            s.triangles[tri + 4] = dstBase + 3;
            s.triangles[tri + 5] = dstBase;

            s.vertices.count += 4;
            s.uvs0.count     += 4;
            s.uvs1.count     += 4;
            s.colors.count   += 4;
            s.triangles.count = tri + 6;
            if (s.uvs2.data != null) s.uvs2.count = s.vertices.count;
            if (s.uvs3.data != null) s.uvs3.count = s.vertices.count;
        }

        private static void EnsureBufferCapacity(ref SubMeshBuffers s, int extraVerts, int extraTris)
        {
            s.vertices.EnsureCapacity(s.vertices.count + extraVerts);
            s.uvs0.EnsureCapacity(s.vertices.count + extraVerts);
            s.uvs1.EnsureCapacity(s.vertices.count + extraVerts);
            s.colors.EnsureCapacity(s.vertices.count + extraVerts);
            s.triangles.EnsureCapacity(s.triangles.count + extraTris);

            if (s.hasUvs2) s.uvs2.EnsureCapacity(s.vertices.count + extraVerts);
            if (s.hasUvs3) s.uvs3.EnsureCapacity(s.vertices.count + extraVerts);
        }

        private static void EnsureUvChannel(ref SubMeshBuffers s, int channel, int requiredCount)
        {
            ref var buf = ref channel == 3 ? ref s.uvs3 : ref s.uvs2;
            buf.EnsureCount(requiredCount);
        }

        private void OnCollectSubMeshes(ref SubMeshCollectionContext context)
        {
            ref var s = ref subMeshBuffers;
            if (s.VertexCount == 0) return;

            var material = GetMaterial();
            if (material == null) return;

            context.Results.Add(new UniTextRenderData
            {
                fontId           = 0,
                materialOverride = material,
                atlasOverride    = null,
                sequence         = LayerSequence,
                sortIndex        = GetSortIndex(),

                vertices       = s.vertices.data,
                uvs0           = s.uvs0.data,
                uvs1           = s.uvs1.data,
                uvs2           = s.hasUvs2 ? s.uvs2.data : null,
                uvs3           = s.hasUvs3 ? s.uvs3.data : null,
                colors         = s.colors.data,
                triangles      = s.triangles.data,
                vertexOffset   = 0,
                vertexCount    = s.vertices.count,
                triangleOffset = 0,
                triangleCount  = s.triangles.count,
                hasUv1         = true,
                hasUv2         = s.hasUvs2,
                hasUv3         = s.hasUvs3,
            });
        }
    }
}
