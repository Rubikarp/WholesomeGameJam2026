using System;
using UnityEngine;
using TextPaintMapping = LightSide.PaintMapping;
using Param = LightSide.HighlightModifier.Param;

namespace LightSide
{
    internal readonly struct ResolvedHighlightPresentation
    {
        public readonly TextPaint paint;
        public readonly GeometryMapping geometry;
        public readonly RangeHeight height;
        public readonly float paddingX;
        public readonly float paddingY;
        public readonly float radius;
        public readonly float merge;
        public readonly RangeDecorationOrder order;
        public readonly Color32 tint;

        public ResolvedHighlightPresentation(in TextPaint paint, GeometryMapping geometry,
            RangeHeight height, float paddingX, float paddingY, float radius, float merge,
            RangeDecorationOrder order, Color32 tint)
        {
            this.paint = paint;
            this.geometry = geometry;
            this.height = height;
            this.paddingX = paddingX;
            this.paddingY = paddingY;
            this.radius = radius;
            this.merge = merge;
            this.order = order;
            this.tint = tint;
        }

        public ResolvedHighlightPresentation WithTint(Color32 value) => new(in paint, geometry,
            height, paddingX, paddingY, radius, merge, order, value);
    }

    /// <summary>
    /// Complete visual definition shared by authored highlights and the live text selection.
    /// </summary>
    [Serializable]
    [StateSource]
    public sealed partial class HighlightPresentation : IHasPaintProvider
    {
        /// <summary>Source of named solid, gradient and texture paints.</summary>
        [SerializeReference, TypeSelector]
        [Tooltip("Source of named paint swatches for the highlight paint.")]
        [StateProperty, StateLink]
        private IPaintProvider provider = new GlobalSettingsPaintProvider();

        /// <summary>Authored highlight paint.</summary>
        [SerializeField, Parameter(Descriptor = false),
         Variant("Default|Color=color:#3380FF66|Swatch=enum:@paints", Discriminator = nameof(PaintRef.kind)),
         StateProperty]
        private PaintRef paint = PaintRef.Solid(new Color32(51, 128, 255, 102));

        /// <summary>How the logical range is split into visual figures.</summary>
        [SerializeField, Parameter, StateProperty]
        private GeometryMapping geometryMapping = GeometryMapping.Range;

        /// <summary>How gradient or texture coordinates restart independently of geometry topology.</summary>
        [SerializeField, Parameter, StateProperty]
        private RangePaintMapping paintMapping = RangePaintMapping.InheritGeometry;

        /// <summary>Gradient geometry; Inherit uses the selected swatch.</summary>
        [SerializeField, Parameter, Inheritable, StateProperty]
        private PaintProjectionKind shape = PaintProjectionKind.Inherit;

        /// <summary>Texture fitting; Inherit uses the selected swatch.</summary>
        [SerializeField, Parameter, Inheritable, StateProperty]
        private PaintFit fit = PaintFit.Inherit;

        /// <summary>Paint projection rotation in degrees; NaN uses the selected swatch.</summary>
        [SerializeField, Parameter, Inheritable(0f), Range(0f, 360f), StateProperty]
        private float angle = float.NaN;

        /// <summary>Paint projection zoom; NaN uses the selected swatch.</summary>
        [SerializeField, Parameter, Inheritable(1f), StateProperty]
        private float scale = float.NaN;

        /// <summary>Paint projection offset in normalized frame units.</summary>
        [SerializeField, Parameter, Inheritable(0f, 0f), StateProperty]
        private Vector2 paintOffset = new(float.NaN, float.NaN);

        /// <summary>Vertical extent used for visual fragments.</summary>
        [SerializeField, Parameter, StateProperty]
        private RangeHeight height = RangeHeight.LineBox;

        /// <summary>Additional expansion around each figure; rounded corners contribute a geometric safe inset separately.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty]
        private UnitVector2 padding = new(Vector2.zero, UnitKind.Em);

        /// <summary>Corner radius of the range surface.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty]
        private UnitValue cornerRadius = UnitValue.Em(0f);

        /// <summary>Edge differences below this value snap to a shared edge across adjacent lines; a negative value uses the radius.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty]
        private UnitValue mergeThreshold = UnitValue.Em(0f);

        /// <summary>Whether the surface renders behind or above the text.</summary>
        [SerializeField, Parameter, StateProperty]
        private RangeDecorationOrder order = RangeDecorationOrder.Behind;

        /// <summary>Colour multiplied with the resolved solid, gradient, or texture paint.</summary>
        [SerializeField, Parameter, StateProperty]
        private Color32 tint = new(255, 255, 255, 255);

        /// <summary>How the highlight composites; Inherit uses the selected paint.</summary>
        [SerializeField, Parameter, Inheritable, StateProperty]
        private LayerBlend blend = LayerBlend.Inherit;

        /// <summary>
        /// Gradient continuation outside the projection frame — visible where <see cref="Scale"/> or
        /// <see cref="PaintOffset"/> leaves part of the surface uncovered; Inherit uses the selected
        /// swatch. Occupies the final parameter slot: any parameter added later must follow it.
        /// </summary>
        [SerializeField, Parameter, Inheritable, StateProperty]
        private PaintSpread spread = PaintSpread.Inherit;

        IPaintProvider IHasPaintProvider.PaintProvider => provider;

        internal static HighlightPresentation SelectionDefault() => new()
        {
            MergeThreshold = UnitValue.Em(-1f),
        };

        internal ResolvedHighlightPresentation Resolve(ref ParameterReader reader,
            TextPaint resolvedPaint, float em, HighlightModifier owner = null,
            RangeIdentity identity = default, RangeSegmentId segment = default)
        {
            reader.NextEnum(out GeometryMapping resolvedGeometry, geometryMapping);
            reader.NextEnum(out RangePaintMapping projection, paintMapping);
            reader.NextEnum(out PaintProjectionKind resolvedShape, shape);
            reader.NextEnum(out PaintFit resolvedFit, fit);
            reader.NextFloat(out var resolvedAngle, angle);
            reader.NextFloat(out var resolvedScale, scale);
            reader.NextVector2(out var resolvedOffset, paintOffset);
            reader.NextEnum(out RangeHeight resolvedHeight, height);
            reader.NextUnitVector2(out var paddingValue, out var paddingUnit, padding);
            var resolvedPadding = new UnitVector2(paddingValue, paddingUnit);
            reader.NextUnitFloat(out var radiusValue, out var radiusUnit, cornerRadius);
            var resolvedRadius = new UnitValue(radiusValue, radiusUnit);
            reader.NextUnitFloat(out var mergeValue, out var mergeUnit, mergeThreshold);
            var resolvedMerge = new UnitValue(mergeValue, mergeUnit);
            reader.NextEnum(out RangeDecorationOrder resolvedOrder, order);
            reader.NextColor(out var resolvedTint, tint);
            reader.NextEnum(out LayerBlend resolvedBlend, blend);
            reader.NextEnum(out PaintSpread resolvedSpread, spread);

            if (owner != null)
            {
                resolvedGeometry = Param.GeometryMapping.ApplyOwned(owner, identity, segment, resolvedGeometry);
                projection = Param.PaintMapping.ApplyOwned(owner, identity, segment, projection);
                resolvedShape = Param.Shape.ApplyOwned(owner, identity, segment, resolvedShape);
                resolvedFit = Param.Fit.ApplyOwned(owner, identity, segment, resolvedFit);
                resolvedAngle = Param.Angle.ApplyOwned(owner, identity, segment, resolvedAngle);
                resolvedScale = Param.Scale.ApplyOwned(owner, identity, segment, resolvedScale);
                resolvedOffset = Param.PaintOffset.ApplyOwned(owner, identity, segment, resolvedOffset);
                resolvedHeight = Param.Height.ApplyOwned(owner, identity, segment, resolvedHeight);
                resolvedPadding = Param.Padding.ApplyOwned(owner, identity, segment, resolvedPadding);
                resolvedRadius = Param.CornerRadius.ApplyOwned(owner, identity, segment, resolvedRadius);
                resolvedMerge = Param.MergeThreshold.ApplyOwned(owner, identity, segment, resolvedMerge);
                resolvedOrder = Param.Order.ApplyOwned(owner, identity, segment, resolvedOrder);
                resolvedBlend = Param.Blend.ApplyOwned(owner, identity, segment, resolvedBlend);
                resolvedSpread = Param.Spread.ApplyOwned(owner, identity, segment, resolvedSpread);
            }

            resolvedPaint.mapping = ToPaintMapping(resolvedGeometry, projection);
            resolvedPaint.ApplyProjectionDetails(resolvedShape, resolvedFit, resolvedAngle,
                resolvedScale, resolvedOffset);
            if (resolvedBlend != LayerBlend.Inherit) resolvedPaint.blend = resolvedBlend;
            if (resolvedSpread != PaintSpread.Inherit) resolvedPaint.spread = resolvedSpread;

            var radius = Mathf.Max(0f, resolvedRadius.ResolvePx(em));
            var merge = resolvedMerge.ResolvePx(em);
            if (merge < 0f) merge = radius;
            return new ResolvedHighlightPresentation(in resolvedPaint, resolvedGeometry, resolvedHeight,
                UnitValue.ResolvePx(resolvedPadding.value.x, resolvedPadding.unit, em),
                UnitValue.ResolvePx(resolvedPadding.value.y, resolvedPadding.unit, em), radius, merge,
                resolvedOrder, resolvedTint);
        }

        private static TextPaintMapping ToPaintMapping(GeometryMapping geometry, RangePaintMapping mapping)
        {
            if (mapping == RangePaintMapping.Glyph) return TextPaintMapping.Glyph;
            if (mapping == RangePaintMapping.Line) return TextPaintMapping.Line;
            if (mapping == RangePaintMapping.Range) return TextPaintMapping.Range;
            if (mapping == RangePaintMapping.TextBlock) return TextPaintMapping.Block;
            return geometry switch
            {
                GeometryMapping.Glyph => TextPaintMapping.Glyph,
                GeometryMapping.Line => TextPaintMapping.Line,
                _ => TextPaintMapping.Range,
            };
        }
    }
}
