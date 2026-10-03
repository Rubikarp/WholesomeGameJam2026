using System;

namespace LightSide
{
    /// <summary>
    /// A selectable-owned UI entity. The owner is available for the whole attached lifetime, allowing
    /// implementations to use Unity UI, native platform UI, or any other presentation mechanism.
    /// </summary>
    /// <remarks>
    /// Implementations assigned in the inspector are managed references: use a serializable managed class,
    /// not a <c>UnityEngine.Object</c>. Derive from <see cref="SelectableEntity"/> for the standard lifetime.
    /// </remarks>
    [StateHierarchy]
    public interface ISelectableEntity
    {
        /// <summary>The selectable this entity is currently attached to, or <see langword="null"/> while detached.</summary>
        UniTextSelectable Owner { get; }

        /// <summary>Attaches the entity to its complete owner before the entity can be used.</summary>
        void Attach(UniTextSelectable owner);

        /// <summary>Ends the current attachment and releases owner-specific state.</summary>
        void Detach();
    }

    /// <summary>
    /// Base implementation of the selectable entity lifetime. Override the protected hooks to bind and
    /// release owner-specific state; serialized configuration remains available between attachments.
    /// </summary>
    [Serializable, StateHierarchy]
    public abstract class SelectableEntity : ISelectableEntity
    {
        [NonSerialized] private UniTextSelectable owner;

        /// <inheritdoc />
        public UniTextSelectable Owner => owner;

        /// <inheritdoc />
        public void Attach(UniTextSelectable value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(owner, value)) return;
            if (!ReferenceEquals(owner, null))
                throw new InvalidOperationException($"{GetType().Name} is already attached to {owner.name}.");

            ValidateAttachment(value);
            owner = value;
            try
            {
                OnAttach();
            }
            catch
            {
                owner = null;
                throw;
            }
        }

        /// <inheritdoc />
        public void Detach()
        {
            if (ReferenceEquals(owner, null)) return;
            OnDetach();
            owner = null;
        }

        /// <summary>Validates serialized configuration before the owner becomes observable.</summary>
        protected virtual void ValidateAttachment(UniTextSelectable value) { }

        /// <summary>Runs after <see cref="Owner"/> is assigned.</summary>
        protected virtual void OnAttach() { }

        /// <summary>Runs before <see cref="Owner"/> is cleared.</summary>
        protected virtual void OnDetach() { }

        /// <summary>Returns the current owner or fails when an operation is invoked outside the attached lifetime.</summary>
        protected UniTextSelectable RequireOwner()
            => owner != null
                ? owner
                : throw new InvalidOperationException($"{GetType().Name} is not attached to a selectable.");
    }
}
