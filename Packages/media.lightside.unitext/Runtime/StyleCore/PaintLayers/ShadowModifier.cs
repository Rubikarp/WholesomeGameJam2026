using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Drop shadow behind the glyph — an offset, blurred duplicate with a colour, gradient, or
    /// texture paint.
    /// </summary>
    /// <remarks>Parameter: <c>paint[,offset][,blur][,spread]</c> + projection overrides (offset = <c>"x y"</c>; px/em).</remarks>
    [Serializable]
    [TypeGroup("Appearance", 3)]
    [TypeColor("#B496FF")]
    [TypeDescription("Adds a drop shadow behind the glyph.")]
    [GenerateParameters]
    public sealed partial class ShadowModifier : PaintLayerModifier
    {
        /// <summary>Shadow paint (inline colour, named swatch, or default). A per-range value overrides it.</summary>
        [SerializeField, Parameter(Descriptor = false), Variant("Default|Color=color:#00000080|Swatch=enum:@paints", Discriminator = nameof(PaintRef.kind)), StateProperty(nameof(MarkMeshDirty))]
        private PaintRef paint;

        /// <summary>Shadow offset. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkMeshDirty))]
        private UnitVector2 offset = new(new Vector2(0.1f, -0.1f), UnitKind.Em);

        /// <summary>Shadow blur radius. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkMeshDirty))]
        private UnitValue blur = UnitValue.Em(0.1f);

        /// <summary>Shadow spread. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkMeshDirty))]
        private UnitValue spread = UnitValue.Em(0.1f);

        protected override PaintRef PaintField => paint;

        protected override float CoverageModeValue => CoverageMode.Shadow;

        protected override Color32 DefaultPaintColor => new Color32(0, 0, 0, 128);

        public override bool RendersBehindFill => true;

        /// <summary>A shadow reads naturally on any silhouette, so emoji and sprites cast one unless a range opts out.</summary>
        protected override ColorGlyphPolicy DefaultColorGlyphPolicy => ColorGlyphPolicy.Apply;

        protected override void ParseExtra(ref ParameterReader reader, in RangeApplyContext context,
            ref LayerGeometry g)
        {
            var resolvedOffset = Param.Offset.ResolveNext(ref reader, this, in context);
            g.offset = resolvedOffset.value;
            g.offsetPx = resolvedOffset.unit == UnitKind.Absolute;
            var resolvedBlur = Param.Blur.ResolveNext(ref reader, this, in context);
            g.softness = resolvedBlur.value;
            g.softnessPx = resolvedBlur.unit == UnitKind.Absolute;
            var resolvedSpread = Param.Spread.ResolveNext(ref reader, this, in context);
            g.p0 = resolvedSpread.value;
            g.p0Px = resolvedSpread.unit == UnitKind.Absolute;
        }
    }
}
