namespace LightSide
{
    /// <summary>
    /// Resolves a <see cref="NativeKeyCode"/> + <see cref="NativeModifiers"/> combination
    /// into an <see cref="EditAction"/>. Platform-specific modifier semantics (Cmd vs Ctrl,
    /// Emacs bindings, Home/End behavior) are resolved once at construction time.
    /// </summary>
    internal sealed class InputPlatformKeyMap
    {
        /// <summary>Whether Mac key semantics apply (Cmd shortcuts, Option word navigation).</summary>
        private readonly bool isMacOS;

        /// <summary>
        /// Whether Emacs keybindings (Ctrl+A/E/F/B/P/N/K/T) are active.
        /// True on macOS by default — these are system-wide bindings in NSText.
        /// </summary>
        private readonly bool emacsBindings;

        /// <summary>
        /// Creates a key map configured for the given platform.
        /// </summary>
        /// <param name="isMacOS">
        /// True for Mac semantics (Cmd for shortcuts, Option for word navigation, Emacs bindings).
        /// False for Windows/Linux (Ctrl for both shortcuts and word navigation).
        /// </param>
        public InputPlatformKeyMap(bool isMacOS)
        {
            this.isMacOS = isMacOS;
            emacsBindings = isMacOS;
        }

        private static InputPlatformKeyMap instance;

        /// <summary>
        /// The per-process key map for the platform the player actually RUNS on — resolved at runtime
        /// via <see cref="PlatformKeySemantics"/>, because compile-time symbols cannot know the host OS
        /// (WebGL in a macOS browser, iPad hardware keyboards). The map is two booleans of derived
        /// state, identical for every component, so one instance serves the process.
        /// </summary>
        public static InputPlatformKeyMap Instance
            => instance ??= new InputPlatformKeyMap(PlatformKeySemantics.PrimaryModifierIsCommand);

        /// <summary>
        /// Resolves a key-down event into an editing action. Enter always resolves to
        /// <see cref="EditAction.InsertNewline"/> — submit semantics are contributed by behaviors
        /// through the <c>KeyResolver</c> hook, which runs before this map.
        /// </summary>
        /// <remarks>
        /// On Windows, Ctrl+Alt together never resolves to a shortcut: AltGr arrives as Ctrl+Alt,
        /// so AltGr-typed characters on European layouts (e.g. AltGr+A on Polish) must not trigger
        /// Ctrl bindings — the Chromium/WPF convention.
        /// </remarks>
        /// <returns>The resolved action, or <see cref="EditAction.None"/> if the key is not mapped.</returns>
        public EditAction Resolve(NativeKeyCode key, NativeModifiers mods)
        {
            bool shift = (mods & NativeModifiers.Shift) != 0;
            bool ctrl = (mods & NativeModifiers.Ctrl) != 0;
            bool alt = (mods & NativeModifiers.Alt) != 0;

            bool action = isMacOS ? (mods & NativeModifiers.Cmd) != 0 : ctrl && !alt;
            bool word = isMacOS ? alt : ctrl && !alt;

            if (action)
            {
                if (key == NativeKeyCode.A) return EditAction.SelectAll;
                if (key == NativeKeyCode.C) return EditAction.Copy;
                if (key == NativeKeyCode.X) return EditAction.Cut;
                if (key == NativeKeyCode.V) return shift ? EditAction.PasteAsPlain : EditAction.Paste;
                if (key == NativeKeyCode.Z) return shift ? EditAction.Redo : EditAction.Undo;
                if (!isMacOS && key == NativeKeyCode.Y) return EditAction.Redo;

                if (isMacOS)
                {
                    if (key == NativeKeyCode.LeftArrow)
                        return shift ? EditAction.SelectLineStart : EditAction.MoveLineStart;
                    if (key == NativeKeyCode.RightArrow)
                        return shift ? EditAction.SelectLineEnd : EditAction.MoveLineEnd;
                    if (key == NativeKeyCode.UpArrow)
                        return shift ? EditAction.SelectDocStart : EditAction.MoveDocStart;
                    if (key == NativeKeyCode.DownArrow)
                        return shift ? EditAction.SelectDocEnd : EditAction.MoveDocEnd;
                    if (key == NativeKeyCode.Backspace)
                        return EditAction.DeleteLineStart;
                }
            }

            if (word)
            {
                if (key == NativeKeyCode.LeftArrow)
                    return shift ? EditAction.SelectWordLeft : EditAction.MoveWordLeft;
                if (key == NativeKeyCode.RightArrow)
                    return shift ? EditAction.SelectWordRight : EditAction.MoveWordRight;
                if (key == NativeKeyCode.Backspace)
                    return EditAction.DeleteWordPrev;
                if (key == NativeKeyCode.Delete)
                    return EditAction.DeleteWordNext;

                if (!isMacOS)
                {
                    if (key == NativeKeyCode.Home)
                        return shift ? EditAction.SelectDocStart : EditAction.MoveDocStart;
                    if (key == NativeKeyCode.End)
                        return shift ? EditAction.SelectDocEnd : EditAction.MoveDocEnd;
                }
            }

            if (emacsBindings && ctrl && (mods & NativeModifiers.Cmd) == 0)
            {
                if (key == NativeKeyCode.A)
                    return shift ? EditAction.SelectLineStart : EditAction.MoveLineStart;
                if (key == NativeKeyCode.E)
                    return shift ? EditAction.SelectLineEnd : EditAction.MoveLineEnd;
                if (key == NativeKeyCode.F)
                    return shift ? EditAction.SelectRight : EditAction.MoveRight;
                if (key == NativeKeyCode.B)
                    return shift ? EditAction.SelectLeft : EditAction.MoveLeft;
                if (key == NativeKeyCode.P)
                    return shift ? EditAction.SelectUp : EditAction.MoveUp;
                if (key == NativeKeyCode.N)
                    return shift ? EditAction.SelectDown : EditAction.MoveDown;
                if (key == NativeKeyCode.K)
                    return EditAction.DeleteLineEnd;
                if (key == NativeKeyCode.T)
                    return EditAction.TransposeChars;
                if (key == NativeKeyCode.D)
                    return EditAction.DeleteNext;
                if (key == NativeKeyCode.H)
                    return EditAction.DeletePrev;
            }

            if (key == NativeKeyCode.LeftArrow) return shift ? EditAction.SelectLeft : EditAction.MoveLeft;
            if (key == NativeKeyCode.RightArrow) return shift ? EditAction.SelectRight : EditAction.MoveRight;
            if (key == NativeKeyCode.UpArrow) return shift ? EditAction.SelectUp : EditAction.MoveUp;
            if (key == NativeKeyCode.DownArrow) return shift ? EditAction.SelectDown : EditAction.MoveDown;
            if (key == NativeKeyCode.PageUp) return shift ? EditAction.SelectPageUp : EditAction.MovePageUp;
            if (key == NativeKeyCode.PageDown) return shift ? EditAction.SelectPageDown : EditAction.MovePageDown;

            if (key == NativeKeyCode.Home)
            {
                if (isMacOS)
                    return shift ? EditAction.SelectDocStart : EditAction.MoveDocStart;
                return shift ? EditAction.SelectLineStart : EditAction.MoveLineStart;
            }

            if (key == NativeKeyCode.End)
            {
                if (isMacOS)
                    return shift ? EditAction.SelectDocEnd : EditAction.MoveDocEnd;
                return shift ? EditAction.SelectLineEnd : EditAction.MoveLineEnd;
            }

            if (key == NativeKeyCode.Delete && shift) return EditAction.Cut;
            if (key == NativeKeyCode.Insert)
                return ctrl ? EditAction.Copy : shift ? EditAction.Paste : EditAction.None;

            if (key == NativeKeyCode.Backspace) return EditAction.DeletePrev;
            if (key == NativeKeyCode.Delete) return EditAction.DeleteNext;
            if (key == NativeKeyCode.Escape) return EditAction.Cancel;

            if (key == NativeKeyCode.Return || key == NativeKeyCode.KeypadEnter)
                return EditAction.InsertNewline;

            return EditAction.None;
        }
    }
}
