using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal sealed class ParameterRuleDrawer : IManagedReferenceDrawer
    {
        private readonly struct BindingOption
        {
            public readonly ModifierNodeId nodeId;
            public readonly string parameterId;
            public readonly string label;
            public readonly Type valueType;

            public BindingOption(ModifierNodeId nodeId, ParameterDescriptor parameter, string path)
            {
                this.nodeId = nodeId;
                parameterId = parameter.Id;
                label = $"{path} / {parameter.DisplayName}";
                valueType = parameter.ValueType;
            }
        }

        private static readonly List<BindingOption> bindingScratch = new();
        private static readonly HashSet<BaseModifier> modifierScratch = new();
        private static readonly Dictionary<ModifierNodeId, int> nodeCounts = new();
        private static Type[] valueTypes;

        [InitializeOnLoadMethod]
        private static void Register() =>
            TypedManagedReferenceDrawerRegistry.Register(typeof(ParameterRule),
                new ParameterRuleDrawer());

        /// <inheritdoc/>
        public VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var binding = new SerializedPropertyBinding(property);
            var context = new SerializedPropertyContext(binding, property, property.displayName);
            var root = InspectorVisuals.CreateStack();
            UniTextInspectorTheme.Initialize(root);
            AddField(root, property, "selector");
            AddField(root, property, "playback");
            AddField(root, property, "scope");
            AddField(root, property, "trigger");
            AddField(root, property, "priority");
            var options = Array.Empty<BindingOption>();
            var target = new SelectorField<string>("Target",
                new[] { "Select parameter..." }, 0);
            InspectorVisuals.MarkFieldAxis(target);
            SerializedPropertyField.AddPrefabOverrideIndicator(target,
                new SerializedPropertyBinding(
                    InspectorHelpers.RequireRelative(property, "targetNode")), true);
            SerializedPropertyField.AddPrefabOverrideIndicator(target,
                new SerializedPropertyBinding(
                    InspectorHelpers.RequireRelative(property, "parameterId")));
            root.Add(target);
            var targetValue = InspectorVisuals.CreateStack();
            root.Add(targetValue);
            AddField(root, property, "composition");
            var message = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            message.style.display = DisplayStyle.None;
            root.Add(message);
            long renderedValueId = long.MinValue;
            Type renderedValueType = null;

            void Refresh()
            {
                var current = binding.FindSerializedProperty();
                if (current == null) return;
                var rule = SerializedPropertyBinding.ResolveInstance(
                                 current.serializedObject.targetObject, current.propertyPath)
                             as ParameterRule ?? throw new InvalidOperationException(
                                 $"Serialized value '{current.propertyPath}' is not a ParameterRule.");
                options = BuildCommonBindings(binding, rule);
                var labels = new List<string>(options.Length + 1)
                {
                    "Select parameter...",
                };
                for (var i = 0; i < options.Length; i++) labels.Add(options[i].label);
                var selected = FindSelected(options, rule.TargetNode, rule.ParameterId) + 1;
                target.SetChoices(labels, Mathf.Clamp(selected, 0, labels.Count - 1));
                var mixedTarget = HasMixedTarget(binding, rule);
                target.showMixedValue = mixedTarget;

                var valueProperty = InspectorHelpers.RequireRelative(current, "targetValue");
                var value = valueProperty.managedReferenceValue;
                var valueId = value == null ? 0 : valueProperty.managedReferenceId;
                var valueType = value?.GetType();
                if (valueId != renderedValueId || valueType != renderedValueType)
                {
                    InspectorVisuals.ClearContent(targetValue);
                    targetValue.Add(SerializedPropertyField.Create(valueProperty));
                    renderedValueId = valueId;
                    renderedValueType = valueType;
                }

                var error = mixedTarget
                    ? "Selected rules target different parameters."
                    : BindingMessageForSelection(binding, rule);
                message.text = error ?? string.Empty;
                message.style.display = string.IsNullOrEmpty(error)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            target.RegisterValueChangedCallback(_ =>
            {
                var index = target.Index - 1;
                var picked = (uint)index < (uint)options.Length
                    ? options[index]
                    : (BindingOption?)null;
                ApplyTarget(binding, picked);
                Refresh();
            });
            root.RegisterCallback<ChangeEvent<Type>>(_ => Refresh());
            return context.Observe(root, Refresh);
        }

        private static void AddField(VisualElement root,
            SerializedProperty property, string relativePath)
            => root.Add(SerializedPropertyField.CreateRelative(property, relativePath));

        private static void ApplyTarget(SerializedPropertyBinding propertyBinding, BindingOption? option)
        {
            Dictionary<UnityEngine.Object, BindingOption> resolved = null;
            if (option.HasValue)
            {
                resolved = new Dictionary<UnityEngine.Object, BindingOption>();
                var targets = propertyBinding.SerializedObject.targetObjects;
                for (var i = 0; i < targets.Length; i++)
                {
                    var rule = propertyBinding.GetValue(targets[i]) as ParameterRule;
                    BuildBindings(targets[i], rule);
                    var index = FindSemantic(bindingScratch, option.Value);
                    if (index < 0)
                        throw new InvalidOperationException(
                            $"Modifier property '{option.Value.label}' is unavailable on '{targets[i].name}'.");
                    resolved.Add(targets[i], bindingScratch[index]);
                }
            }
            propertyBinding.EditSerializedProperties(rule =>
            {
                var targetNode = InspectorHelpers.RequireRelative(rule, "targetNode");
                var parameterId = InspectorHelpers.RequireRelative(rule, "parameterId");
                var targetValue = InspectorHelpers.RequireRelative(rule, "targetValue");
                if (!option.HasValue)
                {
                    targetNode.boxedValue = default(ModifierNodeId);
                    parameterId.stringValue = string.Empty;
                    targetValue.managedReferenceValue = null;
                    return;
                }

                var binding = resolved[rule.serializedObject.targetObject];
                var value = targetValue.managedReferenceValue as RuleValue;
                if (value == null || value.ValueType != binding.valueType)
                    value = CreateValue(binding.valueType);
                targetNode.boxedValue = binding.nodeId;
                parameterId.stringValue = binding.parameterId;
                targetValue.managedReferenceValue = value;
            }, "Change Parameter Rule Target");
        }

        private static bool HasMixedTarget(SerializedPropertyBinding binding,
            ParameterRule primaryRule)
        {
            var targets = binding.SerializedObject.targetObjects;
            BindingOption? first = null;
            var firstNode = default(ModifierNodeId);
            string firstProperty = null;
            for (var i = 0; i < targets.Length; i++)
            {
                var rule = i == 0
                    ? primaryRule
                    : binding.GetValue(targets[i]) as ParameterRule;
                BuildBindings(targets[i], rule);
                var index = rule == null
                    ? -1
                    : FindSelected(bindingScratch, rule.TargetNode, rule.ParameterId);
                var current = index < 0 ? (BindingOption?)null : bindingScratch[index];
                if (i == 0)
                {
                    first = current;
                    firstNode = rule?.TargetNode ?? default;
                    firstProperty = rule?.ParameterId;
                    continue;
                }
                if (first.HasValue != current.HasValue ||
                    first.HasValue && !SameSemanticTarget(first.Value, current.Value) ||
                    !first.HasValue && (rule?.TargetNode ?? default) != firstNode ||
                    !first.HasValue && rule?.ParameterId != firstProperty)
                {
                    BuildBindings(targets[0], primaryRule);
                    return true;
                }
            }
            BuildBindings(targets[0], primaryRule);
            return false;
        }

        private static string BindingMessage(ModifierNodeId nodeId, string parameterId,
            RuleValue value)
        {
            if (!nodeId.IsValid || string.IsNullOrWhiteSpace(parameterId))
                return "Choose a parameter.";
            var first = -1;
            var count = 0;
            for (var i = 0; i < bindingScratch.Count; i++)
            {
                var option = bindingScratch[i];
                if (option.nodeId != nodeId || option.parameterId != parameterId) continue;
                if (first < 0) first = i;
                count++;
            }
            if (count == 0) return $"The selected parameter is not present in this graph.";
            if (nodeCounts.TryGetValue(nodeId, out var nodeCount) && nodeCount > 1)
                return $"Modifier node '{nodeId}' is duplicated in this graph.";
            if (value == null) return $"Choose a {bindingScratch[first].valueType.Name} target value.";
            if (value.ValueType != bindingScratch[first].valueType)
                return $"Target value is {value.ValueType.Name}; the property requires " +
                       $"{bindingScratch[first].valueType.Name}.";
            return null;
        }

        private static string BindingMessageForSelection(
            SerializedPropertyBinding binding, ParameterRule primaryRule)
        {
            var targets = binding.SerializedObject.targetObjects;
            for (var i = 0; i < targets.Length; i++)
            {
                var rule = i == 0
                    ? primaryRule
                    : binding.GetValue(targets[i]) as ParameterRule;
                BuildBindings(targets[i], rule);
                var message = rule == null
                    ? "Rule value is missing."
                    : BindingMessage(rule.TargetNode, rule.ParameterId,
                        rule.TargetValue);
                if (string.IsNullOrEmpty(message)) continue;
                BuildBindings(targets[0], primaryRule);
                return targets.Length == 1
                    ? message
                    : $"{targets[i].name}: {message}";
            }
            BuildBindings(targets[0], primaryRule);
            return null;
        }

        private static int FindSelected(IReadOnlyList<BindingOption> options,
            ModifierNodeId nodeId, string parameterId)
        {
            for (var i = 0; i < options.Count; i++)
                if (options[i].nodeId == nodeId &&
                    options[i].parameterId == parameterId) return i;
            return -1;
        }

        private static BindingOption[] BuildCommonBindings(
            SerializedPropertyBinding binding, ParameterRule primaryRule)
        {
            var targets = binding.SerializedObject.targetObjects;
            BuildBindings(targets[0], primaryRule);
            var common = new List<BindingOption>(bindingScratch);
            for (var targetIndex = 1; targetIndex < targets.Length; targetIndex++)
            {
                var rule = binding.GetValue(targets[targetIndex]) as ParameterRule;
                BuildBindings(targets[targetIndex], rule);
                for (var i = common.Count - 1; i >= 0; i--)
                {
                    var candidate = common[i];
                    if (FindSemantic(bindingScratch, candidate) < 0)
                        common.RemoveAt(i);
                }
            }
            BuildBindings(targets[0], primaryRule);
            return common.ToArray();
        }

        private static int FindSemantic(IReadOnlyList<BindingOption> options,
            BindingOption candidate)
        {
            for (var i = 0; i < options.Count; i++)
                if (SameSemanticTarget(options[i], candidate)) return i;
            return -1;
        }

        private static bool SameSemanticTarget(BindingOption left, BindingOption right)
            => left.parameterId == right.parameterId &&
               left.label == right.label &&
               left.valueType == right.valueType;

        private static void BuildBindings(UnityEngine.Object target, ParameterRule rule)
        {
            bindingScratch.Clear();
            modifierScratch.Clear();
            nodeCounts.Clear();
            switch (target)
            {
                case UniTextBase text:
                    if (AddContainingStyle(text.Styles, rule, "Styles")) break;
                    var presets = text.StylePresets;
                    for (var i = 0; i < presets.Count; i++)
                        if (presets[i] != null && AddContainingStyle(presets[i].Styles, rule,
                                $"Style Presets[{i}]")) return;
                    if (text.UseGlobalStylePreset && UniTextSettings.GlobalStylePreset != null)
                        AddContainingStyle(UniTextSettings.GlobalStylePreset.Styles, rule,
                            "Global Styles");
                    break;
                case StylePreset preset:
                    AddContainingStyle(preset.Styles, rule, "Styles");
                    break;
                case ModifierGraphPreset graphPreset:
                    if (ContainsRule(graphPreset.Root, rule))
                        AddModifier(graphPreset.Root, "Modifier Graph");
                    break;
            }
        }

        private static bool AddContainingStyle(IReadOnlyList<Style> styles, ParameterRule rule,
            string prefix)
        {
            for (var i = 0; i < styles.Count; i++)
            {
                var root = styles[i]?.Modifier;
                if (!ContainsRule(root, rule)) continue;
                AddModifier(root, $"{prefix}[{i}] {root.GetType().Name}");
                return true;
            }
            return false;
        }

        private static bool ContainsRule(BaseModifier modifier, ParameterRule rule)
        {
            if (modifier == null || rule == null) return false;
            if (modifier is InteractiveModifier interactive)
            {
                var rules = interactive.Rules;
                if (ReferenceIdentity.Contains(rules, rule)) return true;
            }
            if (modifier is ModifierGraphModifier reference)
                return ContainsRule(reference.AuthoredRoot, rule);
            if (modifier.Children is not { } children) return false;
            for (var i = 0; i < children.Count; i++)
                if (ContainsRule(children[i], rule)) return true;
            return false;
        }

        private static void AddModifier(BaseModifier modifier, string path)
        {
            if (modifier == null || !modifierScratch.Add(modifier)) return;
            var nodeId = modifier.NodeId;
            nodeCounts.TryGetValue(nodeId, out var nodeCount);
            nodeCounts[nodeId] = nodeCount + 1;
            var parameters = modifier.Descriptors;
            for (var i = 0; i < parameters.Length; i++)
                bindingScratch.Add(new BindingOption(nodeId, parameters[i], path));
            if (modifier is ModifierGraphModifier reference)
            {
                AddModifier(reference.AuthoredRoot, $"{path} / Preset");
                return;
            }
            if (modifier.Children is not { } children) return;
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child != null) AddModifier(child, $"{path} / [{i}] {child.GetType().Name}");
            }
        }

        /// <summary>
        /// Creates the editor for one rule value's payload; unit-typed values present the target
        /// parameter's declared unit vocabulary instead of the payload field's own.
        /// </summary>
        internal static VisualElement CreateValueField(SerializedProperty ruleValue,
            Type modifierType, string parameterId, string label = null)
        {
            var payload = InspectorHelpers.RequireRelative(ruleValue, "value");
            var units = ParameterFieldUtility.ParameterUnits(modifierType, parameterId);
            if (units == null) return SerializedPropertyField.Create(payload, label);
            return (ruleValue.managedReferenceValue as RuleValue)?.ValueType == typeof(UnitVector2)
                ? SerializedPropertyField.Create(payload, label, context =>
                    UniTextSerializedPropertyRenderers.CreateUnitVector2(context, units))
                : SerializedPropertyField.Create(payload, label, context =>
                    UniTextSerializedPropertyRenderers.CreateUnitValue(context, units));
        }

        internal static RuleValue CreateValue(Type targetType)
        {
            if (targetType.IsEnum)
                return (RuleValue)Activator.CreateInstance(
                    typeof(EnumRuleValue<>).MakeGenericType(targetType));
            var types = GetValueTypes();
            RuleValue found = null;
            for (var i = 0; i < types.Length; i++)
            {
                var candidate = (RuleValue)Activator.CreateInstance(types[i]);
                if (candidate.ValueType != targetType) continue;
                if (found != null)
                    throw new InvalidOperationException(
                        $"More than one RuleValue represents {targetType.FullName}.");
                found = candidate;
            }
            return found;
        }

        private static Type[] GetValueTypes()
        {
            if (valueTypes != null) return valueTypes;
            var discovered = new List<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<RuleValue>())
            {
                if (type.IsAbstract || type.ContainsGenericParameters ||
                    type.GetConstructor(Type.EmptyTypes) == null) continue;
                discovered.Add(type);
            }
            valueTypes = discovered.ToArray();
            return valueTypes;
        }

    }
}
