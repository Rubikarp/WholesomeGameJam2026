using LightSide.SceneEditing;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// UniText's palette entries. Object and asset creation follow the surfaces they belong to; the
    /// tool windows and scene-visibility switches appear only in the shortcut's palette, where a
    /// package offers everything it has.
    /// </summary>
    internal sealed class UniTextCommands : ILightSideCommands
    {
        public void Populate(CommandMenu menu, CommandContext context)
        {
            menu.GroupIcon = "unitext-icon";

            if (context.CreatesObjects) AddObjects(menu, context);
            if (context.CreatesAssets)
            {
                AddFonts(menu);
                AddPresets(menu);
            }
            if (context.Has<UniTextBase>())
                menu.Add("Edit Text in Scene", SceneTextEditController.EditSelected,
                    "unitext-editable-icon");
            if (context.Surface == CommandSurface.Global) AddTools(menu);
        }

        private static void AddObjects(CommandMenu menu, CommandContext context)
        {
            var command = new MenuCommand(context.Target);
            menu.Add("Text", () => UniTextObjectMenu.CreateText(command), "unitext-icon");
            menu.Add("Button", () => UniTextObjectMenu.CreateButton(command), "interactive");
            menu.Add("Selectable Text", () => UniTextObjectMenu.CreateSelectableText(command),
                "unitext-selectable-icon");
            menu.Add("Editable Text", () => UniTextObjectMenu.CreateEditableText(command),
                "unitext-editable-icon");
            menu.Add("Input Field", () => UniTextObjectMenu.CreateInputField(command),
                "unitext-input-field-native-config-icon");
            menu.Add("World Text", () => UniTextObjectMenu.CreateWorldText(command),
                "unitext-world-icon");
        }

        /// <summary>
        /// Font commands gate on the same validators their menu items use, so the palette offers a
        /// font conversion exactly when the menu would.
        /// </summary>
        private static void AddFonts(CommandMenu menu)
        {
            if (UniTextFontEditor.CreateFontAssetValidate())
            {
                menu.Add("Fonts/Font Asset", UniTextFontEditor.CreateFontAsset, "unitextfont-icon");
                menu.Add("Fonts/Color Font Asset", UniTextFontEditor.CreateColorFontAsset,
                    "unitextcolorfont-icon");
            }
            if (UniTextFontEditor.CreateFontsCombinedAssetValidate())
                menu.Add("Fonts/Font Stack (Combined)", UniTextFontEditor.CreateFontsCombined,
                    "unitextfonts-icon");
            if (UniTextFontEditor.CreateFontsAssetValidate())
                menu.Add("Fonts/Font Stack (Per Font)", UniTextFontEditor.CreateFontsPerFont,
                    "unitextfonts-icon");
            if (UniTextFontEditor.CreateFontVariantValidate())
                menu.Add("Fonts/Font Variant", UniTextFontEditor.CreateFontVariant,
                    "unitext-font-variant-icon");
            menu.Add("Fonts/System Font Asset", UniTextSystemFontEditor.CreateSystemFontAsset,
                "unitext-system-font-icon");
        }

        private static void AddPresets(CommandMenu menu)
        {
            menu.Add("Presets/Style Preset", () => CreateAsset<StylePreset>("StylePreset"),
                "text-style");
            menu.Add("Presets/Modifier Graph Preset",
                () => CreateAsset<ModifierGraphPreset>("ModifierGraphPreset"),
                "unitext-modifier-graph-icon");
            menu.Add("Presets/Behavior Preset",
                () => CreateAsset<InputBehaviorPreset>("InputBehaviorPreset"),
                "unitext-behavior-preset-icon");
            menu.Add("Presets/Range Channel", () => CreateAsset<RangeChannel>("RangeChannel"), "tag");
            menu.Add("Presets/Paints", () => CreateAsset<UniTextPaints>("UniTextPaints"),
                "unitext-paints-icon");
            menu.Add("Presets/Objects", () => CreateAsset<UniTextObjects>("UniTextObjects"),
                "inline-object");
            menu.Add("Presets/Sprites", () => CreateAsset<UniTextSprites>("UniTextSprites"),
                "unitextsprites-icon");
            menu.Add("Presets/Reveal Handlers",
                () => CreateAsset<UniTextRevealHandlers>("UniTextRevealHandlers"), "animation");
        }

        private static void AddTools(CommandMenu menu)
        {
            menu.Add("Tools/Tools Window", UniTextToolsWindow.ShowWindow, "tune");
            menu.Add("Tools/Glyph Diagnostic", GlyphDiagnosticWindow.Open, "font");
            menu.Add("Tools/Clipboard Inspector", UniTextClipboardInspectorWindow.Open, "clipboard");
            menu.Add("Tools/Range Debugger", UniTextRangeDebuggerWindow.Open, "tag");
            menu.Add("Tools/Migration", UniTextMigrationWindow.ShowWindow, "utility");
            menu.Add("Tools/Custom Effect", CreateCustomShaderMenu.Create, "material");
            menu.Separator("Tools");
            menu.AddToggle("Tools/Respect Scene Visibility",
                () => SceneVisibilityOverlay.Respect = !SceneVisibilityOverlay.Respect,
                SceneVisibilityOverlay.Respect, "unitext-visibility-icon");
            menu.AddToggle("Tools/Show Scene Visibility Overlay",
                () => SceneVisibilityOverlay.Show = !SceneVisibilityOverlay.Show,
                SceneVisibilityOverlay.Show, "eye");
        }

        private static void CreateAsset<T>(string fileName) where T : ScriptableObject
            => ProjectWindowUtil.CreateAsset(ScriptableObject.CreateInstance<T>(),
                fileName + ".asset");
    }
}
