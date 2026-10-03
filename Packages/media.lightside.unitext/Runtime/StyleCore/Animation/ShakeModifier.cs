using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Per-glyph jitter: each glyph gets a pseudo-random offset and tilt that re-rolls
    /// <see cref="Params.rate"/> times per phase unit. Deterministic — the same phase always renders
    /// the same jitter, so scrubbing and rewinding work.
    /// </summary>
    [Serializable]
    [TypeGroup("Animation", 5)]
    [TypeDescription("Random per-glyph jitter; drive Phase to animate.")]
    [GenerateParameters]
    public partial class ShakeModifier : GlyphParamModifier<ShakeModifier.Params>
    {
        public struct Params
        {
            public float amplitude;
            public float angle;
            public float rate;
        }

        /// <summary>Peak positional jitter in pixels.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float amplitude = 1.5f;

        /// <summary>Peak tilt in degrees. 0 keeps glyphs upright.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float angle = 6f;

        /// <summary>Jitter re-rolls per phase unit.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
        private float rate = 15f;

        protected override string AttributeKey => AttributeKeys.Shake;

        protected override Params ResolveParams(in RangeApplyContext context) => new()
        {
            amplitude = Param.Amplitude.Resolve(this, in context),
            angle = Param.Angle.Resolve(this, in context),
            rate = Param.Rate.Resolve(this, in context),
        };

        protected override void OnGlyph(UniTextMeshGenerator gen, int cluster, in Params p, float phase)
        {
            var step = Mathf.FloorToInt(phase * p.rate);
            var verts = gen.Vertices;
            var baseIdx = gen.faceBaseIdx;

            if (p.amplitude != 0f)
            {
                var dx = HashNoise.HashSigned(cluster, step, 1) * p.amplitude;
                var dy = HashNoise.HashSigned(cluster, step, 2) * p.amplitude;
                GlyphQuad.Offset(verts, baseIdx, dx, dy);
            }

            if (p.angle != 0f)
            {
                var center = GlyphQuad.Center(verts, baseIdx);
                var radians = HashNoise.HashSigned(cluster, step, 3) * p.angle * Mathf.Deg2Rad;
                GlyphQuad.Rotate(verts, baseIdx, center.x, center.y, radians);
            }
        }
    }
}
