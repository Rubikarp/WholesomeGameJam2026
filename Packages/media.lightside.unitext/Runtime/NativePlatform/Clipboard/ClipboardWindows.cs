#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Windows clipboard implementation using Win32 P/Invoke. Carries plain text
    /// (CF_UNICODETEXT), HTML (the registered "HTML Format" with the byte-offset header
    /// described in <see href="https://learn.microsoft.com/en-us/windows/win32/dataxchg/html-clipboard-format">
    /// the MSDN HTML Clipboard Format spec</see>), Markdown / UniText source / custom
    /// formats (registered names, UTF-8 + NUL), URLs (CFSTR_INETURLW
    /// <c>UniformResourceLocatorW</c> plus <c>text/uri-list</c>), and PNG images with a
    /// synthesized CF_DIBV5 in both directions so PrintScreen screenshots paste in and
    /// copied images land in Paint / Word.
    /// </summary>
    /// <remarks>
    /// <c>OpenClipboard</c> transiently fails with access-denied whenever another process
    /// holds the clipboard — on Windows 10+ that is routine (Clipboard History, OneDrive,
    /// Ditto, RDP chains re-open it after every update) — so every open goes through a
    /// bounded retry, the same convention WPF and Chromium use.
    /// </remarks>
    internal static class ClipboardWindows
    {
        private const uint CF_UNICODETEXT = 13;
        private const uint CF_DIB = 8;
        private const uint CF_DIBV5 = 17;
        private const uint CF_HDROP = 15;

        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint GMEM_ZEROINIT = 0x0040;
        private const uint GHND = GMEM_MOVEABLE | GMEM_ZEROINIT;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll")]
        private static extern int CountClipboardFormats();

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormatW(string lpszFormat);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr GlobalSize(IntPtr hMem);

        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);

        [DllImport("shell32.dll", SetLastError = true, EntryPoint = "DragQueryFileW")]
        private static extern uint DragQueryFileLen(IntPtr hDrop, uint iFile, IntPtr lpszFile, uint cch);

        private static uint cfHtml;
        private static uint cfMarkdown;
        private static uint cfUniTextSource;
        private static uint cfUrlW;
        private static uint cfUriList;
        private static uint cfPng;
        private static readonly Dictionary<string, uint> registeredCustomFormats = new(StringComparer.Ordinal);

        private static uint CfHtml => cfHtml != 0 ? cfHtml : cfHtml = RegisterClipboardFormatW("HTML Format");
        private static uint CfMarkdown => cfMarkdown != 0 ? cfMarkdown : cfMarkdown = RegisterClipboardFormatW("text/markdown");
        private static uint CfUniTextSource => cfUniTextSource != 0 ? cfUniTextSource : cfUniTextSource = RegisterClipboardFormatW("LightSide.UniText");
        private static uint CfUrlW => cfUrlW != 0 ? cfUrlW : cfUrlW = RegisterClipboardFormatW("UniformResourceLocatorW");
        private static uint CfUriList => cfUriList != 0 ? cfUriList : cfUriList = RegisterClipboardFormatW("text/uri-list");
        private static uint CfPng => cfPng != 0 ? cfPng : cfPng = RegisterClipboardFormatW("PNG");

        private static uint GetOrRegisterCustomFormat(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return 0;
            lock (registeredCustomFormats)
            {
                if (registeredCustomFormats.TryGetValue(identifier, out var cached)) return cached;
                uint atom = RegisterClipboardFormatW(identifier);
                if (atom != 0) registeredCustomFormats[identifier] = atom;
                return atom;
            }
        }

        private static bool OpenClipboardRetry()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero)) return true;
                System.Threading.Thread.Sleep(5);
            }
            Debug.LogWarning("[UniText] OpenClipboard failed after retries — another process is holding the clipboard.");
            return false;
        }

        /// <summary>
        /// The single GetClipboardData / GlobalLock / GlobalSize / copy / unlock
        /// choreography every reader decodes from. The clipboard must already be OPEN —
        /// <see cref="ReadHandleBytes"/> wraps it for single-format reads; the batched
        /// <see cref="GetItems"/> keeps one open across all formats.
        /// </summary>
        private static byte[] ReadHandleBytesOpen(uint format)
        {
            if (format == 0 || !IsClipboardFormatAvailable(format)) return null;
            IntPtr hData = GetClipboardData(format);
            if (hData == IntPtr.Zero) return null;
            IntPtr pData = GlobalLock(hData);
            if (pData == IntPtr.Zero) return null;
            try
            {
                int size = (int)GlobalSize(hData);
                if (size <= 0) return null;
                var buffer = new byte[size];
                Marshal.Copy(pData, buffer, 0, size);
                return buffer;
            }
            finally { GlobalUnlock(hData); }
        }

        private static byte[] ReadHandleBytes(uint format)
        {
            if (format == 0 || !IsClipboardFormatAvailable(format)) return null;
            if (!OpenClipboardRetry()) return null;
            try { return ReadHandleBytesOpen(format); }
            finally { CloseClipboard(); }
        }

        private static string DecodeUtf8Z(byte[] bytes)
        {
            if (bytes == null) return null;
            int len = bytes.Length;
            for (int i = 0; i < bytes.Length; i++)
                if (bytes[i] == 0) { len = i; break; }
            return len == 0 ? null : Encoding.UTF8.GetString(bytes, 0, len);
        }

        /// <summary>Bounded by GlobalSize — never scans past the allocation even when a malformed producer omits the terminator.</summary>
        private static string DecodeUtf16Z(byte[] bytes)
        {
            if (bytes == null) return null;
            int charCount = bytes.Length / 2;
            int len = charCount;
            for (int i = 0; i < charCount; i++)
                if (bytes[i * 2] == 0 && bytes[i * 2 + 1] == 0) { len = i; break; }
            return len == 0 ? null : Encoding.Unicode.GetString(bytes, 0, len * 2);
        }

        public static string GetText() => DecodeUtf16Z(ReadHandleBytes(CF_UNICODETEXT));

        public static bool HasText() => IsClipboardFormatAvailable(CF_UNICODETEXT);

        public static bool HasContent() => CountClipboardFormats() > 0;

        public static void SetText(string text)
        {
            if (!OpenClipboardRetry()) return;
            try
            {
                EmptyClipboard();
                if (string.IsNullOrEmpty(text)) return;
                IntPtr hGlobal = AllocUnicodeText(text);
                if (hGlobal == IntPtr.Zero) return;
                if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                    GlobalFree(hGlobal);
            }
            finally { CloseClipboard(); }
        }

        /// <summary>
        /// Availability probe — <c>IsClipboardFormatAvailable</c> per mapped atom, no
        /// payload transfer. PNG also answers for CF_DIBV5 / CF_DIB (screenshot pastes,
        /// which <see cref="GetData"/> converts).
        /// </summary>
        public static bool HasFormat(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            switch (identifier)
            {
                case "text/plain": return IsClipboardFormatAvailable(CF_UNICODETEXT);
                case "text/html": return IsClipboardFormatAvailable(CfHtml);
                case "text/markdown": return IsClipboardFormatAvailable(CfMarkdown);
                case "text/uri-list": return IsClipboardFormatAvailable(CfUrlW) || IsClipboardFormatAvailable(CfUriList);
                case "image/png":
                    return IsClipboardFormatAvailable(CfPng)
                        || IsClipboardFormatAvailable(CF_DIBV5)
                        || IsClipboardFormatAvailable(CF_DIB);
            }
            if (identifier == ClipboardFormat.UniTextSource.Identifier)
                return IsClipboardFormatAvailable(CfUniTextSource);
            return IsClipboardFormatAvailable(GetOrRegisterCustomFormat(identifier));
        }

        /// <summary>
        /// Reads a non-plain-text format. <c>text/html</c> goes through the CF_HTML
        /// header parser (extracts the fragment between
        /// <c>&lt;!--StartFragment--&gt;</c> and <c>&lt;!--EndFragment--&gt;</c> per the
        /// Win32 HTML Clipboard Format spec); URLs prefer CFSTR_INETURLW; everything else
        /// reads its registered format as UTF-8 bytes.
        /// </summary>
        public static string GetFormat(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            if (!OpenClipboardRetry()) return null;
            try { return ReadFormatTextOpen(identifier); }
            finally { CloseClipboard(); }
        }

        /// <summary>Per-format text read assuming the clipboard is already open — shared by <see cref="GetFormat"/> and the batched <see cref="GetItems"/>.</summary>
        private static string ReadFormatTextOpen(string identifier)
        {
            if (identifier == "text/plain")
                return DecodeUtf16Z(ReadHandleBytesOpen(CF_UNICODETEXT));
            if (identifier == "text/html")
                return ExtractCfHtmlFragment(ReadHandleBytesOpen(CfHtml));
            if (identifier == "text/markdown")
                return DecodeUtf8Z(ReadHandleBytesOpen(CfMarkdown));
            if (identifier == ClipboardFormat.UniTextSource.Identifier)
                return DecodeUtf8Z(ReadHandleBytesOpen(CfUniTextSource));
            if (identifier == "text/uri-list")
                return DecodeUtf16Z(ReadHandleBytesOpen(CfUrlW)) ?? DecodeUtf8Z(ReadHandleBytesOpen(CfUriList));

            return DecodeUtf8Z(ReadHandleBytesOpen(GetOrRegisterCustomFormat(identifier)));
        }

        /// <summary>
        /// Batched multi-format read: ONE open/close serves every requested format
        /// (instead of one OpenClipboard cycle per format), and a
        /// <c>GetClipboardSequenceNumber</c> guard retries once when a clipboard-manager
        /// overwrite lands mid-read — a torn item list mixing two clipboard states would
        /// otherwise paste format A's rich payload with format B's plain text.
        /// </summary>
        public static IReadOnlyList<ClipboardItem> GetItems(IReadOnlyList<ClipboardFormat> formats)
        {
            List<ClipboardItem> items = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                uint sequence = GetClipboardSequenceNumber();
                items = ReadItemsOnce(formats);
                if (items == null) return Array.Empty<ClipboardItem>();
                if (GetClipboardSequenceNumber() == sequence) break;
            }
            return items;
        }

        private static List<ClipboardItem> ReadItemsOnce(IReadOnlyList<ClipboardFormat> formats)
        {
            if (!OpenClipboardRetry()) return null;
            var items = new List<ClipboardItem>(formats.Count);
            try
            {
                for (int i = 0; i < formats.Count; i++)
                {
                    var text = ReadFormatTextOpen(formats[i].Identifier);
                    if (!string.IsNullOrEmpty(text))
                        items.Add(new ClipboardItem(formats[i], text));
                }
            }
            finally { CloseClipboard(); }
            return items;
        }

        public static byte[] GetData(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            if (identifier == "image/png")
            {
                var png = ReadHandleBytes(CfPng);
                if (png != null) return png;
                return DibToPng(ReadHandleBytes(CF_DIBV5) ?? ReadHandleBytes(CF_DIB));
            }
            return ReadHandleBytes(GetOrRegisterCustomFormat(identifier));
        }

        public static bool HasFiles() => IsClipboardFormatAvailable(CF_HDROP);

        public static string[] GetFiles()
        {
            if (!IsClipboardFormatAvailable(CF_HDROP)) return null;
            if (!OpenClipboardRetry()) return null;
            try
            {
                IntPtr hDrop = GetClipboardData(CF_HDROP);
                if (hDrop == IntPtr.Zero) return null;

                uint count = DragQueryFileLen(hDrop, 0xFFFFFFFF, IntPtr.Zero, 0);
                if (count == 0) return null;

                var result = new string[count];
                for (uint i = 0; i < count; i++)
                {
                    uint len = DragQueryFileLen(hDrop, i, IntPtr.Zero, 0);
                    var sb = new StringBuilder((int)len + 1);
                    DragQueryFile(hDrop, i, sb, (uint)sb.Capacity);
                    result[i] = sb.ToString();
                }
                return result;
            }
            finally { CloseClipboard(); }
        }

        /// <summary>
        /// Multi-format atomic write. Opens the clipboard once, empties it, then sets every
        /// item of the pre-partitioned write — CF_UNICODETEXT for plain text, registered
        /// "HTML Format" with the byte-offset header for HTML, CFSTR_INETURLW +
        /// <c>text/uri-list</c> for URLs, registered "PNG" plus a synthesized CF_DIBV5 for
        /// the image slot (Paint / Word / most Win32 apps never read the registered PNG
        /// format). Returns <see langword="true"/> only when at least one item actually
        /// landed, so the caller's plain-text fallback can still salvage a total failure.
        /// </summary>
        public static bool SetItems(ClipboardWrite write)
        {
            if (!OpenClipboardRetry()) return false;

            bool wroteAny = false;
            try
            {
                EmptyClipboard();

                var texts = write.Texts;
                for (int i = 0; i < texts.Count; i++)
                {
                    var item = texts[i];

                    if (item.Format == ClipboardFormat.PlainText)
                    {
                        wroteAny |= PutHandle(CF_UNICODETEXT, AllocUnicodeText(item.Text));
                    }
                    else if (item.Format == ClipboardFormat.Html)
                    {
                        wroteAny |= PutHandle(CfHtml, AllocBytes(BuildCfHtml(item.Text)));
                    }
                    else if (item.Format == ClipboardFormat.Markdown)
                    {
                        wroteAny |= PutHandle(CfMarkdown, AllocBytes(Utf8Z(item.Text)));
                    }
                    else if (item.Format == ClipboardFormat.UniTextSource)
                    {
                        wroteAny |= PutHandle(CfUniTextSource, AllocBytes(Utf8Z(item.Text)));
                    }
                    else if (item.Format == ClipboardFormat.Url)
                    {
                        wroteAny |= PutHandle(CfUrlW, AllocUnicodeText(item.Text));
                        wroteAny |= PutHandle(CfUriList, AllocBytes(Utf8Z(item.Text)));
                    }
                    else
                    {
                        wroteAny |= PutHandle(GetOrRegisterCustomFormat(item.Format.Identifier), AllocBytes(Utf8Z(item.Text)));
                    }
                }

                if (write.HasImage)
                {
                    var id = write.Image.Format.Identifier;
                    uint atom = id == "image/png" ? CfPng : GetOrRegisterCustomFormat(id);
                    wroteAny |= PutHandle(atom, AllocBytes(write.Image.Data));
                    if (id == "image/png")
                        wroteAny |= PutHandle(CF_DIBV5, AllocBytes(PngToDibV5(write.Image.Data)));
                }
                return wroteAny;
            }
            finally { CloseClipboard(); }
        }

        private static bool PutHandle(uint format, IntPtr hGlobal)
        {
            if (format == 0 || hGlobal == IntPtr.Zero)
            {
                if (hGlobal != IntPtr.Zero) GlobalFree(hGlobal);
                return false;
            }
            if (SetClipboardData(format, hGlobal) == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }
            return true;
        }

        private static byte[] Utf8Z(string text) => Encoding.UTF8.GetBytes((text ?? string.Empty) + '\0');

        private static IntPtr AllocUnicodeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return IntPtr.Zero;
            int byteCount = (text.Length + 1) * 2;
            IntPtr hGlobal = GlobalAlloc(GHND, (UIntPtr)byteCount);
            if (hGlobal == IntPtr.Zero) return IntPtr.Zero;

            IntPtr pGlobal = GlobalLock(hGlobal);
            if (pGlobal == IntPtr.Zero) { GlobalFree(hGlobal); return IntPtr.Zero; }
            try
            {
                unsafe
                {
                    fixed (char* src = text)
                    {
                        Buffer.MemoryCopy(src, (void*)pGlobal, byteCount, text.Length * 2);
                    }
                }
            }
            finally { GlobalUnlock(hGlobal); }
            return hGlobal;
        }

        private static IntPtr AllocBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return IntPtr.Zero;
            IntPtr hGlobal = GlobalAlloc(GHND, (UIntPtr)bytes.Length);
            if (hGlobal == IntPtr.Zero) return IntPtr.Zero;

            IntPtr pGlobal = GlobalLock(hGlobal);
            if (pGlobal == IntPtr.Zero) { GlobalFree(hGlobal); return IntPtr.Zero; }
            try { Marshal.Copy(bytes, 0, pGlobal, bytes.Length); }
            finally { GlobalUnlock(hGlobal); }
            return hGlobal;
        }

        /// <summary>
        /// Builds the CF_HTML clipboard payload (UTF-8 bytes with the offset header that
        /// MSDN documents, plus the conventional trailing NUL Chromium writes). Offsets are
        /// zero-padded 10-digit decimals so they have fixed width — we can back-patch them
        /// after the body is assembled without disturbing downstream offsets.
        /// </summary>
        private static byte[] BuildCfHtml(string fragmentHtml)
        {
            const string headerTemplate =
                "Version:0.9\r\n" +
                "StartHTML:{0:D10}\r\n" +
                "EndHTML:{1:D10}\r\n" +
                "StartFragment:{2:D10}\r\n" +
                "EndFragment:{3:D10}\r\n";
            const string htmlPrefix = "<html>\r\n<body>\r\n<!--StartFragment-->";
            const string htmlSuffix = "<!--EndFragment-->\r\n</body>\r\n</html>";

            int headerLen = Encoding.UTF8.GetByteCount(string.Format(headerTemplate, 0, 0, 0, 0));
            int startHtml = headerLen;
            int startFragment = startHtml + Encoding.UTF8.GetByteCount(htmlPrefix);
            int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragmentHtml);
            int endHtml = endFragment + Encoding.UTF8.GetByteCount(htmlSuffix);

            string header = string.Format(headerTemplate, startHtml, endHtml, startFragment, endFragment);

            var sb = new StringBuilder(endHtml + 1);
            sb.Append(header);
            sb.Append(htmlPrefix);
            sb.Append(fragmentHtml);
            sb.Append(htmlSuffix);
            sb.Append('\0');
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>
        /// Extracts the fragment text of a CF_HTML clipboard payload from the offsets
        /// in its header.
        /// </summary>
        private static string ExtractCfHtmlFragment(byte[] buffer)
        {
            if (buffer == null) return null;

            int actualLen = buffer.Length;
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i] == 0) { actualLen = i; break; }
            if (actualLen == 0) return null;

            string full = Encoding.UTF8.GetString(buffer, 0, actualLen);
            int sf = ParseHeaderOffset(full, "StartFragment:");
            int ef = ParseHeaderOffset(full, "EndFragment:");
            if (sf < 0 || ef < 0 || ef <= sf || ef > actualLen) return full;
            return Encoding.UTF8.GetString(buffer, sf, ef - sf);
        }

        private static int ParseHeaderOffset(string header, string key)
        {
            int idx = header.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return -1;
            int start = idx + key.Length;
            int end = start;
            while (end < header.Length && header[end] >= '0' && header[end] <= '9') end++;
            if (end == start) return -1;
            return int.TryParse(header.AsSpan(start, end - start), out var value) ? value : -1;
        }

        private const uint BiRgb = 0;
        private const uint BiBitfields = 3;

        /// <summary>
        /// Decodes a CF_DIB / CF_DIBV5 payload (the format PrintScreen and most Win32 apps
        /// produce) into PNG bytes via a Texture2D round-trip. Covers the layouts that
        /// occur in practice: 32bpp BI_RGB / BI_BITFIELDS with the standard BGRA channel
        /// masks and 24bpp BI_RGB, bottom-up or top-down. A 32bpp image whose alpha channel
        /// is entirely zero is treated as opaque — BI_RGB leaves alpha undefined and
        /// screenshots routinely carry zeros there. Returns <see langword="null"/> on
        /// anything else (compressed, paletted, exotic masks). Main thread only.
        /// </summary>
        private static byte[] DibToPng(byte[] dib)
        {
            if (dib == null || dib.Length < 40) return null;

            int headerSize = BitConverter.ToInt32(dib, 0);
            if (headerSize < 40 || headerSize > dib.Length) return null;
            int width = BitConverter.ToInt32(dib, 4);
            int rawHeight = BitConverter.ToInt32(dib, 8);
            int bitCount = BitConverter.ToUInt16(dib, 14);
            uint compression = BitConverter.ToUInt32(dib, 16);
            uint clrUsed = headerSize >= 36 ? BitConverter.ToUInt32(dib, 32) : 0;

            if (rawHeight == int.MinValue) return null;
            bool topDown = rawHeight < 0;
            int height = Math.Abs(rawHeight);
            if (width <= 0 || height == 0 || (long)width * height > 8192L * 8192L) return null;
            if (bitCount != 32 && bitCount != 24) return null;
            if (compression != BiRgb && compression != BiBitfields) return null;

            int pixelOffset = headerSize;
            if (compression == BiBitfields)
            {
                uint redMask, greenMask, blueMask;
                if (headerSize >= 52)
                {
                    redMask = BitConverter.ToUInt32(dib, 40);
                    greenMask = BitConverter.ToUInt32(dib, 44);
                    blueMask = BitConverter.ToUInt32(dib, 48);
                }
                else
                {
                    if (dib.Length < headerSize + 12) return null;
                    redMask = BitConverter.ToUInt32(dib, headerSize);
                    greenMask = BitConverter.ToUInt32(dib, headerSize + 4);
                    blueMask = BitConverter.ToUInt32(dib, headerSize + 8);
                    pixelOffset += 12;
                }
                if (redMask != 0x00FF0000 || greenMask != 0x0000FF00 || blueMask != 0x000000FF) return null;
            }
            pixelOffset += (int)Math.Min(clrUsed, 256) * 4;

            int bytesPerPixel = bitCount / 8;
            int stride = ((width * bitCount + 31) / 32) * 4;
            if ((long)pixelOffset + (long)stride * height > dib.Length) return null;

            var pixels = new Color32[width * height];
            bool anyAlpha = false;
            for (int y = 0; y < height; y++)
            {
                int srcRow = pixelOffset + (topDown ? y : height - 1 - y) * stride;
                int dstRow = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    int p = srcRow + x * bytesPerPixel;
                    byte b = dib[p];
                    byte g = dib[p + 1];
                    byte r = dib[p + 2];
                    byte a = bitCount == 32 ? dib[p + 3] : (byte)255;
                    if (a != 0) anyAlpha = true;
                    pixels[dstRow + x] = new Color32(r, g, b, a);
                }
            }
            if (!anyAlpha)
                for (int i = 0; i < pixels.Length; i++) pixels[i].a = 255;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                return texture.EncodeToPNG();
            }
            finally { ObjectUtils.SafeDestroy(texture); }
        }

        /// <summary>
        /// Encodes PNG bytes as a CF_DIBV5 payload (BITMAPV5HEADER, 32bpp BI_BITFIELDS,
        /// standard BGRA masks, sRGB, bottom-up) via a Texture2D round-trip, so pasting a
        /// copied image works in the Win32 apps that never read the registered PNG format.
        /// Returns <see langword="null"/> when the PNG cannot be decoded. Main thread only.
        /// </summary>
        private static byte[] PngToDibV5(byte[] png)
        {
            if (png == null || png.Length == 0) return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(png, false)) return null;
                int width = texture.width;
                int height = texture.height;
                var pixels = texture.GetPixels32();

                const int headerSize = 124;
                int stride = width * 4;
                var dib = new byte[headerSize + stride * height];
                void PutInt(int offset, int value) => BitConverter.GetBytes(value).CopyTo(dib, offset);
                void PutUInt(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(dib, offset);

                PutInt(0, headerSize);
                PutInt(4, width);
                PutInt(8, height);
                dib[12] = 1;
                dib[14] = 32;
                PutUInt(16, BiBitfields);
                PutInt(20, stride * height);
                PutInt(24, 2835);
                PutInt(28, 2835);
                PutUInt(40, 0x00FF0000);
                PutUInt(44, 0x0000FF00);
                PutUInt(48, 0x000000FF);
                PutUInt(52, 0xFF000000);
                PutUInt(56, 0x73524742);
                PutInt(108, 4);

                for (int y = 0; y < height; y++)
                {
                    int srcRow = y * width;
                    int dstRow = headerSize + y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        var c = pixels[srcRow + x];
                        int p = dstRow + x * 4;
                        dib[p] = c.b;
                        dib[p + 1] = c.g;
                        dib[p + 2] = c.r;
                        dib[p + 3] = c.a;
                    }
                }
                return dib;
            }
            finally { ObjectUtils.SafeDestroy(texture); }
        }
    }
}
#endif
