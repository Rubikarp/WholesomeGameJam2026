using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal partial class UniTextFontEditor
    {
        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var isMultiEdit = targets.Length > 1;
            var isVariant = variantSourceProp != null;
            var root = UniTextInspectorTheme.CreateRoot();

            if (isVariant)
            {
                var section = CreateToolkitSection("Variant Source");
                section.Add(SerializedPropertyField.Create(variantSourceProp));
                var source = isMultiEdit
                    ? null
                    : variantSourceProp.objectReferenceValue as UniTextFont;
                root.Add(section);
                if (source != null && source.FaceCount != 1)
                    root.Add(CreateDeferredFaceSelector(
                        source, faceIndexProp, false));
            }
            else if (sourceFontProp.objectReferenceValue != null ||
                     sourceFontProp.hasMultipleDifferentValues)
            {
                var section = CreateToolkitSection("Source Font (Editor Only)");
                var source = SerializedPropertyField.Create(sourceFontProp, "Source Font File");
                source.SetEnabled(false);
                section.Add(source);
                root.Add(section);
            }

            root.Add(CreateFontDataStatus(isMultiEdit));
            if (!isVariant && !isMultiEdit && ((UniTextFont)target).FaceCount != 1)
            {
                var ownFace = InspectorHelpers.RequireRelative(faceInfoProp, "faceIndex");
                root.Add(CreateDeferredFaceSelector(
                    (UniTextFont)target, ownFace, true));
            }

            root.Add(CreateFaceInfo(isVariant));
            var variableAxes = GetCommonVariableAxes(
                out var hasVariableFonts, out var completeVariableSchema);
            if (variableAxes.Count > 0)
                root.Add(CreateVariableAxes(variableAxes, completeVariableSchema));
            else if (isMultiEdit && hasVariableFonts)
                root.Add(CreateVariableAxesUnavailable());

            var metrics = CreateToolkitSection("Metrics");
            metrics.Add(SerializedPropertyField.CreateRelative(faceInfoProp, "lineHeight"));
            metrics.Add(SerializedPropertyField.CreateRelative(faceInfoProp, "ascentLine"));
            metrics.Add(SerializedPropertyField.CreateRelative(faceInfoProp, "descentLine"));
            root.Add(metrics);

            var spacing = CreateToolkitSection("Spacing & Style");
            spacing.Add(SerializedPropertyField.Create(spacingOffsetProp));
            spacing.Add(SerializedPropertyField.Create(spaceAdvanceProp, "Space Width"));
            spacing.Add(SerializedPropertyField.Create(italicStyleProp));
            spacing.Add(SerializedPropertyField.Create(fakeBoldWeightProp));
            root.Add(spacing);

            var sizing = CreateToolkitSection("Sizing");
            sizing.Add(SerializedPropertyField.Create(fontScaleProp));
            sizing.Add(SerializedPropertyField.Create(
                participatesInNormalizationProp, "Normalize Size"));
            root.Add(sizing);

            var rasterization = CreateToolkitSection("Rasterization");
            if (colorPixelSizeProp != null)
                rasterization.Add(SerializedPropertyField.Create(
                    colorPixelSizeProp, "Color Pixel Size"));
            else
            {
                rasterization.Add(SerializedPropertyField.Create(
                    sdfDetailMultiplierProp, "SDF Detail"));
                rasterization.Add(SerializedPropertyField.Create(
                    tileSizeOffsetProp, "Tile Size Offset"));
            }
            root.Add(rasterization);

            if (colorPixelSizeProp == null)
                root.Add(isMultiEdit
                    ? CreateMultiGlyphOverrides()
                    : CreateGlyphOverrides());
            root.Add(CreateRuntimeData());

#if UNITEXT_DEBUG
            if (!isMultiEdit)
                root.Add(CreateDebug((UniTextFont)target));
#endif
            return root;
        }

        private static InspectorSection CreateToolkitSection(string title,
            bool collapsible = false, bool initiallyExpanded = true) =>
            InspectorVisuals.CreateSection(title, collapsible, initiallyExpanded);

        private VisualElement CreateFaceSelector(UniTextFont source,
            SerializedProperty property, bool reseed)
        {
            var labels = GetFaceLabels(source);
            if (labels == null)
                return SerializedPropertyField.Create(property, "Face Index");
            var choices = new List<string>(labels.Length);
            for (var i = 0; i < labels.Length; i++) choices.Add(labels[i]);
            var binding = new SerializedPropertyBinding(property);
            var field = new SelectorField<string>("Face Index", choices,
                Mathf.Clamp(property.intValue, 0, labels.Length - 1))
            {
                tooltip = property.tooltip,
            };
            InspectorVisuals.MarkFieldAxis(field);
            field.RegisterValueChangedCallback(evt =>
            {
                var index = field.Index;
                SetFaceIndex(binding, index, reseed);
            });
            void Refresh()
            {
                var index = Mathf.Clamp((int)binding.Value, 0, choices.Count - 1);
                field.SetValueWithoutNotify(choices[index]);
                field.showMixedValue = binding.HasMultipleValues;
            }
            return new SerializedPropertyContext(binding, property, "Face Index")
                .Bind(field, Refresh);
        }

        private VisualElement CreateDeferredFaceSelector(UniTextFont source,
            SerializedProperty property, bool reseed)
        {
            var section = CreateToolkitSection("Collection Face", true, false);
            var built = false;
            section.Changed += expanded =>
            {
                if (!expanded || built) return;
                built = true;
                section.Add(CreateFaceSelector(source, property, reseed));
            };
            return section;
        }

        private static void SetFaceIndex(SerializedPropertyBinding binding,
            int index, bool reseed)
        {
            if (!reseed)
            {
                binding.SetValue(index, "Change Font Face");
                return;
            }
            var targets = binding.SerializedObject.targetObjects;
            for (var i = 0; i < targets.Length; i++)
                if (!((UniTextFont)targets[i]).TryReadFaceInfo(index, out _, out _))
                    throw new InvalidOperationException(
                        $"Face index {index} is unavailable on '{targets[i].name}'.");
            binding.EditSerializedProperties(selected =>
            {
                var font = (UniTextFont)selected.serializedObject.targetObject;
                font.TryReadFaceInfo(index, out var info, out var unitsPerEm);
                selected.intValue = index;
                InspectorHelpers.RequireProperty(
                    selected.serializedObject, "faceInfo").boxedValue = info;
                InspectorHelpers.RequireProperty(
                    selected.serializedObject, "unitsPerEm").intValue = unitsPerEm;
            }, "Change Font Face");
            for (var i = 0; i < targets.Length; i++)
                ((UniTextFont)targets[i]).EditorEnsureFaceInfo();
        }

        private VisualElement CreateFontDataStatus(bool isMultiEdit)
        {
            var section = CreateToolkitSection("Font Data Status");
            if (isMultiEdit)
            {
                var withData = 0;
                foreach (var value in targets)
                    if (((UniTextFont)value).HasFontData) withData++;
                var color = withData == targets.Length
                    ? EditorResources.StatusSuccess
                    : withData == 0
                        ? EditorResources.StatusError
                        : EditorResources.StatusWarning;
                var text = withData == targets.Length
                    ? $"✓ All {targets.Length} fonts have data"
                    : withData == 0
                        ? "✗ No font data on any selected font"
                        : $"⚠ {withData}/{targets.Length} fonts have data";
                section.Add(InspectorVisuals.CreateStatusLabel(text, color));
                return section;
            }

            var font = (UniTextFont)target;
            var hasData = font.HasFontData;
            var raw = font.RawFontDataSize;
            section.Add(InspectorVisuals.CreateStatusLabel(hasData
                    ? $"✓ Font data available ({raw:N0} bytes)"
                    : "✗ No font data — text will not render",
                hasData ? EditorResources.StatusSuccess : EditorResources.StatusError));
            if (!hasData)
            {
                section.Add(new HelpBox(
                    "No font data is embedded. Create a UniText Font Asset from a source Font file.",
                    HelpBoxMessageType.Warning));
                return section;
            }

            var compressed = font.CompressedFontDataSize;
            if (compressed > 0 && compressed < raw)
                section.Add(InspectorVisuals.CreateStatusLabel(
                    $"\u2937 Compressed in asset: {compressed:N0} bytes ({(float)compressed / raw:P0} of raw)",
                    EditorResources.StatusInfo));
            section.Add(new HelpBox(sourceFontProp.objectReferenceValue != null
                    ? "Compressed font data stays in the asset in the Editor and WebGL; other players package it alongside the Player or SBP content that uses this font. The source reference is editor-only."
                    : "Compressed font data stays in the asset in the Editor and WebGL; other players package it alongside the Player or SBP content that uses this font.",
                HelpBoxMessageType.Info));
            return section;
        }

        private VisualElement CreateFaceInfo(bool isVariant)
        {
            var section = CreateToolkitSection("Face Info", true, false);
            if (isVariant)
                section.Add(new HelpBox(
                    "Seeded from Source, then fully owned by this variant. Source only provides font bytes.",
                    HelpBoxMessageType.Info));

            var deferred = new HashSet<string>
            {
                "familyName", "styleName", "weightClass", "isItalic", "faceIndex",
                "lineHeight", "ascentLine", "descentLine",
            };
            foreach (var property in InspectorHelpers.VisibleChildren(faceInfoProp))
                if (!deferred.Contains(property.name))
                    section.Add(SerializedPropertyField.Create(property));

            foreach (var name in new[] { "familyName", "styleName", "weightClass", "isItalic" })
            {
                var field = SerializedPropertyField.Create(
                    InspectorHelpers.RequireRelative(faceInfoProp, name));
                field.SetEnabled(isVariant);
                section.Add(field);
            }
            section.Add(new Button(() =>
            {
                for (var i = 0; i < targets.Length; i++)
                {
                    var font = (UniTextFont)targets[i];
                    var index = font is UniTextFontVariant variant
                        ? variant.FaceIndex
                        : font.FaceInfo.faceIndex;
                    if (!font.TryReadFaceInfo(index, out _, out _))
                        throw new InvalidOperationException(
                            $"Face index {index} is unavailable on '{font.name}'.");
                }
                new SerializedPropertyBinding(faceInfoProp).EditSerializedProperties(property =>
                {
                    var font = (UniTextFont)property.serializedObject.targetObject;
                    var index = font is UniTextFontVariant variant
                        ? variant.FaceIndex
                        : font.FaceInfo.faceIndex;
                    font.TryReadFaceInfo(index, out var info, out var unitsPerEm);
                    property.boxedValue = info;
                    InspectorHelpers.RequireProperty(
                        property.serializedObject, "unitsPerEm").intValue = unitsPerEm;
                }, "Reset Font Metrics");
                for (var i = 0; i < targets.Length; i++)
                    ((UniTextFont)targets[i]).EditorEnsureFaceInfo();
            }) { text = isVariant ? "Reset Metrics from Source" : "Reset Metrics" });
            return section;
        }

        private readonly struct VariableAxisDescriptor
        {
            public readonly uint tag;
            public readonly float minValue;
            public readonly float maxValue;
            public readonly float defaultValue;

            public VariableAxisDescriptor(uint tag, float minValue, float maxValue,
                float defaultValue)
            {
                this.tag = tag;
                this.minValue = minValue;
                this.maxValue = maxValue;
                this.defaultValue = defaultValue;
            }
        }

        private List<VariableAxisDescriptor> GetCommonVariableAxes(
            out bool hasVariableFonts, out bool complete)
        {
            hasVariableFonts = false;
            complete = true;
            var result = new List<VariableAxisDescriptor>();
            var targetAxes = new HB.hb_ot_var_axis_info_t[targets.Length][];
            for (var i = 0; i < targets.Length; i++)
                targetAxes[i] = ((UniTextFont)targets[i]).EditorVariableAxes;
            var first = targetAxes[0];
            if (first == null)
            {
                for (var i = 1; i < targets.Length; i++)
                    hasVariableFonts |= targetAxes[i] != null;
                complete = !hasVariableFonts;
                return result;
            }

            hasVariableFonts = true;
            for (var axisIndex = 0; axisIndex < first.Length; axisIndex++)
            {
                var axis = first[axisIndex];
                var compatible = true;
                for (var targetIndex = 1; targetIndex < targets.Length; targetIndex++)
                {
                    var candidateAxes = targetAxes[targetIndex];
                    var found = false;
                    if (candidateAxes != null)
                    {
                        for (var candidateIndex = 0;
                             candidateIndex < candidateAxes.Length; candidateIndex++)
                        {
                            var candidate = candidateAxes[candidateIndex];
                            if (candidate.tag != axis.tag) continue;
                            found = Mathf.Approximately(candidate.minValue, axis.minValue) &&
                                    Mathf.Approximately(candidate.maxValue, axis.maxValue) &&
                                    Mathf.Approximately(candidate.defaultValue, axis.defaultValue);
                            break;
                        }
                    }
                    if (found) continue;
                    compatible = false;
                    complete = false;
                    break;
                }
                if (compatible)
                    result.Add(new VariableAxisDescriptor(axis.tag, axis.minValue,
                        axis.maxValue, axis.defaultValue));
            }

            for (var targetIndex = 1; targetIndex < targets.Length; targetIndex++)
            {
                var candidateAxes = targetAxes[targetIndex];
                if (candidateAxes == null || candidateAxes.Length != first.Length)
                    complete = false;
            }
            return result;
        }

        private VisualElement CreateVariableAxes(
            IReadOnlyList<VariableAxisDescriptor> axes, bool complete)
        {
            var section = CreateToolkitSection("Variable Font Axes");
            section.Add(new Label(
                "Defaults used when no <var> tag is set. Reset keeps the font's own default."));
            if (!complete)
                section.Add(new HelpBox(
                    "Only axes with the same tag, range, and default on every selected font are shown.",
                    HelpBoxMessageType.Info));
            var binding = new SerializedPropertyBinding(axisDefaultsProp);
            var context = new SerializedPropertyContext(
                binding, axisDefaultsProp, "Axis Defaults");
            context.Bind(section);
            foreach (var axis in axes)
            {
                var tag = unchecked((int)axis.tag);
                var row = InspectorVisuals.CreateRow();
                var slider = new InspectorSlider(AxisTagToString(axis.tag),
                    axis.minValue, axis.maxValue);
                InspectorVisuals.AlignFields(slider);
                slider.tooltip =
                    $"min {axis.minValue} · default {axis.defaultValue} · max {axis.maxValue}";
                slider.style.flexGrow = 1f;
                row.Add(slider);
                var reset = new Button(() =>
                    RemoveAxisDefault(binding, tag)) { text = "Reset" };
                row.Add(reset);

                void Refresh()
                {
                    ReadAxisDefault(binding, tag, axis.defaultValue,
                        out var value, out var mixed, out var hasOverride);
                    slider.SetValueWithoutNotify(value);
                    slider.showMixedValue = mixed;
                    reset.SetEnabled(hasOverride);
                }

                slider.RegisterValueChangedCallback(evt =>
                    SetAxisDefault(binding, tag, evt.newValue, axis.defaultValue));
                section.Add(context.Observe(row, Refresh));
            }
            return section;
        }

        private static VisualElement CreateVariableAxesUnavailable()
        {
            var section = CreateToolkitSection("Variable Font Axes");
            section.Add(new HelpBox(
                "The selected fonts do not share a compatible variable-axis schema.",
                HelpBoxMessageType.Info));
            return section;
        }

        private static void ReadAxisDefault(SerializedPropertyBinding binding,
            int tag, float defaultValue, out float value, out bool mixed,
            out bool hasOverride)
        {
            var currentValue = defaultValue;
            var differs = false;
            var overridden = false;
            var first = true;
            binding.VisitTargetProperties((_, array) =>
            {
                var index = FindAxisDefaultIndex(array, tag);
                overridden |= index >= 0;
                var current = index < 0
                    ? defaultValue
                    : InspectorHelpers.RequireRelative(
                        array.GetArrayElementAtIndex(index), "value").floatValue;
                if (first)
                {
                    currentValue = current;
                    first = false;
                }
                else if (!Mathf.Approximately(current, currentValue))
                    differs = true;
            });
            value = currentValue;
            mixed = differs;
            hasOverride = overridden;
        }

        private VisualElement CreateGlyphOverrides()
        {
            var section = CreateToolkitSection("Glyph Overrides");
            var input = new TextField("Input Text")
            {
                value = glyphPickerText,
            };
            InspectorVisuals.Attach(input);
            section.Add(input);
            var grid = InspectorVisuals.CreateWrapRow();
            section.Add(grid);
            var list = SerializedPropertyField.Create(glyphOverridesProp, "Overrides");
            section.Add(list);
            var binding = new SerializedPropertyBinding(glyphOverridesProp);

            void SyncSelection()
            {
                glyphPickerSelection.Clear();
                var property = binding.RequireSerializedProperty();
                for (var i = 0; i < property.arraySize; i++)
                    glyphPickerSelection.Add(InspectorHelpers.RequireRelative(
                        property.GetArrayElementAtIndex(i), "glyphIndex").intValue);
            }

            void RefreshGrid()
            {
                InspectorVisuals.ClearContent(grid);
                grid.style.display = glyphPickerEntries.Count == 0
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
                for (var i = 0; i < glyphPickerEntries.Count; i++)
                {
                    var entry = glyphPickerEntries[i];
                    var button = new InspectorPillButton(() =>
                    {
                        ToggleGlyphOverride(binding, entry.glyphIndex);
                        SyncSelection();
                        RefreshGrid();
                    });
                    button.style.width = 72f;
                    button.style.height = 72f;
                    button.style.flexDirection = FlexDirection.Column;
                    button.SetState(glyphPickerSelection.Contains(entry.glyphIndex),
                        false, EditorResources.ToggleAccent);
                    var image = new Image
                    {
                        image = entry.preview,
                        scaleMode = ScaleMode.ScaleToFit,
                    };
                    image.style.flexGrow = 1f;
                    button.Add(image);
                    button.Add(new Label(entry.label));
                    grid.Add(button);
                }
            }

            input.RegisterValueChangedCallback(evt =>
            {
                glyphPickerText = evt.newValue ?? string.Empty;
                RebuildGlyphPicker();
                RefreshGrid();
            });
            if (!string.IsNullOrEmpty(glyphPickerText)) RebuildGlyphPicker();
            new SerializedPropertyContext(binding, glyphOverridesProp, "Glyph Overrides")
                .Observe(section, () =>
                {
                    SyncSelection();
                    RefreshGrid();
                });
            return section;
        }

        private VisualElement CreateMultiGlyphOverrides()
        {
            var section = CreateToolkitSection("Glyph Overrides");
            section.Add(new HelpBox(
                "The glyph picker is available for a single font because glyph indices are font-specific. Rows whose indices exist in every selected override list remain editable.",
                HelpBoxMessageType.Info));
            section.Add(SerializedPropertyField.Create(glyphOverridesProp, "Overrides"));
            return section;
        }

        private VisualElement CreateRuntimeData()
        {
            var section = CreateToolkitSection("Runtime Data");
            var statistics = new Label();
            section.Add(statistics);
            var clear = new Button(() =>
            {
                foreach (var value in targets)
                    ((UniTextFont)value).ClearDynamicData();
                Refresh();
            }) { text = targets.Length == 1
                    ? "Clear Runtime Data"
                    : $"Clear Runtime Data ({targets.Length} fonts)" };
            clear.AddToClassList("unitext-runtime-data__clear");
            section.Add(clear);

            void Refresh()
            {
                var glyphs = 0;
                var characters = 0;
                foreach (var value in targets)
                {
                    var font = (UniTextFont)value;
                    glyphs += font.MaterializedGlyphCount;
                    characters += font.MaterializedCharacterCount;
                }
                statistics.text = targets.Length == 1
                    ? $"Glyphs: {glyphs}  |  Characters: {characters}"
                    : $"{targets.Length} fonts  |  Glyphs: {glyphs}  |  Characters: {characters}";
            }

            Refresh();
            return section;
        }

#if UNITEXT_DEBUG
        private VisualElement CreateDebug(UniTextFont font)
        {
            var section = CreateToolkitSection("Debug");
            var colorAtlas = GlyphAtlas.Color;
            if (colorAtlas == null || colorAtlas.AtlasTexture == null ||
                colorAtlas.PageCount == 0)
            {
                section.Add(new Label("No atlas textures available."));
                return section;
            }
            section.Add(new Label(
                $"Color: {colorAtlas.PageCount} pages · {colorAtlas.AtlasTexture.width}×{colorAtlas.AtlasTexture.height}"));
            section.Add(CreateAtlasDebug("SDF Atlas",
                GlyphAtlas.GetInstance(UniTextRenderMode.SDF), false,
                () => debugSdfSlice, value => debugSdfSlice = value, font.name));
            section.Add(CreateAtlasDebug("MSDF Atlas",
                GlyphAtlas.GetInstance(UniTextRenderMode.MSDF), true,
                () => debugMsdfSlice, value => debugMsdfSlice = value, font.name));
            return section;
        }

        private static VisualElement CreateAtlasDebug(string label, GlyphAtlas atlas,
            bool isMsdf, Func<int> getSlice, Action<int> setSlice, string fontName)
        {
            var root = InspectorVisuals.CreateStack();
            if (atlas == null || atlas.AtlasTexture == null || atlas.PageCount == 0)
            {
                root.Add(new Label($"{label}: empty"));
                return root;
            }
            var texture = atlas.AtlasTexture;
            root.Add(new Label(
                $"{label}: {texture.width}×{texture.height} · {atlas.PageCount} pages · {texture.graphicsFormat}"));
            var slider = new InspectorSliderInt("Page", 0, atlas.PageCount - 1)
            {
                value = Mathf.Clamp(getSlice(), 0, atlas.PageCount - 1),
            };
            slider.RegisterValueChangedCallback(evt => setSlice(evt.newValue));
            root.Add(slider);
            root.Add(new Button(() =>
            {
                var slice = slider.value;
                var mode = isMsdf ? "msdf" : "sdf";
                SaveAtlasSliceAsPng(texture, slice, isMsdf,
                    $"{fontName}_{mode}_page{slice}.png");
            }) { text = "Save Page as PNG" });
            return root;
        }
#endif

        private static void ToggleGlyphOverride(SerializedPropertyBinding binding, int glyphIndex)
        {
            binding.EditSerializedProperties(array =>
            {
                for (var i = 0; i < array.arraySize; i++)
                {
                    if (InspectorHelpers.RequireRelative(
                            array.GetArrayElementAtIndex(i), "glyphIndex").intValue !=
                        glyphIndex) continue;
                    array.DeleteArrayElementAtIndex(i);
                    return;
                }

                var index = array.arraySize;
                array.InsertArrayElementAtIndex(index);
                var element = array.GetArrayElementAtIndex(index);
                InspectorHelpers.RequireRelative(element, "glyphIndex").intValue = glyphIndex;
                InspectorHelpers.RequireRelative(element, "tileSizeOverride").intValue = 0;
                InspectorHelpers.RequireRelative(element, "advanceScale").floatValue = 1f;
                InspectorHelpers.RequireRelative(element, "scale").floatValue = 1f;
            }, "Toggle Glyph Override");
        }

        private static void SetAxisDefault(SerializedPropertyBinding binding, int tag,
            float value, float fontDefault)
        {
            binding.EditSerializedProperties(array =>
            {
                var index = FindAxisDefaultIndex(array, tag);
                if (Mathf.Approximately(value, fontDefault))
                {
                    if (index >= 0) array.DeleteArrayElementAtIndex(index);
                    return;
                }
                if (index < 0)
                {
                    index = array.arraySize;
                    array.InsertArrayElementAtIndex(index);
                    InspectorHelpers.RequireRelative(
                        array.GetArrayElementAtIndex(index), "tag").intValue = tag;
                }
                InspectorHelpers.RequireRelative(
                    array.GetArrayElementAtIndex(index), "value").floatValue = value;
            }, "Change Variable Font Axis");
        }

        private static void RemoveAxisDefault(SerializedPropertyBinding binding, int tag)
        {
            binding.EditSerializedProperties(array =>
            {
                var index = FindAxisDefaultIndex(array, tag);
                if (index >= 0) array.DeleteArrayElementAtIndex(index);
            }, "Reset Variable Font Axis");
        }

        private static int FindAxisDefaultIndex(SerializedProperty array, int tag)
        {
            for (var i = 0; i < array.arraySize; i++)
                if (InspectorHelpers.RequireRelative(
                        array.GetArrayElementAtIndex(i), "tag").intValue == tag)
                    return i;
            return -1;
        }

        private static string AxisTagToString(uint tag)
            => System.Text.Encoding.ASCII.GetString(new[]
            {
                (byte)(tag >> 24), (byte)(tag >> 16), (byte)(tag >> 8), (byte)tag,
            });

    }
}
