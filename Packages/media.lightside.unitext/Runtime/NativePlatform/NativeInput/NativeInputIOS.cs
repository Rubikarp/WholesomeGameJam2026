#if UNITY_IOS && !UNITY_EDITOR

using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace LightSide
{
    internal sealed class NativeInputIOS : INativeInputBackend, INativeFieldBackend
    {
        bool disposed;
        bool hasPendingFloatingCursorPoint;
        Vector2 pendingFloatingCursorScreenPoint;
        string contextTextCache;
        int contextWindowStartCache;
        int contextVersion;
        NativeInputReporter reporter;
        NativeInputReporter quiescedReporter;
        NativeInputReporter openingReporter;
        NativeInputReporter failedOpenReporter;
        NativeInputReporter keyboardReporter;
        NativeInputReporter pendingQuiescenceReporter;
        Action pendingQuiescence;
        bool activeNativeField;
        bool openingNativeField;
        bool quiescedNativeField;
        int pendingQuiescenceRequestId;
        int nextQuiescenceRequestId;

        static NativeInputIOS instance;

        static readonly TextInputDelegate textInputDelegate = OnTextInputCallback;
        static readonly TextReplacementDelegate textReplacementDelegate = OnTextReplacementCallback;
        static readonly KeyDownDelegate keyDownDelegate = OnKeyDownCallback;
        static readonly CompositionDelegate compositionDelegate = OnCompositionCallback;
        static readonly CompositionEndedDelegate compositionEndedDelegate = OnCompositionEndedCallback;
        static readonly KeyboardEventDelegate keyboardEventDelegate = OnKeyboardEventCallback;
        static readonly NativeFieldEditDelegate nativeFieldEditDelegate = OnNativeFieldEditCallback;
        static readonly NativeFieldCompositionDelegate nativeFieldCompositionDelegate = OnNativeFieldCompositionCallback;
        static readonly NativeFieldSelectionDelegate nativeFieldSelectionDelegate = OnNativeFieldSelectionCallback;
        static readonly NativeFieldActionDelegate nativeFieldActionDelegate = OnNativeFieldActionCallback;
        static readonly NativeInputQuiescedDelegate nativeInputQuiescedDelegate = OnNativeInputQuiescedCallback;
        static readonly NativeInputFaultDelegate nativeInputFaultDelegate = OnNativeInputFaultCallback;
        static readonly FloatingCursorPointDelegate floatingCursorPointDelegate = OnFloatingCursorPoint;
        static readonly SetSelectionCharRangeDelegate setSelectionCharRangeDelegate = OnSetSelectionCharRange;
        static readonly GetCharRangeRectDelegate getCharRangeRectDelegate = OnGetCharRangeRect;
        static readonly ClosestCharAtPointDelegate closestCharAtPointDelegate = OnClosestCharAtPoint;
        static readonly WritingDirectionDelegate writingDirectionDelegate = OnWritingDirection;

        static CompositionClause[] compositionClauseBuffer = new CompositionClause[16];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            UniTextNativeInput.RegisterBackend(Create, 0);
        }

        static INativeInputBackend Create()
        {
            var backend = new NativeInputIOS();
            instance = backend;
            UniTextNativeInput_Init(
                textInputDelegate,
                textReplacementDelegate,
                keyDownDelegate,
                compositionDelegate,
                compositionEndedDelegate,
                keyboardEventDelegate,
                nativeFieldEditDelegate,
                nativeFieldCompositionDelegate,
                nativeFieldSelectionDelegate,
                nativeFieldActionDelegate,
                nativeInputQuiescedDelegate,
                nativeInputFaultDelegate,
                floatingCursorPointDelegate);
            UniTextNativeInput_RegisterSelectionCallback(setSelectionCharRangeDelegate);
            UniTextNativeInput_RegisterGeometryQueries(
                getCharRangeRectDelegate,
                closestCharAtPointDelegate,
                writingDirectionDelegate);
            return backend;
        }

        public void OpenInput(in NativeInputOpenRequest request, NativeInputReporter value)
        {
            BeginOpen(value, false);
            failedOpenReporter = value;
            var args = CreateShowKeyboardArgs(request.Keyboard, request.SecureTextEntry,
                new NativeFieldShape(false, request.AcceptsNewlines,
                    request.Keyboard?.ReturnKeyType ?? ReturnKeyType.Default));
            args.showSoftwareKeyboard = request.ShowSoftwareKeyboard ? 1 : 0;
            args.sessionId = value.SessionId;
            args.passwordRules = AllocOptionalUtf8(request.Keyboard?.PasswordRules);
            try
            {
                int shown = UniTextNativeInput_ShowKeyboard(ref args);
                if (shown == 0)
                {
                    throw new InvalidOperationException("The iOS native input could not be opened.");
                }
                failedOpenReporter = null;
                CompleteOpen(value, false);
            }
            finally
            {
                FreeOptionalUtf8(args.passwordRules);
            }
        }

        public void SetCursorScreenPos(Vector2 screenPos, float lineHeight)
        {
            ThrowIfDisposed();
            UniTextNativeInput_SetCursorPos(screenPos.x, screenPos.y, 1f, lineHeight);
        }

        public void SetInputFieldRect(Rect screenRect)
        {
            ThrowIfDisposed();
            UniTextNativeInput_SetInputFieldRect(
                screenRect.xMin, screenRect.yMin, screenRect.width, screenRect.height);
        }

        public void FlushPendingInput()
        {
            ThrowIfDisposed();
            if (!hasPendingFloatingCursorPoint) return;
            hasPendingFloatingCursorPoint = false;
            var target = EditingReporter;
            if (target == null) return;
            int charIndex = target.Context.HitTestChar(pendingFloatingCursorScreenPoint);
            if (charIndex >= 0) target.ReportSelectionChanged(charIndex, charIndex);
        }

        public bool WantsTextContext => true;

        public unsafe void PushTextContext(string text, int windowStart,
            int selectionStart, int selectionEnd, bool forceRestart)
        {
            ThrowIfDisposed();
            text ??= string.Empty;
            bool textChanged = !ReferenceEquals(text, contextTextCache)
                               || windowStart != contextWindowStartCache;
            if (textChanged || forceRestart)
            {
                if (contextVersion == int.MaxValue)
                    throw new InvalidOperationException("The iOS text-context version space is exhausted.");
                contextVersion++;
            }
            if (textChanged)
            {
                fixed (char* chars = text)
                {
                    UniTextNativeInput_SetTextContext(
                        contextVersion, (IntPtr)chars, text.Length, windowStart,
                        selectionStart, selectionEnd, forceRestart ? 1 : 0);
                }
                contextTextCache = text;
                contextWindowStartCache = windowStart;
                return;
            }

            UniTextNativeInput_SetTextContext(
                contextVersion, IntPtr.Zero, -1, windowStart,
                selectionStart, selectionEnd, forceRestart ? 1 : 0);
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
            var args = CreateShowKeyboardArgs(request.Config, request.PasswordMode, in request.Shape);
            args.showSoftwareKeyboard = 1;
            args.useNativeField = 1;
            args.readOnly = request.ReadOnly ? 1 : 0;
            args.copyAllowed = request.CopyAllowed ? 1 : 0;
            args.selectionStart = request.SelectionStart;
            args.selectionEnd = request.SelectionEnd;
            args.sessionId = request.SessionId;
            args.authorityRevision = request.AuthorityRevision;
            try
            {
                args.passwordRules = AllocOptionalUtf8(request.Config?.PasswordRules);
                args.initialText = AllocOptionalUtf8(request.Text);
                args.accessibilityIdentifier = AllocOptionalUtf8(presentation.Identifier);
                args.placeholder = AllocOptionalUtf8(presentation.Placeholder);
                args.presenterId = AllocOptionalUtf8(presentation.PresenterId);
                args.presenterData = AllocOptionalUtf8(presentation.PresenterData);
                int shown = UniTextNativeInput_ShowKeyboard(ref args);
                if (shown == 0)
                    throw new InvalidOperationException(
                        $"The iOS native field could not be created with presenter '{presentation.PresenterId}'.");
                failedOpenReporter = null;
                CompleteOpen(value, true);
            }
            finally
            {
                FreeOptionalUtf8(args.passwordRules);
                FreeOptionalUtf8(args.initialText);
                FreeOptionalUtf8(args.accessibilityIdentifier);
                FreeOptionalUtf8(args.placeholder);
                FreeOptionalUtf8(args.presenterId);
                FreeOptionalUtf8(args.presenterData);
            }
        }

        public unsafe void ReconcileNativeField(int sessionId, int sourceNativeRevision,
            int authorityRevision, string text, int selectionStart, int selectionEnd)
        {
            ThrowIfDisposed();
            if (text == null)
            {
                UniTextNativeInput_SetNativeFieldState(
                    sessionId, sourceNativeRevision, authorityRevision,
                    IntPtr.Zero, -1, selectionStart, selectionEnd);
                return;
            }
            if (text.Length == 0)
            {
                UniTextNativeInput_SetNativeFieldState(
                    sessionId, sourceNativeRevision, authorityRevision,
                    IntPtr.Zero, 0, selectionStart, selectionEnd);
                return;
            }
            fixed (char* chars = text)
            {
                UniTextNativeInput_SetNativeFieldState(
                    sessionId, sourceNativeRevision, authorityRevision,
                    (IntPtr)chars, text.Length, selectionStart, selectionEnd);
            }
        }

        public void UpdateNativeField(in NativeFieldUpdateRequest request)
        {
            ThrowIfDisposed();
            var presentation = request.Presentation;
            var args = CreateShowKeyboardArgs(
                request.Config, request.PasswordMode, in request.Shape);
            args.showSoftwareKeyboard = 1;
            args.useNativeField = 1;
            args.readOnly = request.ReadOnly ? 1 : 0;
            args.copyAllowed = request.CopyAllowed ? 1 : 0;
            args.sessionId = request.SessionId;
            args.authorityRevision = request.AuthorityRevision;
            try
            {
                args.passwordRules = AllocOptionalUtf8(request.Config?.PasswordRules);
                args.presenterId = AllocOptionalUtf8(presentation.PresenterId);
                args.placeholder = AllocOptionalUtf8(presentation.Placeholder);
                args.accessibilityIdentifier = AllocOptionalUtf8(presentation.Identifier);
                args.presenterData = AllocOptionalUtf8(presentation.PresenterData);
                if (UniTextNativeInput_UpdateNativeField(ref args) == 0)
                    throw new InvalidOperationException(
                        $"The iOS native field presenter '{presentation.PresenterId}' is missing or invalid.");
            }
            finally
            {
                FreeOptionalUtf8(args.passwordRules);
                FreeOptionalUtf8(args.presenterId);
                FreeOptionalUtf8(args.placeholder);
                FreeOptionalUtf8(args.accessibilityIdentifier);
                FreeOptionalUtf8(args.presenterData);
            }
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
                    "The iOS native field producer is not available for inheritance.");
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
                    "The reporter is not bound to the active iOS input producer.");
            if (pendingQuiescenceReporter != null)
                throw new InvalidOperationException(
                    "The iOS input producer is already quiescing.");
            if (nextQuiescenceRequestId == int.MaxValue)
                throw new InvalidOperationException(
                    "The iOS input producer exhausted its quiescence request space.");
            FlushPendingInput();
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
                    "The reporter is not bound to a quiesced iOS input producer.");
            if (quiescedNativeField)
                UniTextNativeInput_CloseNativeField(value.SessionId);
            else
                UniTextNativeInput_HideKeyboard();
            quiescedReporter = null;
            quiescedNativeField = false;
            ResetInputCaches();
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
                ResetInputCaches();
                if (!failedTransaction) UniTextNativeInput_AbortInput(value.SessionId);
                return;
            }
            bool active = ReferenceEquals(reporter, value);
            bool quiesced = ReferenceEquals(quiescedReporter, value);
            if (!active && !quiesced)
                throw new InvalidOperationException(
                    "The reporter is not bound to the iOS input producer.");
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
            ResetInputCaches();
            UniTextNativeInput_AbortInput(value.SessionId);
        }

        NativeInputReporter EditingReporter => openingReporter ?? reporter;

        NativeInputReporter KeyboardReporter =>
            openingReporter ?? reporter ?? quiescedReporter ?? keyboardReporter;

        void BeginOpen(NativeInputReporter value, bool nativeField)
        {
            ThrowIfDisposed();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (reporter != null || openingReporter != null)
                throw new InvalidOperationException(
                    "The iOS input producer is already open or opening.");
            if (failedOpenReporter != null)
                throw new InvalidOperationException(
                    "The failed iOS input transaction has not been aborted.");
            openingReporter = value;
            openingNativeField = nativeField;
        }

        void CompleteOpen(NativeInputReporter value, bool nativeField)
        {
            if (!ReferenceEquals(openingReporter, value))
                throw new InvalidOperationException(
                    "The iOS input producer lost its opening reporter.");
            openingReporter = null;
            openingNativeField = false;
            failedOpenReporter = null;
            reporter = value;
            activeNativeField = nativeField;
            quiescedReporter = null;
            quiescedNativeField = false;
            keyboardReporter = value;
            ResetInputCaches();
        }

        void ClearPendingQuiescence()
        {
            pendingQuiescenceReporter = null;
            pendingQuiescence = null;
            pendingQuiescenceRequestId = 0;
        }

        void ResetInputCaches()
        {
            hasPendingFloatingCursorPoint = false;
            contextTextCache = null;
            contextWindowStartCache = 0;
        }

        void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(NativeInputIOS));
        }

        static ShowKeyboardArgs CreateShowKeyboardArgs(NativeKeyboardConfig config,
            bool passwordMode, in NativeFieldShape shape)
        {
            var args = new ShowKeyboardArgs
            {
                secureTextEntry = passwordMode ? 1 : 0,
                wraps = shape.Wraps ? 1 : 0,
                acceptsNewlines = shape.AcceptsNewlines ? 1 : 0,
                returnKeyType = MapReturnKeyIOS(config, shape.AcceptsNewlines),
                returnKey = (int)shape.ReturnKey,
            };
            if (config == null) return args;
            args.keyboardType = MapKeyboardTypeIOS(config);
            args.autoCapitalization = (int)config.AutoCapitalization;
            args.autoCorrection = (int)config.AutoCorrection;
            args.spellChecking = (int)config.SpellChecking;
            args.autofillHint = (int)config.AutofillHint;
            args.smartQuotes = (int)config.SmartQuotes;
            args.smartDashes = (int)config.SmartDashes;
            args.smartInsertDelete = (int)config.SmartInsertDelete;
            args.enablesReturnKeyAuto = config.EnablesReturnKeyAutomatically ? 1 : 0;
            return args;
        }

        static IntPtr AllocOptionalUtf8(string value)
            => string.IsNullOrEmpty(value) ? IntPtr.Zero : MarshalUtf8.AllocHGlobalUtf8(value);

        static void FreeOptionalUtf8(IntPtr ptr)
        {
            if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
        }

        static int MapKeyboardTypeIOS(NativeKeyboardConfig config)
        {
            string overrideValue = config.IOSKeyboardTypeOverride;
            if (!string.IsNullOrEmpty(overrideValue))
            {
                if (overrideValue.EqualsIgnoreCase("namePhonePad")) return 6;
                if (overrideValue.EqualsIgnoreCase("twitter")) return 9;
                if (overrideValue.EqualsIgnoreCase("asciiCapableNumberPad")) return 11;
            }
            return config.KeyboardType switch
            {
                KeyboardType.ASCIICapable => 1,
                KeyboardType.NumbersAndPunctuation => 2,
                KeyboardType.URL => 3,
                KeyboardType.NumberPad => 4,
                KeyboardType.PhonePad => 5,
                KeyboardType.EmailAddress => 7,
                KeyboardType.DecimalPad => 8,
                KeyboardType.WebSearch => 10,
                _ => 0,
            };
        }

        /// <summary>
        /// Maps the declared return key to its iOS counterpart. A field that accepts line breaks
        /// always gets the plain return key: it spends that key on newlines, so any other label
        /// would promise an action the key does not perform. The declared action still reaches the
        /// presenter, which surfaces it where it can be pressed.
        /// </summary>
        static int MapReturnKeyIOS(NativeKeyboardConfig config, bool acceptsNewlines)
        {
            if (acceptsNewlines || config == null) return acceptsNewlines ? 0 : 9;
            string overrideValue = config.IOSReturnKeyOverride;
            if (!string.IsNullOrEmpty(overrideValue))
            {
                if (overrideValue.EqualsIgnoreCase("google")) return 2;
                if (overrideValue.EqualsIgnoreCase("join")) return 3;
                if (overrideValue.EqualsIgnoreCase("route")) return 5;
                if (overrideValue.EqualsIgnoreCase("yahoo")) return 8;
                if (overrideValue.EqualsIgnoreCase("emergencyCall")) return 10;
                if (overrideValue.EqualsIgnoreCase("continue")) return 11;
            }
            return config.ReturnKeyType switch
            {
                ReturnKeyType.Go => 1,
                ReturnKeyType.Next => 4,
                ReturnKeyType.Search => 6,
                ReturnKeyType.Send => 7,
                ReturnKeyType.Done => 9,
                ReturnKeyType.Enter => 0,
                _ => 9,
            };
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
            ResetInputCaches();
            UniTextNativeInput_Shutdown();
        }

        delegate void TextInputDelegate(string text);
        delegate void TextReplacementDelegate(int contextVersion, int charStart, int charLength,
            string text);
        delegate void KeyDownDelegate(int keyCode, int modifiers);
        delegate void CompositionDelegate(string text, IntPtr clauseStarts, IntPtr clauseEnds,
            IntPtr clauseStyles, int clauseCount, int cursorPos);
        delegate void CompositionEndedDelegate();
        delegate void KeyboardEventDelegate(int phase, float x, float y, float w, float h,
            float animationDuration, int easing, float animationFraction);
        delegate void NativeFieldEditDelegate(int sessionId, int nativeRevision, int authorityRevision,
            int rangeStart, int rangeLength, string replacement,
            int selectionStart, int selectionEnd);
        delegate void NativeFieldCompositionDelegate(int sessionId, int nativeRevision,
            int authorityRevision, int phase, int replacementStart, int replacementLength,
            string compositionText, int cursorPosition);
        delegate void NativeFieldSelectionDelegate(int sessionId, int nativeRevision,
            int authorityRevision, int selectionStart, int selectionEnd);
        delegate void NativeFieldActionDelegate(int sessionId, int nativeRevision,
            int authorityRevision, string action, int modifiers);
        delegate void NativeInputQuiescedDelegate(int sessionId, int nativeRevision,
            int authorityRevision, int requestId);
        delegate void NativeInputFaultDelegate(int sessionId, string message);
        delegate void FloatingCursorPointDelegate(float screenX, float screenY);
        delegate void SetSelectionCharRangeDelegate(int charStart, int charLength);
        delegate int GetCharRangeRectDelegate(int charStart, int charLength, IntPtr outRect);
        delegate int ClosestCharAtPointDelegate(float screenX, float screenY);
        delegate int WritingDirectionDelegate(int charIndex);

        [MonoPInvokeCallback(typeof(TextInputDelegate))]
        static void OnTextInputCallback(string text)
        {
            var target = instance?.EditingReporter;
            if (target != null) target.ReportTextInput(text ?? string.Empty);
        }

        [MonoPInvokeCallback(typeof(TextReplacementDelegate))]
        static void OnTextReplacementCallback(int contextVersion, int charStart, int charLength,
            string text)
        {
            var backend = instance;
            if (backend == null || contextVersion != backend.contextVersion) return;
            backend.EditingReporter?.ReportTextReplacement(
                charStart, charLength, text ?? string.Empty);
        }

        [MonoPInvokeCallback(typeof(KeyDownDelegate))]
        static void OnKeyDownCallback(int keyCode, int modifiers)
        {
            instance?.EditingReporter?.ReportKeyDown(
                (NativeKeyCode)keyCode, (NativeModifiers)modifiers);
        }

        [MonoPInvokeCallback(typeof(CompositionDelegate))]
        static unsafe void OnCompositionCallback(string text, IntPtr clauseStartsPtr,
            IntPtr clauseEndsPtr, IntPtr clauseStylesPtr, int clauseCount, int cursorPos)
        {
            var target = instance?.EditingReporter;
            if (target == null) return;
            if (string.IsNullOrEmpty(text))
            {
                target.ReportCompositionChanged(new CompositionData
                {
                    text = ReadOnlySpan<char>.Empty,
                    clauses = ReadOnlySpan<CompositionClause>.Empty,
                    cursorPosition = 0,
                });
                return;
            }

            int actualClauseCount;
            if (clauseCount > 0 && clauseStartsPtr != IntPtr.Zero
                                && clauseEndsPtr != IntPtr.Zero
                                && clauseStylesPtr != IntPtr.Zero)
            {
                CompositionClause.EnsureCapacity(ref compositionClauseBuffer, clauseCount);
                int* starts = (int*)clauseStartsPtr;
                int* ends = (int*)clauseEndsPtr;
                int* styles = (int*)clauseStylesPtr;
                for (int i = 0; i < clauseCount; i++)
                {
                    compositionClauseBuffer[i] = new CompositionClause
                    {
                        startOffset = starts[i],
                        endOffset = ends[i],
                        style = (CompositionClauseStyle)styles[i],
                    };
                }
                actualClauseCount = clauseCount;
            }
            else
            {
                actualClauseCount = CompositionClause.FillFallback(
                    ref compositionClauseBuffer, text.Length);
            }

            var data = new CompositionData
            {
                text = text.AsSpan(),
                clauses = new ReadOnlySpan<CompositionClause>(
                    compositionClauseBuffer, 0, actualClauseCount),
                cursorPosition = cursorPos,
            };
            target.ReportCompositionChanged(data);
        }

        [MonoPInvokeCallback(typeof(CompositionEndedDelegate))]
        static void OnCompositionEndedCallback()
        {
            instance?.EditingReporter?.ReportCompositionEnded();
        }

        [MonoPInvokeCallback(typeof(KeyboardEventDelegate))]
        static void OnKeyboardEventCallback(int phase, float x, float y, float w, float h,
            float animationDuration, int easing, float animationFraction)
        {
            var target = instance?.KeyboardReporter;
            if (target == null) return;
            var value = new KeyboardEvent
            {
                phase = (KeyboardEventPhase)phase,
                area = new Rect(x, y, w, h),
                animationDuration = animationDuration,
                easing = (KeyboardEasing)easing,
                animationFraction = animationFraction,
                hasFrameSyncedAnimation = false,
            };
            target.ReportKeyboardEvent(in value);
        }

        [MonoPInvokeCallback(typeof(NativeFieldEditDelegate))]
        static void OnNativeFieldEditCallback(int sessionId, int nativeRevision,
            int authorityRevision, int rangeStart, int rangeLength, string replacement,
            int selectionStart, int selectionEnd)
        {
            var target = NativeFieldCallbackReporter(sessionId);
            if (target == null) return;
            target.ReportFieldEdit(nativeRevision, authorityRevision,
                rangeStart, rangeLength, replacement ?? string.Empty, selectionStart, selectionEnd);
        }

        [MonoPInvokeCallback(typeof(NativeFieldCompositionDelegate))]
        static void OnNativeFieldCompositionCallback(int sessionId, int nativeRevision,
            int authorityRevision, int phase, int replacementStart, int replacementLength,
            string compositionText, int cursorPosition)
        {
            var target = NativeFieldCallbackReporter(sessionId);
            if (target == null) return;
            if ((uint)phase > (uint)NativeFieldCompositionPhase.Cancel)
                throw new ArgumentOutOfRangeException(nameof(phase));
            target.ReportFieldComposition(nativeRevision, authorityRevision,
                (NativeFieldCompositionPhase)phase, replacementStart, replacementLength,
                compositionText ?? string.Empty, cursorPosition);
        }

        [MonoPInvokeCallback(typeof(NativeFieldSelectionDelegate))]
        static void OnNativeFieldSelectionCallback(int sessionId, int nativeRevision,
            int authorityRevision, int selectionStart, int selectionEnd)
        {
            var target = NativeFieldCallbackReporter(sessionId);
            if (target == null) return;
            target.ReportFieldSelection(nativeRevision, authorityRevision,
                selectionStart, selectionEnd);
        }

        [MonoPInvokeCallback(typeof(NativeFieldActionDelegate))]
        static void OnNativeFieldActionCallback(int sessionId, int nativeRevision,
            int authorityRevision, string action, int modifiers)
        {
            var target = NativeFieldCallbackReporter(sessionId);
            if (target == null) return;
            target.ReportFieldAction(nativeRevision, authorityRevision, action,
                (NativeModifiers)modifiers);
        }

        [MonoPInvokeCallback(typeof(NativeInputQuiescedDelegate))]
        static void OnNativeInputQuiescedCallback(int sessionId, int nativeRevision,
            int authorityRevision, int requestId)
        {
            var backend = instance;
            var target = backend?.pendingQuiescenceReporter;
            if (target == null) return;
            if (!ReferenceEquals(backend.reporter, target) || target.SessionId != sessionId ||
                backend.pendingQuiescenceRequestId != requestId)
                throw new InvalidOperationException(
                    "The iOS native input quiescence callback does not match its pending request.");
            bool nativeField = backend.activeNativeField;
            if (nativeField)
            {
                target.ReportFieldQuiesced(nativeRevision, authorityRevision);
            }
            else if (nativeRevision <= 0 || authorityRevision != 1)
            {
                throw new InvalidOperationException(
                    "The iOS transparent input barrier has an invalid revision envelope.");
            }
            var completion = backend.pendingQuiescence;
            backend.ClearPendingQuiescence();
            backend.reporter = null;
            backend.activeNativeField = false;
            backend.quiescedReporter = target;
            backend.quiescedNativeField = nativeField;
            completion();
        }

        [MonoPInvokeCallback(typeof(NativeInputFaultDelegate))]
        static void OnNativeInputFaultCallback(int sessionId, string message)
        {
            var backend = instance;
            if (backend == null || backend.disposed) return;
            var target = backend.openingReporter ?? backend.reporter ?? backend.quiescedReporter;
            if (target == null || target.SessionId != sessionId)
                throw new InvalidOperationException(
                    "The iOS native input fault does not match a live reporter.");
            target.ReportFault(new InvalidOperationException(
                string.IsNullOrEmpty(message)
                    ? $"iOS native input session {sessionId} failed without detail."
                    : message));
        }

        static NativeInputReporter NativeFieldCallbackReporter(int sessionId)
        {
            var backend = instance;
            if (backend == null || backend.disposed) return null;
            if (backend.openingNativeField && backend.openingReporter?.SessionId == sessionId)
                return backend.openingReporter;
            return backend.activeNativeField && backend.reporter?.SessionId == sessionId
                ? backend.reporter
                : null;
        }

        [MonoPInvokeCallback(typeof(FloatingCursorPointDelegate))]
        static void OnFloatingCursorPoint(float screenX, float screenY)
        {
            var backend = instance;
            if (backend == null || backend.disposed || backend.EditingReporter == null) return;
            backend.pendingFloatingCursorScreenPoint = new Vector2(screenX, screenY);
            backend.hasPendingFloatingCursorPoint = true;
        }

        [MonoPInvokeCallback(typeof(SetSelectionCharRangeDelegate))]
        static void OnSetSelectionCharRange(int charStart, int charLength)
        {
            instance?.EditingReporter?.Context.SelectCharRange(charStart, charLength);
        }

        [MonoPInvokeCallback(typeof(GetCharRangeRectDelegate))]
        static unsafe int OnGetCharRangeRect(int charStart, int charLength, IntPtr outRect)
        {
            if (outRect == IntPtr.Zero) return 0;
            var context = instance?.EditingReporter?.Context;
            if (context == null || !context.TryGetCharRangeRect(charStart, charLength, out var rect))
                return 0;
            float* destination = (float*)outRect.ToPointer();
            destination[0] = rect.x;
            destination[1] = rect.y;
            destination[2] = rect.width;
            destination[3] = rect.height;
            return 1;
        }

        [MonoPInvokeCallback(typeof(ClosestCharAtPointDelegate))]
        static int OnClosestCharAtPoint(float screenX, float screenY)
        {
            var context = instance?.EditingReporter?.Context;
            return context == null ? -1 : context.HitTestChar(new Vector2(screenX, screenY));
        }

        [MonoPInvokeCallback(typeof(WritingDirectionDelegate))]
        static int OnWritingDirection(int charIndex)
        {
            var context = instance?.EditingReporter?.Context;
            return context == null ? 0 : context.WritingDirection(charIndex);
        }

        const string DllName = "__Internal";

        [DllImport(DllName)]
        static extern void UniTextNativeInput_Init(
            TextInputDelegate onTextInput,
            TextReplacementDelegate onTextReplacement,
            KeyDownDelegate onKeyDown,
            CompositionDelegate onComposition,
            CompositionEndedDelegate onCompositionEnded,
            KeyboardEventDelegate onKeyboardEvent,
            NativeFieldEditDelegate onNativeFieldEdit,
            NativeFieldCompositionDelegate onNativeFieldComposition,
            NativeFieldSelectionDelegate onNativeFieldSelection,
            NativeFieldActionDelegate onNativeFieldAction,
            NativeInputQuiescedDelegate onNativeInputQuiesced,
            NativeInputFaultDelegate onNativeInputFault,
            FloatingCursorPointDelegate onFloatingCursorPoint);

        [DllImport(DllName)]
        static extern void UniTextNativeInput_Shutdown();

        [DllImport(DllName)]
        static extern void UniTextNativeInput_RegisterSelectionCallback(
            SetSelectionCharRangeDelegate setSelectionCharRange);

        [DllImport(DllName)]
        static extern void UniTextNativeInput_RegisterGeometryQueries(
            GetCharRangeRectDelegate getCharRangeRect,
            ClosestCharAtPointDelegate closestCharAtPoint,
            WritingDirectionDelegate writingDirection);

        [DllImport(DllName)]
        static extern void UniTextNativeInput_SetCursorPos(float x, float y, float w, float h);

        [DllImport(DllName)]
        static extern void UniTextNativeInput_SetTextContext(
            int version, IntPtr text, int textLength, int windowStart,
            int selectionStart, int selectionEnd, int forceRestart);

        [StructLayout(LayoutKind.Sequential)]
        struct ShowKeyboardArgs
        {
            public int keyboardType;
            public int returnKeyType;
            public int returnKey;
            public int autoCapitalization;
            public int autoCorrection;
            public int spellChecking;
            public int secureTextEntry;
            public int autofillHint;
            public int smartQuotes;
            public int smartDashes;
            public int smartInsertDelete;
            public int enablesReturnKeyAuto;
            public int showSoftwareKeyboard;
            public int useNativeField;
            public int wraps;
            public int acceptsNewlines;
            public int readOnly;
            public int copyAllowed;
            public int selectionStart;
            public int selectionEnd;
            public int sessionId;
            public int authorityRevision;
            public IntPtr passwordRules;
            public IntPtr initialText;
            public IntPtr accessibilityIdentifier;
            public IntPtr placeholder;
            public IntPtr presenterId;
            public IntPtr presenterData;
        }

        [DllImport(DllName)]
        static extern int UniTextNativeInput_ShowKeyboard(ref ShowKeyboardArgs args);

        [DllImport(DllName)]
        static extern void UniTextNativeInput_HideKeyboard();

        [DllImport(DllName)]
        static extern void UniTextNativeInput_SetInputFieldRect(float x, float y, float w, float h);

        [DllImport(DllName)]
        static extern void UniTextNativeInput_SetNativeFieldState(
            int sessionId, int sourceNativeRevision, int authorityRevision,
            IntPtr text, int textLength, int selectionStart, int selectionEnd);

        [DllImport(DllName)]
        static extern int UniTextNativeInput_UpdateNativeField(ref ShowKeyboardArgs args);

        [DllImport(DllName)]
        static extern void UniTextNativeInput_CloseNativeField(int sessionId);

        [DllImport(DllName)]
        static extern void UniTextNativeInput_FocusNativeField(int sessionId);

        [DllImport(DllName)]
        static extern void UniTextNativeInput_QuiesceInput(
            int sessionId, int disposition, int requestId);

        [DllImport(DllName)]
        static extern int UniTextNativeInput_AbortInput(int sessionId);
    }
}

#endif
