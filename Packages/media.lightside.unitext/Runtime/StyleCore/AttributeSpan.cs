using System;


namespace LightSide
{
    internal struct AttributeSpan : IEquatable<AttributeSpan>
    {
        public int start;


        public int end;


        public readonly BaseModifier modifier;


        public readonly ModifierParameters parameters;

        public readonly RangeSource source;

        public readonly RangeChannel channel;

        public readonly object payload;

        public string matchKey;

        public readonly RangePointAffinity pointAffinity;

        public readonly RangeTracking tracking;

        private RangeId rangeId;

        private TextRevision revision;

        public RangeSegmentId segmentId;

        public bool HasAttribution => rangeId.IsValid;

        public RangeAttribution Attribution
            => RangeAttribution.Restore(source, new RangeIdentity(source.RuntimeId, rangeId),
                channel, payload, revision);

        public string parameter => parameters.ResolvedValue;

        public int Length => end - start;

        public AttributeSpan(int start, int end, BaseModifier modifier, ModifierParameters parameters,
            RangeSource source, RangeChannel channel = null, object payload = null,
            string matchKey = null, RangePointAffinity pointAffinity = RangePointAffinity.After,
            RangeTracking tracking = default)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            this.start = start;
            this.end = end;
            this.modifier = modifier;
            this.parameters = parameters;
            this.source = source;
            this.channel = channel;
            this.payload = payload;
            this.matchKey = matchKey;
            this.pointAffinity = pointAffinity;
            this.tracking = tracking.OrDefault();
            rangeId = default;
            revision = default;
            segmentId = default;
        }

        public AttributeSpan(int start, int end, BaseModifier modifier, ModifierParameters parameters,
            RangeAttribution attribution, RangeSegmentId segmentId,
            RangePointAffinity pointAffinity, RangeTracking tracking)
        {
            if (!attribution.IsValid) throw new ArgumentException("Range attribution is invalid.", nameof(attribution));
            this.start = start;
            this.end = end;
            this.modifier = modifier;
            this.parameters = parameters;
            source = attribution.source;
            channel = attribution.channel;
            payload = attribution.payload;
            matchKey = null;
            this.pointAffinity = pointAffinity;
            this.tracking = tracking.OrDefault();
            rangeId = attribution.identity.Range;
            revision = attribution.revision;
            this.segmentId = segmentId;
        }

        public void AssignAttribution(in RangeAttribution value, RangeSegmentId valueSegmentId)
        {
            if (!value.IsValid)
                throw new ArgumentException("Range attribution is invalid.", nameof(value));
            if (!ReferenceEquals(source, value.source) || !ReferenceEquals(channel, value.channel) ||
                !ReferenceEquals(payload, value.payload))
                throw new InvalidOperationException("Range attribution does not belong to this span.");
            rangeId = value.identity.Range;
            revision = value.revision;
            segmentId = valueSegmentId;
            matchKey = null;
        }

        public bool Contains(int index)
        {
            return index >= start && index < end;
        }

        public bool Overlaps(AttributeSpan other)
        {
            return start < other.end && end > other.start;
        }

        public bool Equals(AttributeSpan other)
        {
            return start == other.start && end == other.end && ReferenceEquals(modifier, other.modifier);
        }

        public override bool Equals(object obj)
        {
            return obj is AttributeSpan other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(start, end, modifier);
        }

        public static bool operator ==(AttributeSpan left, AttributeSpan right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AttributeSpan left, AttributeSpan right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"[{start}-{end}] {modifier?.GetType().Name ?? "null"}";
        }
    }
}
