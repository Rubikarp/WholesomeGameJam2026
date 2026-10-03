using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LightSide
{
    internal static class NativeInputSession
    {
        private const int WholeContextLimit = 1024;
        private const int ContextWindowLength = 512;
        private const float KeyboardLossGrace = 1f;

        private static UniTextEditable editor;
        private static EditableInputContext context;
        private static NativeFieldReplicaSession replica;
        private static NativeInputReporter reporter;
        private static NativeInputReporter transferredReporter;
        private static int generation;
        private static int nextInputSessionId;
        private static int activeSessionId;
        private static InputProducerState producerState;
        private static bool forceContextSync;
        private static (object identity, int version) platformMutationTextStamp;
        private static (object identity, int version) contextTextStamp;
        private static int contextSelectionStart = -1;
        private static int contextSelectionEnd = -1;
        private static int contextWindowStart = -1;
        private static int contextWindowEnd = -1;
        private static string contextWindow;
        private static bool keyboardRequested;
        private static ProducerConfiguration appliedConfiguration;
        private static bool replicaKeyboardLost;
        private static float replicaKeyboardLostAt;
        private static int selfInflictedKeyboardHides;
        private static bool configurationDirty;
        private static UniTextEditable inputDispatchEditor;
        private static int fieldIntentDepth;
        private static bool compositionInvalidated;
        private static QuiescencePurpose quiescencePurpose;
        private static Action releaseCompletion;
        private static Action compositionCommitCompletion;
        private static Task pendingTransparentAction;
        private static UniTextEditable pendingTransparentActionEditor;
        private static NativeInputReporter pendingTransparentActionReporter;
        private static readonly Queue<PendingTransparentIntent> pendingTransparentIntents = new(4);
        private static Action<NativeInputReporter> producerAcquisition;
        private static Action<NativeInputReporter> keyProducerRequest;
        private static bool resetting;
        private static readonly List<NativeFieldOverlayBehavior> fieldPresentations = new(2);
        private static readonly List<PlaceholderDecorator> placeholders = new(1);
        private static bool inputGeometryDirty = true;
        private static bool inputGeometryStampValid;
        private static bool inputGeometryOutputValid;
        private static bool inputGeometryVisible;
        private static bool inputGeometryOutputVisible;
        private static RectTransform inputGeometryTextTransform;
        private static RectTransform inputGeometryFieldTransform;
        private static Camera inputGeometryCamera;
        private static Matrix4x4 inputGeometryTextMatrix;
        private static Matrix4x4 inputGeometryFieldMatrix;
        private static Matrix4x4 inputGeometryCameraView;
        private static Matrix4x4 inputGeometryCameraProjection;
        private static Rect inputGeometryTextRect;
        private static Rect inputGeometryFieldLocalRect;
        private static Rect inputGeometryCameraPixelRect;
        private static Rect inputGeometryKeyboardArea;
        private static int inputGeometryScreenWidth;
        private static int inputGeometryScreenHeight;
        private static float inputGeometryImeOffsetX;
        private static float inputGeometryImeScaleX;
        private static float inputGeometryImeOffsetY;
        private static float inputGeometryImeScaleY;
        private static Vector2 inputGeometryCaretPosition;
        private static float inputGeometryLineHeight;
        private static Rect inputGeometryFieldRect;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            Reset();
            resetting = false;
            UniTextEditable.EditingSessionRequested -= Request;
            UniTextEditable.EditingSessionRequested += Request;
            UniTextEditable.EditingSessionReleaseRequested = Release;
            UniTextEditable.EditingSessionAbortRequested = Abort;
            UniTextEditable.CompositionCommitRequested = CommitComposition;
            UniTextNativeInput.KeyboardVisibilityChanged -= OnKeyboardVisibilityChanged;
            UniTextNativeInput.KeyboardVisibilityChanged += OnKeyboardVisibilityChanged;
            Application.quitting -= Reset;
            Application.quitting += Reset;
#if UNITY_EDITOR
            EditorLifecycle.ManagedCleaning -= Reset;
            EditorLifecycle.ManagedCleaning += Reset;
#endif
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor() => Initialize();
#endif

        private static void Request(UniTextEditable target, bool showKeyboard)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (resetting)
            {
                target.Deactivate();
                return;
            }
            if (ReferenceEquals(editor, target))
            {
                if (showKeyboard)
                {
                    try { Refocus(target); }
                    catch
                    {
                        CleanupFailedSession(target);
                        throw;
                    }
                }
                return;
            }

            NativeKeyInputSession.SupersedePendingAcquisition();
            var sessionId = AllocateInputSessionId();
            var token = ++generation;
            editor = target;
            activeSessionId = sessionId;
            keyboardRequested = showKeyboard;
            context = new EditableInputContext(target);
            producerState = InputProducerState.Waiting;
            target.FrameTicked += Tick;
            target.CompositionStateTransitioning += OnCompositionStateChanged;
            target.InputSessionConfigurationChanged += InvalidateConfiguration;
            target.InputGeometryChanged += InvalidateInputGeometry;

            try
            {
                UniTextNativeInput.SetImeEnabled(true);
                configurationDirty = false;
                Action<NativeInputReporter> acquired = source =>
                    ContinueRequest(target, token, sessionId, source);
                producerAcquisition = acquired;
                NativeKeyInputSession.TransferToEditable(acquired);
            }
            catch
            {
                try
                {
                    if (ReferenceEquals(editor, target)) ForceReleaseCurrent();
                    else CloseTransferredProducer();
                }
                catch (Exception cleanupError)
                {
                    Debug.LogException(cleanupError, target);
                }
                throw;
            }
        }

        private static void ContinueRequest(UniTextEditable target, int token, int sessionId,
            NativeInputReporter source)
        {
            producerAcquisition = null;
            if (!ReferenceEquals(editor, target) || activeSessionId != sessionId ||
                producerState != InputProducerState.Waiting)
            {
                CloseProducer(source);
                return;
            }

            if (source != null)
            {
                if (transferredReporter != null &&
                    !ReferenceEquals(transferredReporter, source))
                {
                    CloseProducer(source);
                    throw new InvalidOperationException(
                        "Two quiesced input producers cannot be inherited by one editing session.");
                }
                transferredReporter = source;
            }
            producerState = InputProducerState.Closed;
            if (releaseCompletion != null)
            {
                CompleteRelease();
                return;
            }

            try
            {
                configurationDirty = false;
                var request = default(KeyboardRequest);
                target.ResolveKeyboardRequest(ref request);
                request.config?.Validate();
                if (!IsCurrent(target, token)) return;
                var presentation = GetPresentation(target);
                OpenProducer(target, request.config, presentation, keyboardRequested);
                if (!IsCurrent(target, token) || producerState == InputProducerState.Quiescing)
                    return;
                forceContextSync = true;
                PushState(target);
            }
            catch
            {
                try
                {
                    if (ReferenceEquals(editor, target)) ForceReleaseCurrent();
                    else CloseTransferredProducer();
                }
                catch (Exception cleanupError)
                {
                    Debug.LogException(cleanupError, target);
                }
                throw;
            }
        }

        /// <summary>
        /// Re-requests input for the editor that already owns the session. Replacing the producer
        /// restarts the software keyboard and replays its show animation, so it happens only when
        /// the request is not already satisfied.
        /// </summary>
        private static void Refocus(UniTextEditable target)
        {
            var satisfied = keyboardRequested && !configurationDirty &&
                            producerState == InputProducerState.Open &&
                            UniTextNativeInput.IsKeyboardVisible;
            keyboardRequested = true;
            if (satisfied)
            {
                CatZones.input.Meow("[Input] Refocus satisfied; keyboard left alone");
                return;
            }
            CatZones.input.MeowFormat(
                "[Input] Refocus forces a producer restart: state={0}, dirty={1}, keyboardVisible={2}",
                producerState, configurationDirty, UniTextNativeInput.IsKeyboardVisible);
            configurationDirty = true;
            if (producerState != InputProducerState.Open) return;
            BeginQuiescence(QuiescencePurpose.Resume,
                NativeCompositionDisposition.Preserve);
        }

        private static bool OpenProducer(UniTextEditable target, NativeKeyboardConfig config,
            NativeFieldOverlayBehavior fieldPresentation, bool showKeyboard)
        {
            var token = generation;
            var inputSessionId = activeSessionId;
            var previous = producerState == InputProducerState.Quiesced
                ? reporter
                : transferredReporter;
            var visible = showKeyboard && fieldPresentation != null;
            var currentReplica = replica;
            var inputContext = context;
            var previousAcceptsEditingReports = inputContext.AcceptsEditingReports;
            var recreateReplica = false;
            var presentation = visible
                ? fieldPresentation.Capture(GetPlaceholderText(target))
                : default;
            var shape = NativeFieldShape.For(target, config);
            if (visible && currentReplica != null)
            {
                recreateReplica = currentReplica.RequiresRecreation(in shape, in presentation);
                CatZones.input.MeowFormat(
                    "[Input] Replica {0}: wraps={1}, newlines={2}, returnKey={3}, presenter='{4}'",
                    recreateReplica ? "rebuild" : "update", shape.Wraps, shape.AcceptsNewlines,
                    shape.ReturnKey, presentation.PresenterId);
                if (!recreateReplica)
                    currentReplica.Update(config, target.SecureInput, in shape,
                        target.ReadOnly, target.IsCopyAllowed(), in presentation);
                if (!IsCurrent(target, token) || !ReferenceEquals(replica, currentReplica) ||
                    !currentReplica.IsActive) return false;
            }

            inputContext.AcceptsEditingReports = !visible;
            producerState = InputProducerState.Opening;
            NativeInputReporter candidate = null;
            NativeFieldReplicaSession candidateReplica = null;
            try
            {
                candidate = UniTextNativeInput.CreateReporter(
                    inputSessionId, inputContext, inputContext, previous);
                if (!ReferenceEquals(editor, target) || !ReferenceEquals(context, inputContext) ||
                    activeSessionId != inputSessionId)
                {
                    UniTextNativeInput.AbortInput(candidate);
                    return false;
                }
                reporter = candidate;
                if (visible && currentReplica != null)
                {
                    if (recreateReplica)
                    {
                        selfInflictedKeyboardHides++;
                        currentReplica.Reopen(candidate, config, target.SecureInput,
                            in shape, target.ReadOnly, target.IsCopyAllowed(),
                            in presentation);
                    }
                    else
                        currentReplica.Focus(candidate);
                }
                else if (visible)
                {
                    CatZones.input.MeowFormat(
                        "[Input] Replica open: session={0}, wraps={1}, newlines={2}, returnKey={3}, presenter='{4}'",
                        inputSessionId, shape.Wraps, shape.AcceptsNewlines, shape.ReturnKey,
                        presentation.PresenterId);
                    candidateReplica = NativeFieldReplicaSession.Create(target, inputSessionId);
                    replica = candidateReplica;
                    candidateReplica.Open(candidate, config, target.SecureInput,
                        in shape, target.ReadOnly, target.IsCopyAllowed(), in presentation);
                }
                else
                {
                    CatZones.input.MeowFormat(
                        "[Input] Transparent producer open: session={0}, keyboard={1}, newlines={2}",
                        inputSessionId, showKeyboard, shape.AcceptsNewlines);
                    var request = new NativeInputOpenRequest(showKeyboard, config,
                        target.SecureInput, shape.AcceptsNewlines);
                    UniTextNativeInput.OpenInput(candidate, in request);
                }
            }
            catch
            {
                inputContext.AcceptsEditingReports = previousAcceptsEditingReports;
                if (candidate != null && !candidate.IsClosed)
                {
                    try { UniTextNativeInput.AbortInput(candidate); }
                    catch (Exception cleanupError) { Debug.LogException(cleanupError, target); }
                }
                candidateReplica?.Detach();
                if (ReferenceEquals(replica, candidateReplica)) replica = null;
                if (ReferenceEquals(reporter, candidate))
                {
                    reporter = previous != null && previous.IsClosed ? null : previous;
                    producerState = reporter == null
                        ? InputProducerState.Closed
                        : InputProducerState.Quiesced;
                }
                else if (ReferenceEquals(editor, target) &&
                         ReferenceEquals(context, inputContext) &&
                         producerState == InputProducerState.Opening)
                    producerState = previous == null
                        ? InputProducerState.Closed
                        : InputProducerState.Quiesced;
                throw;
            }

            if (!ReferenceEquals(editor, target) || !ReferenceEquals(context, inputContext) ||
                !ReferenceEquals(reporter, candidate) || activeSessionId != inputSessionId ||
                producerState != InputProducerState.Opening) return false;
            if (!visible && currentReplica != null)
            {
                currentReplica.Detach();
                if (ReferenceEquals(replica, currentReplica)) replica = null;
            }
            transferredReporter = null;
            producerState = InputProducerState.Open;
            appliedConfiguration = ProducerConfiguration.Capture(target, config,
                fieldPresentation, showKeyboard, in shape, in presentation);
            InvalidateInputGeometryProducer();
            if (releaseCompletion != null)
            {
                BeginQuiescence(QuiescencePurpose.Release);
                return false;
            }
            if (compositionCommitCompletion != null)
            {
                BeginQuiescence(QuiescencePurpose.Resume);
                return false;
            }
            if (compositionInvalidated)
            {
                BeginQuiescence(QuiescencePurpose.Resume,
                    NativeCompositionDisposition.Cancel);
                return false;
            }
            if (configurationDirty)
            {
                if (!AppliedConfigurationStillCurrent(target))
                {
                    BeginQuiescence(QuiescencePurpose.Resume,
                        NativeCompositionDisposition.Preserve);
                    return false;
                }
                CatZones.input.Meow(
                    "[Input] Configuration invalidation matched the just-opened producer; kept");
                configurationDirty = false;
            }
            return true;
        }

        private static void Release(UniTextEditable target, Action completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            if (!ReferenceEquals(editor, target))
            {
                completion();
                return;
            }
            CatZones.input.MeowFormat(
                "[Input] Release requested: session={0}, replica={1}, keyboardRequested={2}, state={3}",
                activeSessionId, replica != null, keyboardRequested, producerState);
            if (releaseCompletion != null)
                throw new InvalidOperationException("An input session release is already pending.");
            ++generation;
            releaseCompletion = completion;

            if (producerState == InputProducerState.Quiescing)
            {
                quiescencePurpose = QuiescencePurpose.Release;
                return;
            }

            if (producerState == InputProducerState.Waiting ||
                producerState == InputProducerState.Opening) return;
            if (producerState == InputProducerState.Quiesced)
            {
                quiescencePurpose = QuiescencePurpose.Release;
                if (pendingTransparentAction != null) return;
                CompleteRelease();
                return;
            }
            if (producerState == InputProducerState.Closed)
            {
                CompleteRelease();
                return;
            }

            BeginQuiescence(QuiescencePurpose.Release);
        }

        private static void Abort(UniTextEditable target)
        {
            if (!ReferenceEquals(editor, target)) return;
            CatZones.input.MeowFormat(
                "[Input] Abort requested: session={0}, replica={1}, state={2}",
                activeSessionId, replica != null, producerState);
            ++generation;
            ForceReleaseCurrent();
        }

        private static void CommitComposition(UniTextEditable target, Action completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            if (!ReferenceEquals(editor, target))
            {
                completion();
                return;
            }
            if (compositionCommitCompletion != null)
                throw new InvalidOperationException("A composition commit is already pending.");
            compositionCommitCompletion = completion;

            if (producerState == InputProducerState.Waiting ||
                producerState == InputProducerState.Quiescing ||
                producerState == InputProducerState.Opening)
                return;
            if (producerState == InputProducerState.Open)
            {
                BeginQuiescence(QuiescencePurpose.Resume);
                return;
            }
            if (producerState == InputProducerState.Quiesced &&
                pendingTransparentAction != null)
                return;
            CompleteCompositionResolution();
        }

        private static void BeginQuiescence(QuiescencePurpose purpose,
            NativeCompositionDisposition disposition = NativeCompositionDisposition.Commit)
        {
            if (producerState == InputProducerState.Quiescing)
            {
                if (quiescencePurpose == purpose) return;
                throw new InvalidOperationException("Another input quiescence operation is pending.");
            }
            if (producerState != InputProducerState.Open)
                throw new InvalidOperationException("The active input producer is not accepting input.");
            quiescencePurpose = purpose;
            producerState = InputProducerState.Quiescing;
            var target = editor;
            var source = reporter;
            try
            {
                UniTextNativeInput.QuiesceInput(source, disposition,
                    () => OnInputQuiesced(source));
            }
            catch
            {
                if (ReferenceEquals(reporter, source) &&
                    producerState == InputProducerState.Quiescing &&
                    ReferenceEquals(editor, target))
                    CleanupFailedSession(target);
                throw;
            }
        }

        private static void OnInputQuiesced(NativeInputReporter source)
        {
            if (!ReferenceEquals(reporter, source) ||
                producerState != InputProducerState.Quiescing) return;

            producerState = InputProducerState.Quiesced;
            if (pendingTransparentAction != null) return;
            FinishInputQuiescence(source);
        }

        private static void FinishInputQuiescence(NativeInputReporter source)
        {
            if (!ReferenceEquals(reporter, source) ||
                producerState != InputProducerState.Quiesced) return;
            var purpose = quiescencePurpose;
            quiescencePurpose = QuiescencePurpose.None;
            var target = editor;
            ExceptionDispatchInfo failure = null;
            var synchronized = SynchronizeInvalidatedComposition(target, ref failure);
            if (!synchronized)
            {
                CaptureCleanup(ForceReleaseCurrent, target, ref failure);
                CaptureCleanup(() => target?.Deactivate(), target, ref failure);
                failure?.Throw();
                return;
            }
            try
            {
                switch (purpose)
                {
                    case QuiescencePurpose.Release:
                        CompleteRelease();
                        break;
                    case QuiescencePurpose.Resume:
                        CompleteCompositionResolution();
                        break;
                    default:
                        throw new InvalidOperationException("The input producer reported an unrequested barrier.");
                }
            }
            catch (Exception error)
            {
                if (failure == null) failure = ExceptionDispatchInfo.Capture(error);
                else Debug.LogException(error, target);
            }
            if (failure != null && ReferenceEquals(editor, target) &&
                ReferenceEquals(reporter, source) && producerState == InputProducerState.Quiesced)
                CleanupFailedSession(target);
            failure?.Throw();
        }

        private static void CompleteCompositionResolution()
        {
            var target = editor;
            if (ReferenceEquals(target, null))
            {
                var completion = compositionCommitCompletion;
                compositionCommitCompletion = null;
                completion?.Invoke();
                return;
            }

            var resume = producerState == InputProducerState.Quiesced;
            ExceptionDispatchInfo failure = null;
            var synchronized = CompletePendingCompositionCommit(
                target, ref failure, reporter != null);
            if (!synchronized || failure != null)
            {
                CaptureCleanup(ForceReleaseCurrent, target, ref failure);
                CaptureCleanup(target.Deactivate, target, ref failure);
                failure?.Throw();
                return;
            }
            if (resume && ReferenceEquals(editor, target))
            {
                configurationDirty = true;
                CaptureCleanup(() => RefreshConfiguration(target), target, ref failure);
            }
            failure?.Throw();
        }

        private static bool SynchronizeInvalidatedComposition(UniTextEditable target,
            ref ExceptionDispatchInfo failure)
        {
            if (!compositionInvalidated) return true;
            compositionInvalidated = false;
            if (ReferenceEquals(target, null)) return true;
            forceContextSync = true;
            try
            {
                if (replica != null) replica.SyncExternal();
                else PushTextContext(target);
                return true;
            }
            catch (Exception error)
            {
                if (failure == null) failure = ExceptionDispatchInfo.Capture(error);
                else Debug.LogException(error, target);
                return false;
            }
        }

        private static bool CompletePendingCompositionCommit(UniTextEditable target,
            ref ExceptionDispatchInfo failure, bool project = true)
        {
            var completion = compositionCommitCompletion;
            compositionCommitCompletion = null;
            CaptureCleanup(() => completion?.Invoke(), target, ref failure);
            if (!project || !ReferenceEquals(editor, target)) return true;
            compositionInvalidated = false;
            forceContextSync = true;
            try
            {
                if (replica != null) replica.SyncExternal();
                else PushTextContext(target);
                return true;
            }
            catch (Exception error)
            {
                if (failure == null) failure = ExceptionDispatchInfo.Capture(error);
                else Debug.LogException(error, target);
                return false;
            }
        }

        private static void CompleteRelease()
        {
            var target = editor;
            var completion = releaseCompletion;
            releaseCompletion = null;
            if (ReferenceEquals(target, null))
            {
                completion?.Invoke();
                return;
            }

            ExceptionDispatchInfo failure = null;
            if (!CompletePendingCompositionCommit(target, ref failure, reporter != null))
            {
                releaseCompletion = completion;
                CaptureCleanup(ForceReleaseCurrent, target, ref failure);
                failure?.Throw();
                return;
            }
            var source = reporter ?? transferredReporter;
            replica?.Detach();
            replica = null;
            ClearManagedSession(target);
            transferredReporter = source;
            CaptureCleanup(() => completion?.Invoke(), target, ref failure);
            var keyRequest = keyProducerRequest;
            keyProducerRequest = null;
            if (keyRequest != null && ReferenceEquals(transferredReporter, source))
            {
                transferredReporter = null;
                CaptureCleanup(() => keyRequest(source), target, ref failure);
            }
            if (ReferenceEquals(transferredReporter, source))
                CaptureCleanup(CloseTransferredProducer, target, ref failure);
            failure?.Throw();
        }

        private static void ForceReleaseCurrent()
        {
            var target = editor;
            if (ReferenceEquals(target, null))
            {
                ExceptionDispatchInfo emptyFailure = null;
                CaptureCleanup(() => AbortProducer(reporter), null, ref emptyFailure);
                CaptureCleanup(() => AbortProducer(transferredReporter), null, ref emptyFailure);
                CaptureCleanup(() => replica?.Detach(), null, ref emptyFailure);
                replica = null;
                reporter = null;
                transferredReporter = null;
                pendingTransparentAction = null;
                pendingTransparentActionEditor = null;
                pendingTransparentActionReporter = null;
                pendingTransparentIntents.Clear();
                emptyFailure?.Throw();
                return;
            }

            var source = reporter;
            var inherited = transferredReporter;
            var acquisition = producerAcquisition;
            var release = releaseCompletion;
            var compositionCommit = compositionCommitCompletion;
            var keyRequest = keyProducerRequest;
            producerAcquisition = null;
            releaseCompletion = null;
            compositionCommitCompletion = null;
            keyProducerRequest = null;
            quiescencePurpose = QuiescencePurpose.None;

            ExceptionDispatchInfo failure = null;
            if (acquisition != null)
                CaptureCleanup(() => NativeKeyInputSession.CancelTransferToEditable(acquisition),
                    target, ref failure);
            CaptureCleanup(() => AbortProducer(source), target, ref failure);
            if (!ReferenceEquals(inherited, source))
                CaptureCleanup(() => AbortProducer(inherited), target, ref failure);
            CaptureCleanup(() => replica?.Detach(), target, ref failure);
            replica = null;
            ClearManagedSession(target);
            transferredReporter = null;
            CaptureCleanup(() => UniTextNativeInput.SetImeEnabled(false), target, ref failure);
            CaptureCleanup(() => compositionCommit?.Invoke(), target, ref failure);
            CaptureCleanup(() => release?.Invoke(), target, ref failure);
            CaptureCleanup(() => keyRequest?.Invoke(null), target, ref failure);
            failure?.Throw();
        }

        private static void CaptureCleanup(Action action, UnityEngine.Object owner,
            ref ExceptionDispatchInfo failure)
        {
            try { action(); }
            catch (Exception error)
            {
                if (failure == null) failure = ExceptionDispatchInfo.Capture(error);
                else Debug.LogException(error, owner);
            }
        }

        private static void CleanupFailedSession(UniTextEditable target)
        {
            ExceptionDispatchInfo failure = null;
            if (ReferenceEquals(editor, target))
                CaptureCleanup(ForceReleaseCurrent, target, ref failure);
            CaptureCleanup(target.Deactivate, target, ref failure);
            if (failure != null) Debug.LogException(failure.SourceException, target);
        }

        private static void ClearManagedSession(UniTextEditable target)
        {
            target.FrameTicked -= Tick;
            target.CompositionStateTransitioning -= OnCompositionStateChanged;
            target.InputSessionConfigurationChanged -= InvalidateConfiguration;
            target.InputGeometryChanged -= InvalidateInputGeometry;
            editor = null;
            context = null;
            reporter = null;
            activeSessionId = 0;
            producerState = InputProducerState.Closed;
            pendingTransparentAction = null;
            pendingTransparentActionEditor = null;
            pendingTransparentActionReporter = null;
            pendingTransparentIntents.Clear();
            ResetContextCache();
            ResetInputGeometry();
        }

        private static void CloseTransferredProducer()
        {
            var source = transferredReporter;
            if (source == null) return;
            transferredReporter = null;
            ExceptionDispatchInfo failure = null;
            CaptureCleanup(() => CloseProducer(source), null, ref failure);
            CaptureCleanup(() => UniTextNativeInput.SetImeEnabled(false), null, ref failure);
            failure?.Throw();
        }

        private static void AbortProducer(NativeInputReporter source)
        {
            if (source != null && !source.IsClosed) UniTextNativeInput.AbortInput(source);
        }

        internal static void CloseProducer(NativeInputReporter source)
        {
            if (source == null || source.IsClosed) return;
            try { UniTextNativeInput.CloseInput(source); }
            catch (Exception error)
            {
                try
                {
                    if (!source.IsClosed) UniTextNativeInput.AbortInput(source);
                }
                catch (Exception cleanupError) { Debug.LogException(cleanupError); }
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }

        private static void Reset()
        {
            resetting = true;
            NativeKeyInputSession.SupersedePendingAcquisition();
            keyProducerRequest = null;
            ++generation;
            quiescencePurpose = QuiescencePurpose.None;
            ExceptionDispatchInfo failure = null;
            var target = editor;
            if (!ReferenceEquals(target, null))
            {
                CaptureCleanup(ForceReleaseCurrent, target, ref failure);
                CaptureCleanup(target.Deactivate, target, ref failure);
            }
            else
            {
                var release = releaseCompletion;
                var compositionCommit = compositionCommitCompletion;
                var acquisition = producerAcquisition;
                producerAcquisition = null;
                releaseCompletion = null;
                compositionCommitCompletion = null;
                if (acquisition != null)
                    CaptureCleanup(() => NativeKeyInputSession.CancelTransferToEditable(acquisition),
                        null, ref failure);
                CaptureCleanup(() => AbortProducer(reporter), null, ref failure);
                CaptureCleanup(() => AbortProducer(transferredReporter), null, ref failure);
                CaptureCleanup(() => compositionCommit?.Invoke(), null, ref failure);
                CaptureCleanup(() => release?.Invoke(), null, ref failure);
            }

            CaptureCleanup(() => UniTextNativeInput.SetImeEnabled(false), target, ref failure);

            replica?.Detach();
            replica = null;
            editor = null;
            context = null;
            reporter = null;
            activeSessionId = 0;
            producerState = InputProducerState.Closed;
            releaseCompletion = null;
            compositionCommitCompletion = null;
            pendingTransparentAction = null;
            pendingTransparentActionEditor = null;
            pendingTransparentActionReporter = null;
            pendingTransparentIntents.Clear();
            producerAcquisition = null;
            keyProducerRequest = null;
            quiescencePurpose = QuiescencePurpose.None;
            transferredReporter = null;
            ResetContextCache();
            ResetInputGeometry();
            failure?.Throw();
        }

        private static bool IsCurrent(UniTextEditable target, int token)
            => generation == token && ReferenceEquals(editor, target) && target.IsActive;

        internal static void RequestKeyProducer(Action<NativeInputReporter> completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            if (keyProducerRequest != null && keyProducerRequest != completion)
                throw new InvalidOperationException("Another key input producer request is pending.");

            if (ReferenceEquals(editor, null))
            {
                var source = transferredReporter;
                transferredReporter = null;
                completion(source);
                return;
            }

            keyProducerRequest = completion;
            try { editor.Deactivate(); }
            catch
            {
                if (keyProducerRequest == completion) keyProducerRequest = null;
                throw;
            }
        }

        internal static void CancelKeyProducerRequest(Action<NativeInputReporter> completion)
        {
            if (keyProducerRequest == completion) keyProducerRequest = null;
        }

        private static NativeFieldOverlayBehavior GetPresentation(UniTextEditable target)
        {
            if (!SupportsNativeFieldPresentation()) return null;
            try
            {
                target.GetBehaviors(fieldPresentations);
                if (fieldPresentations.Count == 0) return null;
                if (fieldPresentations.Count > 1)
                    throw new InvalidOperationException(
                        $"'{target.name}' has more than one native field presenter.");
                return fieldPresentations[0];
            }
            finally
            {
                fieldPresentations.Clear();
            }
        }

        private static string GetPlaceholderText(UniTextEditable target)
        {
            try
            {
                target.GetBehaviors(placeholders);
                for (var i = 0; i < placeholders.Count; i++)
                {
                    var text = placeholders[i].PlaceholderText;
                    if (!string.IsNullOrEmpty(text)) return text;
                }
                return null;
            }
            finally
            {
                placeholders.Clear();
            }
        }

        private static bool SupportsNativeFieldPresentation()
            => Application.platform == RuntimePlatform.Android ||
               Application.platform == RuntimePlatform.IPhonePlayer ||
               Application.platform == RuntimePlatform.WebGLPlayer;

        internal static int AllocateInputSessionId()
        {
            if (nextInputSessionId == int.MaxValue)
                throw new InvalidOperationException("The native input session identifier space is exhausted.");
            return ++nextInputSessionId;
        }

        internal static bool HandleProducerFault(NativeInputReporter source, Exception error)
        {
            if (!ReferenceEquals(reporter, source) &&
                !ReferenceEquals(transferredReporter, source)) return false;
            var target = editor;
            Debug.LogException(error, target);
            if (!ReferenceEquals(target, null))
            {
                CleanupFailedSession(target);
                return true;
            }

            if (ReferenceEquals(transferredReporter, source)) transferredReporter = null;
            ExceptionDispatchInfo failure = null;
            CaptureCleanup(() => AbortProducer(source), null, ref failure);
            CaptureCleanup(() => UniTextNativeInput.SetImeEnabled(false), null, ref failure);
            if (failure != null) Debug.LogException(failure.SourceException);
            return true;
        }

        private enum QuiescencePurpose
        {
            None,
            Release,
            Resume,
        }

        private enum InputProducerState
        {
            Closed,
            Waiting,
            Opening,
            Open,
            Quiescing,
            Quiesced,
        }

        private static void OnKeyDown(NativeKeyCode key, NativeModifiers modifiers)
        {
            if (pendingTransparentAction != null)
            {
                pendingTransparentIntents.Enqueue(PendingTransparentIntent.KeyDown(key, modifiers));
                return;
            }
            DispatchAction(static (target, state) =>
                target.HandleKeyDown(state.key, state.modifiers), (key, modifiers));
        }

        private static void OnTextInput(string text)
            => DispatchTextInput(text, 0, 0, false);

        private static void OnTextReplacement(int charStart, int charLength, string text)
            => DispatchTextInput(text, charStart, charLength, true);

        private static void DispatchTextInput(string text, int charStart, int charLength,
            bool hasReplacement)
        {
            if (pendingTransparentAction != null)
            {
                pendingTransparentIntents.Enqueue(hasReplacement
                    ? PendingTransparentIntent.TextReplacement(charStart, charLength, text)
                    : PendingTransparentIntent.TextInput(text));
                return;
            }
            var target = editor;
            if (target == null || compositionInvalidated) return;
            var source = reporter;
            var previousDispatchEditor = inputDispatchEditor;
            inputDispatchEditor = target;
            bool exact;
            Exception failure = null;
            try
            {
                exact = hasReplacement
                    ? target.HandleTextInput(text, charStart, charLength)
                    : target.HandleTextInput(text);
            }
            catch (Exception error) { exact = false; failure = error; }
            finally { inputDispatchEditor = previousDispatchEditor; }
            if (failure != null)
            {
                FailTransparentInput(target, source);
                ExceptionDispatchInfo.Capture(failure).Throw();
                return;
            }
            if (!ReferenceEquals(editor, target)) return;
            if (!exact) RecoverTransparentInput(target);
            else platformMutationTextStamp = target.TextStorageStamp;
        }

        private static void OnDeleteBackward()
        {
            if (pendingTransparentAction != null)
            {
                pendingTransparentIntents.Enqueue(PendingTransparentIntent.DeleteBackward());
                return;
            }
            Dispatch(static (target, _) => target.HandleDeleteBack(), false);
        }

        private static void OnCompositionChanged(CompositionData data)
        {
            if (pendingTransparentAction != null)
            {
                pendingTransparentIntents.Enqueue(PendingTransparentIntent.CompositionChanged(data));
                return;
            }
            var target = editor;
            if (target == null || compositionInvalidated) return;
            var source = reporter;
            var previousDispatchEditor = inputDispatchEditor;
            inputDispatchEditor = target;
            Exception failure = null;
            try
            {
                target.HandleCompositionChanged(data);
            }
            catch (Exception error) { failure = error; }
            finally { inputDispatchEditor = previousDispatchEditor; }
            if (failure != null)
            {
                FailTransparentInput(target, source);
                ExceptionDispatchInfo.Capture(failure).Throw();
                return;
            }
            if (!ReferenceEquals(editor, target)) return;
            if (target.IsComposing) platformMutationTextStamp = target.TextStorageStamp;
            else RecoverTransparentInput(target);
        }

        private static void OnCompositionEnded()
        {
            if (pendingTransparentAction != null)
            {
                pendingTransparentIntents.Enqueue(PendingTransparentIntent.CompositionEnded());
                return;
            }
            Dispatch(static (target, _) => target.HandleCompositionEnded(), false);
        }

        private static void OnSelectionChanged(int start, int end)
        {
            if (pendingTransparentAction != null)
            {
                pendingTransparentIntents.Enqueue(PendingTransparentIntent.SelectionChanged(start, end));
                return;
            }
            Dispatch(static (target, state) => target.HandleSelectionInput(state.start, state.end),
                (start, end));
        }

        private static void OnEditorAction(string action)
        {
            if (pendingTransparentAction != null)
            {
                pendingTransparentIntents.Enqueue(PendingTransparentIntent.EditorAction(action));
                return;
            }
            DispatchAction(static (target, value) => ExecuteAction(target, value, NativeModifiers.None),
                action);
        }

        private static void DispatchAction<T>(Func<UniTextEditable, T, Task> action, T state)
        {
            var target = editor;
            if (target == null || compositionInvalidated) return;
            var source = reporter;
            var previousDispatchEditor = inputDispatchEditor;
            inputDispatchEditor = target;
            Exception failure = null;
            try
            {
                var operation = action(target, state) ??
                                throw new InvalidOperationException(
                                    "An editor action returned no completion task.");
                if (operation.IsCompleted)
                {
                    operation.GetAwaiter().GetResult();
                    if (ReferenceEquals(editor, target) && ReferenceEquals(reporter, source))
                        platformMutationTextStamp = target.TextStorageStamp;
                    return;
                }
                if (!ReferenceEquals(editor, target) || !ReferenceEquals(reporter, source))
                {
                    operation.GetAwaiter().OnCompleted(
                        () => CompleteTransparentAction(operation, target, source));
                    return;
                }

                pendingTransparentAction = operation;
                pendingTransparentActionEditor = target;
                pendingTransparentActionReporter = source;
                try
                {
                    if (producerState == InputProducerState.Open)
                        BeginQuiescence(QuiescencePurpose.Resume,
                            NativeCompositionDisposition.Preserve);
                    else if (producerState != InputProducerState.Quiescing &&
                             producerState != InputProducerState.Quiesced)
                        throw new InvalidOperationException(
                            "The asynchronous editor action has no input barrier.");
                }
                catch
                {
                    if (ReferenceEquals(pendingTransparentAction, operation) &&
                        ReferenceEquals(pendingTransparentActionEditor, target) &&
                        ReferenceEquals(pendingTransparentActionReporter, source))
                    {
                        pendingTransparentAction = null;
                        pendingTransparentActionEditor = null;
                        pendingTransparentActionReporter = null;
                    }
                    operation.GetAwaiter().OnCompleted(
                        () => CompleteTransparentAction(operation, target, source));
                    throw;
                }
                operation.GetAwaiter().OnCompleted(
                    () => CompleteTransparentAction(operation, target, source));
            }
            catch (Exception error) { failure = error; }
            finally { inputDispatchEditor = previousDispatchEditor; }
            if (failure != null)
            {
                FailTransparentInput(target, source);
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private static void CompleteTransparentAction(Task operation, UniTextEditable target,
            NativeInputReporter source)
        {
            Exception failure = null;
            try { operation.GetAwaiter().GetResult(); }
            catch (Exception error)
            {
                failure = error;
                Debug.LogException(error, target);
            }

            if (!ReferenceEquals(pendingTransparentAction, operation) ||
                !ReferenceEquals(pendingTransparentActionEditor, target) ||
                !ReferenceEquals(pendingTransparentActionReporter, source))
                return;
            pendingTransparentAction = null;
            pendingTransparentActionEditor = null;
            pendingTransparentActionReporter = null;
            if (!ReferenceEquals(editor, target) || !ReferenceEquals(reporter, source)) return;
            if (failure != null)
            {
                CleanupFailedSession(target);
                return;
            }

            platformMutationTextStamp = target.TextStorageStamp;
            try
            {
                DrainTransparentIntents(target, source);
            }
            catch (Exception error)
            {
                Debug.LogException(error, target);
                pendingTransparentIntents.Clear();
                if (ReferenceEquals(editor, target) && ReferenceEquals(reporter, source))
                    CleanupFailedSession(target);
                return;
            }
            if (pendingTransparentAction != null) return;
            if (!ReferenceEquals(editor, target) || !ReferenceEquals(reporter, source)) return;
            if (producerState != InputProducerState.Quiesced) return;
            try { FinishInputQuiescence(source); }
            catch (Exception error)
            {
                Debug.LogException(error, target);
                if (ReferenceEquals(editor, target) && ReferenceEquals(reporter, source))
                    CleanupFailedSession(target);
            }
        }

        private static void Dispatch<T>(Action<UniTextEditable, T> action, T state)
        {
            var target = editor;
            if (target == null || compositionInvalidated) return;
            var source = reporter;
            var previousDispatchEditor = inputDispatchEditor;
            inputDispatchEditor = target;
            Exception failure = null;
            try
            {
                action(target, state);
            }
            catch (Exception error) { failure = error; }
            finally { inputDispatchEditor = previousDispatchEditor; }
            if (failure != null)
            {
                FailTransparentInput(target, source);
                ExceptionDispatchInfo.Capture(failure).Throw();
                return;
            }
            if (!ReferenceEquals(editor, target)) return;
            platformMutationTextStamp = target.TextStorageStamp;
        }

        private static INativeFieldIntentSink BeginFieldIntent(int sessionId)
        {
            var current = replica;
            if (current == null || current.SessionId != sessionId || !current.IsActive)
                throw new InvalidOperationException("No native field replica accepts the callback.");
            fieldIntentDepth++;
            return current;
        }

        private static void EndFieldIntent() => fieldIntentDepth--;

        private static void FaultFieldIntent(INativeFieldIntentSink sink, int sessionId,
            int nativeRevision)
        {
            try { sink.Fault(sessionId, nativeRevision); }
            catch (Exception cleanupError) { Debug.LogException(cleanupError); }
        }

        private static void DrainTransparentIntents(UniTextEditable target,
            NativeInputReporter source)
        {
            while (pendingTransparentAction == null && pendingTransparentIntents.Count != 0 &&
                   ReferenceEquals(editor, target) && ReferenceEquals(reporter, source))
            {
                var intent = pendingTransparentIntents.Dequeue();
                intent.Dispatch();
            }
        }

        private static void RecoverTransparentInput(UniTextEditable target)
        {
            if (!ReferenceEquals(editor, target)) return;
            platformMutationTextStamp = default;
            forceContextSync = true;
        }

        private static void FailTransparentInput(UniTextEditable target,
            NativeInputReporter source)
        {
            try
            {
                if (ReferenceEquals(editor, target) && ReferenceEquals(reporter, source))
                    CleanupFailedSession(target);
                else
                    target.Deactivate();
            }
            catch (Exception cleanupError)
            {
                Debug.LogException(cleanupError, target);
            }
        }

        private enum PendingTransparentIntentKind
        {
            KeyDown,
            TextInput,
            TextReplacement,
            DeleteBackward,
            EditorAction,
            CompositionChanged,
            CompositionEnded,
            SelectionChanged,
        }

        private readonly struct PendingTransparentIntent
        {
            private readonly PendingTransparentIntentKind kind;
            private readonly NativeKeyCode key;
            private readonly NativeModifiers modifiers;
            private readonly string text;
            private readonly CompositionClause[] clauses;
            private readonly int first;
            private readonly int second;

            private PendingTransparentIntent(PendingTransparentIntentKind kind,
                NativeKeyCode key = default, NativeModifiers modifiers = default,
                string text = null, CompositionClause[] clauses = null,
                int first = 0, int second = 0)
            {
                this.kind = kind;
                this.key = key;
                this.modifiers = modifiers;
                this.text = text;
                this.clauses = clauses;
                this.first = first;
                this.second = second;
            }

            internal static PendingTransparentIntent KeyDown(NativeKeyCode key,
                NativeModifiers modifiers)
                => new(PendingTransparentIntentKind.KeyDown, key, modifiers);

            internal static PendingTransparentIntent TextInput(string text)
                => new(PendingTransparentIntentKind.TextInput, text: text);

            internal static PendingTransparentIntent TextReplacement(int start, int length,
                string text)
                => new(PendingTransparentIntentKind.TextReplacement, text: text,
                    first: start, second: length);

            internal static PendingTransparentIntent DeleteBackward()
                => new(PendingTransparentIntentKind.DeleteBackward);

            internal static PendingTransparentIntent EditorAction(string action)
                => new(PendingTransparentIntentKind.EditorAction, text: action);

            internal static PendingTransparentIntent CompositionChanged(CompositionData data)
                => new(PendingTransparentIntentKind.CompositionChanged,
                    text: data.text.ToString(), clauses: data.clauses.ToArray(),
                    first: data.cursorPosition);

            internal static PendingTransparentIntent CompositionEnded()
                => new(PendingTransparentIntentKind.CompositionEnded);

            internal static PendingTransparentIntent SelectionChanged(int start, int end)
                => new(PendingTransparentIntentKind.SelectionChanged, first: start, second: end);

            internal void Dispatch()
            {
                switch (kind)
                {
                    case PendingTransparentIntentKind.KeyDown:
                        OnKeyDown(key, modifiers);
                        break;
                    case PendingTransparentIntentKind.TextInput:
                        OnTextInput(text);
                        break;
                    case PendingTransparentIntentKind.TextReplacement:
                        OnTextReplacement(first, second, text);
                        break;
                    case PendingTransparentIntentKind.DeleteBackward:
                        OnDeleteBackward();
                        break;
                    case PendingTransparentIntentKind.EditorAction:
                        OnEditorAction(text);
                        break;
                    case PendingTransparentIntentKind.CompositionChanged:
                        var data = new CompositionData
                        {
                            text = text.AsSpan(),
                            clauses = clauses,
                            cursorPosition = first,
                        };
                        OnCompositionChanged(data);
                        break;
                    case PendingTransparentIntentKind.CompositionEnded:
                        OnCompositionEnded();
                        break;
                    case PendingTransparentIntentKind.SelectionChanged:
                        OnSelectionChanged(first, second);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        internal static Task ExecuteAction(UniTextEditable target, string action,
            NativeModifiers modifiers)
        {
            switch (action)
            {
                case NativeEditorAction.Submit: return target.ExecuteWithCompletion(EditAction.Submit);
                case NativeEditorAction.Return:
                    return target.HandleKeyDown(NativeKeyCode.Return, modifiers);
                case NativeEditorAction.Done:
                    target.Defocus();
                    return Task.CompletedTask;
                case NativeEditorAction.Cancel: return target.ExecuteWithCompletion(EditAction.Cancel);
                case NativeEditorAction.Copy: return target.ExecuteWithCompletion(EditAction.Copy);
                case NativeEditorAction.Cut: return target.ExecuteWithCompletion(EditAction.Cut);
                case NativeEditorAction.Paste: return target.ExecuteWithCompletion(EditAction.Paste);
                case NativeEditorAction.PastePlain: return target.ExecuteWithCompletion(EditAction.PasteAsPlain);
                case NativeEditorAction.Undo: return target.ExecuteWithCompletion(EditAction.Undo);
                case NativeEditorAction.Redo: return target.ExecuteWithCompletion(EditAction.Redo);
                case NativeEditorAction.Next:
                    MoveFocus(target, false);
                    return Task.CompletedTask;
                case NativeEditorAction.Previous:
                    MoveFocus(target, true);
                    return Task.CompletedTask;
                default: throw new ArgumentOutOfRangeException(nameof(action), action,
                    "The native field reported an unsupported editor action.");
            }
        }

        internal static void HandleReplicaFault(UniTextEditable target, int sessionId)
        {
            if (ReferenceEquals(editor, target) && replica?.SessionId == sessionId)
            {
                CleanupFailedSession(target);
                return;
            }
            target.Deactivate();
        }

        private static void MoveFocus(UniTextEditable target, bool previous)
        {
            var focusable = target.GetComponent<Selectable>();
            if (focusable == null) return;
            var next = previous ? focusable.FindSelectableOnUp() : focusable.FindSelectableOnDown();
            next?.Select();
        }

        private static void OnCompositionStateChanged(bool composing)
        {
            var target = editor;
            if (target == null) return;
            if (composing)
            {
                compositionInvalidated = false;
                return;
            }
            if (ReferenceEquals(inputDispatchEditor, target) || fieldIntentDepth != 0) return;
            replica?.InvalidateNativeComposition();
            compositionInvalidated = true;
            forceContextSync = true;
            if (producerState == InputProducerState.Open)
            {
                BeginQuiescence(QuiescencePurpose.Resume,
                    NativeCompositionDisposition.Cancel);
                return;
            }
            if (producerState == InputProducerState.Closed)
                compositionInvalidated = false;
        }

        /// <summary>
        /// A visible replica is docked to the software keyboard and cannot outlive it: without the
        /// keyboard it is an OS view over the scene that accepts no typing and offers no dismissal.
        /// The keyboard also disappears while the platform reacts to a rotation or a configuration
        /// change and returns on its own, so loss counts only once it outlasts
        /// <see cref="KeyboardLossGrace"/>.
        /// </summary>
        private static void OnKeyboardVisibilityChanged(bool visible)
        {
            if (visible)
            {
                CatZones.input.MeowFormat("[Input] Keyboard visible; pending self-inflicted hides={0}",
                    selfInflictedKeyboardHides);
                replicaKeyboardLost = false;
                selfInflictedKeyboardHides = 0;
                return;
            }
            if (replica == null || !keyboardRequested)
            {
                CatZones.input.MeowFormat("[Input] Keyboard hidden; no docked replica to lose (replica={0}, requested={1})",
                    replica != null, keyboardRequested);
                return;
            }

            if (selfInflictedKeyboardHides > 0)
            {
                selfInflictedKeyboardHides--;
                CatZones.input.MeowFormat(
                    "[Input] Keyboard hidden by our own replica replacement; {0} left to absorb",
                    selfInflictedKeyboardHides);
                return;
            }
            CatZones.input.Meow("[Input] Keyboard hidden under a docked replica; loss armed");
            replicaKeyboardLost = true;
            replicaKeyboardLostAt = Time.unscaledTime;
        }

        private static void InvalidateConfiguration()
        {
            configurationDirty = true;
            InvalidateInputGeometry();
        }

        private static void InvalidateInputGeometry() => inputGeometryDirty = true;

        private static void InvalidateInputGeometryProducer()
        {
            inputGeometryDirty = true;
            inputGeometryOutputValid = false;
        }

        private static void Tick(float _)
        {
            var target = editor;
            if (target == null) return;
            try { TickCore(target); }
            catch
            {
                CleanupFailedSession(target);
                throw;
            }
        }

        private static void TickCore(UniTextEditable target)
        {
            UniTextNativeInput.FlushPendingInput();
            if (!ReferenceEquals(editor, target)) return;
            if (producerState == InputProducerState.Waiting ||
                producerState == InputProducerState.Opening ||
                producerState == InputProducerState.Quiescing ||
                producerState == InputProducerState.Quiesced)
                return;

            if (replicaKeyboardLost &&
                Time.unscaledTime - replicaKeyboardLostAt >= KeyboardLossGrace)
            {
                replicaKeyboardLost = false;
                if (replica != null && keyboardRequested && !UniTextNativeInput.IsKeyboardVisible)
                {
                    CatZones.input.MeowFormat(
                        "[Input] Keyboard stayed gone for {0}s under a docked replica; releasing focus",
                        KeyboardLossGrace);
                    target.Defocus();
                    return;
                }
                CatZones.input.Meow("[Input] Keyboard came back before the grace expired; focus kept");
            }

            replica?.SyncExternal();
            if (!ReferenceEquals(editor, target) || producerState != InputProducerState.Open)
                return;
            if (configurationDirty)
            {
                RefreshConfiguration(target);
                if (!ReferenceEquals(editor, target) ||
                    producerState != InputProducerState.Open) return;
            }
            if (replica == null) PushTextContext(target);
            PushGeometry(target, replica != null);
        }

        private static void RefreshConfiguration(UniTextEditable target)
        {
            try { RefreshConfigurationCore(target); }
            catch
            {
                CleanupFailedSession(target);
                throw;
            }
        }

        private static void RefreshConfigurationCore(UniTextEditable target)
        {
            if (producerState == InputProducerState.Open)
            {
                if (AppliedConfigurationStillCurrent(target))
                {
                    CatZones.input.Meow(
                        "[Input] Configuration invalidation matched the applied configuration; producer kept");
                    configurationDirty = false;
                    return;
                }
                BeginQuiescence(QuiescencePurpose.Resume,
                    NativeCompositionDisposition.Preserve);
                return;
            }
            if (producerState != InputProducerState.Quiesced &&
                producerState != InputProducerState.Closed)
                return;

            var token = generation;
            configurationDirty = false;
            var request = default(KeyboardRequest);
            target.ResolveKeyboardRequest(ref request);
            request.config?.Validate();
            if (!IsCurrent(target, token)) return;
            var fieldPresentation = GetPresentation(target);
            if (OpenProducer(target, request.config, fieldPresentation, keyboardRequested) &&
                IsCurrent(target, token))
            {
                forceContextSync = true;
                PushState(target);
            }
        }

        /// <summary>
        /// Whether the open producer's resolved configuration still matches what a reopen would
        /// apply, making the pending invalidation a no-op. A producer replacement restarts the
        /// software keyboard, so an invalidation that resolves to an unchanged configuration must
        /// never reach one.
        /// </summary>
        private static bool AppliedConfigurationStillCurrent(UniTextEditable target)
        {
            var request = default(KeyboardRequest);
            target.ResolveKeyboardRequest(ref request);
            var fieldPresentation = GetPresentation(target);
            var visible = keyboardRequested && fieldPresentation != null;
            var shape = NativeFieldShape.For(target, request.config);
            var presentation = visible
                ? fieldPresentation.Capture(GetPlaceholderText(target))
                : default;
            return appliedConfiguration.Matches(ProducerConfiguration.Capture(
                target, request.config, fieldPresentation, keyboardRequested,
                in shape, in presentation));
        }

        /// <summary>
        /// The resolved inputs one producer generation applied, compared against a fresh
        /// resolution to decide whether a configuration invalidation changed anything.
        /// </summary>
        private struct ProducerConfiguration
        {
            private bool captured;
            private NativeKeyboardConfig config;
            private int configVersion;
            private NativeFieldOverlayBehavior presenter;
            private NativeFieldShape shape;
            private NativeFieldPresentationState presentation;
            private bool secureInput;
            private bool readOnly;
            private bool copyAllowed;
            private bool showKeyboard;

            internal static ProducerConfiguration Capture(UniTextEditable target,
                NativeKeyboardConfig config, NativeFieldOverlayBehavior presenter,
                bool showKeyboard, in NativeFieldShape shape,
                in NativeFieldPresentationState presentation) => new()
            {
                captured = true,
                config = config,
                configVersion = config?.Version ?? 0,
                presenter = presenter,
                shape = shape,
                presentation = presentation,
                secureInput = target.SecureInput,
                readOnly = target.ReadOnly,
                copyAllowed = target.IsCopyAllowed(),
                showKeyboard = showKeyboard,
            };

            internal bool Matches(in ProducerConfiguration current)
                => captured && current.captured &&
                   ReferenceEquals(config, current.config) &&
                   configVersion == current.configVersion &&
                   ReferenceEquals(presenter, current.presenter) &&
                   shape.Equals(current.shape) &&
                   presentation.Equals(current.presentation) &&
                   secureInput == current.secureInput &&
                   readOnly == current.readOnly &&
                   copyAllowed == current.copyAllowed &&
                   showKeyboard == current.showKeyboard;
        }

        private static void PushState(UniTextEditable target)
        {
            if (replica == null) PushTextContext(target);
            InvalidateInputGeometry();
            PushGeometry(target, replica != null);
        }

        private static void PushGeometry(UniTextEditable target, bool visibleField)
        {
            var textTransform = target.TextComponent.rectTransform;
            var fieldTransform = target.GetFieldRectTransform();
            var camera = CanvasUtil.GetCanvasCamera(target.TextComponent.canvas);
            var textMatrix = textTransform.localToWorldMatrix;
            var fieldMatrix = fieldTransform.localToWorldMatrix;
            var textRect = textTransform.rect;
            var fieldLocalRect = fieldTransform.rect;
            var cameraView = camera != null ? camera.worldToCameraMatrix : default;
            var cameraProjection = camera != null ? camera.projectionMatrix : default;
            var cameraPixelRect = camera != null ? camera.pixelRect : default;
            var keyboardArea = visibleField ? UniTextNativeInput.KeyboardArea : default;
            var screenWidth = Screen.width;
            var screenHeight = Screen.height;
            UniTextNativeInput.GetImeWindowProjection(out var imeOffsetX, out var imeScaleX,
                out var imeOffsetY, out var imeScaleY);

            var imeProjectionChanged = !inputGeometryStampValid ||
                                       inputGeometryImeOffsetX != imeOffsetX ||
                                       inputGeometryImeScaleX != imeScaleX ||
                                       inputGeometryImeOffsetY != imeOffsetY ||
                                       inputGeometryImeScaleY != imeScaleY;
            if (!inputGeometryStampValid || inputGeometryVisible != visibleField ||
                !ReferenceEquals(inputGeometryTextTransform, textTransform) ||
                !ReferenceEquals(inputGeometryFieldTransform, fieldTransform) ||
                !ReferenceEquals(inputGeometryCamera, camera) ||
                inputGeometryTextMatrix != textMatrix ||
                inputGeometryFieldMatrix != fieldMatrix ||
                inputGeometryCameraView != cameraView ||
                inputGeometryCameraProjection != cameraProjection ||
                inputGeometryTextRect != textRect ||
                inputGeometryFieldLocalRect != fieldLocalRect ||
                inputGeometryCameraPixelRect != cameraPixelRect ||
                inputGeometryKeyboardArea != keyboardArea ||
                inputGeometryScreenWidth != screenWidth ||
                inputGeometryScreenHeight != screenHeight || imeProjectionChanged)
                inputGeometryDirty = true;

            inputGeometryStampValid = true;
            inputGeometryVisible = visibleField;
            inputGeometryTextTransform = textTransform;
            inputGeometryFieldTransform = fieldTransform;
            inputGeometryCamera = camera;
            inputGeometryTextMatrix = textMatrix;
            inputGeometryFieldMatrix = fieldMatrix;
            inputGeometryCameraView = cameraView;
            inputGeometryCameraProjection = cameraProjection;
            inputGeometryTextRect = textRect;
            inputGeometryFieldLocalRect = fieldLocalRect;
            inputGeometryCameraPixelRect = cameraPixelRect;
            inputGeometryKeyboardArea = keyboardArea;
            inputGeometryScreenWidth = screenWidth;
            inputGeometryScreenHeight = screenHeight;
            inputGeometryImeOffsetX = imeOffsetX;
            inputGeometryImeScaleX = imeScaleX;
            inputGeometryImeOffsetY = imeOffsetY;
            inputGeometryImeScaleY = imeScaleY;
            if (!inputGeometryDirty) return;

            target.GetInputCaretScreenGeometry(out var position, out var lineHeight);
            var source = target.GetFieldScreenRect();
            var fieldRect = source;
            if (visibleField)
            {
                var width = keyboardArea.width > 0f ? keyboardArea.width : screenWidth;
                var x = keyboardArea.width > 0f ? keyboardArea.xMin : 0f;
                var y = keyboardArea.height > 0f ? keyboardArea.yMax : 0f;
                fieldRect = new Rect(x, y, width, source.height);
            }

            var caretChanged = !inputGeometryOutputValid ||
                               inputGeometryCaretPosition != position;
            var cursorChanged = caretChanged || !inputGeometryOutputValid ||
                                inputGeometryLineHeight != lineHeight;
            var fieldChanged = !inputGeometryOutputValid ||
                               inputGeometryOutputVisible != visibleField ||
                               inputGeometryFieldRect != fieldRect;
            if (caretChanged || imeProjectionChanged)
                UniTextNativeInput.SetImeCursorPosition(position);
            if (cursorChanged)
                UniTextNativeInput.SetCursorScreenPos(position, lineHeight);
            if (fieldChanged)
                UniTextNativeInput.SetInputFieldRect(fieldRect);

            inputGeometryCaretPosition = position;
            inputGeometryLineHeight = lineHeight;
            inputGeometryFieldRect = fieldRect;
            inputGeometryOutputVisible = visibleField;
            inputGeometryOutputValid = true;
            inputGeometryDirty = false;
        }

        private static void ResetInputGeometry()
        {
            inputGeometryDirty = true;
            inputGeometryStampValid = false;
            inputGeometryOutputValid = false;
            inputGeometryTextTransform = null;
            inputGeometryFieldTransform = null;
            inputGeometryCamera = null;
        }

        private static void PushTextContext(UniTextEditable target)
        {
            if (!UniTextNativeInput.WantsTextContext || target.IsComposing) return;
            target.GetCharSelection(out var selectionStart, out var selectionLength);
            var selectionEnd = selectionStart + selectionLength;
            var textStamp = target.TextStorageStamp;
            if (!forceContextSync && textStamp == contextTextStamp &&
                selectionStart == contextSelectionStart && selectionEnd == contextSelectionEnd)
                return;

            var forceRestart = forceContextSync ||
                               (textStamp != contextTextStamp &&
                                textStamp != platformMutationTextStamp);
            var total = target.CharCount;
            SelectContextWindow(target, total, selectionStart, selectionEnd, forceRestart,
                out var windowStart, out var windowEnd);
            if (contextWindow == null || contextTextStamp != textStamp ||
                contextWindowStart != windowStart || contextWindowEnd != windowEnd)
            {
                contextWindow = CaptureRange(target, windowStart, windowEnd - windowStart);
                contextWindowStart = windowStart;
                contextWindowEnd = windowEnd;
            }

            UniTextNativeInput.PushTextContext(contextWindow, windowStart,
                selectionStart, selectionEnd, forceRestart);
            contextTextStamp = textStamp;
            contextSelectionStart = selectionStart;
            contextSelectionEnd = selectionEnd;
            forceContextSync = false;
        }

        /// <summary>
        /// Selects the UTF-16 window projected to the IME, reusing the previous window while the
        /// selection sits comfortably inside it — a stable window keeps each projection an
        /// in-place reconciliation for the platform instead of an IME context change.
        /// </summary>
        private static void SelectContextWindow(UniTextEditable target, int total,
            int selectionStart, int selectionEnd, bool reanchor, out int start, out int end)
        {
            if (total <= WholeContextLimit)
            {
                start = 0;
                end = total;
                return;
            }

            if (!reanchor && contextWindowStart >= 0)
            {
                start = Math.Min(contextWindowStart, selectionStart);
                end = Math.Max(Math.Min(contextWindowEnd, total), selectionEnd);
                const int margin = ContextWindowLength / 4;
                if (end - start <= ContextWindowLength * 2 &&
                    (start == 0 || selectionStart - start >= margin) &&
                    (end == total || end - selectionEnd >= margin))
                    return;
            }

            start = Mathf.Clamp(selectionStart + (selectionEnd - selectionStart) / 2 -
                                ContextWindowLength / 2,
                0, total - ContextWindowLength);
            end = start + ContextWindowLength;
            Span<char> pair = stackalloc char[2];
            if (start > 0 && target.CopyCharRangeTo(start - 1, 2, pair) == 2 &&
                char.IsHighSurrogate(pair[0]) && char.IsLowSurrogate(pair[1]))
                start--;
            if (end < total && target.CopyCharRangeTo(end - 1, 2, pair) == 2 &&
                char.IsHighSurrogate(pair[0]) && char.IsLowSurrogate(pair[1]))
                end++;
        }

        internal static string CaptureText(UniTextEditable target)
            => CaptureRange(target, 0, target.CharCount);

        internal static string CaptureRange(UniTextEditable target, int start, int length)
        {
            if (length <= 0) return string.Empty;
            return string.Create(length, (target, start), static (span, state) =>
                state.target.CopyCharRangeTo(state.start, span.Length, span));
        }

        private static void ResetContextCache()
        {
            appliedConfiguration = default;
            forceContextSync = false;
            platformMutationTextStamp = default;
            contextTextStamp = default;
            contextSelectionStart = -1;
            contextSelectionEnd = -1;
            contextWindowStart = -1;
            contextWindowEnd = -1;
            contextWindow = null;
            configurationDirty = false;
            compositionInvalidated = false;
            keyboardRequested = false;
            replicaKeyboardLost = false;
            selfInflictedKeyboardHides = 0;
        }

        private sealed class EditableInputContext : ITextInputContext, INativeInputRecipient
        {
            private readonly UniTextEditable target;

            internal EditableInputContext(UniTextEditable target) => this.target = target;

            internal bool AcceptsEditingReports { get; set; }

            public int CharCount => target.CharCount;
            public int CopyCharRange(int charStart, int charLength, Span<char> destination)
                => target.CopyCharRangeTo(charStart, charLength, destination);
            public (int start, int length) GetCharSelection()
            {
                target.GetCharSelection(out var start, out var length);
                return (start, length);
            }
            public void SelectCharRange(int charStart, int charLength)
                => target.SelectCharRange(charStart, charLength);
            public bool TryGetCharRangeRect(int charStart, int charLength, out Rect rect)
            {
                if (target.TryGetFirstScreenRectForCharRange(charStart, charLength,
                        out var x, out var y, out var width, out var height))
                {
                    rect = new Rect(x, y, width, height);
                    return true;
                }
                rect = default;
                return false;
            }
            public int HitTestChar(Vector2 screenPos)
                => target.ClosestCharIndexAtScreenPoint(screenPos.x, screenPos.y);
            public int TokenizerQuery(int charIndex, int granularity, int direction, int action)
                => target.TokenizerQuery(charIndex, granularity, direction, action);
            public int WritingDirection(int charIndex)
                => target.WritingDirectionAtCharIndex(charIndex);
            public string InitialText => CaptureText(target);

            void INativeInputRecipient.ReceiveKeyDown(NativeKeyCode key, NativeModifiers modifiers)
            {
                if (AcceptsEditingReports) OnKeyDown(key, modifiers);
            }
            void INativeInputRecipient.ReceiveTextInput(string text)
            {
                if (AcceptsEditingReports) OnTextInput(text);
            }
            void INativeInputRecipient.ReceiveTextReplacement(int charStart, int charLength,
                string text)
            {
                if (AcceptsEditingReports) OnTextReplacement(charStart, charLength, text);
            }
            void INativeInputRecipient.ReceiveDeleteBackward()
            {
                if (AcceptsEditingReports) OnDeleteBackward();
            }
            void INativeInputRecipient.ReceiveEditorAction(string action)
            {
                if (AcceptsEditingReports) OnEditorAction(action);
            }
            void INativeInputRecipient.ReceiveCompositionChanged(CompositionData data)
            {
                if (AcceptsEditingReports) OnCompositionChanged(data);
            }
            void INativeInputRecipient.ReceiveCompositionEnded()
            {
                if (AcceptsEditingReports) OnCompositionEnded();
            }
            void INativeInputRecipient.ReceiveSelectionChanged(int charStart, int charEnd)
            {
                if (AcceptsEditingReports) OnSelectionChanged(charStart, charEnd);
            }
            void INativeInputRecipient.ReceiveFieldEdit(in NativeFieldEditIntent intent)
            {
                var sink = BeginFieldIntent(intent.SessionId);
                try { sink.Apply(in intent); }
                catch { FaultFieldIntent(sink, intent.SessionId, intent.NativeRevision); throw; }
                finally { EndFieldIntent(); }
            }
            void INativeInputRecipient.ReceiveFieldComposition(in NativeFieldCompositionIntent intent)
            {
                var sink = BeginFieldIntent(intent.SessionId);
                try { sink.Apply(in intent); }
                catch { FaultFieldIntent(sink, intent.SessionId, intent.NativeRevision); throw; }
                finally { EndFieldIntent(); }
            }
            void INativeInputRecipient.ReceiveFieldSelection(in NativeFieldSelectionIntent intent)
            {
                var sink = BeginFieldIntent(intent.SessionId);
                try { sink.Apply(in intent); }
                catch { FaultFieldIntent(sink, intent.SessionId, intent.NativeRevision); throw; }
                finally { EndFieldIntent(); }
            }
            void INativeInputRecipient.ReceiveFieldAction(in NativeFieldActionIntent intent)
            {
                var sink = BeginFieldIntent(intent.SessionId);
                try { sink.Apply(in intent); }
                catch { FaultFieldIntent(sink, intent.SessionId, intent.NativeRevision); throw; }
                finally { EndFieldIntent(); }
            }
            void INativeInputRecipient.ReceiveFieldQuiesced(in NativeInputQuiescedIntent intent)
            {
                var sink = BeginFieldIntent(intent.SessionId);
                try { sink.Apply(in intent); }
                catch { FaultFieldIntent(sink, intent.SessionId, intent.NativeRevision); throw; }
                finally { EndFieldIntent(); }
            }
            void INativeInputRecipient.ReceiveFault(NativeInputReporter source, Exception error)
                => HandleProducerFault(source, error);
        }
    }
}
