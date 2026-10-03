using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Media offered to a <see cref="UniTextEditable.MediaReceived"/> hook — from a clipboard paste, a
    /// drag-and-drop, or a picker (see <see cref="Source"/>), before the text-adapter pipeline runs. A handler
    /// probes the formats it cares about — image blobs via <see cref="GetData"/>, files via
    /// <see cref="GetFiles"/> / <see cref="ReadFile"/> — and sets <see cref="Handled"/> to consume it; left
    /// unhandled, a paste falls through to the normal text channels. <see cref="MediaInputBehavior"/> is the
    /// inherit-and-override entry point.
    /// </summary>
    public struct MediaContent
    {
        private readonly IClipboardProvider provider;
        private readonly Dictionary<string, byte[]> inlineData;
        private readonly string[] fileRefs;
        private bool handled;

        /// <summary>The editor the media is going into.</summary>
        public UniTextEditable Editable { get; }

        /// <summary>Whether this arrived by paste, drop, or picker.</summary>
        public MediaSource Source { get; }

        internal MediaContent(UniTextEditable editable, IClipboardProvider provider, MediaSource source)
        {
            Editable = editable;
            this.provider = provider;
            inlineData = null;
            fileRefs = null;
            Source = source;
            handled = false;
        }

        internal MediaContent(UniTextEditable editable, MediaSource source, Dictionary<string, byte[]> inlineData, string[] fileRefs)
        {
            Editable = editable;
            provider = null;
            this.inlineData = inlineData;
            this.fileRefs = fileRefs;
            Source = source;
            handled = false;
        }

        /// <summary>Set to consume the media and skip the text channels. Sticky once set.</summary>
        public bool Handled { get => handled; set => handled |= value; }

        /// <summary>Whether <paramref name="format"/> is present.</summary>
        public bool Has(ClipboardFormat format)
            => provider != null ? provider.HasFormat(format)
             : inlineData != null && inlineData.ContainsKey(format.Identifier);

        /// <summary>Raw bytes for <paramref name="format"/>, or <see langword="null"/> if absent.</summary>
        public byte[] GetData(ClipboardFormat format)
            => provider != null ? provider.GetData(format)
             : inlineData != null && inlineData.TryGetValue(format.Identifier, out var d) ? d : null;

        /// <summary>Whether file references are present.</summary>
        public bool HasFiles
            => provider is IMediaClipboardProvider m ? m.HasFiles()
             : fileRefs != null && fileRefs.Length > 0;

        /// <summary>File references (paths or URIs), or <see langword="null"/> if none. Get one's bytes with <see cref="ReadFile"/>.</summary>
        public string[] GetFiles()
            => provider is IMediaClipboardProvider m ? m.GetFiles() : fileRefs;

        /// <summary>Reads the bytes behind a reference from <see cref="GetFiles"/>, resolved the way the platform requires. <see langword="null"/> if unreadable.</summary>
        public byte[] ReadFile(string fileReference) => MediaFileReader.Read(fileReference);

        /// <summary>
        /// Async variant of <see cref="ReadFile"/> — the right call for anything that can be
        /// large (video, archives): the file IO runs off the main thread. Resolves to
        /// <see langword="null"/> if unreadable.
        /// </summary>
        public System.Threading.Tasks.Task<byte[]> ReadFileAsync(string fileReference) => MediaFileReader.ReadAsync(fileReference);
    }

    /// <summary>Subscribe on <see cref="UniTextEditable.MediaReceived"/> to consume a paste, drop, or picker selection before the text channels.</summary>
    public delegate void MediaReceivedHook(ref MediaContent content);
}
