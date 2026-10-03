using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace LightSide
{
    /// <summary>
    /// Visual classification of one clause in an IME composition.
    /// </summary>
    public enum CompositionClauseStyle
    {
        /// <summary>Unconverted input (ATTR_INPUT). Visual: thin underline.</summary>
        Unconverted,
        /// <summary>Active clause being converted (ATTR_TARGET_CONVERTED). Visual: thick underline or highlight.</summary>
        TargetConverted,
        /// <summary>Already converted clause (ATTR_CONVERTED). Visual: thin underline.</summary>
        Converted,
        /// <summary>Selected but not yet converted (ATTR_TARGET_NOTCONVERTED). Visual: thick underline.</summary>
        TargetNotConverted,
        /// <summary>Input error (ATTR_INPUT_ERROR). Visual: squiggly underline.</summary>
        Error,
    }

    /// <summary>
    /// A single clause (segment) within an IME composition string, with its boundaries and style.
    /// </summary>
    public struct CompositionClause
    {
        /// <summary>Start char offset within the composition string (inclusive).</summary>
        public int startOffset;

        /// <summary>End char offset within the composition string (exclusive).</summary>
        public int endOffset;

        /// <summary>Visual style for this clause, derived from the platform's attribute data.</summary>
        public CompositionClauseStyle style;

        /// <summary>Ensures that <paramref name="buffer"/> can hold at least <paramref name="count"/> clauses.</summary>
        public static void EnsureCapacity(ref CompositionClause[] buffer, int count)
        {
            if (buffer.Length >= count) return;
            var size = buffer.Length > 0 ? buffer.Length : 16;
            while (size < count) size *= 2;
            var grown = new CompositionClause[size];
            Array.Copy(buffer, grown, buffer.Length);
            buffer = grown;
        }

        /// <summary>
        /// Writes one unconverted clause spanning the composition and returns one.
        /// </summary>
        public static int FillFallback(ref CompositionClause[] buffer, int textLength)
        {
            EnsureCapacity(ref buffer, 1);
            buffer[0] = new CompositionClause
            {
                startOffset = 0,
                endOffset = textLength,
                style = CompositionClauseStyle.Unconverted,
            };
            return 1;
        }
    }

    /// <summary>
    /// Ephemeral composition data whose spans are valid only for the synchronous report call.
    /// </summary>
    public ref struct CompositionData
    {
        /// <summary>
        /// The current composition string. Span into a reusable internal buffer —
        /// must be consumed or copied within the callback scope.
        /// </summary>
        public ReadOnlySpan<char> text;

        /// <summary>
        /// Clause boundaries and styles. Span into a reusable internal buffer —
        /// must be consumed or copied within the callback scope.
        /// </summary>
        public ReadOnlySpan<CompositionClause> clauses;

        /// <summary>
        /// Cursor position (char offset) within the composition string, or -1 while the
        /// platform temporarily suppresses the insertion point.
        /// </summary>
        public int cursorPosition;
    }

    /// <summary>
    /// Supported identifiers for native editor commands and software-keyboard actions.
    /// </summary>
    public static class NativeEditorAction
    {
        /// <summary>Commits the field's content (Go / Search / Send / Done).</summary>
        public const string Submit = "submit";

        /// <summary>
        /// Reports the return key itself, raised when the platform defines no editor action for it.
        /// The editor resolves it through its key bindings, so a newline is one possible outcome
        /// and <see cref="SubmitKeyBehavior"/> can bind it to submission instead.
        /// </summary>
        public const string Return = "return";

        /// <summary>
        /// Ends the editing session, releasing focus and the software keyboard with it. Unlike
        /// <see cref="Cancel"/> it commits nothing and discards nothing, and unlike
        /// <see cref="Submit"/> it raises no submission.
        /// </summary>
        public const string Done = "done";

        /// <summary>Moves focus to the next field.</summary>
        public const string Next = "next";

        /// <summary>Moves focus to the previous field.</summary>
        public const string Previous = "previous";

        /// <summary>Dismisses the current editing operation.</summary>
        public const string Cancel = "cancel";

        /// <summary>Copies the selected content through the editor clipboard pipeline.</summary>
        public const string Copy = "copy";

        /// <summary>Cuts the selected content through the editor clipboard pipeline.</summary>
        public const string Cut = "cut";

        /// <summary>Pastes rich clipboard content through the editor input pipeline.</summary>
        public const string Paste = "paste";

        /// <summary>Pastes plain clipboard text through the editor input pipeline.</summary>
        public const string PastePlain = "pastePlain";

        /// <summary>Reverts the latest editor transaction.</summary>
        public const string Undo = "undo";

        /// <summary>Reapplies the latest reverted editor transaction.</summary>
        public const string Redo = "redo";

        /// <summary>Determines whether an identifier belongs to the supported command vocabulary.</summary>
        /// <param name="action">Identifier to validate.</param>
        /// <returns>True for a supported identifier; otherwise false.</returns>
        public static bool IsSupported(string action)
            => action == Submit || action == Return || action == Done ||
               action == Next || action == Previous ||
               action == Cancel || action == Copy || action == Cut || action == Paste ||
               action == PastePlain || action == Undo || action == Redo;
    }

    /// <summary>
    /// Specifies how a native producer resolves its active composition before quiescence completes.
    /// </summary>
    public enum NativeCompositionDisposition
    {
        /// <summary>Preserves the platform composition for the next producer epoch.</summary>
        Preserve,

        /// <summary>Reports the composed text as committed input before ending the composition.</summary>
        Commit,

        /// <summary>Ends the composition without reporting its text as committed input.</summary>
        Cancel,
    }

    /// <summary>
    /// Configuration for opening one native input producer.
    /// </summary>
    public readonly struct NativeInputOpenRequest
    {
        /// <summary>Gets whether the producer should show a software keyboard.</summary>
        public bool ShowSoftwareKeyboard { get; }

        /// <summary>Gets the keyboard traits to read during the open call; null selects platform defaults.</summary>
        public NativeKeyboardConfig Keyboard { get; }

        /// <summary>Gets whether the producer must use secure text entry.</summary>
        public bool SecureTextEntry { get; }

        /// <summary>Gets whether the authoritative editor accepts line breaks.</summary>
        public bool AcceptsNewlines { get; }

        /// <summary>
        /// Creates configuration for one producer epoch.
        /// </summary>
        /// <param name="showSoftwareKeyboard">Whether a software keyboard is requested.</param>
        /// <param name="keyboard">Keyboard traits, or null for platform defaults.</param>
        /// <param name="secureTextEntry">Whether secure entry is required.</param>
        /// <param name="acceptsNewlines">Whether line breaks are accepted.</param>
        public NativeInputOpenRequest(bool showSoftwareKeyboard, NativeKeyboardConfig keyboard,
            bool secureTextEntry, bool acceptsNewlines)
        {
            ShowSoftwareKeyboard = showSoftwareKeyboard;
            Keyboard = keyboard;
            SecureTextEntry = secureTextEntry;
            AcceptsNewlines = acceptsNewlines;
        }
    }

    internal interface INativeInputRecipient
    {
        void ReceiveKeyDown(NativeKeyCode key, NativeModifiers modifiers);
        void ReceiveTextInput(string text);
        void ReceiveTextReplacement(int charStart, int charLength, string text);
        void ReceiveDeleteBackward();
        void ReceiveEditorAction(string action);
        void ReceiveCompositionChanged(CompositionData data);
        void ReceiveCompositionEnded();
        void ReceiveSelectionChanged(int charStart, int charEnd);
        void ReceiveFieldEdit(in NativeFieldEditIntent intent);
        void ReceiveFieldComposition(in NativeFieldCompositionIntent intent);
        void ReceiveFieldSelection(in NativeFieldSelectionIntent intent);
        void ReceiveFieldAction(in NativeFieldActionIntent intent);
        void ReceiveFieldQuiesced(in NativeInputQuiescedIntent intent);
        void ReceiveFault(NativeInputReporter source, Exception error);
    }

    internal sealed class NativeInputGeneration
    {
        internal readonly INativeInputBackend Backend;
        internal bool IsAlive = true;

        internal NativeInputGeneration(INativeInputBackend backend) => Backend = backend;
    }

    /// <summary>
    /// Session-scoped sink for ordered input produced by one native backend generation.
    /// </summary>
    /// <remarks>
    /// Editing reports and context access are accepted on the creating thread until the
    /// quiescence barrier completes or the epoch is closed. A terminal fault remains reportable
    /// while the epoch is quiesced. Keyboard lifecycle reports remain valid until the owning
    /// backend generation is disposed.
    /// </remarks>
    public sealed class NativeInputReporter : ITextInputContext
    {
        private enum ReporterState
        {
            Open,
            Quiescing,
            Quiesced,
            Closed,
        }

        private readonly ITextInputContext context;
        private readonly INativeInputRecipient recipient;
        private readonly NativeInputGeneration generation;
        private readonly int threadId;
        private ReporterState state;
        private int quiescenceToken;

        internal NativeInputReporter(int sessionId, ITextInputContext context,
            INativeInputRecipient recipient, NativeInputGeneration generation)
        {
            if (sessionId <= 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.recipient = recipient ?? throw new ArgumentNullException(nameof(recipient));
            this.generation = generation ?? throw new ArgumentNullException(nameof(generation));
            SessionId = sessionId;
            threadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>Gets the opaque positive identifier of the logical input session shared by its replacement epochs.</summary>
        public int SessionId { get; }

        /// <summary>Gets the epoch-gated document and geometry context.</summary>
        public ITextInputContext Context => this;

        internal NativeInputGeneration Generation => generation;

        internal INativeInputRecipient Recipient => recipient;

        internal bool IsOpen => state == ReporterState.Open;

        internal bool IsQuiesced => state == ReporterState.Quiesced;

        internal bool IsClosed => state == ReporterState.Closed;

        /// <summary>Reports one platform-independent editing key press.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="key"/> is not deliverable.</exception>
        /// <exception cref="ArgumentException"><paramref name="modifiers"/> contains an undefined flag.</exception>
        /// <exception cref="InvalidOperationException">The epoch no longer accepts editing reports.</exception>
        public void ReportKeyDown(NativeKeyCode key, NativeModifiers modifiers)
        {
            ValidateEditingReport();
            if (!NativeKeyCodeExtensions.IsDeliverable((int)key))
                throw new ArgumentOutOfRangeException(nameof(key));
            const NativeModifiers allModifiers = NativeModifiers.Shift | NativeModifiers.Ctrl |
                                                 NativeModifiers.Alt | NativeModifiers.Cmd;
            if ((modifiers & ~allModifiers) != 0)
                throw new ArgumentException("The modifier set contains an undefined flag.", nameof(modifiers));
            UniTextNativeInput.DispatchKeyDown(this, key, modifiers);
        }

        /// <summary>Reports committed text after platform layout and IME processing.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The epoch no longer accepts editing reports.</exception>
        public void ReportTextInput(string text)
        {
            ValidateEditingReport();
            if (text == null) throw new ArgumentNullException(nameof(text));
            UniTextNativeInput.DispatchTextInput(this, text);
        }

        /// <summary>Reports replacement text for an exact UTF-16 range in the committed document.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The range lies outside the committed document.</exception>
        /// <exception cref="ArgumentException">A range endpoint splits a Unicode scalar value.</exception>
        /// <exception cref="InvalidOperationException">The epoch no longer accepts editing reports.</exception>
        public void ReportTextReplacement(int charStart, int charLength, string text)
        {
            ValidateEditingReport();
            if (text == null) throw new ArgumentNullException(nameof(text));
            ValidateCharRange(charStart, charLength);
            UniTextNativeInput.DispatchTextReplacement(this, charStart, charLength, text);
        }

        /// <summary>Reports one soft-input backward-delete command.</summary>
        /// <exception cref="InvalidOperationException">The epoch no longer accepts editing reports.</exception>
        public void ReportDeleteBackward()
        {
            ValidateEditingReport();
            UniTextNativeInput.DispatchDeleteBackward(this);
        }

        /// <summary>Reports one supported editor-action identifier.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="action"/> is unsupported.</exception>
        /// <exception cref="InvalidOperationException">The epoch no longer accepts editing reports.</exception>
        public void ReportEditorAction(string action)
        {
            ValidateEditingReport();
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (!NativeEditorAction.IsSupported(action))
                throw new ArgumentException("The editor action is unsupported.", nameof(action));
            UniTextNativeInput.DispatchEditorAction(this, action);
        }

        /// <summary>
        /// Reports the complete current composition snapshot, whose spans are consumed before return.
        /// </summary>
        /// <exception cref="ArgumentException">A cursor or clause lies outside the composition text.</exception>
        /// <exception cref="InvalidOperationException">The epoch no longer accepts editing reports.</exception>
        public void ReportCompositionChanged(CompositionData data)
        {
            ValidateEditingReport();
            ValidateComposition(in data);
            UniTextNativeInput.DispatchCompositionChanged(this, data);
        }

        /// <summary>Reports that the current composition has ended after any committed text.</summary>
        /// <exception cref="InvalidOperationException">The epoch no longer accepts editing reports.</exception>
        public void ReportCompositionEnded()
        {
            ValidateEditingReport();
            UniTextNativeInput.DispatchCompositionEnded(this);
        }

        /// <summary>Reports UTF-16 selection endpoints in the authoritative committed document.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Either endpoint lies outside the committed document.</exception>
        /// <exception cref="ArgumentException">Either endpoint splits a Unicode scalar value.</exception>
        /// <exception cref="InvalidOperationException">The epoch no longer accepts editing reports.</exception>
        public void ReportSelectionChanged(int charStart, int charEnd)
        {
            ValidateEditingReport();
            if (charStart < 0) throw new ArgumentOutOfRangeException(nameof(charStart));
            if (charEnd < 0) throw new ArgumentOutOfRangeException(nameof(charEnd));
            int charCount = context.CharCount;
            if (charStart > charCount) throw new ArgumentOutOfRangeException(nameof(charStart));
            if (charEnd > charCount) throw new ArgumentOutOfRangeException(nameof(charEnd));
            ValidateCharBoundary(charStart, charCount, nameof(charStart));
            ValidateCharBoundary(charEnd, charCount, nameof(charEnd));
            UniTextNativeInput.DispatchSelectionChanged(this, charStart, charEnd);
        }

        /// <summary>
        /// Reports one exact UTF-16 replacement performed by a visible native replica.
        /// <paramref name="nativeRevision"/> numbers the replica state this intent produces;
        /// <paramref name="authorityRevision"/> names the projection it was computed against.
        /// </summary>
        internal void ReportFieldEdit(int nativeRevision, int authorityRevision, int start, int length,
            string text, int selectionStart, int selectionEnd)
        {
            ValidateEditingReport();
            if (text == null) throw new ArgumentNullException(nameof(text));
            ValidateFieldRevisions(nativeRevision, authorityRevision);
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (selectionStart < 0) throw new ArgumentOutOfRangeException(nameof(selectionStart));
            if (selectionEnd < 0) throw new ArgumentOutOfRangeException(nameof(selectionEnd));
            UniTextNativeInput.DispatchFieldEdit(this, new NativeFieldEditIntent(
                SessionId, nativeRevision, authorityRevision, start, length, text,
                selectionStart, selectionEnd));
        }

        internal void ReportFieldComposition(int nativeRevision, int authorityRevision,
            NativeFieldCompositionPhase phase, int start, int length, string text, int cursor)
        {
            ValidateEditingReport();
            if (text == null) throw new ArgumentNullException(nameof(text));
            ValidateFieldRevisions(nativeRevision, authorityRevision);
            if ((uint)phase > (uint)NativeFieldCompositionPhase.Cancel)
                throw new ArgumentOutOfRangeException(nameof(phase));
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (cursor < -1 || cursor > text.Length)
                throw new ArgumentOutOfRangeException(nameof(cursor));
            UniTextNativeInput.DispatchFieldComposition(this, new NativeFieldCompositionIntent(
                SessionId, nativeRevision, authorityRevision, phase, start, length, text, cursor));
        }

        internal void ReportFieldSelection(int nativeRevision, int authorityRevision,
            int start, int end)
        {
            ValidateEditingReport();
            ValidateFieldRevisions(nativeRevision, authorityRevision);
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (end < 0) throw new ArgumentOutOfRangeException(nameof(end));
            UniTextNativeInput.DispatchFieldSelection(this, new NativeFieldSelectionIntent(
                SessionId, nativeRevision, authorityRevision, start, end));
        }

        internal void ReportFieldAction(int nativeRevision, int authorityRevision, string action,
            NativeModifiers modifiers)
        {
            ValidateEditingReport();
            if (action == null) throw new ArgumentNullException(nameof(action));
            ValidateFieldRevisions(nativeRevision, authorityRevision);
            if (!NativeEditorAction.IsSupported(action))
                throw new ArgumentException("The editor action is unsupported.", nameof(action));
            UniTextNativeInput.DispatchFieldAction(this, new NativeFieldActionIntent(
                SessionId, nativeRevision, authorityRevision, action, modifiers));
        }

        /// <summary>Reports the replica barrier that closes the acknowledged intent stream.</summary>
        internal void ReportFieldQuiesced(int nativeRevision, int authorityRevision)
        {
            ValidateEditingReport();
            ValidateFieldRevisions(nativeRevision, authorityRevision);
            UniTextNativeInput.DispatchFieldQuiesced(this, new NativeInputQuiescedIntent(
                SessionId, nativeRevision, authorityRevision));
        }

        /// <summary>Reports a terminal producer failure for this exact epoch.</summary>
        /// <param name="error">Failure reported by the producer.</param>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The epoch is closed or no longer current.</exception>
        public void ReportFault(Exception error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            ValidateFaultReport();
            UniTextNativeInput.DispatchFault(this, error);
        }

        /// <summary>Reports one validated software-keyboard lifecycle snapshot.</summary>
        /// <exception cref="ArgumentException"><paramref name="value"/> contains invalid lifecycle data.</exception>
        /// <exception cref="InvalidOperationException">The owning backend generation has been disposed.</exception>
        public void ReportKeyboardEvent(in KeyboardEvent value)
        {
            ValidateKeyboardReport();
            if ((uint)value.phase > (uint)KeyboardEventPhase.WillChangeFrame)
                throw new ArgumentException("The keyboard phase is undefined.", nameof(value));
            if ((uint)value.easing > (uint)KeyboardEasing.EaseInOut)
                throw new ArgumentException("The keyboard easing is undefined.", nameof(value));
            if (!UniTextNativeInput.IsFinite(value.animationDuration) || value.animationDuration < 0f ||
                !UniTextNativeInput.IsFinite(value.animationFraction) || value.animationFraction < 0f ||
                value.animationFraction > 1f || !UniTextNativeInput.IsValidRect(value.area))
                throw new ArgumentException("The keyboard lifecycle data is invalid.", nameof(value));
            UniTextNativeInput.DispatchKeyboardEvent(this, in value);
        }

        /// <summary>Reports a phase-less software-keyboard visibility and area snapshot.</summary>
        /// <exception cref="ArgumentException"><paramref name="area"/> is not a finite non-negative rectangle.</exception>
        /// <exception cref="InvalidOperationException">The owning backend generation has been disposed.</exception>
        public void ReportKeyboardAreaChanged(bool visible, Rect area)
        {
            ValidateKeyboardReport();
            if (!UniTextNativeInput.IsValidRect(area))
                throw new ArgumentException("The keyboard area is invalid.", nameof(area));
            UniTextNativeInput.DispatchKeyboardAreaChanged(this, visible, area);
        }

        internal int BeginQuiescence()
        {
            ValidateThread();
            if (state != ReporterState.Open)
                throw new InvalidOperationException("The input epoch is not open.");
            if (quiescenceToken == int.MaxValue)
                throw new InvalidOperationException("The input epoch exhausted its quiescence token space.");
            state = ReporterState.Quiescing;
            return ++quiescenceToken;
        }

        internal void CompleteQuiescence(int token)
        {
            ValidateThread();
            if (state != ReporterState.Quiescing || token != quiescenceToken)
                throw new InvalidOperationException("The quiescence completion does not belong to the pending barrier.");
            state = ReporterState.Quiesced;
        }

        internal void Close()
        {
            ValidateThread();
            state = ReporterState.Closed;
        }

        int ITextInputContext.CharCount
        {
            get
            {
                ValidateContextAccess();
                return context.CharCount;
            }
        }

        int ITextInputContext.CopyCharRange(int charStart, int charLength, Span<char> destination)
        {
            ValidateContextAccess();
            return context.CopyCharRange(charStart, charLength, destination);
        }

        (int start, int length) ITextInputContext.GetCharSelection()
        {
            ValidateContextAccess();
            return context.GetCharSelection();
        }

        void ITextInputContext.SelectCharRange(int charStart, int charLength)
        {
            ValidateContextAccess();
            context.SelectCharRange(charStart, charLength);
        }

        bool ITextInputContext.TryGetCharRangeRect(int charStart, int charLength, out Rect rect)
        {
            ValidateContextAccess();
            return context.TryGetCharRangeRect(charStart, charLength, out rect);
        }

        int ITextInputContext.HitTestChar(Vector2 screenPos)
        {
            ValidateContextAccess();
            return context.HitTestChar(screenPos);
        }

        int ITextInputContext.TokenizerQuery(int charIndex, int granularity, int direction, int action)
        {
            ValidateContextAccess();
            return context.TokenizerQuery(charIndex, granularity, direction, action);
        }

        int ITextInputContext.WritingDirection(int charIndex)
        {
            ValidateContextAccess();
            return context.WritingDirection(charIndex);
        }

        string ITextInputContext.InitialText
        {
            get
            {
                ValidateContextAccess();
                return context.InitialText;
            }
        }

        private void ValidateEditingReport()
        {
            ValidateThread();
            if (!generation.IsAlive || state != ReporterState.Open && state != ReporterState.Quiescing)
                throw new InvalidOperationException("The input epoch no longer accepts editing reports.");
        }

        private void ValidateKeyboardReport()
        {
            ValidateThread();
            if (!generation.IsAlive)
                throw new InvalidOperationException("The owning backend generation has been disposed.");
        }

        private void ValidateFaultReport()
        {
            ValidateThread();
            if (!generation.IsAlive || state == ReporterState.Closed)
                throw new InvalidOperationException("The input epoch no longer accepts fault reports.");
        }

        private void ValidateContextAccess() => ValidateEditingReport();

        private static void ValidateFieldRevisions(int nativeRevision, int authorityRevision)
        {
            if (nativeRevision <= 0) throw new ArgumentOutOfRangeException(nameof(nativeRevision));
            if (authorityRevision <= 0) throw new ArgumentOutOfRangeException(nameof(authorityRevision));
        }

        private void ValidateThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("Native input reports and context queries require the creating thread.");
        }

        private static void ValidateComposition(in CompositionData data)
        {
            if (data.cursorPosition < -1 || data.cursorPosition > data.text.Length)
                throw new ArgumentException("The composition cursor lies outside the text.", nameof(data));
            int previousEnd = 0;
            for (int i = 0; i < data.clauses.Length; i++)
            {
                var clause = data.clauses[i];
                if (clause.startOffset < previousEnd || clause.endOffset < clause.startOffset ||
                    clause.endOffset > data.text.Length ||
                    (uint)clause.style > (uint)CompositionClauseStyle.Error)
                    throw new ArgumentException("A composition clause is invalid.", nameof(data));
                previousEnd = clause.endOffset;
            }
        }

        private void ValidateCharRange(int charStart, int charLength)
        {
            if (charStart < 0) throw new ArgumentOutOfRangeException(nameof(charStart));
            if (charLength < 0) throw new ArgumentOutOfRangeException(nameof(charLength));
            int charCount = context.CharCount;
            if (charStart > charCount) throw new ArgumentOutOfRangeException(nameof(charStart));
            if (charLength > charCount - charStart)
                throw new ArgumentOutOfRangeException(nameof(charLength));
            ValidateCharBoundary(charStart, charCount, nameof(charStart));
            ValidateCharBoundary(charStart + charLength, charCount, nameof(charLength));
        }

        private void ValidateCharBoundary(int charIndex, int charCount, string parameter)
        {
            if (charIndex == 0 || charIndex == charCount) return;
            Span<char> pair = stackalloc char[2];
            if (context.CopyCharRange(charIndex - 1, 2, pair) != 2)
                throw new InvalidOperationException(
                    "The text input context returned an incomplete UTF-16 boundary.");
            if (char.IsHighSurrogate(pair[0]) && char.IsLowSurrogate(pair[1]))
                throw new ArgumentException("The range endpoint splits a Unicode scalar value.", parameter);
        }

    }

    /// <summary>
    /// Facade for native producer lifecycles and software-keyboard notifications.
    /// </summary>
    public static partial class UniTextNativeInput
    {
#if UNITY_EDITOR
        static UniTextNativeInput() => EditorLifecycle.ManagedCleaning += Shutdown;
#else
        static UniTextNativeInput() => Application.quitting += Shutdown;
#endif

        #region Producer lifecycle

        internal static NativeInputReporter CreateReporter(int sessionId,
            ITextInputContext context, INativeInputRecipient recipient,
            NativeInputReporter inherited = null)
        {
            if (sessionId <= 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (recipient == null) throw new ArgumentNullException(nameof(recipient));
            if (pendingReporter != null)
                throw new InvalidOperationException("Another input epoch is being opened.");

            NativeInputGeneration generation;
            FactoryEntry factory;
            bool staged;
            if (inherited != null)
            {
                ValidateCurrentReporter(inherited);
                if (!inherited.IsQuiesced)
                    throw new InvalidOperationException("Only a quiesced input epoch can be inherited.");
                generation = inherited.Generation;
                factory = backendFactory;
                staged = false;
            }
            else
            {
                if (activeReporter != null && !activeReporter.IsClosed)
                    throw new InvalidOperationException("The current input epoch must be closed or inherited.");
                ResolveGeneration(out generation, out factory, out staged);
            }

            var reporter = new NativeInputReporter(sessionId, context, recipient, generation);
            pendingReporter = reporter;
            pendingFactory = factory;
            pendingGenerationIsStaged = staged;
            return reporter;
        }

        internal static void OpenInput(NativeInputReporter reporter,
            in NativeInputOpenRequest request)
        {
            ValidatePendingReporter(reporter);
            pendingOpenInProgress = true;
            try
            {
                reporter.Generation.Backend.OpenInput(in request, reporter);
                CommitPendingReporter(reporter);
            }
            catch
            {
                FailPendingReporter(reporter);
                throw;
            }
        }

        internal static void OpenNativeField(NativeInputReporter reporter,
            in NativeFieldOpenRequest request)
        {
            ValidatePendingReporter(reporter);
            try
            {
                if (request.SessionId != reporter.SessionId)
                    throw new ArgumentException("The native field request does not belong to the reporter.", nameof(request));
                if (reporter.Generation.Backend is not INativeFieldBackend fieldBackend)
                    throw new NotSupportedException(
                        $"{reporter.Generation.Backend.GetType().FullName} does not support native field presentation.");
                pendingOpenInProgress = true;
                fieldBackend.OpenNativeField(in request, reporter);
                CommitPendingReporter(reporter);
            }
            catch
            {
                FailPendingReporter(reporter);
                throw;
            }
        }

        internal static void ReconcileNativeField(int sessionId, int sourceNativeRevision,
            int authorityRevision, string text, int selectionStart, int selectionEnd)
        {
            var sessionBackend = GetSessionBackend(sessionId);
            if (sessionBackend is not INativeFieldBackend nativeFieldBackend)
                throw new InvalidOperationException("The active input backend does not support native fields.");
            nativeFieldBackend.ReconcileNativeField(sessionId, sourceNativeRevision,
                authorityRevision, text, selectionStart, selectionEnd);
        }

        internal static void UpdateNativeField(in NativeFieldUpdateRequest request)
        {
            var sessionBackend = GetSessionBackend(request.SessionId);
            if (sessionBackend is not INativeFieldBackend nativeFieldBackend)
                throw new InvalidOperationException("The active input backend does not support native fields.");
            nativeFieldBackend.UpdateNativeField(in request);
        }

        internal static void FocusNativeField(int sessionId, NativeInputReporter reporter)
        {
            if (sessionId <= 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
            ValidatePendingReporter(reporter);
            try
            {
                if (reporter.SessionId != sessionId)
                    throw new ArgumentException("The session identifier does not belong to the reporter.", nameof(sessionId));
                if (reporter.Generation.Backend is not INativeFieldBackend fieldBackend)
                    throw new NotSupportedException(
                        $"{reporter.Generation.Backend.GetType().FullName} does not support native field presentation.");
                pendingOpenInProgress = true;
                fieldBackend.FocusNativeField(sessionId, reporter);
                CommitPendingReporter(reporter);
            }
            catch
            {
                FailPendingReporter(reporter);
                throw;
            }
        }

        internal static void QuiesceInput(NativeInputReporter reporter,
            NativeCompositionDisposition disposition, Action completion)
        {
            if (reporter == null) throw new ArgumentNullException(nameof(reporter));
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            if ((uint)disposition > (uint)NativeCompositionDisposition.Cancel)
                throw new ArgumentOutOfRangeException(nameof(disposition));
            ValidateCurrentReporter(reporter);
            int token = reporter.BeginQuiescence();
            try
            {
                reporter.Generation.Backend.QuiesceInput(reporter, disposition, () =>
                {
                    reporter.CompleteQuiescence(token);
                    completion();
                });
            }
            catch (Exception error)
            {
                try
                {
                    if (!reporter.IsClosed) AbortInput(reporter);
                }
                catch (Exception cleanupError)
                {
                    Debug.LogException(cleanupError);
                }
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }

        internal static void CloseInput(NativeInputReporter reporter)
        {
            if (reporter == null) throw new ArgumentNullException(nameof(reporter));
            ValidateCurrentReporter(reporter);
            if (!reporter.IsQuiesced)
                throw new InvalidOperationException("Graceful close requires a completed quiescence barrier.");
            try
            {
                reporter.Generation.Backend.CloseInput(reporter);
            }
            catch (Exception error)
            {
                try
                {
                    if (!reporter.IsClosed) AbortInput(reporter);
                }
                catch (Exception cleanupError)
                {
                    Debug.LogException(cleanupError);
                }
                ExceptionDispatchInfo.Capture(error).Throw();
            }
            reporter.Close();
            activeReporter = null;
        }

        internal static void AbortInput(NativeInputReporter reporter)
        {
            if (reporter == null) throw new ArgumentNullException(nameof(reporter));
            bool current = ReferenceEquals(reporter, activeReporter);
            bool pending = ReferenceEquals(reporter, pendingReporter);
            if (!current && !pending)
                throw new InvalidOperationException("The reporter is not bound to the active backend generation.");
            if (!ReferenceEquals(reporter.Generation, backendGeneration) &&
                !pendingGenerationIsStaged)
                throw new InvalidOperationException("The reporter is not bound to the active backend generation.");
            bool disposeGeneration = pending && pendingGenerationIsStaged;
            bool abortProducer = current || pending && pendingOpenInProgress;
            var generation = reporter.Generation;
            reporter.Close();
            Exception failure = null;
            bool abortFailed = false;
            try
            {
                if (abortProducer) generation.Backend.AbortInput(reporter);
            }
            catch (Exception error)
            {
                failure = error;
                abortFailed = true;
            }
            finally
            {
                if (current) activeReporter = null;
                if (pending) ClearPendingReporter();
                if (abortFailed)
                    PoisonCurrentGeneration(generation, ref failure);
                if (disposeGeneration)
                {
                    try
                    {
                        DisposeGeneration(generation.Backend, generation);
                    }
                    catch (Exception error)
                    {
                        if (failure == null) failure = error;
                        else Debug.LogException(error);
                    }
                }
            }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        /// <summary>
        /// Updates the focused editor rectangle used by the active platform producer.
        /// </summary>
        /// <remarks>
        /// The request has no effect without an open producer. A visible native presenter may
        /// apply its own platform-defined layout.
        /// </remarks>
        /// <param name="screenRect">Field rect in Unity screen pixels (Y-up, origin at bottom-left).</param>
        /// <exception cref="ArgumentException"><paramref name="screenRect"/> is not finite or has a negative size.</exception>
        public static void SetInputFieldRect(Rect screenRect)
        {
            if (!IsValidRect(screenRect))
                throw new ArgumentException("The input field rectangle is invalid.", nameof(screenRect));
            CommandBackend?.SetInputFieldRect(screenRect);
        }

        /// <summary>Gets whether a software keyboard is currently visible.</summary>
        public static bool IsKeyboardVisible => lastKeyboardVisible;

        /// <summary>Occurs when software-keyboard visibility changes.</summary>
        public static event Action<bool> KeyboardVisibilityChanged;

        /// <summary>
        /// Occurs when the platform delivers a keyboard lifecycle event — show, hide, frame
        /// change, and (where supported) per-frame animation progress.
        /// </summary>
        /// <remarks>
        /// On platforms with frame-synchronized animation, the
        /// <see cref="KeyboardEventPhase.AnimationProgress"/> phase fires once per rendered frame
        /// during keyboard animation.
        /// </remarks>
        public static event Action<KeyboardEvent> KeyboardChanged;

        /// <summary>
        /// Screen area occupied by the software keyboard. Returns <see cref="Rect.zero"/>
        /// when no keyboard is visible. In an embedded WebGL player, coordinates remain relative
        /// to the canvas and the rect can extend beyond the Unity screen bounds.
        /// </summary>
        public static Rect KeyboardArea => keyboardArea;

        private static Rect keyboardArea;

        #endregion

        #region Control

        /// <summary>
        /// Updates the IME candidate anchor in Unity screen coordinates.
        /// </summary>
        /// <remarks>The request has no effect without an open producer.</remarks>
        /// <param name="screenPos">Caret position in screen coordinates (pixels).</param>
        /// <param name="lineHeight">Height of the text line in screen pixels. Used by the OS to
        /// position the IME candidate window below the current line of text.</param>
        /// <exception cref="ArgumentException"><paramref name="screenPos"/> is not finite.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="lineHeight"/> is negative or not finite.</exception>
        public static void SetCursorScreenPos(Vector2 screenPos, float lineHeight)
        {
            if (!IsFinite(screenPos.x) || !IsFinite(screenPos.y))
                throw new ArgumentException("The cursor position is not finite.", nameof(screenPos));
            if (!IsFinite(lineHeight) || lineHeight < 0f)
                throw new ArgumentOutOfRangeException(nameof(lineHeight));
            CommandBackend?.SetCursorScreenPos(screenPos, lineHeight);
        }

        internal static bool IsValidRect(Rect value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.width) &&
               IsFinite(value.height) && value.width >= 0f && value.height >= 0f;

        internal static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        internal static void PushTextContext(string text, int windowStart, int selectionStart, int selectionEnd, bool forceRestart)
        {
            CommandBackend?.PushTextContext(text, windowStart, selectionStart, selectionEnd, forceRestart);
        }

        internal static bool WantsTextContext => CommandBackend?.WantsTextContext == true;

        internal static readonly Action FlushPendingInput = static () => CommandBackend?.FlushPendingInput();

        /// <summary>
        /// Returns the native window's client-area size in OS pixels. Used to convert between
        /// Unity's virtual screen coordinates and actual window coordinates. Returns
        /// (Screen.width, Screen.height) on platforms without native window access.
        /// </summary>
        public static Vector2Int GetWindowClientSize()
        {
            return getWindowClientSize != null
                ? getWindowClientSize()
                : new Vector2Int(Screen.width, Screen.height);
        }

        internal static Func<Vector2Int> getWindowClientSize;

        internal static Action<RectInt> imeCaretScreenRect;

        /// <summary>Requests a desktop-space caret rectangle from platforms that expose native IME placement.</summary>
        public static void SetImeCaretScreenRect(RectInt osDesktopRect) => imeCaretScreenRect?.Invoke(osDesktopRect);

        /// <summary>
        /// Sets Unity's IME state through the active EventSystem's <see cref="BaseInput"/> when one
        /// exists — the switch desktop Linux keys its shift- and layout-resolved character translation
        /// on under Input System-only handling; the Input System's own keyboard command does not engage
        /// it there and serves only as the module-less fallback.
        /// </summary>
        internal static void SetImeEnabled(bool enabled)
        {
            var input = EventSystemInput;
            if (input != null)
            {
                input.imeCompositionMode = enabled ? IMECompositionMode.On : IMECompositionMode.Auto;
                return;
            }
#if ENABLE_LEGACY_INPUT_MANAGER
            Input.imeCompositionMode = enabled ? IMECompositionMode.On : IMECompositionMode.Auto;
#elif ENABLE_INPUT_SYSTEM
            Keyboard.current?.SetIMEEnabled(enabled);
#endif
        }

        private static BaseInput EventSystemInput
        {
            get
            {
                var eventSystem = EventSystem.current;
                if (eventSystem == null) return null;
                var module = eventSystem.currentInputModule;
                return module != null ? module.input : null;
            }
        }

        internal static void SetImeCursorPosition(Vector2 screenPos)
        {
            GetImeWindowProjection(out float offsetX, out float scaleX,
                out float offsetY, out float scaleY);
            var position = new Vector2(
                offsetX + screenPos.x * scaleX,
                offsetY - screenPos.y * scaleY);
            var input = EventSystemInput;
            if (input != null)
            {
                input.compositionCursorPos = position;
                return;
            }
#if ENABLE_LEGACY_INPUT_MANAGER
            Input.compositionCursorPos = position;
#elif ENABLE_INPUT_SYSTEM
            Keyboard.current?.SetIMECursorPosition(position);
#endif
        }

        internal static void GetImeWindowProjection(out float offsetX, out float scaleX,
            out float offsetY, out float scaleY)
        {
            float screenWidth = Screen.width;
            float screenHeight = Screen.height;
            float x = 0f;
            float y = 0f;
            float width = screenWidth;
            float height = screenHeight;
#if UNITY_EDITOR
            if (InputUtils.TryGetEditorGameViewProjection(out var view, out var fit))
            {
                width = screenWidth * fit;
                height = screenHeight * fit;
                x = view.x + (view.width - width) * 0.5f;
                y = 2f * view.y + (view.height - height) * 0.5f;
            }
#endif
            offsetX = x;
            scaleX = screenWidth > 0f ? width / screenWidth : 1f;
            offsetY = y + height;
            scaleY = screenHeight > 0f ? height / screenHeight : 1f;
        }

        #endregion

        #region Backend registration

        private sealed class FactoryEntry
        {
            public Func<INativeInputBackend> create;
            public int priority;
        }

        private static INativeInputBackend backend;
        private static FactoryEntry backendFactory;
        private static NativeInputGeneration backendGeneration;
        private static NativeInputReporter activeReporter;
        private static NativeInputReporter pendingReporter;
        private static FactoryEntry pendingFactory;
        private static bool pendingGenerationIsStaged;
        private static bool pendingOpenInProgress;
        private static readonly List<FactoryEntry> factories = new();
        private static readonly List<WeakReference<INativeInputBackend>> issuedBackends = new();

        /// <summary>
        /// Registers a lazily created backend candidate ordered by descending priority.
        /// </summary>
        /// <remarks>
        /// A null factory result declines the current activation. Every non-null result must be
        /// a fresh instance owned exclusively by the facade; factory exceptions fail activation.
        /// Equal priorities prefer the latest registration, and registering the same delegate
        /// replaces its previous entry.
        /// </remarks>
        /// <param name="factory">Factory invoked when its candidate is eligible for activation.</param>
        /// <param name="priority">Ordering value relative to other registered candidates.</param>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public static void RegisterBackend(Func<INativeInputBackend> factory, int priority = 0)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            InsertFactory(new FactoryEntry { create = factory, priority = priority });
        }

        private static void InsertFactory(FactoryEntry entry)
        {
            bool replacesActiveFactory = false;
            for (int index = factories.Count - 1; index >= 0; index--)
            {
                var existing = factories[index];
                if (existing.create != entry.create) continue;
                replacesActiveFactory |= ReferenceEquals(existing, backendFactory);
                factories.RemoveAt(index);
            }

            int insertionIndex = 0;
            while (insertionIndex < factories.Count && factories[insertionIndex].priority > entry.priority)
                insertionIndex++;
            factories.Insert(insertionIndex, entry);
            if (replacesActiveFactory) backendFactory = entry;
        }

        private static void ResolveGeneration(out NativeInputGeneration generation,
            out FactoryEntry factory, out bool staged)
        {
            int activeIndex = backend == null ? factories.Count : factories.IndexOf(backendFactory);
            if (activeIndex < 0) activeIndex = factories.Count;
            for (int index = 0; index < activeIndex; index++)
            {
                var entry = factories[index];
                var created = entry.create();
                if (created == null) continue;
                ValidateFreshBackend(created);
                generation = new NativeInputGeneration(created);
                factory = entry;
                staged = true;
                return;
            }

            if (backend != null)
            {
                generation = backendGeneration;
                factory = backendFactory;
                staged = false;
                return;
            }

            throw new InvalidOperationException("No registered native input backend accepted the current environment.");
        }

        private static void ValidateFreshBackend(INativeInputBackend candidate)
        {
            for (int index = issuedBackends.Count - 1; index >= 0; index--)
            {
                if (!issuedBackends[index].TryGetTarget(out var issued))
                {
                    issuedBackends.RemoveAt(index);
                    continue;
                }
                if (ReferenceEquals(issued, candidate))
                    throw new InvalidOperationException(
                        "A backend factory returned an instance that the facade already owns or disposed.");
            }
            issuedBackends.Add(new WeakReference<INativeInputBackend>(candidate));
        }

        private static void ValidatePendingReporter(NativeInputReporter reporter)
        {
            if (reporter == null) throw new ArgumentNullException(nameof(reporter));
            if (!ReferenceEquals(reporter, pendingReporter) || !reporter.Generation.IsAlive)
                throw new InvalidOperationException("The reporter is not the pending input epoch.");
        }

        private static void ValidateCurrentReporter(NativeInputReporter reporter)
        {
            if (!ReferenceEquals(reporter, activeReporter) ||
                !ReferenceEquals(reporter.Generation, backendGeneration) ||
                !reporter.Generation.IsAlive)
                throw new InvalidOperationException("The reporter is not bound to the active backend generation.");
        }

        private static INativeInputBackend GetSessionBackend(int sessionId)
        {
            if (sessionId <= 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
            if (pendingOpenInProgress && pendingReporter?.SessionId == sessionId)
                return pendingReporter.Generation.Backend;
            if (activeReporter?.SessionId == sessionId) return activeReporter.Generation.Backend;
            throw new InvalidOperationException("The session is not bound to the active input epoch.");
        }

        private static INativeInputBackend CommandBackend => pendingOpenInProgress
            ? pendingReporter?.Generation.Backend
            : activeReporter?.Generation.Backend;

        private static void CommitPendingReporter(NativeInputReporter reporter)
        {
            ValidatePendingReporter(reporter);
            var oldReporter = activeReporter;
            var oldBackend = backend;
            var oldGeneration = backendGeneration;
            bool replaceGeneration = pendingGenerationIsStaged;

            if (replaceGeneration)
            {
                backend = reporter.Generation.Backend;
                backendFactory = pendingFactory;
                backendGeneration = reporter.Generation;
            }

            activeReporter = reporter;
            if (oldReporter != null && !ReferenceEquals(oldReporter, reporter)) oldReporter.Close();
            ClearPendingReporter();

            if (replaceGeneration && oldBackend != null)
            {
                try
                {
                    DisposeGeneration(oldBackend, oldGeneration);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                }
            }
        }

        private static void FailPendingReporter(NativeInputReporter reporter)
        {
            if (!ReferenceEquals(reporter, pendingReporter)) return;
            reporter.Close();
            bool disposeGeneration = pendingGenerationIsStaged;
            bool abortProducer = pendingOpenInProgress;
            var failedGeneration = reporter.Generation;
            Exception abortFailure = null;
            try
            {
                if (abortProducer) failedGeneration.Backend.AbortInput(reporter);
            }
            catch (Exception error)
            {
                abortFailure = error;
                Debug.LogException(error);
            }
            finally
            {
                ClearPendingReporter();
            }
            if (abortFailure != null)
            {
                PoisonCurrentGeneration(failedGeneration, ref abortFailure);
            }
            if (!disposeGeneration) return;
            try
            {
                DisposeGeneration(failedGeneration.Backend, failedGeneration);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        private static void PoisonCurrentGeneration(NativeInputGeneration generation,
            ref Exception failure)
        {
            if (!ReferenceEquals(generation, backendGeneration)) return;
            activeReporter?.Close();
            var poisonedBackend = backend;
            DetachCurrentGeneration();
            if (poisonedBackend == null)
            {
                generation.IsAlive = false;
                return;
            }
            CaptureCleanup(() => DisposeGeneration(poisonedBackend, generation), ref failure);
        }

        /// <summary>
        /// Drops the current backend generation together with everything only it could observe. The
        /// software keyboard is one such observation: it is reported by a live generation and by
        /// nothing else, so a retained visibility would outlive every producer able to contradict it
        /// and would answer <see cref="IsKeyboardVisible"/> with a fact no longer in evidence.
        /// </summary>
        private static void DetachCurrentGeneration()
        {
            activeReporter = null;
            backend = null;
            backendFactory = null;
            backendGeneration = null;
            keyboardArea = Rect.zero;
            lastKeyboardVisible = false;
        }

        private static void ClearPendingReporter()
        {
            pendingReporter = null;
            pendingFactory = null;
            pendingGenerationIsStaged = false;
            pendingOpenInProgress = false;
        }

        private static void DisposeGeneration(INativeInputBackend value,
            NativeInputGeneration generation)
        {
            try
            {
                value.Dispose();
            }
            finally
            {
                generation.IsAlive = false;
            }
        }

        internal static void Shutdown()
        {
            var pending = pendingReporter;
            var pendingGeneration = pending?.Generation;
            bool abortPending = pendingOpenInProgress && pending != null;
            bool disposePending = pendingGenerationIsStaged && pendingGeneration != null;
            pending?.Close();
            activeReporter?.Close();
            ClearPendingReporter();

            var oldBackend = backend;
            var oldGeneration = backendGeneration;
            DetachCurrentGeneration();
            getWindowClientSize = null;

            Exception failure = null;
            if (abortPending)
                CaptureCleanup(() => pendingGeneration.Backend.AbortInput(pending), ref failure);
            if (disposePending)
                CaptureCleanup(() => DisposeGeneration(pendingGeneration.Backend, pendingGeneration), ref failure);
            if (oldBackend != null)
                CaptureCleanup(() => DisposeGeneration(oldBackend, oldGeneration), ref failure);
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static void CaptureCleanup(Action cleanup, ref Exception failure)
        {
            try
            {
                cleanup();
            }
            catch (Exception error)
            {
                if (failure == null) failure = error;
                else Debug.LogException(error);
            }
        }

        #endregion

        #region Event dispatch

        internal static void DispatchKeyDown(NativeInputReporter source,
            NativeKeyCode key, NativeModifiers modifiers)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveKeyDown(key, modifiers);
        }

        internal static void DispatchTextInput(NativeInputReporter source, string text)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveTextInput(text);
        }

        internal static void DispatchTextReplacement(NativeInputReporter source,
            int charStart, int charLength, string text)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveTextReplacement(charStart, charLength, text);
        }

        internal static void DispatchDeleteBackward(NativeInputReporter source)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveDeleteBackward();
        }

        internal static void DispatchEditorAction(NativeInputReporter source, string action)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveEditorAction(action);
        }

        internal static void DispatchCompositionChanged(NativeInputReporter source,
            CompositionData data)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveCompositionChanged(data);
        }

        internal static void DispatchCompositionEnded(NativeInputReporter source)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveCompositionEnded();
        }

        internal static void DispatchSelectionChanged(NativeInputReporter source,
            int charStart, int charEnd)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveSelectionChanged(charStart, charEnd);
        }

        internal static void DispatchFieldEdit(NativeInputReporter source,
            in NativeFieldEditIntent intent)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveFieldEdit(in intent);
        }

        internal static void DispatchFieldComposition(NativeInputReporter source,
            in NativeFieldCompositionIntent intent)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveFieldComposition(in intent);
        }

        internal static void DispatchFieldSelection(NativeInputReporter source,
            in NativeFieldSelectionIntent intent)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveFieldSelection(in intent);
        }

        internal static void DispatchFieldAction(NativeInputReporter source,
            in NativeFieldActionIntent intent)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveFieldAction(in intent);
        }

        internal static void DispatchFieldQuiesced(NativeInputReporter source,
            in NativeInputQuiescedIntent intent)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveFieldQuiesced(in intent);
        }

        internal static void DispatchFault(NativeInputReporter source, Exception error)
        {
            ValidateEditingDispatcher(source);
            source.Recipient.ReceiveFault(source, error);
        }

        private static bool lastKeyboardVisible;

        private static void DispatchKeyboardVisibilityChanged(bool visible)
        {
            if (visible == lastKeyboardVisible) return;
            lastKeyboardVisible = visible;
            KeyboardVisibilityChanged?.Invoke(visible);
        }

        internal static void DispatchKeyboardEvent(NativeInputReporter source,
            in KeyboardEvent value)
        {
            if (!source.Generation.IsAlive)
                throw new InvalidOperationException("The reporter's backend generation is disposed.");
            if (value.phase == KeyboardEventPhase.DidHide || value.phase == KeyboardEventPhase.WillHide)
                keyboardArea = Rect.zero;
            else if (value.phase == KeyboardEventPhase.DidShow ||
                     value.phase == KeyboardEventPhase.WillShow ||
                     value.phase == KeyboardEventPhase.WillChangeFrame)
                keyboardArea = value.area;

            KeyboardChanged?.Invoke(value);

            if (value.phase == KeyboardEventPhase.DidShow)
                DispatchKeyboardVisibilityChanged(true);
            else if (value.phase == KeyboardEventPhase.DidHide)
                DispatchKeyboardVisibilityChanged(false);
        }

        internal static void DispatchKeyboardAreaChanged(NativeInputReporter source,
            bool visible, Rect area)
        {
            if (!source.Generation.IsAlive)
                throw new InvalidOperationException("The reporter's backend generation is disposed.");
            keyboardArea = visible ? area : Rect.zero;
            DispatchKeyboardVisibilityChanged(visible);
        }

        private static void ValidateEditingDispatcher(NativeInputReporter source)
        {
            if (!ReferenceEquals(source, activeReporter) && !ReferenceEquals(source, pendingReporter))
                throw new InvalidOperationException("The reporter is not bound to the current producer epoch.");
        }

        #endregion
    }

    /// <summary>
    /// Platform transport that binds one native input producer to one reporter epoch at a time.
    /// </summary>
    /// <remarks>
    /// Methods and reporter calls use the Unity main thread. A backend may replace only a
    /// quiesced producer. Each platform callback remains bound to the reporter captured for its
    /// producer epoch. Disposal releases all platform resources and permits no later reports.
    /// </remarks>
    public interface INativeInputBackend : IDisposable
    {
        /// <summary>
        /// Opens a producer for an epoch, atomically replacing a previously quiesced producer.
        /// </summary>
        /// <remarks>
        /// Failure leaves the previously quiesced producer frozen and available for explicit
        /// close or another replacement attempt; the facade aborts the failed new reporter.
        /// </remarks>
        /// <param name="request">Producer and software-keyboard configuration.</param>
        /// <param name="reporter">Exclusive sink, context, and identity of the new producer epoch.</param>
        void OpenInput(in NativeInputOpenRequest request, NativeInputReporter reporter);

        /// <summary>
        /// Freezes new input, drains prior output in source order, resolves composition, and
        /// invokes the barrier completion exactly once.
        /// </summary>
        /// <remarks>
        /// Editing output and context access end before the callback. If the method throws before
        /// completion, it must not invoke the callback later and the facade aborts the reporter.
        /// </remarks>
        /// <param name="reporter">Reporter bound to the producer being frozen.</param>
        /// <param name="disposition">Resolution for an active composition.</param>
        /// <param name="quiesced">Main-thread barrier completion, which may be invoked synchronously.</param>
        void QuiesceInput(NativeInputReporter reporter,
            NativeCompositionDisposition disposition, Action quiesced);

        /// <summary>Terminally releases a producer after its quiescence barrier has completed.</summary>
        /// <remarks>A failure causes the facade to abort the producer before propagating the close error.</remarks>
        /// <param name="reporter">Reporter of the quiesced producer.</param>
        void CloseInput(NativeInputReporter reporter);

        /// <summary>Terminally releases a producer from any state and suppresses its later editing output.</summary>
        /// <remarks>A failure retires and disposes the entire backend generation.</remarks>
        /// <param name="reporter">Reporter of the producer being abandoned.</param>
        void AbortInput(NativeInputReporter reporter);

        /// <summary>Updates the IME candidate anchor in Unity screen pixels.</summary>
        void SetCursorScreenPos(Vector2 screenPos, float lineHeight);

        /// <summary>Updates the focused editor rectangle in Unity screen pixels.</summary>
        void SetInputFieldRect(Rect screenRect);

        /// <summary>
        /// Delivers buffered platform input through the current reporter in source order.
        /// </summary>
        void FlushPendingInput();

        /// <summary>
        /// Updates a push-model IME mirror with a UTF-16 document window and selection.
        /// </summary>
        void PushTextContext(string text, int windowStart, int selectionStart, int selectionEnd, bool forceRestart);

        /// <summary>Gets whether document-window updates are consumed.</summary>
        bool WantsTextContext { get; }
    }

    internal interface INativeFieldBackend
    {
        void OpenNativeField(in NativeFieldOpenRequest request, NativeInputReporter reporter);
        void ReconcileNativeField(int sessionId, int sourceNativeRevision,
            int authorityRevision, string text, int selectionStart, int selectionEnd);
        void UpdateNativeField(in NativeFieldUpdateRequest request);
        void FocusNativeField(int sessionId, NativeInputReporter reporter);
    }

    /// <summary>
    /// Authoritative committed-document, selection, and geometry surface for native text input.
    /// </summary>
    /// <remarks>
    /// Offsets use UTF-16 code units, coordinates use Unity screen pixels, and calls use the
    /// reporter epoch's creating thread.
    /// </remarks>
    public interface ITextInputContext
    {
        /// <summary>Committed document length in UTF-16 code units.</summary>
        int CharCount { get; }

        /// <summary>
        /// Copies committed chars <c>[charStart, charStart + charLength)</c> into
        /// <paramref name="destination"/>, clamping to document bounds and destination
        /// capacity. Returns the number of chars actually written.
        /// </summary>
        int CopyCharRange(int charStart, int charLength, Span<char> destination);

        /// <summary>Current selection normalised to (start, length); a collapsed caret reports (caret, 0).</summary>
        (int start, int length) GetCharSelection();

        /// <summary>
        /// Moves the selection to the given char range (platform <c>replacementRange</c>
        /// semantics — the subsequent text insert overwrites the range). Length 0 places a caret.
        /// </summary>
        void SelectCharRange(int charStart, int charLength);

        /// <summary>
        /// Screen rect of the range's portion on its first line (macOS
        /// <c>firstRectForCharacterRange:</c>, iOS <c>firstRectForRange:</c> — the OS aligns
        /// accent pickers, dictation highlights, and candidate windows to it). Returns false
        /// when no layout is available yet.
        /// </summary>
        bool TryGetCharRangeRect(int charStart, int charLength, out Rect rect);

        /// <summary>Char offset closest to a screen point, or -1 when no layout is available.</summary>
        int HitTestChar(Vector2 screenPos);

        /// <summary>
        /// Text-unit navigation for the platform tokenizer (iOS <c>UITextInputTokenizer</c> and
        /// macOS equivalents). <paramref name="granularity"/>: 0 = character (grapheme), 1 = word.
        /// <paramref name="direction"/>: 0 = forward, 1 = backward. <paramref name="action"/>:
        /// 0 = position toward boundary, 1 = is-at-boundary, 2 = enclosing-range start,
        /// 3 = enclosing-range end, 4 = is-within-unit. Returns a char offset for position
        /// queries, 0 / 1 for boolean queries, -1 when unsupported.
        /// </summary>
        int TokenizerQuery(int charIndex, int granularity, int direction, int action);

        /// <summary>BiDi writing direction at a char offset: 0 = natural / unknown, 1 = LTR, 2 = RTL.</summary>
        int WritingDirection(int charIndex);

        /// <summary>Full committed text used to seed a native field overlay when it is shown.</summary>
        string InitialText { get; }
    }
}
