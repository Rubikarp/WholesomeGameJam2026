using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// Editing capability for <see cref="UniTextSelectable"/>: text storage (gap buffer),
    /// undo/redo, IME composition, validators, caret rendering, input intents,
    /// touch gesture pipeline, keyboard / clipboard handling, and the public mutation API
    /// (<c>InsertText</c>, <c>Cut/Copy/Paste</c>, <c>Undo/Redo</c>, etc.).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The component requires <see cref="UniTextSelectable"/> on the same GameObject as
    /// <see cref="UniTextBase"/>; form-field policies are composed through input behaviors.
    /// </para>
    /// <para>
    /// Add a <see cref="UnityEngine.UI.ContentSizeFitter"/> or place it under a layout group to
    /// size its RectTransform from the text; sizing is
    /// measured through the <see cref="UniTextBase"/> component's own
    /// <see cref="UnityEngine.UI.ILayoutElement"/> implementation. A pointer click activates
    /// it and places the caret; Unity UI navigation reaches it through the hidden
    /// <see cref="UniTextFocusable"/> the editor manages automatically. Put it directly under a
    /// <see cref="UnityEngine.UI.RectMask2D"/> to make that parent the clipping viewport and enable
    /// internal scrolling when content overflows.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(UniTextSelectable))]
    [DisallowMultipleComponent]
    [AddComponentMenu(UniTextMenu.AddComponent.Editable)]
    public sealed partial class UniTextEditable : MonoBehaviour, ITextDocument, ISavedStateProvider,
        IInputBehaviorChangeSink
    {
        /// <summary>Renderer used for the blinking caret.</summary>
        [SerializeField, StateProperty(nameof(ApplyCaretRendererChange))]
        [Tooltip("Caret renderer for the blinking cursor. If null, one is created on first activation. Subclass InputCaretRenderer for custom shapes (block cursor, underline, gradient).")]
        private InputCaretRenderer caretRenderer;

        [SerializeField, StatePassive]
        [Tooltip("When true, text can be selected and copied but not edited.")]
        private bool readOnly;

        [NonSerialized] private bool arrowKeyEscapesFormatting;

        private const float CaretScrollMargin = 5f;

        /// <summary>Local input behaviors in hook order.</summary>
        [SerializeField, StateList(nameof(ApplyLocalBehaviorsChange), Name = nameof(Behaviors),
            Owned = true, AllowNullItems = false, AllowDuplicateReferences = false)]
        private TypedList<InputBehavior> behaviors = new();

        /// <summary>Shared behavior presets applied after local behaviors.</summary>
        [SerializeField, StateList(nameof(ApplyBehaviorPresetsChange), AllowNullItems = false,
            AllowDuplicateReferences = false)]
        [Tooltip("Shared behavior sets (project assets) applied on top of the local Behaviors list. Each editor runs its own runtime copy, so per-instance behavior state never touches the asset.")]
        private StyledList<InputBehaviorPreset> behaviorPresets = new();

        private readonly List<(InputBehaviorPreset source, InputBehaviorPreset copy)>
            runtimeBehaviorPresets = new();
        private ReferenceBinding<InputBehaviorPreset> subscribedBehaviorPresets;

        /// <summary>The locals actually enabled — play-mode inspector edits can drop elements from
        /// <see cref="behaviors"/> before their subscriptions are unwound.</summary>
        private readonly List<InputBehavior> enabledLocalBehaviors = new();
        private readonly List<InputBehavior> desiredLocalBehaviors = new();

#pragma warning disable 414
        private bool behaviorsLive;
#pragma warning restore 414

        private OrderedEvent<InputEdit> inputFilter;
        private OrderedEvent<KeyResolve> keyResolver;
        private OrderedEvent<KeyboardRequest> keyboardResolver;

        /// <summary>
        /// Hook: veto or rewrite any document mutation before it applies — typing, paste, IME commit,
        /// deletions, cut, transpose. Not consulted for in-progress IME composition, undo/redo replay, or
        /// programmatic <see cref="Text"/> writes. Hooks run by ascending order, each seeing the
        /// previous one's result; equal orders retain subscription order and
        /// <see cref="InputEdit.Rejected"/> is sticky. Behaviors subscribe in their <c>OnEnable</c>.
        /// </summary>
        public OrderedEvent<InputEdit> InputFilter
            => inputFilter ??= new OrderedEvent<InputEdit>();

        /// <summary>
        /// Ordered hook for contributing a key-to-action binding before the platform key map.
        /// Lower orders resolve first; after one sets an action, later hooks must preserve it.
        /// </summary>
        public OrderedEvent<KeyResolve> KeyResolver
            => keyResolver ??= new OrderedEvent<KeyResolve>();

        /// <summary>Hook: consume media — a paste, a drag-and-drop, or a picker selection — before the text-adapter pipeline. Behaviors subscribe in their <c>OnEnable</c>; see <see cref="MediaInputBehavior"/>.</summary>
        public event MediaReceivedHook MediaReceived;

        /// <summary>
        /// Ordered hook for composing keyboard configuration each time the soft keyboard
        /// is shown. Lower orders contribute first; equal orders retain subscription order.
        /// </summary>
        public OrderedEvent<KeyboardRequest> KeyboardResolver
            => keyboardResolver ??= new OrderedEvent<KeyboardRequest>();

        internal void ResolveKeyboardRequest(ref KeyboardRequest request)
            => keyboardResolver?.Invoke(ref request);

        private Action<float> frameTicked;

        internal event Action<float> FrameTicked
        {
            add { frameTicked += value; Enroll(); }
            remove { frameTicked -= value; }
        }

        internal int displayMaskCodepoint;
        private bool copyBlocked;
        private int secureInputCount;

        internal bool CopyBlocked
        {
            get => copyBlocked;
            set
            {
                if (copyBlocked == value) return;
                copyBlocked = value;
                NotifyInputSessionConfigurationChanged();
            }
        }

        internal bool SecureInput => secureInputCount != 0;

        internal void RequireSecureInput()
        {
            if (secureInputCount++ == 0)
                NotifyInputSessionConfigurationChanged();
        }

        internal void ReleaseSecureInput()
        {
            if (secureInputCount == 0)
                throw new InvalidOperationException("Secure input requirements are unbalanced.");
            if (--secureInputCount == 0)
                NotifyInputSessionConfigurationChanged();
        }

        private int newlineSuppressionCount;

        /// <summary>
        /// Length cap published by the enforcing behavior (<see cref="LengthLimitBehavior"/>) for
        /// counters to display; <see cref="LengthLimit.Max"/> 0 = none. Read-only for consumers —
        /// enforcement and configuration live on the behavior, and it republishes on every edit,
        /// so an external write here would neither cap input nor survive.
        /// </summary>
        public LengthLimit LengthLimit { get; internal set; }

        private UniTextSelectable selectableCache;

        /// <summary>
        /// Sibling selection component on the same GameObject. Resolved once via
        /// <see cref="GetComponent{T}"/>; <see cref="RequireComponent"/> guarantees presence.
        /// </summary>
        public UniTextSelectable Selectable
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (selectableCache == null) selectableCache = GetComponent<UniTextSelectable>();
                return selectableCache;
            }
        }
        
        /// <summary>Current caret or selection range.</summary>
        public TextSelection Selection => Selectable.Selection;
        /// <summary>Text component whose document this editor mutates.</summary>
        public UniTextBase TextComponent => Selectable.TextComponent;

        private AttributedDocument document;
        private GapBuffer ViewText { get; set; }
        internal (object identity, int version) TextStorageStamp
            => (ViewText, ViewText?.Version ?? 0);
        internal UndoStack undoStack;
        private readonly List<SourceAnnotation> importAnnotations = new();
        private readonly List<AttributeParser.MarkupMatch> importMatches = new();
        private readonly List<AttributeSpan> renderAttributeSpans = new();
        private readonly List<(int start, int end)> renderProtectionRanges = new();
        private readonly List<SourceAnnotation> renderAnnotationScratch = new();
        private int[] sourceViewToDocument = { 0 };
        private int[] documentToSourceViewInsertion = { 0 };

        internal bool documentChanged;
        internal bool isComposing;
        internal CompositionOverlay compositionOverlay;
        internal int codepointCount;
        internal bool isActive;

        private bool textDirtyFlag;
        private bool selectionDirtyFlag;

        internal bool textDirty
        {
            get => textDirtyFlag;
            set { textDirtyFlag = value; if (value) Enroll(); }
        }

        internal bool selectionDirty
        {
            get => selectionDirtyFlag;
            set { selectionDirtyFlag = value; if (value) Enroll(); }
        }

        internal int RenderedCodepointCount => isComposing
            ? codepointCount + compositionOverlay.codepointCount
            : HasMarkupView ? markupViewMap.VisibleLength : codepointCount;

        internal int GetVisibleCaretCodepoint()
        {
            if (!isComposing) return DocumentToRendered(Selection.Focus);
            return compositionOverlay.insertionCodepoint + compositionOverlay.CursorCodepointOffset();
        }

        internal void MarkDocumentChanged(string reason)
        {
            codepointCount = ViewText.CodepointCount;
            reason ??= TextChangeReason.Programmatic;
            textDirty = true;
            var keepPending = documentChanged
                && reason == StyleEditReason
                && pendingChangeReason != null
                && pendingChangeReason.StartsWith("input.", StringComparison.Ordinal);
            if (!keepPending) pendingChangeReason = reason;
            documentChanged = true;
            desiredX = float.NaN;
        }

        /// <summary>Occurs synchronously after a committed edit and before the frame-coalesced <see cref="TextChanged"/> event.</summary>
        public event Action<EditShape> EditApplied;

        private void ApplyShapeToDerivedState(in EditShape shape)
            => ApplyShapeToDerivedState(in shape, in shape);

        private void ApplyShapeToDerivedState(in EditShape viewShape, in EditShape documentShape)
        {
            codepointCount = ViewText.CodepointCount;
            if (viewShape.Removed != 0 || viewShape.Inserted != 0)
            {
                textMutationVersion++;
                lastTextMutationStamp = TextStorageStamp;
                lastTextMutationShape = viewShape;
            }
            documentVersion++;
            TextComponent.DocumentEdited?.Invoke(documentShape.Start, documentShape.Removed, documentShape.Inserted);
            EditApplied?.Invoke(viewShape);
        }

        private string pendingChangeReason;
        private int documentVersion;
        private int textMutationVersion;
        private (object identity, int version) lastTextMutationStamp;
        private EditShape lastTextMutationShape;

        private bool IsExactTextMutation(int version, int start, int removed, int inserted,
            TextSelection selection)
            => textMutationVersion == unchecked(version + 1) &&
               TextStorageStamp == lastTextMutationStamp &&
               lastTextMutationShape.Start == start &&
               lastTextMutationShape.Removed == removed &&
               lastTextMutationShape.Inserted == inserted &&
               Selection == selection;

        /// <summary>Number of codepoints in the currently navigable editing view: visible document in Hidden, synthesized source in Raw or Reveal.</summary>
        public int CodepointCount => codepointCount;

        /// <summary>Number of UTF-16 units in the currently navigable editing view.</summary>
        public int CharCount => ViewText?.Length ?? 0;
        /// <summary>Monotonic document version incremented after committed edits.</summary>
        public int Version => documentVersion;

        internal bool IsCharBoundary(int charIndex)
        {
            var text = ViewText;
            if (text == null) return charIndex == 0;
            return (uint)charIndex <= (uint)text.Length &&
                   text.SnapToPairBoundary(charIndex) == charIndex;
        }

        int ITextDocument.CopyCodepointRange(int startCodepoint, int codepointCount, Span<char> destination)
            => ViewText?.CopyCodepointRange(startCodepoint, codepointCount, destination) ?? 0;

        int ITextDocument.GetCodepointAt(int codepointIndex)
        {
            if (ViewText == null) return 0;
            if ((uint)codepointIndex >= (uint)codepointCount) return 0;
            int charIdx = ViewText.CodepointToCharIndex(codepointIndex);
            int before = ViewText.BeforeGap.Length;
            char high = charIdx < before
                ? ViewText.BeforeGap[charIdx]
                : ViewText.AfterGap[charIdx - before];
            if (!char.IsHighSurrogate(high)) return high;
            int nextChar = charIdx + 1;
            if (nextChar >= ViewText.Length) return high;
            char low = nextChar < before
                ? ViewText.BeforeGap[nextChar]
                : ViewText.AfterGap[nextChar - before];
            return char.IsLowSurrogate(low) ? char.ConvertToUtf32(high, low) : high;
        }


        private char[] syncBuffer = Array.Empty<char>();

        internal bool HasLayout
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => TextComponent.Buffers != null;
        }

        /// <summary>
        /// Serialized source representation of the attributed document. Setting imports configured markup;
        /// getting preserves the imported source byte-for-byte until an edit and otherwise exports current spans.
        /// </summary>
        public string Text
        {
            get => document?.IsInitialized == true ? document.SourceText : (ViewText?.ToString() ?? string.Empty);
            set => SetText(value);
        }

        /// <summary>Visible document text without serialized markup, independent of the current markup presentation mode.</summary>
        public string VisibleText => document != null ? document.Text.ToString() : string.Empty;

        /// <summary>
        /// Copies the currently navigable editing view into a caller-provided buffer. Returns the number of
        /// UTF-16 units written without allocating; use <see cref="Text"/> for serialized markup.
        /// </summary>
        public int CopyTextTo(Span<char> destination)
        {
            return ViewText != null ? ViewText.CopyTo(0, ViewText.Length, destination) : 0;
        }

        /// <summary>Compares the currently navigable editing view to a known string without allocation.</summary>
        public bool TextEquals(ReadOnlySpan<char> other)
        {
            if (ViewText == null) return other.Length == 0;
            if (ViewText.Length != other.Length) return false;

            var before = ViewText.BeforeGap;
            if (!other.Slice(0, before.Length).SequenceEqual(before)) return false;

            var after = ViewText.AfterGap;
            return other.Slice(before.Length).SequenceEqual(after);
        }

        /// <summary>Compares the currently navigable editing view to a known string without allocation.</summary>
        public bool TextEquals(string other) => TextEquals(other.AsSpan());

        /// <summary>Caret position (codepoint index). Equivalent to <c>Selection.Focus</c>.</summary>
        public int CaretPosition
        {
            get => Selection.Focus;
            set => MoveCaretTo(value);
        }

        /// <summary>Selection start (codepoint index, inclusive).</summary>
        public int SelectionStart => Selection.Start;

        /// <summary>Selection end (codepoint index, exclusive).</summary>
        public int SelectionEnd => Selection.End;

        /// <summary>
        /// Currently selected text. Allocates a string. Empty when no selection.
        /// </summary>
        public string SelectedText
        {
            get
            {
                var sel = Selection;
                if (sel.IsCollapsed || ViewText == null) return string.Empty;
                return ViewText.GetCodepointRange(sel.Start, sel.Length);
            }
        }

        /// <summary>Read-only mode. Text can be selected and copied but not edited.</summary>
        public bool ReadOnly
        {
            get => readOnly;
            set
            {
                if (readOnly == value) return;
                readOnly = value;
                NotifyInputSessionConfigurationChanged();
            }
        }

        /// <summary>Whether a repeated outward arrow at a formatting boundary clears an explicitly retained typing context. Off unless published by <see cref="TextFormattingBehavior"/>.</summary>
        public bool ArrowKeyEscapesFormatting { get => arrowKeyEscapesFormatting; set => arrowKeyEscapesFormatting = value; }

        private void ApplyCaretRendererChange(InputCaretRenderer previous, InputCaretRenderer current)
        {
            if (previous != null && previous != current) previous.enabled = false;
            selectionDirty = true;
        }

        /// <summary>
        /// Occurs when document text or attributed ranges change. Read the navigable view without allocation
        /// through <see cref="CharCount"/>, <see cref="CopyTextTo"/>, and <see cref="TextEquals(string)"/>;
        /// read serialized markup through <see cref="Text"/>.
        /// </summary>
        public event Action TextChanged;

        /// <summary>
        /// Occurs when document text or attributed ranges change. Provides the full serialized source string
        /// and allocates only when subscribed.
        /// </summary>
        public event Action<string> ValueChanged;

        /// <summary>
        /// Occurs when the text content has changed, alongside <see cref="TextChanged"/>, with the originating
        /// <see cref="TextChangeReason"/> string — distinguishes user typing
        /// (<c>input.type</c>) from paste (<c>input.paste</c>), programmatic mutation
        /// (<c>program.set</c>), IME commit (<c>input.type.compose</c>), and
        /// undo/redo replay (<c>input.restore</c>). Everything under <c>input.*</c> is
        /// user-originated.
        /// </summary>
        public event Action<string> DocumentChanged;

        /// <summary>
        /// Occurs when the user has submitted. Enter inserts a newline by default — a submit-resolving
        /// behavior defines what submits: <see cref="SingleLineBehavior"/> (Enter, form field) or
        /// <see cref="SubmitKeyBehavior"/> (Enter or Ctrl/Cmd+Enter, composer). Gamepad submit
        /// (EventSystem) also fires this. Carries the current text snapshot. Post-submit focus is
        /// the submit-defining behavior's policy — both carry <c>KeepFocusOnSubmit</c> with
        /// archetype defaults: <see cref="SingleLineBehavior"/> releases focus (form),
        /// <see cref="SubmitKeyBehavior"/> keeps it (composer); without one, focus stays.
        /// </summary>
        public event Action<string> Submitted;

        /// <summary>
        /// Occurs when the user has cancelled editing — Escape (desktop), system back (mobile).
        /// The editor does not deactivate automatically; add <see cref="DefocusOnCancelBehavior"/> or
        /// subscribe directly when cancellation should release focus. Android delivers the back key
        /// only where the platform routes it to the app: an app opted into OnBackInvokedCallback
        /// dispatch (Unity's Predictive Back Support), and Android 13/14 while the keyboard is
        /// showing, dismiss the keyboard without raising this.
        /// </summary>
        public event Action Cancelled;

        /// <summary>Occurs when the editor has gained input focus (activated) — by pointer, navigation, or programmatic select.</summary>
        public event Action Focused;

        /// <summary>
        /// Occurs when the editor has lost input focus (deactivated), carrying how the session
        /// concluded. <see cref="Submitted"/> and <see cref="Cancelled"/> report the user's intent
        /// while editing continues; this reports the end itself.
        /// </summary>
        public event Action<EditingEndReason> Defocused;

        /// <summary>
        /// Frame-coalesced selection-change notification. Parameters: (selectionStart,
        /// selectionEnd) in codepoint indices. Fires at most once per frame after
        /// <see cref="UniTextSelectable.SelectionChanged"/> mutations have settled.
        /// </summary>
        public event Action<int, int> SelectionChanged;

        /// <summary>Occurs when IME composition state changes — fires <see langword="true"/> on start, <see langword="false"/> on commit or cancel.</summary>
        public event Action<bool> CompositionStateChanged;

        internal event Action<bool> CompositionStateTransitioning;
        internal event Action InputSessionConfigurationChanged;
        internal event Action InputGeometryChanged;

        internal void NotifyInputSessionConfigurationChanged()
            => InputSessionConfigurationChanged?.Invoke();

        private void Awake()
        {
            EnsureInitialized();
        }

        internal void EnsureInitialized()
        {
            if (document != null) return;
            document = new AttributedDocument(64);
            ViewText = document.Text;
            undoStack = new UndoStack();
        }

        private void OnStyleGraphChanged()
        {
            EndCompositionBeforeDocumentMutation();
            DiscardTypingStyles();
            if (HasMarkupView) ExitMarkupView();
            InvalidateMarkupViewMatches();
            var parser = TextComponent.AttributeParser;
            if (parser != null && parser.LiteralEscapeRule() == null)
                parser.Register(new Style { Source = new BackslashEscapeRule() });
            var source = document.IsInitialized ? document.SourceText : TextComponent.Text;
            ImportMarkup(source, parser);
            if (markupVisibility != MarkupVisibility.Hidden) EnterMarkupView();
            Enroll();
        }

        private EditShape ImportMarkup(string source, AttributeParser parser = null)
        {
            parser ??= TextComponent?.AttributeParser;
            source = UnicodeData.NormalizeNewlines(source ?? string.Empty);
            EditShape shape;
            if (parser != null)
            {
                var visible = AttributedDocumentMarkup.Import(parser, source, importAnnotations, importMatches,
                    out sourceViewToDocument, out documentToSourceViewInsertion);
                shape = document.Set(visible.AsSpan(), importAnnotations, source);
            }
            else
            {
                sourceViewToDocument = new[] { 0 };
                documentToSourceViewInsertion = new[] { 0 };
                shape = document.Set(source.AsSpan(), null, source);
            }
            codepointCount = ViewText.CodepointCount;
            textDirty = true;
            return shape;
        }

        /// <summary>The built-in clipboard adapter set, ordered highest-fidelity first.</summary>
        internal IReadOnlyList<IClipboardAdapter> ActiveAdapters => ClipboardAdapterDefaults.All;

        private void OnEnable()
        {
            if (disablePending)
            {
                disablePending = false;
                return;
            }
            if (behaviors == null) SetBehaviorsState(new TypedList<InputBehavior>());
            if (behaviorPresets == null) SetBehaviorPresetsState(new StyledList<InputBehaviorPreset>());
            EnsureInitialized();

            if (TextComponent == null)
            {
                Debug.LogError(
                    $"[UniTextEditable] '{gameObject.name}': UniTextBase missing on the same GameObject. " +
                    "Editor disabled.", this);
                enabled = false;
                return;
            }

            textDirty = true;
            TextComponent.IsDocumentHost = true;
            TextComponent.MeasureTrailingWhitespace = true;
            TextComponent.StyleGraphChanged += OnStyleGraphChanged;
            if (!TextComponent.EnsureAttributeParserCreated())
                OnStyleGraphChanged();

            HookSweep();
#if UNITY_EDITOR
            editModeEditables.Remove(this);
            if (!Application.isPlaying) editModeEditables.Add(this);
#endif
            Enroll();
            TextComponent.Committed += OnCommitted;
            SubscribeTextEvents();
            UniTextFocusable.Sync(gameObject);

            TextComponent.DocumentToRendered = DocumentToRendered;

            if (Selectable != null)
            {
                Selectable.editingLayerActive = true;
                Selectable.copyOverride = Copy;
                Selectable.document = this;
                Selectable.visibleToSource = RenderedToDocument;
                Selectable.SelectionChanged += OnSelectableSelectionChanged;
            }

            InitializeTouch();
            EnableBehaviors();
            SyncChromeModifiers();
        }

        private void OnDisable()
        {
            if (isActive)
            {
                disablePending = true;
                Deactivate();
                return;
            }
            CompleteDisable();
        }

        private void CompleteDisable()
        {
            DiscardTypingStyles();
            if (HasMarkupView)
                ExitMarkupView();
            Unenroll();
#if UNITY_EDITOR
            editModeEditables.Remove(this);
#endif
            if (TextComponent != null)
            {
                TextComponent.IsDocumentHost = false;
                TextComponent.MeasureTrailingWhitespace = false;
                TextComponent.Projection = default;
                TextComponent.DocumentToRendered = null;
                TextComponent.Committed -= OnCommitted;
                TextComponent.StyleGraphChanged -= OnStyleGraphChanged;
            }
            UnsubscribeTextEvents();

            if (Selectable != null)
            {
                Selectable.editingLayerActive = false;
                Selectable.copyOverride = null;
                Selectable.document = null;
                Selectable.visibleToSource = null;
                Selectable.SelectionChanged -= OnSelectableSelectionChanged;
            }

            UniTextFocusable.Sync(gameObject);
            DisableBehaviors();
            ReleaseChromeModifiers();
            TeardownTouch();
        }

        private void ApplyLocalBehaviorsChange(
            in StateListMutation<InputBehavior> mutation)
        {
            if (!behaviorsLive) return;
            ReconcileLocalBehaviors(FirstAffectedIndex(in mutation));
        }

        private void ApplyBehaviorPresetsChange(
            in StateListMutation<InputBehaviorPreset> mutation)
        {
            if (!behaviorsLive) return;
            ReconcileBehaviorPresetCopies();
        }

        private static int FirstAffectedIndex(
            in StateListMutation<InputBehavior> mutation)
            => mutation.Kind switch
            {
                StateListMutationKind.Add => mutation.PreviousCount,
                StateListMutationKind.Move => Math.Min(mutation.Index,
                    mutation.DestinationIndex),
                StateListMutationKind.Clear or StateListMutationKind.ReplaceAll => 0,
                _ => mutation.Index
            };

        private void EnableBehaviors()
        {
            ValidateBehaviors();
            ValidateBehaviorPresets();
            ReconcileLocalBehaviors();
            CreateBehaviorPresetCopies();
            behaviorsLive = true;
        }

        private void DisableBehaviors()
        {
            for (int i = enabledLocalBehaviors.Count - 1; i >= 0; i--)
            {
                var behavior = enabledLocalBehaviors[i];
                behavior.Disable();
                behavior.SetOwner(null);
            }
            enabledLocalBehaviors.Clear();
            desiredLocalBehaviors.Clear();
            DestroyBehaviorPresetCopies();
            behaviorsLive = false;
        }

        private void ReconcileLocalBehaviors(int forcedPrefix = int.MaxValue)
        {
            desiredLocalBehaviors.Clear();
            for (var i = 0; i < behaviors.Count; i++)
            {
                var behavior = behaviors[i];
                desiredLocalBehaviors.Add(behavior);
            }

            var prefix = 0;
            var shared = Math.Min(enabledLocalBehaviors.Count, desiredLocalBehaviors.Count);
            while (prefix < shared &&
                   ReferenceEquals(enabledLocalBehaviors[prefix], desiredLocalBehaviors[prefix]))
                prefix++;
            prefix = Math.Min(prefix, forcedPrefix);

            for (var i = enabledLocalBehaviors.Count - 1; i >= prefix; i--)
            {
                var behavior = enabledLocalBehaviors[i];
                behavior.Disable();
                behavior.SetOwner(null);
                enabledLocalBehaviors.RemoveAt(i);
            }

            for (var i = prefix; i < desiredLocalBehaviors.Count; i++)
            {
                var behavior = desiredLocalBehaviors[i];
                behavior.SetOwner(this, this);
                behavior.Enable();
                enabledLocalBehaviors.Add(behavior);
            }
        }

        private void CreateBehaviorPresetCopies()
        {
            for (var i = 0; i < behaviorPresets.Count; i++)
                runtimeBehaviorPresets.Add((behaviorPresets[i],
                    CreateBehaviorPresetCopy(behaviorPresets[i])));
            EnableBehaviorPresetCopiesFrom(0);
            RefreshBehaviorPresetSubscriptions();
        }

        private void EnableCopyBehaviors(InputBehaviorPreset copy)
        {
            var presetBehaviors = copy.Behaviors;
            for (int j = 0; j < presetBehaviors.Count; j++)
            {
                var behavior = presetBehaviors[j];
                behavior.SetOwner(this, this);
                behavior.Enable();
            }
        }

        private void DestroyBehaviorPresetCopies()
        {
            DisableBehaviorPresetCopiesFrom(0);
            for (var i = runtimeBehaviorPresets.Count - 1; i >= 0; i--)
                RemoveBehaviorPresetCopyAt(i);

            subscribedBehaviorPresets?.Clear();
        }

        private void OnBehaviorPresetChanged(InputBehaviorPreset preset,
            in InputBehaviorPresetChange change)
        {
            if (!behaviorsLive) return;
            var presetIndex = FindBehaviorPresetBinding(preset);
            if (presetIndex < 0) return;
            if (TryReplayBehaviorPresetChange(presetIndex, preset, in change)) return;
            if (TryReconcileBehaviorPresetSuffix(presetIndex, preset, in change)) return;
            var replacement = CreateBehaviorPresetCopy(preset);
            DisableBehaviorPresetCopiesFrom(presetIndex);
            var binding = runtimeBehaviorPresets[presetIndex];
            var previous = binding.copy;
            runtimeBehaviorPresets[presetIndex] = (binding.source, replacement);
            ReleaseBehaviorPresetCopy(previous);
            EnableBehaviorPresetCopiesFrom(presetIndex);
        }

        private bool TryReplayBehaviorPresetChange(int presetIndex,
            InputBehaviorPreset preset, in InputBehaviorPresetChange change)
        {
            if (change.Structural || change.Behavior == null || change.Source == null ||
                !change.Member.IsValid)
                return false;
            var behaviorIndex = ReferenceIdentity.IndexOf(preset.Behaviors,
                change.Behavior);
            var copy = runtimeBehaviorPresets[presetIndex].copy;
            if ((uint)behaviorIndex >= (uint)copy.Behaviors.Count) return false;
            var targetBehavior = copy.Behaviors[behaviorIndex];
            if (targetBehavior is IStateMemberReplay target &&
                target.ReplayStateMember(change.Source, change.Member))
                return true;
            return targetBehavior.ReplayNestedStateMember(change.Behavior,
                change.Source, change.Member);
        }

        private bool TryReconcileBehaviorPresetSuffix(int presetIndex,
            InputBehaviorPreset preset, in InputBehaviorPresetChange change)
        {
            if (!change.Structural) return false;
            var first = change.Kind switch
            {
                StateChangeKind.Value => ReferenceIdentity.IndexOf(preset.Behaviors,
                    change.Behavior),
                StateChangeKind.Add or StateChangeKind.Insert or StateChangeKind.Replace or
                    StateChangeKind.Remove => change.Index,
                StateChangeKind.Move => Math.Min(change.Index, change.DestinationIndex),
                StateChangeKind.Clear or StateChangeKind.ReplaceAll => 0,
                _ => -1
            };
            var current = runtimeBehaviorPresets[presetIndex].copy;
            if ((uint)first > (uint)current.Behaviors.Count ||
                (uint)first > (uint)preset.Behaviors.Count)
                return false;

            var replacement = CreateBehaviorPresetCopy(preset);
            try
            {
                var replacementBehaviors = replacement.DetachRuntimeBehaviors();
                if (replacementBehaviors.Length != preset.Behaviors.Count)
                    throw new InvalidOperationException(
                        "A runtime input behavior preset copy did not preserve its source topology.");

                var merged = new InputBehavior[replacementBehaviors.Length];
                for (var i = 0; i < first; i++) merged[i] = current.Behaviors[i];
                for (var i = first; i < merged.Length; i++)
                    merged[i] = replacementBehaviors[i];

                DisableBehaviorPresetSuffix(presetIndex, first);
                for (var i = current.Behaviors.Count - 1; i >= first; i--)
                    current.Behaviors[i].SetOwner(null);
                current.ReplaceRuntimeBehaviors(merged);
                EnableBehaviorPresetSuffix(presetIndex, first);
                return true;
            }
            finally
            {
                ObjectUtils.SafeDestroy(replacement);
            }
        }

        private void ReconcileBehaviorPresetCopies()
        {
            var prefix = 0;
            var shared = Math.Min(runtimeBehaviorPresets.Count, behaviorPresets.Count);
            while (prefix < shared &&
                   ReferenceEquals(runtimeBehaviorPresets[prefix].source,
                       behaviorPresets[prefix]))
                prefix++;
            if (prefix == runtimeBehaviorPresets.Count && prefix == behaviorPresets.Count)
                return;
            DisableBehaviorPresetCopiesFrom(prefix);
            ReconcileBehaviorPresetCopyOrder(prefix);
            EnableBehaviorPresetCopiesFrom(prefix);
            RefreshBehaviorPresetSubscriptions();
        }

        private void ReconcileBehaviorPresetCopyOrder(int firstChangedIndex)
        {
            for (var targetIndex = firstChangedIndex; targetIndex < behaviorPresets.Count; targetIndex++)
            {
                var source = behaviorPresets[targetIndex];
                if (targetIndex < runtimeBehaviorPresets.Count &&
                    ReferenceEquals(runtimeBehaviorPresets[targetIndex].source, source))
                    continue;

                var existingIndex = targetIndex + 1;
                while (existingIndex < runtimeBehaviorPresets.Count &&
                       !ReferenceEquals(runtimeBehaviorPresets[existingIndex].source, source))
                    existingIndex++;

                if (existingIndex < runtimeBehaviorPresets.Count)
                {
                    var existing = runtimeBehaviorPresets[existingIndex];
                    runtimeBehaviorPresets.RemoveAt(existingIndex);
                    runtimeBehaviorPresets.Insert(targetIndex, existing);
                    continue;
                }

                runtimeBehaviorPresets.Insert(targetIndex,
                    (source, CreateBehaviorPresetCopy(source)));
            }

            while (runtimeBehaviorPresets.Count > behaviorPresets.Count)
                RemoveBehaviorPresetCopyAt(runtimeBehaviorPresets.Count - 1);
        }

        private static InputBehaviorPreset CreateBehaviorPresetCopy(InputBehaviorPreset source)
        {
            var copy = Instantiate(source);
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.PrepareRuntimeCopyForEditable();
            return copy;
        }

        private void DisableBehaviorPresetCopiesFrom(int index)
        {
            for (var i = runtimeBehaviorPresets.Count - 1; i >= index; i--)
            {
                var copy = runtimeBehaviorPresets[i].copy;
                var presetBehaviors = copy.Behaviors;
                for (var j = presetBehaviors.Count - 1; j >= 0; j--)
                    presetBehaviors[j].Disable();
            }
        }

        private void EnableBehaviorPresetCopiesFrom(int index)
        {
            for (var i = index; i < runtimeBehaviorPresets.Count; i++)
                EnableCopyBehaviors(runtimeBehaviorPresets[i].copy);
        }

        private void RemoveBehaviorPresetCopyAt(int index)
        {
            ReleaseBehaviorPresetCopy(runtimeBehaviorPresets[index].copy);
            runtimeBehaviorPresets.RemoveAt(index);
        }

        private int FindBehaviorPresetBinding(InputBehaviorPreset source)
        {
            for (var i = 0; i < runtimeBehaviorPresets.Count; i++)
                if (ReferenceEquals(runtimeBehaviorPresets[i].source, source)) return i;
            return -1;
        }

        private static void ReleaseBehaviorPresetCopy(InputBehaviorPreset copy)
        {
            var presetBehaviors = copy.Behaviors;
            for (var i = presetBehaviors.Count - 1; i >= 0; i--)
            {
                var behavior = presetBehaviors[i];
                behavior.SetOwner(null);
            }
            ObjectUtils.SafeDestroy(copy);
        }

        private void RefreshBehaviorPresetSubscriptions()
        {
            subscribedBehaviorPresets ??= new ReferenceBinding<InputBehaviorPreset>(
                SubscribeBehaviorPreset, UnsubscribeBehaviorPreset);
            subscribedBehaviorPresets.Reconcile(behaviorPresets);
        }

        private void SubscribeBehaviorPreset(InputBehaviorPreset preset)
            => preset.DeltaChanged += OnBehaviorPresetChanged;

        private void UnsubscribeBehaviorPreset(InputBehaviorPreset preset)
            => preset.DeltaChanged -= OnBehaviorPresetChanged;

        private void ValidateBehaviorPresets()
            => StateListRules.ValidateReferences(behaviorPresets, false, false,
                nameof(behaviorPresets));

        private void ValidateBehaviors()
            => StateListRules.ValidateReferences(behaviors, false, false, nameof(behaviors));

        void IInputBehaviorChangeSink.MarkInputBehaviorChanged(InputBehavior behavior,
            IStateMemberReplay source, StateMember member, bool structural)
        {
            if (!behaviorsLive) return;
            var index = ReferenceIdentity.IndexOf(enabledLocalBehaviors, behavior);
            if (index >= 0)
            {
                ReconcileLocalBehaviors(index);
                return;
            }

            for (var presetIndex = 0; presetIndex < runtimeBehaviorPresets.Count; presetIndex++)
            {
                var copy = runtimeBehaviorPresets[presetIndex].copy;
                var behaviorIndex = ReferenceIdentity.IndexOf(copy.Behaviors, behavior);
                if (behaviorIndex < 0) continue;
                RestartBehaviorPresetSuffix(presetIndex, behaviorIndex);
                return;
            }
        }

        private void RestartBehaviorPresetSuffix(int presetIndex, int behaviorIndex)
        {
            DisableBehaviorPresetSuffix(presetIndex, behaviorIndex);
            EnableBehaviorPresetSuffix(presetIndex, behaviorIndex);
        }

        private void DisableBehaviorPresetSuffix(int presetIndex, int behaviorIndex)
        {
            for (var i = runtimeBehaviorPresets.Count - 1; i >= presetIndex; i--)
            {
                var copy = runtimeBehaviorPresets[i].copy;
                var presetBehaviors = copy.Behaviors;
                var first = i == presetIndex ? behaviorIndex : 0;
                for (var j = presetBehaviors.Count - 1; j >= first; j--)
                    presetBehaviors[j].Disable();
            }
        }

        private void EnableBehaviorPresetSuffix(int presetIndex, int behaviorIndex)
        {
            for (var i = presetIndex; i < runtimeBehaviorPresets.Count; i++)
            {
                var copy = runtimeBehaviorPresets[i].copy;
                var presetBehaviors = copy.Behaviors;
                var first = i == presetIndex ? behaviorIndex : 0;
                for (var j = first; j < presetBehaviors.Count; j++)
                {
                    var current = presetBehaviors[j];
                    current.SetOwner(this, this);
                    current.Enable();
                }
            }
        }

        /// <summary>Returns the first attached behavior of type <typeparamref name="T"/>, or <see langword="null"/>.</summary>
        public T GetBehavior<T>() where T : InputBehavior
        {
            for (int i = 0; i < behaviors.Count; i++)
                if (behaviors[i] is T match) return match;
            for (int i = 0; i < runtimeBehaviorPresets.Count; i++)
            {
                var copy = runtimeBehaviorPresets[i].copy;
                var presetBehaviors = copy.Behaviors;
                for (int j = 0; j < presetBehaviors.Count; j++)
                    if (presetBehaviors[j] is T match) return match;
            }
            return null;
        }

        /// <summary>Appends every attached behavior of type <typeparamref name="T"/> (local then preset) to <paramref name="results"/>.</summary>
        public void GetBehaviors<T>(List<T> results) where T : InputBehavior
        {
            for (int i = 0; i < behaviors.Count; i++)
                if (behaviors[i] is T match) results.Add(match);
            for (int i = 0; i < runtimeBehaviorPresets.Count; i++)
            {
                var copy = runtimeBehaviorPresets[i].copy;
                var presetBehaviors = copy.Behaviors;
                for (int j = 0; j < presetBehaviors.Count; j++)
                    if (presetBehaviors[j] is T match) results.Add(match);
            }
        }

        internal bool ResolveDisplayMask(out int maskCodepoint)
        {
            maskCodepoint = displayMaskCodepoint;
            return displayMaskCodepoint != 0;
        }

        internal bool IsCopyAllowed() => !copyBlocked;

        private void OnSelectableSelectionChanged(SelectionChangedArgs args)
        {
            selectionDirty = true;
            InputGeometryChanged?.Invoke();
            if (markupVisibility == MarkupVisibility.RevealActiveRange && TextComponent != null
                && RevealWindowChanged())
                TextComponent.SetDirty(UniTextDirty.Text);
            if (caretRenderer != null) caretRenderer.ResetBlink();
            if (args.UserEvent != SelectionChangeReason.Input
                && args.UserEvent != SelectionChangeReason.Normalize)
                DiscardTypingStyles();
        }

        private void SetComposing(bool value)
        {
            if (isComposing == value) return;
            isComposing = value;
            NotifyCompositionStateChanged(value);
        }

        private void NotifyCompositionStateChanged(bool value)
        {
            CompositionStateTransitioning?.Invoke(value);
            CompositionStateChanged?.Invoke(value);
        }

        /// <summary>True while the editor is active, including while its input source is completing release.</summary>
        public bool IsActive => isActive;

        /// <summary>True while an IME composition (marked text) is in progress.</summary>
        public bool IsComposing => isComposing;

        private static readonly List<UniTextEditable> enrolled = new();
#if UNITY_EDITOR
        private static readonly List<UniTextEditable> editModeEditables = new();
#endif
        private static bool sweepHooked;
        private bool isEnrolled;

        private static void HookSweep()
        {
            if (sweepHooked) return;
            sweepHooked = true;
            UniTextBase.ProcessingStarted += ProcessEnrolled;
        }

        /// <summary>
        /// Registers this editable for the per-frame sweep. Called by every frame-work flag
        /// setter and by <see cref="Activate"/>; idempotent and O(1). Idle editables are not
        /// on the list and pay nothing while any other UniText in the scene processes.
        /// </summary>
        internal void Enroll()
        {
            if (isEnrolled || !isActiveAndEnabled) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif
            isEnrolled = true;
            enrolled.Add(this);
        }

        private void Unenroll()
        {
            if (!isEnrolled) return;
            isEnrolled = false;
            enrolled.Remove(this);
        }

        private static readonly List<UniTextEditable> sweepScratch = new();

        /// <summary>
        /// One static sweep subscriber for the whole editing layer: ticks only the editables
        /// with pending frame work.
        /// In edit mode enrollment cannot be relied on (no focus/input) — every enabled
        /// editable is processed instead, which keeps Inspector text edits reconciled.
        /// <see cref="ProcessFrame"/> raises public events whose handlers may Enroll/Unenroll
        /// (focus changes, <c>enabled</c> flips), so the sweep walks a snapshot — the live list
        /// is never indexed while it can be mutated under us.
        /// </summary>
        private static void ProcessEnrolled()
        {
            sweepScratch.Clear();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                sweepScratch.AddRange(editModeEditables);
                for (var i = 0; i < sweepScratch.Count; i++)
                {
                    var editable = sweepScratch[i];
                    if (editable != null) editable.ProcessFrame();
                }
                sweepScratch.Clear();
                return;
            }
#endif
            sweepScratch.AddRange(enrolled);
            for (var i = 0; i < sweepScratch.Count; i++)
            {
                var editable = sweepScratch[i];
                if (!editable.isEnrolled) continue;
                if (editable != null && editable.isActiveAndEnabled)
                    editable.ProcessFrame();
                if (editable != null && editable.HasContinuousFrameWork()) continue;
                editable.isEnrolled = false;
                enrolled.Remove(editable);
            }
            sweepScratch.Clear();
        }

        private bool HasContinuousFrameWork()
            => isActive || textDirtyFlag || selectionDirtyFlag || reclaimFocusPending
               || caretContextPendingDirty || touchPointerActive || frameTicked != null
               || (touchGesture != null && touchGesture.HasPendingDispatch);

#if UNITY_EDITOR
        /// <summary>The serialized component string edit-mode reconciliation last synced FROM. Only a
        /// genuine inspector edit (the serialized string itself changing) reseeds the gap buffer —
        /// comparing buffer content against the serialized string would revert edit-mode
        /// <see cref="SetText"/> calls, since <see cref="SyncToUniText"/> intentionally never writes
        /// <see cref="UniTextBase.Text"/>.</summary>
        [NonSerialized] private string lastReconciledText;
#endif

        private void ProcessFrame()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && !isComposing)
            {
                var src = TextComponent.Text;
                if (src != lastReconciledText)
                {
                    lastReconciledText = src;
                    if (!string.Equals(Text, src, StringComparison.Ordinal))
                    {
                        DiscardTypingStyles();
                        if (HasMarkupView) ExitMarkupView();
                        ImportMarkup(src);
                        if (markupVisibility != MarkupVisibility.Hidden) EnterMarkupView();
                    }
                }
            }
#endif

            frameTicked?.Invoke(Time.unscaledDeltaTime);
            if (isDragging && isActive)
                DragAutoScroll();

            if (reclaimFocusPending && !InputUtils.GetPointerPressed())
            {
                reclaimFocusPending = false;
                var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                if (isActive && eventSystem != null && eventSystem.currentSelectedGameObject != gameObject)
                    eventSystem.SetSelectedGameObject(gameObject);
            }

            UpdateTouchGesture();
            CheckViewportResize();

            bool textChanged =
                (TextComponent.ScheduledCommitChanges & UniTextCommitChanges.Content) != 0;

            if (textDirty)
            {
                textDirty = false;
                textChanged = true;
                SyncPresentation();

                if (documentChanged)
                {
                    documentChanged = false;
                    var reason = pendingChangeReason ?? TextChangeReason.Programmatic;
                    pendingChangeReason = null;
                    TextChanged?.Invoke();
                    if (ValueChanged != null) ValueChanged.Invoke(Text);
                    DocumentChanged?.Invoke(reason);
                }

                selectionDirty = true;

                if (textDirty)
                {
                    Debug.LogWarning(
                        $"[UniTextEditable] Text was modified inside TextChanged/ValueChanged callback on '{gameObject.name}'. " +
                        "This is not supported — the change will be processed next frame.", this);
                }
            }
            else if (textChanged)
            {
                ApplyMarkupRenderState(isComposing && compositionOverlay.IsActive);
            }

            if (textChanged)
                LayoutRebuilder.MarkLayoutForRebuild(TextComponent.rectTransform);

            if (textChanged && touchActive)
                HideInsertionHandle();

            const UniTextCommitChanges consumerChanges = UniTextCommitChanges.Content |
                                                         UniTextCommitChanges.Layout |
                                                         UniTextCommitChanges.GlyphGeometry;
            bool consumerCommitPending =
                (TextComponent.ScheduledCommitChanges & consumerChanges) != 0;

            if (selectionDirty)
            {
                selectionDirty = false;

                if (!consumerCommitPending)
                {
                    UpdateCaretAndSelection();
                    OnTouchSelectionChanged();
                }
                else touchSelectionUpdatePending = true;
                SelectionChanged?.Invoke(Selection.Start, Selection.End);
                if (!consumerCommitPending)
                    UpdateCaretContext();
            }

            if (!consumerCommitPending)
            {
                UpdateInsertionHandleIfVisible();
                UpdateSelectionHandlePositions();
            }
        }

        private void OnCommitted(UniTextCommitChanges changes)
        {
            if ((changes & UniTextCommitChanges.Content) != 0)
                RebuildMarkupViewMap();
            if ((changes & UniTextCommitChanges.Layout) != 0)
                FitUniTextToContent();
            const UniTextCommitChanges consumerChanges = UniTextCommitChanges.Content |
                                                         UniTextCommitChanges.Layout |
                                                         UniTextCommitChanges.GlyphGeometry;
            if ((changes & consumerChanges) == 0) return;
            if ((changes & UniTextCommitChanges.GlyphGeometry) != 0)
                InvalidateCaretGeometry();
            UpdateCaretAndSelection();
            UpdateCaretContext();
            if (touchSelectionUpdatePending)
            {
                touchSelectionUpdatePending = false;
                OnTouchSelectionChanged();
            }
            UpdateInsertionHandleIfVisible();
            UpdateSelectionHandlePositions();
            InputGeometryChanged?.Invoke();
        }

        private void SyncToUniText()
        {
            var compositionActive = isComposing && compositionOverlay.IsActive;
            var compositionChars = compositionActive ? compositionOverlay.textLength : 0;

            if (ResolveDisplayMask(out var maskCodepoint))
            {
                CacheMaskUnit(maskCodepoint);
                var charsPerCodepoint = cachedMaskUnitCount;
                int maskCharLen = codepointCount * charsPerCodepoint;
                int totalLen = maskCharLen + compositionChars;
                if (syncBuffer.Length < totalLen)
                    syncBuffer = new char[Mathf.NextPowerOfTwo(Mathf.Max(totalLen, 64))];

                int splitMaskChars = compositionActive
                    ? compositionOverlay.insertionCodepoint * charsPerCodepoint
                    : maskCharLen;

                WriteMaskChars(syncBuffer, 0, splitMaskChars / charsPerCodepoint);

                if (compositionActive)
                {
                    new ReadOnlySpan<char>(compositionOverlay.textBuffer, 0, compositionChars)
                        .CopyTo(syncBuffer.AsSpan(splitMaskChars, compositionChars));
                    int trailingCps = codepointCount - compositionOverlay.insertionCodepoint;
                    WriteMaskChars(syncBuffer, splitMaskChars + compositionChars, trailingCps);
                }

                TextComponent.SetPlainText(syncBuffer, 0, totalLen);
                return;
            }

            int gapLen = ViewText.Length;
            int totalChars = gapLen + compositionChars;
            if (syncBuffer.Length < totalChars)
                syncBuffer = new char[Mathf.NextPowerOfTwo(Mathf.Max(totalChars, 64))];

            if (!compositionActive)
            {
                ViewText.CopyTo(syncBuffer);
                if (HasMarkupView)
                    TextComponent.SetPlainText(syncBuffer, 0, gapLen);
                else
                {
                    BuildRenderAttributes(-1, 0);
                    TextComponent.SetAttributedText(syncBuffer, 0, gapLen, renderAttributeSpans, renderProtectionRanges);
                }
                return;
            }

            int insertCharIndex = ViewText.CodepointToCharIndex(compositionOverlay.insertionCodepoint);
            ViewText.CopyTo(0, insertCharIndex, syncBuffer.AsSpan(0, insertCharIndex));
            new ReadOnlySpan<char>(compositionOverlay.textBuffer, 0, compositionChars)
                .CopyTo(syncBuffer.AsSpan(insertCharIndex, compositionChars));
            ViewText.CopyTo(insertCharIndex, gapLen - insertCharIndex,
                syncBuffer.AsSpan(insertCharIndex + compositionChars, gapLen - insertCharIndex));

            if (HasMarkupView)
                TextComponent.SetPlainText(syncBuffer, 0, totalChars);
            else
            {
                BuildRenderAttributes(insertCharIndex, compositionChars);
                TextComponent.SetAttributedText(syncBuffer, 0, totalChars, renderAttributeSpans, renderProtectionRanges);
            }
        }

        private void BuildRenderAttributes(int compositionCharIndex, int compositionChars)
        {
            renderAttributeSpans.Clear();
            renderProtectionRanges.Clear();
            if (!document.IsInitialized) return;

            var annotations = document.Annotations;
            renderAnnotationScratch.Clear();
            for (var i = 0; i < annotations.Count; i++)
            {
                var annotation = annotations[i];
                if (annotation.modifier != null && annotation.end > annotation.start)
                    renderAnnotationScratch.Add(annotation);
                if (annotation.kind == SourceAnnotationKind.Style || annotation.end <= annotation.start) continue;
                var start = ViewText.CodepointToCharIndex(annotation.start);
                var end = ViewText.CodepointToCharIndex(annotation.end);
                if (compositionCharIndex >= 0)
                {
                    if (start >= compositionCharIndex) start += compositionChars;
                    if (end > compositionCharIndex) end += compositionChars;
                }
                if (end > start) renderProtectionRanges.Add((start, end));
            }
            renderAnnotationScratch.Sort(static (a, b) => a.order.CompareTo(b.order));
            for (var i = 0; i < renderAnnotationScratch.Count; i++)
            {
                var annotation = renderAnnotationScratch[i];
                var start = ViewText.CodepointToCharIndex(annotation.start);
                var end = ViewText.CodepointToCharIndex(annotation.end);
                if (compositionCharIndex >= 0)
                {
                    if (start >= compositionCharIndex) start += compositionChars;
                    if (end > compositionCharIndex) end += compositionChars;
                }
                renderAttributeSpans.Add(new AttributeSpan(start, end, annotation.modifier,
                    ModifierParameters.Explicit(annotation.parameter), annotation.sourceRule ?? annotation.rule));
            }
        }

        private void SyncPresentation()
        {
            if (TextComponent == null || ViewText == null) return;
            codepointCount = ViewText.CodepointCount;
            Selectable?.ClampToBuffer(codepointCount);
            ApplyMarkupRenderState(isComposing && compositionOverlay.IsActive);
            SyncToUniText();
        }

        private int cachedMaskUnitCodepoint;
        private int cachedMaskUnitCount;
        private char cachedMaskChar0;
        private char cachedMaskChar1;

        private void CacheMaskUnit(int maskCodepoint)
        {
            if (cachedMaskUnitCount != 0 && cachedMaskUnitCodepoint == maskCodepoint) return;
            cachedMaskUnitCodepoint = maskCodepoint;
            cachedMaskUnitCount = UnicodeData.EncodeUtf16(maskCodepoint,
                out var high, out var low);
            cachedMaskChar0 = (char)high;
            cachedMaskChar1 = (char)low;
        }

        private void WriteMaskChars(char[] dest, int destStart, int codepointCount)
        {
            if (codepointCount <= 0) return;
            for (int i = 0; i < codepointCount; i++)
            {
                var o = destStart + i * cachedMaskUnitCount;
                dest[o] = cachedMaskChar0;
                if (cachedMaskUnitCount == 2) dest[o + 1] = cachedMaskChar1;
            }
        }

        /// <summary>
        /// Replaces the entire text content, resetting selection and undo history and discarding
        /// any in-progress composition.
        /// </summary>
        public void SetText(string value)
        {
            value ??= string.Empty;
            EnsureInitialized();
            EndCompositionBeforeDocumentMutation();

            var restoreMarkupView = HasMarkupView;
            if (restoreMarkupView) ExitMarkupView();

            var shape = ReplaceAllText(value);
            ApplyShapeToDerivedState(in shape);
            Selectable?.SetSelectionInternal(TextSelection.Caret(codepointCount), SelectionChangeReason.Programmatic);
            undoStack.Clear();
            MarkDocumentChanged(TextChangeReason.Programmatic);
            if (restoreMarkupView)
            {
                EnterMarkupView();
                MoveCaretTo(codepointCount);
                SyncPresentation();
            }
        }

        /// <summary>
        /// Wholesale document swap shared by the <see cref="SetText"/> family: clears style
        /// ranges silently (wholesale set resets attribution — the UIKit convention), replaces
        /// the gap buffer content, and refreshes the codepoint count. Returns the swap's
        /// minimal <see cref="EditShape"/> (common prefix/suffix factored out). Selection,
        /// undo policy, and document events stay with the caller.
        /// </summary>
        private EditShape ReplaceAllText(string value)
        {
            DiscardTypingStyles();
            return ImportMarkup(value);
        }

        /// <summary>
        /// Replaces the text from a non-user source (network sync, state restoration,
        /// remote collaboration), discarding any in-progress composition first.
        /// </summary>
        /// <param name="value">The text to set. <see langword="null"/> is treated as empty.</param>
        /// <param name="recordUndo">
        /// When <see langword="true"/>, the swap is recorded as a single Replace entry the user
        /// can undo / redo. When <see langword="false"/> (default), the swap itself is not
        /// recorded and <paramref name="preserveHistory"/> decides what happens to existing
        /// history.
        /// </param>
        /// <param name="preserveHistory">
        /// Only meaningful with <paramref name="recordUndo"/> <see langword="false"/>. Default
        /// (<see langword="false"/>) clears the undo history — positions recorded against the
        /// old text are meaningless against the new one. <see langword="true"/> rebases entry
        /// positions through the swap's minimal diff shape instead; entries overlapping the
        /// changed region are dropped (with their far side of the undo pointer, keeping the
        /// replay chain contiguous). Because the diff factors out the common prefix/suffix, a
        /// network echo that only appends or edits elsewhere keeps the user's local history —
        /// a whole-document rewrite drops everything, which is the honest semantics.
        /// </param>
        /// <param name="reason">
        /// <see cref="TextChangeReason"/> reported to <see cref="DocumentChanged"/>;
        /// <see langword="null"/> = <see cref="TextChangeReason.Programmatic"/>. Collaborative
        /// integrators pass <see cref="TextChangeReason.Network"/> to distinguish their own
        /// writes.
        /// </param>
        public void SetTextProgrammatic(string value, bool recordUndo = false, bool preserveHistory = false,
            string reason = null)
        {
            value ??= string.Empty;
            EnsureInitialized();
            EndCompositionBeforeDocumentMutation();

            var restoreMarkupView = HasMarkupView;
            if (restoreMarkupView) ExitMarkupView();

            if (recordUndo)
            {
                var selectionBefore = Selection;
                var oldVisible = document.Text.ToString();
                var stateBefore = document.CaptureState();
                var shape = ReplaceAllText(value);
                ApplyShapeToDerivedState(in shape);
                var newVisible = document.Text.ToString();
                var stateAfter = document.CaptureState();
                if (oldVisible.Length > 0 || newVisible.Length > 0
                    || stateBefore.annotations.Length > 0 || stateAfter.annotations.Length > 0)
                {
                    undoStack.RecordAttributed(oldVisible.AsSpan(), newVisible.AsSpan(), stateBefore, stateAfter,
                        selectionBefore, document.Text.CodepointCount);
                    undoStack.BreakCoalescing();
                }
            }
            else
            {
                var shape = ReplaceAllText(value);
                ApplyShapeToDerivedState(in shape);
                if (preserveHistory) undoStack.Rebase(shape);
                else undoStack.Clear();
            }

            Selectable?.SetSelectionInternal(TextSelection.Caret(codepointCount), SelectionChangeReason.Programmatic);
            MarkDocumentChanged(reason ?? TextChangeReason.Programmatic);
            if (restoreMarkupView)
            {
                EnterMarkupView();
                MoveCaretTo(codepointCount);
                SyncPresentation();
            }
        }

        /// <summary>Programmatically moves the caret to <paramref name="codepointIndex"/> (clamped).</summary>
        public void MoveCaretTo(int codepointIndex)
        {
            EnsureInitialized();
            if (Selectable == null) return;
            desiredX = float.NaN;
            var clamped = Mathf.Clamp(codepointIndex, 0, codepointCount);
            if (Selectable.SetCaret(clamped, CaretAffinity.Downstream, SelectionChangeReason.Programmatic))
            {
                selectionDirty = true;
                undoStack?.BreakCoalescing();
                caretRenderer?.ResetBlink();
            }
        }

        /// <summary>Programmatically selects the range [<paramref name="start"/>, <paramref name="end"/>] (clamped).</summary>
        public void Select(int start, int end)
        {
            EnsureInitialized();
            if (Selectable == null) return;
            desiredX = float.NaN;
            start = Mathf.Clamp(start, 0, codepointCount);
            end = Mathf.Clamp(end, 0, codepointCount);
            if (Selectable.SetSelection(start, end, CaretAffinity.Downstream, SelectionChangeReason.Programmatic))
            {
                selectionDirty = true;
                undoStack?.BreakCoalescing();
                caretRenderer?.ResetBlink();
            }
        }

        /// <summary>Clears the undo/redo history.</summary>
        public void ClearUndoHistory() => undoStack?.Clear();

        /// <summary>True when an undo step is available.</summary>
        public bool CanUndo => undoStack != null && undoStack.CanUndo;

        /// <summary>True when a redo step is available.</summary>
        public bool CanRedo => undoStack != null && undoStack.CanRedo;

        private void EnsureCaretRenderer()
        {
            if (caretRenderer != null) return;

            var go = new GameObject("Caret", typeof(InputCaretRenderer));
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(transform, false);

            var rt = go.GetComponent<RectTransform>();
            rt.StretchToParent();
            rt.pivot = TextComponent != null ? TextComponent.rectTransform.pivot : new Vector2(0f, 1f);

            CaretRenderer = go.GetComponent<InputCaretRenderer>();
            caretRenderer.color = TextComponent.color;
        }

        /// <summary>
        /// Whether the document accepts newline input — false makes the return key spend itself on
        /// the action it declares instead of a line break. Also drives the single-line scroll reset.
        /// </summary>
        internal bool AcceptsNewlines => newlineSuppressionCount == 0;


        internal void SuppressNewlines()
        {
            if (newlineSuppressionCount++ == 0)
                NotifyInputSessionConfigurationChanged();
        }

        internal void ReleaseNewlineSuppression()
        {
            if (newlineSuppressionCount == 0)
                throw new InvalidOperationException("Newline suppression is not active.");
            if (--newlineSuppressionCount == 0)
                NotifyInputSessionConfigurationChanged();
        }
    }
}
