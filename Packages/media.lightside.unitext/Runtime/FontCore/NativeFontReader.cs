#if (UNITY_IOS && !UNITY_EDITOR) || UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace LightSide
{
    /// <summary>
    /// Apple font reader and emoji renderer using Core Text/Core Graphics API.
    /// Required because:
    /// 1. Core Text fonts do not always expose a readable file URL
    /// 2. Apple Color Emoji uses proprietary 'emjc' format (LZFSE compression)
    ///    which FreeType cannot decode - must use Core Text for rendering
    /// 3. macOS 15+ ships faces whose outlines live in the 'hvgl' table, which no
    ///    file-based loader can rasterize
    /// </summary>
    internal static class NativeFontReader
    {
        private const int ExpectedAbiVersion = 3;

        /// <summary>Name the reader's exports are bound through: statically linked into the iOS player, a dynamic library on macOS. Both are built from the same source.</summary>
        private const string Library =
#if UNITY_IOS && !UNITY_EDITOR
            "__Internal";
#else
            "UniTextSystemFontMacOS";
#endif

        private const bool TolerateUnavailableSystemFontMatches =
#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
            true;
#else
            false;
#endif

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFaceInfo
        {
            internal int unitsPerEm;
            internal int lineHeight;
            internal int ascentLine;
            internal int capLine;
            internal int meanLine;
            internal int descentLine;
            internal int typoAscent;
            internal int typoDescent;
            internal int typoLineGap;
            internal int winAscent;
            internal int winDescent;
            internal int useTypoMetrics;
            internal int superscriptOffset;
            internal int superscriptSize;
            internal int subscriptOffset;
            internal int subscriptSize;
            internal int underlineOffset;
            internal int underlineThickness;
            internal int strikethroughOffset;
            internal int strikethroughThickness;
            internal int tabWidth;
            internal int weightClass;
            internal int isItalic;

            internal FaceInfo ToFaceInfo(string family, string style) => new()
            {
                faceIndex = 0,
                familyName = family,
                styleName = style,
                unitsPerEm = unitsPerEm,
                lineHeight = lineHeight,
                ascentLine = ascentLine,
                capLine = capLine,
                meanLine = meanLine,
                descentLine = descentLine,
                typoAscent = typoAscent,
                typoDescent = typoDescent,
                typoLineGap = typoLineGap,
                winAscent = winAscent,
                winDescent = winDescent,
                useTypoMetrics = useTypoMetrics != 0,
                superscriptOffset = superscriptOffset,
                superscriptSize = superscriptSize,
                subscriptOffset = subscriptOffset,
                subscriptSize = subscriptSize,
                underlineOffset = underlineOffset,
                underlineThickness = underlineThickness,
                strikethroughOffset = strikethroughOffset,
                strikethroughThickness = strikethroughThickness,
                tabWidth = tabWidth,
                weightClass = weightClass,
                isItalic = isItalic != 0,
            };
        }

        private sealed unsafe class CoreTextOutlineSource : IGlyphOutlineSource
        {
            private sealed class SharedState
            {
                internal readonly object gate = new();
                internal readonly FaceInfo faceInfo;
                internal IntPtr font;
                internal int references = 1;

                internal SharedState(IntPtr font, FaceInfo faceInfo)
                {
                    this.font = font;
                    this.faceInfo = faceInfo;
                }
            }

            private SharedState state;

            internal CoreTextOutlineSource(IntPtr font, FaceInfo faceInfo)
            {
                if (font == IntPtr.Zero) throw new ArgumentException("A CoreText outline source requires a font.", nameof(font));
                state = new SharedState(font, faceInfo);
            }

            private CoreTextOutlineSource(SharedState state) => this.state = state;

            public FaceInfo FaceInfo => state?.faceInfo
                ?? throw new ObjectDisposedException(nameof(CoreTextOutlineSource));

            public IGlyphOutlineSource Retain()
            {
                var current = state ?? throw new ObjectDisposedException(nameof(CoreTextOutlineSource));
                lock (current.gate)
                {
                    if (current.font == IntPtr.Zero)
                        throw new ObjectDisposedException(nameof(CoreTextOutlineSource));
                    current.references++;
                    return new CoreTextOutlineSource(current);
                }
            }

            public IntPtr RentFace(int[] axisTags, int[] coordinates)
            {
                var current = state ?? throw new ObjectDisposedException(nameof(CoreTextOutlineSource));
                var hasCoordinates = coordinates is { Length: > 0 };
                if (hasCoordinates && (axisTags == null || axisTags.Length != coordinates.Length))
                    throw new InvalidOperationException("CoreText variation coordinates do not match the font axes.");

                lock (current.gate)
                {
                    if (current.font == IntPtr.Zero)
                        throw new ObjectDisposedException(nameof(CoreTextOutlineSource));
                    if (!hasCoordinates)
                    {
                        var retained = UniText_RetainSystemFont(current.font);
                        if (retained == IntPtr.Zero)
                            throw new InvalidOperationException("CoreText failed to retain a system-font face.");
                        return retained;
                    }

                    var font = UniText_CreateSystemFontVariation(current.font,
                        axisTags, coordinates, coordinates.Length);
                    if (font == IntPtr.Zero)
                        throw new InvalidOperationException("CoreText failed to create a system-font variation.");
                    return font;
                }
            }

            public void ReturnFace(IntPtr face)
            {
                if (face != IntPtr.Zero) UniText_ReleaseSystemFont(face);
            }

            public int Decompose(IntPtr face, uint glyphIndex,
                float* curves, int* types, int* curveCount, int maxCurves,
                int* contours, int* contourCount, int maxContours,
                out int bearingX, out int bearingY, out int advanceX,
                out int width, out int height)
                => UniText_DecomposeSystemFontGlyph(face, glyphIndex,
                    curves, types, curveCount, maxCurves,
                    contours, contourCount, maxContours,
                    out bearingX, out bearingY, out advanceX, out width, out height);

            public void Dispose()
            {
                var current = Interlocked.Exchange(ref state, null);
                if (current == null) return;
                GC.SuppressFinalize(this);
                lock (current.gate)
                {
                    current.references--;
                    if (current.references != 0) return;
                    if (current.font != IntPtr.Zero) UniText_ReleaseSystemFont(current.font);
                    current.font = IntPtr.Zero;
                }
            }

            ~CoreTextOutlineSource() => Dispose();
        }

        [DllImport(Library)]
        private static extern int UniText_GetSystemFontReaderAbiVersion();

        [DllImport(Library)]
        private static extern IntPtr UniText_OpenSystemEmojiFace(out NativeFaceInfo info,
            out IntPtr identity, out IntPtr family, out IntPtr style);

        [DllImport(Library)]
        private static extern IntPtr UniText_RetainSystemEmojiFace(IntPtr face);

        [DllImport(Library)]
        private static extern void UniText_ReleaseSystemEmojiFace(IntPtr face);

        [DllImport(Library)]
        private static extern int UniText_GetSystemEmojiGlyph(IntPtr face, uint codepoint,
            out uint glyphIndex, out int advance);

        [DllImport(Library)]
        private static extern int UniText_GetSystemEmojiGlyphAdvance(IntPtr face,
            uint glyphIndex, out int advance);

        [DllImport(Library)]
        private static extern unsafe int UniText_ShapeSystemEmojiRun(IntPtr face,
            int* codepoints, int codepointCount,
            uint* outGlyphIds, int* outAdvances, int* outClusters,
            int maxOutput);

        [DllImport(Library)]
        private static extern int UniText_RenderSystemEmojiGlyph(IntPtr face,
            uint glyphIndex, int pixelSize,
            out IntPtr outPixels, out int outWidth, out int outHeight,
            out int outBearingX, out int outBearingY, out float outAdvance);

        [DllImport(Library)]
        private static unsafe extern IntPtr UniText_ResolveSystemFontBatch(
            char* text, int textLength,
            [In] int[] offsets, int count,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string language,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string family,
            int requestWeight, int requestItalic);

#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
        [DllImport(Library)]
        private static unsafe extern IntPtr UniText_ResolveSystemFontRuns(
            char* text, int textLength,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string language,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string family,
            int requestWeight, int requestItalic);

        [DllImport(Library)]
        private static extern int UniText_GetSystemFontRunRange(IntPtr handle, int index,
            out int utf16Start, out int utf16Length);

        [DllImport(Library)]
        private static unsafe extern IntPtr UniText_ResolveNamedSystemFont(
            char* text, int textLength,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string postScriptName);
#endif

        [DllImport(Library)]
        private static extern int UniText_GetSystemFontBatchCount(IntPtr handle);

        [DllImport(Library)]
        private static extern int UniText_GetSystemFontBatchSource(IntPtr handle, int index,
            out IntPtr sourceKey, out IntPtr postScriptName, out IntPtr filePath,
            out int axisCount, out int usesCoreTextOutlines);

        [DllImport(Library)]
        private static extern int UniText_GetSystemFontBatchAxis(IntPtr handle, int matchIndex,
            int axisIndex, out int tag, out float value);

        [DllImport(Library)]
        private static extern int UniText_WriteSystemFontBatchSfnt(IntPtr handle, int matchIndex,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out long length);

        [DllImport(Library)]
        private static extern int UniText_GetSystemFontBatchFaceInfo(IntPtr handle, int matchIndex,
            out NativeFaceInfo info, out IntPtr family, out IntPtr style);

        [DllImport(Library)]
        private static extern IntPtr UniText_CreateSystemFontBatchOutlineFace(IntPtr handle, int matchIndex);

        [DllImport(Library)]
        private static extern IntPtr UniText_CreateSystemFontVariation(IntPtr font,
            [In] int[] tags, [In] int[] coordinates, int count);

        [DllImport(Library)]
        private static extern void UniText_ReleaseSystemFont(IntPtr font);

        [DllImport(Library)]
        private static extern IntPtr UniText_RetainSystemFont(IntPtr font);

        [DllImport(Library)]
        private static extern unsafe int UniText_DecomposeSystemFontGlyph(IntPtr font, uint glyphIndex,
            float* curves, int* types, int* curveCount, int maxCurves,
            int* contours, int* contourCount, int maxContours,
            out int bearingX, out int bearingY, out int advanceX,
            out int width, out int height);

        [DllImport(Library)]
        private static extern void UniText_ReleaseSystemFontBatch(IntPtr handle);

        [ThreadStatic] private static int[] singleOffsets;
        [ThreadStatic] private static SystemFontSourceMatch[] singleMatches;

        [DllImport(Library)]
        private static extern void UniText_FreeBuffer(IntPtr buffer);

        [DllImport(Library)]
        private static extern void UniText_TrimFontCaches();

        private static readonly string reconstructedFontDirectory = Path.Combine(
            Path.GetTempPath(), "UniText", "CoreText");

        static NativeFontReader()
        {
            var version = UniText_GetSystemFontReaderAbiVersion();
            if (version != ExpectedAbiVersion)
                throw new InvalidOperationException(
                    $"The Apple system-font plugin ABI is {version}; UniText requires {ExpectedAbiVersion}.");
            UnityEngine.Application.lowMemory += TrimCaches;
        }

        internal static void TrimCaches()
        {
            UniText_TrimFontCaches();
        }

        internal static bool TryResolveSystemFont(string text, string language, string family,
            int requestWeight, bool requestItalic, out SystemFontSourceMatch match)
        {
            match = default;
            if (string.IsNullOrEmpty(text)) return false;
            var offsets = singleOffsets ??= new int[2];
            var matches = singleMatches ??= new SystemFontSourceMatch[1];
            offsets[0] = 0;
            offsets[1] = text.Length;
            matches[0] = default;
            if (!TryResolveSystemFontBatch(text, offsets, 1, language, family,
                    requestWeight, requestItalic, matches)
                || string.IsNullOrEmpty(matches[0].descriptor))
                return false;
            match = matches[0];
            matches[0] = default;
            return true;
        }

        internal static unsafe bool TryResolveSystemFontBatch(string text, int[] offsets, int count,
            string language, string family, int requestWeight, bool requestItalic,
            SystemFontSourceMatch[] matches)
        {
            if (string.IsNullOrEmpty(text) || count <= 0) return false;
            IntPtr handle;
            fixed (char* utf16 = text)
                handle = UniText_ResolveSystemFontBatch(utf16, text.Length, offsets, count,
                    language, family, requestWeight, requestItalic ? 1 : 0);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("CoreText failed to resolve system fonts.");
            try
            {
                for (var i = 0; i < count; i++)
                {
                    TryReadSystemFontBatchMatch(handle, i, offsets[i + 1] - offsets[i],
                        requestWeight, requestItalic, out matches[i],
                        tolerateUnavailableMatch: TolerateUnavailableSystemFontMatches);
                }
                return true;
            }
            finally
            {
                UniText_ReleaseSystemFontBatch(handle);
            }
        }

#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
        internal static unsafe SystemFontRuns OpenSystemFontRuns(string text, string language, string family,
            int requestWeight, bool requestItalic)
        {
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("Text cannot be empty.", nameof(text));
            IntPtr handle;
            fixed (char* utf16 = text)
                handle = UniText_ResolveSystemFontRuns(utf16, text.Length, language, family,
                    requestWeight, requestItalic ? 1 : 0);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("CoreText failed to resolve system-font runs.");
            try
            {
                var count = UniText_GetSystemFontBatchCount(handle);
                if (count <= 0 || count > text.Length)
                    throw new InvalidOperationException("CoreText returned an invalid system-font run count.");
                return new SystemFontRuns(handle, count, requestWeight, requestItalic);
            }
            catch
            {
                UniText_ReleaseSystemFontBatch(handle);
                throw;
            }
        }

        internal static unsafe bool TryResolveNamedSystemFont(string text, string postScriptName,
            int requestWeight, bool requestItalic, out SystemFontSourceMatch match)
        {
            match = default;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(postScriptName)) return false;
            IntPtr handle;
            fixed (char* utf16 = text)
                handle = UniText_ResolveNamedSystemFont(utf16, text.Length, postScriptName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("CoreText failed to resolve a named system font.");
            try
            {
                if (UniText_GetSystemFontBatchCount(handle) != 1)
                    throw new InvalidOperationException("CoreText returned an invalid named-font result.");
                return TryReadSystemFontBatchMatch(handle, 0, text.Length,
                    requestWeight, requestItalic, out match, tolerateUnavailableMatch: true);
            }
            finally
            {
                UniText_ReleaseSystemFontBatch(handle);
            }
        }

        internal static bool TryResolveSystemEmojiFont(out FontSource fontSource,
            out int faceIndex, out string postScriptName)
        {
            fontSource = null;
            faceIndex = -1;
            postScriptName = null;
            if (!TryResolveSystemFont(char.ConvertFromUtf32(0x1F600),
                    "und-Zsye", "Apple Color Emoji", 400, false, out var match))
                return false;
            try
            {
                if (match.fontSource == null || match.faceIndex < 0) return false;
                fontSource = match.fontSource;
                faceIndex = match.faceIndex;
                postScriptName = match.postScriptName;
                return true;
            }
            finally { match.glyphOutlineSource?.Dispose(); }
        }

        internal static string GetScriptFontCandidate(UnicodeScript script, int index)
        {
            if (script == UnicodeScript.Han)
                return index switch
                {
                    0 => "HiraginoSansGB-W3",
                    1 => "NomNaTong-Regular",
                    2 => "PingFangSC-Regular",
                    3 => "PingFangTC-Regular",
                    4 => "PingFangHK-Regular",
                    5 => "HiraginoSansTC-W3",
                    _ => null,
                };
            var stem = script switch
            {
                UnicodeScript.Ahom => "NotoSerifAhom",
                UnicodeScript.Balinese => "NotoSerifBalinese",
                UnicodeScript.NyiakengPuachueHmong => "NotoSerifHmongNyiakeng",
                UnicodeScript.Yezidi => "NotoSerifYezidi",
                UnicodeScript.MeroiticCursive => "NotoSansMeroitic",
                UnicodeScript.MeroiticHieroglyphs => "NotoSansMeroitic",
                UnicodeScript.Nko => "NotoSansNKo",
                _ => (byte)script > (byte)UnicodeScript.Inherited ? $"NotoSans{script}" : null,
            };
            return index switch
            {
                0 when stem != null => $"{stem}-Regular",
                1 => stem,
                _ => null,
            };
        }
#endif

        private static bool TryReadSystemFontBatchMatch(IntPtr handle, int index,
            int coveredUtf16Length, int requestWeight, bool requestItalic,
            out SystemFontSourceMatch match, bool tolerateUnavailableMatch = false)
        {
            match = default;
            var matchResult = UniText_GetSystemFontBatchSource(handle, index,
                out var sourceKeyPointer, out var postScriptNamePointer, out var filePathPointer,
                out var axisCount, out var usesCoreTextOutlines);
            if (matchResult < 0)
            {
                if (tolerateUnavailableMatch) return false;
                throw new InvalidOperationException("CoreText failed to materialize a matched system font.");
            }
            if (matchResult == 0) return false;
            if (sourceKeyPointer == IntPtr.Zero || postScriptNamePointer == IntPtr.Zero
                || axisCount < 0 || axisCount > 64
                || (usesCoreTextOutlines != 0 && usesCoreTextOutlines != 1))
                throw new InvalidOperationException("CoreText returned an invalid system-font match.");

            var sourceKey = Marshal.PtrToStringUTF8(sourceKeyPointer);
            var postScriptName = Marshal.PtrToStringUTF8(postScriptNamePointer);
            var filePath = filePathPointer == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUTF8(filePathPointer);
            if (string.IsNullOrEmpty(sourceKey) || string.IsNullOrEmpty(postScriptName))
                throw new InvalidOperationException("CoreText returned a system font without an identity.");
            if (filePathPointer != IntPtr.Zero && string.IsNullOrEmpty(filePath))
                throw new InvalidOperationException("CoreText returned an invalid system-font file path.");

            var materializedSourceKey = sourceKey;
            FontSource fontSource = null;
            var faceIndex = -1;
            if (!string.IsNullOrEmpty(filePath))
            {
                fontSource = SystemFontByteCache.GetOrAdd(sourceKey,
                    _ => TryOpenCoreTextFile(filePath));
                if (fontSource == null
                    || !SystemFontFaces.TryFindExactPostScriptFace(fontSource,
                        postScriptName, out faceIndex))
                    fontSource = null;
            }

            if (fontSource == null)
            {
                materializedSourceKey = $"coretext-sfnt:{sourceKey.Length}:{sourceKey}{postScriptName}";
                fontSource = SystemFontByteCache.GetOrAdd(materializedSourceKey,
                    _ => ReconstructSystemFontSource(handle, index));
                faceIndex = 0;
            }
            if (fontSource == null || fontSource.Length == 0)
            {
                if (tolerateUnavailableMatch) return false;
                throw new InvalidOperationException(
                    $"CoreText failed to expose shaping tables for system font '{postScriptName}'.");
            }

            UniTextFont.AxisDefault[] axes = null;
            if (axisCount > 0)
            {
                axes = new UniTextFont.AxisDefault[axisCount];
                for (var axisIndex = 0; axisIndex < axisCount; axisIndex++)
                {
                    if (UniText_GetSystemFontBatchAxis(handle, index, axisIndex,
                            out var tag, out var value) == 0)
                        throw new InvalidOperationException(
                            $"CoreText returned incomplete variation data for system font '{postScriptName}'.");
                    axes[axisIndex] = new UniTextFont.AxisDefault { tag = tag, value = value };
                }
            }

            CoreTextOutlineSource outlineSource = null;
            if (usesCoreTextOutlines != 0)
            {
                if (UniText_GetSystemFontBatchFaceInfo(handle, index,
                        out var nativeInfo, out var familyPointer, out var stylePointer) == 0)
                    throw new InvalidOperationException(
                        $"CoreText failed to provide metrics for system font '{postScriptName}'.");
                var outlineFace = UniText_CreateSystemFontBatchOutlineFace(handle, index);
                if (outlineFace == IntPtr.Zero)
                    throw new InvalidOperationException(
                        $"CoreText failed to retain the outline face for system font '{postScriptName}'.");
                var faceInfo = nativeInfo.ToFaceInfo(
                    Marshal.PtrToStringUTF8(familyPointer),
                    Marshal.PtrToStringUTF8(stylePointer));
                faceInfo.faceIndex = faceIndex;
                outlineSource = new CoreTextOutlineSource(outlineFace, faceInfo);
            }

            match = new SystemFontSourceMatch
            {
                descriptor = materializedSourceKey,
                postScriptName = postScriptName,
                fontSource = fontSource,
                glyphOutlineSource = outlineSource,
                faceIndex = faceIndex,
                requestedWeight = requestWeight > 0 ? requestWeight : 400,
                requestedItalic = requestItalic,
                axes = axes,
                coveredUtf16Length = coveredUtf16Length,
                scale = 1f,
            };
            return true;
        }

#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
        internal sealed class SystemFontRuns : IDisposable
        {
            private IntPtr handle;
            private readonly int requestWeight;
            private readonly bool requestItalic;

            internal int Count { get; }

            internal SystemFontRuns(IntPtr handle, int count, int requestWeight, bool requestItalic)
            {
                this.handle = handle;
                Count = count;
                this.requestWeight = requestWeight;
                this.requestItalic = requestItalic;
            }

            internal void GetRange(int index, out int utf16Start, out int utf16Length)
            {
                var current = handle;
                if (current == IntPtr.Zero)
                    throw new ObjectDisposedException(nameof(SystemFontRuns));
                if (UniText_GetSystemFontRunRange(current, index,
                        out utf16Start, out utf16Length) == 0
                    || utf16Start < 0 || utf16Length <= 0)
                    throw new InvalidOperationException("CoreText returned an invalid system-font run range.");
            }

            internal bool TryRead(int index, int utf16Length, out SystemFontSourceMatch match)
            {
                var current = handle;
                if (current == IntPtr.Zero)
                    throw new ObjectDisposedException(nameof(SystemFontRuns));
                return TryReadSystemFontBatchMatch(current, index, utf16Length,
                    requestWeight, requestItalic, out match, tolerateUnavailableMatch: true);
            }

            public void Dispose()
            {
                var current = Interlocked.Exchange(ref handle, IntPtr.Zero);
                if (current != IntPtr.Zero) UniText_ReleaseSystemFontBatch(current);
            }
        }
#endif

        private static FontSource TryOpenCoreTextFile(string path)
        {
            try { return FontFileCache.OpenSnapshot(path); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static FontSource ReconstructSystemFontSource(IntPtr handle, int matchIndex)
        {
            Directory.CreateDirectory(reconstructedFontDirectory);
            var path = Path.Combine(reconstructedFontDirectory, $"{Guid.NewGuid():N}.sfnt");
            var created = false;
            try
            {
                var status = UniText_WriteSystemFontBatchSfnt(handle, matchIndex, path,
                    out var expectedLength);
                created = status == 1;
                if (!created || expectedLength <= 0 || expectedLength > int.MaxValue)
                    return null;
                var source = FileFontSource.OpenEphemeral(path);
                if (source.Length != expectedLength) return null;
                return source;
            }
            finally
            {
                if (created && File.Exists(path)) File.Delete(path);
            }
        }

        internal static IntPtr OpenSystemEmojiFace(out string identity, out FaceInfo faceInfo)
        {
            identity = null;
            faceInfo = default;
            var face = UniText_OpenSystemEmojiFace(out var nativeInfo,
                out var identityPointer, out var familyPointer, out var stylePointer);
            if (face == IntPtr.Zero)
                throw new InvalidOperationException("CoreText failed to open the Apple Color Emoji face.");
            try
            {
                if (identityPointer == IntPtr.Zero
                    || familyPointer == IntPtr.Zero || stylePointer == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "CoreText returned incomplete Apple Color Emoji metadata.");
                identity = Marshal.PtrToStringUTF8(identityPointer);
                var family = Marshal.PtrToStringUTF8(familyPointer);
                var style = Marshal.PtrToStringUTF8(stylePointer);
                if (string.IsNullOrEmpty(identity)
                    || string.IsNullOrEmpty(family) || string.IsNullOrEmpty(style)
                    || nativeInfo.unitsPerEm <= 0)
                    throw new InvalidOperationException(
                        "CoreText returned invalid Apple Color Emoji metadata.");
                faceInfo = nativeInfo.ToFaceInfo(family, style);
                return face;
            }
            catch
            {
                UniText_ReleaseSystemEmojiFace(face);
                throw;
            }
        }

        internal static IntPtr RetainSystemEmojiFace(IntPtr face)
        {
            if (face == IntPtr.Zero)
                throw new ArgumentException("A system emoji face is required.", nameof(face));
            var retained = UniText_RetainSystemEmojiFace(face);
            if (retained == IntPtr.Zero)
                throw new InvalidOperationException("CoreText failed to retain the system emoji face.");
            return retained;
        }

        internal static void ReleaseSystemEmojiFace(IntPtr face)
        {
            if (face != IntPtr.Zero) UniText_ReleaseSystemEmojiFace(face);
        }

        internal static bool TryGetSystemEmojiGlyph(IntPtr face, uint codepoint,
            out uint glyphIndex, out int advance)
        {
            if (face == IntPtr.Zero)
                throw new ArgumentException("A system emoji face is required.", nameof(face));
            if (codepoint > 0x10FFFF || codepoint is >= 0xD800 and <= 0xDFFF)
                throw new ArgumentOutOfRangeException(nameof(codepoint));
            var status = UniText_GetSystemEmojiGlyph(face, codepoint,
                out glyphIndex, out advance);
            if (status < 0)
                throw new InvalidOperationException("CoreText failed to map a system emoji glyph.");
            if (status == 0)
            {
                if (glyphIndex != 0 || advance != 0)
                    throw new InvalidOperationException(
                        "CoreText returned invalid missing-glyph data for the system emoji face.");
                return false;
            }
            if (status != 1 || glyphIndex == 0 || glyphIndex > ushort.MaxValue)
                throw new InvalidOperationException("CoreText returned an invalid system emoji glyph.");
            return true;
        }

        internal static int GetSystemEmojiGlyphAdvance(IntPtr face, uint glyphIndex)
        {
            if (face == IntPtr.Zero)
                throw new ArgumentException("A system emoji face is required.", nameof(face));
            if (glyphIndex == 0 || glyphIndex > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(glyphIndex));
            if (UniText_GetSystemEmojiGlyphAdvance(face, glyphIndex, out var advance) != 1)
                throw new InvalidOperationException(
                    "CoreText failed to read a system emoji glyph advance.");
            return advance;
        }

        internal static unsafe int ShapeSystemEmojiRun(IntPtr face,
            int* codepoints, int codepointCount,
            uint* outGlyphIds, int* outAdvances, int* outClusters,
            int maxOutput)
        {
            if (face == IntPtr.Zero)
                throw new ArgumentException("A system emoji face is required.", nameof(face));
            if (codepointCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(codepointCount));
            if (codepoints == null)
                throw new ArgumentNullException(nameof(codepoints));
            if (maxOutput < 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutput));
            if (maxOutput > 0
                && (outGlyphIds == null || outAdvances == null || outClusters == null))
                throw new ArgumentNullException(nameof(outGlyphIds));

            var result = UniText_ShapeSystemEmojiRun(face,
                codepoints, codepointCount,
                outGlyphIds, outAdvances, outClusters, maxOutput);
            if (result == int.MinValue)
                throw new InvalidOperationException("CoreText failed to shape the system emoji run.");
            if (result > maxOutput || (result < 0 && -(long)result <= maxOutput))
                throw new InvalidOperationException(
                    "CoreText returned an invalid system emoji shaping result.");
            return result;
        }

        internal static bool TryRenderSystemEmojiGlyph(IntPtr face,
            uint glyphIndex, int pixelSize, out FreeType.RenderedGlyph result)
        {
            result = default;
            if (face == IntPtr.Zero)
                throw new ArgumentException("A system emoji face is required.", nameof(face));
            if (glyphIndex == 0) return false;
            if (glyphIndex > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(glyphIndex));
            if (pixelSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pixelSize));

            var status = UniText_RenderSystemEmojiGlyph(face, glyphIndex, pixelSize,
                out var pixels, out var width, out var height,
                out var bearingX, out var bearingY, out var advance);
            byte[] rgbaPixels = null;
            var transferred = false;
            try
            {
                if (status < 0)
                    throw new InvalidOperationException(
                        "CoreText failed to render the system emoji glyph.");
                if (status == 0)
                {
                    if (pixels != IntPtr.Zero || width != 0 || height != 0
                        || bearingX != 0 || bearingY != 0)
                        throw new InvalidOperationException(
                            "CoreText returned invalid empty system emoji glyph pixels.");
                    return false;
                }
                if (status != 1 || width < 0 || height < 0
                    || float.IsNaN(advance) || float.IsInfinity(advance)
                    || (width == 0) != (height == 0)
                    || ((width == 0) != (pixels == IntPtr.Zero)))
                    throw new InvalidOperationException(
                        "CoreText returned invalid system emoji glyph pixels.");

                if (width == 0)
                {
                    result = new FreeType.RenderedGlyph
                    {
                        isValid = true,
                        advanceX = advance,
                    };
                    return true;
                }

                var size = checked(width * height * 4);
                rgbaPixels = ArrayPool<byte>.Rent(size);
                Marshal.Copy(pixels, rgbaPixels, 0, size);
                result = new FreeType.RenderedGlyph
                {
                    isValid = true,
                    width = width,
                    height = height,
                    bearingX = bearingX,
                    bearingY = bearingY,
                    advanceX = advance,
                    rgbaPixels = rgbaPixels,
                    isBGRA = false,
                };
                transferred = true;
                return true;
            }
            finally
            {
                try
                {
                    if (pixels != IntPtr.Zero) UniText_FreeBuffer(pixels);
                }
                finally
                {
                    if (!transferred && rgbaPixels != null)
                        ArrayPool<byte>.Return(rgbaPixels);
                }
            }
        }
    }
}
#endif
