using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Configuration settings for text processing operations.
    /// </summary>
    /// <remarks>
    /// This struct encapsulates all parameters needed by <see cref="TextProcessor"/> to process,
    /// shape, and lay out Unicode text. It combines layout settings with text-specific options
    /// like font size and base direction.
    /// </remarks>
    /// <seealso cref="TextProcessor"/>
    /// <seealso cref="LayoutSettings"/>
    public struct TextProcessSettings
    {
        /// <summary>
        /// Maximum float value used to represent unlimited width or height.
        /// </summary>
        public const float FloatMax = 32767f;

        /// <summary>
        /// Layout settings including alignment, max dimensions, and spacing.
        /// </summary>
        public LayoutSettings layout;

        /// <summary>
        /// Font size in points for text rendering.
        /// </summary>
        public float fontSize;

        /// <summary>
        /// Base paragraph direction for bidirectional text processing.
        /// </summary>
        /// <remarks>
        /// When set to <see cref="TextDirection.Auto"/>, the direction is determined
        /// automatically from the text content using the Unicode BiDi algorithm (UAX #9).
        /// </remarks>
        public TextDirection baseDirection;

        /// <summary>
        /// Gets or sets a value indicating whether word wrapping is enabled.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to wrap text at word boundaries when exceeding
        /// <see cref="LayoutSettings.maxWidth"/>; otherwise, <see langword="false"/>.
        /// </value>
        public bool enableWordWrap;
    }

    /// <summary>Mutable alignment state for one line during layout.</summary>
    public struct LineStyleContext
    {
        /// <summary>Zero-based line index.</summary>
        public int lineIndex;

        /// <summary>First cluster belonging to the line.</summary>
        public int startCluster;

        /// <summary>Exclusive end cluster of the line.</summary>
        public int endCluster;

        /// <summary>Resolved horizontal alignment.</summary>
        public HorizontalAlignment alignment;

        /// <summary>Resolved justification mode.</summary>
        public TextJustify justify;

        /// <summary>Resolved alignment override for the final line.</summary>
        public LastLineAlignment lastLine;

        /// <summary>Smallest word-separator width justification compression may reach, as a fraction of natural width.</summary>
        public float wordSpaceMin;

        /// <summary>Largest word-separator width justification expansion spends before the letter and glyph levers, as a fraction of natural width.</summary>
        public float wordSpaceMax;

        /// <summary>Largest letter-spacing reduction justification compression may spend, in em (non-positive).</summary>
        public float letterSpaceMin;

        /// <summary>Largest letter-spacing addition justification expansion may spend, in em (non-negative).</summary>
        public float letterSpaceMax;

        /// <summary>Smallest glyph-width fraction justification compression may reach; <c>1</c> disables the glyph lever.</summary>
        public float glyphScaleMin;

        /// <summary>Largest glyph-width fraction justification expansion may reach; <c>1</c> disables the glyph lever.</summary>
        public float glyphScaleMax;
    }

    /// <summary>Mutable line-height mode and scale for one line.</summary>
    public struct LineHeightModeContext
    {
        /// <summary>Zero-based line index.</summary>
        public int lineIndex;

        /// <summary>First cluster belonging to the line.</summary>
        public int startCluster;

        /// <summary>Exclusive end cluster of the line.</summary>
        public int endCluster;

        /// <summary>Resolved line-height mode.</summary>
        public LineHeightMode mode;

        /// <summary>Resolved line-height scale.</summary>
        public float scale;
    }

    /// <summary>Mutable vertical advance for one line.</summary>
    public struct LineHeightContext
    {
        /// <summary>Zero-based line index.</summary>
        public int lineIndex;

        /// <summary>First cluster belonging to the line.</summary>
        public int startCluster;

        /// <summary>Exclusive end cluster of the line.</summary>
        public int endCluster;

        /// <summary>Font size used by the current layout pass.</summary>
        public float fontSize;

        /// <summary>Resolved vertical advance.</summary>
        public float lineAdvance;
    }

    /// <summary>
    /// Processes Unicode text through script analysis, BiDi reordering, shaping, and layout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="TextProcessor"/> is the main entry point for the text processing pipeline.
    /// It orchestrates multiple Unicode algorithms to produce correctly shaped and positioned glyphs.
    /// </para>
    /// <para>
    /// <b>Processing pipeline:</b>
    /// </para>
    /// <list type="number">
    /// <item><description>Parsing — converts UTF-16 to codepoints</description></item>
    /// <item><description>Script analysis (UAX #24) — identifies script per codepoint</description></item>
    /// <item><description>BiDi algorithm (UAX #9) — determines text direction and reordering</description></item>
    /// <item><description>Itemization — splits text into runs by script, direction, and font</description></item>
    /// <item><description>Shaping — converts codepoints to positioned glyphs via HarfBuzz</description></item>
    /// <item><description>Line breaking (UAX #14) — determines line break opportunities</description></item>
    /// <item><description>Layout — positions glyphs according to alignment settings</description></item>
    /// </list>
    /// <para>
    /// <b>Performance:</b> The processor caches intermediate results. Use invalidation methods
    /// only when necessary to avoid redundant processing.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var buffers = new UniTextBuffers();
    /// var processor = new TextProcessor(buffers);
    /// processor.SetFontProvider(fontProvider);
    ///
    /// var settings = new TextProcessSettings
    /// {
    ///     fontSize = 24f,
    ///     enableWordWrap = true
    /// };
    /// settings.layout.maxWidth = 400f;
    ///
    /// processor.EnsureFirstPass(text, settings);
    /// processor.EnsureLines(settings.layout.maxWidth, settings.fontSize, settings.enableWordWrap);
    /// processor.EnsurePositions(settings);
    ///
    /// // Access results
    /// var glyphs = processor.PositionedGlyphs;
    /// </code>
    /// </example>
    /// <seealso cref="UniTextBuffers"/>
    /// <seealso cref="TextProcessSettings"/>
    /// <seealso href="https://unicode.org/reports/tr9/">UAX #9: Unicode Bidirectional Algorithm</seealso>
    /// <seealso href="https://unicode.org/reports/tr14/">UAX #14: Unicode Line Breaking Algorithm</seealso>
    /// <seealso href="https://unicode.org/reports/tr24/">UAX #24: Unicode Script Property</seealso>
    public sealed partial class TextProcessor : IDisposable
    {
        #region Cache types & constants

        /// <summary>Tolerance for matching cached float-keyed inputs (width, fontSize, height) when deciding cache reuse.</summary>
        private const float CacheEpsilon = 0.001f;

        /// <summary>Cache key for line-breaking results — invalidates on width / fontSize / wordWrap change.</summary>
        private struct LinesCacheKey
        {
            public bool valid;
            public float width;
            public float fontSize;
            public bool wordWrap;

            public static LinesCacheKey Empty => new() { width = -1, fontSize = -1 };

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Matches(float width, float fontSize, bool wordWrap) =>
                valid &&
                Math.Abs(this.width - width) < CacheEpsilon &&
                Math.Abs(this.fontSize - fontSize) < CacheEpsilon &&
                this.wordWrap == wordWrap;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Set(float width, float fontSize, bool wordWrap)
            {
                this.width = width;
                this.fontSize = fontSize;
                this.wordWrap = wordWrap;
                valid = true;
            }

            public void Invalidate()
            {
                valid = false;
                width = -1;
                fontSize = -1;
                wordWrap = false;
            }
        }

        /// <summary>Cache key for final glyph positions.</summary>
        private struct PositionsCacheKey
        {
            public bool valid;
            public float maxHeight;
            public HorizontalAlignment hAlign;
            public VerticalAlignment vAlign;
            public TextJustify textJustify;
            public LastLineAlignment lastLineAlignment;
            public bool measureTrailingWhitespace;

            public static PositionsCacheKey Empty => new() { maxHeight = -1 };

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Matches(float maxHeight, HorizontalAlignment hAlign, VerticalAlignment vAlign,
                TextJustify textJustify, LastLineAlignment lastLineAlignment, bool measureTrailingWhitespace)
            {
                if (!valid) return false;
                var heightMatches = (float.IsInfinity(this.maxHeight) && float.IsInfinity(maxHeight))
                                    || Math.Abs(this.maxHeight - maxHeight) < CacheEpsilon;
                return heightMatches && this.hAlign == hAlign && this.vAlign == vAlign
                    && this.textJustify == textJustify && this.lastLineAlignment == lastLineAlignment
                    && this.measureTrailingWhitespace == measureTrailingWhitespace;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Set(float maxHeight, HorizontalAlignment hAlign, VerticalAlignment vAlign,
                TextJustify textJustify, LastLineAlignment lastLineAlignment, bool measureTrailingWhitespace)
            {
                this.maxHeight = maxHeight;
                this.hAlign = hAlign;
                this.vAlign = vAlign;
                this.textJustify = textJustify;
                this.lastLineAlignment = lastLineAlignment;
                this.measureTrailingWhitespace = measureTrailingWhitespace;
                valid = true;
            }

            public void Invalidate()
            {
                valid = false;
                maxHeight = -1;
            }
        }

        /// <summary>Cached line-height metrics keyed by fontSize. Reusable only when lineSpacing is 0.</summary>
        private struct LineHeightCache
        {
            public float fontSize;
            public float rawHeight;
            public float mainAscender;
            public float mainDescender;
            public float effectiveFirstLineHeight;
            public float effectiveLastLineHeight;
            public LineHeightMode lineHeightMode;
            public float lineHeightScale;
            public float fitLineHeightScale;

            public static LineHeightCache Empty => new() { fontSize = -1 };

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MatchesFor(float fontSize, float lineSpacing, LineHeightMode lineHeightMode, float lineHeightScale,
                float fitLineHeightScale) =>
                Math.Abs(this.fontSize - fontSize) < CacheEpsilon && lineSpacing == 0f
                && this.lineHeightMode == lineHeightMode
                && Math.Abs(this.fitLineHeightScale - fitLineHeightScale) < CacheEpsilon
                && (lineHeightMode != LineHeightMode.Scaled || Math.Abs(this.lineHeightScale - lineHeightScale) < CacheEpsilon);
        }

        #endregion

        #region Shared services

        private static BidiEngine BidiEngine => SharedPipelineComponents.BidiEngine;
        private static ScriptAnalyzer ScriptAnalyzer => SharedPipelineComponents.ScriptAnalyzer;
        private static GraphemeBreaker GraphemeBreaker => SharedPipelineComponents.GraphemeBreaker;
        private static Shaper Shaper => SharedPipelineComponents.Shaper;
        private static LineBreaker LineBreaker => SharedPipelineComponents.LineBreaker;
        private static TextLayout Layout => SharedPipelineComponents.Layout;

        #endregion

        #region Fields

        /// <summary>The buffer container holding all intermediate and final processing results.</summary>
        public readonly UniTextBuffers buf;

        private UniTextFontProvider fontProvider;
        private float resultWidth;
        private float resultHeight;
        private float firstLineTop;
        private float lastLineBottom;

        private bool hasValidFirstPassData;
        private bool hasValidGlyphsInAtlas;

        private PooledBuffer<int> fontIdOverrides;
        private PooledBuffer<float> effectiveWidths;
        private PooledBuffer<int> suppressedLayoutBreaks;
        private byte pendingHiddenMask;
        private byte layoutHiddenMask;
        private float lineWidthReserve;
        private bool collectingHiddenLayout;
        private bool applyingHiddenLayout;
        private bool hasFontIdOverrides;

        private byte cachedSettingsLanguageIndex;
        private string cachedSettingsLanguageTag;

        /// <summary>Exact variation instances produced by cluster font resolution and consumed by shaping and rasterization.</summary>
        private Dictionary<int, VariationRunInfo> variationMap;

        private LinesCacheKey linesCache = LinesCacheKey.Empty;
        private PositionsCacheKey positionsCache = PositionsCacheKey.Empty;
        private LineHeightCache heightCache = LineHeightCache.Empty;

        private TextProcessSettings lastSettings;

        private ParagraphShapeCache shapeCache;

        /// <summary>
        /// Mixed into every paragraph fingerprint. Bumped when shaping inputs outside the hashed
        /// buffers change — font provider identity, font asset dirt — so all cached paragraphs
        /// re-shape on the next pass.
        /// </summary>
        private int shapeEpoch;

        #endregion

        #region Events

        private OrderedEvent parsed;

        /// <summary>Runs after parsing and before shaping, in ascending subscriber order.</summary>
        public OrderedEvent Parsed => parsed ??= new OrderedEvent();

        private OrderedEvent analyzed;

        /// <summary>
        /// Runs once bidi levels, scripts, break opportunities, grapheme breaks and word boundaries
        /// are final, and before itemization reads them. The phase for work that needs analysis
        /// results yet must still reach shaping — hiding clusters from it, above all.
        /// </summary>
        public OrderedEvent Analyzed => analyzed ??= new OrderedEvent();

        private OrderedEvent shaped;

        /// <summary>Runs after shaping and before shaped advances are consumed.</summary>
        public OrderedEvent Shaped => shaped ??= new OrderedEvent();

        private OrderedEvent linesBroken;
        private float currentLineBreakWidth;

        /// <summary>
        /// Runs after lines are broken or remeasured and before positioning, including forced rewraps.
        /// </summary>
        public OrderedEvent LinesBroken => linesBroken ??= new OrderedEvent();

        private OrderedEvent<TextProcessSettings> configureSettings;

        /// <summary>Mutates paragraph-level settings before each processing stage consumes them.</summary>
        public OrderedEvent<TextProcessSettings> ConfigureSettings
            => configureSettings ??= new OrderedEvent<TextProcessSettings>();

        private OrderedEvent<LineStyleContext> resolveLineStyle;

        /// <summary>Resolves paragraph-scoped alignment for each line.</summary>
        public OrderedEvent<LineStyleContext> OnResolveLineStyle
            => resolveLineStyle ??= new OrderedEvent<LineStyleContext>();

        private OrderedEvent<LineHeightModeContext> resolveLineHeight;

        /// <summary>Resolves line-height mode and scale before each line's base height is computed.</summary>
        public OrderedEvent<LineHeightModeContext> OnResolveLineHeight
            => resolveLineHeight ??= new OrderedEvent<LineHeightModeContext>();

        private OrderedEvent layoutComplete;

        /// <summary>Runs after final glyph positions have been calculated.</summary>
        public OrderedEvent LayoutComplete => layoutComplete ??= new OrderedEvent();

        private OrderedEvent<LineHeightContext> calculateLineHeight;

        /// <summary>Mutates the vertical advance calculated for each line.</summary>
        public OrderedEvent<LineHeightContext> OnCalculateLineHeight
            => calculateLineHeight ??= new OrderedEvent<LineHeightContext>();

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of the <see cref="TextProcessor"/> class.
        /// </summary>
        /// <param name="uniTextBuffers">The buffer container for processing data.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="uniTextBuffers"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Required UniText settings or Unicode data are unavailable.
        /// </exception>
        public TextProcessor(UniTextBuffers uniTextBuffers)
        {
            buf = uniTextBuffers ?? throw new ArgumentNullException(nameof(uniTextBuffers));
            UnicodeData.EnsureInitialized();
        }

        /// <summary>
        /// Sets the font provider used for font lookup and glyph metrics.
        /// Must be called with a non-null provider before <see cref="EnsureFirstPass"/>.
        /// </summary>
        /// <param name="provider">The font provider to use.</param>
        public void SetFontProvider(UniTextFontProvider provider)
        {
            if (fontProvider != provider)
            {
                hasValidGlyphsInAtlas = false;
                shapeEpoch++;
            }
            fontProvider = provider;
        }

        /// <summary>Invalidates every cached paragraph shaping result; the next pass re-shapes all of them. Call when font state changes without the hashed pipeline inputs changing (font asset swap, fallback registration).</summary>
        internal void BumpShapeEpoch() => shapeEpoch++;

        /// <summary>Returns the pooled arrays held by the paragraph caches and any unspliced shape jobs. Pair with <see cref="UniTextBuffers.EnsureReturnBuffers"/> at component teardown.</summary>
        internal void ReleaseParagraphCaches()
        {
            shapeCache?.Clear();
            analysisCache?.Clear();
            ReturnPendingShapeArrays();
            pendingShapes.Return();
            ReturnPendingAnalysisArrays();
            pendingAnalyses.Return();
        }

        /// <summary>Releases pooled processing state and ordered callback storage.</summary>
        public void Dispose()
        {
            ReleaseParagraphCaches();
            fontIdOverrides.Return();
            effectiveWidths.Return();
            suppressedLayoutBreaks.Return();
            parsed?.Release();
            analyzed?.Release();
            shaped?.Release();
            linesBroken?.Release();
            layoutComplete?.Release();
            configureSettings?.Release();
            resolveLineStyle?.Release();
            resolveLineHeight?.Release();
            calculateLineHeight?.Release();
        }

        /// <summary>Returns job-owned arrays from an exception-aborted pass and invalidates every result that could depend on its partial state.</summary>
        internal void AbortFirstPass()
        {
            ReturnPendingShapeArrays();
            ReturnPendingAnalysisArrays();
            InvalidateFirstPassData();
        }

        #endregion

        #region Public state

        /// <summary>
        /// Gets a value indicating whether valid first pass data (parsing, BiDi, shaping) is available.
        /// </summary>
        /// <value>
        /// <see langword="true"/> if first pass processing has completed successfully;
        /// otherwise, <see langword="false"/>.
        /// </value>
        public bool HasValidFirstPassData => hasValidFirstPassData;

        /// <summary>
        /// Gets a value indicating whether valid positioned glyphs are available.
        /// </summary>
        /// <value>
        /// <see langword="true"/> if layout has completed and <see cref="PositionedGlyphs"/>
        /// contains valid data; otherwise, <see langword="false"/>.
        /// </value>
        public bool HasValidPositionedGlyphs => positionsCache.valid;

        /// <summary>
        /// Gets the actual width of the laid out text.
        /// </summary>
        /// <value>The width in pixels after layout, or 0 if layout has not completed.</value>
        public float ResultWidth => resultWidth;

        /// <summary>
        /// Gets the actual height of the laid out text.
        /// </summary>
        /// <value>The height in pixels after layout, or 0 if layout has not completed.</value>
        public float ResultHeight => resultHeight;

        internal float FirstLineTop => firstLineTop;

        internal float LastLineBottom => lastLineBottom;

        /// <summary>
        /// Gets the positioned glyphs ready for rendering.
        /// </summary>
        /// <value>
        /// A read-only span of <see cref="PositionedGlyph"/> containing final glyph positions,
        /// or an empty span if <see cref="HasValidPositionedGlyphs"/> is <see langword="false"/>.
        /// </value>
        /// <remarks>
        /// Access this property after calling <see cref="EnsurePositions"/> to get the final
        /// glyph data for rendering to a mesh or texture.
        /// </remarks>
        public ReadOnlySpan<PositionedGlyph> PositionedGlyphs => buf.positionedGlyphs.Span;

        #endregion

        #region Internal API (atlas integration)

        internal UniTextFontProvider FontProviderForAtlas => fontProvider;
        internal bool HasValidGlyphsInAtlas { get => hasValidGlyphsInAtlas; set => hasValidGlyphsInAtlas = value; }

        #endregion

        #region Invalidation

        /// <summary>
        /// Invalidates all cached processing data, forcing a complete reprocess on next call.
        /// </summary>
        /// <remarks>
        /// Call this method when the text content changes. This invalidates first pass data
        /// (parsing, BiDi, shaping) and all dependent layout data.
        /// </remarks>
        public void InvalidateFirstPassData()
        {
            hasValidFirstPassData = false;
            hasValidGlyphsInAtlas = false;
            InvalidateLayoutData();
        }

        /// <summary>
        /// Invalidates cached layout data while preserving shaping results.
        /// </summary>
        /// <remarks>
        /// Call this method when layout parameters change (width, font size, word wrap)
        /// but the text content remains the same. Shaping data is preserved.
        /// </remarks>
        public void InvalidateLayoutData()
        {
            linesCache.Invalidate();
            positionsCache.Invalidate();
            fitBreakValid = false;
        }

        /// <summary>
        /// Invalidates cached glyph positions while preserving line break data.
        /// </summary>
        /// <remarks>
        /// Call this method when alignment or max height changes but line breaks remain valid.
        /// </remarks>
        public void InvalidatePositionedGlyphs()
        {
            positionsCache.Invalidate();
        }

        #endregion

        #region Ensure (pipeline entry points)

        /// <summary>
        /// Ensures the first pass processing (parsing, BiDi, shaping) is complete.
        /// </summary>
        /// <param name="text">The Unicode text to process.</param>
        /// <param name="settings">The processing settings including font size and direction.</param>
        /// <remarks>
        /// <para>
        /// This method performs the first pass of text processing if not already cached:
        /// parsing, script analysis, BiDi analysis, itemization, and shaping.
        /// </para>
        /// <para>
        /// If <see cref="HasValidFirstPassData"/> is <see langword="true"/>, this method
        /// returns immediately without reprocessing.
        /// </para>
        /// </remarks>
        public void EnsureFirstPass(ReadOnlySpan<char> text, TextProcessSettings settings)
        {
            if (!EnsureFirstPassBegin(text, settings)) return;
            for (var i = 0; i < PendingShapeJobCount; i++)
                RunShapeJob(i);
            EnsureFirstPassFinish();
        }

        /// <summary>
        /// Pump-facing first-pass opener: validity/empty guards plus <c>BeginFirstPass</c>. True means shape
        /// jobs are queued and <see cref="EnsureFirstPassFinish"/> MUST follow after they run (on any threads);
        /// false means this pass is already complete or empty and neither jobs nor Finish apply.
        /// </summary>
        internal bool EnsureFirstPassBegin(ReadOnlySpan<char> text, TextProcessSettings settings)
        {
            UniTextDebug.Increment(ref UniTextDebug.TextProcessor_EnsureShapingCount);

            if (hasValidFirstPassData) return false;

            UniTextDebug.BeginSample("TextProcessor.EnsureShaping");

            DropFitState();
            buf.Reset();

            if (text.IsEmpty)
            {
                ResolveEmptyBaseDirection(settings);
                hasValidFirstPassData = false;
                UniTextDebug.EndSample();
                return false;
            }

            fontProvider.SetFontSize(settings.fontSize);
            var began = BeginFirstPass(text, settings);
            UniTextDebug.EndSample();
            return began;
        }

        /// <summary>Parallel-path opener half A: guards + <see cref="BeginFirstPassA"/> (parse + analysis prepare). True = analysis jobs queued and <see cref="EnsureFirstPassBeginB"/> MUST follow after they run (on any threads).</summary>
        internal bool EnsureFirstPassBeginA(ReadOnlySpan<char> text, TextProcessSettings settings)
        {
            if (hasValidFirstPassData) return false;
            DropFitState();
            buf.Reset();
            if (text.IsEmpty) { ResolveEmptyBaseDirection(settings); hasValidFirstPassData = false; return false; }
            fontProvider.SetFontSize(settings.fontSize);
            return BeginFirstPassA(text, settings);
        }

        /// <summary>Parallel-path opener half B: <see cref="BeginFirstPassB"/> (analysis finish + font faces + shape prepare). Call only after A returned true and its analysis jobs completed.</summary>
        internal void EnsureFirstPassBeginB() => BeginFirstPassB();

        internal void EnsureFirstPassFinish() => FinishFirstPass();

        /// <summary>
        /// Determines whether cached line data can be reused for the specified parameters.
        /// </summary>
        /// <param name="width">The maximum width for line breaking.</param>
        /// <param name="fontSize">The font size in points.</param>
        /// <param name="wordWrap">Whether word wrapping is enabled.</param>
        /// <returns>
        /// <see langword="true"/> if cached line data matches the parameters;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanReuseLines(float width, float fontSize, bool wordWrap)
        {
            return linesCache.Matches(width, fontSize, wordWrap);
        }

        /// <summary>
        /// Ensures line breaking is complete for the specified parameters.
        /// </summary>
        /// <param name="width">The maximum width for line breaking in pixels.</param>
        /// <param name="fontSize">The font size in points.</param>
        /// <param name="wordWrap">Whether to wrap text at word boundaries.</param>
        /// <remarks>
        /// <para>
        /// Requires <see cref="EnsureFirstPass"/> to be called first.
        /// If parameters match cached values, returns immediately.
        /// </para>
        /// </remarks>
        public void EnsureLines(float width, float fontSize, bool wordWrap)
        {
            if (!hasValidFirstPassData) return;
            if (CanReuseLines(width, fontSize, wordWrap)) return;

            UniTextDebug.BeginSample("TextProcessor.EnsureLines");
            EnsureLinesInternal(width, fontSize, wordWrap, buf.cpWidths.Span);
            UniTextDebug.EndSample();
        }

        /// <summary>
        /// Determines whether cached glyph positions can be reused for the specified parameters.
        /// </summary>
        /// <param name="maxHeight">The maximum height for layout.</param>
        /// <param name="hAlign">The horizontal alignment.</param>
        /// <param name="vAlign">The vertical alignment.</param>
        /// <param name="textJustify">The justification mode.</param>
        /// <param name="lastLineAlignment">The last-line alignment when justifying.</param>
        /// <returns>
        /// <see langword="true"/> if cached positions match the parameters;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanReusePositions(float maxHeight, HorizontalAlignment hAlign, VerticalAlignment vAlign,
            TextJustify textJustify, LastLineAlignment lastLineAlignment)
            => CanReusePositions(maxHeight, hAlign, vAlign, textJustify, lastLineAlignment, false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanReusePositions(float maxHeight, HorizontalAlignment hAlign, VerticalAlignment vAlign,
            TextJustify textJustify, LastLineAlignment lastLineAlignment, bool measureTrailingWhitespace)
        {
            return positionsCache.Matches(maxHeight, hAlign, vAlign, textJustify, lastLineAlignment,
                measureTrailingWhitespace);
        }

        /// <summary>
        /// Ensures final glyph positioning is complete for the specified settings.
        /// </summary>
        /// <param name="settings">The layout settings including alignment and max dimensions.</param>
        /// <remarks>
        /// <para>
        /// Requires <see cref="EnsureLines"/> to be called first.
        /// If parameters match cached values, returns immediately.
        /// </para>
        /// <para>
        /// After this method completes successfully, <see cref="PositionedGlyphs"/>
        /// contains the final glyph data ready for rendering.
        /// </para>
        /// </remarks>
        public void EnsurePositions(TextProcessSettings settings)
        {
            if (!linesCache.valid) return;
            configureSettings?.Invoke(ref settings);
            if (CanReusePositions(settings.layout.maxHeight, settings.layout.horizontalAlignment, settings.layout.verticalAlignment,
                    settings.layout.textJustify, settings.layout.lastLineAlignment,
                    settings.layout.measureTrailingWhitespace)) return;

            UniTextDebug.BeginSample("TextProcessor.EnsurePositions");

            lastSettings = settings;
            buf.positionedGlyphs.count = 0;
            LayoutText(settings);

            positionsCache.Set(settings.layout.maxHeight, settings.layout.horizontalAlignment, settings.layout.verticalAlignment,
                settings.layout.textJustify, settings.layout.lastLineAlignment,
                settings.layout.measureTrailingWhitespace);

            layoutComplete?.Invoke();

            UniTextDebug.EndSample();
        }

        #endregion

        #region Forced re-runs (for modifiers that mutate widths/glyphs after layout)

        /// <summary>
        /// Forces a complete relayout using custom codepoint widths.
        /// </summary>
        /// <param name="cpWidths">Custom widths for each codepoint, used for layout calculations.</param>
        /// <remarks>
        /// Use this method when codepoint widths have been modified externally (e.g., by modifiers)
        /// and the layout needs to be recalculated. Preserves shaping data but recalculates
        /// line breaks and positions.
        /// </remarks>
        public void ForceRelayout(ReadOnlySpan<float> cpWidths)
        {
            if (!hasValidFirstPassData) return;

            InvalidateLayoutData();
            EnsureLinesInternal(lastSettings.layout.maxWidth, lastSettings.fontSize, lastSettings.enableWordWrap, cpWidths);
            EnsurePositions(lastSettings);
        }

        /// <summary>Requests one final line break that excludes clusters carrying <paramref name="mask"/>.</summary>
        /// <remarks>Valid only while handling the first <see cref="LinesBroken"/> notification.</remarks>
        internal void RebreakHidden(byte mask)
        {
            if (!hasValidFirstPassData || !collectingHiddenLayout || applyingHiddenLayout)
                return;

            pendingHiddenMask |= mask;
        }

        internal bool IsCollectingHiddenLayout => collectingHiddenLayout;

        internal float CurrentLineBreakWidth => currentLineBreakWidth;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsClusterHiddenFromLayout(int cluster)
            => layoutHiddenMask != 0 && (uint)cluster < (uint)buf.hiddenClusters.count
                                     && (buf.hiddenClusters.data[cluster] & layoutHiddenMask) != 0;

        /// <summary>
        /// Downgrades to optional every mandatory break standing directly behind a cluster flagged by
        /// <paramref name="mask"/>, so text taken out of the layout takes the line breaks it carried
        /// with it instead of leaving blank lines behind. A break followed by visible text keeps its
        /// line, including a blank line written between two visible paragraphs.
        /// </summary>
        internal void SuppressHiddenBreaks(byte mask)
            => SuppressHiddenBreaks(mask, false);

        private void SuppressHiddenBreaks(byte mask, bool recordForLayout)
        {
            if (recordForLayout) suppressedLayoutBreaks.FakeClear();

            var breakCount = buf.breakOpportunities.count;
            var hiddenCount = buf.hiddenClusters.count;
            var cpCount = buf.codepoints.count;
            if (breakCount == 0 || hiddenCount == 0 || cpCount == 0) return;

            var breaks = buf.breakOpportunities.data;
            var hidden = buf.hiddenClusters.data;
            var codepoints = buf.codepoints.data;
            var limit = Math.Min(breakCount, cpCount);
            var collapsedAhead = false;

            for (var c = cpCount - 1; c >= 1; c--)
            {
                if (!UnicodeData.IsMandatoryBreakChar(codepoints[c]))
                    collapsedAhead = (uint)c < (uint)hiddenCount &&
                                     (hidden[c] & mask) != 0;

                if (!collapsedAhead || c >= limit ||
                    breaks[c] != LineBreakType.Mandatory) continue;

                if (recordForLayout) suppressedLayoutBreaks.Add(c);
                breaks[c] = LineBreakType.Optional;
            }
        }

        private void RestoreSuppressedLayoutBreaks()
        {
            var count = suppressedLayoutBreaks.count;
            if (count == 0) return;

            var breaks = buf.breakOpportunities.data;
            var indices = suppressedLayoutBreaks.data;
            var limit = buf.breakOpportunities.count;
            for (var i = 0; i < count; i++)
                if (indices[i] < limit) breaks[indices[i]] = LineBreakType.Mandatory;
        }

        private bool BuildEffectiveWidths(ReadOnlySpan<float> source, byte mask)
        {
            var cpCount = buf.codepoints.count;
            var hiddenCount = Math.Min(buf.hiddenClusters.count, cpCount);
            if (cpCount == 0 || hiddenCount == 0) return false;

            effectiveWidths.EnsureCount(cpCount);
            source.Slice(0, cpCount).CopyTo(effectiveWidths.data.AsSpan(0, cpCount));

            var hidden = buf.hiddenClusters.data;
            var any = false;
            for (var c = 0; c < hiddenCount; c++)
            {
                if ((hidden[c] & mask) == 0) continue;
                effectiveWidths.data[c] = 0f;
                any = true;
            }

            return any;
        }

        /// <summary>
        /// Forces recalculation of glyph positions while preserving line breaks.
        /// </summary>
        /// <remarks>
        /// Use this method when run widths have changed but line breaks remain valid.
        /// Updates line widths and recalculates glyph positions without re-breaking lines.
        /// </remarks>
        public void ForceReposition()
        {
            if (!hasValidFirstPassData || !linesCache.valid) return;

            UpdateLineWidths();
            positionsCache.valid = false;
            EnsurePositions(lastSettings);
        }

        #endregion

        #region Measurement state snapshot

        internal struct LayoutSnapshot
        {
            internal bool linesValid;
            internal float width;
            internal float fontSize;
            internal bool wordWrap;
            internal bool positionsValid;
            internal FitBudgets fit;
        }

        internal LayoutSnapshot SnapshotLayout() => new()
        {
            linesValid = linesCache.valid,
            width = linesCache.width,
            fontSize = linesCache.fontSize,
            wordWrap = linesCache.wordWrap,
            positionsValid = positionsCache.valid,
            fit = appliedFit
        };

        /// <summary>
        /// Re-establishes the fit, line and position state captured by <see cref="SnapshotLayout"/>.
        /// Re-runs line breaking with the snapshot key and,
        /// when positions were valid, repositions with <see cref="lastSettings"/> — firing
        /// <see cref="LayoutComplete"/> so overflow modifiers reapply, exactly like a real pass.
        /// </summary>
        internal void RestoreLayout(in LayoutSnapshot snapshot)
        {
            if (!hasValidFirstPassData)
            {
                DropFitState();
                InvalidateLayoutData();
                return;
            }

            ApplyFitAdjustments(in snapshot.fit);
            if (!snapshot.linesValid)
            {
                InvalidateLayoutData();
                return;
            }

            EnsureLinesInternal(snapshot.width, snapshot.fontSize, snapshot.wordWrap,
                buf.cpWidths.Span, snapshot.fit.lineHeightScale);
            if (snapshot.positionsValid)
                EnsurePositions(lastSettings);
        }

        #endregion
    }
}
