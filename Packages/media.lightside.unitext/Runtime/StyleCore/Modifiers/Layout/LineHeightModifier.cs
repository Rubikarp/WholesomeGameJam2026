using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Sets each line's height, per paragraph.
    /// </summary>
    /// <remarks>
    /// Parameters (comma-separated): <c>Value, Mode, Scale, Leading</c> — all optional.
    /// <list type="bullet">
    /// <item><c>Mode</c>: <c>content</c> (grow to the tallest font on the line — default),
    /// <c>primary</c> (primary font only, so fallback glyphs never enlarge the line), or
    /// <c>scaled</c> (exactly <c>Scale × fontSize</c>).</item>
    /// <item><c>Value</c>: optional nudge on top of the mode — <c>150%</c> or <c>1.5em</c> (multiplier),
    /// <c>40</c> (absolute pixels), <c>+10</c> / <c>-5</c> (delta pixels). A bare number is pixels, not a
    /// multiplier; <c>0</c> or empty means no nudge.</item>
    /// <item><c>Scale</c>: the multiplier for <c>scaled</c> mode; ignored otherwise.</item>
    /// <item><c>Leading</c>: how extra spacing is split — <c>halfLeading</c> (CSS), <c>leadingAbove</c>
    /// (Figma), <c>leadingBelow</c> (Android).</item>
    /// </list>
    /// To set a later parameter without the earlier ones, leave them empty: <c>&lt;lh=,primary&gt;</c>,
    /// <c>&lt;lh=,scaled,2&gt;</c>. When several ranges set an absolute height on a line, the largest wins.
    /// </remarks>
    [Serializable]
    [TypeGroup("Layout", 3)]
    [TypeDescription("Adjusts the vertical spacing between lines.")]
    [GenerateParameters]
    public partial class LineHeightModifier : BaseModifier
    {
        /// <summary>Optional height nudge on top of the mode (multiplier, absolute px, or delta). A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|%|delta"), StateProperty(nameof(MarkLayoutDirty))] private UnitValue heightValue;
        /// <summary>How the base line height is derived. A per-range value overrides it.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkLayoutDirty))] private LineHeightMode mode = LineHeightMode.Content;
        /// <summary>Multiplier used in <see cref="LineHeightMode.Scaled"/> mode.</summary>
        [SerializeField, Parameter, VisibleWhen("mode", "Scaled"), StateProperty(nameof(MarkLayoutDirty))] private float scale = 1.2f;
        /// <summary>How extra line spacing is split above/below the text.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkLayoutDirty))] private LeadingDistribution leading = LeadingDistribution.HalfLeading;

        private enum UnitMode : byte { Multiplier, Absolute, Delta }

        private struct Range
        {
            public int start;
            public int end;
            public bool hasValue;
            public float value;
            public UnitMode unitMode;
            public LineHeightMode mode;
            public float scale;
        }

        private PooledList<Range> ranges;

        private LeadingDistribution blockLeading = LeadingDistribution.HalfLeading;

        private OrderedEventHandler<LineHeightContext> lineHeightCallback;
        private OrderedEventHandler<LineHeightModeContext> resolveLineHeightCallback;
        private OrderedEventHandler<TextProcessSettings> configureCallback;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<Range>(4);
            ranges.FakeClear();
            blockLeading = LeadingDistribution.HalfLeading;
            lineHeightCallback ??= OnCalculateLineHeight;
            uniText.TextProcessor.OnCalculateLineHeight.Subscribe(lineHeightCallback);
            resolveLineHeightCallback ??= OnResolveLineHeight;
            uniText.TextProcessor.OnResolveLineHeight.Subscribe(resolveLineHeightCallback);
            configureCallback ??= OnConfigure;
            uniText.TextProcessor.ConfigureSettings.Subscribe(configureCallback);
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.OnCalculateLineHeight.Unsubscribe(lineHeightCallback);
            uniText.TextProcessor.OnResolveLineHeight.Unsubscribe(resolveLineHeightCallback);
            uniText.TextProcessor.ConfigureSettings.Unsubscribe(configureCallback);
        }

        protected override void OnDestroy()
        {
            ranges?.Return();
            ranges = null;
        }

        protected override void BeforeApply()
        {
            ranges?.FakeClear();
            blockLeading = LeadingDistribution.HalfLeading;
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var reader = context.Parameters.GetReader();

            var afterLegacyMode = reader;
            if (afterLegacyMode.Next(out var first) &&
                (first.EqualsIgnoreCase("h") || first.EqualsIgnoreCase("s")))
            {
                reader = afterLegacyMode;
            }

            var range = new Range
            {
                start = context.Segment.Range.start,
                end = context.Segment.Range.End,
                mode = mode,
                scale = scale
            };

            var height = Param.HeightValue.ResolveNext(ref reader, this, in context);
            var nudge = height.value;
            var unit = height.unit;
            range.hasValue = true;
            range.value = nudge;
            switch (unit)
            {
                case UnitKind.Delta:
                    range.unitMode = UnitMode.Delta;
                    break;
                case UnitKind.Absolute:
                    range.unitMode = UnitMode.Absolute;
                    break;
                default:
                    range.unitMode = UnitMode.Multiplier;
                    if (unit == UnitKind.Percent)
                        range.value /= 100f;
                    break;
            }

            if (range.unitMode != UnitMode.Delta && range.value <= 0f)
                range.hasValue = false;

            range.mode = Param.Mode.ResolveNext(ref reader, this, in context);
            range.scale = Param.Scale.ResolveNext(ref reader, this, in context);
            blockLeading = Param.Leading.ResolveNext(ref reader, this, in context);

            ranges.Add(range);
        }

        private void OnResolveLineHeight(ref LineHeightModeContext context)
        {
            for (var i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                if (r.end <= context.startCluster || r.start >= context.endCluster) continue;
                context.mode = r.mode;
                context.scale = r.scale;
            }
        }

        private void OnConfigure(ref TextProcessSettings settings)
        {
            settings.layout.leadingDistribution = blockLeading;
        }

        private void OnCalculateLineHeight(ref LineHeightContext context)
        {
            if (ranges == null || ranges.Count == 0)
                return;

            var defaultAdvance = context.lineAdvance;

            var hasAbsolute = false;
            var absoluteValue = 0f;
            for (var i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                if (range.end <= context.startCluster || range.start >= context.endCluster)
                    continue;
                if (!range.hasValue || range.unitMode != UnitMode.Absolute)
                    continue;
                if (!hasAbsolute || range.value > absoluteValue)
                {
                    absoluteValue = range.value;
                    hasAbsolute = true;
                }
            }

            var baseAdvance = hasAbsolute ? absoluteValue : defaultAdvance;

            var hasResult = hasAbsolute;
            var result = absoluteValue;

            for (var i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                if (range.end <= context.startCluster || range.start >= context.endCluster)
                    continue;
                if (!range.hasValue)
                    continue;

                float candidate;
                switch (range.unitMode)
                {
                    case UnitMode.Absolute:
                        continue;
                    case UnitMode.Delta:
                        candidate = baseAdvance + range.value;
                        break;
                    default:
                        candidate = baseAdvance * range.value;
                        break;
                }

                if (!hasResult || candidate > result)
                {
                    result = candidate;
                    hasResult = true;
                }
            }

            if (hasResult)
                context.lineAdvance = result;
        }
    }

}
