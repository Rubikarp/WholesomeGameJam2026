using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Sets horizontal alignment, justify mode, and last-line alignment for the paragraphs it covers.
    /// Apply whole-text or per range/tag — each paragraph can differ.
    /// </summary>
    [Serializable]
    [TypeGroup("Layout", 1)]
    [TypeDescription("Sets horizontal alignment + justify mode + last-line alignment (per paragraph).")]
    [GenerateParameters]
    public sealed partial class AlignmentModifier : BaseModifier
    {
        /// <summary>Horizontal alignment default. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkPositionsDirty))] private HorizontalAlignment align = HorizontalAlignment.Start;
        /// <summary>Justify mode default. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkPositionsDirty))] private TextJustify justify = TextJustify.Auto;
        /// <summary>Last-line alignment default. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkPositionsDirty))] private LastLineAlignment lastLine = LastLineAlignment.Auto;

        /// <summary>Smallest word-separator width under justification, in percent of natural width.</summary>
        [SerializeField, StateProperty(nameof(MarkPositionsDirty)), Range(50f, 100f)]
        [Tooltip("Justification may shrink word spaces down to this percentage of their natural width.")]
        private float wordSpacingMin = 80f;

        /// <summary>Largest word-separator width justification spends before the letter and glyph levers, in percent of natural width.</summary>
        [SerializeField, StateProperty(nameof(MarkPositionsDirty)), Range(100f, 300f)]
        [Tooltip("Justification stretches word spaces up to this percentage before spending letter " +
                 "spacing and glyph scaling. Space beyond every budget still lands on word spaces.")]
        private float wordSpacingMax = 133f;

        /// <summary>Largest letter-spacing reduction justification may spend, in percent of em.</summary>
        [SerializeField, StateProperty(nameof(MarkPositionsDirty)), Range(-10f, 0f)]
        [Tooltip("Justification may tighten letter spacing by up to this percentage of em. " +
                 "Typographic practice stays within -2%.")]
        private float letterSpacingMin;

        /// <summary>Largest letter-spacing addition justification may spend, in percent of em.</summary>
        [SerializeField, StateProperty(nameof(MarkPositionsDirty)), Range(0f, 10f)]
        [Tooltip("Justification may widen letter spacing by up to this percentage of em. " +
                 "Typographic practice stays within +2%.")]
        private float letterSpacingMax;

        /// <summary>Smallest glyph width under justification, in percent. 100 disables compression.</summary>
        [SerializeField, StateProperty(nameof(MarkPositionsDirty)), Range(90f, 100f)]
        [Tooltip("Justification may compress glyphs down to this percentage of their width. " +
                 "The hz/InDesign norm stays within 98-100.")]
        private float glyphScalingMin = 100f;

        /// <summary>Largest glyph width under justification, in percent. 100 disables expansion.</summary>
        [SerializeField, StateProperty(nameof(MarkPositionsDirty)), Range(100f, 110f)]
        [Tooltip("Justification may widen glyphs up to this percentage of their width. " +
                 "The hz/InDesign norm stays within 100-102.")]
        private float glyphScalingMax = 100f;

        private struct Range
        {
            public int start;
            public int end;
            public HorizontalAlignment align;
            public TextJustify justify;
            public LastLineAlignment lastLine;
        }

        private PooledList<Range> ranges;

        private OrderedEventHandler<LineStyleContext> resolveLineStyleCallback;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<Range>(4);
            ranges.FakeClear();
            resolveLineStyleCallback ??= OnResolveLineStyle;
            uniText.TextProcessor.OnResolveLineStyle.Subscribe(resolveLineStyleCallback);
        }

        protected override void OnDisable()
            => uniText.TextProcessor.OnResolveLineStyle.Unsubscribe(resolveLineStyleCallback);

        protected override void OnDestroy()
        {
            ranges?.Return();
            ranges = null;
        }

        protected override void BeforeApply() => ranges?.FakeClear();

        protected override void OnApply(in RangeApplyContext context)
        {
            var a = Param.Align.Resolve(this, in context);
            var j = Param.Justify.Resolve(this, in context);
            var l = Param.LastLine.Resolve(this, in context);
            ranges.Add(new Range
            {
                start = context.Segment.Range.start,
                end = context.Segment.Range.End,
                align = a,
                justify = j,
                lastLine = l
            });
        }

        private void OnResolveLineStyle(ref LineStyleContext context)
        {
            for (var i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                if (r.end <= context.startCluster || r.start >= context.endCluster) continue;
                context.alignment = r.align;
                context.justify = r.justify;
                context.lastLine = r.lastLine;
                context.wordSpaceMin = wordSpacingMin * 0.01f;
                context.wordSpaceMax = wordSpacingMax * 0.01f;
                context.letterSpaceMin = letterSpacingMin * 0.01f;
                context.letterSpaceMax = letterSpacingMax * 0.01f;
                context.glyphScaleMin = glyphScalingMin * 0.01f;
                context.glyphScaleMax = glyphScalingMax * 0.01f;
            }
        }
    }
}
