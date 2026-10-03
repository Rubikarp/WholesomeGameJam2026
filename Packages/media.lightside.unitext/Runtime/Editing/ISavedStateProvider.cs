using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Persists and restores transient editor state — text, selection, scroll position,
    /// pending IME composition — across host-driven view recreation. The pattern matches
    /// Android <c>SavedStateHandle</c> / iOS <c>UIStateRestoration</c> / Web bfcache
    /// (<c>pageshow</c> with <c>persisted=true</c>): the platform hands the editor an
    /// opaque dictionary on suspend, hands it back on resume.
    /// </summary>
    /// <remarks>
    /// The platform integration (Android process death restoration, iOS background-app
    /// kill, Web bfcache) is P2 work — the interface ships in P1 so integrators wiring
    /// their own state-bundle plumbing have a stable contract today.
    /// </remarks>
    public interface ISavedStateProvider
    {
        /// <summary>
        /// Writes editor state into <paramref name="bundle"/>. Keys use the <c>"unitext."</c>
        /// prefix to avoid collisions with caller-owned entries.
        /// </summary>
        void SaveState(IDictionary<string, object> bundle);

        /// <summary>
        /// Reads editor state from <paramref name="bundle"/>. Missing or wrongly-typed
        /// entries are silently ignored; restoration is best-effort.
        /// </summary>
        void RestoreState(IDictionary<string, object> bundle);
    }
}
