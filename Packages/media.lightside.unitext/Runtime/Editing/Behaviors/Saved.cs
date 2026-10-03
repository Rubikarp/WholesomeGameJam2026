namespace LightSide
{
    /// <summary>
    /// Enable-time save slot for a host value a behavior overrides while active: capture-and-override
    /// is one assignment on enable, <see cref="Restore"/> hands the saved value back on disable.
    /// Never serialized — behaviors re-capture on every enable.
    /// </summary>
    internal struct Saved<T>
    {
        private T value;

        /// <summary>Remembers <paramref name="current"/> and returns <paramref name="replacement"/> to assign in its place.</summary>
        public T Capture(T current, T replacement)
        {
            value = current;
            return replacement;
        }

        /// <summary>The value captured on enable, to assign back on disable.</summary>
        public T Restore() => value;
    }
}
