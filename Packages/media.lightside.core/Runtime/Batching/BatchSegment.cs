using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// One neutral render segment handed to <see cref="WorldBatcher"/> during a capture window. The arrays
    /// are BORROWED (source-pooled) and valid ONLY for the duration of <see cref="IWorldBatchSource.Collect"/>
    /// — the engine copies out immediately, so a source must not retain or mutate them afterwards.
    /// <c>material</c> is already resolved by the source (never the engine's concern) and <c>lit</c> is the
    /// source's per-segment lighting flag (drives per-vertex normal computation and receive-shadows).
    /// </summary>
    public struct BatchSegment
    {
        public Material material;
        public bool lit;

        /// <summary>Segment identity within its source: <c>sequence</c> pairs a segment with the matching one of the next re-collect, <c>sortIndex</c> is the intra-source draw order.</summary>
        public int sequence;
        public int sortIndex;

        public Vector3[] vertices;
        public Vector4[] uvs0;
        public Vector4[] uvs1;
        public Vector4[] uvs2;
        public Vector4[] uvs3;

        /// <summary>Per-surface extra params of the shared vertex contract; <see langword="null"/> when the source writes none.</summary>
        public Vector4[] tangents;
        public Color32[] colors;

        /// <summary>Segment-relative triangle indices (they index the <c>[vertexOffset, vertexOffset+vertexCount)</c> slice).</summary>
        public int[] triangles;

        public int vertexOffset;
        public int vertexCount;
        public int triangleOffset;
        public int triangleCount;

        public bool hasUv1;
        public bool hasUv2;
        public bool hasUv3;
    }
}
