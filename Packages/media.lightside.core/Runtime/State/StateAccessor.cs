using System;

namespace LightSide
{
    /// <summary>
    /// Opts an assembly into <see cref="StateAccessor"/> generation for every state owner it
    /// declares. Accessors cost one shared instance and two delegates per member plus a generic
    /// instantiation per (owner, value) pair, so assemblies that never address members
    /// programmatically leave it off. Inherited accessors chain only through bases declared in the
    /// same assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class GenerateStateAccessorsAttribute : Attribute
    {
        /// <summary>
        /// Full name of a field attribute whose members are additionally collected into
        /// <c>StateAccess.Marked</c>, in the same order as <c>StateAccess.All</c>. The meaning of
        /// the marker belongs to the declaring assembly; generation only groups by it.
        /// </summary>
        public string Marker { get; set; }
    }

    /// <summary>
    /// A state owner whose generated accessor set is reachable through its instances —
    /// <see cref="StateAccessors"/> returns the concrete type's <c>StateAccess.All</c>.
    /// </summary>
    public interface IStateAccessSource
    {
        /// <summary>Every accessor of the instance's type, inherited members first.</summary>
        StateAccessor[] StateAccessors { get; }

        /// <summary>
        /// The accessors carrying the assembly's declared marker attribute, inherited members
        /// first; empty when the assembly declares no marker.
        /// </summary>
        StateAccessor[] MarkedStateAccessors { get; }
    }

    /// <summary>
    /// Receives a <see cref="StateAccessor"/> with its owner and value types resolved, so callers
    /// can build typed structures over an untyped accessor list without reflection.
    /// </summary>
    public interface IStateAccessorVisitor
    {
        /// <summary>Handles one accessor with both type arguments bound.</summary>
        void Visit<TOwner, TValue>(StateAccessor<TOwner, TValue> accessor);
    }

    /// <summary>
    /// Reflection-free access to one generated state member: its identity, its value, and the
    /// invalidation its owner declared for it.
    /// </summary>
    /// <remarks>
    /// Instances are emitted by the state generator into the owner's nested <c>StateAccess</c>
    /// class and are immutable and shared. Recover the type arguments through
    /// <see cref="Accept{TVisitor}"/>.
    /// </remarks>
    public abstract class StateAccessor
    {
        /// <summary>Token identifying the member within its declaring type.</summary>
        public StateMember Member { get; }

        /// <summary>Backing field name — the stable identifier persisted by consumers.</summary>
        public string Name { get; }

        /// <summary>Type declaring the member.</summary>
        public abstract Type OwnerType { get; }

        /// <summary>Type of the member's value.</summary>
        public abstract Type ValueType { get; }

        /// <summary>Whether the owner declared an invalidation that runs without a transition.</summary>
        public abstract bool CanInvalidate { get; }

        private protected StateAccessor(StateMember member, string name)
        {
            Member = member;
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        /// <summary>Dispatches to <paramref name="visitor"/> with both type arguments bound.</summary>
        public abstract void Accept<TVisitor>(TVisitor visitor) where TVisitor : IStateAccessorVisitor;

        /// <inheritdoc/>
        public override string ToString() => OwnerType.Name + "." + Name;
    }

    /// <summary>Typed <see cref="StateAccessor"/> over one member of <typeparamref name="TOwner"/>.</summary>
    public sealed class StateAccessor<TOwner, TValue> : StateAccessor
    {
        private readonly Func<TOwner, TValue> getter;
        private readonly Action<TOwner, TValue> setter;
        private readonly Action<TOwner> invalidator;

        /// <inheritdoc/>
        public override Type OwnerType => typeof(TOwner);

        /// <inheritdoc/>
        public override Type ValueType => typeof(TValue);

        /// <inheritdoc/>
        public override bool CanInvalidate => invalidator != null;

        /// <summary>
        /// Creates an accessor. <paramref name="invalidator"/> is the owner's own change
        /// notification for this member, or <see langword="null"/> when the member's notification
        /// needs a value transition and therefore cannot be raised on its own.
        /// </summary>
        public StateAccessor(StateMember member, string name, Func<TOwner, TValue> getter,
            Action<TOwner, TValue> setter, Action<TOwner> invalidator = null)
            : base(member, name)
        {
            this.getter = getter ?? throw new ArgumentNullException(nameof(getter));
            this.setter = setter ?? throw new ArgumentNullException(nameof(setter));
            this.invalidator = invalidator;
        }

        /// <summary>Reads the member's current value.</summary>
        public TValue Get(TOwner owner) => getter(owner);

        /// <summary>Writes the member through its generated transition, notifications included.</summary>
        public void Set(TOwner owner, TValue value) => setter(owner, value);

        /// <summary>
        /// Raises the owner's declared notification for this member without changing its value.
        /// </summary>
        /// <exception cref="InvalidOperationException">The member has no standalone notification;
        /// test <see cref="StateAccessor.CanInvalidate"/> first.</exception>
        public void Invalidate(TOwner owner)
        {
            if (invalidator == null)
                throw new InvalidOperationException(
                    $"{OwnerType.Name}.{Name} declares no standalone invalidation.");
            invalidator(owner);
        }

        /// <inheritdoc/>
        public override void Accept<TVisitor>(TVisitor visitor) => visitor.Visit(this);
    }

    /// <summary>Helpers for the generated accessor sets.</summary>
    public static class StateAccessors
    {
        /// <summary>Empty set, shared.</summary>
        public static readonly StateAccessor[] None = Array.Empty<StateAccessor>();

        /// <summary>Concatenates an inherited accessor set with a declaring type's own, inherited first.</summary>
        public static StateAccessor[] Concat(StateAccessor[] inherited, StateAccessor[] own)
        {
            if (inherited == null || inherited.Length == 0) return own ?? None;
            if (own == null || own.Length == 0) return inherited;
            var result = new StateAccessor[inherited.Length + own.Length];
            Array.Copy(inherited, result, inherited.Length);
            Array.Copy(own, 0, result, inherited.Length, own.Length);
            return result;
        }

        /// <summary>Finds an accessor by its backing field name, or returns null.</summary>
        public static StateAccessor Find(StateAccessor[] set, string name)
        {
            if (set == null) return null;
            for (var i = 0; i < set.Length; i++)
                if (string.Equals(set[i].Name, name, StringComparison.Ordinal))
                    return set[i];
            return null;
        }
    }
}
