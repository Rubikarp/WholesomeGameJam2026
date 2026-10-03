using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(RangeChannel))]
    [CanEditMultipleObjects]
    internal sealed class RangeChannelEditor : FullWidthEditor
    {
        private static Selector.SelectorItem[] payloadItems;

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = UniTextInspectorTheme.CreateRoot();
            var section = InspectorVisuals.CreateSection("Channel Contract");

            var property = InspectorHelpers.RequireProperty(serializedObject, "payloadTypeName");
            var binding = new SerializedPropertyBinding(property);
            var field = new SelectorField<string>(
                "Payload Type",
                property.stringValue ?? string.Empty,
                () => Items((string)binding.Value));
            field.tooltip = "Semantic payload contract. None creates an event-only channel.";
            InspectorVisuals.MarkFieldAxis(field);
            var context = new SerializedPropertyContext(
                binding, property, "Payload Type");
            section.Add(context.Bind(field,
                undoName: "Set Range Channel Payload Type"));
            var error = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            section.Add(error);
            root.Add(section);

            void Refresh()
            {
                var current = binding.FindSerializedProperty();
                if (current == null) return;
                var missing = CountMissingPayloadTypes(binding);
                error.text = missing == 0
                    ? string.Empty
                    : binding.SerializedObject.targetObjects.Length == 1
                        ? $"Missing payload type: {current.stringValue}"
                        : $"{missing}/{binding.SerializedObject.targetObjects.Length} selected channels reference missing payload types.";
                error.style.display = missing > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            return context.Observe(root, Refresh);
        }

        private static int CountMissingPayloadTypes(SerializedPropertyBinding binding)
        {
            var count = 0;
            var targets = binding.SerializedObject.targetObjects;
            for (var i = 0; i < targets.Length; i++)
            {
                var value = binding.GetValue(targets[i]) as string;
                if (!string.IsNullOrEmpty(value) && Type.GetType(value, false) == null)
                    count++;
            }
            return count;
        }

        private static Selector.SelectorItem[] Items(string current)
        {
            var items = payloadItems ??= CollectItems();
            if (string.IsNullOrEmpty(current) || Type.GetType(current, false) != null)
                return items;
            return StateArray.Add(items, new Selector.SelectorItem
            {
                displayName = "Missing Type",
                searchText = current,
                groupName = "Unavailable",
                description = current,
                value = current,
            });
        }

        private static Selector.SelectorItem[] CollectItems()
        {
            var types = new HashSet<Type>
            {
                typeof(string), typeof(bool), typeof(int), typeof(long), typeof(float),
                typeof(double), typeof(Guid), typeof(Vector2), typeof(Vector3), typeof(Color),
            };

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] candidates;
                try
                {
                    candidates = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    candidates = exception.Types;
                }

                for (var i = 0; i < candidates.Length; i++)
                {
                    var type = candidates[i];
                    if (type == null || type == typeof(void) || type.IsPointer || type.IsByRef ||
                        type.ContainsGenericParameters || !(type.IsPublic || type.IsNestedPublic))
                        continue;
                    types.Add(type);
                }
            }

            var ordered = types.OrderBy(static type => type.Assembly.GetName().Name)
                .ThenBy(static type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            var result = new Selector.SelectorItem[ordered.Length + 1];
            result[0] = new Selector.SelectorItem
            {
                displayName = "None (events only)",
                searchText = "none events only",
                value = string.Empty,
            };
            for (var i = 0; i < ordered.Length; i++)
            {
                var type = ordered[i];
                var assembly = type.Assembly.GetName().Name;
                result[i + 1] = new Selector.SelectorItem
                {
                    displayName = FriendlyName(type),
                    searchText = type.FullName,
                    groupName = string.IsNullOrEmpty(type.Namespace)
                        ? assembly
                        : assembly + "/" + type.Namespace.Replace('.', '/'),
                    description = type.AssemblyQualifiedName,
                    value = type.AssemblyQualifiedName,
                    accentKey = type,
                };
            }
            return result;
        }

        private static string FriendlyName(Type type)
        {
            if (!type.IsGenericType) return type.Name;
            var name = type.Name;
            var tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }
    }
}
