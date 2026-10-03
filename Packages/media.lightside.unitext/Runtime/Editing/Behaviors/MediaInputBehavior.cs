using System;

namespace LightSide
{
    /// <summary>
    /// Base for handling received media — a clipboard paste, a drag-and-drop, or a picker selection. All three
    /// arrive the same way (check <see cref="MediaContent.Source"/> when it matters). The hook is subscribed
    /// here — subclass and override <see cref="OnImage"/> / <see cref="OnFiles"/> (or <see cref="OnMedia"/> for
    /// full control over every format) to do whatever the app needs: upload, insert an inline object, write to
    /// disk. Return <see langword="true"/> from an override to consume; otherwise it falls through to the text
    /// channels.
    /// </summary>
    /// <remarks>
    /// File copies (a video, gif, or any file) arrive as references through <see cref="OnFiles"/> — read each
    /// with <see cref="ReadFile"/>. Inline image data arrives through <see cref="OnImage"/>. Reading can prompt
    /// the user on iOS / Android / web; the hook only fires on an actual paste / drop / pick, which is the
    /// consent the platforms expect.
    /// </remarks>
    [Serializable]
    public abstract class MediaInputBehavior : InputBehavior
    {
        private static readonly ClipboardFormat[] imageFormats =
            { ClipboardFormat.Png, ClipboardFormat.Jpeg, ClipboardFormat.Gif };

        protected override void OnEnable() => editable.MediaReceived += HandleMedia;
        protected override void OnDisable() => editable.MediaReceived -= HandleMedia;

        private void HandleMedia(ref MediaContent content)
        {
            if (OnMedia(ref content)) { content.Handled = true; return; }

            if (content.HasFiles)
            {
                var files = content.GetFiles();
                if (files != null && files.Length > 0 && OnFiles(files)) { content.Handled = true; return; }
            }

            for (int i = 0; i < imageFormats.Length; i++)
            {
                if (!content.Has(imageFormats[i])) continue;
                var data = content.GetData(imageFormats[i]);
                if (data != null && data.Length > 0 && OnImage(data, imageFormats[i])) { content.Handled = true; return; }
            }
        }

        /// <summary>
        /// Full-control hook — inspect any format on <paramref name="content"/> directly. Return
        /// <see langword="true"/> to consume. Default does nothing and lets <see cref="OnFiles"/> /
        /// <see cref="OnImage"/> run.
        /// </summary>
        protected virtual bool OnMedia(ref MediaContent content) => false;

        /// <summary>Override to handle file references (image / video / any file). Return <see langword="true"/> if consumed.</summary>
        protected virtual bool OnFiles(string[] paths) => false;

        /// <summary>Override to handle a raster image's bytes. Return <see langword="true"/> if consumed.</summary>
        protected virtual bool OnImage(byte[] data, ClipboardFormat format) => false;

        /// <summary>
        /// Reads the bytes of a reference from <see cref="OnFiles"/>, resolved the way the platform requires —
        /// a path read on desktop, a <c>content://</c> resolver on Android, the captured browser blob on web.
        /// <see langword="null"/> if unreadable.
        /// </summary>
        protected byte[] ReadFile(string fileReference) => MediaFileReader.Read(fileReference);

        /// <summary>
        /// Off-main-thread variant of <see cref="ReadFile"/> — read a dropped video or large file
        /// without blocking the frame (upload flows).
        /// </summary>
        protected System.Threading.Tasks.Task<byte[]> ReadFileAsync(string fileReference) => MediaFileReader.ReadAsync(fileReference);
    }
}
