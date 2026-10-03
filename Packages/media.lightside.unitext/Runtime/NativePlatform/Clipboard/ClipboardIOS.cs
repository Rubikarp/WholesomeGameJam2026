#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LightSide
{
    /// <summary>
    /// iOS clipboard implementation using a native Objective-C plugin.
    /// Wraps <c>UIPasteboard.generalPasteboard</c> for plain text operations.
    /// </summary>
    /// <remarks>
    /// On iOS, native plugin functions are linked statically (<c>[DllImport("__Internal")]</c>).
    /// The native side is compiled from <c>UniTextClipboard.mm</c> in the iOS Plugins folder.
    /// Strings returned from native code are allocated via <c>strdup</c> and freed via
    /// <c>UniTextClipboard_FreeString</c> on the C# side.
    /// </remarks>
    internal static class ClipboardIOS
    {

        [DllImport("__Internal")]
        private static extern IntPtr UniTextClipboard_GetText();

        [DllImport("__Internal")]
        private static extern void UniTextClipboard_SetText(string text);

        [DllImport("__Internal")]
        private static extern int UniTextClipboard_HasText();

        [DllImport("__Internal")]
        private static extern int UniTextClipboard_HasContent();

        [DllImport("__Internal")]
        private static extern void UniTextClipboard_FreeString(IntPtr ptr);

        public static string GetText()
        {
            IntPtr ptr = UniTextClipboard_GetText();
            if (ptr == IntPtr.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUTF8(ptr);
            }
            finally
            {
                UniTextClipboard_FreeString(ptr);
            }
        }

        public static void SetText(string text)
        {
            UniTextClipboard_SetText(text ?? string.Empty);
        }

        public static bool HasText()
        {
            return UniTextClipboard_HasText() != 0;
        }

        public static bool HasContent() => UniTextClipboard_HasContent() != 0;

        [DllImport("__Internal")]
        private static extern IntPtr UniTextClipboard_GetFormat(string identifier);

        [DllImport("__Internal")]
        private static extern void UniTextClipboard_SetItems(IntPtr[] formats, IntPtr[] payloads, int count,
            string imageFormat, IntPtr imageBytes, int imageLength);

        public static string GetFormat(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            IntPtr ptr = UniTextClipboard_GetFormat(identifier);
            if (ptr == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(ptr); }
            finally { UniTextClipboard_FreeString(ptr); }
        }

        [DllImport("__Internal")] private static extern IntPtr UniTextClipboard_GetData(string identifier, out int length);
        [DllImport("__Internal")] private static extern void UniTextClipboard_SetData(string identifier, IntPtr bytes, int length);
        [DllImport("__Internal")] private static extern void UniTextClipboard_FreeData(IntPtr ptr);
        [DllImport("__Internal")] private static extern int UniTextClipboard_HasFormatData(string identifier);
        [DllImport("__Internal")] private static extern int UniTextClipboard_HasFiles();
        [DllImport("__Internal")] private static extern IntPtr UniTextClipboard_GetFiles();

        public static byte[] GetData(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            IntPtr p = UniTextClipboard_GetData(identifier, out int len);
            if (p == IntPtr.Zero || len <= 0) return null;
            try { var buf = new byte[len]; Marshal.Copy(p, buf, 0, len); return buf; }
            finally { UniTextClipboard_FreeData(p); }
        }

        /// <summary>Availability probe via <c>containsPasteboardTypes:</c> — no payload transfer, any UTI (text or binary).</summary>
        public static bool HasFormat(string identifier)
            => !string.IsNullOrEmpty(identifier) && UniTextClipboard_HasFormatData(identifier) != 0;

        public static bool HasFiles() => UniTextClipboard_HasFiles() != 0;

        public static string[] GetFiles()
        {
            IntPtr p = UniTextClipboard_GetFiles();
            if (p == IntPtr.Zero) return null;
            try { var s = Marshal.PtrToStringUTF8(p); return string.IsNullOrEmpty(s) ? null : s.Split('\n'); }
            finally { UniTextClipboard_FreeString(p); }
        }

        /// <summary>
        /// Multi-format atomic write: text items and the image payload travel in ONE
        /// native <c>SetItems</c> dispatch — <c>UIPasteboard.items</c> takes a single
        /// dictionary mixing <c>NSString</c> and <c>NSData</c> values, so text is never
        /// dropped when an image rides along. Image-only writes go through the dedicated
        /// binary entry point.
        /// </summary>
        public static bool SetItems(ClipboardWrite write)
        {
            var texts = write.Texts;

            if (texts.Count == 0)
            {
                if (!write.HasImage) return false;
                var data = write.Image.Data;
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try { UniTextClipboard_SetData(write.Image.Format.Identifier, h.AddrOfPinnedObject(), data.Length); }
                finally { h.Free(); }
                return true;
            }

            if (!write.HasImage)
                return MarshalUtf8.WithUtf8Items(texts, static (formats, payloads, count) =>
                    UniTextClipboard_SetItems(formats, payloads, count, null, IntPtr.Zero, 0));

            var handle = GCHandle.Alloc(write.Image.Data, GCHandleType.Pinned);
            try
            {
                string imageFormat = write.Image.Format.Identifier;
                IntPtr imageBytes = handle.AddrOfPinnedObject();
                int imageLength = write.Image.Data.Length;
                return MarshalUtf8.WithUtf8Items(texts, (formats, payloads, count) =>
                    UniTextClipboard_SetItems(formats, payloads, count, imageFormat, imageBytes, imageLength));
            }
            finally { handle.Free(); }
        }

        /// <summary>No batched read on this platform — the provider reads per-format.</summary>
        public static IReadOnlyList<ClipboardItem> GetItems(IReadOnlyList<ClipboardFormat> formats) => null;
    }
}
#endif
