using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal static class RangePayloadBindingMenu
    {
        private readonly struct Option
        {
            public readonly string label;
            public readonly string member;
            public readonly string description;
            public readonly bool disabled;

            public Option(string label, string member, string description = null,
                bool disabled = false)
            {
                this.label = label;
                this.member = member;
                this.description = description;
                this.disabled = disabled;
            }
        }

        private static readonly List<Option> options = new();
        private static readonly HashSet<(RangeChannel channel, string member)> unique = new();

        public static void Show(Rect rect, SerializedProperty member, Type expectedType,
            bool allowTextConversion)
        {
            Build(expectedType, allowTextConversion);
            var items = new Selector.SelectorItem[options.Count];
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                items[i] = new Selector.SelectorItem
                {
                    displayName = option.label,
                    searchText = option.label,
                    description = option.description,
                    disabled = option.disabled,
                    value = option.member,
                };
            }
            Selector.Show(rect, items, member.stringValue, value =>
            {
                new SerializedPropertyBinding(member).SetValue(
                    (string)value, "Change Payload Member");
            },
                emptyMessage: "No compatible RangeChannel payload members.");
        }

        private static void Build(Type expectedType, bool allowTextConversion)
        {
            options.Clear();
            unique.Clear();
            var guids = AssetDatabase.FindAssets("t:RangeChannel");
            for (var i = 0; i < guids.Length; i++)
            {
                var channel = AssetDatabase.LoadAssetAtPath<RangeChannel>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (channel == null) continue;
                Type payloadType;
                try { payloadType = channel.PayloadType; }
                catch (InvalidOperationException exception)
                {
                    options.Add(new Option($"{channel.name} (Unavailable)", null,
                        exception.Message, true));
                    continue;
                }
                if (payloadType == null || typeof(IRangePayloadValues).IsAssignableFrom(payloadType))
                    continue;
                var fields = payloadType.GetFields(BindingFlags.Instance | BindingFlags.Public);
                for (var j = 0; j < fields.Length; j++)
                    Add(channel, fields[j].Name, fields[j].FieldType, expectedType,
                        allowTextConversion);
                var properties = payloadType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                for (var j = 0; j < properties.Length; j++)
                {
                    var property = properties[j];
                    if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                    Add(channel, property.Name, property.PropertyType, expectedType,
                        allowTextConversion);
                }
            }
        }

        private static void Add(RangeChannel channel, string member, Type valueType,
            Type expectedType, bool allowTextConversion)
        {
            if (!allowTextConversion && expectedType != null &&
                !expectedType.IsAssignableFrom(valueType)) return;
            if (!unique.Add((channel, member))) return;
            options.Add(new Option($"{channel.name}/{member} ({valueType.Name})", member));
        }
    }

    internal sealed class RangePayloadStringBindingDrawer : IManagedReferenceDrawer
    {
        [InitializeOnLoadMethod]
        private static void Register() => TypedManagedReferenceDrawerRegistry.Register(
            typeof(RangePayloadStringBinding), new RangePayloadStringBindingDrawer());

        /// <inheritdoc/>
        public VisualElement CreatePropertyGUI(SerializedProperty property)
            => CreateMemberField(
                InspectorHelpers.RequireRelative(property, "member"), typeof(string), true);

        internal static VisualElement CreateMemberField(SerializedProperty member,
            Type expectedType, bool allowTextConversion)
        {
            if (member == null)
                throw new InvalidOperationException("Payload binding has no member field.");
            var row = InspectorVisuals.CreateCompactRow();
            UniTextInspectorTheme.Initialize(row);
            var field = SerializedPropertyField.Create(member, "Payload Member");
            field.AddToClassList("unitext-inspector-field-row__value");
            row.Add(field);
            InspectorSelectorButton button = null;
            button = new InspectorSelectorButton(() => RangePayloadBindingMenu.Show(
                button.worldBound, member, expectedType, allowTextConversion))
            {
                tooltip = "Choose a RangeChannel payload member",
            };
            button.AddToClassList("unitext-inspector-field-row__unit");
            row.Add(button);
            var context = new SerializedPropertyContext(member, "Payload Member");
            void Refresh()
            {
                var current = context.Binding.FindSerializedProperty();
                if (current != null)
                    button.SetValueAccent(current.stringValue, current.displayName);
            }
            return context.Observe(row, Refresh);
        }

    }

    internal sealed class RuleValueDrawer : IManagedReferenceDrawer
    {
        [InitializeOnLoadMethod]
        private static void Register() => TypedManagedReferenceDrawerRegistry.Register(
            typeof(RuleValue), new RuleValueDrawer());

        /// <inheritdoc/>
        public VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var binding = new SerializedPropertyBinding(property);
            var source = InspectorHelpers.RequireRelative(property, "source");
            var body = InspectorVisuals.CreateStack();
            var root = InspectorVisuals.CreateStack();
            UniTextInspectorTheme.Initialize(root);
            root.Add(SerializedPropertyField.Create(source));
            root.Add(body);
            var sourceContext = new SerializedPropertyContext(source);

            void Rebuild()
            {
                InspectorVisuals.ClearContent(body);
                InspectorMotion.SetExpanded(body, BuildBody());
            }

            bool BuildBody()
            {
                var current = binding.FindSerializedProperty();
                if (current == null) return false;
                var value = SerializedPropertyBinding.ResolveInstance(
                    binding.SerializedObject.targetObject, binding.PropertyPath)
                    as RuleValue ?? throw new InvalidOperationException(
                        $"Serialized property '{binding.PropertyPath}' is not a rule value.");
                var currentSource = InspectorHelpers.RequireRelative(current, "source");
                if (currentSource.hasMultipleDifferentValues) return false;
                if ((RangeValueSource)currentSource.enumValueIndex == RangeValueSource.PayloadMember)
                {
                    var payloadMember = InspectorHelpers.RequireRelative(current, "payloadMember");
                    body.Add(RangePayloadStringBindingDrawer.CreateMemberField(
                        payloadMember, value.ValueType, false));
                    return true;
                }

                var storedValue = current.FindPropertyRelative("value");
                if (storedValue == null) return false;
                body.Add(SerializedPropertyField.Create(storedValue));
                return true;
            }

            return sourceContext.Observe(root, Rebuild);
        }
    }
}
