using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(DefaultParameterAttribute))]
    internal class DefaultParameterDrawer : LightSidePropertyDrawer<DefaultParameterAttribute>
    {
        private static readonly Dictionary<string, bool> expandedStates = new();

        private static bool GetExpanded(string path) => !expandedStates.TryGetValue(path, out var e) || e;
        private static void SetExpanded(string path, bool value) => expandedStates[path] = value;

        /// <summary>Override lists the user revealed this session (empty string but shown). The reveal toggle lives on the Style's source row (<see cref="StyleDrawer"/>), not here.</summary>
        private static readonly HashSet<string> shownParams = new();

        /// <summary>Whether the override list is explicitly visible or contains a value.</summary>
        internal static bool IsShown(string key, bool hasValue) => hasValue || shownParams.Contains(key);

        internal static void SetShown(string key, bool on)
        {
            if (on) shownParams.Add(key);
            else shownParams.Remove(key);
        }

        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var view = new ToolkitParameterView(context);
            return context.Observe(view, view.Refresh);
        }

        internal static VisualElement CreateToolkitHeaderAction(SerializedPropertyContext context)
        {
            var button = new InspectorIconButton { tooltip = "Default parameter override" };
            button.AddToClassList("lightside-field-action");
            button.AddToClassList("unitext-default-parameters__reveal");
            var shown = false;

            void Refresh()
            {
                var property = context.Binding.FindSerializedProperty();
                var modifier = property == null
                    ? null
                    : ParameterFieldUtility.FindModifierProperty(property);
                var available = modifier != null &&
                                !ParameterFieldUtility.HasDifferentModifierSchemas(modifier) &&
                                HasSchema(modifier.managedReferenceValue?.GetType(), modifier);
                button.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
                if (!available) return;

                shown = IsShown(ParameterFieldUtility.PropertyStateKey(property),
                    HasAnyValue(context.Binding));
                button.SetState(shown, EditorResources.ToggleAccent, "edit");
            }

            button.clicked += () =>
            {
                var property = context.Binding.FindSerializedProperty();
                if (property == null) return;
                var previous = shown;
                var next = !previous;
                SetShown(ParameterFieldUtility.PropertyStateKey(property), next);
                if (!next)
                    context.SetValue(string.Empty, "Clear Default Parameters");
                Refresh();
                using var evt = ChangeEvent<bool>.GetPooled(previous, next);
                evt.target = button;
                button.SendEvent(evt);
            };
            return context.Observe(button, Refresh);
        }

        internal static ToolkitParameterList AddToolkitList(VisualElement root, string titleText,
            ParameterFieldUtility.ParamField[] fields, string segment, BaseModifier modifier,
            string key, Action<Func<string, BaseModifier, string>> changed)
        {
            if (fields == null || fields.Length == 0) return null;
            var list = new ToolkitParameterList(titleText, fields, modifier, key, changed);
            root.Add(list);
            list.Refresh(segment);
            return list;
        }

        internal static VisualElement CreateParameterRow() => InspectorVisuals.CreateParameterRow();

        internal sealed class ToolkitParameterList : VisualElement
        {
            private readonly ParameterFieldUtility.ParamField[] fields;
            private readonly BaseModifier modifier;
            private readonly string key;
            private readonly Action<Func<string, BaseModifier, string>> changed;
            private readonly List<int> visibleRows = new();
            private readonly InspectorListView list;
            private string segment;
            private string[] tokens;
            private bool[] mixedFields;
            private string[] selectedSegments;
            private BaseModifier[] selectedModifiers;
            private ParameterFieldUtility.ParamContext parameterContext;

            internal ToolkitParameterList(string title, ParameterFieldUtility.ParamField[] fields,
                BaseModifier modifier, string key,
                Action<Func<string, BaseModifier, string>> changed)
            {
                this.fields = fields ?? throw new ArgumentNullException(nameof(fields));
                this.modifier = modifier;
                this.key = key ?? throw new ArgumentNullException(nameof(key));
                this.changed = changed ?? throw new ArgumentNullException(nameof(changed));
                UniTextInspectorTheme.Initialize(this);
                list = new InspectorListView(title, CreateParameterRow, BindRow,
                    InspectorVisuals.ClearContent, false, RowIdentity, RefreshRow);
                list.RowMenuOpening += PopulateRowMenu;
                var modifierType = modifier?.GetType();
                if (modifierType != null)
                    list.Header.SetContent(
                        ElementLabelUtility.Colored(title, modifierType),
                        EditorResources.GetTypeIcon(modifierType));
                Add(list);
                list.ExpandedChanged += value => SetExpanded(key, value);
                list.ClearRequested += ClearParameters;
                var add = list.Header.AddButton;
                add.clicked += () =>
                {
                    var items = ParameterFieldUtility.BuildToggleItems(
                        ParameterFieldUtility.RowFilter.OptInSegment, fields, tokens,
                        parameterContext);
                    if (items.Length == 0) return;
                    Selector.ShowMultiple(add.worldBound, items,
                        value => value is int index && visibleRows.Contains(index),
                        value =>
                        {
                            if (value is not int index) return;
                            if (visibleRows.Contains(index))
                                SetField(index, string.Empty);
                            else
                            {
                                SetField(index,
                                    ParameterFieldUtility.ActivationToken(in fields[index]));
                                SetExpanded(key, true);
                            }
                            Refresh(segment, selectedSegments, selectedModifiers);
                        });
                };
            }

            internal void Refresh(string value, IReadOnlyList<string> selectedValues = null,
                IReadOnlyList<BaseModifier> targetModifiers = null)
            {
                segment = value ?? string.Empty;
                var split = ParameterFieldUtility.SplitParameter(segment);
                tokens = new string[Math.Max(fields.Length, split.Length)];
                Array.Copy(split, tokens, split.Length);
                mixedFields = new bool[fields.Length];
                selectedSegments = selectedValues == null
                    ? new[] { segment }
                    : CopyValues(selectedValues);
                selectedModifiers = targetModifiers == null
                    ? new[] { modifier }
                    : CopyModifiers(targetModifiers);
                if (selectedModifiers.Length != selectedSegments.Length)
                    throw new InvalidOperationException(
                        "Selected parameter segments and modifiers must have the same length.");
                parameterContext = new ParameterFieldUtility.ParamContext(modifier);
                visibleRows.Clear();
                for (var i = 0; i < fields.Length; i++)
                {
                    var shown = ParameterFieldUtility.RowShown(
                        ParameterFieldUtility.RowFilter.OptInSegment,
                        fields, tokens, i, context: parameterContext);
                    var first = ParameterFieldUtility.ResolveComparableToolkitValue(
                        in fields[i], TokenAt(tokens, i), parameterContext, i);
                    if (selectedSegments != null)
                    {
                        for (var targetIndex = 0; targetIndex < selectedSegments.Length; targetIndex++)
                        {
                            var targetSplit = ParameterFieldUtility.SplitParameter(
                                selectedSegments[targetIndex] ?? string.Empty);
                            var targetTokens = new string[Math.Max(fields.Length, targetSplit.Length)];
                            Array.Copy(targetSplit, targetTokens, targetSplit.Length);
                            var targetContext = new ParameterFieldUtility.ParamContext(
                                selectedModifiers[targetIndex]);
                            shown |= ParameterFieldUtility.RowShown(
                                ParameterFieldUtility.RowFilter.OptInSegment,
                                fields, targetTokens, i, context: targetContext);
                            var target = ParameterFieldUtility.ResolveComparableToolkitValue(
                                in fields[i], TokenAt(targetTokens, i), targetContext, i);
                            mixedFields[i] |= first.variantIndex != target.variantIndex ||
                                              !string.Equals(first.token, target.token,
                                                  StringComparison.Ordinal);
                        }
                    }
                    if (shown)
                        visibleRows.Add(i);
                }
                list.Rebuild(visibleRows.Count, GetExpanded(key));
            }

            private object RowIdentity(int displayIndex)
                => visibleRows[displayIndex];

            /// <summary>
            /// The row's value joins the shared clipboard as the modifier field's own type, so it
            /// pastes into every field of that type and takes any compatible value in return.
            /// </summary>
            private void PopulateRowMenu(DropdownMenu menu, int displayIndex)
            {
                if (modifier == null || (uint)displayIndex >= (uint)visibleRows.Count) return;
                var fieldIndex = visibleRows[displayIndex];
                var member = ParameterFieldUtility.GetParameterMembers(
                    modifier.GetType())[fieldIndex];
                InspectorContextMenu.AppendCommand(menu, "Copy", InspectorContextMenu.CopyIcon,
                    _ => PropertyClipboard.CopyObject(ParameterFieldUtility.ParseParameterValue(
                        member, in fields[fieldIndex], TokenAt(tokens, fieldIndex),
                        parameterContext, fieldIndex)));
                PropertyClipboardMenu.AppendPaste(menu, "Paste", member.FieldType, value =>
                {
                    SetField(fieldIndex, ParameterFieldUtility.EncodeToken(in fields[fieldIndex],
                        ParameterFieldUtility.FormatToken(member, value)));
                    Refresh(segment, selectedSegments, selectedModifiers);
                });
            }

            private void BindRow(VisualElement row, int displayIndex)
            {
                var fieldIndex = visibleRows[displayIndex];
                var removable = !ParameterFieldUtility.IsRequiredField(in fields[fieldIndex]);
                var field = ParameterFieldUtility.CreateToolkitField(
                    fields[fieldIndex], tokens[fieldIndex], parameterContext, fieldIndex,
                    mixedFields[fieldIndex],
                    (_, next) =>
                    {
                        SetField(fieldIndex, next);
                        if (ParameterFieldUtility.AffectsSiblingVisibility(fields, fieldIndex))
                            Refresh(segment, selectedSegments, selectedModifiers);
                    });
                field.AddToClassList(InspectorVisuals.ParameterFieldClass);
                row.Add(field);
                if (removable)
                    row.Add(InspectorListView.CreateRemoveButton(() =>
                    {
                        SetField(fieldIndex, string.Empty);
                        Refresh(segment, selectedSegments, selectedModifiers);
                    }, $"Remove {fields[fieldIndex].name}"));
            }

            private void RefreshRow(VisualElement row, int displayIndex)
            {
                var fieldIndex = visibleRows[displayIndex];
                ParameterFieldUtility.RefreshToolkitField(row, tokens[fieldIndex], parameterContext,
                    mixedFields[fieldIndex]);
            }

            private void SetField(int fieldIndex, string value) => SetField(fieldIndex,
                ParameterFieldUtility.ResolveToolkitValue(
                    in fields[fieldIndex], value, parameterContext, fieldIndex));

            private void SetField(int fieldIndex,
                ParameterFieldUtility.ToolkitValue value)
            {
                var previousToken = TokenAt(
                    ParameterFieldUtility.SplitParameter(segment), fieldIndex);
                var previous = ParameterFieldUtility.ResolveToolkitValue(
                    in fields[fieldIndex], previousToken, parameterContext, fieldIndex);
                string Transform(string current, BaseModifier currentModifier)
                    => ParameterFieldUtility.MergeParameterSegment(current, fields,
                        fieldIndex, previous, value,
                        new ParameterFieldUtility.ParamContext(currentModifier),
                        parameterContext);
                segment = Transform(segment, modifier);
                for (var i = 0; i < selectedSegments.Length; i++)
                    selectedSegments[i] = Transform(selectedSegments[i], selectedModifiers[i]);
                changed(Transform);
            }

            private void ClearParameters()
            {
                string Transform(string current, BaseModifier currentModifier)
                {
                    current ??= string.Empty;
                    var context = new ParameterFieldUtility.ParamContext(currentModifier);
                    for (var i = 0; i < fields.Length; i++)
                        if (!ParameterFieldUtility.IsRequiredField(in fields[i]))
                            current = ParameterFieldUtility.SetFieldToken(
                                current, fields, i, string.Empty, context);
                    return current;
                }

                segment = Transform(segment, modifier);
                for (var i = 0; i < selectedSegments.Length; i++)
                    selectedSegments[i] = Transform(selectedSegments[i], selectedModifiers[i]);
                changed(Transform);
                Refresh(segment, selectedSegments, selectedModifiers);
            }

            private static string TokenAt(string[] values, int index) =>
                (uint)index < (uint)values.Length ? values[index] ?? string.Empty : string.Empty;

            private static string[] CopyValues(IReadOnlyList<string> values)
            {
                var copy = new string[values.Count];
                for (var i = 0; i < values.Count; i++) copy[i] = values[i];
                return copy;
            }

            private static BaseModifier[] CopyModifiers(IReadOnlyList<BaseModifier> values)
            {
                var copy = new BaseModifier[values.Count];
                for (var i = 0; i < values.Count; i++) copy[i] = values[i];
                return copy;
            }
        }

        private sealed class ToolkitParameterView : VisualElement
        {
            private readonly SerializedPropertyContext context;
            private readonly InspectorStack content;
            private readonly List<ToolkitParameterList> lists = new();
            private readonly List<int> segmentIndices = new();
            private string structureSignature;
            private int segmentCount;
            private TextField rawField;

            public override VisualElement contentContainer => (VisualElement)content ?? this;

            internal ToolkitParameterView(SerializedPropertyContext context)
            {
                this.context = context;
                content = InspectorVisuals.CreateStack();
                hierarchy.Add(content);
                UniTextInspectorTheme.Initialize(this);
                AddToClassList("unitext-default-parameters");
            }

            internal void Refresh()
            {
                var property = context.Binding.FindSerializedProperty();
                var modifierProperty = property == null
                    ? null
                    : ParameterFieldUtility.FindModifierProperty(property);
                var modifierType = modifierProperty?.managedReferenceValue?.GetType();
                var mixedSchema = modifierProperty != null &&
                                  ParameterFieldUtility.HasDifferentModifierSchemas(
                                      modifierProperty);
                var stateKey = property == null
                    ? null
                    : ParameterFieldUtility.PropertyStateKey(property);
                var visible = modifierProperty != null &&
                              (mixedSchema || HasSchema(modifierType, modifierProperty)) &&
                              IsShown(stateKey, HasAnyValue(context.Binding));
                style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible)
                {
                    ResetStructure();
                    return;
                }

                ParameterFieldUtility.UpdateParameterShape(property, modifierProperty);
                if (mixedSchema)
                {
                    if (structureSignature != "mixed") BuildMixedStructure();
                    var value = context.Binding.Value as string ?? string.Empty;
                    if (rawField.value != value) rawField.SetValueWithoutNotify(value);
                    rawField.showMixedValue = context.Binding.HasMultipleValues;
                    InspectorVisuals.RefreshTextFieldPresentation(rawField);
                    return;
                }
                var signature = stateKey + "|" +
                                ParameterFieldUtility.GetModifierSignature(modifierProperty);
                if (signature != structureSignature)
                    BuildStructure(modifierProperty, modifierType, stateKey, signature);
                RefreshLists(modifierProperty);
            }

            private void BuildMixedStructure()
            {
                ResetStructure();
                structureSignature = "mixed";
                Add(new HelpBox(
                    "Selected modifiers use different parameter schemas. Raw values remain editable without interpreting them through the wrong modifier type.",
                    HelpBoxMessageType.Info));
                rawField = new TextField(context.Label);
                rawField.RegisterValueChangedCallback(evt =>
                    context.SetValue(evt.newValue, "Change Default Parameters"));
                Add(rawField);
            }

            private void BuildStructure(SerializedProperty modifierProperty, Type modifierType,
                string stateKey, string signature)
            {
                InspectorVisuals.ClearContent(this);
                lists.Clear();
                segmentIndices.Clear();
                structureSignature = signature;
                if (modifierProperty.managedReferenceValue is CompositeModifier composite)
                {
                    var entries = ParameterFieldUtility.GetCompositeEntries(modifierProperty);
                    if (entries == null) return;
                    var compositeModifierPath = modifierProperty.propertyPath;
                    segmentCount = entries.Length;
                    var children = modifierProperty.FindPropertyRelative("modifiers.items");
                    for (var i = 0; i < entries.Length; i++)
                    {
                        var index = i;
                        var modifier = index < composite.Modifiers.Count
                            ? composite.Modifiers[index]
                            : null;
                        var list = AddToolkitList(this, entries[index].displayName,
                            entries[index].fields, string.Empty, modifier,
                            ChildStateKey(stateKey, children, index), transform =>
                            {
                                context.Binding.TransformValue((target, value) =>
                                {
                                    var current = ParameterFieldUtility.SplitSegments(
                                        value as string ?? string.Empty, entries.Length);
                                    current[index] = transform(current[index],
                                        ResolveModifier(target, compositeModifierPath, index));
                                    return ParameterFieldUtility.JoinSegments(current);
                                }, "Change Default Parameters");
                            });
                        if (list != null)
                        {
                            lists.Add(list);
                            segmentIndices.Add(index);
                        }
                    }
                    return;
                }

                segmentCount = 1;
                var fields = ParameterFieldUtility.GetFields(modifierType);
                var modifierPath = modifierProperty.propertyPath;
                var title = $"Default Parameters · {ElementLabelUtility.DisplayName(modifierProperty.managedReferenceValue)}";
                var single = AddToolkitList(this, title, fields, string.Empty,
                    modifierProperty.managedReferenceValue as BaseModifier, stateKey,
                    transform => context.Binding.TransformValue((target, value) =>
                            transform(value as string ?? string.Empty,
                                ResolveModifier(target, modifierPath)),
                        "Change Default Parameters"));
                if (single != null)
                {
                    lists.Add(single);
                    segmentIndices.Add(-1);
                }
            }

            private void RefreshLists(SerializedProperty modifierProperty)
            {
                if (modifierProperty.managedReferenceValue is CompositeModifier)
                {
                    var segments = ParameterFieldUtility.SplitSegments(
                        context.Binding.Value as string ?? string.Empty, segmentCount);
                    for (var i = 0; i < lists.Count; i++)
                    {
                        var segmentIndex = segmentIndices[i];
                        lists[i].Refresh(segments[segmentIndex],
                            SelectedSegments(context.Binding, segmentCount, segmentIndex),
                            SelectedModifiers(context.Binding, modifierProperty.propertyPath,
                                segmentIndex));
                    }
                    return;
                }

                if (lists.Count == 1)
                    lists[0].Refresh(context.Binding.Value as string ?? string.Empty,
                        SelectedSegments(context.Binding, 1, -1),
                        SelectedModifiers(context.Binding, modifierProperty.propertyPath, -1));
            }

            private void ResetStructure()
            {
                if (structureSignature == null && content.childCount == 0) return;
                InspectorVisuals.ClearContent(this);
                lists.Clear();
                segmentIndices.Clear();
                segmentCount = 0;
                structureSignature = null;
                rawField = null;
            }
        }

        internal static string[] SelectedSegments(SerializedPropertyBinding binding,
            int segmentCount, int segmentIndex)
        {
            var targets = binding.SerializedObject.targetObjects;
            var values = new string[targets.Length];
            for (var i = 0; i < targets.Length; i++)
            {
                var value = binding.GetValue(targets[i]) as string ?? string.Empty;
                values[i] = segmentIndex < 0
                    ? value
                    : ParameterFieldUtility.SplitSegments(value, segmentCount)[segmentIndex];
            }
            return values;
        }

        internal static BaseModifier[] SelectedModifiers(SerializedPropertyBinding binding,
            string modifierPath, int childIndex)
        {
            var targets = binding.SerializedObject.targetObjects;
            var values = new BaseModifier[targets.Length];
            for (var i = 0; i < targets.Length; i++)
                values[i] = ResolveModifier(targets[i], modifierPath, childIndex);
            return values;
        }

        internal static BaseModifier ResolveModifier(UnityEngine.Object target,
            string modifierPath, int childIndex = -1)
        {
            var modifier = SerializedPropertyBinding.ResolveInstance(target, modifierPath)
                           as BaseModifier ?? throw new InvalidOperationException(
                               $"Serialized path '{modifierPath}' does not contain a modifier on '{target.name}'.");
            if (childIndex < 0) return modifier;
            return ((CompositeModifier)modifier).Modifiers[childIndex] ??
                   throw new InvalidOperationException(
                       $"Composite modifier child {childIndex} is missing on '{target.name}'.");
        }

        private static bool HasAnyValue(SerializedPropertyBinding binding)
        {
            var targets = binding.SerializedObject.targetObjects;
            for (var i = 0; i < targets.Length; i++)
                if (!string.IsNullOrEmpty(binding.GetValue(targets[i]) as string)) return true;
            return false;
        }

        internal static string ChildStateKey(string parentKey,
            SerializedProperty children, int index)
        {
            if (children == null || (uint)index >= (uint)children.arraySize)
                return parentKey + ":empty:" + index;
            var child = children.GetArrayElementAtIndex(index);
            return child.managedReferenceValue == null
                ? parentKey + ":empty:" + index
                : parentKey + ":" + child.managedReferenceId;
        }

        /// <summary>Whether the paired modifier exposes a parameter schema to override.</summary>
        internal static bool HasSchema(Type modType, SerializedProperty modifierProp)
        {
            if (modType == null) return false;
            if (modType == typeof(CompositeModifier))
            {
                var entries = ParameterFieldUtility.GetCompositeEntries(modifierProp);
                if (entries == null) return false;
                ParameterFieldUtility.CountCompositeStats(entries, out _, out var total);
                return total > 0;
            }
            return ParameterFieldUtility.GetFields(modType).Length > 0;
        }

    }
}
