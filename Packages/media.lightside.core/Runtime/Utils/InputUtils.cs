using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using System;
using System.Reflection;
#endif

namespace LightSide
{
    /// <summary>
    /// Backend-agnostic input reads. Calling <see cref="UnityEngine.Input"/> directly throws an
    /// <see cref="System.InvalidOperationException"/> every frame when the project's active input
    /// handling is set to the Input System package only; these route to <c>Keyboard.current</c> /
    /// <c>Mouse.current</c> in that configuration, to legacy <c>Input</c> otherwise.
    /// </summary>
    public static class InputUtils
    {
        /// <summary>Whether <paramref name="key"/> is currently held. Backend-safe equivalent of <see cref="UnityEngine.Input.GetKey(KeyCode)"/>.</summary>
        public static bool GetKey(KeyCode key)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(key);
#elif ENABLE_INPUT_SYSTEM
            var k = ToKey(key);
            if (k == Key.None) return false;
            var kb = Keyboard.current;
            return kb != null && kb[k].isPressed;
#else
            return false;
#endif
        }

        /// <summary>Whether <paramref name="key"/> went down this frame. Backend-safe equivalent of <see cref="UnityEngine.Input.GetKeyDown(KeyCode)"/>.</summary>
        public static bool GetKeyDown(KeyCode key)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(key);
#elif ENABLE_INPUT_SYSTEM
            var k = ToKey(key);
            if (k == Key.None) return false;
            var kb = Keyboard.current;
            return kb != null && kb[k].wasPressedThisFrame;
#else
            return false;
#endif
        }

        /// <summary>
        /// Pointer position in screen pixels, origin bottom-left (as <see cref="Input.mousePosition"/>).
        /// Under the Input System this reads <c>Pointer.current</c> — mouse, touch, or pen, whichever
        /// acted last — so touch-only devices report the finger, not (0,0).
        /// </summary>
        public static Vector2 MousePosition
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                if (Input.touchCount > 0) return Input.GetTouch(0).position;
                return Input.mousePosition;
#elif ENABLE_INPUT_SYSTEM
                var p = UnityEngine.InputSystem.Pointer.current;
                return p != null ? p.position.ReadValue() : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>Whether mouse <paramref name="button"/> (0 = left, 1 = right, 2 = middle) went down this frame. Backend-safe equivalent of <see cref="UnityEngine.Input.GetMouseButtonDown(int)"/>.</summary>
        public static bool GetMouseButtonDown(int button)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(button);
#elif ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            var ctrl = button switch { 0 => m.leftButton, 1 => m.rightButton, 2 => m.middleButton, _ => null };
            return ctrl != null && ctrl.wasPressedThisFrame;
#else
            return false;
#endif
        }

        /// <summary>Whether the primary pointer (left mouse button, touch, or pen) is pressed right now — the backend-safe "is a tap/drag in progress" query.</summary>
        public static bool GetPointerPressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0)
            {
                var phase = Input.GetTouch(0).phase;
                return phase != TouchPhase.Ended && phase != TouchPhase.Canceled;
            }
            return Input.GetMouseButton(0);
#elif ENABLE_INPUT_SYSTEM
            var p = UnityEngine.InputSystem.Pointer.current;
            return p != null && p.press.isPressed;
#else
            return false;
#endif
        }

        /// <summary>Whether a primary touch began this frame.</summary>
        public static bool GetTouchBegan()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began;
#elif ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            return ts != null && ts.primaryTouch.press.wasPressedThisFrame;
#else
            return false;
#endif
        }

        /// <summary>Characters typed this frame. Backend-safe equivalent of <see cref="UnityEngine.Input.inputString"/>.</summary>
        public static string InputString
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.inputString;
#elif ENABLE_INPUT_SYSTEM
                var kb = Keyboard.current;
                if (kb == null) return string.Empty;
                if (!ReferenceEquals(kb, subscribedKeyboard))
                {
                    if (subscribedKeyboard != null) subscribedKeyboard.onTextInput -= OnTextInput;
                    subscribedKeyboard = kb;
                    kb.onTextInput += OnTextInput;
                }
                return textInputFrame == Time.frameCount ? textInput.ToString() : string.Empty;
#else
                return string.Empty;
#endif
            }
        }

#if UNITY_EDITOR
        private static bool gameViewReflectionInit;
        private static Type gameViewType;
        private static PropertyInfo viewInWindowProp;
        private static UnityEngine.Object cachedGameView;

        /// <summary>
        /// The Game View's rendering area within its window, in pixels (the toolbar/tab strip sits
        /// above it, so <c>y</c> is the toolbar height), via reflection on the internal
        /// UnityEditor.GameView.viewInWindow. The resolved window is cached — callers invoke this per
        /// frame and <c>Resources.FindObjectsOfTypeAll</c> walks every loaded object. False on reflection failure.
        /// </summary>
        public static bool TryGetEditorGameViewArea(out Rect areaPx)
        {
            areaPx = default;
            if (!gameViewReflectionInit)
            {
                gameViewReflectionInit = true;
                gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                if (gameViewType != null)
                {
                    viewInWindowProp = gameViewType.GetProperty("viewInWindow",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }
            }

            if (gameViewType == null || viewInWindowProp == null) return false;

            if (cachedGameView == null)
            {
                var focused = UnityEditor.EditorWindow.focusedWindow;
                if (focused != null && gameViewType.IsInstanceOfType(focused))
                {
                    cachedGameView = focused;
                }
                else
                {
                    var gameViews = Resources.FindObjectsOfTypeAll(gameViewType);
                    if (gameViews.Length == 0) return false;
                    cachedGameView = gameViews[0];
                }
            }

            var rect = (Rect)viewInWindowProp.GetValue(cachedGameView);
            float ppp = UnityEditor.EditorGUIUtility.pixelsPerPoint;
            areaPx = new Rect(rect.x * ppp, rect.y * ppp, rect.width * ppp, rect.height * ppp);
            return areaPx.height > 0f;
        }

        /// <summary>Returns the Game View area and its render-texture-to-window scale.</summary>
        public static bool TryGetEditorGameViewProjection(out Rect areaPx, out float renderToWindowScale)
        {
            areaPx = default;
            renderToWindowScale = 1f;
            if (Screen.width <= 0 || Screen.height <= 0
                || !TryGetEditorGameViewArea(out areaPx)
                || areaPx.width <= 0f || areaPx.height <= 0f) return false;

            renderToWindowScale = Mathf.Min(areaPx.width / Screen.width, areaPx.height / Screen.height);
            return renderToWindowScale > 0f;
        }
#endif

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private static readonly System.Text.StringBuilder textInput = new();
        private static int textInputFrame = -1;
        private static Keyboard subscribedKeyboard;

        private static void OnTextInput(char c)
        {
            var frame = Time.frameCount;
            if (frame != textInputFrame) { textInput.Clear(); textInputFrame = frame; }
            textInput.Append(c);
        }

        private static Key ToKey(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z) return Key.A + (key - KeyCode.A);
            if (key >= KeyCode.F1 && key <= KeyCode.F12) return Key.F1 + (key - KeyCode.F1);
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return Key.Numpad0 + (key - KeyCode.Keypad0);

            return key switch
            {
                KeyCode.Alpha0 => Key.Digit0,
                KeyCode.Alpha1 => Key.Digit1,
                KeyCode.Alpha2 => Key.Digit2,
                KeyCode.Alpha3 => Key.Digit3,
                KeyCode.Alpha4 => Key.Digit4,
                KeyCode.Alpha5 => Key.Digit5,
                KeyCode.Alpha6 => Key.Digit6,
                KeyCode.Alpha7 => Key.Digit7,
                KeyCode.Alpha8 => Key.Digit8,
                KeyCode.Alpha9 => Key.Digit9,

                KeyCode.UpArrow => Key.UpArrow,
                KeyCode.DownArrow => Key.DownArrow,
                KeyCode.LeftArrow => Key.LeftArrow,
                KeyCode.RightArrow => Key.RightArrow,

                KeyCode.Space => Key.Space,
                KeyCode.Return => Key.Enter,
                KeyCode.KeypadEnter => Key.NumpadEnter,
                KeyCode.Escape => Key.Escape,
                KeyCode.Tab => Key.Tab,
                KeyCode.Backspace => Key.Backspace,
                KeyCode.Delete => Key.Delete,
                KeyCode.Insert => Key.Insert,
                KeyCode.Home => Key.Home,
                KeyCode.End => Key.End,
                KeyCode.PageUp => Key.PageUp,
                KeyCode.PageDown => Key.PageDown,
                KeyCode.CapsLock => Key.CapsLock,

                KeyCode.BackQuote => Key.Backquote,
                KeyCode.Minus => Key.Minus,
                KeyCode.Equals => Key.Equals,
                KeyCode.LeftBracket => Key.LeftBracket,
                KeyCode.RightBracket => Key.RightBracket,
                KeyCode.Backslash => Key.Backslash,
                KeyCode.Semicolon => Key.Semicolon,
                KeyCode.Quote => Key.Quote,
                KeyCode.Comma => Key.Comma,
                KeyCode.Period => Key.Period,
                KeyCode.Slash => Key.Slash,

                KeyCode.LeftShift => Key.LeftShift,
                KeyCode.RightShift => Key.RightShift,
                KeyCode.LeftControl => Key.LeftCtrl,
                KeyCode.RightControl => Key.RightCtrl,
                KeyCode.LeftAlt => Key.LeftAlt,
                KeyCode.RightAlt => Key.RightAlt,
                KeyCode.LeftCommand => Key.LeftMeta,
                KeyCode.RightCommand => Key.RightMeta,
                KeyCode.LeftWindows => Key.LeftMeta,
                KeyCode.RightWindows => Key.RightMeta,

                _ => Key.None
            };
        }
#endif
    }
}
