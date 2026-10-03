namespace LightSide
{
    internal readonly struct RangeAttribution
    {
        public readonly RangeSource source;
        public readonly RangeIdentity identity;
        public readonly RangeChannel channel;
        public readonly object payload;
        public readonly TextRevision revision;

        public bool IsValid => source != null && identity.IsValid && revision.IsValid;

        public RangeAttribution(RangeSource source, RangeIdentity identity, RangeChannel channel,
            object payload, TextRevision revision)
            : this(source, identity, channel, payload, revision, true) { }

        internal static RangeAttribution Restore(RangeSource source, RangeIdentity identity,
            RangeChannel channel, object payload, TextRevision revision)
            => new(source, identity, channel, payload, revision, false);

        private RangeAttribution(RangeSource source, RangeIdentity identity, RangeChannel channel,
            object payload, TextRevision revision, bool validate)
        {
            if (validate)
            {
                if (source == null) throw new System.ArgumentNullException(nameof(source));
                if (!identity.IsValid) throw new System.ArgumentException("Range identity is invalid.", nameof(identity));
                if (identity.Source != source.RuntimeId)
                    throw new System.ArgumentException("Range identity does not belong to its source.", nameof(identity));
                if (!revision.IsValid) throw new System.ArgumentException("Text revision is invalid.", nameof(revision));
                channel?.ValidatePayload(payload);
                if (channel == null && payload != null)
                    throw new System.ArgumentException("A payload requires a range channel.", nameof(payload));
            }
            this.source = source;
            this.identity = identity;
            this.channel = channel;
            this.payload = payload;
            this.revision = revision;
        }
    }
}
