using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Shared engine for paint layers (fill, stroke, shadow, glow, inner-shadow). Resolves a
    /// <see cref="TextPaint"/> from the parameter, precomputes gradient/texture mapping per styled
    /// range, and stamps coverage quads through <see cref="CoverageQuadOps"/>. Thin subclasses only
    /// pick a coverage mode and parse their geometry params — all paint/mapping logic lives here once.
    /// </summary>
    /// <remarks>
    /// Parameter grammar: <c>paint</c>, then the subclass's layer-geometry tokens
    /// (<see cref="ParseExtra"/>), then the shared projection overrides
    /// <c>[,mapping][,shape][,fit][,angle][,scale][,offset][,corners][,miterLimit][,tint][,blend][,spread]</c>.
    /// Projection values affect gradient/texture paints; tint multiplies every paint kind.
    /// The paint slot is optional-with-rewind: only a genuine paint token
    /// (colour literal or known swatch name) is consumed; anything else rewinds to the geometry
    /// slots, so <c>&lt;stroke=2px&gt;</c> reads as a width. Each projection value resolves through
    /// the override hierarchy, weakest → strongest: swatch → modifier field → default parameters →
    /// tag attribute (<see cref="TextPaint.ApplyProjection"/>).
    /// </remarks>
    [Serializable]
    [GenerateParameters]
    public abstract partial class PaintLayerModifier : EffectModifier, IHasPaintProvider,
        IModifierCommitChanges,
        IModifierRuleWeightReceiver
    {
        UniTextCommitChanges IModifierCommitChanges.CommitChanges
            => UniTextCommitChanges.Appearance;
        /// <summary>
        /// Source of named paint swatches this layer resolves (e.g. <c>&lt;fill=ember&gt;</c>,
        /// <c>&lt;stroke=gold&gt;</c>). <see langword="null"/> disables swatch resolution, leaving only
        /// inline colour tokens. A gradient or texture paint is only reachable through a swatch, so set
        /// this to give the layer a gradient/texture from code.
        /// </summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyProviderChange)),
         StateLink(nameof(OnProviderStateChanged))]
        [Tooltip("Source of named paint swatches for the paint parameter.")]
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

        /// <summary>How a gradient/texture paint spreads over the text (whole block, per line, per glyph, or per range). Inherit (default) uses the swatch's value; a per-range value overrides both.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        private PaintMapping mapping = PaintMapping.Inherit;

        /// <summary>Gradient geometry (linear, radial, angular). Inherit (default) uses the swatch's value; a per-range value overrides both.</summary>
        [SerializeField, Parameter, Inheritable, StateProperty(nameof(MarkMeshDirty))]
        private PaintProjectionKind shape = PaintProjectionKind.Inherit;

        /// <summary>How a texture paint fits its mapping frame. Inherit (default) uses the swatch's value; a per-range value overrides both.</summary>
        [SerializeField, Parameter, Inheritable, StateProperty(nameof(MarkMeshDirty))]
        private PaintFit fit = PaintFit.Inherit;

        /// <summary>Rotation of the gradient/texture mapping, in degrees. NaN (default) uses the swatch's angle; a per-range value overrides both.</summary>
        [SerializeField, Parameter, Inheritable(0f), Range(0f, 360f), StateProperty(nameof(MarkMeshDirty))]
        private float angle = float.NaN;

        /// <summary>Uniform zoom of the mapping frame; non-positive is treated as 1. NaN (default) uses the swatch's scale; a per-range value overrides both.</summary>
        [SerializeField, Parameter, Inheritable(1f), StateProperty(nameof(MarkMeshDirty))]
        private float scale = float.NaN;

        /// <summary>Pan of the paint's sample origin in normalized frame units. NaN per axis uses the swatch's offset; a per-range value overrides both.</summary>
        [SerializeField, Parameter, Inheritable(0f, 0f), StateProperty(nameof(MarkMeshDirty))]
        private Vector2 paintOffset = new(float.NaN, float.NaN);

        /// <summary>Corner treatment of this layer's offset iso-lines (outline rim, shadow spread).
        /// Round is artifact-free at any width; Sharp keeps mitered corners up to <see cref="MiterLimit"/>.
        /// Single-channel SDF mode has no perpendicular field — Sharp renders as Round there.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        private CornerStyle corners = CornerStyle.Round;

        /// <summary>Sharp-corner miter limit as a multiple of the offset width (SVG stroke-miterlimit semantics); corners spikier than this are clipped.</summary>
        [SerializeField, Parameter, Range(1f, 8f), StateProperty(nameof(MarkMeshDirty))]
        private float miterLimit = 2f;

        /// <summary>Colour multiplied with the resolved solid, gradient, or texture paint.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        private Color32 tint = new(255, 255, 255, 255);

        /// <summary>The subclass's own <c>[Parameter]</c> paint, used as the default when the markup carries no paint value.</summary>
        protected virtual PaintRef PaintField => default;

        /// <summary>Coverage mode written to TEXCOORD2.x (see <see cref="CoverageMode"/>).</summary>
        protected abstract float CoverageModeValue { get; }

        /// <summary>Solid colour applied when the paint token is omitted entirely. White for the face/fill; dark layers (stroke, shadow) override so a bare tag still renders sensibly without a paint provider. A present-but-unresolved name still falls back to white, not this.</summary>
        protected virtual Color32 DefaultPaintColor => new Color32(255, 255, 255, 255);

        /// <summary>When true, the first hit on a glyph recolours the suppressed default base quad instead of emitting a duplicate (fill behaviour).</summary>
        protected virtual bool ClaimsBase => false;

        /// <summary>
        /// Parses layer-specific tokens (after paint/mapping/shape/angle/scale) into coverage params and an
        /// optional quad offset. Read scalars with <see cref="ParameterReader.NextUnitFloat"/> / vectors with
        /// <see cref="ParameterReader.NextUnitVector2"/> and set the matching <c>*Px</c> flag from the unit
        /// (<see cref="UnitKind.Absolute"/> = px), so the value resolves to em at emit.
        /// </summary>
        protected virtual void ParseExtra(ref ParameterReader reader, in RangeApplyContext context,
            ref LayerGeometry g) { }

        /// <summary>
        /// A layer's parsed geometry before per-glyph unit resolution. Each value is em unless its <c>*Px</c>
        /// flag is set — a px value yields a constant on-screen size, converted to em at emit with the glyph's
        /// metric factor (coverage params by <c>1/(Pad·factor)</c>, positional offsets by <c>1/factor</c>).
        /// </summary>
        protected struct LayerGeometry
        {
            public float p0, p1, softness;
            public Vector2 offset;
            public bool p0Px, p1Px, softnessPx, offsetPx;
        }

        /// <summary>How a layer's offset iso-lines treat glyph corners.</summary>
        public enum CornerStyle : byte
        {
            /// <summary>Euclidean distance: correct round joins at any width, no seam artifacts.</summary>
            Round,
            /// <summary>Perpendicular distance: mitered sharp joins, clipped at <see cref="MiterLimit"/>.</summary>
            Sharp
        }

        protected struct LayerRange
        {
            public int start, end;
            public TextPaint paint;
            public int rampRow;
            public PaintFrame frame;
            public float p0, p1, softness;
            public float corner;
            public Vector2 offset;
            public bool p0Px, p1Px, softnessPx, offsetPx;
            /// <summary>Whether the range decorates colour glyphs through their silhouette field.</summary>
            public bool colorGlyphs;
        }

        private struct TextureEmit
        {
            public int sourceBaseIdx;
            public int rangeIndex;
            public int sequence;
            public LayerBlend blend;
            public float delta;
            public int filterIdx;
        }

        private PooledBuffer<LayerRange> ranges;
        private PooledBuffer<TextureEmit> textureEmits;
        private readonly PooledList<Rect> boundsCache = new();
        private readonly List<Rect> lineRects = new();
        private bool hasNonSolidPaint;
        private Action layoutCallback;
        private Action meshCallback;
        private bool framesDirty;
        private float ruleWeight = 1f;

        void IModifierRuleWeightReceiver.SetRuleWeight(float weight)
            => ruleWeight = Mathf.Clamp01(weight);
        private int lineRectCursor;
        private readonly PaintEmitter emitter = new();

        private PooledArrayAttribute<byte> winnerAttribute;
        private string attributeKey;

        /// <summary>
        /// Per-codepoint winner-index buffer key, unique per modifier instance so two layers of the
        /// same type on one component never alias each other's winner buffers.
        /// </summary>
        private string AttributeKey =>
            attributeKey ??= $"declayer#{RuntimeHelpers.GetHashCode(this):x8}";

        protected override void OnEnable()
        {
            ranges.FakeClear();
            textureEmits.FakeClear();
            hasNonSolidPaint = false;
            buffers.PrepareAttribute(ref winnerAttribute, AttributeKey);
            base.OnEnable();
            layoutCallback ??= MarkFramesDirty;
            meshCallback ??= RebuildFramesIfDirty;
            uniText.TextProcessor.LayoutComplete.Subscribe(layoutCallback);
            uniText.BeforeGenerateMesh.Subscribe(meshCallback);
            emitter.Attach(uniText, this, provider);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            uniText.TextProcessor.LayoutComplete.Unsubscribe(layoutCallback);
            uniText.BeforeGenerateMesh.Unsubscribe(meshCallback);
            emitter.Detach();
        }

        protected override void OnDestroy()
        {
            ranges.Return();
            textureEmits.Return();
            boundsCache.Return();
            emitter.Return();
            buffers?.ReleaseAttributeData(AttributeKey);
            winnerAttribute = null;
            base.OnDestroy();
        }

        protected override void ResetOwnRequests()
        {
            base.ResetOwnRequests();
            textureEmits.FakeClear();
            emitter.Clear();
        }

        /// <summary>
        /// Clears the range accumulator AND releases the emitter's held gradient ramp rows — the re-run
        /// <see cref="OnApply"/> re-acquires them immediately, so an animated parameter never leaks a row
        /// reference per frame (the atlas sweep's grace period protects the currently displayed mesh).
        /// The winner buffer is zeroed here too: a granular re-apply replays without the
        /// <c>OnEnable</c>-time <c>PrepareAttribute</c> clear, and stale winner indices would point into
        /// the freshly reset <see cref="ranges"/> list.
        /// </summary>
        protected override void BeforeApply()
        {
            ranges.FakeClear();
            hasNonSolidPaint = false;
            framesDirty = true;
            emitter.ResetResolution();
            winnerAttribute?.ClearAll();
        }

        protected internal override void PrepareForParallel()
        {
            base.PrepareForParallel();
            emitter.PrepareForParallel(provider);
        }

        /// <summary>
        /// Resolves the range's paint and geometry, then stamps the per-codepoint winner-index
        /// buffer: overlapping ranges of one modifier resolve innermost-wins ONCE here (an
        /// <see cref="IsInner"/> compare against the current holder), so the per-glyph hook reads a
        /// single buffer slot instead of scanning ranges. The buffer indexes at most 255 applied
        /// ranges per layer instance; ranges beyond that keep the earlier winner.
        /// </summary>
        protected override void OnApply(in RangeApplyContext context)
        {
            var reader = context.Parameters.GetReader();
            TextPaint paint;
            int rampRow;
            if (reader.TryPeekOptional(out var pt) && emitter.IsPaintToken(pt))
            {
                reader.Next(out _);
                paint = emitter.ResolvePaint(pt, out rampRow);
            }
            else
            {
                var field = PaintField;
                paint = emitter.ResolvePaint(in field, out rampRow);
                if (field.IsDefault) paint.color = DefaultPaintColor;
            }

            var g = new LayerGeometry();
            ParseExtra(ref reader, in context, ref g);

            paint.ApplyProjection(
                Param.Mapping.ResolveNext(ref reader, this, in context),
                Param.Shape.ResolveNext(ref reader, this, in context),
                Param.Fit.ResolveNext(ref reader, this, in context),
                Param.Angle.ResolveNext(ref reader, this, in context),
                Param.Scale.ResolveNext(ref reader, this, in context),
                Param.PaintOffset.ResolveNext(ref reader, this, in context));
            var resolvedCorners = Param.Corners.ResolveNext(ref reader, this, in context);
            var resolvedMiterLimit = Param.MiterLimit.ResolveNext(ref reader, this, in context);
            paint.ApplyTint(Param.Tint.ResolveNext(ref reader, this, in context));
            paint.color.a = (byte)Mathf.RoundToInt(paint.color.a * ruleWeight);
            paint.blend = ResolveBlend(ref reader, in context, paint.blend);
            paint.spread = ResolveSpread(ref reader, in context, paint.spread);
            var colorGlyphs = ResolveColorGlyphs(ref reader, in context) == ColorGlyphPolicy.Apply;

            if (paint.kind != PaintSourceKind.Solid) hasNonSolidPaint = true;

            var start = context.Segment.Range.start;
            var end = context.Segment.Range.End;
            var clampedEnd = Math.Min(end, buffers.codepoints.count);
            ranges.Add(new LayerRange
            {
                start = start,
                end = clampedEnd,
                paint = paint,
                rampRow = rampRow,
                p0 = g.p0,
                p1 = g.p1,
                softness = g.softness,
                corner = resolvedCorners == CornerStyle.Sharp
                    ? Mathf.Clamp(resolvedMiterLimit, 1f, 8f)
                    : CoverageMode.RoundCorners,
                offset = g.offset,
                p0Px = g.p0Px,
                p1Px = g.p1Px,
                softnessPx = g.softnessPx,
                offsetPx = g.offsetPx,
                colorGlyphs = colorGlyphs,
            });

            var index = ranges.count;
            ref readonly var added = ref ranges.data[index - 1];
            if (colorGlyphs) RequestColorGlyphField(start, clampedEnd, EstimateReachEm(in added));

            if (index > byte.MaxValue) return;
            var winners = winnerAttribute.buffer.data;
            if (winners == null) return;
            for (var i = Math.Max(start, 0); i < clampedEnd; i++)
            {
                var prev = winners[i];
                if (prev == 0 || IsInner(in added, in ranges.data[prev - 1]))
                    winners[i] = (byte)index;
            }
        }

        private void MarkFramesDirty() => framesDirty = true;

        /// <summary>
        /// Rebuilds gradient/texture frames at <see cref="UniTextBase.BeforeGenerateMesh"/> — after any
        /// re-apply (which <see cref="BeforeApply"/> flags, clearing the old frame) or glyph move (flagged
        /// on <c>LayoutComplete</c>), and before emission reads them. Decoupling from the positions pass
        /// lets a mesh-only parameter change restore Block/Range frames that would otherwise stay cleared.
        /// </summary>
        private void RebuildFramesIfDirty()
        {
            if (!framesDirty) return;
            framesDirty = false;
            RebuildPaintFrames();
        }

        private void RebuildPaintFrames()
        {
            if (ranges.count == 0 || !hasNonSolidPaint) return;

            var needsLines = false;
            for (var i = 0; i < ranges.count; i++)
            {
                ref readonly var r = ref ranges.data[i];
                if (r.paint.kind == PaintSourceKind.Solid) continue;
                if (r.paint.mapping is PaintMapping.Line or PaintMapping.Block)
                {
                    needsLines = true;
                    break;
                }
            }

            lineRects.Clear();
            lineRectCursor = 0;
            var blockRect = new Rect(0, 0, 1, 1);
            if (needsLines)
            {
                uniText.GetRangeBounds(0, buffers.codepoints.count, boundsCache);
                for (var i = 0; i < boundsCache.Count; i++)
                    lineRects.Add(boundsCache[i]);
                blockRect = Union(boundsCache);
            }

            for (var i = 0; i < ranges.count; i++)
            {
                ref var r = ref ranges.data[i];
                if (r.paint.kind == PaintSourceKind.Solid) continue;
                if (r.paint.mapping == PaintMapping.Block)
                    r.frame = PaintMappingMath.BuildFrame(in r.paint, blockRect);
                else if (r.paint.mapping == PaintMapping.Range)
                {
                    uniText.GetRangeBounds(r.start, r.end, boundsCache);
                    r.frame = PaintMappingMath.BuildFrame(in r.paint, Union(boundsCache));
                }
            }
        }

        /// <summary>
        /// O(1) per glyph: the winning range comes from the per-codepoint winner buffer resolved at
        /// apply time; no range scan happens here.
        /// </summary>
        protected override void OnGlyphEffect()
        {
            var gen = uniText.MeshGenerator;
            var colorFace = gen.font.IsColor;
            if (colorFace && !gen.HasColorFaceField) return;

            var cluster = gen.currentCluster;
            var winners = winnerAttribute?.buffer.data;
            if (winners == null || (uint)cluster >= (uint)winners.Length) return;
            var best = winners[cluster] - 1;
            if (best < 0 || best >= ranges.count) return;

            if (ClaimsBase && gen.suppressInheritedFill) return;

            ref readonly var sel = ref ranges.data[best];
            if (colorFace && !sel.colorGlyphs) return;
            var baseIdx = gen.faceBaseIdx;
            var ext = ComputeExtent(gen, baseIdx, in sel, gen.currentGlyphScale, out var selDelta);
            if (ext > gen.currentMaxGlyphExtent) gen.currentMaxGlyphExtent = ext;

            var filterIdx = gen.filters.ResolveIndex(cluster, LayerSequence);

            var claims = ClaimsBase && !gen.fillClaimedThisGlyph;
            if (claims)
            {
                gen.fillClaimedThisGlyph = true;
                var outputSequence = gen.hasClaimedFillLayerOverride
                    ? gen.claimedFillSequenceOverride
                    : LayerSequence + gen.sequenceBias;
                var outputBlend = gen.hasClaimedFillBlendOverride
                    ? gen.claimedFillBlendOverride
                    : sel.paint.blend;
                gen.claimedFillSequence = outputSequence;
                gen.claimedFillBlend = outputBlend;
                if (sel.paint.kind == PaintSourceKind.Texture)
                {
                    gen.baseFaceClaimed = true;
                    EnqueueTexture(baseIdx, best, outputSequence, outputBlend, selDelta, filterIdx);
                }
                else if (colorFace)
                {
                    gen.StashPreClaimAlpha(baseIdx);
                    gen.baseFaceClaimed = true;
                    EnqueueDuplicate(baseIdx, best | (filterIdx << 8), outputBlend, outputSequence);
                }
                else
                {
                    EmitPaint(gen, baseIdx, best, true, outputSequence, outputBlend, selDelta,
                        filterIdx: filterIdx);
                }
            }
            else if (sel.paint.kind == PaintSourceKind.Texture)
            {
                EnqueueTexture(baseIdx, best, LayerSequence + gen.sequenceBias, sel.paint.blend,
                    selDelta, filterIdx);
            }
            else
            {
                EnqueueDuplicate(baseIdx, best | (filterIdx << 8), sel.paint.blend);
            }
        }

        /// <summary>
        /// Texture sub-mesh copies defer to <c>onMainPassFinalize</c> like every other duplicate —
        /// copying at <c>onGlyph</c> time would snapshot the face before later vertex-mutating
        /// modifiers (wobble) run, making the visible textured quad ignore their motion while the
        /// hidden base moved (order-dependent rendering). Claim flags are set at glyph time so the
        /// base quad is suppressed; the geometry copy happens once all per-glyph mutation is done.
        /// </summary>
        private void EnqueueTexture(int sourceBaseIdx, int rangeIndex, int sequence,
            LayerBlend outputBlend, float delta, int filterIdx)
        {
            textureEmits.Add(new TextureEmit
            {
                sourceBaseIdx = sourceBaseIdx,
                rangeIndex = rangeIndex,
                sequence = sequence,
                blend = outputBlend,
                delta = delta,
                filterIdx = filterIdx,
            });
        }

        protected override void OnFlush()
        {
            base.OnFlush();
            var count = textureEmits.count;
            if (count == 0) return;

            var gen = uniText.MeshGenerator;
            var data = textureEmits.data;
            for (var i = 0; i < count; i++)
            {
                ref var e = ref data[i];
                EmitPaint(gen, e.sourceBaseIdx, e.rangeIndex, false, e.sequence, e.blend,
                    e.delta, e.sourceBaseIdx, e.filterIdx);
            }
        }

        /// <summary>Innermost-wins for overlapping ranges of one modifier (nested same-name tags): the
        /// deepest (latest-opened) range overrides — greater start, then narrower end. Tag ranges are
        /// properly nested, so this matches HTML semantics where the inner tag overrides the outer.
        /// Applied once per codepoint at apply time (the winner buffer), never per glyph.</summary>
        private static bool IsInner(in LayerRange candidate, in LayerRange current)
            => candidate.start != current.start
                ? candidate.start > current.start
                : candidate.end < current.end;

        protected override void OnEmitQuad(int sourceBaseIdx, int destBaseIdx, int payload)
        {
            var rangeIndex = payload & 0xFF;
            var filterIdx = (int)((uint)payload >> 8);
            EmitPaint(uniText.MeshGenerator, destBaseIdx, rangeIndex, false, LayerSequence,
                ranges.data[rangeIndex].paint.blend, -1f, sourceBaseIdx, filterIdx);
        }

        private void EmitPaint(UniTextMeshGenerator gen, int baseIdx, int rangeIndex, bool claims,
            int outputSequence, LayerBlend outputBlend, float precomputedDelta = -1f,
            int sourceBaseIdx = -1, int filterIdx = 0)
        {
            ref readonly var r = ref ranges.data[rangeIndex];
            var outputPaint = r.paint;
            outputPaint.blend = outputBlend;
            var rampRow = r.rampRow;
            emitter.ApplyFilter(gen, filterIdx, ref outputPaint, ref rampRow);
            var glyphScale = sourceBaseIdx >= 0 ? gen.GlyphScale(sourceBaseIdx) : gen.currentGlyphScale;
            var metricFactor = gen.fontMetricFactor * glyphScale;
            var tap = CoverageModeValue >= CoverageMode.InnerShadow;
            ResolvePx(in r, metricFactor, tap, out var p0, out var p1, out var softness);
            var pos = metricFactor > 1e-9f ? 1f / metricFactor : 0f;
            var offset = r.offsetPx ? r.offset * pos : r.offset;

            var frame = r.paint.kind == PaintSourceKind.Solid ? default : ResolveFrame(in r, gen, baseIdx);
            float delta;
            if (precomputedDelta >= 0f) delta = precomputedDelta;
            else ComputeExtent(gen, baseIdx, in r, glyphScale, out delta);

            emitter.Paint(gen, baseIdx, in outputPaint, in frame, rampRow, CoverageModeValue,
                p0, p1, softness, gen.defaultColor.a, claims, outputSequence, offset, delta,
                sourceBaseIdx, r.corner);
        }

        /// <summary>
        /// The single px→em conversion contract for layer geometry, shared by emission and extent
        /// computation: coverage params scale by <c>1/(Pad·metricFactor)</c>, positional values by
        /// <c>1/metricFactor</c>; <paramref name="tap"/> (inner shadow) reads p0 as positional.
        /// <paramref name="metricFactor"/> is the glyph's own em→pixel factor —
        /// <see cref="UniTextMeshGenerator.fontMetricFactor"/> times the per-glyph scale on its quad;
        /// the font-level factor alone pins px to the component font size.
        /// </summary>
        private static void ResolvePx(in LayerRange r, float metricFactor, bool tap,
            out float p0, out float p1, out float softness)
        {
            var cov = metricFactor > 1e-9f ? 1f / (GlyphAtlas.Pad * metricFactor) : 0f;
            var pos = metricFactor > 1e-9f ? 1f / metricFactor : 0f;
            p0 = r.p0Px ? r.p0 * (tap ? pos : cov) : r.p0;
            p1 = r.p1Px ? r.p1 * pos : r.p1;
            softness = r.softnessPx ? r.softness * cov : r.softness;
        }

        /// <summary>
        /// The layer's <b>intrinsic</b> outward extent (em-normalized) beyond the glyph edge — bold's
        /// face dilate is NOT included; the tier upgrade adds it (so bold + effect stack regardless of
        /// modifier order). The return feeds <see cref="UniTextMeshGenerator.currentMaxGlyphExtent"/>;
        /// <paramref name="delta"/> is the quad growth past the already-dilated edge, capped to the SDF
        /// pad room. Inner-shadow is inset → no outward extent.
        /// </summary>
        /// <summary>
        /// The outward reach in em a range asks of a colour glyph's silhouette field, estimated at apply
        /// time from the layer geometry alone: px values convert at the component font size, and the
        /// per-glyph scale and height the mesh pass knows later are covered by its tier-upgrade path.
        /// </summary>
        private float EstimateReachEm(in LayerRange r)
        {
            if (CoverageModeValue >= CoverageMode.InnerShadow) return 0f;
            ResolvePx(in r, uniText.FontSize, false, out var p0, out var p1, out var softness);
            var outward = CoverageModeValue == CoverageMode.Stroke
                ? p0 * (1f + p1) + softness
                : p0 + softness;
            return outward > 0f ? outward * GlyphAtlas.Pad : 0f;
        }

        private float ComputeExtent(UniTextMeshGenerator gen, int baseIdx, in LayerRange r,
            float glyphScale, out float delta)
        {
            delta = 0f;
            if (CoverageModeValue >= CoverageMode.InnerShadow) return 0f;

            var glyphH = gen.FaceGlyphH(baseIdx);
            if (glyphH < 1e-6f) return 0f;

            ResolvePx(in r, gen.fontMetricFactor * glyphScale, false, out var p0, out var p1, out var softness);

            var padGlyph = GlyphAtlas.Pad / glyphH;

            var outward = CoverageModeValue == CoverageMode.Stroke
                ? p0 * (1f + p1) + softness
                : p0 + softness;
            if (outward < 0f) outward = 0f;

            var intrinsic = outward * padGlyph;
            var capped = intrinsic > padGlyph ? padGlyph : intrinsic;

            var room = padGlyph - gen.Uvs1[baseIdx].y * padGlyph;
            delta = capped < room ? capped : room;
            if (delta < 0f) delta = 0f;

            return capped;
        }

        private PaintFrame ResolveFrame(in LayerRange r, UniTextMeshGenerator gen, int baseIdx)
        {
            switch (r.paint.mapping)
            {
                case PaintMapping.Glyph:
                    return PaintMappingMath.BuildFrame(in r.paint,GlyphInkRect(gen, baseIdx));
                case PaintMapping.Line:
                    return PaintMappingMath.BuildFrame(in r.paint,LineRectFor(gen, baseIdx));
                default:
                    return r.frame;
            }
        }

        private static Rect GlyphInkRect(UniTextMeshGenerator gen, int baseIdx)
        {
            var v = gen.Vertices;
            var uv = gen.Uvs0;
            var aspect = gen.Uvs1[baseIdx].x;

            var qL = v[baseIdx].x;
            var qR = v[baseIdx + 2].x;
            var qB = v[baseIdx].y;
            var qT = v[baseIdx + 1].y;

            if (gen.TryGetColorFaceField(baseIdx, out var field))
            {
                var padX = Mathf.Abs(qR - qL) * field.padFracX;
                var padY = Mathf.Abs(qT - qB) * field.padFracY;
                return Rect.MinMaxRect(Mathf.Min(qL, qR) + padX, Mathf.Min(qB, qT) + padY,
                    Mathf.Max(qL, qR) - padX, Mathf.Max(qB, qT) - padY);
            }

            var uL = uv[baseIdx].x;
            var uR = uv[baseIdx + 2].x;
            var vB = uv[baseIdx].y;
            var vT = uv[baseIdx + 1].y;

            var du = uR - uL;
            if (Mathf.Abs(du) < 1e-6f) du = 1f;
            var dv = vT - vB;
            if (Mathf.Abs(dv) < 1e-6f) dv = 1f;

            var inkL = qL + (0f - uL) / du * (qR - qL);
            var inkR = qL + (aspect - uL) / du * (qR - qL);
            var inkB = qB + (0f - vB) / dv * (qT - qB);
            var inkT = qB + (1f - vB) / dv * (qT - qB);

            return Rect.MinMaxRect(Mathf.Min(inkL, inkR), Mathf.Min(inkB, inkT), Mathf.Max(inkL, inkR), Mathf.Max(inkB, inkT));
        }

        /// <summary>
        /// Line rect for a glyph's Y, starting the probe at the previously matched line: glyphs
        /// arrive in line order, so the lookup is O(1) amortized instead of a per-glyph scan.
        /// </summary>
        private Rect LineRectFor(UniTextMeshGenerator gen, int baseIdx)
        {
            var count = lineRects.Count;
            if (count == 0) return new Rect(0, 0, 1, 1);
            var y = (gen.Vertices[baseIdx].y + gen.Vertices[baseIdx + 1].y) * 0.5f;
            for (var step = 0; step < count; step++)
            {
                var i = lineRectCursor + step;
                if (i >= count) i -= count;
                var rect = lineRects[i];
                if (y >= rect.yMin && y <= rect.yMax)
                {
                    lineRectCursor = i;
                    return rect;
                }
            }
            return lineRects[0];
        }

        private static Rect Union(PooledList<Rect> rects)
        {
            if (rects.Count == 0) return new Rect(0, 0, 1, 1);
            var r = rects[0];
            float xMin = r.xMin, yMin = r.yMin, xMax = r.xMax, yMax = r.yMax;
            for (var i = 1; i < rects.Count; i++)
            {
                var c = rects[i];
                if (c.xMin < xMin) xMin = c.xMin;
                if (c.yMin < yMin) yMin = c.yMin;
                if (c.xMax > xMax) xMax = c.xMax;
                if (c.yMax > yMax) yMax = c.yMax;
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
