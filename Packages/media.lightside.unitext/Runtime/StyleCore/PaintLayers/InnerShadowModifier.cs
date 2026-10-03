using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Inset shadow that hugs the inner edge of the glyph, with a colour, gradient, or texture.
    /// The offset is a shader parameter (second SDF tap), not a quad displacement.
    /// </summary>
    /// <remarks>Parameter: <c>paint[,offset][,blur]</c> + projection overrides (offset = <c>"x y"</c>; px/em, applied as the SDF tap).</remarks>
    [Serializable]
    [TypeGroup("Appearance", 5)]
    [TypeDescription("Adds an inset shadow inside the glyph.")]
    [GenerateParameters]
    public sealed partial class InnerShadowModifier : PaintLayerModifier
    {
        /// <summary>Inner-shadow paint (inline colour, named swatch, or default). A per-range value overrides it.</summary>
        [SerializeField, Parameter(Descriptor = false), Variant("Default|Color=color:#000000FF|Swatch=enum:@paints", Discriminator = nameof(PaintRef.kind)), StateProperty(nameof(MarkMeshDirty))]
        private PaintRef paint;

        /// <summary>Inner-shadow tap offset (applied as a second SDF tap). A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkMeshDirty))]
        private UnitVector2 offset = new(new Vector2(0f, -0.1f), UnitKind.Em);

        /// <summary>Inner-shadow blur radius. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkMeshDirty))]
        private UnitValue blur = UnitValue.Em(0.1f);

        protected override PaintRef PaintField => paint;

        protected override float CoverageModeValue => CoverageMode.InnerShadow;

        protected override Color32 DefaultPaintColor => new Color32(0, 0, 0, 255);

        protected override void ParseExtra(ref ParameterReader reader, in RangeApplyContext context,
            ref LayerGeometry g)
        {
            var resolvedOffset = Param.Offset.ResolveNext(ref reader, this, in context);
            var tapPx = resolvedOffset.unit == UnitKind.Absolute;
            g.p0 = resolvedOffset.value.x;
            g.p1 = resolvedOffset.value.y;
            g.p0Px = tapPx;
            g.p1Px = tapPx;
            var resolvedBlur = Param.Blur.ResolveNext(ref reader, this, in context);
            g.softness = resolvedBlur.value;
            g.softnessPx = resolvedBlur.unit == UnitKind.Absolute;
        }
    }
}
