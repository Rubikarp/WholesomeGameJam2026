using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Runtime-owned range source for search results, diagnostics, annotations and other
    /// externally produced ranges. Changes become visible through atomic transactions.
    /// </summary>
    [Serializable]
    [TypeMenuSuffix("RangeSource", "Source")]
    [TypeGroup("Ranges", 0)]
    public sealed partial class MutableRangeSource : RangeSource
    {
        [StateField(nameof(NotifyChanged))]
        [SerializeField] private string identity;

        [NonSerialized] private RangeSourceMatch[] matches = Array.Empty<RangeSourceMatch>();
        [NonSerialized] private int version;

        /// <summary>
        /// Stable configuration identity used by tooling. Runtime entity identity still uses
        /// this source instance's <see cref="RangeSource.RuntimeId"/>.
        /// </summary>
        public override string Identity => string.IsNullOrEmpty(identity) ? base.Identity : identity;

        /// <summary>Number of committed logical range entities.</summary>
        public int Count
        {
            get
            {
                lock (this) return matches.Length;
            }
        }

        /// <summary>Copies the currently committed entity snapshot for inspection or event correlation.</summary>
        public RangeEntity[] CaptureEntities()
        {
            lock (this)
            {
                var result = new RangeEntity[matches.Length];
                for (var i = 0; i < matches.Length; i++)
                {
                    var match = matches[i];
                    var segments = new RangeSegment[match.segments.Length];
                    Array.Copy(match.segments, segments, segments.Length);
                    result[i] = new RangeEntity(new RangeIdentity(RuntimeId, match.rangeId), segments,
                        match.channel, match.payload, match.revision);
                }
                return result;
            }
        }

        /// <summary>Gets one committed entity by its source-local identity.</summary>
        public bool TryGetEntity(RangeId id, out RangeEntity entity)
        {
            lock (this)
            {
                for (var i = 0; i < matches.Length; i++)
                {
                    var match = matches[i];
                    if (match.rangeId != id) continue;
                    var segments = new RangeSegment[match.segments.Length];
                    Array.Copy(match.segments, segments, segments.Length);
                    entity = new RangeEntity(new RangeIdentity(RuntimeId, id), segments,
                        match.channel, match.payload, match.revision);
                    return true;
                }
            }

            entity = default;
            return false;
        }

        /// <summary>Sets the tooling identity without changing existing entity identifiers.</summary>
        public void SetIdentity(string value) => SetIdentityState(value);

        /// <summary>
        /// Begins an isolated update against <paramref name="revision"/>. Commit fails if text
        /// or this source changed after the transaction began.
        /// </summary>
        public MutableRangeUpdate BeginUpdate(TextRevision revision)
        {
            lock (this)
            {
                var current = Snapshot;
                if (!current.IsValid)
                    throw new InvalidOperationException(
                        "MutableRangeSource is not bound to a UniText component with a completed parse.");
                if (revision != current.Revision)
                    throw new InvalidOperationException(
                        $"Range update targets stale text revision {revision}; current revision is {current.Revision}.");

                var copy = new Dictionary<RangeId, RangeSourceMatch>(matches.Length);
                for (var i = 0; i < matches.Length; i++)
                    copy.Add(matches[i].rangeId, matches[i]);
                return new MutableRangeUpdate(this, revision, version, copy);
            }
        }

        /// <summary>Begins an update against an immutable snapshot captured from the owner.</summary>
        public MutableRangeUpdate BeginUpdate(in TextSnapshot textSnapshot)
        {
            if (!textSnapshot.IsValid)
                throw new ArgumentException("Text snapshot is invalid.", nameof(textSnapshot));
            return BeginUpdate(textSnapshot.Revision);
        }

        /// <summary>Replaces all entities with single-segment ranges in one notification.</summary>
        public void SetRanges(TextRevision revision, IEnumerable<TextRange> ranges)
        {
            if (ranges == null) throw new ArgumentNullException(nameof(ranges));
            using var update = BeginUpdate(revision);
            update.Clear();
            foreach (var range in ranges)
                update.Add(range);
            update.Commit();
        }

        protected override void CollectRanges(in TextSnapshot currentSnapshot, RangeMatchWriter writer)
        {
            RangeSourceMatch[] current;
            lock (this) current = matches;
            for (var i = 0; i < current.Length; i++)
            {
                var match = current[i];
                if (match.revision != currentSnapshot.Revision)
                    throw new InvalidOperationException(
                        $"Range source '{Identity}' contains revision {match.revision} while the rendered text is {currentSnapshot.Revision}.");
                writer.Add(in match);
            }
        }

        protected override void OnSnapshotChanged(in TextSnapshot previous, in TextSnapshot current)
        {
            if (!previous.IsValid || matches.Length == 0) return;

            var edit = TextEditMap.Detect(previous.Text.Span, current.Text.Span);
            var remapped = new List<RangeSourceMatch>(matches.Length);
            for (var i = 0; i < matches.Length; i++)
            {
                var match = matches[i];
                var segments = RemapSegments(match.segments, in edit, current.CodepointCount);
                if (segments.Length == 0) continue;
                remapped.Add(new RangeSourceMatch(match.rangeId, true, segments, match.channel,
                    match.payload, current.Revision, in match.parameters, match.matchKey));
            }

            matches = remapped.ToArray();
            version++;
        }

        internal TextSnapshot RequireSnapshot(TextRevision expected)
        {
            var current = Snapshot;
            if (!current.IsValid)
                throw new InvalidOperationException("The source is not bound to a completed text snapshot.");
            if (current.Revision != expected)
                throw new InvalidOperationException(
                    $"Range update targets stale text revision {expected}; current revision is {current.Revision}.");
            return current;
        }

        internal RangeSourceMatch CreateMatch(RangeId id, RangeSegment[] segments,
            RangeChannel channel, object payload, TextRevision revision,
            in ModifierParameters parameters)
        {
            var snapshot = RequireSnapshot(revision);
            if (!id.IsValid) throw new ArgumentException("Range identity is invalid.", nameof(id));
            if (segments == null || segments.Length == 0)
                throw new ArgumentException("A range entity requires at least one segment.", nameof(segments));

            var ids = new HashSet<RangeSegmentId>();
            var copy = new RangeSegment[segments.Length];
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.Range.start < 0 || segment.Range.length < 0 ||
                    segment.Range.End > snapshot.CodepointCount)
                    throw new ArgumentOutOfRangeException(nameof(segments),
                        $"Segment [{segment.Range.start}, {segment.Range.End}) is outside revision {revision} with {snapshot.CodepointCount} codepoints.");
                if (!ids.Add(segment.Id))
                    throw new ArgumentException("Segment identities must be unique inside an entity.", nameof(segments));
                ReserveSegmentId(segment.Id);
                copy[i] = segment;
            }

            ReserveRangeId(id);
            channel?.ValidatePayload(payload);
            if (channel == null && payload != null)
                throw new ArgumentException("A payload requires a range channel.", nameof(payload));
            return new RangeSourceMatch(id, true, copy, channel, payload, revision,
                in parameters, null);
        }

        internal void Commit(TextRevision revision, int expectedVersion,
            Dictionary<RangeId, RangeSourceMatch> updated)
        {
            lock (this)
            {
                RequireSnapshot(revision);
                if (version != expectedVersion)
                    throw new InvalidOperationException(
                        "MutableRangeSource changed after this update began. Start a new transaction from the current state.");

                var result = new RangeSourceMatch[updated.Count];
                var index = 0;
                foreach (var pair in updated)
                    result[index++] = pair.Value;
                Array.Sort(result, static (left, right) => left.rangeId.Value.CompareTo(right.rangeId.Value));
                matches = result;
                version++;
            }

            NotifyChanged();
        }

        private static RangeSegment[] RemapSegments(RangeSegment[] source, in TextEditMap edit, int textLength)
        {
            var result = new RangeSegment[source.Length];
            var count = 0;
            for (var i = 0; i < source.Length; i++)
            {
                var segment = source[i];
                if (segment.IsPoint)
                {
                    var affinity = segment.PointAffinity == RangePointAffinity.Before
                        ? RangeBoundaryAffinity.Before
                        : RangeBoundaryAffinity.After;
                    var position = edit.MapPosition(segment.Range.start, affinity);
                    position = Math.Clamp(position, 0, textLength);
                    result[count++] = new RangeSegment(segment.Id, new TextRange(position, 0),
                        segment.PointAffinity, segment.Tracking);
                    continue;
                }

                var range = segment.Range;
                var fullyRemoved = edit.removed > 0 && edit.start <= range.start && edit.OldEnd >= range.End;
                if (fullyRemoved && segment.Tracking.DeletionBehavior == RangeDeletionBehavior.Remove)
                    continue;

                var start = edit.MapPosition(range.start, segment.Tracking.StartAffinity);
                var end = edit.MapPosition(range.End, segment.Tracking.EndAffinity);
                start = Math.Clamp(start, 0, textLength);
                end = Math.Clamp(end, start, textLength);
                result[count++] = new RangeSegment(segment.Id, new TextRange(start, end - start),
                    segment.PointAffinity, segment.Tracking);
            }

            if (count == result.Length) return result;
            var trimmed = new RangeSegment[count];
            Array.Copy(result, trimmed, count);
            return trimmed;
        }

    }

    /// <summary>An isolated mutable-source transaction committed as one range-set change.</summary>
    public sealed class MutableRangeUpdate : IDisposable
    {
        private MutableRangeSource source;
        private readonly TextRevision revision;
        private readonly int version;
        private readonly Dictionary<RangeId, RangeSourceMatch> ranges;
        private bool completed;

        internal MutableRangeUpdate(MutableRangeSource source, TextRevision revision, int version,
            Dictionary<RangeId, RangeSourceMatch> ranges)
        {
            this.source = source;
            this.revision = revision;
            this.version = version;
            this.ranges = ranges;
        }

        /// <summary>Adds a new single-segment entity and returns its source-local identity.</summary>
        public RangeId Add(TextRange range, string parameter = null, string defaultParameter = null,
            RangeChannel channel = null, object payload = null, RangeTracking tracking = default,
            RangePointAffinity pointAffinity = RangePointAffinity.After)
        {
            EnsureOpen();
            var id = source.AllocateRangeId();
            var segment = new RangeSegment(source.AllocateSegmentId(), range, pointAffinity, tracking);
            ranges.Add(id, source.CreateMatch(id, new[] { segment }, channel, payload, revision,
                ModifierParameters.Positional(parameter, defaultParameter)));
            return id;
        }

        /// <summary>Adds a new entity through a channel facade that validates its payload type.</summary>
        public RangeId Add<T>(TextRange range, RangeChannel<T> channel, T payload,
            string parameter = null, string defaultParameter = null, RangeTracking tracking = default,
            RangePointAffinity pointAffinity = RangePointAffinity.After)
            => Add(range, parameter, defaultParameter, channel.Untyped, payload, tracking, pointAffinity);

        /// <summary>Adds a new entity whose primary value bypasses positional tokenization.</summary>
        public RangeId AddOpaque(TextRange range, string value, string defaultValue = null,
            RangeChannel channel = null, object payload = null, RangeTracking tracking = default,
            RangePointAffinity pointAffinity = RangePointAffinity.After)
        {
            EnsureOpen();
            var id = source.AllocateRangeId();
            var segment = new RangeSegment(source.AllocateSegmentId(), range, pointAffinity, tracking);
            ranges.Add(id, source.CreateMatch(id, new[] { segment }, channel, payload, revision,
                ModifierParameters.OpaquePrimary(value, defaultValue)));
            return id;
        }

        /// <summary>Adds or restores an entity with a caller-supplied stable identity.</summary>
        public void Add(RangeId id, RangeSegment[] segments, string parameter = null,
            string defaultParameter = null, RangeChannel channel = null, object payload = null)
        {
            EnsureOpen();
            ranges.Add(id, source.CreateMatch(id, segments, channel, payload, revision,
                ModifierParameters.Positional(parameter, defaultParameter)));
        }

        /// <summary>Adds or restores one single-segment entity with a caller-supplied identity.</summary>
        public void Add(RangeId id, TextRange range, string parameter = null,
            string defaultParameter = null, RangeChannel channel = null, object payload = null,
            RangeTracking tracking = default, RangePointAffinity pointAffinity = RangePointAffinity.After)
        {
            EnsureOpen();
            var segment = new RangeSegment(source.GetPrimarySegmentId(id), range, pointAffinity, tracking);
            ranges.Add(id, source.CreateMatch(id, new[] { segment }, channel, payload, revision,
                ModifierParameters.Positional(parameter, defaultParameter)));
        }

        /// <summary>Moves a single-segment entity while preserving its range and segment identities.</summary>
        public void Move(RangeId id, TextRange range)
        {
            EnsureOpen();
            if (!ranges.TryGetValue(id, out var existing))
                throw new KeyNotFoundException($"Range {id} does not exist in this source.");
            if (existing.segments.Length != 1)
                throw new InvalidOperationException("Use the segment overload to move a multi-segment entity.");

            var previous = existing.segments[0];
            var segment = new RangeSegment(previous.Id, range, previous.PointAffinity, previous.Tracking);
            ranges[id] = source.CreateMatch(id, new[] { segment }, existing.channel, existing.payload,
                revision, in existing.parameters);
        }

        /// <summary>Replaces an entity's segments while preserving its range identity.</summary>
        public void Move(RangeId id, RangeSegment[] segments)
        {
            EnsureOpen();
            if (!ranges.TryGetValue(id, out var existing))
                throw new KeyNotFoundException($"Range {id} does not exist in this source.");
            ranges[id] = source.CreateMatch(id, segments, existing.channel, existing.payload,
                revision, in existing.parameters);
        }

        /// <summary>Changes presentation overrides without touching identity, geometry or payload.</summary>
        public void SetParameters(RangeId id, string parameter, string defaultParameter = null)
        {
            EnsureOpen();
            if (!ranges.TryGetValue(id, out var existing))
                throw new KeyNotFoundException($"Range {id} does not exist in this source.");
            ranges[id] = source.CreateMatch(id, existing.segments, existing.channel, existing.payload,
                revision, ModifierParameters.Positional(parameter, defaultParameter));
        }

        /// <summary>Changes the opaque primary value without tokenizing delimiters inside it.</summary>
        public void SetOpaqueValue(RangeId id, string value, string defaultValue = null)
        {
            EnsureOpen();
            if (!ranges.TryGetValue(id, out var existing))
                throw new KeyNotFoundException($"Range {id} does not exist in this source.");
            ranges[id] = source.CreateMatch(id, existing.segments, existing.channel, existing.payload,
                revision, ModifierParameters.OpaquePrimary(value, defaultValue));
        }

        /// <summary>Changes semantic routing and payload without touching identity or geometry.</summary>
        public void SetPayload(RangeId id, RangeChannel channel, object payload)
        {
            EnsureOpen();
            if (!ranges.TryGetValue(id, out var existing))
                throw new KeyNotFoundException($"Range {id} does not exist in this source.");
            ranges[id] = source.CreateMatch(id, existing.segments, channel, payload,
                revision, in existing.parameters);
        }

        /// <summary>Changes semantic routing through a channel facade that validates its payload type.</summary>
        public void SetPayload<T>(RangeId id, RangeChannel<T> channel, T payload)
            => SetPayload(id, channel.Untyped, payload);

        /// <summary>Removes an entity if it exists in the transaction.</summary>
        public bool Remove(RangeId id)
        {
            EnsureOpen();
            return ranges.Remove(id);
        }

        /// <summary>Removes all entities from the transaction.</summary>
        public void Clear()
        {
            EnsureOpen();
            ranges.Clear();
        }

        /// <summary>Atomically publishes all accumulated changes.</summary>
        public void Commit()
        {
            EnsureOpen();
            source.Commit(revision, version, ranges);
            completed = true;
            source = null;
        }

        /// <summary>Discards an uncommitted transaction.</summary>
        public void Dispose()
        {
            completed = true;
            source = null;
        }

        private void EnsureOpen()
        {
            if (completed || source == null)
                throw new ObjectDisposedException(nameof(MutableRangeUpdate));
        }
    }
}
