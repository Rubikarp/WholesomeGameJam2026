#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// WebGL clipboard backend using <c>navigator.clipboard.write([ClipboardItem])</c>
    /// for multi-format writes and the global <c>paste</c> DOM event for synchronous
    /// multi-format reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Async Clipboard API is asynchronous — C# cannot block on it. Reads are
    /// served from a synchronous DOM-event cache populated when the browser delivers
    /// a <c>paste</c> event with full <c>DataTransfer</c> contents. Writes go through
    /// <c>navigator.clipboard.write</c> with a single <c>ClipboardItem</c> carrying
    /// every requested format simultaneously — the paste consumer chooses the richest
    /// format it understands. Markdown is delivered through the Web Custom Format
    /// channel (<c>web text/markdown</c>) on Chromium; Firefox / Safari ignore it.
    /// </para>
    /// </remarks>
    internal static class ClipboardWebGL
    {
        [DllImport("__Internal")]
        private static extern IntPtr UniTextClipboard_GetText();

        [DllImport("__Internal")]
        private static extern IntPtr UniTextClipboard_GetFormat(string format);

        [DllImport("__Internal")]
        private static extern void UniTextClipboard_SetText(string text);

        [DllImport("__Internal")]
        private static extern int UniTextClipboard_SetItems(IntPtr[] formats, IntPtr[] payloads, int count,
            string imageMime, IntPtr imageBytes, int imageLength);

        [DllImport("__Internal")]
        private static extern void UniTextClipboard_RequestAsyncReadText(string format, int requestId);

        [DllImport("__Internal")]
        private static extern int UniTextClipboard_GetCaptureSequence();

        [DllImport("__Internal")]
        private static extern void UniTextClipboard_Init();

        [DllImport("__Internal")]
        private static extern void UniTextClipboard_Shutdown();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EagerInit()
        {
            try { UniTextClipboard_Init(); }
            catch (Exception e) { Debug.LogWarning($"[UniText] WebGL clipboard init failed: {e.Message}"); }
            Application.quitting += Shutdown;
        }

        /// <summary>
        /// Detaches the JS paste listener and frees the capture cache — without it a player
        /// re-instantiated on the same page leaves the old listener calling into the previous
        /// wasm module's heap.
        /// </summary>
        private static void Shutdown()
        {
            try { UniTextClipboard_Shutdown(); }
            catch (Exception e) { Debug.LogWarning($"[UniText] WebGL clipboard shutdown failed: {e.Message}"); }
        }

        public static string GetText()
        {
            IntPtr ptr = UniTextClipboard_GetText();
            return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
        }

        public static void SetText(string text)
        {
            UniTextClipboard_SetText(text ?? string.Empty);
        }

        /// <summary>
        /// Always true: the browser reveals clipboard content only on a user paste event (the
        /// sync capture cache is structurally empty before that), so availability is unknowable
        /// ahead of time — paste is offered optimistically and the async pipeline resolves the
        /// actual content on demand.
        /// </summary>
        public static bool HasText() => true;

        public static bool HasContent() => true;

        /// <summary>
        /// Reads a non-plain-text format captured on the most recent paste event.
        /// Returns <see langword="null"/> when nothing was captured for that format.
        /// </summary>
        public static string GetFormat(string formatIdentifier)
        {
            if (string.IsNullOrEmpty(formatIdentifier)) return null;
            IntPtr ptr = UniTextClipboard_GetFormat(formatIdentifier);
            return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
        }

        /// <summary>
        /// Availability probe against the paste-event capture cache (text slots and binary
        /// blobs) — an in-heap pointer check, no clipboard access. The browser gives no
        /// synchronous OS-clipboard probe; a true check is the async path.
        /// </summary>
        public static bool HasFormat(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            return UniTextClipboard_GetFormat(identifier) != IntPtr.Zero
                   || UniTextClipboard_HasFormatData(identifier) != 0;
        }

        [DllImport("__Internal")] private static extern int UniTextClipboard_HasFormatData(string format);
        [DllImport("__Internal")] private static extern int UniTextClipboard_GetDataLength(string format);
        [DllImport("__Internal")] private static extern void UniTextClipboard_GetDataCopy(string format, IntPtr dst, int maxLen);
        [DllImport("__Internal")] private static extern int UniTextClipboard_HasFiles();
        [DllImport("__Internal")] private static extern IntPtr UniTextClipboard_GetFiles();
        [DllImport("__Internal")] private static extern int UniTextClipboard_WriteImage(string mime, IntPtr data, int len);
        [DllImport("__Internal")] private static extern int UniTextClipboard_ReadFileLength(string name);
        [DllImport("__Internal")] private static extern void UniTextClipboard_ReadFileCopy(string name, IntPtr dst, int maxLen);

        public static byte[] GetData(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            int len = UniTextClipboard_GetDataLength(identifier);
            if (len <= 0) return null;
            var buf = new byte[len];
            var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try { UniTextClipboard_GetDataCopy(identifier, h.AddrOfPinnedObject(), buf.Length); }
            finally { h.Free(); }
            return buf;
        }

        public static bool HasFiles() => UniTextClipboard_HasFiles() != 0;

        public static string[] GetFiles()
        {
            IntPtr p = UniTextClipboard_GetFiles();
            if (p == IntPtr.Zero) return null;
            var s = Marshal.PtrToStringUTF8(p);
            return string.IsNullOrEmpty(s) ? null : s.Split('\n');
        }

        public static byte[] ReadFile(string fileReference)
        {
            if (string.IsNullOrEmpty(fileReference)) return null;
            int len = UniTextClipboard_ReadFileLength(fileReference);
            if (len <= 0) return null;
            var buf = new byte[len];
            var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try { UniTextClipboard_ReadFileCopy(fileReference, h.AddrOfPinnedObject(), buf.Length); }
            finally { h.Free(); }
            return buf;
        }

        /// <summary>
        /// Multi-format atomic write: text items and the image payload travel in ONE
        /// jslib <c>SetItems</c> dispatch — Chromium's <c>ClipboardItem</c> carries
        /// <c>{"image/png": blob, "text/plain": blob, …}</c> in a single write, so text is
        /// never dropped when an image rides along. The jslib returns 0 on browsers
        /// without the async ClipboardItem API; the return value is deliberately ignored
        /// here: in that branch the jslib already wrote the plain-text item (when present)
        /// via <c>execCommand('copy')</c>, and rich-only lists are simply unwritable on
        /// such browsers — the provider's plain-only salvage could recover nothing either
        /// way, so reporting <see langword="true"/> just prevents a duplicate plain write.
        /// </summary>
        public static bool SetItems(ClipboardWrite write)
        {
            var texts = write.Texts;

            if (texts.Count == 0)
            {
                if (!write.HasImage) return false;
                var data = write.Image.Data;
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try { return UniTextClipboard_WriteImage(write.Image.Format.Identifier, h.AddrOfPinnedObject(), data.Length) != 0; }
                finally { h.Free(); }
            }

            if (!write.HasImage)
                return MarshalUtf8.WithUtf8Items(texts, static (formats, payloads, count) =>
                    UniTextClipboard_SetItems(formats, payloads, count, null, IntPtr.Zero, 0));

            var handle = GCHandle.Alloc(write.Image.Data, GCHandleType.Pinned);
            try
            {
                string imageMime = write.Image.Format.Identifier;
                IntPtr imageBytes = handle.AddrOfPinnedObject();
                int imageLength = write.Image.Data.Length;
                return MarshalUtf8.WithUtf8Items(texts, (formats, payloads, count) =>
                    UniTextClipboard_SetItems(formats, payloads, count, imageMime, imageBytes, imageLength));
            }
            finally { handle.Free(); }
        }

        /// <summary>No batched sync read on this platform — reads come from the paste capture cache; the true batch is <see cref="GetItemsAsync"/>.</summary>
        public static IReadOnlyList<ClipboardItem> GetItems(IReadOnlyList<ClipboardFormat> formats) => null;

        public static Task<string> GetTextAsync(string formatIdentifier)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            int requestId = WebGLAsyncDispatcher.RegisterText(tcs);
            UniTextClipboard_RequestAsyncReadText(formatIdentifier ?? "text/plain", requestId);
            return tcs.Task;
        }

        [DllImport("__Internal")] private static extern void UniTextClipboard_RequestAsyncReadAll(string formats, int requestId);

        private static int lastConsumedSequence;

        /// <summary>
        /// Whether a real DOM <c>paste</c> event refreshed the capture cache since the last
        /// consume. Writes mirror into the cache but deliberately do not bump the sequence —
        /// self-written content never masquerades as a fresh external paste.
        /// </summary>
        internal static bool HasFreshCapture() => UniTextClipboard_GetCaptureSequence() != lastConsumedSequence;

        internal static void MarkCaptureConsumed() => lastConsumedSequence = UniTextClipboard_GetCaptureSequence();

        /// <summary>
        /// The paste pipeline's one-shot read. Fast path: a real paste event just filled the
        /// capture cache (hardware Ctrl+V, browser Edit &gt; Paste) — every format comes from
        /// the cache with zero clipboard reads and the capture is marked consumed. Otherwise
        /// (programmatic paste from our own UI) ONE <c>navigator.clipboard.read()</c> serves
        /// every requested format (<c>UniTextClipboard_RequestAsyncReadAll</c>) — Safari and
        /// Firefox reject reads after the first consumes the transient user activation, and
        /// Safari shows a paste prompt per read; a denied / empty read falls back to the
        /// capture cache inside <see cref="WebGLAsyncDispatcher"/>.
        /// </summary>
        public static async Task<IReadOnlyList<ClipboardItem>> GetItemsAsync(IReadOnlyList<ClipboardFormat> formats)
        {
            if (HasFreshCapture())
            {
                MarkCaptureConsumed();
                return ReadCapturedItems(formats);
            }

            var sb = new System.Text.StringBuilder(64);
            for (int i = 0; i < formats.Count; i++)
            {
                if (string.IsNullOrEmpty(formats[i].Identifier)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(formats[i].Identifier);
            }

            var tcs = new TaskCompletionSource<IReadOnlyList<ClipboardItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
            int requestId = WebGLAsyncDispatcher.RegisterItems(tcs, formats);
            UniTextClipboard_RequestAsyncReadAll(sb.ToString(), requestId);
            return await tcs.Task;
        }

        /// <summary>Whatever the last paste event captured for the requested formats.</summary>
        internal static IReadOnlyList<ClipboardItem> ReadCapturedItems(IReadOnlyList<ClipboardFormat> formats)
        {
            var items = new List<ClipboardItem>(formats.Count);
            for (int i = 0; i < formats.Count; i++)
            {
                var text = formats[i] == ClipboardFormat.PlainText ? GetText() : GetFormat(formats[i].Identifier);
                if (!string.IsNullOrEmpty(text)) items.Add(new ClipboardItem(formats[i], text));
            }
            return items;
        }
    }
}
#endif
