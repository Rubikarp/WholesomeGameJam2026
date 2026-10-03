#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
namespace LightSide
{
    internal static class WindowsFontBackend
    {
        internal static bool TryResolve(string text, string language, string family,
            int requestWeight, bool requestItalic, out SystemFontSourceMatch match)
        {
            match = default;
            if (string.IsNullOrEmpty(text)) return false;
            var offsets = ArrayPool<int>.Rent(2);
            var matches = ArrayPool<SystemFontSourceMatch>.Rent(1);
            try
            {
                offsets[0] = 0;
                offsets[1] = text.Length;
                if (!WindowsSystemFontNative.TryResolveBatch(text, offsets, 1, language, family,
                        requestWeight, requestItalic, matches)
                    || string.IsNullOrEmpty(matches[0].descriptor))
                    return false;
                match = matches[0];
                return true;
            }
            finally
            {
                ArrayPool<int>.Return(offsets);
                ArrayPool<SystemFontSourceMatch>.Return(matches);
            }
        }

        internal static bool TryResolveBatch(string text, int[] offsets, int count,
            string language, string family, int requestWeight, bool requestItalic,
            SystemFontSourceMatch[] matches)
            => !string.IsNullOrEmpty(text) && count > 0
               && WindowsSystemFontNative.TryResolveBatch(text, offsets, count, language, family,
                   requestWeight, requestItalic, matches);

        internal static void Shutdown() => WindowsSystemFontNative.Shutdown();
    }
}
#endif
