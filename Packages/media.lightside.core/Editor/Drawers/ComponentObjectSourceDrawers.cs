using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(ComponentTypeToken))]
    internal sealed class ComponentTypeTokenDrawer : LightSidePropertyDrawer<ComponentTypeToken>
    {
        private static Selector.SelectorItem[] items;

        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var field = new SelectorField<Type>(context.Label, Read(context.Value), Items)
            {
                tooltip = context.Property.tooltip
            };
            context.Bind(field, type => new ComponentTypeToken(type), read: Read,
                undoName: "Change Component Type");

            var error = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            var root = InspectorVisuals.CreateStack();
            root.Add(field);
            root.Add(error);

            void RefreshError()
            {
                if (context.Binding.FindSerializedProperty() == null) return;
                var token = (ComponentTypeToken)context.Binding.Value;
                var valid = token.TryResolve(out _, out var message);
                error.text = valid ? string.Empty : message;
                error.style.display = valid ? DisplayStyle.None : DisplayStyle.Flex;
            }

            return context.Observe(root, RefreshError);
        }

        private static Type Read(object value)
        {
            var token = (ComponentTypeToken)value;
            return token.TryResolve(out var type, out _) ? type : null;
        }

        private static Selector.SelectorItem[] Items() => items ??= BuildItems();

        private static Selector.SelectorItem[] BuildItems()
        {
            var types = new List<Type> { typeof(Component) };
            foreach (var type in TypeCache.GetTypesDerivedFrom<Component>())
                if (!type.ContainsGenericParameters)
                    types.Add(type);

            types.Sort(static (left, right) =>
            {
                if (left == typeof(Component)) return right == typeof(Component) ? 0 : -1;
                if (right == typeof(Component)) return 1;
                var name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
                return name != 0
                    ? name
                    : string.Compare(left.FullName, right.FullName, StringComparison.Ordinal);
            });

            var result = new Selector.SelectorItem[types.Count];
            for (var i = 0; i < types.Count; i++)
            {
                var type = types[i];
                var root = type == typeof(Component);
                var group = root ? string.Empty : type.Namespace ?? string.Empty;
                result[i] = new Selector.SelectorItem
                {
                    displayName = root ? "Any Component" : ObjectNames.NicifyVariableName(type.Name),
                    searchText = type.AssemblyQualifiedName,
                    secondaryText = root ? null : type.Namespace,
                    groupName = group,
                    groupOrder = root
                        ? -1
                        : group.StartsWith("Unity", StringComparison.Ordinal) ? 500 : 0,
                    icon = EditorResources.GetTypeIcon(type),
                    groupIcon = EditorResources.GetGroupIcon(group),
                    description = type.IsAbstract
                        ? "Accepts concrete components derived from this type."
                        : null,
                    value = type,
                    accentKey = type
                };
            }
            return result;
        }
    }

    internal sealed class ComponentObjectSourceDrawer : IManagedReferenceDrawer
    {
        private enum SourceKind : byte
        {
            Single,
            Tree,
            List
        }

        private readonly SourceKind kind;

        private ComponentObjectSourceDrawer(SourceKind kind) => this.kind = kind;

        [InitializeOnLoadMethod]
        private static void Register()
        {
            TypedManagedReferenceDrawerRegistry.Register(
                typeof(ComponentSource), new ComponentObjectSourceDrawer(SourceKind.Single));
            TypedManagedReferenceDrawerRegistry.Register(
                typeof(ComponentTreeSource), new ComponentObjectSourceDrawer(SourceKind.Tree));
            TypedManagedReferenceDrawerRegistry.Register(
                typeof(ComponentListSource), new ComponentObjectSourceDrawer(SourceKind.List));
        }

        /// <inheritdoc/>
        public VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var binding = new SerializedPropertyBinding(property);
            var componentType = InspectorHelpers.RequireRelative(property, "componentType");
            var payload = kind switch
            {
                SourceKind.Single => InspectorHelpers.RequireRelative(property, "component"),
                SourceKind.List => InspectorHelpers.RequireRelative(property, "components"),
                _ => null
            };
            var root = InspectorVisuals.CreateStack();

            void Rebuild()
            {
                var current = binding.FindSerializedProperty();
                if (current == null) return;
                InspectorVisuals.ClearContent(root);

                var typeProperty = InspectorHelpers.RequireRelative(current, "componentType");
                root.Add(SerializedPropertyField.Create(typeProperty));
                if (typeProperty.hasMultipleDifferentValues)
                {
                    AddUnconstrainedFields(root, current);
                    return;
                }
                var token = (ComponentTypeToken)typeProperty.boxedValue;
                if (!token.TryResolve(out var required, out _))
                {
                    AddUnconstrainedFields(root, current);
                    return;
                }

                switch (kind)
                {
                    case SourceKind.Single:
                        AddSingle(root, current, required);
                        break;
                    case SourceKind.Tree:
                        root.Add(SerializedPropertyField.CreateRelative(current, "root"));
                        root.Add(SerializedPropertyField.CreateRelative(current, "includeInactive"));
                        break;
                    case SourceKind.List:
                        AddList(root, current, required);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return payload == null
                ? SerializedPropertyField.Observe(root, Rebuild, componentType)
                : SerializedPropertyField.Observe(root, Rebuild, componentType, payload);
        }

        private void AddUnconstrainedFields(VisualElement root, SerializedProperty source)
        {
            switch (kind)
            {
                case SourceKind.Single:
                    root.Add(SerializedPropertyField.CreateRelative(source, "component"));
                    break;
                case SourceKind.Tree:
                    root.Add(SerializedPropertyField.CreateRelative(source, "root"));
                    root.Add(SerializedPropertyField.CreateRelative(source, "includeInactive"));
                    break;
                case SourceKind.List:
                    root.Add(SerializedPropertyField.CreateRelative(source, "components"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void AddSingle(VisualElement root, SerializedProperty source, Type required)
        {
            var component = InspectorHelpers.RequireRelative(source, "component");
            var value = component.objectReferenceValue as Component;
            if (value != null && !required.IsInstanceOfType(value))
            {
                root.Add(new HelpBox(
                    $"{value.GetType().FullName} is not assignable to {required.FullName}. " +
                    "Clear the component or restore a compatible type.",
                    HelpBoxMessageType.Error));
                root.Add(SerializedPropertyField.Create(component));
                return;
            }

            root.Add(SerializedPropertyField.Create(component, null, context =>
                context.Bind<Object>(new InspectorObjectField(
                        context.Label, required, AllowsSceneObjects(context.Binding.SerializedObject)),
                    undoName: "Change Component")));
        }

        private static void AddList(VisualElement root, SerializedProperty source, Type required)
        {
            var components = InspectorHelpers.RequireRelative(source, "components");
            var incompatible = IncompatibleEntries(components, required);
            if (incompatible != null)
                root.Add(new HelpBox(
                    $"Entries {incompatible} are not assignable to {required.FullName}. " +
                    "Restore a compatible type or replace those entries.",
                    HelpBoxMessageType.Error));

            root.Add(SerializedPropertyField.CreateCollection(components, components.displayName,
                (rect, insert) => ObjectSelector.Show(rect, required, null,
                    selected => insert(selected), showNone: false,
                    allowSceneObjects: AllowsSceneObjects(components.serializedObject))));
        }

        private static string IncompatibleEntries(SerializedProperty components, Type required)
        {
            string result = null;
            for (var i = 0; i < components.arraySize; i++)
            {
                var value = components.GetArrayElementAtIndex(i).objectReferenceValue as Component;
                if (value != null && required.IsInstanceOfType(value)) continue;
                result = result == null ? i.ToString() : result + ", " + i;
            }
            return result;
        }

        private static bool AllowsSceneObjects(SerializedObject serializedObject)
        {
            var targets = serializedObject.targetObjects;
            for (var i = 0; i < targets.Length; i++)
                if (EditorUtility.IsPersistent(targets[i])) return false;
            return true;
        }
    }
}
