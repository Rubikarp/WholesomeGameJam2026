using System;
using System.Collections.Generic;
using System.Reflection;

namespace LightSide
{
    /// <summary>
    /// Shared type picker for <c>[SerializeReference]</c> fields and lists: discovers the concrete
    /// types deriving from a base, builds the grouped <see cref="Selector"/> menu (icons, groups,
    /// descriptions), and reports the chosen <see cref="Type"/>. Backs both the per-element type
    /// dropdown (<see cref="TypeSelectorDrawer"/>) and the list "+" button
    /// so every managed-reference surface offers the same default
    /// out of the box. Per-type customization stays with the consumer — it owns what to do with the
    /// picked type.
    /// </summary>
    public static class ManagedReferenceTypeMenu
    {
        /// <summary>One concrete type as the picker presents it: trimmed display name, <see cref="TypeGroupAttribute"/> group and order, <see cref="TypeDescriptionAttribute"/> text.</summary>
        public struct TypeEntry
        {
            public Type type;
            public string displayName;
            public string groupName;
            public int groupOrder;
            public string description;
        }

        private sealed class TypeCache
        {
            public readonly List<TypeEntry> entries;
            public readonly Dictionary<Type, string> displayNames;

            public TypeCache(List<TypeEntry> entries, Dictionary<Type, string> displayNames)
            {
                this.entries = entries;
                this.displayNames = displayNames;
            }
        }

        private static readonly Dictionary<Type, TypeCache> typeCache = new();

        /// <summary>Element type of an array / <c>List&lt;&gt;</c>, else the type itself.</summary>
        public static Type GetBaseType(Type fieldType)
            => SerializedReflection.GetCollectionElementType(fieldType) ?? fieldType;

        /// <summary>
        /// Trimmed display name of the instance type, or the none-label when null. A type closed over an
        /// argument names the argument too, because the argument is what distinguishes one such instance
        /// from another.
        /// </summary>
        public static string DisplayName(Type currentType, Type baseType)
        {
            if (currentType == null)
                return NoneLabel(baseType);

            var cache = GetOrCreateCache(baseType);
            if (!currentType.IsConstructedGenericType)
                return cache.displayNames.TryGetValue(currentType, out var name)
                    ? name
                    : TrimSuffix(BareName(currentType), currentType);

            var definition = currentType.GetGenericTypeDefinition();
            var stem = cache.displayNames.TryGetValue(definition, out var found)
                ? found
                : TrimSuffix(BareName(definition), definition);
            return $"{stem} ({ArgumentNames(currentType)})";
        }

        /// <summary>Type name without the arity marker a generic definition carries.</summary>
        public static string BareName(Type type)
        {
            var name = type.Name;
            var tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }

        private static string ArgumentNames(Type constructed)
        {
            var arguments = constructed.GetGenericArguments();
            if (arguments.Length == 1) return arguments[0].Name;

            var names = new string[arguments.Length];
            for (var i = 0; i < arguments.Length; i++) names[i] = arguments[i].Name;
            return string.Join(", ", names);
        }

        private static readonly Dictionary<Type, string> noneLabelCache = new();

        /// <summary>
        /// Label of the picker's "none" entry: the text a <see cref="SelectorNoneLabelAttribute"/> on
        /// the base type (or an ancestor) declares, "(None)" otherwise.
        /// </summary>
        public static string NoneLabel(Type baseType)
        {
            if (baseType == null) return "(None)";
            if (noneLabelCache.TryGetValue(baseType, out var cached)) return cached;

            var label = baseType.GetCustomAttribute<SelectorNoneLabelAttribute>()?.Label ?? "(None)";
            noneLabelCache[baseType] = label;
            return label;
        }

        /// <summary>
        /// Opens the grouped type picker for <paramref name="baseType"/>'s concrete subtypes at
        /// <paramref name="rect"/>, reporting the chosen type (or <see langword="null"/> for the
        /// none option) to <paramref name="onPicked"/>. Types assignable to an entry of
        /// <paramref name="exclude"/> are not offered.
        /// </summary>
        public static void Show(UnityEngine.Rect rect, Type baseType, Type currentType,
            Action<Type> onPicked, bool showNone, IReadOnlyList<Type> exclude = null)
        {
            if (baseType == null) return;
            var screenRect = InspectorVisuals.CaptureScreenRect(rect);
            var cache = GetOrCreateCache(baseType);
            var items = new List<Selector.SelectorItem>(cache.entries.Count);

            for (var i = 0; i < cache.entries.Count; i++)
            {
                var entry = cache.entries[i];
                if (IsExcluded(entry.type, exclude)) continue;
                items.Add(new Selector.SelectorItem
                {
                    displayName = entry.type.IsGenericTypeDefinition
                        ? entry.displayName + " …"
                        : entry.displayName,
                    searchText = entry.type.FullName,
                    groupName = entry.groupName,
                    groupOrder = entry.groupOrder,
                    icon = EditorResources.GetTypeIcon(entry.type),
                    groupIcon = EditorResources.GetGroupIcon(entry.groupName),
                    description = entry.description,
                    details = entry.description,
                    value = entry.type
                });
            }

            var selected = currentType != null && currentType.IsConstructedGenericType
                ? currentType.GetGenericTypeDefinition()
                : currentType;

            Selector.ShowAtScreenRect(screenRect, items.ToArray(), selected, value =>
            {
                var picked = (Type)value;
                if (picked == null || !picked.IsGenericTypeDefinition)
                {
                    onPicked(picked);
                    return;
                }
                OpenNextStep(() => PickArgument(screenRect, picked, picked.GetGenericArguments(),
                    new Type[picked.GetGenericArguments().Length], 0, onPicked));
            }, showNone: showNone, noneLabel: NoneLabel(baseType));
        }

        /// <summary>
        /// Opens the next step of a chained choice after the current popup has finished closing. A popup opened
        /// from inside another's selection handler is registered as its child and torn down with it, so the step
        /// waits — and carries a screen rectangle, because panel coordinates stop converting once the layout
        /// pass that produced them has ended.
        /// </summary>
        private static void OpenNextStep(Action step) => UnityEditor.EditorApplication.delayCall += () => step();

        private static bool IsExcluded(Type type, IReadOnlyList<Type> exclude)
        {
            if (exclude == null) return false;
            for (var i = 0; i < exclude.Count; i++)
                if (exclude[i] != null && exclude[i].IsAssignableFrom(type)) return true;
            return false;
        }

        /// <summary>
        /// Asks for each of <paramref name="definition"/>'s type arguments in turn, then reports the closed
        /// type. Candidates are exactly the types the parameter's own constraints admit, so a parameter
        /// declared <c>where T : Component</c> offers the project's components and nothing else.
        /// </summary>
        /// <remarks>
        /// Nothing is reported when the author dismisses a step, so an abandoned choice leaves the field as it
        /// was rather than half-formed.
        /// </remarks>
        public static void ShowTypeArguments(UnityEngine.Rect rect, Type definition, Action<Type> onPicked)
        {
            if (definition == null || !definition.IsGenericTypeDefinition) return;
            var parameters = definition.GetGenericArguments();
            PickArgument(InspectorVisuals.CaptureScreenRect(rect), definition, parameters,
                new Type[parameters.Length], 0, onPicked);
        }

        /// <summary><paramref name="screenRect"/> is the anchor in screen coordinates, captured while a layout
        /// pass was still in progress.</summary>
        private static void PickArgument(ScreenRect screenRect, Type definition, Type[] parameters,
            Type[] chosen, int index, Action<Type> onPicked)
        {
            if (index >= parameters.Length)
            {
                onPicked(definition.MakeGenericType(chosen));
                return;
            }

            var candidates = GetTypeArgumentCandidates(parameters[index]);
            var items = new Selector.SelectorItem[candidates.Count];
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var group = candidate.Namespace ?? string.Empty;
                items[i] = new Selector.SelectorItem
                {
                    displayName = candidate.Name,
                    searchText = candidate.FullName,
                    groupName = group,
                    groupOrder = group.StartsWith("Unity", StringComparison.Ordinal) ? 500 : 0,
                    icon = EditorResources.GetTypeIcon(candidate),
                    value = candidate
                };
            }

            Selector.ShowAtScreenRect(screenRect, items, chosen[index], value =>
            {
                chosen[index] = (Type)value;
                if (index + 1 >= parameters.Length)
                {
                    onPicked(definition.MakeGenericType(chosen));
                    return;
                }
                OpenNextStep(() =>
                    PickArgument(screenRect, definition, parameters, chosen, index + 1, onPicked));
            }, showNone: false);
        }

        /// <summary>
        /// Types a generic parameter's constraints admit, sorted by name. Empty when the parameter
        /// declares no constraint that names a base — an unbounded parameter has no offerable set.
        /// </summary>
        public static IReadOnlyList<Type> GetTypeArgumentCandidates(Type parameter)
        {
            if (parameter == null || !parameter.IsGenericParameter) return Array.Empty<Type>();
            if (argumentCache.TryGetValue(parameter, out var cached)) return cached;

            var constraints = parameter.GetGenericParameterConstraints();
            var root = PrimaryConstraint(constraints);
            if (root == null)
            {
                argumentCache[parameter] = Array.Empty<Type>();
                return Array.Empty<Type>();
            }

            var found = new List<Type>();
            if (Admits(root, parameter, constraints)) found.Add(root);
            foreach (var candidate in UnityEditor.TypeCache.GetTypesDerivedFrom(root))
                if (Admits(candidate, parameter, constraints))
                    found.Add(candidate);

            found.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            argumentCache[parameter] = found;
            return found;
        }

        /// <summary>Whether every one of a definition's parameters has at least one admissible argument.</summary>
        private static bool IsClosable(Type definition)
        {
            foreach (var parameter in definition.GetGenericArguments())
                if (GetTypeArgumentCandidates(parameter).Count == 0)
                    return false;
            return true;
        }

        private static Type PrimaryConstraint(Type[] constraints)
        {
            foreach (var constraint in constraints)
                if (constraint.IsClass && !constraint.ContainsGenericParameters)
                    return constraint;

            foreach (var constraint in constraints)
                if (constraint.IsInterface && !constraint.ContainsGenericParameters)
                    return constraint;

            return null;
        }

        /// <summary>Checks constraints decidable before substitution; <see cref="Type.MakeGenericType"/> validates dependent constraints after every argument is chosen.</summary>
        private static bool Admits(Type candidate, Type parameter, Type[] constraints)
        {
            if (candidate.IsGenericTypeDefinition || candidate.IsGenericParameter)
                return false;

            var attributes = parameter.GenericParameterAttributes;
            if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0 && candidate.IsValueType)
                return false;
            if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0 && !candidate.IsValueType)
                return false;
            if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
                !candidate.IsValueType && (candidate.IsAbstract || !IsInstantiable(candidate)))
                return false;

            foreach (var constraint in constraints)
            {
                if (constraint.IsGenericParameter || constraint.ContainsGenericParameters) continue;
                if (!constraint.IsAssignableFrom(candidate)) return false;
            }

            return true;
        }

        private static readonly Dictionary<Type, IReadOnlyList<Type>> argumentCache = new();

        /// <summary>Concrete, <see cref="SerializableAttribute"/>-marked, non-hidden subtypes of <paramref name="baseType"/> with display name, <see cref="TypeGroupAttribute"/> grouping and description, sorted as in the picker. Shared discovery so callers don't re-scan <see cref="UnityEditor.TypeCache"/>.</summary>
        public static IReadOnlyList<TypeEntry> GetConcreteTypes(Type baseType) => GetOrCreateCache(baseType).entries;

        private static TypeCache GetOrCreateCache(Type baseType)
        {
            if (typeCache.TryGetValue(baseType, out var cache))
                return cache;

            var entries = new List<TypeEntry>();
            var displayNames = new Dictionary<Type, string>();

            var types = UnityEditor.TypeCache.GetTypesDerivedFrom(baseType);

            foreach (var type in types)
            {
                if (type.IsAbstract)
                    continue;

                if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                    continue;

                if (!type.IsSerializable)
                    continue;

                if (type.GetCustomAttribute<HideFromTypeSelectorAttribute>() != null)
                    continue;

#if !UNITY_2023_1_OR_NEWER
                if (type.IsGenericTypeDefinition)
                    continue;
#endif

                if (!IsInstantiable(type))
                    continue;

                if (type.IsGenericTypeDefinition && !IsClosable(type))
                    continue;

                var groupAttr = type.GetCustomAttribute<TypeGroupAttribute>();
                var displayName = TrimSuffix(BareName(type), type);

                entries.Add(new TypeEntry
                {
                    type = type,
                    displayName = displayName,
                    groupName = groupAttr?.GroupName ?? string.Empty,
                    groupOrder = groupAttr?.Order ?? 999,
                    description = ElementLabelUtility.Description(type)
                });

                displayNames[type] = displayName;
            }

            entries.Sort(CompareEntries);

            cache = new TypeCache(entries, displayNames);
            typeCache[baseType] = cache;
            return cache;
        }

        /// <summary>
        /// Creates the picked type the way the picker's own menu predicate accepts it: through the
        /// parameterless constructor, public or not.
        /// </summary>
        public static object Create(Type type) => Activator.CreateInstance(type, nonPublic: true);

        private static bool IsInstantiable(Type type)
            => type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null) != null;

        private static int CompareEntries(TypeEntry a, TypeEntry b)
        {
            var orderCmp = a.groupOrder.CompareTo(b.groupOrder);
            if (orderCmp != 0) return orderCmp;

            var groupCmp = string.Compare(a.groupName, b.groupName, StringComparison.Ordinal);
            if (groupCmp != 0) return groupCmp;

            return string.Compare(a.displayName, b.displayName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Display name for the type menu and element labels: strips the category suffix declared by
        /// the nearest ancestor or interface carrying <see cref="TypeMenuSuffixAttribute"/>. Scoped to
        /// the type's own category — a name that merely contains another category's suffix keeps its
        /// full name; an unannotated category returns the bare type name.
        /// </summary>
        public static string TrimSuffix(string name, Type type)
        {
            if (string.IsNullOrEmpty(name) || type == null) return name;
            var attr = ResolveSuffixAttribute(type);
            if (attr == null) return name;
            foreach (var suffix in attr.Suffixes)
            {
                var trimmed = Strip(name, suffix);
                if (trimmed != name) return trimmed;
            }
            return name;
        }

        private static readonly Dictionary<Type, TypeMenuSuffixAttribute> suffixAttributeCache = new();

        private static TypeMenuSuffixAttribute ResolveSuffixAttribute(Type type)
        {
            if (suffixAttributeCache.TryGetValue(type, out var cached)) return cached;

            TypeMenuSuffixAttribute result = null;
            for (var t = type; t != null; t = t.BaseType)
            {
                result = t.GetCustomAttribute<TypeMenuSuffixAttribute>(inherit: false);
                if (result != null) break;
            }
            if (result == null)
            {
                foreach (var iface in type.GetInterfaces())
                {
                    result = iface.GetCustomAttribute<TypeMenuSuffixAttribute>(inherit: false);
                    if (result != null) break;
                }
            }
            suffixAttributeCache[type] = result;
            return result;
        }

        private static string Strip(string name, string suffix)
            => name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - suffix.Length)
                : name;
    }
}
