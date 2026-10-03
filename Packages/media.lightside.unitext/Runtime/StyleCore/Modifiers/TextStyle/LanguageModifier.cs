using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies a BCP 47 language tag to a text range for OpenType-aware shaping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The language tag is forwarded to HarfBuzz as <c>hb_language_t</c>, which drives
    /// the OpenType <c>locl</c> (Localized Forms) GSUB feature. This is essential for
    /// pan-CJK fonts such as Noto Sans CJK and Source Han Sans, where a single code point
    /// renders with different region-specific glyphs (Simplified Chinese, Traditional Chinese,
    /// Japanese, Korean) depending on the language tag.
    /// </para>
    /// <para>
    /// Common tags: <c>zh-Hans</c>, <c>zh-Hant</c>, <c>zh-HK</c>, <c>ja</c>, <c>ko</c>,
    /// <c>en</c>, <c>ar</c>, <c>he</c>. HarfBuzz converts BCP 47 to OpenType language tags
    /// automatically (<c>zh-Hans</c> → <c>ZHS</c>, <c>ja</c> → <c>JAN</c>, etc.).
    /// </para>
    /// <para>
    /// Apply to the entire text via <c>Style.WholeText</c> for a global default,
    /// or per-range via the rich-text tag <c>&lt;lang=zh-Hans&gt;...&lt;/lang&gt;</c> for
    /// mixed-language content.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 2)]
    [TypeDescription("Applies a BCP 47 language tag that activates OpenType 'locl' region-specific glyph variants (critical for CJK).")]
    [GenerateParameters]
    public partial class LanguageModifier : PooledAttributeModifier<byte>
    {
        /// <summary>BCP 47 language tag. A per-range <c>&lt;lang=…&gt;</c> value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkTextDirty))] private string language = "en";

        protected sealed override string AttributeKey => AttributeKeys.Language;

        protected override void OnDisable()
        {
            base.OnDisable();
            attribute?.ClearAll();
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var tag = Param.Language.Resolve(this, in context);
            if (string.IsNullOrWhiteSpace(tag)) return;

            var index = LanguageRegistry.Register(tag);
            if (index == LanguageRegistry.Unset) return;

            attribute.FillRange(context.Segment.Range, index);
        }
    }
}
