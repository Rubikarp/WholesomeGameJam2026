using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Finds or constructs stretchy delimiters (parentheses, brackets, braces, radical signs)
    /// using pre-made glyph variants or glyph assembly from the OpenType MATH MathVariants table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the glyph construction algorithm from the OpenType MATH specification (v1.9.1),
    /// MathVariants / GlyphAssembly section. The algorithm:
    /// </para>
    /// <list type="number">
    /// <item>Map the delimiter codepoint to a base glyph ID.</item>
    /// <item>Try pre-made size variants (cheaper than assembly). Pick the smallest variant
    ///        whose advance in the extension direction meets the target size.</item>
    /// <item>Fall back to glyph assembly: repeat extenders enough to cover the target, choose
    ///        one legal overlap for every connection, then emit positioned parts.</item>
    /// <item>If the font cannot reach the target, use its largest available glyph, including the base glyph.</item>
    /// </list>
    /// <para>
    /// Temporary native query arrays and expanded assembly indices use the shared pool.
    /// All HarfBuzz values are in font design units; conversion to pixels uses
    /// <see cref="MathLayoutContext.ToPixels(int)"/>.
    /// </para>
    /// </remarks>
    internal struct MathDelimiterBuilder
    {
        private const int InitialQueryCapacity = 8;

        /// <summary>
        /// Finds or constructs the closest available vertical delimiter for <paramref name="targetHeight"/>.
        /// Returns the index of the resulting <see cref="MathBox"/> (Delimiter type) in the buffer.
        /// </summary>
        /// <param name="codepoint">Unicode codepoint of the delimiter character.</param>
        /// <param name="targetHeight">Desired height (ascent + descent) in pixels.</param>
        /// <param name="buffer">The box/assembly-part buffer to append to.</param>
        /// <param name="ctx">Current math layout context (font, size, metrics).</param>
        /// <returns>Index of the added MathBox in the buffer, or -1 on failure.</returns>
        public int BuildVerticalDelimiter(
            int codepoint, float targetHeight,
            ref MathBoxBuffer buffer, ref MathLayoutContext ctx,
            IMathGlyphProvider provider, bool alignToAxis = true, bool allowAssembly = true)
        {
            return BuildDelimiter(codepoint, targetHeight, HB.DIRECTION_BTT,
                ref buffer, ref ctx, provider, MathGlyphForm.Default, alignToAxis, allowAssembly);
        }

        /// <summary>
        /// Finds or constructs the closest available horizontal delimiter for <paramref name="targetWidth"/>.
        /// Returns the index of the resulting <see cref="MathBox"/> (Delimiter type) in the buffer.
        /// </summary>
        /// <param name="codepoint">Unicode codepoint of the delimiter character.</param>
        /// <param name="targetWidth">Desired width in pixels.</param>
        /// <param name="buffer">The box/assembly-part buffer to append to.</param>
        /// <param name="ctx">Current math layout context (font, size, metrics).</param>
        /// <returns>Index of the added MathBox in the buffer, or -1 on failure.</returns>
        public int BuildHorizontalDelimiter(
            int codepoint, float targetWidth,
            ref MathBoxBuffer buffer, ref MathLayoutContext ctx,
            IMathGlyphProvider provider, MathGlyphForm form = MathGlyphForm.Default)
        {
            return BuildDelimiter(codepoint, targetWidth, HB.DIRECTION_LTR,
                ref buffer, ref ctx, provider, form, false, true);
        }


        /// <summary>
        /// Core implementation for both vertical and horizontal delimiter construction.
        /// </summary>
        /// <param name="codepoint">Unicode codepoint of the delimiter.</param>
        /// <param name="targetSize">Minimum required size in pixels (height for vertical, width for horizontal).</param>
        /// <param name="direction">HarfBuzz direction constant: BTT for vertical, LTR for horizontal.</param>
        /// <param name="buffer">The box/assembly-part buffer.</param>
        /// <param name="ctx">Current math layout context.</param>
        /// <returns>Index of the added MathBox, or -1 on failure.</returns>
        private int BuildDelimiter(
            int codepoint, float targetSize, int direction,
            ref MathBoxBuffer buffer, ref MathLayoutContext ctx,
            IMathGlyphProvider provider, MathGlyphForm form,
            bool alignToAxis, bool allowAssembly)
        {
            if (codepoint == 0x2E || codepoint == 0)
            {
                var axisHeightPx = ctx.ToPixels(ctx.metrics.axisHeight);
                var box = MathBox.CreateKern(0);
                box.height = targetSize * 0.5f + axisHeightPx;
                box.depth = Math.Max(0f, targetSize * 0.5f - axisHeightPx);
                return buffer.Add(box);
            }

            if (!provider.TryGetGlyphId(ctx.fontId, codepoint, MathStyleUtil.SizeLevel(ctx.style), form,
                    out var baseGlyph))
                return -1;

            var baseSize = direction == HB.DIRECTION_BTT || direction == HB.DIRECTION_TTB
                ? (provider.GetGlyphHeight(ctx.fontId, baseGlyph)
                   + provider.GetGlyphDepth(ctx.fontId, baseGlyph)) * ctx.pixelScale
                : provider.GetAdvanceWidth(ctx.fontId, baseGlyph) * ctx.pixelScale;
            if (baseSize >= targetSize)
                return EmitBaseGlyph(baseGlyph, direction, alignToAxis,
                    ref buffer, ref ctx, provider);

            var variantIndex = TryFindVariant(
                baseGlyph, targetSize, direction,
                ref buffer, ref ctx, provider,
                alignToAxis,
                out var largestVariant, out var hasLargestVariant);

            if (variantIndex >= 0)
                return variantIndex;

            var assemblyIndex = allowAssembly
                ? TryBuildAssembly(baseGlyph, targetSize, direction,
                    ref buffer, ref ctx, provider, alignToAxis)
                : -1;

            if (assemblyIndex >= 0)
                return assemblyIndex;

            if (hasLargestVariant)
                return EmitVariant(largestVariant, direction, alignToAxis,
                    ref buffer, ref ctx, provider);

            return EmitBaseGlyph(baseGlyph, direction, alignToAxis,
                ref buffer, ref ctx, provider);
        }


        /// <summary>
        /// Searches pre-made glyph variants for one that meets the target size.
        /// Returns the box index if found, or -1 if no variant is large enough.
        /// </summary>
        private int TryFindVariant(
            uint baseGlyph, float targetSize, int direction,
            ref MathBoxBuffer buffer, ref MathLayoutContext ctx,
            IMathGlyphProvider provider,
            bool alignToAxis,
            out HB.hb_ot_math_glyph_variant_t largestVariant,
            out bool hasLargestVariant)
        {
            largestVariant = default;
            hasLargestVariant = false;
            var variantArray = ArrayPool<HB.hb_ot_math_glyph_variant_t>.Rent(InitialQueryCapacity);
            try
            {
                var count = provider.GetGlyphVariants(ctx.fontId, baseGlyph, direction, variantArray);

                if (count <= 0)
                    return -1;

                if (count > variantArray.Length)
                {
                    var expectedCount = count;
                    var expanded = ArrayPool<HB.hb_ot_math_glyph_variant_t>.Rent(count);
                    ArrayPool<HB.hb_ot_math_glyph_variant_t>.Return(variantArray);
                    variantArray = expanded;
                    count = provider.GetGlyphVariants(ctx.fontId, baseGlyph, direction, variantArray);
                    if (count != expectedCount)
                        throw new InvalidOperationException("Math glyph variant count changed while reading an immutable font.");
                }

                var pixelScale = ctx.pixelScale;
                var sufficientIndex = -1;

                for (var i = 0; i < count; i++)
                {
                    ref readonly var variant = ref variantArray[i];
                    if (!hasLargestVariant || variant.advance > largestVariant.advance)
                    {
                        largestVariant = variant;
                        hasLargestVariant = true;
                    }

                    if (variant.advance * pixelScale >= targetSize
                        && (sufficientIndex < 0
                            || variant.advance < variantArray[sufficientIndex].advance))
                        sufficientIndex = i;
                }

                return sufficientIndex >= 0
                    ? EmitVariant(variantArray[sufficientIndex], direction, alignToAxis,
                        ref buffer, ref ctx, provider)
                    : -1;
            }
            finally
            {
                ArrayPool<HB.hb_ot_math_glyph_variant_t>.Return(variantArray);
            }
        }

        private int EmitVariant(HB.hb_ot_math_glyph_variant_t variant, int direction,
            bool alignToAxis,
            ref MathBoxBuffer buffer, ref MathLayoutContext ctx, IMathGlyphProvider provider)
        {
            var glyph = variant.glyph;
            var pixelScale = ctx.pixelScale;
            var advance = variant.advance * pixelScale;
            var italic = provider.GetItalicCorrection(ctx.fontId, glyph) * pixelScale;

            float width;
            float height;
            float depth;
            if (direction == HB.DIRECTION_BTT || direction == HB.DIRECTION_TTB)
            {
                width = provider.GetAdvanceWidth(ctx.fontId, glyph) * pixelScale;
                height = provider.GetGlyphHeight(ctx.fontId, glyph) * pixelScale;
                depth = provider.GetGlyphDepth(ctx.fontId, glyph) * pixelScale;
            }
            else
            {
                width = advance;
                height = provider.GetGlyphHeight(ctx.fontId, glyph) * pixelScale;
                depth = provider.GetGlyphDepth(ctx.fontId, glyph) * pixelScale;
            }

            var box = MathBox.CreateDelimiterVariant(
                (int)glyph, ctx.fontId, ctx.fontSize, width, height, depth, italic);
            if (alignToAxis && (direction == HB.DIRECTION_BTT || direction == HB.DIRECTION_TTB))
                AlignVerticalGlyph(ref box, ref ctx);
            return buffer.Add(box);
        }


        /// <summary>
        /// Constructs a delimiter from assembly parts following the MathML Core assembly algorithm:
        /// <list type="number">
        /// <item>Compute directly how many copies of each extender are required at minimum overlap.</item>
        /// <item>Use one uniform overlap for every connection, bounded by both connectors.</item>
        /// <item>Emit the resulting sequence in the order defined by the font.</item>
        /// </list>
        /// </summary>
        private int TryBuildAssembly(
            uint baseGlyph, float targetSize, int direction,
            ref MathBoxBuffer buffer, ref MathLayoutContext ctx,
            IMathGlyphProvider provider, bool alignToAxis)
        {
            var partsArray = ArrayPool<HB.hb_ot_math_glyph_part_t>.Rent(InitialQueryCapacity);
            int partCount;
            int italicsCorrectionDu;
            try
            {
                partCount = provider.GetGlyphAssembly(ctx.fontId, baseGlyph, direction, partsArray,
                    out italicsCorrectionDu);
                if (partCount <= 0)
                    return -1;

                if (partCount > partsArray.Length)
                {
                    var expectedCount = partCount;
                    var expanded = ArrayPool<HB.hb_ot_math_glyph_part_t>.Rent(partCount);
                    ArrayPool<HB.hb_ot_math_glyph_part_t>.Return(partsArray);
                    partsArray = expanded;
                    partCount = provider.GetGlyphAssembly(ctx.fontId, baseGlyph, direction, partsArray,
                        out italicsCorrectionDu);
                    if (partCount != expectedCount)
                        throw new InvalidOperationException("Math glyph assembly count changed while reading an immutable font.");
                }

                return BuildAssembly(partsArray.AsSpan(0, partCount), italicsCorrectionDu, targetSize, direction,
                    ref buffer, ref ctx, provider, alignToAxis);
            }
            finally
            {
                ArrayPool<HB.hb_ot_math_glyph_part_t>.Return(partsArray);
            }
        }

        private int BuildAssembly(ReadOnlySpan<HB.hb_ot_math_glyph_part_t> parts,
            int italicsCorrectionDu, float targetSize, int direction,
            ref MathBoxBuffer buffer, ref MathLayoutContext ctx,
            IMathGlyphProvider provider, bool alignToAxis)
        {
            var partCount = parts.Length;

            var minOverlapDu = provider.GetMinConnectorOverlap(ctx.fontId, direction);
            if (minOverlapDu < 0)
                return -1;

            var pixelScale = ctx.pixelScale;
            var targetSizeDu = targetSize / pixelScale;

            var nonExtenderCount = 0;
            var extenderCount = 0;
            float nonExtenderAdvanceDu = 0f;
            float extenderAdvanceDu = 0f;
            for (var i = 0; i < partCount; i++)
            {
                if ((parts[i].flags & HB.HB_OT_MATH_GLYPH_PART_FLAG_EXTENDER) != 0)
                {
                    extenderCount++;
                    extenderAdvanceDu += parts[i].fullAdvance;
                }
                else
                {
                    nonExtenderCount++;
                    nonExtenderAdvanceDu += parts[i].fullAdvance;
                }
            }

            var requiredAdvanceDu = targetSizeDu - nonExtenderAdvanceDu
                                    + minOverlapDu * (nonExtenderCount - 1);
            var extenderRepeat = 0;
            if (requiredAdvanceDu > 0f)
            {
                var extenderGrowthDu = extenderAdvanceDu - minOverlapDu * extenderCount;
                if (extenderCount == 0 || extenderGrowthDu <= 0f)
                    return -1;

                var requiredRepeat = Math.Ceiling(requiredAdvanceDu / extenderGrowthDu);
                var maxRepeat = (int.MaxValue - nonExtenderCount) / extenderCount;
                if (double.IsNaN(requiredRepeat) || double.IsInfinity(requiredRepeat)
                    || requiredRepeat > maxRepeat)
                    return -1;
                extenderRepeat = (int)requiredRepeat;
            }

            if (nonExtenderCount == 0 && extenderRepeat == 0)
                extenderRepeat = 1;
            var totalPartCount = nonExtenderCount + extenderCount * extenderRepeat;
            if (totalPartCount == 0)
                return -1;

            var expandedPartIndices = ArrayPool<int>.Rent(totalPartCount);
            try
            {
                var expandedIdx = 0;
                for (var i = 0; i < partCount; i++)
                {
                    var isExtender = (parts[i].flags & HB.HB_OT_MATH_GLYPH_PART_FLAG_EXTENDER) != 0;
                    var repeats = isExtender ? extenderRepeat : 1;
                    for (var r = 0; r < repeats; r++)
                        expandedPartIndices[expandedIdx++] = i;
                }

                var connectionCount = totalPartCount - 1;
                var totalAdvanceDu = nonExtenderAdvanceDu + extenderRepeat * extenderAdvanceDu;
                var overlapDu = connectionCount > 0
                    ? (totalAdvanceDu - targetSizeDu) / connectionCount
                    : 0f;
                for (var i = 0; i < connectionCount; i++)
                {
                    var leftPart = expandedPartIndices[i];
                    var rightPart = expandedPartIndices[i + 1];
                    overlapDu = Math.Min(overlapDu, Math.Min(
                        (float)parts[leftPart].endConnectorLength,
                        (float)parts[rightPart].startConnectorLength));
                }
                if (connectionCount > 0 && overlapDu < minOverlapDu)
                    return -1;

                var resultSizeDu = totalAdvanceDu - overlapDu * connectionCount;
                var sizeTolerance = Math.Max(0.001f, Math.Abs(targetSizeDu) * 0.000001f);
                if (resultSizeDu + sizeTolerance < targetSizeDu)
                    return -1;

                var assemblyStartIndex = buffer.assemblyParts.count;
                float currentOffset = 0;
                float maxPartWidth = 0;
                float maxPartHeight = 0;
                float maxPartDepth = 0;

                for (var i = 0; i < totalPartCount; i++)
                {
                    var pIdx = expandedPartIndices[i];
                    var fullAdvanceDu = (float)parts[pIdx].fullAdvance;
                    var glyph = parts[pIdx].glyph;

                    if (i > 0)
                        currentOffset -= overlapDu;

                    var offsetPx = currentOffset * pixelScale;
                    var partWidth = provider.GetAdvanceWidth(ctx.fontId, glyph) * pixelScale;
                    var partHeight = provider.GetGlyphHeight(ctx.fontId, glyph) * pixelScale;
                    var partDepth = provider.GetGlyphDepth(ctx.fontId, glyph) * pixelScale;
                    if (partWidth > maxPartWidth) maxPartWidth = partWidth;
                    if (partHeight > maxPartHeight) maxPartHeight = partHeight;
                    if (partDepth > maxPartDepth) maxPartDepth = partDepth;

                    buffer.AddAssemblyPart(new MathAssemblyPart
                    {
                        glyphId = (int)glyph,
                        offset = offsetPx,
                        width = partWidth,
                        height = partHeight,
                        depth = partDepth
                    });

                    currentOffset += fullAdvanceDu;
                }

                if (extenderRepeat == 0)
                {
                    for (var i = 0; i < partCount; i++)
                    {
                        if ((parts[i].flags & HB.HB_OT_MATH_GLYPH_PART_FLAG_EXTENDER) == 0)
                            continue;

                        var glyph = parts[i].glyph;
                        var partWidth = provider.GetAdvanceWidth(ctx.fontId, glyph) * pixelScale;
                        var partHeight = provider.GetGlyphHeight(ctx.fontId, glyph) * pixelScale;
                        var partDepth = provider.GetGlyphDepth(ctx.fontId, glyph) * pixelScale;
                        if (partWidth > maxPartWidth) maxPartWidth = partWidth;
                        if (partHeight > maxPartHeight) maxPartHeight = partHeight;
                        if (partDepth > maxPartDepth) maxPartDepth = partDepth;
                    }
                }

                var totalSizePx = resultSizeDu * pixelScale;

                var italicPx = italicsCorrectionDu * pixelScale;
                float boxWidth, boxHeight, boxDepth;

                if (direction == HB.DIRECTION_BTT || direction == HB.DIRECTION_TTB)
                {
                    if (alignToAxis)
                    {
                        var axisHeightPx = ctx.ToPixels(ctx.metrics.axisHeight);
                        boxHeight = totalSizePx * 0.5f + axisHeightPx;
                        boxDepth = Math.Max(0f, totalSizePx * 0.5f - axisHeightPx);
                    }
                    else
                    {
                        boxHeight = totalSizePx;
                        boxDepth = 0f;
                    }
                    boxWidth = maxPartWidth;
                }
                else
                {
                    boxWidth = totalSizePx;
                    boxHeight = maxPartHeight;
                    boxDepth = maxPartDepth;
                }

                var isHorizontal = direction == HB.DIRECTION_LTR;
                var assemblyBox = MathBox.CreateDelimiterAssembly(
                    ctx.fontId, ctx.fontSize,
                    boxWidth, boxHeight, boxDepth,
                    assemblyStartIndex, totalPartCount, isHorizontal);
                assemblyBox.italic = italicPx;

                return buffer.Add(assemblyBox);
            }
            finally
            {
                ArrayPool<int>.Return(expandedPartIndices);
            }
        }


        /// <summary>
        /// Emits a base glyph that already satisfies the requested size.
        /// </summary>
        private int EmitBaseGlyph(
            uint baseGlyph, int direction, bool alignToAxis,
            ref MathBoxBuffer buffer, ref MathLayoutContext ctx,
            IMathGlyphProvider provider)
        {
            var pixelScale = ctx.pixelScale;
            var hAdvanceDu = provider.GetAdvanceWidth(ctx.fontId, baseGlyph);
            var widthPx = hAdvanceDu * pixelScale;

            var italicDu = provider.GetItalicCorrection(ctx.fontId, baseGlyph);
            var italicPx = italicDu * pixelScale;

            var height = provider.GetGlyphHeight(ctx.fontId, baseGlyph) * pixelScale;
            var depth = provider.GetGlyphDepth(ctx.fontId, baseGlyph) * pixelScale;

            var box = MathBox.CreateDelimiterVariant(
                (int)baseGlyph, ctx.fontId, ctx.fontSize,
                widthPx, height, depth, italicPx);

            if (alignToAxis && (direction == HB.DIRECTION_BTT || direction == HB.DIRECTION_TTB))
                AlignVerticalGlyph(ref box, ref ctx);

            return buffer.Add(box);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AlignVerticalGlyph(ref MathBox box, ref MathLayoutContext ctx)
        {
            var baselineOffset = ctx.ToPixels(ctx.metrics.axisHeight)
                                 - (box.height - box.depth) * 0.5f;
            box.delimiterBaselineOffset = baselineOffset;
            box.height = Math.Max(0f, box.height + baselineOffset);
            box.depth = Math.Max(0f, box.depth - baselineOffset);
        }

    }
}
