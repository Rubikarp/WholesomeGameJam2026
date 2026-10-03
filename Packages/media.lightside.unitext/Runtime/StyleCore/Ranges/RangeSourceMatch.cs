using System;
using System.Collections.Generic;

namespace LightSide
{
    internal readonly struct RangeSourceMatch
    {
        public readonly RangeId rangeId;
        public readonly bool hasRangeId;
        public readonly RangeSegment[] segments;
        public readonly RangeChannel channel;
        public readonly object payload;
        public readonly TextRevision revision;
        public readonly ModifierParameters parameters;
        public readonly string matchKey;

        public RangeSourceMatch(RangeId rangeId, bool hasRangeId, RangeSegment[] segments,
            RangeChannel channel, object payload, TextRevision revision,
            in ModifierParameters parameters, string matchKey)
        {
            this.rangeId = rangeId;
            this.hasRangeId = hasRangeId;
            this.segments = segments;
            this.channel = channel;
            this.payload = payload;
            this.revision = revision;
            this.parameters = parameters;
            this.matchKey = matchKey;
        }
    }

    /// <summary>
    /// Validated output writer supplied to custom <see cref="RangeSource"/> implementations.
    /// All ranges use rendered Unicode codepoint coordinates.
    /// </summary>
    public readonly ref struct RangeMatchWriter
    {
        private readonly RangeSource source;
        private readonly TextSnapshot snapshot;
        private readonly List<RangeSourceMatch> output;

        internal RangeMatchWriter(RangeSource source, in TextSnapshot snapshot,
            List<RangeSourceMatch> output)
        {
            this.source = source;
            this.snapshot = snapshot;
            this.output = output;
        }

        internal void Add(in RangeSourceMatch match)
        {
            if (match.revision != snapshot.Revision)
                throw new InvalidOperationException(
                    $"Range match revision {match.revision} does not match snapshot revision {snapshot.Revision}.");
            if (!match.hasRangeId || !match.rangeId.IsValid)
                throw new InvalidOperationException("A retained range match requires a stable range identity.");
            for (var i = 0; i < match.segments.Length; i++)
                ValidateRange(match.segments[i].Range);
            match.channel?.ValidatePayload(match.payload);
            if (match.channel == null && match.payload != null)
                throw new InvalidOperationException("A payload requires a range channel.");
            output.Add(match);
        }

        /// <summary>Adds one anonymous single-segment match, optionally reconciled by a stable key.</summary>
        public void Add(TextRange range, string parameter = null, string defaultParameter = null,
            RangeChannel channel = null, object payload = null, string matchKey = null,
            RangePointAffinity pointAffinity = RangePointAffinity.After, RangeTracking tracking = default)
        {
            var parameters = ModifierParameters.Positional(parameter, defaultParameter);
            AddAnonymous(range, in parameters, channel, payload, matchKey, pointAffinity, tracking);
        }

        /// <summary>Adds one source-addressable single-segment entity.</summary>
        public void Add(RangeId id, TextRange range, string parameter = null,
            string defaultParameter = null, RangeChannel channel = null, object payload = null,
            RangePointAffinity pointAffinity = RangePointAffinity.After, RangeTracking tracking = default)
        {
            var parameters = ModifierParameters.Positional(parameter, defaultParameter);
            AddStable(id, range, in parameters, channel, payload, pointAffinity, tracking);
        }

        /// <summary>Adds one source-addressable entity with one or more explicit segments.</summary>
        public void Add(RangeId id, RangeSegment[] segments, string parameter = null,
            string defaultParameter = null, RangeChannel channel = null, object payload = null)
        {
            if (segments == null || segments.Length == 0)
                throw new ArgumentException("A range entity requires at least one segment.", nameof(segments));

            var copy = new RangeSegment[segments.Length];
            var ids = new HashSet<RangeSegmentId>();
            for (var i = 0; i < segments.Length; i++)
            {
                ValidateRange(segments[i].Range);
                if (!ids.Add(segments[i].Id))
                    throw new ArgumentException("Segment identities must be unique inside an entity.", nameof(segments));
                source.ReserveSegmentId(segments[i].Id);
                copy[i] = segments[i];
            }

            source.ReserveRangeId(id);
            channel?.ValidatePayload(payload);
            if (channel == null && payload != null)
                throw new ArgumentException("A payload requires a range channel.", nameof(payload));

            output.Add(new RangeSourceMatch(id, true, copy, channel, payload, snapshot.Revision,
                ModifierParameters.Positional(parameter, defaultParameter), null));
        }

        /// <summary>Adds an opaque primary value that bypasses positional tokenization.</summary>
        public void AddOpaque(TextRange range, string value, string defaultValue = null,
            RangeChannel channel = null, object payload = null, string matchKey = null,
            RangePointAffinity pointAffinity = RangePointAffinity.After, RangeTracking tracking = default)
        {
            var parameters = ModifierParameters.OpaquePrimary(value, defaultValue);
            AddAnonymous(range, in parameters, channel, payload, matchKey, pointAffinity, tracking);
        }

        /// <summary>Adds one stable entity whose primary value bypasses positional tokenization.</summary>
        public void AddOpaque(RangeId id, TextRange range, string value, string defaultValue = null,
            RangeChannel channel = null, object payload = null,
            RangePointAffinity pointAffinity = RangePointAffinity.After, RangeTracking tracking = default)
        {
            var parameters = ModifierParameters.OpaquePrimary(value, defaultValue);
            AddStable(id, range, in parameters, channel, payload, pointAffinity, tracking);
        }

        private void AddAnonymous(TextRange range, in ModifierParameters parameters,
            RangeChannel channel, object payload, string matchKey,
            RangePointAffinity pointAffinity, RangeTracking tracking)
        {
            ValidateRange(range);
            channel?.ValidatePayload(payload);
            if (channel == null && payload != null)
                throw new ArgumentException("A payload requires a range channel.", nameof(payload));

            var segment = new RangeSegment(new RangeSegmentId(1), range, pointAffinity, tracking);
            output.Add(new RangeSourceMatch(default, false, new[] { segment }, channel, payload,
                snapshot.Revision, in parameters, matchKey));
        }

        private void AddStable(RangeId id, TextRange range, in ModifierParameters parameters,
            RangeChannel channel, object payload, RangePointAffinity pointAffinity,
            RangeTracking tracking)
        {
            ValidateRange(range);
            source.ReserveRangeId(id);
            channel?.ValidatePayload(payload);
            if (channel == null && payload != null)
                throw new ArgumentException("A payload requires a range channel.", nameof(payload));

            var segment = new RangeSegment(source.GetPrimarySegmentId(id), range, pointAffinity, tracking);
            output.Add(new RangeSourceMatch(id, true, new[] { segment }, channel, payload,
                snapshot.Revision, in parameters, null));
        }

        private void ValidateRange(TextRange range)
        {
            if (range.start < 0 || range.length < 0 || range.End > snapshot.CodepointCount)
                throw new ArgumentOutOfRangeException(nameof(range),
                    $"Range [{range.start}, {range.End}) is outside snapshot revision {snapshot.Revision} with {snapshot.CodepointCount} codepoints.");
        }
    }
}
