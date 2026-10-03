#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LightSide
{
    /// <summary>
    /// macOS clipboard implementation using the Objective-C runtime directly via P/Invoke.
    /// Wraps <c>[NSPasteboard generalPasteboard]</c> with <c>NSPasteboardTypeString</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses <c>libobjc.dylib</c> to send Objective-C messages without requiring a compiled
    /// native plugin. This is a standard technique: <c>objc_getClass</c>, <c>sel_registerName</c>,
    /// and <c>objc_msgSend</c> are stable public APIs in macOS's Objective-C runtime.
    /// </para>
    /// <para>
    /// The <c>NSPasteboardTypeString</c> constant is a global <c>NSString*</c> exported by AppKit.
    /// It is loaded at first use via <c>dlsym</c> from <c>libAppKit</c>.
    /// </para>
    /// <para>
    /// Lifetime: <c>stringWithUTF8String:</c> returns an AUTORELEASED object and a P/Invoke
    /// caller cannot join the <c>objc_retainAutoreleasedReturnValue</c> handshake — Unity's
    /// player loop drains the main autorelease pool every iteration, so any NSString cached
    /// in a static MUST be retained at cache time (<see cref="CreateRetainedNSString"/>) or
    /// it dangles on the next frame. Transient strings that never outlive the call stay on
    /// the plain <see cref="CreateNSString"/> path.
    /// </para>
    /// </remarks>
    internal static class ClipboardMacOS
    {

        private const string ObjCLib = "/usr/lib/libobjc.dylib";
        private const string DlLib = "/usr/lib/libSystem.B.dylib";

        [DllImport(ObjCLib)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjCLib)] private static extern IntPtr sel_registerName(string name);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool objc_msgSend_bool_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool objc_msgSend_bool_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_NewData(IntPtr cls, IntPtr selector, IntPtr bytes, UIntPtr length);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        private static extern UIntPtr objc_msgSend_NUInt(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_UIntPtr(IntPtr receiver, IntPtr selector, UIntPtr arg1);

        [DllImport(DlLib)] private static extern IntPtr dlopen(string path, int mode);
        [DllImport(DlLib)] private static extern IntPtr dlsym(IntPtr handle, string symbol);

        private static IntPtr clsNSPasteboard;
        private static IntPtr clsNSString;

        private static IntPtr selGeneralPasteboard;
        private static IntPtr selClearContents;
        private static IntPtr selSetStringForType;
        private static IntPtr selStringForType;
        private static IntPtr selTypes;
        private static IntPtr selContainsObject;
        private static IntPtr selStringWithUTF8String;
        private static IntPtr selUTF8String;
        private static IntPtr selRetain;

        private static IntPtr clsNSData;
        private static IntPtr clsNSURL;
        private static IntPtr clsNSArray;
        private static IntPtr selDataWithBytesLength;
        private static IntPtr selBytes;
        private static IntPtr selLength;
        private static IntPtr selSetDataForType;
        private static IntPtr selDataForType;
        private static IntPtr selReadObjects;
        private static IntPtr selArrayWithObject;
        private static IntPtr selCount;
        private static IntPtr selObjectAtIndex;
        private static IntPtr selPath;
        private static IntPtr selIsFileURL;

        private static IntPtr nsPasteboardTypeString;

        private static bool initialized;

        private static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;

            clsNSPasteboard = objc_getClass("NSPasteboard");
            clsNSString = objc_getClass("NSString");

            selGeneralPasteboard = sel_registerName("generalPasteboard");
            selClearContents = sel_registerName("clearContents");
            selSetStringForType = sel_registerName("setString:forType:");
            selStringForType = sel_registerName("stringForType:");
            selTypes = sel_registerName("types");
            selContainsObject = sel_registerName("containsObject:");
            selStringWithUTF8String = sel_registerName("stringWithUTF8String:");
            selUTF8String = sel_registerName("UTF8String");
            selRetain = sel_registerName("retain");

            clsNSData = objc_getClass("NSData");
            clsNSURL = objc_getClass("NSURL");
            clsNSArray = objc_getClass("NSArray");
            selDataWithBytesLength = sel_registerName("dataWithBytes:length:");
            selBytes = sel_registerName("bytes");
            selLength = sel_registerName("length");
            selSetDataForType = sel_registerName("setData:forType:");
            selDataForType = sel_registerName("dataForType:");
            selReadObjects = sel_registerName("readObjectsForClasses:options:");
            selArrayWithObject = sel_registerName("arrayWithObject:");
            selCount = sel_registerName("count");
            selObjectAtIndex = sel_registerName("objectAtIndex:");
            selPath = sel_registerName("path");
            selIsFileURL = sel_registerName("isFileURL");

            nsPasteboardTypeString = ResolveAppKitType("NSPasteboardTypeString", "public.utf8-plain-text");
        }

        private static IntPtr CreateNSString(string str)
        {
            if (str == null) return IntPtr.Zero;

            IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(str);
            try
            {
                return objc_msgSend_IntPtr_IntPtr(clsNSString, selStringWithUTF8String, utf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8);
            }
        }

        /// <summary>
        /// For NSStrings cached in statics: the autoreleased factory result is retained so
        /// it survives the per-frame autorelease pool drain (see the class remarks).
        /// Intentionally never released — the cached UTIs are process-lifetime constants.
        /// </summary>
        private static IntPtr CreateRetainedNSString(string str)
        {
            IntPtr ns = CreateNSString(str);
            return ns == IntPtr.Zero ? IntPtr.Zero : objc_msgSend_IntPtr(ns, selRetain);
        }

        private static string NSStringToManaged(IntPtr nsString)
        {
            if (nsString == IntPtr.Zero) return null;

            IntPtr utf8Ptr = objc_msgSend_IntPtr(nsString, selUTF8String);
            if (utf8Ptr == IntPtr.Zero) return null;

            return Marshal.PtrToStringUTF8(utf8Ptr);
        }

        private static IntPtr GetGeneralPasteboard()
        {
            return objc_msgSend_IntPtr(clsNSPasteboard, selGeneralPasteboard);
        }

        public static string GetText()
        {
            EnsureInitialized();

            IntPtr pasteboard = GetGeneralPasteboard();
            if (pasteboard == IntPtr.Zero) return null;

            IntPtr nsStr = objc_msgSend_IntPtr_IntPtr(pasteboard, selStringForType, nsPasteboardTypeString);
            return NSStringToManaged(nsStr);
        }

        public static void SetText(string text)
        {
            EnsureInitialized();

            IntPtr pasteboard = GetGeneralPasteboard();
            if (pasteboard == IntPtr.Zero) return;

            objc_msgSend_void(pasteboard, selClearContents);

            if (string.IsNullOrEmpty(text))
                return;

            IntPtr nsText = CreateNSString(text);
            if (nsText == IntPtr.Zero) return;

            objc_msgSend_IntPtr_IntPtr_IntPtr(pasteboard, selSetStringForType, nsText, nsPasteboardTypeString);
        }

        public static bool HasText()
        {
            EnsureInitialized();

            IntPtr pasteboard = GetGeneralPasteboard();
            if (pasteboard == IntPtr.Zero) return false;

            IntPtr types = objc_msgSend_IntPtr(pasteboard, selTypes);
            if (types == IntPtr.Zero) return false;

            return objc_msgSend_bool_IntPtr(types, selContainsObject, nsPasteboardTypeString);
        }

        public static bool HasContent()
        {
            EnsureInitialized();
            IntPtr pasteboard = GetGeneralPasteboard();
            if (pasteboard == IntPtr.Zero) return false;
            IntPtr types = objc_msgSend_IntPtr(pasteboard, selTypes);
            return types != IntPtr.Zero && objc_msgSend_NUInt(types, selCount) != UIntPtr.Zero;
        }

        /// <summary>
        /// Reads a non-plain-text format from the general pasteboard. Maps the canonical
        /// MIME identifier on <see cref="ClipboardFormat"/> to the platform UTI
        /// (<c>text/html</c> → <c>public.html</c>; <c>text/markdown</c> →
        /// <c>net.daringfireball.markdown</c>). Returns <see langword="null"/> when the
        /// format is not present on the clipboard.
        /// </summary>
        public static string GetFormat(string formatIdentifier)
        {
            if (string.IsNullOrEmpty(formatIdentifier)) return null;

            EnsureInitialized();

            IntPtr pasteboard = GetGeneralPasteboard();
            if (pasteboard == IntPtr.Zero) return null;

            IntPtr uti = MapIdentifierToUti(formatIdentifier);
            if (uti == IntPtr.Zero) return null;

            IntPtr nsStr = objc_msgSend_IntPtr_IntPtr(pasteboard, selStringForType, uti);
            return NSStringToManaged(nsStr);
        }

        /// <summary>
        /// Multi-format write. Clears the general pasteboard once and then sets every
        /// item of the pre-partitioned write with its corresponding UTI via
        /// <c>setString:forType:</c> / <c>setData:forType:</c>. NSPasteboard allows
        /// multiple types per clipboard write — the consumer's paste picks the richest
        /// format it understands. <c>text/uri-list</c> maps to <c>public.url</c>, which
        /// semantically carries ONE URL — only the first line of a multi-URL list is
        /// written (Apple platforms have no multi-URL string UTI). Returns
        /// <see langword="true"/> only when at least one item landed, so the caller's
        /// plain-text fallback can salvage a total failure.
        /// </summary>
        public static bool SetItems(ClipboardWrite write)
        {
            EnsureInitialized();

            IntPtr pasteboard = GetGeneralPasteboard();
            if (pasteboard == IntPtr.Zero) return false;

            objc_msgSend_void(pasteboard, selClearContents);

            bool wroteAny = false;
            var texts = write.Texts;
            for (int i = 0; i < texts.Count; i++)
            {
                IntPtr uti = MapIdentifierToUti(texts[i].Format.Identifier);
                if (uti == IntPtr.Zero) continue;

                string text = texts[i].Text;
                if (texts[i].Format == ClipboardFormat.Url)
                    text = FirstLine(text);

                IntPtr nsText = CreateNSString(text);
                if (nsText == IntPtr.Zero) continue;

                wroteAny |= objc_msgSend_bool_IntPtr_IntPtr(pasteboard, selSetStringForType, nsText, uti);
            }

            if (write.HasImage)
            {
                IntPtr uti = MapIdentifierToUti(write.Image.Format.Identifier);
                IntPtr data = uti != IntPtr.Zero ? NSDataFromBytes(write.Image.Data) : IntPtr.Zero;
                if (data != IntPtr.Zero)
                    wroteAny |= objc_msgSend_bool_IntPtr_IntPtr(pasteboard, selSetDataForType, data, uti);
            }

            return wroteAny;
        }

        /// <summary>No batched read on this platform — per-format pasteboard reads are already one process-local call each.</summary>
        public static IReadOnlyList<ClipboardItem> GetItems(IReadOnlyList<ClipboardFormat> formats) => null;

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int lf = text.IndexOf('\n');
            if (lf < 0) return text;
            return text.Substring(0, lf).TrimEnd('\r');
        }

        private static IntPtr nsPasteboardTypeHtml;
        private static IntPtr nsPasteboardTypeMarkdown;
        private static IntPtr nsPasteboardTypeUrl;
        private static IntPtr nsPasteboardTypeUniTextSource;
        private static readonly System.Collections.Generic.Dictionary<string, IntPtr> customUtiCache
            = new(System.StringComparer.Ordinal);

        private static IntPtr MapIdentifierToUti(string identifier)
        {
            switch (identifier)
            {
                case "text/plain":
                    return nsPasteboardTypeString;
                case "text/html":
                    if (nsPasteboardTypeHtml == IntPtr.Zero)
                        nsPasteboardTypeHtml = ResolveAppKitType("NSPasteboardTypeHTML", "public.html");
                    return nsPasteboardTypeHtml;
                case "text/markdown":
                    if (nsPasteboardTypeMarkdown == IntPtr.Zero)
                        nsPasteboardTypeMarkdown = CreateRetainedNSString("net.daringfireball.markdown");
                    return nsPasteboardTypeMarkdown;
                case "text/uri-list":
                    if (nsPasteboardTypeUrl == IntPtr.Zero)
                        nsPasteboardTypeUrl = CreateRetainedNSString("public.url");
                    return nsPasteboardTypeUrl;
                case "application/vnd.lightside.unitext":
                    if (nsPasteboardTypeUniTextSource == IntPtr.Zero)
                        nsPasteboardTypeUniTextSource = CreateRetainedNSString("com.lightside.unitext");
                    return nsPasteboardTypeUniTextSource;
                case "image/png": return NsTypePng();
                case "image/jpeg": return NsTypeJpeg();
                case "image/gif": return NsTypeGif();
            }

            if (string.IsNullOrEmpty(identifier)) return IntPtr.Zero;
            if (customUtiCache.TryGetValue(identifier, out var cached) && cached != IntPtr.Zero)
                return cached;
            IntPtr uti = CreateRetainedNSString(identifier);
            if (uti != IntPtr.Zero) customUtiCache[identifier] = uti;
            return uti;
        }

        private static IntPtr nsTypePng, nsTypeJpeg, nsTypeGif, nsTypeTiff, nsTypeFileUrl;

        private static IntPtr NsTypePng() => nsTypePng != IntPtr.Zero ? nsTypePng : (nsTypePng = ResolveAppKitType("NSPasteboardTypePNG", "public.png"));
        private static IntPtr NsTypeJpeg() => nsTypeJpeg != IntPtr.Zero ? nsTypeJpeg : (nsTypeJpeg = CreateRetainedNSString("public.jpeg"));
        private static IntPtr NsTypeGif() => nsTypeGif != IntPtr.Zero ? nsTypeGif : (nsTypeGif = CreateRetainedNSString("com.compuserve.gif"));
        private static IntPtr NsTypeTiff() => nsTypeTiff != IntPtr.Zero ? nsTypeTiff : (nsTypeTiff = ResolveAppKitType("NSPasteboardTypeTIFF", "public.tiff"));
        private static IntPtr NsTypeFileUrl() => nsTypeFileUrl != IntPtr.Zero ? nsTypeFileUrl : (nsTypeFileUrl = ResolveAppKitType("NSPasteboardTypeFileURL", "public.file-url"));

        private static IntPtr appKitHandle;

        /// <summary>Resolves an AppKit-exported NSString* pasteboard-type constant by symbol, falling back to a retained literal UTI. The dlopen handle is resolved once and cached (AppKit is already loaded — this is a lookup, not a load).</summary>
        private static IntPtr ResolveAppKitType(string symbol, string fallbackUti)
        {
            if (appKitHandle == IntPtr.Zero)
                appKitHandle = dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", 0x10);
            if (appKitHandle != IntPtr.Zero)
            {
                IntPtr sym = dlsym(appKitHandle, symbol);
                if (sym != IntPtr.Zero)
                {
                    IntPtr value = Marshal.ReadIntPtr(sym);
                    if (value != IntPtr.Zero) return value;
                }
            }
            return CreateRetainedNSString(fallbackUti);
        }

        public static byte[] GetData(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            EnsureInitialized();
            IntPtr pb = GetGeneralPasteboard();
            if (pb == IntPtr.Zero) return null;
            IntPtr uti = MapIdentifierToUti(identifier);
            if (uti == IntPtr.Zero) return null;
            return NSDataToBytes(objc_msgSend_IntPtr_IntPtr(pb, selDataForType, uti));
        }

        /// <summary>Availability probe via <c>[pasteboard.types containsObject:]</c> — no payload transfer, any UTI (text or binary).</summary>
        public static bool HasFormat(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            EnsureInitialized();
            IntPtr pb = GetGeneralPasteboard();
            if (pb == IntPtr.Zero) return false;
            IntPtr uti = MapIdentifierToUti(identifier);
            if (uti == IntPtr.Zero) return false;
            IntPtr types = objc_msgSend_IntPtr(pb, selTypes);
            return types != IntPtr.Zero && objc_msgSend_bool_IntPtr(types, selContainsObject, uti);
        }

        public static bool HasFiles()
        {
            EnsureInitialized();
            IntPtr pb = GetGeneralPasteboard();
            if (pb == IntPtr.Zero) return false;
            IntPtr types = objc_msgSend_IntPtr(pb, selTypes);
            return types != IntPtr.Zero && objc_msgSend_bool_IntPtr(types, selContainsObject, NsTypeFileUrl());
        }

        /// <summary>
        /// File attachments only — web links are not files. <c>readObjectsForClasses:[NSURL]</c>
        /// also reads <c>public.url</c> entries (a copied Safari link would yield its path
        /// component as a bogus "file"), so every URL is filtered by <c>isFileURL</c>, matching
        /// the iOS plugin and the CF_HDROP / file-url semantics of this API. Returns
        /// <see langword="null"/> when no file URLs remain.
        /// </summary>
        public static string[] GetFiles()
        {
            EnsureInitialized();
            IntPtr pb = GetGeneralPasteboard();
            if (pb == IntPtr.Zero) return null;

            IntPtr classArray = objc_msgSend_IntPtr_IntPtr(clsNSArray, selArrayWithObject, clsNSURL);
            if (classArray == IntPtr.Zero) return null;
            IntPtr urls = objc_msgSend_IntPtr_IntPtr_IntPtr(pb, selReadObjects, classArray, IntPtr.Zero);
            if (urls == IntPtr.Zero) return null;

            int count = (int)objc_msgSend_NUInt(urls, selCount);
            if (count <= 0) return null;
            var result = new System.Collections.Generic.List<string>(count);
            for (int i = 0; i < count; i++)
            {
                IntPtr url = objc_msgSend_IntPtr_UIntPtr(urls, selObjectAtIndex, (UIntPtr)i);
                if (url == IntPtr.Zero || !objc_msgSend_bool(url, selIsFileURL)) continue;
                var path = NSStringToManaged(objc_msgSend_IntPtr(url, selPath));
                if (!string.IsNullOrEmpty(path)) result.Add(path);
            }
            return result.Count == 0 ? null : result.ToArray();
        }

        private static byte[] NSDataToBytes(IntPtr data)
        {
            if (data == IntPtr.Zero) return null;
            int len = (int)objc_msgSend_NUInt(data, selLength);
            if (len <= 0) return null;
            IntPtr src = objc_msgSend_IntPtr(data, selBytes);
            if (src == IntPtr.Zero) return null;
            var buf = new byte[len];
            Marshal.Copy(src, buf, 0, len);
            return buf;
        }

        private static IntPtr NSDataFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return IntPtr.Zero;
            var h = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return objc_msgSend_NewData(clsNSData, selDataWithBytesLength, h.AddrOfPinnedObject(), (UIntPtr)bytes.Length); }
            finally { h.Free(); }
        }
    }
}
#endif
