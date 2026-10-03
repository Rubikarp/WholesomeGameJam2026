#if UNITY_ANDROID

using System;
using System.Globalization;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.Scripting;

namespace LightSide
{
    internal sealed class NativeInputAndroid : INativeInputBackend, INativeFieldBackend
    {
        private const int TYPE_MASK_CLASS    = 0x0000000F;
        private const int TYPE_CLASS_TEXT    = 0x00000001;
        private const int TYPE_CLASS_NUMBER  = 0x00000002;
        private const int TYPE_CLASS_PHONE   = 0x00000003;

        private const int TYPE_TEXT_VARIATION_NORMAL        = 0x00000000;
        private const int TYPE_TEXT_VARIATION_URI            = 0x00000010;
        private const int TYPE_TEXT_VARIATION_EMAIL_ADDRESS  = 0x00000020;
        private const int TYPE_TEXT_VARIATION_SHORT_MESSAGE  = 0x00000040;
        private const int TYPE_TEXT_VARIATION_PASSWORD       = 0x00000080;

        private const int TYPE_TEXT_FLAG_CAP_CHARACTERS = 0x00001000;
        private const int TYPE_TEXT_FLAG_CAP_WORDS      = 0x00002000;
        private const int TYPE_TEXT_FLAG_CAP_SENTENCES  = 0x00004000;
        private const int TYPE_TEXT_FLAG_AUTO_CORRECT   = 0x00008000;
        private const int TYPE_TEXT_FLAG_MULTI_LINE     = 0x00020000;
        private const int TYPE_TEXT_FLAG_NO_SUGGESTIONS = 0x00080000;

        private const int TYPE_NUMBER_VARIATION_PASSWORD = 0x00000010;
        private const int TYPE_NUMBER_FLAG_SIGNED  = 0x00001000;
        private const int TYPE_NUMBER_FLAG_DECIMAL = 0x00002000;

        private const int IME_ACTION_UNSPECIFIED = 0x00000000;
        private const int IME_ACTION_NONE     = 0x00000001;
        private const int IME_ACTION_GO       = 0x00000002;
        private const int IME_ACTION_SEARCH   = 0x00000003;
        private const int IME_ACTION_SEND     = 0x00000004;
        private const int IME_ACTION_NEXT     = 0x00000005;
        private const int IME_ACTION_DONE     = 0x00000006;
        private const int IME_ACTION_PREVIOUS = 0x00000007;

        private const int IME_FLAG_NO_PERSONALIZED_LEARNING = 0x01000000;
        private const int IME_FLAG_FORCE_ASCII              = unchecked((int)0x80000000);

        private AndroidJavaClass javaClass;
        private bool disposed;
        private NativeInputReporter reporter;
        private NativeInputReporter quiescedReporter;

        /// <summary>
        /// Carries software-keyboard reports for the whole backend generation, including after the
        /// session that installed it has closed. Closing a producer takes the keyboard down and the
        /// resulting report crosses back asynchronously, so a session-scoped carrier would drop the
        /// only notice that the keyboard is gone; keyboard reports are gated on the generation alone.
        /// </summary>
        private NativeInputReporter keyboardReporter;
        private NativeInputReporter failedOpenReporter;
        private NativeInputReporter faultedReporter;
        private Action quiescenceCompletion;
        private int quiescenceRequestId;
        private int nextQuiescenceRequestId;
        private bool nativeFieldMode;
        private bool quiescedNativeFieldMode;
        private int nativeProducerEpoch;
        private int quiescedProducerEpoch;
        private int transparentSessionId;
        private int transparentEpoch;
        private int nextProducerEpoch;
        private int lastTransparentRevision;

        private CompositionClause[] clauseBuf = new CompositionClause[16];

        private const char SEP = (char)UnicodeData.UnitSeparator;

        private int contextVersion;

        private IntPtr setTextContextMethod;
        private IntPtr setInputFieldRectMethod;
        private IntPtr reconcileNativeFieldMethod;
        private readonly jvalue[] jargs6 = new jvalue[6];
        private readonly jvalue[] jargs4 = new jvalue[4];
        private readonly jvalue[] nativeFieldReconcileArgs = new jvalue[6];

        private static NativeInputAndroid instance;
        private static GameObject receiverGo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (Application.isEditor) return;
            UniTextNativeInput.RegisterBackend(Create, 0);
        }

        private static INativeInputBackend Create()
        {
            var backend = new NativeInputAndroid();
            backend.Setup();
            return backend;
        }

        private void Setup()
        {
            try
            {
                javaClass = new AndroidJavaClass("com.unitext.input.UniTextNativeInput");
                var raw = javaClass.GetRawClass();
                ValidateJavaAbi(raw);

                if (instance != null || receiverGo != null)
                    throw new InvalidOperationException("An Android native input backend is already initialized.");
                instance = this;
                receiverGo = new GameObject("_UniTextNativeInput");
                UnityEngine.Object.DontDestroyOnLoad(receiverGo);
                receiverGo.hideFlags = HideFlags.HideAndDontSave;
                receiverGo.AddComponent<MessageReceiver>();
                javaClass.CallStatic("initialize");
            }
            catch
            {
                if (instance == this) instance = null;
                try
                {
                    if (receiverGo != null) UnityEngine.Object.Destroy(receiverGo);
                }
                catch (Exception cleanupError) { Debug.LogException(cleanupError); }
                finally { receiverGo = null; }
                try { javaClass?.Dispose(); }
                catch (Exception cleanupError) { Debug.LogException(cleanupError); }
                javaClass = null;
                setTextContextMethod = IntPtr.Zero;
                setInputFieldRectMethod = IntPtr.Zero;
                reconcileNativeFieldMethod = IntPtr.Zero;
                throw;
            }
        }

        private void ValidateJavaAbi(IntPtr rawClass)
        {
            RequireJavaMethod(rawClass, "initialize", "()V");
            RequireJavaMethod(rawClass, "showKeyboardTransparent", "(IIIIZ)V");
            RequireJavaMethod(rawClass, "showKeyboardNativeField",
                "(IIIIIIZZZZZILjava/lang/String;IILjava/lang/String;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;)V");
            RequireJavaMethod(rawClass, "updateNativeField",
                "(IIIIIZZZZZILjava/lang/String;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;)V");
            reconcileNativeFieldMethod = RequireJavaMethod(rawClass, "reconcileNativeField",
                "(IIILjava/lang/String;II)V");
            RequireJavaMethod(rawClass, "focusNativeField", "(II)V");
            RequireJavaMethod(rawClass, "quiesceInput", "(III)V");
            RequireJavaMethod(rawClass, "closeInput", "(I)V");
            RequireJavaMethod(rawClass, "abortInput", "(I)V");
            setTextContextMethod = RequireJavaMethod(rawClass,
                "setTextContext", "(ILjava/lang/String;IIIZ)V");
            setInputFieldRectMethod = RequireJavaMethod(rawClass,
                "setInputFieldRect", "(FFFF)V");
        }

        private static IntPtr RequireJavaMethod(IntPtr rawClass, string name, string signature)
        {
            var method = AndroidJNI.GetStaticMethodID(rawClass, name, signature);
            if (method == IntPtr.Zero)
                throw new MissingMethodException($"UniText Android bridge is missing {name}{signature}.");
            return method;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Exception failure = null;
            var bound = reporter ?? quiescedReporter;
            if (bound != null && javaClass != null)
            {
                try { javaClass.CallStatic("abortInput", bound.SessionId); }
                catch (Exception error) { failure = error; }
            }
            reporter = null;
            quiescedReporter = null;
            keyboardReporter = null;
            failedOpenReporter = null;
            faultedReporter = null;
            quiescenceCompletion = null;
            nativeProducerEpoch = 0;
            quiescedProducerEpoch = 0;
            transparentSessionId = 0;
            transparentEpoch = 0;
            lastTransparentRevision = 0;
            try { ReleaseContextJString(); }
            catch (Exception error)
            {
                if (failure == null) failure = error;
                else Debug.LogException(error);
            }
            try { javaClass?.Dispose(); }
            catch (Exception error)
            {
                if (failure == null) failure = error;
                else Debug.LogException(error);
            }
            finally
            {
                javaClass = null;
                setTextContextMethod = IntPtr.Zero;
                setInputFieldRectMethod = IntPtr.Zero;
                reconcileNativeFieldMethod = IntPtr.Zero;
                if (receiverGo != null)
                {
                    UnityEngine.Object.Destroy(receiverGo);
                    receiverGo = null;
                }
                if (instance == this) instance = null;
            }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        public void SetCursorScreenPos(Vector2 screenPos, float lineHeight)
        {
        }

        public void SetInputFieldRect(Rect screenRect)
        {
            EnsureAvailable();
            jargs4[0].f = screenRect.xMin;
            jargs4[1].f = screenRect.yMin;
            jargs4[2].f = screenRect.width;
            jargs4[3].f = screenRect.height;
            AndroidJNI.CallStaticVoidMethod(javaClass.GetRawClass(), setInputFieldRectMethod, jargs4);
        }

        public bool WantsTextContext => true;

        private string contextTextCache;
        private int contextWindowStartCache;
        private IntPtr contextJStringCache;

        public void PushTextContext(string text, int windowStart, int selectionStart, int selectionEnd, bool forceRestart)
        {
            EnsureAvailable();
            if (reporter == null || nativeFieldMode) return;

            text ??= string.Empty;
            if (contextJStringCache == IntPtr.Zero
                || !ReferenceEquals(text, contextTextCache)
                || windowStart != contextWindowStartCache)
            {
                if (contextVersion == int.MaxValue)
                    throw new InvalidOperationException("The Android text-context version space is exhausted.");
                ReleaseContextJString();
                var local = AndroidJNI.NewString(text);
                contextJStringCache = AndroidJNI.NewGlobalRef(local);
                AndroidJNI.DeleteLocalRef(local);
                contextTextCache = text;
                contextWindowStartCache = windowStart;
                contextVersion++;
            }
            else if (forceRestart)
            {
                if (contextVersion == int.MaxValue)
                    throw new InvalidOperationException("The Android text-context version space is exhausted.");
                contextVersion++;
            }

            jargs6[0].i = contextVersion;
            jargs6[1].l = contextJStringCache;
            jargs6[2].i = windowStart;
            jargs6[3].i = selectionStart;
            jargs6[4].i = selectionEnd;
            jargs6[5].z = forceRestart;
            AndroidJNI.CallStaticVoidMethod(javaClass.GetRawClass(), setTextContextMethod, jargs6);
        }

        private void ReleaseContextJString()
        {
            if (contextJStringCache == IntPtr.Zero) return;
            AndroidJNI.DeleteGlobalRef(contextJStringCache);
            contextJStringCache = IntPtr.Zero;
            contextTextCache = null;
        }

        public void FlushPendingInput()
        {
        }

        public void OpenInput(in NativeInputOpenRequest request, NativeInputReporter value)
        {
            EnsureAvailable();
            ValidateOpen(value);

            var inherited = quiescedReporter;
            var inheritedNativeFieldMode = quiescedNativeFieldMode;
            var previousKeyboardReporter = keyboardReporter;
            int previousSessionId = transparentSessionId;
            int previousEpoch = transparentEpoch;
            int previousRevision = lastTransparentRevision;
            int previousContextVersion = contextVersion;
            int previousNativeProducerEpoch = nativeProducerEpoch;
            bool inheritsTransparent = inherited != null && !inheritedNativeFieldMode;

            int inputType = BuildInputType(request.Keyboard, request.SecureTextEntry);
            if (request.AcceptsNewlines && (inputType & TYPE_MASK_CLASS) == TYPE_CLASS_TEXT)
                inputType |= TYPE_TEXT_FLAG_MULTI_LINE;
            int imeOptions = BuildImeOptions(request.Keyboard, request.AcceptsNewlines);
            int producerEpoch = NextProducerEpoch();

            reporter = value;
            nativeFieldMode = false;
            nativeProducerEpoch = 0;
            keyboardReporter = value;
            transparentSessionId = value.SessionId;
            transparentEpoch = producerEpoch;
            lastTransparentRevision = 0;
            contextVersion = inheritsTransparent ? previousContextVersion : 0;
            failedOpenReporter = null;
            try
            {
                if (!inheritsTransparent) ReleaseContextJString();
                javaClass.CallStatic("showKeyboardTransparent", value.SessionId,
                    transparentEpoch, inputType, imeOptions, request.ShowSoftwareKeyboard);
                quiescedReporter = null;
                quiescedNativeFieldMode = false;
                quiescedProducerEpoch = 0;
            }
            catch
            {
                reporter = null;
                nativeFieldMode = false;
                quiescedReporter = inherited;
                quiescedNativeFieldMode = inheritedNativeFieldMode;
                keyboardReporter = previousKeyboardReporter;
                nativeProducerEpoch = previousNativeProducerEpoch;
                transparentSessionId = previousSessionId;
                transparentEpoch = previousEpoch;
                lastTransparentRevision = previousRevision;
                contextVersion = previousContextVersion;
                failedOpenReporter = value;
                throw;
            }
        }

        public void OpenNativeField(in NativeFieldOpenRequest request,
            NativeInputReporter value)
        {
            EnsureAvailable();
            ValidateOpen(value);
            if (request.SessionId != value.SessionId)
                throw new ArgumentException("The native field request does not belong to the reporter.",
                    nameof(request));

            int inputType = BuildInputType(request.Config, request.PasswordMode);
            if (request.Shape.AcceptsNewlines && (inputType & TYPE_MASK_CLASS) == TYPE_CLASS_TEXT)
                inputType |= TYPE_TEXT_FLAG_MULTI_LINE;
            int imeOptions = BuildImeOptions(request.Config, request.Shape.AcceptsNewlines);
            int autofillHint = BuildAutofillHint(request.Config);
            string text = request.Text ?? string.Empty;
            var presentation = request.Presentation;
            string presenterId = presentation.PresenterId;
            string placeholder = presentation.Placeholder ?? string.Empty;
            string identifier = presentation.Identifier ?? string.Empty;
            string presenterData = presentation.PresenterData ?? string.Empty;
            int producerEpoch = NextProducerEpoch();

            var inherited = quiescedReporter;
            var inheritedNativeFieldMode = quiescedNativeFieldMode;
            var previousKeyboardReporter = keyboardReporter;
            int previousSessionId = transparentSessionId;
            int previousEpoch = transparentEpoch;
            int previousRevision = lastTransparentRevision;
            int previousContextVersion = contextVersion;
            int previousNativeProducerEpoch = nativeProducerEpoch;
            reporter = value;
            nativeFieldMode = true;
            nativeProducerEpoch = producerEpoch;
            keyboardReporter = value;
            failedOpenReporter = null;
            transparentSessionId = 0;
            transparentEpoch = 0;
            lastTransparentRevision = 0;

            try
            {
                ReleaseContextJString();
                javaClass.CallStatic("showKeyboardNativeField",
                    request.SessionId, producerEpoch, request.AuthorityRevision,
                    inputType, imeOptions, (int)request.Shape.ReturnKey,
                    request.Shape.Wraps, request.Shape.AcceptsNewlines, request.ReadOnly,
                    request.CopyAllowed, request.PasswordMode, autofillHint,
                    text, request.SelectionStart, request.SelectionEnd,
                    presenterId, placeholder, identifier, presenterData);
                quiescedReporter = null;
                quiescedNativeFieldMode = false;
                quiescedProducerEpoch = 0;
            }
            catch
            {
                reporter = null;
                nativeFieldMode = false;
                quiescedReporter = inherited;
                quiescedNativeFieldMode = inheritedNativeFieldMode;
                keyboardReporter = previousKeyboardReporter;
                nativeProducerEpoch = previousNativeProducerEpoch;
                transparentSessionId = previousSessionId;
                transparentEpoch = previousEpoch;
                lastTransparentRevision = previousRevision;
                contextVersion = previousContextVersion;
                failedOpenReporter = value;
                throw;
            }
        }

        public void ReconcileNativeField(int sessionId, int sourceNativeRevision,
            int authorityRevision, string text, int selectionStart, int selectionEnd)
        {
            EnsureAvailable();
            EnsureNativeFieldSession(sessionId);

            IntPtr localText = text != null ? AndroidJNI.NewString(text) : IntPtr.Zero;
            nativeFieldReconcileArgs[0].i = sessionId;
            nativeFieldReconcileArgs[1].i = sourceNativeRevision;
            nativeFieldReconcileArgs[2].i = authorityRevision;
            nativeFieldReconcileArgs[3].l = localText;
            nativeFieldReconcileArgs[4].i = selectionStart;
            nativeFieldReconcileArgs[5].i = selectionEnd;
            try
            {
                AndroidJNI.CallStaticVoidMethod(javaClass.GetRawClass(), reconcileNativeFieldMethod,
                    nativeFieldReconcileArgs);
            }
            finally
            {
                if (localText != IntPtr.Zero) AndroidJNI.DeleteLocalRef(localText);
            }
        }

        public void UpdateNativeField(in NativeFieldUpdateRequest request)
        {
            EnsureAvailable();
            EnsureNativeFieldSession(request.SessionId);
            int inputType = BuildInputType(request.Config, request.PasswordMode);
            if (request.Shape.AcceptsNewlines && (inputType & TYPE_MASK_CLASS) == TYPE_CLASS_TEXT)
                inputType |= TYPE_TEXT_FLAG_MULTI_LINE;
            int imeOptions = BuildImeOptions(request.Config, request.Shape.AcceptsNewlines);
            int autofillHint = BuildAutofillHint(request.Config);
            var presentation = request.Presentation;
            string presenterId = presentation.PresenterId;

            javaClass.CallStatic("updateNativeField",
                request.SessionId, request.AuthorityRevision, inputType, imeOptions,
                (int)request.Shape.ReturnKey, request.Shape.Wraps,
                request.Shape.AcceptsNewlines, request.ReadOnly, request.CopyAllowed,
                request.PasswordMode, autofillHint, presenterId,
                presentation.Placeholder ?? string.Empty,
                presentation.Identifier ?? string.Empty,
                presentation.PresenterData ?? string.Empty);
        }

        public void FocusNativeField(int sessionId, NativeInputReporter value)
        {
            EnsureAvailable();
            ValidateOpen(value);
            if (sessionId != value.SessionId)
                throw new ArgumentException("The native field session does not belong to the reporter.",
                    nameof(sessionId));
            if (quiescedReporter == null || !quiescedNativeFieldMode ||
                quiescedReporter.SessionId != sessionId)
                throw new InvalidOperationException("The Android native field is not quiesced for focus transfer.");

            var inherited = quiescedReporter;
            var previousKeyboardReporter = keyboardReporter;
            int previousNativeProducerEpoch = nativeProducerEpoch;
            int producerEpoch = NextProducerEpoch();
            reporter = value;
            nativeFieldMode = true;
            nativeProducerEpoch = producerEpoch;
            keyboardReporter = value;
            failedOpenReporter = null;
            try
            {
                javaClass.CallStatic("focusNativeField", sessionId, producerEpoch);
                quiescedReporter = null;
                quiescedNativeFieldMode = false;
                quiescedProducerEpoch = 0;
            }
            catch
            {
                reporter = null;
                nativeFieldMode = false;
                nativeProducerEpoch = previousNativeProducerEpoch;
                quiescedReporter = inherited;
                quiescedNativeFieldMode = true;
                keyboardReporter = previousKeyboardReporter;
                failedOpenReporter = value;
                throw;
            }
        }

        public void QuiesceInput(NativeInputReporter value,
            NativeCompositionDisposition disposition, Action quiesced)
        {
            EnsureAvailable();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (quiesced == null) throw new ArgumentNullException(nameof(quiesced));
            if (!ReferenceEquals(reporter, value))
                throw new InvalidOperationException("The reporter is not bound to the Android input producer.");
            if (quiescenceCompletion != null)
                throw new InvalidOperationException("An Android input quiescence is already pending.");
            if (nextQuiescenceRequestId == int.MaxValue)
                throw new InvalidOperationException("The Android input quiescence request space is exhausted.");

            quiescenceRequestId = ++nextQuiescenceRequestId;
            quiescenceCompletion = quiesced;
            try
            {
                javaClass.CallStatic("quiesceInput", value.SessionId,
                    (int)disposition, quiescenceRequestId);
            }
            catch
            {
                quiescenceRequestId = 0;
                quiescenceCompletion = null;
                throw;
            }
        }

        public void CloseInput(NativeInputReporter value)
        {
            EnsureAvailable();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!ReferenceEquals(quiescedReporter, value) || reporter != null ||
                quiescenceCompletion != null)
                throw new InvalidOperationException("The reporter is not bound to a quiesced Android input producer.");
            ReleaseContextJString();
            javaClass.CallStatic("closeInput", value.SessionId);
            quiescedReporter = null;
            quiescedNativeFieldMode = false;
            quiescedProducerEpoch = 0;
            nativeProducerEpoch = 0;
            transparentSessionId = 0;
            transparentEpoch = 0;
            lastTransparentRevision = 0;
        }

        public void AbortInput(NativeInputReporter value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(failedOpenReporter, value))
            {
                failedOpenReporter = null;
                return;
            }
            if (ReferenceEquals(faultedReporter, value))
            {
                faultedReporter = null;
                ClearBoundReporter(value);
                return;
            }
            EnsureAvailable();
            bool active = ReferenceEquals(reporter, value);
            bool frozen = ReferenceEquals(quiescedReporter, value);
            if (!active && !frozen)
                throw new InvalidOperationException("The reporter is not bound to the Android input producer.");

            javaClass.CallStatic("abortInput", value.SessionId);
            ClearBoundReporter(value);
        }

        private void ClearBoundReporter(NativeInputReporter value)
        {
            bool active = ReferenceEquals(reporter, value);
            bool frozen = ReferenceEquals(quiescedReporter, value);
            if (!active && !frozen) return;
            if (active) reporter = null;
            if (frozen) quiescedReporter = null;
            if (active && quiescenceCompletion != null)
            {
                quiescenceCompletion = null;
                quiescenceRequestId = 0;
            }
            nativeFieldMode = false;
            quiescedNativeFieldMode = false;
            nativeProducerEpoch = 0;
            quiescedProducerEpoch = 0;
            transparentSessionId = 0;
            transparentEpoch = 0;
            lastTransparentRevision = 0;
            ReleaseContextJString();
        }

        private void ValidateOpen(NativeInputReporter value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (reporter != null || quiescenceCompletion != null)
                throw new InvalidOperationException("An Android input producer is already open.");
        }

        private void EnsureNativeFieldSession(int sessionId)
        {
            bool open = reporter != null && nativeFieldMode && reporter.SessionId == sessionId;
            bool frozen = quiescedReporter != null && quiescedNativeFieldMode &&
                          quiescedReporter.SessionId == sessionId;
            if (!open && !frozen)
                throw new InvalidOperationException("The Android native field session is not open.");
        }

        private void EnsureAvailable()
        {
            if (disposed || javaClass == null)
                throw new ObjectDisposedException(nameof(NativeInputAndroid));
        }

        private int NextProducerEpoch()
        {
            if (nextProducerEpoch == int.MaxValue)
                throw new InvalidOperationException("The Android input producer epoch space is exhausted.");
            return ++nextProducerEpoch;
        }

        private static int BuildInputType(NativeKeyboardConfig config, bool passwordMode = false)
        {
            if (config == null)
            {
                if (passwordMode)
                    return TYPE_CLASS_TEXT | TYPE_TEXT_VARIATION_PASSWORD;
                return TYPE_CLASS_TEXT | TYPE_TEXT_VARIATION_NORMAL;
            }

            if (passwordMode)
            {
                if (config.KeyboardType == KeyboardType.NumberPad ||
                    config.KeyboardType == KeyboardType.DecimalPad)
                    return TYPE_CLASS_NUMBER | TYPE_NUMBER_VARIATION_PASSWORD;
                return TYPE_CLASS_TEXT | TYPE_TEXT_VARIATION_PASSWORD;
            }

            int flags;

            switch (config.KeyboardType)
            {
                case KeyboardType.NumberPad:
                    return TYPE_CLASS_NUMBER;
                case KeyboardType.DecimalPad:
                    flags = TYPE_CLASS_NUMBER | TYPE_NUMBER_FLAG_DECIMAL | TYPE_NUMBER_FLAG_SIGNED;
                    return flags;
                case KeyboardType.PhonePad:
                    return TYPE_CLASS_PHONE;
                case KeyboardType.URL:
                    flags = TYPE_CLASS_TEXT | TYPE_TEXT_VARIATION_URI;
                    break;
                case KeyboardType.EmailAddress:
                    flags = TYPE_CLASS_TEXT | TYPE_TEXT_VARIATION_EMAIL_ADDRESS;
                    break;
                default:
                    flags = TYPE_CLASS_TEXT | TYPE_TEXT_VARIATION_NORMAL;
                    break;
            }

            switch (config.AutoCapitalization)
            {
                case AutoCapitalization.Words:
                    flags |= TYPE_TEXT_FLAG_CAP_WORDS;
                    break;
                case AutoCapitalization.Sentences:
                    flags |= TYPE_TEXT_FLAG_CAP_SENTENCES;
                    break;
                case AutoCapitalization.AllCharacters:
                    flags |= TYPE_TEXT_FLAG_CAP_CHARACTERS;
                    break;
            }

            if (config.AutoCorrection == AutoCorrection.Enabled)
                flags |= TYPE_TEXT_FLAG_AUTO_CORRECT;
            else if (config.AutoCorrection == AutoCorrection.Disabled)
                flags |= TYPE_TEXT_FLAG_NO_SUGGESTIONS;

            if (config.SpellChecking == SpellChecking.Disabled)
                flags |= TYPE_TEXT_FLAG_NO_SUGGESTIONS;

            return flags;
        }

        private static int BuildImeOptions(NativeKeyboardConfig config, bool acceptsNewlines)
        {
            var returnKey = config != null && !acceptsNewlines
                ? config.ReturnKeyType
                : ReturnKeyType.Default;
            int options;

            switch (returnKey)
            {
                case ReturnKeyType.Default:
                    options = acceptsNewlines ? IME_ACTION_NONE : IME_ACTION_DONE;
                    break;
                case ReturnKeyType.Go:
                    options = IME_ACTION_GO;
                    break;
                case ReturnKeyType.Search:
                    options = IME_ACTION_SEARCH;
                    break;
                case ReturnKeyType.Send:
                    options = IME_ACTION_SEND;
                    break;
                case ReturnKeyType.Next:
                    options = IME_ACTION_NEXT;
                    break;
                case ReturnKeyType.Previous:
                    options = IME_ACTION_PREVIOUS;
                    break;
                case ReturnKeyType.Enter:
                    options = IME_ACTION_NONE;
                    break;
                case ReturnKeyType.Done:
                    options = IME_ACTION_DONE;
                    break;
                default:
                    options = acceptsNewlines ? IME_ACTION_NONE : IME_ACTION_DONE;
                    break;
            }

            var flags = config?.ImeFlags ?? AndroidImeFlags.None;
            if ((flags & AndroidImeFlags.NoPersonalizedLearning) != 0)
                options |= IME_FLAG_NO_PERSONALIZED_LEARNING;
            if ((flags & AndroidImeFlags.ForceAscii) != 0)
                options |= IME_FLAG_FORCE_ASCII;

            return options;
        }

        private static int BuildAutofillHint(NativeKeyboardConfig config)
            => config == null ? -1 : (int)config.AutofillHint;

        #region Span-based message parsing

        private static bool NextInt(ReadOnlySpan<char> s, ref int pos, out int value)
        {
            int start = pos;
            while (pos < s.Length && s[pos] != SEP) pos++;
            bool ok = int.TryParse(s.Slice(start, pos - start),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            if (pos < s.Length) pos++;
            return ok;
        }

        private static int RequireInt(ReadOnlySpan<char> message, ref int position)
        {
            if (!NextInt(message, ref position, out int value))
                throw new FormatException("The Android native input callback contains an invalid integer field.");
            return value;
        }

        private NativeInputReporter ReadTransparentEnvelope(ReadOnlySpan<char> message,
            ref int position)
        {
            int sessionId = RequireInt(message, ref position);
            int epoch = RequireInt(message, ref position);
            int nativeRevision = RequireInt(message, ref position);
            var target = reporter;
            if (target == null || nativeFieldMode || target.SessionId != sessionId ||
                sessionId != transparentSessionId || epoch != transparentEpoch ||
                nativeRevision <= lastTransparentRevision)
                return null;
            if (nativeRevision != lastTransparentRevision + 1)
                throw new InvalidOperationException("Android transparent input revisions must be contiguous.");
            lastTransparentRevision = nativeRevision;
            return target;
        }

        #endregion

        private void HandleCompositionMessage(NativeInputReporter target, ReadOnlySpan<char> s)
        {
            int position = 0;
            int cursorPos = RequireInt(s, ref position);
            int separator = s.Slice(position).IndexOf(SEP);
            if (separator < 0)
                throw new FormatException("The Android composition callback is incomplete.");
            var clauseInfo = s.Slice(position, separator);
            var compText = s.Slice(position + separator + 1);

            int clauseCount = ParseClauses(clauseInfo, compText.Length);

            var data = new CompositionData
            {
                text = compText,
                clauses = new ReadOnlySpan<CompositionClause>(clauseBuf, 0, clauseCount),
                cursorPosition = cursorPos,
            };

            target.ReportCompositionChanged(data);
        }

        private int ParseClauses(ReadOnlySpan<char> clauseInfo, int textLength)
        {
            int count = 0;

            int pos = 0;
            while (pos < clauseInfo.Length)
            {
                var rest = clauseInfo.Slice(pos);
                int semicolon = rest.IndexOf(';');
                var entry = semicolon < 0 ? rest : rest.Slice(0, semicolon);
                pos += semicolon < 0 ? rest.Length : semicolon + 1;

                int comma1 = entry.IndexOf(',');
                if (comma1 < 0) continue;
                int comma2 = entry.Slice(comma1 + 1).IndexOf(',');
                if (comma2 < 0) continue;
                comma2 += comma1 + 1;

                if (int.TryParse(entry.Slice(0, comma1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int start) &&
                    int.TryParse(entry.Slice(comma1 + 1, comma2 - comma1 - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int end) &&
                    int.TryParse(entry.Slice(comma2 + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int styleVal))
                {
                    CompositionClause.EnsureCapacity(ref clauseBuf, count + 1);
                    clauseBuf[count++] = new CompositionClause
                    {
                        startOffset = start,
                        endOffset = end,
                        style = (CompositionClauseStyle)styleVal,
                    };
                }
            }

            if (count == 0 && textLength > 0)
                count = CompositionClause.FillFallback(ref clauseBuf, textLength);

            return count;
        }

        private static void HandleKeyDownMessage(NativeInputReporter target,
            ReadOnlySpan<char> s)
        {
            int pos = 0;
            int keyCode = RequireInt(s, ref pos);
            int modifiers = RequireInt(s, ref pos);
            target.ReportKeyDown((NativeKeyCode)keyCode, (NativeModifiers)modifiers);
        }

        private static int lastKeyboardDeviceWidthPx;

        private static float DeviceToUnityScale(int deviceWidthPx)
            => deviceWidthPx > 0 ? (float)Screen.width / deviceWidthPx : 1f;

        private void HandleKeyboardAreaMessage(string message)
        {
            var target = keyboardReporter;
            if (target == null) return;
            var s = message.AsSpan();
            int pos = 0;
            int visibleInt = RequireInt(s, ref pos);
            int height = RequireInt(s, ref pos);

            bool visible = visibleInt != 0;
            float scale = DeviceToUnityScale(lastKeyboardDeviceWidthPx);
            target.ReportKeyboardAreaChanged(visible,
                visible ? new Rect(0, 0, Screen.width, height * scale) : Rect.zero);
        }

        private void HandleKeyboardEventMessage(string message)
        {
            var target = keyboardReporter;
            if (target == null) return;
            var s = message.AsSpan();
            int pos = 0;
            int phase = RequireInt(s, ref pos);
            int x = RequireInt(s, ref pos);
            int y = RequireInt(s, ref pos);
            int w = RequireInt(s, ref pos);
            int h = RequireInt(s, ref pos);
            int durationMs = RequireInt(s, ref pos);
            int easing = RequireInt(s, ref pos);
            int fractionE4 = RequireInt(s, ref pos);
            int frameSynced = RequireInt(s, ref pos);

            if (w > 0) lastKeyboardDeviceWidthPx = w;
            float scale = DeviceToUnityScale(w);
            var value = new KeyboardEvent
            {
                phase = (KeyboardEventPhase)phase,
                area = new Rect(x * scale, y * scale, w * scale, h * scale),
                animationDuration = durationMs / 1000f,
                easing = (KeyboardEasing)easing,
                animationFraction = fractionE4 / 10000f,
                hasFrameSyncedAnimation = frameSynced != 0,
            };
            target.ReportKeyboardEvent(in value);
        }

        private NativeInputReporter RequireNativeFieldReporter(int sessionId)
        {
            var target = reporter;
            return target != null && nativeFieldMode && target.SessionId == sessionId
                ? target
                : null;
        }

        private void HandleQuiescedMessage(ReadOnlySpan<char> message)
        {
            int position = 0;
            int sessionId = RequireInt(message, ref position);
            int epoch = RequireInt(message, ref position);
            int nativeRevision = RequireInt(message, ref position);
            int authorityRevision = RequireInt(message, ref position);
            int requestId = RequireInt(message, ref position);
            var source = reporter;
            var completion = quiescenceCompletion;
            if (source == null || completion == null || source.SessionId != sessionId ||
                requestId != quiescenceRequestId)
                return;

            bool wasNativeField = nativeFieldMode;
            int producerEpoch = wasNativeField ? nativeProducerEpoch : transparentEpoch;
            if (producerEpoch <= 0)
                throw new InvalidOperationException("An Android input barrier has no producer epoch.");
            if (wasNativeField)
            {
                if (epoch != 0)
                    throw new InvalidOperationException("An Android native field barrier cannot carry a transparent epoch.");
                source.ReportFieldQuiesced(nativeRevision, authorityRevision);
            }
            else
            {
                if (sessionId != transparentSessionId || epoch != transparentEpoch) return;
                if (authorityRevision != 1)
                    throw new InvalidOperationException("An Android transparent barrier has an invalid authority revision.");
                if (nativeRevision <= lastTransparentRevision) return;
                if (nativeRevision != lastTransparentRevision + 1)
                    throw new InvalidOperationException("Android transparent input revisions must be contiguous.");
                lastTransparentRevision = nativeRevision;
            }

            reporter = null;
            nativeFieldMode = false;
            quiescedReporter = source;
            quiescedNativeFieldMode = wasNativeField;
            quiescedProducerEpoch = producerEpoch;
            quiescenceCompletion = null;
            quiescenceRequestId = 0;
            completion();
        }

        private sealed class MessageReceiver : MonoBehaviour
        {
            [Preserve]
            private void OnTextInput(string text)
            {
                if (instance == null) return;
                var message = text.AsSpan();
                int position = 0;
                var target = instance.ReadTransparentEnvelope(message, ref position);
                if (target == null) return;
                target.ReportTextInput(message.Slice(position).ToString());
            }

            [Preserve]
            private void OnTextReplacement(string message)
            {
                if (instance == null) return;
                var value = message.AsSpan();
                int position = 0;
                var target = instance.ReadTransparentEnvelope(value, ref position);
                if (target == null) return;
                int version = RequireInt(value, ref position);
                int start = RequireInt(value, ref position);
                int length = RequireInt(value, ref position);
                if (version != instance.contextVersion) return;
                target.ReportTextReplacement(start, length, value.Slice(position).ToString());
            }

            [Preserve]
            private void OnKeyDown(string message)
            {
                if (instance == null) return;
                var value = message.AsSpan();
                int position = 0;
                var target = instance.ReadTransparentEnvelope(value, ref position);
                if (target != null) HandleKeyDownMessage(target, value.Slice(position));
            }

            [Preserve]
            private void OnEditorAction(string message)
            {
                if (instance == null) return;
                var value = message.AsSpan();
                int position = 0;
                var target = instance.ReadTransparentEnvelope(value, ref position);
                if (target == null) return;
                if (!int.TryParse(value.Slice(position), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int action))
                    throw new FormatException("The Android editor-action callback has an invalid action code.");

                switch (action)
                {
                    case IME_ACTION_GO:
                    case IME_ACTION_SEARCH:
                    case IME_ACTION_SEND:
                    case IME_ACTION_DONE:
                        target.ReportEditorAction(NativeEditorAction.Submit);
                        break;
                    case IME_ACTION_UNSPECIFIED:
                    case IME_ACTION_NONE:
                        break;
                    case IME_ACTION_NEXT:
                        target.ReportEditorAction(NativeEditorAction.Next);
                        break;
                    case IME_ACTION_PREVIOUS:
                        target.ReportEditorAction(NativeEditorAction.Previous);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(action), action,
                            "The Android IME action is unsupported.");
                }
            }

            [Preserve]
            private void OnTextAction(string message)
            {
                if (instance == null) return;
                var value = message.AsSpan();
                int position = 0;
                var target = instance.ReadTransparentEnvelope(value, ref position);
                if (target != null)
                    target.ReportEditorAction(value.Slice(position).ToString());
            }

            [Preserve]
            private void OnCompositionChanged(string message)
            {
                if (instance == null) return;
                var value = message.AsSpan();
                int position = 0;
                var target = instance.ReadTransparentEnvelope(value, ref position);
                if (target != null)
                    instance.HandleCompositionMessage(target, value.Slice(position));
            }

            [Preserve]
            private void OnCompositionEnded(string message)
            {
                if (instance == null) return;
                var value = message.AsSpan();
                int position = 0;
                var target = instance.ReadTransparentEnvelope(value, ref position);
                target?.ReportCompositionEnded();
            }

            [Preserve]
            private void OnNativeInputFault(string message)
            {
                if (instance == null) return;
                var value = message.AsSpan();
                int position = 0;
                int sessionId = RequireInt(value, ref position);
                int epoch = RequireInt(value, ref position);
                var target = instance.reporter;
                int expectedEpoch = instance.nativeFieldMode
                    ? instance.nativeProducerEpoch
                    : instance.transparentEpoch;
                if (target == null || target.SessionId != sessionId || expectedEpoch != epoch)
                {
                    target = instance.quiescedReporter;
                    expectedEpoch = instance.quiescedProducerEpoch;
                }
                if (target == null || target.SessionId != sessionId || epoch <= 0 ||
                    expectedEpoch != epoch) return;

                string reason = value.Slice(position).ToString();
                if (reason.Length == 0) reason = "The Android producer reported no failure detail.";
                if (instance.faultedReporter != null &&
                    !ReferenceEquals(instance.faultedReporter, target))
                    throw new InvalidOperationException("Another Android input fault is being reported.");
                instance.faultedReporter = target;
                try
                {
                    target.ReportFault(new InvalidOperationException(
                        $"Android native input session {sessionId} failed: {reason}"));
                }
                finally
                {
                    if (ReferenceEquals(instance.faultedReporter, target))
                    {
                        instance.faultedReporter = null;
                        instance.ClearBoundReporter(target);
                    }
                }
            }

            [Preserve]
            private void OnNativeFieldEdit(string message)
            {
                if (instance == null) return;
                var value = message.AsSpan();
                int position = 0;
                int sessionId = RequireInt(value, ref position);
                int nativeRevision = RequireInt(value, ref position);
                int authorityRevision = RequireInt(value, ref position);
                int start = RequireInt(value, ref position);
                int length = RequireInt(value, ref position);
                int selectionStart = RequireInt(value, ref position);
                int selectionEnd = RequireInt(value, ref position);
                var target = instance.RequireNativeFieldReporter(sessionId);
                if (target == null) return;
                target.ReportFieldEdit(nativeRevision, authorityRevision,
                    start, length, value.Slice(position).ToString(), selectionStart, selectionEnd);
            }

            [Preserve]
            private void OnNativeFieldComposition(string message)
            {
                if (instance == null) return;
                var value = message.AsSpan();
                int position = 0;
                int sessionId = RequireInt(value, ref position);
                int nativeRevision = RequireInt(value, ref position);
                int authorityRevision = RequireInt(value, ref position);
                int phase = RequireInt(value, ref position);
                int start = RequireInt(value, ref position);
                int length = RequireInt(value, ref position);
                int cursor = RequireInt(value, ref position);
                var target = instance.RequireNativeFieldReporter(sessionId);
                if (target == null) return;
                if ((uint)phase > (uint)NativeFieldCompositionPhase.Cancel)
                    throw new FormatException("The Android composition callback has an unknown phase.");
                target.ReportFieldComposition(nativeRevision, authorityRevision,
                    (NativeFieldCompositionPhase)phase, start, length,
                    value.Slice(position).ToString(), cursor);
            }

            [Preserve]
            private void OnNativeFieldSelection(string message)
            {
                if (instance == null) return;
                var value = message.AsSpan();
                int position = 0;
                int sessionId = RequireInt(value, ref position);
                int nativeRevision = RequireInt(value, ref position);
                int authorityRevision = RequireInt(value, ref position);
                int start = RequireInt(value, ref position);
                int end = RequireInt(value, ref position);
                var target = instance.RequireNativeFieldReporter(sessionId);
                if (target == null) return;
                target.ReportFieldSelection(nativeRevision, authorityRevision, start, end);
            }

            [Preserve]
            private void OnNativeFieldAction(string message)
            {
                if (instance == null) return;
                var value = message.AsSpan();
                int position = 0;
                int sessionId = RequireInt(value, ref position);
                int nativeRevision = RequireInt(value, ref position);
                int authorityRevision = RequireInt(value, ref position);
                int modifiers = RequireInt(value, ref position);
                var target = instance.RequireNativeFieldReporter(sessionId);
                if (target == null) return;
                target.ReportFieldAction(nativeRevision, authorityRevision,
                    value.Slice(position).ToString(), (NativeModifiers)modifiers);
            }

            [Preserve]
            private void OnNativeInputQuiesced(string message)
            {
                if (instance == null) return;
                instance.HandleQuiescedMessage(message.AsSpan());
            }

            [Preserve]
            private void OnKeyboardAreaChanged(string message)
            {
                if (instance == null) return;
                instance.HandleKeyboardAreaMessage(message);
            }

            [Preserve]
            private void OnKeyboardEvent(string message)
            {
                if (instance == null) return;
                instance.HandleKeyboardEventMessage(message);
            }

            [Preserve]
            private void OnSelectionChanged(string message)
            {
                if (instance == null) return;
                var s = message.AsSpan();
                int pos = 0;
                var target = instance.ReadTransparentEnvelope(s, ref pos);
                if (target == null) return;
                int version = RequireInt(s, ref pos);
                int start = RequireInt(s, ref pos);
                int end = RequireInt(s, ref pos);
                if (version != instance.contextVersion) return;
                target.ReportSelectionChanged(start, end);
            }

        }
    }
}

#endif
