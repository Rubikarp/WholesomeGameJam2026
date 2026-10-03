using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(UniTextSystemFont))]
    [CanEditMultipleObjects]
    internal sealed class UniTextSystemFontEditor : FullWidthEditor
    {
        private static readonly UniTextSystemFont.FontPlatform[] tabs =
        {
            UniTextSystemFont.FontPlatform.Common,
            UniTextSystemFont.FontPlatform.Windows,
            UniTextSystemFont.FontPlatform.MacOS,
            UniTextSystemFont.FontPlatform.Linux,
            UniTextSystemFont.FontPlatform.iOS,
            UniTextSystemFont.FontPlatform.Android,
        };

        private static readonly string[] tabLabels =
            { "Common", "Win", "macOS", "Linux", "iOS", "Android" };

        private static readonly string[] configPropertyNames =
            { "common", "windows", "macos", "linux", "ios", "android" };

        [SerializeField] private int activeTabIndex;
        private readonly List<InspectorPillButton> tabButtons = new();
        private SerializedProperty fontScale;
        private SerializedProperty italicStyle;
        private SerializedProperty spacingOffset;
        private SerializedProperty fakeBoldWeight;
        private VisualElement activeConfiguration;
        private VisualElement resolutionStatus;
        private bool faceInfoExpanded;

        private void OnEnable()
        {
            fontScale = InspectorHelpers.RequireProperty(serializedObject, "fontScale");
            italicStyle = InspectorHelpers.RequireProperty(serializedObject, "italicStyle");
            spacingOffset = InspectorHelpers.RequireProperty(serializedObject, "spacingOffset");
            fakeBoldWeight = InspectorHelpers.RequireProperty(serializedObject, "fakeBoldWeight");
            activeTabIndex = Mathf.Clamp(activeTabIndex, 0, tabs.Length - 1);
        }

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = UniTextInspectorTheme.CreateRoot();
            var configuration = InspectorVisuals.CreateSection("Platform Configuration");
            var tabRow = InspectorVisuals.CreateRow();
            tabButtons.Clear();
            for (var i = 0; i < tabs.Length; i++)
            {
                var index = i;
                var button = new InspectorPillButton(() => SelectTab(index))
                {
                    text = tabLabels[i],
                };
                button.style.flexGrow = 1f;
                tabButtons.Add(button);
                tabRow.Add(button);
            }
            configuration.Add(tabRow);
            activeConfiguration = InspectorVisuals.CreateStack();
            configuration.Add(activeConfiguration);
            root.Add(configuration);

            var shared = InspectorVisuals.CreateSection("Shared Settings");
            shared.Add(new HelpBox(
                "These settings apply across all platforms. Glyph overrides are not available on system fonts because glyph indices are font-file-specific.",
                HelpBoxMessageType.Info));
            shared.Add(SerializedPropertyField.Create(fontScale));
            shared.Add(SerializedPropertyField.Create(italicStyle));
            shared.Add(SerializedPropertyField.Create(spacingOffset));
            shared.Add(SerializedPropertyField.Create(fakeBoldWeight));
            root.Add(shared);

            var statusSection = InspectorVisuals.CreateSection("Resolution Status");
            resolutionStatus = InspectorVisuals.CreateStack();
            statusSection.Add(resolutionStatus);
            root.Add(statusSection);

            for (var i = 0; i < configPropertyNames.Length; i++)
            {
                var config = InspectorHelpers.RequireProperty(serializedObject,
                    configPropertyNames[i]);
                TrackResolutionProperty(root,
                    InspectorHelpers.RequireRelative(config, "fontName"));
                var face = InspectorHelpers.RequireRelative(config, "faceInfo");
                TrackResolutionProperty(root,
                    InspectorHelpers.RequireRelative(face, "familyName"));
                TrackResolutionProperty(root,
                    InspectorHelpers.RequireRelative(face, "styleName"));
                TrackResolutionProperty(root,
                    InspectorHelpers.RequireRelative(face, "unitsPerEm"));
            }
            BuildActiveConfiguration();
            RefreshResolutionStatus();
            return root;
        }

        private void TrackResolutionProperty(VisualElement root,
            SerializedProperty property)
            => root.TrackPropertyValue(property, _ => RefreshResolutionStatus());

        private void SelectTab(int index)
        {
            if (activeTabIndex == index) return;
            activeTabIndex = index;
            BuildActiveConfiguration();
        }

        private void BuildActiveConfiguration()
        {
            InspectorVisuals.ClearContent(activeConfiguration);
            for (var i = 0; i < tabButtons.Count; i++)
                tabButtons[i].SetState(i == activeTabIndex, false,
                    EditorResources.ToggleAccent);

            var tab = tabs[activeTabIndex];
            if (tab == UniTextSystemFont.FontPlatform.Common)
                activeConfiguration.Add(new HelpBox(
                    "Common selection maps to a per-platform equivalent at runtime and is used when the active platform leaves its font unset.",
                    HelpBoxMessageType.Info));

            var config = InspectorHelpers.RequireProperty(
                serializedObject, configPropertyNames[activeTabIndex]);
            activeConfiguration.Add(CreateFontPicker(tab,
                InspectorHelpers.RequireRelative(config, "fontName")));

            var faceInfo = InspectorVisuals.CreateSection(
                "Face Info Overrides", true, faceInfoExpanded);
            faceInfo.Changed += value => faceInfoExpanded = value;
            faceInfo.Add(new HelpBox(
                "Unset values come from the loaded font. Enable a row to override that value.",
                HelpBoxMessageType.Info));
            var face = InspectorHelpers.RequireRelative(config, "faceInfo");
            faceInfo.Add(CreateStringOverride(InspectorHelpers.RequireRelative(face, "familyName"), "Family Name"));
            faceInfo.Add(CreateStringOverride(InspectorHelpers.RequireRelative(face, "styleName"), "Style Name"));
            faceInfo.Add(CreateItalicOverride(InspectorHelpers.RequireRelative(face, "italicTriState")));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "weightClass"), "Weight Class", 100, 900));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "unitsPerEm"), "Units Per Em"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "lineHeight"), "Line Height"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "ascentLine"), "Ascent Line"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "capLine"), "Cap Line"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "meanLine"), "Mean Line (x-height)"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "descentLine"), "Descent Line"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "underlineOffset"), "Underline Offset"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "underlineThickness"), "Underline Thickness"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "strikethroughOffset"), "Strikethrough Offset"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "strikethroughThickness"), "Strikethrough Thickness"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "superscriptOffset"), "Superscript Offset"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "superscriptSize"), "Superscript Size"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "subscriptOffset"), "Subscript Offset"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "subscriptSize"), "Subscript Size"));
            faceInfo.Add(CreateIntOverride(InspectorHelpers.RequireRelative(face, "tabWidth"), "Tab Width"));
            activeConfiguration.Add(faceInfo);

            var renderingTitle = new Label("Rendering Overrides");
            renderingTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            activeConfiguration.Add(renderingTitle);
            activeConfiguration.Add(CreateFloatOverride(
                InspectorHelpers.RequireRelative(config, "sdfDetailMultiplier"), "SDF Detail",
                value => !float.IsNaN(value) && value > 0f,
                UniTextSystemFont.UnsetFloat, 1f, 0.25f, 8f));
            activeConfiguration.Add(CreateIntSliderOverride(
                InspectorHelpers.RequireRelative(config, "tileSizeOffsetOverride"), "Tile Size Offset",
                UniTextSystemFont.UnsetInt, 0, -2, 2));
        }

        private VisualElement CreateFontPicker(UniTextSystemFont.FontPlatform tab,
            SerializedProperty property)
        {
            var choices = new List<string>(BuildNameOptions(tab));
            var binding = new SerializedPropertyBinding(property);
            var current = property.stringValue ?? string.Empty;
            var index = choices.IndexOf(current);
            if (index < 0) index = 0;
            var field = new SelectorField<string>("Font", choices, index)
            {
                tooltip = "Pick a font guaranteed to ship with this platform.",
            };
            InspectorVisuals.MarkFieldAxis(field);
            return new SerializedPropertyContext(
                    binding, property, property.displayName)
                .Bind(field, value => value == choices[0] ? string.Empty : value,
                    read: value => string.IsNullOrEmpty((string)value)
                        ? choices[0]
                        : (string)value,
                    undoName: "Change System Font");
        }

        private static string[] BuildNameOptions(UniTextSystemFont.FontPlatform tab)
        {
            if (tab == UniTextSystemFont.FontPlatform.Common)
            {
                var result = new string[SystemFontCatalog.Common.Length + 1];
                result[0] = "(default)";
                for (var i = 0; i < SystemFontCatalog.Common.Length; i++)
                    result[i + 1] = SystemFontCatalog.Common[i].displayName;
                return result;
            }

            var entries = SystemFontCatalog.EntriesFor(tab);
            var names = new string[entries.Length + 1];
            names[0] = "(inherit Common)";
            for (var i = 0; i < entries.Length; i++)
                names[i + 1] = entries[i].displayName;
            return names;
        }

        private static VisualElement CreateStringOverride(SerializedProperty property,
            string label)
        {
            var binding = new SerializedPropertyBinding(property);
            var field = new TextField(label);
            return CreateOverrideRow(binding, field,
                value => !string.IsNullOrEmpty((string)value),
                () => string.Empty,
                () => string.Empty);
        }

        private static VisualElement CreateIntOverride(SerializedProperty property,
            string label, int min = int.MinValue + 1, int max = int.MaxValue)
        {
            var binding = new SerializedPropertyBinding(property);
            var field = new IntegerField(label);
            field.RegisterValueChangedCallback(evt => field.SetValueWithoutNotify(
                Mathf.Clamp(evt.newValue, min, max)));
            return CreateOverrideRow(binding, field,
                value => (int)value != UniTextSystemFont.UnsetInt,
                () => UniTextSystemFont.UnsetInt,
                () => 0,
                value => Mathf.Clamp((int)value, min, max));
        }

        private static VisualElement CreateItalicOverride(SerializedProperty property)
        {
            var binding = new SerializedPropertyBinding(property);
            return CreateOverrideRow(binding, new InspectorToggle("Italic"),
                value => (int)value >= 0,
                () => -1,
                () => 0,
                value => (bool)value ? 1 : 0,
                value => (int)value == 1);
        }

        private static VisualElement CreateIntSliderOverride(SerializedProperty property,
            string label, int unsetValue, int defaultValue, int min, int max)
        {
            var binding = new SerializedPropertyBinding(property);
            return CreateOverrideRow(binding, new InspectorSliderInt(label, min, max),
                value => (int)value != unsetValue,
                () => unsetValue,
                () => defaultValue);
        }

        private static VisualElement CreateFloatOverride(SerializedProperty property,
            string label, Func<float, bool> isSet, float unsetValue,
            float defaultValue, float min, float max)
        {
            var binding = new SerializedPropertyBinding(property);
            return CreateOverrideRow(binding, new InspectorSlider(label, min, max),
                value => isSet((float)value),
                () => unsetValue,
                () => defaultValue);
        }

        private static VisualElement CreateOverrideRow<T>(SerializedPropertyBinding binding,
            BaseField<T> field, Func<object, bool> isSet, Func<object> unsetValue,
            Func<object> defaultValue, Func<T, object> write = null,
            Func<object, T> read = null)
        {
            var enabled = new InspectorToggle();
            InspectorVisuals.MarkFieldAxis(field);
            var row = InspectorVisuals.CreateOverrideRow(enabled, field);

            void Refresh()
            {
                var targets = binding.SerializedObject.targetObjects;
                var activeCount = 0;
                object firstActive = null;
                for (var i = 0; i < targets.Length; i++)
                {
                    var targetValue = binding.GetValue(targets[i]);
                    if (!isSet(targetValue)) continue;
                    firstActive ??= targetValue;
                    activeCount++;
                }
                var active = activeCount > 0;
                enabled.SetValueWithoutNotify(activeCount == targets.Length);
                enabled.showMixedValue = active && activeCount < targets.Length;
                field.SetEnabled(active);
                var value = active
                    ? read != null ? read(firstActive) : (T)firstActive
                    : read != null ? read(defaultValue()) : (T)defaultValue();
                if (!EqualityComparer<T>.Default.Equals(field.value, value))
                    field.SetValueWithoutNotify(value);
                field.showMixedValue = binding.HasMultipleValues;
            }

            enabled.RegisterValueChangedCallback(evt =>
            {
                binding.TransformValue(value => evt.newValue
                    ? isSet(value) ? value : defaultValue()
                    : unsetValue());
                if (evt.newValue)
                {
                    field.SetEnabled(true);
                    field.Focus();
                }
                else
                {
                    Refresh();
                }
            });
            field.RegisterValueChangedCallback(evt =>
            {
                binding.SetValue(write != null ? write(evt.newValue) : evt.newValue);
            });
            return new SerializedPropertyContext(binding, binding.FindSerializedProperty(),
                binding.DisplayName).Bind(row, Refresh);
        }

        private void RefreshResolutionStatus()
        {
            if (resolutionStatus == null) return;
            var font = (UniTextSystemFont)target;
            InspectorVisuals.ClearContent(resolutionStatus);
            if (targets.Length > 1)
            {
                var loaded = 0;
                for (var i = 0; i < targets.Length; i++)
                    if (((UniTextSystemFont)targets[i]).HasFontData) loaded++;
                resolutionStatus.Add(InspectorVisuals.CreateStatusLabel(
                    loaded == targets.Length
                        ? $"\u2713 All {targets.Length} fonts resolved"
                        : loaded == 0
                            ? "\u2717 No selected fonts resolved"
                            : $"\u26a0 {loaded}/{targets.Length} fonts resolved",
                    loaded == targets.Length
                        ? EditorResources.StatusSuccess
                        : loaded == 0
                            ? EditorResources.StatusError
                            : EditorResources.StatusWarning));
                resolutionStatus.Add(new Button(() =>
                {
                    for (var i = 0; i < targets.Length; i++)
                    {
                        var selected = (UniTextSystemFont)targets[i];
                        selected.Invalidate();
                        EditorUtility.SetDirty(selected);
                    }
                    RefreshResolutionStatus();
                }) { text = $"Re-Resolve ({targets.Length})" });
                return;
            }
            if (font.HasFontData)
            {
                resolutionStatus.Add(InspectorVisuals.CreateStatusLabel(
                    $"✓ Loaded ({font.RawFontDataSize:N0} bytes)",
                    EditorResources.StatusSuccess));
                resolutionStatus.Add(new Label($"Font: {font.ResolvedFontName ?? "?"}"));
                resolutionStatus.Add(new Label($"Platform: {font.ResolvedPlatform}"));
                resolutionStatus.Add(new Label($"Path: {font.ResolvedPath ?? "?"}"));
                resolutionStatus.Add(new Label($"Family: {font.FaceInfo.familyName ?? "?"}"));
                resolutionStatus.Add(new Label($"Style: {font.FaceInfo.styleName ?? "?"}"));
                resolutionStatus.Add(new Label($"UPEM: {font.UnitsPerEm}"));
            }
            else
            {
                resolutionStatus.Add(InspectorVisuals.CreateStatusLabel(
                    "✗ Font not resolved on this machine.", EditorResources.StatusError));
                var hostPlatform = SystemFontCatalog.CurrentPlatform();
                var foreignPlatform = font.ResolvedPlatform != hostPlatform;
                resolutionStatus.Add(new HelpBox(foreignPlatform
                        ? $"Previewing {font.ResolvedPlatform} on a {hostPlatform} host. Target-platform families are not installed here; this is expected."
                        : "Neither the selected font nor the Common fallback is installed, and the OS offered no default face.",
                    foreignPlatform ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning));
            }

            resolutionStatus.Add(new Button(() =>
            {
                font.Invalidate();
                EditorUtility.SetDirty(font);
                RefreshResolutionStatus();
            }) { text = "Re-Resolve" });
        }

        [MenuItem(UniTextMenu.Create.SystemFontAsset, false, 100)]
        internal static void CreateSystemFontAsset()
        {
            var asset = CreateInstance<UniTextSystemFont>();
            asset.name = "SystemFont";
            var folder = GetSelectedFolder();
            var path = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(folder, "SystemFont.asset").Replace("\\", "/"));
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static string GetSelectedFolder()
        {
            foreach (var value in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(value);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.IsValidFolder(path)) return path;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) return directory.Replace("\\", "/");
            }
            return "Assets";
        }
    }
}
