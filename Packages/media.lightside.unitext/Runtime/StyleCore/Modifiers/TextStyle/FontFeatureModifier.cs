using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Turns OpenType features on or off over a text range.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parameter: a comma-separated list of settings, each a four-character OpenType tag with an
    /// optional value — <c>kern 0</c> (no kerning), <c>-liga</c> (no standard ligatures),
    /// <c>+dlig</c>, <c>tnum</c> (tabular figures), <c>ss01 2</c>. A bare tag means 1, a
    /// <c>-</c> prefix means 0, and the value may follow a space, colon or equals sign.
    /// </para>
    /// <para>
    /// Features are merged, not replaced: overlapping ranges and other feature-writing modifiers
    /// (small caps, superscript) keep their settings, and the innermost value wins a shared tag.
    /// Tags the font does not carry are ignored by the shaper.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 1)]
    [TypeDescription("Turns OpenType features on or off (kerning, ligatures, figures, stylistic sets).")]
    [GenerateParameters]
    public partial class FontFeatureModifier : BaseModifier
    {
        /// <summary>Feature settings, e.g. <c>kern 0, -liga</c>. A per-range <c>&lt;feature=…&gt;</c> value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkTextDirty))] private string features = "kern 0";

        protected override void OnApply(in RangeApplyContext context)
        {
            var specification = Param.Features.Resolve(this, in context);
            if (string.IsNullOrWhiteSpace(specification)) return;

            buffers.AddFontFeatures(context.Segment.Range, FontFeatureRegistry.Register(specification));
        }
    }
}
