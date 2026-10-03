using System;

namespace LightSide
{
    /// <summary>
    /// Keeps the tagged range together as one word — no soft break is taken inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The range takes no soft, CJK, or dictionary break inside it, so it moves whole to the next line when it does
    /// not fit. Wrapping stays allowed immediately before and after it, so it behaves as a single word — and, like
    /// any word, a range wider than the text box still breaks to fit rather than overflowing. Applied to the
    /// character span of a visible markup tag (via a <see cref="ChromeRule"/>), it makes the tag a cohesive word.
    /// </para>
    /// <para>
    /// Pair with a <see cref="TagRule"/> (conventional tag name <c>nobr</c>):
    /// <c>uniText.Styles.Add(Style.Tag(new NoBreakModifier(), "nobr"))</c>.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Layout", 3)]
    [TypeDescription("Keeps the range together as one unbreakable word (white-space: nowrap).")]
    public class NoBreakModifier : RangeCollectingModifier
    {
        protected override void ApplyRange(UniTextBuffers buffers, TextRange range)
        {
            var breaks = buffers.breakOpportunities.data;
            var start = range.start;
            var end = Math.Min(range.End, buffers.breakOpportunities.count - 1);
            if (end <= start) return;

            if (breaks[start] == LineBreakType.None) breaks[start] = LineBreakType.Optional;
            if (breaks[end] == LineBreakType.None) breaks[end] = LineBreakType.Optional;
            for (var i = start + 1; i < end; i++)
                if (breaks[i] == LineBreakType.Optional) breaks[i] = LineBreakType.None;
        }
    }
}
