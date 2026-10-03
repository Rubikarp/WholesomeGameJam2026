using System.Collections.Generic;
using System.Threading.Tasks;

namespace LightSide
{
    /// <summary>
    /// Optional <see cref="IClipboardProvider"/> extension for binary media. File copies arrive as
    /// references — a path list (desktop) or URI list (sandboxed platforms) — not as a single blob, so they
    /// have their own accessor; raster blobs read through <see cref="IClipboardProvider.GetData"/>. A
    /// provider that does not implement this carries text only.
    /// </summary>
    public interface IMediaClipboardProvider : IClipboardProvider
    {
        /// <summary>Whether the clipboard carries file references (CF_HDROP / file-url / content-uri / uri-list).</summary>
        bool HasFiles();

        /// <summary>
        /// File references on the clipboard — absolute paths on desktop, URIs on sandboxed platforms (iOS /
        /// Android / web) — or <see langword="null"/> when none. Get a reference's bytes with <see cref="ReadFile"/>.
        /// </summary>
        string[] GetFiles();

        /// <summary>
        /// Reads the bytes of a reference from <see cref="GetFiles"/>, resolving it however the platform
        /// requires — a path read on desktop, a <c>content://</c> resolver on Android, the captured browser
        /// blob on web. <see langword="null"/> if the reference is unreadable.
        /// </summary>
        byte[] ReadFile(string fileReference);

        /// <summary>
        /// Async variant of <see cref="ReadFile"/> for large media (a dropped video must not
        /// stall the main thread). Desktop reads on a worker; platforms whose bytes are
        /// already in memory (web captured blob) complete synchronously.
        /// </summary>
        Task<byte[]> ReadFileAsync(string fileReference);

        /// <summary>Format identifiers currently on the clipboard, for inspection. Answered by availability probes only — no payload transfers. May be empty when the platform cannot enumerate.</summary>
        IReadOnlyList<ClipboardFormat> GetAvailableFormats();
    }
}
