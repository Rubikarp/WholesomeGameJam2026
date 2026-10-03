using System;

namespace LightSide
{
    /// <summary>Identifies one generated state member within the current process.</summary>
    public readonly struct StateMember : IEquatable<StateMember>
    {
        private readonly RuntimeTypeHandle declaringType;
        private readonly ushort localId;

        private StateMember(RuntimeTypeHandle declaringType, ushort localId)
        {
            this.declaringType = declaringType;
            this.localId = localId;
        }

        /// <summary>Creates a member token local to its declaring type.</summary>
        public static StateMember Create<TDeclaring>(ushort localId)
            => new(typeof(TDeclaring).TypeHandle, localId);

        /// <summary>Whether this token identifies a generated member.</summary>
        public bool IsValid => !declaringType.Equals(default) && localId != 0;

        /// <inheritdoc/>
        public bool Equals(StateMember other)
            => declaringType.Equals(other.declaringType) && localId == other.localId;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is StateMember other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(declaringType, localId);

        /// <summary>Returns whether two tokens identify the same generated member.</summary>
        public static bool operator ==(StateMember left, StateMember right) => left.Equals(right);
        /// <summary>Returns whether two tokens identify different generated members.</summary>
        public static bool operator !=(StateMember left, StateMember right) => !left.Equals(right);
    }

    /// <summary>Classifies one semantic state transition.</summary>
    public enum StateChangeKind : byte
    {
        /// <summary>One scalar or nested value changed.</summary>
        Value,
        /// <summary>One item was appended.</summary>
        Add,
        /// <summary>One item was inserted.</summary>
        Insert,
        /// <summary>One item was replaced.</summary>
        Replace,
        /// <summary>One item was removed.</summary>
        Remove,
        /// <summary>One item moved within an ordered collection.</summary>
        Move,
        /// <summary>Every collection item was removed.</summary>
        Clear,
        /// <summary>The complete ordered collection contents were replaced.</summary>
        ReplaceAll,
        /// <summary>The source was replaced without a recoverable member delta.</summary>
        Reset
    }

    /// <summary>Describes which source member changed and the structural operation, if any.</summary>
    public readonly struct StateChange
    {
        /// <summary>Creates one scalar or structural semantic transition.</summary>
        public StateChange(StateMember member, StateChangeKind kind = StateChangeKind.Value,
            int index = -1, int destinationIndex = -1)
        {
            Member = member;
            Kind = kind;
            Index = index;
            DestinationIndex = destinationIndex;
        }

        /// <summary>Gets the semantic member that changed.</summary>
        public StateMember Member { get; }
        /// <summary>Gets the scalar or structural operation that committed.</summary>
        public StateChangeKind Kind { get; }
        /// <summary>Gets the affected source index, or -1 when no index applies.</summary>
        public int Index { get; }
        /// <summary>Gets the destination index of a move, or -1 otherwise.</summary>
        public int DestinationIndex { get; }
    }

    /// <summary>Receives one committed semantic transition from its original source.</summary>
    public delegate void StateChangedHandler(IStateChangeSource source, in StateChange change);

    /// <summary>Publishes committed semantic changes from reusable state.</summary>
    public interface IStateChangeSource
    {
        /// <summary>Occurs after one semantic state transition commits.</summary>
        event StateChangedHandler Changed;
    }

    /// <summary>Replays one generated member from another instance through its normal transition.</summary>
    public interface IStateMemberReplay
    {
        /// <summary>
        /// Applies the matching member from <paramref name="source"/> and returns whether this
        /// generated type owns that replayable member.
        /// </summary>
        bool ReplayStateMember(IStateMemberReplay source, StateMember member);
    }

    /// <summary>Controls and maps an acyclic generated nested state graph.</summary>
    public interface IStateLinkGraph
    {
        /// <summary>Retains nested state observation until paired with a release.</summary>
        void RetainStateLinks();

        /// <summary>Releases one prior retain of nested state observation.</summary>
        void ReleaseStateLinks();

        /// <summary>
        /// Finds every target corresponding to <paramref name="source"/>, stores the latest match
        /// in <paramref name="target"/>, and returns the match count without mutating state.
        /// </summary>
        int FindStateGraphTargets(IStateLinkGraph sourceRoot, IStateChangeSource source,
            ref IStateMemberReplay target);
    }

    /// <summary>Replays exact scalar members between homologous generated graphs.</summary>
    public static class StateGraphReplay
    {
        /// <summary>
        /// Replays a member only when its original source maps to exactly one target in the
        /// homologous graph.
        /// </summary>
        public static bool TryReplay(IStateLinkGraph targetRoot, IStateLinkGraph sourceRoot,
            IStateChangeSource source, StateMember member)
        {
            if (targetRoot == null) throw new ArgumentNullException(nameof(targetRoot));
            if (sourceRoot == null) throw new ArgumentNullException(nameof(sourceRoot));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!member.IsValid)
                throw new ArgumentException("A generated member token is required.", nameof(member));
            if (source is not IStateMemberReplay sourceReplay) return false;
            IStateMemberReplay target = null;
            return targetRoot.FindStateGraphTargets(sourceRoot, source, ref target) == 1 &&
                   target.ReplayStateMember(sourceReplay, member);
        }
    }

}
