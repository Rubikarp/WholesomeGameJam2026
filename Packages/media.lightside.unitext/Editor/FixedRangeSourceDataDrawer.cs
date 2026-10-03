using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(FixedRangeSource.Entry))]
    internal sealed class FixedRangeSourceDataDrawer : LightSidePropertyDrawer<FixedRangeSource.Entry>
    {
        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var view = new ToolkitView(context);
            return context.Observe(view, view.Refresh);
        }

        private sealed class ToolkitView : VisualElement
        {
            private readonly SerializedPropertyContext context;
            private readonly InspectorFoldoutHeader header = new();
            private readonly VisualElement body;
            private readonly List<DefaultParameterDrawer.ToolkitParameterList> lists = new();
            private readonly List<int> segmentIndices = new();
            private string structureSignature;
            private int segmentCount;

            internal ToolkitView(SerializedPropertyContext context)
            {
                this.context = context;
                UniTextInspectorTheme.Initialize(this);
                body = InspectorVisuals.CreateHierarchyBody(header);
                Add(header);
                Add(body);
                header.Changed += value =>
                {
                    RequireProperty().isExpanded = value;
                    Refresh();
                };
                RegisterCallback<ChangeEvent<Type>>(_ => Refresh());
            }

            internal void Refresh()
            {
                var property = RequireProperty();
                var range = InspectorHelpers.RequireRelative(property, "range");
                var parameter = InspectorHelpers.RequireRelative(property, "parameter");
                var modifier = ParameterFieldUtility.FindModifierProperty(property);
                if (modifier != null)
                    ParameterFieldUtility.UpdateParameterShape(parameter, modifier);
                var value = parameter.stringValue;
                header.SetContent(range.hasMultipleDifferentValues ||
                                  parameter.hasMultipleDifferentValues
                    ? "—"
                    : string.IsNullOrEmpty(value)
                        ? $"Range: {range.stringValue}"
                        : $"Range: {range.stringValue} · {value}");
                header.SetExpandedWithoutNotify(property.isExpanded);
                if (property.isExpanded) RefreshBody(range, parameter, modifier);
                InspectorMotion.SetExpanded(body, property.isExpanded);
            }

            private void RefreshBody(SerializedProperty range, SerializedProperty parameter,
                SerializedProperty modifier)
            {
                if (modifier != null &&
                    ParameterFieldUtility.HasDifferentModifierSchemas(modifier))
                {
                    if (structureSignature != "mixed")
                    {
                        InspectorVisuals.ClearContent(body);
                        lists.Clear();
                        segmentIndices.Clear();
                        structureSignature = "mixed";
                        body.Add(SerializedPropertyField.Create(range, "Range"));
                        body.Add(new HelpBox(
                            "Selected modifiers use different parameter schemas. Range parameters are shown as raw values.",
                            HelpBoxMessageType.Info));
                        body.Add(SerializedPropertyField.Create(parameter));
                    }
                    return;
                }

                var signature = modifier == null
                    ? "raw"
                    : ParameterFieldUtility.GetModifierSignature(modifier);
                if (signature != structureSignature)
                    BuildStructure(range, parameter, modifier, signature);
                RefreshLists(parameter, modifier);
            }

            private void BuildStructure(SerializedProperty range,
                SerializedProperty parameter, SerializedProperty modifier, string signature)
            {
                InspectorVisuals.ClearContent(body);
                lists.Clear();
                segmentIndices.Clear();
                segmentCount = 0;
                structureSignature = signature;
                body.Add(SerializedPropertyField.Create(range, "Range"));
                var modifierType = modifier?.managedReferenceValue?.GetType();
                if (modifier?.managedReferenceValue is CompositeModifier composite)
                {
                    var entries = ParameterFieldUtility.GetCompositeEntries(modifier);
                    if (entries == null) return;
                    var compositeModifierPath = modifier.propertyPath;
                    segmentCount = entries.Length;
                    var children = modifier.FindPropertyRelative("modifiers.items");
                    var stateKey = ParameterFieldUtility.PropertyStateKey(parameter);
                    var parameterBinding = new SerializedPropertyBinding(parameter);
                    for (var i = 0; i < entries.Length; i++)
                    {
                        var index = i;
                        var child = index < composite.Modifiers.Count
                            ? composite.Modifiers[index]
                            : null;
                        var list = DefaultParameterDrawer.AddToolkitList(body,
                            entries[index].displayName, entries[index].fields,
                            string.Empty, child,
                            DefaultParameterDrawer.ChildStateKey(stateKey, children, index),
                            transform =>
                            {
                                parameterBinding.TransformValue((target, value) =>
                                {
                                    var current = ParameterFieldUtility.SplitSegments(
                                        value as string ?? string.Empty, entries.Length);
                                    current[index] = transform(current[index],
                                        DefaultParameterDrawer.ResolveModifier(
                                            target, compositeModifierPath, index));
                                    return ParameterFieldUtility.JoinSegments(current);
                                }, "Change Range Parameters");
                            });
                        if (list == null) continue;
                        lists.Add(list);
                        segmentIndices.Add(index);
                    }
                    return;
                }

                if (modifierType == null)
                {
                    body.Add(SerializedPropertyField.Create(parameter));
                    return;
                }

                segmentCount = 1;
                var fields = ParameterFieldUtility.GetFields(modifierType);
                var binding = new SerializedPropertyBinding(parameter);
                var modifierPath = modifier.propertyPath;
                var single = DefaultParameterDrawer.AddToolkitList(body, "Parameters", fields,
                    string.Empty, modifier.managedReferenceValue as BaseModifier,
                    ParameterFieldUtility.PropertyStateKey(parameter),
                    transform => binding.TransformValue((target, value) =>
                            transform(value as string ?? string.Empty,
                                DefaultParameterDrawer.ResolveModifier(target, modifierPath)),
                        "Change Range Parameters"));
                if (single == null) return;
                lists.Add(single);
                segmentIndices.Add(-1);
            }

            private void RefreshLists(SerializedProperty parameter, SerializedProperty modifier)
            {
                if (modifier?.managedReferenceValue is CompositeModifier)
                {
                    var segments = ParameterFieldUtility.SplitSegments(
                        parameter.stringValue ?? string.Empty, segmentCount);
                    var binding = new SerializedPropertyBinding(parameter);
                    for (var i = 0; i < lists.Count; i++)
                    {
                        var segmentIndex = segmentIndices[i];
                        lists[i].Refresh(segments[segmentIndex],
                            DefaultParameterDrawer.SelectedSegments(
                                binding, segmentCount, segmentIndex),
                            DefaultParameterDrawer.SelectedModifiers(binding,
                                modifier.propertyPath, segmentIndex));
                    }
                    return;
                }

                if (lists.Count == 1)
                {
                    var binding = new SerializedPropertyBinding(parameter);
                    lists[0].Refresh(parameter.stringValue ?? string.Empty,
                        DefaultParameterDrawer.SelectedSegments(binding, 1, -1),
                        DefaultParameterDrawer.SelectedModifiers(
                            binding, modifier.propertyPath, -1));
                }
            }

            private SerializedProperty RequireProperty() =>
                context.Binding.RequireSerializedProperty();
        }
    }
}
