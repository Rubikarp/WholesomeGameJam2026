using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal abstract partial class UniTextBaseEditor
    {
        private const string AlignedFieldClass = InspectorVisuals.FieldAxisClass;

        private static Color alignAccent => EditorResources.ToggleAccent;
        private static readonly string[] alignIconNames =
        {
            "left-align", "h-center-align", "right-align", "top-align",
            "middle-align", "bottom-align", "justify-align",
        };
        private static readonly HorizontalAlignment[] hAlignDirectional =
        {
            HorizontalAlignment.Start,
            HorizontalAlignment.Center,
            HorizontalAlignment.End,
            HorizontalAlignment.Justify,
        };
        private static readonly HorizontalAlignment[] hAlignPhysical =
        {
            HorizontalAlignment.Left,
            HorizontalAlignment.Center,
            HorizontalAlignment.Right,
            HorizontalAlignment.Justify,
        };
        private readonly HorizontalAlignment[] hAlignValues = (HorizontalAlignment[])hAlignDirectional.Clone();
        private static readonly VerticalAlignment[] vAlignValues =
        {
            VerticalAlignment.Top,
            VerticalAlignment.Middle,
            VerticalAlignment.Bottom,
        };
        private static readonly int[] hAlignIcons = { 0, 1, 2, 6 };
        private static readonly int[] vAlignIcons = { 3, 4, 5 };
        private static readonly string[] hAlignDirectionalTooltips =
            { "Start (right for RTL paragraphs)", "Center", "End (left for RTL paragraphs)", "Justify" };
        private static readonly string[] hAlignPhysicalTooltips = { "Left", "Center", "Right", "Justify" };
        private readonly string[] hAlignTooltips = (string[])hAlignDirectionalTooltips.Clone();
        private static readonly string[] vAlignTooltips = { "Top", "Middle", "Bottom" };

        private readonly List<(InspectorPillButton button, StylePresetEntry preset)> presetButtons = new();
        private TextField sourceTextField;
        private TextField renderedPreviewField;
        private TextField cleanPreviewField;
        private Label renderedPreviewLabel;
        private VisualElement renderedPreviewBlock;
        private VisualElement cleanPreviewBlock;
        private IVisualElementScheduledItem previewRefresh;
        private InspectorPillButton sceneEditButton;
        private InspectorPillButton visibilityButton;
        private InspectorSelectorButton viewButton;
        private InspectorRailSection styleSection;
        private InspectorPillButton globalToggle;
        private InspectorPillButton autoAlignToggle;

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            uniText = (UniTextBase)target;

            var root = UniTextInspectorTheme.CreateRoot();

            root.Add(CreateTextSection());
            root.Add(CreateFontSection());
            root.Add(CreateLayoutSection());
            root.Add(CreateStyleSection());
            AddVariantSections(root);

            var other = CreateToolkitSection("Other");
            AddOtherFields(other);
            root.Add(other);

            AddDebugSections(root);
            return root;
        }

        protected virtual void AddVariantSections(VisualElement root) { }

        protected virtual void AddOtherFields(VisualElement container) { }

        protected virtual void AddDebugSections(VisualElement root) { }

        private VisualElement CreateTextSection()
        {
            var section = CreateToolkitSection("Text", textProp);
            var toolbar = InspectorVisuals.CreateRow();

            var expand = CreateStaticToggle(
                "Expand",
                () => textAreaExpand,
                value =>
                {
                    textAreaExpand = value;
                    RefreshTextFieldStyle();
                });
            expand.AddToClassList("unitext-toolbar__expand");
            toolbar.Add(expand);

            viewButton = new InspectorSelectorButton();
            viewButton.AddToClassList("unitext-view-selector");
            viewButton.clicked += () => ShowViewMenu(viewButton);
            toolbar.Add(viewButton);
            RefreshViewButton();

            var textSize = new InspectorFillSlider(8, 24, textAreaFontSize,
                size => $"Size {size}");
            textSize.AddToClassList("unitext-text-size");
            textSize.RegisterValueChangedCallback(evt =>
            {
                textAreaFontSize = evt.newValue;
                EditorPrefs.SetInt(TextAreaFontSizePreference, textAreaFontSize);
                RefreshTextFieldStyle();
            });
            toolbar.Add(textSize);

            visibilityButton = new InspectorPillButton(ToggleVisibility)
            {
                tooltip = "Show or hide the text without a rebuild (IsVisible).",
            };
            visibilityButton.AddToClassList("unitext-toolbar-icon");
            toolbar.Add(visibilityButton);
            visibilityButton.schedule.Execute(RefreshVisibility).Every(250);
            RefreshVisibility();

            sceneEditButton = new InspectorPillButton(ToggleSceneEditing)
            {
                tooltip = "Toggle inline text editing in the Scene View.",
            };
            sceneEditButton.AddToClassList("unitext-toolbar-icon");
            sceneEditButton.SetEnabled(targets.Length == 1);
            toolbar.Add(sceneEditButton);
            section.Add(toolbar);

            sourceTextField = SerializedPropertyField.Create<TextField>(
                textProp, textProp.displayName);
            section.Add(sourceTextField);

            renderedPreviewBlock = CreatePreviewBlock(out renderedPreviewLabel, out renderedPreviewField);
            cleanPreviewBlock = CreatePreviewBlock(out var cleanPreviewLabel, out cleanPreviewField);
            cleanPreviewLabel.text = RichDot(dotColorNone) + " Clean Text (markup stripped)";
            section.Add(renderedPreviewBlock);
            section.Add(cleanPreviewBlock);

            RefreshTextFieldStyle();
            SerializedPropertyField.Observe(
                section, RefreshTextPreviews, textProp);

            previewRefresh = section.schedule.Execute(RefreshTextPreviews).Every(250);
            if ((textViewMode & (TextViewMode.Rendered | TextViewMode.Clean)) == 0)
                previewRefresh.Pause();

            Action refreshSceneEditing = RefreshSceneEditing;
            section.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                SceneEditing.SceneTextEditSession.Changed += refreshSceneEditing;
                RefreshSceneEditing();
                RefreshTextPreviews();
            });
            section.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                SceneEditing.SceneTextEditSession.Changed -= refreshSceneEditing;
                previewRefresh?.Pause();
            });

            return section;
        }

        private static VisualElement CreatePreviewBlock(out Label label, out TextField field)
        {
            var block = InspectorVisuals.CreateStack();
            block.AddToClassList("unitext-preview");

            label = new Label { enableRichText = true };
            label.AddToClassList("unitext-preview__label");
            block.Add(label);

            field = new InspectorTextArea { isReadOnly = true };
            block.Add(field);
            return block;
        }

        private void RefreshTextFieldStyle()
        {
            if (sourceTextField == null) return;

            ApplyTextFieldStyle(sourceTextField);
            ApplyTextFieldStyle(renderedPreviewField);
            ApplyTextFieldStyle(cleanPreviewField);
        }

        private void ApplyTextFieldStyle(TextField field)
        {
            if (field == null) return;
            InspectorVisuals.RefreshTextFieldPresentation(field);
            field.style.fontSize = textAreaFontSize;
            field.style.height = new StyleLength(StyleKeyword.Auto);
            field.style.minHeight = InspectorVisuals.ControlHeight;
            field.style.maxHeight = textAreaExpand
                ? new StyleLength(StyleKeyword.None)
                : new StyleLength(InspectorVisuals.ControlHeight * 4f);
        }

        private void RefreshTextPreviews()
        {
            if (renderedPreviewBlock == null) return;

            RefreshViewButton();
            ApplyTextFieldStyle(sourceTextField);
            sourceTextField.style.display = (textViewMode & TextViewMode.Serialized) != 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            var singleTarget = targets.Length == 1;
            var showRendered = singleTarget && (textViewMode & TextViewMode.Rendered) != 0;
            var showClean = singleTarget && (textViewMode & TextViewMode.Clean) != 0;

            renderedPreviewBlock.style.display = showRendered ? DisplayStyle.Flex : DisplayStyle.None;
            cleanPreviewBlock.style.display = showClean ? DisplayStyle.Flex : DisplayStyle.None;

            if (!showRendered && !showClean)
            {
                previewRefresh?.Pause();
                return;
            }

            previewRefresh?.Resume();
            uniText = target as UniTextBase;
            if (uniText == null) return;

            if (showRendered)
            {
                renderedPreviewLabel.text = LabelForOverride(uniText.TextOverride);
                var rendered = uniText.RenderedText;
                renderedPreviewField.SetValueWithoutNotify(
                    rendered.IsEmpty ? string.Empty : rendered.ToString());
            }

            if (showClean)
            {
                var clean = uniText.CleanText;
                cleanPreviewField.SetValueWithoutNotify(
                    clean.IsEmpty ? string.Empty : clean.ToString());
            }
            ApplyTextFieldStyle(renderedPreviewField);
            ApplyTextFieldStyle(cleanPreviewField);
        }

        private void RefreshViewButton()
        {
            if (viewButton == null) return;
            var label = ViewModeLabel(textViewMode);
            viewButton.text = label;
            viewButton.SetValueAccent(textViewMode, label);
        }

        private void ShowViewMenu(VisualElement anchor)
        {
            var items = new[]
            {
                ViewModeItem("Serialized Text", TextViewMode.Serialized),
                ViewModeItem("Rendered Text", TextViewMode.Rendered),
                ViewModeItem("Clean Text", TextViewMode.Clean),
            };
            Selector.ShowMultiple(anchor.worldBound, items,
                value => (textViewMode & (TextViewMode)value) != 0,
                value => SetViewMode(textViewMode ^ (TextViewMode)value));
        }

        private static Selector.SelectorItem ViewModeItem(
            string label, TextViewMode value) => new()
        {
            displayName = label,
            value = value,
        };

        private void SetViewMode(TextViewMode mode)
        {
            textViewMode = mode;
            EditorPrefs.SetInt(TextViewsPreference, (int)mode);
            RefreshViewButton();
            RefreshTextPreviews();
        }

        private void ToggleVisibility()
        {
            var allVisible = true;
            for (var i = 0; i < targets.Length; i++)
                if (targets[i] is UniTextBase { IsVisible: false })
                {
                    allVisible = false;
                    break;
                }

            for (var i = 0; i < targets.Length; i++)
                if (targets[i] is UniTextBase text)
                    text.IsVisible = !allVisible;

            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            if (visibilityButton == null) return;
            var visible = 0;
            for (var i = 0; i < targets.Length; i++)
                if (targets[i] is UniTextBase { IsVisible: true })
                    visible++;
            var all = visible == targets.Length;
            visibilityButton.SetState(
                all, visible > 0 && !all, alignAccent, all ? "eye" : "eye-off");
        }

        private void ToggleSceneEditing()
        {
            if (IsSceneEditingThisTarget())
                SceneEditing.SceneTextEditSession.End();
            else
                SceneEditing.SceneTextEditSession.BeginNoClick((UniTextBase)target);
            RefreshSceneEditing();
        }

        private bool IsSceneEditingThisTarget() =>
            targets.Length == 1
            && SceneEditing.SceneTextEditSession.Active
            && ReferenceEquals(SceneEditing.SceneTextEditSession.Target, target);

        private void RefreshSceneEditing()
        {
            if (sceneEditButton == null) return;
            sceneEditButton.SetState(IsSceneEditingThisTarget(), false, alignAccent, "edit");
        }

        private VisualElement CreateFontSection()
        {
            var section = CreateToolkitSection("Font");
            section.Add(SerializedPropertyField.Create(fontProp, "Font"));
            section.Add(SerializedPropertyField.Create(fontStackProp, "Font Stack"));
            section.Add(SerializedPropertyField.Create(renderModeProp, "Render Mode"));

            var sizeRow = InspectorVisuals.CreateRow();
            sizeRow.AddToClassList("unitext-font-size-row");
            var sizeFields = InspectorVisuals.CreateStack();
            sizeFields.AddToClassList("unitext-font-size-row__fields");

            var fontSize = SerializedPropertyField.Create<FloatField>(
                fontSizeProp, "Font Size");
            sizeFields.Add(fontSize);

            var autoFields = InspectorVisuals.CreateRow();
            var minSize = SerializedPropertyField.Create<FloatField>(minFontSizeProp, "Min");
            var maxSize = SerializedPropertyField.Create<FloatField>(maxFontSizeProp, "Max");
            minSize.RemoveFromClassList(AlignedFieldClass);
            maxSize.RemoveFromClassList(AlignedFieldClass);
            var currentSize = new FloatField("Current")
            {
                tooltip = "Final font size chosen by Auto Size for the current frame.",
                isReadOnly = true,
            };
            minSize.AddToClassList("unitext-auto-size-fields__field");
            maxSize.AddToClassList("unitext-auto-size-fields__field");
            currentSize.AddToClassList("unitext-auto-size-fields__field");
            currentSize.AddToClassList("unitext-auto-size-fields__field--readonly");
            autoFields.Add(minSize);
            autoFields.Add(maxSize);
            autoFields.Add(currentSize);
            sizeFields.Add(autoFields);
            sizeRow.Add(sizeFields);

            IVisualElementScheduledItem currentSizeRefresh = null;
            void RefreshCurrentSize()
            {
                var first = ((UniTextBase)targets[0]).CurrentFontSize;
                var mixed = false;
                for (var i = 1; i < targets.Length; i++)
                    if (!Mathf.Approximately(
                            ((UniTextBase)targets[i]).CurrentFontSize, first))
                    {
                        mixed = true;
                        break;
                    }
                currentSize.SetValueWithoutNotify(first);
                currentSize.showMixedValue = mixed;
            }

            void RefreshAutoSize()
            {
                var mixed = autoSizeProp.hasMultipleDifferentValues;
                var enabled = !mixed && ((UniTextBase)target).AutoSize;
                fontSize.style.display = enabled ? DisplayStyle.None : DisplayStyle.Flex;
                autoFields.style.display = enabled || mixed
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                if (enabled || mixed)
                {
                    RefreshCurrentSize();
                    currentSizeRefresh?.Resume();
                }
                else
                {
                    currentSizeRefresh?.Pause();
                }
            }

            var autoSize = CreateToggle("Auto Size", autoSizeProp, RefreshAutoSize);
            sizeRow.Add(autoSize);
            section.Add(sizeRow);

            var fitStepsField = SerializedPropertyField.Create(fitStepsProp, "Fit Steps");
            section.Add(fitStepsField);

            var warning = new HelpBox(
                "Font Size below 1 may render unstably. To make text smaller, lower the GameObject's Scale instead.",
                HelpBoxMessageType.Warning);
            section.Add(warning);

            void RefreshWarning()
            {
                var visible = false;
                for (var i = 0; i < targets.Length; i++)
                {
                    if (targets[i] is not UniTextBase value) continue;
                    if (value.FontSize < 1f
                        || value.AutoSize && (value.MinFontSize < 1f || value.MaxFontSize < 1f))
                    {
                        visible = true;
                        break;
                    }
                }
                warning.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            void RefreshFitSteps()
            {
                var enabled = !autoSizeProp.hasMultipleDifferentValues && ((UniTextBase)target).AutoSize;
                fitStepsField.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
            }

            currentSizeRefresh = currentSize.schedule.Execute(() =>
            {
                if (targets.Length > 0) RefreshCurrentSize();
            }).Every(250);
            SerializedPropertyField.Observe(section, () =>
                {
                    RefreshAutoSize();
                    RefreshWarning();
                    RefreshFitSteps();
                },
                autoSizeProp, fontSizeProp, minFontSizeProp, maxFontSizeProp, fitStepsProp);

            GraphicInspectorFields.AddAppearance(section, serializedObject, false);
            return section;
        }

        private VisualElement CreateLayoutSection()
        {
            var section = CreateToolkitSection("Layout");
            var row = InspectorVisuals.CreateWrapRow();

            row.Add(CreateToggle("Word Wrap", wordWrapProp));

            var basisBinding = new SerializedPropertyBinding(horizontalAlignmentProp);
            autoAlignToggle = new InspectorPillButton(() => FlipAlignmentBasis(basisBinding))
            {
                text = "Auto Align",
                tooltip = "On, the edge is picked per paragraph from its own writing direction, "
                          + "so an RTL paragraph aligns to the opposite side. Off, the edge is physical.",
            };
            autoAlignToggle.AddToClassList("unitext-auto-align");

            var alignment = InspectorVisuals.CreateRow();
            alignment.AddToClassList("unitext-alignment");
            alignment.Add(CreateAlignmentGroup(
                horizontalAlignmentProp, hAlignValues, hAlignIcons, hAlignTooltips,
                "Change Horizontal Alignment", RefreshAlignmentBasis));
            var vertical = CreateAlignmentGroup(
                verticalAlignmentProp, vAlignValues, vAlignIcons, vAlignTooltips,
                "Change Vertical Alignment");
            alignment.Add(vertical);
            row.Add(alignment);

            row.Add(CreatePaddingField("Padding", paddingProp, "Change Padding"));
            row.Add(autoAlignToggle);

            section.Add(row);
            return section;
        }

        private void RefreshAlignmentBasis()
        {
            if (autoAlignToggle == null) return;

            var physical = (HorizontalAlignment)horizontalAlignmentProp.intValue
                is HorizontalAlignment.Left or HorizontalAlignment.Right;
            var directional = (HorizontalAlignment)horizontalAlignmentProp.intValue
                is HorizontalAlignment.Start or HorizontalAlignment.End;

            Array.Copy(physical ? hAlignPhysical : hAlignDirectional, hAlignValues, hAlignValues.Length);
            Array.Copy(physical ? hAlignPhysicalTooltips : hAlignDirectionalTooltips,
                hAlignTooltips, hAlignTooltips.Length);

            autoAlignToggle.style.display = physical || directional
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            autoAlignToggle.SetState(!physical, false, alignAccent);
        }

        private void FlipAlignmentBasis(SerializedPropertyBinding binding)
        {
            var flipped = (HorizontalAlignment)horizontalAlignmentProp.intValue switch
            {
                HorizontalAlignment.Start => HorizontalAlignment.Left,
                HorizontalAlignment.End => HorizontalAlignment.Right,
                HorizontalAlignment.Left => HorizontalAlignment.Start,
                HorizontalAlignment.Right => HorizontalAlignment.End,
                _ => (HorizontalAlignment)horizontalAlignmentProp.intValue
            };
            binding.SetValue(flipped, "Change Horizontal Alignment");
        }

        private VisualElement CreateStyleSection()
        {
            ComputeStyleCountRange(out _, out _, out var globalCount);
            styleSection = CreateToolkitSection("Style", null, "Style");
            presetButtons.Clear();

            var presetRow = InspectorVisuals.CreateRow();
            presetRow.AddToClassList("unitext-preset-row");
            var presets = InspectorVisuals.CreateCompactWrapRow();
            presets.AddToClassList("unitext-presets");
            var lastVisualGroup = -1;
            foreach (var item in GetToggleRowItems())
            {
                var preset = (StylePresetEntry)item.value;
                var captured = preset;
                var button = new InspectorPillButton(() =>
                {
                    if (GetToggleState(captured) == ToggleState.All)
                        RemovePreset(captured);
                    else
                        AddPreset(captured);
                    RefreshStyleState();
                })
                {
                    tooltip = item.displayName,
                };
                button.AddToClassList("unitext-preset");
                if (lastVisualGroup >= 0 && preset.visualGroup != lastVisualGroup)
                    button.AddToClassList("unitext-preset--new-group");
                lastVisualGroup = preset.visualGroup;
                presets.Add(button);
                presetButtons.Add((button, preset));
            }
            presetRow.Add(presets);

            globalToggle = CreateToggle(
                $"Use Global Style Preset ({globalCount})",
                useGlobalStylePresetProp,
                RefreshStyleState);
            globalToggle.AddToClassList("unitext-global-preset");
            presetRow.Add(globalToggle);
            styleSection.Add(presetRow);

            var styleItems = InspectorHelpers.RequireRelative(stylesProp, "items");
            var presetItems = InspectorHelpers.RequireRelative(stylePresetsProp, "items");
            styleSection.Add(SerializedPropertyField.Create(stylesProp, "Styles"));
            styleSection.Add(SerializedPropertyField.Create(stylePresetsProp, "Style Presets"));

            SerializedPropertyField.Observe(styleSection, RefreshStyleState,
                styleItems, presetItems, useGlobalStylePresetProp);
            Action refreshStyleState = RefreshStyleState;
            styleSection.RegisterCallback<AttachToPanelEvent>(_ =>
                EditorApplication.projectChanged += refreshStyleState);
            styleSection.RegisterCallback<DetachFromPanelEvent>(_ =>
                EditorApplication.projectChanged -= refreshStyleState);
            return styleSection;
        }

        private void RefreshStyleState()
        {
            if (styleSection == null) return;

            ComputeStyleCountRange(out var minimum, out var maximum,
                out var globalCount);
            styleSection.SetTitle(minimum == maximum
                ? $"Style ({minimum})"
                : $"Style ({minimum}\u2013{maximum})");

            if (globalToggle != null)
                globalToggle.text = $"Use Global Style Preset ({globalCount})";
            globalToggle?.EnableInClassList(
                "unitext-global-preset--unavailable", globalCount == 0);

            foreach (var (button, preset) in presetButtons)
            {
                var state = GetToggleState(preset);
                button.SetState(
                    state == ToggleState.All,
                    state == ToggleState.Mixed,
                    EditorResources.GetAccentColor(preset),
                    preset.iconName);
            }
        }

        protected InspectorRailSection CreateToolkitSection(
            string title,
            SerializedProperty property = null,
            string prefsKey = null)
        {
            var section = InspectorVisuals.CreateRailSection(title, prefsKey);
            if (property != null)
            {
                section.tooltip = property.tooltip;
                var binding = new SerializedPropertyBinding(property);
                new SerializedPropertyContext(binding, property, property.displayName)
                    .Bind(section.Header);
            }
            return section;
        }

        protected InspectorPillButton CreateToggle(
            string label,
            SerializedProperty property,
            Action changed = null)
            => SerializedPropertyField.CreateToggle(property, label, changed);

        private static InspectorPillButton CreateStaticToggle(
            string label,
            Func<bool> getter,
            Action<bool> setter)
        {
            InspectorPillButton toggle = null;
            void Refresh() => toggle.SetState(getter(), false, alignAccent);
            toggle = new InspectorPillButton(() =>
            {
                setter(!getter());
                Refresh();
            }) { text = label };
            Refresh();
            return toggle;
        }

        protected VisualElement CreateToggleRow(params VisualElement[] toggles)
        {
            var row = InspectorVisuals.CreateCompactWrapRow();
            for (var i = 0; i < toggles.Length; i++)
                row.Add(toggles[i]);
            return row;
        }

        private VisualElement CreateAlignmentGroup<T>(
            SerializedProperty property,
            T[] values,
            int[] iconIndices,
            string[] tooltips,
            string undoLabel,
            Action beforeRefresh = null)
        {
            var binding = new SerializedPropertyBinding(property);
            var group = InspectorVisuals.CreateCompactRow();
            group.AddToClassList("unitext-alignment-group");
            var controls = new List<InspectorPillButton>();
            var matchCounts = new int[values.Length];

            for (var i = 0; i < values.Length; i++)
            {
                var slot = i;
                var button = new InspectorPillButton(() =>
                {
                    binding.SetValue(values[slot], undoLabel);
                    Refresh();
                });
                button.AddToClassList("unitext-alignment-choice");
                group.Add(button);
                controls.Add(button);
            }

            void Refresh()
            {
                beforeRefresh?.Invoke();
                var selectedTargets = binding.SerializedObject.targetObjects;
                Array.Clear(matchCounts, 0, matchCounts.Length);
                for (var i = 0; i < selectedTargets.Length; i++)
                {
                    var current = (T)binding.GetValue(selectedTargets[i]);
                    for (var j = 0; j < values.Length; j++)
                        if (EqualityComparer<T>.Default.Equals(current, values[j]))
                            matchCounts[j]++;
                }

                for (var i = 0; i < controls.Count; i++)
                {
                    var button = controls[i];
                    var matched = matchCounts[i];
                    var active = matched == selectedTargets.Length;
                    var mixed = matched > 0 && !active;
                    var iconName = alignIconNames[iconIndices[i]];
                    button.tooltip = tooltips[i];
                    button.SetState(active, mixed, alignAccent, iconName);
                }
            }

            return new SerializedPropertyContext(binding, property, property.displayName)
                .Bind(group, Refresh);
        }

        protected VisualElement CreatePaddingField(
            string label,
            SerializedProperty property,
            string undoLabel)
        {
            return PaddingBoxButton.Create(
                property, label, undoLabel, SceneView.RepaintAll);
        }
    }
}
