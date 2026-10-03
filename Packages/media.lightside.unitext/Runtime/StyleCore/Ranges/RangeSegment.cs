using System;

namespace LightSide
{
    /// <summary>Chooses which side of a point receives text inserted at exactly that position.</summary>
    public enum RangePointAffinity : byte
    {
        /// <summary>The point remains before inserted text.</summary>
        Before,

        /// <summary>The point moves after inserted text.</summary>
        After
    }

    /// <summary>Chooses which side of an insertion a tracked range boundary follows.</summary>
    public enum RangeBoundaryAffinity : byte
    {
        /// <summary>The boundary remains before text inserted at its position.</summary>
        Before,

        /// <summary>The boundary moves after text inserted at its position.</summary>
        After
    }

    /// <summary>Controls a segment fully covered by deleted or replaced text.</summary>
    public enum RangeDeletionBehavior : byte
    {
        /// <summary>Remove the segment, and the entity when no segments remain.</summary>
        Remove,

        /// <summary>Keep a point segment at the edit boundary.</summary>
        Collapse
    }

    /// <summary>Tracking policy applied when a text revision replaces one contiguous region.</summary>
    public readonly struct RangeTracking
    {
        private readonly byte configured;

        /// <summary>How the inclusive start follows insertion at the same position.</summary>
        public RangeBoundaryAffinity StartAffinity { get; }

        /// <summary>How the exclusive end follows insertion at the same position.</summary>
        public RangeBoundaryAffinity EndAffinity { get; }

        /// <summary>What remains when a deletion fully covers the segment.</summary>
        public RangeDeletionBehavior DeletionBehavior { get; }

        /// <summary>
        /// Content-oriented tracking: insertion at the start stays outside, insertion at the end
        /// stays outside, and deleting all content removes the segment.
        /// </summary>
        public static RangeTracking Content { get; } = new(
            RangeBoundaryAffinity.After, RangeBoundaryAffinity.Before, RangeDeletionBehavior.Remove);

        /// <summary>Creates an explicit boundary and deletion tracking policy.</summary>
        public RangeTracking(RangeBoundaryAffinity startAffinity, RangeBoundaryAffinity endAffinity,
            RangeDeletionBehavior deletionBehavior = RangeDeletionBehavior.Remove)
        {
            StartAffinity = startAffinity;
            EndAffinity = endAffinity;
            DeletionBehavior = deletionBehavior;
            configured = 1;
        }

        internal RangeTracking OrDefault() => configured == 0 ? Content : this;
    }

    /// <summary>Identifies a segment inside a multi-segment entity.</summary>
    public readonly struct RangeSegmentId : IEquatable<RangeSegmentId>
    {
        /// <summary>Source-local nonzero identifier.</summary>
        public uint Value { get; }

        /// <summary>Whether this identifier can address a segment.</summary>
        public bool IsValid => Value != 0;

        /// <summary>Creates a nonzero source-local segment identifier.</summary>
        public RangeSegmentId(uint value)
        {
            if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        /// <inheritdoc/>
        public bool Equals(RangeSegmentId other) => Value == other.Value;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RangeSegmentId other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => (int)Value;
        /// <inheritdoc/>
        public override string ToString() => IsValid ? Value.ToString() : "Invalid";
        /// <summary>Compares two segment identifiers.</summary>
        public static bool operator ==(RangeSegmentId left, RangeSegmentId right) => left.Equals(right);
        /// <summary>Compares two segment identifiers.</summary>
        public static bool operator !=(RangeSegmentId left, RangeSegmentId right) => !left.Equals(right);
    }

    /// <summary>One contiguous or point-like target of a range entity.</summary>
    public readonly struct RangeSegment
    {
        /// <summary>Stable identity within the owning range entity.</summary>
        public RangeSegmentId Id { get; }

        /// <summary>Rendered Unicode codepoint range.</summary>
        public TextRange Range { get; }

        /// <summary>Tracking side used when <see cref="Range"/> is a point.</summary>
        public RangePointAffinity PointAffinity { get; }

        /// <summary>Boundary and deletion tracking for non-point ranges.</summary>
        public RangeTracking Tracking { get; }

        /// <summary>Whether this segment addresses a position instead of text content.</summary>
        public bool IsPoint => Range.length == 0;

        /// <summary>Creates one contiguous or point-like segment in rendered codepoint space.</summary>
        public RangeSegment(RangeSegmentId id, TextRange range,
            RangePointAffinity pointAffinity = RangePointAffinity.After, RangeTracking tracking = default)
        {
            if (!id.IsValid) throw new ArgumentException("Segment identity is invalid.", nameof(id));
            if (range.start < 0) throw new ArgumentOutOfRangeException(nameof(range));
            if (range.length < 0) throw new ArgumentOutOfRangeException(nameof(range));
            Id = id;
            Range = range;
            PointAffinity = pointAffinity;
            Tracking = tracking.OrDefault();
        }
    }
}
