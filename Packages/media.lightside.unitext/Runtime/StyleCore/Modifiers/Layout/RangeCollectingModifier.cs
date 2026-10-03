using System;

namespace LightSide
{
    /// <summary>
    /// Base for modifiers that collect codepoint ranges during apply and act on them in the Shaped phase — the
    /// channel through which a modifier influences wrapping. Subclasses implement <see cref="ApplyRange"/>.
    /// </summary>
    [Serializable]
    public abstract class RangeCollectingModifier : BaseModifier
    {
        private PooledList<TextRange> ranges;

        /// <summary>Whether a zero-length range (a pure boundary with nothing to keep or collapse) is recorded.</summary>
        protected virtual bool AllowEmptyRange => false;

        private Action shapedCallback;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<TextRange>(8);
            ranges.FakeClear();
            shapedCallback ??= OnShaped;
            uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
        }

        protected override void OnDisable() => uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);

        protected override void OnDestroy()
        {
            ranges?.Return();
            ranges = null;
        }

        protected override void BeforeApply() => ranges?.FakeClear();

        protected override void OnApply(in RangeApplyContext context)
        {
            var start = context.Segment.Range.start;
            var end = context.Segment.Range.End;
            var cpCount = buffers.codepoints.count;
            if (start > cpCount) return;
            end = Math.Min(end, cpCount);
            if (end < start || (!AllowEmptyRange && end == start)) return;
            ranges.Add(new TextRange(start, end - start));
        }

        private void OnShaped()
        {
            for (var i = 0; i < ranges.Count; i++)
                ApplyRange(buffers, ranges[i]);
        }

        /// <summary>Acts on one collected range in the Shaped phase — emit it to a line-breaker buffer, or edit its break opportunities.</summary>
        protected abstract void ApplyRange(UniTextBuffers buffers, TextRange range);
    }
}
