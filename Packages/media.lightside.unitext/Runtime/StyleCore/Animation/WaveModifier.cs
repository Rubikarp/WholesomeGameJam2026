using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightSide
{
    /// <summary>
    /// Vertical sine wave traveling along the text:
    /// <c>offset = sin(Phase·frequency + cluster·spread)·amplitude</c>. Drive
    /// <see cref="GlyphParamModifier{TParams}.Phase"/> to animate; a fixed phase renders that exact
    /// wave snapshot.
    /// </summary>
    [Serializable]
    [TypeGroup("Animation", 1)]
    [TypeDescription("Vertical sine wave along the text; drive Phase to animate.")]
    [GenerateParameters]
    public partial class WaveModifier : GlyphParamModifier<WaveModifier.Params>
    {
        public struct Params
        {
            public float amplitude;
            public float frequency;
            public float spread;
        }

        /// <summary>Peak vertical offset in pixels.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float amplitude = 3f;

        /// <summary>Oscillations per phase unit.</summary>
        [SerializeField, Parameter, FormerlySerializedAs("speed"), StateProperty(nameof(MarkParamsDirty))]
        private float frequency = 3f;

        /// <summary>Phase offset between adjacent clusters — how far the wave travels along the text.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float spread = 0.5f;

        protected override string AttributeKey => AttributeKeys.Wave;

        protected override Params ResolveParams(in RangeApplyContext context) => new()
        {
            amplitude = Param.Amplitude.Resolve(this, in context),
            frequency = Param.Frequency.Resolve(this, in context),
            spread = Param.Spread.Resolve(this, in context),
        };

        protected override void OnGlyph(UniTextMeshGenerator gen, int cluster, in Params p, float phase)
        {
            var offset = Mathf.Sin(phase * p.frequency + cluster * p.spread) * p.amplitude;
            GlyphQuad.Offset(gen.Vertices, gen.faceBaseIdx, 0f, offset);
        }
    }
}
