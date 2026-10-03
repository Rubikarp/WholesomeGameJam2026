using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Configuration for text layout and positioning.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="TextLayout"/> to control how text is positioned within the available bounds.
    /// Includes settings for maximum dimensions, spacing, and alignment.
    /// </remarks>
    public struct LayoutSettings
    {
        /// <summary>Maximum width for text layout. Use <see cref="TextProcessSettings.FloatMax"/> for unlimited.</summary>
        public float maxWidth;

        /// <summary>Maximum height for text layout. Use <see cref="TextProcessSettings.FloatMax"/> for unlimited.</summary>
        public float maxHeight;

        /// <summary>Additional spacing between lines (can be negative).</summary>
        public float lineSpacing;

        /// <summary>Fallback line height when font metrics are unavailable.</summary>
        public float defaultLineHeight;

        /// <summary>Horizontal text alignment within the layout bounds.</summary>
        public HorizontalAlignment horizontalAlignment;

        /// <summary>Vertical text alignment within the layout bounds.</summary>
        public VerticalAlignment verticalAlignment;

        /// <summary>
        /// How extra space is distributed when <see cref="horizontalAlignment"/> is
        /// <see cref="HorizontalAlignment.Justify"/>. Ignored otherwise.
        /// </summary>
        public TextJustify textJustify;

        /// <summary>
        /// How the paragraph-terminating line is aligned when <see cref="horizontalAlignment"/>
        /// is <see cref="HorizontalAlignment.Justify"/>. Ignored otherwise.
        /// </summary>
        public LastLineAlignment lastLineAlignment;

        /// <summary>Smallest word-separator width justification compression may reach, as a fraction of natural width.</summary>
        public float justifyWordSpaceMin;

        /// <summary>Largest word-separator width justification expansion spends before the letter and glyph levers engage, as a fraction of natural width. Residual expansion beyond every budget still lands on word separators.</summary>
        public float justifyWordSpaceMax;

        /// <summary>Largest letter-spacing reduction justification compression may spend, in em (non-positive).</summary>
        public float justifyLetterSpaceMin;

        /// <summary>Largest letter-spacing addition justification expansion may spend, in em (non-negative).</summary>
        public float justifyLetterSpaceMax;

        /// <summary>Smallest glyph-width fraction justification compression may reach; <c>1</c> disables the glyph lever.</summary>
        public float justifyGlyphScaleMin;

        /// <summary>Largest glyph-width fraction justification expansion may reach; <c>1</c> disables the glyph lever.</summary>
        public float justifyGlyphScaleMax;

        /// <summary>Top edge metric for text box trimming.</summary>
        public TextOverEdge overEdge;

        /// <summary>Bottom edge metric for text box trimming.</summary>
        public TextUnderEdge underEdge;

        /// <summary>How extra leading from line-height is distributed relative to the content area.</summary>
        public LeadingDistribution leadingDistribution;

        /// <summary>Whether trailing spaces and tabs participate in alignment and reported extents without changing line breaking.</summary>
        public bool measureTrailingWhitespace;

        /// <summary>How each line's height is determined relative to the fonts it contains.</summary>
        public LineHeightMode lineHeightMode;

        /// <summary>Line height as a multiple of font size when <see cref="lineHeightMode"/> is <see cref="LineHeightMode.Scaled"/>. Ignored otherwise.</summary>
        public float lineHeightScale;

        /// <summary>Project-wide default line-height mode seeded into <see cref="Default"/>. Mirrored from <see cref="UniTextSettings.DefaultLineHeightMode"/> and pushed from the main thread, so worker-thread layout reads it without touching <c>Resources</c>.</summary>
        public static LineHeightMode DefaultLineHeightMode = LineHeightMode.Scaled;

        /// <summary>Project-wide default line-height scale seeded into <see cref="Default"/>. Mirrored from <see cref="UniTextSettings.LineHeightScale"/>.</summary>
        public static float DefaultLineHeightScale = 1.4f;

        /// <summary>
        /// Gets the default layout settings with unlimited dimensions, top vertical alignment and
        /// start horizontal alignment.
        /// </summary>
        public static LayoutSettings Default => new()
        {
            maxWidth = TextProcessSettings.FloatMax,
            maxHeight = TextProcessSettings.FloatMax,
            lineSpacing = 0,
            defaultLineHeight = 20,
            horizontalAlignment = HorizontalAlignment.Start,
            verticalAlignment = VerticalAlignment.Top,
            textJustify = TextJustify.Auto,
            lastLineAlignment = LastLineAlignment.Auto,
            overEdge = TextOverEdge.Ascent,
            underEdge = TextUnderEdge.Descent,
            leadingDistribution = LeadingDistribution.HalfLeading,
            measureTrailingWhitespace = false,
            lineHeightMode = DefaultLineHeightMode,
            lineHeightScale = DefaultLineHeightScale,
            justifyWordSpaceMin = 0.8f,
            justifyWordSpaceMax = 1.33f,
            justifyLetterSpaceMin = 0f,
            justifyLetterSpaceMax = 0f,
            justifyGlyphScaleMin = 1f,
            justifyGlyphScaleMax = 1f
        };
    }


    /// <summary>
    /// Positions glyphs within the layout bounds based on line breaking results and alignment settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the final positioning stage of the text processing pipeline. It takes the output
    /// from <see cref="LineBreaker"/> (lines and runs) and produces <see cref="PositionedGlyph"/>
    /// data with final X/Y coordinates.
    /// </para>
    /// <para>
    /// Handles:
    /// <list type="bullet">
    /// <item>Horizontal alignment (left, center, right) with RTL awareness</item>
    /// <item>Vertical alignment (top, middle, bottom)</item>
    /// <item>Line spacing and margins</item>
    /// <item>Glyph scaling based on font size</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <seealso cref="TextProcessor"/>
    /// <seealso cref="LayoutSettings"/>
    public sealed class TextLayout
    {
        private OrderedEvent<LineStyleContext> lineStyleResolver;

        internal void SetLineStyleResolver(OrderedEvent<LineStyleContext> resolver) => lineStyleResolver = resolver;

        private LayoutSettings settings;

        private float fontAscender;
        private float fontDescender;
        private float fontLineHeight;
        private float fontCapHeight;
        private float fontTypoAscent;
        private float fontTypoDescent;
        private float fontXHeight;
        private float glyphScale = 1f;
        private float effectiveFirstLineHeight;
        private float effectiveLastLineHeight;
        private float blockOver;
        private float blockUnder;
        private byte[] hiddenClusters;
        private int hiddenClusterCount;
        private byte hiddenMask;

        internal void SetHiddenLayout(byte[] flags, int count, byte mask)
        {
            hiddenClusters = flags;
            hiddenClusterCount = count;
            hiddenMask = mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsLayoutHidden(int cluster)
            => hiddenMask != 0 && (uint)cluster < (uint)hiddenClusterCount
                               && (hiddenClusters[cluster] & hiddenMask) != 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextLayout"/> class with default settings.
        /// </summary>
        public TextLayout()
        {
            settings = LayoutSettings.Default;
        }

        /// <summary>
        /// Sets font metrics used for line height and baseline calculations.
        /// </summary>
        /// <param name="ascender">Distance from baseline to top of tallest glyph.</param>
        /// <param name="descender">Distance from baseline to bottom of lowest glyph (typically negative).</param>
        /// <param name="lineHeight">Total line height from the font metrics.</param>
        /// <param name="glyphScaleFactor">Scale factor applied to all glyph positions (default 1.0).</param>
        /// <param name="capHeight">Cap height for visual vertical centering (0 to skip correction).</param>
        /// <param name="typoAscent">Typographic ascent for the Text over-edge (0 falls back to ascender).</param>
        /// <param name="typoDescent">Typographic descent, negative, for the Text under-edge (0 falls back to descender).</param>
        /// <param name="xHeight">x-height for the XHeight over-edge (0 falls back to ascender).</param>
        public void SetFontMetrics(float ascender, float descender, float lineHeight, float glyphScaleFactor = 1f, float capHeight = 0f, float typoAscent = 0f, float typoDescent = 0f, float xHeight = 0f)
        {
            fontAscender = ascender;
            fontDescender = descender;
            fontLineHeight = lineHeight;
            fontCapHeight = capHeight;
            fontTypoAscent = typoAscent;
            fontTypoDescent = typoDescent;
            fontXHeight = xHeight;
            glyphScale = glyphScaleFactor;
        }

        /// <summary>
        /// Sets the layout settings controlling dimensions and alignment.
        /// </summary>
        /// <param name="newSettings">The new layout settings to apply.</param>
        public void SetLayoutSettings(LayoutSettings newSettings)
        {
            settings = newSettings;
        }

        /// <summary>
        /// Sets the effective line heights after modifier callbacks, used for half-leading calculation.
        /// </summary>
        /// <param name="firstLineHeight">Effective height of the first line (0 = use base metrics).</param>
        /// <param name="lastLineHeight">Effective height of the last line (0 = use base metrics).</param>
        public void SetEffectiveLineHeights(float firstLineHeight, float lastLineHeight, float blockOver = 0f, float blockUnder = 0f)
        {
            effectiveFirstLineHeight = firstLineHeight;
            effectiveLastLineHeight = lastLineHeight;
            this.blockOver = blockOver;
            this.blockUnder = blockUnder;
        }

        private float[] glyphXScales;
        private int glyphXScaleCount;
        private float blockGlyphScale = 1f;
        private float fontSizePx;
        private bool wroteGlyphXScales;

        /// <summary>Whether the last <see cref="Layout"/> pass wrote any per-line glyph x-scale. The
        /// caller deactivates the channel when neither this nor the block fill used it.</summary>
        internal bool WroteGlyphXScales => wroteGlyphXScales;

        /// <summary>
        /// Hands the pass its glyph x-scale channel and fit context. <paramref name="xScales"/>
        /// arrives sized to the codepoint count and pre-filled with <paramref name="blockGlyphScale"/>
        /// (zeroed when it is 1); justification multiplies per-line glyph factors into it. Null
        /// deactivates the channel. <paramref name="fontSizePx"/> is the effective font size, the em
        /// base for letter-spacing budgets.
        /// </summary>
        internal void SetFitState(float[] xScales, int count, float blockGlyphScale, float fontSizePx)
        {
            glyphXScales = xScales;
            glyphXScaleCount = count;
            this.blockGlyphScale = blockGlyphScale;
            this.fontSizePx = fontSizePx;
        }

        /// <summary>
        /// Positions all glyphs from the line breaking results into final screen coordinates.
        /// </summary>
        /// <param name="lines">The lines produced by line breaking.</param>
        /// <param name="runs">The shaped runs referenced by lines.</param>
        /// <param name="glyphs">The shaped glyphs referenced by runs.</param>
        /// <param name="totalHeight">Pre-computed total text height (from TextProcessor).</param>
        /// <param name="result">Output array to receive positioned glyphs.</param>
        /// <param name="glyphCount">Returns the number of positioned glyphs written.</param>
        /// <param name="width">Returns the maximum line width encountered.</param>
        /// <param name="height">Returns the total text height.</param>
        /// <remarks>
        /// <para>
        /// The method iterates through all lines, applying horizontal alignment per-line
        /// and accounting for RTL paragraphs. Vertical positioning starts from the top
        /// and advances downward by line height plus spacing.
        /// </para>
        /// <para>
        /// Each glyph's final position combines the line's X offset, the glyph's advance
        /// within the run, and any glyph-specific offsets from shaping (e.g., diacritics).
        /// </para>
        /// </remarks>
        public void Layout(
            Span<TextLine> lines,
            ReadOnlySpan<ShapedRun> runs,
            ReadOnlySpan<ShapedGlyph> glyphs,
            ReadOnlySpan<int> codepoints,
            float totalHeight,
            PositionedGlyph[] result,
            ref int glyphCount,
            out float width,
            out float height)
        {
            wroteGlyphXScales = false;
            glyphCount = 0;
            width = 0;
            height = 0;

            var lineCount = lines.Length;
            if (lineCount == 0)
                return;

            var computedLineHeight = fontLineHeight;
            if (computedLineHeight <= 0)
                computedLineHeight = fontAscender - fontDescender;
            if (computedLineHeight <= 0)
                computedLineHeight = settings.defaultLineHeight;

            var ascender = fontAscender;
            if (ascender <= 0) ascender = computedLineHeight * 0.8f;

            var contentArea = ascender - fontDescender;
            var firstLeading = MathF.Max(0, effectiveFirstLineHeight - contentArea);
            var topLeading = settings.leadingDistribution switch
            {
                LeadingDistribution.LeadingAbove => firstLeading,
                LeadingDistribution.LeadingBelow => 0f,
                _ => firstLeading * 0.5f
            };

            float topMetric = settings.overEdge switch
            {
                TextOverEdge.CapHeight when fontCapHeight > 0 => fontCapHeight,
                TextOverEdge.XHeight when fontXHeight > 0 => fontXHeight,
                TextOverEdge.Text when fontTypoAscent > 0 => fontTypoAscent,
                TextOverEdge.HalfLeading => ascender + topLeading,
                _ => ascender
            };
            topMetric += blockOver;

            var trimAmount = ComputeTrimAmount(ascender, fontDescender,
                fontCapHeight, settings.overEdge, settings.underEdge,
                settings.leadingDistribution,
                effectiveFirstLineHeight, effectiveLastLineHeight,
                fontTypoAscent, fontTypoDescent, fontXHeight);

            var effectiveHeight = totalHeight - trimAmount + blockOver + blockUnder;

            var y = ComputeTextStartY(effectiveHeight, settings) + topMetric;
            float maxLineWidth = 0;

            var availableWidth = settings.maxWidth;
            var hAlign = settings.horizontalAlignment;
            var hasFiniteWidth = !float.IsInfinity(availableWidth) && availableWidth > 0;

            for (var i = 0; i < lineCount; i++)
            {
                ref var line = ref lines[i];
                var runStart = line.runStart;
                var runCount = line.runCount;
                var runEnd = runStart + runCount;
                var lineGlyphStart = glyphCount;

                var lineWidth = (line.width
                    + (settings.measureTrailingWhitespace ? line.trailingWhitespace : 0f)) * glyphScale;
                var marginScaled = line.startMargin * glyphScale;

                var isRtlLine = (line.paragraphBaseLevel & 1) == 1;
                var isParagraphLastLine = line.endedByMandatoryBreak || i == lineCount - 1;

                var lineAlign = hAlign;
                var lineJustify = settings.textJustify;
                var lineLastLine = settings.lastLineAlignment;
                var wordSpaceMin = settings.justifyWordSpaceMin;
                var wordSpaceMax = settings.justifyWordSpaceMax;
                var letterSpaceMin = settings.justifyLetterSpaceMin;
                var letterSpaceMax = settings.justifyLetterSpaceMax;
                var glyphScaleMin = settings.justifyGlyphScaleMin;
                var glyphScaleMax = settings.justifyGlyphScaleMax;
                if (lineStyleResolver?.HasSubscribers == true)
                {
                    var context = new LineStyleContext
                    {
                        lineIndex = i,
                        startCluster = line.range.start,
                        endCluster = line.range.End,
                        alignment = lineAlign,
                        justify = lineJustify,
                        lastLine = lineLastLine,
                        wordSpaceMin = wordSpaceMin,
                        wordSpaceMax = wordSpaceMax,
                        letterSpaceMin = letterSpaceMin,
                        letterSpaceMax = letterSpaceMax,
                        glyphScaleMin = glyphScaleMin,
                        glyphScaleMax = glyphScaleMax
                    };
                    lineStyleResolver.Invoke(ref context);
                    lineAlign = context.alignment;
                    lineJustify = context.justify;
                    lineLastLine = context.lastLine;
                    wordSpaceMin = context.wordSpaceMin;
                    wordSpaceMax = context.wordSpaceMax;
                    letterSpaceMin = context.letterSpaceMin;
                    letterSpaceMax = context.letterSpaceMax;
                    glyphScaleMin = context.glyphScaleMin;
                    glyphScaleMax = context.glyphScaleMax;
                }

                var effectiveAlign = ResolveEffectiveAlignment(lineAlign, lineLastLine, isParagraphLastLine);
                var gapPx = availableWidth - marginScaled - lineWidth;
                var justifyThisLine = hasFiniteWidth
                    && effectiveAlign == HorizontalAlignment.Justify
                    && (gapPx > 0.01f || gapPx < -0.01f);

                int firstTrailingWsCp = 0;
                var perSepPx = 0f;
                var perBoundaryPx = 0f;
                var lineGlyphFactor = 1f;
                var appliedDeltaPx = 0f;

                if (justifyThisLine)
                {
                    firstTrailingWsCp = ComputeFirstTrailingWsCp(codepoints, line);
                    var effectiveJustifyMode = ResolveJustifyOpportunities(
                        lineJustify, runs, glyphs, codepoints,
                        runStart, runCount, firstTrailingWsCp,
                        out var opportunities);

                    if (effectiveJustifyMode == TextJustify.None
                        || effectiveJustifyMode == TextJustify.InterWord && opportunities.wordCount <= 0
                        || opportunities.wordCount <= 0 && opportunities.charCount <= 0)
                    {
                        justifyThisLine = false;
                    }
                    else
                    {
                        ComputeJustifyDistribution(gapPx, effectiveJustifyMode, in opportunities,
                            wordSpaceMin, wordSpaceMax, letterSpaceMin, letterSpaceMax,
                            glyphScaleMin, glyphScaleMax, fontSizePx,
                            out perSepPx, out perBoundaryPx, out lineGlyphFactor, out appliedDeltaPx);

                        if (appliedDeltaPx == 0f && lineGlyphFactor == 1f)
                            justifyThisLine = false;
                    }
                }

                if (!justifyThisLine && effectiveAlign == HorizontalAlignment.Justify)
                    effectiveAlign = HorizontalAlignment.Start;

                float x;
                if (hasFiniteWidth)
                {
                    x = justifyThisLine
                        ? ComputeJustifiedLineStartX(isRtlLine, marginScaled)
                        : ComputeLineStartX(lineWidth, isRtlLine, availableWidth, effectiveAlign);

                    if (isRtlLine && !settings.measureTrailingWhitespace && line.trailingWhitespace > 0)
                        x -= line.trailingWhitespace * glyphScale;
                }
                else
                    x = 0;

                if (line.startMargin > 0 && hasFiniteWidth && !justifyThisLine)
                {
                    if (isRtlLine)
                    {
                        if (effectiveAlign is HorizontalAlignment.Start or HorizontalAlignment.Right)
                            x -= marginScaled;
                        else if (effectiveAlign == HorizontalAlignment.Center)
                            x = (availableWidth - marginScaled - lineWidth) * 0.5f;
                    }
                    else
                    {
                        if (effectiveAlign is HorizontalAlignment.Start or HorizontalAlignment.Left)
                            x += marginScaled;
                        else if (effectiveAlign == HorizontalAlignment.Center)
                            x = marginScaled + (availableWidth - marginScaled - lineWidth) * 0.5f;
                    }
                }

                int prevCluster = -1;

                for (var r = runStart; r < runEnd; r++)
                {
                    ref readonly var run = ref runs[r];
                    var glyphStart = run.glyphStart;
                    var glyphLen = run.glyphCount;

                    var fontId = run.fontId;
                    var glyphEnd = glyphStart + glyphLen;

                    for (var g = glyphStart; g < glyphEnd; g++)
                    {
                        ref readonly var glyph = ref glyphs[g];
                        var clusterIdx = glyph.cluster;
                        if (IsLayoutHidden(clusterIdx)) continue;

                        if (justifyThisLine
                            && perBoundaryPx != 0f
                            && prevCluster >= 0
                            && clusterIdx != prevCluster
                            && clusterIdx < firstTrailingWsCp)
                        {
                            x += perBoundaryPx;
                        }

                        var glyphX = x + glyph.offsetX * glyphScale;
                        var advanceScaled = glyph.advanceX * glyphScale;

                        if (justifyThisLine
                            && lineGlyphFactor != 1f
                            && clusterIdx < firstTrailingWsCp
                            && glyph.advanceX != 0f)
                        {
                            advanceScaled *= lineGlyphFactor;
                            if (glyphXScales != null && (uint)clusterIdx < (uint)glyphXScaleCount)
                            {
                                glyphXScales[clusterIdx] = blockGlyphScale * lineGlyphFactor;
                                wroteGlyphXScales = true;
                            }
                        }

                        var boundsTop = y - ascender;
                        var boundsBottom = y - fontDescender;

                        result[glyphCount++] = new PositionedGlyph
                        {
                            glyphId = glyph.glyphId,
                            cluster = clusterIdx,
                            x = glyphX,
                            y = y - glyph.offsetY * glyphScale,
                            fontId = fontId,
                            shapedGlyphIndex = g,
                            left = x,
                            right = x + advanceScaled,
                            top = boundsTop,
                            bottom = boundsBottom
                        };
                        x += advanceScaled;

                        if (justifyThisLine)
                        {
                            if (perSepPx != 0f
                                && clusterIdx < firstTrailingWsCp
                                && (uint)clusterIdx < (uint)codepoints.Length
                                && UnicodeData.IsJustifiableWordSeparator(codepoints[clusterIdx]))
                            {
                                x += perSepPx;
                            }
                            prevCluster = clusterIdx;
                        }
                    }
                }

                line.glyphStart = lineGlyphStart;
                line.glyphCount = glyphCount - lineGlyphStart;
                line.widthPx = justifyThisLine ? lineWidth + appliedDeltaPx : lineWidth;

                var renderedWidth = line.widthPx;
                if (renderedWidth > maxLineWidth)
                    maxLineWidth = renderedWidth;

                y += line.advance;
            }

            width = maxLineWidth;
            height = effectiveHeight;
        }

        /// <summary>
        /// Resolves the per-line alignment under CSS Text Module 3 §6.3 <c>text-align-last</c> semantics.
        /// Non-justify alignments pass through unchanged. For <see cref="HorizontalAlignment.Justify"/>:
        /// non-paragraph-last lines stay <see cref="HorizontalAlignment.Justify"/>; paragraph-last lines
        /// (mandatory-break-terminated or final-of-document, includes single-line paragraphs) follow
        /// <paramref name="lastLineAlignment"/>, defaulting to <see cref="HorizontalAlignment.Start"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static HorizontalAlignment ResolveEffectiveAlignment(
            HorizontalAlignment hAlign, LastLineAlignment lastLineAlignment, bool isParagraphLastLine)
        {
            if (hAlign != HorizontalAlignment.Justify) return hAlign;
            if (!isParagraphLastLine) return HorizontalAlignment.Justify;

            return lastLineAlignment switch
            {
                LastLineAlignment.Center => HorizontalAlignment.Center,
                LastLineAlignment.End => HorizontalAlignment.End,
                LastLineAlignment.Justify => HorizontalAlignment.Justify,
                _ => HorizontalAlignment.Start,
            };
        }

        /// <summary>
        /// Starting X for a justified line. Content fills <c>availableWidth - marginScaled</c>;
        /// unmeasured trailing whitespace hangs from the paragraph-end side.
        /// </summary>
        /// <remarks>
        /// LTR: content runs from <paramref name="marginScaled"/> (left, after start margin) to
        /// the available right edge. RTL: content runs from <c>0</c> (left edge) to the available
        /// right edge minus the start margin (which sits on the right in RTL).
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeJustifiedLineStartX(bool isRtlLine, float marginScaled)
        {
            return isRtlLine ? 0f : marginScaled;
        }

        private int ComputeFirstTrailingWsCp(ReadOnlySpan<int> codepoints, in TextLine line)
        {
            var lineStart = line.range.start;
            var lineEnd = lineStart + line.range.length - 1;
            if (lineEnd < lineStart) return lineStart;
            if ((uint)lineEnd >= (uint)codepoints.Length) return lineStart;

            var firstTrailing = lineEnd + 1;
            for (var cp = lineEnd; cp >= lineStart; cp--)
            {
                if (IsLayoutHidden(cp)) continue;
                if (!LineBreaker.IsHangingWhitespace(codepoints[cp])) break;
                firstTrailing = cp;
            }
            return firstTrailing;
        }

        private struct JustifyOpportunities
        {
            public int wordCount;
            public int charCount;
            public float wordAdvancePx;
            public float scalableAdvancePx;
        }

        /// <summary>
        /// Single-pass resolver: walks the line once to count inter-word and inter-character
        /// opportunities and sum the distributable widths — separator advances (the word-space
        /// budget base) and all glyph advances (the glyph-scale budget base). For
        /// <see cref="TextJustify.Auto"/>, picks <see cref="TextJustify.InterWord"/> when the line
        /// contains any whitespace separator (Latin / Cyrillic / Korean / mixed) and falls back to
        /// <see cref="TextJustify.InterCharacter"/> on lines without whitespace (pure CJK, dense Thai).
        /// </summary>
        private TextJustify ResolveJustifyOpportunities(
            TextJustify requested,
            ReadOnlySpan<ShapedRun> runs, ReadOnlySpan<ShapedGlyph> glyphs, ReadOnlySpan<int> codepoints,
            int runStart, int runCount, int firstTrailingWsCp,
            out JustifyOpportunities opportunities)
        {
            opportunities = default;
            if (requested == TextJustify.None)
                return TextJustify.None;

            int wordOpportunities = 0;
            int charOpportunities = 0;
            float wordAdvance = 0f;
            float scalableAdvance = 0f;
            int prevCluster = -1;

            var runEnd = runStart + runCount;
            for (var r = runStart; r < runEnd; r++)
            {
                ref readonly var run = ref runs[r];
                var gEnd = run.glyphStart + run.glyphCount;
                for (var g = run.glyphStart; g < gEnd; g++)
                {
                    var cluster = glyphs[g].cluster;
                    if (IsLayoutHidden(cluster)) continue;
                    if (cluster >= firstTrailingWsCp) continue;

                    if (prevCluster >= 0 && cluster != prevCluster)
                        charOpportunities++;

                    if ((uint)cluster < (uint)codepoints.Length
                        && UnicodeData.IsJustifiableWordSeparator(codepoints[cluster]))
                    {
                        wordOpportunities++;
                        wordAdvance += glyphs[g].advanceX;
                    }

                    scalableAdvance += glyphs[g].advanceX;

                    prevCluster = cluster;
                }
            }

            opportunities.wordCount = wordOpportunities;
            opportunities.charCount = charOpportunities;
            opportunities.wordAdvancePx = wordAdvance * glyphScale;
            opportunities.scalableAdvancePx = scalableAdvance * glyphScale;

            switch (requested)
            {
                case TextJustify.InterWord:
                    return TextJustify.InterWord;
                case TextJustify.InterCharacter:
                    return TextJustify.InterCharacter;
                default:
                    return wordOpportunities > 0 ? TextJustify.InterWord : TextJustify.InterCharacter;
            }
        }

        /// <summary>
        /// Distributes a signed line gap over the justification ladder — word spaces, then letter
        /// spacing, then glyph scaling, each up to its budget. Expansion beyond every budget still
        /// lands on the mode's opportunities (word separators, or cluster boundaries without them),
        /// so a line always reaches the margin; compression stops at the budgets and leaves the
        /// remainder overfull. <see cref="TextJustify.InterWord"/> spends word spaces only;
        /// <see cref="TextJustify.InterCharacter"/> never touches word spaces.
        /// </summary>
        private static void ComputeJustifyDistribution(float gapPx, TextJustify mode,
            in JustifyOpportunities opp,
            float wordSpaceMin, float wordSpaceMax, float letterSpaceMinEm, float letterSpaceMaxEm,
            float glyphScaleMin, float glyphScaleMax, float emPx,
            out float perSepPx, out float perBoundaryPx, out float glyphFactor, out float appliedPx)
        {
            perSepPx = 0f;
            perBoundaryPx = 0f;
            glyphFactor = 1f;

            var expanding = gapPx > 0f;
            var wordCap = mode == TextJustify.InterCharacter || opp.wordCount == 0 ? 0f
                : opp.wordAdvancePx * (expanding ? MathF.Max(0f, wordSpaceMax - 1f) : MathF.Max(0f, 1f - wordSpaceMin));
            var letterCap = mode == TextJustify.InterWord || opp.charCount == 0 ? 0f
                : opp.charCount * emPx * (expanding ? MathF.Max(0f, letterSpaceMaxEm) : MathF.Max(0f, -letterSpaceMinEm));
            var glyphCap = mode == TextJustify.InterWord || opp.scalableAdvancePx <= 0f ? 0f
                : opp.scalableAdvancePx * (expanding ? MathF.Max(0f, glyphScaleMax - 1f) : MathF.Max(0f, 1f - glyphScaleMin));

            var remaining = MathF.Abs(gapPx);
            var word = MathF.Min(remaining, wordCap);
            remaining -= word;
            var letter = MathF.Min(remaining, letterCap);
            remaining -= letter;
            var glyph = MathF.Min(remaining, glyphCap);
            remaining -= glyph;

            if (expanding && remaining > 0f)
            {
                if (mode != TextJustify.InterCharacter && opp.wordCount > 0)
                    word += remaining;
                else if (opp.charCount > 0)
                    letter += remaining;
                else if (opp.wordCount > 0)
                    word += remaining;
            }

            var sign = expanding ? 1f : -1f;
            if (opp.wordCount > 0) perSepPx = sign * word / opp.wordCount;
            if (opp.charCount > 0) perBoundaryPx = sign * letter / opp.charCount;
            if (opp.scalableAdvancePx > 0f) glyphFactor = 1f + sign * glyph / opp.scalableAdvancePx;
            appliedPx = sign * (word + letter + glyph);
        }

        /// <summary>
        /// Computes the total height trim based on edge metrics and leading distribution.
        /// </summary>
        /// <param name="ascender">Font ascender value.</param>
        /// <param name="descender">Font descender value (typically negative).</param>
        /// <param name="capHeight">Font cap height (0 if unavailable).</param>
        /// <param name="overEdge">Top edge metric.</param>
        /// <param name="underEdge">Bottom edge metric.</param>
        /// <param name="distribution">How extra leading is distributed.</param>
        /// <param name="effectiveFirstLineHeight">Effective height of the first line (including modifier adjustments).</param>
        /// <param name="effectiveLastLineHeight">Effective height of the last line (including modifier adjustments).</param>
        /// <param name="typoAscent">Typographic ascent for the Text over-edge (0 to skip).</param>
        /// <param name="typoDescent">Typographic descent, negative, for the Text under-edge (0 to skip).</param>
        /// <param name="xHeight">x-height for the XHeight over-edge (0 to skip).</param>
        /// <returns>The total amount to subtract from raw height to get effective height.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeTrimAmount(
            float ascender, float descender,
            float capHeight, TextOverEdge overEdge, TextUnderEdge underEdge,
            LeadingDistribution distribution,
            float effectiveFirstLineHeight, float effectiveLastLineHeight,
            float typoAscent = 0f, float typoDescent = 0f, float xHeight = 0f)
        {
            var contentArea = ascender - descender;
            var firstLeading = MathF.Max(0, effectiveFirstLineHeight - contentArea);
            var lastLeading = MathF.Max(0, effectiveLastLineHeight - contentArea);

            var topLeading = distribution switch
            {
                LeadingDistribution.LeadingAbove => firstLeading,
                LeadingDistribution.LeadingBelow => 0f,
                _ => firstLeading * 0.5f
            };

            var bottomLeading = distribution switch
            {
                LeadingDistribution.LeadingAbove => 0f,
                LeadingDistribution.LeadingBelow => lastLeading,
                _ => lastLeading * 0.5f
            };

            float topTrim = overEdge switch
            {
                TextOverEdge.CapHeight when capHeight > 0 => ascender - capHeight,
                TextOverEdge.XHeight when xHeight > 0 => ascender - xHeight,
                TextOverEdge.Text when typoAscent > 0 => ascender - typoAscent,
                TextOverEdge.HalfLeading => -topLeading,
                _ => 0f
            };

            float bottomTrim = underEdge switch
            {
                TextUnderEdge.Baseline => -descender,
                TextUnderEdge.Text when typoDescent < 0 => typoDescent - descender,
                TextUnderEdge.HalfLeading => -bottomLeading,
                _ => 0f
            };

            return topTrim + bottomTrim;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeLineStartX(float lineWidth, bool isRtlLine, float availableWidth,
            HorizontalAlignment alignment)
        {
            return alignment switch
            {
                HorizontalAlignment.Start => isRtlLine ? availableWidth - lineWidth : 0,
                HorizontalAlignment.End => isRtlLine ? 0 : availableWidth - lineWidth,
                HorizontalAlignment.Left => 0,
                HorizontalAlignment.Right => availableWidth - lineWidth,
                HorizontalAlignment.Justify => isRtlLine ? availableWidth - lineWidth : 0,
                _ => (availableWidth - lineWidth) * 0.5f
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float ComputeTextStartY(float totalTextHeight, LayoutSettings settings)
        {
            var availableHeight = settings.maxHeight;
            if (float.IsInfinity(availableHeight) || availableHeight <= 0)
                return 0;

            return settings.verticalAlignment switch
            {
                VerticalAlignment.Middle => (availableHeight - totalTextHeight) * 0.5f
                    + (settings.overEdge == TextOverEdge.Ascent && fontCapHeight > 0
                        ? (fontCapHeight - fontAscender - fontDescender) * 0.5f : 0f),
                VerticalAlignment.Bottom => availableHeight - totalTextHeight,
                _ => 0
            };
        }
    }
}
