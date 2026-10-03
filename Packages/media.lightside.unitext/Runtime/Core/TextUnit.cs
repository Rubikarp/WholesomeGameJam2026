using System;

namespace LightSide
{
    /// <summary>
    /// Granularity a stretch of text is counted and stepped in.
    /// </summary>
    /// <remarks>
    /// Every unit but <see cref="Line"/> is a property of the text; <see cref="Line"/> is a property
    /// of the current wrap and therefore changes with the box, the font size and every other layout
    /// input.
    /// </remarks>
    public enum TextUnit : byte
    {
        /// <summary>One grapheme cluster — a user-perceived character.</summary>
        Cluster,

        /// <summary>One word as UAX #29 and the configured dictionaries segment it, carrying the spaces and punctuation that trail it.</summary>
        Word,

        /// <summary>One line of the current wrap, so the step follows the box the text is laid out in.</summary>
        Line,

        /// <summary>One paragraph — the text between hard breaks, whatever the wrap does to it.</summary>
        Paragraph,
    }

    /// <summary>A set of <see cref="TextUnit"/> granularities.</summary>
    [Flags]
    public enum TextUnits : byte
    {
        /// <summary>No granularity.</summary>
        None = 0,

        /// <inheritdoc cref="TextUnit.Cluster"/>
        Cluster = 1 << (int)TextUnit.Cluster,

        /// <inheritdoc cref="TextUnit.Word"/>
        Word = 1 << (int)TextUnit.Word,

        /// <inheritdoc cref="TextUnit.Line"/>
        Line = 1 << (int)TextUnit.Line,

        /// <inheritdoc cref="TextUnit.Paragraph"/>
        Paragraph = 1 << (int)TextUnit.Paragraph,

        /// <summary>Every granularity.</summary>
        All = Cluster | Word | Line | Paragraph,

        /// <summary>The granularities holding more than one cluster — those a per-glyph effect cannot express.</summary>
        Grouping = Word | Line | Paragraph,
    }

    /// <summary>Bridges a single <see cref="TextUnit"/> and the <see cref="TextUnits"/> sets it belongs to.</summary>
    public static class TextUnitExtensions
    {
        /// <summary>The one-granularity set holding <paramref name="unit"/>.</summary>
        public static TextUnits Flag(this TextUnit unit) => (TextUnits)(1 << (int)unit);

        /// <summary>Whether <paramref name="units"/> holds <paramref name="unit"/>.</summary>
        public static bool Has(this TextUnits units, TextUnit unit) => (units & unit.Flag()) != 0;
    }

    /// <summary>
    /// The one rule that decides where units begin, over one codepoint span. Driven in step with a
    /// cluster walk: the caller reports every cluster lead exactly once — through <see cref="Starts"/>
    /// when it may open a unit, or <see cref="Skip"/> when the caller's own policy excludes it — so a
    /// single numbering serves every consumer without a second pass over the text.
    /// </summary>
    internal ref struct TextUnitWalk
    {
        private readonly TextUnit unit;
        private readonly ReadOnlySpan<int> codepoints;
        private readonly ReadOnlySpan<bool> wordBoundaries;
        private readonly ReadOnlySpan<TextLine> lines;
        private readonly int origin;
        private int lineCursor;
        private bool afterHardBreak;
        private bool started;

        /// <summary>
        /// Walks <paramref name="codepoints"/>, whose first element sits at <paramref name="origin"/>
        /// in the document. <paramref name="wordBoundaries"/> is the same span's slice of the parse's
        /// boundaries and <paramref name="lines"/> the document's lines; either being absent falls the
        /// walk back to <see cref="TextUnit.Cluster"/>.
        /// </summary>
        public TextUnitWalk(TextUnit unit, ReadOnlySpan<int> codepoints,
            ReadOnlySpan<bool> wordBoundaries, ReadOnlySpan<TextLine> lines, int origin)
        {
            this.unit = unit switch
            {
                TextUnit.Word when wordBoundaries.Length < codepoints.Length => TextUnit.Cluster,
                TextUnit.Line when lines.Length == 0 => TextUnit.Cluster,
                _ => unit,
            };
            this.codepoints = codepoints;
            this.wordBoundaries = wordBoundaries;
            this.lines = lines;
            this.origin = origin;
            lineCursor = 0;
            afterHardBreak = false;
            started = false;
        }

        /// <summary>Reports a cluster the caller's policy keeps off the numbering; it still positions the walk.</summary>
        public void Skip(int index) => afterHardBreak = UnicodeData.IsMandatoryBreakChar(codepoints[index]);

        /// <summary>Whether the cluster at <paramref name="index"/> opens a unit of its own.</summary>
        public bool Starts(int index)
        {
            var opens = Opens(index);
            afterHardBreak = UnicodeData.IsMandatoryBreakChar(codepoints[index]);
            started = true;
            return opens;
        }

        private bool Opens(int index)
        {
            if (!started) return true;

            switch (unit)
            {
                case TextUnit.Word:
                    return wordBoundaries[index] &&
                           WordSegmentationProcessor.IsWordCharacter(codepoints[index]);

                case TextUnit.Line:
                    return AtLineStart(origin + index);

                case TextUnit.Paragraph:
                    return afterHardBreak;

                default:
                    return true;
            }
        }

        private bool AtLineStart(int codepoint)
        {
            while (lineCursor < lines.Length && lines[lineCursor].range.start < codepoint)
                lineCursor++;

            return lineCursor < lines.Length && lines[lineCursor].range.start == codepoint;
        }
    }
}
