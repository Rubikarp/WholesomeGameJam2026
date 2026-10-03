#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

namespace LightSide
{
    internal static class PasteControlIOS
    {
        [DllImport("__Internal")] private static extern int UniTextPasteControl_IsSupported();
        [DllImport("__Internal")] private static extern int UniTextPasteControl_Create(string gameObjectName, int displayMode, int cornerStyle);
        [DllImport("__Internal")] private static extern void UniTextPasteControl_SetFrame(int handle, float x, float yBottom, float width, float height);
        [DllImport("__Internal")] private static extern void UniTextPasteControl_SetHidden(int handle, int hidden);
        [DllImport("__Internal")] private static extern void UniTextPasteControl_Destroy(int handle);
        [DllImport("__Internal")] private static extern IntPtr UniTextPasteControl_GetCapturedPayload(string identifier);
        [DllImport("__Internal")] private static extern void UniTextPasteControl_ClearCapturedPayload();

        public static bool IsSupported() => UniTextPasteControl_IsSupported() != 0;

        /// <summary>
        /// Creates the native UIPasteControl overlay HIDDEN (its initial frame is garbage
        /// until the first layout). The owner MUST call <see cref="SetHidden"/> with
        /// <see langword="false"/> once the first valid <see cref="SetFrame"/> has been
        /// sent, and mirror its own enabled state into <see cref="SetHidden"/> thereafter —
        /// otherwise the overlay exists but is invisible and untouchable.
        /// </summary>
        public static int Create(string gameObjectName, int displayMode, int cornerStyle)
            => UniTextPasteControl_Create(gameObjectName ?? string.Empty, displayMode, cornerStyle);

        public static void SetFrame(int handle, float x, float yBottom, float width, float height)
            => UniTextPasteControl_SetFrame(handle, x, yBottom, width, height);

        public static void SetHidden(int handle, bool hidden) => UniTextPasteControl_SetHidden(handle, hidden ? 1 : 0);
        public static void Destroy(int handle) => UniTextPasteControl_Destroy(handle);

        public static string GetCapturedPayload(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            IntPtr ptr = UniTextPasteControl_GetCapturedPayload(identifier);
            return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
        }

        public static void ClearCapturedPayload() => UniTextPasteControl_ClearCapturedPayload();
    }
}
#endif
