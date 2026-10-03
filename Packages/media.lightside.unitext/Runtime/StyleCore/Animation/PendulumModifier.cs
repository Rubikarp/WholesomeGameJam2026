using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Glyphs swing around their top edge like hanging signs:
    /// <c>rotation = sin(Phase·frequency + cluster·spread)·angle</c> around the top-center pivot.
    /// </summary>
    [Serializable]
    [TypeGroup("Animation", 7)]
    [TypeDescription("Glyphs swing from their top edge; drive Phase to animate.")]
    [GenerateParameters]
    public partial class PendulumModifier : GlyphParamModifier<PendulumModifier.Params>
    {
        public struct Params
        {
            public float angle;
            public float frequency;
            public float spread;
        }

        /// <summary>Peak swing in degrees.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float angle = 15f;

        /// <summary>Swings per phase unit.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float frequency = 2f;

        /// <summary>Phase offset between adjacent clusters.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float spread = 0.5f;

        protected override string AttributeKey => AttributeKeys.Pendulum;

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
            var baseIdx = gen.faceBaseIdx;
            var pivotX = (verts[baseIdx + 1].x + verts[baseIdx + 2].x) * 0.5f;
            var pivotY = Mathf.Max(verts[baseIdx + 1].y, verts[baseIdx + 2].y);
            GlyphQuad.Rotate(verts, baseIdx, pivotX, pivotY, radians);
        }
    }
}
