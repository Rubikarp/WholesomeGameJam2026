using System;

namespace LightSide
{
    /// <summary>
    /// Base class for modifiers that append duplicate glyph geometry behind or in front of the
    /// face (outline, shadow, extrude, glow, custom effects).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contract:
    /// </para>
    /// <list type="number">
    /// <item>At the geometry-consumer tail of <c>onGlyph</c>, after glyph transformations, the
    /// subclass's <see cref="OnGlyphEffect"/> decides whether the current glyph needs an effect quad
    /// and, if so, calls <see cref="EnqueueDuplicate"/> with an arbitrary <c>payload</c> integer —
    /// typically an index into the modifier's own per-emit data table.</item>
    /// <item>At <c>onMainPassFinalize</c>, the per-modifier emit queue is flushed via
    /// <see cref="OnFlush"/>. The default implementation reserves 4 vertices for each pending emit
    /// (copying source geometry, recording the quad at the modifier's <see cref="ILayer.LayerSequence"/>)
    /// and calls <see cref="OnEmitQuad"/> so the subclass can fill colour, UV2, offsets. Multi-layer
    /// effects override <see cref="OnFlush"/> to flush per-layer buffers.</item>
    /// </list>
    /// <para>
    /// Painter order is the single layer axis: every quad — face and duplicates — records its
    /// <see cref="ILayer.LayerSequence"/>, and the generator sorts the index buffer by it, so
    /// stacking follows the order layers appear in <c>Styles</c>.
    /// </para>
    /// <para>
    /// The base class deliberately knows <em>nothing</em> about colour, dilate, softness, offsets,
    /// gradients, or any effect-specific parameter. Those live entirely inside the subclass.
    /// Common write patterns (solid colour + UV2, per-vertex gradient, offset, expand) are exposed
    /// via the static <see cref="CoverageQuadOps"/> helpers so subclasses share the implementation
    /// without sharing the data layout.
    /// </para>
    /// </remarks>
    [Serializable]
    [GenerateParameters]
    public abstract partial class EffectModifier : BaseModifier, ILayer
    {
        /// <summary>How this effect's emitted quads composite with the layers rendered beneath them.</summary>
        [UnityEngine.SerializeField, Parameter, Inheritable, StateProperty(nameof(MarkMeshDirty))]
        private LayerBlend blend = LayerBlend.Inherit;

        /// <summary>
        /// Gradient continuation outside the paint's mapping frame, honoured by effects that project a
        /// paint; Inherit uses the swatch's value.
        /// </summary>
        [UnityEngine.SerializeField, Parameter, Inheritable, StateProperty(nameof(MarkMeshDirty))]
        private PaintSpread paintSpread = PaintSpread.Inherit;

        /// <summary>
        /// Whether the effect decorates colour glyphs — emoji and inline sprites — through their
        /// silhouette; Inherit uses the effect's own default (<see cref="DefaultColorGlyphPolicy"/>). The
        /// final parameter slot of every effect, so any parameter added later must follow it.
        /// </summary>
        [UnityEngine.SerializeField, Parameter, Inheritable, StateProperty(nameof(MarkMeshDirty))]
        private ColorGlyphPolicy colorGlyphs = ColorGlyphPolicy.Inherit;

        public int LayerSequence { get; set; }
        public virtual bool RendersBehindFill => false;
        public virtual bool ClaimsFill => false;

        /// <summary>The colour-glyph policy an unresolved <see cref="ColorGlyphPolicy.Inherit"/> chain resolves to: skip, unless the effect reads naturally on a silhouette (a shadow, a glow).</summary>
        protected virtual ColorGlyphPolicy DefaultColorGlyphPolicy => ColorGlyphPolicy.Skip;

        private PooledArrayAttribute<byte> colorGlyphFieldAttribute;

        /// <summary>
        /// One pending request to duplicate a face quad. The subclass attaches arbitrary
        /// meaning to <see cref="payload"/> — typically an index into its own per-emit table.
        /// </summary>
        protected struct EmitRecord
        {
            public int sourceBaseIdx;
            public int payload;
            public int sequence;
            public LayerBlend blend;
        }

        private PooledBuffer<EmitRecord> emits;

        private Action onGlyphCallback;
        private Action onRebuildStartCallback;
        private Action flushCallback;

        /// <summary>
        /// Called once per glyph during mesh generation. The subclass decides whether the current
        /// glyph (<see cref="UniTextMeshGenerator.currentCluster"/>) needs an effect quad and
        /// invokes <see cref="EnqueueDuplicate"/> if so. A colour glyph (<c>font.IsColor</c>) may be
        /// decorated only when <see cref="UniTextMeshGenerator.HasColorFaceField"/> holds and the
        /// range's colour-glyph policy is <see cref="ColorGlyphPolicy.Apply"/>; its duplicates then
        /// sample the silhouette field instead of the bitmap.
        /// </summary>
        protected abstract void OnGlyphEffect();

        /// <summary>Resolves this effect's range blend override against its inherited mode.</summary>
        protected LayerBlend ResolveBlend(ref ParameterReader reader, in RangeApplyContext context,
            LayerBlend inherited = LayerBlend.Normal)
        {
            var resolved = Param.Blend.ResolveNext(ref reader, this, in context);
            return resolved != LayerBlend.Inherit ? resolved : inherited;
        }

        /// <summary>
        /// Resolves this effect's range blend override against its inherited mode by the
        /// parameter's own slot — for effects that read no other parameter through a reader.
        /// </summary>
        protected LayerBlend ResolveBlend(in RangeApplyContext context,
            LayerBlend inherited = LayerBlend.Normal)
        {
            var resolved = Param.Blend.Resolve(this, in context);
            return resolved != LayerBlend.Inherit ? resolved : inherited;
        }

        /// <summary>
        /// Resolves this effect's range spread override against its inherited value. Reads the slot
        /// before the colour-glyph policy, so call it after every other paint parameter the effect reads.
        /// </summary>
        protected PaintSpread ResolveSpread(ref ParameterReader reader, in RangeApplyContext context,
            PaintSpread inherited = PaintSpread.Clamp)
        {
            var resolved = Param.PaintSpread.ResolveNext(ref reader, this, in context);
            return resolved != PaintSpread.Inherit ? resolved : inherited;
        }

        /// <summary>
        /// Resolves the range's colour-glyph policy — Inherit falls through to
        /// <see cref="DefaultColorGlyphPolicy"/>. Reads the final parameter slot, so call it last.
        /// </summary>
        protected ColorGlyphPolicy ResolveColorGlyphs(ref ParameterReader reader, in RangeApplyContext context)
        {
            var resolved = Param.ColorGlyphs.ResolveNext(ref reader, this, in context);
            return resolved != ColorGlyphPolicy.Inherit ? resolved : DefaultColorGlyphPolicy;
        }

        /// <summary>Resolves the range's colour-glyph policy by the parameter's own slot — for effects that read no other parameter through a reader.</summary>
        protected ColorGlyphPolicy ResolveColorGlyphs(in RangeApplyContext context)
        {
            var resolved = Param.ColorGlyphs.Resolve(this, in context);
            return resolved != ColorGlyphPolicy.Inherit ? resolved : DefaultColorGlyphPolicy;
        }

        /// <summary>
        /// Asks the atlas pipeline for the silhouette fields of the colour glyphs in <c>[start, end)</c>
        /// (codepoint indices), reserving at least <paramref name="reachEm"/> of rim beyond the glyph edge.
        /// Call from <see cref="BaseModifier.OnApply"/> for every range whose policy is
        /// <see cref="ColorGlyphPolicy.Apply"/>; the fields exist by the time the mesh is built.
        /// </summary>
        protected void RequestColorGlyphField(int start, int end, float reachEm)
            => ColorGlyphField.Request(colorGlyphFieldAttribute, start, end, reachEm);

        /// <summary>
        /// Writes the actual per-vertex data into a reserved effect-quad slot. The base has
        /// already copied geometry (positions, UV0, UV1) from the source face quad and queued the
        /// triangle indices; <paramref name="destBaseIdx"/> points at the first of the four
        /// duplicate vertices. The subclass writes <see cref="UniTextMeshGenerator.Colors"/>,
        /// <see cref="UniTextMeshGenerator.Uvs2"/>, and (if needed) re-positions
        /// <see cref="UniTextMeshGenerator.Vertices"/> through <see cref="CoverageQuadOps"/> helpers.
        /// </summary>
        /// <param name="sourceBaseIdx">First vertex of the source face quad — read-only reference for alpha modulation, gradient evaluation, etc.</param>
        /// <param name="destBaseIdx">First vertex of the reserved effect quad — destination of all writes.</param>
        /// <param name="payload">Arbitrary subclass-defined tag captured at <see cref="EnqueueDuplicate"/> time.</param>
        protected abstract void OnEmitQuad(int sourceBaseIdx, int destBaseIdx, int payload);

        protected override void OnEnable()
        {
            emits.FakeClear();
            buffers.PrepareAttribute(ref colorGlyphFieldAttribute, AttributeKeys.ColorGlyphField);

            onGlyphCallback ??= OnGlyph;
            onRebuildStartCallback ??= ResetOwnRequests;
            flushCallback ??= OnFlush;

            var gen = uniText.MeshGenerator;
            gen.onGlyph.Subscribe(onGlyphCallback, UniTextMeshGenerator.GlyphGeometryConsumerOrder);
            gen.onRebuildStart.Subscribe(onRebuildStartCallback);
            gen.onMainPassFinalize.Subscribe(flushCallback);
        }

        protected override void OnDisable()
        {
            var gen = uniText.MeshGenerator;
            gen.onGlyph.Unsubscribe(onGlyphCallback);
            gen.onRebuildStart.Unsubscribe(onRebuildStartCallback);
            gen.onMainPassFinalize.Unsubscribe(flushCallback);
        }

        protected override void OnDestroy()
        {
            emits.Return();
            buffers?.ReleaseAttributeData(AttributeKeys.ColorGlyphField);
            colorGlyphFieldAttribute = null;
            onGlyphCallback = null;
            onRebuildStartCallback = null;
            flushCallback = null;
        }

        private void OnGlyph() => OnGlyphEffect();

        /// <summary>
        /// Resets per-cycle emit state. Subclasses with extra buffers (e.g. multi-layer effects)
        /// override and chain to clear their own storage.
        /// </summary>
        protected virtual void ResetOwnRequests()
        {
            emits.FakeClear();
        }

        /// <summary>
        /// Queues a duplicate of the given face quad for emission at <c>onMainPassFinalize</c>, recorded
        /// at this modifier's <see cref="ILayer.LayerSequence"/> — raised by the generator's current
        /// <see cref="UniTextMeshGenerator.sequenceBias"/>, captured here — so it stacks at its layer
        /// position inside whichever band the glyph is emitted into.
        /// </summary>
        /// <param name="sourceBaseIdx">First vertex of the source face quad (use <c>gen.faceBaseIdx</c>).</param>
        /// <param name="payload">Arbitrary integer carried verbatim into <see cref="OnEmitQuad"/>. Typically an index into a per-emit table the subclass maintains alongside its per-range data.</param>
        protected void EnqueueDuplicate(int sourceBaseIdx, int payload = 0,
            LayerBlend blend = LayerBlend.Normal)
            => EnqueueDuplicate(sourceBaseIdx, payload, blend, LayerSequence + uniText.MeshGenerator.sequenceBias);

        /// <summary>Queues a duplicate at an explicit layer <paramref name="sequence"/> — a claimed fill's output sequence rather than this modifier's own.</summary>
        protected void EnqueueDuplicate(int sourceBaseIdx, int payload, LayerBlend blend, int sequence)
        {
            emits.Add(new EmitRecord
            {
                sourceBaseIdx = sourceBaseIdx,
                payload = payload,
                sequence = sequence,
                blend = blend,
            });
        }

        /// <summary>
        /// Flushes the pending emit queue. Default implementation processes <see cref="emits"/>
        /// in the order they were enqueued — correct for single-layer effects. Multi-layer
        /// effects (e.g. extrude) override and iterate their own per-layer buffers in layer-major
        /// order, calling <see cref="ReserveQuad"/> + <see cref="OnEmitQuad"/> directly.
        /// </summary>
        protected virtual void OnFlush()
        {
            var count = emits.count;
            if (count == 0) return;

            var gen = uniText.MeshGenerator;
            var data = emits.data;
            for (var i = 0; i < count; i++)
            {
                ref var e = ref data[i];
                var destIdx = ReserveQuad(gen, e.sourceBaseIdx, e.sequence, e.blend);
                OnEmitQuad(e.sourceBaseIdx, destIdx, e.payload);
            }
        }

        /// <summary>
        /// Reserves a 4-vertex effect-quad slot: copies positions, UV0 and UV1 from the source quad —
        /// or, for a colour face that carries a silhouette field, builds the SDF quad over that field —
        /// records the quad at <paramref name="sequence"/> with <paramref name="blend"/>, and advances
        /// <see cref="UniTextMeshGenerator.vertexCount"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="UniTextMeshGenerator.Colors"/> and <see cref="UniTextMeshGenerator.Uvs2"/>
        /// are <strong>not</strong> initialised here — the subclass writes both in
        /// <see cref="OnEmitQuad"/> via the <see cref="CoverageQuadOps"/> helpers. Leaving them
        /// untouched keeps the base ignorant of per-effect data layout.
        /// </para>
        /// </remarks>
        /// <returns>First vertex index of the new quad.</returns>
        protected static int ReserveQuad(UniTextMeshGenerator gen, int sourceBaseIdx, int sequence,
            LayerBlend blend = LayerBlend.Normal)
        {
            gen.EnsureCapacity(4, 0);
            gen.EnsureUvBuffer(2);

            var destIdx = gen.vertexCount;
            var verts = gen.Vertices;
            var uvs0 = gen.Uvs0;
            var uvs1 = gen.Uvs1;

            if (gen.TryGetColorFaceField(sourceBaseIdx, out var field))
            {
                ColorFieldQuad.Write(verts, uvs1[sourceBaseIdx].z, verts, uvs0, uvs1,
                    sourceBaseIdx, destIdx, in field);
            }
            else
            {
                Array.Copy(verts, sourceBaseIdx, verts, destIdx, 4);
                Array.Copy(uvs0, sourceBaseIdx, uvs0, destIdx, 4);
                Array.Copy(uvs1, sourceBaseIdx, uvs1, destIdx, 4);
            }

            gen.AddSdfQuad(sequence, destIdx, blend);
            gen.vertexCount += 4;
            return destIdx;
        }
    }
}
