using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Pluggable system-clipboard provider. The default implementation wraps the active
    /// platform (Win32 / NSPasteboard / UIPasteboard / Android ClipboardManager /
    /// <c>navigator.clipboard</c> / xclip-xsel-wl-paste); integrators substitute a custom
    /// provider for testing, sandboxed clipboards, or platforms outside the shipped set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The interface deliberately mirrors the W3C Async Clipboard API
    /// (<c>navigator.clipboard.read()</c> / <c>write(ClipboardItem[])</c>) and Apple's
    /// <see href="https://developer.apple.com/documentation/uikit/uipasteboard">UIPasteboard
    /// <c>setItems:</c></see> shape: items are <em>arrays</em> of (format, payload), not
    /// single strings. Multi-format writes are atomic — the consumer on paste picks the
    /// richest format it understands. This is the foundation D-005 calls out: shipping the
    /// primitives in P1 means P2 features (markdown channel, sanitize-on-paste pipeline,
    /// atomic-token UTI registration) extend the interface without breaking integrators.
    /// </para>
    /// <para>
    /// Multi-format is supported on Windows / macOS / iOS / Android / WebGL. Linux reads
    /// any offered target through the clipboard tools (<c>xclip -t</c> /
    /// <c>wl-paste --type</c>) but can only OFFER one format per write — the tool serves a
    /// single target — so multi-format writes keep the plain-text floor (or the richest
    /// single format when no plain item is present). See the per-platform matrix on
    /// <see cref="UniTextClipboard"/>.
    /// </para>
    /// </remarks>
    public interface IClipboardProvider
    {
        /// <summary>Whether the clipboard contains at least one item, regardless of format.</summary>
        bool HasContent();

        /// <summary>
        /// Reports whether the clipboard currently carries <paramref name="format"/>. This
        /// is an availability probe only — implementations must answer it from cheap native
        /// presence APIs (Win32 <c>IsClipboardFormatAvailable</c>, <c>NSPasteboard.types</c>,
        /// <c>ClipDescription</c> MIME list, X11 <c>TARGETS</c>) and never transfer the
        /// payload. Use <see cref="TryGetText"/> when the payload is wanted; probing then
        /// reading doubles the OS transfer. Provider impls that don't understand a format
        /// return <see langword="false"/> regardless of OS-level state.
        /// </summary>
        bool HasFormat(ClipboardFormat format);

        /// <summary>
        /// Reads the clipboard payload for <paramref name="format"/> as text. The default
        /// path decodes the format's bytes as UTF-8 — appropriate for
        /// <see cref="ClipboardFormat.PlainText"/> / <see cref="ClipboardFormat.Html"/> /
        /// <see cref="ClipboardFormat.Url"/>. Returns <see langword="null"/> when the
        /// format is not present.
        /// </summary>
        string GetText(ClipboardFormat format);

        /// <summary>
        /// Single-transfer read: fetches <paramref name="format"/>'s text payload in one OS
        /// clipboard access and reports presence and content together. Returns
        /// <see langword="false"/> (with <paramref name="text"/> <see langword="null"/> or
        /// empty) when the format is absent. The paste pipeline's per-adapter read — never
        /// pair <see cref="HasFormat"/> with <see cref="GetText"/> for that.
        /// </summary>
        bool TryGetText(ClipboardFormat format, out string text);

        /// <summary>
        /// Reads the clipboard payload for <paramref name="format"/> as raw bytes. Use for
        /// binary formats (P2 image, RTFD) or when the consumer wants control over the
        /// text decoding. Returns <see langword="null"/> when the format is not present.
        /// </summary>
        byte[] GetData(ClipboardFormat format);

        /// <summary>
        /// Writes the entire <paramref name="items"/> list to the clipboard in a single
        /// atomic OS-level write. Subsequent reads see all formats simultaneously and the
        /// consumer's preferred-format chain selects which one to ingest.
        /// </summary>
        void SetItems(IReadOnlyList<ClipboardItem> items);
    }
}
