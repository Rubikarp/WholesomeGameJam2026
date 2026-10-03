namespace LightSide
{
    internal readonly struct InteractiveRangeSettings
    {
        public readonly int priority;
        public readonly RangeInteractionScope activationScope;
        public readonly UnitVector2 hitPadding;
        public readonly UnitVector2 minimumHitSize;
        public readonly WrappedGapPolicy gapPolicy;
        public readonly RangeWhitespacePolicy whitespacePolicy;
        public readonly InlineObjectPolicy inlineObjectPolicy;
        public readonly bool passThrough;

        public InteractiveRangeSettings(int priority, RangeInteractionScope activationScope,
            UnitVector2 hitPadding, UnitVector2 minimumHitSize, WrappedGapPolicy gapPolicy,
            RangeWhitespacePolicy whitespacePolicy, InlineObjectPolicy inlineObjectPolicy,
            bool passThrough)
        {
            this.priority = priority;
            this.activationScope = activationScope;
            this.hitPadding = hitPadding;
            this.minimumHitSize = minimumHitSize;
            this.gapPolicy = gapPolicy;
            this.whitespacePolicy = whitespacePolicy;
            this.inlineObjectPolicy = inlineObjectPolicy;
            this.passThrough = passThrough;
        }
    }

    /// <summary>
    /// Represents a clickable / hoverable / long-pressable range within text.
    /// </summary>
    /// <remarks>
    /// Produced by <see cref="InteractiveModifier"/> subclasses to mark interactive regions
    /// like links, hashtags, mentions, or custom clickable areas. Ranges are stored per owning
    /// modifier; overlapping targets resolve through explicit priority, Style order, specificity
    /// and stable registration order before one routed event is emitted.
    /// </remarks>
    public readonly struct InteractiveRange
    {
        /// <summary>Start cluster index (inclusive).</summary>
        public readonly int start;

        /// <summary>End cluster index (exclusive).</summary>
        public readonly int end;

        private readonly object payload;
        private readonly string primaryValue;
        internal readonly InteractiveRangeSettings settings;

        /// <summary>Stable source-scoped identity shared by every segment of the entity.</summary>
        public RangeIdentity Identity { get; }

        /// <summary>Concrete segment represented by this target.</summary>
        public RangeSegment Segment { get; }

        /// <summary>Stable semantic routing asset, or null for modifier-local routing.</summary>
        public RangeChannel Channel { get; }

        /// <summary>Range source that owns identity and lifetime.</summary>
        public RangeSource Source { get; }

        /// <summary>Rendered-text revision against which this range is valid.</summary>
        public TextRevision Revision { get; }

        /// <summary>Typed semantic value emitted by the source.</summary>
        public RangePayloadView Payload => new(payload);
        /// <summary>Opaque primary source value, such as a URL or matched mention text.</summary>
        public string PrimaryValue => primaryValue;

        internal InteractiveRange(in RangeApplyContext context, string primaryValue,
            RangeChannel channel, in InteractiveRangeSettings settings)
        {
            start = context.Segment.Range.start;
            end = context.Segment.Range.End;
            this.primaryValue = primaryValue;
            payload = context.Payload.Value;
            Identity = context.Identity;
            Segment = context.Segment;
            Channel = channel;
            Source = context.Source;
            Revision = context.Revision;
            this.settings = settings;
        }

        /// <summary>Returns true if the cluster is within this range.</summary>
        public bool Contains(int cluster) => cluster >= start && cluster < end;

        /// <summary>Returns an empty/invalid range.</summary>
        public static InteractiveRange None => default;

        /// <summary>Returns whether this target has a current source-owned identity.</summary>
        public bool IsValid => Identity.IsValid && Segment.Id.IsValid && Source != null;
    }
}
