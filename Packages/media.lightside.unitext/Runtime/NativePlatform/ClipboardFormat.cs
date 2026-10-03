using System;

namespace LightSide
{
    /// <summary>
    /// Cross-platform identifier for a clipboard payload. The canonical identifier is a
    /// MIME-style string (<c>text/plain</c>, <c>text/html</c>, <c>text/uri-list</c>); each
    /// platform clipboard provider translates to the correct OS-level identifier at the
    /// boundary (UTI on Apple, CF_HTML / "HTML Format" on Windows, MIME on Web / Android /
    /// Linux).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Well-known formats: <see cref="PlainText"/>, <see cref="Html"/>, <see cref="Url"/>,
    /// <see cref="Markdown"/>, <see cref="UniTextSource"/>, and the raster image trio
    /// (<see cref="Png"/> / <see cref="Jpeg"/> / <see cref="Gif"/>); integrator formats go
    /// through <see cref="Custom"/>. RTF / RTFD remain reserved (decision D-005) and land
    /// without breaking this contract. Per-platform support is summarised on
    /// <see cref="UniTextClipboard"/>.
    /// </para>
    /// <para>
    /// Equality and hash are by <see cref="Identifier"/> only — provider implementations
    /// compare formats via <c>format == ClipboardFormat.Html</c>, not by reference.
    /// </para>
    /// </remarks>
    public readonly struct ClipboardFormat : IEquatable<ClipboardFormat>
    {
        /// <summary>MIME-style canonical identifier. Never <see langword="null"/>.</summary>
        public string Identifier { get; }

        /// <summary>True for a raster image format (<c>image/*</c>) — the partition marshaled across the native boundary as binary data rather than UTF-8 text.</summary>
        public bool IsImage => Identifier != null && Identifier.StartsWith("image/", StringComparison.Ordinal);

        private ClipboardFormat(string identifier)
        {
            Identifier = identifier ?? string.Empty;
        }

        /// <summary>UTF-8 plain text (<c>text/plain</c>). Apple UTI: <c>public.utf8-plain-text</c>. Windows: <c>CF_UNICODETEXT</c>.</summary>
        public static readonly ClipboardFormat PlainText = new("text/plain");

        /// <summary>HTML fragment with optional source-URL header (<c>text/html</c>). Apple UTI: <c>public.html</c>. Windows: <c>CF_HTML</c> ("HTML Format").</summary>
        public static readonly ClipboardFormat Html = new("text/html");

        /// <summary>URI list, one URL per line, LF-separated (<c>text/uri-list</c>). Apple UTI: <c>public.url</c>. Windows: <c>UniformResourceLocatorW</c>.</summary>
        public static readonly ClipboardFormat Url = new("text/uri-list");

        /// <summary>
        /// CommonMark-style markdown source (<c>text/markdown</c>, RFC 7763). On Chromium
        /// browsers the Web Async Clipboard API delivers this via the <c>web text/markdown</c>
        /// custom format; Firefox / Safari clipboard support is not yet shipping it natively.
        /// On Apple platforms the canonical identifier is exposed as the dynamic UTI
        /// <c>net.daringfireball.markdown</c> by some apps — UniText negotiates the active
        /// identifier at the platform boundary; integrators consume / produce
        /// <see cref="Markdown"/> through this same constant. Used by chat composers and
        /// editor apps to preserve source markdown across copy / paste (Slack, Discord,
        /// Notion, Obsidian, Typora, VS Code).
        /// </summary>
        public static readonly ClipboardFormat Markdown = new("text/markdown");

        /// <summary>
        /// UniText-native round-trip channel as a vendor-tree MIME type
        /// (<c>application/vnd.lightside.unitext</c>, RFC 6838). Carries a JSON
        /// <c>UniTextClipboardFragment</c>: the selection's visible text plus markup spans
        /// keyed by modifier signature — so a copy from one UniText field pastes into
        /// another with full semantic fidelity (each span re-emitted in the destination's
        /// own syntax, degraded to plain text when the destination lacks the modifier),
        /// regardless of which modifiers an HTML or Markdown serializer can represent.
        /// Interop consumers must parse the fragment JSON, not treat the payload as raw
        /// source markup. Per-platform mapping:
        /// <list type="bullet">
        ///   <item><description>Chromium browsers — <c>web application/vnd.lightside.unitext</c> (Async Clipboard custom format, mandatory <c>web </c> prefix).</description></item>
        ///   <item><description>macOS / iOS — UTI <c>com.lightside.unitext</c> (reverse-DNS).</description></item>
        ///   <item><description>Windows — <c>RegisterClipboardFormatW("LightSide.UniText")</c>.</description></item>
        ///   <item><description>Android / Linux — MIME identifier as-is.</description></item>
        /// </list>
        /// External apps that do not recognise this format silently fall back to
        /// <see cref="PlainText"/> / <see cref="Html"/> in the same clipboard write — copy
        /// always emits the universal floor alongside the custom format.
        /// </summary>
        public static readonly ClipboardFormat UniTextSource = new("application/vnd.lightside.unitext");

        /// <summary>Raster PNG bytes (<c>image/png</c>). Apple UTI <c>public.png</c>; browser <c>image/png</c> blob. Windows writes the registered <c>"PNG"</c> format plus a synthesized <c>CF_DIBV5</c>, and reads either — so PrintScreen screenshots paste in and copied images land in Paint / Word. The cross-app baseline for image copy/paste.</summary>
        public static readonly ClipboardFormat Png = new("image/png");

        /// <summary>Raster JPEG bytes (<c>image/jpeg</c>). Apple UTI <c>public.jpeg</c>.</summary>
        public static readonly ClipboardFormat Jpeg = new("image/jpeg");

        /// <summary>GIF bytes, animated or still (<c>image/gif</c>).</summary>
        public static readonly ClipboardFormat Gif = new("image/gif");

        private static readonly byte[] pngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly byte[] jpegSignature = { 0xFF, 0xD8, 0xFF };
        private static readonly byte[] gifSignature = { 0x47, 0x49, 0x46, 0x38 };

        /// <summary>
        /// Detects a raster image format from a payload's leading bytes by file signature (magic number) — the
        /// content-based identification browsers and file managers use, since a file extension lies or is absent
        /// (a pasted path may be a sandbox or <c>content://</c> URI with none). Recognises <see cref="Png"/>,
        /// <see cref="Jpeg"/>, and <see cref="Gif"/>; pass at least the first 8 bytes. Returns
        /// <see langword="false"/> on any other or truncated input.
        /// </summary>
        public static bool TryDetectImage(ReadOnlySpan<byte> data, out ClipboardFormat format)
        {
            if (StartsWith(data, pngSignature)) { format = Png; return true; }
            if (StartsWith(data, jpegSignature)) { format = Jpeg; return true; }
            if (StartsWith(data, gifSignature)) { format = Gif; return true; }
            format = default;
            return false;
        }

        private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
            => data.Length >= signature.Length && data.Slice(0, signature.Length).SequenceEqual(signature);

        /// <summary>
        /// Integrator-defined format. <paramref name="identifier"/> is passed verbatim to
        /// the native channel (Win32 registered format, Apple UTI, Android MIME, Chromium
        /// custom format with auto-applied <c>web </c> prefix). Use the vendor MIME tree
        /// (<c>application/vnd.&lt;vendor&gt;.&lt;type&gt;</c>) for cross-platform safety.
        /// </summary>
        public static ClipboardFormat Custom(string identifier) => new(identifier ?? string.Empty);

        public bool Equals(ClipboardFormat other) => string.Equals(Identifier, other.Identifier, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ClipboardFormat other && Equals(other);
        public override int GetHashCode() => Identifier?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public override string ToString() => Identifier ?? string.Empty;

        public static bool operator ==(ClipboardFormat a, ClipboardFormat b) => a.Equals(b);
        public static bool operator !=(ClipboardFormat a, ClipboardFormat b) => !a.Equals(b);
    }

    /// <summary>
    /// A single (format, payload) pair on the clipboard. Multi-format writes pass a list
    /// of items in one atomic <see cref="IClipboardProvider.SetItems"/> call; the OS
    /// stores them as a single clipboard write whose richest available format is selected
    /// by the consumer on paste.
    /// </summary>
    /// <remarks>
    /// <see cref="Data"/> holds the format's bytes. For text formats the bytes are UTF-8
    /// (so a consumer can <c>Encoding.UTF8.GetString(item.Data)</c>); for binary formats
    /// (P2 image, RTFD) it is the format's native byte representation.
    /// </remarks>
    public readonly struct ClipboardItem
    {
        /// <summary>Format identifier for <see cref="Data"/>.</summary>
        public ClipboardFormat Format { get; }

        /// <summary>Payload bytes. Never <see langword="null"/>; may be empty.</summary>
        public byte[] Data { get; }

        /// <summary>Payload decoded as UTF-8 text. Empty string for an empty payload.</summary>
        public string Text => Data == null || Data.Length == 0
            ? string.Empty
            : System.Text.Encoding.UTF8.GetString(Data);

        public ClipboardItem(ClipboardFormat format, byte[] data)
        {
            Format = format;
            Data = data ?? Array.Empty<byte>();
        }

        /// <summary>
        /// Convenience constructor that UTF-8-encodes a string into the payload. Use for
        /// any text-style format (<see cref="ClipboardFormat.PlainText"/>,
        /// <see cref="ClipboardFormat.Html"/>, <see cref="ClipboardFormat.Url"/>).
        /// </summary>
        public ClipboardItem(ClipboardFormat format, string text)
        {
            Format = format;
            Data = string.IsNullOrEmpty(text)
                ? Array.Empty<byte>()
                : System.Text.Encoding.UTF8.GetBytes(text);
        }
    }
}
