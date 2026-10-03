using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Visual style of decoration lines (CSS Text Decoration Module Level 4: text-decoration-style).
    /// </summary>
    public enum LineStyle : byte
    {
        Solid = 0,
        Double = 1,
        Dotted = 2,
        Dashed = 3,
        Wavy = 4,
    }

    /// <summary>
    /// Base class for modifiers that render horizontal lines across text (underline, strikethrough).
    /// </summary>
    /// <remarks>
    /// Subclasses define the vertical offset of the line relative to the baseline. Lines break
    /// across wrapped text lines automatically. Decoration quads run through the standard
    /// <see cref="UniTextMeshGenerator.onGlyph"/> pipeline with
    /// <see cref="UniTextMeshGenerator.isVirtualGlyph"/> set, so per-glyph modifiers (color,
    /// gradient, outline, shadow) apply uniformly to face glyphs and decoration lines. An overlay
    /// range raises that whole stack — the line and every layer applied to it — above the text it
    /// crosses, instead of interleaving each layer at its own position in <c>Styles</c>.
    /// </remarks>
    /// <seealso cref="UnderlineModifier"/>
    /// <seealso cref="StrikethroughModifier"/>
    [Serializable]
    [GenerateParameters]
    public abstract partial class BaseLineModifier : BaseModifier, IHasPaintProvider, ILayer,
        IModifierCommitChanges
    {
        UniTextCommitChanges IModifierCommitChanges.CommitChanges
            => UniTextCommitChanges.Appearance;
        public int LayerSequence { get; set; }
        public virtual bool RendersBehindFill => false;
        public virtual bool ClaimsFill => false;

        /// <summary>Source of named paint swatches resolved by this decoration line.</summary>
        [SerializeReference, TypeSelector]
        [Tooltip("Source of named paint swatches for the line's Paint parameter. Leave a tag's Paint unset to inherit the text fill (text-decoration-color: currentColor).")]
        [StateProperty(nameof(ApplyProviderChange)),
         StateLink(nameof(OnProviderStateChanged))]
        private IPaintProvider provider;

        IPaintProvider IHasPaintProvider.PaintProvider => provider;

        private void ApplyProviderChange(IPaintProvider previous, IPaintProvider current)
        {
            if (IsInitialized && uniText != null)
            {
                emitter.Detach();
                emitter.Attach(uniText, this, current);
            }
            MarkMeshDirty();
        }

        private void OnProviderStateChanged(IStateChangeSource source,
            in StateChange change)
            => MarkNestedStateChanged(UniTextDirty.Mesh, source, in change);

        /// <summary>Line paint (inline colour or named swatch); the default inherits the text fill. A per-range value overrides it.</summary>
        [SerializeField, Parameter(Descriptor = false), Variant("Default|Color=color:#FFFFFFFF|Swatch=enum:@paints", Discriminator = nameof(PaintRef.kind)), StateProperty(nameof(MarkMeshDirty))] private PaintRef paint;
        /// <summary>Line style (solid, double, dotted, dashed, wavy). A per-range value overrides it.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))] private LineStyle style = LineStyle.Solid;
        /// <summary>Line thickness; zero = auto (font metric). A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkMeshDirty))] private UnitValue thickness;
        /// <summary>Vertical offset from the metric line position. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkMeshDirty))] private UnitValue offset;
        /// <summary>Break the line where descenders/ascenders cross it. A per-range value overrides it.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))] private bool skipInk;
        /// <summary>How the line composites; Inherit uses its paint or the fill inherited by currentColor.</summary>
        [SerializeField, Parameter, Inheritable, StateProperty(nameof(MarkMeshDirty))]
        private LayerBlend blend = LayerBlend.Inherit;
        /// <summary>Draws the line and every layer applied to it above the whole text, instead of stacking each layer at its own position in <c>Styles</c>. A per-range value overrides it.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        [Tooltip("Draw the line and its own stroke, shadow and glow above the text instead of interleaving them with the glyphs' layers.")]
        private bool overlay;

        private readonly PaintEmitter emitter = new();
        private Action rebuildingCallback;
        private Action mainPassCallback;

        private bool curHasPaint;
        private TextPaint curPaint;
        private int curRampRow;
        private PaintFrame curFrame;
        private LayerBlend curBlend;
        private bool curInheritsBlend;
        private float curEntryLeft, curEntryRight;

        /// <summary>
        /// Per-range visual parameters for a single decoration tag. Stored once per tag in <see cref="paramsList"/>;
        /// the per-codepoint <see cref="flagsAttribute"/> byte holds <c>(index + 1)</c> so codepoints
        /// outside any tag stay at 0.
        /// </summary>
        /// <remarks>
        /// <c>thicknessPx</c> / <c>offsetPx</c>: <c>NaN</c> means "use font's metric (auto)".
        /// </remarks>
        protected struct LineParams
        {
            public float thicknessPx;
            public float offsetPx;
            public LineStyle style;
            public bool skipInk;
            public TextPaint paint;
            public int rampRow;
            public bool hasPaint;
            public LayerBlend blend;
            public bool inheritsBlend;
            public bool overlay;
        }

        protected struct LineSegment
        {
            public float startX;
            public float endX;
            public float baselineY;
            public long varHash48;
            public int cluster;
            public float uvLeft;
            public float uvRight;
            public byte paramIndex;
            /// <summary>X origin of the pattern rhythm for this stripe (mark <c>k</c> starts at
            /// <c>patternStartX + k * step</c>). Unused for non-pattern segments.</summary>
            public float patternStartX;
            /// <summary>Visual extent of the whole stripe on this line — the frame a line's own gradient/texture maps across, so it stays continuous over the per-glyph body quads.</summary>
            public float lineLeft, lineRight;
        }

        protected PooledArrayAttribute<byte> flagsAttribute;
        protected PooledList<LineParams> paramsList;

        private LineSegment[] lineSegments;
        private int lineSegmentsCapacity;
        private int lineSegmentCount;

        private bool segmentsComputed;
        private float underscoreScale;
        private float cachedGlyphHeightLocal;
        private UniTextFont.Core cachedUnderscoreFont;


        /// <summary>Stable name of the decoration this subclass draws.</summary>
        protected abstract string AttributeKey { get; }

        private string bufferKey;

        /// <summary>
        /// Key of this instance's own flag buffer. A decoration is a layer: two of the same kind on one
        /// component draw two lines from their own parameters, so their buffers never alias.
        /// </summary>
        private string BufferKey =>
            bufferKey ??= $"{AttributeKey}#{RuntimeHelpers.GetHashCode(this):x8}";

        protected abstract float GetLineOffset(FaceInfo faceInfo, float scale);

        protected sealed override void OnEnable()
        {
            buffers.PrepareAttribute(ref flagsAttribute, BufferKey);

            paramsList ??= new PooledList<LineParams>(8);
            paramsList.FakeClear();

            if (lineSegments == null)
            {
                lineSegments = ArrayPool<LineSegment>.Rent(64);
                lineSegmentsCapacity = 64;
            }
            lineSegmentCount = 0;
            segmentsComputed = false;

            rebuildingCallback ??= OnRebuilding;
            mainPassCallback ??= OnMainPassComplete;
            uniText.Rebuilding += rebuildingCallback;
            uniText.MeshGenerator.onMainPassComplete.Subscribe(mainPassCallback);
            emitter.Attach(uniText, this, provider);
        }

        protected sealed override void OnDisable()
        {
            uniText.Rebuilding -= rebuildingCallback;
            uniText.MeshGenerator.onMainPassComplete.Unsubscribe(mainPassCallback);
            emitter.Detach();
        }

        /// <summary>
        /// The previous paint resolution must reset before the re-run <see cref="OnApply"/>.
        /// </summary>
        protected override void BeforeApply()
        {
            paramsList?.FakeClear();
            emitter.ResetResolution();
        }

        protected sealed override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(BufferKey);
            flagsAttribute = null;

            paramsList?.Return();
            paramsList = null;

            rangeEntriesScratch?.Return();
            rangeEntriesScratch = null;

            if (lineSegments != null)
            {
                ArrayPool<LineSegment>.Return(lineSegments);
                lineSegments = null;
            }

            emitter.Return();
        }

        protected internal override void PrepareForParallel()
        {
            base.PrepareForParallel();
            emitter.PrepareForParallel(provider);
        }

        protected sealed override void OnApply(in RangeApplyContext context)
        {
            var reader = context.Parameters.GetReader();
            var lineParams = ParseLineParams(ref reader, in context);

            if (paramsList.Count >= 255)
            {
                paramsList[254] = lineParams;
            }
            else
            {
                paramsList.Add(lineParams);
            }
            var paramIndex = (byte)Math.Min(paramsList.Count, 255);

            flagsAttribute.FillRange(context.Segment.Range, paramIndex);

            buffers.RequestVirtualCodepoint('_');
            if (lineParams.style == LineStyle.Dotted)
                buffers.RequestVirtualCodepoint('•');
        }

        private LineParams ParseLineParams(ref ParameterReader reader,
            in RangeApplyContext context)
        {
            var p = new LineParams
            {
                thicknessPx = float.NaN,
                offsetPx = float.NaN,
                style = style,
                skipInk = skipInk,
            };

            var baseSize = buffers.shapingFontSize > 0 ? buffers.shapingFontSize : uniText.FontSize;

            if (reader.TryPeekOptional(out var pt) && emitter.IsPaintToken(pt))
            {
                reader.Next(out _);
                p.paint = emitter.ResolvePaint(pt, out p.rampRow);
                p.hasPaint = true;
            }
            else if (!paint.IsDefault)
            {
                p.paint = emitter.ResolvePaint(in paint, out p.rampRow);
                p.hasPaint = true;
            }

            if (reader.TryPeekOptional(out var styleToken) && TryParseStyle(styleToken, out var parsedStyle))
            {
                reader.Next(out _);
                p.style = parsedStyle;
            }
            p.style = Param.Style.ApplyOwned(this, context.Identity, context.Segment.Id, p.style);

            var resolvedThickness = Param.Thickness.ResolveNext(ref reader, this, in context);
            var thickPixels = UnitValue.ResolvePx(resolvedThickness.value, resolvedThickness.unit,
                baseSize);
            if (thickPixels > 0f) p.thicknessPx = thickPixels;

            var resolvedOffset = Param.Offset.ResolveNext(ref reader, this, in context);
            var offPixels = UnitValue.ResolvePx(resolvedOffset.value, resolvedOffset.unit, baseSize);
            if (offPixels != 0f) p.offsetPx = offPixels;

            p.skipInk = Param.SkipInk.ResolveNext(ref reader, this, in context);

            var resolvedBlend = Param.Blend.ResolveNext(ref reader, this, in context);
            p.inheritsBlend = !p.hasPaint && resolvedBlend == LayerBlend.Inherit;
            p.blend = resolvedBlend != LayerBlend.Inherit
                ? resolvedBlend
                : p.hasPaint ? p.paint.blend : LayerBlend.Normal;
            if (p.hasPaint) p.paint.blend = p.blend;

            p.overlay = Param.Overlay.ResolveNext(ref reader, this, in context);

            return p;
        }

        /// <summary>
        /// Optional-slot parse for the style keyword, mirroring the paint slot's peek: a
        /// non-style token (a numeric thickness in <c>&lt;u=2&gt;</c>) is NOT consumed — it flows
        /// to the thickness slot instead of being silently eaten as Solid.
        /// </summary>
        private static bool TryParseStyle(ReadOnlySpan<char> token, out LineStyle style)
        {
            if (token.IsPrefixOf("solid"))  { style = LineStyle.Solid;  return true; }
            if (token.IsPrefixOf("double")) { style = LineStyle.Double; return true; }
            if (token.IsPrefixOf("dotted")) { style = LineStyle.Dotted; return true; }
            if (token.IsPrefixOf("dashed")) { style = LineStyle.Dashed; return true; }
            if (token.IsPrefixOf("wavy"))   { style = LineStyle.Wavy;   return true; }
            style = LineStyle.Solid;
            return false;
        }

        private void OnRebuilding()
        {
            flagsAttribute = buffers.GetAttributeData<PooledArrayAttribute<byte>>(BufferKey);
            segmentsComputed = false;
            emitter.Clear();
        }

        private void AddSegment(float startX, float endX, float baselineY, long varHash48, int cluster, float uvLeft, float uvRight, byte paramIndex, float patternStartX = -1f)
        {
            ArrayPool<LineSegment>.GrowDouble(ref lineSegments, ref lineSegmentsCapacity, lineSegmentCount);

            lineSegments[lineSegmentCount] = new LineSegment
            {
                startX = startX,
                endX = endX,
                baselineY = baselineY,
                varHash48 = varHash48,
                cluster = cluster,
                uvLeft = uvLeft,
                uvRight = uvRight,
                paramIndex = paramIndex,
                patternStartX = patternStartX,
                lineLeft = curEntryLeft,
                lineRight = curEntryRight,
            };
            lineSegmentCount++;
        }

        private void OnMainPassComplete()
        {
            var gen = uniText.MeshGenerator;
            if (gen == null) return;

            if (!segmentsComputed)
            {
                ComputeLineSegments(gen);
                segmentsComputed = true;
            }

            if (lineSegmentCount == 0) return;

            var fontProvider = uniText.FontProvider;
            var faceInfo = cachedUnderscoreFont.FaceInfo;
            var fontLineOffset = GetLineOffset(faceInfo, underscoreScale);
            var autoLineThickness = cachedGlyphHeightLocal > 0f ? cachedGlyphHeightLocal : gen.FontSize * 0.05f;
            for (var i = 0; i < lineSegmentCount; i++)
            {
                ref var seg = ref lineSegments[i];

                var thicknessOverride = float.NaN;
                var lineOffset = fontLineOffset;
                var style = LineStyle.Solid;
                var bias = 0;
                curHasPaint = false;
                curBlend = LayerBlend.Normal;
                curInheritsBlend = true;
                if (seg.paramIndex > 0 && seg.paramIndex - 1 < paramsList.Count)
                {
                    var p = paramsList[seg.paramIndex - 1];
                    if (!float.IsNaN(p.thicknessPx))
                        thicknessOverride = p.thicknessPx;
                    if (!float.IsNaN(p.offsetPx)) lineOffset = fontLineOffset + p.offsetPx;
                    style = p.style;
                    curBlend = p.blend;
                    curInheritsBlend = p.inheritsBlend;
                    if (p.overlay) bias = gen.overlayBias;

                    if (p.hasPaint)
                    {
                        curHasPaint = true;
                        curPaint = p.paint;
                        curRampRow = p.rampRow;
                    }
                }

                var resolvedThickness = float.IsNaN(thicknessOverride) ? autoLineThickness : thicknessOverride;

                if (curHasPaint && curPaint.kind != PaintSourceKind.Solid)
                    curFrame = PaintMappingMath.BuildFrame(in curPaint, new Rect(
                        seg.lineLeft, seg.baselineY + lineOffset - resolvedThickness,
                        seg.lineRight - seg.lineLeft, resolvedThickness * 2f));

                gen.sequenceBias = bias;
                gen.suppressInheritedFill = curHasPaint;
                gen.hasClaimedFillLayerOverride = true;
                gen.claimedFillSequenceOverride = LayerSequence + bias;
                gen.hasClaimedFillBlendOverride = !curInheritsBlend;
                gen.claimedFillBlendOverride = curBlend;
                try
                {
                    RenderSegmentForStyle(gen, fontProvider, ref seg, lineOffset, resolvedThickness, style);
                }
                finally
                {
                    gen.sequenceBias = 0;
                    gen.suppressInheritedFill = false;
                    gen.hasClaimedFillLayerOverride = false;
                    gen.hasClaimedFillBlendOverride = false;
                }
            }
        }

        private void RenderSegmentForStyle(UniTextMeshGenerator gen, UniTextFontProvider fontProvider,
            ref LineSegment seg, float lineOffset, float thickness, LineStyle style)
        {
            switch (style)
            {
                case LineStyle.Double:
                    RenderDouble(gen, fontProvider, ref seg, lineOffset, thickness);
                    break;
                case LineStyle.Dotted:
                    RenderPattern(gen, fontProvider, ref seg, lineOffset, thickness, dotMode: true);
                    break;
                case LineStyle.Dashed:
                    RenderPattern(gen, fontProvider, ref seg, lineOffset, thickness, dotMode: false);
                    break;
                case LineStyle.Wavy:
                case LineStyle.Solid:
                default:
                    LineRenderHelper.DrawLine(gen, fontProvider, seg.startX, seg.endX, seg.baselineY, lineOffset,
                        seg.cluster, seg.varHash48, seg.uvLeft, seg.uvRight, thickness);
                    EmitLine(gen);
                    break;
            }
        }

        /// <summary>
        /// Emits the line quad just produced by <see cref="LineRenderHelper"/>: its own paint replaces the
        /// inherited fill when set (otherwise the fill it claimed through <c>onGlyph</c> stays), then records
        /// the quad at this layer's sequence — inside the band the segment is emitted into — so the line
        /// stacks at its position in <c>Styles</c>, or above the whole text when the range is overlay.
        /// </summary>
        private void EmitLine(UniTextMeshGenerator gen)
        {
            var sequence = LayerSequence + gen.sequenceBias;
            var outputBlend = curInheritsBlend ? gen.claimedFillBlend : curBlend;
            if (curHasPaint)
            {
                var outputPaint = curPaint;
                var rampRow = curRampRow;
                emitter.ApplyFilter(gen, gen.filters.ResolveIndex(gen.currentCluster, LayerSequence),
                    ref outputPaint, ref rampRow);
                emitter.Paint(gen, gen.faceBaseIdx, in outputPaint, in curFrame, rampRow, CoverageMode.Fill,
                    0f, 0f, 0f, gen.defaultColor.a, true, sequence, Vector2.zero, 0f);
            }
            if (!gen.baseFaceClaimed) gen.AddSdfQuad(sequence, gen.faceBaseIdx, outputBlend);
        }

        /// <summary>
        /// Draws two parallel solid lines. Each sub-line has full thickness <paramref name="perLineThickness"/>;
        /// top center stays at <paramref name="lineOffset"/> (default single-line position) and the bottom
        /// drops below by <c>2 * perLineThickness</c>, leaving a visible gap equal to per-line thickness.
        /// </summary>
        private void RenderDouble(UniTextMeshGenerator gen, UniTextFontProvider fontProvider,
            ref LineSegment seg, float lineOffset, float perLineThickness)
        {
            var topOffset = lineOffset;
            var bottomOffset = lineOffset - perLineThickness * 2f;

            LineRenderHelper.DrawLine(gen, fontProvider, seg.startX, seg.endX, seg.baselineY, topOffset,
                seg.cluster, seg.varHash48, seg.uvLeft, seg.uvRight, perLineThickness);
            EmitLine(gen);
            LineRenderHelper.DrawLine(gen, fontProvider, seg.startX, seg.endX, seg.baselineY, bottomOffset,
                seg.cluster, seg.varHash48, seg.uvLeft, seg.uvRight, perLineThickness);
            EmitLine(gen);
        }

        /// <summary>
        /// Draws repeating short marks along the segment. <paramref name="dotMode"/> = true emits
        /// near-square dots; false emits longer dashes. Pattern length scales with thickness so the
        /// rhythm stays proportional across font sizes.
        /// </summary>
        private void RenderPattern(UniTextMeshGenerator gen, UniTextFontProvider fontProvider,
            ref LineSegment seg, float lineOffset, float thickness, bool dotMode)
        {
            var lineThickness = Math.Max(thickness, 1f);
            var markLen = Math.Max(dotMode ? lineThickness * 2.0f : lineThickness * 3.0f, 1f);
            var step = Math.Max(lineThickness * 4.5f, markLen + 1f);
            if (seg.endX <= seg.startX) return;
            var rhythmStartX = seg.patternStartX;
            var rel = seg.startX - rhythmStartX;
            var k = rel <= 0f ? 0 : (int)Math.Ceiling(rel / step);
            var x = rhythmStartX + k * step;

            while (x + markLen <= seg.endX)
            {
                if (dotMode)
                {
                    LineRenderHelper.DrawDot(gen, fontProvider, x + markLen * 0.5f, seg.baselineY, lineOffset,
                        seg.cluster, markLen);
                }
                else
                {
                    LineRenderHelper.DrawLine(gen, fontProvider, x, x + markLen, seg.baselineY, lineOffset,
                        seg.cluster, seg.varHash48, seg.uvLeft, seg.uvRight, thickness);
                }
                EmitLine(gen);
                x += step;
            }
        }

        private PooledList<LineRangeEntry> rangeEntriesScratch;

        private void ComputeLineSegments(UniTextMeshGenerator gen)
        {
            lineSegmentCount = 0;

            var fontProvider = uniText.FontProvider;
            cachedUnderscoreFont = fontProvider.GetFont(fontProvider.FindFontForCodepoint('_'));
            underscoreScale = fontProvider.MetricScale(cachedUnderscoreFont, gen.FontSize);

            var underscoreGlyphIndex = cachedUnderscoreFont.GetGlyphIndexForUnicode('_');
            if (underscoreGlyphIndex == 0) return;

            var flagsBuffer = flagsAttribute?.buffer.data;
            if (!flagsBuffer.HasAnyFlags()) return;

            var allGlyphs = buffers.positionedGlyphs.data;
            if (buffers.positionedGlyphs.count == 0) return;
            if (buffers.lines.count == 0) return;

            var defaultVarHash = cachedUnderscoreFont.DefaultVarHash48;
            var underscoreFontHash = cachedUnderscoreFont.FontDataHash;
            var glyphLookup = cachedUnderscoreFont.GlyphLookupTable;

            const float sdfPadding = UniTextMeshGenerator.DefaultSdfPadding;
            var aspect = 1f;
            var glyphHeightLocal = gen.FontSize * 0.05f;
            if (glyphLookup != null &&
                glyphLookup.TryGetValue(GlyphAtlas.MakeKey(defaultVarHash, underscoreGlyphIndex), out var underscoreData) &&
                underscoreData.metrics.height > 0)
            {
                aspect = underscoreData.metrics.width / underscoreData.metrics.height;
                glyphHeightLocal = underscoreData.metrics.height * underscoreScale;
            }
            const float capFraction = 0.2f;
            var centerX = aspect * 0.5f;
            var capLeftEnd = aspect * capFraction;
            var capRightStart = aspect * (1f - capFraction);
            var uvRightCap = aspect + sdfPadding;
            var capWidthPerThickness = capLeftEnd + sdfPadding;
            cachedGlyphHeightLocal = glyphHeightLocal;
            var autoLineThickness = glyphHeightLocal;
            var skipInkThreshold = -GetLineOffset(cachedUnderscoreFont.FaceInfo, underscoreScale) - glyphHeightLocal * 0.5f;

            var offsetX = gen.offsetX;
            var offsetY = gen.offsetY;
            var fontSize = gen.FontSize;

            bool IsSkipInk(int idx)
            {
                ref readonly var g = ref allGlyphs[idx];
                var glyphFont = fontProvider.GetFont(g.fontId);
                if (glyphFont == null) return false;
                var varHash = (buffers.variationMap != null && buffers.variationMap.TryGetValue(g.fontId, out var vi))
                    ? vi.varHash48 : glyphFont.DefaultVarHash48;
                if (!gen.TryGetCachedGlyphEntry(GlyphAtlas.MakeKey(varHash, (uint)g.glyphId), out var entry))
                    return false;
                var glyphScale = fontProvider.MetricScale(glyphFont, fontSize);
                var descentPx = (entry.metrics.height - entry.metrics.horizontalBearingY) * glyphScale;
                return descentPx > skipInkThreshold;
            }

            rangeEntriesScratch ??= new PooledList<LineRangeEntry>(8);

            var flagsLength = flagsBuffer.Length;
            var lastPatternLineIdx = -1;
            var lastPatternStartX = 0f;

            var c = 0;
            while (c < flagsLength)
            {
                var p = flagsBuffer[c];
                if (p == 0) { c++; continue; }
                var stripeStart = c;
                while (c < flagsLength && flagsBuffer[c] == p) c++;
                var stripeEnd = c;

                if (p - 1 >= paramsList.Count) continue;
                var lp = paramsList[p - 1];
                var stripeThickness = float.IsNaN(lp.thicknessPx) ? autoLineThickness : lp.thicknessPx;
                var capWidth = capWidthPerThickness * stripeThickness;
                var isPattern = lp.style == LineStyle.Dotted || lp.style == LineStyle.Dashed;

                uniText.CollectRangeEntries(stripeStart, stripeEnd, rangeEntriesScratch);

                for (var ei = 0; ei < rangeEntriesScratch.Count; ei++)
                {
                    var entry = rangeEntriesScratch[ei];
                    var visualLeft = offsetX + entry.minX;
                    var visualRight = offsetX + entry.maxX;
                    if (visualRight <= visualLeft) continue;

                    curEntryLeft = visualLeft;
                    curEntryRight = visualRight;

                    var firstG = entry.firstGlyphIdx;
                    var lastG = entry.lastGlyphIdx;
                    ref readonly var firstGlyph = ref allGlyphs[firstG];
                    ref readonly var lastGlyph = ref allGlyphs[lastG];

                    if (lastPatternLineIdx != entry.lineIdx)
                    {
                        lastPatternStartX = visualLeft;
                        lastPatternLineIdx = entry.lineIdx;
                    }

                    if (isPattern)
                    {
                        var runActive = false;
                        var runStartX = 0f;
                        var runEndX = 0f;
                        var runCluster = 0;
                        var runVarHash = 0L;
                        var runBaselineY = 0f;
                        var firstRunIdx = -1;
                        var lastRunIdx = -1;

                        for (var k = firstG; k <= lastG; k++)
                        {
                            ref readonly var gk = ref allGlyphs[k];
                            if (lp.skipInk && IsSkipInk(k))
                            {
                                if (runActive)
                                {
                                    AddSegment(runStartX, runEndX, runBaselineY, runVarHash, runCluster,
                                        -sdfPadding, uvRightCap, p, lastPatternStartX);
                                    if (firstRunIdx < 0) firstRunIdx = lineSegmentCount - 1;
                                    lastRunIdx = lineSegmentCount - 1;
                                    runActive = false;
                                }
                                continue;
                            }
                            if (!runActive)
                            {
                                runActive = true;
                                runStartX = offsetX + gk.left;
                                runCluster = gk.cluster;
                                runVarHash = ResolveLineVarHash(fontProvider, gk.fontId, underscoreFontHash, defaultVarHash);
                                runBaselineY = offsetY - gk.y;
                            }
                            runEndX = offsetX + gk.right;
                        }
                        if (runActive)
                        {
                            AddSegment(runStartX, runEndX, runBaselineY, runVarHash, runCluster,
                                -sdfPadding, uvRightCap, p, lastPatternStartX);
                            if (firstRunIdx < 0) firstRunIdx = lineSegmentCount - 1;
                            lastRunIdx = lineSegmentCount - 1;
                        }

                        if (firstRunIdx >= 0)
                        {
                            lineSegments[firstRunIdx].startX = visualLeft;
                            lineSegments[lastRunIdx].endX = visualRight;
                        }
                    }
                    else
                    {
                        var effCap = capWidth > 0f ? Math.Min(capWidth, (visualRight - visualLeft) * 0.5f) : 0f;
                        var bodyLeft = visualLeft + effCap;
                        var bodyRight = visualRight - effCap;

                        if (effCap > 0f)
                        {
                            var firstVh = ResolveLineVarHash(fontProvider, firstGlyph.fontId, underscoreFontHash, defaultVarHash);
                            AddSegment(visualLeft, bodyLeft, offsetY - firstGlyph.y, firstVh, firstGlyph.cluster,
                                -sdfPadding, capLeftEnd, p);
                        }

                        for (var k = firstG; k <= lastG; k++)
                        {
                            ref readonly var gk = ref allGlyphs[k];
                            if (lp.skipInk && IsSkipInk(k)) continue;
                            var bL = Math.Max(offsetX + gk.left, bodyLeft);
                            var bR = Math.Min(offsetX + gk.right, bodyRight);
                            if (bL >= bR) continue;
                            var vh = ResolveLineVarHash(fontProvider, gk.fontId, underscoreFontHash, defaultVarHash);
                            AddSegment(bL, bR, offsetY - gk.y, vh, gk.cluster, centerX, centerX, p);
                        }

                        if (effCap > 0f)
                        {
                            var lastVh = ResolveLineVarHash(fontProvider, lastGlyph.fontId, underscoreFontHash, defaultVarHash);
                            AddSegment(bodyRight, visualRight, offsetY - lastGlyph.y, lastVh, lastGlyph.cluster,
                                capRightStart, uvRightCap, p);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Resolves varHash48 for a line segment. If the text glyph's font matches the
        /// underscore font (same base font), uses the text's variation directly.
        /// Otherwise finds a companion variation of the underscore font with matching axes.
        /// </summary>
        private long ResolveLineVarHash(UniTextFontProvider fontProvider, int glyphFontId,
            int underscoreFontHash, long defaultVarHash)
        {
            var glyphFont = fontProvider.GetFont(glyphFontId);
            if (glyphFont == null) return defaultVarHash;

            if (glyphFont.FontDataHash == underscoreFontHash)
                return buffers.ResolveVarHash48(glyphFontId, glyphFont);

            var companion = buffers.FindCompanionVarHash(glyphFontId, underscoreFontHash);
            return companion != 0 ? companion : defaultVarHash;
        }
    }

}
