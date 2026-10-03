using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// One budget of the auto-size fit ladder: an adjustment the component spends, in list order,
    /// before reducing the font size. Each step maps a fraction of its configured budget onto the
    /// shared <see cref="FitBudgets"/> the layout core consumes.
    /// </summary>
    [Serializable]
    [StateHierarchy]
    [StateSource]
    public abstract partial class FitStep
    {
        /// <summary>Writes this step's budget, scaled by <paramref name="fraction"/> (0–1), into <paramref name="budgets"/>.</summary>
        internal abstract void Apply(ref FitBudgets budgets, float fraction);
    }

    /// <summary>
    /// Tightens letter spacing, up to a limit, before the font size shrinks. The reduction applies
    /// uniformly to every non-zero glyph advance.
    /// </summary>
    [Serializable]
    [TypeDescription("Tightens letter spacing before the font size shrinks.")]
    public sealed partial class TrackingFitStep : FitStep
    {
        /// <summary>Largest allowed tracking reduction, in em. Apple-style tightening sits near 0.02.</summary>
        [SerializeField, StateProperty, Range(0f, 0.1f)]
        [Tooltip("Maximum letter-spacing reduction, in em.")]
        private float maxCompression = 0.02f;

        internal override void Apply(ref FitBudgets budgets, float fraction)
            => budgets.trackingEm = -maxCompression * fraction;
    }

    /// <summary>
    /// Compresses glyphs horizontally, up to a limit, before the font size shrinks — the hz /
    /// InDesign glyph-scaling lever. Quads and advances narrow together, so the text keeps its
    /// height while gaining columns.
    /// </summary>
    /// <remarks>
    /// Values beyond ~2–3% visibly distort letterforms (vertical strokes thin while horizontal
    /// ones keep their weight) and stretch SDF effects anisotropically; print systems cap this
    /// lever at 2%.
    /// </remarks>
    [Serializable]
    [TypeDescription("Compresses glyph width (hz-style) before the font size shrinks.")]
    public sealed partial class GlyphScaleFitStep : FitStep
    {
        /// <summary>Largest allowed width reduction, in percent. 2 matches the pdfTeX/InDesign norm.</summary>
        [SerializeField, StateProperty, Range(0f, 10f)]
        [Tooltip("Maximum glyph-width compression, in percent. Above 3% letterforms visibly distort.")]
        private float maxCompression = 2f;

        internal override void Apply(ref FitBudgets budgets, float fraction)
            => budgets.glyphScale = 1f - maxCompression * 0.01f * fraction;
    }

    /// <summary>
    /// Compresses line height, down to a limit, before the font size shrinks. Line boxes keep
    /// their glyphs; only the vertical advance between lines contracts.
    /// </summary>
    [Serializable]
    [TypeDescription("Compresses line height before the font size shrinks.")]
    public sealed partial class LineHeightFitStep : FitStep
    {
        /// <summary>Smallest allowed line-height fraction of the natural value, in percent.</summary>
        [SerializeField, StateProperty, Range(50f, 100f)]
        [Tooltip("Minimum line height, as a percentage of the natural line height.")]
        private float minScale = 90f;

        internal override void Apply(ref FitBudgets budgets, float fraction)
            => budgets.lineHeightScale = 1f - (1f - minScale * 0.01f) * fraction;
    }
}
