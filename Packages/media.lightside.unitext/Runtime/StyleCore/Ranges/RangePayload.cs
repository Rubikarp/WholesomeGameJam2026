using System;

namespace LightSide
{
    /// <summary>Read-only typed access to semantic data carried by a range entity.</summary>
    public readonly struct RangePayloadView
    {
        private readonly object value;

        internal RangePayloadView(object value) => this.value = value;

        /// <summary>Whether this entity carries a semantic payload.</summary>
        public bool HasValue => value != null;
        /// <summary>Concrete runtime type, or null when no payload exists.</summary>
        public Type Type => value?.GetType();

        /// <summary>Attempts to read the payload through a compatible type.</summary>
        public bool TryGet<T>(out T result)
        {
            if (value is T typed)
            {
                result = typed;
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>Returns the typed payload or throws when it is absent or incompatible.</summary>
        public T Get<T>()
        {
            if (TryGet<T>(out var result)) return result;
            throw new InvalidCastException(value == null
                ? $"The range has no payload; requested {typeof(T).FullName}."
                : $"Range payload is {value.GetType().FullName}; requested {typeof(T).FullName}.");
        }

        internal object Value => value;
    }
}
