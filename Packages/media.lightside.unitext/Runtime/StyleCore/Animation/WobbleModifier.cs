using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Jelly-like rocking: each glyph tilts back and forth around its center,
    /// <c>rotation = sin(Phase·frequency + cluster·spread)·angle</c>.
    /// </summary>
    [Serializable]
    [TypeGroup("Animation", 2)]
    [TypeDescription("Glyphs rock around their centers; drive Phase to animate.")]
    [GenerateParameters]
    public partial class WobbleModifier : GlyphParamModifier<WobbleModifier.Params>
    {
        public struct Params
        {
            public float angle;
            public float frequency;
            public float spread;
        }

        /// <summary>Peak tilt in degrees.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float angle = 8f;

        /// <summary>Oscillations per phase unit.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float frequency = 4f;

        /// <summary>Phase offset between adjacent clusters.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float spread = 0.8f;

        protected override string AttributeKey => AttributeKeys.Wobble;

        protected override Params ResolveParams(in RangeApplyContext context) => new()
        {
            angle = Param.Angle.Resolve(this, in context),
            frequency = Param.Frequency.Resolve(this, in context),
            spread = Param.Spread.Resolve(this, in context),
        };

        protected override void OnGlyph(UniTextMeshGenerator gen, int cluster, in Params p, float phase)
        {
            var radians = Mathf.Sin(phase * p.frequency + cluster * p.spread) * p.angle * Mathf.Deg2Rad;
            var verts = gen.Vertices;
            var center = GlyphQuad.Center(verts, gen.faceBaseIdx);
            GlyphQuad.Rotate(verts, gen.faceBaseIdx, center.x, center.y, radians);
        }
    }
}
