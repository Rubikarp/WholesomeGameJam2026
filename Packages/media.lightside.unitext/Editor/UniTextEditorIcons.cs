using System;
using System.Collections.Generic;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Registers UniText's icon source with <see cref="EditorResources"/>: the package's editor
    /// Resources root plus the per-type and per-group icon maps its modifiers and parse rules
    /// show in type selectors and element labels.
    /// </summary>
    [InitializeOnLoad]
    internal static class UniTextEditorIcons
    {
        static UniTextEditorIcons()
        {
            EditorResources.RegisterIconSource("UniText/Icons/",
                new Dictionary<Type, string>
                {
                    { typeof(BoldModifier), "bold" },
                    { typeof(ItalicModifier), "italic" },
                    { typeof(UnderlineModifier), "underline" },
                    { typeof(StrikethroughModifier), "strikethrough" },
                    { typeof(UppercaseModifier), "uppercase" },
                    { typeof(LowercaseModifier), "lowercase" },
                    { typeof(SizeModifier), "size" },
                    { typeof(ColorModifier), "color" },
                    { typeof(LetterSpacingModifier), "letter-spacing" },
                    { typeof(LineHeightModifier), "line-height" },
                    { typeof(ParagraphSpacingModifier), "line-height" },
                    { typeof(NoBreakModifier), "nobr" },
                    { typeof(WordSpacingModifier), "word-spacing" },
                    { typeof(SeparatorModifier), "nobr" },
                    { typeof(FillModifier), "fill" },
                    { typeof(StrokeModifier), "outline" },
                    { typeof(ShadowModifier), "shadow" },
                    { typeof(GlowModifier), "shadow" },
                    { typeof(InnerShadowModifier), "shadow" },
                    { typeof(ExtrudeModifier), "shadow" },
                    { typeof(LinkModifier), "link" },
                    { typeof(InteractiveModifier), "link" },
                    { typeof(SpoilerModifier), "fill" },
                    { typeof(TriggerWordParseRule), "tag" },
                    { typeof(EllipsisModifier), "ellipsis" },
                    { typeof(TruncateModifier), "ellipsis" },
                    { typeof(RevealModifier), "ellipsis" },
                    { typeof(ListModifier), "list" },
                    { typeof(ObjModifier), "inline-object" },
                    { typeof(SpriteModifier), "inline-object" },
                    { typeof(SmallCapsModifier), "smallcaps" },
                    { typeof(MaterialModifier), "material" },
                    { typeof(FontModifier), "font" },
                    { typeof(LanguageModifier), "language" },
                    { typeof(TagRule), "tag" },
                    { typeof(MarkdownWrapRule), "markdown" },
                    { typeof(MarkdownLinkParseRule), "markdown" },
                    { typeof(MarkdownListParseRule), "markdown" },
                    { typeof(RawUrlParseRule), "link" },
                    { typeof(VariationModifier), "tune" },
                    { typeof(FontFeatureModifier), "tune" },
                    { typeof(ScriptPositionModifier), "script-position" },
                    { typeof(CompositeModifier), "shapes" },
                },
                new Dictionary<string, string>
                {
                    { "Text Style", "text-style" },
                    { "Decoration", "decoration" },
                    { "Appearance", "color" },
                    { "Interactive", "interactive" },
                    { "Layout", "layout" },
                    { "Inline", "inline-object" },
                    { "Tags", "tag" },
                    { "Inline Tags", "inline-object" },
                    { "Markdown", "markdown" },
                    { "Auto-detect", "auto-detect" },
                    { "Utility", "utility" },
                    { "Animation", "animation" },
                    { "Whole Text", "text-style" },
                });
        }
    }
}
