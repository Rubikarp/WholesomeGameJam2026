using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Produces consistent names, colours, and icons for serialized managed-reference elements.
    /// </summary>
    public static class ElementLabelUtility
    {
        private const int MaxComposedParts = 3;

        private static readonly Dictionary<Type, bool> toStringOverrideCache = new();
        private static readonly Dictionary<Type, string> descriptionCache = new();
        private static readonly Dictionary<Type, FieldInfo[]> composedPartCache = new();
        private static readonly Dictionary<Type, Func<object, InspectorLabel>> composers = new();
        private static readonly Dictionary<Type, FieldInfo> namingFieldCache = new();

        /// <summary>Returns whether a property is an element of a serialized array or list.</summary>
        public static bool IsArrayElement(SerializedProperty property) =>
            property.propertyPath.EndsWith("]", StringComparison.Ordinal);

        /// <summary>
        /// Wraps text in the stable editor accent of <paramref name="accent"/>. Pass a
        /// <see cref="Type"/> when the type is the identity — every instance of a modifier is the
        /// same thing — and the value itself when the value is, as for an enum member.
        /// </summary>
        public static string Colored(string text, object accent)
        {
            var color = EditorResources.GetAccentColor(accent, text);
            return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
        }

        /// <summary>
        /// Uses an overridden <see cref="object.ToString"/> for self-describing instances; otherwise
        /// returns the type name with its menu suffix removed.
        /// </summary>
        public static string DisplayName(object instance)
        {
            if (instance == null) return null;
            var type = instance.GetType();
            return HasOverriddenToString(type)
                ? instance.ToString()
                : ManagedReferenceTypeMenu.TrimSuffix(type.Name, type);
        }

        /// <summary>
        /// Text, icon, and accent identity for one value, resolved without any per-type editor code:
        /// an overridden <see cref="object.ToString"/> wins outright, otherwise the value is named by
        /// the concrete types held in its managed-reference members — the choices its author actually
        /// made — and finally by its own type name. Each part carries its own type's colour and the
        /// first part with an icon supplies the icon, so a composite reads as its contents rather
        /// than as its container.
        /// </summary>
        public static InspectorLabel Compose(object value)
        {
            if (value == null) return new InspectorLabel("(None)");
            var type = value.GetType();
            for (var current = type; current != null && current != typeof(object);
                 current = current.BaseType)
                if (composers.TryGetValue(current, out var composer))
                {
                    var registered = composer(value);
                    if (registered.Text != null) return registered;
                    break;
                }
            if (value is UnityEngine.Object asset)
                return asset == null
                    ? new InspectorLabel("(None)")
                    : new InspectorLabel(Colored(asset.name, asset),
                        AssetPreview.GetMiniThumbnail(asset), asset.name, asset);
            if (HasOverriddenToString(type))
                return Compose(value.ToString(), type.IsValueType ? value : type);

            var fields = ComposedParts(type);
            var parts = new (string Caption, object Value)[fields.Length];
            for (var i = 0; i < fields.Length; i++)
                parts[i] = (ObjectNames.NicifyVariableName(fields[i].Name),
                    fields[i].GetValue(value));
            var composed = ComposeParts(parts);
            if (composed.Text == null)
            {
                var named = NamingField(type)?.GetValue(value)?.ToString();
                return string.IsNullOrEmpty(named)
                    ? Compose(ManagedReferenceTypeMenu.TrimSuffix(type.Name, type), type)
                    : Compose(named, type.IsValueType ? value : type);
            }
            return composed.Image != null
                ? composed
                : new InspectorLabel(composed.Text, EditorResources.GetTypeIcon(type),
                    composed.PlainText, composed.AccentKey);
        }

        /// <summary>
        /// Builds a label from an explicit part order. The first part names the value and the rest
        /// qualify it, captioned when the caption is given and bare otherwise; a missing first part
        /// marks the label with <c>?</c>. Returns an empty label when every part is null, so the
        /// caller decides how an unconfigured value reads.
        /// </summary>
        public static InspectorLabel ComposeParts(params (string Caption, object Value)[] parts)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            string plain = null;
            string rich = null;
            Texture2D icon = null;
            object accent = null;
            var used = 0;
            var primaryMissing = false;
            var truncated = false;
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Value;
                if (part == null)
                {
                    if (used == 0) primaryMissing = true;
                    continue;
                }
                if (used == MaxComposedParts)
                {
                    truncated = true;
                    break;
                }
                var partType = part.GetType();
                var name = DisplayName(part);
                if (used == 0)
                {
                    plain = name;
                    rich = Colored(name, partType);
                    accent = partType;
                }
                else
                {
                    var caption = parts[i].Caption;
                    var prefix = string.IsNullOrEmpty(caption) ? string.Empty : caption + ": ";
                    plain += $" ({prefix}{name})";
                    rich += $" ({prefix}{Colored(name, partType)})";
                }
                icon ??= EditorResources.GetTypeIcon(partType);
                used++;
            }

            if (used == 0) return default;
            if (truncated)
            {
                plain += " …";
                rich += " …";
            }
            if (primaryMissing)
            {
                plain = $"?({plain})";
                rich = $"?({rich})";
            }
            return new InspectorLabel(rich, icon, plain, accent);
        }

        /// <summary>
        /// Label for a value that names itself with one piece of text. <paramref name="accent"/>
        /// follows <see cref="Colored"/>: the type when the type is the identity, the value when the
        /// value is.
        /// </summary>
        public static InspectorLabel Compose(string text, object accent)
            => new(Colored(text, accent),
                EditorResources.GetTypeIcon(accent as Type ?? accent?.GetType()), text, accent);

        /// <summary>
        /// Registers how one type names itself, ahead of the derived form. Use only where the derived
        /// part order or wording is wrong for the type. Returning an empty label falls through to the
        /// derived form, so a composer may handle only the cases it cares about.
        /// </summary>
        public static void RegisterComposer<T>(Func<T, InspectorLabel> compose) where T : class
        {
            if (compose == null) throw new ArgumentNullException(nameof(compose));
            composers[typeof(T)] = value => compose((T)value);
        }

        /// <summary>
        /// Serialized field that names an instance holding no managed references: its first string,
        /// otherwise its first enum. Numbers and vectors describe a value but do not identify it.
        /// </summary>
        private static FieldInfo NamingField(Type type)
        {
            if (namingFieldCache.TryGetValue(type, out var cached)) return cached;
            cached = FindNamingField(type);
            namingFieldCache.Add(type, cached);
            return cached;
        }

        private static FieldInfo FindNamingField(Type type)
        {
            FieldInfo enumField = null;
            for (var current = type; current != null && current != typeof(object);
                 current = current.BaseType)
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                        BindingFlags.NonPublic |
                                                        BindingFlags.DeclaredOnly))
                {
                    if (!SerializedReflection.IsSerializedField(field)) continue;
                    if (field.FieldType == typeof(string)) return field;
                    enumField ??= field.FieldType.IsEnum ? field : null;
                }
            return enumField;
        }

        /// <summary>Serialized managed-reference members in Unity's serialization order.</summary>
        private static FieldInfo[] ComposedParts(Type type)
        {
            if (composedPartCache.TryGetValue(type, out var cached)) return cached;
            var result = new List<FieldInfo>();
            CollectComposedParts(type, result);
            cached = result.ToArray();
            composedPartCache.Add(type, cached);
            return cached;
        }

        private static void CollectComposedParts(Type type, List<FieldInfo> result)
        {
            if (type == null || type == typeof(object)) return;
            CollectComposedParts(type.BaseType, result);
            var start = result.Count;
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                 BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (field.IsDefined(typeof(SerializeReference), true) &&
                    SerializedReflection.IsSerializedField(field))
                    result.Add(field);
            result.Sort(start, result.Count - start, DeclarationOrder.Instance);
        }

        /// <summary>Reflection does not guarantee declaration order; the metadata token restores it.</summary>
        private sealed class DeclarationOrder : IComparer<FieldInfo>
        {
            internal static readonly DeclarationOrder Instance = new();
            public int Compare(FieldInfo left, FieldInfo right)
                => left.MetadataToken.CompareTo(right.MetadataToken);
        }

        /// <summary>Returns the coloured display name of a managed-reference value, or null.</summary>
        public static string ColoredName(SerializedProperty property)
        {
            var value = property?.managedReferenceValue;
            return value == null ? null : Colored(DisplayName(value), value.GetType());
        }

        /// <summary>Returns the description registered for a managed type, or null.</summary>
        public static string Description(Type type)
        {
            if (type == null) return null;
            if (descriptionCache.TryGetValue(type, out var cached)) return cached;
            return descriptionCache[type] =
                type.GetCustomAttribute<TypeDescriptionAttribute>()?.Description;
        }

        /// <summary>Returns the registered icon for a managed-reference value, or null.</summary>
        public static Texture2D Icon(SerializedProperty property)
        {
            var value = property?.managedReferenceValue;
            return value == null ? null : EditorResources.GetTypeIcon(value.GetType());
        }

        private static bool HasOverriddenToString(Type type)
        {
            if (toStringOverrideCache.TryGetValue(type, out var cached)) return cached;
            var method = type.GetMethod(
                nameof(ToString),
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            var overridden = method != null && method.DeclaringType != typeof(object) &&
                             method.DeclaringType != typeof(ValueType);
            toStringOverrideCache[type] = overridden;
            return overridden;
        }
    }
}
