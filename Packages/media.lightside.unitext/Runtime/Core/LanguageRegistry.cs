using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Process-wide registry mapping BCP 47 language tags to HarfBuzz language handles and
    /// compact byte indices suitable for per-codepoint attribute storage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HarfBuzz language handles (<c>hb_language_t</c>) are opaque pointers cached internally
    /// by HarfBuzz, so equal BCP 47 strings resolve to the same pointer. HarfBuzz maps BCP 47
    /// to OpenType language tags (e.g. <c>zh-Hans</c> → <c>ZHS</c>, <c>ja</c> → <c>JAN</c>)
    /// automatically, which drives the <c>locl</c> GSUB feature in pan-CJK fonts.
    /// </para>
    /// <para>
    /// The registry is additive: once a tag is registered, its index stays stable for the
    /// lifetime of the process. Index 0 is reserved for "unset" and maps to <c>HB_LANGUAGE_INVALID</c>.
    /// </para>
    /// </remarks>
    public static class LanguageRegistry
    {
        /// <summary>Index reserved for "no language set". Maps to <c>IntPtr.Zero</c>.</summary>
        public const byte Unset = 0;

        private const int MaxLanguages = 255;

        private static readonly Dictionary<string, byte> indexByTag = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<IntPtr> handleByIndex = new(8) { IntPtr.Zero };
        private static readonly List<string> tagByIndex = new(8) { string.Empty };
        private static readonly object syncRoot = new();

        /// <summary>
        /// Registers a BCP 47 language tag and returns its compact byte index.
        /// Returns <see cref="Unset"/> for null/empty tags or if the registry is full.
        /// </summary>
        /// <param name="bcp47">BCP 47 language tag (e.g. "zh-Hans", "ja", "ko").</param>
        public static byte Register(string bcp47)
        {
            if (string.IsNullOrWhiteSpace(bcp47))
                return Unset;

            lock (syncRoot)
            {
                if (indexByTag.TryGetValue(bcp47, out var existing))
                    return existing;

                if (handleByIndex.Count > MaxLanguages)
                    return Unset;

                var handle = HB.LanguageFromString(bcp47);
                var index = (byte)handleByIndex.Count;
                handleByIndex.Add(handle);
                tagByIndex.Add(bcp47);
                indexByTag[bcp47] = index;
                return index;
            }
        }

        /// <summary>Returns the HarfBuzz language handle for a registry index, or <see cref="IntPtr.Zero"/> if unset.</summary>
        public static IntPtr GetHandle(byte index)
        {
            if (index == Unset) return IntPtr.Zero;
            lock (syncRoot)
            {
                return index < handleByIndex.Count ? handleByIndex[index] : IntPtr.Zero;
            }
        }

        /// <summary>Returns the BCP 47 tag for a registry index, or empty string if unset.</summary>
        public static string GetTag(byte index)
        {
            if (index == Unset) return string.Empty;
            lock (syncRoot)
            {
                return index < tagByIndex.Count ? tagByIndex[index] : string.Empty;
            }
        }
    }
}
