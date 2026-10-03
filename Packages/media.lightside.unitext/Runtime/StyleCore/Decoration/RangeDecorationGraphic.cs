using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>Canvas surface populated from one range decoration draw batch.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class RangeDecorationGraphic : MaskableGraphic
    {
        private static readonly Vector3 defaultNormal = new(0f, 0f, -1f);
        private static readonly Vector4 defaultTangent = new(1f, 0f, 0f, -1f);

        private RangeDecorationRenderer source;
        private int batchIndex;

        /// <summary><see langword="null"/> — decorations sample nothing of their own.</summary>
        /// <remarks>
        /// The Canvas batch key pairs material with this texture, and <see cref="Graphic"/>'s white
        /// stand-in would split decorations off the surface they share with text.
        /// </remarks>
        public override Texture mainTexture => null;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            color = Color.white;
        }

        public void Bind(RangeDecorationRenderer source, int batchIndex)
        {
            this.source = source;
            this.batchIndex = batchIndex;
        }

        /// <summary>Ignores uGUI's aggregate invalidation; this surface rebuilds only from <see cref="Rebuild()"/>.</summary>
        public override void SetAllDirty() { }

        /// <summary>Ignores uGUI's mesh queue; this surface rebuilds only from <see cref="Rebuild()"/>.</summary>
        public override void SetVerticesDirty() { }

        /// <summary>Ignores uGUI's material queue; this surface rebuilds only from <see cref="Rebuild()"/>.</summary>
        public override void SetMaterialDirty() { }

        /// <summary>Ignores uGUI's rebuild pass; this surface rebuilds only from <see cref="Rebuild()"/>.</summary>
        public override void Rebuild(CanvasUpdate update) { }

        /// <summary>
        /// Rebuilds mesh and material synchronously, for the current frame's draw.
        /// </summary>
        /// <remarks>
        /// Owning the rebuild rather than queueing into <see cref="CanvasUpdateRegistry"/> is the point:
        /// decoration geometry is produced late in <c>willRenderCanvases</c>
        /// (<see cref="UniTextBase.ProcessingEnded"/>), after the registry has already rebuilt graphics for
        /// this frame, so anything queued would surface one frame late — init flicker, stale rects
        /// lingering after a text swap.
        /// </remarks>
        public void Rebuild()
        {
            UpdateGeometry();
            UpdateMaterial();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (source == null) return;

            var batches = source.DrawBatches;
            if ((uint)batchIndex >= (uint)batches.Length) return;
            ref readonly var batch = ref batches[batchIndex];
            var slices = source.DrawSlices;
            var sliceEnd = batch.sliceStart + batch.sliceCount;
            for (var si = batch.sliceStart; si < sliceEnd; si++)
            {
                ref readonly var slice = ref slices[si];
                AddMesh(vh, slice.group.ExistingDecorationMesh, slice.meshBatch);
            }
        }

        private static void AddMesh(VertexHelper vh, RangeDecorationMesh mesh, int batchIndex)
        {
            if (mesh == null) return;
            var batches = mesh.Batches;
            if ((uint)batchIndex >= (uint)batches.Length) return;
            ref readonly var batch = ref batches[batchIndex];
            var vertices = mesh.Vertices;
            var end = batch.vertexStart + batch.vertexCount;
            var baseVertex = vh.currentVertCount;
            for (var i = batch.vertexStart; i < end; i++)
            {
                ref readonly var vertex = ref vertices[i];
                vh.AddVert(vertex.position, vertex.color, vertex.uv0, vertex.uv1, vertex.uv2,
                    vertex.uv3, defaultNormal, vertex.tangent);
            }

            var indices = mesh.Indices;
            var indexEnd = batch.indexStart + batch.indexCount;
            for (var i = batch.indexStart; i < indexEnd; i += 3)
            {
                vh.AddTriangle(
                    baseVertex + indices[i] - batch.vertexStart,
                    baseVertex + indices[i + 1] - batch.vertexStart,
                    baseVertex + indices[i + 2] - batch.vertexStart);
            }
        }

    }
}
