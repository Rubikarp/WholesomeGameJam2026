using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>How one logical segment is split into visual background figures.</summary>
    public enum GeometryMapping : byte
    {
        /// <summary>One independently rounded figure per shaped glyph cluster.</summary>
        Glyph,
        /// <summary>One independently rounded figure per final line/BiDi fragment.</summary>
        Line,
        /// <summary>Final line/BiDi fragments with rounding only at the logical range endpoints.</summary>
        Range,
        /// <summary>One connected layout-space multi-line contour without internal seams.</summary>
        Block,
    }

    /// <summary>Projection frame used by a range decoration independently of its geometry topology.</summary>
    public enum RangePaintMapping : byte
    {
        /// <summary>Glyph/Line follow the matching geometry; Range/Block use one range frame.</summary>
        InheritGeometry,
        /// <summary>Restart paint for every shaped glyph cluster.</summary>
        Glyph,
        /// <summary>Restart paint for every layout line.</summary>
        Line,
        /// <summary>Use one paint frame for the logical entity segment.</summary>
        Range,
        /// <summary>Use one paint frame for the entire rendered text block.</summary>
        TextBlock,
    }

    /// <summary>
    /// Paints non-interactive backgrounds for matched text ranges. The same presentation model renders
    /// live text selection without routing selection changes through the parser.
    /// </summary>
    [Serializable]
    [TypeGroup("Appearance", 0)]
    [TypeDescription("Paints a range background with independent geometry and paint mappings.")]
    [GenerateParameters]
    public sealed partial class HighlightModifier : BaseRangeDecorationModifier, IHasPaintProvider
    {

        /// <summary>Visual definition used for every range emitted by this modifier.</summary>
        [SerializeField, ParameterContainer(Invalidate = nameof(InvalidateDecorationOutput)),
         StateProperty(nameof(ApplyPresentationChange)),
         StateLink(nameof(OnPresentationStateChanged))]
        private HighlightPresentation presentation = new();

        [NonSerialized] private HighlightDecorationBuilder builder;
        [NonSerialized] private bool presentationSubscribed;

        IPaintProvider IHasPaintProvider.PaintProvider => presentation.Provider;
        protected override IPaintProvider DecorationPaintProvider => presentation.Provider;
        protected override bool RequiresExactVisualGlyphs => false;

        private void ApplyPresentationChange(HighlightPresentation previous,
            ref HighlightPresentation current)
        {
            if (current == null)
            {
                current = previous;
                throw new ArgumentNullException(nameof(current));
            }
            if (presentationSubscribed && previous != null)
                previous.Changed -= OnPresentationChanged;
            if (presentationSubscribed)
                current.Changed += OnPresentationChanged;
            DecorationPresentationChanged();
        }

        /// <inheritdoc cref="HighlightPresentation.Provider"/>
        public IPaintProvider Provider { get => presentation.Provider; set => presentation.Provider = value; }

        /// <inheritdoc cref="HighlightPresentation.Paint"/>
        public PaintRef Paint { get => presentation.Paint; set => presentation.Paint = value; }

        /// <inheritdoc cref="HighlightPresentation.GeometryMapping"/>
        public GeometryMapping GeometryMapping { get => presentation.GeometryMapping; set => presentation.GeometryMapping = value; }

        /// <inheritdoc cref="HighlightPresentation.PaintMapping"/>
        public RangePaintMapping PaintMapping { get => presentation.PaintMapping; set => presentation.PaintMapping = value; }

        /// <inheritdoc cref="HighlightPresentation.Shape"/>
        public PaintProjectionKind Shape { get => presentation.Shape; set => presentation.Shape = value; }

        /// <inheritdoc cref="HighlightPresentation.Fit"/>
        public PaintFit Fit { get => presentation.Fit; set => presentation.Fit = value; }

        /// <inheritdoc cref="HighlightPresentation.Angle"/>
        public float Angle { get => presentation.Angle; set => presentation.Angle = value; }

        /// <inheritdoc cref="HighlightPresentation.Scale"/>
        public float Scale { get => presentation.Scale; set => presentation.Scale = value; }

        /// <inheritdoc cref="HighlightPresentation.PaintOffset"/>
        public Vector2 PaintOffset { get => presentation.PaintOffset; set => presentation.PaintOffset = value; }

        /// <inheritdoc cref="HighlightPresentation.Height"/>
        public RangeHeight Height { get => presentation.Height; set => presentation.Height = value; }

        /// <inheritdoc cref="HighlightPresentation.Padding"/>
        public UnitVector2 Padding { get => presentation.Padding; set => presentation.Padding = value; }

        /// <inheritdoc cref="HighlightPresentation.CornerRadius"/>
        public UnitValue CornerRadius { get => presentation.CornerRadius; set => presentation.CornerRadius = value; }

        /// <inheritdoc cref="HighlightPresentation.MergeThreshold"/>
        public UnitValue MergeThreshold { get => presentation.MergeThreshold; set => presentation.MergeThreshold = value; }

        /// <inheritdoc cref="HighlightPresentation.Order"/>
        public RangeDecorationOrder Order { get => presentation.Order; set => presentation.Order = value; }

        /// <inheritdoc cref="HighlightPresentation.Tint"/>
        public Color32 Tint { get => presentation.Tint; set => presentation.Tint = value; }

        /// <inheritdoc cref="HighlightPresentation.Blend"/>
        public LayerBlend Blend { get => presentation.Blend; set => presentation.Blend = value; }

        protected override void OnDecorationEnable()
        {
            presentation.Changed += OnPresentationChanged;
            presentationSubscribed = true;
        }

        protected override void OnDecorationDisable()
        {
            if (!presentationSubscribed) return;
            presentation.Changed -= OnPresentationChanged;
            presentationSubscribed = false;
        }

        protected override void OnDecorationDestroy()
        {
            builder?.Dispose();
            builder = null;
        }

        protected override void OnBuildDecoration(in RangeDecorationContext context,
            ref RangeMeshWriter writer)
        {
            var reader = context.Parameters.GetReader();
            var authoredPaint = presentation.Paint;
            var resolvedPaint = ResolveDecorationPaintSource(ref reader, in authoredPaint,
                new Color32(51, 128, 255, 102), out var rampRow);
            var resolved = presentation.Resolve(ref reader, resolvedPaint, uniText.CurrentFontSize,
                this, context.Identity, context.Segment.Id);
            var tint = Param.Tint.ApplyOwned(this, context.Identity, context.Segment.Id,
                resolved.tint);
            tint.a = (byte)Mathf.RoundToInt(tint.a * context.RuleWeight);
            resolved = resolved.WithTint(tint);

            (builder ??= new HighlightDecorationBuilder()).Build(context.Geometry,
                context.Segment.Range.start, context.Segment.Range.End, uniText.CurrentFontSize,
                in resolved, rampRow, ref writer);
        }

        private void OnPresentationStateChanged(IStateChangeSource source,
            in StateChange change)
            => MarkNestedStateChanged(UniTextDirty.Mesh, source, in change);

        private void OnPresentationChanged(IStateChangeSource _,
            in StateChange __) => DecorationPresentationChanged();
    }
}
