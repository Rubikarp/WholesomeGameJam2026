using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    public sealed partial class TextProcessor
    {
        /// <summary>
        /// Gets the total width of all text runs without line wrapping.
        /// </summary>
        /// <returns>The unwrapped text width at the shaping font size, or 0 if no valid data.</returns>
        /// <remarks>
        /// This returns the width as if the text were rendered on a single line
        /// at the font size used during shaping.
        /// </remarks>
        public float GetUnwrappedWidth()
        {
            if (!hasValidFirstPassData) return 0;

            var cpCount = buf.codepoints.count;
            var widths = CurrentLayoutWidths;

            float total = 0;
            if (layoutHiddenMask != 0)
            {
                for (var i = 0; i < cpCount; i++) total += widths[i];
            }
            else
            {
                var count = buf.shapedRuns.count;
                for (var i = 0; i < count; i++)
                    total += buf.shapedRuns[i].width;
            }

            var cps = buf.codepoints.data;
            for (var j = cpCount - 1; j >= 0; j--)
            {
                if (IsClusterHiddenFromLayout(j)) continue;
                if (!LineBreaker.IsHangingWhitespace(cps[j])) break;
                total -= widths[j];
            }

            return total;
        }

        private ReadOnlySpan<float> CurrentLayoutWidths
            => layoutHiddenMask != 0
                ? effectiveWidths.data.AsSpan(0, buf.codepoints.count)
                : buf.cpWidths.Span;

        /// <summary>
        /// Gets the preferred width for the text at the specified font size.
        /// </summary>
        /// <param name="fontSize">The font size in points.</param>
        /// <returns>The preferred width in pixels, or 0 if no valid data.</returns>
        /// <remarks>
        /// Returns the width of the widest line, accounting for explicit line breaks
        /// but not word wrapping. Use this for auto-sizing calculations.
        /// </remarks>
        public float GetPreferredWidth(float fontSize) => GetPreferredWidth(fontSize, false);

        internal float GetPreferredWidth(float fontSize, bool measureTrailingWhitespace)
        {
            if (!hasValidFirstPassData) return 0;
            var glyphScale = buf.GetGlyphScale(fontSize);
            return Mathf.Ceil(GetMaxLineWidth(measureTrailingWhitespace) * glyphScale);
        }

        /// <summary>
        /// Gets the preferred height for the text at the specified font size.
        /// </summary>
        /// <param name="fontSize">The font size in points.</param>
        /// <param name="lineSpacing">Additional spacing between lines. Default is 0.</param>
        /// <returns>The preferred height in pixels, or 0 if no valid line data.</returns>
        /// <remarks>
        /// Requires <see cref="EnsureLines"/> to be called first.
        /// Returns cached height computed after line breaking, which includes any
        /// per-line adjustments from <see cref="OnCalculateLineHeight"/>.
        /// </remarks>
        public float GetPreferredHeight(float fontSize, float lineSpacing = 0f)
            => GetPreferredHeightCore(fontSize, lineSpacing, appliedFit.lineHeightScale);

        private float GetPreferredHeightCore(float fontSize, float lineSpacing, float fitLineHeightScale)
        {
            if (!linesCache.valid) return 0;

            var settings = new TextProcessSettings { layout = LayoutSettings.Default, fontSize = fontSize };
            settings.layout.lineSpacing = lineSpacing;
            configureSettings?.Invoke(ref settings);

            if (!heightCache.MatchesFor(fontSize, lineSpacing, settings.layout.lineHeightMode, settings.layout.lineHeightScale, fitLineHeightScale))
                ComputeLineHeights(fontSize, lineSpacing, settings.layout.leadingDistribution, settings.layout.lineHeightMode, settings.layout.lineHeightScale, fitLineHeightScale);

            return HeightFromComputedLines(fontSize, settings.layout);
        }

        /// <summary>
        /// Block height from the line heights already in <c>heightCache</c>, trimmed to the block
        /// edges <paramref name="layout"/> asks for. Valid only right after the cache was filled
        /// for <paramref name="fontSize"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float HeightFromComputedLines(float fontSize, in LayoutSettings layout)
        {
            var capHeight = fontProvider.GetCapHeight(fontSize);
            fontProvider.GetTypoMetrics(fontSize, out var typoAscent, out var typoDescent);
            var xHeight = fontProvider.GetXHeight(fontSize);
            var trim = TextLayout.ComputeTrimAmount(heightCache.mainAscender, heightCache.mainDescender,
                capHeight, layout.overEdge, layout.underEdge, layout.leadingDistribution,
                heightCache.effectiveFirstLineHeight, heightCache.effectiveLastLineHeight,
                typoAscent, typoDescent, xHeight);
            return heightCache.rawHeight - trim + BlockOverReserve + BlockUnderReserve;
        }

        /// <summary>
        /// Probe half of component text measurement — overwrites line state and leaves it dirty.
        /// Callers wrap it in <see cref="SnapshotLayout"/> / <see cref="RestoreLayout"/>, possibly
        /// together with other probes (auto-size fitting).
        /// </summary>
        internal Vector2 MeasureSizeCore(float maxWidth, float fontSize, bool wordWrap,
            bool measureTrailingWhitespace, in FitBudgets budgets)
        {
            ApplyFitProbe(in budgets);
            EnsureLinesInternal(maxWidth, fontSize, wordWrap, buf.cpWidths.Span, budgets.lineHeightScale);

            var maxLineWidth = 0f;
            var lines = buf.lines.data;
            var lineCount = buf.lines.count;
            for (var i = 0; i < lineCount; i++)
            {
                var width = lines[i].width + lines[i].startMargin
                    + (measureTrailingWhitespace ? lines[i].trailingWhitespace : 0f);
                if (width > maxLineWidth) maxLineWidth = width;
            }

            return new Vector2(
                Mathf.Ceil(maxLineWidth * buf.GetGlyphScale(fontSize)),
                GetPreferredHeightCore(fontSize, 0f, budgets.lineHeightScale));
        }

        /// <summary>
        /// Gets the maximum width among all lines, considering explicit line breaks.
        /// </summary>
        /// <returns>The maximum line width at the shaping font size, or 0 if no valid data.</returns>
        /// <remarks>
        /// Measurement-only — does not mutate <see cref="EnsureLines"/> / <see cref="EnsurePositions"/>
        /// caches. Layout queries fired by Unity's <c>ILayoutElement</c> contract (and any other
        /// host that calls this on a frame between mesh-build and consumption) must not invalidate
        /// the rendered glyph state, otherwise <see cref="PositionedGlyphs"/> consumers — hit-test,
        /// caret rendering, link click — observe an empty span until the next mesh rebuild.
        /// Word wrapping is ignored; mandatory line breaks remain effective. Measured over the text as
        /// authored — pristine widths, every mandatory break standing — and never over a hidden layout,
        /// which is what a host asking this before it hands the text a width depends on: a width that
        /// answered for the current collapse would move whenever the collapse it decides moves, and
        /// settle only a frame later.
        /// </remarks>
        public float GetMaxLineWidth() => GetMaxLineWidth(false);

        internal float GetMaxLineWidth(bool measureTrailingWhitespace)
        {
            if (!hasValidFirstPassData) return 0;

            var cpCount = buf.codepoints.count;
            if (cpCount == 0) return 0;

            var marginsSpan = buf.startMargins.count >= cpCount
                ? buf.startMargins.data.AsSpan(0, cpCount)
                : ReadOnlySpan<float>.Empty;

            var maxWidth = LineBreaker.ComputeMaxLineWidthAtMandatoryBreaks(
                buf.codepoints.Span,
                buf.cpWidths.Span,
                buf.breakOpportunities.Span,
                marginsSpan,
                measureTrailingWhitespace,
                ReadOnlySpan<byte>.Empty,
                0,
                ReadOnlySpan<int>.Empty);

            return maxWidth > 0f ? maxWidth + lineWidthReserve : GetUnwrappedWidth();
        }

        /// <summary>
        /// The largest font size in <paramref name="minSize"/>–<paramref name="maxSize"/> whose text
        /// fits the target box, or <paramref name="minSize"/> when none does. Layout state is
        /// restored around the search.
        /// </summary>
        /// <param name="minSize">The minimum allowed font size.</param>
        /// <param name="maxSize">The maximum allowed font size.</param>
        /// <param name="targetWidth">The target width in pixels.</param>
        /// <param name="targetHeight">The target height in pixels.</param>
        /// <param name="baseSettings">The base processing settings.</param>
        /// <remarks>
        /// <para>
        /// Requires <see cref="HasValidFirstPassData"/> to be <see langword="true"/>.
        /// </para>
        /// <para>
        /// <b>Performance:</b> re-breaks lines once per candidate the search cannot answer from the
        /// line set it already holds.
        /// </para>
        /// </remarks>
        public float FindOptimalFontSize(
            float minSize,
            float maxSize,
            float targetWidth,
            float targetHeight,
            TextProcessSettings baseSettings)
        {
            var snapshot = SnapshotLayout();
            try
            {
                configureSettings?.Invoke(ref baseSettings);
                return FindOptimalFontSizeCore(minSize, maxSize, targetWidth, targetHeight,
                    baseSettings, FitBudgets.Identity);
            }
            finally
            {
                RestoreLayout(snapshot);
            }
        }

        /// <summary>
        /// The largest font size that fits the target box with the given fit adjustments held
        /// constant. <paramref name="baseSettings"/> must already be configured (see
        /// <see cref="PrepareSettings"/>); the public overload configures and forwards here.
        /// Lines are left broken and committed at the returned size.
        /// </summary>
        internal float FindOptimalFontSizeCore(
            float minSize,
            float maxSize,
            float targetWidth,
            float targetHeight,
            in TextProcessSettings baseSettings,
            in FitBudgets budgets)
        {
            if (!hasValidFirstPassData) return minSize;
            if (targetWidth <= 0 || targetHeight <= 0) return minSize;
            if (buf.shapingFontSize <= 0) return minSize;

            ApplyFitProbe(in budgets);
            fitBreakValid = false;

            var fitLh = budgets.lineHeightScale;
            var size = maxSize > minSize
                ? SolveFontSize(minSize, maxSize, targetWidth, targetHeight, baseSettings, fitLh)
                : minSize;

            var solvedLineCount = buf.lines.count;
            CommitLines(size, targetWidth, baseSettings, fitLh);

            if (buf.lines.count > solvedLineCount && size > minSize)
            {
                size = SolveFontSize(minSize, Mathf.Max(minSize, BelowBoundary(size)),
                    targetWidth, targetHeight, baseSettings, fitLh);
                CommitLines(size, targetWidth, baseSettings, fitLh);
            }

            return size;
        }

        #region Fit solving

        /// <summary>
        /// Relative font-size bracket below which a fit search stops refining. Wider than
        /// <see cref="FitBoundaryGuard"/>, so a bracket straddling a boundary settles on the size
        /// inside it instead of bisecting the guard band.
        /// </summary>
        private const float FitSizeEpsilon = 1e-3f;

        /// <summary>
        /// Relative back-off off an exact boundary. A size sitting on the width a line set exactly
        /// fills is decided by float rounding, so line breaking there may hand back a different set
        /// than the one measured; every boundary a search lands on is approached from inside it.
        /// </summary>
        private const float FitBoundaryGuard = 1e-4f;

        /// <summary>Probe cap for a search that a non-affine line-height hook keeps unsettled.</summary>
        private const int FitMaxIterations = 8;

        /// <summary>Line-break width the current line set was produced for; <see cref="fitBreakValid"/> guards it.</summary>
        private float fitBreakMaxWidth;

        private bool fitBreakValid;

        /// <summary>
        /// Walks the height curve, which rises with the font size and steps up wherever the text
        /// takes another line. A line set holds up to the width it exactly fills, and its height
        /// over that span is affine, so one span is settled outright: either the target lies inside
        /// it — the interpolated root is then the answer — or its whole span fits and the search
        /// moves to the span above. Sizes a span cannot place are bracketed and bisected.
        /// </summary>
        private float SolveFontSize(float minSize, float maxSize, float targetWidth, float targetHeight,
            in TextProcessSettings settings, float fitLh)
        {
            var best = float.NaN;
            var hi = float.PositiveInfinity;
            var size = maxSize;

            for (var iter = 0; iter < FitMaxIterations; iter++)
            {
                var height = FitHeightAt(size, targetWidth, settings, fitLh);

                if (height > targetHeight)
                {
                    hi = size;
                    var floor = float.IsNaN(best) ? minSize : best;
                    if (hi - floor <= hi * FitSizeEpsilon) return floor;

                    var next = height >= float.MaxValue
                        ? BelowBoundary(WidthCapOfCurrentLines(targetWidth))
                        : PredictFittingSize(size, height, floor, targetWidth, targetHeight, settings, fitLh);

                    size = next > floor && next < hi ? next : (floor + hi) * 0.5f;
                    if (hi - size <= hi * FitSizeEpsilon) size = floor;
                    continue;
                }

                best = size;

                var ceiling = float.IsPositiveInfinity(hi) ? maxSize : Mathf.Min(maxSize, BelowBoundary(hi));
                var widthCap = WidthCapOfCurrentLines(targetWidth);
                var cap = widthCap < ceiling ? BelowBoundary(widthCap) : ceiling;
                if (cap <= size + size * FitSizeEpsilon) return size;

                var capHeight = MeasureCurrentLines(cap, targetWidth, settings, fitLh);
                if (capHeight > targetHeight)
                {
                    var root = BelowBoundary(InterpolateSize(size, height, cap, capHeight, targetHeight));
                    if (!(root > size) || root >= cap) return size;

                    return MeasureCurrentLines(root, targetWidth, settings, fitLh) <= targetHeight ? root : size;
                }

                best = cap;
                if (cap >= ceiling - ceiling * FitSizeEpsilon) return cap;

                var above = AboveBoundary(widthCap);
                if (!(above > cap) || above >= ceiling) return cap;
                size = above;
            }

            return float.IsNaN(best) ? minSize : best;
        }

        /// <summary>The largest size that stays strictly inside a boundary landing on <paramref name="size"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float BelowBoundary(float size) => size * (1f - FitBoundaryGuard);

        /// <summary>The smallest size that lies strictly past a boundary landing on <paramref name="size"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float AboveBoundary(float size) => size * (1f + FitBoundaryGuard);

        /// <summary>Next candidate below an oversized one, read off the affine height model of the line set it produced.</summary>
        private float PredictFittingSize(float size, float height, float minSize, float targetWidth,
            float targetHeight, in TextProcessSettings settings, float fitLh)
        {
            var probe = Mathf.Max(minSize, size - Mathf.Max(size * FitSizeEpsilon, (size - minSize) * 0.25f));
            if (probe >= size) return minSize;

            var probeHeight = MeasureCurrentLines(probe, targetWidth, settings, fitLh);
            if (probeHeight >= float.MaxValue) return probe;

            return BelowBoundary(InterpolateSize(size, height, probe, probeHeight, targetHeight));
        }

        /// <summary>The size an affine model through two measured points puts at <paramref name="targetHeight"/>, or NaN when the model does not rise.</summary>
        private static float InterpolateSize(float a, float heightA, float b, float heightB, float targetHeight)
        {
            var slope = (heightB - heightA) / (b - a);
            return slope > 0f ? a + (targetHeight - heightA) / slope : float.NaN;
        }

        /// <summary>
        /// Largest font size at which every line of the current set still fits
        /// <paramref name="targetWidth"/> — and so, with word wrap on, the largest at which line
        /// breaking still produces this set. A line's start margin eats the same width its ink does.
        /// </summary>
        private float WidthCapOfCurrentLines(float targetWidth)
        {
            var lineCnt = buf.lines.count;
            var linesData = buf.lines.data;
            var maxLineWidth = 0f;
            for (var i = 0; i < lineCnt; i++)
            {
                var width = linesData[i].width + linesData[i].startMargin;
                if (width > maxLineWidth) maxLineWidth = width;
            }

            return maxLineWidth > 0f ? targetWidth * buf.shapingFontSize / maxLineWidth : float.MaxValue;
        }

        /// <summary>The width line breaking runs against — in shaping units, so it moves with the font size only while wrapping.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float BreakWidthFor(float fontSize, float targetWidth, bool wordWrap)
            => wordWrap ? targetWidth / buf.GetGlyphScale(fontSize) : TextProcessSettings.FloatMax;

        /// <summary>Whether the line set in the buffers is the one these break inputs produce.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool FitBreakMatches(float fontSize, float targetWidth, bool wordWrap)
            => fitBreakValid && fitBreakMaxWidth == BreakWidthFor(fontSize, targetWidth, wordWrap);

        /// <summary>
        /// Fitted height at one size, re-breaking only when the break inputs actually moved — with
        /// word wrap off they never do, and a line-height budget never touches them.
        /// </summary>
        private float FitHeightAt(float fontSize, float targetWidth, in TextProcessSettings settings, float fitLh)
        {
            if (!FitBreakMatches(fontSize, targetWidth, settings.enableWordWrap))
                BreakForFit(fontSize, targetWidth, settings.enableWordWrap, true);

            return MeasureCurrentLines(fontSize, targetWidth, settings, fitLh);
        }

        /// <summary>
        /// Fitted height of the line set in the buffers, measured at <paramref name="fontSize"/>
        /// whether or not that is the size it was broken for, or <see cref="float.MaxValue"/> when
        /// word wrap is off and a line overflows the width.
        /// </summary>
        private float MeasureCurrentLines(float fontSize, float targetWidth,
            in TextProcessSettings settings, float fitLh)
        {
            if (!settings.enableWordWrap && fontSize > WidthCapOfCurrentLines(targetWidth))
                return float.MaxValue;

            ComputeLineHeights(fontSize, settings.layout.lineSpacing, settings.layout.leadingDistribution,
                settings.layout.lineHeightMode, settings.layout.lineHeightScale, fitLh);
            return HeightFromComputedLines(fontSize, settings.layout);
        }

        private void BreakForFit(float fontSize, float targetWidth, bool wordWrap, bool measureOnly)
        {
            var maxWidth = BreakWidthFor(fontSize, targetWidth, wordWrap);

            buf.lines.count = 0;
            buf.orderedRuns.count = 0;
            buf.positionedGlyphs.count = 0;

            BreakLines(maxWidth, buf.cpWidths.Span, measureOnly);

            if (measureOnly) linesCache.Invalidate();
            else linesCache.Set(targetWidth, fontSize, wordWrap);
            positionsCache.valid = false;

            fitBreakMaxWidth = maxWidth;
            fitBreakValid = true;
        }

        /// <summary>Leaves the buffers holding a renderable line set with line heights at the chosen size.</summary>
        private void CommitLines(float fontSize, float targetWidth, in TextProcessSettings settings, float fitLh)
        {
            BreakForFit(fontSize, targetWidth, settings.enableWordWrap, false);
            ComputeLineHeights(fontSize, settings.layout.lineSpacing, settings.layout.leadingDistribution,
                settings.layout.lineHeightMode, settings.layout.lineHeightScale, fitLh);
        }

        #endregion

        /// <summary>Runs the paragraph-level settings hooks once so repeated fit probes can reuse one configured value.</summary>
        internal TextProcessSettings PrepareSettings(TextProcessSettings settings)
        {
            configureSettings?.Invoke(ref settings);
            return settings;
        }

        /// <summary>
        /// Measures the fitted content height at one font size under the given fit adjustments,
        /// leaving those adjustments active for the next probe or the enclosing layout snapshot.
        /// <paramref name="preparedSettings"/> must come from <see cref="PrepareSettings"/>.
        /// Returns <see cref="float.MaxValue"/> when word wrap is off and a line overflows the width.
        /// </summary>
        internal float MeasureFitHeight(float fontSize, float targetWidth,
            in TextProcessSettings preparedSettings, in FitBudgets budgets)
        {
            if (!hasValidFirstPassData || buf.shapingFontSize <= 0) return 0f;
            ApplyFitProbe(in budgets);
            return FitHeightAt(fontSize, targetWidth, preparedSettings, budgets.lineHeightScale);
        }

        /// <summary>Opens a run of fit probes: the next one re-breaks rather than trusting the line set it finds.</summary>
        internal void BeginFitProbes() => fitBreakValid = false;

        #region Fit adjustments

        private FitBudgets appliedFit = FitBudgets.Identity;

        /// <summary>Gets the fit adjustments active for shaped advances and line-height calculations.</summary>
        internal FitBudgets AppliedFit => appliedFit;

        /// <summary>
        /// Bakes the given fit adjustments into shaped advances (snapshotting pristine values on
        /// first use), reprojects codepoint widths, and invalidates line/position caches. The
        /// line-height part is stored and consumed by every subsequent height computation.
        /// Idempotent per value; call with <see cref="FitBudgets.Identity"/> to restore.
        /// </summary>
        internal void ApplyFitAdjustments(in FitBudgets budgets)
            => ApplyFitAdjustments(in budgets, false);

        private void ApplyFitProbe(in FitBudgets budgets)
            => ApplyFitAdjustments(in budgets, true);

        private void ApplyFitAdjustments(in FitBudgets budgets, bool retainBase)
        {
            var hasBaked = buf.fitBaseAdvances.count > 0;
            var advancesChanged = appliedFit.trackingEm != budgets.trackingEm
                || appliedFit.glyphScale != budgets.glyphScale;
            var lineHeightChanged = appliedFit.lineHeightScale != budgets.lineHeightScale;

            if (!advancesChanged && !lineHeightChanged)
            {
                if (!retainBase && budgets.IsAdvanceIdentity)
                    buf.fitBaseAdvances.count = 0;
                return;
            }

            if (advancesChanged)
            {
                if (!budgets.IsAdvanceIdentity)
                {
                    EnsureFitSnapshot();
                    WriteFitAdvances(budgets.glyphScale, budgets.trackingEm * buf.shapingFontSize);
                }
                else if (hasBaked)
                {
                    WriteFitAdvances(1f, 0f);
                }
            }
            else if (lineHeightChanged)
            {
                positionsCache.Invalidate();
            }

            if (!retainBase && budgets.IsAdvanceIdentity)
                buf.fitBaseAdvances.count = 0;
            appliedFit = budgets;
        }

        /// <summary>
        /// Discards fit state without touching advances. Legal only when a first-pass re-shape is
        /// about to rebuild every shaped glyph from raw shaping data.
        /// </summary>
        private void DropFitState()
        {
            buf.fitBaseAdvances.count = 0;
            appliedFit = FitBudgets.Identity;
        }

        /// <summary>
        /// Restores pristine shaped advances and neutral fit state. No-op when nothing is applied.
        /// </summary>
        internal void ResetFitAdjustments()
            => ApplyFitAdjustments(FitBudgets.Identity);

        private void EnsureFitSnapshot()
        {
            if (buf.fitBaseAdvances.count > 0) return;

            var glyphCount = buf.shapedGlyphs.count;
            buf.fitBaseAdvances.EnsureCount(glyphCount);
            var baseAdv = buf.fitBaseAdvances.data;
            var glyphs = buf.shapedGlyphs.data;
            for (var g = 0; g < glyphCount; g++)
                baseAdv[g] = glyphs[g].advanceX;
            buf.fitBaseAdvances.count = glyphCount;
        }

        /// <summary>
        /// Rewrites every glyph advance as <c>base × scale (+ tracking on non-zero advances)</c>
        /// from the pristine snapshot, recomputes run widths, and reprojects codepoint widths.
        /// <c>(1, 0)</c> restores the snapshot exactly.
        /// </summary>
        private void WriteFitAdvances(float scale, float trackingPx)
        {
            layoutHiddenMask = 0;
            effectiveWidths.FakeClear();
            suppressedLayoutBreaks.FakeClear();

            var baseAdv = buf.fitBaseAdvances.data;
            var glyphs = buf.shapedGlyphs.data;
            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;
            var cpCount = buf.codepoints.count;
            buf.cpWidths.EnsureCount(cpCount);
            var widths = buf.cpWidths.data;
            Array.Clear(widths, 0, cpCount);

            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;
                var width = 0f;
                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    var adv = baseAdv[g];
                    if (adv != 0f) adv = adv * scale + trackingPx;
                    glyphs[g].advanceX = adv;
                    width += adv;

                    var cluster = glyphs[g].cluster;
                    if ((uint)cluster < (uint)cpCount)
                        widths[cluster] += adv;
                }
                run.width = width;
            }

            InvalidateLayoutData();
        }

        #endregion

        private void EnsureLinesInternal(float width, float fontSize, bool wordWrap, ReadOnlySpan<float> cpWidths,
            float fitLineHeightScale = -1f)
        {
            buf.lines.count = 0;
            buf.orderedRuns.count = 0;
            buf.positionedGlyphs.count = 0;
            positionsCache.valid = false;

            var effectiveMaxWidth = BreakWidthFor(fontSize, width, wordWrap);
            BreakLines(effectiveMaxWidth, cpWidths);

            linesCache.Set(width, fontSize, wordWrap);
            fitBreakMaxWidth = effectiveMaxWidth;
            fitBreakValid = true;

            ComputeLineHeights(fontSize, 0f, fitLineHeightScale: fitLineHeightScale);
        }

        private void BreakLines(float maxWidth, ReadOnlySpan<float> cpWidths, bool measureOnly = false)
        {
            UniTextDebug.BeginSample("TextProcessor.BreakLines");
            pendingHiddenMask = 0;
            layoutHiddenMask = 0;
            suppressedLayoutBreaks.FakeClear();

            try
            {
                RunLineBreak(maxWidth, cpWidths, measureOnly, 0);

                currentLineBreakWidth = maxWidth;
                collectingHiddenLayout = true;
                try
                {
                    linesBroken?.Invoke();
                }
                finally
                {
                    collectingHiddenLayout = false;
                }

                var hiddenMask = pendingHiddenMask;
                if (hiddenMask == 0 || !BuildEffectiveWidths(cpWidths, hiddenMask))
                    return;

                SuppressHiddenBreaks(hiddenMask, true);
                applyingHiddenLayout = true;
                layoutHiddenMask = hiddenMask;
                try
                {
                    RunLineBreak(maxWidth, effectiveWidths.data.AsSpan(0, buf.codepoints.count),
                        measureOnly, hiddenMask);
                    linesBroken?.Invoke();
                }
                finally
                {
                    applyingHiddenLayout = false;
                }
            }
            finally
            {
                collectingHiddenLayout = false;
                applyingHiddenLayout = false;
                pendingHiddenMask = 0;
                RestoreSuppressedLayoutBreaks();
                UniTextDebug.EndSample();
            }
        }

        private void RunLineBreak(float maxWidth, ReadOnlySpan<float> cpWidths, bool measureOnly,
            byte hiddenMask)
        {
            buf.lines.count = 0;
            buf.orderedRuns.count = 0;

            var linesArr = buf.lines.data;
            var orderedRunsArr = buf.orderedRuns.data;
            var lineCnt = buf.lines.count;
            var orderedRunCnt = buf.orderedRuns.count;

            var cpCount = buf.codepoints.count;

            var marginsSpan = buf.startMargins.count >= cpCount
                ? buf.startMargins.data.AsSpan(0, cpCount)
                : ReadOnlySpan<float>.Empty;

            LineBreaker.BreakLines(
                buf.codepoints.Span,
                buf.shapedRuns.Span,
                buf.shapedGlyphs.Span,
                cpWidths,
                buf.breakOpportunities.Span,
                SortedSegmentBreaks(),
                maxWidth,
                buf.paragraphs.Span,
                ref linesArr, ref lineCnt,
                ref orderedRunsArr, ref orderedRunCnt,
                marginsSpan,
                hiddenMask == 0 ? ReadOnlySpan<byte>.Empty : buf.hiddenClusters.Span,
                hiddenMask,
                measureOnly);

            buf.lines.data = linesArr;
            buf.orderedRuns.data = orderedRunsArr;
            buf.lines.count = lineCnt;
            buf.orderedRuns.count = orderedRunCnt;
            buf.SetLineAdvanceCount(0);

            CatZones.layout.MeowFormat("[TextProcessor] BreakLines: {0} lines, maxWidth={1:F0}", lineCnt, maxWidth);
        }

        private ReadOnlySpan<TextRange> SortedSegmentBreaks()
        {
            var count = buf.segmentBreaks.count;
            if (count == 0) return ReadOnlySpan<TextRange>.Empty;

            var data = buf.segmentBreaks.data;
            for (var i = 1; i < count; i++)
            {
                var cur = data[i];
                var j = i - 1;
                while (j >= 0 && data[j].start > cur.start)
                {
                    data[j + 1] = data[j];
                    j--;
                }
                data[j + 1] = cur;
            }

            return data.AsSpan(0, count);
        }

        private float BlockOverReserve => buf.lines.count > 0 ? buf.lines[0].overReserve : 0f;

        private float BlockUnderReserve
            => buf.lines.count > 0 ? buf.lines[buf.lines.count - 1].underReserve : 0f;

        /// <summary>Reserves extra leading on one side of a specific line for boundary decorations (over/under ruby, etc.): <paramref name="over"/> above the line, <paramref name="under"/> below it. Added to that line's outer gap, or to the block edge for the first/last line. Largest request per side wins. Called from a modifier's line-height callback.</summary>
        public void ReserveLineSpace(int lineIndex, float over, float under)
        {
            if ((uint)lineIndex >= (uint)buf.lines.count) return;
            ref var line = ref buf.lines.data[lineIndex];
            if (over > line.overReserve) line.overReserve = over;
            if (under > line.underReserve) line.underReserve = under;
        }

        /// <summary>
        /// Reserves <paramref name="width"/> shaping units beside every line for something the text
        /// holds room for rather than wraps — a label a frontier keeps a place for, and its like. Added
        /// to the width the text reports, so a host sizing to that width leaves the room instead of
        /// handing back exactly the text and forcing whoever reserves to take it out of the content.
        /// Largest request wins, and requests last until the next parse re-declares them. Called from
        /// a modifier's shaped-phase callback, over widths no allocated width can move — a reserve
        /// answering to the width it helps decide would chase itself.
        /// </summary>
        public void ReserveLineWidth(float width)
        {
            if (width > lineWidthReserve) lineWidthReserve = width;
        }

        /// <summary>Adds a signed <paramref name="amount"/> to the gap between <paramref name="lineIndex"/> and the following line — negative pulls them together, down to full overlap; the gap never inverts. Requests sum, unlike the largest-wins <see cref="ReserveLineSpace"/>, so a caller composing several of its own values resolves them before calling. Out-of-range indices are ignored, which is what keeps block edges untouched. Called from a modifier's line-height callback.</summary>
        public void AddLineGap(int lineIndex, float amount)
        {
            if ((uint)lineIndex >= (uint)buf.lines.count) return;
            buf.lines.data[lineIndex].gap += amount;
        }

        private void ComputeLineHeights(float fontSize, float lineSpacing,
            LeadingDistribution distribution = LeadingDistribution.HalfLeading,
            LineHeightMode lineHeightMode = LineHeightMode.Content, float lineHeightScale = 1.2f,
            float fitLineHeightScale = -1f)
        {
            if (fitLineHeightScale <= 0f) fitLineHeightScale = appliedFit.lineHeightScale;
            var lineCount = buf.lines.count;
            var lines = buf.lines.data;

            buf.SetLineAdvanceCount(0);
            for (var i = 0; i < lineCount; i++)
            {
                lines[i].overReserve = 0f;
                lines[i].underReserve = 0f;
                lines[i].gap = 0f;
            }

            if (lineCount == 0)
            {
                heightCache.rawHeight = 0;
                heightCache.fontSize = fontSize;
                heightCache.lineHeightMode = lineHeightMode;
                heightCache.lineHeightScale = lineHeightScale;
                heightCache.fitLineHeightScale = fitLineHeightScale;
                return;
            }

            fontProvider.GetLineMetrics(fontSize, out var mainAscender, out var mainDescender, out var mainLineHeight);

            var sizeScales = buf.GetAttributeData<PooledArrayAttribute<float>>(AttributeKeys.Size)?.buffer.data;

            var orderedRuns = buf.orderedRuns.data;
            var totalLineAdvances = 0f;

            for (var i = 0; i < lineCount; i++)
            {
                ref readonly var line = ref lines[i];
                var lineMode = lineHeightMode;
                var lineScale = lineHeightScale;
                if (resolveLineHeight?.HasSubscribers == true)
                {
                    var modeContext = new LineHeightModeContext
                    {
                        lineIndex = i,
                        startCluster = line.range.start,
                        endCluster = line.range.End,
                        mode = lineMode,
                        scale = lineScale
                    };
                    resolveLineHeight.Invoke(ref modeContext);
                    lineMode = modeContext.mode;
                    lineScale = modeContext.scale;
                }
                float h = MaxLineHeightOnLine(line, orderedRuns, fontSize, mainLineHeight, sizeScales, lineMode, lineScale) + lineSpacing;
                if (calculateLineHeight?.HasSubscribers == true)
                {
                    var heightContext = new LineHeightContext
                    {
                        lineIndex = i,
                        startCluster = line.range.start,
                        endCluster = line.range.End,
                        fontSize = fontSize,
                        lineAdvance = h
                    };
                    calculateLineHeight.Invoke(ref heightContext);
                    h = heightContext.lineAdvance;
                }
                if (fitLineHeightScale != 1f) h *= fitLineHeightScale;
                lines[i].advance = h;
            }

            heightCache.effectiveFirstLineHeight = lines[0].advance;
            heightCache.effectiveLastLineHeight = lines[lineCount - 1].advance;

            var prevH = lines[0].advance;
            for (var i = 0; i < lineCount - 1; i++)
            {
                var currH = prevH;
                var nextH = lines[i + 1].advance;
                prevH = nextH;

                var advance = distribution switch
                {
                    LeadingDistribution.LeadingAbove => nextH,
                    LeadingDistribution.LeadingBelow => currH,
                    _ => (currH + nextH) * 0.5f
                };

                advance += lines[i + 1].overReserve + lines[i].underReserve + lines[i].gap;
                if (advance < 0f) advance = 0f;

                lines[i].advance = advance;
                totalLineAdvances += advance;
            }

            lines[lineCount - 1].advance = 0f;

            var runningAdvance = 0f;
            for (var i = 0; i < lineCount; i++)
            {
                runningAdvance += lines[i].advance;
                lines[i].advancePrefix = runningAdvance;
            }

            buf.SetLineAdvanceCount(lineCount);

            heightCache.rawHeight = mainAscender - mainDescender + totalLineAdvances;
            heightCache.mainAscender = mainAscender;
            heightCache.mainDescender = mainDescender;
            heightCache.fontSize = fontSize;
            heightCache.lineHeightMode = lineHeightMode;
            heightCache.lineHeightScale = lineHeightScale;
            heightCache.fitLineHeightScale = fitLineHeightScale;
        }

        /// <summary>
        /// Line-box height for one line under the active <see cref="LineHeightMode"/>:
        /// <see cref="LineHeightMode.Content"/> grows to the tallest font used on the line (CSS inline-layout
        /// model), with the primary font's line height as the lower bound; <see cref="LineHeightMode.Primary"/>
        /// uses the primary font's height only; <see cref="LineHeightMode.Scaled"/> pins it to
        /// <paramref name="lineHeightScale"/> × font size. Every mode is then measured at the line's own
        /// effective size — the largest size factor on it — so a line whose visible content is entirely
        /// scaled follows that scale in both directions. The factor applies uniformly to all fonts on the
        /// line rather than per run, which over-estimates only when the largest factor and the tallest
        /// font sit on different runs.
        /// </summary>
        private float MaxLineHeightOnLine(in TextLine line, ShapedRun[] orderedRuns, float fontSize, float primaryLineHeight,
            float[] sizeScales, LineHeightMode mode = LineHeightMode.Content, float lineHeightScale = 1.2f)
        {
            var sizeScale = MaxSizeScaleOnLine(line, sizeScales);

            if (mode == LineHeightMode.Primary) return primaryLineHeight * sizeScale;
            if (mode == LineHeightMode.Scaled) return lineHeightScale * fontSize * sizeScale;

            var max = primaryLineHeight;
            var runEnd = line.runStart + line.runCount;
            var lastFontId = -1;
            for (var r = line.runStart; r < runEnd; r++)
            {
                var fontId = orderedRuns[r].fontId;
                if (fontId == lastFontId) continue;
                lastFontId = fontId;

                var font = fontProvider.GetFont(fontId);
                if (font is null) continue;

                var scale = fontProvider.MetricScale(font, fontSize);
                var faceInfo = font.FaceInfo;
                var lh = faceInfo.lineHeight * scale;
                if (lh <= 0)
                    lh = (faceInfo.ascentLine - faceInfo.descentLine) * scale * 1.2f;

                if (lh > max) max = lh;
            }
            return max * sizeScale;
        }

        /// <summary>
        /// The largest font-size factor covering the line's visible content, or 1 when none does.
        /// Trailing line-break controls carry no ink and are excluded, so a break outside the scaled
        /// range does not hold the line at the base size; replacement clusters are excluded because
        /// their owner reserves their real geometry.
        /// </summary>
        private float MaxSizeScaleOnLine(in TextLine line, float[] sizeScales)
        {
            if (sizeScales == null) return 1f;

            var codepoints = buf.codepoints.data;
            var end = Math.Min(line.range.End, Math.Min(sizeScales.Length, buf.codepoints.count));
            var start = line.range.start;
            while (end > start && IsLineBreakControl(codepoints[end - 1])) end--;

            var hidden = buf.hiddenClusters.data;
            var hiddenCount = buf.hiddenClusters.count;

            var max = 0f;
            for (var i = start; i < end; i++)
            {
                if ((uint)i < (uint)hiddenCount
                    && (hidden[i] & (HiddenClusterBits.Replacement | layoutHiddenMask)) != 0)
                    continue;

                var scale = sizeScales[i];
                if (scale <= 0f) scale = 1f;
                if (scale > max) max = scale;
            }

            return max > 0f ? max : 1f;
        }

        private static bool IsLineBreakControl(int codepoint)
        {
            var cls = UnicodeData.Provider.GetLineBreakClass(codepoint);
            return cls == LineBreakClass.BK || cls == LineBreakClass.CR ||
                   cls == LineBreakClass.LF || cls == LineBreakClass.NL;
        }

        private void LayoutText(TextProcessSettings settings)
        {
            UniTextDebug.BeginSample("TextProcessor.LayoutText");
            buf.positionedGlyphs.count = 0;
            buf.positionedGlyphs.EnsureCapacity(buf.shapedGlyphs.count);

            ComputeLineHeights(settings.fontSize, settings.layout.lineSpacing, settings.layout.leadingDistribution,
                settings.layout.lineHeightMode, settings.layout.lineHeightScale);

            fontProvider.GetLineMetrics(settings.fontSize, out var ascender, out var descender, out var lineHeight);
            var capHeight = fontProvider.GetCapHeight(settings.fontSize);
            fontProvider.GetTypoMetrics(settings.fontSize, out var typoAscent, out var typoDescent);
            var xHeight = fontProvider.GetXHeight(settings.fontSize);
            Layout.SetFontMetrics(ascender, descender, lineHeight, buf.GetGlyphScale(settings.fontSize), capHeight, typoAscent, typoDescent, xHeight);

            Layout.SetLayoutSettings(settings.layout);
            Layout.SetEffectiveLineHeights(heightCache.effectiveFirstLineHeight, heightCache.effectiveLastLineHeight,
                BlockOverReserve, BlockUnderReserve);
            Layout.SetLineStyleResolver(resolveLineStyle);
            Layout.SetHiddenLayout(layoutHiddenMask == 0 ? null : buf.hiddenClusters.data,
                layoutHiddenMask == 0 ? 0 : buf.hiddenClusters.count, layoutHiddenMask);

            var blockGlyphScale = appliedFit.glyphScale;
            var mayScaleGlyphs = blockGlyphScale != 1f
                || settings.layout.horizontalAlignment == HorizontalAlignment.Justify
                || resolveLineStyle?.HasSubscribers == true;
            if (mayScaleGlyphs)
            {
                var xScaleSpan = buf.PrepareGlyphXScales();
                xScaleSpan.Fill(blockGlyphScale != 1f ? blockGlyphScale : 0f);
                Layout.SetFitState(buf.glyphXScales.data, buf.glyphXScales.count,
                    blockGlyphScale, settings.fontSize);
            }
            else
            {
                buf.glyphXScales.count = 0;
                Layout.SetFitState(null, 0, 1f, settings.fontSize);
            }

            var glyphCnt = buf.positionedGlyphs.count;
            Layout.Layout(
                buf.lines.Span,
                buf.orderedRuns.Span,
                buf.shapedGlyphs.Span,
                buf.codepoints.Span,
                heightCache.rawHeight,
                buf.positionedGlyphs.data, ref glyphCnt,
                out resultWidth, out resultHeight);
            buf.positionedGlyphs.count = glyphCnt;

            if (blockGlyphScale == 1f && !Layout.WroteGlyphXScales)
                buf.glyphXScales.count = 0;

            UpdateOuterLineBounds();
            FillParagraphPositionSlices();

            UniTextDebug.EndSample();
        }

        private void UpdateOuterLineBounds()
        {
            firstLineTop = 0f;
            lastLineBottom = 0f;

            var lines = buf.lines;
            if (lines.count == 0) return;

            var glyphs = buf.positionedGlyphs;
            ref readonly var firstLine = ref lines[0];
            if (firstLine.glyphCount > 0)
            {
                firstLineTop = glyphs[firstLine.glyphStart].top;
                var glyphEnd = firstLine.glyphStart + firstLine.glyphCount;
                for (var i = firstLine.glyphStart + 1; i < glyphEnd; i++)
                    if (glyphs[i].top < firstLineTop) firstLineTop = glyphs[i].top;
            }

            ref readonly var lastLine = ref lines[lines.count - 1];
            if (lastLine.glyphCount <= 0) return;

            lastLineBottom = glyphs[lastLine.glyphStart].bottom;
            var lastGlyphEnd = lastLine.glyphStart + lastLine.glyphCount;
            for (var i = lastLine.glyphStart + 1; i < lastGlyphEnd; i++)
                if (glyphs[i].bottom > lastLineBottom) lastLineBottom = glyphs[i].bottom;
        }

        /// <summary>Fills each paragraph's positioned-glyph slice and its exact vertical band (min top / max bottom over the slice's glyph boxes).</summary>
        private void FillParagraphPositionSlices()
        {
            var table = buf.paragraphs.data;
            var paragraphCount = buf.paragraphs.count;
            var lines = buf.lines.data;
            var glyphs = buf.positionedGlyphs.data;

            var runningEnd = 0;
            for (var p = 0; p < paragraphCount; p++)
            {
                ref var para = ref table[p];
                if (para.lineCount > 0)
                {
                    ref readonly var first = ref lines[para.lineStart];
                    ref readonly var last = ref lines[para.lineStart + para.lineCount - 1];
                    para.posStart = first.glyphStart;
                    para.posCount = last.glyphStart + last.glyphCount - first.glyphStart;
                    runningEnd = para.posStart + para.posCount;

                    var top = float.PositiveInfinity;
                    var bottom = float.NegativeInfinity;
                    for (var g = para.posStart; g < para.posStart + para.posCount; g++)
                    {
                        if (glyphs[g].top < top) top = glyphs[g].top;
                        if (glyphs[g].bottom > bottom) bottom = glyphs[g].bottom;
                    }

                    if (float.IsPositiveInfinity(top) || float.IsNegativeInfinity(bottom))
                    {
                        para.topY = float.NegativeInfinity;
                        para.bottomY = float.PositiveInfinity;
                    }
                    else
                    {
                        para.topY = top;
                        para.bottomY = bottom;
                    }
                }
                else
                {
                    para.posStart = runningEnd;
                    para.posCount = 0;
                    para.topY = float.NegativeInfinity;
                    para.bottomY = float.PositiveInfinity;
                }
            }
        }

        private void UpdateLineWidths()
        {
            var lines = buf.lines.data;
            var lineCount = buf.lines.count;
            var runs = buf.orderedRuns.data;
            var glyphs = buf.shapedGlyphs.data;
            var cps = buf.codepoints.data;
            var hidden = layoutHiddenMask == 0 ? ReadOnlySpan<byte>.Empty : buf.hiddenClusters.Span;

            for (var i = 0; i < lineCount; i++)
            {
                ref var line = ref lines[i];
                var totalWidth = 0f;
                var runEnd = line.runStart + line.runCount;
                if (layoutHiddenMask == 0)
                {
                    for (var r = line.runStart; r < runEnd; r++)
                        totalWidth += runs[r].width;
                }
                else
                {
                    for (var r = line.runStart; r < runEnd; r++)
                    {
                        var run = runs[r];
                        var glyphEnd = run.glyphStart + run.glyphCount;
                        for (var g = run.glyphStart; g < glyphEnd; g++)
                            if (!HiddenClusterBits.IsHidden(hidden, glyphs[g].cluster, layoutHiddenMask))
                                totalWidth += glyphs[g].advanceX;
                    }
                }

                float trailingWs = 0;
                var lineEnd = line.range.start + line.range.length - 1;
                for (var cp = lineEnd; cp >= line.range.start; cp--)
                {
                    if (HiddenClusterBits.IsHidden(hidden, cp, layoutHiddenMask)) continue;
                    if (!LineBreaker.IsHangingWhitespace(cps[cp])) break;
                    for (var r = line.runStart; r < runEnd; r++)
                    {
                        var run = runs[r];
                        if (cp < run.range.start || cp >= run.range.End) continue;
                        var gEnd = run.glyphStart + run.glyphCount;
                        for (var g = run.glyphStart; g < gEnd; g++)
                            if (glyphs[g].cluster == cp)
                                trailingWs += glyphs[g].advanceX;
                        break;
                    }
                }

                line.width = totalWidth - trailingWs;
                line.trailingWhitespace = trailingWs;
            }

            linesBroken?.Invoke();
        }
    }
}
