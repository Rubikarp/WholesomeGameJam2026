#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LightSide
{
    /// <summary>fontconfig cluster resolver carrying language, family, weight, slant and variation coordinates.</summary>
    internal static class LinuxFontBackend
    {
        private const string Fc = "libfontconfig.so.1";

        [DllImport(Fc)] private static extern IntPtr FcInitLoadConfigAndFonts();
        [DllImport(Fc)] private static extern void FcConfigDestroy(IntPtr config);
        [DllImport(Fc)] private static extern IntPtr FcCharSetCreate();
        [DllImport(Fc)] private static extern int FcCharSetAddChar(IntPtr cs, uint ucs4);
        [DllImport(Fc)] private static extern void FcCharSetDestroy(IntPtr cs);
        [DllImport(Fc)] private static extern IntPtr FcPatternCreate();
        [DllImport(Fc)] private static extern int FcPatternAddCharSet(IntPtr pat, [MarshalAs(UnmanagedType.LPStr)] string obj, IntPtr cs);
        [DllImport(Fc)] private static extern int FcPatternAddInteger(IntPtr pat, [MarshalAs(UnmanagedType.LPStr)] string obj, int value);
        [DllImport(Fc)] private static extern int FcPatternAddString(IntPtr pat, [MarshalAs(UnmanagedType.LPStr)] string obj, [MarshalAs(UnmanagedType.LPStr)] string value);
        [DllImport(Fc)] private static extern void FcPatternDestroy(IntPtr pat);
        [DllImport(Fc)] private static extern int FcWeightFromOpenType(int otWeight);
        [DllImport(Fc)] private static extern int FcConfigSubstitute(IntPtr config, IntPtr pat, int kind);
        [DllImport(Fc)] private static extern void FcDefaultSubstitute(IntPtr pat);
        [DllImport(Fc)] private static extern IntPtr FcFontSort(
            IntPtr config, IntPtr pat, int trim, IntPtr charset, out int result);
        [DllImport(Fc)] private static extern void FcFontSetDestroy(IntPtr set);
        [DllImport(Fc)] private static extern int FcPatternGetString(IntPtr pat, [MarshalAs(UnmanagedType.LPStr)] string obj, int id, out IntPtr str);
        [DllImport(Fc)] private static extern int FcPatternGetInteger(IntPtr pat, [MarshalAs(UnmanagedType.LPStr)] string obj, int id, out int value);
        [DllImport(Fc)] private static extern int FcPatternGetCharSet(IntPtr pat, [MarshalAs(UnmanagedType.LPStr)] string obj, int id, out IntPtr value);
        [DllImport(Fc)] private static extern int FcCharSetHasChar(IntPtr cs, uint ucs4);

        private const int FcMatchPattern = 0;
        private const int FcSlantRoman = 0;
        private const int FcSlantItalic = 100;

        private static int availability;
        private static IntPtr config;
        private static readonly object initLock = new();

        internal static bool TryResolve(string text, string language, string family,
            int requestWeight, bool requestItalic, out SystemFontSourceMatch match)
            => TryResolveRange(text, 0, text?.Length ?? 0, language, family,
                requestWeight, requestItalic, out match);

        internal static bool TryResolveBatch(string text, int[] offsets, int count,
            string language, string family, int requestWeight, bool requestItalic,
            SystemFontSourceMatch[] matches)
        {
            if (string.IsNullOrEmpty(text) || count <= 0 || !IsAvailable()) return false;

            var charset = FcCharSetCreate();
            if (charset == IntPtr.Zero) return false;
            AddCharacters(charset, text, 0, text.Length);
            var pattern = CreatePattern(charset, language, family, requestWeight, requestItalic);
            if (pattern == IntPtr.Zero)
            {
                FcCharSetDestroy(charset);
                return false;
            }

            var sorted = FcFontSort(config, pattern, 0, IntPtr.Zero, out _);
            SystemFontSourceMatch[] candidateMatches = null;
            byte[] candidateStates = null;
            try
            {
                if (sorted == IntPtr.Zero) return false;
                var set = Marshal.PtrToStructure<FcFontSet>(sorted);
                var candidateCount = Math.Max(1, set.nfont);
                candidateMatches = ArrayPool<SystemFontSourceMatch>.Rent(candidateCount);
                candidateStates = ArrayPool<byte>.Rent(candidateCount);
                Array.Clear(candidateStates, 0, candidateCount);
                for (var requestIndex = 0; requestIndex < count; requestIndex++)
                {
                    var start = offsets[requestIndex];
                    var length = offsets[requestIndex + 1] - start;
                    for (var fontIndex = 0; fontIndex < set.nfont; fontIndex++)
                    {
                        var candidate = Marshal.ReadIntPtr(set.fonts, fontIndex * IntPtr.Size);
                        if (!PatternCovers(candidate, text, start, length)) continue;
                        if (candidateStates[fontIndex] == 0)
                        {
                            candidateStates[fontIndex] = TryReadMatch(candidate, 0,
                                out candidateMatches[fontIndex]) ? (byte)2 : (byte)1;
                        }
                        if (candidateStates[fontIndex] != 2) continue;
                        var match = candidateMatches[fontIndex];
                        match.coveredUtf16Length = length;
                        matches[requestIndex] = match;
                        break;
                    }
                }
                return true;
            }
            finally
            {
                if (candidateMatches != null)
                    ArrayPool<SystemFontSourceMatch>.Return(candidateMatches);
                if (candidateStates != null) ArrayPool<byte>.Return(candidateStates);
                if (sorted != IntPtr.Zero) FcFontSetDestroy(sorted);
                FcPatternDestroy(pattern);
                FcCharSetDestroy(charset);
            }
        }

        private static bool TryResolveRange(string text, int textStart, int textLength,
            string language, string family, int requestWeight, bool requestItalic,
            out SystemFontSourceMatch match)
        {
            match = default;
            if (string.IsNullOrEmpty(text) || textLength <= 0 || !IsAvailable()) return false;

            var charset = FcCharSetCreate();
            if (charset == IntPtr.Zero) return false;
            AddCharacters(charset, text, textStart, textLength);
            var pattern = CreatePattern(charset, language, family, requestWeight, requestItalic);
            if (pattern == IntPtr.Zero)
            {
                FcCharSetDestroy(charset);
                return false;
            }

            var sorted = FcFontSort(config, pattern, 0, IntPtr.Zero, out _);
            try
            {
                if (sorted == IntPtr.Zero) return false;
                var set = Marshal.PtrToStructure<FcFontSet>(sorted);
                for (var i = 0; i < set.nfont; i++)
                {
                    var candidate = Marshal.ReadIntPtr(set.fonts, i * IntPtr.Size);
                    if (PatternCovers(candidate, text, textStart, textLength)
                        && TryReadMatch(candidate, textLength, out match)) return true;
                }
                return false;
            }
            finally
            {
                if (sorted != IntPtr.Zero) FcFontSetDestroy(sorted);
                FcPatternDestroy(pattern);
                FcCharSetDestroy(charset);
            }
        }

        private static void AddCharacters(IntPtr charset, string text, int start, int length)
        {
            var end = start + length;
            for (var offset = start; offset < end;)
            {
                var codepoint = char.ConvertToUtf32(text, offset);
                offset += char.IsSurrogatePair(text, offset) ? 2 : 1;
                if (!UnicodeData.IsDefaultIgnorable(codepoint))
                    FcCharSetAddChar(charset, (uint)codepoint);
            }
        }

        private static IntPtr CreatePattern(IntPtr charset, string language, string family,
            int requestWeight, bool requestItalic)
        {
            var pattern = FcPatternCreate();
            if (pattern == IntPtr.Zero) return IntPtr.Zero;
            FcPatternAddCharSet(pattern, "charset", charset);
            if (!string.IsNullOrEmpty(family)) FcPatternAddString(pattern, "family", family);
            if (!string.IsNullOrEmpty(language)) FcPatternAddString(pattern, "lang", language);
            var weight = requestWeight > 0 ? requestWeight : 400;
            FcPatternAddInteger(pattern, "weight", FcWeightFromOpenType(weight));
            FcPatternAddInteger(pattern, "slant", requestItalic ? FcSlantItalic : FcSlantRoman);
            FcConfigSubstitute(config, pattern, FcMatchPattern);
            FcDefaultSubstitute(pattern);
            return pattern;
        }

        private static bool TryReadMatch(IntPtr pattern, int coveredUtf16Length,
            out SystemFontSourceMatch match)
        {
            match = default;
            if (pattern == IntPtr.Zero
                || FcPatternGetString(pattern, "file", 0, out var filePtr) != 0
                || filePtr == IntPtr.Zero)
                return false;

            var path = Marshal.PtrToStringAnsi(filePtr);
            var faceIndex = FcPatternGetInteger(pattern, "index", 0, out var index) == 0
                ? index
                : 0;
            var axes = TryGetString(pattern, "fontvariations", out var variations)
                ? ParseAxes(variations)
                : null;
            match = new SystemFontSourceMatch
            {
                descriptor = path,
                faceIndex = faceIndex,
                axes = axes,
                coveredUtf16Length = coveredUtf16Length,
                scale = 1f,
            };
            return true;
        }

        private static bool PatternCovers(IntPtr pattern, string text, int start, int length)
        {
            if (FcPatternGetCharSet(pattern, "charset", 0, out var charset) != 0
                || charset == IntPtr.Zero) return false;
            var end = start + length;
            for (var offset = start; offset < end;)
            {
                var codepoint = char.ConvertToUtf32(text, offset);
                offset += char.IsSurrogatePair(text, offset) ? 2 : 1;
                if (!UnicodeData.IsDefaultIgnorable(codepoint)
                    && FcCharSetHasChar(charset, (uint)codepoint) == 0)
                    return false;
            }
            return true;
        }

        private static bool TryGetString(IntPtr pattern, string property, out string value)
        {
            value = null;
            if (FcPatternGetString(pattern, property, 0, out var ptr) != 0 || ptr == IntPtr.Zero)
                return false;
            value = Marshal.PtrToStringAnsi(ptr);
            return !string.IsNullOrEmpty(value);
        }

        private static UniTextFont.AxisDefault[] ParseAxes(string variations)
        {
            if (string.IsNullOrEmpty(variations)) return null;
            var parsed = new List<UniTextFont.AxisDefault>();
            var parts = variations.Split(',');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                var equals = part.IndexOf('=');
                if (equals != 4 || !float.TryParse(part.Substring(equals + 1),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    continue;
                var tag = part;
                parsed.Add(new UniTextFont.AxisDefault
                {
                    tag = tag[0] << 24 | tag[1] << 16 | tag[2] << 8 | tag[3],
                    value = value,
                });
            }
            return parsed.Count > 0 ? parsed.ToArray() : null;
        }

        private static bool IsAvailable()
        {
            if (availability != 0) return availability > 0;
            lock (initLock)
            {
                if (availability != 0) return availability > 0;
                try
                {
                    config = FcInitLoadConfigAndFonts();
                    availability = config != IntPtr.Zero ? 1 : -1;
                }
                catch (DllNotFoundException)
                {
                    availability = -1;
                }
                catch (EntryPointNotFoundException)
                {
                    availability = -1;
                }
                return availability > 0;
            }
        }

        internal static void Shutdown()
        {
            lock (initLock)
            {
                if (config != IntPtr.Zero) FcConfigDestroy(config);
                config = IntPtr.Zero;
                availability = 0;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FcFontSet
        {
            internal int nfont;
            internal int sfont;
            internal IntPtr fonts;
        }
    }
}
#endif
