using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace LightSide
{
    /// <summary>
    /// Unity-event backend for keyboard editing and single-clause IME composition.
    /// </summary>
    /// <remarks>
    /// Keys and characters both come from the operating system's key events, drained through
    /// <see cref="Event.PopEvent"/> while a field is focused — the same source Unity's own input
    /// fields read, which is what carries layout-resolved characters and the system's key repeat.
    /// The drain consumes those events, so other consumers stop seeing keyboard input while a field
    /// is focused. The backend does not show software keyboards, present native fields, or consume
    /// pushed document windows.
    /// </remarks>
    public sealed class ManagedInputBackend : INativeInputBackend
    {
        /// <summary>Priority below built-in native transports.</summary>
        public const int FallbackPriority = -100;

        private static CompositionClause[] clauseBuffer = new CompositionClause[1];

        private NativeInputReporter reporter;
        private NativeInputReporter quiescedReporter;
        private bool composing;
        private string lastComposition = string.Empty;
        private char[] textFilterBuffer = new char[64];
        private int lastPumpFrame = -1;
        private bool starvationReported;
        private int starvedKeystrokes;
        private readonly Event guiEvent = new Event();

        private const int TextStarvationLimit = 8;
        private const NativeModifiers ShortcutModifiers = NativeModifiers.Ctrl | NativeModifiers.Cmd;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private static string imeCompositionCache = string.Empty;
        private static Keyboard imeSubscribedKeyboard;

        private static void EnsureImeSubscription()
        {
            var kb = Keyboard.current;
            if (ReferenceEquals(kb, imeSubscribedKeyboard)) return;
            if (imeSubscribedKeyboard != null)
                imeSubscribedKeyboard.onIMECompositionChange -= OnImeCompositionChange;
            imeSubscribedKeyboard = kb;
            imeCompositionCache = string.Empty;
            if (kb != null)
                kb.onIMECompositionChange += OnImeCompositionChange;
        }

        private static void OnImeCompositionChange(UnityEngine.InputSystem.LowLevel.IMECompositionString composition)
        {
            imeCompositionCache = composition.Count == 0 ? string.Empty : composition.ToString();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            UniTextNativeInput.RegisterBackend(static () => new ManagedInputBackend(), FallbackPriority);
        }

        /// <summary>Opens one reporter epoch.</summary>
        public void OpenInput(in NativeInputOpenRequest request, NativeInputReporter value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (reporter != null)
                throw new InvalidOperationException("The managed input producer is already open.");
            reporter = value;
            quiescedReporter = null;
        }

        /// <summary>Updates Unity's IME candidate position.</summary>
        public void SetCursorScreenPos(Vector2 screenPos, float lineHeight)
        {
            UniTextNativeInput.SetImeCursorPosition(screenPos);
        }

        /// <summary>Accepts focused-editor geometry without platform presentation.</summary>
        public void SetInputFieldRect(Rect screenRect)
        {
        }

        /// <summary>Drains the current frame, resolves composition, and completes the barrier.</summary>
        public void QuiesceInput(NativeInputReporter value,
            NativeCompositionDisposition disposition, Action quiesced)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (quiesced == null) throw new ArgumentNullException(nameof(quiesced));
            if ((uint)disposition > (uint)NativeCompositionDisposition.Cancel)
                throw new ArgumentOutOfRangeException(nameof(disposition));
            if (!ReferenceEquals(reporter, value))
                throw new InvalidOperationException("The reporter is not bound to the managed producer.");
            FlushPendingInput();
            if (composing)
            {
                if (disposition == NativeCompositionDisposition.Commit && lastComposition.Length > 0)
                    value.ReportTextInput(lastComposition);
                if (disposition != NativeCompositionDisposition.Preserve)
                {
                    ResetComposition();
                    value.ReportCompositionEnded();
                }
            }
            reporter = null;
            quiescedReporter = value;
            quiesced();
        }

        /// <summary>Releases a quiesced epoch.</summary>
        public void CloseInput(NativeInputReporter value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!ReferenceEquals(quiescedReporter, value))
                throw new InvalidOperationException("The reporter is not bound to a quiesced managed producer.");
            quiescedReporter = null;
            ResetComposition();
        }

        /// <summary>Abandons an epoch and clears composition state.</summary>
        public void AbortInput(NativeInputReporter value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!ReferenceEquals(reporter, value) && !ReferenceEquals(quiescedReporter, value))
                throw new InvalidOperationException("The reporter is not bound to the managed producer.");
            if (ReferenceEquals(reporter, value)) reporter = null;
            if (ReferenceEquals(quiescedReporter, value)) quiescedReporter = null;
            ResetComposition();
        }

        /// <summary>Gets whether pushed document windows are consumed.</summary>
        public bool WantsTextContext => false;

        /// <summary>Accepts a document-window update without retaining it.</summary>
        public void PushTextContext(string text, int windowStart, int selectionStart, int selectionEnd, bool forceRestart)
        {
        }

        /// <summary>
        /// Reports at most one ordered snapshot of Unity input per rendered frame.
        /// </summary>
        public void FlushPendingInput()
        {
            var active = reporter;
            if (active == null) return;
            int frame = Time.frameCount;
            if (frame == lastPumpFrame) return;
            lastPumpFrame = frame;

            PumpComposition(active);
            if (!ReferenceEquals(reporter, active)) return;
            PumpEvents(active);
        }

        /// <summary>Releases the epoch and composition state.</summary>
        public void Dispose()
        {
            reporter = null;
            quiescedReporter = null;
            ResetComposition();
        }

        private void ResetComposition()
        {
            composing = false;
            lastComposition = string.Empty;
        }

        private void PumpEvents(NativeInputReporter active)
        {
            int written = 0;
            while (Event.PopEvent(guiEvent))
            {
                if (guiEvent.rawType != EventType.KeyDown) continue;

                var mods = ToNativeModifiers(guiEvent.modifiers);
                bool shortcut = (mods & ShortcutModifiers) != 0 && (mods & NativeModifiers.Alt) == 0;
                char c = guiEvent.character;

                if (!shortcut && c != '\0' && !UnicodeData.IsC0ControlOrDelete(c))
                {
                    if (textFilterBuffer.Length <= written)
                        Array.Resize(ref textFilterBuffer, textFilterBuffer.Length * 2);
                    textFilterBuffer[written++] = c;
                    continue;
                }

                if (composing) continue;
                if (!NativeKeyCodeMap.TryFromUnity(guiEvent.keyCode, out var native)) continue;
                if (!shortcut && NativeKeyCodeExtensions.IsLetter(native))
                {
                    if ((mods & (ShortcutModifiers | NativeModifiers.Alt)) == 0) ReportTextStarvation();
                    continue;
                }

                FlushText(active, ref written);
                if (!ReferenceEquals(reporter, active)) return;
                active.ReportKeyDown(native, mods);
                if (!ReferenceEquals(reporter, active)) return;
            }
            FlushText(active, ref written);
        }

        private void FlushText(NativeInputReporter active, ref int written)
        {
            if (written == 0) return;
            var text = new string(textFilterBuffer, 0, written);
            written = 0;
            starvedKeystrokes = 0;
            active.ReportTextInput(text);
        }

        private static NativeModifiers ToNativeModifiers(EventModifiers modifiers)
        {
            var mods = NativeModifiers.None;
            if ((modifiers & EventModifiers.Shift) != 0) mods |= NativeModifiers.Shift;
            if ((modifiers & EventModifiers.Control) != 0) mods |= NativeModifiers.Ctrl;
            if ((modifiers & EventModifiers.Alt) != 0) mods |= NativeModifiers.Alt;
            if ((modifiers & EventModifiers.Command) != 0) mods |= NativeModifiers.Cmd;
            return mods;
        }

        private void PumpComposition(NativeInputReporter active)
        {
            var comp = CompositionString;
            if (string.Equals(comp, lastComposition, StringComparison.Ordinal)) return;
            lastComposition = comp;

            if (comp.Length > 0)
            {
                composing = true;
                int clauseCount = CompositionClause.FillFallback(ref clauseBuffer, comp.Length);
                var data = new CompositionData
                {
                    text = comp.AsSpan(),
                    clauses = new ReadOnlySpan<CompositionClause>(clauseBuffer, 0, clauseCount),
                    cursorPosition = comp.Length,
                };
                active.ReportCompositionChanged(data);
            }
            else if (composing)
            {
                composing = false;
                active.ReportCompositionEnded();
            }
        }

        private void ReportTextStarvation()
        {
            if (starvationReported) return;
            if (++starvedKeystrokes < TextStarvationLimit) return;
            starvationReported = true;
            Debug.LogError(
                "[UniText] Key events are arriving but carry no characters, so those keystrokes type " +
                "nothing. Text comes from the operating system's key events, which this platform leaves " +
                "empty for them.");
        }

        private static string CompositionString
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.compositionString ?? string.Empty;
#elif ENABLE_INPUT_SYSTEM
                EnsureImeSubscription();
                return imeCompositionCache;
#else
                return string.Empty;
#endif
            }
        }
    }
}
