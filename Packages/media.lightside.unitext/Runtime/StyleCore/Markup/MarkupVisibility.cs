namespace LightSide
{
    /// <summary>
    /// How an editable presents the markup syntax characters of its source.
    /// </summary>
    public enum MarkupVisibility
    {
        /// <summary>Tags are hidden and atomic — the caret steps over them and only the styled result shows. The default.</summary>
        Hidden,

        /// <summary>Tags are hidden except those of the range the caret is inside, which reveal as editable source.</summary>
        RevealActiveRange,

        /// <summary>Every tag shows as literal source text and the caret moves through it — the raw markup view.</summary>
        Raw
    }

    internal static class MarkupReveal
    {
        public static bool Includes(int windowStart, int windowEnd, int extentStart, int extentEnd)
            => windowEnd >= extentStart && windowStart <= extentEnd;
    }
}
