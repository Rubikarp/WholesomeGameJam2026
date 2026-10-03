using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Specifies the base text direction for bidirectional text processing.
    /// </summary>
    /// <seealso cref="BidiEngine"/>
    public enum TextDirection : byte
    {
        /// <summary>Left-to-right direction (e.g., Latin, Cyrillic).</summary>
        LeftToRight = 0,

        /// <summary>Right-to-left direction (e.g., Arabic, Hebrew).</summary>
        RightToLeft = 1,

        /// <summary>Automatically detect direction from text content using UAX #9.</summary>
        Auto = 2
    }


    /// <summary>
    /// Specifies the type of line break opportunity according to UAX #14.
    /// </summary>
    /// <seealso cref="LineBreakAlgorithm"/>
    public enum LineBreakType : byte
    {
        /// <summary>No break allowed at this position.</summary>
        None = 0,

        /// <summary>Break is allowed but not required (soft break).</summary>
        Optional = 1,

        /// <summary>Break is required at this position (hard break after CR, LF, etc.).</summary>
        Mandatory = 2
    }


    /// <summary>
    /// Represents a shaped glyph with positioning information from the shaping engine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Produced by <see cref="Shaper"/> after OpenType shaping. Contains the glyph ID,
    /// cluster mapping back to codepoints, and advance/offset values.
    /// </para>
    /// </remarks>
    public struct ShapedGlyph
    {
        /// <summary><see cref="glyphId"/> of a slot whose visual a modifier supplies itself; the font holds no glyph behind it, so no font or atlas query may be made with it.</summary>
        public const int NoGlyph = -1;

        /// <summary>The font-specific glyph identifier, or <see cref="NoGlyph"/>.</summary>
        public int glyphId;

        /// <summary>Absolute index into the codepoints array for the cluster that produced this glyph.</summary>
        public int cluster;

        /// <summary>Horizontal advance to the next glyph position.</summary>
        public float advanceX;

        /// <summary>Vertical advance to the next glyph position.</summary>
        public float advanceY;

        /// <summary>Horizontal offset from the pen position for rendering.</summary>
        public float offsetX;

        /// <summary>Vertical offset from the pen position for rendering.</summary>
        public float offsetY;
    }

    /// <summary>
    /// One glyph exactly as HarfBuzz emits it: glyph id, cluster (relative to the shaped item's
    /// start), and advances/offsets in integer FONT DESIGN UNITS (pre-scale, pre-override). This is
    /// the size-independent unit the word-shape cache stores; the readback loop re-applies scale,
    /// tracking, per-font overrides and the cluster offset to produce a <see cref="ShapedGlyph"/>.
    /// </summary>
    internal struct RawShapedGlyph
    {
        public int glyphId;
        public int cluster;
        public int xAdvance;
        public int yAdvance;
        public int xOffset;
        public int yOffset;

        /// <summary>HarfBuzz glyph flags (low bit = <see cref="HB.GLYPH_FLAG_UNSAFE_TO_BREAK"/>); gates whether a word may be split out and cached.</summary>
        public int flags;
    }


    /// <summary>
    /// Represents a range of indices in a text buffer.
    /// </summary>
    public readonly struct TextRange : IEquatable<TextRange>
    {
        /// <summary>The starting index of the range.</summary>
        public readonly int start;

        /// <summary>The number of elements in the range.</summary>
        public readonly int length;

        /// <summary>Gets the exclusive end index of the range.</summary>
        public int End => start + length;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextRange"/> struct.
        /// </summary>
        /// <param name="start">The starting index.</param>
        /// <param name="length">The number of elements.</param>
        public TextRange(int start, int length)
        {
            this.start = start;
            this.length = length;
        }

        /// <summary>
        /// Determines whether the specified index is within this range.
        /// </summary>
        /// <param name="index">The index to check.</param>
        /// <returns><see langword="true"/> if the index is within the range; otherwise, <see langword="false"/>.</returns>
        public bool Contains(int index)
        {
            return index >= start && index < End;
        }

        /// <summary>
        /// Determines whether this range overlaps with another range.
        /// </summary>
        /// <param name="other">The other range to check.</param>
        /// <returns><see langword="true"/> if the ranges overlap; otherwise, <see langword="false"/>.</returns>
        public bool Overlaps(TextRange other)
        {
            return start < other.End && End > other.start;
        }

        /// <inheritdoc/>
        public bool Equals(TextRange other)
        {
            return start == other.start && length == other.length;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is TextRange other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(start, length);
        }

        /// <summary>Determines whether two ranges are equal.</summary>
        public static bool operator ==(TextRange left, TextRange right)
        {
            return left.Equals(right);
        }

        /// <summary>Determines whether two ranges are not equal.</summary>
        public static bool operator !=(TextRange left, TextRange right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// A source-text range and the text it collapses to in the rendered projection. <see cref="visible"/> is
    /// <see langword="null"/> for stripped tag characters and the inserted glyph (one ORC for an inline object,
    /// the escaped character for an escape) for a self-closing insertion. The synthesized markup view's
    /// map is built from these, so a tag that renders a glyph counts as that glyph instead of
    /// vanishing — without it the caret desyncs and deletion splits the tag — and the plain-text projection
    /// carries the rendered character rather than the source markup.
    /// </summary>
    internal readonly struct ProjectedRange
    {
        public readonly int start;
        public readonly int length;
        public readonly string visible;

        /// <summary>
        /// Rendered width of <see cref="visible"/> in CODEPOINTS — the unit the editing
        /// markup-view map runs in. An astral glyph (a parse rule inserting "😊") counts as one;
        /// <c>visible.Length</c> would count its two UTF-16 chars and desync every visible
        /// offset after the range.
        /// </summary>
        public readonly int visibleWidth;

        public int End => start + length;
        public int VisibleWidth => visibleWidth;

        public ProjectedRange(int start, int length, string visible)
        {
            this.start = start;
            this.length = length;
            this.visible = visible;
            visibleWidth = UnicodeData.CountCodepoints(visible);
        }
    }


    /// <summary>
    /// Represents a segment of text with uniform script, direction, and font before shaping.
    /// </summary>
    /// <remarks>
    /// Created during itemization to split text into homogeneous segments
    /// that can be shaped independently.
    /// </remarks>
    public struct TextRun
    {
        /// <summary>The codepoint range covered by this run.</summary>
        public TextRange range;

        /// <summary>The BiDi embedding level (odd = RTL, even = LTR).</summary>
        public byte bidiLevel;

        /// <summary>Language registry index for the OpenType language tag applied during shaping. 0 = unset.</summary>
        public byte language;

        /// <summary>The Unicode script of this run.</summary>
        public UnicodeScript script;

        /// <summary>The font ID to use for shaping this run.</summary>
        public int fontId;

        /// <summary>Gets the text direction derived from the BiDi level.</summary>
        public TextDirection Direction => (bidiLevel & 1) == 0
            ? TextDirection.LeftToRight
            : TextDirection.RightToLeft;
    }


    /// <summary>
    /// Represents a text run after shaping, with glyph information and metrics.
    /// </summary>
    public struct ShapedRun
    {
        /// <summary>The codepoint range covered by this run.</summary>
        public TextRange range;

        /// <summary>Index of the first glyph in the shaped glyphs buffer.</summary>
        public int glyphStart;

        /// <summary>Number of glyphs in this run.</summary>
        public int glyphCount;

        /// <summary>Total width of all glyphs in this run.</summary>
        public float width;

        /// <summary>The text direction of this run.</summary>
        public TextDirection direction;

        /// <summary>The BiDi embedding level.</summary>
        public byte bidiLevel;

        /// <summary>Language registry index for the OpenType language tag applied during shaping. 0 = unset.</summary>
        public byte language;

        /// <summary>The font ID used for this run.</summary>
        public int fontId;
    }


    /// <summary>
    /// Stores variable font axis values for a variation-overridden shaping run.
    /// Maps a variation fontId to its base font and resolved axis values.
    /// </summary>
    internal struct VariationRunInfo
    {
        /// <summary>FontDataHash of the base (non-varied) font.</summary>
        public int baseFontHash;

        /// <summary>Pre-computed varHash48 for atlas glyph keys.</summary>
        public long varHash48;

        /// <summary>HarfBuzz variation structs for hb_font_set_variations.</summary>
        public HB.hb_variation_t[] hbVariations;

        /// <summary>Ordered axis values for FT_Set_Var_Design_Coordinates (FreeType uses 16.16 fixed-point).</summary>
        public int[] ftCoords;
    }


    /// <summary>
    /// Represents a line of text after line breaking.
    /// </summary>
    public struct TextLine
    {
        /// <summary>The codepoint range covered by this line.</summary>
        public TextRange range;

        /// <summary>Index of the first run in the ordered runs buffer for this line.</summary>
        public int runStart;

        /// <summary>Number of runs in this line.</summary>
        public int runCount;

        /// <summary>Index of the first positioned glyph belonging to this line (set during layout).</summary>
        public int glyphStart;

        /// <summary>Number of positioned glyphs belonging to this line (set during layout).</summary>
        public int glyphCount;

        /// <summary>Content width in font units (excluding trailing whitespace).</summary>
        public float width;

        /// <summary>Measured line width in mesh-local pixels, set during layout.</summary>
        public float widthPx;

        /// <summary>Width of trailing whitespace excluded from <see cref="width"/>, in font units.</summary>
        public float trailingWhitespace;

        /// <summary>The base BiDi level of the paragraph containing this line.</summary>
        public byte paragraphBaseLevel;

        /// <summary>Left margin for this line (e.g., for list indentation).</summary>
        public float startMargin;

        /// <summary>
        /// Vertical distance from this line's baseline to the next one's, set during layout; zero on
        /// the last line, which advances to nothing. Valid only while
        /// <see cref="UniTextBuffers.HasLineAdvances"/> holds — a re-break clears it.
        /// </summary>
        public float advance;

        /// <summary>
        /// Inclusive prefix sum of <see cref="advance"/> over lines <c>0..this</c>, set during layout,
        /// which lets a Y coordinate find its line by binary search rather than accumulation. Valid
        /// only while <see cref="UniTextBuffers.HasLineAdvances"/> holds.
        /// </summary>
        public float advancePrefix;

        /// <summary>
        /// Extra leading reserved above this line for tall boundary content such as over-ruby; on the
        /// first line it reserves the block's top instead. Written through
        /// <see cref="TextProcessor.ReserveLineSpace"/> from a line-height callback, largest wins.
        /// </summary>
        public float overReserve;

        /// <summary>
        /// Extra leading reserved below this line; on the last line it reserves the block's bottom
        /// instead. Written through <see cref="TextProcessor.ReserveLineSpace"/> from a line-height
        /// callback, largest wins.
        /// </summary>
        public float underReserve;

        /// <summary>
        /// Signed adjustment to the gap between this line and the next — negative pulls them together,
        /// down to full overlap, and the gap never inverts. Unused on the last line, so block edges
        /// stay untouched. Written through <see cref="TextProcessor.AddLineGap"/> from a line-height
        /// callback, requests summing.
        /// </summary>
        public float gap;

        /// <summary>
        /// True when this line was terminated by a UAX #14 mandatory break (CR / LF / NEL / LS / PS / etc.)
        /// — i.e. the next line starts a new paragraph for justification purposes.
        /// </summary>
        /// <remarks>
        /// Drives last-line semantics under <see cref="HorizontalAlignment.Justify"/>: a mandatory-break
        /// line follows <see cref="LastLineAlignment"/> rather than being stretched. The final line of
        /// the document is also treated as a paragraph-last line regardless of this flag.
        /// </remarks>
        public bool endedByMandatoryBreak;

        /// <summary>True when the line's paragraph base direction is right-to-left (UAX #9 odd embedding level).</summary>
        public bool IsRtl => (paragraphBaseLevel & 1) != 0;
    }

    /// <summary>
    /// One hard-break-delimited unit of the text (a UAX #9 bidi paragraph, trailing separator included)
    /// with its slices into the flat pipeline buffers. The unit of incremental invalidation,
    /// within-component parallelism and viewport culling: bidi resolution and shaping runs never cross
    /// a paragraph boundary; line wrapping normally closes at the trailing hard break but crosses the
    /// boundary when that break is suppressed via <c>breakOpportunities</c> or consumed inside a
    /// separator delimiter (see <c>LineBreaker.WrapState</c>). All indices are absolute (document-wide),
    /// matching the flat buffers they slice.
    /// </summary>
    internal struct Paragraph
    {
        public int cpStart;
        public int cpCount;

        /// <summary>UAX #9 paragraph embedding level (odd = RTL).</summary>
        public byte baseLevel;

        /// <summary>Slice of <c>buffers.shapedRuns</c>.</summary>
        public int runStart, runCount;

        /// <summary>Slice of <c>buffers.shapedGlyphs</c>.</summary>
        public int glyphStart, glyphCount;

        /// <summary>
        /// Slice of <c>buffers.lines</c> — the lines the paragraph's wrap call closed. With a suppressed
        /// or delimiter-consumed hard break a line may start in an earlier paragraph, leaving that
        /// paragraph with <c>lineCount == 0</c>.
        /// </summary>
        public int lineStart, lineCount;

        /// <summary>Slice of <c>buffers.orderedRuns</c>, derived from the line slice after bidi reorder.</summary>
        public int orderedRunStart, orderedRunCount;

        /// <summary>Slice of <c>buffers.positionedGlyphs</c>, set during layout.</summary>
        public int posStart, posCount;

        /// <summary>
        /// Vertical band in text space (baseline-relative, Y-down from the rect top; <c>topY &lt; bottomY</c>),
        /// set during layout from the first/last line's glyph boxes. Drives viewport culling of quad emission.
        /// </summary>
        public float topY, bottomY;

        /// <summary>Fingerprint of the itemize+shape inputs; gates the per-paragraph shape cache.</summary>
        public ulong shapeHash;

        /// <summary>Fingerprint of the codepoints + base direction; gates the per-paragraph analysis cache (twin of <see cref="shapeHash"/>).</summary>
        public ulong analysisHash;

        /// <summary>Exclusive end codepoint.</summary>
        public int CpEnd => cpStart + cpCount;
    }


    /// <summary>Vertical extent of a range's per-line boxes (the walker's height mode).</summary>
    public enum RangeHeight : byte
    {
        /// <summary>Full line metrics (ascender to descender) — selection-like. The same height selection rects use. With line spacing &gt; 1 this leaves a gap between lines; use <see cref="LineAdvance"/> for a seamless band.</summary>
        LineBox,
        /// <summary>Tight glyph ink extents for content-hugging range geometry.</summary>
        Content,
        /// <summary>Full line pitch (advance): each band spans the whole line's vertical stride, so consecutive lines' rects share an edge with no gap — gap-free selection, exactly like a browser highlighting wrapped text.</summary>
        LineAdvance
    }

    /// <summary>One per-line source box of a cluster range, in component-local coordinates.</summary>
    public struct RangeBoundsEntry
    {
        public Rect rect;
        /// <summary>Layout line index; consecutive entries with different indices are on adjacent lines.</summary>
        public int lineIndex;
        /// <summary>Resolved direction of the first shaping run represented by this fragment.</summary>
        public bool rtl;
        /// <summary>Smallest selected logical cluster represented by this fragment.</summary>
        public int clusterStart;
        /// <summary>Exclusive selected logical cluster bound represented by this fragment.</summary>
        public int clusterEnd;
        /// <summary>Whether this visual fragment contains the selected range's logical start.</summary>
        public bool containsRangeStart;
        /// <summary>Whether this visual fragment contains the selected range's logical end.</summary>
        public bool containsRangeEnd;
        /// <summary>Physical side on which the logical range start lies.</summary>
        public bool rangeStartOnRight;
        /// <summary>Physical side on which the logical range end lies.</summary>
        public bool rangeEndOnRight;
        internal int firstGlyphIndex;
        internal int lastGlyphIndex;
    }

    /// <summary>
    /// A contiguous run of positioned glyphs within a single line whose clusters fall inside a queried range.
    /// Bounds are in local mesh coordinates (relative to the text origin), with X clamped to the line's
    /// measured extent.
    /// </summary>
    public struct LineRangeEntry
    {
        /// <summary>Index of the line in <c>buffers.lines</c>.</summary>
        public int lineIdx;

        /// <summary>Index of the first positioned glyph of this run (inclusive).</summary>
        public int firstGlyphIdx;

        /// <summary>Index of the last positioned glyph of this run (inclusive).</summary>
        public int lastGlyphIdx;

        /// <summary>Left edge of the run in local coordinates, clamped to line content extent.</summary>
        public float minX;

        /// <summary>Right edge of the run in local coordinates, clamped to line content extent.</summary>
        public float maxX;

        /// <summary>Top edge of the run in local coordinates (uses ascender of glyphs on the line).</summary>
        public float minY;

        /// <summary>Bottom edge of the run in local coordinates (uses descender of glyphs on the line).</summary>
        public float maxY;
    }


    /// <summary>
    /// Represents a glyph with final position coordinates ready for rendering.
    /// </summary>
    /// <remarks>
    /// This is the final output of the text processing pipeline, containing
    /// all information needed to render a glyph to a mesh or texture.
    /// </remarks>
    public struct PositionedGlyph
    {
        /// <summary>The font-specific glyph identifier, or <see cref="ShapedGlyph.NoGlyph"/>.</summary>
        public int glyphId;

        /// <summary>Index of the source codepoint cluster.</summary>
        public int cluster;

        /// <summary>X position of the glyph origin.</summary>
        public float x;

        /// <summary>Y position of the glyph origin.</summary>
        public float y;

        /// <summary>The font ID used for this glyph.</summary>
        public int fontId;

        /// <summary>Optional visual scale around the glyph origin; zero preserves the inherited text size.</summary>
        public float scale;

        /// <summary>Index into the shaped glyphs buffer.</summary>
        public int shapedGlyphIndex;

        /// <summary>Left edge of the glyph bounding box.</summary>
        public float left;

        /// <summary>Top edge of the glyph bounding box.</summary>
        public float top;

        /// <summary>Right edge of the glyph bounding box.</summary>
        public float right;

        /// <summary>Bottom edge of the glyph bounding box.</summary>
        public float bottom;
    }

    /// <summary>
    /// A pre-shaped glyph a modifier hands to the rasterizer when its glyph id comes from OpenType
    /// shaping (GSUB) rather than a 1:1 codepoint lookup — e.g. shaped ruby, where Arabic joining forms
    /// and ligatures differ from the cmap glyph. Registered for atlas rasterization alongside virtual codepoints.
    /// </summary>
    public struct VirtualGlyph
    {
        /// <summary>Font the glyph id belongs to.</summary>
        public int fontId;

        /// <summary>Shaped glyph id to rasterize.</summary>
        public uint glyphId;

        /// <summary>Silhouette-field request for a colour glyph (see <see cref="AttributeKeys.ColorGlyphField"/>); 0 asks for the bitmap alone.</summary>
        public byte fieldExtent;
    }

    /// <summary>
    /// A character resolved for drawing outside the document text — an ellipsis dot, a list marker, a
    /// wheel symbol — in the face the surrounding text uses: which glyph to draw and how far the pen
    /// moves past it, which is all placing one takes.
    /// </summary>
    /// <remarks>
    /// The advance is the only metric a resolve answers in every phase. Ink metrics — bearings, extent —
    /// belong to the glyph's atlas entry, which exists only once rasterization has run, while injected
    /// characters are measured as early as the shaped phase.
    /// </remarks>
    /// <seealso cref="UniTextBuffers.TryResolveInjectedGlyph"/>
    public readonly struct InjectedGlyph
    {
        /// <summary>Font carrying the glyph.</summary>
        public int FontId { get; }

        /// <summary>Glyph index inside <see cref="FontId"/>.</summary>
        public uint GlyphIndex { get; }

        /// <summary>Horizontal advance in pixels at the requested size, with the font's advance overrides applied.</summary>
        public float Advance { get; }

        internal InjectedGlyph(int fontId, uint glyphIndex, float advance)
        {
            FontId = fontId;
            GlyphIndex = glyphIndex;
            Advance = advance;
        }
    }


    internal struct CachedGlyphData
    {
        public int rectX;
        public int rectY;
        public int rectWidth;
        public int rectHeight;
        public float bearingX;
        public float bearingY;
        public float width;
        public float height;
        public int atlasIndex;
        public bool isValid;
    }


    /// <summary>
    /// Specifies horizontal text alignment within the layout bounds.
    /// </summary>
    /// <remarks>
    /// <see cref="Start"/>, <see cref="End"/> and <see cref="Justify"/> are logical: they resolve
    /// against each paragraph's own base direction (UAX #9), so a single value produces different
    /// edges for differently-directed paragraphs of the same text. <see cref="Left"/> and
    /// <see cref="Right"/> are physical and ignore direction. Mirrors CSS <c>text-align</c>,
    /// which carries both families.
    /// </remarks>
    public enum HorizontalAlignment : byte
    {
        /// <summary>Align to the paragraph start edge — left in LTR, right in RTL.</summary>
        Start = 0,

        /// <summary>Center text horizontally.</summary>
        Center = 1,

        /// <summary>Align to the paragraph end edge — right in LTR, left in RTL.</summary>
        End = 2,

        /// <summary>
        /// Stretch each line to fill the full available width by distributing extra space
        /// according to <see cref="TextJustify"/>. The final line of each paragraph is aligned
        /// per <see cref="LastLineAlignment"/> (defaults to start, matching CSS
        /// <c>text-align-last: auto</c>).
        /// </summary>
        Justify = 3,

        /// <summary>Align to the left edge whatever the paragraph direction.</summary>
        Left = 4,

        /// <summary>Align to the right edge whatever the paragraph direction.</summary>
        Right = 5
    }


    /// <summary>
    /// Uniform text-fitting adjustments resolved by the auto-size fit ladder and applied on top of
    /// shaped advances: extra tracking per non-zero-advance glyph, a horizontal glyph-scale
    /// multiplier, and a line-height multiplier.
    /// </summary>
    public struct FitBudgets
    {
        /// <summary>Tracking added to every non-zero glyph advance, in em (negative compresses).</summary>
        public float trackingEm;

        /// <summary>Horizontal scale multiplier applied to glyph advances and quads; <c>1</c> is none.</summary>
        public float glyphScale;

        /// <summary>Multiplier applied to every line's advance height; <c>1</c> is none.</summary>
        public float lineHeightScale;

        /// <summary>Whether every adjustment is at its neutral value.</summary>
        public bool IsIdentity => trackingEm == 0f && glyphScale == 1f && lineHeightScale == 1f;

        /// <summary>Whether the advance-affecting adjustments are at their neutral values.</summary>
        public bool IsAdvanceIdentity => trackingEm == 0f && glyphScale == 1f;

        /// <summary>The neutral value: no tracking, no glyph scaling, no line-height scaling.</summary>
        public static FitBudgets Identity => new() { glyphScale = 1f, lineHeightScale = 1f };
    }


    /// <summary>
    /// Specifies how extra horizontal space is distributed when <see cref="HorizontalAlignment.Justify"/>
    /// is active. Mirrors the CSS Text Module Level 3 <c>text-justify</c> property values.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><see cref="Auto"/> — distribute at word separators; if a line contains no word
    /// separators (typical for pure CJK content), fall back to <see cref="InterCharacter"/>.
    /// Default; matches CSS <c>text-justify: auto</c> behaviour.</item>
    /// <item><see cref="InterWord"/> — distribute only at word-separator characters
    /// (UAX category Zs, excluding NBSP / narrow NBSP). Appropriate for scripts that
    /// separate words with spaces (Latin, Cyrillic, Greek, Korean).</item>
    /// <item><see cref="InterCharacter"/> — distribute at every cluster boundary on the line.
    /// Appropriate for scripts without word separators (Japanese, Chinese, Thai).</item>
    /// <item><see cref="None"/> — no expansion. Justified lines render as start-aligned.</item>
    /// </list>
    /// </remarks>
    public enum TextJustify : byte
    {
        /// <summary>Distribute at word separators; fall back to cluster boundaries when no separators exist on the line.</summary>
        Auto = 0,

        /// <summary>Distribute only at word-separator characters (UAX Zs, excluding NBSP).</summary>
        InterWord = 1,

        /// <summary>Distribute at every grapheme cluster boundary on the line.</summary>
        InterCharacter = 2,

        /// <summary>Do not expand. Justified lines render as start-aligned.</summary>
        None = 3
    }


    /// <summary>
    /// Specifies how the final line of a justified paragraph (and lines preceding a mandatory
    /// break) is aligned. Mirrors CSS Text Module Level 3 <c>text-align-last</c>.
    /// </summary>
    /// <remarks>
    /// The "last line" of a paragraph is the line ended by a mandatory break (CR / LF / etc.)
    /// or the very last line in the document. <see cref="Auto"/> matches the CSS default
    /// behaviour: when <see cref="HorizontalAlignment.Justify"/> is active, the last line is
    /// aligned to the paragraph start.
    /// </remarks>
    public enum LastLineAlignment : byte
    {
        /// <summary>CSS default: equivalent to <see cref="Start"/> when justification is active.</summary>
        Auto = 0,

        /// <summary>Align to the paragraph start edge (left in LTR, right in RTL).</summary>
        Start = 1,

        /// <summary>Align to the paragraph end edge (right in LTR, left in RTL).</summary>
        End = 2,

        /// <summary>Center the last line horizontally.</summary>
        Center = 3,

        /// <summary>Justify the last line as well, stretching it to fill the available width.</summary>
        Justify = 4
    }


    /// <summary>
    /// Specifies vertical text alignment within the layout bounds.
    /// </summary>
    public enum VerticalAlignment : byte
    {
        /// <summary>Align text to the top edge.</summary>
        Top = 0,

        /// <summary>Center text vertically.</summary>
        Middle = 1,

        /// <summary>Align text to the bottom edge.</summary>
        Bottom = 2
    }

    /// <summary>
    /// Specifies which metric defines the top edge of the text box.
    /// </summary>
    /// <remarks>
    /// Controls how the first line is positioned relative to the container top.
    /// Matches CSS <c>text-box-edge</c> over-edge values and Figma Vertical Trim.
    /// </remarks>
    public enum TextOverEdge : byte
    {
        /// <summary>Top edge at ascent line — default, fits all ascenders and diacritics.</summary>
        Ascent = 0,

        /// <summary>Top edge at cap height — tighter fit, matches Figma Vertical Trim.</summary>
        CapHeight = 1,

        /// <summary>Top edge includes half-leading — matches CSS and Figma Standard mode.</summary>
        HalfLeading = 2,

        /// <summary>Top edge at the typographic ascent (OS/2 sTypo, CSS <c>text-box-edge: text</c>) — the designer's content-box top; the tight, browser-matched fit. Falls back to the ascent line when the font has no typo metrics.</summary>
        Text = 3,

        /// <summary>Top edge at x-height (CSS <c>text-box-edge: ex</c>) — trims to the top of lowercase letters.</summary>
        XHeight = 4,
    }


    /// <summary>
    /// Specifies which metric defines the bottom edge of the text box.
    /// </summary>
    /// <remarks>
    /// Controls how the last line contributes to the total text height.
    /// Matches CSS <c>text-box-edge</c> under-edge values and Figma Vertical Trim.
    /// </remarks>
    public enum TextUnderEdge : byte
    {
        /// <summary>Bottom edge at descent line — default, fits all descenders.</summary>
        Descent = 0,

        /// <summary>Bottom edge at baseline — tighter fit, matches Figma Vertical Trim.</summary>
        Baseline = 1,

        /// <summary>Bottom edge includes half-leading — matches CSS and Figma Standard mode.</summary>
        HalfLeading = 2,

        /// <summary>Bottom edge at the typographic descent (OS/2 sTypo, CSS <c>text-box-edge: text</c>). Falls back to the descent line when the font has no typo metrics.</summary>
        Text = 3,
    }


    /// <summary>
    /// Controls how extra leading from line-height is distributed relative to the content area.
    /// </summary>
    /// <remarks>
    /// Different platforms use different models:
    /// <list type="bullet">
    /// <item><see cref="HalfLeading"/> — CSS standard: split equally above and below.</item>
    /// <item><see cref="LeadingAbove"/> — Figma / iOS: all extra space above the line.</item>
    /// <item><see cref="LeadingBelow"/> — Android View / legacy: all extra space below the line.</item>
    /// </list>
    /// </remarks>
    public enum LeadingDistribution : byte
    {
        /// <summary>Extra leading split equally above and below (CSS half-leading model).</summary>
        HalfLeading = 0,

        /// <summary>All extra leading placed above the line (Figma model).</summary>
        LeadingAbove = 1,

        /// <summary>All extra leading placed below the line (Android View model).</summary>
        LeadingBelow = 2,
    }

    /// <summary>
    /// How a line's height is determined relative to the fonts it contains.
    /// </summary>
    public enum LineHeightMode : byte
    {
        /// <summary>Line grows to fit the tallest font on it, including fallback fonts (CSS <c>line-height: normal</c>).</summary>
        Content = 0,

        /// <summary>Height comes from the primary font only; fallback glyphs never enlarge the line (Android <c>fallbackLineSpacing=false</c>).</summary>
        Primary = 1,

        /// <summary>Every line is exactly <c>scale × fontSize</c> regardless of content; tall glyphs may overlap (CSS fixed <c>line-height</c>, Flutter <c>forceStrutHeight</c>).</summary>
        Scaled = 2,
    }


    /// <summary>
    /// Result of a shaping operation containing glyphs and metrics.
    /// </summary>
    /// <remarks>
    /// This is a ref struct to allow returning a span without allocation.
    /// </remarks>
    public readonly ref struct ShapingResult
    {
        /// <summary>The shaped glyphs with positioning information.</summary>
        public readonly ReadOnlySpan<ShapedGlyph> Glyphs;

        /// <summary>The total horizontal advance of all glyphs.</summary>
        public readonly float TotalAdvance;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapingResult"/> struct.
        /// </summary>
        /// <param name="glyphs">The shaped glyphs.</param>
        /// <param name="totalAdvance">The total advance width.</param>
        public ShapingResult(ReadOnlySpan<ShapedGlyph> glyphs, float totalAdvance)
        {
            Glyphs = glyphs;
            TotalAdvance = totalAdvance;
        }
    }
}
