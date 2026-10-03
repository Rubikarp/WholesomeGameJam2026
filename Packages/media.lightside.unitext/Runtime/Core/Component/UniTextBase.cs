using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// Shared base for UniText components — handles text processing (Unicode, BiDi, shaping,
    /// line breaking, modifiers, emoji, font fallback, variable fonts). Concrete subclasses
    /// supply the rendering backend: <see cref="UniText"/> (Canvas) and <see cref="UniTextWorld"/>
    /// (world-space).
    /// </summary>
    [ExecuteAlways]
    public abstract partial class UniTextBase : MaskableGraphic
#if UNITY_EDITOR
        , IEditorSerializedPropertyStateOwner
#endif
    {

        #region Serialized Fields

        [TextArea(3, 10)]
        [SerializeField, StateField(nameof(ApplySerializedTextChange))]
        [Tooltip("The text content to display. Supports Unicode, emoji, and custom markup.")]
        private string text = "";

        [NonSerialized] protected ReadOnlyMemory<char> sourceText;
        [NonSerialized] private bool isTextFromBuffer;
        [NonSerialized] private PooledBuffer<char> stringBuilderScratch;

        [NonSerialized] private IUniTextResolver textResolver;
        [NonSerialized] private ReadOnlyMemory<char> resolvedText;
        [NonSerialized] private bool hasResolvedText;
        [NonSerialized] private UniTextAttachments attachments;

        /// <summary>Optional primary font overriding the first font-stack family.</summary>
        [SerializeField, StateProperty(nameof(ApplyFontChange))]
        [Tooltip("Optional primary font. When set, overrides the primary picked from Font Stack; " +
                 "Font Stack still serves as fallback for missing characters. Leave empty to use " +
                 "Font Stack's first family.")]
        private UniTextFont font;

        /// <summary>Optional primary and fallback font collection.</summary>
        [SerializeField, StateProperty(nameof(ApplyFontStackChange))]
        [Tooltip("Optional font collection. Provides the primary (when Font is unset) and the " +
                 "fallback chain for characters the primary doesn't have. Leave empty for a " +
                 "single-font setup.")]
        private UniTextFontStack fontStack;

        /// <summary>Gets or sets the base font size in points.</summary>
        [SerializeField, StateProperty(nameof(ApplyFontSizeChange))]
        [Tooltip("Base font size in points.")]
        protected float fontSize = 36f;

        /// <summary>Gets or sets whether word wrapping is enabled.</summary>
        [SerializeField, StateProperty(nameof(MarkLayoutDirty))]
        [Tooltip("Enable word wrapping at container boundaries.")]
        protected bool wordWrap = true;

        /// <summary>
        /// Inner inset between the <see cref="RectTransform"/> edge and the text layout area.
        /// Components: <c>x</c> = Left, <c>y</c> = Bottom, <c>z</c> = Right, <c>w</c> = Top.
        /// The hit-test and raycast area remains the full RectTransform.
        /// </summary>
        [SerializeField, StateProperty(nameof(ApplyPaddingChange))]
        [Tooltip("Inner inset (Left, Bottom, Right, Top) that shrinks the text area inside the " +
                 "RectTransform. Useful for outline/shadow bleed or caret/IME insets. The raycast " +
                 "area is not affected — it stays the full RectTransform.")]
        private Vector4 padding;

        /// <summary>Gets or sets the horizontal text alignment.</summary>
        [SerializeField, StateProperty(nameof(MarkPositionsDirty))]
        [Tooltip("Horizontal text alignment within the container.")]
        private HorizontalAlignment horizontalAlignment = HorizontalAlignment.Start;

        /// <summary>Gets or sets the vertical text alignment.</summary>
        [SerializeField, StateProperty(nameof(MarkPositionsDirty))]
        [Tooltip("Vertical text alignment within the container.")]
        private VerticalAlignment verticalAlignment = VerticalAlignment.Top;

        /// <summary>Gets or sets whether automatic font sizing is enabled.</summary>
        [SerializeField, StateProperty(nameof(MarkLayoutDirty))]
        [Tooltip("Automatically adjust font size to fit container.")]
        protected bool autoSize;

        /// <summary>Gets or sets the minimum font size for auto-sizing.</summary>
        [SerializeField, StateProperty(nameof(ApplyAutoSizeBoundChange))]
        [Tooltip("Minimum font size when auto-sizing.")]
        protected float minFontSize = 10f;

        /// <summary>Gets or sets the maximum font size for auto-sizing.</summary>
        [SerializeField, StateProperty(nameof(ApplyAutoSizeBoundChange))]
        [Tooltip("Maximum font size when auto-sizing.")]
        protected float maxFontSize = 72f;

        /// <summary>
        /// Budgets auto-sizing spends, in list order, before reducing the font size. Each entry is
        /// tried at its full budget; the first that makes the text fit is dialed back to the least
        /// adjustment that still fits. Empty (the default) means font size is the only lever.
        /// </summary>
        [SerializeField, StateList(nameof(ApplyFitStepsChange), Owned = true, AllowNullItems = false)]
        [Tooltip("Budgets Auto Size spends, in order, before reducing the font size: letter-spacing, " +
                 "glyph-width, and line-height compression. Empty = font size only.")]
        private TypedList<FitStep> fitSteps = new();

        [NonSerialized] private ReferenceBinding<FitStep> boundFitSteps;

        /// <summary>Local range-source and modifier-graph pairs.</summary>
        [SerializeField, StateList(nameof(ApplyStylesChange),
            Validator = nameof(ValidateStylesMutation), Owned = true, AllowNullItems = false,
            AllowDuplicateReferences = false)]
        [Tooltip("Range source and modifier-graph pairs for markup, authored ranges, runtime ranges, and whole-text effects.")]
        private StyledList<Style> styles = new();

        /// <summary>Shared style presets applied after local styles.</summary>
        [SerializeField, StateList(nameof(RefreshStylePresetBindings), AllowNullItems = false,
            AllowDuplicateReferences = false)]
        [Tooltip("Shared modifier configurations (ScriptableObjects) to apply in addition to local styles.")]
        private StyledList<StylePreset> stylePresets = new();

        /// <summary>Whether this component applies the project-wide style preset.</summary>
        [SerializeField, StateProperty(nameof(ApplyGlobalStylePresetUsageChange))]
        [Tooltip("Apply the project-wide StylePreset from Project Settings on top of local " +
                 "Styles / Style Presets. Disable to opt this component out of global rules " +
                 "(e.g. debug overlays where markup would interfere).")]
        private bool useGlobalStylePreset = true;

        [NonSerialized] private StyleRuntimeSet styleRuntime;
        private StyleRuntimeSet RuntimeStyles =>
            styleRuntime ??= new StyleRuntimeSet(this);

        /// <summary>Gets or sets the text rendering mode used for glyph coverage.</summary>
        [SerializeField, StateProperty(nameof(ApplyRenderModeChange))]
        [Tooltip("SDF: rounded corners on outline/underlay effects. MSDF: sharp corners.")]
        private UniTextRenderMode renderMode = UniTextRenderMode.SDF;

        #endregion

        #region Runtime State

        protected TextProcessor textProcessor;
        private UniTextFontProvider fontProvider;
        protected UniTextMeshGenerator meshGenerator;
        private AttributeParser attributeParser;
        [NonSerialized] private bool attributeParserAttached;
        protected UniTextBuffers buffers;

        [NonSerialized] private UniTextDirty dirtyFlags = UniTextDirty.All;

        /// <summary>Gets the current dirty flags indicating what needs rebuilding.</summary>
        public UniTextDirty CurrentDirtyFlags => dirtyFlags;
        internal UniTextCommitChanges ScheduledCommitChanges
            => isProcessing ? processingCommitChanges | deferredCommitChanges : pendingCommitChanges;
        [NonSerialized] private bool textIsParsed;
        [NonSerialized] private bool isRegisteredDirty;
        [NonSerialized] private bool isProcessing;
        /// <summary>True while <see cref="DeInit"/> unwinds pipeline resources. Teardown publishes
        /// <see cref="DirtyFlagsChanged"/> and <see cref="Deinitializing"/> before
        /// <see cref="textProcessor"/> dies, so <see cref="ValidateAndInitialize"/> and
        /// <see cref="EnsureAttributeParserCreated"/> refuse while it is set — state built by a
        /// re-entering listener would subscribe to resources this teardown destroys next. Nested
        /// teardown (an empty-text <see cref="SetSource"/> reached from a listener) restores the
        /// flag rather than clearing it, so the inner pass cannot reopen the outer window.</summary>
        [NonSerialized] private bool isDeinitializing;
        [NonSerialized] private UniTextDirty deferredDirtyFlags;
        [NonSerialized] private UniTextCommitChanges pendingCommitChanges = UniTextCommitChanges.All;
        [NonSerialized] private UniTextCommitChanges processingCommitChanges;
        [NonSerialized] private UniTextCommitChanges deferredCommitChanges;

        private float resultWidth;
        private float resultHeight;

        private struct RefCountTracker
        {
            private PooledBuffer<long> current;
            private PooledBuffer<long> previous;
            /// <summary>Atlas the current keys are AddRef'd into. Releases must target it even if the component's atlas changed since (render mode switch, atlas recreation) — releasing old keys into the new atlas both leaks the old entries and steals refs from same-keyed new ones.</summary>
            private GlyphAtlas atlas;

            public int Count => current.count;

            public void Update(GlyphAtlas newAtlas, ref PooledBuffer<long> newKeys)
            {
                var previousAtlas = atlas;
                atlas = newAtlas;
                (previous, current) = (current, previous);
                current.FakeClear();
                current.EnsureCapacity(newKeys.count);
                newKeys.Span.CopyTo(current.data);
                current.count = newKeys.count;
                for (int i = 0; i < current.count; i++)
                    newAtlas.AddRef(current[i]);
                if (previousAtlas != null)
                    for (int i = 0; i < previous.count; i++)
                        previousAtlas.Release(previous[i]);
            }

            public void ReleaseAll()
            {
                if (atlas != null)
                    for (int i = 0; i < current.count; i++)
                        atlas.Release(current[i]);
                current.FakeClear();
            }

            /// <summary>Forgets removed atlas keys without releasing unrelated entries that the currently displayed mesh still uses.</summary>
            public bool DropMissing()
            {
                if (current.count == 0) return false;
                int write = 0;
                for (int i = 0; i < current.count; i++)
                {
                    long key = current[i];
                    if (atlas.TryGetEntry(key, out _))
                        current[write++] = key;
                }
                bool removed = write != current.count;
                current.count = write;
                return removed;
            }

            public void Return()
            {
                current.Return();
                previous.Return();
                atlas = null;
            }
        }

        private RefCountTracker glyphRefs;
        private RefCountTracker colorRefs;
        private RefCountTracker fieldRefs;

        protected List<UniTextRenderData> renderData;

        private float lastKnownWidth = -1;
        private float lastKnownHeight = -1;

        /// <summary>Occurs when text is about to be rebuilt.</summary>
        public event Action Rebuilding;

        /// <summary>Occurs when glyphs have been positioned, before mesh generation. Last chance to inspect or inject positioned glyphs (used by per-glyph modifiers).</summary>
        private OrderedEvent beforeGenerateMesh;

        /// <summary>Runs after glyph positioning and before mesh generation.</summary>
        public OrderedEvent BeforeGenerateMesh => beforeGenerateMesh ??= new OrderedEvent();

        /// <summary>Occurs when the RectTransform height has changed.</summary>
        public event Action RectHeightChanged;

        /// <summary>Occurs when dirty flags have changed, indicating what needs rebuilding.</summary>
        public event Action<UniTextDirty> DirtyFlagsChanged;

        private Action frameUpdated;

        /// <summary>
        /// Occurs once per frame while the component is enabled, from <see cref="CoreLoop.Updating"/>
        /// immediately before script Update callbacks. In edit mode an active subscription keeps the
        /// editor pumping frames until removed. Subscribing and unsubscribing are safe from this
        /// component's processing on worker threads (modifier lifecycle raised during the sweep);
        /// the tick subscription then settles on the main thread when the sweep completes.
        /// </summary>
        public event Action FrameUpdated
        {
            add
            {
                frameUpdated += value;
                RefreshFrameTick();
            }
            remove
            {
                frameUpdated -= value;
                RefreshFrameTick();
            }
        }

        /// <summary>
        /// Occurs when THIS component's mesh has been applied in a processing sweep (main
        /// thread, layout and glyph geometry are final for the frame). Fires only when this
        /// component actually reprocessed — consumers reacting to layout changes (coordinate
        /// maps, caret geometry, auxiliary geometry, overlay positioning) subscribe here and
        /// never pay for other components' updates.
        /// </summary>
        public event Action LayoutCommitted;

        /// <summary>Occurs after this component commits a processing pass and identifies which observable outputs changed.</summary>
        public event Action<UniTextCommitChanges> Committed;

        /// <summary>Occurs after styles are invalidated and before text-pipeline resources are released.</summary>
        internal event Action Deinitializing;

        /// <summary>Publishes a new mesh generator after creation and <see langword="null"/> before disposal.</summary>
        internal event Action<UniTextMeshGenerator> MeshGeneratorChanged;

        /// <summary>Lets internal attachments finalize commit data before public observers are notified.</summary>
        internal event Action<UniTextCommitChanges> CommitFinalizing;

        #endregion

        #region Public API

        /// <summary>Gets the text processor instance handling shaping and layout.</summary>
        public TextProcessor TextProcessor => textProcessor;

        /// <summary>Gets the mesh generator instance.</summary>
        public UniTextMeshGenerator MeshGenerator => meshGenerator;

        /// <summary>Gets the font provider managing font assets and fallbacks.</summary>
        public UniTextFontProvider FontProvider => fontProvider;

        /// <summary>Gets the buffer container for text processing.</summary>
        public UniTextBuffers Buffers => buffers;

        internal T GetOrCreateAttachment<T>(Func<UniTextBase, T> factory)
            where T : class, IDisposable
            => (attachments ??= new UniTextAttachments()).GetOrCreate(this, factory);

        internal bool TryGetAttachment<T>(out T attachment) where T : class, IDisposable
        {
            if (attachments != null) return attachments.TryGet(out attachment);
            attachment = null;
            return false;
        }

        /// <summary>
        /// The runtime source text — the last value assigned via <see cref="Text"/> or any
        /// <c>SetText</c> overload, before any resolver substitution. Zero-alloc.
        /// </summary>
        public ReadOnlyMemory<char> RawText => sourceText;

        /// <summary>
        /// Gets the substitute produced by the attached <see cref="TextResolver"/> on the
        /// last rebuild, or an empty memory when no resolver is attached or
        /// <see cref="IUniTextResolver.TryResolve"/> returned <see langword="false"/>.
        /// Zero-alloc. Test <see cref="TextOverride"/> for
        /// <see cref="TextOverrideSource.Resolver"/> to know if this value is in use.
        /// </summary>
        public ReadOnlyMemory<char> ResolvedText => hasResolvedText ? resolvedText : default;

        /// <summary>
        /// Gets the text actually fed into the parsing / shaping / layout pipeline: the
        /// resolver's output if one is active, otherwise <see cref="RawText"/>. Zero-alloc.
        /// Still contains markup; for the markup-stripped form use <see cref="CleanText"/>.
        /// </summary>
        public ReadOnlyMemory<char> RenderedText => hasResolvedText ? resolvedText : sourceText;

        /// <summary>
        /// Gets <see cref="RenderedText"/> with parsed markup removed. Zero-alloc. The
        /// backing buffer is pooled and may be rewritten on the next parse — do not store
        /// the span; call <c>new string(span)</c> if you need a stable string.
        /// </summary>
        public ReadOnlySpan<char> CleanText =>
            attributeParser != null ? attributeParser.CleanTextSpan : RenderedText.Span;

        /// <summary>
        /// Captures immutable rendered text and its range-coordinate revision. The component must
        /// have completed at least one parse after its current style graph was attached.
        /// </summary>
        public TextSnapshot CaptureTextSnapshot()
        {
            var snapshot = attributeParser?.TextSnapshot ?? default;
            if (!snapshot.IsValid)
                throw new InvalidOperationException(
                    "A text snapshot is unavailable until UniText completes its first parse.");
            return snapshot;
        }

        /// <summary>
        /// Number of codepoints in the rendered text (markup resolved). 0 before the first
        /// rebuild. This is the codepoint space shared by <see cref="RenderedCodepoints"/>,
        /// <see cref="FindAll"/> results, highlight-layer ranges and the range-geometry queries.
        /// </summary>
        public int CodepointCount => buffers?.codepoints.count ?? 0;

        /// <summary>
        /// The rendered text as Unicode codepoints — the codepoint space highlight layers and
        /// range geometry index into. Zero-alloc. The backing buffer is pooled and rewritten on
        /// the next rebuild — do not store the span. For the UTF-16 form use <see cref="CleanText"/>.
        /// </summary>
        public ReadOnlySpan<int> RenderedCodepoints =>
            buffers != null ? (ReadOnlySpan<int>)buffers.codepoints.Span : default;

        /// <summary>
        /// Copies one validated rendered-codepoint range into a stable UTF-16 string. This is the
        /// shared representation used by range actions and external export adapters.
        /// </summary>
        public string GetRangeText(TextRange range)
        {
            var codepoints = RenderedCodepoints;
            if (range.start < 0 || range.length < 0 || range.End > codepoints.Length)
                throw new ArgumentOutOfRangeException(nameof(range),
                    $"Range {range.start}..{range.End} is outside 0..{codepoints.Length}.");
            if (range.length == 0) return string.Empty;
            var chars = new char[range.length * 2];
            var written = 0;
            for (var i = range.start; i < range.End; i++)
                written += UnicodeData.EncodeUtf16(codepoints[i], chars, written);
            return new string(chars, 0, written);
        }

        /// <summary>
        /// Finds every occurrence of <paramref name="query"/> in the rendered text, writing one
        /// codepoint range per match into <paramref name="results"/> (left to right,
        /// non-overlapping). Returns the number of matches written; stops early when
        /// <paramref name="results"/> is full. Ranges feed <see cref="MutableRangeSource"/>
        /// and <see cref="GetRangeBounds"/> directly. Allocation-free for queries up to
        /// 64 UTF-16 chars. Main thread.
        /// </summary>
        /// <remarks>
        /// Case-insensitive comparisons use the engine's bundled Unicode simple case mappings —
        /// per-codepoint and locale-independent. Multi-codepoint foldings (German ß ↔ ss) and
        /// locale-specific rules (Turkish dotless i) do not match; culture-sensitive
        /// <see cref="StringComparison"/> values behave as their ordinal counterparts.
        /// </remarks>
        public int FindAll(ReadOnlySpan<char> query, StringComparison comparison, Span<TextRange> results)
        {
            if (buffers == null || query.IsEmpty || results.IsEmpty) return 0;

            var ignoreCase = comparison == StringComparison.OrdinalIgnoreCase
                             || comparison == StringComparison.InvariantCultureIgnoreCase
                             || comparison == StringComparison.CurrentCultureIgnoreCase;

            Span<int> queryCps = query.Length <= 64 ? stackalloc int[query.Length] : new int[query.Length];
            var queryLen = 0;
            for (var i = 0; i < query.Length;)
            {
                var cp = (int)UnicodeData.DecodeAt(query, i, out var size);
                i += size;
                queryCps[queryLen++] = ignoreCase ? UnicodeData.GetSimpleLowercase(cp) : cp;
            }

            var text = buffers.codepoints.Span;
            var found = 0;
            for (var i = 0; i + queryLen <= text.Length && found < results.Length; i++)
            {
                var match = true;
                for (var j = 0; j < queryLen; j++)
                {
                    var cp = text[i + j];
                    if (ignoreCase) cp = UnicodeData.GetSimpleLowercase(cp);
                    if (cp != queryCps[j]) { match = false; break; }
                }
                if (!match) continue;
                results[found++] = new TextRange(i, queryLen);
                i += queryLen - 1;
            }
            return found;
        }

        /// <summary>
        /// Combination of flags describing which runtime source(s) are currently overriding
        /// the serialized <see cref="Text"/>. Flags may combine — for example,
        /// <see cref="TextOverrideSource.SetText"/> | <see cref="TextOverrideSource.Resolver"/>
        /// when a <c>SetText</c> buffer feeds an attached resolver that further substitutes
        /// the text.
        /// </summary>
        public TextOverrideSource TextOverride =>
            (isTextFromBuffer ? TextOverrideSource.SetText : 0) |
            (hasResolvedText ? TextOverrideSource.Resolver : 0);

        /// <summary>
        /// Gets or sets a resolver that may override the source text before parsing without
        /// modifying the serialized <c>text</c> field. Useful for editor-time localization
        /// preview and runtime text-binding without dirtying scenes or prefabs.
        /// See <see cref="IUniTextResolver"/> for the contract.
        /// </summary>
        public IUniTextResolver TextResolver
        {
            get => textResolver;
            set
            {
                if (textResolver == value) return;
                var previous = textResolver;
                textResolver = value;
                hasResolvedText = false;
                resolvedText = default;
                previous?.OnDetached(this);
                value?.OnAttached(this);
                SetDirty(UniTextDirty.Text);
            }
        }

        /// <summary>Gets the computed size of the rendered text.</summary>
        public Vector2 ResultSize => new(resultWidth, resultHeight);

        /// <summary>Gets the positioned glyphs after processing.</summary>
        public ReadOnlySpan<PositionedGlyph> ResultGlyphs => textProcessor != null ? textProcessor.PositionedGlyphs : ReadOnlySpan<PositionedGlyph>.Empty;

        /// <summary>
        /// Gets the effective primary font: the explicit <see cref="Font"/> if set,
        /// otherwise <see cref="UniTextFontStack.PrimaryFont"/> from <see cref="FontStack"/>.
        /// </summary>
        public UniTextFont PrimaryFont => font != null ? font : fontStack?.PrimaryFont;

        /// <summary>Resolved primary runtime — the actual font the engine renders with, including the OS default when no asset is assigned. Use this in modifiers and layout instead of <see cref="PrimaryFont"/> (which is null for asset-less components).</summary>
        internal UniTextFont.Core PrimaryFontCore => fontProvider?.PrimaryFont;

        /// <summary>Gets the current effective font size (accounts for auto-sizing).</summary>
        public float CurrentFontSize => autoSize
            ? (cachedEffectiveFontSize > 0 ? cachedEffectiveFontSize : maxFontSize)
            : fontSize;

        private void ApplyGlobalStylePresetUsageChange()
        {
            if (UniTextSettings.GlobalStylePreset == null) return;
            RuntimeStyles.ReconcilePresets();
        }

        /// <summary>
        /// Gets or sets the serialized source text. The getter returns the serialized field
        /// as-is and has no side effects — use <see cref="RenderedText"/> to observe what is
        /// actually being rendered when an override (<c>SetText</c> buffer or
        /// <see cref="TextResolver"/>) is active.
        /// </summary>
        /// <remarks>
        /// The setter normalizes CRLF to LF, writes both the serialized field and the runtime
        /// source buffer, and clears any prior <c>SetText</c> override. It does not affect an
        /// attached <see cref="TextResolver"/>.
        /// </remarks>
        public string Text
        {
            get => text;
            set
            {
                if (isTextFromBuffer && string.Equals(text, value, StringComparison.Ordinal))
                {
                    SetSource((value ?? "").AsMemory(), fromBuffer: false, keepProjection: false);
                    return;
                }
                SetTextState(value);
            }
        }

        private void ApplySerializedTextChange(string previous, ref string current)
        {
            current = UnicodeData.NormalizeNewlines(current);
            if (!isTextFromBuffer && string.Equals(previous, current, StringComparison.Ordinal)) return;
            SetSource((current ?? "").AsMemory(), fromBuffer: false, keepProjection: false);
        }

        /// <summary>
        /// The single text-source assignment tail: every content setter funnels through here.
        /// <paramref name="keepProjection"/> is the one contract difference between the static
        /// setters (projection resets — a plain write is a static parse again) and the editing
        /// host's <see cref="SetPlainText"/> (its active <see cref="Projection"/> survives).
        /// </summary>
        private void SetSource(ReadOnlyMemory<char> source, bool fromBuffer, bool keepProjection)
        {
            sourceText = source;
            isTextFromBuffer = fromBuffer;
            if (!keepProjection)
            {
                parseProjection = default;
                attributedInput = false;
            }
            if (sourceText.IsEmpty && !IsDocumentHost) DeInit();
            else SetDirty(UniTextDirty.Text);
        }

        /// <summary>
        /// Sets text content from a char array without allocating a string.
        /// Ideal for frequently updated text (timers, scores, etc.).
        /// </summary>
        public void SetText(char[] source, int start, int length)
            => SetSource(new ReadOnlyMemory<char>(source, start, length), fromBuffer: true, keepProjection: false);

        private ParseProjection parseProjection;
        private bool attributedInput;
        private IReadOnlyList<AttributeSpan> attributedSpans;
        private IReadOnlyList<(int start, int end)> attributedProtection;

        internal ParseProjection Projection { set => parseProjection = value; }

        /// <summary>Whether an <see cref="ITextDocument"/> host on this GameObject is enabled — set by the
        /// host itself (its enable/disable), so core carries no editing-layer type knowledge. Keeps an
        /// empty editable field one strut tall, in edit mode and play mode alike.</summary>
        internal bool IsDocumentHost;

        private bool measureTrailingWhitespace;

        internal bool MeasureTrailingWhitespace
        {
            get => measureTrailingWhitespace;
            set
            {
                if (measureTrailingWhitespace == value) return;
                measureTrailingWhitespace = value;
                SetDirty(UniTextDirty.Layout);
            }
        }

        /// <summary>
        /// Document→rendered codepoint mapping, installed by the document host when hidden markup
        /// makes the two spaces diverge; <see langword="null"/> when they coincide. Siblings that
        /// track document space (selection, highlight layers) read it at repaint.
        /// </summary>
        internal Func<int, int> DocumentToRendered;

        /// <summary>
        /// Per-edit document mutation notification <c>(start, removedCodepoints, insertedCodepoints)</c>,
        /// raised by the document host as each edit lands. Siblings holding document-space ranges
        /// subscribe to remap them.
        /// </summary>
        internal Action<int, int, int> DocumentEdited;

        /// <summary>The active markup parser, or <see langword="null"/> until styles initialize it.</summary>
        internal AttributeParser AttributeParser => attributeParser;

        /// <summary>Hysteresis margin for viewport culling, as a fraction of window height added to each side: emission covers the padded band, and only a window escaping it triggers a re-mesh — small scrolls cost nothing.</summary>
        private const float VisibleWindowPadding = 0.5f;

        private Rect? visibleWindow;
        [NonSerialized] private float emittedBandMin = float.NegativeInfinity;
        [NonSerialized] private float emittedBandMax = float.PositiveInfinity;

        /// <summary>
        /// Local-space window that bounds mesh emission: paragraphs fully outside it produce no quads.
        /// Layout, selection, caret and hit-testing are unaffected — only rendering is windowed.
        /// <see langword="null"/> renders everything. Canvas components feed it from the mask clip rect
        /// automatically; set it explicitly for custom virtualized scrollers, in this RectTransform's
        /// local space.
        /// </summary>
        public Rect? VisibleWindow
        {
            get => visibleWindow;
            set
            {
                visibleWindow = value;
                if (WindowEscapesEmittedBand())
                    SetDirty(UniTextDirty.Mesh);
            }
        }

        private bool WindowEscapesEmittedBand()
        {
            if (visibleWindow is not { } window)
                return !float.IsNegativeInfinity(emittedBandMin) || !float.IsPositiveInfinity(emittedBandMax);
            return window.yMin < emittedBandMin || window.yMax > emittedBandMax;
        }

        internal void RecordEmittedBand(float yMin, float yMax)
        {
            emittedBandMin = yMin;
            emittedBandMax = yMax;
        }

        /// <summary>
        /// How the editing layer feeds its document in: identical to <see cref="SetText(char[], int, int)"/>
        /// except the active <see cref="Projection"/> survives — the host assigns the projection before
        /// syncing content, and the projection (not the content setter) decides how markup renders.
        /// </summary>
        internal void SetPlainText(char[] source, int start, int length)
        {
            attributedInput = false;
            SetSource(new ReadOnlyMemory<char>(source, start, length), fromBuffer: true, keepProjection: true);
        }

        internal void SetAttributedText(char[] source, int start, int length,
            IReadOnlyList<AttributeSpan> spans, IReadOnlyList<(int start, int end)> protection)
        {
            attributedInput = true;
            attributedSpans = spans;
            attributedProtection = protection;
            SetSource(new ReadOnlyMemory<char>(source, start, length), fromBuffer: true, keepProjection: true);
        }

        /// <summary>
        /// Sets the text to render without writing to the serialized <c>text</c> field.
        /// The change is visible at runtime and in edit mode without marking the scene or
        /// prefab as dirty — suitable for editor-time preview (localization) or transient
        /// runtime substitution.
        /// </summary>
        /// <param name="source">The text buffer to render. Must remain valid until the next
        /// text assignment on this component.</param>
        /// <remarks>
        /// Unlike the <see cref="Text"/> setter, this method does not normalize line endings
        /// and does not persist the value. For derived text reacting to an external signal,
        /// consider <see cref="TextResolver"/> instead.
        /// </remarks>
        public void SetText(ReadOnlyMemory<char> source)
            => SetSource(source, fromBuffer: true, keepProjection: false);

        /// <summary>
        /// Sets the text to render without writing to the serialized <c>text</c> field.
        /// Convenience overload equivalent to <c>SetText(source.AsMemory())</c>.
        /// The change does not mark the scene or prefab as dirty.
        /// </summary>
        /// <param name="source">The text to render. <see langword="null"/> is treated as empty.</param>
        public void SetText(string source) => SetText((source ?? "").AsMemory());

        /// <summary>
        /// Sets the text to render from a <see cref="StringBuilder"/> without writing to the
        /// serialized <c>text</c> field and without allocating a <see cref="string"/>. The
        /// contents are copied into a pooled internal buffer, so the supplied
        /// <see cref="StringBuilder"/> may be mutated freely after the call.
        /// </summary>
        /// <param name="source">The text source. <see langword="null"/> is treated as empty.</param>
        /// <remarks>
        /// Allocation-free per call once the internal scratch buffer has grown to the required
        /// capacity. If you already keep your text in a <see cref="char"/> array, prefer
        /// <see cref="SetText(char[], int, int)"/> to skip the copy.
        /// </remarks>
        public void SetText(StringBuilder source)
        {
            var length = source?.Length ?? 0;
            if (length == 0)
            {
                SetText(ReadOnlyMemory<char>.Empty);
                return;
            }
            stringBuilderScratch.EnsureCapacity(length);
            source.CopyTo(0, stringBuilderScratch.data, 0, length);
            SetText(stringBuilderScratch.data, 0, length);
        }

        /// <summary>
        /// Sets the text to render from a character span without writing to the serialized
        /// <c>text</c> field and without allocating a <see cref="string"/>. The span is copied
        /// into a pooled internal buffer, so its backing storage may be reused or released
        /// immediately after the call — making this the safe bridge from a pool-rented builder
        /// such as ZString's <c>Utf16ValueStringBuilder.AsSpan()</c>.
        /// </summary>
        /// <param name="source">The text to render. An empty span clears the text.</param>
        /// <remarks>
        /// Allocation-free per call once the internal scratch buffer has grown to the required
        /// capacity. Unlike <see cref="SetText(ReadOnlyMemory{char})"/>, this does not retain the
        /// caller's memory, so a builder whose buffer returns to a pool on dispose is safe to use.
        /// </remarks>
        public void SetText(ReadOnlySpan<char> source)
        {
            var length = source.Length;
            if (length == 0)
            {
                SetText(ReadOnlyMemory<char>.Empty);
                return;
            }
            stringBuilderScratch.EnsureCapacity(length);
            source.CopyTo(stringBuilderScratch.data);
            SetText(stringBuilderScratch.data, 0, length);
        }

        private void ApplyFontChange(UniTextFont previous, UniTextFont current)
        {
            SyncAssetSubscriptions();
            SetDirty(UniTextDirty.Font);
        }

        private void ApplyFontStackChange(UniTextFontStack previous, UniTextFontStack current)
        {
            SyncAssetSubscriptions();
            SetDirty(UniTextDirty.Font);
        }

        private void ApplyFontSizeChange(float previous, ref float current)
        {
            current = Mathf.Max(0.01f, current);
            if (Mathf.Approximately(previous, current))
            {
                current = previous;
                return;
            }
            SetDirty(UniTextDirty.Layout);
        }

        /// <summary>
        /// BCP 47 language tag for this text (e.g. <c>zh-Hans</c>, <c>ja</c>, <c>en-US</c>).
        /// Picks the correct font variant for the script and enables the OpenType <c>locl</c>
        /// feature. Per-range overrides via <c>&lt;lang=...&gt;...&lt;/lang&gt;</c> take priority.
        /// </summary>
        public string Language
        {
            get => GetWholeTextParameter<LanguageModifier>();
            set
            {
                if (string.IsNullOrEmpty(value)) ClearWholeText<LanguageModifier>();
                else SetWholeText<LanguageModifier>(value);
            }
        }

        /// <summary>
        /// The <see cref="RectTransform.rect"/> with <see cref="Padding"/> applied: origin shifted
        /// by <c>(Left, Bottom)</c>, size shrunk by <c>(Left+Right, Bottom+Top)</c>, clamped to
        /// non-negative. Main-thread only.
        /// </summary>
        public Rect GetPaddedRect() => ApplyPadding(rectTransform.rect);

        private Rect ApplyPadding(Rect outer)
        {
            var width = outer.width - padding.x - padding.z;
            var height = outer.height - padding.y - padding.w;
            return new Rect(
                outer.xMin + padding.x,
                outer.yMin + padding.y,
                width < 0f ? 0f : width,
                height < 0f ? 0f : height);
        }

        private void ApplyRenderModeChange(UniTextRenderMode previous, UniTextRenderMode current)
        {
            CatZones.lifecycle.MeowFormat("[UniText] RenderMode switch '{0}': {1}→{2}", name, previous, current);
            if (textProcessor != null) textProcessor.HasValidGlyphsInAtlas = false;
            SetAppearanceDirty();
        }

        private void ApplyAutoSizeBoundChange(float previous, ref float current)
        {
            current = Mathf.Max(0.01f, current);
            if (Mathf.Approximately(previous, current))
            {
                current = previous;
                return;
            }
            if (autoSize) SetDirty(UniTextDirty.Layout);
        }

        private void ApplyPaddingChange(Vector4 previous, ref Vector4 current)
        {
            if (previous != current)
            {
                SetDirty(UniTextDirty.Layout);
                return;
            }
            current = previous;
        }

        private void MarkLayoutDirty() => SetDirty(UniTextDirty.Layout);

        private void MarkPositionsDirty() => SetDirty(UniTextDirty.Positions);

        private void ApplyFitStepsChange()
        {
            if (isActiveAndEnabled) BindFitSteps();
            SetDirty(UniTextDirty.Layout);
        }

        private void BindFitSteps()
        {
            boundFitSteps ??= new ReferenceBinding<FitStep>(ConnectFitStep, DisconnectFitStep);
            boundFitSteps.Reconcile(fitSteps);
        }

        private void ConnectFitStep(FitStep step) => step.Changed += OnFitStepChanged;

        private void DisconnectFitStep(FitStep step) => step.Changed -= OnFitStepChanged;

        private void OnFitStepChanged(IStateChangeSource source, in StateChange change)
            => SetDirty(UniTextDirty.Layout);

        /// <summary>Rebuilds presentation data from cached glyph positions without reporting glyph movement.</summary>
        public void SetAppearanceDirty()
            => SetDirty(UniTextDirty.Mesh, UniTextCommitChanges.Appearance);

        /// <inheritdoc/>
        public override Color color
        {
            get => base.color;
            set
            {
                if (base.color == value) return;
                base.color = value;
                SetAppearanceDirty();
            }
        }

        /// <summary>Marks the specified pipeline stages dirty, conservatively assuming a mesh rebuild may move glyph faces.</summary>
        public void SetDirty(UniTextDirty flags)
            => SetDirty(flags, DefaultCommitChanges(flags));

        /// <summary>Marks pipeline work dirty and declares the observable outputs that may change when it commits.</summary>
        public void SetDirty(UniTextDirty flags, UniTextCommitChanges changes)
        {
            if (flags == UniTextDirty.None) return;
            if (changes == UniTextCommitChanges.None)
                throw new ArgumentException("A dirty pipeline pass must declare an observable change.", nameof(changes));
            CatZones.dirty.MeowFormat("[UniText] SetDirty: {0}, {1}", flags, cachedTransformData.name);
            dirtyFlags |= flags;
            if (isProcessing)
            {
                deferredDirtyFlags |= flags;
                deferredCommitChanges |= changes;
            }
            else
                pendingCommitChanges |= changes;

            if ((flags & UniTextDirty.Font) != 0 && !isProcessing) ReleaseFontProvider();

            if ((flags & UniTextDirty.FullRebuild) != 0)
            {
                textIsParsed = false;
                textProcessor?.InvalidateFirstPassData();
                InvalidateLayoutCache();
                attributeParser?.ClearPendingReapply();
            }
            else if ((flags & UniTextDirty.Layout) != 0)
            {
                textProcessor?.InvalidateLayoutData();
                InvalidateLayoutCache();
            }
            else if ((flags & UniTextDirty.Positions) != 0)
            {
                textProcessor?.InvalidatePositionedGlyphs();
            }

            RegisterDirty(this);

            DirtyFlagsChanged?.Invoke(flags);
            OnSetDirty(flags);
        }

        private static UniTextCommitChanges DefaultCommitChanges(UniTextDirty flags)
        {
            var changes = UniTextCommitChanges.None;
            if ((flags & UniTextDirty.FullRebuild) != 0)
                changes |= UniTextCommitChanges.Content | UniTextCommitChanges.Layout |
                           UniTextCommitChanges.GlyphGeometry | UniTextCommitChanges.Appearance;
            else if ((flags & UniTextDirty.Layout) != 0)
                changes |= UniTextCommitChanges.Layout | UniTextCommitChanges.GlyphGeometry |
                           UniTextCommitChanges.Appearance;
            else if ((flags & UniTextDirty.Positions) != 0)
                changes |= UniTextCommitChanges.Layout | UniTextCommitChanges.GlyphGeometry |
                           UniTextCommitChanges.Appearance;
            else if ((flags & UniTextDirty.Mesh) != 0)
                changes |= UniTextCommitChanges.GlyphGeometry | UniTextCommitChanges.Appearance;

            return changes;
        }

        private void ReleaseFontProvider()
        {
            DeinitializeAllStyles();
            fontProvider?.Dispose();
            fontProvider = null;
            DisposeMeshGenerator();
            textProcessor?.BumpShapeEpoch();
        }

        private void DisposeMeshGenerator()
        {
            if (meshGenerator == null) return;
            var current = meshGenerator;
            meshGenerator = null;
            try
            {
                MeshGeneratorChanged?.Invoke(null);
            }
            finally
            {
                current.Dispose();
            }
        }

        /// <summary>Ignores Unity's aggregate Graphic invalidation; use <see cref="SetDirty(UniTextDirty)"/>.</summary>
        public override void SetAllDirty() { }

        /// <summary>Ignores Unity's Graphic mesh queue; use <see cref="SetDirty(UniTextDirty)"/>.</summary>
        public override void SetVerticesDirty() { }

        /// <summary>Ignores Unity's Graphic material queue unless a rendering backend overrides it.</summary>
        public override void SetMaterialDirty() { }

        #endregion

        #region Modifiers

        /// <summary>
        /// Adds a standalone parse rule (one that operates without a modifier, e.g. &lt;noparse&gt;).
        /// The rule must report <see cref="ParseRule.IsStandalone"/> as <see langword="true"/>.
        /// </summary>
        /// <param name="rule">The standalone rule to add.</param>
        public void AddRule(ParseRule rule)
        {
            if (rule == null) return;
            if (!rule.IsStandalone)
            {
                Debug.LogError($"[UniText] Rule {rule.GetType().Name} is not standalone. Add a Style with a modifier instead.");
                return;
            }

            Styles.Add(new Style { Source = rule });
        }

        /// <summary>Removes a standalone rule previously added via <see cref="AddRule"/>.</summary>
        /// <param name="rule">The rule to remove.</param>
        /// <returns><see langword="true"/> if the rule was found and removed.</returns>
        public bool RemoveRule(ParseRule rule)
        {
            if (rule == null) return false;

            for (var i = 0; i < styles.Count; i++)
            {
                if (styles[i].Source == rule && styles[i].Modifier == null)
                    return Styles.Remove(styles[i]);
            }
            return false;
        }

        private void ValidateStylesMutation(in StateListMutation<Style> mutation)
        {
            for (var i = 0; i < mutation.Count; i++)
            {
                var style = mutation[i];
                BaseModifier.ValidateGraph(style.Modifier);
                RangeSource.ValidateGraph(style.Source);
            }
            Style.ValidateCollectionGraphs(in mutation);
        }

        private void ApplyStylesChange() => RuntimeStyles.ReconcileLocal();

        private void RefreshStylePresetBindings() => RuntimeStyles.ReconcilePresets();

        internal void NotifyStyleGraphChanged() => StyleGraphChanged?.Invoke();

        /// <summary>Deinitializes all modifiers but keeps them registered (for font changes).</summary>
        private void DeinitializeAllStyles()
        {
            attributeParser?.DeinitializeModifiers();
            DeinitializeProjectionModifiers();
        }

        private void DeinitializeProjectionModifiers()
        {
            var modifiers = parseProjection.chromeModifiers;
            if (modifiers == null) return;
            for (var i = 0; i < modifiers.Count; i++)
                modifiers[i].Destroy();
        }

        internal event Action StyleGraphChanged;

        internal bool EnsureAttributeParserCreated()
        {
            if (isDeinitializing) return false;
            var created = false;
            if (attributeParser == null && (styles is { Count: > 0 } || HasAnyStylePresets()))
            {
                buffers ??= new UniTextBuffers();
                var candidate = new AttributeParser(this);
                attributeParser = candidate;
                try
                {
                    RuntimeStyles.AttachParser();
                }
                catch (Exception failure)
                {
                    attributeParser = null;
                    try
                    {
                        candidate.Release();
                    }
                    catch (Exception cleanupFailure)
                    {
                        throw new AggregateException(failure, cleanupFailure);
                    }
                    throw;
                }
                created = true;
                NotifyStyleGraphChanged();
            }
            if (attributeParser != null && textProcessor != null && !attributeParserAttached)
            {
                textProcessor.Parsed.Subscribe(attributeParser.Apply);
                attributeParserAttached = true;
            }
            return created;
        }

        private bool HasAnyStylePresets()
        {
            for (var i = 0; i < stylePresets.Count; i++)
            {
                var config = stylePresets[i];
                if (config != null && config.Styles is { Count: > 0 })
                    return true;
            }
            if (!useGlobalStylePreset) return false;
            var global = UniTextSettings.GlobalStylePreset;
            return global != null && global.Styles is { Count: > 0 };
        }

        private void ValidateLocalStyles()
        {
            StateListRules.ValidateReferences(styles, false, false, nameof(styles));
            var mutation = StateListMutation<Style>.CreateReplacement(styles);
            ValidateStylesMutation(in mutation);
        }

        /// <summary>
        /// Tears down the attribute parser and runtime preset copies (when alive), then marks the
        /// component dirty (<see cref="UniTextDirty.Text"/>) so the next pipeline pass lazily
        /// rebuilds via <see cref="ValidateAndInitialize"/> → <see cref="EnsureAttributeParserCreated"/>.
        /// The dirty flag is set unconditionally — a preset that was empty at init time has no
        /// parser yet, but may have just gained styles and now needs one created.
        /// </summary>
        internal void InvalidateStyles()
        {
            ReleaseStyleRuntime();
            SetDirty(UniTextDirty.Text);
        }

        private void ReleaseStyleRuntime()
        {
            styleRuntime?.DestroyRuntime();
            if (attributeParser != null)
            {
                attributeParser.Release();
                if (textProcessor != null && attributeParserAttached)
                {
                    textProcessor.Parsed.Unsubscribe(attributeParser.Apply);
                }

                attributeParser = null;
                attributeParserAttached = false;
            }
        }

        #endregion

        #region Lifecycle

#if UNITY_EDITOR
        /// <summary>Leaves serialized invalidation to the main-thread state reconciliation path.</summary>
        protected override void OnValidate() { }

        int IEditorSerializedPropertyStateOwner.EditorSerializedPropertyCount
            => IsWorldSpace ? 1 : 3;

        void IEditorSerializedPropertyStateOwner.GetEditorSerializedProperty(int index,
            out string path, out int callback)
            => (path, callback) = index switch
            {
                0 => ("m_Color", 0),
                1 when !IsWorldSpace => ("m_RaycastTarget", 1),
                2 when !IsWorldSpace => ("m_Maskable", 2),
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };

        void IEditorSerializedPropertyStateOwner.InvokeEditorSerializedPropertyChanged(int callback)
        {
            switch (callback)
            {
                case 0:
                    SetAppearanceDirty();
                    return;
                case 1:
                    SetRaycastDirty();
                    return;
                case 2:
                    RecalculateClipping();
                    RecalculateMasking();
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(callback));
            }
        }
#endif

        protected override void OnEnable()
        {
            base.OnEnable();
            CatZones.lifecycle.Meow($"[UniText] OnEnable, {name}", this);
            if (!isTextFromBuffer) sourceText = (text ?? "").AsMemory();
            BindFitSteps();
            Sub();
            SetDirty(UniTextDirty.All);
            RefreshFrameTick();
        }

        private Action frameTickCallback;
        private TickHandle frameTickHandle;

        /// <summary>
        /// Gets whether the component currently requires a per-frame tick. Overrides extend the
        /// demand with their own per-frame state and call <see cref="RefreshFrameTick"/> whenever
        /// that state changes.
        /// </summary>
        protected virtual bool NeedsFrameTick => frameUpdated != null || pointerSessions.Count > 0;

        private bool frameTickRefreshDeferred;

        /// <summary>
        /// Aligns the <see cref="CoreLoop.Updating"/> subscription with
        /// <see cref="NeedsFrameTick"/>; call after any change to the state it reads. Off the main
        /// thread the refresh is deferred and applied when the processing sweep completes.
        /// </summary>
        protected void RefreshFrameTick()
        {
            if (!MainThread.IsCurrent)
            {
                frameTickRefreshDeferred = true;
                return;
            }
            CoreLoop.Updating.Toggle(ref frameTickHandle, frameTickCallback ??= OnFrameTick,
                isActiveAndEnabled && NeedsFrameTick);
        }

        /// <summary>
        /// Runs the component's per-frame work from <see cref="CoreLoop.Updating"/> while
        /// <see cref="NeedsFrameTick"/> holds. Overrides run their own work after the base call.
        /// </summary>
        protected virtual void OnFrameTick()
        {
            TickLongPress();
            frameUpdated?.Invoke();
        }

        protected override void OnDisable()
        {
            RefreshFrameTick();
            boundFitSteps?.Clear();
            UnSub();
            base.OnDisable();
            DeInit();
        }

        protected override void OnDestroy()
        {
            UnSub();
            DeInit(true);
            styleRuntime?.Dispose();
            attachments?.Dispose();
            attachments = null;
            rangeEntriesScratch?.Return();
            rangeEntriesScratch = null;
            boundsEntriesScratch.Return();
            stringBuilderScratch.Return();
            beforeGenerateMesh?.Release();
            base.OnDestroy();
            if (textResolver != null)
            {
                var r = textResolver;
                textResolver = null;
                hasResolvedText = false;
                resolvedText = default;
                r.OnDetached(this);
            }
        }

        /// <summary>True between <see cref="Sub"/> and <see cref="UnSub"/>; the font and font-stack
        /// subscriptions exist only inside this window — asset events survive play sessions when
        /// domain reload is off, so a delegate left on an asset outlives the component.</summary>
        private bool subscribed;

        [NonSerialized] private UniTextFont subscribedFont;
        [NonSerialized] private UniTextFontStack subscribedFontStack;

        /// <summary>
        /// Aligns the font and font-stack subscriptions with the fields while <see cref="subscribed"/>,
        /// releasing exactly the targets that were taken: a field rewritten without its callback,
        /// or an asset unloaded under the component, never strands a delegate.
        /// </summary>
        private void SyncAssetSubscriptions()
        {
            Rewire(ref subscribedFont, subscribed ? font : null, OnFontChanged);
            Rewire(ref subscribedFontStack, subscribed ? fontStack : null, OnFontStackChanged);
        }

        private static void Rewire<T>(ref T current, T target, StateChangedHandler handler)
            where T : class, IStateChangeSource
        {
            if (ReferenceEquals(current, target)) return;
            if (!ReferenceEquals(current, null)) current.Changed -= handler;
            if (!ReferenceEquals(target, null)) target.Changed += handler;
            current = target;
        }

        protected virtual void Sub()
        {
            subscribed = true;
            SyncAssetSubscriptions();
#if UNITY_EDITOR
            UnityEditor.SceneVisibilityManager.visibilityChanged += OnSceneVisibilityChanged;
            SceneVisibilityOverlay.Changed += OnSceneVisibilityChanged;
#endif
            EmojiFont.DisableChanged += OnDisableChanged;
            SystemFont.Changed += OnDisableChanged;
            GlyphAtlas.AnyAtlasCompacted += OnAtlasCompacted;
            GlyphAtlas.AnyAtlasContentLost += OnAtlasEntriesCleared;
            UniTextFont.Core.AnyAtlasEntriesCleared += OnAtlasEntriesCleared;
            UniTextSettings.Changed += OnSettingsChanged;
            RuntimeStyles.Listen();
        }

        protected virtual void UnSub()
        {
            subscribed = false;
            SyncAssetSubscriptions();
#if UNITY_EDITOR
            UnityEditor.SceneVisibilityManager.visibilityChanged -= OnSceneVisibilityChanged;
            SceneVisibilityOverlay.Changed -= OnSceneVisibilityChanged;
#endif
            EmojiFont.DisableChanged -= OnDisableChanged;
            SystemFont.Changed -= OnDisableChanged;
            GlyphAtlas.AnyAtlasCompacted -= OnAtlasCompacted;
            GlyphAtlas.AnyAtlasContentLost -= OnAtlasEntriesCleared;
            UniTextFont.Core.AnyAtlasEntriesCleared -= OnAtlasEntriesCleared;
            UniTextSettings.Changed -= OnSettingsChanged;
            styleRuntime?.Unlisten();
        }

        /// <summary>
        /// Maps exact <see cref="UniTextSettings.Changed"/> members to this component's earliest
        /// affected pipeline stage. <see cref="UniTextSettings.Affects"/> also handles complete
        /// settings-instance replacement.
        /// </summary>
        private void OnSettingsChanged(in StateChange change)
        {
            var reset = change.Kind == StateChangeKind.Reset;
            if (useGlobalStylePreset &&
                UniTextSettings.Affects(in change, UniTextSettings.Members.GlobalStylePreset))
                RuntimeStyles.ReconcilePresets(!reset);

            var fontChanged = UniTextSettings.Affects(
                in change, UniTextSettings.Members.SystemFont);
            if (fontChanged)
            {
                SharedFontCache.InvalidateAll();
            }

            var normalizationChanged =
                UniTextSettings.Affects(in change, UniTextSettings.Members.FontNormalizeMetric) ||
                UniTextSettings.Affects(in change, UniTextSettings.Members.FontNormalizeTarget);
            if (normalizationChanged)
            {
                fontProvider?.SetNormalization(UniTextSettings.FontNormalizeMetric,
                    UniTextSettings.FontNormalizeTarget);
                textProcessor?.BumpShapeEpoch();
            }

            var textChanged =
                UniTextSettings.Affects(in change, UniTextSettings.Members.Language) ||
                UniTextSettings.Affects(in change, UniTextSettings.Members.Dictionaries) ||
                normalizationChanged;
            var layoutChanged =
                UniTextSettings.Affects(in change, UniTextSettings.Members.LineHeightMode) ||
                UniTextSettings.Affects(in change, UniTextSettings.Members.LineHeightScale);
            if (fontChanged) SetDirty(UniTextDirty.Font);
            else if (textChanged) SetDirty(UniTextDirty.Text);
            else if (layoutChanged) SetDirty(UniTextDirty.Layout);
        }

        private void OnAtlasEntriesCleared()
        {
            if (textProcessor != null)
            {
                textProcessor.HasValidGlyphsInAtlas = false;
                textProcessor.buf.hasValidGlyphCache = false;
            }
            bool glyphsChanged = glyphRefs.DropMissing();
            bool colorRefsChanged = colorRefs.DropMissing();
            bool fieldRefsChanged = fieldRefs.DropMissing();
            if (!glyphsChanged && !colorRefsChanged && !fieldRefsChanged) return;
            SetAppearanceDirty();
        }

        private void OnAtlasCompacted(GpuAtlas<GlyphAtlas.GlyphEntry> compactedAtlas)
        {
            bool isMyAtlas = glyphRefs.Count > 0 && compactedAtlas == GlyphAtlas.GetInstance(RenderMode);
            bool isColorAtlas = colorRefs.Count > 0 && compactedAtlas == GlyphAtlas.Color;
            bool isFieldAtlas = fieldRefs.Count > 0
                                && GlyphAtlas.TryGetExistingInstance(UniTextRenderMode.SDF, out var fieldAtlas)
                                && compactedAtlas == fieldAtlas;
            if (!isMyAtlas && !isColorAtlas && !isFieldAtlas) return;

            CatZones.glyphAtlas.MeowFormat("[UniText] OnAtlasCompacted '{0}': regen mesh, glyphRefs={1}, colorRefs={2}, fieldRefs={3}",
                name, glyphRefs.Count, colorRefs.Count, fieldRefs.Count);

            if (textProcessor != null)
                textProcessor.buf.hasValidGlyphCache = false;
            SetAppearanceDirty();
        }

        protected void DeInit(bool isDestroying = false)
        {
            CatZones.lifecycle.MeowFormat("[UniText] DeInit '{0}': isDestroying={1}, heldKeys={2}+{3}e",
                name, isDestroying, glyphRefs.Count, colorRefs.Count);
            var wasDeinitializing = isDeinitializing;
            isDeinitializing = true;
            try
            {
                ReleaseAllGlyphAtlasRefs();
                glyphRefs.Return();
                colorRefs.Return();
                fieldRefs.Return();
                DeinitializeProjectionModifiers();
                if (!isDestroying)
                {
                    ClearAllRenderers();
                }
                InvalidateStyles();
                Deinitializing?.Invoke();
                ResetPointerSessions();

                textProcessor?.Dispose();
                textProcessor = null;
                fontProvider?.Dispose();
                fontProvider = null;
                DisposeMeshGenerator();

                OnDeInit();
                buffers?.EnsureReturnBuffers();
                UnregisterDirty(this);
            }
            finally
            {
                isDeinitializing = wasDeinitializing;
            }
        }

        /// <summary>
        /// Updates glyph atlas reference counts. AddRef all new keys first, then Release
        /// all old keys.
        /// </summary>
        private void UpdateGlyphAtlasRefCounts()
        {
            if (meshGenerator == null) return;

            glyphRefs.Update(GlyphAtlas.GetInstance(RenderMode), ref meshGenerator.usedGlyphKeys);

            var colorAtlas = GlyphAtlas.Color;
            if (colorAtlas != null)
                colorRefs.Update(colorAtlas, ref meshGenerator.usedColorKeys);
            else
                colorRefs.ReleaseAll();

            if (meshGenerator.usedFieldKeys.count > 0 || fieldRefs.Count > 0)
            {
                if (GlyphAtlas.TryGetExistingInstance(UniTextRenderMode.SDF, out var fieldAtlas))
                    fieldRefs.Update(fieldAtlas, ref meshGenerator.usedFieldKeys);
                else
                    fieldRefs.ReleaseAll();
            }

            CatZones.glyphAtlas.MeowFormat("[UniText] UpdateRefCounts '{0}': glyph={1}, color={2}, field={3}",
                name, glyphRefs.Count, colorRefs.Count, fieldRefs.Count);
        }

        private void ReleaseAllGlyphAtlasRefs()
        {
            glyphRefs.ReleaseAll();
            colorRefs.ReleaseAll();
            fieldRefs.ReleaseAll();
        }

        /// <summary>
        /// Releases this component's atlas refs at the START of a glyph re-collection, so the state is
        /// current within the frame of the change: the outgoing set's orphans park in the LRU
        /// immediately and the incoming set reuses their tiles in the SAME frame — a font swap or a
        /// variable-axis change re-rasterizes in place with ZERO atlas growth. Safe because collection
        /// enumerates every glyph the new state uses and the batch-prepare phase revives and pins any
        /// still-used refCount-0 key BEFORE any allocation can evict (all prepares run before all
        /// renders), and batch protection then holds everything until the renderer commit.
        /// </summary>
        internal void ReleaseRefsForRebuild()
        {
            glyphRefs.ReleaseAll();
            colorRefs.ReleaseAll();
            fieldRefs.ReleaseAll();
        }

        private void OnDisableChanged()
        {
            SetDirty(UniTextDirty.Font);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            var rect = GetPaddedRect();
            var width = rect.width;
            var height = rect.height;

            var widthChanged = !Mathf.Approximately(width, lastKnownWidth);
            var heightChanged = !Mathf.Approximately(height, lastKnownHeight);

            if (heightChanged)
            {
                lastKnownHeight = height;
                RectHeightChanged?.Invoke();
            }

            if (widthChanged)
            {
                lastKnownWidth = width;

                var effectiveFontSize = autoSize ? maxFontSize : fontSize;
                var canReuse = textProcessor != null && textProcessor.CanReuseLines(width, effectiveFontSize, wordWrap);

                if (canReuse)
                {
                    SetDirty(UniTextDirty.Positions);
                }
                else
                {
                    SetDirty(UniTextDirty.Layout);
                }
            }
            else
            {
                SetDirty(UniTextDirty.Positions);
            }
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            SetDirty(UniTextDirty.Layout);
        }

        private void OnFontChanged(IStateChangeSource _, in StateChange change)
        {
            if (change.Kind == StateChangeKind.Reset ||
                change.Member == UniTextFont.Members.FontDataHash ||
                change.Member == UniTextFont.Members.FaceInfo ||
                change.Member == UniTextFont.Members.UnitsPerEm ||
                change.Member == UniTextFont.Members.SdfDetailMultiplier ||
                change.Member == UniTextFont.Members.TileSizeOffset ||
                change.Member == UniTextFont.Members.GlyphOverrides ||
                change.Member == UniTextFont.Members.AxisDefaults ||
                change.Member == UniTextFontVariant.Members.Source ||
                change.Member == UniTextFontVariant.Members.FaceIndex ||
                change.Member == UniTextColorFont.Members.ColorPixelSize ||
                change.Member == UniTextSystemFont.Members.Common ||
                change.Member == UniTextSystemFont.Members.Windows ||
                change.Member == UniTextSystemFont.Members.Macos ||
                change.Member == UniTextSystemFont.Members.Linux ||
                change.Member == UniTextSystemFont.Members.Ios ||
                change.Member == UniTextSystemFont.Members.Android)
            {
                SetDirty(UniTextDirty.Font);
                return;
            }
            if (change.Member == UniTextFont.Members.ItalicStyle)
            {
                SetDirty(UniTextDirty.Mesh);
                return;
            }
            if (change.Member == UniTextFont.Members.SpacingOffset ||
                change.Member == UniTextFont.Members.SpaceAdvance ||
                change.Member == UniTextFont.Members.FakeBoldWeight ||
                change.Member == UniTextFont.Members.FontScale ||
                change.Member == UniTextFont.Members.ParticipatesInNormalization)
            {
                SetDirty(UniTextDirty.Text);
                return;
            }
            throw new ArgumentOutOfRangeException(nameof(change));
        }

        private void OnFontStackChanged(IStateChangeSource source, in StateChange change)
        {
            if (source is UniTextFont)
            {
                OnFontChanged(source, in change);
                return;
            }
            if (source is UniTextFontStack &&
                (change.Kind != StateChangeKind.Value ||
                 change.Member == UniTextFontStack.Members.Families ||
                 change.Member == UniTextFontStack.Members.FallbackStack))
            {
                SetDirty(UniTextDirty.Font);
                return;
            }
            throw new ArgumentOutOfRangeException(nameof(change));
        }

        #endregion

        #region Rebuild

        /// <inheritdoc/>
        public override void Rebuild(CanvasUpdate update) { }

        /// <inheritdoc/>
        protected override void UpdateMaterial() { }

        protected virtual bool ValidateAndInitialize()
        {
            if (isDeinitializing) return false;
            UniTextDebug.BeginSample("UniText.ValidateAndInitialize");

            buffers ??= new UniTextBuffers();
            buffers.EnsureRentBuffers(sourceText.Length);

            if (textProcessor == null)
            {
                textProcessor = new TextProcessor(buffers);
                CatZones.lifecycle.Meow("[UniText] TextProcessor created", this);
            }

            EnsureAttributeParserCreated();

            if (fontProvider == null)
            {
                fontProvider = new UniTextFontProvider(font, fontStack);
                fontProvider.SetNormalization(UniTextSettings.FontNormalizeMetric, UniTextSettings.FontNormalizeTarget);
                if(fontProvider.PrimaryFont == null)
                {
                    DeInit();
                    return false;
                }
                meshGenerator = new UniTextMeshGenerator(fontProvider, buffers);
                MeshGeneratorChanged?.Invoke(meshGenerator);
                textProcessor.SetFontProvider(fontProvider);
                CatZones.lifecycle.Meow("[UniText] FontProvider created", this);
            }

            UniTextDebug.EndSample();
            return true;
        }

        private ReadOnlySpan<char> ParseOrGetParsedAttributes()
        {
            if (!textIsParsed)
            {
                UniTextDebug.BeginSample("UniText.ParseAttributes");

                if (textResolver != null)
                    hasResolvedText = textResolver.TryResolve(sourceText, out resolvedText);
                else
                    hasResolvedText = false;

                var textToParse = hasResolvedText ? resolvedText.Span : sourceText.Span;

                if (attributedInput)
                    attributeParser?.ParseAttributed(sourceText.Span, attributedSpans, attributedProtection);
                else
                    attributeParser?.Parse(textToParse, parseProjection);
                textIsParsed = true;
                UniTextDebug.EndSample();
            }

            if (attributeParser != null) return attributeParser.CleanTextSpan;
            return hasResolvedText ? resolvedText.Span : sourceText.Span;
        }

        /// <summary>
        /// The single construction point for pipeline settings: defaults + box + font size + wrap.
        /// Alignment is applied only by the mesh path (<see cref="CreateProcessSettings(Rect, float)"/>);
        /// measurement passes are alignment-independent.
        /// </summary>
        private TextProcessSettings CreateProcessSettings(float maxWidth, float maxHeight, float fontSize, bool wrap)
        {
            var settings = new TextProcessSettings
            {
                layout = LayoutSettings.Default,
                fontSize = fontSize,
                enableWordWrap = wrap,
                baseDirection = TextDirection.Auto,
            };
            settings.layout.maxWidth = maxWidth;
            settings.layout.maxHeight = maxHeight;
            settings.layout.measureTrailingWhitespace = measureTrailingWhitespace;
            return settings;
        }

        private TextProcessSettings CreateProcessSettings(Rect rect, float effectiveFontSize)
        {
            var settings = CreateProcessSettings(rect.width, rect.height, effectiveFontSize, wordWrap);
            settings.layout.horizontalAlignment = horizontalAlignment;
            settings.layout.verticalAlignment = verticalAlignment;
            return settings;
        }

        #endregion

        #region Abstract / Virtual Contract

        /// <summary>
        /// Whether this text renders in world space (a scene mesh) rather than in a Canvas. Consumers
        /// that adapt to render space query this instead of testing for a concrete component
        /// type; <see cref="UniTextWorld"/> overrides it to <see langword="true"/>.
        /// </summary>
        public virtual bool IsWorldSpace => false;

        /// <summary>Applies generated mesh data to the rendering backend (CanvasRenderer or MeshRenderer sub-meshes).</summary>
        protected abstract void UpdateRendering();

        /// <summary>Clears all sub-mesh renderers (without destroying GameObjects).</summary>
        protected abstract void ClearAllRenderers();

        /// <summary>
        /// Applies the current <see cref="RenderSuppressed"/> state to the rendering backend. Must only toggle
        /// drawing — cull for Canvas, batch membership (degenerate indices) for world — never tear down or
        /// rebuild, so the user <see cref="Show"/>/<see cref="Hide"/> and the scene-visibility eye share one
        /// allocation-free, non-structural path.
        /// </summary>
        protected abstract void ApplyVisibility();

        /// <summary>Called after SetDirty. Override to trigger Canvas layout rebuild.</summary>
        protected virtual void OnSetDirty(UniTextDirty flags) { }

        /// <summary>Called during DeInit for subclass-specific cleanup (e.g., stencil materials).</summary>
        protected virtual void OnDeInit() { }

        /// <summary>Transform under which this component parents its generated render children (glyph sub-meshes, inline media). Main thread only — the Canvas backend lazily creates a hidden container.</summary>
        public virtual RectTransform RenderRoot => rectTransform;

#if UNITEXT_TESTS
        /// <summary>Called after meshes are applied, before ReturnInstanceBuffers. Override for test mesh copying.</summary>
        protected virtual void CopyMeshesForTests() { }
#endif

        #endregion

        #region Visibility

        private bool hidden;

        /// <summary>Combined render-suppression: user <see cref="Hide"/> or the editor scene-visibility "eye". The single state both visibility paths resolve to before <see cref="ApplyVisibility"/>.</summary>
        internal bool RenderSuppressed
        {
            get
            {
#if UNITY_EDITOR
                return hidden || sceneVisibilityHidden;
#else
                return hidden;
#endif
            }
        }

        /// <summary>
        /// Whether the text is currently drawn and hit-testable. Setting this is equivalent to
        /// calling <see cref="Show"/> (<see langword="true"/>) or <see cref="Hide"/> (<see langword="false"/>).
        /// </summary>
        public bool IsVisible
        {
            get => !hidden;
            set { if (value) Show(); else Hide(); }
        }

        /// <summary>
        /// Re-shows text previously hidden with <see cref="Hide"/>. Reuses the already-built layout and mesh, so it
        /// is allocation-free and instant — no re-parse, shaping, layout or mesh rebuild. No-op if already visible.
        /// </summary>
        public void Show()
        {
            if (!hidden) return;
            hidden = false;
            ApplyVisibilityChange();
        }

        /// <summary>
        /// Hides the text while keeping its built layout, mesh and pooled buffers intact: stops drawing and pointer
        /// hit-testing without tearing down the pipeline. Prefer this over disabling the GameObject/component or
        /// assigning empty text for text shown and hidden repeatedly (pooled lists, tooltips, HUD) — those force a
        /// full pipeline rebuild on every re-show, whereas <see cref="Show"/> after <see cref="Hide"/> is free.
        /// No-op if already hidden.
        /// </summary>
        public void Hide()
        {
            if (hidden) return;
            hidden = true;
            ApplyVisibilityChange();
        }

        private void ApplyVisibilityChange()
        {
            ApplyVisibility();
#if UNITY_EDITOR
            CoreLoop.RequestEditorFrame();
#endif
        }

        public override bool Raycast(Vector2 sp, Camera eventCamera)
        {
            if (hidden) return false;
            return base.Raycast(sp, eventCamera);
        }

        #endregion

        #region Glyph Query

        /// <summary>
        /// Collects per-line geometric runs of positioned glyphs whose clusters fall inside
        /// <c>[<paramref name="startCluster"/>, <paramref name="endCluster"/>)</c>. Output bounds
        /// are in mesh-local coordinates with X clamped to each line's measured extent. One <see cref="LineRangeEntry"/>
        /// is emitted per contiguous run within a line — multiple entries per line are possible if the
        /// matched clusters are non-contiguous in visual order.
        /// </summary>
        /// <param name="startCluster">Cluster start (inclusive).</param>
        /// <param name="endCluster">Cluster end (exclusive).</param>
        /// <param name="output">Pooled list to receive entries (cleared before use).</param>
        public void CollectRangeEntries(int startCluster, int endCluster, PooledList<LineRangeEntry> output)
        {
            output.FakeClear();

            if (textProcessor == null || endCluster <= startCluster) return;

            var lines = buffers.lines;
            var lineCount = lines.count;
            if (lineCount == 0) return;

            var glyphs = textProcessor.PositionedGlyphs;

            var startLine = SelectionHitTest.FindLineAtCodepoint(startCluster, lines);
            var endLine = SelectionHitTest.FindLineAtCodepoint(endCluster - 1, lines);

            for (var li = startLine; li <= endLine; li++)
            {
                ref readonly var line = ref lines[li];
                if (line.range.End <= startCluster || line.range.start >= endCluster) continue;
                if (line.glyphCount == 0) continue;

                var firstG = line.glyphStart;
                var lastG = firstG + line.glyphCount - 1;

                float contentLeft, contentRight;
                if (line.IsRtl)
                {
                    contentRight = glyphs[lastG].right;
                    contentLeft = contentRight - line.widthPx;
                }
                else
                {
                    contentLeft = glyphs[firstG].left;
                    contentRight = contentLeft + line.widthPx;
                }

                var emitFirstG = -1;
                var emitLastG = -1;
                var emitRtl = false;
                float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;

                for (var g = firstG; g <= lastG; g++)
                {
                    ref readonly var glyph = ref glyphs[g];
                    var inRange = glyph.cluster >= startCluster && glyph.cluster < endCluster;

                    if (inRange)
                    {
                        var rtl = buffers.IsRtlLevelAt(glyph.cluster);
                        if (emitFirstG >= 0 && emitRtl != rtl)
                        {
                            AddLineRangeEntry(output, li, emitFirstG, emitLastG,
                                minX, maxX, minY, maxY, contentLeft, contentRight);
                            emitFirstG = -1;
                        }

                        if (emitFirstG < 0)
                        {
                            emitFirstG = g;
                            emitRtl = rtl;
                            minX = glyph.left;
                            maxX = glyph.right;
                            minY = glyph.top;
                            maxY = glyph.bottom;
                        }
                        else
                        {
                            if (glyph.left < minX) minX = glyph.left;
                            if (glyph.right > maxX) maxX = glyph.right;
                            if (glyph.top < minY) minY = glyph.top;
                            if (glyph.bottom > maxY) maxY = glyph.bottom;
                        }
                        emitLastG = g;
                    }
                    else if (emitFirstG >= 0)
                    {
                        AddLineRangeEntry(output, li, emitFirstG, emitLastG,
                            minX, maxX, minY, maxY, contentLeft, contentRight);
                        emitFirstG = -1;
                    }
                }

                if (emitFirstG >= 0)
                    AddLineRangeEntry(output, li, emitFirstG, emitLastG,
                        minX, maxX, minY, maxY, contentLeft, contentRight);
            }
        }

        private static void AddLineRangeEntry(PooledList<LineRangeEntry> output, int lineIndex,
            int firstGlyph, int lastGlyph, float minX, float maxX, float minY, float maxY,
            float contentLeft, float contentRight)
        {
            var clampedMinX = minX < contentLeft ? contentLeft : minX;
            var clampedMaxX = maxX > contentRight ? contentRight : maxX;
            if (clampedMaxX <= clampedMinX) return;
            output.Add(new LineRangeEntry
            {
                lineIdx = lineIndex,
                firstGlyphIdx = firstGlyph,
                lastGlyphIdx = lastGlyph,
                minX = clampedMinX,
                maxX = clampedMaxX,
                minY = minY,
                maxY = maxY,
            });
        }

        private PooledList<LineRangeEntry> rangeEntriesScratch;
        private PooledBuffer<RangeBoundsEntry> boundsEntriesScratch;

        /// <summary>
        /// Gets bounding rectangles for a cluster range. One <see cref="Rect"/> per contiguous run
        /// of glyphs within a line that falls inside <c>[<paramref name="startCluster"/>, <paramref name="endCluster"/>)</c>.
        /// Trailing whitespace at line ends is excluded (CSS Text §4.1.3). Empty wrapped lines whose
        /// break codepoint lies inside the range receive a synthetic narrow rect for caret/selection rendering.
        /// Wrapper over <see cref="CollectRangeBounds"/> in <see cref="RangeHeight.LineBox"/> mode.
        /// </summary>
        public void GetRangeBounds(int startCluster, int endCluster, IList<Rect> results)
        {
            results.Clear();
            CollectRangeBounds(startCluster, endCluster, RangeHeight.LineBox, ref boundsEntriesScratch);
            for (var i = 0; i < boundsEntriesScratch.count; i++)
                results.Add(boundsEntriesScratch[i].rect);
        }

        /// <summary>
        /// The single per-line range-geometry walker behind <see cref="GetRangeBounds"/> and other
        /// range geometry consumers: one <see cref="RangeBoundsEntry"/> per contiguous per-line run
        /// (via <see cref="CollectRangeEntries"/>), plus a synthetic narrow rect (0.25 line-height
        /// wide) for empty lines inside the range. Vertical extent per <see cref="RangeHeight"/>:
        /// <c>LineBox</c> keeps the run's line metrics, <c>Content</c> tightens to glyph ink,
        /// <c>LineAdvance</c> expands to the full line pitch anchored at the document band origin so
        /// consecutive lines' bands share edges. The walk is clamped to the binary-searched
        /// [startLine, endLine] span; empty-line and band placement stack by
        /// <see cref="TextLine.advancePrefix"/>, and <c>LineAdvance</c> keeps the run's line metrics
        /// while <see cref="UniTextBuffers.HasLineAdvances"/> is false.
        /// </summary>
        internal void CollectRangeBounds(int startCluster, int endCluster, RangeHeight height,
            ref PooledBuffer<RangeBoundsEntry> results)
        {
            results.FakeClear();

            if (textProcessor == null || endCluster <= startCluster) return;

            var lines = buffers.lines;
            if (lines.count == 0) return;

            rangeEntriesScratch ??= new PooledList<LineRangeEntry>(8);
            CollectRangeEntries(startCluster, endCluster, rangeEntriesScratch);

            var rect = cachedTransformData.rect;
            var glyphs = textProcessor.PositionedGlyphs;
            var referenceLineHeight = glyphs.Length > 0
                ? glyphs[0].bottom - glyphs[0].top
                : CurrentFontSize;

            var startLine = SelectionHitTest.FindLineAtCodepoint(startCluster, lines);
            var endLine = SelectionHitTest.FindLineAtCodepoint(endCluster - 1, lines);

            var entryIdx = 0;
            var entryCount = rangeEntriesScratch.Count;
            var bandOrigin = SelectionHitTest.FirstBandTop(glyphs, buffers);
            var lineAdvance = height == RangeHeight.LineAdvance;

            for (var li = startLine; li <= endLine; li++)
            {
                ref readonly var line = ref lines[li];

                if (line.glyphCount == 0)
                {
                    if (line.range.start >= startCluster && line.range.start < endCluster)
                    {
                        var emptyTop = li > 0 ? LineBandTop(li, bandOrigin) : 0f;
                        var emptyH = referenceLineHeight;
                        if (line.advance > 0)
                            emptyH = line.advance;
                        else if (li > 0)
                            emptyH = lines[li - 1].advance;

                        var spaceW = emptyH * 0.25f;
                        results.Add(new RangeBoundsEntry
                        {
                            rect = new Rect(rect.xMin, rect.yMax - emptyTop - emptyH, spaceW, emptyH),
                            lineIndex = li,
                            rtl = line.IsRtl,
                            firstGlyphIndex = -1,
                            lastGlyphIndex = -1,
                        });
                    }
                    continue;
                }

                while (entryIdx < entryCount && rangeEntriesScratch[entryIdx].lineIdx == li)
                {
                    var e = rangeEntriesScratch[entryIdx];
                    var minY = e.minY;
                    var maxY = e.maxY;
                    if (height == RangeHeight.Content)
                        ComputeInkExtents(this, glyphs, e.firstGlyphIdx, e.lastGlyphIdx, ref minY, ref maxY);
                    else if (lineAdvance && buffers.HasLineAdvances)
                    {
                        minY = LineBandTop(li, bandOrigin);
                        maxY = bandOrigin + line.advancePrefix;
                    }

                    results.Add(new RangeBoundsEntry
                    {
                        rect = new Rect(rect.xMin + e.minX, rect.yMax - maxY, e.maxX - e.minX, maxY - minY),
                        lineIndex = li,
                        rtl = line.IsRtl,
                        firstGlyphIndex = e.firstGlyphIdx,
                        lastGlyphIndex = e.lastGlyphIdx,
                    });
                    entryIdx++;
                }
            }

        }

        /// <summary>
        /// Top of line <paramref name="li"/>'s vertical band in text space: the document band origin
        /// plus the stacked advances of every preceding line, so empty-line rects and
        /// <see cref="RangeHeight.LineAdvance"/> bands share one anchoring for the whole layout
        /// (the same band model <c>SelectionHitTest.FindLineAtTextY</c> hit-tests with).
        /// </summary>
        private float LineBandTop(int li, float bandOrigin)
        {
            if (li <= 0) return bandOrigin;
            return bandOrigin + buffers.lines[li - 1].advancePrefix;
        }

        /// <summary>
        /// Tightens a run's vertical extent to the union of its glyphs' ink boxes, using the same
        /// atlas metrics and <c>MetricScale</c> the mesh generator positions quads with. Glyphs
        /// without usable ink data (emoji, unresolved fonts) contribute their line-metric box; a
        /// run with no ink at all (whitespace) keeps the incoming line-metric extent.
        /// </summary>
        internal static void ComputeInkExtents(UniTextBase text, ReadOnlySpan<PositionedGlyph> glyphs,
            int firstGlyph, int lastGlyph, ref float minY, ref float maxY)
        {
            var atlas = GlyphAtlas.GetInstance(text.RenderMode);
            var fontProvider = text.FontProvider;
            if (atlas == null || fontProvider == null) return;

            var buffers = text.Buffers;
            var fontSize = text.CurrentFontSize;
            var lastFontId = int.MinValue;
            var skipFont = true;
            var metricScale = 0f;
            long varHash = 0;
            var inkMin = float.MaxValue;
            var inkMax = float.MinValue;

            for (var g = firstGlyph; g <= lastGlyph; g++)
            {
                ref readonly var glyph = ref glyphs[g];
                if (glyph.fontId != lastFontId)
                {
                    lastFontId = glyph.fontId;
                    var font = fontProvider.GetFont(lastFontId);
                    skipFont = font == null || font.IsColor;
                    if (!skipFont)
                    {
                        metricScale = fontProvider.MetricScale(font, fontSize);
                        varHash = buffers.ResolveVarHash48(lastFontId, font);
                    }
                }

                if (skipFont)
                {
                    if (glyph.top < inkMin) inkMin = glyph.top;
                    if (glyph.bottom > inkMax) inkMax = glyph.bottom;
                    continue;
                }

                if (!atlas.TryGetEntry(GlyphAtlas.MakeKey(varHash, (uint)glyph.glyphId), out var entry))
                    continue;

                var inkHeight = entry.metrics.height * metricScale;
                if (inkHeight <= 0f) continue;

                var top = glyph.y - entry.metrics.horizontalBearingY * metricScale;
                var bottom = top + inkHeight;
                if (top < inkMin) inkMin = top;
                if (bottom > inkMax) inkMax = bottom;
            }

            if (inkMin < inkMax)
            {
                minY = inkMin;
                maxY = inkMax;
            }
        }

        /// <summary>Gets the total number of glyphs.</summary>
        public int GlyphCount => textProcessor?.PositionedGlyphs.Length ?? 0;

        #endregion

#if UNITY_EDITOR

        internal bool sceneVisibilityHidden;

        private void OnSceneVisibilityChanged()
        {
            if (this == null) return;
            var hidden = SceneVisibilityOverlay.Respect
                         && UnityEditor.SceneVisibilityManager.instance.IsHidden(gameObject);
            if (hidden == sceneVisibilityHidden) return;
            sceneVisibilityHidden = hidden;
            ApplyVisibilityChange();
        }

#endif
    }
}
