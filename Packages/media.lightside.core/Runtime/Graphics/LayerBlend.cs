namespace LightSide
{
    /// <summary>
    /// Defines how a premultiplied layer composites its color with the backdrop while preserving source-over alpha.
    /// </summary>
    /// <remarks>
    /// Serialized values 4 and 5 are reserved for the removed Darken and Lighten modes and are never reused.
    /// </remarks>
    public enum LayerBlend
    {
        /// <summary>No value at this layer (see <see cref="PaintInherit"/>); an unresolved chain means <see cref="Normal"/>.</summary>
        Inherit = PaintInherit.None,

        /// <summary>
        /// Composites the layer with standard premultiplied source-over blending.
        /// </summary>
        Normal = 0,

        /// <summary>
        /// Uses a fast premultiplied multiply state that is exact for opaque backdrops.
        /// </summary>
        Multiply = 1,

        /// <summary>
        /// Applies exact premultiplied screen blending through fixed-function blend factors.
        /// </summary>
        Screen = 2,

        /// <summary>
        /// Adds premultiplied color while compositing alpha source-over.
        /// </summary>
        Additive = 3,

        /// <summary>
        /// Applies exact premultiplied exclusion blending through fixed-function blend factors.
        /// </summary>
        Exclusion = 6,
    }
}
