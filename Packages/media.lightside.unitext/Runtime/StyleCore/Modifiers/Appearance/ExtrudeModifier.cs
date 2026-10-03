using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Adds a 3D extrude/bevel effect by appending a stack of progressively offset duplicate
    /// quads behind the face, shaded across a near→far colour gradient.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layers are flushed back-to-front in <i>layer-major</i> order across all glyphs (every
    /// glyph's deepest slice first, then every glyph's next-deeper slice, …, then face). Naïve
    /// per-glyph fanout would invert that order at glyph boundaries when the cumulative offset
    /// exceeds glyph advance, leaving farther slices of glyph N+1 covering closer slices of
    /// glyph N. Per-layer buffers preserve the painter order regardless of overlap.
    /// </para>
    /// <para>
    /// Parameter: <c>offset</c> (<c>"x y"</c>) + <c>[,#nearColor][,#farColor][,dilate][,softness]</c>.
    /// Defaults: offset = (0.3, -0.3) in em (scaled by the glyph metric factor; with
    /// <see cref="FixedPixelSize"/> the values are fixed pixels instead), near = white, far = black,
    /// dilate = 0, softness = 0.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Appearance", 6)]
    [TypeColor("#FFA05A")]
    [TypeDescription("Adds a 3D extrude effect behind the text.")]
    [GenerateParameters]
    public partial class ExtrudeModifier : EffectModifier, IModifierCommitChanges
    {
        UniTextCommitChanges IModifierCommitChanges.CommitChanges
            => UniTextCommitChanges.Appearance;
        /// <summary>Per-step displacement of the extrusion. A per-range value overrides it.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))] private Vector2 offset = new(0.3f, -0.3f);
        /// <summary>Colour of the face-adjacent (near) slice. A per-range value overrides it.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))] private Color32 nearColor = new(255, 255, 255, 255);
        /// <summary>Colour of the deepest (far) slice. A per-range value overrides it.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))] private Color32 farColor = new(0, 0, 0, 255);
        /// <summary>SDF dilation applied to each slice. A per-range value overrides it.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))] private float dilate;
        /// <summary>Edge softness of each slice. A per-range value overrides it.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))] private float softness;

        /// <summary>
        /// Number of extrude layers stacked behind the face (minimum 1). More layers produce a
        /// smoother depth gradient and a longer apparent extrusion at the cost of additional
        /// duplicate-quad geometry per glyph.
        /// </summary>
        [SerializeField, StateProperty(nameof(ApplyStepsChange))] private int steps = 10;

        private void ApplyStepsChange(int previous, ref int current)
        {
            current = Math.Max(1, current);
            if (current == previous) return;
            RebuildLayerBuffers();
            MarkMeshDirty();
        }

        /// <summary>
        /// When <see langword="true"/>, layers alternate between the near and far face colours
        /// rather than blending across, producing a faceted bevel highlight. The layer count
        /// effectively doubles (<see cref="Steps"/> × 2) because each step contributes a near
        /// face and a side face.
        /// </summary>
        [SerializeField, StateProperty(nameof(ApplyBevelChange))] private bool bevel;

        private void ApplyBevelChange()
        {
            RebuildLayerBuffers();
            MarkMeshDirty();
        }

        /// <summary>
        /// When <see langword="true"/>, the per-range <c>offsetX</c> / <c>offsetY</c> /
        /// <c>dilate</c> / <c>softness</c> values are interpreted in fixed pixels and compensated
        /// by the glyph metric factor so the extrusion has constant on-screen geometry independent
        /// of font size.
        /// </summary>
        [SerializeField, StateProperty(nameof(MarkMeshDirty))] private bool fixedPixelSize;

        public override bool RendersBehindFill => true;

        private struct ExtrudeRange
        {
            public int start, end;
            public float dilate, softness;
            public float offsetX, offsetY;
            public Color32 nearColor, farColor;
            public LayerBlend blend;
            public bool colorGlyphs;
        }

        private struct ExtrudeEmit
        {
            public int sourceBaseIdx;
            public Color32 color;
            public float dilate, softness;
            public float offsetX, offsetY;
            public float expandDelta;
            public int sequence;
            public LayerBlend blend;
        }

        private PooledList<ExtrudeRange> ranges;
        private PooledBuffer<ExtrudeEmit>[] layers;
        private int activeSteps;
        private int activeLayerCount;
        private bool isWorldText;
        private Quaternion inverseRotation;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<ExtrudeRange>();
            ranges.FakeClear();

            activeSteps = Math.Max(1, steps);
            activeLayerCount = bevel ? activeSteps * 2 : activeSteps;
            EnsureLayerBuffers(activeLayerCount);
            for (var i = 0; i < activeLayerCount; i++)
                layers[i].FakeClear();

            base.OnEnable();
        }

        protected internal override void PrepareForParallel()
        {
            isWorldText = uniText != null && uniText.IsWorldSpace;
            if (isWorldText)
                inverseRotation = Quaternion.Inverse(uniText.transform.rotation);
        }

        protected override void OnDestroy()
        {
            ranges?.Return();
            ranges = null;
            if (layers != null)
            {
                for (var i = 0; i < layers.Length; i++)
                    layers[i].Return();
                layers = null;
            }
            base.OnDestroy();
        }

        protected override void ResetOwnRequests()
        {
            base.ResetOwnRequests();
            if (layers == null) return;
            for (var i = 0; i < layers.Length; i++)
                layers[i].FakeClear();
        }

        protected override void BeforeApply() => ranges?.FakeClear();

        protected override void OnApply(in RangeApplyContext context)
        {
            var resolvedBlend = ResolveBlend(in context);
            var off = Param.Offset.Resolve(this, in context);
            var near = Param.NearColor.Resolve(this, in context);
            var far = Param.FarColor.Resolve(this, in context);
            var dil = Param.Dilate.Resolve(this, in context);
            var soft = Param.Softness.Resolve(this, in context);
            var colorGlyphs = ResolveColorGlyphs(in context) == ColorGlyphPolicy.Apply;

            var start = context.Segment.Range.start;
            var end = context.Segment.Range.End;
            ranges.Add(new ExtrudeRange
            {
                start = start,
                end = end,
                dilate = dil,
                softness = soft,
                offsetX = off.x,
                offsetY = off.y,
                nearColor = near,
                farColor = far,
                blend = resolvedBlend,
                colorGlyphs = colorGlyphs,
            });

            if (!colorGlyphs) return;
            var reachEm = fixedPixelSize
                ? (dil + soft) / Mathf.Max(uniText.FontSize, 1e-3f)
                : dil * GlyphAtlas.Pad + soft;
            RequestColorGlyphField(start, end, reachEm);
        }

        protected override void OnGlyphEffect()
        {
            var gen = uniText.MeshGenerator;
            var colorFace = gen.font.IsColor;
            if (colorFace && !gen.HasColorFaceField) return;

            var cluster = gen.currentCluster;
            var count = ranges.buffer.count;
            var data = ranges.buffer.data;

            for (var i = 0; i < count; i++)
            {
                ref var range = ref data[i];
                if (cluster < range.start || cluster >= range.end) continue;
                if (colorFace && !range.colorGlyphs) continue;

                var baseIdx = gen.faceBaseIdx;
                var glyphH = gen.FaceGlyphH(baseIdx);
                if (glyphH < 1e-6f) return;

                var faceDilate = gen.Uvs1[baseIdx].y;
                var padGlyph = GlyphAtlas.Pad / glyphH;

                float dilate, softness, meshOffX, meshOffY;
                if (fixedPixelSize)
                {
                    var glyphScale = gen.currentGlyphScale;
                    var metricFactor = gen.fontMetricFactor * glyphScale;
                    dilate = range.dilate / (GlyphAtlas.Pad * metricFactor);
                    softness = range.softness / metricFactor;
                    var of = gen.fontMetricFactor / (gen.height * glyphScale);
                    meshOffX = range.offsetX * of;
                    meshOffY = range.offsetY * of;
                }
                else
                {
                    dilate = range.dilate;
                    softness = range.softness;
                    meshOffX = range.offsetX * gen.fontMetricFactor;
                    meshOffY = range.offsetY * gen.fontMetricFactor;
                }

                if (isWorldText)
                {
                    var localDir = inverseRotation * new Vector3(meshOffX, meshOffY, 0f);
                    meshOffX = localDir.x;
                    meshOffY = localDir.y;
                }

                var intrinsic = dilate * padGlyph + softness / glyphH;
                if (intrinsic < 0f) intrinsic = 0f;
                var growth = intrinsic > padGlyph ? padGlyph : intrinsic;
                if (growth > gen.currentMaxGlyphExtent)
                    gen.currentMaxGlyphExtent = growth;

                var room = padGlyph - faceDilate * padGlyph;
                var expandDelta = growth < room ? growth : room;
                if (expandDelta < 0f) expandDelta = 0f;

                var s = (float)activeSteps;
                var sequence = LayerSequence + gen.sequenceBias;

                var near = range.nearColor;
                var far = range.farColor;
                var filterIdx = gen.filters.ResolveIndex(cluster, LayerSequence);
                if (filterIdx != 0)
                {
                    var filterMatrix = gen.filters.GetMatrix(filterIdx);
                    near = filterMatrix.Transform(near);
                    far = filterMatrix.Transform(far);
                }

                if (!bevel)
                {
                    for (var layer = 0; layer < activeSteps; layer++)
                    {
                        var t = (activeSteps - layer) / s;
                        var color = Color32.Lerp(near, far, t);
                        layers[layer].Add(new ExtrudeEmit
                        {
                            sourceBaseIdx = baseIdx,
                            color = color,
                            dilate = dilate,
                            softness = softness,
                            offsetX = meshOffX * t,
                            offsetY = meshOffY * t,
                            expandDelta = expandDelta,
                            sequence = sequence,
                            blend = range.blend,
                        });
                    }
                }
                else
                {
                    for (var layer = 0; layer < activeLayerCount; layer++)
                    {
                        var pair = layer / 2;
                        var step = activeSteps - 1 - pair;
                        var isXFace = (layer % 2) == 1;
                        var tMain = (step + 1) / s;
                        var tSide = step / s;

                        float offX, offY;
                        Color32 layerColor;
                        if (isXFace)
                        {
                            offX = meshOffX * tMain;
                            offY = meshOffY * tSide;
                            layerColor = far;
                        }
                        else
                        {
                            offX = meshOffX * tSide;
                            offY = meshOffY * tMain;
                            layerColor = near;
                        }

                        layers[layer].Add(new ExtrudeEmit
                        {
                            sourceBaseIdx = baseIdx,
                            color = layerColor,
                            dilate = dilate,
                            softness = softness,
                            offsetX = offX,
                            offsetY = offY,
                            expandDelta = expandDelta,
                            sequence = sequence,
                            blend = range.blend,
                        });
                    }
                }

                return;
            }
        }

        /// <summary>
        /// Layer-major flush: every glyph's deepest slice first, then every glyph's next slice, …,
        /// then the face. Each layer maintains its own emit buffer, so painter order survives
        /// inter-glyph overlap when the cumulative extrusion offset exceeds glyph advance. All slices
        /// record the sequence captured for their glyph; the generator's stable sort then keeps the
        /// deepest-first emission order within the layer.
        /// </summary>
        protected override void OnFlush()
        {
            var gen = uniText.MeshGenerator;
            var frame = default(PaintFrame);

            for (var l = 0; l < activeLayerCount; l++)
            {
                var count = layers[l].count;
                if (count == 0) continue;

                var data = layers[l].data;
                for (var i = 0; i < count; i++)
                {
                    ref var e = ref data[i];
                    var destIdx = ReserveQuad(gen, e.sourceBaseIdx, e.sequence, e.blend);
                    var scale = gen.GlyphScale(e.sourceBaseIdx);
                    var paint = TextPaint.Solid(e.color);
                    CoverageQuadOps.Write(gen, destIdx, in paint, in frame, -1,
                        CoverageMode.Fill, 0f, 0f, e.softness, 255);
                    CoverageQuadOps.ModulateAlpha(gen.Colors, destIdx, gen, e.sourceBaseIdx);
                    if (e.dilate != 0f) AddDilate(gen, destIdx, e.dilate);
                    CoverageQuadOps.ApplyOffset(gen, destIdx, e.offsetX * scale, e.offsetY * scale);
                    if (e.expandDelta > 0f)
                        gen.ExpandQuad(destIdx, e.expandDelta);
                }
            }
        }

        private static void AddDilate(UniTextMeshGenerator gen, int destBaseIdx, float dilate)
        {
            var uvs1 = gen.Uvs1;
            uvs1[destBaseIdx].y     += dilate;
            uvs1[destBaseIdx + 1].y += dilate;
            uvs1[destBaseIdx + 2].y += dilate;
            uvs1[destBaseIdx + 3].y += dilate;
        }

        /// <summary>
        /// Not used — <see cref="ExtrudeModifier"/> bypasses the base emit-queue indirection and
        /// writes all per-layer slots directly inside its <see cref="OnFlush"/> override.
        /// </summary>
        protected override void OnEmitQuad(int sourceBaseIdx, int destBaseIdx, int payload)
        {
        }

        private void RebuildLayerBuffers()
        {
            if (!IsInitialized) return;
            activeSteps = Math.Max(1, steps);
            activeLayerCount = bevel ? activeSteps * 2 : activeSteps;
            EnsureLayerBuffers(activeLayerCount);
            for (var i = 0; i < activeLayerCount; i++)
                layers[i].FakeClear();
        }

        private void EnsureLayerBuffers(int count)
        {
            if (layers != null && layers.Length == count) return;

            if (layers != null)
                for (var i = 0; i < layers.Length; i++)
                    layers[i].Return();

            layers = new PooledBuffer<ExtrudeEmit>[count];
        }
    }
}
