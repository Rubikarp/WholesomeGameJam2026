namespace LightSide
{
    /// <summary>
    /// Supplies a detached snapshot for a value-type field or collection element containing mutable
    /// reference storage. Capture must be thread-safe and must not call Unity API.
    /// </summary>
    public interface IStateSnapshot<T>
    {
        /// <summary>Returns a snapshot that does not alias mutable storage in this value.</summary>
        T CaptureStateSnapshot();

        /// <summary>Compares the current value with a previously detached snapshot.</summary>
        bool StateEquals(in T snapshot);
    }
}
