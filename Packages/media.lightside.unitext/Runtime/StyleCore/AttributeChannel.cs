using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Single owner of the pipeline work behind one attribute key: subscribes the passes that read the
    /// keyed buffer and holds the per-range state those passes consume.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One channel exists per key per component however many modifiers write that key, so a pass runs
    /// once over the merged buffer instead of once per writer. Writers only fill the buffer from
    /// <see cref="BaseModifier.OnApply"/>; anything a pass reads belongs here, never on a writer.
    /// </para>
    /// <para>
    /// <see cref="OnActivate"/> runs when the first writer enables and <see cref="OnDeactivate"/> when
    /// the last one disables, so subscriptions match the writers' active span exactly.
    /// <see cref="OnRelease"/> runs when the key's last writer is destroyed. Passes may run on the
    /// rebuild worker — no Unity API calls.
    /// </para>
    /// </remarks>
    public abstract class AttributeChannel
    {
        private readonly List<BaseModifier> holders = new();
        private int cycle;

        /// <summary>The attribute key this channel owns.</summary>
        protected string Key { get; private set; }

        /// <summary>The component this channel serves, from its first writer until release.</summary>
        protected UniTextBase uniText;

        /// <summary>Text-processing buffers of <see cref="uniText"/>.</summary>
        protected UniTextBuffers buffers;

        /// <summary>
        /// An active writer, for dispatching hooks a pass needs from the writer's type. Any of them
        /// serves: a pass reads the merged buffer and this channel's state, never a writer's fields.
        /// </summary>
        protected BaseModifier Provider => holders.Count > 0 ? holders[0] : null;

        internal void Activate(string key, BaseModifier holder, UniTextBase owner, UniTextBuffers ownerBuffers)
        {
            if (IndexOfHolder(holder) >= 0) return;
            holders.Add(holder);
            if (holders.Count != 1) return;

            Key = key;
            uniText = owner;
            buffers = ownerBuffers;
            OnActivate();
        }

        internal void Deactivate(BaseModifier holder)
        {
            var index = IndexOfHolder(holder);
            if (index < 0) return;
            holders.RemoveAt(index);

            if (holders.Count == 0)
            {
                OnDeactivate();
                return;
            }

            if (index == 0) OnProviderChanged();
        }

        private int IndexOfHolder(BaseModifier holder)
        {
            for (var i = 0; i < holders.Count; i++)
                if (ReferenceEquals(holders[i], holder)) return i;
            return -1;
        }

        internal void BeginCycle(int applyCycle)
        {
            if (cycle == applyCycle) return;
            cycle = applyCycle;
            OnBeginCycle();
        }

        internal void Release()
        {
            if (holders.Count > 0)
            {
                holders.Clear();
                OnDeactivate();
            }

            OnRelease();
            uniText = null;
            buffers = null;
        }

        /// <summary>Subscribes the channel's passes.</summary>
        protected abstract void OnActivate();

        /// <summary>Unsubscribes the channel's passes.</summary>
        protected abstract void OnDeactivate();

        /// <summary>Releases pooled state acquired by <see cref="OnActivate"/>.</summary>
        protected virtual void OnRelease() { }

        /// <summary>
        /// Rebinds whatever <see cref="OnActivate"/> captured from <see cref="Provider"/>, after that
        /// writer left with others still active. A channel that captures nothing needs no override.
        /// </summary>
        protected virtual void OnProviderChanged() { }

        /// <summary>
        /// Brings state the writers append to back to a valid start, once per apply cycle before the
        /// first <see cref="BaseModifier.OnApply"/> of that cycle. Every writer of the key re-applies
        /// within one cycle, so entries collected here are always complete.
        /// </summary>
        protected virtual void OnBeginCycle() { }
    }
}
