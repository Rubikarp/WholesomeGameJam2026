namespace LightSide
{
    /// <summary>
    /// How an analytic surface turns its field into coverage. Rides <c>TEXCOORD2.x</c> packed as
    /// <c>mode + 16 * shapeKind</c>; the remaining components carry the mode's arguments.
    /// </summary>
    /// <remarks>C# mirror of the mode ladder in <c>LightSideShapeSurface.hlsl</c>; the two must agree.</remarks>
    public static class LightSideShapeCoverageMode
    {
        /// <summary>Solid interior of the field.</summary>
        public const float Fill = 0f;

        /// <summary>Band along the contour. Arguments: width, alignment.</summary>
        public const float Stroke = 1f;

        /// <summary>Offset, blurred copy of the field. Arguments: offset xy, blur, spread.</summary>
        public const float Shadow = 2f;

        /// <summary>Shadow confined to the interior. Same arguments as <see cref="Shadow"/>.</summary>
        public const float InnerShadow = 3f;

        /// <summary>
        /// No field at all — coverage is interpolated from the vertex stream and resolved to a
        /// screen-space antialiased edge. For emitters of arbitrary triangles, which no shape describes.
        /// Argument: the per-vertex coverage.
        /// </summary>
        public const float VertexCoverage = 4f;
    }
}
