using System;
using UnityEngine;

namespace LightSide
{
    public abstract partial class UniTextBase
    {
        #region Cached Layout State

        /// <summary>
        /// Cached effective font size after auto-sizing. Zero means layout has not been computed;
        /// consumers fall back to <c>maxFontSize</c>/<c>fontSize</c> as appropriate.
        /// </summary>
        protected float cachedEffectiveFontSize;

        /// <summary>
        /// Cached preferred height — what the text wants at its unconstrained (main-pass) size.
        /// Not updated by the second pass: parent layout groups read this as "desired height",
        /// which must stay stable even when the rect is smaller than desired.
        /// </summary>
        protected float cachedPreferredHeight;

        private float cachedMainPassWidth;
        private float cachedMainPassFontSize;
        private bool hasValidMainPass;

        private float cachedSecondPassHeight;
        private bool hasValidSecondPass;

        #endregion

        #region Public API

        /// <summary>
        /// Height the text content needs inside the padded inner rect (accounts for auto-sizing).
        /// Does <b>not</b> include vertical <see cref="Padding"/> — this is the content-box height,
        /// matching CSS <c>height</c> with <c>box-sizing: content-box</c>. The <c>ILayoutElement</c>
        /// contract adds vertical padding so <c>ContentSizeFitter</c> sizes the outer
        /// <c>RectTransform</c> to fit content + padding.
        /// </summary>
        public float PreferredHeight => cachedPreferredHeight;

        /// <summary>
        /// Line height of the current font at the current size, available without any text —
        /// the CSS strut. An empty editable document measures and shows its caret one strut
        /// tall; an empty label stays zero.
        /// </summary>
        internal float StrutLineHeight
        {
            get
            {
                var core = PrimaryFontCore;
                if (core != null)
                {
                    var height = StrutOf(core.FaceInfo, core.FontScale, core.UnitsPerEm);
                    if (height > 0f) return height;
                }
                var primaryFont = PrimaryFont;
                if (primaryFont != null)
                {
                    var height = StrutOf(primaryFont.FaceInfo, primaryFont.FontScale, primaryFont.UnitsPerEm);
                    if (height > 0f) return height;
                }

                if (SystemFont.Default is { } systemDefault)
                {
                    var height = StrutOf(systemDefault.FaceInfo, systemDefault.FontScale, systemDefault.UnitsPerEm);
                    if (height > 0f) return height;
                }

                return CurrentFontSize;
            }
        }

        private float StrutOf(FaceInfo faceInfo, float fontScale, int unitsPerEm)
            => unitsPerEm > 0
                ? (faceInfo.ascentLine - faceInfo.descentLine) * CurrentFontSize * fontScale / unitsPerEm
                : 0f;

        /// <summary>Zero-width first line positioned by the component's block alignment.</summary>
        internal Rect GetEmptyLineRect()
        {
            var rect = GetPaddedRect();
            var height = StrutLineHeight;
            var isRtl = Buffers.baseDirection == TextDirection.RightToLeft;
            var x = horizontalAlignment switch
            {
                HorizontalAlignment.Center => rect.center.x,
                HorizontalAlignment.End => isRtl ? rect.xMin : rect.xMax,
                HorizontalAlignment.Left => rect.xMin,
                HorizontalAlignment.Right => rect.xMax,
                _ => isRtl ? rect.xMax : rect.xMin
            };
            var y = verticalAlignment switch
            {
                VerticalAlignment.Middle => rect.center.y - height * 0.5f,
                VerticalAlignment.Bottom => rect.yMin,
                _ => rect.yMax - height
            };
            return new Rect(x, y, 0f, height);
        }

        /// <summary>
        /// Measures the size text occupies under the given constraints and setting overrides —
        /// the displayed text, layout and mesh are left untouched.
        /// </summary>
        /// <remarks>
        /// Runs the component's full pipeline: styles and modifiers, font fallback, direction,
        /// word wrap; the text resolver is bypassed. Every non-null override in
        /// <paramref name="options"/> acts exactly as if set on the component (auto-size fits the
        /// font into <c>maxWidth</c> × <c>maxHeight</c> like into a real rect). Overrides live in a
        /// detached measurement state; component fields are never rewritten, and layout caches plus
        /// <see cref="UniTextDirty"/> are restored afterwards.
        /// Dimensions are outer (<see cref="Padding"/> included), so the result maps directly to
        /// a <c>RectTransform</c>. Measuring another <see cref="TextMeasureOptions.text"/>
        /// re-parses and re-shapes both texts — cache the result instead of calling every frame.
        /// Main thread only.
        /// </remarks>
        public Vector2 MeasureText(in TextMeasureOptions options = default)
        {
            var state = new TextMeasureState(this, in options);
            if (!CanWork) return state.PaddingSize;
            if (!ValidateAndInitialize()) return state.PaddingSize;

            var savedFlags = dirtyFlags;
            var snapshot = textProcessor.SnapshotLayout();
            var measureOtherText = options.text != null;
            var hadValidFirstPass = textProcessor.HasValidFirstPassData;
            var probeFirstPass = measureOtherText || !hadValidFirstPass ||
                                 !Mathf.Approximately(buffers.shapingFontSize, state.ShapingFontSize);

            try
            {
                if (probeFirstPass)
                {
                    ReadOnlySpan<char> cleanText;
                    if (measureOtherText)
                    {
                        attributeParser?.Parse(options.text);
                        if (attributeParser != null) cleanText = attributeParser.CleanTextSpan;
                        else cleanText = options.text;
                    }
                    else
                    {
                        cleanText = ParseOrGetParsedAttributes();
                    }

                    textProcessor.InvalidateFirstPassData();
                    textProcessor.EnsureFirstPass(cleanText, new TextProcessSettings
                    {
                        fontSize = state.ShapingFontSize,
                        baseDirection = TextDirection.Auto
                    });
                }

                return MeasureCore(options.maxWidth, options.maxHeight, in state);
            }
            finally
            {
                try
                {
                    if (probeFirstPass)
                    {
                        textProcessor.InvalidateFirstPassData();
                        if (measureOtherText) textIsParsed = false;
                        if (hadValidFirstPass && !sourceText.IsEmpty) DoFirstPass();
                    }
                }
                finally
                {
                    try
                    {
                        textProcessor.RestoreLayout(snapshot);
                    }
                    finally
                    {
                        dirtyFlags = savedFlags;
                    }
                }
            }
        }

        private Vector2 MeasureCore(float? maxWidth, float? maxHeight, in TextMeasureState state)
        {
            var paddingSize = state.PaddingSize;
            if (textProcessor == null || !textProcessor.HasValidFirstPassData)
                return paddingSize;

            var innerWidth = (maxWidth ?? float.PositiveInfinity) - paddingSize.x;
            var innerHeight = (maxHeight ?? float.PositiveInfinity) - paddingSize.y;
            if (innerWidth < 0) innerWidth = 0;
            if (innerHeight < 0) innerHeight = 0;

            float effectiveFontSize;
            var budgets = FitBudgets.Identity;
            if (!state.autoSize)
            {
                effectiveFontSize = state.fontSize;
            }
            else if (float.IsPositiveInfinity(innerHeight) && (state.wordWrap || float.IsPositiveInfinity(innerWidth)))
            {
                effectiveFontSize = state.maxFontSize;
            }
            else
            {
                var targetWidth = float.IsPositiveInfinity(innerWidth) ? TextProcessSettings.FloatMax : innerWidth;
                var targetHeight = float.IsPositiveInfinity(innerHeight) ? TextProcessSettings.FloatMax : innerHeight;
                var fitSettings = CreateProcessSettings(targetWidth, targetHeight, state.maxFontSize, state.wordWrap);
                effectiveFontSize = SolveFit(state.minFontSize, state.maxFontSize,
                    targetWidth, targetHeight, fitSettings, apply: false, out budgets);
            }

            return textProcessor.MeasureSizeCore(innerWidth, effectiveFontSize, state.wordWrap,
                measureTrailingWhitespace, in budgets) + paddingSize;
        }

        private readonly struct TextMeasureState
        {
            internal readonly float fontSize;
            internal readonly bool autoSize;
            internal readonly float minFontSize;
            internal readonly float maxFontSize;
            internal readonly bool wordWrap;
            private readonly Vector4 padding;

            internal TextMeasureState(UniTextBase text, in TextMeasureOptions options)
            {
                fontSize = Mathf.Max(0.01f, options.fontSize ?? text.fontSize);
                autoSize = options.autoSize ?? text.autoSize;
                minFontSize = Mathf.Max(0.01f, options.minFontSize ?? text.minFontSize);
                maxFontSize = Mathf.Max(0.01f, options.maxFontSize ?? text.maxFontSize);
                wordWrap = options.wordWrap ?? text.wordWrap;
                padding = options.padding ?? text.padding;
            }

            internal float ShapingFontSize => autoSize ? maxFontSize : fontSize;
            internal Vector2 PaddingSize => new(padding.x + padding.z, padding.y + padding.w);
        }

        #endregion

        #region Layout Computation

        /// <summary>
        /// Main layout pass. Computes the "preferred" state — <see cref="cachedPreferredHeight"/>
        /// and an initial <see cref="cachedEffectiveFontSize"/> — for the current rect width.
        /// Cached by width only: result is independent of <c>rect.height</c>.
        /// </summary>
        /// <remarks>
        /// Main-thread only. Canvas variant invokes this from
        /// <see cref="UnityEngine.UI.ILayoutElement.CalculateLayoutInputVertical"/>; world-space
        /// variant has it called implicitly by <see cref="EnsureLayoutFit"/>.
        /// </remarks>
        protected void EnsureLayoutComputed()
        {
            if (!CanWork || sourceText.IsEmpty || textProcessor == null || !textProcessor.HasValidFirstPassData)
            {
                hasValidMainPass = false;
                hasValidSecondPass = false;
                if (sourceText.IsEmpty)
                    cachedEffectiveFontSize = autoSize ? maxFontSize : fontSize;
                cachedPreferredHeight = IsDocumentHost ? StrutLineHeight : 0f;
                return;
            }

            var rect = GetPaddedRect();
            if (rect.width <= 0f)
            {
                hasValidMainPass = false;
                hasValidSecondPass = false;
                cachedPreferredHeight = 0f;
                return;
            }

            if (hasValidMainPass && Mathf.Approximately(cachedMainPassWidth, rect.width))
                return;

            textProcessor.ResetFitAdjustments();
            cachedEffectiveFontSize = GetEffectiveFontSize(rect.width, TextProcessSettings.FloatMax);
            cachedMainPassFontSize = cachedEffectiveFontSize;
            textProcessor.EnsureLines(rect.width, cachedEffectiveFontSize, wordWrap);

            var probeSize = (autoSize && wordWrap) ? maxFontSize : cachedEffectiveFontSize;
            cachedPreferredHeight = textProcessor.GetPreferredHeight(probeSize, 0f);

            cachedMainPassWidth = rect.width;
            hasValidMainPass = true;
            hasValidSecondPass = false;
        }

        /// <summary>
        /// Full layout: main pass then, if needed, shrinks <see cref="cachedEffectiveFontSize"/>
        /// under the current <c>rect.height</c>. Main-pass state comes from the width cache;
        /// second pass is cached by <c>rect.height</c> so repeat calls within a frame are free.
        /// </summary>
        /// <remarks>
        /// Main-thread only. Canvas variant invokes this from
        /// <see cref="UnityEngine.UI.ILayoutController.SetLayoutVertical"/>; world-space variant
        /// from the render pipeline before mesh generation.
        /// </remarks>
        protected void EnsureLayoutFit()
        {
            EnsureLayoutComputed();
            if (!hasValidMainPass) return;
            if (!autoSize) return;

            var rect = GetPaddedRect();
            if (rect.height <= 0f) return;
            if (rect.height >= cachedPreferredHeight - 0.01f)
            {
                cachedEffectiveFontSize = cachedMainPassFontSize;
                hasValidSecondPass = false;
                if (wordWrap && !textProcessor.AppliedFit.IsIdentity)
                {
                    textProcessor.ResetFitAdjustments();
                    textProcessor.EnsureLines(rect.width, cachedEffectiveFontSize, wordWrap);
                }
                return;
            }

            if (hasValidSecondPass && Mathf.Approximately(cachedSecondPassHeight, rect.height))
                return;

            var settings = CreateProcessSettings(rect.width, rect.height, maxFontSize, wordWrap);

            cachedEffectiveFontSize = SolveFit(rect.width, rect.height, settings, apply: true, out _);
            textProcessor.EnsureLines(rect.width, cachedEffectiveFontSize, wordWrap);

            cachedSecondPassHeight = rect.height;
            hasValidSecondPass = true;
        }

        private float GetEffectiveFontSize(float width, float height)
        {
            if (!autoSize) return fontSize;
            if (wordWrap) return maxFontSize;

            return SolveFit(width, height,
                CreateProcessSettings(width, height, maxFontSize, false), apply: true, out _);
        }

        /// <summary>
        /// Chooses the effective font size for the box in two phases: first the largest font size
        /// that fits with every <see cref="fitSteps"/> budget available, then the least ladder
        /// spending that still fits at that size — so a budget is never applied beyond what the
        /// achieved font size requires. With an empty ladder this is exactly the plain font-size
        /// search. When <paramref name="apply"/> is set, the chosen adjustments are baked into the
        /// processor for rendering; non-applying callers restore the surrounding layout transaction.
        /// </summary>
        private float SolveFit(float targetWidth, float targetHeight, TextProcessSettings settings,
            bool apply, out FitBudgets chosen)
            => SolveFit(minFontSize, maxFontSize, targetWidth, targetHeight, settings, apply, out chosen);

        private float SolveFit(float minSize, float maxSize, float targetWidth, float targetHeight,
            TextProcessSettings settings, bool apply, out FitBudgets chosen)
        {
            var steps = fitSteps;
            settings = textProcessor.PrepareSettings(settings);
            textProcessor.BeginFitProbes();
            float size;

            if (steps == null || steps.Count == 0)
            {
                chosen = FitBudgets.Identity;
                size = textProcessor.FindOptimalFontSizeCore(
                    minSize, maxSize, targetWidth, targetHeight, settings, in chosen);
            }
            else
            {
                size = maxSize;
                if (!TrySolveAtSize(maxSize, targetWidth, targetHeight, settings, out chosen))
                {
                    size = textProcessor.FindOptimalFontSizeCore(
                        minSize, maxSize, targetWidth, targetHeight, settings, in chosen);
                    TrySolveAtSize(size, targetWidth, targetHeight, settings, out chosen);
                }
            }

            if (apply) textProcessor.ApplyFitAdjustments(in chosen);
            return size;
        }

        /// <summary>
        /// Finds the least ladder spending that fits the box at a fixed font size: none when the
        /// text already fits; otherwise the shortest prefix of steps (in list order) whose full
        /// budgets fit, with the engaging step dialed down to the least fraction that still fits
        /// and every earlier step then dialed down to its own least fraction given the others — a
        /// step never stays spent beyond what the fit requires. False when every budget at full
        /// still overflows; <paramref name="chosen"/> is then every budget at full — the best effort.
        /// </summary>
        private bool TrySolveAtSize(float fontSize, float targetWidth, float targetHeight,
            in TextProcessSettings settings, out FitBudgets chosen)
        {
            var identity = FitBudgets.Identity;
            if (textProcessor.MeasureFitHeight(fontSize, targetWidth, settings, in identity) <= targetHeight)
            {
                chosen = identity;
                return true;
            }

            Span<float> fractions = stackalloc float[fitSteps.Count];

            var engaged = -1;
            for (var i = 0; i < fractions.Length; i++)
            {
                fractions[i] = 1f;
                if (FractionsFit(fractions, fontSize, targetWidth, targetHeight, settings))
                {
                    engaged = i;
                    break;
                }
            }

            if (engaged < 0)
            {
                chosen = ComposeFractions(fractions);
                return false;
            }

            MinimizeFraction(fractions, engaged, false, fontSize, targetWidth, targetHeight, settings);
            for (var j = 0; j < engaged; j++)
                MinimizeFraction(fractions, j, true, fontSize, targetWidth, targetHeight, settings);

            chosen = ComposeFractions(fractions);
            return true;
        }

        private FitBudgets ComposeFractions(ReadOnlySpan<float> fractions)
        {
            var budgets = FitBudgets.Identity;
            for (var i = 0; i < fractions.Length; i++)
                fitSteps[i].Apply(ref budgets, fractions[i]);
            return budgets;
        }

        private float FractionsHeight(ReadOnlySpan<float> fractions, float fontSize, float targetWidth,
            in TextProcessSettings settings)
        {
            var budgets = ComposeFractions(fractions);
            return textProcessor.MeasureFitHeight(fontSize, targetWidth, settings, in budgets);
        }

        private bool FractionsFit(ReadOnlySpan<float> fractions, float fontSize, float targetWidth,
            float targetHeight, in TextProcessSettings settings)
            => FractionsHeight(fractions, fontSize, targetWidth, settings) <= targetHeight;

        /// <summary>Fraction bracket below which a budget search stops refining.</summary>
        private const float FitFractionEpsilon = 1f / 256f;

        /// <summary>Probe cap for a budget search whose height model is a line-count staircase.</summary>
        private const int FitFractionIterations = 6;

        /// <summary>
        /// Dials one step's fraction down to the least that still fits, holding every other
        /// fraction fixed. <paramref name="tryZero"/> short-circuits with a single probe when the
        /// step is not needed at all alongside the others; without it the caller guarantees the
        /// current value fits and zero does not.
        /// </summary>
        private void MinimizeFraction(Span<float> fractions, int index, bool tryZero, float fontSize,
            float targetWidth, float targetHeight, in TextProcessSettings settings)
        {
            var lo = 0f;
            var hi = 1f;
            var lastFraction = float.NaN;
            var lastHeight = float.NaN;
            var prevFraction = float.NaN;
            var prevHeight = float.NaN;

            if (tryZero)
            {
                fractions[index] = 0f;
                lastHeight = FractionsHeight(fractions, fontSize, targetWidth, settings);
                if (lastHeight <= targetHeight) return;
                lastFraction = 0f;
            }

            for (var iter = 0; iter < FitFractionIterations && hi - lo > FitFractionEpsilon; iter++)
            {
                var next = InterpolateFraction(prevFraction, prevHeight, lastFraction, lastHeight, targetHeight);
                if (!(next > lo && next < hi)) next = (lo + hi) * 0.5f;

                fractions[index] = next;
                var height = FractionsHeight(fractions, fontSize, targetWidth, settings);

                prevFraction = lastFraction;
                prevHeight = lastHeight;
                lastFraction = next;
                lastHeight = height;

                if (height <= targetHeight) hi = next;
                else lo = next;
            }

            fractions[index] = hi;
        }

        /// <summary>
        /// The fraction an affine model through the last two measured points puts at
        /// <paramref name="targetHeight"/>, or NaN before two usable points exist or when the model
        /// does not fall — the caller bisects instead.
        /// </summary>
        private static float InterpolateFraction(float a, float heightA, float b, float heightB,
            float targetHeight)
        {
            if (float.IsNaN(heightA) || float.IsNaN(heightB)) return float.NaN;
            if (heightA >= float.MaxValue || heightB >= float.MaxValue) return float.NaN;

            var slope = (heightB - heightA) / (b - a);
            return slope < 0f ? b + (targetHeight - heightB) / slope : float.NaN;
        }

        private void InvalidateLayoutCache()
        {
            hasValidMainPass = false;
            hasValidSecondPass = false;
        }

        #endregion
    }

    /// <summary>
    /// Box constraints and setting overrides for <see cref="UniTextBase.MeasureText"/>. Every
    /// null field falls back to the component's current value, so <c>default</c> measures the
    /// current text, as configured, at its natural unconstrained size. Overrides behave exactly
    /// like the matching component settings (with <see cref="autoSize"/> on, <see cref="fontSize"/>
    /// is ignored and <see cref="minFontSize"/> / <see cref="maxFontSize"/> drive the fit).
    /// Dimensions are outer (<see cref="UniTextBase.Padding"/> included).
    /// </summary>
    public struct TextMeasureOptions
    {
        /// <summary>Markup text to measure. Null — the component's current text.</summary>
        public string text;

        /// <summary>Wrap width. Null — unconstrained.</summary>
        public float? maxWidth;

        /// <summary>Box height: with auto-size the font shrinks to fit <see cref="maxWidth"/> × this before measuring, like in a real rect. Null — unconstrained. Does not affect flow without auto-size.</summary>
        public float? maxHeight;

        public float? fontSize;
        public bool? autoSize;
        public float? minFontSize;
        public float? maxFontSize;
        public bool? wordWrap;
        public Vector4? padding;
    }
}
