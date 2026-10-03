using System;
using UnityEngine;

namespace LightSide
{
    internal struct RangeDecorationVertex
    {
        public Vector3 position;
        public Color32 color;

        /// <summary>Shared LightSide surface contract — see <c>LightSideSurface.hlsl</c>. Decorations are
        /// surface kind <see cref="LightSideSurfaceKind.Shape"/> drawn as <see cref="ShapeKind.RoundedRect"/>.</summary>
        public Vector4 uv0;

        public Vector4 uv1;
        public Vector4 uv2;
        public Vector4 uv3;
        public Vector4 tangent;
    }

    internal struct RangeDecorationBatch
    {
        public Texture2D texture;
        public LayerBlend blend;
        public int vertexStart;
        public int vertexCount;
        public int indexStart;
        public int indexCount;
    }

    internal sealed class RangeDecorationMesh
    {
        private PooledBuffer<RangeDecorationVertex> vertices;
        private PooledBuffer<int> indices;
        private PooledBuffer<RangeDecorationBatch> batches;
        private int activeBatch = -1;

        public ReadOnlySpan<RangeDecorationVertex> Vertices => vertices.Span;
        public ReadOnlySpan<int> Indices => indices.Span;
        public ReadOnlySpan<RangeDecorationBatch> Batches => batches.Span;
        public bool IsEmpty => indices.count == 0;

        public void Clear()
        {
            vertices.FakeClear();
            indices.FakeClear();
            batches.FakeClear();
            activeBatch = -1;
        }

        public void Return()
        {
            vertices.Return();
            indices.Return();
            batches.Return();
            activeBatch = -1;
        }

        public void Begin(Texture2D texture, LayerBlend blend)
        {
            if (activeBatch >= 0 && ReferenceEquals(batches[activeBatch].texture, texture) &&
                batches[activeBatch].blend == blend)
                return;

            CompleteBatch();
            batches.Add(new RangeDecorationBatch
            {
                texture = texture,
                blend = blend,
                vertexStart = vertices.count,
                indexStart = indices.count,
            });
            activeBatch = batches.count - 1;
        }

        public int AddVertex(in RangeDecorationVertex vertex)
        {
            if (activeBatch < 0)
                throw new InvalidOperationException("SetPaint must be called before emitting range-decoration geometry.");
            var index = vertices.count;
            vertices.Add(vertex);
            return index;
        }

        public void AddTriangle(int a, int b, int c)
        {
            if (activeBatch < 0)
                throw new InvalidOperationException("SetPaint must be called before emitting range-decoration geometry.");

            var first = batches[activeBatch].vertexStart;
            var end = vertices.count;
            if (a < first || b < first || c < first || a >= end || b >= end || c >= end)
                throw new ArgumentOutOfRangeException(nameof(a),
                    "A triangle can reference only vertices emitted for the current paint batch.");

            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        public void CompleteBatch()
        {
            if (activeBatch < 0) return;
            ref var batch = ref batches[activeBatch];
            batch.vertexCount = vertices.count - batch.vertexStart;
            batch.indexCount = indices.count - batch.indexStart;
            if (batch.indexCount == 0)
            {
                vertices.count = batch.vertexStart;
                indices.count = batch.indexStart;
                batches.count--;
            }
            activeBatch = -1;
        }
    }
}
