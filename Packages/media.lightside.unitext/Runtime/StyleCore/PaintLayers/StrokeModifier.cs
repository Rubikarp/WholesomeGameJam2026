using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Draws a real outline (a rim band around the glyph edge, not a filled blob) with a colour,
    /// gradient, or texture. Works with the fill off (stroke-only).
    /// </summary>
    /// <remarks>Parameter: <c>paint[,width][,align][,softness]</c> + projection overrides.</remarks>
    [Serializable]
    [TypeGroup("Appearance", 2)]
    [TypeDescription("Draws a real outline (rim) around the glyph.")]
    [GenerateParameters]
    public sealed partial class StrokeModifier : PaintLayerModifier
    {
        /// <summary>Stroke paint (inline colour, named swatch, or default). A per-range value overrides it.</summary>
        [SerializeField, Parameter(Descriptor = false), Variant("Default|Color=color:#000000FF|Swatch=enum:@paints", Discriminator = nameof(PaintRef.kind)), StateProperty(nameof(MarkMeshDirty))]
        private PaintRef paint;

        /// <summary>Stroke width. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkMeshDirty))]
        private UnitValue width = UnitValue.Em(0.12f);

        /// <summary>Rim alignment relative to the glyph edge: −1 inside … +1 outside. A per-range value overrides it.</summary>
        [SerializeField, Parameter(Parser = nameof(ParseAlign)), Range(-1f, 1f), Tooltip("−1 = inside, 0 = centered, +1 = outside"), StateProperty(nameof(MarkMeshDirty))]
        private float align = 1f;

        /// <summary>Edge softness. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkMeshDirty))]
        private UnitValue softness = UnitValue.Em(0f);

        protected override PaintRef PaintField => paint;

        protected override float CoverageModeValue => CoverageMode.Stroke;

        protected override Color32 DefaultPaintColor => new Color32(0, 0, 0, 255);

        /// <summary>Outside strokes (align &gt; 0) render behind the fill; centered/inside strokes stay in front so the opaque fill doesn't cover them.</summary>
        public override bool RendersBehindFill => align > 0f;

        protected override void ParseExtra(ref ParameterReader reader, in RangeApplyContext context,
            ref LayerGeometry g)
        {
            var resolvedWidth = Param.Width.ResolveNext(ref reader, this, in context);
            g.p0 = resolvedWidth.value * 0.5f;
            g.p0Px = resolvedWidth.unit == UnitKind.Absolute;
            g.p1 = Mathf.Clamp(Param.Align.ResolveNext(ref reader, this, in context), -1f, 1f);
            var resolvedSoftness = Param.Softness.ResolveNext(ref reader, this, in context);
            g.softness = resolvedSoftness.value;
            g.softnessPx = resolvedSoftness.unit == UnitKind.Absolute;
        }

        /// <summary>Accepts the legacy keywords <c>inside</c>/<c>center</c>/<c>outside</c> as −1/0/+1
        /// alongside the continuous numeric value.</summary>
        private static bool ParseAlign(ReadOnlySpan<char> token, out float value)
        {
            if (token.EqualsIgnoreCase("inside"))
            {
                value = -1f;
                return true;
            }
            if (token.EqualsIgnoreCase("center"))
            {
                value = 0f;
                return true;
            }
            if (token.EqualsIgnoreCase("outside"))
            {
                value = 1f;
                return true;
            }
            return ParameterReader.ParseFloat(token, out value);
        }
    }
}
