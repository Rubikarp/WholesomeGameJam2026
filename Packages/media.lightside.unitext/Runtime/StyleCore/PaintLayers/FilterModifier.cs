using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies a <see cref="ColorFilter"/> to everything rendered below it in the style stack — an
    /// adjustment layer for text. Within its ranges it recolours the glyph face, every paint layer
    /// stacked beneath it, and colour emoji; stacked filters compose bottom-up, and a filter at the
    /// end of the style list recolours the whole styled text. The filter transforms each layer's
    /// colour as it composites; non-Normal blend layers keep their blend against the backdrop.
    /// </summary>
    /// <remarks>
    /// Parameter: an optional strength scaling the whole filter for the range — 1 applies it as
    /// authored, 0 disables it (e.g. <c>&lt;sepia=0.5&gt;</c> with a sepia filter bound to the
    /// <c>sepia</c> tag).
    /// </remarks>
    [Serializable]
    [TypeGroup("Appearance", 7)]
    [TypeDescription("Recolours everything below it — greyscale, saturation, tint, and other colour filters.")]
    [GenerateParameters]
    public sealed partial class FilterModifier : BaseModifier, ILayer, IModifierCommitChanges
    {
        UniTextCommitChanges IModifierCommitChanges.CommitChanges
            => UniTextCommitChanges.Appearance;

        public int LayerSequence { get; set; }
        public bool RendersBehindFill => false;
        public bool ClaimsFill => false;

        /// <summary>The colour adjustment applied below this position in the style stack. Null filters nothing; after mutating the assigned instance in place, reassign or mark the text dirty to re-render.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(MarkMeshDirty))]
        private ColorFilter filter = new GrayscaleFilter();

        /// <summary>How far the filter is applied: 1 as authored, 0 not at all, values between fade it out. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Range(0f, 1f), StateProperty(nameof(MarkMeshDirty))]
        [Tooltip("Scales the whole filter toward no change — 1 applies it as authored, 0 disables it.")]
        private float strength = 1f;

        /// <summary>The kind of filter applied, so the modifier reads as its choice rather than as its container.</summary>
        public override string ToString() => filter?.ToString() ?? "Filter";

        private struct FilterSpan
        {
            public int start, end;
            public ColorMatrix matrix;
        }

        private PooledBuffer<FilterSpan> spans;

        internal bool HasSpans => spans.count > 0;

        protected override void OnEnable()
        {
            spans.FakeClear();
            uniText.MeshGenerator.filters.Register(this);
        }

        protected override void OnDisable()
            => uniText.MeshGenerator.filters.Unregister(this);

        protected override void OnDestroy()
            => spans.Return();

        protected override void BeforeApply()
            => spans.FakeClear();

        protected override void OnApply(in RangeApplyContext context)
        {
            if (filter == null) return;
            var resolved = Mathf.Clamp01(Param.Strength.Resolve(this, in context));
            var matrix = ColorMatrix.Lerp(ColorMatrix.Identity, filter.ToMatrix(), resolved);
            if (matrix.IsIdentity) return;

            var start = Math.Max(context.Segment.Range.start, 0);
            var end = Math.Min(context.Segment.Range.End, buffers.codepoints.count);
            if (end <= start) return;
            spans.Add(new FilterSpan { start = start, end = end, matrix = matrix });
        }

        /// <summary>
        /// Folds this filter's spans covering <paramref name="cluster"/> into
        /// <paramref name="matrix"/> when this filter is stamped above
        /// <paramref name="belowSequence"/>; nested spans compose outer-to-inner.
        /// </summary>
        internal void AccumulateFilter(int cluster, int belowSequence, ref ColorMatrix matrix,
            ref bool any)
        {
            if (LayerSequence <= belowSequence) return;
            var count = spans.count;
            if (count == 0) return;

            var data = spans.data;
            for (var i = 0; i < count; i++)
            {
                ref readonly var span = ref data[i];
                if (cluster < span.start || cluster >= span.end) continue;
                matrix = any ? ColorMatrix.Multiply(in span.matrix, in matrix) : span.matrix;
                any = true;
            }
        }
    }
}
