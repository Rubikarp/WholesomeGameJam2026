using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Overrides the font used for a text range by selecting a <see cref="FontFamily"/> from the
    /// component's <see cref="UniTextFontStack"/> by its <see cref="FontFamily.name"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parameter: the target family name (case-sensitive). Example: <c>&lt;font=pixel&gt;Score&lt;/font&gt;</c>.
    /// </para>
    /// <para>
    /// <b>Priority:</b> a matched font wins over <see cref="FontFamily.preferredLanguage"/>
    /// selection and over the regular FontStack fallback chain. If the chosen family's primary
    /// lacks a glyph for a codepoint, the normal fallback chain still kicks in for that codepoint.
    /// </para>
    /// <para>
    /// If the name is not found in the FontStack, a warning is logged once per unresolved name
    /// and affected codepoints render with the default fallback behavior.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 3)]
    [TypeDescription("Selects a font by FontFamily.name from the component's FontStack for this range.")]
    [GenerateParameters]
    public partial class FontModifier : PooledAttributeModifier<int>
    {
        /// <summary>Target <see cref="FontFamily.name"/>. A per-range <c>&lt;font=…&gt;</c> value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkTextDirty))] private string familyName = "";

        protected sealed override string AttributeKey => AttributeKeys.Font;

        protected override void OnDisable()
        {
            base.OnDisable();
            attribute?.ClearAll();
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var family = Param.FamilyName.Resolve(this, in context);
            if (string.IsNullOrEmpty(family)) return;

            var fp = uniText?.FontProvider;
            if (fp == null) return;

            var fontId = fp.TryGetFontIdByFamilyName(family);
            if (fontId == 0)
            {
                Debug.LogWarning($"[FontModifier] Family \"{family}\" not found in FontStack. " +
                                 $"Check FontFamily.name entries on {uniText?.cachedTransformData.name}.");
                return;
            }

            attribute.FillRange(context.Segment.Range, fontId);
        }
    }
}
