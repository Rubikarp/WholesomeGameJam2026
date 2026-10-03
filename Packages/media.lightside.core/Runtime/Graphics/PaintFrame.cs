namespace LightSide
{
    /// <summary>
    /// Precomputed frame mapping positions into normalized paint space. It is built once for a selected
    /// bounds owner and queried for every emitted vertex without allocation or Unity object access.
    /// </summary>
    public struct PaintFrame
    {
        /// <summary>Centre of the projection frame in source coordinates.</summary>
        public float centerX, centerY;

        /// <summary>Cosine and sine of the frame's authored rotation.</summary>
        public float cosA, sinA;

        /// <summary>Half-extents along the rotated projection axes.</summary>
        public float halfU, halfV;

        /// <summary>Normalized sample-origin offset.</summary>
        public float offsetX, offsetY;
    }
}
