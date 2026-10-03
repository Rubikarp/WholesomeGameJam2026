using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    internal enum MathGlyphForm : byte
    {
        Default,
        Dotless,
        FlattenedAccent
    }

    /// <summary>
    /// Provides glyph-level metrics required by the math layout engine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layout engine needs three pieces of information per glyph: horizontal advance width,
    /// height (ascent above baseline), and depth (descent below baseline). They come from the
    /// font's shaping metrics and unscaled outline metrics.
    /// </para>
    /// <para>
    /// The layout engine also needs to map Unicode codepoints to glyph IDs, query italic
    /// corrections from the MATH table, and fetch top accent attachment points.
    /// All values are in font design units; the layout engine converts to pixels using
    /// <see cref="MathLayoutContext.ToPixels(int)"/>.
    /// </para>
    /// </remarks>
    internal interface IMathGlyphProvider
    {
        /// <summary>
        /// Gets the glyph ID for a Unicode codepoint in the specified font.
        /// Returns false if the glyph is not found.
        /// </summary>
        bool TryGetGlyphId(int fontId, int codepoint, int scriptLevel, MathGlyphForm form,
            out uint glyphId);

        /// <summary>
        /// Gets the horizontal advance width for a glyph, in design units.
        /// </summary>
        int GetAdvanceWidth(int fontId, uint glyphId);

        /// <summary>
        /// Gets the height (ascent above baseline) for a glyph, in design units.
        /// Positive upward.
        /// </summary>
        int GetGlyphHeight(int fontId, uint glyphId);

        /// <summary>
        /// Gets the depth (descent below baseline) for a glyph, in design units.
        /// Positive downward.
        /// </summary>
        int GetGlyphDepth(int fontId, uint glyphId);

        /// <summary>
        /// Gets the italic correction for a glyph from the MATH table, in design units.
        /// Returns 0 if not available.
        /// </summary>
        int GetItalicCorrection(int fontId, uint glyphId);

        /// <summary>
        /// Gets the top accent attachment point for a glyph, in design units.
        /// Returns half the advance width if not specified in the MATH table.
        /// </summary>
        int GetTopAccentAttachment(int fontId, uint glyphId);

        bool IsGlyphExtendedShape(int fontId, uint glyphId);

        int GetGlyphKerning(int fontId, uint glyphId, HB.MathKern kern, int correctionHeight);

        int GetGlyphVariants(int fontId, uint glyphId, int direction,
            HB.hb_ot_math_glyph_variant_t[] variants);

        int GetMinConnectorOverlap(int fontId, int direction);

        int GetGlyphAssembly(int fontId, uint glyphId, int direction,
            HB.hb_ot_math_glyph_part_t[] parts, out int italicsCorrection);

        void ShapeMathText(ReadOnlySpan<char> text, float fontSize, int scriptLevel,
            ref PooledBuffer<ShapedGlyph> glyphs, ref PooledBuffer<int> glyphFonts,
            out float width);

        void ShapeText(ReadOnlySpan<char> text, float fontSize,
            ref PooledBuffer<ShapedGlyph> glyphs, ref PooledBuffer<int> glyphFonts,
            out float width);

        /// <summary>
        /// Returns the exact shaped glyph ink extents, or false when the font cannot provide them.
        /// </summary>
        bool TryGetTextGlyphExtents(int fontId, uint glyphId, float fontSize,
            out float height, out float depth);

        void GetTextLineMetrics(int fontId, float fontSize, out float height, out float depth);
    }


    /// <summary>
    /// Converts a <see cref="MathNodeList"/> AST into a <see cref="MathBoxBuffer"/> box tree,
    /// implementing the core algorithm from TeX's Appendix G and the OpenType MATH table spec.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layout is recursive and bottom-up: children are laid out first, then assembled into
    /// parent containers. All boxes are stored in a flat <see cref="MathBoxBuffer"/> array for
    /// cache-friendly traversal and zero per-node heap allocation.
    /// </para>
    /// <para>
    /// <b>Algorithm overview</b> (Appendix G rules by node type):
    /// <list type="bullet">
    /// <item><b>Atom</b> (Rule 1/17): Convert symbol to glyph box with metrics.</item>
    /// <item><b>Group</b> (Rules 1-4): Lay out children left-to-right in an HBox with inter-atom spacing.</item>
    /// <item><b>Fraction</b> (Rule 15): Position numerator/denominator around axis with fraction bar.</item>
    /// <item><b>Script</b> (Rule 18): Position superscript/subscript with shift/gap constraints.</item>
    /// <item><b>Radical</b> (Rule 11): Build radical sign + overline + body.</item>
    /// <item><b>Operator</b> (Rule 13): Large operator with optional limits above/below.</item>
    /// <item><b>Accent</b> (Rule 12): Position accent above base.</item>
    /// <item><b>Delimiter</b>: Stretchy variants or glyph assemblies around the body.</item>
    /// <item><b>Matrix</b>: Grid layout with column alignment.</item>
    /// <item><b>Space</b>: Mu-to-pixel conversion.</item>
    /// <item><b>StyleChange</b>: Modify layout context style.</item>
    /// </list>
    /// </para>
    /// <para>
    /// All positioning constants come from the <see cref="MathFontMetrics"/> struct, which mirrors
    /// the OpenType MATH constants used by these layout algorithms.
    /// </para>
    /// </remarks>
    internal struct MathLayoutEngine
    {
        private MathBoxBuffer buffer;
        private IMathGlyphProvider glyphProvider;
        private string source;

        private float baseFontSize;

        private PooledBuffer<int> childIndices;
        private PooledBuffer<ShapedGlyph> shapedText;
        private PooledBuffer<int> shapedTextFonts;

        /// <summary>
        /// Lays out a math formula, converting the AST into a positioned box tree.
        /// </summary>
        /// <param name="nodes">The parsed math AST.</param>
        /// <param name="ctx">The layout context (style, font, metrics). Modified during recursion.</param>
        /// <param name="provider">Provides glyph metrics from the font.</param>
        /// <returns>A layout result containing the box tree and overall dimensions.</returns>
        public MathLayoutResult Layout(ref MathNodeList nodes, ref MathLayoutContext ctx,
            IMathGlyphProvider provider, string formula)
        {
            glyphProvider = provider;
            source = formula;
            baseFontSize = ctx.fontSize;
            buffer.Rent(64);
            childIndices.Rent(32);
            shapedText.Rent(16);
            shapedTextFonts.Rent(16);
            var transferred = false;
            try
            {
                var rootBoxIndex = LayoutNode(ref nodes, nodes.root, ref ctx);

                ref var rootBox = ref buffer[rootBoxIndex];
                var result = new MathLayoutResult
                {
                    buffer = buffer,
                    rootIndex = rootBoxIndex,
                    width = rootBox.width,
                    height = rootBox.height,
                    depth = rootBox.depth,
                };

                transferred = true;
                return result;
            }
            finally
            {
                childIndices.Return();
                shapedText.Return();
                shapedTextFonts.Return();
                if (!transferred)
                    buffer.Return();
            }
        }

        /// <summary>
        /// Recursively lays out a single AST node and returns the box index in the buffer.
        /// Dispatches to type-specific layout methods based on <see cref="MathNodeType"/>.
        /// </summary>
        private int LayoutNode(ref MathNodeList nodes, int nodeIndex, ref MathLayoutContext ctx)
        {
            if (nodeIndex == MathNode.None)
                return AddEmptyKern();

            ref var node = ref nodes[nodeIndex];

            switch (node.type)
            {
                case MathNodeType.Atom:
                    return LayoutAtom(ref node, ref ctx);

                case MathNodeType.Group:
                    return LayoutGroup(ref nodes, ref node, ref ctx);

                case MathNodeType.Reference:
                    return LayoutNode(ref nodes, node.child0, ref ctx);

                case MathNodeType.Fraction:
                    return LayoutFraction(ref nodes, ref node, ref ctx);

                case MathNodeType.Script:
                    return LayoutScript(ref nodes, ref node, ref ctx);

                case MathNodeType.Radical:
                    return LayoutRadical(ref nodes, ref node, ref ctx);

                case MathNodeType.Operator:
                    return LayoutOperator(ref node, ref ctx);

                case MathNodeType.Accent:
                    return LayoutAccent(ref nodes, ref node, ref ctx);

                case MathNodeType.Bar:
                    return LayoutBar(ref nodes, ref node, ref ctx);

                case MathNodeType.Delimiter:
                    return LayoutDelimiter(ref nodes, ref node, ref ctx);

                case MathNodeType.Matrix:
                    return LayoutMatrix(ref nodes, ref node, ref ctx);

                case MathNodeType.Space:
                    return LayoutSpace(ref node, ref ctx);

                case MathNodeType.Text:
                    return LayoutText(ref node, ref ctx);

                case MathNodeType.StyleChange:
                    ctx.style = node.TargetStyle;
                    ctx.ApplyScaledFontSize(baseFontSize);
                    return AddEmptyKern();

                default:
                    throw new InvalidOperationException($"Unsupported math node type {node.type}.");
            }
        }

        /// <summary>
        /// Lays out a single atom (symbol/character) as a Glyph box.
        /// </summary>
        /// <remarks>
        /// Retrieves glyph metrics (advance, height, depth) and italic correction from the font.
        /// The italic correction is stored on the box for use by script positioning (Rule 18).
        /// </remarks>
        private int LayoutAtom(ref MathNode node, ref MathLayoutContext ctx,
            MathGlyphForm form = MathGlyphForm.Default)
        {
            var cp = node.codepoint;
            if (cp <= 0)
                return AddEmptyKern();

            if (!glyphProvider.TryGetGlyphId(ctx.fontId, cp, MathStyleUtil.SizeLevel(ctx.style), form,
                    out var glyphId))
                throw new InvalidOperationException($"Math font does not contain U+{cp:X4}.");

            var advanceDU = glyphProvider.GetAdvanceWidth(ctx.fontId, glyphId);
            var heightDU = glyphProvider.GetGlyphHeight(ctx.fontId, glyphId);
            var depthDU = glyphProvider.GetGlyphDepth(ctx.fontId, glyphId);
            var italicDU = glyphProvider.GetItalicCorrection(ctx.fontId, glyphId);

            var advance = ctx.ToPixels(advanceDU);
            var height = ctx.ToPixels(heightDU);
            var depth = ctx.ToPixels(depthDU);
            var italic = ctx.ToPixels(italicDU);

            var box = MathBox.CreateGlyph(
                (int)glyphId, ctx.fontId, ctx.fontSize,
                advance, height, depth, italic);

            return buffer.Add(box);
        }

        /// <summary>
        /// Lays out a group of nodes as a horizontal list (HBox).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Implements TeX's conversion of a math list to a horizontal list:
        /// <list type="number">
        /// <item>Layout each child node to get its box.</item>
        /// <item>Determine the effective atom types, applying Bin-to-Ord conversion (TeXbook Rule 5).</item>
        /// <item>Insert inter-atom spacing from the spacing table (TeXbook Chapter 18, p.170).</item>
        /// <item>Build an HBox with summed width and max height/depth.</item>
        /// </list>
        /// </para>
        /// <para>
        /// Bin-to-Ord conversion: a Bin atom becomes Ord when preceded by Bin, Op, Rel, Open,
        /// Punct, or nothing; or when followed by Rel, Close, Punct, or nothing.
        /// </para>
        /// </remarks>
        private int LayoutGroup(ref MathNodeList nodes, ref MathNode node,
            ref MathLayoutContext ctx)
        {
            var childCount = node.childCount;
            if (childCount == 0)
                return AddEmptyKern();

            var childStart = node.childStart;
            var groupCtx = ctx;
            const int pairStride = 4;

            var savedChildCount = childIndices.count;
            childIndices.EnsureCapacity(savedChildCount + childCount * pairStride);

            for (var i = 0; i < childCount; i++)
            {
                var childNodeIndex = childStart + i;
                ref var childNode = ref nodes[childNodeIndex];

                var effectiveNodeIndex = childNode.type == MathNodeType.Reference
                    ? childNode.child0
                    : childNodeIndex;
                ref var effectiveNode = ref nodes[effectiveNodeIndex];
                if (effectiveNode.type == MathNodeType.StyleChange)
                {
                    groupCtx.style = effectiveNode.TargetStyle;
                    groupCtx.ApplyScaledFontSize(baseFontSize);
                    continue;
                }

                var boxIndex = LayoutNode(ref nodes, childNodeIndex, ref groupCtx);
                var atomType = MathNodeList.GetAtomType(ref childNode);

                childIndices.Add(boxIndex);
                childIndices.Add(atomType);
                childIndices.Add(BitConverter.SingleToInt32Bits(groupCtx.EmSize));
                childIndices.Add(MathStyleUtil.SizeLevel(groupCtx.style) > 0 ? 1 : 0);
            }

            var pairCount = (childIndices.count - savedChildCount) / pairStride;
            if (pairCount == 0)
            {
                childIndices.count = savedChildCount;
                return AddEmptyKern();
            }

            if (pairCount == 1)
            {
                var singleIndex = childIndices[savedChildCount];
                childIndices.count = savedChildCount;
                return singleIndex;
            }

            for (var i = 0; i < pairCount; i++)
            {
                var typeSlot = savedChildCount + i * pairStride + 1;
                var atomType = childIndices[typeSlot];

                if (atomType == (int)MathAtomType.Bin)
                {
                    var prevType = FindAtomType(i - 1, -1, savedChildCount, pairCount, pairStride);
                    if (MathSpacing.ShouldBinBecomeOrd(prevType))
                    {
                        childIndices[typeSlot] = (int)MathAtomType.Ord;
                        continue;
                    }

                    var nextType = FindAtomType(i + 1, 1, savedChildCount, pairCount, pairStride);
                    if (MathSpacing.ShouldBinBecomeOrdRight(nextType))
                        childIndices[typeSlot] = (int)MathAtomType.Ord;
                }
            }

            var hboxChildStart = buffer.Count;
            float totalWidth = 0f;
            float maxHeight = 0f;
            float maxDepth = 0f;

            var previousType = -1;
            for (var i = 0; i < pairCount; i++)
            {
                var pairStart = savedChildCount + i * pairStride;
                var currentType = childIndices[pairStart + 1];
                if (previousType >= 0 && currentType >= 0)
                {
                    var emSize = BitConverter.Int32BitsToSingle(childIndices[pairStart + 2]);
                    var isScript = childIndices[pairStart + 3] != 0;
                    var spacingPx = MathSpacing.GetSpacingPx((MathAtomType)previousType,
                        (MathAtomType)currentType, isScript, emSize);
                    if (spacingPx > 0f)
                    {
                        buffer.Add(MathBox.CreateKern(spacingPx));
                        totalWidth += spacingPx;
                    }
                }

                var srcBoxIndex = childIndices[pairStart];
                CopyBoxToEnd(srcBoxIndex);

                ref var childBox = ref buffer[buffer.Count - 1];
                totalWidth += childBox.width;
                var ch = childBox.height - childBox.shift;
                var cd = childBox.depth + childBox.shift;
                if (ch > maxHeight) maxHeight = ch;
                if (cd > maxDepth) maxDepth = cd;
                if (currentType >= 0)
                    previousType = currentType;
            }

            childIndices.count = savedChildCount;

            var hboxChildCount = buffer.Count - hboxChildStart;

            if (hboxChildCount == 1)
                return hboxChildStart;

            var hbox = MathBox.CreateHBox();
            hbox.firstChild = hboxChildStart;
            hbox.childCount = hboxChildCount;
            hbox.width = totalWidth;
            hbox.height = maxHeight;
            hbox.depth = maxDepth;

            return buffer.Add(hbox);
        }

        /// <summary>
        /// Lays out a fraction (<c>\frac</c> or <c>\binom</c>) as a VBox.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Implements TeX Appendix G Rule 15 with OpenType MATH constants:
        /// </para>
        /// <para>
        /// <b>With fraction bar (Rule 15a-d, \frac):</b>
        /// <list type="number">
        /// <item>Layout numerator in NumeratorStyle, denominator in DenominatorStyle.</item>
        /// <item>Get rule thickness from fractionRuleThickness.</item>
        /// <item>Center the bar on the math axis (axisHeight).</item>
        /// <item>Position numerator: shift up by fractionNumeratorShiftUp (display variant in display style).</item>
        /// <item>Position denominator: shift down by fractionDenominatorShiftDown.</item>
        /// <item>Enforce minimum gaps: fractionNumeratorGapMin (display: fractionNumDisplayStyleGapMin)
        ///        between numerator bottom ink and bar top ink;
        ///        fractionDenominatorGapMin (display: fractionDenomDisplayStyleGapMin)
        ///        between bar bottom ink and denominator top ink.</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>Without fraction bar (Rule 15e, <c>\binom</c>):</b>
        /// <list type="number">
        /// <item>Use stackTopShiftUp/stackBottomShiftDown (display variants in display style).</item>
        /// <item>Enforce stackGapMin (display: stackDisplayStyleGapMin) between ink edges.</item>
        /// </list>
        /// </para>
        /// <para>
        /// The result is an HBox wrapping a VBox to properly center the fraction on the axis.
        /// Width is max(numerator, denominator) width.
        /// </para>
        /// </remarks>
        private int LayoutFraction(ref MathNodeList nodes, ref MathNode node,
            ref MathLayoutContext ctx)
        {
            ref readonly var m = ref ctx.metrics;
            var isDisplay = ctx.IsDisplay;

            var numCtx = ctx;
            numCtx.style = ctx.NumeratorStyle();
            numCtx.ApplyScaledFontSize(baseFontSize);
            var numIndex = LayoutNode(ref nodes, node.child0, ref numCtx);

            var denCtx = ctx;
            denCtx.style = ctx.DenominatorStyle();
            denCtx.ApplyScaledFontSize(baseFontSize);
            var denIndex = LayoutNode(ref nodes, node.child1, ref denCtx);

            var numWidth = buffer[numIndex].width;
            var numHeight = buffer[numIndex].height;
            var numDepth = buffer[numIndex].depth;
            var denWidth = buffer[denIndex].width;
            var denHeight = buffer[denIndex].height;
            var denDepth = buffer[denIndex].depth;

            var fracWidth = Math.Max(numWidth, denWidth);

            var axisHeight = ctx.ToPixels(m.axisHeight);
            var hasRule = node.HasRule;

            float numShiftUp, denShiftDown;

            if (hasRule)
            {
                var ruleThicknessPx = ctx.ToPixels(m.fractionRuleThickness);

                numShiftUp = ctx.ToPixels(isDisplay
                    ? m.fractionNumeratorDisplayStyleShiftUp
                    : m.fractionNumeratorShiftUp);

                denShiftDown = ctx.ToPixels(isDisplay
                    ? m.fractionDenominatorDisplayStyleShiftDown
                    : m.fractionDenominatorShiftDown);

                var numGapMin = ctx.ToPixels(isDisplay
                    ? m.fractionNumDisplayStyleGapMin
                    : m.fractionNumeratorGapMin);

                var denGapMin = ctx.ToPixels(isDisplay
                    ? m.fractionDenomDisplayStyleGapMin
                    : m.fractionDenominatorGapMin);

                var barTopY = axisHeight + ruleThicknessPx * 0.5f;
                var barBottomY = axisHeight - ruleThicknessPx * 0.5f;

                var numGap = (numShiftUp - numDepth) - barTopY;
                if (numGap < numGapMin)
                    numShiftUp += numGapMin - numGap;

                var denGap = barBottomY - (denHeight - denShiftDown);
                if (denGap < denGapMin)
                    denShiftDown += denGapMin - denGap;

                var actualNumGap = (numShiftUp - numDepth) - barTopY;
                var actualDenGap = barBottomY - (denHeight - denShiftDown);

                var numCentered = CopyBoxCentered(numIndex, fracWidth, numWidth);
                var kernGap1 = buffer.Add(MathBox.CreateKern(actualNumGap));
                var rule = buffer.Add(MathBox.CreateRule(fracWidth, ruleThicknessPx * 0.5f,
                    ruleThicknessPx * 0.5f, ruleThicknessPx));
                var kernGap2 = buffer.Add(MathBox.CreateKern(actualDenGap));
                var denCentered = CopyBoxCentered(denIndex, fracWidth, denWidth);

                var vboxChildStart = buffer.Count;
                CopyBoxToEnd(numCentered);
                CopyBoxToEnd(kernGap1);
                CopyBoxToEnd(rule);
                CopyBoxToEnd(kernGap2);
                CopyBoxToEnd(denCentered);
                var vboxChildCount = 5;

                var vboxHeight = numShiftUp + numHeight;
                var vboxDepth = denShiftDown + denDepth;

                var vbox = MathBox.CreateVBox();
                vbox.firstChild = vboxChildStart;
                vbox.childCount = vboxChildCount;
                vbox.width = fracWidth;
                vbox.height = vboxHeight;
                vbox.depth = vboxDepth;

                return buffer.Add(vbox);
            }
            else
            {
                numShiftUp = ctx.ToPixels(isDisplay
                    ? m.stackTopDisplayStyleShiftUp
                    : m.stackTopShiftUp);

                denShiftDown = ctx.ToPixels(isDisplay
                    ? m.stackBottomDisplayStyleShiftDown
                    : m.stackBottomShiftDown);

                var gapMin = ctx.ToPixels(isDisplay
                    ? m.stackDisplayStyleGapMin
                    : m.stackGapMin);

                var gap = (numShiftUp - numDepth) - (denHeight - denShiftDown);
                if (gap < gapMin)
                {
                    var deficit = gapMin - gap;
                    numShiftUp += deficit * 0.5f;
                    denShiftDown += deficit * 0.5f;
                }

                var actualGap = (numShiftUp - numDepth) - (denHeight - denShiftDown);

                var numCentered = CopyBoxCentered(numIndex, fracWidth, numWidth);
                var kernGap = buffer.Add(MathBox.CreateKern(actualGap));
                var denCentered = CopyBoxCentered(denIndex, fracWidth, denWidth);

                var vboxChildStart = buffer.Count;
                CopyBoxToEnd(numCentered);
                CopyBoxToEnd(kernGap);
                CopyBoxToEnd(denCentered);
                var vboxChildCount = 3;

                var vboxHeight = numShiftUp + numHeight;
                var vboxDepth = denShiftDown + denDepth;

                var vbox = MathBox.CreateVBox();
                vbox.firstChild = vboxChildStart;
                vbox.childCount = vboxChildCount;
                vbox.width = fracWidth;
                vbox.height = vboxHeight;
                vbox.depth = vboxDepth;

                return buffer.Add(vbox);
            }
        }

        /// <summary>
        /// Lays out superscript and/or subscript attached to a base.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Implements TeX Appendix G Rule 18 with OpenType MATH constants:
        /// </para>
        /// <para>
        /// <b>Superscript only:</b>
        /// <list type="number">
        /// <item>Layout base, then superscript in SuperscriptStyle.</item>
        /// <item>Set supShift = superscriptShiftUp (or superscriptShiftUpCramped if cramped).</item>
        /// <item>Enforce superscriptBottomMin: bottom of superscript ink must be at least this high.</item>
        /// <item>Enforce superscriptBaselineDropMax: sup baseline must not drop below base top by more than this.</item>
        /// <item>Add italic correction of base to superscript horizontal position.</item>
        /// <item>Add spaceAfterScript kern.</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>Subscript only:</b>
        /// <list type="number">
        /// <item>Layout base, then subscript in SubscriptStyle.</item>
        /// <item>Set subShift = subscriptShiftDown.</item>
        /// <item>Enforce subscriptTopMax: top of subscript ink must not exceed this above baseline.</item>
        /// <item>Enforce subscriptBaselineDropMin: sub baseline must drop at least this below base bottom.</item>
        /// <item>Add spaceAfterScript kern.</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>Both superscript and subscript:</b>
        /// <list type="number">
        /// <item>Compute individual shifts as above.</item>
        /// <item>Enforce subSuperscriptGapMin between super bottom ink and sub top ink.</item>
        /// <item>Enforce superscriptBottomMaxWithSubscript: if super bottom exceeds this,
        ///        push subscript down instead of raising superscript further.</item>
        /// </list>
        /// </para>
        /// </remarks>
        private int LayoutScript(ref MathNodeList nodes, ref MathNode node,
            ref MathLayoutContext ctx)
        {
            ref readonly var m = ref ctx.metrics;

            var baseIndex = LayoutNode(ref nodes, node.child0, ref ctx);
            var baseHeight = buffer[baseIndex].height;
            var baseDepth = buffer[baseIndex].depth;
            var baseItalic = buffer[baseIndex].italic;
            var baseIsBoxOrExtendedShape = IsBoxOrExtendedShape(baseIndex, ctx.fontId);

            var hasSup = node.child1 != MathNode.None;
            var hasSub = node.child2 != MathNode.None;

            int supIndex = -1;
            var supCtx = ctx;
            float supHeight = 0f, supDepth = 0f;
            if (hasSup)
            {
                supCtx.style = ctx.SuperscriptStyle();
                supCtx.ApplyScaledFontSize(baseFontSize);
                supIndex = LayoutNode(ref nodes, node.child1, ref supCtx);
                supHeight = buffer[supIndex].height;
                supDepth = buffer[supIndex].depth;
            }

            int subIndex = -1;
            var subCtx = ctx;
            float subHeight = 0f, subDepth = 0f;
            if (hasSub)
            {
                subCtx.style = ctx.SubscriptStyle();
                subCtx.ApplyScaledFontSize(baseFontSize);
                subIndex = LayoutNode(ref nodes, node.child2, ref subCtx);
                subHeight = buffer[subIndex].height;
                subDepth = buffer[subIndex].depth;
            }

            ref var baseNode = ref nodes[node.child0];
            if (baseNode.type == MathNodeType.Operator
                && (baseNode.LimitPlacement == MathLimitPlacement.OverUnder
                    || ctx.IsDisplay && baseNode.LimitPlacement == MathLimitPlacement.Display))
                return BuildLimitsVBox(baseIndex, supIndex, subIndex, ref ctx);

            var spaceAfterScript = ctx.ToPixels(m.spaceAfterScript);

            if (hasSup && !hasSub)
            {
                var supShift = ctx.ToPixels(ctx.IsCramped
                    ? m.superscriptShiftUpCramped
                    : m.superscriptShiftUp);

                var supBottomMin = ctx.ToPixels(m.superscriptBottomMin);
                var supBottom = supShift - supDepth;
                if (supBottom < supBottomMin)
                    supShift += supBottomMin - supBottom;

                if (baseIsBoxOrExtendedShape)
                {
                    var dropMax = ctx.ToPixels(m.superscriptBaselineDropMax);
                    var drop = baseHeight - supShift;
                    if (drop > dropMax)
                        supShift = baseHeight - dropMax;
                }

                var mathKern = GetScriptKern(baseIndex, supIndex, supShift, true, ref ctx, ref supCtx);
                return BuildSupOnlyHBox(baseIndex, supIndex, supShift,
                    baseItalic + mathKern, spaceAfterScript);
            }

            if (hasSub && !hasSup)
            {
                var subShift = ctx.ToPixels(m.subscriptShiftDown);

                var subTopMax = ctx.ToPixels(m.subscriptTopMax);
                var subTop = subHeight - subShift;
                if (subTop > subTopMax)
                    subShift = subHeight - subTopMax;

                if (baseIsBoxOrExtendedShape)
                {
                    var dropMin = ctx.ToPixels(m.subscriptBaselineDropMin);
                    var dropActual = subShift - baseDepth;
                    if (dropActual < dropMin)
                        subShift = baseDepth + dropMin;
                }

                var mathKern = GetScriptKern(baseIndex, subIndex, subShift, false, ref ctx, ref subCtx);
                return BuildSubOnlyHBox(baseIndex, subIndex, subShift, mathKern, spaceAfterScript);
            }

            if (hasSup && hasSub)
            {
                var supShift = ctx.ToPixels(ctx.IsCramped
                    ? m.superscriptShiftUpCramped
                    : m.superscriptShiftUp);

                var subShift = ctx.ToPixels(m.subscriptShiftDown);

                if (baseIsBoxOrExtendedShape)
                {
                    var dropMax = ctx.ToPixels(m.superscriptBaselineDropMax);
                    supShift = Math.Max(supShift, baseHeight - dropMax);

                    var dropMin = ctx.ToPixels(m.subscriptBaselineDropMin);
                    subShift = Math.Max(subShift, baseDepth + dropMin);
                }

                var supBottomMin = ctx.ToPixels(m.superscriptBottomMin);
                var supBottom = supShift - supDepth;
                if (supBottom < supBottomMin)
                    supShift += supBottomMin - supBottom;

                var subTopMax = ctx.ToPixels(m.subscriptTopMax);
                var subTop = subHeight - subShift;
                if (subTop > subTopMax)
                    subShift = subHeight - subTopMax;

                var gap = (supShift - supDepth) - (subHeight - subShift);
                var gapMin = ctx.ToPixels(m.subSuperscriptGapMin);

                if (gap < gapMin)
                {
                    var supBottomMaxWithSub = ctx.ToPixels(m.superscriptBottomMaxWithSubscript);
                    var currentSupBottom = supShift - supDepth;
                    var raise = Math.Min(gapMin - gap,
                        Math.Max(0f, supBottomMaxWithSub - currentSupBottom));
                    supShift += raise;
                    gap += raise;

                    if (gap < gapMin)
                        subShift += gapMin - gap;
                }

                var supKern = GetScriptKern(baseIndex, supIndex, supShift, true, ref ctx, ref supCtx);
                var subKern = GetScriptKern(baseIndex, subIndex, subShift, false, ref ctx, ref subCtx);
                return BuildSupSubHBox(baseIndex, supIndex, subIndex,
                    supShift, subShift, baseItalic + supKern, subKern, spaceAfterScript);
            }

            return baseIndex;
        }

        private bool IsBoxOrExtendedShape(int boxIndex, int mathFontId)
        {
            ref var box = ref buffer[boxIndex];
            if (box.type == MathBoxType.Glyph)
                return box.fontId != mathFontId
                       || glyphProvider.IsGlyphExtendedShape(box.fontId, (uint)box.glyphId);
            if (box.type == MathBoxType.Delimiter && box.delimiterGlyphId >= 0)
                return glyphProvider.IsGlyphExtendedShape(box.delimiterFontId,
                    (uint)box.delimiterGlyphId);
            return true;
        }

        private float GetScriptKern(int baseIndex, int scriptIndex, float shift, bool superscript,
            ref MathLayoutContext baseCtx, ref MathLayoutContext scriptCtx)
        {
            ref var baseBox = ref buffer[baseIndex];
            ref var scriptBox = ref buffer[scriptIndex];
            if (!TryGetKernGlyph(ref baseBox, baseCtx.fontId,
                    out var baseGlyph, out var baseBaselineOffset)
                || !TryGetKernGlyph(ref scriptBox, scriptCtx.fontId,
                    out var scriptGlyph, out var scriptBaselineOffset))
                return 0f;

            if (superscript)
            {
                var first = MathKernPair(baseCtx.fontId, baseGlyph, HB.MathKern.TopRight,
                    shift - scriptBox.depth - baseBaselineOffset, baseCtx.pixelScale,
                    scriptGlyph, HB.MathKern.BottomLeft,
                    -scriptBox.depth - scriptBaselineOffset, scriptCtx.pixelScale);
                var second = MathKernPair(baseCtx.fontId, baseGlyph, HB.MathKern.TopRight,
                    baseBox.height - baseBaselineOffset, baseCtx.pixelScale,
                    scriptGlyph, HB.MathKern.BottomLeft,
                    baseBox.height - shift - scriptBaselineOffset, scriptCtx.pixelScale);
                return Math.Min(first, second);
            }

            var top = MathKernPair(baseCtx.fontId, baseGlyph, HB.MathKern.BottomRight,
                scriptBox.height - shift - baseBaselineOffset, baseCtx.pixelScale,
                scriptGlyph, HB.MathKern.TopLeft,
                scriptBox.height - scriptBaselineOffset, scriptCtx.pixelScale);
            var bottom = MathKernPair(baseCtx.fontId, baseGlyph, HB.MathKern.BottomRight,
                -baseBox.depth - baseBaselineOffset, baseCtx.pixelScale,
                scriptGlyph, HB.MathKern.TopLeft,
                shift - baseBox.depth - scriptBaselineOffset, scriptCtx.pixelScale);
            return Math.Min(top, bottom);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetKernGlyph(ref MathBox box, int fontId,
            out uint glyph, out float baselineOffset)
        {
            if (box.type == MathBoxType.Glyph && box.fontId == fontId)
            {
                glyph = (uint)box.glyphId;
                baselineOffset = 0f;
                return true;
            }

            if (box.type == MathBoxType.Delimiter && box.delimiterGlyphId >= 0
                && box.delimiterFontId == fontId)
            {
                glyph = (uint)box.delimiterGlyphId;
                baselineOffset = box.delimiterBaselineOffset;
                return true;
            }

            glyph = 0;
            baselineOffset = 0f;
            return false;
        }

        private float MathKernPair(int fontId, uint baseGlyph, HB.MathKern baseCorner,
            float baseHeight, float baseScale, uint scriptGlyph, HB.MathKern scriptCorner,
            float scriptHeight, float scriptScale)
        {
            var baseCorrection = baseScale > 0f ? (int)Math.Round(baseHeight / baseScale) : 0;
            var scriptCorrection = scriptScale > 0f ? (int)Math.Round(scriptHeight / scriptScale) : 0;
            return glyphProvider.GetGlyphKerning(fontId, baseGlyph, baseCorner, baseCorrection) * baseScale
                   + glyphProvider.GetGlyphKerning(fontId, scriptGlyph, scriptCorner, scriptCorrection) * scriptScale;
        }

        /// <summary>
        /// Lays out a radical (square root or n-th root).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Implements TeX Appendix G Rule 11 with OpenType MATH constants:
        /// </para>
        /// <list type="number">
        /// <item>Layout body in CrampedStyle.</item>
        /// <item>Get radicalVerticalGap (display variant in display style).</item>
        /// <item>Get radicalRuleThickness and radicalExtraAscender.</item>
        /// <item>Compute the required height for the radical sign: body height + gap + rule thickness.</item>
        /// <item>Select a radical variant or construct one from its MATH assembly.</item>
        /// <item>If degree present: layout in ScriptScript style, position with
        ///        radicalKernBeforeDegree, radicalKernAfterDegree, radicalDegreeBottomRaisePercent.</item>
        /// </list>
        /// </remarks>
        private int LayoutRadical(ref MathNodeList nodes, ref MathNode node,
            ref MathLayoutContext ctx)
        {
            ref readonly var m = ref ctx.metrics;

            var bodyCtx = ctx;
            bodyCtx.style = ctx.CrampedStyle();
            var bodyIndex = LayoutNode(ref nodes, node.child0, ref bodyCtx);

            var bodyWidth = buffer[bodyIndex].width;
            var bodyHeight = buffer[bodyIndex].height;
            var bodyDepth = buffer[bodyIndex].depth;

            var verticalGap = ctx.ToPixels(ctx.IsDisplay
                ? m.radicalDisplayStyleVerticalGap
                : m.radicalVerticalGap);
            var ruleThickness = ctx.ToPixels(m.radicalRuleThickness);
            var extraAscender = ctx.ToPixels(m.radicalExtraAscender);

            var innerHeight = bodyHeight + verticalGap;
            var totalHeight = innerHeight + ruleThickness + extraAscender;
            var totalDepth = bodyDepth;

            var vboxChildStart = buffer.Count;

            buffer.Add(MathBox.CreateKern(extraAscender));
            buffer.Add(MathBox.CreateRule(bodyWidth, ruleThickness, 0f, ruleThickness));
            buffer.Add(MathBox.CreateKern(verticalGap));
            CopyBoxToEnd(bodyIndex);

            var vboxChildCount = buffer.Count - vboxChildStart;

            var vbox = MathBox.CreateVBox();
            vbox.firstChild = vboxChildStart;
            vbox.childCount = vboxChildCount;
            vbox.width = bodyWidth;
            vbox.height = totalHeight;
            vbox.depth = totalDepth;

            var vboxIndex = buffer.Add(vbox);

            var surdTargetHeight = innerHeight + ruleThickness + totalDepth;
            var delimBuilder = new MathDelimiterBuilder();
            var surdIndex = delimBuilder.BuildVerticalDelimiter(
                0x221A, surdTargetHeight, ref buffer, ref ctx, glyphProvider,
                alignToAxis: false);
            if (surdIndex < 0)
                throw new InvalidOperationException(
                    "Math font does not contain radical glyph U+221A.");

            var overbarTop = totalHeight - extraAscender;
            buffer[surdIndex].shift = buffer[surdIndex].height - overbarTop;

            var radHboxStart = buffer.Count;
            buffer.Add(buffer[surdIndex]);
            CopyBoxToEnd(vboxIndex);
            vboxIndex = AddMeasuredHBox(radHboxStart);

            if (node.child1 != MathNode.None)
            {
                var degCtx = ctx;
                degCtx.style = MathStyle.ScriptScript;
                degCtx.ApplyScaledFontSize(baseFontSize);
                var degIndex = LayoutNode(ref nodes, node.child1, ref degCtx);

                var degDepth = buffer[degIndex].depth;

                var kernBefore = Math.Max(0f, ctx.ToPixels(m.radicalKernBeforeDegree));
                var kernAfter = Math.Max(-buffer[degIndex].width,
                    ctx.ToPixels(m.radicalKernAfterDegree));

                ref var radicalBox = ref buffer[vboxIndex];
                var radicalBlockSize = radicalBox.height + radicalBox.depth;
                var degShift = radicalBlockSize * m.radicalDegreeBottomRaisePercent / 100f
                               - radicalBox.depth + degDepth;

                var hboxChildStart = buffer.Count;

                if (kernBefore > 0.001f)
                    buffer.Add(MathBox.CreateKern(kernBefore));

                CopyBoxToEnd(degIndex);
                buffer[buffer.Count - 1].shift = -degShift;

                if (Math.Abs(kernAfter) > 0.001f)
                    buffer.Add(MathBox.CreateKern(kernAfter));

                CopyBoxToEnd(vboxIndex);
                return AddMeasuredHBox(hboxChildStart);
            }

            return vboxIndex;
        }

        private int LayoutOperator(ref MathNode node, ref MathLayoutContext ctx)
        {
            if (node.codepoint == 0 || (node.commandLength > 1 && node.codepoint <= 0x7F))
                return LayoutTextSpan(node.commandStart, node.commandLength, ref ctx, true);

            if (ctx.IsDisplay && node.IsLargeOperator)
            {
                var target = ctx.ToPixels(ctx.metrics.displayOperatorMinHeight);
                var builder = new MathDelimiterBuilder();
                var variant = builder.BuildVerticalDelimiter(
                    node.codepoint, target, ref buffer, ref ctx, glyphProvider,
                    alignToAxis: false, allowAssembly: false);
                if (variant < 0)
                    throw new InvalidOperationException(
                        $"Math font does not contain display operator U+{node.codepoint:X4}.");
                return variant;
            }

            return LayoutAtom(ref node, ref ctx);
        }

        /// <summary>
        /// Builds a VBox with an operator and its limits above/below (display-style limit placement).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Uses OpenType MATH constants:
        /// <list type="bullet">
        /// <item>upperLimitGapMin: minimum gap between upper limit bottom ink and operator top ink.</item>
        /// <item>upperLimitBaselineRiseMin: minimum distance from upper limit baseline to operator top.</item>
        /// <item>lowerLimitGapMin: minimum gap between lower limit top ink and operator bottom ink.</item>
        /// <item>lowerLimitBaselineDropMin: minimum distance from lower limit baseline to operator bottom.</item>
        /// </list>
        /// </para>
        /// <para>
        /// The italic correction of the operator is used to horizontally offset the limits:
        /// upper limit is shifted right by italic/2, lower limit shifted left by italic/2.
        /// This compensates for the slant of integral signs and similar operators.
        /// </para>
        /// </remarks>
        private int BuildLimitsVBox(int opIndex, int upperIndex, int lowerIndex,
            ref MathLayoutContext ctx)
        {
            ref readonly var m = ref ctx.metrics;

            var opWidth = buffer[opIndex].width;
            var opHeight = buffer[opIndex].height;
            var opDepth = buffer[opIndex].depth;
            var opItalic = buffer[opIndex].italic;

            float upperGap = 0f;
            float upperH = 0f, upperD = 0f;
            float lowerGap = 0f;
            float lowerH = 0f, lowerD = 0f;

            if (upperIndex >= 0)
            {
                upperH = buffer[upperIndex].height;
                upperD = buffer[upperIndex].depth;
                var gapMin = ctx.ToPixels(m.upperLimitGapMin);
                var riseMin = ctx.ToPixels(m.upperLimitBaselineRiseMin);

                upperGap = gapMin;
                var riseFromGap = upperGap + upperD;
                if (riseFromGap < riseMin)
                    upperGap = riseMin - upperD;
            }

            if (lowerIndex >= 0)
            {
                lowerH = buffer[lowerIndex].height;
                lowerD = buffer[lowerIndex].depth;
                var gapMin = ctx.ToPixels(m.lowerLimitGapMin);
                var dropMin = ctx.ToPixels(m.lowerLimitBaselineDropMin);

                lowerGap = gapMin;
                var dropFromGap = lowerGap + lowerH;
                if (dropFromGap < dropMin)
                    lowerGap = dropMin - lowerH;
            }

            var upperOffset = upperIndex >= 0
                ? (opWidth - buffer[upperIndex].width + opItalic) * 0.5f
                : 0f;
            var lowerOffset = lowerIndex >= 0
                ? (opWidth - buffer[lowerIndex].width - opItalic) * 0.5f
                : 0f;
            var minX = Math.Min(0f, Math.Min(upperOffset, lowerOffset));
            var maxX = opWidth;
            if (upperIndex >= 0)
                maxX = Math.Max(maxX, upperOffset + buffer[upperIndex].width);
            if (lowerIndex >= 0)
                maxX = Math.Max(maxX, lowerOffset + buffer[lowerIndex].width);
            var totalWidth = maxX - minX;

            var upperCentered = upperIndex >= 0
                ? CopyBoxAt(upperIndex, totalWidth, upperOffset - minX)
                : -1;
            var opCentered = CopyBoxAt(opIndex, totalWidth, -minX);
            var lowerCentered = lowerIndex >= 0
                ? CopyBoxAt(lowerIndex, totalWidth, lowerOffset - minX)
                : -1;

            var vboxChildStart = buffer.Count;
            float vboxHeight = 0f;
            float vboxDepth = 0f;

            if (upperIndex >= 0)
            {
                CopyBoxToEnd(upperCentered);
                vboxHeight += upperH + upperD;

                buffer.Add(MathBox.CreateKern(upperGap));
                vboxHeight += upperGap;
            }

            CopyBoxToEnd(opCentered);
            vboxHeight += opHeight;
            vboxDepth = opDepth;

            if (lowerIndex >= 0)
            {
                buffer.Add(MathBox.CreateKern(lowerGap));
                vboxDepth += lowerGap;

                CopyBoxToEnd(lowerCentered);
                vboxDepth += lowerH + lowerD;
            }

            var vboxChildCount = buffer.Count - vboxChildStart;

            var vbox = MathBox.CreateVBox();
            vbox.firstChild = vboxChildStart;
            vbox.childCount = vboxChildCount;
            vbox.width = totalWidth;
            vbox.height = vboxHeight;
            vbox.depth = vboxDepth;

            return buffer.Add(vbox);
        }

        /// <summary>
        /// Lays out a math accent above its base.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Implements TeX Appendix G Rule 12:
        /// </para>
        /// <list type="number">
        /// <item>Layout the base in CrampedStyle.</item>
        /// <item>Layout the accent glyph.</item>
        /// <item>If base height &gt; accentBaseHeight, raise the accent by (base height - accentBaseHeight).</item>
        /// <item>Horizontally center the accent over the base using top accent attachment points
        ///        from the MATH table.</item>
        /// </list>
        /// </remarks>
        private int LayoutAccent(ref MathNodeList nodes, ref MathNode node,
            ref MathLayoutContext ctx)
        {
            ref readonly var m = ref ctx.metrics;

            var baseCtx = ctx;
            baseCtx.style = ctx.CrampedStyle();
            var baseNode = nodes[node.child0];
            int baseIndex;
            if (baseNode.type == MathNodeType.Atom)
                baseIndex = LayoutAtom(ref baseNode, ref baseCtx, MathGlyphForm.Dotless);
            else
                baseIndex = LayoutNode(ref nodes, node.child0, ref baseCtx);

            var baseW = buffer[baseIndex].width;
            var baseH = buffer[baseIndex].height;
            var baseD = buffer[baseIndex].depth;

            var accentCp = node.codepoint;
            if (accentCp <= 0)
                return baseIndex;

            var flattenedAccentBaseHeight = ctx.ToPixels(m.flattenedAccentBaseHeight);
            var accentForm = baseH > flattenedAccentBaseHeight
                ? MathGlyphForm.FlattenedAccent
                : MathGlyphForm.Default;
            var builder = new MathDelimiterBuilder();
            var accentIndex = builder.BuildHorizontalDelimiter(
                accentCp, baseW, ref buffer, ref ctx, glyphProvider, accentForm);
            if (accentIndex < 0)
                throw new InvalidOperationException($"Math font does not contain accent U+{accentCp:X4}.");

            var accentAdv = buffer[accentIndex].width;
            var accentHeight = buffer[accentIndex].height;
            var accentDepth = buffer[accentIndex].depth;

            var accentBaseHeight = ctx.ToPixels(m.accentBaseHeight);
            var baseOverlap = Math.Min(baseH, accentBaseHeight);
            var overlapKern = -(accentDepth + baseOverlap);

            var accentOffset = (baseW - accentAdv) * 0.5f;
            if (buffer[baseIndex].type == MathBoxType.Glyph
                && buffer[baseIndex].fontId == ctx.fontId
                && buffer[accentIndex].type == MathBoxType.Delimiter
                && buffer[accentIndex].delimiterGlyphId >= 0)
            {
                var baseAttachment = ctx.ToPixels(glyphProvider.GetTopAccentAttachment(
                    ctx.fontId, (uint)buffer[baseIndex].glyphId));
                var accentAttachment = ctx.ToPixels(glyphProvider.GetTopAccentAttachment(
                    ctx.fontId, (uint)buffer[accentIndex].delimiterGlyphId));
                accentOffset = baseAttachment - accentAttachment;
            }

            var minX = Math.Min(0f, accentOffset);
            var maxX = Math.Max(baseW, accentOffset + accentAdv);
            var totalWidth = maxX - minX;
            var positionedAccent = CopyBoxAt(accentIndex, totalWidth, accentOffset - minX);
            var positionedBase = CopyBoxAt(baseIndex, totalWidth, -minX);

            var accentBaseline = baseH - baseOverlap;
            var totalHeight = Math.Max(baseH, accentBaseline + accentHeight);
            var topKern = totalHeight - accentBaseline - accentHeight;

            var vboxChildStart = buffer.Count;
            if (topKern > 0.001f)
                buffer.Add(MathBox.CreateKern(topKern));
            CopyBoxToEnd(positionedAccent);
            buffer.Add(MathBox.CreateKern(overlapKern));
            CopyBoxToEnd(positionedBase);

            var vboxChildCount = buffer.Count - vboxChildStart;

            var totalDepth = baseD;

            var vbox = MathBox.CreateVBox();
            vbox.firstChild = vboxChildStart;
            vbox.childCount = vboxChildCount;
            vbox.width = totalWidth;
            vbox.height = totalHeight;
            vbox.depth = totalDepth;

            return buffer.Add(vbox);
        }

        /// <summary>Lays out an OpenType MATH overbar or underbar around a cramped base.</summary>
        private int LayoutBar(ref MathNodeList nodes, ref MathNode node,
            ref MathLayoutContext ctx)
        {
            var baseCtx = ctx;
            baseCtx.style = ctx.CrampedStyle();
            var baseIndex = LayoutNode(ref nodes, node.child0, ref baseCtx);
            var baseWidth = buffer[baseIndex].width;
            var baseHeight = buffer[baseIndex].height;
            var baseDepth = buffer[baseIndex].depth;

            ref readonly var metrics = ref ctx.metrics;
            var underbar = node.IsUnderbar;
            var gap = ctx.ToPixels(underbar
                ? metrics.underbarVerticalGap
                : metrics.overbarVerticalGap);
            var thickness = ctx.ToPixels(underbar
                ? metrics.underbarRuleThickness
                : metrics.overbarRuleThickness);
            var extra = ctx.ToPixels(underbar
                ? metrics.underbarExtraDescender
                : metrics.overbarExtraAscender);

            var childStart = buffer.Count;
            if (underbar)
            {
                CopyBoxToEnd(baseIndex);
                buffer.Add(MathBox.CreateKern(gap));
                buffer.Add(MathBox.CreateRule(baseWidth, thickness, 0f, thickness));
                buffer.Add(MathBox.CreateKern(extra));
            }
            else
            {
                buffer.Add(MathBox.CreateKern(extra));
                buffer.Add(MathBox.CreateRule(baseWidth, thickness, 0f, thickness));
                buffer.Add(MathBox.CreateKern(gap));
                CopyBoxToEnd(baseIndex);
            }

            var box = MathBox.CreateVBox();
            box.firstChild = childStart;
            box.childCount = 4;
            box.width = baseWidth;
            box.height = baseHeight + (underbar ? 0f : gap + thickness + extra);
            box.depth = baseDepth + (underbar ? gap + thickness + extra : 0f);
            return buffer.Add(box);
        }

        /// <summary>
        /// Lays out a delimited group (\left...\right).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Steps:
        /// <list type="number">
        /// <item>Layout the body.</item>
        /// <item>Determine target size for delimiters from body height + depth.</item>
        /// <item>Select variants or construct assemblies that cover the body extent.</item>
        /// <item>Build HBox: [left_delim] [body] [right_delim].</item>
        /// </list>
        /// </para>
        /// </remarks>
        private int LayoutDelimiter(ref MathNodeList nodes, ref MathNode node,
            ref MathLayoutContext ctx)
        {
            var bodyIndex = LayoutNode(ref nodes, node.child0, ref ctx);
            return WrapWithDelimiters(bodyIndex, node.codepoint, node.DelimiterRight, ref ctx);
        }

        /// <summary>Creates a vertical delimiter using the smallest sufficient or largest available construction.</summary>
        private int LayoutDelimiterGlyph(int codepoint, float targetSize, ref MathLayoutContext ctx)
        {
            var builder = new MathDelimiterBuilder();
            var index = builder.BuildVerticalDelimiter(
                codepoint, targetSize, ref buffer, ref ctx, glyphProvider);
            if (index < 0)
                throw new InvalidOperationException($"Math font does not contain delimiter U+{codepoint:X4}.");
            return index;
        }

        /// <summary>
        /// Lays out a matrix/array as a grid.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Steps:
        /// <list type="number">
        /// <item>Layout all cells.</item>
        /// <item>Compute column widths (max cell width per column).</item>
        /// <item>Compute row heights and depths (max per row).</item>
        /// <item>Build VBox of HBox rows. Each HBox contains cells with kerns for column spacing.</item>
        /// <item>Optionally wrap with delimiters based on MatrixDelimiterStyle.</item>
        /// </list>
        /// </para>
        /// </remarks>
        private int LayoutMatrix(ref MathNodeList nodes, ref MathNode node,
            ref MathLayoutContext ctx)
        {
            var cols = node.MatrixCols;
            var totalCells = node.childCount;
            var rows = cols > 0 ? totalCells / cols : 0;

            if (rows == 0 || cols == 0)
                return AddEmptyKern();

            var cellCtx = ctx;
            if (ctx.IsDisplay)
            {
                cellCtx.style = MathStyle.Text;
            }

            var cellBoxStart = childIndices.count;
            childIndices.EnsureCapacity(cellBoxStart + totalCells);
            for (var i = 0; i < totalCells; i++)
            {
                var cellNodeIndex = node.childStart + i;
                var currentCellCtx = cellCtx;
                var cellBoxIndex = LayoutNode(ref nodes, cellNodeIndex, ref currentCellCtx);
                childIndices.Add(cellBoxIndex);
            }

            var colWidthStart = childIndices.count;
            childIndices.EnsureCapacity(colWidthStart + cols);
            for (var c = 0; c < cols; c++)
                childIndices.Add(0);

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    var cellIdx = childIndices[cellBoxStart + r * cols + c];
                    ref var cellBox = ref buffer[cellIdx];
                    ref var colWidthSlot = ref childIndices[colWidthStart + c];
                    var colWidth = BitConverter.Int32BitsToSingle(colWidthSlot);
                    if (cellBox.width > colWidth)
                        colWidthSlot = BitConverter.SingleToInt32Bits(cellBox.width);
                }
            }

            var rowMetricsStart = childIndices.count;
            childIndices.EnsureCapacity(rowMetricsStart + rows * 2);
            for (var r = 0; r < rows; r++)
            {
                childIndices.Add(0);
                childIndices.Add(0);
            }

            for (var r = 0; r < rows; r++)
            {
                float maxH = 0f, maxD = 0f;
                for (var c = 0; c < cols; c++)
                {
                    var cellIdx = childIndices[cellBoxStart + r * cols + c];
                    ref var cellBox = ref buffer[cellIdx];
                    if (cellBox.height > maxH) maxH = cellBox.height;
                    if (cellBox.depth > maxD) maxD = cellBox.depth;
                }
                childIndices[rowMetricsStart + r * 2] = BitConverter.SingleToInt32Bits(maxH);
                childIndices[rowMetricsStart + r * 2 + 1] = BitConverter.SingleToInt32Bits(maxD);
            }

            var alignment = node.MatrixAlignment;
            var rowGap = ctx.EmSize * 0.2f;

            float totalWidth = 0f;

            for (var c = 0; c < cols; c++)
            {
                totalWidth += BitConverter.Int32BitsToSingle(childIndices[colWidthStart + c]);
                if (c < cols - 1) totalWidth += MatrixColumnGap(alignment, c, ctx.EmSize);
            }

            for (var r = 0; r < rows; r++)
            {
                var rowH = BitConverter.Int32BitsToSingle(childIndices[rowMetricsStart + r * 2]);
                var rowD = BitConverter.Int32BitsToSingle(childIndices[rowMetricsStart + r * 2 + 1]);

                var rowChildStart = buffer.Count;
                for (var c = 0; c < cols; c++)
                {
                    if (c > 0)
                        buffer.Add(MathBox.CreateKern(MatrixColumnGap(alignment, c - 1, ctx.EmSize)));

                    var cellIdx = childIndices[cellBoxStart + r * cols + c];
                    var cellW = buffer[cellIdx].width;
                    var colWidth = BitConverter.Int32BitsToSingle(childIndices[colWidthStart + c]);

                    var freeWidth = colWidth - cellW;
                    var leftPad = alignment switch
                    {
                        MatrixAlignmentStyle.Left => 0f,
                        MatrixAlignmentStyle.Alternating => (c & 1) == 0 ? freeWidth : 0f,
                        _ => freeWidth * 0.5f,
                    };
                    if (leftPad > 0.001f)
                        buffer.Add(MathBox.CreateKern(leftPad));

                    CopyBoxToEnd(cellIdx);

                    var rightPad = colWidth - cellW - leftPad;
                    if (rightPad > 0.001f)
                        buffer.Add(MathBox.CreateKern(rightPad));
                }

                var rowChildCount = buffer.Count - rowChildStart;

                var rowBox = MathBox.CreateHBox();
                rowBox.firstChild = rowChildStart;
                rowBox.childCount = rowChildCount;
                rowBox.width = totalWidth;
                rowBox.height = rowH;
                rowBox.depth = rowD;

                childIndices.Add(buffer.Add(rowBox));
            }

            var rowBoxesStart = rowMetricsStart + rows * 2;
            var vboxChildStart = buffer.Count;
            float vboxHeight = 0f;
            float vboxDepth = 0f;

            for (var r = 0; r < rows; r++)
            {
                if (r > 0)
                {
                    buffer.Add(MathBox.CreateKern(rowGap));
                    vboxHeight += rowGap;
                }

                var rowIndex = childIndices[rowBoxesStart + r];
                CopyBoxToEnd(rowIndex);
                ref var row = ref buffer[rowIndex];
                vboxHeight += row.height;
                if (r == rows - 1)
                    vboxDepth = row.depth;
                else
                    vboxHeight += row.depth;
            }

            var vboxChildCount = buffer.Count - vboxChildStart;

            var axisHeight = ctx.ToPixels(ctx.metrics.axisHeight);
            var totalH = vboxHeight + vboxDepth;

            var vbox = MathBox.CreateVBox();
            vbox.firstChild = vboxChildStart;
            vbox.childCount = vboxChildCount;
            vbox.width = totalWidth;
            vbox.height = Math.Max(0f, totalH * 0.5f + axisHeight);
            vbox.depth = Math.Max(0f, totalH * 0.5f - axisHeight);

            var matrixIndex = buffer.Add(vbox);

            childIndices.count = cellBoxStart;

            var delimStyle = node.MatrixDelimiter;
            if (delimStyle != MatrixDelimiterStyle.None)
            {
                int leftCp = 0, rightCp = 0;
                switch (delimStyle)
                {
                    case MatrixDelimiterStyle.Parentheses:
                        leftCp = '(';
                        rightCp = ')';
                        break;
                    case MatrixDelimiterStyle.Brackets:
                        leftCp = '[';
                        rightCp = ']';
                        break;
                    case MatrixDelimiterStyle.Braces:
                        leftCp = 0x7B;
                        rightCp = 0x7D;
                        break;
                    case MatrixDelimiterStyle.Vertical:
                        leftCp = '|';
                        rightCp = '|';
                        break;
                    case MatrixDelimiterStyle.DoubleVertical:
                        leftCp = 0x2225;
                        rightCp = 0x2225;
                        break;
                    case MatrixDelimiterStyle.Cases:
                        leftCp = 0x7B;
                        rightCp = 0;
                        break;
                }

                return WrapWithDelimiters(matrixIndex, leftCp, rightCp, ref ctx);
            }

            return matrixIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float MatrixColumnGap(MatrixAlignmentStyle alignment, int precedingColumn,
            float emSize)
        {
            if (alignment == MatrixAlignmentStyle.Alternating)
                return (precedingColumn & 1) == 0 ? 0f : emSize * 2f;
            return emSize;
        }

        /// <summary>
        /// Converts a mu-width space node to a pixel-width kern box.
        /// 1 mu = 1/18 em.
        /// </summary>
        private int LayoutSpace(ref MathNode node, ref MathLayoutContext ctx)
        {
            var mu = node.SpaceWidth;
            var px = MathSpacing.MuToPixels(mu, ctx.EmSize);
            return buffer.Add(MathBox.CreateKern(px));
        }

        /// <summary>
        /// Lays out a text node (\text{...}).
        /// </summary>
        /// <remarks>
        /// Text nodes use the core isolated-text path, including BiDi, script itemization,
        /// fallback resolution, OpenType shaping, and mark positioning.
        /// </remarks>
        private int LayoutText(ref MathNode node, ref MathLayoutContext ctx)
        {
            return LayoutTextSpan(node.commandStart, node.commandLength, ref ctx, false);
        }

        private int LayoutTextSpan(int start, int length, ref MathLayoutContext ctx,
            bool applyScriptStyle)
        {
            if (length == 0)
                return AddEmptyKern();

            shapedText.FakeClear();
            shapedTextFonts.FakeClear();
            var text = source.AsSpan(start, length);
            if (applyScriptStyle)
            {
                glyphProvider.ShapeMathText(text, ctx.fontSize,
                    MathStyleUtil.SizeLevel(ctx.style), ref shapedText, ref shapedTextFonts,
                    out var mathTextWidth);
                return BuildShapedTextBox(ref ctx, mathTextWidth, true);
            }

            glyphProvider.ShapeText(text, ctx.fontSize, ref shapedText, ref shapedTextFonts,
                out var textWidth);
            return BuildShapedTextBox(ref ctx, textWidth, false);
        }

        private int BuildShapedTextBox(ref MathLayoutContext ctx, float width, bool usesMathFont)
        {
            var childStart = buffer.Count;
            var height = 0f;
            var depth = 0f;
            var metricsFontId = int.MinValue;
            var textLineHeight = 0f;
            var textLineDepth = 0f;

            if (shapedText.count != shapedTextFonts.count)
                throw new InvalidOperationException("Text shaping returned mismatched glyph and font counts.");

            for (var i = 0; i < shapedText.count; i++)
            {
                ref var shapedGlyph = ref shapedText[i];
                var shapedFontId = shapedTextFonts[i];
                float glyphHeight;
                float glyphDepth;
                if (usesMathFont)
                {
                    glyphHeight = ctx.ToPixels(
                        glyphProvider.GetGlyphHeight(shapedFontId, (uint)shapedGlyph.glyphId));
                    glyphDepth = ctx.ToPixels(
                        glyphProvider.GetGlyphDepth(shapedFontId, (uint)shapedGlyph.glyphId));
                }
                else
                {
                    if (!glyphProvider.TryGetTextGlyphExtents(shapedFontId,
                            (uint)shapedGlyph.glyphId, ctx.fontSize,
                            out glyphHeight, out glyphDepth))
                    {
                        if (shapedFontId != metricsFontId)
                        {
                            metricsFontId = shapedFontId;
                            glyphProvider.GetTextLineMetrics(shapedFontId, ctx.fontSize,
                                out textLineHeight, out textLineDepth);
                        }
                        glyphHeight = textLineHeight;
                        glyphDepth = textLineDepth;
                    }
                }

                glyphHeight = Math.Max(0f, glyphHeight + shapedGlyph.offsetY);
                glyphDepth = Math.Max(0f, glyphDepth - shapedGlyph.offsetY);
                buffer.Add(MathBox.CreateGlyph(shapedGlyph.glyphId, shapedFontId, ctx.fontSize,
                    shapedGlyph.advanceX, glyphHeight, glyphDepth, 0f,
                    shapedGlyph.offsetX, shapedGlyph.offsetY));
                if (glyphHeight > height) height = glyphHeight;
                if (glyphDepth > depth) depth = glyphDepth;
            }

            var childCount = buffer.Count - childStart;
            if (childCount == 0)
                return AddEmptyKern();
            if (childCount == 1)
                return childStart;

            var hbox = MathBox.CreateHBox();
            hbox.firstChild = childStart;
            hbox.childCount = childCount;
            hbox.width = width;
            hbox.height = height;
            hbox.depth = depth;
            return buffer.Add(hbox);
        }

        /// <summary>
        /// Builds an HBox with base + superscript (shifted up) + spaceAfterScript.
        /// </summary>
        private int BuildSupOnlyHBox(int baseIndex, int supIndex, float supShift,
            float horizontalOffset, float spaceAfterScript)
        {
            var hboxChildStart = buffer.Count;

            CopyBoxToEnd(baseIndex);

            if (Math.Abs(horizontalOffset) > 0.001f)
                buffer.Add(MathBox.CreateKern(horizontalOffset));

            CopyBoxToEnd(supIndex);
            buffer[buffer.Count - 1].shift = -supShift;

            if (spaceAfterScript > 0.001f)
                buffer.Add(MathBox.CreateKern(spaceAfterScript));
            return AddMeasuredHBox(hboxChildStart);
        }

        /// <summary>
        /// Builds an HBox with base + subscript (shifted down) + spaceAfterScript.
        /// </summary>
        private int BuildSubOnlyHBox(int baseIndex, int subIndex, float subShift,
            float horizontalOffset, float spaceAfterScript)
        {
            var hboxChildStart = buffer.Count;

            CopyBoxToEnd(baseIndex);

            if (Math.Abs(horizontalOffset) > 0.001f)
                buffer.Add(MathBox.CreateKern(horizontalOffset));

            CopyBoxToEnd(subIndex);
            buffer[buffer.Count - 1].shift = subShift;

            if (spaceAfterScript > 0.001f)
                buffer.Add(MathBox.CreateKern(spaceAfterScript));
            return AddMeasuredHBox(hboxChildStart);
        }

        /// <summary>
        /// Builds an HBox with base + superscript (shifted up) + subscript (shifted down)
        /// + spaceAfterScript.
        /// </summary>
        /// <remarks>
        /// The superscript is offset right by the base's italic correction.
        /// The subscript is placed directly after the base (no italic offset).
        /// Both are followed by spaceAfterScript.
        /// </remarks>
        private int BuildSupSubHBox(int baseIndex, int supIndex, int subIndex,
            float supShift, float subShift, float supOffset, float subOffset,
            float spaceAfterScript)
        {
            var supW = buffer[supIndex].width;
            var subW = buffer[subIndex].width;

            var hboxChildStart = buffer.Count;

            CopyBoxToEnd(baseIndex);

            if (Math.Abs(supOffset) > 0.001f)
                buffer.Add(MathBox.CreateKern(supOffset));

            CopyBoxToEnd(supIndex);
            buffer[buffer.Count - 1].shift = -supShift;

            var backKern = -(supW + supOffset);
            if (Math.Abs(backKern) > 0.001f)
                buffer.Add(MathBox.CreateKern(backKern));

            if (Math.Abs(subOffset) > 0.001f)
                buffer.Add(MathBox.CreateKern(subOffset));

            CopyBoxToEnd(subIndex);
            buffer[buffer.Count - 1].shift = subShift;

            var scriptWidth = Math.Max(supOffset + supW, subOffset + subW);
            var widthCorrection = scriptWidth - subOffset - subW;
            if (Math.Abs(widthCorrection) > 0.001f)
                buffer.Add(MathBox.CreateKern(widthCorrection));

            if (spaceAfterScript > 0.001f)
                buffer.Add(MathBox.CreateKern(spaceAfterScript));
            return AddMeasuredHBox(hboxChildStart);
        }

        /// <summary>
        /// Wraps a content box with left and/or right delimiter glyphs.
        /// </summary>
        private int WrapWithDelimiters(int contentIndex, int leftCp, int rightCp,
            ref MathLayoutContext ctx)
        {
            var contentHeight = buffer[contentIndex].height;
            var contentDepth = buffer[contentIndex].depth;
            var axisHeight = ctx.ToPixels(ctx.metrics.axisHeight);
            var targetHeight = 2f * Math.Max(0f,
                Math.Max(contentHeight - axisHeight, contentDepth + axisHeight));

            var leftIndex = leftCp > 0
                ? LayoutDelimiterGlyph(leftCp, targetHeight, ref ctx)
                : -1;
            var rightIndex = rightCp > 0
                ? LayoutDelimiterGlyph(rightCp, targetHeight, ref ctx)
                : -1;

            var hboxChildStart = buffer.Count;

            if (leftIndex >= 0)
                CopyBoxToEnd(leftIndex);

            CopyBoxToEnd(contentIndex);

            if (rightIndex >= 0)
                CopyBoxToEnd(rightIndex);

            return AddMeasuredHBox(hboxChildStart);
        }

        private int AddMeasuredHBox(int childStart)
        {
            var childCount = buffer.Count - childStart;
            if (childCount == 0)
                return AddEmptyKern();
            if (childCount == 1)
                return childStart;

            var width = 0f;
            var height = 0f;
            var depth = 0f;
            for (var i = childStart; i < childStart + childCount; i++)
            {
                ref var child = ref buffer[i];
                width += child.width;
                height = Math.Max(height, child.height - child.shift);
                depth = Math.Max(depth, child.depth + child.shift);
            }

            var hbox = MathBox.CreateHBox();
            hbox.firstChild = childStart;
            hbox.childCount = childCount;
            hbox.width = width;
            hbox.height = height;
            hbox.depth = depth;
            return buffer.Add(hbox);
        }

        /// <summary>
        /// Creates and adds a zero-width kern box. Used as a placeholder for absent nodes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AddEmptyKern()
        {
            return buffer.Add(MathBox.CreateKern(0f));
        }

        /// <summary>
        /// Copies a box from an arbitrary position to the end of the buffer,
        /// ensuring contiguity for HBox/VBox children.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CopyBoxToEnd(int sourceIndex)
        {
            buffer.Add(buffer[sourceIndex]);
        }

        /// <summary>
        /// Creates a centered copy of a box within <paramref name="parentWidth"/>.
        /// Returns the index of the resulting box (an HBox with centering kerns, or a direct copy).
        /// </summary>
        private int CopyBoxCentered(int sourceIndex, float parentWidth, float childWidth)
        {
            var pad = (parentWidth - childWidth) * 0.5f;
            return CopyBoxAt(sourceIndex, parentWidth, pad);
        }

        private int CopyBoxAt(int sourceIndex, float parentWidth, float offset)
        {
            var childWidth = buffer[sourceIndex].width;
            var trailing = parentWidth - offset - childWidth;
            if (Math.Abs(offset) < 0.001f && Math.Abs(trailing) < 0.001f)
            {
                var idx = buffer.Count;
                CopyBoxToEnd(sourceIndex);
                return idx;
            }

            var hboxStart = buffer.Count;
            if (Math.Abs(offset) > 0.001f)
                buffer.Add(MathBox.CreateKern(offset));
            CopyBoxToEnd(sourceIndex);
            if (Math.Abs(trailing) > 0.001f)
                buffer.Add(MathBox.CreateKern(trailing));

            var src = buffer[sourceIndex];
            var hbox = MathBox.CreateHBox();
            hbox.firstChild = hboxStart;
            hbox.childCount = buffer.Count - hboxStart;
            hbox.width = parentWidth;
            hbox.height = src.height;
            hbox.depth = src.depth;
            return buffer.Add(hbox);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindAtomType(int index, int step, int start, int count, int stride)
        {
            for (var i = index; i >= 0 && i < count; i += step)
            {
                var type = childIndices[start + i * stride + 1];
                if (type >= 0)
                    return type;
            }
            return -1;
        }
    }
}
