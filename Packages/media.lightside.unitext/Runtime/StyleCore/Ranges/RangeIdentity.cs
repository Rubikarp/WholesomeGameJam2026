using System;

namespace LightSide
{
    /// <summary>Identifies one range inside its owning source.</summary>
    public readonly struct RangeId : IEquatable<RangeId>
    {
        /// <summary>Source-local nonzero identifier.</summary>
        public ulong Value { get; }
        /// <summary>Whether this value can address a range entity.</summary>
        public bool IsValid => Value != 0;

        /// <summary>Creates a nonzero source-local range identifier.</summary>
        public RangeId(ulong value)
        {
            if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        /// <inheritdoc/>
        public bool Equals(RangeId other) => Value == other.Value;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RangeId other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => Value.GetHashCode();
        /// <inheritdoc/>
        public override string ToString() => IsValid ? Value.ToString() : "Invalid";
        /// <summary>Compares two range identifiers.</summary>
        public static bool operator ==(RangeId left, RangeId right) => left.Equals(right);
        /// <summary>Compares two range identifiers.</summary>
        public static bool operator !=(RangeId left, RangeId right) => !left.Equals(right);
    }

    /// <summary>Identifies one bound range source for the lifetime of its runtime instance.</summary>
    public readonly struct RangeSourceId : IEquatable<RangeSourceId>
    {
        /// <summary>Process-local nonzero source identifier.</summary>
        public ulong Value { get; }
        /// <summary>Whether this value can address a bound source.</summary>
        public bool IsValid => Value != 0;

        internal RangeSourceId(ulong value) => Value = value;

        /// <inheritdoc/>
        public bool Equals(RangeSourceId other) => Value == other.Value;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RangeSourceId other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => Value.GetHashCode();
        /// <inheritdoc/>
        public override string ToString() => IsValid ? Value.ToString() : "Invalid";
        /// <summary>Compares two source identifiers.</summary>
        public static bool operator ==(RangeSourceId left, RangeSourceId right) => left.Equals(right);
        /// <summary>Compares two source identifiers.</summary>
        public static bool operator !=(RangeSourceId left, RangeSourceId right) => !left.Equals(right);
    }

    /// <summary>Stable runtime address of a range entity.</summary>
    public readonly struct RangeIdentity : IEquatable<RangeIdentity>
    {
        /// <summary>Runtime source that owns the local range identifier.</summary>
        public RangeSourceId Source { get; }
        /// <summary>Identifier local to <see cref="Source"/>.</summary>
        public RangeId Range { get; }
        /// <summary>Whether both parts form an addressable identity.</summary>
        public bool IsValid => Source.IsValid && Range.IsValid;

        /// <summary>Creates a stable runtime entity address.</summary>
        public RangeIdentity(RangeSourceId source, RangeId range)
        {
            if (!source.IsValid) throw new ArgumentException("Source identity is invalid.", nameof(source));
            if (!range.IsValid) throw new ArgumentException("Range identity is invalid.", nameof(range));
            Source = source;
            Range = range;
        }

        /// <inheritdoc/>
        public bool Equals(RangeIdentity other) => Source == other.Source && Range == other.Range;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RangeIdentity other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Source, Range);
        /// <inheritdoc/>
        public override string ToString() => IsValid ? $"{Source}:{Range}" : "Invalid";
        /// <summary>Compares two entity identities.</summary>
        public static bool operator ==(RangeIdentity left, RangeIdentity right) => left.Equals(right);
        /// <summary>Compares two entity identities.</summary>
        public static bool operator !=(RangeIdentity left, RangeIdentity right) => !left.Equals(right);
    }
}
