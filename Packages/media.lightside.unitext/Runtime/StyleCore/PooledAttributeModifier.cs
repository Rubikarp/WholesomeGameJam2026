using System;

namespace LightSide
{
    /// <summary>Owns one pooled modifier attribute from initialization through destruction.</summary>
    /// <remarks>
    /// Modifiers sharing an <see cref="AttributeKey"/> share one buffer and merge into it — the range
    /// applied last wins a contested codepoint. The pipeline work reading that buffer belongs to the
    /// key's <see cref="AttributeChannel"/>, which runs once per rebuild no matter how many modifiers
    /// write the key. Give a key that composes rather than merges (a layer drawn per modifier) a
    /// per-instance key instead of a channel.
    /// </remarks>
    [Serializable]
    public abstract class PooledAttributeModifier<T> : BaseModifier where T : unmanaged
    {
        /// <summary>The pooled buffer prepared before derived enable logic and released on destruction.</summary>
        protected PooledArrayAttribute<T> attribute;

        [NonSerialized] private AttributeChannel channel;
        [NonSerialized] private Func<AttributeChannel> channelFactory;

        /// <summary>Stable key under which this modifier owns its primary pooled buffer.</summary>
        protected abstract string AttributeKey { get; }

        /// <summary>
        /// Owner of the passes reading <see cref="AttributeKey"/>, shared with every other modifier
        /// writing that key; <see langword="null"/> outside the active span or when the key has no pass.
        /// </summary>
        protected AttributeChannel SharedChannel => channel;

        /// <summary>
        /// Builds the channel for <see cref="AttributeKey"/>, at most once per key per component. The
        /// default declares none, for a key the core reads directly instead of through a pass.
        /// </summary>
        protected virtual AttributeChannel CreateChannel() => null;

        internal sealed override object ChannelIdentity => channel;

        /// <inheritdoc/>
        protected override void OnEnable()
        {
            buffers.PrepareAttribute(ref attribute, AttributeKey);

            if (channel != null)
            {
                channel.Activate(AttributeKey, this, uniText, buffers);
                return;
            }

            channelFactory ??= CreateChannel;
            channel = buffers.ActivateChannel(AttributeKey, this, uniText, channelFactory);
        }

        /// <inheritdoc/>
        protected override void OnDisable() => channel?.Deactivate(this);

        /// <inheritdoc/>
        protected override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKey);
            attribute = null;
            channel = null;
        }

        /// <inheritdoc/>
        protected override void BeforeApply() => channel?.BeginCycle(CurrentApplyCycle);
    }
}
