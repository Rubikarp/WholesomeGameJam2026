using System;

namespace LightSide
{
    /// <summary>Immutable entity, source and parameter data supplied to a modifier application.</summary>
    public readonly ref struct RangeApplyContext
    {
        /// <summary>Stable source-scoped identity shared by every segment of the entity.</summary>
        public RangeIdentity Identity { get; }
        /// <summary>The concrete entity segment being applied, in rendered codepoint space.</summary>
        public RangeSegment Segment { get; }
        /// <summary>Optional project asset used for semantic routing.</summary>
        public RangeChannel Channel { get; }
        /// <summary>Optional immutable semantic value associated with the entity.</summary>
        public RangePayloadView Payload { get; }
        /// <summary>The source that owns identity, geometry and lifetime.</summary>
        public RangeSource Source { get; }
        /// <summary>The rendered-text revision against which the segment is valid.</summary>
        public TextRevision Revision { get; }
        /// <summary>Explicit and source-default presentation overrides for this modifier node.</summary>
        public ModifierParameters Parameters { get; }
        /// <summary>
        /// Opaque semantic value emitted by the source, independent from Composite presentation
        /// slots. Mention handles, URLs and similar values remain available to every child.
        /// </summary>
        public string PrimaryValue { get; }
        internal RangeApplyContext(in RangeAttribution attribution, in RangeSegment segment,
            in ModifierParameters parameters)
        {
            if (!attribution.IsValid) throw new ArgumentException("Range attribution is invalid.", nameof(attribution));
            Identity = attribution.identity;
            Segment = segment;
            Channel = attribution.channel;
            Payload = new RangePayloadView(attribution.payload);
            Source = attribution.source;
            Revision = attribution.revision;
            Parameters = parameters;
            PrimaryValue = parameters.PrimaryValue;
        }

        internal RangeApplyContext WithParameters(in ModifierParameters parameters)
            => new(Identity, Segment, Channel, Payload, Source, Revision, parameters, PrimaryValue);

        internal static RangeApplyContext ForRule(in InteractiveRange range)
            => new(range.Identity, range.Segment, range.Channel, range.Payload, range.Source,
                range.Revision, default, range.PrimaryValue);

        /// <summary>Copies the context into a storable snapshot, valid until the parse that produced it is replaced.</summary>
        internal RangeApplyMemo Retain() => new(in this);

        internal RangeApplyContext(RangeIdentity identity, RangeSegment segment, RangeChannel channel,
            RangePayloadView payload, RangeSource source, TextRevision revision,
            ModifierParameters parameters, string primaryValue)
        {
            Identity = identity;
            Segment = segment;
            Channel = channel;
            Payload = payload;
            Source = source;
            Revision = revision;
            Parameters = parameters;
            PrimaryValue = primaryValue;
        }
    }

    /// <summary>
    /// Storable snapshot of one <see cref="RangeApplyContext"/>, for range state a modifier
    /// re-resolves after application. Valid for the parse that produced it; an apply cycle that
    /// replaces the entry must capture a fresh snapshot.
    /// </summary>
    internal readonly struct RangeApplyMemo
    {
        private readonly RangeIdentity identity;
        private readonly RangeSegment segment;
        private readonly RangeChannel channel;
        private readonly RangePayloadView payload;
        private readonly RangeSource source;
        private readonly TextRevision revision;
        private readonly ModifierParameters parameters;
        private readonly string primaryValue;

        internal RangeApplyMemo(in RangeApplyContext context)
        {
            identity = context.Identity;
            segment = context.Segment;
            channel = context.Channel;
            payload = context.Payload;
            source = context.Source;
            revision = context.Revision;
            parameters = context.Parameters;
            primaryValue = context.PrimaryValue;
        }

        /// <summary>Rebuilds the applied context this snapshot was taken from.</summary>
        internal RangeApplyContext ToContext()
            => new(identity, segment, channel, payload, source, revision, parameters, primaryValue);
    }
}
