using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Normalizes mixed fonts to a common visual size by matching x-height or cap-height
    /// (CSS <c>font-size-adjust</c>). Target 0 = match the primary font; &gt;0 = match that aspect
    /// value (metric ÷ font size) for every font. Apply whole-text.
    /// </summary>
    [Serializable]
    [TypeGroup("Layout", 4)]
    [TypeDescription("Scales fonts to a shared x-height / cap-height so mixed fonts look consistent.")]
    [GenerateParameters]
    public sealed partial class FontSizeMatchModifier : BaseModifier
    {
        /// <summary>Which metric fonts are matched on. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkTextDirty))] private FontNormalizeMetric metric = FontNormalizeMetric.XHeight;
        /// <summary>Target aspect value; 0 matches the primary font. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkTextDirty))] private float target;

        protected override void OnApply(in RangeApplyContext context)
        {
            uniText.FontProvider?.SetNormalization(
                Param.Metric.Resolve(this, in context),
                Param.Target.Resolve(this, in context));
        }

        protected override void OnDestroy()
            => uniText.FontProvider?.SetNormalization(UniTextSettings.FontNormalizeMetric, UniTextSettings.FontNormalizeTarget);
    }
}
