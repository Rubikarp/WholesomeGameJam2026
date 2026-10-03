using System;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace LightSide
{
    internal static class NativeKeyInputSession
    {
        private static readonly KeyInputContext inputContext = new();
        private static readonly Action<NativeInputReporter> producerReady = AcceptProducer;
        private static GameObject owner;
        private static Action<NativeKeyCode, NativeModifiers> keyDown;
        private static NativeInputReporter reporter;
        private static Action<NativeInputReporter> transferCompletion;
        private static ProducerState state;
        private static int sessionId;
        private static bool waitingForProducer;
        private static bool resetting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            Reset();
            resetting = false;
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

        internal static void Subscribe(GameObject target,
            Action<NativeKeyCode, NativeModifiers> handler)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (resetting) return;
            if (!ReferenceEquals(owner, target))
            {
                if (keyDown != null)
                    throw new InvalidOperationException(
                        "A different GameObject still owns the key input session.");
                var nextSessionId = NativeInputSession.AllocateInputSessionId();
                owner = target;
                sessionId = nextSessionId;
            }

            keyDown -= handler;
            keyDown += handler;
            EnsureProducer();
        }

        internal static void Unsubscribe(GameObject target,
            Action<NativeKeyCode, NativeModifiers> handler)
        {
            if (!ReferenceEquals(owner, target) || handler == null) return;
            keyDown -= handler;
            if (keyDown != null) return;
            if (waitingForProducer)
            {
                waitingForProducer = false;
                NativeInputSession.CancelKeyProducerRequest(producerReady);
                owner = null;
                sessionId = 0;
                return;
            }
            if (state == ProducerState.Open) BeginQuiescence();
            else if (state == ProducerState.Quiesced) FinishQuiescence();
            else if (state == ProducerState.Closed)
            {
                owner = null;
                sessionId = 0;
            }
        }

        internal static void Ensure(GameObject target)
        {
            if (ReferenceEquals(owner, target) && keyDown != null) EnsureProducer();
        }

        internal static void SupersedePendingAcquisition()
        {
            if (!waitingForProducer) return;
            waitingForProducer = false;
            NativeInputSession.CancelKeyProducerRequest(producerReady);
            keyDown = null;
            owner = null;
            sessionId = 0;
        }

        internal static void TransferToEditable(Action<NativeInputReporter> completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            if (transferCompletion != null && transferCompletion != completion)
                throw new InvalidOperationException("Another input producer transfer is pending.");
            keyDown = null;
            owner = null;
            sessionId = 0;
            transferCompletion = completion;
            if (state == ProducerState.Open) BeginQuiescence();
            else if (state == ProducerState.Quiesced) FinishQuiescence();
            else if (state == ProducerState.Closed)
            {
                transferCompletion = null;
                completion(null);
            }
        }

        internal static void CancelTransferToEditable(Action<NativeInputReporter> completion)
        {
            if (transferCompletion != completion) return;
            transferCompletion = null;
            if (keyDown != null) return;
            if (state == ProducerState.Open) BeginQuiescence();
            else if (state == ProducerState.Quiesced) FinishQuiescence();
        }

        private static void EnsureProducer()
        {
            if (state != ProducerState.Closed || waitingForProducer || keyDown == null) return;
            waitingForProducer = true;
            try { NativeInputSession.RequestKeyProducer(producerReady); }
            catch
            {
                waitingForProducer = false;
                throw;
            }
        }

        private static void AcceptProducer(NativeInputReporter source)
        {
            if (!waitingForProducer)
            {
                NativeInputSession.CloseProducer(source);
                return;
            }
            waitingForProducer = false;
            if (keyDown == null)
            {
                NativeInputSession.CloseProducer(source);
                owner = null;
                sessionId = 0;
                return;
            }

            try
            {
                UniTextNativeInput.SetImeEnabled(false);
            }
            catch (Exception error)
            {
                if (source != null && !source.IsClosed)
                    LogCleanup(() => NativeInputSession.CloseProducer(source));
                ExceptionDispatchInfo.Capture(error).Throw();
            }
            Open(source);
        }

        private static void Open(NativeInputReporter inherited)
        {
            var previous = inherited;
            NativeInputReporter candidate = null;
            reporter = previous;
            state = ProducerState.Opening;
            try
            {
                candidate = UniTextNativeInput.CreateReporter(
                    sessionId, inputContext, inputContext, previous);
                reporter = candidate;
                var request = new NativeInputOpenRequest(false, null, false, false);
                UniTextNativeInput.OpenInput(candidate, in request);
                if (!ReferenceEquals(reporter, candidate) || state != ProducerState.Opening)
                    return;
                state = ProducerState.Open;
                if (transferCompletion != null || keyDown == null) BeginQuiescence();
            }
            catch (Exception error)
            {
                var failure = ExceptionDispatchInfo.Capture(error);
                if (candidate != null && !candidate.IsClosed)
                    LogCleanup(() => UniTextNativeInput.AbortInput(candidate));
                if (previous != null && !previous.IsClosed)
                    LogCleanup(() => NativeInputSession.CloseProducer(previous));
                reporter = null;
                state = ProducerState.Closed;
                if (keyDown == null)
                {
                    owner = null;
                    sessionId = 0;
                }
                failure.Throw();
            }
        }

        private static void BeginQuiescence()
        {
            if (state == ProducerState.Quiescing) return;
            if (state != ProducerState.Open)
                throw new InvalidOperationException("The key input producer is not open.");
            var source = reporter;
            state = ProducerState.Quiescing;
            try
            {
                UniTextNativeInput.QuiesceInput(source, NativeCompositionDisposition.Cancel,
                    () => OnQuiesced(source));
            }
            catch (Exception error)
            {
                var failure = ExceptionDispatchInfo.Capture(error);
                if (ReferenceEquals(reporter, source))
                {
                    reporter = null;
                    state = ProducerState.Closed;
                    if (!source.IsClosed)
                        LogCleanup(() => UniTextNativeInput.AbortInput(source));
                }
                failure.Throw();
            }
        }

        private static void OnQuiesced(NativeInputReporter source)
        {
            if (!ReferenceEquals(reporter, source) || state != ProducerState.Quiescing) return;
            state = ProducerState.Quiesced;
            FinishQuiescence();
        }

        private static void FinishQuiescence()
        {
            var source = reporter;
            var transfer = transferCompletion;
            transferCompletion = null;
            reporter = null;
            state = ProducerState.Closed;
            if (transfer != null)
            {
                ExceptionDispatchInfo failure = null;
                try { transfer(source); }
                catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
                if (keyDown != null)
                {
                    try { EnsureProducer(); }
                    catch (Exception error)
                    {
                        if (failure == null) failure = ExceptionDispatchInfo.Capture(error);
                        else Debug.LogException(error);
                    }
                }
                failure?.Throw();
                return;
            }
            if (keyDown != null)
            {
                Open(source);
                return;
            }

            owner = null;
            sessionId = 0;
            NativeInputSession.CloseProducer(source);
        }

        private static void LogCleanup(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private static void HandleFault(NativeInputReporter source, Exception error)
        {
            if (NativeInputSession.HandleProducerFault(source, error)) return;
            Debug.LogException(error, owner);

            var transfer = transferCompletion;
            transferCompletion = null;
            reporter = null;
            state = ProducerState.Closed;
            LogCleanup(() => UniTextNativeInput.AbortInput(source));
            if (transfer != null) LogCleanup(() => transfer(null));
            if (keyDown == null)
            {
                owner = null;
                sessionId = 0;
            }
        }

        private static void Reset()
        {
            resetting = true;
            if (waitingForProducer)
                NativeInputSession.CancelKeyProducerRequest(producerReady);
            waitingForProducer = false;
            transferCompletion = null;
            keyDown = null;
            owner = null;
            sessionId = 0;
            var source = reporter;
            reporter = null;
            state = ProducerState.Closed;
            if (source != null && !source.IsClosed) UniTextNativeInput.AbortInput(source);
        }

        private enum ProducerState
        {
            Closed,
            Opening,
            Open,
            Quiescing,
            Quiesced,
        }

        private sealed class KeyInputContext : ITextInputContext, INativeInputRecipient
        {
            public int CharCount => 0;
            public int CopyCharRange(int charStart, int charLength, Span<char> destination) => 0;
            public (int start, int length) GetCharSelection() => default;
            public void SelectCharRange(int charStart, int charLength) { }
            public bool TryGetCharRangeRect(int charStart, int charLength, out Rect rect)
            {
                rect = default;
                return false;
            }
            public int HitTestChar(Vector2 screenPos) => -1;
            public int TokenizerQuery(int charIndex, int granularity, int direction, int action) => -1;
            public int WritingDirection(int charIndex) => 0;
            public string InitialText => string.Empty;

            void INativeInputRecipient.ReceiveKeyDown(NativeKeyCode key, NativeModifiers modifiers)
                => keyDown?.Invoke(key, modifiers);
            void INativeInputRecipient.ReceiveTextInput(string text) { }
            void INativeInputRecipient.ReceiveTextReplacement(int charStart, int charLength,
                string text) { }
            void INativeInputRecipient.ReceiveDeleteBackward() { }
            void INativeInputRecipient.ReceiveEditorAction(string action) { }
            void INativeInputRecipient.ReceiveCompositionChanged(CompositionData data) { }
            void INativeInputRecipient.ReceiveCompositionEnded() { }
            void INativeInputRecipient.ReceiveSelectionChanged(int charStart, int charEnd) { }
            void INativeInputRecipient.ReceiveFieldEdit(in NativeFieldEditIntent intent)
                => throw NoReplica();
            void INativeInputRecipient.ReceiveFieldComposition(in NativeFieldCompositionIntent intent)
                => throw NoReplica();
            void INativeInputRecipient.ReceiveFieldSelection(in NativeFieldSelectionIntent intent)
                => throw NoReplica();
            void INativeInputRecipient.ReceiveFieldAction(in NativeFieldActionIntent intent)
                => throw NoReplica();
            void INativeInputRecipient.ReceiveFieldQuiesced(in NativeInputQuiescedIntent intent)
                => throw NoReplica();
            void INativeInputRecipient.ReceiveFault(NativeInputReporter source, Exception error)
                => HandleFault(source, error);

            private static InvalidOperationException NoReplica()
                => new("A keyboard-only input session has no native field replica.");
        }
    }
}
