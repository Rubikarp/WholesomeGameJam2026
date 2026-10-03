using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Which authored resource supplies a paint's colour.</summary>
    public enum PaintSourceKind : byte
    {
        /// <summary>A single colour with no sampled resource.</summary>
        Solid = 0,

        /// <summary>A colour ramp sampled through a projection.</summary>
        Gradient = 1,

        /// <summary>A 2D texture sampled through a projection.</summary>
        Texture = 2,
    }

    /// <summary>
    /// Serializable paint content shared by renderers. <see cref="color"/> is the solid value for
    /// <see cref="PaintSourceKind.Solid"/> and the tint multiplied with sampled gradients or textures.
    /// Projection and consumer-specific placement policy live outside this value.
    /// </summary>
    [Serializable]
    public struct PaintSource : IEquatable<PaintSource>, IStateSnapshot<PaintSource>
    {
        /// <summary>A white solid source with a valid retained default gradient.</summary>
        public static PaintSource Default => new()
        {
            kind = PaintSourceKind.Solid,
            color = Color.white,
            gradient = Gradient.Default,
        };

        /// <summary>Selects the active source while retaining the authored state of the other fields.</summary>
        public PaintSourceKind kind;

        /// <summary>Solid colour or tint multiplied with the sampled source.</summary>
        public Color color;

        /// <summary>Colour ramp used when <see cref="kind"/> is <see cref="PaintSourceKind.Gradient"/>.</summary>
        public Gradient gradient;

        /// <summary>Texture used when <see cref="kind"/> is <see cref="PaintSourceKind.Texture"/>.</summary>
        public Texture2D texture;

        /// <summary>Captures the source with detached gradient-stop storage.</summary>
        public PaintSource CaptureStateSnapshot()
        {
            var snapshot = this;
            snapshot.gradient = gradient.CaptureStateSnapshot();
            return snapshot;
        }

        /// <summary>Compares all authored source state, including inactive fields and gradient contents.</summary>
        public bool StateEquals(in PaintSource snapshot)
        {
            var snapshotGradient = snapshot.gradient;
            return kind == snapshot.kind &&
                   color.Equals(snapshot.color) &&
                   gradient.StateEquals(in snapshotGradient) &&
                   ReferenceEquals(texture, snapshot.texture);
        }

        /// <inheritdoc/>
        public bool Equals(PaintSource other) => StateEquals(in other);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is PaintSource other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine((int)kind, color, gradient, texture);
    }

    /// <summary>Consumer-neutral paint appearance: colour source, projection, and layer compositing.</summary>
    [Serializable]
    public struct Paint : IEquatable<Paint>, IStateSnapshot<Paint>
    {
        /// <summary>A white solid paint using normal source-over compositing.</summary>
        public static Paint Default => new()
        {
            source = PaintSource.Default,
            projection = PaintProjection.Default,
            blend = LayerBlend.Normal,
        };

        /// <summary>Solid colour, gradient, or texture payload.</summary>
        public PaintSource source;

        /// <summary>Projection used by gradients and textures.</summary>
        public PaintProjection projection;

        /// <summary>How the painted layer composites with the layers beneath it.</summary>
        public LayerBlend blend;

        /// <inheritdoc/>
        public Paint CaptureStateSnapshot()
        {
            var snapshot = this;
            snapshot.source = source.CaptureStateSnapshot();
            return snapshot;
        }

        /// <inheritdoc/>
        public bool StateEquals(in Paint snapshot)
        {
            var snapshotSource = snapshot.source;
            var snapshotProjection = snapshot.projection;
            return source.StateEquals(in snapshotSource) &&
                   projection.StateEquals(in snapshotProjection) &&
                   blend == snapshot.blend;
        }

        /// <inheritdoc/>
        public bool Equals(Paint other) => StateEquals(in other);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Paint other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(source, projection, (int)blend);
    }
}
