#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

namespace LightSide
{
    /// <summary>Exact AFontMatcher cluster resolver for Android API 29+, with fonts.xml fallback below API 29.</summary>
    internal static class AndroidFontBackend
    {
        [DllImport("android")] private static extern IntPtr AFontMatcher_create();
        [DllImport("android")] private static extern void AFontMatcher_destroy(IntPtr matcher);
        [DllImport("android")] private static extern void AFontMatcher_setStyle(
            IntPtr matcher, ushort weight, [MarshalAs(UnmanagedType.I1)] bool italic);
        [DllImport("android")] private static extern void AFontMatcher_setLocales(
            IntPtr matcher, [MarshalAs(UnmanagedType.LPStr)] string languageTags);
        [DllImport("android")] private static extern IntPtr AFontMatcher_match(
            IntPtr matcher, [MarshalAs(UnmanagedType.LPStr)] string familyName,
            IntPtr text, uint textLength, out uint runLengthOut);
        [DllImport("android")] private static extern IntPtr AFont_getFontFilePath(IntPtr font);
        [DllImport("android")] private static extern UIntPtr AFont_getCollectionIndex(IntPtr font);
        [DllImport("android")] private static extern UIntPtr AFont_getAxisCount(IntPtr font);
        [DllImport("android")] private static extern uint AFont_getAxisTag(IntPtr font, uint axisIndex);
        [DllImport("android")] private static extern float AFont_getAxisValue(IntPtr font, uint axisIndex);
        [DllImport("android")] private static extern void AFont_close(IntPtr font);

        private static bool available = true;

        internal static bool TryResolve(string text, string language, string family,
            int requestWeight, bool requestItalic, out SystemFontSourceMatch match)
        {
            var offsets = new[] { 0, text?.Length ?? 0 };
            var matches = new SystemFontSourceMatch[1];
            if (!TryResolveBatch(text, offsets, 1, language, family,
                    requestWeight, requestItalic, matches))
            {
                match = default;
                return false;
            }
            match = matches[0];
            return !string.IsNullOrEmpty(match.descriptor)
                   && match.coveredUtf16Length >= text.Length;
        }

        internal static bool TryResolveBatch(string text, int[] offsets, int count,
            string language, string family, int requestWeight, bool requestItalic,
            SystemFontSourceMatch[] matches)
        {
            if (string.IsNullOrEmpty(text) || count <= 0) return false;
            if (!available)
                return AndroidFontsXmlResolver.TryResolveBatch(text, offsets, count, language,
                    family, requestWeight, requestItalic, matches);

            IntPtr matcher;
            try
            {
                matcher = AFontMatcher_create();
            }
            catch (EntryPointNotFoundException)
            {
                available = false;
                return AndroidFontsXmlResolver.TryResolveBatch(text, offsets, count, language,
                    family, requestWeight, requestItalic, matches);
            }

            if (matcher == IntPtr.Zero)
                return AndroidFontsXmlResolver.TryResolveBatch(text, offsets, count, language,
                    family, requestWeight, requestItalic, matches);

            GCHandle textHandle = default;
            try
            {
                var weight = Math.Clamp(requestWeight > 0 ? requestWeight : 400, 1, 1000);
                AFontMatcher_setStyle(matcher, (ushort)weight, requestItalic);
                if (!string.IsNullOrEmpty(language))
                    AFontMatcher_setLocales(matcher, language);

                var utf16 = text.ToCharArray();
                textHandle = GCHandle.Alloc(utf16, GCHandleType.Pinned);
                var position = 0;
                var requestIndex = 0;
                while (position < text.Length)
                {
                    var pointer = IntPtr.Add(textHandle.AddrOfPinnedObject(), position * 2);
                    var matchedFont = AFontMatcher_match(matcher,
                        string.IsNullOrEmpty(family) ? "sans-serif" : family,
                        pointer, (uint)(text.Length - position), out var runLength);
                    if (matchedFont == IntPtr.Zero) return false;
                    if (runLength == 0 || runLength > (uint)(text.Length - position))
                    {
                        AFont_close(matchedFont);
                        return false;
                    }

                    var runEnd = position + (int)runLength;
                    try
                    {
                        var pathPtr = AFont_getFontFilePath(matchedFont);
                        var path = pathPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(pathPtr) : null;
                        if (!string.IsNullOrEmpty(path))
                        {
                            var source = new SystemFontSourceMatch
                            {
                                descriptor = path,
                                faceIndex = checked((int)AFont_getCollectionIndex(matchedFont).ToUInt64()),
                                axes = ReadAxes(matchedFont),
                                coveredUtf16Length = (int)runLength,
                                scale = 1f,
                            };
                            FillCoveredRequests(offsets, count, position, runEnd,
                                in source, matches, ref requestIndex);
                        }
                    }
                    finally { AFont_close(matchedFont); }
                    position = runEnd;
                }
                return true;
            }
            finally
            {
                if (textHandle.IsAllocated) textHandle.Free();
                AFontMatcher_destroy(matcher);
            }
        }

        private static void FillCoveredRequests(int[] offsets, int count, int runStart, int runEnd,
            in SystemFontSourceMatch source, SystemFontSourceMatch[] matches, ref int index)
        {
            while (index < count && offsets[index + 1] <= runStart) index++;
            while (index < count && offsets[index] < runEnd)
            {
                if (offsets[index] >= runStart && offsets[index + 1] <= runEnd)
                {
                    var match = source;
                    match.coveredUtf16Length = offsets[index + 1] - offsets[index];
                    matches[index] = match;
                }
                index++;
            }
        }

        private static UniTextFont.AxisDefault[] ReadAxes(IntPtr font)
        {
            var count64 = AFont_getAxisCount(font).ToUInt64();
            if (count64 == 0 || count64 > 64) return null;
            var axes = new UniTextFont.AxisDefault[(int)count64];
            for (uint i = 0; i < (uint)axes.Length; i++)
            {
                axes[i] = new UniTextFont.AxisDefault
                {
                    tag = unchecked((int)AFont_getAxisTag(font, i)),
                    value = AFont_getAxisValue(font, i),
                };
            }
            return axes;
        }
    }
}
#endif
