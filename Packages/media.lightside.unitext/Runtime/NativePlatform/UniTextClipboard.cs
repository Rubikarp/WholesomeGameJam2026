using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using PlatformClipboard = LightSide.ClipboardWindows;
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
using PlatformClipboard = LightSide.ClipboardMacOS;
#elif UNITY_IOS && !UNITY_EDITOR
using PlatformClipboard = LightSide.ClipboardIOS;
#elif UNITY_ANDROID && !UNITY_EDITOR
using PlatformClipboard = LightSide.ClipboardAndroid;
#elif UNITY_WEBGL && !UNITY_EDITOR
using PlatformClipboard = LightSide.ClipboardWebGL;
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
using PlatformClipboard = LightSide.ClipboardLinux;
#else
using PlatformClipboard = LightSide.ClipboardFallback;
#endif

namespace LightSide
{
    /// <summary>
    /// Cross-platform system-clipboard access. Routes through <see cref="Provider"/> to
    /// the active <see cref="IClipboardProvider"/>; convenience static methods on this
    /// class cover the plain-text fast path that most integrators need.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bypasses Unity's <c>GUIUtility.systemCopyBuffer</c> — that API delivers plain text
    /// only and goes through Unity's editor abstractions, missing OS-level features the
    /// real clipboard exposes (Win32 CF_HTML, NSPasteboard, UIPasteboard items, etc.).
    /// Each platform uses its native clipboard API directly; integrators that need
    /// multi-format access (HTML paste from Word / Notion / Chrome, images, custom
    /// in-app formats for atomic-token round-trip) go through
    /// <see cref="Provider"/>.<see cref="IClipboardProvider.SetItems"/> /
    /// <see cref="IClipboardProvider.TryGetText"/> / <see cref="IClipboardProvider.GetData"/>.
    /// </para>
    /// <para>Per-platform format support:</para>
    /// <list type="table">
    ///   <listheader><term>Platform</term><description>Formats</description></listheader>
    ///   <item><term>Windows</term><description>Plain (CF_UNICODETEXT), HTML ("HTML Format" with CF_HTML header), Markdown / UniText source / custom (registered formats, UTF-8 + NUL), URL (UniformResourceLocatorW + text/uri-list), PNG (registered "PNG" + synthesized CF_DIBV5 both directions), files (CF_HDROP).</description></item>
    ///   <item><term>macOS</term><description>All formats as pasteboard UTIs in one atomic write (public.html, net.daringfireball.markdown, com.lightside.unitext, public.url, public.png / jpeg / gif, custom UTIs verbatim), files (public.file-url).</description></item>
    ///   <item><term>iOS</term><description>Same UTI mapping via the native plugin; one items dictionary per write. UIPasteControl feeds captured payloads past the paste-permission prompt.</description></item>
    ///   <item><term>Android</term><description>Plain + HTML in one ClipData (newHtmlText); vendor / custom MIMEs advertised in the ClipDescription with payloads carried by an in-process cache (same-app round-trip; the platform clip has no per-MIME binary slot); images via FileProvider content URI.</description></item>
    ///   <item><term>WebGL</term><description>Async Clipboard API: text/plain, text/html, text/uri-list, image/png native; anything else through the Chromium <c>web </c> custom-format prefix (invisible to Firefox / Safari). Sync reads come from the capture cache the browser <c>paste</c> event fills; programmatic paste must use the async path.</description></item>
    ///   <item><term>Linux</term><description>Any target via xclip / wl-clipboard subprocesses; reads accept any offered target, writes offer ONE format (plain preferred, else the richest single item). xsel is plain-text only. Prefer the async provider path — sync reads spawn a blocking subprocess.</description></item>
    ///   <item><term>Other</term><description><c>GUIUtility.systemCopyBuffer</c> plain-text fallback.</description></item>
    /// </list>
    /// <para>
    /// Integrators wanting a mock for tests, a sandboxed clipboard, or platforms outside
    /// the shipped set assign <see cref="Provider"/> directly. Setting it to
    /// <see langword="null"/> falls back to the platform default.
    /// </para>
    /// </remarks>
    public static class UniTextClipboard
    {
        private static IClipboardProvider customProvider;
        private static readonly IClipboardProvider DefaultProvider = new PlatformClipboardProvider();

        /// <summary>
        /// Active clipboard provider. Returns the platform default unless an integrator
        /// has installed a custom one. Assigning <see langword="null"/> reverts to the
        /// default.
        /// </summary>
        public static IClipboardProvider Provider
        {
            get => customProvider ?? DefaultProvider;
            set => customProvider = value;
        }

        /// <summary>
        /// Reads plain text from the system clipboard. Equivalent to
        /// <c>Provider.GetText(ClipboardFormat.PlainText)</c>. Returns <see langword="null"/>
        /// or empty when the clipboard has no text payload.
        /// </summary>
        public static string GetText() => Provider.GetText(ClipboardFormat.PlainText);

        /// <summary>
        /// Writes plain text to the system clipboard, replacing any prior contents.
        /// Equivalent to <c>Provider.SetItems(new[] { new ClipboardItem(PlainText, text) })</c>.
        /// </summary>
        public static void SetText(string text)
        {
            var items = new[] { new ClipboardItem(ClipboardFormat.PlainText, text ?? string.Empty) };
            Provider.SetItems(items);
        }

        /// <summary>
        /// Reports whether the clipboard currently carries plain text — an availability
        /// probe, no payload transfer. Equivalent to
        /// <c>Provider.HasFormat(ClipboardFormat.PlainText)</c>.
        /// </summary>
        public static bool HasText() => Provider.HasFormat(ClipboardFormat.PlainText);

        /// <summary>Whether the system clipboard contains at least one item, regardless of format.</summary>
        public static bool HasContent() => Provider?.HasContent() == true;

        /// <summary>
        /// Async plain-text read. On WebGL goes through <c>navigator.clipboard.readText()</c>
        /// (works for programmatic paste, not just hardware Ctrl+V) and requires a
        /// user-activation context. Other platforms wrap <see cref="GetText()"/> in a completed task.
        /// </summary>
        public static Task<string> GetTextAsync() => GetTextAsync(ClipboardFormat.PlainText);

        /// <summary>Async read for any format. See <see cref="GetTextAsync()"/> for the WebGL contract.</summary>
        public static Task<string> GetTextAsync(ClipboardFormat format)
        {
            var provider = Provider;
            if (provider is IAsyncClipboardProvider asyncProvider)
                return asyncProvider.GetTextAsync(format);
            return Task.FromResult(provider?.GetText(format));
        }
    }

    /// <summary>
    /// A multi-format clipboard write, pre-partitioned by
    /// <see cref="PlatformClipboardProvider.SetItems"/>, which owns the cross-platform
    /// policy: the image gets a dedicated slot (first non-empty image item), and a
    /// markdown item synthesizes the plain-text floor when no plain item is present.
    /// Platform classes receive this structure and do transport mechanics only.
    /// </summary>
    internal readonly struct ClipboardWrite
    {
        /// <summary>All non-image items in write order (a synthesized plain floor comes first). Never null.</summary>
        public readonly List<ClipboardItem> Texts;

        /// <summary>First image item carrying a non-empty payload. Valid only when <see cref="HasImage"/>.</summary>
        public readonly ClipboardItem Image;

        public readonly bool HasImage;

        private readonly int plainIndex;
        private readonly int htmlIndex;

        public bool HasPlain => plainIndex >= 0;
        public ClipboardItem Plain => Texts[plainIndex];
        public bool HasHtml => htmlIndex >= 0;
        public ClipboardItem Html => Texts[htmlIndex];

        public bool IsEmpty => Texts.Count == 0 && !HasImage;

        private ClipboardWrite(List<ClipboardItem> texts, ClipboardItem image, bool hasImage,
            int plainIndex, int htmlIndex)
        {
            Texts = texts;
            Image = image;
            HasImage = hasImage;
            this.plainIndex = plainIndex;
            this.htmlIndex = htmlIndex;
        }

        public static ClipboardWrite Partition(IReadOnlyList<ClipboardItem> items)
        {
            var texts = new List<ClipboardItem>(items.Count);
            ClipboardItem image = default;
            bool hasImage = false;
            int plainIndex = -1, htmlIndex = -1, markdownIndex = -1;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Format.IsImage)
                {
                    if (!hasImage && item.Data.Length > 0)
                    {
                        image = item;
                        hasImage = true;
                    }
                    continue;
                }
                if (plainIndex < 0 && item.Format == ClipboardFormat.PlainText) plainIndex = texts.Count;
                else if (htmlIndex < 0 && item.Format == ClipboardFormat.Html) htmlIndex = texts.Count;
                else if (markdownIndex < 0 && item.Format == ClipboardFormat.Markdown) markdownIndex = texts.Count;
                texts.Add(item);
            }

            if (plainIndex < 0 && markdownIndex >= 0)
            {
                texts.Insert(0, new ClipboardItem(ClipboardFormat.PlainText, texts[markdownIndex].Data));
                plainIndex = 0;
                if (htmlIndex >= 0) htmlIndex++;
            }

            return new ClipboardWrite(texts, image, hasImage, plainIndex, htmlIndex);
        }
    }

    /// <summary>
    /// Default <see cref="IClipboardProvider"/> dispatching to the active platform's
    /// native clipboard helpers. Every platform class exposes the same static member
    /// shape, selected once by the <c>PlatformClipboard</c> alias at the top of this file
    /// — adding a platform is a one-file change and no per-method dispatch ladder can
    /// silently miss a branch. Formats a platform cannot carry return
    /// <see langword="null"/> / <see langword="false"/>. Cross-platform write policy
    /// (image slot, markdown-as-plain floor) is decided ONCE here via
    /// <see cref="ClipboardWrite.Partition"/> — platform classes never re-derive it.
    /// Selection is deliberately compile-time (unlike <see cref="UniTextNativeInput"/>'s
    /// runtime factory registry): clipboard calls are rare, must stay synchronous, and
    /// have no transient-init-failure mode to heal from — integrator override goes
    /// through <see cref="UniTextClipboard.Provider"/> instead.
    /// </summary>
    internal sealed class PlatformClipboardProvider : IClipboardProvider, IAsyncClipboardProvider, IMediaClipboardProvider
    {
        public bool HasContent() => PlatformClipboard.HasContent();

        public bool HasFormat(ClipboardFormat format)
            => format == ClipboardFormat.PlainText
                ? PlatformClipboard.HasText()
                : PlatformClipboard.HasFormat(format.Identifier);

        public string GetText(ClipboardFormat format)
            => format == ClipboardFormat.PlainText
                ? PlatformClipboard.GetText()
                : PlatformClipboard.GetFormat(format.Identifier);

        public bool TryGetText(ClipboardFormat format, out string text)
        {
            text = GetText(format);
            return !string.IsNullOrEmpty(text);
        }

        public byte[] GetData(ClipboardFormat format)
        {
            var data = PlatformClipboard.GetData(format.Identifier);
            if (data != null) return data;
            var text = GetText(format);
            return string.IsNullOrEmpty(text) ? null : Encoding.UTF8.GetBytes(text);
        }

        public void SetItems(IReadOnlyList<ClipboardItem> items)
        {
            if (items == null || items.Count == 0) return;

            var write = ClipboardWrite.Partition(items);
            if (write.IsEmpty) return;
            if (PlatformClipboard.SetItems(write)) return;

            if (write.HasPlain)
                PlatformClipboard.SetText(write.Plain.Text);
        }

        public Task<string> GetTextAsync(ClipboardFormat format)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ClipboardWebGL.GetTextAsync(format.Identifier);
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            return ClipboardLinux.HasNativeTool
                ? Task.Run(() => GetText(format))
                : Task.FromResult(GetText(format));
#else
            return Task.FromResult(GetText(format));
#endif
        }

        public Task<bool> HasFormatAsync(ClipboardFormat format)
        {
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            return ClipboardLinux.HasNativeTool
                ? Task.Run(() => HasFormat(format))
                : Task.FromResult(HasFormat(format));
#else
            return Task.FromResult(HasFormat(format));
#endif
        }

        public Task<IReadOnlyList<ClipboardItem>> GetItemsAsync(IReadOnlyList<ClipboardFormat> formats)
        {
            if (formats == null || formats.Count == 0)
                return Task.FromResult<IReadOnlyList<ClipboardItem>>(Array.Empty<ClipboardItem>());
#if UNITY_WEBGL && !UNITY_EDITOR
            return ClipboardWebGL.GetItemsAsync(formats);
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            return ClipboardLinux.HasNativeTool
                ? Task.Run(() => ReadItemsSync(formats))
                : Task.FromResult(ReadItemsSync(formats));
#else
            return Task.FromResult(ReadItemsSync(formats));
#endif
        }

        private IReadOnlyList<ClipboardItem> ReadItemsSync(IReadOnlyList<ClipboardFormat> formats)
        {
            var batch = PlatformClipboard.GetItems(formats);
            if (batch != null) return batch;

            var items = new List<ClipboardItem>(formats.Count);
            for (int i = 0; i < formats.Count; i++)
                if (HasFormat(formats[i]) && TryGetText(formats[i], out var text))
                    items.Add(new ClipboardItem(formats[i], text));
            return items;
        }

        public bool HasFiles() => PlatformClipboard.HasFiles();

        public string[] GetFiles() => PlatformClipboard.GetFiles();

        public byte[] ReadFile(string fileReference) => MediaFileReader.Read(fileReference);

        public Task<byte[]> ReadFileAsync(string fileReference) => MediaFileReader.ReadAsync(fileReference);

        public IReadOnlyList<ClipboardFormat> GetAvailableFormats()
        {
            var present = new List<ClipboardFormat>(probeFormats.Length);
            foreach (var f in probeFormats)
                if (HasFormat(f)) present.Add(f);
            return present;
        }

        private static readonly ClipboardFormat[] probeFormats =
        {
            ClipboardFormat.PlainText, ClipboardFormat.Html, ClipboardFormat.Markdown,
            ClipboardFormat.UniTextSource, ClipboardFormat.Url,
            ClipboardFormat.Png, ClipboardFormat.Jpeg, ClipboardFormat.Gif,
        };
    }

    /// <summary>
    /// Plain-text-only clipboard for platforms with no native backend (consoles, future
    /// targets) — <c>GUIUtility.systemCopyBuffer</c> is the only channel Unity guarantees
    /// there. Same static member shape as the platform classes.
    /// </summary>
    internal static class ClipboardFallback
    {
        public static string GetText() => GUIUtility.systemCopyBuffer;
        public static void SetText(string text) => GUIUtility.systemCopyBuffer = text ?? string.Empty;
        public static bool HasText() => !string.IsNullOrEmpty(GUIUtility.systemCopyBuffer);
        public static bool HasContent() => HasText();
        public static string GetFormat(string identifier) => null;
        public static bool HasFormat(string identifier) => false;
        public static bool SetItems(ClipboardWrite write) => false;
        public static IReadOnlyList<ClipboardItem> GetItems(IReadOnlyList<ClipboardFormat> formats) => null;
        public static byte[] GetData(string identifier) => null;
        public static bool HasFiles() => false;
        public static string[] GetFiles() => null;
    }
}
