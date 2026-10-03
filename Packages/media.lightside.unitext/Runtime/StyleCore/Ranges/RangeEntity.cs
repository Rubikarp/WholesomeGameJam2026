using System;

namespace LightSide
{
    /// <summary>Immutable runtime view of one logical range and all of its segments.</summary>
    public readonly struct RangeEntity
    {
        private readonly RangeSegment[] segments;
        private readonly object payload;

        /// <summary>Stable source-scoped runtime identity.</summary>
        public RangeIdentity Identity { get; }
        /// <summary>Optional project asset used for semantic routing.</summary>
        public RangeChannel Channel { get; }
        /// <summary>Rendered-text revision that owns all segment coordinates.</summary>
        public TextRevision Revision { get; }
        /// <summary>One or more contiguous or point-like targets.</summary>
        public ReadOnlySpan<RangeSegment> Segments => segments;
        /// <summary>Typed read-only access to the optional semantic payload.</summary>
        public RangePayloadView Payload => new(payload);

        internal RangeEntity(RangeIdentity identity, RangeSegment[] segments, RangeChannel channel,
            object payload, TextRevision revision)
        {
            if (!identity.IsValid) throw new ArgumentException("Range identity is invalid.", nameof(identity));
            if (segments == null || segments.Length == 0)
                throw new ArgumentException("A range entity requires at least one segment.", nameof(segments));
            channel?.ValidatePayload(payload);
            if (channel == null && payload != null)
                throw new ArgumentException("A payload requires a range channel.", nameof(payload));

            Identity = identity;
            this.segments = segments;
            Channel = channel;
            this.payload = payload;
            Revision = revision;
        }
    }
}
