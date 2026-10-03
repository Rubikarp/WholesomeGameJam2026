using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Soft halo around the glyph (a shadow with no offset and a wide soft edge), with a colour,
    /// gradient, or texture.
    /// </summary>
    /// <remarks>Parameter: <c>paint[,radius]</c> + projection overrides.</remarks>
    [Serializable]
    [TypeGroup("Appearance", 4)]
    [TypeDescription("Adds a soft glow halo around the glyph.")]
    [GenerateParameters]
    public sealed partial class GlowModifier : PaintLayerModifier
    {
        /// <summary>Glow paint (inline colour, named swatch, or default). A per-range value overrides it.</summary>
        [SerializeField, Parameter(Descriptor = false), Variant("Default|Color=color:#FFFFFFFF|Swatch=enum:@paints", Discriminator = nameof(PaintRef.kind)), StateProperty(nameof(MarkMeshDirty))]
        private PaintRef paint;

        /// <summary>Halo radius. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkMeshDirty))]
        private UnitValue radius = UnitValue.Em(0.3f);

        protected override PaintRef PaintField => paint;

        protected override float CoverageModeValue => CoverageMode.Shadow;

        public override bool RendersBehindFill => true;

        /// <summary>A glow reads naturally on any silhouette, so emoji and sprites glow unless a range opts out.</summary>
        protected override ColorGlyphPolicy DefaultColorGlyphPolicy => ColorGlyphPolicy.Apply;

        protected override void ParseExtra(ref ParameterReader reader, in RangeApplyContext context,
            ref LayerGeometry g)
        {
            var resolvedRadius = Param.Radius.ResolveNext(ref reader, this, in context);
            var px = resolvedRadius.unit == UnitKind.Absolute;
            g.p0 = resolvedRadius.value;
            g.softness = resolvedRadius.value;
            g.p0Px = px;
            g.softnessPx = px;
        }
    }
}
