using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Scale pulsation around each glyph's center:
    /// <c>scale = 1 + sin(Phase·frequency + cluster·spread)·amount</c>.
    /// </summary>
    [Serializable]
    [TypeGroup("Animation", 3)]
    [TypeDescription("Glyphs pulse in scale; drive Phase to animate.")]
    [GenerateParameters]
    public partial class PulseModifier : GlyphParamModifier<PulseModifier.Params>
    {
        public struct Params
        {
            public float amount;
            public float frequency;
            public float spread;
        }

        /// <summary>Peak scale deviation: 0.15 pulses between 85% and 115%.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float amount = 0.15f;

        /// <summary>Pulses per phase unit.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float frequency = 3f;

        /// <summary>Phase offset between adjacent clusters.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float spread = 0.3f;

        protected override string AttributeKey => AttributeKeys.Pulse;

        protected override Params ResolveParams(in RangeApplyContext context) => new()
        {
            amount = Param.Amount.Resolve(this, in context),
            frequency = Param.Frequency.Resolve(this, in context),
            spread = Param.Spread.Resolve(this, in context),
        };

        protected override void OnGlyph(UniTextMeshGenerator gen, int cluster, in Params p, float phase)
        {
            var scale = 1f + Mathf.Sin(phase * p.frequency + cluster * p.spread) * p.amount;
            var verts = gen.Vertices;
            var center = GlyphQuad.Center(verts, gen.faceBaseIdx);
            GlyphQuad.Scale(verts, gen.faceBaseIdx, center.x, center.y, scale, scale);
        }
    }
}
