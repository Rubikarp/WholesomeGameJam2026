using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Adds vertical spacing between paragraphs (text separated by line breaks).
    /// </summary>
    /// <remarks>
    /// Parameters (comma-separated): <c>After, Before</c> — both optional.
    /// <c>After</c> opens a gap below each covered paragraph, <c>Before</c> above it; where two
    /// paragraphs meet the values add up (no CSS-style margin collapsing). Nothing is added at the
    /// block edges. Units: <c>10</c> (pixels), <c>50%</c> or <c>0.5em</c> (× font size).
    /// Negative values pull the neighbouring paragraphs together, down to full overlap; the gap
    /// never inverts. When several ranges cover one paragraph, the largest value per side wins.
    /// </remarks>
    [Serializable]
    [TypeGroup("Layout", 3)]
    [TypeDescription("Adds vertical spacing between paragraphs.")]
    [GenerateParameters]
    public partial class ParagraphSpacingModifier : BaseModifier
    {
        /// <summary>Gap opened below each covered paragraph. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|%|em"), StateProperty(nameof(MarkLayoutDirty))] private UnitValue after;
        /// <summary>Gap opened above each covered paragraph. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|%|em"), StateProperty(nameof(MarkLayoutDirty))] private UnitValue before;

        private struct Range
        {
            public int start;
            public int end;
            public bool hasAfter;
            public float after;
            public UnitKind afterUnit;
            public bool hasBefore;
            public float before;
            public UnitKind beforeUnit;
        }

        private PooledList<Range> ranges;

        private OrderedEventHandler<LineHeightContext> lineHeightCallback;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<Range>(4);
            ranges.FakeClear();
            lineHeightCallback ??= OnCalculateLineHeight;
            uniText.TextProcessor.OnCalculateLineHeight.Subscribe(lineHeightCallback);
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.OnCalculateLineHeight.Unsubscribe(lineHeightCallback);
        }

        protected override void OnDestroy()
        {
            ranges?.Return();
            ranges = null;
        }

        protected override void BeforeApply() => ranges?.FakeClear();

        protected override void OnApply(in RangeApplyContext context)
        {
            var range = new Range
            {
                start = context.Segment.Range.start,
                end = context.Segment.Range.End
            };
            var afterValue = Param.After.Resolve(this, in context);
            var beforeValue = Param.Before.Resolve(this, in context);
            range.after = afterValue.value;
            range.afterUnit = afterValue.unit;
            range.before = beforeValue.value;
            range.beforeUnit = beforeValue.unit;
            range.hasAfter = range.after != 0f;
            range.hasBefore = range.before != 0f;

            if (range.hasAfter || range.hasBefore)
                ranges.Add(range);
        }

        private void OnCalculateLineHeight(ref LineHeightContext context)
        {
            if (ranges == null || ranges.Count == 0)
                return;

            var paragraphCount = buffers.paragraphs.count;
            if (paragraphCount < 2)
                return;

            var paragraphs = buffers.paragraphs.data;

            for (var p = 0; p < paragraphCount; p++)
            {
                ref readonly var paragraph = ref paragraphs[p];
                var lastCp = paragraph.CpEnd - 1;
                var startsHere = p > 0 && paragraph.cpStart >= context.startCluster && paragraph.cpStart < context.endCluster;
                var endsHere = p < paragraphCount - 1 && lastCp >= context.startCluster && lastCp < context.endCluster;
                if (!startsHere && !endsHere)
                    continue;

                var hasAfterValue = false;
                var afterValue = 0f;
                var hasBeforeValue = false;
                var beforeValue = 0f;

                for (var i = 0; i < ranges.Count; i++)
                {
                    var range = ranges[i];
                    if (range.end <= paragraph.cpStart || range.start > lastCp)
                        continue;

                    if (endsHere && range.hasAfter)
                    {
                        var value = Resolve(range.after, range.afterUnit, context.fontSize);
                        if (!hasAfterValue || value > afterValue) { afterValue = value; hasAfterValue = true; }
                    }
                    if (startsHere && range.hasBefore)
                    {
                        var value = Resolve(range.before, range.beforeUnit, context.fontSize);
                        if (!hasBeforeValue || value > beforeValue) { beforeValue = value; hasBeforeValue = true; }
                    }
                }

                if (hasAfterValue) uniText.TextProcessor.AddLineGap(context.lineIndex, afterValue);
                if (hasBeforeValue) uniText.TextProcessor.AddLineGap(context.lineIndex - 1, beforeValue);
            }
        }

        private static float Resolve(float value, UnitKind unit, float fontSize) => unit switch
        {
            UnitKind.Percent => value / 100f * fontSize,
            UnitKind.Em => value * fontSize,
            _ => value
        };
    }
}
