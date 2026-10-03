using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Cursor appearance types. Each maps to platform-native cursor IDs
    /// via <see cref="UniTextCursor"/>'s internal mapping table.
    /// If the current platform has no native cursor for a type, the default arrow is used.
    /// </summary>
    public enum CursorType
    {
        Default,
        Text,
        Link,
        Crosshair,
        Move,
        NotAllowed,
        Help,
        Wait,
        Progress,
        Grab,
        Grabbing,
        ResizeHorizontal,
        ResizeVertical,
        ResizeNWSE,
        ResizeNESW,
        ColResize,
        RowResize,
        ZoomIn,
        ZoomOut,
        ContextMenu,
        Cell,
        Copy,
        Alias,
        NoDrop,
        VerticalText,
        AllScroll,
    }

    /// <summary>
    /// Manages mouse cursor appearance. Maps <see cref="CursorType"/> to platform-native
    /// cursor IDs and extracts system cursor bitmaps on first use.
    /// </summary>
    internal static class UniTextCursor
    {
        /// <summary>
        /// Per-type native cursor identifiers. No Linux column: there is no Linux hardware-cursor
        /// backend (X11/Wayland cursor loading needs libXcursor / wl_pointer work in the native
        /// plugin), and the managed fallback cannot reach xcursor themes — Linux falls to
        /// <see cref="ApplyDefault"/> until a native backend exists.
        /// </summary>
        private struct PlatformIds
        {
            public int windowsId;
            public string macSelector;
            public string cssCursor;
        }

        private const int CursorTypeCount = (int)CursorType.AllScroll + 1;

        private static readonly PlatformIds[] map = BuildMap();

        private static PlatformIds[] BuildMap()
        {
            var table = new PlatformIds[CursorTypeCount];
            void Set(CursorType type, PlatformIds ids) => table[(int)type] = ids;

            Set(CursorType.Text,             new() { windowsId = 32513, macSelector = "IBeamCursor",                 cssCursor = "text" });
            Set(CursorType.Link,             new() { windowsId = 32649, macSelector = "pointingHandCursor",          cssCursor = "pointer" });
            Set(CursorType.Crosshair,        new() { windowsId = 32515, macSelector = "crosshairCursor",             cssCursor = "crosshair" });
            Set(CursorType.Move,             new() { windowsId = 32646,                                              cssCursor = "move" });
            Set(CursorType.NotAllowed,       new() { windowsId = 32648, macSelector = "operationNotAllowedCursor",   cssCursor = "not-allowed" });
            Set(CursorType.Help,             new() { windowsId = 32651,                                              cssCursor = "help" });
            Set(CursorType.Wait,             new() { windowsId = 32514,                                              cssCursor = "wait" });
            Set(CursorType.Progress,         new() { windowsId = 32650,                                              cssCursor = "progress" });
            Set(CursorType.Grab,             new() {                    macSelector = "openHandCursor",              cssCursor = "grab" });
            Set(CursorType.Grabbing,         new() {                    macSelector = "closedHandCursor",            cssCursor = "grabbing" });
            Set(CursorType.ResizeHorizontal, new() { windowsId = 32644, macSelector = "resizeLeftRightCursor",       cssCursor = "ew-resize" });
            Set(CursorType.ResizeVertical,   new() { windowsId = 32645, macSelector = "resizeUpDownCursor",          cssCursor = "ns-resize" });
            Set(CursorType.ResizeNWSE,       new() { windowsId = 32642,                                              cssCursor = "nwse-resize" });
            Set(CursorType.ResizeNESW,       new() { windowsId = 32643,                                              cssCursor = "nesw-resize" });
            Set(CursorType.ColResize,        new() {                    macSelector = "columnResizeCursor",          cssCursor = "col-resize" });
            Set(CursorType.RowResize,        new() {                    macSelector = "rowResizeCursor",             cssCursor = "row-resize" });
            Set(CursorType.ZoomIn,           new() {                                                                 cssCursor = "zoom-in" });
            Set(CursorType.ZoomOut,          new() {                                                                 cssCursor = "zoom-out" });
            Set(CursorType.ContextMenu,      new() {                    macSelector = "contextualMenuCursor",        cssCursor = "context-menu" });
            Set(CursorType.Cell,             new() {                                                                 cssCursor = "cell" });
            Set(CursorType.Copy,             new() {                    macSelector = "dragCopyCursor",              cssCursor = "copy" });
            Set(CursorType.Alias,            new() {                    macSelector = "dragLinkCursor",              cssCursor = "alias" });
            Set(CursorType.NoDrop,           new() { windowsId = 32648, macSelector = "operationNotAllowedCursor",   cssCursor = "no-drop" });
            Set(CursorType.VerticalText,     new() {                    macSelector = "IBeamCursorForVerticalLayout", cssCursor = "vertical-text" });
            Set(CursorType.AllScroll,        new() {                                                                 cssCursor = "all-scroll" });

            return table;
        }

        private static CursorType current = CursorType.Default;

        private static CursorType baseWish = CursorType.Default;
        private static object claimOwner;

        private static readonly (Texture2D tex, Vector2 hot)[] cache = new (Texture2D, Vector2)[CursorTypeCount];
        private static readonly bool[] cacheSet = new bool[CursorTypeCount];

        private static readonly (Texture2D tex, Vector2 hot)[] overrides = new (Texture2D, Vector2)[CursorTypeCount];
        private static readonly bool[] overrideSet = new bool[CursorTypeCount];

        /// <summary>Set to false to disable all cursor changes by UniText.</summary>
        public static bool Enabled = true;

        /// <summary>
        /// Registers a custom cursor texture for a type, overriding OS extraction — including
        /// <see cref="CursorType.Default"/> (a custom default arrow). Passing a null texture
        /// clears the override.
        /// </summary>
        public static void SetCursor(CursorType type, Texture2D texture, Vector2 hotspot)
        {
            int i = (int)type;
            overrides[i] = (texture, hotspot);
            overrideSet[i] = texture != null;
            cache[i] = default;
            cacheSet[i] = false;
        }

        /// <summary>
        /// Activates the cursor for the given type. Surface owners (selectable I-beam, field chrome)
        /// call this per pointer move; while a <see cref="Claim"/> is active the wish is recorded but
        /// the claimed cursor stays on screen until <see cref="Release"/>.
        /// </summary>
        public static void Set(CursorType type)
        {
            baseWish = type;
            if (claimOwner != null) return;
            Apply(type);
        }

        /// <summary>
        /// Claims the cursor over the per-move surface writers — a hovered link sets
        /// <see cref="CursorType.Link"/> and it survives the selectable's I-beam writes until
        /// <see cref="Release"/>. Last claim wins; releasing a stale owner is a no-op.
        /// </summary>
        internal static void Claim(object owner, CursorType type)
        {
            claimOwner = owner;
            Apply(type);
        }

        internal static void Release(object owner)
        {
            if (!ReferenceEquals(claimOwner, owner)) return;
            claimOwner = null;
            Apply(baseWish);
        }

        private static void Apply(CursorType type)
        {
            if (!Enabled || current == type)
                return;

            current = type;
            int i = (int)type;

            if (overrideSet[i])
            {
                Cursor.SetCursor(overrides[i].tex, overrides[i].hot, CursorMode.Auto);
                return;
            }

            if (type == CursorType.Default)
            {
                ApplyDefault();
                return;
            }

            var ids = map[i];

#if UNITY_WEBGL && !UNITY_EDITOR
            if (ids.cssCursor != null) { WebGLSetCursor(ids.cssCursor); return; }
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            if (ids.macSelector != null) { MacSetNative(ids.macSelector); return; }
#endif

            if (cacheSet[i])
            {
                Cursor.SetCursor(cache[i].tex, cache[i].hot, CursorMode.Auto);
                return;
            }

            if (TryExtractSystemCursor(ids, out var tex, out var hot))
            {
                cache[i] = (tex, hot);
                cacheSet[i] = true;
                Cursor.SetCursor(tex, hot, CursorMode.Auto);
                return;
            }

            ApplyDefault();
        }

        private static void ApplyDefault()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLSetCursor("default");
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            MacSetNative("arrowCursor");
#else
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
#endif
        }

        private static bool TryExtractSystemCursor(PlatformIds ids, out Texture2D tex, out Vector2 hot)
        {
            tex = null;
            hot = Vector2.zero;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (ids.windowsId != 0)
                return Win32Extract(ids.windowsId, out tex, out hot);
#endif
            return false;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN

        [StructLayout(LayoutKind.Sequential)] private struct ICONINFO    { [MarshalAs(UnmanagedType.Bool)] public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
        [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO  { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT        { public int left, top, right, bottom; }

        [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr h, int id);
        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr h, out ICONINFO info);
        [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr h, int cx, int cy, uint s, IntPtr hbr, uint fl);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int idx);
        [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr hbr);
        [DllImport("gdi32.dll")]  private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]  private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")]  private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")]  private static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")]  private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint u, out IntPtr bits, IntPtr sec, uint off);
        [DllImport("gdi32.dll")]  private static extern IntPtr CreateSolidBrush(uint color);

        private static bool Win32Extract(int cursorId, out Texture2D tex, out Vector2 hot)
        {
            tex = null; hot = Vector2.zero;

            var hCur = LoadCursor(IntPtr.Zero, cursorId);
            if (hCur == IntPtr.Zero) return false;
            if (!GetIconInfo(hCur, out var info)) return false;

            try
            {
                int w = GetSystemMetrics(13), h = GetSystemMetrics(14);
                if (w <= 0 || h <= 0) { w = 32; h = 32; }

                var bmi = new BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                var hdc = CreateCompatibleDC(IntPtr.Zero);
                var hBmp = CreateDIBSection(hdc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
                if (hBmp == IntPtr.Zero) { DeleteDC(hdc); return false; }

                var old = SelectObject(hdc, hBmp);
                int bc = w * h * 4;
                var bp = new byte[bc]; var wp = new byte[bc];
                var rc = new RECT { right = w, bottom = h };

                var bb = CreateSolidBrush(0x00000000);
                FillRect(hdc, ref rc, bb); DeleteObject(bb);
                DrawIconEx(hdc, 0, 0, hCur, w, h, 0, IntPtr.Zero, 3);
                Marshal.Copy(bits, bp, 0, bc);

                var wb = CreateSolidBrush(0x00FFFFFF);
                FillRect(hdc, ref rc, wb); DeleteObject(wb);
                DrawIconEx(hdc, 0, 0, hCur, w, h, 0, IntPtr.Zero, 3);
                Marshal.Copy(bits, wp, 0, bc);

                SelectObject(hdc, old); DeleteObject(hBmp); DeleteDC(hdc);

                var px = new Color32[w * h];
                bool any = false;
                for (int y = 0; y < h; y++)
                {
                    int srcRow = y * w;
                    int dstRow = (h - 1 - y) * w;
                    for (int x = 0; x < w; x++)
                    {
                        int bi = (srcRow + x) * 4;
                        int bR = bp[bi+2], bG = bp[bi+1], bB = bp[bi];
                        int wR = wp[bi+2], wG = wp[bi+1], wB = wp[bi];
                        int d = wR-bR; if (wG-bG > d) d = wG-bG; if (wB-bB > d) d = wB-bB;
                        int a = 255 - d;
                        if (a <= 0) continue;
                        any = true;
                        int di = dstRow + x;
                        if (a >= 255) { px[di] = new Color32((byte)bR,(byte)bG,(byte)bB,255); }
                        else { float af = a/255f; px[di] = new Color32((byte)Mathf.Min(bR/af,255),(byte)Mathf.Min(bG/af,255),(byte)Mathf.Min(bB/af,255),(byte)a); }
                    }
                }
                if (!any) return false;

                hot = new Vector2(info.xHotspot, info.yHotspot);

                tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
#if UNITY_EDITOR
                    alphaIsTransparency = true,
#endif
                    hideFlags = HideFlags.HideAndDontSave
                };
                tex.SetPixels32(px);
                tex.Apply(false, false);
                return true;
            }
            finally
            {
                if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
                DeleteObject(info.hbmMask);
            }
        }
#endif

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]  private static extern IntPtr objc_getClass(string name);
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")] private static extern IntPtr sel_registerName(string name);
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]   private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        private static void MacSetNative(string selector)
        {
            var cls = objc_getClass("NSCursor");
            if (cls == IntPtr.Zero) return;
            var cursor = objc_msgSend(cls, sel_registerName(selector));
            if (cursor == IntPtr.Zero) return;
            objc_msgSend(cursor, sel_registerName("set"));
        }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void UniTextSetCursor(string css);

        private static void WebGLSetCursor(string css)
        {
            try { UniTextSetCursor(css); } catch {  }
        }
#endif
    }
}
