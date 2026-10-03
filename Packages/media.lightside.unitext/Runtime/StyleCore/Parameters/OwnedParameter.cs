using System;

namespace LightSide
{
    /// <summary>
    /// Ownership of one parameter on one materialized range. The owned value composes on the
    /// range's cascade result under <see cref="Composition"/>; releasing the handle — or the
    /// range's identity retiring with a text edit — returns the parameter to its cascade.
    /// </summary>
    /// <remarks>
    /// Writing changes only this ownership; the serialized modifier field and other ranges keep
    /// their values. Members other than <see cref="IsAlive"/> and <see cref="Release"/> throw
    /// <see cref="ObjectDisposedException"/> once the ownership is dead.
    /// </remarks>
    public sealed class OwnedParameter<TValue> : IDisposable
    {
        private readonly IOwnedParameterAccess<TValue> access;

        internal OwnedParameter(IOwnedParameterAccess<TValue> access) => this.access = access;

        /// <summary>The owned value.</summary>
        public TValue Value
        {
            get
            {
                RequireAlive();
                return access.GetValue();
            }
            set
            {
                RequireAlive();
                access.SetValue(value);
            }
        }

        /// <summary>The cascade result beneath this ownership.</summary>
        public TValue Baseline
        {
            get
            {
                RequireAlive();
                return access.GetBaseline();
            }
        }

        /// <summary>How the owned value combines with the cascade result.</summary>
        public ParameterComposition Composition
        {
            get
            {
                RequireAlive();
                return access.Composition;
            }
        }

        /// <summary>Whether the ownership still holds.</summary>
        public bool IsAlive => access?.IsAlive == true;

        /// <summary>Releases the ownership; the parameter falls back to its cascade. Idempotent.</summary>
        public void Release()
        {
            if (IsAlive) access.Release();
        }

        /// <inheritdoc cref="Release"/>
        public void Dispose() => Release();

        private void RequireAlive()
        {
            if (!IsAlive) throw new ObjectDisposedException(nameof(OwnedParameter<TValue>));
        }
    }
}
