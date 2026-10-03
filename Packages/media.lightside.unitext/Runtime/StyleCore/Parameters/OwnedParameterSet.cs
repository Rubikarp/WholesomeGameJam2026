using System;
using System.Collections.Generic;

namespace LightSide
{
    internal interface IOwnedParameterSetLifecycle
    {
        bool IsAlive { get; }
        void Reconcile();
        void Kill();
    }

    /// <summary>
    /// Standing ownership of one parameter across every range its query currently matches. The
    /// set re-materializes after each parse: ranges that stop matching release their owned value,
    /// newly matching ranges receive the broadcast <see cref="Value"/>, surviving ranges keep
    /// theirs, and members follow text order.
    /// </summary>
    /// <remarks>
    /// Obtained from <see cref="UniTextRanges.Own{TModifier,TValue}(in RangeQuery,
    /// ParameterDescriptor{TModifier,TValue}, ParameterComposition, int)"/>. Per-member values
    /// written through <see cref="SetValue"/> live until the next parse; a driver distributing
    /// values re-applies them from <see cref="Changed"/>.
    /// </remarks>
    public sealed class OwnedParameterSet<TModifier, TValue> : IOwnedParameterSetLifecycle,
        IDisposable
        where TModifier : BaseModifier
    {
        private readonly UniTextRanges runtime;
        private readonly RangeQuery query;
        private readonly TModifier modifier;
        private readonly ParameterDescriptor<TModifier, TValue> parameter;
        private readonly ParameterComposition composition;
        private readonly int priority;
        private readonly List<ModifierRange> members = new();
        private readonly List<ParameterContribution<TModifier, TValue>> contributions = new();
        private readonly List<ModifierRange> collectScratch = new();
        private readonly List<ParameterContribution<TModifier, TValue>> contributionScratch = new();
        private TValue broadcastValue;
        private bool hasBroadcast;
        private bool alive = true;

        /// <summary>Occurs after membership re-materialized against a new parse.</summary>
        public event Action Changed;

        /// <summary>The query selecting this set's members.</summary>
        public RangeQuery Query => query;

        /// <summary>The parameter this set owns on every member.</summary>
        public ParameterDescriptor<TModifier, TValue> Parameter => parameter;

        /// <summary>Whether the set still owns its members.</summary>
        public bool IsAlive => alive;

        /// <summary>Number of members in the current parse, in text order.</summary>
        public int Count
        {
            get
            {
                RequireAlive();
                return members.Count;
            }
        }

        /// <summary>
        /// The value owned on every member. Setting it broadcasts to current members and to every
        /// member a later parse adds; reading returns the last broadcast value.
        /// </summary>
        public TValue Value
        {
            get
            {
                RequireAlive();
                return broadcastValue;
            }
            set
            {
                RequireAlive();
                broadcastValue = value;
                hasBroadcast = true;
                for (var i = 0; i < contributions.Count; i++) contributions[i].SetValue(value);
            }
        }

        internal OwnedParameterSet(UniTextRanges runtime, in RangeQuery query, TModifier modifier,
            ParameterDescriptor<TModifier, TValue> parameter, ParameterComposition composition,
            int priority)
        {
            this.runtime = runtime;
            this.query = query;
            this.modifier = modifier;
            this.parameter = parameter;
            this.composition = composition;
            this.priority = priority;
        }

        /// <summary>Returns the member at a text-order index.</summary>
        public ModifierRange GetRange(int index)
        {
            RequireAlive();
            if ((uint)index >= (uint)members.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return members[index];
        }

        /// <summary>Writes one member's owned value; it lives until the next parse re-materializes the set.</summary>
        public void SetValue(int index, TValue value)
        {
            RequireAlive();
            if ((uint)index >= (uint)contributions.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            contributions[index].SetValue(value);
        }

        /// <summary>
        /// Deactivates every member's owned value without releasing membership: the parameters
        /// fall back to their cascades or other owners until values are written again.
        /// </summary>
        public void Withhold()
        {
            RequireAlive();
            for (var i = 0; i < contributions.Count; i++) contributions[i]?.Withhold();
        }

        /// <summary>Releases every owned value; the parameters fall back to their cascades. Idempotent.</summary>
        public void Release()
        {
            if (!alive) return;
            KillCore();
            runtime.RemoveOwnedSet(this);
        }

        /// <inheritdoc cref="Release"/>
        public void Dispose() => Release();

        void IOwnedParameterSetLifecycle.Reconcile() => Reconcile();

        internal void Reconcile()
        {
            if (!alive) return;
            if (modifier.Owner == null)
            {
                for (var i = 0; i < contributions.Count; i++) contributions[i].Release();
                contributions.Clear();
                members.Clear();
                Changed?.Invoke();
                return;
            }

            query.Collect(collectScratch);
            contributionScratch.Clear();
            for (var i = 0; i < collectScratch.Count; i++)
            {
                var range = collectScratch[i];
                var existing = IndexOfMember(range.Identity, range.Segment);
                if (existing >= 0)
                {
                    contributionScratch.Add(contributions[existing]);
                    contributions[existing] = null;
                }
                else
                {
                    var slot = runtime.GetOrCreateSlot(modifier, parameter, range.Identity,
                        range.Segment);
                    var created = new ParameterContribution<TModifier, TValue>(slot, parameter,
                        parameter.Identity(composition), composition, priority,
                        runtime.NextDeclarationOrder());
                    if (hasBroadcast) created.SetValue(broadcastValue);
                    contributionScratch.Add(created);
                }
            }

            for (var i = 0; i < contributions.Count; i++) contributions[i]?.Release();
            contributions.Clear();
            contributions.AddRange(contributionScratch);
            members.Clear();
            members.AddRange(collectScratch);
            contributionScratch.Clear();
            collectScratch.Clear();
            Changed?.Invoke();
        }

        void IOwnedParameterSetLifecycle.Kill() => KillCore();

        private void KillCore()
        {
            if (!alive) return;
            alive = false;
            for (var i = 0; i < contributions.Count; i++) contributions[i]?.Release();
            contributions.Clear();
            members.Clear();
        }

        private int IndexOfMember(RangeIdentity identity, RangeSegmentId segment)
        {
            for (var i = 0; i < members.Count; i++)
                if (members[i].Identity == identity && members[i].Segment == segment)
                    return i;
            return -1;
        }

        private void RequireAlive()
        {
            if (!alive)
                throw new ObjectDisposedException(nameof(OwnedParameterSet<TModifier, TValue>));
        }
    }
}
