#if UNITY_WEBGL && !UNITY_EDITOR

using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace LightSide
{
    internal sealed class NativeInputWebGL : INativeInputBackend, INativeFieldBackend
    {
        private static readonly KeyboardEventDelegate keyboardEventDelegate = OnKeyboardEvent;
        private static readonly StringDelegate textInputDelegate = OnTextInput;
        private static readonly TextReplacementDelegate textReplacementDelegate = OnTextReplacement;
        private static readonly VoidDelegate compositionEndedDelegate = OnCompositionEnded;
        private static readonly CompositionDelegate compositionDelegate = OnComposition;
        private static readonly KeyDelegate keyDelegate = OnKey;
        private static readonly FieldEditDelegate fieldEditDelegate = OnFieldEdit;
        private static readonly FieldCompositionDelegate fieldCompositionDelegate = OnFieldComposition;
        private static readonly FieldSelectionDelegate fieldSelectionDelegate = OnFieldSelection;
        private static readonly FieldActionDelegate fieldActionDelegate = OnFieldAction;
        private static readonly FieldQuiescedDelegate fieldQuiescedDelegate = OnInputQuiesced;
        private static readonly InputFaultDelegate inputFaultDelegate = OnInputFault;

        private static int applePlatform = -1;
        private static NativeInputWebGL instance;

        private bool disposed;
        private NativeInputReporter reporter;
        private NativeInputReporter quiescedReporter;
        private NativeInputReporter openingReporter;
        private NativeInputReporter failedOpenReporter;
        private NativeInputReporter keyboardReporter;
        private NativeInputReporter pendingQuiescenceReporter;
        private Action pendingQuiescence;
        private bool activeNativeField;
        private bool openingNativeField;
        private bool quiescedNativeField;
        private int pendingQuiescenceRequestId;
        private int nextQuiescenceRequestId;

        internal static bool IsApplePlatform
        {
            get
            {
                if (applePlatform < 0) applePlatform = UniTextNativeInput_IsApplePlatform();
                return applePlatform != 0;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            WebGLInput.captureAllKeyboardInput = false;
            UniTextNativeInput.RegisterBackend(Create, 0);
        }

        private static INativeInputBackend Create()
        {
            UniTextNativeInput_RegisterKeyboardCallback(keyboardEventDelegate);
            UniTextNativeInput_RegisterCompositionCallbacks(
                compositionDelegate, compositionEndedDelegate, textInputDelegate,
                textReplacementDelegate, keyDelegate);
            UniTextNativeInput_RegisterNativeFieldCallbacks(
                fieldEditDelegate, fieldCompositionDelegate, fieldSelectionDelegate,
                fieldActionDelegate, fieldQuiescedDelegate, inputFaultDelegate);
            var backend = new NativeInputWebGL();
            instance = backend;
            return backend;
        }

        public void SetCursorScreenPos(Vector2 screenPos, float lineHeight)
        {
            ThrowIfDisposed();
            UniTextNativeInput_SetCaretScreenPos(screenPos.x, screenPos.y, lineHeight);
        }

        public void SetInputFieldRect(Rect screenRect)
        {
            ThrowIfDisposed();
            UniTextNativeInput_SetInputFieldRect(
                screenRect.xMin, screenRect.yMin, screenRect.width, screenRect.height);
        }

        public void OpenInput(in NativeInputOpenRequest request, NativeInputReporter value)
        {
            BeginOpen(value, false);
            failedOpenReporter = value;
            UniTextNativeInput_ShowTransparentInput(value.SessionId,
                request.SecureTextEntry ? 1 : 0,
                MapEnterKeyHint(request.Keyboard, request.AcceptsNewlines),
                request.ShowSoftwareKeyboard ? 1 : 0);
            failedOpenReporter = null;
            CompleteOpen(value, false);
        }

        public void OpenNativeField(in NativeFieldOpenRequest request, NativeInputReporter value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (request.SessionId != value.SessionId)
                throw new ArgumentException(
                    "The native field request does not belong to the reporter.", nameof(request));
            BeginOpen(value, true);
            failedOpenReporter = value;
            var presentation = request.Presentation;
            UniTextNativeInput_ShowNativeField(
                request.SessionId, request.AuthorityRevision, request.Shape.Wraps ? 1 : 0,
                request.Shape.AcceptsNewlines ? 1 : 0, (int)request.Shape.ReturnKey,
                request.Text, request.SelectionStart, request.SelectionEnd,
                presentation.PresenterId, presentation.Placeholder ?? string.Empty,
                presentation.Identifier ?? string.Empty, presentation.PresenterData ?? string.Empty,
                MapInputMode(request.Config),
                MapEnterKeyHint(request.Config, request.Shape.AcceptsNewlines),
                MapAutocapitalize(request.Config), MapAutocorrect(request.Config),
                MapSpellcheck(request.Config), MapAutocomplete(request.Config),
                request.PasswordMode ? 1 : 0, request.ReadOnly ? 1 : 0,
                request.CopyAllowed ? 1 : 0);
            failedOpenReporter = null;
            CompleteOpen(value, true);
        }

        public void ReconcileNativeField(int sessionId, int sourceNativeRevision,
            int authorityRevision, string text, int selectionStart, int selectionEnd)
        {
            ThrowIfDisposed();
            UniTextNativeInput_ReconcileNativeField(sessionId, sourceNativeRevision,
                authorityRevision, text, selectionStart, selectionEnd);
        }

        public void UpdateNativeField(in NativeFieldUpdateRequest request)
        {
            ThrowIfDisposed();
            var presentation = request.Presentation;
            UniTextNativeInput_UpdateNativeField(request.SessionId, request.AuthorityRevision,
                request.Shape.Wraps ? 1 : 0, request.Shape.AcceptsNewlines ? 1 : 0,
                (int)request.Shape.ReturnKey,
                presentation.PresenterId, presentation.Placeholder ?? string.Empty,
                presentation.Identifier ?? string.Empty, presentation.PresenterData ?? string.Empty,
                MapInputMode(request.Config),
                MapEnterKeyHint(request.Config, request.Shape.AcceptsNewlines),
                MapAutocapitalize(request.Config), MapAutocorrect(request.Config),
                MapSpellcheck(request.Config), MapAutocomplete(request.Config),
                request.PasswordMode ? 1 : 0, request.ReadOnly ? 1 : 0,
                request.CopyAllowed ? 1 : 0);
        }

        public void FocusNativeField(int sessionId, NativeInputReporter value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (sessionId != value.SessionId)
                throw new ArgumentException(
                    "The native field session does not belong to the reporter.", nameof(sessionId));
            if (quiescedReporter == null || !quiescedNativeField ||
                quiescedReporter.SessionId != sessionId)
                throw new InvalidOperationException(
                    "The WebGL native field producer is not available for inheritance.");
            BeginOpen(value, true);
            failedOpenReporter = value;
            UniTextNativeInput_FocusNativeField(sessionId);
            failedOpenReporter = null;
            CompleteOpen(value, true);
        }

        public void QuiesceInput(NativeInputReporter value,
            NativeCompositionDisposition disposition, Action quiesced)
        {
            ThrowIfDisposed();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (quiesced == null) throw new ArgumentNullException(nameof(quiesced));
            if ((uint)disposition > (uint)NativeCompositionDisposition.Cancel)
                throw new ArgumentOutOfRangeException(nameof(disposition));
            if (!ReferenceEquals(reporter, value))
                throw new InvalidOperationException(
                    "The reporter is not bound to the active WebGL input producer.");
            if (pendingQuiescenceReporter != null)
                throw new InvalidOperationException(
                    "The WebGL input producer is already quiescing.");
            if (nextQuiescenceRequestId == int.MaxValue)
                throw new InvalidOperationException(
                    "The WebGL input producer exhausted its quiescence request space.");
            int requestId = ++nextQuiescenceRequestId;
            pendingQuiescenceReporter = value;
            pendingQuiescence = quiesced;
            pendingQuiescenceRequestId = requestId;
            try
            {
                UniTextNativeInput_QuiesceInput(
                    value.SessionId, (int)disposition, requestId);
            }
            catch
            {
                if (ReferenceEquals(pendingQuiescenceReporter, value))
                    ClearPendingQuiescence();
                throw;
            }
        }

        public void CloseInput(NativeInputReporter value)
        {
            ThrowIfDisposed();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!ReferenceEquals(quiescedReporter, value))
                throw new InvalidOperationException(
                    "The reporter is not bound to a quiesced WebGL input producer.");
            if (quiescedNativeField)
                UniTextNativeInput_CloseNativeField(value.SessionId);
            else
                UniTextNativeInput_HideKeyboard();
            quiescedReporter = null;
            quiescedNativeField = false;
        }

        public void AbortInput(NativeInputReporter value)
        {
            ThrowIfDisposed();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(openingReporter, value))
            {
                bool failedTransaction = ReferenceEquals(failedOpenReporter, value);
                openingReporter = null;
                openingNativeField = false;
                if (failedTransaction) failedOpenReporter = null;
                if (!failedTransaction) UniTextNativeInput_AbortInput(value.SessionId);
                return;
            }
            bool active = ReferenceEquals(reporter, value);
            bool quiesced = ReferenceEquals(quiescedReporter, value);
            if (!active && !quiesced)
                throw new InvalidOperationException(
                    "The reporter is not bound to the WebGL input producer.");
            if (active)
            {
                reporter = null;
                activeNativeField = false;
            }
            if (quiesced)
            {
                quiescedReporter = null;
                quiescedNativeField = false;
            }
            if (ReferenceEquals(pendingQuiescenceReporter, value))
                ClearPendingQuiescence();
            UniTextNativeInput_AbortInput(value.SessionId);
        }

        public void FlushPendingInput()
        {
            ThrowIfDisposed();
        }

        public void PushTextContext(string text, int windowStart, int selectionStart,
            int selectionEnd, bool forceRestart)
        {
            ThrowIfDisposed();
        }
        public bool WantsTextContext => false;

        private NativeInputReporter EditingReporter => openingReporter ?? reporter;

        private NativeInputReporter KeyboardReporter =>
            openingReporter ?? reporter ?? quiescedReporter ?? keyboardReporter;

        private void BeginOpen(NativeInputReporter value, bool nativeField)
        {
            ThrowIfDisposed();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (reporter != null || openingReporter != null)
                throw new InvalidOperationException(
                    "The WebGL input producer is already open or opening.");
            if (failedOpenReporter != null)
                throw new InvalidOperationException(
                    "The failed WebGL input transaction has not been aborted.");
            openingReporter = value;
            openingNativeField = nativeField;
        }

        private void CompleteOpen(NativeInputReporter value, bool nativeField)
        {
            if (!ReferenceEquals(openingReporter, value))
                throw new InvalidOperationException(
                    "The WebGL input producer lost its opening reporter.");
            openingReporter = null;
            openingNativeField = false;
            failedOpenReporter = null;
            reporter = value;
            activeNativeField = nativeField;
            quiescedReporter = null;
            quiescedNativeField = false;
            keyboardReporter = value;
        }

        private void ClearPendingQuiescence()
        {
            pendingQuiescenceReporter = null;
            pendingQuiescence = null;
            pendingQuiescenceRequestId = 0;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(NativeInputWebGL));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (instance == this) instance = null;
            reporter = null;
            quiescedReporter = null;
            openingReporter = null;
            failedOpenReporter = null;
            keyboardReporter = null;
            activeNativeField = false;
            openingNativeField = false;
            quiescedNativeField = false;
            ClearPendingQuiescence();
            UniTextNativeInput_Shutdown();
        }

        private static string MapInputMode(NativeKeyboardConfig config)
            => config?.KeyboardType switch
            {
                KeyboardType.NumbersAndPunctuation => "numeric",
                KeyboardType.URL => "url",
                KeyboardType.NumberPad => "numeric",
                KeyboardType.PhonePad => "tel",
                KeyboardType.EmailAddress => "email",
                KeyboardType.DecimalPad => "decimal",
                KeyboardType.WebSearch => "search",
                _ => string.Empty,
            };

        /// <summary>
        /// Maps the declared return key to its enterkeyhint counterpart. A field that accepts line
        /// breaks always gets the plain key: it spends that key on newlines, so any other label
        /// would promise an action the key does not perform.
        /// </summary>
        private static string MapEnterKeyHint(NativeKeyboardConfig config, bool acceptsNewlines)
        {
            if (acceptsNewlines) return "enter";
            return config?.ReturnKeyType switch
            {
                ReturnKeyType.Go => "go",
                ReturnKeyType.Search => "search",
                ReturnKeyType.Send => "send",
                ReturnKeyType.Next => "next",
                ReturnKeyType.Previous => "previous",
                ReturnKeyType.Enter => "enter",
                ReturnKeyType.Done => "done",
                _ => "done",
            };
        }

        private static string MapAutocapitalize(NativeKeyboardConfig config)
            => config?.AutoCapitalization switch
            {
                AutoCapitalization.None => "off",
                AutoCapitalization.Words => "words",
                AutoCapitalization.Sentences => "sentences",
                AutoCapitalization.AllCharacters => "characters",
                _ => string.Empty,
            };

        private static string MapAutocorrect(NativeKeyboardConfig config)
            => config?.AutoCorrection switch
            {
                AutoCorrection.Enabled => "on",
                AutoCorrection.Disabled => "off",
                _ => string.Empty,
            };

        private static string MapSpellcheck(NativeKeyboardConfig config)
            => config?.SpellChecking switch
            {
                SpellChecking.Enabled => "true",
                SpellChecking.Disabled => "false",
                _ => string.Empty,
            };

        private static string MapAutocomplete(NativeKeyboardConfig config)
            => config?.AutofillHint switch
            {
                AutofillHint.None => "off",
                AutofillHint.Username => "username",
                AutofillHint.Password => "current-password",
                AutofillHint.NewPassword => "new-password",
                AutofillHint.OneTimeCode => "one-time-code",
                AutofillHint.EmailAddress => "email",
                AutofillHint.TelephoneNumber => "tel",
                AutofillHint.Name => "name",
                AutofillHint.GivenName => "given-name",
                AutofillHint.FamilyName => "family-name",
                AutofillHint.PostalAddress => "street-address",
                AutofillHint.PostalCode => "postal-code",
                AutofillHint.CreditCardNumber => "cc-number",
                AutofillHint.URL => "url",
                _ => string.Empty,
            };

        [MonoPInvokeCallback(typeof(KeyboardEventDelegate))]
        private static void OnKeyboardEvent(int phase, float x, float y, float width, float height,
            float duration, int easing, float fraction, int frameSynced)
        {
            var target = instance?.KeyboardReporter;
            if (target == null) return;
            var value = new KeyboardEvent
            {
                phase = (KeyboardEventPhase)phase,
                area = new Rect(x, y, width, height),
                animationDuration = duration,
                easing = (KeyboardEasing)easing,
                animationFraction = fraction,
                hasFrameSyncedAnimation = frameSynced != 0,
            };
            target.ReportKeyboardEvent(in value);
        }

        [MonoPInvokeCallback(typeof(StringDelegate))]
        private static void OnTextInput(IntPtr text)
        {
            var target = instance?.EditingReporter;
            if (target == null) return;
            target.ReportTextInput(Utf8(text));
        }

        [MonoPInvokeCallback(typeof(TextReplacementDelegate))]
        private static void OnTextReplacement(int direction, IntPtr text)
        {
            var target = instance?.EditingReporter;
            if (target == null) return;
            if (direction < -1 || direction > 1)
                throw new ArgumentOutOfRangeException(nameof(direction));
            var selection = target.Context.GetCharSelection();
            if (direction == 0 || selection.length != 0)
            {
                target.ReportTextReplacement(selection.start, selection.length, Utf8(text));
                return;
            }
            if (direction < 0) target.ReportDeleteBackward();
            else target.ReportKeyDown(NativeKeyCode.Delete, NativeModifiers.None);
        }

        [MonoPInvokeCallback(typeof(VoidDelegate))]
        private static void OnCompositionEnded()
            => instance?.EditingReporter?.ReportCompositionEnded();

        [MonoPInvokeCallback(typeof(CompositionDelegate))]
        private static void OnComposition(IntPtr text, int length, int cursor)
        {
            var target = instance?.EditingReporter;
            if (target == null) return;
            var value = Utf8(text);
            if (value.Length == 0)
            {
                target.ReportCompositionChanged(new CompositionData
                {
                    text = ReadOnlySpan<char>.Empty,
                    clauses = ReadOnlySpan<CompositionClause>.Empty,
                    cursorPosition = 0,
                });
                return;
            }
            Span<CompositionClause> clauses = stackalloc CompositionClause[1];
            clauses[0] = new CompositionClause
            {
                startOffset = 0,
                endOffset = value.Length,
                style = CompositionClauseStyle.Unconverted,
            };
            target.ReportCompositionChanged(new CompositionData
            {
                text = value.AsSpan(),
                clauses = clauses,
                cursorPosition = cursor,
            });
        }

        [MonoPInvokeCallback(typeof(KeyDelegate))]
        private static void OnKey(int key, int modifiers)
            => instance?.EditingReporter?.ReportKeyDown(
                (NativeKeyCode)key, (NativeModifiers)modifiers);

        [MonoPInvokeCallback(typeof(FieldEditDelegate))]
        private static void OnFieldEdit(int sessionId, int nativeRevision, int authorityRevision,
            int start, int length, IntPtr text, int selectionStart, int selectionEnd)
        {
            var target = NativeFieldCallbackReporter(sessionId);
            if (target == null) return;
            target.ReportFieldEdit(nativeRevision, authorityRevision,
                start, length, Utf8(text), selectionStart, selectionEnd);
        }

        [MonoPInvokeCallback(typeof(FieldCompositionDelegate))]
        private static void OnFieldComposition(int sessionId, int nativeRevision, int authorityRevision,
            int phase, int start, int length, IntPtr text, int cursor)
        {
            var target = NativeFieldCallbackReporter(sessionId);
            if (target == null) return;
            if ((uint)phase > (uint)NativeFieldCompositionPhase.Cancel)
                throw new ArgumentOutOfRangeException(nameof(phase));
            target.ReportFieldComposition(nativeRevision, authorityRevision,
                (NativeFieldCompositionPhase)phase, start, length, Utf8(text), cursor);
        }

        [MonoPInvokeCallback(typeof(FieldSelectionDelegate))]
        private static void OnFieldSelection(int sessionId, int nativeRevision, int authorityRevision,
            int start, int end)
        {
            var target = NativeFieldCallbackReporter(sessionId);
            if (target == null) return;
            target.ReportFieldSelection(nativeRevision, authorityRevision, start, end);
        }

        [MonoPInvokeCallback(typeof(FieldActionDelegate))]
        private static void OnFieldAction(int sessionId, int nativeRevision,
            int authorityRevision, IntPtr action, int modifiers)
        {
            var target = NativeFieldCallbackReporter(sessionId);
            if (target == null) return;
            target.ReportFieldAction(nativeRevision, authorityRevision, Utf8(action),
                (NativeModifiers)modifiers);
        }

        [MonoPInvokeCallback(typeof(FieldQuiescedDelegate))]
        private static void OnInputQuiesced(int sessionId, int nativeRevision,
            int authorityRevision, int requestId)
        {
            var backend = instance;
            var target = backend?.pendingQuiescenceReporter;
            if (target == null) return;
            if (!ReferenceEquals(backend.reporter, target) || target.SessionId != sessionId ||
                backend.pendingQuiescenceRequestId != requestId)
                throw new InvalidOperationException(
                    "The WebGL native input quiescence callback does not match its pending request.");
            bool nativeField = backend.activeNativeField;
            if (nativeField)
            {
                target.ReportFieldQuiesced(nativeRevision, authorityRevision);
            }
            else if (nativeRevision <= 0 || authorityRevision != 1)
            {
                throw new InvalidOperationException(
                    "The WebGL transparent input barrier has an invalid revision envelope.");
            }
            var completion = backend.pendingQuiescence;
            backend.ClearPendingQuiescence();
            backend.reporter = null;
            backend.activeNativeField = false;
            backend.quiescedReporter = target;
            backend.quiescedNativeField = nativeField;
            completion();
        }

        [MonoPInvokeCallback(typeof(InputFaultDelegate))]
        private static void OnInputFault(int sessionId, IntPtr message)
        {
            var backend = instance;
            if (backend == null || backend.disposed) return;
            var target = backend.openingReporter ?? backend.reporter ?? backend.quiescedReporter;
            if (target == null || target.SessionId != sessionId)
                throw new InvalidOperationException(
                    "The WebGL native input fault does not match a live reporter.");
            string reason = Utf8(message);
            target.ReportFault(new InvalidOperationException(
                reason.Length == 0
                    ? $"WebGL native input session {sessionId} failed without detail."
                    : reason));
        }

        private static NativeInputReporter NativeFieldCallbackReporter(int sessionId)
        {
            var backend = instance;
            if (backend == null || backend.disposed) return null;
            if (backend.openingNativeField && backend.openingReporter?.SessionId == sessionId)
                return backend.openingReporter;
            return backend.activeNativeField && backend.reporter?.SessionId == sessionId
                ? backend.reporter
                : null;
        }

        private static string Utf8(IntPtr value)
            => value == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(value) ?? string.Empty;

        private delegate void KeyboardEventDelegate(int phase, float x, float y, float width,
            float height, float duration, int easing, float fraction, int frameSynced);
        private delegate void StringDelegate(IntPtr value);
        private delegate void TextReplacementDelegate(int direction, IntPtr value);
        private delegate void VoidDelegate();
        private delegate void CompositionDelegate(IntPtr text, int length, int cursor);
        private delegate void KeyDelegate(int key, int modifiers);
        private delegate void FieldEditDelegate(int sessionId, int nativeRevision, int authorityRevision,
            int start, int length, IntPtr text, int selectionStart, int selectionEnd);
        private delegate void FieldCompositionDelegate(int sessionId, int nativeRevision,
            int authorityRevision, int phase, int start, int length, IntPtr text, int cursor);
        private delegate void FieldSelectionDelegate(int sessionId, int nativeRevision,
            int authorityRevision, int start, int end);
        private delegate void FieldActionDelegate(int sessionId, int nativeRevision,
            int authorityRevision, IntPtr action, int modifiers);
        private delegate void FieldQuiescedDelegate(int sessionId, int nativeRevision,
            int authorityRevision, int requestId);
        private delegate void InputFaultDelegate(int sessionId, IntPtr message);

        private const string DllName = "__Internal";

        [DllImport(DllName)] private static extern void UniTextNativeInput_RegisterKeyboardCallback(KeyboardEventDelegate callback);
        [DllImport(DllName)] private static extern void UniTextNativeInput_RegisterCompositionCallbacks(CompositionDelegate composition, VoidDelegate ended, StringDelegate text, TextReplacementDelegate replacement, KeyDelegate key);
        [DllImport(DllName)] private static extern void UniTextNativeInput_RegisterNativeFieldCallbacks(FieldEditDelegate edit, FieldCompositionDelegate composition, FieldSelectionDelegate selection, FieldActionDelegate action, FieldQuiescedDelegate quiesced, InputFaultDelegate fault);
        [DllImport(DllName)] private static extern void UniTextNativeInput_ShowTransparentInput(int sessionId, int secureEntry, string enterKeyHint, int showSoftwareKeyboard);
        [DllImport(DllName)] private static extern void UniTextNativeInput_ShowNativeField(int sessionId, int authorityRevision, int wraps, int acceptsNewlines, int returnKey, string text, int selectionStart, int selectionEnd, string presenterId, string placeholder, string identifier, string presenterData, string inputMode, string enterKeyHint, string autocapitalize, string autocorrect, string spellcheck, string autocomplete, int secureEntry, int readOnly, int copyAllowed);
        [DllImport(DllName)] private static extern void UniTextNativeInput_ReconcileNativeField(int sessionId, int sourceNativeRevision, int authorityRevision, string text, int selectionStart, int selectionEnd);
        [DllImport(DllName)] private static extern void UniTextNativeInput_UpdateNativeField(int sessionId, int authorityRevision, int wraps, int acceptsNewlines, int returnKey, string presenterId, string placeholder, string identifier, string presenterData, string inputMode, string enterKeyHint, string autocapitalize, string autocorrect, string spellcheck, string autocomplete, int secureEntry, int readOnly, int copyAllowed);
        [DllImport(DllName)] private static extern void UniTextNativeInput_CloseNativeField(int sessionId);
        [DllImport(DllName)] private static extern void UniTextNativeInput_FocusNativeField(int sessionId);
        [DllImport(DllName)] private static extern void UniTextNativeInput_QuiesceInput(int sessionId, int disposition, int requestId);
        [DllImport(DllName)] private static extern void UniTextNativeInput_AbortInput(int sessionId);
        [DllImport(DllName)] private static extern void UniTextNativeInput_HideKeyboard();
        [DllImport(DllName)] private static extern void UniTextNativeInput_Shutdown();
        [DllImport(DllName)] private static extern int UniTextNativeInput_IsApplePlatform();
        [DllImport(DllName)] private static extern void UniTextNativeInput_SetInputFieldRect(float x, float y, float width, float height);
        [DllImport(DllName)] private static extern void UniTextNativeInput_SetCaretScreenPos(float x, float y, float lineHeight);
    }
}

#endif
