using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Periodic hops off the baseline: glyphs jump and land with a sharpened half-sine, staying put
    /// between hops. Drive <see cref="GlyphParamModifier{TParams}.Phase"/> to animate.
    /// </summary>
    [Serializable]
    [TypeGroup("Animation", 4)]
    [TypeDescription("Glyphs hop off the baseline; drive Phase to animate.")]
    [GenerateParameters]
    public partial class BounceModifier : GlyphParamModifier<BounceModifier.Params>
    {
        public struct Params
        {
            public float amplitude;
            public float frequency;
            public float spread;
        }

        /// <summary>Hop height in pixels.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float amplitude = 6f;

        /// <summary>Hops per phase unit.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float frequency = 2f;

        /// <summary>Phase offset between adjacent clusters.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float spread = 0.35f;

        protected override string AttributeKey => AttributeKeys.Bounce;

        protected override Params ResolveParams(in RangeApplyContext context) => new()
        {
            amplitude = Param.Amplitude.Resolve(this, in context),
            frequency = Param.Frequency.Resolve(this, in context),
            spread = Param.Spread.Resolve(this, in context),
        };

        protected override void OnGlyph(UniTextMeshGenerator gen, int cluster, in Params p, float phase)
        {
            var s = Mathf.Sin(phase * p.frequency + cluster * p.spread);
            if (s <= 0f) return;
            var offset = s * s * s * p.amplitude;
            GlyphQuad.Offset(gen.Vertices, gen.faceBaseIdx, 0f, offset);
        }
    }
}
