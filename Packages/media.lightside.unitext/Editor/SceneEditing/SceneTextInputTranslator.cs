using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide.SceneEditing
{
    /// <summary>
    /// Translates a SceneView UI Toolkit <see cref="KeyDownEvent"/> into the runtime input funnel so the SceneView reuses
    /// <c>InputPlatformKeyMap</c>, the <c>KeyResolver</c> hooks (bold/italic shortcuts), and
    /// <c>Execute</c> with zero duplicated editing logic. Printable characters go through TextInput;
    /// every navigation / editing key and modified shortcut (arrows, Ctrl+A/C/V/X/Z/Y, …) goes through
    /// KeyDown so the key map resolves it. Printable and IME text stays owned by the focused Toolkit
    /// field and enters through its value-change event.
    /// </summary>
    internal static class SceneTextInputTranslator
    {
        private const char Delete = (char)0x7F;

        internal static bool ProcessKeyDown(UniTextEditable editable, KeyDownEvent e)
            => ProcessKeyDown(editable, e.keyCode, e.character, e.modifiers);

        private static bool ProcessKeyDown(UniTextEditable editable, KeyCode keyCode, char character,
            EventModifiers modifiers)
        {
            var mods = ToNativeModifiers(modifiers);
            bool primary = (mods & (NativeModifiers.Ctrl | NativeModifiers.Cmd)) != 0;

            if (!primary && IsPrintable(character)) return false;

            if (NativeKeyCodeMap.TryFromUnity(keyCode, out var native))
            {
                if (NativeKeyCodeExtensions.IsLetter(native) && !primary) return false;
                editable.HandleKeyDown(native, mods);
                return true;
            }

            return false;
        }

        private static bool IsPrintable(char c) => c >= ' ' && c != Delete;

        private static NativeModifiers ToNativeModifiers(EventModifiers m)
        {
            var r = NativeModifiers.None;
            if ((m & EventModifiers.Shift) != 0) r |= NativeModifiers.Shift;
            if ((m & EventModifiers.Control) != 0) r |= NativeModifiers.Ctrl;
            if ((m & EventModifiers.Alt) != 0) r |= NativeModifiers.Alt;
            if ((m & EventModifiers.Command) != 0) r |= NativeModifiers.Cmd;
            return r;
        }
    }
}
