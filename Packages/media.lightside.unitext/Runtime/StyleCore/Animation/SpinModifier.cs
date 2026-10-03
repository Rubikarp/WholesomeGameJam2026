using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Continuous rotation of each glyph around its center:
    /// <c>rotation = (Phase·frequency + cluster·spread)</c> full turns.
    /// </summary>
    [Serializable]
    [TypeGroup("Animation", 8)]
    [TypeDescription("Glyphs rotate around their centers; drive Phase to animate.")]
    [GenerateParameters]
    public partial class SpinModifier : GlyphParamModifier<SpinModifier.Params>
    {
        public struct Params
        {
            public float frequency;
            public float spread;
        }

        /// <summary>Full turns per phase unit.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float frequency = 0.5f;

        /// <summary>Turn offset between adjacent clusters.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float spread = 0.1f;

        protected override string AttributeKey => AttributeKeys.Spin;

        protected override Params ResolveParams(in RangeApplyContext context) => new()
        {
            frequency = Param.Frequency.Resolve(this, in context),
            spread = Param.Spread.Resolve(this, in context),
        };

        protected override void OnGlyph(UniTextMeshGenerator gen, int cluster, in Params p, float phase)
        {
            var radians = (phase * p.frequency + cluster * p.spread) * (2f * Mathf.PI);
            var verts = gen.Vertices;
            var center = GlyphQuad.Center(verts, gen.faceBaseIdx);
            GlyphQuad.Rotate(verts, gen.faceBaseIdx, center.x, center.y, radians);
        }
    }
}
