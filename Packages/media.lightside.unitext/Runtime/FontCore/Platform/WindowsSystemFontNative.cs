#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LightSide
{
    internal static class WindowsSystemFontNative
    {
        private const string Library = "UniTextSystemFontWindows";
        private const int AbiVersion = 1;
        private static int validatedAbiVersion;
        private static volatile bool nativeLoaded;

        [DllImport(Library, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UniText_SystemFontGetAbiVersion();

        [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        private static extern int UniText_SystemFontResolveRuns(
            string text, int textLength, string locale, string family, int weight, int italic,
            out IntPtr handle);

        [DllImport(Library, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UniText_SystemFontGetMatchCount(IntPtr handle);

        [DllImport(Library, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UniText_SystemFontGetMatch(IntPtr handle, int index,
            out int start, out int length, out int faceIndex, out float scale,
            out IntPtr path, out int axisCount);

        [DllImport(Library, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UniText_SystemFontGetAxis(IntPtr handle, int matchIndex,
            int axisIndex, out int tag, out float value);

        [DllImport(Library, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern void UniText_SystemFontReleaseMatches(IntPtr handle);

        [DllImport(Library, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern void UniText_SystemFontShutdown();

        internal static bool TryResolveBatch(string text, int[] offsets, int count,
            string language, string family, int requestWeight, bool requestItalic,
            SystemFontSourceMatch[] matches)
        {
            if (string.IsNullOrEmpty(text) || count <= 0) return false;
            ValidateAbi();
            var result = UniText_SystemFontResolveRuns(text, text.Length,
                language ?? string.Empty, family ?? string.Empty,
                Math.Clamp(requestWeight > 0 ? requestWeight : 400, 1, 1000),
                requestItalic ? 1 : 0, out var handle);
            nativeLoaded = true;
            Marshal.ThrowExceptionForHR(result);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("The Windows system-font resolver returned no match handle.");
            try
            {
                var matchCount = UniText_SystemFontGetMatchCount(handle);
                if (matchCount < 0)
                    throw new InvalidOperationException("The Windows system-font resolver returned an invalid match count.");
                var requestIndex = 0;
                for (var i = 0; i < matchCount; i++)
                {
                    if (UniText_SystemFontGetMatch(handle, i, out var runStart, out var runLength,
                            out var faceIndex, out var scale, out var pathPointer, out var axisCount) == 0
                        || runLength <= 0)
                        throw new InvalidOperationException("The Windows system-font resolver returned an invalid match.");
                    if (pathPointer == IntPtr.Zero)
                        continue;

                    UniTextFont.AxisDefault[] axes = null;
                    if ((uint)axisCount > 64)
                        throw new InvalidOperationException("The Windows system-font resolver returned an invalid axis count.");
                    if (axisCount > 0)
                    {
                        axes = new UniTextFont.AxisDefault[axisCount];
                        for (var axisIndex = 0; axisIndex < axisCount; axisIndex++)
                        {
                            if (UniText_SystemFontGetAxis(handle, i, axisIndex,
                                    out var tag, out var value) == 0)
                                throw new InvalidOperationException("The Windows system-font resolver returned an invalid axis.");
                            axes[axisIndex] = new UniTextFont.AxisDefault { tag = tag, value = value };
                        }
                    }

                    var source = new SystemFontSourceMatch
                    {
                        descriptor = Marshal.PtrToStringUni(pathPointer),
                        faceIndex = faceIndex,
                        axes = axes,
                        coveredUtf16Length = runLength,
                        scale = scale,
                    };
                    FillCoveredRequests(offsets, count, runStart, runStart + runLength,
                        in source, matches, ref requestIndex);
                }
                return true;
            }
            finally
            {
                UniText_SystemFontReleaseMatches(handle);
            }
        }

        internal static void Shutdown()
        {
            if (!nativeLoaded) return;
            UniText_SystemFontShutdown();
            nativeLoaded = false;
        }

        private static void ValidateAbi()
        {
            if (Volatile.Read(ref validatedAbiVersion) == AbiVersion) return;
            var actual = UniText_SystemFontGetAbiVersion();
            if (actual != AbiVersion)
                throw new InvalidOperationException(
                    $"UniTextSystemFontWindows ABI {actual} is incompatible with required ABI {AbiVersion}.");
            Volatile.Write(ref validatedAbiVersion, actual);
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
    }
}
#endif
