using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// RGB-split glitch bursts: during a burst the glyph doubles into magenta and cyan copies pushed
    /// in opposite directions under a crisp original, with horizontal jerks and rare blink-outs.
    /// Re-rolls <see cref="Rate"/> times per phase unit; deterministic per phase, so scrubbing
    /// reproduces the same bursts. A colour glyph under the Apply policy gets the jerks, the
    /// blink-outs and silhouette fringes; its picture stays in its own layer.
    /// </summary>
    [Serializable]
    [TypeGroup("Animation", 9)]
    [TypeDescription("RGB-split glitch bursts; drive Phase to animate.")]
    [GenerateParameters]
    public partial class GlitchModifier : EffectModifier
    {
        public struct Params
        {
            public float intensity;
            public float split;
            public float amplitude;
            public float rate;
            public float phase;
            internal LayerBlend blend;
            internal bool colorGlyphs;
        }

        /// <summary>Fraction of glyphs glitching at any moment (0 none, 1 constant chaos).</summary>
        [SerializeField, Parameter, Range(0f, 1f), NumberStateProperty(nameof(MarkParamsDirty), Clamp01 = true)]
        private float intensity = 0.5f;

        /// <summary>Peak distance of the magenta/cyan copies from the glyph, in pixels.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float split = 3f;

        /// <summary>Peak horizontal jerk in pixels.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float amplitude = 2f;

        /// <summary>Corruption re-rolls per phase unit.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float rate = 12f;

        /// <summary>Animation input in abstract time units. Wholly external — a driver, tween, or Animator writes it; the modifier only renders it.</summary>
        [SerializeField, SlotlessParameter, StateProperty(nameof(MarkParamsDirty))]
        private float phase;

        private void MarkParamsDirty() => MarkRenderDirty(paramSets.Count > 0);

        private struct EmitData
        {
            public float offsetX;
            public Color32 color;
            public bool copyFaceColors;
        }

        private static readonly Color32 magenta = new(255, 0, 255, 255);
        private static readonly Color32 cyan = new(0, 255, 255, 255);

        private struct Entry
        {
            public Params p;
            public RangeApplyMemo memo;
        }

        private PooledArrayAttribute<byte> attribute;
        private readonly PooledList<Entry> paramSets = new();
        private PooledList<EmitData> emitData;

        private string bufferKey;

        /// <summary>
        /// Key of this instance's own parameter-index buffer. An effect is a layer: two glitch modifiers
        /// on one component render from their own parameter sets, so their buffers never alias.
        /// </summary>
        private string BufferKey =>
            bufferKey ??= $"{AttributeKeys.Glitch}#{RuntimeHelpers.GetHashCode(this):x8}";

        protected override void OnEnable()
        {
            base.OnEnable();
            buffers.PrepareAttribute(ref attribute, BufferKey);
            paramSets.FakeClear();
            emitData ??= new PooledList<EmitData>(16);
            emitData.FakeClear();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            attribute?.buffer.data?.AsSpan().Clear();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            buffers?.ReleaseAttributeData(BufferKey);
            attribute = null;
            paramSets.Return();
            emitData?.Return();
            emitData = null;
        }

        protected override void BeforeApply() => paramSets.FakeClear();

        protected override void ResetOwnRequests()
        {
            base.ResetOwnRequests();
            emitData.FakeClear();
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var index = paramSets.Count;
            paramSets.Add(new Entry
            {
                p = ResolveEntryParams(in context),
                memo = context.Retain(),
            });

            var paramIndex = (byte)Math.Min(index + 1, byte.MaxValue);
            var start = context.Segment.Range.start;
            var end = context.Segment.Range.End;
            var actualEnd = Math.Min(end, buffers.codepoints.count);
            var buffer = attribute.buffer.data;
            for (var c = start; c < actualEnd; c++)
                buffer[c] = paramIndex;

            if (paramSets[index].p.colorGlyphs)
                RequestColorGlyphField(start, end, 0f);
        }

        private Params ResolveEntryParams(in RangeApplyContext context) => new()
        {
            intensity = Mathf.Clamp01(Param.Intensity.Resolve(this, in context)),
            split = Param.Split.Resolve(this, in context),
            amplitude = Param.Amplitude.Resolve(this, in context),
            rate = Param.Rate.Resolve(this, in context),
            phase = Param.Phase.Resolve(this, in context),
            blend = ResolveBlend(in context),
            colorGlyphs = ResolveColorGlyphs(in context) == ColorGlyphPolicy.Apply,
        };

        /// <summary>Re-resolves each entry's parameters from its retained context, so parameter and phase changes reach the mesh without a re-apply.</summary>
        protected internal override void PrepareForParallel()
        {
            base.PrepareForParallel();
            if (!IsInitialized) return;
            for (var i = 0; i < paramSets.Count; i++)
            {
                ref var entry = ref paramSets[i];
                var context = entry.memo.ToContext();
                entry.p = ResolveEntryParams(in context);
            }
        }

        protected override void OnGlyphEffect()
        {
            var gen = uniText.MeshGenerator;
            var buffer = attribute.buffer.data;
            var cluster = gen.currentCluster;
            if (buffer == null || (uint)cluster >= (uint)buffer.Length) return;

            int paramIndex = buffer[cluster];
            if (paramIndex == 0 || paramIndex > paramSets.Count) return;
            ref readonly var p = ref paramSets[paramIndex - 1].p;

            var step = Mathf.FloorToInt(p.phase * p.rate);
            if (HashNoise.Hash01(cluster, step, 0) >= p.intensity * 0.4f) return;

            var verts = gen.Vertices;
            var baseIdx = gen.faceBaseIdx;

            if (HashNoise.Hash01(cluster, step, 3) < 0.12f)
            {
                var colors = gen.Colors;
                for (var i = 0; i < 4; i++)
                    colors[baseIdx + i].a = 0;
                return;
            }

            if (p.amplitude != 0f)
            {
                var dx = HashNoise.HashSigned(cluster, step, 1) * p.amplitude;
                var dy = HashNoise.HashSigned(cluster, step, 2) * p.amplitude * 0.2f;
                GlyphQuad.Offset(verts, baseIdx, dx, dy);
            }

            if (p.split <= 0f) return;
            var colorFace = gen.font.IsColor;
            if (colorFace && (!p.colorGlyphs || !gen.HasColorFaceField)) return;

            var fringeCyan = cyan;
            var fringeMagenta = magenta;
            var filterIdx = gen.filters.ResolveIndex(cluster, LayerSequence);
            if (filterIdx != 0)
            {
                var filterMatrix = gen.filters.GetMatrix(filterIdx);
                fringeCyan = filterMatrix.Transform(fringeCyan);
                fringeMagenta = filterMatrix.Transform(fringeMagenta);
            }

            var distance = p.split * (0.6f + 0.8f * HashNoise.Hash01(cluster, step, 4));
            Emit(baseIdx, -distance, fringeCyan, copyFace: false, p.blend);
            Emit(baseIdx, distance, fringeMagenta, copyFace: false, p.blend);
            if (!colorFace) Emit(baseIdx, 0f, default, copyFace: true, p.blend);
        }

        private void Emit(int sourceBaseIdx, float offsetX, Color32 color, bool copyFace,
            LayerBlend blend)
        {
            emitData.Add(new EmitData { offsetX = offsetX, color = color, copyFaceColors = copyFace });
            EnqueueDuplicate(sourceBaseIdx, emitData.Count - 1, blend);
        }

        /// <summary>
        /// The two tinted copies keep the face's per-vertex alpha so fades carry over; the zero-offset
        /// face clone re-draws the original on top of them within this modifier's layer, which is what
        /// keeps the glyph crisp between its colour fringes.
        /// </summary>
        protected override void OnEmitQuad(int sourceBaseIdx, int destBaseIdx, int payload)
        {
            var gen = uniText.MeshGenerator;
            ref readonly var e = ref emitData[payload];

            if (e.offsetX != 0f)
                GlyphQuad.Offset(gen.Vertices, destBaseIdx, e.offsetX, 0f);

            var colors = gen.Colors;
            if (e.copyFaceColors)
            {
                Array.Copy(colors, sourceBaseIdx, colors, destBaseIdx, 4);
            }
            else
            {
                for (var i = 0; i < 4; i++)
                {
                    var c = e.color;
                    c.a = colors[sourceBaseIdx + i].a;
                    colors[destBaseIdx + i] = c;
                }
            }

            var uvs2 = gen.Uvs2;
            for (var i = 0; i < 4; i++)
                uvs2[destBaseIdx + i] = Vector4.zero;

            var uvs3 = gen.Uvs3;
            if (uvs3 != null)
                for (var i = 0; i < 4; i++)
                    uvs3[destBaseIdx + i] = Vector4.zero;
        }
    }
}
