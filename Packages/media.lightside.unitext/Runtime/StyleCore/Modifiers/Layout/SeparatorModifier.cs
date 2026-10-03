using System;

namespace LightSide
{
    /// <summary>
    /// Keeps each run of text between tagged separators whole during word wrap: a segment either
    /// fits entirely on the current line or moves to a fresh line and the separator before it
    /// collapses. A segment wider than the text box wraps inside itself as usual, and the lines
    /// holding its fragments accept no further segments.
    /// </summary>
    /// <remarks>
    /// Pair with a <see cref="SeparatorParseRule"/>, which inserts the separator string and marks
    /// its range:
    /// <c>uniText.Styles.Add(new Style { Modifier = new SeparatorModifier(), Source = new SeparatorParseRule() })</c>.
    /// </remarks>
    [Serializable]
    [TypeGroup("Layout", 3)]
    [TypeDescription("Fits whole segments between separators on a line; a separator collapses when its segment wraps.")]
    public class SeparatorModifier : RangeCollectingModifier
    {
        protected override bool AllowEmptyRange => true;

        protected override void ApplyRange(UniTextBuffers buffers, TextRange range) => buffers.segmentBreaks.Add(range);
    }
}
