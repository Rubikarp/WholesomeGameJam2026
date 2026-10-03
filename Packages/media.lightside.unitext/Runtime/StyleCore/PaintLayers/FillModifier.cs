using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Fills the glyph interior with a colour, gradient, or texture. The first fill on a glyph
    /// claims the (default-suppressed) base quad; additional fills stack above it. Any fill present
    /// suppresses the implicit default fill.
    /// </summary>
    /// <remarks>Parameter: <c>paint[,softness][,dilate]</c> (paint = colour literal or swatch name) + projection overrides.</remarks>
    [Serializable]
    [TypeGroup("Appearance", 1)]
    [TypeDescription("Fills the glyph interior with a colour, gradient, or texture.")]
    [GenerateParameters]
    public sealed partial class FillModifier : PaintLayerModifier
    {
        /// <summary>Fill paint (inline colour, named swatch, or default). A per-range value overrides it.</summary>
        [SerializeField, Parameter(Descriptor = false), Variant("Default|Color=color:#FFFFFFFF|Swatch=enum:@paints", Discriminator = nameof(PaintRef.kind)), StateProperty(nameof(MarkMeshDirty))]
        private PaintRef paint;

        /// <summary>Edge softness. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkMeshDirty))]
        private UnitValue softness = UnitValue.Em(0f);

        /// <summary>Outward dilation of the fill. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkMeshDirty))]
        private UnitValue dilate = UnitValue.Em(0f);

        protected override PaintRef PaintField => paint;

        protected override float CoverageModeValue => CoverageMode.Fill;
        protected override bool ClaimsBase => true;

        public override bool ClaimsFill => true;

        protected override void ParseExtra(ref ParameterReader reader, in RangeApplyContext context,
            ref LayerGeometry g)
        {
            var resolvedSoftness = Param.Softness.ResolveNext(ref reader, this, in context);
            g.softness = resolvedSoftness.value;
            g.softnessPx = resolvedSoftness.unit == UnitKind.Absolute;
            var resolvedDilate = Param.Dilate.ResolveNext(ref reader, this, in context);
            g.p0 = resolvedDilate.value;
            g.p0Px = resolvedDilate.unit == UnitKind.Absolute;
        }
    }
}
