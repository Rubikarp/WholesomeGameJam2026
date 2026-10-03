namespace LightSide
{
    /// <summary>
    /// The paint kind word every LightSide surface writes into <c>TEXCOORD3.w</c> (added to
    /// <c>8 × spread</c>). C# mirror of the encoding <c>LightSidePaint.hlsl</c> decodes; the two must agree.
    /// </summary>
    public static class LightSidePaintKind
    {
        /// <summary>Vertex colour only — no sampled source.</summary>
        public const float Solid = 0f;

        /// <summary>Ramp projected onto a straight axis.</summary>
        public const float Linear = 1f;

        /// <summary>Ramp projected by distance from the frame centre.</summary>
        public const float Radial = 2f;

        /// <summary>Ramp swept around the frame centre by angle.</summary>
        public const float Angular = 3f;

        /// <summary>Texture sampled inside its frame.</summary>
        public const float Texture = 4f;

        /// <summary>Texture repeated across the frame; the raw coordinate reaches the sampler unclamped.</summary>
        public const float TiledTexture = 5f;

        /// <summary>Ramp driven by the surface's own signed distance; the shader supplies the parameter through <c>LIGHTSIDE_PAINT_DISTANCE_T</c>.</summary>
        public const float Distance = 6f;

        /// <summary>The gradient kind word for a projection.</summary>
        public static float OfProjection(PaintProjectionKind kind) => (float)kind + 1f;

        /// <summary>The texture kind word for a fit — <see cref="PaintFit.Tile"/> maps to the unclamped variant.</summary>
        public static float OfTexture(PaintFit fit) => fit == PaintFit.Tile ? TiledTexture : Texture;
    }
}
