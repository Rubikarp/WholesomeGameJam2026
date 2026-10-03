using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>How a sampled paint progresses through its projection frame.</summary>
    public enum PaintProjectionKind : sbyte
    {
        /// <summary>No value at this layer (see <see cref="PaintInherit"/>); an unresolved chain means <see cref="Linear"/>.</summary>
        Inherit = PaintInherit.None,

        /// <summary>Projects positions onto a straight axis.</summary>
        Linear = 0,

        /// <summary>Uses distance from the projection centre.</summary>
        Radial = 1,

        /// <summary>Sweeps around the projection centre by angle.</summary>
        Angular = 2,
    }

    /// <summary>
    /// How a source's own aspect is fitted to its projection frame. Textures take their aspect from the
    /// sampled pixels; <see cref="PaintProjectionKind.Radial"/> and <see cref="PaintProjectionKind.Angular"/>
    /// are square sources, so anything but <see cref="Stretch"/> makes them geometrically true.
    /// <see cref="PaintProjectionKind.Linear"/> reads one axis only and ignores this.
    /// </summary>
    public enum PaintFit : sbyte
    {
        /// <summary>No value at this layer (see <see cref="PaintInherit"/>); an unresolved chain means <see cref="Stretch"/>.</summary>
        Inherit = PaintInherit.None,

        /// <summary>Fills both axes independently and may distort the source.</summary>
        Stretch = 0,

        /// <summary>Fits the complete source inside the frame while preserving aspect ratio.</summary>
        Contain = 1,

        /// <summary>Covers the complete frame while preserving aspect ratio and cropping overflow.</summary>
        Cover = 2,

        /// <summary>
        /// Sizes aspect-correct cells from the frame's U axis and makes <see cref="PaintProjection.scale"/>
        /// the repeat count. Repetition itself comes from the wrap policy: a texture's own wrap mode, or
        /// <see cref="PaintProjection.spread"/> for a gradient.
        /// </summary>
        Tile = 3,
    }

    /// <summary>How a sampled source continues outside its projection frame.</summary>
    public enum PaintSpread : sbyte
    {
        /// <summary>No value at this layer (see <see cref="PaintInherit"/>); an unresolved chain means <see cref="Clamp"/>.</summary>
        Inherit = PaintInherit.None,

        /// <summary>Holds the edge value — the first and last stop fill everything beyond the frame.</summary>
        Clamp = 0,

        /// <summary>Restarts the source at every frame boundary in the authored order, so the ends meet as a step.</summary>
        Repeat = 1,

        /// <summary>Restarts the source reversed at every frame boundary, so adjacent periods meet without a step.</summary>
        Mirror = 2,
    }

    /// <summary>
    /// The shared "no value at this layer" sentinel for paint enums. A field carrying it defers to the
    /// weaker layer of its override chain (tag → default parameters → modifier field → swatch); a chain
    /// that never resolves falls back to the value each enum documents on its <c>Inherit</c> member.
    /// Every resolved member is non-negative, so the sentinel never collides with authored data and
    /// <c>Inherit</c> can be added to an existing enum without renumbering it.
    /// </summary>
    public static class PaintInherit
    {
        /// <summary>Serialized value of every paint enum's <c>Inherit</c> member.</summary>
        public const sbyte None = -1;

        /// <summary>Returns the resolved kind, substituting the documented fallback for <see cref="PaintProjectionKind.Inherit"/>.</summary>
        public static PaintProjectionKind Resolved(this PaintProjectionKind value)
            => value == PaintProjectionKind.Inherit ? PaintProjectionKind.Linear : value;

        /// <summary>Returns the resolved fit, substituting the documented fallback for <see cref="PaintFit.Inherit"/>.</summary>
        public static PaintFit Resolved(this PaintFit value)
            => value == PaintFit.Inherit ? PaintFit.Stretch : value;

        /// <summary>Returns the resolved spread, substituting the documented fallback for <see cref="PaintSpread.Inherit"/>.</summary>
        public static PaintSpread Resolved(this PaintSpread value)
            => value == PaintSpread.Inherit ? PaintSpread.Clamp : value;

        /// <summary>Returns the resolved blend, substituting the documented fallback for <see cref="LayerBlend.Inherit"/>.</summary>
        public static LayerBlend Resolved(this LayerBlend value)
            => value == LayerBlend.Inherit ? LayerBlend.Normal : value;

        /// <summary>
        /// Returns <paramref name="authored"/> with every non-sentinel field of <paramref name="over"/> replacing
        /// its counterpart — the override pass a consumer layers over a named paint. <c>Inherit</c> enum members
        /// and NaN floats and axes keep the authored value.
        /// </summary>
        public static PaintProjection Overlaid(in this PaintProjection authored, in PaintProjection over)
        {
            var result = authored;
            if (over.kind != PaintProjectionKind.Inherit) result.kind = over.kind;
            if (over.fit != PaintFit.Inherit) result.fit = over.fit;
            if (over.spread != PaintSpread.Inherit) result.spread = over.spread;
            if (!float.IsNaN(over.angle)) result.angle = over.angle;
            if (!float.IsNaN(over.scale)) result.scale = over.scale;
            if (!float.IsNaN(over.offset.x)) result.offset.x = over.offset.x;
            if (!float.IsNaN(over.offset.y)) result.offset.y = over.offset.y;
            return result;
        }
    }

    /// <summary>Wrapping and vertex encoding of <see cref="PaintSpread"/>.</summary>
    public static class PaintSpreadExtensions
    {
        /// <summary>
        /// Multiplier that carries a spread mode above a consumer's paint-kind code inside one vertex
        /// float. A power of two, so a shader recovers both parts exactly at any interpolator precision.
        /// </summary>
        public const float CodeStep = 8f;

        /// <summary>
        /// Folds <paramref name="t"/> into the [0,1] source domain. Input below zero folds through
        /// <see cref="Mathf.Floor"/> instead of clamping, so a frame panned behind its origin continues
        /// the pattern.
        /// </summary>
        public static float Wrap(this PaintSpread spread, float t) => spread switch
        {
            PaintSpread.Clamp => Mathf.Clamp01(t),
            PaintSpread.Repeat => Mathf.Repeat(t, 1f),
            PaintSpread.Mirror => Mathf.PingPong(t, 1f),
            _ => throw new ArgumentOutOfRangeException(nameof(spread), spread, "Unknown paint spread."),
        };

        /// <summary>Packs a spread mode onto <paramref name="kindCode"/> for a vertex paint payload.</summary>
        public static float Pack(this PaintSpread spread, float kindCode)
            => kindCode + CodeStep * (float)spread;
    }

    /// <summary>
    /// Serializable, consumer-neutral projection for gradients and textures. It describes sampled
    /// geometry while leaving bounds selection and specialized projection modes to the consumer.
    /// </summary>
    [Serializable]
    public struct PaintProjection : IEquatable<PaintProjection>, IStateSnapshot<PaintProjection>
    {
        /// <summary>Geometry used to derive the sampled gradient coordinate.</summary>
        public PaintProjectionKind kind;

        /// <summary>Aspect and repetition policy used only by texture sources.</summary>
        public PaintFit fit;

        /// <summary>
        /// Continuation outside the frame, used only by gradient sources. Inert for
        /// <see cref="PaintProjectionKind.Angular"/>, whose sweep already spans exactly one period.
        /// </summary>
        public PaintSpread spread;

        /// <summary>Rotation of the projection axes in degrees.</summary>
        public float angle;

        /// <summary>Uniform zoom of the projection; non-positive values resolve to one.</summary>
        public float scale;

        /// <summary>Pan of the sample origin in normalized frame units.</summary>
        [VectorDragField]
        public Vector2 offset;

        /// <summary>Linear, stretched, clamped projection with no rotation or offset and unit scale.</summary>
        public static PaintProjection Default => new()
        {
            kind = PaintProjectionKind.Linear,
            fit = PaintFit.Stretch,
            spread = PaintSpread.Clamp,
            scale = 1f,
        };

        /// <inheritdoc/>
        public PaintProjection CaptureStateSnapshot() => this;

        /// <inheritdoc/>
        public bool StateEquals(in PaintProjection snapshot) => Equals(snapshot);

        /// <inheritdoc/>
        public bool Equals(PaintProjection other)
            => kind == other.kind &&
               fit == other.fit &&
               spread == other.spread &&
               angle.Equals(other.angle) &&
               scale.Equals(other.scale) &&
               offset.Equals(other.offset);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is PaintProjection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
            => HashCode.Combine((int)kind, (int)fit, (int)spread, angle, scale, offset);
    }
}
