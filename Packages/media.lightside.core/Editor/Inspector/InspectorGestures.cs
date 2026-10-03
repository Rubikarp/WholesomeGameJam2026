using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Shared gesture vocabulary for editor controls: platform-correct modifier tests, the common
    /// click-versus-drag threshold, pressed-state retention for captured pointers, and the hidden
    /// anchored pointer a value drag runs on, so every surface reads the same gestures the same way.
    /// </summary>
    public static class InspectorGestures
    {
        /// <summary>Pointer travel below which a press still counts as a click.</summary>
        public const float DragThreshold = 3f;

        /// <summary>Class held on an element while it owns a pointer capture.</summary>
        public const string HeldClass = "lightside-held";

        /// <summary>How far from its surface's edge, in points, a held pointer is recentred.</summary>
        private const float RoamMargin = 48f;

        private static VisualElement pointerShield;
        private static bool pointerHidden;
        private static Vector2 pointerAnchor;
        private static Vector2 pointerAnchorPanel;
        private static Vector2 pendingRecentre;
        private static Rect pointerRoam;

        static InspectorGestures() => AssemblyReloadEvents.beforeAssemblyReload += ShowPointer;

        /// <summary>Whether the platform action modifier is held — Ctrl, or Command on macOS.</summary>
        public static bool Action(EventModifiers modifiers)
            => (modifiers & (EventModifiers.Control | EventModifiers.Command)) != 0;

        /// <summary>Whether the press extends a selection instead of replacing it.</summary>
        public static bool Additive(EventModifiers modifiers)
            => Action(modifiers) || (modifiers & EventModifiers.Shift) != 0;

        /// <summary>Whether pointer travel has crossed the click-versus-drag threshold.</summary>
        public static bool ExceedsDragThreshold(Vector2 delta)
            => Mathf.Abs(delta.x) >= DragThreshold || Mathf.Abs(delta.y) >= DragThreshold;

        /// <summary>
        /// Keeps <see cref="HeldClass"/> on the element while it holds a pointer capture, so
        /// pressed styling survives the pointer leaving the element mid-drag.
        /// </summary>
        public static void HoldPressedState(VisualElement element)
        {
            element.RegisterCallback<PointerCaptureEvent>(evt =>
            {
                if (evt.target == element) element.AddToClassList(HeldClass);
            });
            element.RegisterCallback<PointerCaptureOutEvent>(evt =>
            {
                if (evt.target == element) element.RemoveFromClassList(HeldClass);
            });
        }

        /// <summary>
        /// Hides the system pointer for a drag and anchors it where it is: the window system hides
        /// it and, where it can, confines it to <paramref name="panel"/>, and a shield covers the
        /// panel so nothing the hidden pointer roams over is hovered. The runtime cursor set through
        /// <see cref="UnityEngine.Cursor.SetCursor(Texture2D, Vector2, CursorMode)"/> is never
        /// touched. <paramref name="pointerPosition"/> is the panel position of the pointer event
        /// that starts the drag, which maps the panel onto the window system's pixels. Mouse only:
        /// no other pointer device can be repositioned. Pass every move through
        /// <see cref="TrackPointer"/>, and call <see cref="ShowPointer"/> when the drag ends or
        /// loses its capture, which is also what returns the pointer to where it was hidden.
        /// </summary>
        public static void HidePointer(IPanel panel, Vector2 pointerPosition)
        {
            if (panel == null) throw new ArgumentNullException(nameof(panel));
            ShowPointer();
            pointerAnchor = SystemPointer.Position;
            pointerAnchorPanel = pointerPosition;

            var root = panel.visualTree;
            var bounds = root.worldBound;
            var scale = SystemPointer.UnitsPerPoint(EditorGUIUtility.pixelsPerPoint);
            var margin = Mathf.Min(RoamMargin, Mathf.Min(bounds.width, bounds.height) * 0.25f);
            pointerRoam = new Rect(bounds.x + margin, bounds.y + margin,
                bounds.width - margin * 2f, bounds.height - margin * 2f);
            pendingRecentre = Vector2.zero;
            var min = ToPixels(bounds.min, pointerPosition, scale);
            var max = ToPixels(bounds.max, pointerPosition, scale);
            SystemPointer.Conceal(Rect.MinMaxRect(min.x, min.y, max.x, max.y));
            pointerHidden = true;
            root.Add(Shield);
        }

        private static Vector2 ToPixels(Vector2 panelPoint, Vector2 panelOrigin, float scale)
            => pointerAnchor + (panelPoint - panelOrigin) * scale;

        /// <summary>
        /// Reads one move of a hidden pointer and returns the travel it carries: the event's own
        /// delta less any recall this method performed. Subtracting is what makes travel exact at
        /// any speed, and it holds however the window system reports the recall — as its own event,
        /// folded into the next one, or not at all, since a panel measures each delta from the last
        /// position it was told about. The pointer roams its surface freely and is recalled only
        /// near the edge. Returns <paramref name="delta"/> unchanged while no pointer is hidden.
        /// </summary>
        public static Vector2 TrackPointer(Vector2 delta, Vector2 pointerPosition)
        {
            if (!pointerHidden) return delta;
            var travel = delta - pendingRecentre;
            pendingRecentre = Vector2.zero;
            if (!pointerRoam.Contains(pointerPosition))
            {
                SystemPointer.Position = pointerAnchor;
                pendingRecentre = pointerAnchorPanel - pointerPosition;
            }
            return travel;
        }

        /// <summary>
        /// Reveals the system pointer at the position <see cref="HidePointer"/> hid it. No-op while
        /// no pointer is hidden.
        /// </summary>
        public static void ShowPointer()
        {
            if (!pointerHidden) return;
            pointerHidden = false;
            pointerShield.RemoveFromHierarchy();
            SystemPointer.Position = pointerAnchor;
            SystemPointer.Reveal();
        }

        private static VisualElement Shield
        {
            get
            {
                if (pointerShield != null) return pointerShield;
                pointerShield = new VisualElement();
                pointerShield.style.position = Position.Absolute;
                pointerShield.style.left = 0f;
                pointerShield.style.top = 0f;
                pointerShield.style.right = 0f;
                pointerShield.style.bottom = 0f;
                return pointerShield;
            }
        }

        /// <summary>Pointer position in the window-system screen space of the running editor.</summary>
        private static class SystemPointer
        {
            private const string User32 = "user32.dll";
            private const string CoreGraphics =
                "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
            private const string CoreFoundation =
                "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
            private const string X11 = "libX11.so.6";
            private const string XFixes = "libXfixes.so.3";

            private static IntPtr display;

            public static Vector2 Position
            {
                get
                {
                    switch (Application.platform)
                    {
                        case RuntimePlatform.WindowsEditor:
                            GetCursorPos(out var point);
                            return new Vector2(point.x, point.y);
                        case RuntimePlatform.OSXEditor:
                            var probe = CGEventCreate(IntPtr.Zero);
                            var location = CGEventGetLocation(probe);
                            CFRelease(probe);
                            return new Vector2((float)location.x, (float)location.y);
                        case RuntimePlatform.LinuxEditor:
                            XQueryPointer(Display, XDefaultRootWindow(Display), out _, out _,
                                out var rootX, out var rootY, out _, out _, out _);
                            return new Vector2(rootX, rootY);
                        default:
                            throw Unsupported();
                    }
                }
                set
                {
                    switch (Application.platform)
                    {
                        case RuntimePlatform.WindowsEditor:
                            SetCursorPos(Mathf.RoundToInt(value.x), Mathf.RoundToInt(value.y));
                            break;
                        case RuntimePlatform.OSXEditor:
                            CGWarpMouseCursorPosition(new QuartzPoint { x = value.x, y = value.y });
                            CGAssociateMouseAndMouseCursorPosition(1);
                            break;
                        case RuntimePlatform.LinuxEditor:
                            XWarpPointer(Display, IntPtr.Zero, XDefaultRootWindow(Display),
                                0, 0, 0, 0, Mathf.RoundToInt(value.x), Mathf.RoundToInt(value.y));
                            XFlush(Display);
                            break;
                        default:
                            throw Unsupported();
                    }
                }
            }

            /// <summary>
            /// How many window system units one panel point spans: Quartz addresses the pointer in
            /// logical points, so a Retina backing scale must not enter the conversion; every other
            /// host addresses it in device pixels.
            /// </summary>
            public static float UnitsPerPoint(float panelScale) =>
                Application.platform == RuntimePlatform.OSXEditor ? 1f : panelScale;

            /// <summary>
            /// Hides the pointer for the editor and, where the window system can confine it, holds
            /// it inside <paramref name="bounds"/>, given in window system pixels, so it cannot
            /// surface outside its panel. Pair with <see cref="Reveal"/>.
            /// </summary>
            public static void Conceal(Rect bounds)
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                        ShowCursor(false);
                        var clip = new WindowsRect
                        {
                            left = Mathf.RoundToInt(bounds.xMin),
                            top = Mathf.RoundToInt(bounds.yMin),
                            right = Mathf.RoundToInt(bounds.xMax),
                            bottom = Mathf.RoundToInt(bounds.yMax),
                        };
                        ClipCursor(ref clip);
                        break;
                    case RuntimePlatform.OSXEditor:
                        CGDisplayHideCursor(CGMainDisplayID());
                        break;
                    case RuntimePlatform.LinuxEditor:
                        XFixesHideCursor(Display, XDefaultRootWindow(Display));
                        XFlush(Display);
                        break;
                    default:
                        throw Unsupported();
                }
            }

            public static void Reveal()
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                        ClearCursorClip(IntPtr.Zero);
                        ShowCursor(true);
                        break;
                    case RuntimePlatform.OSXEditor:
                        CGDisplayShowCursor(CGMainDisplayID());
                        break;
                    case RuntimePlatform.LinuxEditor:
                        XFixesShowCursor(Display, XDefaultRootWindow(Display));
                        XFlush(Display);
                        break;
                    default:
                        throw Unsupported();
                }
            }

            private static IntPtr Display =>
                display != IntPtr.Zero ? display : (display = XOpenDisplay(IntPtr.Zero));

            private static PlatformNotSupportedException Unsupported() =>
                new($"The editor pointer cannot be positioned on {Application.platform}.");

            [StructLayout(LayoutKind.Sequential)]
            private struct WindowsPoint
            {
                public int x;
                public int y;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct WindowsRect
            {
                public int left;
                public int top;
                public int right;
                public int bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct QuartzPoint
            {
                public double x;
                public double y;
            }

            [DllImport(User32)] private static extern bool GetCursorPos(out WindowsPoint point);
            [DllImport(User32)] private static extern bool SetCursorPos(int x, int y);
            [DllImport(User32)] private static extern int ShowCursor(bool show);
            [DllImport(User32)] private static extern bool ClipCursor(ref WindowsRect bounds);
            [DllImport(User32, EntryPoint = "ClipCursor")] private static extern bool ClearCursorClip(IntPtr bounds);

            [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
            [DllImport(CoreGraphics)] private static extern QuartzPoint CGEventGetLocation(IntPtr quartzEvent);
            [DllImport(CoreGraphics)] private static extern int CGWarpMouseCursorPosition(QuartzPoint point);
            [DllImport(CoreGraphics)] private static extern int CGAssociateMouseAndMouseCursorPosition(int connected);
            [DllImport(CoreGraphics)] private static extern uint CGMainDisplayID();
            [DllImport(CoreGraphics)] private static extern int CGDisplayHideCursor(uint display);
            [DllImport(CoreGraphics)] private static extern int CGDisplayShowCursor(uint display);
            [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr reference);

            [DllImport(XFixes)] private static extern void XFixesHideCursor(IntPtr display, IntPtr window);
            [DllImport(XFixes)] private static extern void XFixesShowCursor(IntPtr display, IntPtr window);
            [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr name);
            [DllImport(X11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
            [DllImport(X11)] private static extern int XQueryPointer(IntPtr display, IntPtr window,
                out IntPtr root, out IntPtr child, out int rootX, out int rootY,
                out int windowX, out int windowY, out uint mask);
            [DllImport(X11)] private static extern int XWarpPointer(IntPtr display, IntPtr source,
                IntPtr destination, int sourceX, int sourceY, uint sourceWidth, uint sourceHeight,
                int destinationX, int destinationY);
            [DllImport(X11)] private static extern int XFlush(IntPtr display);
        }
    }
}
