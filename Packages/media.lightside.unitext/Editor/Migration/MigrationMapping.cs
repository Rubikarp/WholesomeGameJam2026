using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Static, readonly mapping tables for TMP → UniText migration.
    /// Single source of truth for all correspondences.
    /// </summary>
    internal static class MigrationMapping
    {
        public const string TmpTextUiGuid     = "f4688fdb7df04437aeb418b961361dc5";
        public const string TmpText3DGuid     = "9541d86e2fd84c1d9990edf0852d74ab";
        public const string TmpInputFieldGuid = "2da0c512f12947e489f739169773d7ca";
        public const string TmpSubMeshUiGuid  = "058cba836c1846c3aa1c5fd2e28aea77";
        public const string TmpSubMeshGuid    = "07994bfe8b0e4adb97d706de5dea48d5";
        public const string TmpFontAssetGuid  = "71c1514a6bd24e1e882cebbe1904ce04";
        public const string TmpSpriteAssetGuid = "84a92b25f83d49b9bc132d206b370281";
        public const string TmpStyleSheetGuid = "ab2114bdc8544297b417dfefe9f1e410";
        public const string TmpSettingsGuid   = "2705215ac5b84b70bacc50632be6e391";
        public const string TmpDropdownGuid   = "7b743370ac3e4ec2a1668f5455a8ef8a";

        public const string UniTextGuid          = "beaa34cb0e58d624bb3a264b28600785";
        public const string UniTextWorldGuid     = "f82394fefa9244d49e439daa1fb85977";
        public const string UniTextEditableGuid  = "3cc1333b5b590af44b4c869dab470954";

        /// <summary>YAML field holding a TMP font asset's own ordered fallback list.</summary>
        public const string FontFallbackField = "m_FallbackFontAssetTable";

        /// <summary>The same list on font assets written by older TMP versions.</summary>
        public const string LegacyFontFallbackField = "fallbackFontAssets";

        /// <summary>YAML field holding the project-wide fallback list in <c>TMP_Settings</c>.</summary>
        public const string GlobalFallbackField = "m_fallbackFontAssets";

        public static readonly Dictionary<string, string> ComponentGuidMap = new()
        {
            { TmpTextUiGuid,     UniTextGuid },
            { TmpText3DGuid,     UniTextWorldGuid },
            { TmpInputFieldGuid, UniTextEditableGuid },
        };

        /// <summary>GUIDs of TMP sub-mesh components that should be removed after migration.</summary>
        public static readonly HashSet<string> SubMeshGuids = new()
        {
            TmpSubMeshUiGuid,
            TmpSubMeshGuid,
        };

        /// <summary>All TMP component GUIDs we scan for in scenes/prefabs.</summary>
        public static readonly HashSet<string> AllTmpComponentGuids = new()
        {
            TmpTextUiGuid, TmpText3DGuid, TmpInputFieldGuid,
            TmpSubMeshUiGuid, TmpSubMeshGuid, TmpDropdownGuid,
        };

        /// <summary>
        /// Whether the migrator can put a UniText component in this TMP component's place. A TMP
        /// component the tool only reports — a dropdown — answers false: its finding is cleared by
        /// skipping it, never by migrating.
        /// </summary>
        public static bool IsMigratableComponent(string tmpGuid)
            => !string.IsNullOrEmpty(tmpGuid) && ComponentGuidMap.ContainsKey(tmpGuid);

        /// <summary>
        /// TMP components a person puts on a GameObject. Sub-meshes are excluded: TMP creates and
        /// destroys them itself, so their presence says nothing about what the scene was authored
        /// against.
        /// </summary>
        public static readonly HashSet<string> AuthoredTmpComponentGuids = new()
        {
            TmpTextUiGuid, TmpText3DGuid, TmpInputFieldGuid, TmpDropdownGuid,
        };

        /// <summary>All TMP asset GUIDs we scan for in .asset files.</summary>
        public static readonly HashSet<string> TmpAssetGuids = new()
        {
            TmpFontAssetGuid, TmpSpriteAssetGuid, TmpStyleSheetGuid, TmpSettingsGuid,
        };

        public static readonly Dictionary<string, string> TmpGuidToName = new()
        {
            { TmpTextUiGuid,      "TextMeshProUGUI" },
            { TmpText3DGuid,      "TextMeshPro (3D)" },
            { TmpInputFieldGuid,  "TMP_InputField" },
            { TmpSubMeshUiGuid,   "TMP_SubMeshUI" },
            { TmpSubMeshGuid,     "TMP_SubMesh" },
            { TmpFontAssetGuid,   "TMP_FontAsset" },
            { TmpSpriteAssetGuid, "TMP_SpriteAsset" },
            { TmpStyleSheetGuid,  "TMP_StyleSheet" },
            { TmpSettingsGuid,    "TMP_Settings" },
            { TmpDropdownGuid,    "TMP_Dropdown" },
        };

        public static readonly Dictionary<string, string> UniTextGuidToName = new()
        {
            { UniTextGuid,         "UniText" },
            { UniTextWorldGuid,    "UniTextWorld" },
            { UniTextEditableGuid, "UniTextEditable" },
        };

        public static string GetTmpName(string guid) =>
            TmpGuidToName.TryGetValue(guid, out var n) ? n : guid;

        public static string GetTargetName(string tmpGuid) =>
            ComponentGuidMap.TryGetValue(tmpGuid, out var uniGuid)
                ? (UniTextGuidToName.TryGetValue(uniGuid, out var n) ? n : uniGuid)
                : "(none)";

        /// <summary>
        /// Splits a TMP alignment bitfield into UniText's <c>HorizontalAlignment</c> and
        /// <c>VerticalAlignment</c>. <c>flushLastLine</c> carries TMP's Flush mode, which justifies
        /// the last line too; <c>warning</c> is non-null only where UniText has no equivalent and
        /// the value was defaulted.
        /// </summary>
        public static (int horizontal, int vertical, bool flushLastLine, string warning)
            DecomposeAlignment(int tmpAlignment)
        {
            int hBits = tmpAlignment & 0xFF;
            int vBits = (tmpAlignment >> 8) & 0xFF;

            int h;
            bool flushLastLine = false;
            string warning = null;

            switch (hBits)
            {
                case 0x01: h = 0; break;
                case 0x02: h = 1; break;
                case 0x04: h = 2; break;
                case 0x08: h = 3; break;
                case 0x10: h = 3; flushLastLine = true; break;
                case 0x20: h = 0; warning = "Geometry horizontal alignment has no UniText equivalent, defaulted to Start"; break;
                default:   h = 0; break;
            }

            int v;
            switch (vBits)
            {
                case 0x01: v = 0; break;
                case 0x02: v = 1; break;
                case 0x04: v = 2; break;
                case 0x08: v = 0; warning = AppendWarning(warning, "Baseline vertical alignment has no UniText equivalent, defaulted to Top"); break;
                case 0x10: v = 0; warning = AppendWarning(warning, "Geometry vertical alignment has no UniText equivalent, defaulted to Top"); break;
                case 0x20: v = 0; warning = AppendWarning(warning, "Capline vertical alignment has no UniText equivalent, defaulted to Top"); break;
                default:   v = 0; break;
            }

            return (h, v, flushLastLine, warning);
        }

        static string AppendWarning(string warning, string next)
            => string.IsNullOrEmpty(warning) ? next : warning + "; " + next;

        /// <summary>TMP <c>FontStyles.Superscript</c>, migrated through <c>ScriptPositionModifier</c>.</summary>
        public const int SuperscriptStyle = 0x80;

        /// <summary>TMP <c>FontStyles.Subscript</c>, migrated through <c>ScriptPositionModifier</c>.</summary>
        public const int SubscriptStyle = 0x100;

        /// <summary>
        /// TMP font-style flags with no automatic path: Highlight alone, because TMP keeps no
        /// per-component highlight paint to carry over.
        /// </summary>
        public const int UnmappedFontStyles = 0x200;

        public struct FontStyleMapping
        {
            public int flag;
            public string modifierTypeName;
            public string tagName;
        }

        public static readonly FontStyleMapping[] FontStyleMappings =
        {
            new() { flag = 1,  modifierTypeName = "BoldModifier",          tagName = "b" },
            new() { flag = 2,  modifierTypeName = "ItalicModifier",        tagName = "i" },
            new() { flag = 4,  modifierTypeName = "UnderlineModifier",     tagName = "u" },
            new() { flag = 8,  modifierTypeName = "LowercaseModifier",     tagName = "lower" },
            new() { flag = 16, modifierTypeName = "UppercaseModifier",     tagName = "upper" },
            new() { flag = 32, modifierTypeName = "SmallCapsModifier",     tagName = "smallcaps" },
            new() { flag = 64, modifierTypeName = "StrikethroughModifier", tagName = "s" },
        };

        public static bool ConvertWordWrap(int tmpWrappingMode) => tmpWrappingMode == 1 || tmpWrappingMode == 2;

        /// <summary>What a script variable of a TMP type stands for, and how its members resolve.</summary>
        public enum TmpTypeKind : byte
        {
            /// <summary>A text component: <c>TextMeshProUGUI</c>, <c>TextMeshPro</c>, <c>TMP_Text</c>.</summary>
            Text,
            /// <summary>A <c>TMP_InputField</c>.</summary>
            InputField,
            /// <summary>A <c>TMP_FontAsset</c>.</summary>
            FontAsset,
        }

        /// <summary>One TMP type and the UniText type that takes its place in source.</summary>
        public readonly struct ScriptTypeMapping
        {
            public ScriptTypeMapping(string uniTextName, TmpTypeKind kind)
            {
                UniTextName = uniTextName;
                Kind = kind;
            }

            /// <summary>Type name written in the migrated source.</summary>
            public readonly string UniTextName;

            /// <summary>What members on a variable of this type resolve against.</summary>
            public readonly TmpTypeKind Kind;
        }

        /// <summary>TMP types the rewrite renames, and the kind each declared variable takes on.</summary>
        public static readonly Dictionary<string, ScriptTypeMapping> ScriptTypes = new(StringComparer.Ordinal)
        {
            { "TextMeshProUGUI", new ScriptTypeMapping("UniText", TmpTypeKind.Text) },
            { "TextMeshPro", new ScriptTypeMapping("UniTextWorld", TmpTypeKind.Text) },
            { "TMP_Text", new ScriptTypeMapping("UniTextBase", TmpTypeKind.Text) },
            { "TMP_InputField", new ScriptTypeMapping("UniTextEditable", TmpTypeKind.InputField) },
            { "TMP_FontAsset", new ScriptTypeMapping("UniTextFont", TmpTypeKind.FontAsset) },
        };

        /// <summary>Type names the rewrite writes into migrated source.</summary>
        public static readonly HashSet<string> IntroducedScriptTypes = BuildIntroducedScriptTypes();

        private static HashSet<string> BuildIntroducedScriptTypes()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mapping in ScriptTypes.Values) names.Add(mapping.UniTextName);
            return names;
        }

        /// <summary>
        /// TMPro types with no UniText counterpart. The rewrite leaves every one of them in place
        /// and keeps <c>using TMPro;</c> in any file that still needs one.
        /// </summary>
        public static readonly Dictionary<string, string> UnmappedScriptTypes = new(StringComparer.Ordinal)
        {
            { "TMP_Dropdown", "no UniText equivalent — use Unity's Dropdown with a UniText label" },
            { "TMP_SpriteAsset", "replace with UniTextSprites used by an AssetSpriteProvider on SpriteModifier" },
            { "TMP_StyleSheet", "no UniText equivalent — use a StylePreset asset" },
            { "TMP_Settings", "no UniText equivalent — see Project Settings → UniText" },
            { "TMP_ColorGradient", "no UniText equivalent — use a gradient paint with FillModifier" },
            { "TMP_TextInfo", "UniText exposes layout through Buffers, ResultGlyphs and TextSnapshot" },
            { "TMP_CharacterInfo", "UniText exposes per-glyph data through ResultGlyphs" },
            { "TMP_LineInfo", "UniText exposes line data through its layout buffers" },
            { "TMP_WordInfo", "UniText exposes word data through its layout buffers" },
            { "TMP_MeshInfo", "UniText builds its mesh through UniTextMeshGenerator" },
            { "TMP_SubMesh", "UniText needs no sub-meshes" },
            { "TMP_SubMeshUI", "UniText needs no sub-meshes" },
            { "TMP_TextUtilities", "no UniText equivalent — see UniTextBase.FindAll and GetRangeText" },
            { "TMP_FontAssetUtilities", "no UniText equivalent" },
            { "TMPro_EventManager", "no UniText equivalent — UniText raises C# events on the component" },
            { "TextAlignmentOptions", "split into HorizontalAlignment and VerticalAlignment" },
            { "HorizontalAlignmentOptions", "use HorizontalAlignment (Start, Center, End, Justify, plus physical Left/Right)" },
            { "VerticalAlignmentOptions", "use VerticalAlignment (Top, Middle, Bottom)" },
            { "FontStyles", "use BoldModifier, ItalicModifier and their siblings" },
            { "FontWeight", "use VariationModifier on the wght axis" },
            { "TextOverflowModes", "use EllipsisModifier or TruncateModifier" },
            { "TextureMappingOptions", "no UniText equivalent" },
            { "TextWrappingModes", "use the WordWrap property" },
            { "ColorMode", "no UniText equivalent — paints carry their own model" },
            { "VertexGradient", "use a gradient paint with FillModifier" },
        };

        /// <summary>One TMP member and what the rewrite does with it.</summary>
        public readonly struct ScriptMemberMapping
        {
            private ScriptMemberMapping(string replacement, string advice)
            {
                Replacement = replacement;
                Advice = advice;
            }

            /// <summary>A member that becomes <paramref name="replacement"/> without changing the call shape.</summary>
            public static ScriptMemberMapping Rename(string replacement) => new(replacement, null);

            /// <summary>A member the rewrite refuses to touch, with the reason a reader needs.</summary>
            public static ScriptMemberMapping Manual(string advice) => new(null, advice);

            /// <summary>UniText member name, or null when there is no mechanical replacement.</summary>
            public readonly string Replacement;

            /// <summary>Why it is left alone, for a member the rewrite refuses.</summary>
            public readonly string Advice;

            /// <summary>Whether the rewrite can apply this mapping on its own.</summary>
            public bool IsAutomatic => Replacement != null;
        }

        /// <summary>Members reached on a TMP text component.</summary>
        public static readonly Dictionary<string, ScriptMemberMapping> TextMembers = new(StringComparer.Ordinal)
        {
            { "text", ScriptMemberMapping.Rename("Text") },
            { "fontSize", ScriptMemberMapping.Rename("FontSize") },
            { "fontSizeMin", ScriptMemberMapping.Rename("MinFontSize") },
            { "fontSizeMax", ScriptMemberMapping.Rename("MaxFontSize") },
            { "enableAutoSizing", ScriptMemberMapping.Rename("AutoSize") },
            { "enableWordWrapping", ScriptMemberMapping.Rename("WordWrap") },
            { "font", ScriptMemberMapping.Rename("Font") },

            { "fontSharedMaterial", ScriptMemberMapping.Manual("UniText resolves its material itself; per-range looks go through MaterialModifier") },
            { "fontMaterial", ScriptMemberMapping.Manual("UniText resolves its material itself; per-range looks go through MaterialModifier") },
            { "fontStyle", ScriptMemberMapping.Manual("apply BoldModifier, ItalicModifier and their siblings as whole-text styles") },
            { "fontWeight", ScriptMemberMapping.Manual("apply a VariationModifier on the wght axis") },
            { "alignment", ScriptMemberMapping.Manual("split into HorizontalAlignment and VerticalAlignment") },
            { "horizontalAlignment", ScriptMemberMapping.Manual("HorizontalAlignment takes Start, Center, End, Justify, Left or Right, not TMP's flags") },
            { "verticalAlignment", ScriptMemberMapping.Manual("VerticalAlignment takes Top, Middle or Bottom, not TMP's flags") },
            { "textWrappingMode", ScriptMemberMapping.Manual("WordWrap is a bool") },
            { "characterSpacing", ScriptMemberMapping.Manual("apply a whole-text LetterSpacingModifier") },
            { "wordSpacing", ScriptMemberMapping.Manual("apply a whole-text WordSpacingModifier") },
            { "lineSpacing", ScriptMemberMapping.Manual("apply a whole-text LineHeightModifier") },
            { "paragraphSpacing", ScriptMemberMapping.Manual("apply a whole-text ParagraphSpacingModifier") },
            { "margin", ScriptMemberMapping.Manual("Padding is (Left, Bottom, Right, Top); TMP's margin is (Left, Top, Right, Bottom)") },
            { "overflowMode", ScriptMemberMapping.Manual("apply EllipsisModifier or TruncateModifier") },
            { "richText", ScriptMemberMapping.Manual("UniText reads only the tags its Styles register") },
            { "isRightToLeftText", ScriptMemberMapping.Manual("apply a whole-text DirectionModifier") },
            { "maxVisibleCharacters", ScriptMemberMapping.Manual("use a RevealModifier") },
            { "firstVisibleCharacter", ScriptMemberMapping.Manual("use a RevealModifier") },
            { "textInfo", ScriptMemberMapping.Manual("read layout through Buffers, ResultGlyphs or CaptureTextSnapshot") },
            { "characterCount", ScriptMemberMapping.Manual("use CodepointCount") },
            { "ForceMeshUpdate", ScriptMemberMapping.Manual("call SetDirty with the UniTextDirty flags that changed") },
            { "GetPreferredValues", ScriptMemberMapping.Manual("read preferredWidth and preferredHeight through ILayoutElement") },
            { "preferredWidth", ScriptMemberMapping.Manual("UniText implements ILayoutElement explicitly — cast to it first") },
            { "preferredHeight", ScriptMemberMapping.Manual("UniText implements ILayoutElement explicitly — cast to it first") },
            { "renderedWidth", ScriptMemberMapping.Manual("use ResultSize") },
            { "renderedHeight", ScriptMemberMapping.Manual("use ResultSize") },
            { "colorGradient", ScriptMemberMapping.Manual("use a gradient paint with FillModifier") },
            { "enableVertexGradient", ScriptMemberMapping.Manual("use a gradient paint with FillModifier") },
            { "faceColor", ScriptMemberMapping.Manual("use color, or a paint layer for anything richer") },
            { "outlineColor", ScriptMemberMapping.Manual("use StrokeModifier") },
            { "outlineWidth", ScriptMemberMapping.Manual("use StrokeModifier") },
            { "spriteAsset", ScriptMemberMapping.Manual("assign UniTextSprites through an AssetSpriteProvider on SpriteModifier") },
            { "styleSheet", ScriptMemberMapping.Manual("use a StylePreset asset") },
            { "textStyle", ScriptMemberMapping.Manual("use a StylePreset asset") },
            { "autoSizeTextContainer", ScriptMemberMapping.Manual("drive the RectTransform through a ContentSizeFitter") },
            { "pageToDisplay", ScriptMemberMapping.Manual("no UniText equivalent") },
            { "linkedTextComponent", ScriptMemberMapping.Manual("no UniText equivalent") },
            { "parseCtrlCharacters", ScriptMemberMapping.Manual("no UniText equivalent") },
            { "overrideColorTags", ScriptMemberMapping.Manual("no UniText equivalent") },
        };

        /// <summary>Members reached on a TMP input field.</summary>
        /// <summary>
        /// Members reached on a TMP font asset. Only the source font file survives the rename with
        /// its meaning intact; everything else names a TMP-side data structure UniText builds
        /// differently, and renaming the variable's type would leave the call unresolved.
        /// </summary>
        public static readonly Dictionary<string, ScriptMemberMapping> FontAssetMembers = new(StringComparer.Ordinal)
        {
            { "sourceFontFile", ScriptMemberMapping.Rename("sourceFont") },

            { "faceInfo", ScriptMemberMapping.Manual("UniTextFont.FaceInfo carries font design units, not TMP's point-size-scaled metrics") },
            { "italicStyle", ScriptMemberMapping.Manual("use UniTextFont.ItalicStyle — the same idea on its own scale") },
            { "normalSpacingOffset", ScriptMemberMapping.Manual("use UniTextFont.SpacingOffset, measured in font units") },
            { "normalStyle", ScriptMemberMapping.Manual("UniText synthesises weight through UniTextFont.FakeBoldWeight") },
            { "boldStyle", ScriptMemberMapping.Manual("UniText synthesises weight through UniTextFont.FakeBoldWeight") },
            { "boldSpacing", ScriptMemberMapping.Manual("no UniText equivalent — spacing is a modifier, not a font setting") },
            { "tabSize", ScriptMemberMapping.Manual("no UniText equivalent on the font asset") },
            { "material", ScriptMemberMapping.Manual("a UniText font owns no material; the component resolves one") },
            { "materialHashCode", ScriptMemberMapping.Manual("a UniText font owns no material") },
            { "atlas", ScriptMemberMapping.Manual("UniText rasterises into a shared atlas owned by the atlas manager") },
            { "atlasTexture", ScriptMemberMapping.Manual("UniText rasterises into a shared atlas owned by the atlas manager") },
            { "atlasTextures", ScriptMemberMapping.Manual("UniText rasterises into a shared atlas owned by the atlas manager") },
            { "atlasWidth", ScriptMemberMapping.Manual("atlas geometry belongs to UniText's atlas manager, not the font") },
            { "atlasHeight", ScriptMemberMapping.Manual("atlas geometry belongs to UniText's atlas manager, not the font") },
            { "atlasPadding", ScriptMemberMapping.Manual("use UniTextFont.AtlasPadding") },
            { "atlasPopulationMode", ScriptMemberMapping.Manual("UniText rasterises on demand; there is no population mode") },
            { "atlasRenderMode", ScriptMemberMapping.Manual("render mode lives on the component as UniTextRenderMode") },
            { "characterTable", ScriptMemberMapping.Manual("UniText resolves characters through the font stack, not a table on the asset") },
            { "characterLookupTable", ScriptMemberMapping.Manual("use UniTextFont.GetGlyphIndexForUnicode") },
            { "glyphTable", ScriptMemberMapping.Manual("use UniTextFont.GlyphLookupTable") },
            { "glyphLookupTable", ScriptMemberMapping.Manual("use UniTextFont.GlyphLookupTable") },
            { "fontFeatureTable", ScriptMemberMapping.Manual("OpenType features are requested per range through FontFeatureModifier") },
            { "fallbackFontAssetTable", ScriptMemberMapping.Manual("fallbacks are the families of a UniTextFontStack") },
            { "creationSettings", ScriptMemberMapping.Manual("no UniText equivalent — a UniText font reads the font file directly") },
            { "HasCharacter", ScriptMemberMapping.Manual("use UniTextFont.GetGlyphIndexForUnicode, which returns 0 for a missing character") },
            { "HasCharacters", ScriptMemberMapping.Manual("use UniTextFont.GetGlyphIndexForUnicode per codepoint") },
            { "TryAddCharacters", ScriptMemberMapping.Manual("UniText adds glyphs to its atlas on demand") },
            { "TryAddGlyphs", ScriptMemberMapping.Manual("UniText adds glyphs to its atlas on demand") },
            { "ClearFontAssetData", ScriptMemberMapping.Manual("UniTextFont.ClearDynamicData has a different call shape") },
            { "CreateFontAsset", ScriptMemberMapping.Manual("use UniTextFont.CreateFontAsset, which takes the font file bytes") },
        };

        public static readonly Dictionary<string, ScriptMemberMapping> InputFieldMembers = new(StringComparer.Ordinal)
        {
            { "text", ScriptMemberMapping.Rename("Text") },
            { "readOnly", ScriptMemberMapping.Rename("ReadOnly") },

            { "onValueChanged", ScriptMemberMapping.Manual("ValueChanged is a C# event; subscribe with += and unsubscribe with -=") },
            { "onSubmit", ScriptMemberMapping.Manual("Submitted is a C# event; subscribe with += and unsubscribe with -=") },
            { "onEndEdit", ScriptMemberMapping.Manual("Defocused carries an EditingEndReason and Submitted carries the text — pick the one the handler means") },
            { "onSelect", ScriptMemberMapping.Manual("Focused takes no argument, unlike TMP's string") },
            { "onDeselect", ScriptMemberMapping.Manual("Defocused carries an EditingEndReason, not the text") },
            { "textComponent", ScriptMemberMapping.Manual("an editable field is composed of a field box and a child text component") },
            { "characterLimit", ScriptMemberMapping.Manual("add a LengthLimitBehavior") },
            { "contentType", ScriptMemberMapping.Manual("set an InputFilter, and add a PasswordBehavior for password content") },
            { "lineType", ScriptMemberMapping.Manual("multiline needs a newline policy behavior") },
            { "placeholder", ScriptMemberMapping.Manual("a placeholder is its own component in the field composition") },
            { "caretColor", ScriptMemberMapping.Manual("the caret is configured on the editable component") },
            { "selectionColor", ScriptMemberMapping.Manual("selection presentation is configured on the editable component") },
            { "ActivateInputField", ScriptMemberMapping.Manual("focus the component through the EventSystem") },
            { "DeactivateInputField", ScriptMemberMapping.Manual("blur the component through the EventSystem") },

            { "caretPosition", ScriptMemberMapping.Rename("CaretPosition") },
            { "isFocused", ScriptMemberMapping.Rename("IsActive") },

            { "SetTextWithoutNotify", ScriptMemberMapping.Manual("SetTextProgrammatic sets the text and names why it changed") },
            { "selectionAnchorPosition", ScriptMemberMapping.Manual("read Selection, or SelectionStart and SelectionEnd; Select(start, end) sets a range rather than one endpoint") },
            { "selectionFocusPosition", ScriptMemberMapping.Manual("read Selection, or SelectionStart and SelectionEnd; Select(start, end) sets a range rather than one endpoint") },
            { "stringPosition", ScriptMemberMapping.Manual("read Selection, or SelectionStart and SelectionEnd; Select(start, end) sets a range rather than one endpoint") },
            { "onValidateInput", ScriptMemberMapping.Manual("add an InputFilter behavior") },
            { "inputValidator", ScriptMemberMapping.Manual("add an InputFilter behavior") },
            { "characterValidation", ScriptMemberMapping.Manual("add an InputFilter behavior") },

            { "interactable", ScriptMemberMapping.Manual(SelectableSurface) },
            { "navigation", ScriptMemberMapping.Manual(SelectableSurface) },
            { "targetGraphic", ScriptMemberMapping.Manual(SelectableSurface) },
            { "colors", ScriptMemberMapping.Manual(SelectableSurface) },
            { "transition", ScriptMemberMapping.Manual(SelectableSurface) },
            { "spriteState", ScriptMemberMapping.Manual(SelectableSurface) },
            { "animationTriggers", ScriptMemberMapping.Manual(SelectableSurface) },
            { "IsInteractable", ScriptMemberMapping.Manual(SelectableSurface) },
        };

        /// <summary>
        /// What to do about a member TMP inherits from <c>Selectable</c>. UniTextEditable is a
        /// plain MonoBehaviour, so Unity UI's selection, transition and navigation model is not
        /// part of it.
        /// </summary>
        const string SelectableSurface =
            "UniTextEditable is a MonoBehaviour, not a UnityEngine.UI.Selectable — keep Unity UI's " +
            "interaction state on a Selectable of your own beside it";

        /// <summary>
        /// TMP tags no UniText modifier reproduces. Left in the text they render as literal
        /// characters, so the markup itself has to be rewritten.
        /// </summary>
        public static readonly HashSet<string> UnsupportedTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "alpha",
            "pos",
            "space",
            "voffset",
            "rotate",
            "width",
            "noparse",
            "page",
            "style",
            "margin",
            "margin-right",
        };

        /// <summary>
        /// Every tag TMP itself treats as markup. A name outside this set is not markup to TMP
        /// either — it already renders as literal characters — so the migration owes it nothing;
        /// a name inside it that no other table here classifies is markup the migration would
        /// drop unremarked, and is reported instead.
        /// </summary>
        public static readonly HashSet<string> TmpTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "align", "allcaps", "alpha", "b", "br", "color", "cspace", "font", "font-weight",
            "gradient", "i", "indent", "line-height", "line-indent", "link", "lowercase",
            "margin", "margin-left", "margin-right", "mark", "material", "mspace", "nobr",
            "noparse", "page", "pos", "quad", "rotate", "s", "size", "smallcaps", "space",
            "sprite", "strikethrough", "style", "sub", "sup", "u", "uppercase", "voffset",
            "width",
        };

        /// <summary>
        /// Tags another stage of the migration owns, so the markup tables carry no entry for them
        /// and their absence from one is not a gap. The sprite migration rewrites
        /// <c>&lt;sprite&gt;</c> and builds the modifier that resolves it, per component, before
        /// any other stage reads the text.
        /// </summary>
        public static readonly HashSet<string> TagsOwnedElsewhere = new(StringComparer.OrdinalIgnoreCase)
        {
            "sprite",
        };

        /// <summary>
        /// Tags UniText can express only through an asset the migrator has no way to resolve, so
        /// the Style entry is left for a human to create and fill in.
        /// </summary>
        public static readonly Dictionary<string, string> TagsNeedingManualSetup = new(StringComparer.OrdinalIgnoreCase)
        {
            { "material", "Add a MaterialModifier + TagRule(\"material\") and assign the replacement material. Left unconfigured it suppresses the text it covers." },
            { "quad",     "Add an ObjModifier + TagRule(\"quad\") and register the image it should resolve." },
            { "gradient", "Add a FillModifier + TagRule(\"gradient\") and assign the paint swatch that replaces the TMP colour gradient." },
        };

        /// <summary>
        /// Tags whose UniText modifier exists but whose value is written differently, so the tag
        /// survives migration while the value does not.
        /// </summary>
        public static readonly Dictionary<string, string> TagValueNotes = new(StringComparer.OrdinalIgnoreCase)
        {
            { "align",        "TMP writes justified/flush; UniText's alignment is Start/Center/End/Justify — rewrite those two values (flush is Justify with LastLineAlignment = Justify)." },
            { "font",         "TMP names a font asset; UniText's <font> names a FontFamily inside the component's FontStack — rewrite the value." },
            { "mark",         "TMP takes a colour; the UniText highlight is configured on the Style entry, so the value in the tag is ignored." },
            { "mspace",       "TMP mspace values without a unit are font units; UniText reads them as pixels — verify visually." },
        };

        /// <summary>What stands behind one tag, with the parameter that configures it.</summary>
        public readonly struct TagBinding
        {
            public readonly string ModifierTypeName;
            public readonly string DefaultParameter;
            public readonly bool Inline;

            /// <summary>
            /// Name of a <c>ModifierGraphPreset</c> in the package's <c>Defaults</c> folder whose
            /// whole graph this tag applies. Set instead of <see cref="ModifierTypeName"/> where a
            /// single modifier cannot express the tag.
            /// </summary>
            public readonly string GraphPresetName;

            /// <summary>
            /// Name of a standalone <c>ParseRule</c> that carries the tag on its own, for markup
            /// whose whole effect is a substitution. Set instead of <see cref="ModifierTypeName"/>;
            /// the Style built from it has no modifier, and the rule owns the tag name it matches,
            /// so the vocabulary key must be that same name.
            /// </summary>
            public readonly string StandaloneRuleTypeName;

            public TagBinding(string modifierTypeName, string defaultParameter = null,
                bool inline = false, string graphPresetName = null,
                string standaloneRuleTypeName = null)
            {
                ModifierTypeName = modifierTypeName;
                DefaultParameter = defaultParameter;
                Inline = inline;
                GraphPresetName = graphPresetName;
                StandaloneRuleTypeName = standaloneRuleTypeName;
            }
        }

        /// <summary>
        /// Every markup word a project teaches UniText once, in
        /// <c>UniTextSettings.GlobalStylePreset</c>: each TMP tag UniText can reproduce, plus the
        /// names UniText spells those same tags by, so text written either way renders. A tag is
        /// bound to a modifier, to a modifier-graph preset, or to a standalone rule that needs no
        /// modifier. The whole table goes into the preset — not the subset a scan happened to see
        /// — because text arriving at runtime from localisation or script is never scanned.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A tag that answers to nothing in <see cref="TmpTags"/> does not belong here: migrated
        /// text cannot contain one, and every entry is a rule the parser walks at each markup
        /// trigger in every component that reads the preset.
        /// </para>
        /// <para>
        /// Absent by design: <c>sprite</c>, which the sprite migration owns per component (see
        /// <see cref="TagsOwnedElsewhere"/>); <c>quad</c>, <c>material</c> and <c>gradient</c>,
        /// which need an asset nothing here can choose (see <see cref="TagsNeedingManualSetup"/>);
        /// and every tag in <see cref="UnsupportedTags"/>, which has no modifier at all.
        /// </para>
        /// </remarks>
        public static readonly Dictionary<string, TagBinding> TagVocabulary =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "b",                 new TagBinding("BoldModifier") },
                { "i",                 new TagBinding("ItalicModifier") },
                { "u",                 new TagBinding("UnderlineModifier") },
                { "s",                 new TagBinding("StrikethroughModifier") },
                { "strikethrough",     new TagBinding("StrikethroughModifier") },
                { "mark",              new TagBinding("HighlightModifier") },
                { "nobr",              new TagBinding("NoBreakModifier") },
                { "smallcaps",         new TagBinding("SmallCapsModifier") },
                { "lowercase",         new TagBinding("LowercaseModifier") },
                { "uppercase",         new TagBinding("UppercaseModifier") },
                { "allcaps",           new TagBinding("UppercaseModifier") },
                { "sub",               new TagBinding("ScriptPositionModifier", "sub") },
                { "sup",               new TagBinding("ScriptPositionModifier", "super") },

                { "size",              new TagBinding("SizeModifier") },
                { "color",             new TagBinding("ColorModifier") },
                { "font",              new TagBinding("FontModifier") },
                { "font-weight",       new TagBinding("VariationModifier") },
                { "align",             new TagBinding("AlignmentModifier") },
                { "cspace",            new TagBinding("LetterSpacingModifier") },
                { "mspace",            new TagBinding("CharacterWidthModifier") },
                { "indent",            new TagBinding("IndentModifier") },
                { "line-indent",       new TagBinding("IndentModifier") },
                { "margin-left",       new TagBinding("IndentModifier") },
                { "line-height",       new TagBinding("LineHeightModifier") },

                { "br",                new TagBinding(null, standaloneRuleTypeName: LineBreakRule) },
                { "link",              new TagBinding(null, graphPresetName: LinkGraphPreset) },

                { "letter-spacing",    new TagBinding("LetterSpacingModifier") },
                { "upper",             new TagBinding("UppercaseModifier") },
                { "lower",             new TagBinding("LowercaseModifier") },
            };

        /// <summary>
        /// Asset name of the shipped modifier-graph preset behind <c>&lt;link&gt;</c>. A link is a
        /// composite — the modifier, its interaction rules and its states — which one modifier
        /// cannot carry, so the tag applies the package default instead.
        /// </summary>
        public const string LinkGraphPreset = "LinkPreset";

        /// <summary>Registered name of the rule that carries <c>&lt;br&gt;</c> on its own.</summary>
        public const string LineBreakRule = "LineBreakParseRule";

        /// <summary>
        /// Creates a modifier by type name, or null when no factory is registered for it.
        /// Every name used by <see cref="TagVocabulary"/> and <see cref="FontStyleMappings"/>
        /// resolves here.
        /// </summary>
        public static BaseModifier CreateModifier(string typeName)
        {
            if (modifierFactories.TryGetValue(typeName, out var factory)) return factory();
            Debug.LogWarning($"[Migration] Unknown modifier type: {typeName}");
            return null;
        }

        /// <summary>
        /// Creates a standalone parse rule by type name, or null when no factory is registered for
        /// it. Every name used as <see cref="TagBinding.StandaloneRuleTypeName"/> resolves here.
        /// </summary>
        public static ParseRule CreateStandaloneRule(string typeName)
        {
            if (standaloneRuleFactories.TryGetValue(typeName, out var factory)) return factory();
            Debug.LogWarning($"[Migration] Unknown parse rule type: {typeName}");
            return null;
        }

        static readonly Dictionary<string, Func<ParseRule>> standaloneRuleFactories = new()
        {
            { LineBreakRule, () => new LineBreakParseRule() },
        };

        static readonly Dictionary<string, Func<BaseModifier>> modifierFactories = new()
        {
            { "AlignmentModifier",      () => new AlignmentModifier() },
            { "BoldModifier",           () => new BoldModifier() },
            { "CharacterWidthModifier", () => new CharacterWidthModifier() },
            { "ColorModifier",          () => new ColorModifier() },
            { "EllipsisModifier",       () => new EllipsisModifier() },
            { "FillModifier",           () => new FillModifier() },
            { "FontModifier",           () => new FontModifier() },
            { "GlowModifier",           () => new GlowModifier() },
            { "HighlightModifier",      () => new HighlightModifier() },
            { "IndentModifier",         () => new IndentModifier() },
            { "ItalicModifier",         () => new ItalicModifier() },
            { "LetterSpacingModifier",  () => new LetterSpacingModifier() },
            { "LineHeightModifier",     () => new LineHeightModifier() },
            { "LowercaseModifier",      () => new LowercaseModifier() },
            { "NoBreakModifier",        () => new NoBreakModifier() },
            { "ObjModifier",            () => new ObjModifier() },
            { "OutlineModifier",        () => new StrokeModifier() },
            { "ScriptPositionModifier", () => new ScriptPositionModifier() },
            { "ShadowModifier",         () => new ShadowModifier() },
            { "SizeModifier",           () => new SizeModifier() },
            { "SmallCapsModifier",      () => new SmallCapsModifier() },
            { "StrikethroughModifier",  () => new StrikethroughModifier() },
            { "StrokeModifier",         () => new StrokeModifier() },
            { "TruncateModifier",       () => new TruncateModifier() },
            { "UnderlineModifier",      () => new UnderlineModifier() },
            { "UppercaseModifier",      () => new UppercaseModifier() },
            { "VariationModifier",      () => new VariationModifier() },
            { "WordSpacingModifier",    () => new WordSpacingModifier() },
        };

        public struct MaterialRecipe
        {
            public string shaderProperty;
            public string description;
            public string uniTextEquivalent;
        }

        public static readonly MaterialRecipe[] MaterialRecipes =
        {
            new() { shaderProperty = "_OutlineColor",   description = "Outline color",   uniTextEquivalent = "a whole-text StrokeModifier (colour, width)" },
            new() { shaderProperty = "_OutlineWidth",   description = "Outline width",   uniTextEquivalent = "a whole-text StrokeModifier (colour, width)" },
            new() { shaderProperty = "_UnderlayColor",  description = "Shadow/underlay",  uniTextEquivalent = "a whole-text ShadowModifier (offset, colour, softness)" },
            new() { shaderProperty = "_UnderlayOffsetX", description = "Shadow offset X", uniTextEquivalent = "a whole-text ShadowModifier (offset, colour, softness)" },
            new() { shaderProperty = "_UnderlayOffsetY", description = "Shadow offset Y", uniTextEquivalent = "a whole-text ShadowModifier (offset, colour, softness)" },
            new() { shaderProperty = "_GlowColor",      description = "Glow effect",     uniTextEquivalent = "a whole-text GlowModifier (colour, radius)" },
        };

        public static readonly Dictionary<string, string> AnimationPropertyMap = new()
        {
            { "m_fontColor",   "m_Color" },
            { "m_fontColor.r", "m_Color.r" },
            { "m_fontColor.g", "m_Color.g" },
            { "m_fontColor.b", "m_Color.b" },
            { "m_fontColor.a", "m_Color.a" },
            { "m_fontSize",    "fontSize" },
            { "m_fontSizeBase","fontSize" },
        };
    }
}
