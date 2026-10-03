namespace LightSide
{
    /// <summary>
    /// Platform-agnostic key codes for keys that trigger editing actions.
    /// </summary>
    /// <remarks>
    /// This is intentionally NOT a full keyboard enum. It contains the navigation and
    /// editing keys plus the complete A–Z letter block, so any letter shortcut is
    /// bindable through <see cref="InputPlatformKeyMap.Resolve"/> and custom key maps.
    /// The native platform layer maps OS-specific scan codes / virtual key codes to
    /// these values before delivering key-down events; bare unmodified letters are
    /// never delivered (they are text, not shortcuts — see
    /// <see cref="NativeKeyCodeExtensions.IsLetter"/>). The integer values are mirrored
    /// in the native plugin sources (iOS/macOS .mm, WebGL .jslib, Android .java) —
    /// append new keys at the end, never reorder.
    /// </remarks>
    public enum NativeKeyCode
    {
        A, B, C, D, E, F, H, K, N, P, T, V, X, Y, Z,

        LeftArrow, RightArrow, UpArrow, DownArrow,
        Home, End,
        PageUp, PageDown,

        Backspace,
        Delete,
        Return,
        KeypadEnter,
        Tab,
        Escape,

        Insert,

        /// <summary>No key — the unbound-shortcut sentinel. Never delivered by the platform layer.</summary>
        None,

        I,
        U,

        /// <summary>The <c>\</c> / <c>|</c> key (US layout; Windows <c>VK_OEM_5</c>).</summary>
        Backslash,

        G, J, L, M, O, Q, R, S, W,
    }

    /// <summary>
    /// ABI companion for <see cref="NativeKeyCode"/>. The enum's integer values are mirrored
    /// in the native plugin sources (macOS/iOS .mm, WebGL .jslib, Android .java) — when a key
    /// is appended to the enum, update <see cref="MaxDelivered"/> and the native tables together
    /// (see Additional/plans ci-checks.md for the sync invariants).
    /// </summary>
    public static class NativeKeyCodeExtensions
    {
        /// <summary>
        /// Highest key value the platform layer may deliver. The single receiver-side clamp —
        /// individual backends must never hardcode their own upper bound (a stale per-backend
        /// clamp silently mutes keys appended later).
        /// </summary>
        public const NativeKeyCode MaxDelivered = NativeKeyCode.W;

        /// <summary>
        /// Whether a raw key value from a native boundary is a real, deliverable key:
        /// in range and not the <see cref="NativeKeyCode.None"/> sentinel.
        /// </summary>
        public static bool IsDeliverable(int keyCode)
            => keyCode >= 0 && keyCode <= (int)MaxDelivered && keyCode != (int)NativeKeyCode.None;

        /// <summary>
        /// Whether the key is a letter (A–Z). Letters are delivered as KeyDown only when
        /// Ctrl/Cmd is held — a bare letter press is text (it arrives via TextInput), never
        /// a shortcut, on every backend.
        /// </summary>
        public static bool IsLetter(NativeKeyCode key)
            => key <= NativeKeyCode.Z || key == NativeKeyCode.I || key == NativeKeyCode.U
               || (key >= NativeKeyCode.G && key <= NativeKeyCode.W);
    }
}
