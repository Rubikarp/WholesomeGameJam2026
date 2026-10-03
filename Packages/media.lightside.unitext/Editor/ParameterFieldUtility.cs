using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Control kind of one <c>[Parameter]</c> slot. Simple kinds are fully described by the enum (+ range); <see cref="Enum"/>, <see cref="Unit"/>, <see cref="UnitVec2"/> and <see cref="Variant"/> carry their authored spec payload in <see cref="ParameterFieldUtility.ParamField.spec"/>.</summary>
    internal enum ParamKind : byte
    {
        Float,
        Int,
        Color,
        Vector2,
        Bool,
        String,
        Enum,
        Unit,
        UnitVec2,
        Variant,
    }

    internal static class ParameterFieldUtility
    {
        /// <summary>
        /// One slot of a modifier's positional parameter schema, built once per type in
        /// <see cref="GetFields"/>. Range/enum/primitive typing is fully resolved here (no string
        /// re-parsing at draw time); <see cref="spec"/> keeps the authored attribute DSL payload for
        /// the kinds whose vocabulary IS a string DSL (<c>[Unit]</c>/<c>[Options]</c>/<c>[Variant]</c>).
        /// </summary>
        internal struct ParamField
        {
            public string name;
            public ParamKind kind;
            /// <summary>Enum: option list (<c>A|B</c>) or provider key (<c>@key</c>). Unit/UnitVec2: unit-entry DSL (<c>px|em(0,4)</c>). Variant: the <c>[Variant]</c> spec. Empty otherwise.</summary>
            public string spec;
            public bool hasRange;
            public float min, max;
            public string defaultValue;
            public string activationValue;
            public string visibleWhen;
            public bool isString;
            public Type valueType;
            public MemberInfo variantDiscriminator;
            public Array variantValues;
        }

        /// <summary>
        /// Values that cannot be reconstructed from a parameter token alone: the live parameter owner
        /// supplies contextual option providers, while live compound fields supply their actual
        /// variant discriminator. String-backed override parameters intentionally carry only the
        /// former and are classified from their token grammar.
        /// </summary>
        internal readonly struct ParamContext
        {
            public readonly object owner;
            private readonly int[] variantIndices;

            public ParamContext(object owner, int[] variantIndices = null)
            {
                this.owner = owner;
                this.variantIndices = variantIndices;
            }

            public int VariantAt(int index) =>
                variantIndices != null && (uint)index < (uint)variantIndices.Length
                    ? variantIndices[index]
                    : -1;
        }

        internal readonly struct ToolkitValue
        {
            public readonly string token;
            public readonly int variantIndex;

            public ToolkitValue(string token, int variantIndex = -1)
            {
                this.token = token ?? string.Empty;
                this.variantIndex = variantIndex;
            }
        }

        internal struct CompositeEntry
        {
            public string displayName;
            public ParamField[] fields;
        }

        internal sealed class ParameterMember
        {
            private readonly FieldInfo[] path;
            private readonly string[] unitNames;

            public ParameterMember(FieldInfo[] path)
            {
                this.path = path;
                SerializedPath = string.Join(".", Array.ConvertAll(path, field => field.Name));
                unitNames = GetUnitNames(GetCustomAttribute<UnitAttribute>()?.Units);
            }

            public FieldInfo Field => path[path.Length - 1];
            public string Name => Field.Name;
            public Type FieldType => Field.FieldType;
            public string SerializedPath { get; }

            public object GetValue(object instance)
            {
                var value = instance;
                for (var i = 0; i < path.Length - 1 && value != null; i++)
                    value = path[i].GetValue(value);
                return value == null ? null : Field.GetValue(value);
            }

            public T GetCustomAttribute<T>() where T : Attribute => Field.GetCustomAttribute<T>();

            /// <summary>This parameter's own spelling of <paramref name="unit"/>, which its <c>[Unit]</c> vocabulary may name differently from the canonical one (<c>abs</c> for <c>px</c>); a token written in any other spelling reads back as a different token.</summary>
            public string UnitName(UnitKind unit) => NameFor(unit, unitNames);
        }

        private static readonly Dictionary<Type, ParamField[]> fieldCache = new();
        private static readonly Dictionary<Type, FieldInfo[]> defaultParameterFieldInfoCache = new();
        private static readonly Dictionary<string, ModifierParameterState> lastModifierState = new();

        private readonly struct ModifierParameterState
        {
            internal readonly string signature;
            internal readonly string parameter;

            internal ModifierParameterState(string signature, string parameter)
            {
                this.signature = signature;
                this.parameter = parameter;
            }
        }

        /// <summary>
        /// Session-UI key for a serialized property: instance id + path. A bare propertyPath collides
        /// between components whose inspectors are open at once (foldout/pending state bleeds across
        /// objects) — the instance id scopes it to the edited object.
        /// </summary>
        internal static string PropertyStateKey(SerializedProperty property) =>
            ObjectUtils.GetInstanceIdCompat(property.serializedObject.targetObject) + ":" + property.propertyPath;

        /// <summary>
        /// The reshape in <see cref="TryReshapeParameter"/> distinguishes a structural modifier edit from
        /// an atomic serialized-state replacement by tracking the parameter beside its modifier signature.
        /// When both values change together (including Undo/Redo and prefab operations), the restored pair
        /// becomes the new baseline instead of being reshaped a second time.
        /// </summary>
        #region Modifier Type Resolution

        internal static SerializedProperty FindModifierProperty(SerializedProperty property)
        {
            var path = property.propertyPath;

            var lastDot = path.LastIndexOf('.');
            if (lastDot > 0)
            {
                var parentPath = path.Substring(0, lastDot);
                var siblingProp = property.serializedObject.FindProperty(parentPath + ".modifier");
                if (siblingProp != null && siblingProp.propertyType == SerializedPropertyType.ManagedReference)
                    return siblingProp;

                var byType = FindModifierSibling(property.serializedObject.FindProperty(parentPath));
                if (byType != null) return byType;
            }

            var searchStart = 0;
            while (true)
            {
                var sourceIdx = path.IndexOf(".source", searchStart, StringComparison.Ordinal);
                if (sourceIdx < 0)
                    break;

                var afterSource = sourceIdx + 7;
                if (afterSource < path.Length && path[afterSource] != '.')
                {
                    searchStart = afterSource;
                    continue;
                }

                var modifierPath = path.Substring(0, sourceIdx) + ".modifier";
                var modifierProp = property.serializedObject.FindProperty(modifierPath);

                if (modifierProp != null && modifierProp.propertyType == SerializedPropertyType.ManagedReference)
                    return modifierProp;

                searchStart = afterSource;
            }

            return null;
        }

        /// <summary>
        /// The first immediate child of <paramref name="parent"/> that is a managed-reference holding a
        /// <see cref="BaseModifier"/> — so the default-parameter field finds its modifier regardless of the
        /// field name the holder uses (<c>modifier</c>, <c>style</c>, …). The sibling selector reference is skipped by the type test.
        /// </summary>
        private static SerializedProperty FindModifierSibling(SerializedProperty parent)
        {
            if (parent == null) return null;
            foreach (var child in InspectorHelpers.VisibleChildren(parent))
                if (child.propertyType == SerializedPropertyType.ManagedReference &&
                    child.managedReferenceValue is BaseModifier)
                    return child;
            return null;
        }

        internal static CompositeEntry[] GetCompositeEntries(SerializedProperty modifierProp)
        {
            var itemsProp = modifierProp.FindPropertyRelative("modifiers.items");
            if (itemsProp == null || !itemsProp.isArray) return null;

            var count = itemsProp.arraySize;
            var entries = new CompositeEntry[count];

            for (var i = 0; i < count; i++)
            {
                var childProp = itemsProp.GetArrayElementAtIndex(i);
                var childType = childProp.managedReferenceValue?.GetType();

                entries[i].displayName = childType != null
                    ? ObjectNames.NicifyVariableName(ManagedReferenceTypeMenu.TrimSuffix(childType.Name, childType))
                    : "Empty";
                entries[i].fields = childType != null ? GetFields(childType) : Array.Empty<ParamField>();
            }

            return entries;
        }

        internal static string GetModifierSignature(SerializedProperty modifierProp)
        {
            var modVal = modifierProp?.managedReferenceValue;
            if (modVal == null) return "";

            var typeName = modVal.GetType().FullName;
            var itemsProp = modifierProp.FindPropertyRelative("modifiers.items");
            if (itemsProp == null || !itemsProp.isArray) return typeName;

            var count = itemsProp.arraySize;
            var parts = new string[count];
            for (var i = 0; i < count; i++)
            {
                var elem = itemsProp.GetArrayElementAtIndex(i);
                parts[i] = elem.managedReferenceValue == null
                    ? "null"
                    : elem.managedReferenceId.ToString(CultureInfo.InvariantCulture);
            }
            return typeName + "[" + string.Join("|", parts) + "]";
        }

        internal static bool HasDifferentModifierSchemas(SerializedProperty modifierProperty)
        {
            var first = GetModifierSchemaSignature(modifierProperty);
            return new SerializedPropertyBinding(modifierProperty)
                .AnyTargetProperty((_, property) => GetModifierSchemaSignature(property) != first);
        }

        private static string GetModifierSchemaSignature(SerializedProperty property)
        {
            var value = property?.managedReferenceValue;
            if (value == null) return string.Empty;
            var signature = value.GetType().FullName;
            var children = property.FindPropertyRelative("modifiers.items");
            if (children == null || !children.isArray) return signature;
            var parts = new string[children.arraySize];
            for (var i = 0; i < children.arraySize; i++)
                parts[i] = GetModifierSchemaSignature(children.GetArrayElementAtIndex(i));
            return signature + "[" + string.Join("|", parts) + "]";
        }

        /// <summary>
        /// Re-fits the stored parameter when a modifier mutates in place between two inspector
        /// frames — a composite's child add/remove/reorder/swap re-pairs segments by child identity,
        /// leaving unmatched children empty (opt-in is never auto-materialised). Keyed by the owning modifier's
        /// <see cref="SerializedProperty.managedReferenceId"/> plus the parameter's path within
        /// the style, never the absolute property path: the id survives a Styles-list reorder
        /// (so moving a style keeps its parameter instead of being reset by the slot it landed
        /// on), while the in-style suffix keeps FixedRangeSource entries that share one modifier
        /// distinct. A null modifier or a not-yet-seen instance (first sight / first frame after
        /// a domain reload) no-ops.
        /// </summary>
        internal static bool TryReshapeParameter(SerializedProperty paramProp,
            SerializedProperty modifierProp, out string reshapedValue)
        {
            reshapedValue = null;

            var target = paramProp.serializedObject.targetObject;
            if (target == null) return false;

            var newSignature = GetModifierSignature(modifierProp);
            if (string.IsNullOrEmpty(newSignature)) return false;

            var key = ObjectUtils.GetInstanceIdCompat(target) + ":"
                      + modifierProp.managedReferenceId + ":" + ParamSlotPath(paramProp, modifierProp);
            var currentValue = paramProp.stringValue;
            var hadPrev = lastModifierState.TryGetValue(key, out var previous);

            if (!hadPrev || previous.signature == newSignature ||
                previous.parameter != currentValue)
            {
                lastModifierState[key] = new ModifierParameterState(newSignature, currentValue);
                return false;
            }

            if (string.IsNullOrEmpty(currentValue))
            {
                lastModifierState[key] = new ModifierParameterState(newSignature, currentValue);
                return false;
            }

            var reshaped = ReshapeParameter(previous.signature, newSignature, currentValue);
            lastModifierState[key] = new ModifierParameterState(newSignature, reshaped);
            if (reshaped == currentValue) return false;

            reshapedValue = reshaped;
            return true;
        }

        internal static bool UpdateParameterShape(SerializedProperty paramProp, SerializedProperty modifierProp)
        {
            var targets = paramProp.serializedObject.targetObjects;
            var values = new Dictionary<UnityEngine.Object, string>(targets.Length);
            var changed = false;
            var binding = new SerializedPropertyBinding(paramProp);
            binding.VisitTargetProperties((target, targetParam) =>
            {
                var targetModifier = InspectorHelpers.RequireProperty(
                    targetParam.serializedObject, modifierProp.propertyPath);
                if (TryReshapeParameter(targetParam, targetModifier, out var reshaped))
                {
                    values[target] = reshaped;
                    changed = true;
                }
                else
                    values[target] = targetParam.stringValue;
            });
            if (changed)
                binding.SetValue(target => values[target], "Update Parameters");
            return changed;
        }

        /// <summary>
        /// The parameter's path relative to its owning style (the modifier's parent). Drops the
        /// reorder-volatile <c>...Array.data[i]</c> prefix so the reshape key stays put when the
        /// Styles list is reordered, while still separating sibling FixedRangeSource entries that live
        /// under the same modifier.
        /// </summary>
        private static string ParamSlotPath(SerializedProperty paramProp, SerializedProperty modifierProp)
        {
            var modPath = modifierProp.propertyPath;
            var cut = modPath.LastIndexOf('.');
            if (cut <= 0) return paramProp.propertyPath;

            var stylePath = modPath.Substring(0, cut);
            var slot = paramProp.propertyPath;
            return slot.Length > stylePath.Length && slot.StartsWith(stylePath, StringComparison.Ordinal)
                ? slot.Substring(stylePath.Length)
                : slot;
        }

        private static string ReshapeParameter(string oldSignature, string newSignature,
            string currentValue)
        {
            ParseSignature(oldSignature, out var oldRoot, out var oldChildren);
            ParseSignature(newSignature, out var newRoot, out var newChildren);

            if (oldRoot != newRoot)
                return "";

            if (newChildren == null)
                return currentValue;

            var oldSegments = SplitSyntax(currentValue, ';');
            var oldChildArr = oldChildren ?? Array.Empty<string>();
            var pool = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
            var pairCount = Math.Min(oldChildArr.Length, oldSegments.Length);
            for (var i = 0; i < pairCount; i++)
            {
                if (!pool.TryGetValue(oldChildArr[i], out var queue))
                {
                    queue = new Queue<string>();
                    pool[oldChildArr[i]] = queue;
                }
                queue.Enqueue(oldSegments[i]);
            }

            var newSegments = new string[newChildren.Length];

            for (var i = 0; i < newChildren.Length; i++)
            {
                newSegments[i] = pool.TryGetValue(newChildren[i], out var queue) && queue.Count > 0
                    ? queue.Dequeue()
                    : "";
            }

            return JoinTrimmed(newSegments, ";");
        }

        private static void ParseSignature(string signature, out string rootType, out string[] children)
        {
            rootType = "";
            children = null;
            if (string.IsNullOrEmpty(signature)) return;

            var openIdx = signature.IndexOf('[');
            if (openIdx < 0)
            {
                rootType = signature;
                return;
            }

            rootType = signature.Substring(0, openIdx);
            var closeIdx = signature.LastIndexOf(']');
            if (closeIdx <= openIdx)
            {
                children = Array.Empty<string>();
                return;
            }

            var inner = signature.Substring(openIdx + 1, closeIdx - openIdx - 1);
            children = inner.Length == 0 ? Array.Empty<string>() : inner.Split('|');
        }

        /// <summary>
        /// Reflects a modifier's <c>[Parameter]</c> fields into the positional schema the drawer
        /// consumes — the field's C# type (refined by <c>[Unit]</c>/<c>[Options]</c>/<c>[Variant]</c>/
        /// <c>[Range]</c>) becomes the <see cref="ParamKind"/> + spec, its declaration order the slot
        /// order (own-type fields before inherited base-type fields, matching the modifier's
        /// <c>OnApply</c> read order), its name the label, and its initializer the default value.
        /// </summary>
        private static readonly Dictionary<Type, ParameterMember[]> parameterMemberCache = new();

        internal static FieldInfo[] GetDefaultParameterFieldInfos(Type holderType)
        {
            if (holderType == null) return Array.Empty<FieldInfo>();
            if (defaultParameterFieldInfoCache.TryGetValue(holderType, out var cached)) return cached;

            var result = new List<FieldInfo>();
            for (var type = holderType; type != null && type != typeof(object); type = type.BaseType)
            {
                var declared = type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                              BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                Array.Sort(declared, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
                foreach (var field in declared)
                    if (field.IsDefined(typeof(DefaultParameterAttribute), false))
                        result.Add(field);
            }

            return defaultParameterFieldInfoCache[holderType] = result.ToArray();
        }

        internal static ParamField[] GetFields(Type modifierType)
        {
            if (fieldCache.TryGetValue(modifierType, out var cached))
                return cached;

            var fields = CollectParameterFields(modifierType);
            object instance = null;
            try { instance = Activator.CreateInstance(modifierType); } catch { /* no default ctor → empty defaults */ }

            var result = new ParamField[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                result[i] = new ParamField
                {
                    name = ObjectNames.NicifyVariableName(f.Name),
                    defaultValue = ResolveDefaultToken(f, instance),
                    activationValue = ResolveActivationToken(f),
                    visibleWhen = ResolveVisibleWhen(f, fields),
                    valueType = f.FieldType,
                };
                ResolveKind(f, ref result[i]);
            }

            fieldCache[modifierType] = result;
            parameterMemberCache[modifierType] = fields.ToArray();
            return result;
        }

        /// <summary>The reflected <c>[Parameter]</c> fields, in the same order as <see cref="GetFields"/>'s schema — so the modifier-body drawer can route each slot back to its C# property/field.</summary>
        internal static ParameterMember[] GetParameterMembers(Type modifierType)
        {
            GetFields(modifierType);
            return parameterMemberCache.TryGetValue(modifierType, out var members)
                ? members
                : Array.Empty<ParameterMember>();
        }

        /// <summary>
        /// Unit vocabulary a modifier declares for one unit-typed parameter, or null when the
        /// parameter is not unit-typed.
        /// </summary>
        internal static string ParameterUnits(Type modifierType, string parameterId)
        {
            if (modifierType == null || string.IsNullOrEmpty(parameterId)) return null;
            var members = GetParameterMembers(modifierType);
            for (var i = 0; i < members.Length; i++)
            {
                if (members[i].Name != parameterId) continue;
                var type = members[i].FieldType;
                return type == typeof(UnitValue) || type == typeof(UnitVector2)
                    ? members[i].GetCustomAttribute<UnitAttribute>()?.Units ?? "px"
                    : null;
            }
            return null;
        }

        /// <summary>Marked fields most-derived first, declaration order within a type — the schema's slot order.</summary>
        private static List<ParameterMember> CollectParameterFields(Type modifierType)
        {
            var result = new List<ParameterMember>();
            for (var t = modifierType; t != null && t != typeof(object); t = t.BaseType)
            {
                var declared = t.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                           BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                Array.Sort(declared, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
                foreach (var f in declared)
                {
                    if (f.IsDefined(typeof(ParameterAttribute), false))
                        result.Add(new ParameterMember(new[] { f }));
                    else if (f.IsDefined(typeof(ParameterContainerAttribute), false))
                        CollectContainerFields(f, result);
                }
            }
            return result;
        }

        private static void CollectContainerFields(FieldInfo container,
            List<ParameterMember> result)
        {
            if (container.FieldType.IsValueType || container.FieldType == typeof(string))
                throw new InvalidOperationException(
                    $"{container.DeclaringType?.FullName}.{container.Name} parameter container must be a reference type.");

            var declared = container.FieldType.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                          BindingFlags.NonPublic);
            Array.Sort(declared, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            foreach (var field in declared)
            {
                if (field.IsDefined(typeof(ParameterAttribute), false))
                    result.Add(new ParameterMember(new[] { container, field }));
                else if (field.IsDefined(typeof(ParameterContainerAttribute), false))
                    throw new InvalidOperationException(
                        $"Nested parameter containers are not supported: {container.FieldType.FullName}.{field.Name}.");
            }
        }

        private static void ResolveKind(ParameterMember member, ref ParamField p)
        {
            var f = member.Field;
            p.spec = "";
            p.isString = f.FieldType == typeof(string);

            var variant = f.GetCustomAttribute<VariantAttribute>();
            if (variant != null)
            {
                p.kind = ParamKind.Variant;
                p.spec = variant.Spec;
                ResolveVariantDiscriminator(f, variant, ref p);
                return;
            }

            var ft = f.FieldType;
            var unit = f.GetCustomAttribute<UnitAttribute>();
            if (ft == typeof(UnitValue)) { p.kind = ParamKind.Unit; p.spec = unit?.Units ?? "px"; return; }
            if (ft == typeof(UnitVector2)) { p.kind = ParamKind.UnitVec2; p.spec = unit?.Units ?? "px"; return; }
            if (ft.IsEnum) { p.kind = ParamKind.Enum; p.spec = string.Join("|", Enum.GetNames(ft)); return; }

            if (ft == typeof(string))
            {
                var options = f.GetCustomAttribute<OptionsAttribute>();
                if (options != null)
                {
                    p.kind = ParamKind.Enum;
                    p.spec = options.Key.StartsWith("@", StringComparison.Ordinal) ? options.Key : "@" + options.Key;
                }
                else
                {
                    p.kind = ParamKind.String;
                }
                return;
            }

            if (ft == typeof(bool)) { p.kind = ParamKind.Bool; return; }
            if (ft == typeof(Color32) || ft == typeof(Color)) { p.kind = ParamKind.Color; return; }
            if (ft == typeof(Vector2)) { p.kind = ParamKind.Vector2; return; }

            if (ft == typeof(int) || ft == typeof(float))
            {
                p.kind = ft == typeof(int) ? ParamKind.Int : ParamKind.Float;
                var range = f.GetCustomAttribute<RangeAttribute>();
                if (range != null)
                {
                    p.hasRange = true;
                    p.min = range.min;
                    p.max = range.max;
                }
                return;
            }

            p.kind = ParamKind.String;
        }

        private static void ResolveVariantDiscriminator(FieldInfo field, VariantAttribute variant,
            ref ParamField parameter)
        {
            Type enumType;
            if (field.FieldType.IsEnum)
            {
                enumType = field.FieldType;
            }
            else if (!string.IsNullOrEmpty(variant.Discriminator))
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                parameter.variantDiscriminator = (MemberInfo)field.FieldType.GetField(variant.Discriminator, flags)
                                                 ?? field.FieldType.GetProperty(variant.Discriminator, flags);
                enumType = parameter.variantDiscriminator switch
                {
                    FieldInfo discriminatorField => discriminatorField.FieldType,
                    PropertyInfo discriminatorProperty when discriminatorProperty.CanRead => discriminatorProperty.PropertyType,
                    _ => throw new InvalidOperationException(
                        $"{field.DeclaringType?.FullName}.{field.Name} names missing discriminator '{variant.Discriminator}'.")
                };
            }
            else
            {
                return;
            }

            if (!enumType.IsEnum)
                throw new InvalidOperationException(
                    $"{field.DeclaringType?.FullName}.{field.Name} variant discriminator must be an enum.");

            var enumFields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
            Array.Sort(enumFields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            var variantValues = Array.CreateInstance(enumType, enumFields.Length);
            for (var i = 0; i < enumFields.Length; i++)
                variantValues.SetValue(enumFields[i].GetValue(null), i);
            parameter.variantValues = variantValues;
            var optionCount = ParseVariantSpec(variant.Spec).Length;
            if (variantValues.Length != optionCount)
                throw new InvalidOperationException(
                    $"{field.DeclaringType?.FullName}.{field.Name} has {optionCount} variants but its discriminator has {variantValues.Length} values.");
        }

        internal static int ResolveVariantIndex(in ParamField parameter, object value)
        {
            if (value == null) return -1;
            var variantValues = parameter.variantValues;
            if (variantValues == null) return -1;

            object discriminator = value;
            if (parameter.variantDiscriminator is FieldInfo discriminatorField)
                discriminator = discriminatorField.GetValue(value);
            else if (parameter.variantDiscriminator is PropertyInfo discriminatorProperty)
                discriminator = discriminatorProperty.GetValue(value);

            for (var i = 0; i < variantValues.Length; i++)
                if (Equals(variantValues.GetValue(i), discriminator))
                    return i;
            return -1;
        }

        private static object ApplyVariantDiscriminator(in ParamField parameter,
            object value, int variantIndex)
        {
            var variantValues = parameter.variantValues;
            if (value == null || variantValues == null ||
                (uint)variantIndex >= (uint)variantValues.Length)
                return value;

            var discriminator = variantValues.GetValue(variantIndex);
            switch (parameter.variantDiscriminator)
            {
                case FieldInfo field:
                    field.SetValue(value, discriminator);
                    return value;
                case PropertyInfo property when property.CanWrite:
                    property.SetValue(value, discriminator);
                    return value;
                case null when value.GetType().IsEnum:
                    return discriminator;
                default:
                    throw new InvalidOperationException(
                        $"Variant discriminator for '{value.GetType().FullName}' is not writable.");
            }
        }

        /// <summary>Formats a field's live value into its parameter token (the inverse of the reader) — seeds the rich editors from the current modifier value.</summary>
        internal static string ResolveDefaultToken(ParameterMember member, object instance)
            => instance == null ? "" : FormatToken(member, member.GetValue(instance));

        private static string FormatFloat(float value)
            => value.ToString(CultureInfo.InvariantCulture);

        private static string FormatInt(int value)
            => value.ToString(CultureInfo.InvariantCulture);

        private static string FormatBool(bool value)
            => value ? "true" : "false";

        private static string FormatColor(Color32 value)
            => $"#{value.r:X2}{value.g:X2}{value.b:X2}{value.a:X2}";

        private static string FormatUnit(float value, string unit)
            => FormatFloat(value) + unit;

        internal static string FormatToken(ParameterMember member, object val)
        {
            if (val == null) return "";
            var f = member.Field;
            var ft = f.FieldType;

            if (val is IMarkupValue mv) return mv.ToToken();
            if (ft == typeof(float)) return FormatFloat((float)val);
            if (ft == typeof(int)) return FormatInt((int)val);
            if (ft == typeof(bool)) return FormatBool((bool)val);
            if (ft == typeof(string)) return (string)val;
            if (ft.IsEnum) return EnumToken(f, val);
            if (ft == typeof(Vector2)) return FormatVector2((Vector2)val);
            if (ft == typeof(Color) || ft == typeof(Color32))
            {
                var c = ft == typeof(Color) ? (Color32)(Color)val : (Color32)val;
                return FormatColor(c);
            }
            if (ft == typeof(UnitValue)) { var u = (UnitValue)val; return FormatUnit(u.value, member.UnitName(u.unit)); }
            if (ft == typeof(UnitVector2)) { var u = (UnitVector2)val; var name = member.UnitName(u.unit); return $"{FormatUnit(u.value.x, name)} {FormatUnit(u.value.y, name)}"; }
            return "";
        }

        /// <summary>An enum's markup token. With <c>[Variant]</c> the value maps to the option whose label matches
        /// the enum name (so a short/custom token like <c>f</c> replaces the member name); otherwise the name itself.</summary>
        private static string EnumToken(FieldInfo f, object val)
        {
            var variant = f.GetCustomAttribute<VariantAttribute>();
            if (variant != null)
            {
                var opts = ParseVariantSpec(variant.Spec);
                var name = val.ToString();
                for (var i = 0; i < opts.Length; i++)
                    if (!opts[i].isSubField && opts[i].label.EqualsIgnoreCase(name))
                        return opts[i].literalValue;
            }
            return val.ToString();
        }

        private static string ResolveActivationToken(ParameterMember member)
        {
            var inheritable = member.GetCustomAttribute<InheritableAttribute>();
            if (inheritable == null) return null;
            if (member.FieldType == typeof(float)) return FormatFloat(inheritable.ActivationX);
            if (member.FieldType == typeof(Vector2))
                return $"{FormatFloat(inheritable.ActivationX)} {FormatFloat(inheritable.ActivationY)}";
            if (member.FieldType.IsEnum) return ResolvedEnumToken(member);
            throw new InvalidOperationException(
                $"{member.Field.DeclaringType?.FullName}.{member.Name} uses Inheritable on unsupported type {member.FieldType.Name}.");
        }

        /// <summary>
        /// Token an inheritable enum takes when an editor turns the override on: its lowest non-negative
        /// member, which is the value each such enum documents as the fallback of an unresolved chain.
        /// </summary>
        private static string ResolvedEnumToken(ParameterMember member)
        {
            var values = Enum.GetValues(member.FieldType);
            var best = long.MaxValue;
            object resolved = null;
            for (var i = 0; i < values.Length; i++)
            {
                var candidate = values.GetValue(i);
                var numeric = Convert.ToInt64(candidate);
                if (numeric < 0L || numeric >= best) continue;
                best = numeric;
                resolved = candidate;
            }
            if (resolved == null)
                throw new InvalidOperationException(
                    $"{member.Field.DeclaringType?.FullName}.{member.Name} uses Inheritable on " +
                    $"{member.FieldType.Name}, which declares no non-negative member to activate.");
            return EnumToken(member.Field, resolved);
        }

        /// <summary>Inverse of <see cref="EnumToken"/>: the enum value a token maps to, honouring a <c>[Variant]</c> label↔token map, else parsed by name.</summary>
        internal static object EnumFromToken(ParameterMember member, Type enumType, string token)
        {
            if (TryEnumFromToken(member, enumType, token, out var value))
                return value;
            throw new ArgumentException($"'{token}' is not a valid {enumType.FullName} value.", nameof(token));
        }

        private static bool TryEnumFromToken(ParameterMember member, Type enumType, string token,
            out object value)
        {
            var f = member.Field;
            var variant = f.GetCustomAttribute<VariantAttribute>();
            if (variant != null)
            {
                var opts = ParseVariantSpec(variant.Spec);
                for (var i = 0; i < opts.Length; i++)
                    if (!opts[i].isSubField && opts[i].literalValue.EqualsIgnoreCase(token ?? ""))
                    {
                        value = Enum.Parse(enumType, opts[i].label, true);
                        return true;
                    }
            }
            var names = Enum.GetNames(enumType);
            if (ParameterTokenExtensions.TryMatchEnum(token.AsSpan(), names, out var index))
            {
                value = Enum.GetValues(enumType).GetValue(index);
                return true;
            }
            value = null;
            return false;
        }

        /// <summary>Resolves <c>[VisibleWhen("field","label")]</c> to the drawer's <c>"slotIndex:label"</c> form.</summary>
        private static string ResolveVisibleWhen(ParameterMember member,
            List<ParameterMember> fields)
        {
            var vw = member.GetCustomAttribute<VisibleWhenAttribute>();
            if (vw == null) return null;
            for (var i = 0; i < fields.Count; i++)
                if (string.Equals(fields[i].Name, vw.Field, StringComparison.Ordinal))
                    return i + ":" + vw.Label;
            return null;
        }

        #endregion

        #region Parameter Editing

        internal static string EncodeToken(in ParamField field, string token)
            => ParameterTokenizer.Encode(token,
                field.isString || field.kind is ParamKind.String or ParamKind.Variant);

        private static string ResolveToken(string token, string fallback)
        {
            var decoded = DecodeToken(token, out var isSet);
            return isSet ? decoded : fallback ?? string.Empty;
        }

        private static string DecodeToken(string token, out bool isSet)
        {
            var source = (token ?? string.Empty).AsSpan();
            var value = ParameterTokenizer.Decode(source, out var decoded, out isSet);
            return decoded ?? (value.Length == source.Length ? token ?? string.Empty : value.ToString());
        }

        private static string ResolveFallbackToken(in ParamField field, ParamContext context,
            int fieldIndex)
        {
            if (context.owner == null || fieldIndex < 0)
                return field.defaultValue ?? string.Empty;
            var member = GetParameterMembers(context.owner.GetType())[fieldIndex];
            return FormatToken(member, member.GetValue(context.owner));
        }

        private static int ResolveFallbackVariant(in ParamField field, ParamContext context,
            int fieldIndex)
        {
            if (context.owner == null || fieldIndex < 0) return -1;
            var member = GetParameterMembers(context.owner.GetType())[fieldIndex];
            return ResolveVariantIndex(in field, member.GetValue(context.owner));
        }

        /// <summary>
        /// Resolves a field's <c>VisibleWhen</c> predicate against the current token state.
        /// Format: <c>"slotIndex:label"</c> — variant slots match the variant option's label,
        /// non-variant slots match the token verbatim. Empty / missing predicate ⇒ always visible.
        /// </summary>
        internal static bool IsFieldVisible(in ParamField field, string[] tokens, ParamField[] allFields,
            ParamContext context = default)
        {
            if (string.IsNullOrEmpty(field.visibleWhen)) return true;

            var sepIdx = field.visibleWhen.IndexOf(':');
            if (sepIdx <= 0) return true;

            if (!ParameterReader.ParseInt(field.visibleWhen.AsSpan(0, sepIdx), out var slotIdx))
                return true;

            if (slotIdx < 0 || slotIdx >= allFields.Length) return false;

            var expected = field.visibleWhen.Substring(sepIdx + 1);
            var triggerField = allFields[slotIdx];
            var triggerToken = DecodeToken(
                slotIdx < tokens.Length ? tokens[slotIdx] : null, out var triggerIsSet);
            if (!triggerIsSet)
                triggerToken = ResolveFallbackToken(in triggerField, context, slotIdx);

            if (triggerField.kind == ParamKind.Variant)
            {
                var opts = ParseVariantSpec(triggerField.spec);
                if (opts.Length == 0) return true;
                var explicitIndex = context.VariantAt(slotIdx);
                if (!triggerIsSet && explicitIndex < 0)
                    explicitIndex = ResolveFallbackVariant(
                        in triggerField, context, slotIdx);
                var idx = ClassifyVariant(in triggerField, opts, triggerToken,
                    context.owner, explicitIndex);
                return opts[idx].label.EqualsIgnoreCase(expected);
            }

            return triggerToken.EqualsIgnoreCase(expected);
        }

        internal static bool AffectsSiblingVisibility(ParamField[] fields, int fieldIndex)
        {
            if ((uint)fieldIndex >= (uint)fields.Length) return false;
            for (var i = 0; i < fields.Length; i++)
            {
                var predicate = fields[i].visibleWhen;
                if (string.IsNullOrEmpty(predicate)) continue;
                var separator = predicate.IndexOf(':');
                if (separator <= 0) continue;
                if (ParameterReader.ParseInt(predicate.AsSpan(0, separator), out var dependency) &&
                    dependency == fieldIndex)
                    return true;
            }
            return false;
        }

        internal static string BuildFullDefault(ParamField[] fields)
        {
            if (fields == null || fields.Length == 0) return "";

            var tokens = new string[fields.Length];
            for (var i = 0; i < fields.Length; i++)
            {
                tokens[i] = IsFieldVisible(in fields[i], tokens, fields)
                    ? EncodeToken(in fields[i], ResolveVisualDefault(in fields[i]))
                    : "";
            }

            return JoinTrimmed(tokens, ",");
        }

        private static string JoinTrimmed(string[] parts, string separator)
        {
            var last = parts.Length - 1;
            while (last > 0 && string.IsNullOrEmpty(parts[last])) last--;
            return string.Join(separator, parts, 0, last + 1);
        }

        private static string ResolveVisualDefault(in ParamField field)
        {
            if (!string.IsNullOrEmpty(field.defaultValue)) return field.defaultValue;

            switch (field.kind)
            {
                case ParamKind.Float:
                case ParamKind.Int:
                case ParamKind.Unit:
                    return "0";
                case ParamKind.Color:
                    return "#FFFFFFFF";
                case ParamKind.Vector2:
                case ParamKind.UnitVec2:
                    return "0 0";
                case ParamKind.Bool:
                    return "false";
                case ParamKind.Enum:
                {
                    var options = ResolveEnumOptions(field.spec);
                    return options != null && options.Length > 0 ? options[0] : "";
                }
                case ParamKind.Variant:
                {
                    var options = ParseVariantSpec(field.spec);
                    if (options.Length == 0) return "";
                    ref var first = ref options[0];
                    return first.isSubField
                        ? (string.IsNullOrEmpty(first.subFieldDefault) ? "" : first.subFieldDefault)
                        : first.literalValue;
                }
                default:
                    return "";
            }
        }

        /// <summary>
        /// A field is <em>required</em> when it has no non-empty default (e.g. a bare <c>string</c>): it must
        /// be supplied, so the opt-in editor shows it always rather than hiding it behind "+". Fields with a
        /// real default are optional — hidden until added, then shown at that default.
        /// </summary>
        internal static bool IsRequiredField(in ParamField field) =>
            string.IsNullOrEmpty(ResolveVisualDefault(in field));

        internal static void CountCompositeStats(CompositeEntry[] entries,
            out int modsWithFields, out int totalParams)
        {
            modsWithFields = 0;
            totalParams = 0;
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].fields.Length > 0)
                {
                    modsWithFields++;
                    totalParams += entries[i].fields.Length;
                }
            }
        }

        #endregion

        #region Parameter Rows

        internal static bool IsTokenSet(string[] tokens, int i)
        {
            if (i >= tokens.Length) return false;
            ParameterTokenizer.Decode(tokens[i].AsSpan(), out _, out var isSet);
            return isSet;
        }

        /// <summary>Which fields a parameter row loop shows.</summary>
        internal enum RowFilter
        {
            /// <summary>Opt-in over a sparse positional segment: required fields (no default — the tag cannot omit them) always show; optional ones only while their token is set. Remove = clear the slot.</summary>
            OptInSegment,
            /// <summary>Opt-in over live object values: a field shows when it differs from its default or was temporarily revealed.</summary>
            LiveDiff,
        }

        /// <summary>
        /// The one shown-row predicate behind every parameter list. Conditional (<c>visibleWhen</c>)
        /// fields always show once their trigger matches — they carry the trigger option's payload.
        /// <paramref name="tokens"/> must be padded to at least <paramref name="fields"/>.Length.
        /// </summary>
        internal static bool RowShown(RowFilter filter, ParamField[] fields, string[] tokens, int i,
            Func<int, bool> isRevealed = null, ParamContext context = default)
        {
            if (!IsFieldVisible(in fields[i], tokens, fields, context)) return false;
            switch (filter)
            {
                case RowFilter.OptInSegment:
                    return IsRequiredField(in fields[i]) || IsTokenSet(tokens, i) ||
                           !string.IsNullOrEmpty(fields[i].visibleWhen);
                default:
                    return !string.IsNullOrEmpty(fields[i].visibleWhen) ||
                           tokens[i] != EncodeToken(in fields[i], fields[i].defaultValue) ||
                           (isRevealed != null && isRevealed(i));
            }
        }

        /// <summary>
        /// Selector entries for every field the user can toggle on and off under
        /// <paramref name="filter"/>'s rules: visible, not governed by a <c>visibleWhen</c>
        /// trigger, and — for <see cref="RowFilter.OptInSegment"/>, whose required fields are
        /// permanently shown — not required. Each entry is the parameter name with its data kind
        /// on the right.
        /// </summary>
        internal static Selector.SelectorItem[] BuildToggleItems(RowFilter filter,
            ParamField[] fields, string[] tokens, ParamContext context = default)
        {
            var list = new List<Selector.SelectorItem>(fields.Length);
            for (var i = 0; i < fields.Length; i++)
            {
                if (!IsFieldVisible(in fields[i], tokens, fields, context) ||
                    filter == RowFilter.OptInSegment && IsRequiredField(in fields[i]) ||
                    !string.IsNullOrEmpty(fields[i].visibleWhen)) continue;
                var kind = fields[i].kind;
                list.Add(new Selector.SelectorItem
                {
                    displayName = fields[i].name,
                    value = i,
                    createDecorator = () => CreateTypeHint(kind),
                });
            }
            return list.ToArray();
        }

        internal static string JoinSegments(string[] segments) => JoinTrimmed(segments, ";");

        internal static string[] SplitSegments(string full, int minCount)
        {
            var segs = SplitSyntax(full, ';');
            if (segs.Length >= minCount) return segs;
            var ex = new string[minCount];
            for (var i = 0; i < minCount; i++) ex[i] = i < segs.Length ? segs[i] : "";
            return ex;
        }

        internal static string SetFieldToken(string segment, ParamField[] fields, int index,
            string value, ParamContext context = default)
        {
            var split = SplitParameter(segment);
            var tokens = PadTokens(split, Math.Max(index + 1, fields.Length));
            if (ReferenceEquals(tokens, split))
                tokens = (string[])tokens.Clone();
            tokens[index] = value;
            return JoinParameter(tokens, fields, context);
        }

        internal static string ActivationToken(in ParamField field)
            => EncodeToken(in field,
                field.activationValue ?? ResolveVisualDefault(in field));

        /// <summary>Grows (never truncates — surplus trailing tokens from an older schema must survive edits) and returns the SAME array when already long enough; callers clone before mutating a borrowed result.</summary>
        private static string[] PadTokens(string[] tokens, int minLength)
        {
            if (tokens.Length >= minLength) return tokens;
            var result = new string[minLength];
            for (var i = 0; i < minLength; i++) result[i] = i < tokens.Length ? tokens[i] : "";
            return result;
        }

        private static VisualElement CreateTypeHint(ParamKind kind)
            => Selector.CreateTypeHint(PrettyKind(kind));

        private static string PrettyKind(ParamKind kind) => kind switch
        {
            ParamKind.Float => "float",
            ParamKind.Int => "int",
            ParamKind.Color => "color",
            ParamKind.Vector2 or ParamKind.UnitVec2 => "vec2",
            ParamKind.Bool => "bool",
            ParamKind.String => "text",
            ParamKind.Enum => "enum",
            ParamKind.Unit => "unit",
            ParamKind.Variant => "variant",
            _ => "",
        };

        #endregion

        #region Toolkit Fields

        private sealed class ToolkitParameterField : VisualElement
        {
            private readonly ParamField field;
            private readonly int fieldIndex;
            private readonly Action<ToolkitValue, ToolkitValue> changed;
            private ParamContext context;
            private VisualElement control;
            private string rawToken;
            private string token;
            private int variantIndex;
            private int optionsRevision;
            private bool mixedValue;

            public ToolkitParameterField(ParamField field, string token,
                ParamContext context, int fieldIndex, bool mixed,
                Action<ToolkitValue, ToolkitValue> changed)
            {
                this.field = field;
                this.fieldIndex = fieldIndex;
                this.changed = changed ?? throw new ArgumentNullException(nameof(changed));
                Refresh(token, context, mixed);
                schedule.Execute(SyncOptions).Every(250);
            }

            /// <summary>
            /// Rebuilds the control when any input it was built from changed. Every input
            /// <see cref="CreateToolkitControl"/> reads belongs in that comparison — one left out
            /// keeps a control shaped for state that no longer exists.
            /// </summary>
            public void Refresh(string nextToken, ParamContext nextContext, bool mixed)
            {
                nextToken ??= string.Empty;
                var nextValue = ResolveToken(nextToken,
                    ResolveFallbackToken(in field, nextContext, fieldIndex));
                var nextVariant = ResolveToolkitVariantIndex(
                    in field, nextToken, nextContext, fieldIndex);
                var nextOptions = ResolveOptionsRevision(in field, nextContext);
                var rebuild = control == null || rawToken != nextToken || token != nextValue ||
                              variantIndex != nextVariant || optionsRevision != nextOptions;
                rawToken = nextToken;
                token = nextValue;
                variantIndex = nextVariant;
                optionsRevision = nextOptions;
                context = nextContext;
                mixedValue = mixed;
                if (rebuild)
                {
                    InspectorVisuals.ClearContent(this);
                    control = CreateToolkitControl(field, token, () => context, fieldIndex, next =>
                    {
                        var previous = new ToolkitValue(rawToken, variantIndex);
                        var encoded = new ToolkitValue(
                            EncodeToken(in field, next.token), next.variantIndex);
                        rawToken = encoded.token;
                        token = next.token;
                        variantIndex = next.variantIndex;
                        changed(previous, encoded);
                    });
                    Add(control);
                }
                SetToolkitMixedValue(control, mixed);
            }

            /// <summary>
            /// An <c>@key</c> catalog is editor state outside the serialized object — the provider
            /// assigned on the owner, and the asset or settings that provider reads — so no property
            /// change announces its content. The rendered shape follows it on a timer instead.
            /// </summary>
            private void SyncOptions()
            {
                if (ResolveOptionsRevision(in field, context) != optionsRevision)
                    Refresh(rawToken, context, mixedValue);
            }
        }

        internal static VisualElement CreateToolkitField(ParamField field, string token,
            ParamContext context, int fieldIndex, bool mixed,
            Action<ToolkitValue, ToolkitValue> changed)
            => new ToolkitParameterField(field, token, context, fieldIndex, mixed, changed);

        internal static void RefreshToolkitField(VisualElement row, string token,
            ParamContext context, bool mixed)
        {
            var field = row.Q<ToolkitParameterField>() ??
                        throw new InvalidOperationException(
                            "Parameter list row has no retained field.");
            field.Refresh(token, context, mixed);
        }

        private static VisualElement CreateToolkitControl(ParamField field, string token,
            Func<ParamContext> context, int fieldIndex, Action<ToolkitValue> changed)
        {
            var fallback = ResolveFallbackToken(in field, context(), fieldIndex);
            switch (field.kind)
            {
                case ParamKind.Float:
                {
                    if (!ParameterReader.ParseFloat(token.AsSpan(), out var value))
                        ParameterReader.ParseFloat(fallback.AsSpan(), out value);
                    if (field.hasRange)
                        return BindToolkit(new InspectorSlider(field.name, field.min, field.max), value,
                            FormatFloat,
                            next => changed(new ToolkitValue(next)));
                    return BindToolkit(new FloatField(field.name), value,
                        FormatFloat,
                        next => changed(new ToolkitValue(next)));
                }
                case ParamKind.Int:
                {
                    if (!ParameterReader.ParseInt(token.AsSpan(), out var value))
                        ParameterReader.ParseInt(fallback.AsSpan(), out value);
                    if (field.hasRange)
                        return BindToolkit(new InspectorSliderInt(
                                field.name, (int)field.min, (int)field.max),
                            value, FormatInt,
                            next => changed(new ToolkitValue(next)));
                    return BindToolkit(new IntegerField(field.name), value,
                        FormatInt,
                        next => changed(new ToolkitValue(next)));
                }
                case ParamKind.Color:
                {
                    if (!ColorParsing.TryParse(token, out var color) &&
                        !ColorParsing.TryParse(fallback, out color))
                        color = new Color32(255, 255, 255, 255);
                    return BindToolkit(new ColorField(field.name), (Color)color, next =>
                    {
                        var value = (Color32)next;
                        return FormatColor(value);
                    }, next => changed(new ToolkitValue(next)));
                }
                case ParamKind.Vector2:
                    return BindToolkit(new EditorVector2DragField(field.name),
                        ParseVector2(token, fallback), FormatVector2,
                        next => changed(new ToolkitValue(next)));
                case ParamKind.Bool:
                {
                    if (!ParameterTokenExtensions.TryParseBool(token.AsSpan(), out var value))
                        ParameterTokenExtensions.TryParseBool(fallback.AsSpan(), out value);
                    return BindToolkit(new InspectorToggle(field.name), value,
                        FormatBool,
                        next => changed(new ToolkitValue(next)));
                }
                case ParamKind.Enum:
                    return CreateToolkitEnum(field.name, token, field.spec,
                        fallback, () => context().owner,
                        next => changed(new ToolkitValue(next)));
                case ParamKind.Unit:
                    return CreateToolkitUnit(field, token, fallback,
                        next => changed(new ToolkitValue(next)));
                case ParamKind.UnitVec2:
                    return CreateToolkitUnitVector(field, token, fallback,
                        next => changed(new ToolkitValue(next)));
                case ParamKind.Variant:
                    return CreateToolkitVariant(field, token, context, fieldIndex, changed);
                default:
                    return BindToolkit(new TextField(field.name), token ?? string.Empty,
                        next => next, next => changed(new ToolkitValue(next)));
            }
        }

        internal static void SetToolkitMixedValue(VisualElement element, bool mixed)
        {
            switch (element)
            {
                case BaseField<float> field: field.showMixedValue = mixed; break;
                case BaseField<int> field: field.showMixedValue = mixed; break;
                case BaseField<Color> field: field.showMixedValue = mixed; break;
                case BaseField<Vector2> field: field.showMixedValue = mixed; break;
                case BaseField<bool> field: field.showMixedValue = mixed; break;
                case BaseField<string> field: field.showMixedValue = mixed; break;
            }
            for (var i = 0; i < element.childCount; i++)
                SetToolkitMixedValue(element.ElementAt(i), mixed);
        }

        internal static ToolkitValue ResolveToolkitValue(in ParamField field, string token,
            ParamContext context, int fieldIndex) => new(token,
            ResolveToolkitVariantIndex(in field, token, context, fieldIndex));

        internal static ToolkitValue ResolveComparableToolkitValue(in ParamField field,
            string token, ParamContext context, int fieldIndex)
        {
            var fallback = ResolveFallbackToken(in field, context, fieldIndex);
            var effective = ResolveToken(token, fallback);
            var variant = ResolveToolkitVariantIndex(in field, token, context, fieldIndex);
            if (context.owner == null || fieldIndex < 0)
                return new ToolkitValue(effective, variant);

            var member = GetParameterMembers(context.owner.GetType())[fieldIndex];
            var value = ParseParameterValue(member, in field, token, variant, fallback);
            return new ToolkitValue(FormatToken(member, value), variant);
        }

        private static int ResolveToolkitVariantIndex(in ParamField field, string token,
            ParamContext context, int fieldIndex)
        {
            if (field.kind != ParamKind.Variant) return -1;
            var options = ParseVariantSpec(field.spec);
            token = DecodeToken(token, out var isSet);
            if (!isSet)
                token = ResolveFallbackToken(in field, context, fieldIndex);
            var explicitIndex = context.VariantAt(fieldIndex);
            if (!isSet && explicitIndex < 0)
                explicitIndex = ResolveFallbackVariant(in field, context, fieldIndex);
            return options.Length == 0
                ? -1
                : ClassifyVariant(in field, options, token, context.owner,
                    explicitIndex);
        }

        /// <summary>
        /// Change stamp over the <c>@key</c> catalogs a field renders for the context's owner —
        /// zero when it names none. Two equal stamps mean the same dropdown content, so a control
        /// built from one stays valid under the other.
        /// </summary>
        private static int ResolveOptionsRevision(in ParamField field, ParamContext context)
        {
            switch (field.kind)
            {
                case ParamKind.Enum:
                    return SpecOptionsRevision(field.spec, context.owner);
                case ParamKind.Variant:
                {
                    var options = ParseVariantSpec(field.spec);
                    var revision = 0;
                    for (var i = 0; i < options.Length; i++)
                        if (options[i].isSubField && options[i].subFieldKind == ParamKind.Enum)
                            revision = HashCode.Combine(revision,
                                SpecOptionsRevision(options[i].subFieldSpec, context.owner));
                    return revision;
                }
                default:
                    return 0;
            }
        }

        private static int SpecOptionsRevision(string spec, object owner)
        {
            if (string.IsNullOrEmpty(spec) || spec[0] != '@' ||
                !TryGetRichOptionsCached(CatalogKey(spec), owner, out var options))
                return 0;
            var revision = options.Count;
            for (var i = 0; i < options.Count; i++)
                revision = HashCode.Combine(revision, options[i].value);
            return revision;
        }

        private static string MergeToolkitToken(ParamField field, ToolkitValue current,
            ToolkitValue previous, ToolkitValue next, ParamContext currentContext,
            ParamContext editContext, int fieldIndex, bool decoded = false)
        {
            var currentFallback = ResolveFallbackToken(in field, currentContext, fieldIndex);
            var editFallback = ResolveFallbackToken(in field, editContext, fieldIndex);
            var currentToken = decoded
                ? current.token
                : ResolveToken(current.token, currentFallback);
            var previousToken = decoded
                ? previous.token
                : ResolveToken(previous.token, editFallback);
            var nextToken = decoded
                ? next.token
                : ResolveToken(next.token, editFallback);
            string Finish(string value) => decoded
                ? value
                : EncodeToken(in field, value);
            switch (field.kind)
            {
                case ParamKind.Color:
                {
                    var currentColor = ParseColor(currentToken, currentFallback);
                    var previousColor = ParseColor(previousToken, editFallback);
                    var nextColor = ParseColor(nextToken, editFallback);
                    if (nextColor.r != previousColor.r) currentColor.r = nextColor.r;
                    if (nextColor.g != previousColor.g) currentColor.g = nextColor.g;
                    if (nextColor.b != previousColor.b) currentColor.b = nextColor.b;
                    if (nextColor.a != previousColor.a) currentColor.a = nextColor.a;
                    return Finish(FormatColor(currentColor));
                }
                case ParamKind.Vector2:
                {
                    var currentValue = ParseVector2(currentToken, currentFallback);
                    var previousValue = ParseVector2(previousToken, editFallback);
                    var nextValue = ParseVector2(nextToken, editFallback);
                    if (!nextValue.x.Equals(previousValue.x)) currentValue.x = nextValue.x;
                    if (!nextValue.y.Equals(previousValue.y)) currentValue.y = nextValue.y;
                    return Finish(FormatVector2(currentValue));
                }
                case ParamKind.Unit:
                    return Finish(MergeUnit(field, currentToken, previousToken, nextToken,
                        currentFallback, editFallback));
                case ParamKind.UnitVec2:
                    return Finish(MergeUnitVector(field, currentToken, previousToken, nextToken,
                        currentFallback, editFallback));
                case ParamKind.Variant:
                {
                    var options = ParseVariantSpec(field.spec);
                    if (options.Length == 0) return next.token;
                    var previousIndex = ClassifyVariant(in field, options, previousToken,
                        editContext.owner, previous.variantIndex);
                    var currentIndex = ClassifyVariant(in field, options, currentToken,
                        currentContext.owner, current.variantIndex);
                    var nextIndex = ClassifyVariant(in field, options, nextToken,
                        editContext.owner, next.variantIndex);
                    if (previousIndex != nextIndex || currentIndex != nextIndex ||
                        (uint)nextIndex >= (uint)options.Length)
                        return next.token;
                    var option = options[nextIndex];
                    if (!option.isSubField) return next.token;
                    return Finish(MergeToolkitToken(new ParamField
                    {
                        name = option.label,
                        kind = option.subFieldKind,
                        spec = option.subFieldSpec,
                        defaultValue = option.subFieldDefault,
                    }, new ToolkitValue(currentToken),
                        new ToolkitValue(previousToken),
                        new ToolkitValue(nextToken), currentContext, editContext, -1, true));
                }
                default:
                    return next.token;
            }
        }

        internal static string MergeParameterSegment(string segment, ParamField[] fields,
            int fieldIndex, ToolkitValue previous, ToolkitValue next,
            ParamContext currentContext, ParamContext editContext)
        {
            segment ??= string.Empty;
            var tokens = SplitParameter(segment);
            var currentToken = fieldIndex < tokens.Length ? tokens[fieldIndex] : string.Empty;
            var current = ResolveToolkitValue(
                in fields[fieldIndex], currentToken, currentContext, fieldIndex);
            return SetFieldToken(segment, fields, fieldIndex,
                MergeToolkitToken(fields[fieldIndex], current, previous, next,
                    currentContext, editContext, fieldIndex), currentContext);
        }

        internal static object MergeParameterValue(ParameterMember member, in ParamField field,
            object current, ToolkitValue previous, ToolkitValue next,
            ParamContext currentContext, ParamContext editContext, int fieldIndex)
        {
            var token = EncodeToken(in field, FormatToken(member, current));
            var currentValue = new ToolkitValue(token, ResolveVariantIndex(in field, current));
            var merged = MergeToolkitToken(field, currentValue, previous, next,
                currentContext, editContext, fieldIndex);
            return ParseParameterValue(member, in field, merged, next.variantIndex,
                ResolveFallbackToken(in field, currentContext, fieldIndex));
        }

        internal static object ParseParameterValue(ParameterMember member, in ParamField field,
            string token, ParamContext context = default, int fieldIndex = -1)
        {
            var fallback = ResolveFallbackToken(in field, context, fieldIndex);
            return ParseParameterValue(member, in field, token,
                ResolveToolkitValue(in field, token, context, fieldIndex).variantIndex,
                fallback);
        }

        private static object ParseParameterValue(ParameterMember member, in ParamField field,
            string token, int variantIndex, string fallback)
        {
            token = ResolveToken(token, fallback);
            var type = member.FieldType;
            if (typeof(IMarkupValue).IsAssignableFrom(type))
            {
                var value = (IMarkupValue)Activator.CreateInstance(type);
                value.FromToken(token);
                return ApplyVariantDiscriminator(in field, value, variantIndex);
            }
            if (type == typeof(Color32))
                return ParseColor(token, fallback);
            if (type == typeof(Color))
                return (Color)ParseColor(token, fallback);
            if (type == typeof(float))
                return ParameterReader.ParseFloat(token.AsSpan(), out var floatValue)
                    ? floatValue
                    : ParameterReader.ParseFloat(fallback.AsSpan(), out floatValue)
                        ? floatValue
                        : 0f;
            if (type == typeof(int))
                return ParameterReader.ParseInt(token.AsSpan(), out var intValue)
                    ? intValue
                    : ParameterReader.ParseInt(fallback.AsSpan(), out intValue)
                        ? intValue
                        : 0;
            if (type == typeof(bool))
                return ParameterTokenExtensions.TryParseBool(token.AsSpan(), out var boolValue)
                    ? boolValue
                    : ParameterTokenExtensions.TryParseBool(fallback.AsSpan(), out boolValue) && boolValue;
            if (type == typeof(Vector2))
                return ParseVector2(token, fallback);
            if (type.IsEnum)
            {
                if (!TryEnumFromToken(member, type, token, out var enumValue))
                    enumValue = EnumFromToken(member, type, fallback);
                return ApplyVariantDiscriminator(in field,
                    enumValue, variantIndex);
            }
            if (type == typeof(string)) return token ?? string.Empty;
            if (type == typeof(UnitValue))
            {
                ParseUnitDefault(fallback, out var fallbackValue, out var fallbackUnit);
                var reader = new ParameterReader(token);
                reader.NextUnitFloat(out var value, out var unit,
                    new UnitValue(fallbackValue, fallbackUnit));
                return new UnitValue(value, unit);
            }
            if (type == typeof(UnitVector2))
            {
                ParseUnitVectorDefault(fallback, out var fallbackValue, out var fallbackUnit);
                var reader = new ParameterReader(token);
                reader.NextUnitVector2(out var value, out var unit,
                    new UnitVector2(fallbackValue, fallbackUnit));
                return new UnitVector2(value, unit);
            }
            return token;
        }

        private static Color32 ParseColor(string value, string defaultValue)
        {
            if (ColorParsing.TryParse(value, out var color)) return color;
            return ColorParsing.TryParse(defaultValue, out color)
                ? color
                : new Color32(255, 255, 255, 255);
        }

        private static string MergeUnit(ParamField field, string current,
            string previous, string next, string currentFallback, string editFallback)
        {
            var spec = GetUnitSpec(field.spec);
            if (spec.entries.Length == 0) return next;
            var currentDefaultUnit = ParseDefaultUnit(currentFallback);
            var editDefaultUnit = ParseDefaultUnit(editFallback);
            ParseUnitValue(current, spec.names, currentDefaultUnit,
                out var currentValue, out var currentUnit);
            ParseUnitValue(previous, spec.names, editDefaultUnit,
                out var previousValue, out var previousUnit);
            ParseUnitValue(next, spec.names, editDefaultUnit,
                out var nextValue, out var nextUnit);
            if (currentUnit < 0)
                ParseUnitValue(currentFallback, spec.names, currentDefaultUnit,
                    out currentValue, out currentUnit);
            if (previousUnit < 0)
                ParseUnitValue(editFallback, spec.names, editDefaultUnit,
                    out previousValue, out previousUnit);
            if (nextUnit < 0)
                ParseUnitValue(editFallback, spec.names, editDefaultUnit,
                    out nextValue, out nextUnit);
            if (!nextValue.Equals(previousValue)) currentValue = nextValue;
            if (nextUnit != previousUnit) currentUnit = nextUnit;
            if (currentUnit < 0) currentUnit = 0;
            return SerializeUnitValue(currentValue, spec.names[currentUnit]);
        }

        private static string MergeUnitVector(ParamField field, string current,
            string previous, string next, string currentFallback, string editFallback)
        {
            var spec = GetUnitSpec(field.spec);
            if (spec.entries.Length == 0)
            {
                var currentVector = ParseVector2(current, currentFallback);
                var previousVector = ParseVector2(previous, editFallback);
                var nextVector = ParseVector2(next, editFallback);
                if (!nextVector.x.Equals(previousVector.x)) currentVector.x = nextVector.x;
                if (!nextVector.y.Equals(previousVector.y)) currentVector.y = nextVector.y;
                return FormatVector2(currentVector);
            }
            ParseUnitVectorDefault(currentFallback,
                out var currentDefaultValue, out var currentDefaultUnit);
            ParseUnitVectorDefault(editFallback,
                out var editDefaultValue, out var editDefaultUnit);
            ParseUnitVector2(current, spec.names, currentDefaultValue, currentDefaultUnit,
                out var currentValue, out var currentUnit);
            ParseUnitVector2(previous, spec.names, editDefaultValue, editDefaultUnit,
                out var previousValue, out var previousUnit);
            ParseUnitVector2(next, spec.names, editDefaultValue, editDefaultUnit,
                out var nextValue, out var nextUnit);
            if (currentUnit < 0)
                ParseUnitVector2(currentFallback, spec.names,
                    currentDefaultValue, currentDefaultUnit,
                    out currentValue, out currentUnit);
            if (previousUnit < 0)
                ParseUnitVector2(editFallback, spec.names,
                    editDefaultValue, editDefaultUnit,
                    out previousValue, out previousUnit);
            if (nextUnit < 0)
                ParseUnitVector2(editFallback, spec.names,
                    editDefaultValue, editDefaultUnit,
                    out nextValue, out nextUnit);
            if (!nextValue.x.Equals(previousValue.x)) currentValue.x = nextValue.x;
            if (!nextValue.y.Equals(previousValue.y)) currentValue.y = nextValue.y;
            if (nextUnit != previousUnit) currentUnit = nextUnit;
            if (currentUnit < 0) currentUnit = 0;
            return SerializeUnitVector2(currentValue, spec.names[currentUnit]);
        }

        private static TField BindToolkit<TField, TValue>(TField field, TValue value,
            Func<TValue, string> serialize, Action<string> changed)
            where TField : BaseField<TValue>
        {
            InspectorVisuals.Attach(field);
            field.SetValueWithoutNotify(value);
            InspectorVisuals.MarkFieldAxis(field);
            field.RegisterValueChangedCallback(evt => changed(serialize(evt.newValue)));
            return field;
        }

        private static VisualElement CreateToolkitEnum(string label, string token, string spec,
            string defaultValue, Func<object> owner, Action<string> changed)
        {
            var value = string.IsNullOrEmpty(token) ? defaultValue : token;
            if (!string.IsNullOrEmpty(spec) && spec[0] == '@')
            {
                var key = CatalogKey(spec);
                if (TryGetRichOptionsCached(key, owner(), out var rich))
                {
                    if (rich.Count == 0)
                        return BindToolkit(new TextField(label), token ?? string.Empty,
                            next => next, changed);
                    var selected = FindRichOption(rich, value);
                    var field = new SelectorField<string>(label,
                        selected < 0 ? UnlistedValue(value) : rich[selected].value,
                        () =>
                        {
                            if (!TryGetRichOptionsCached(key, owner(), out var current))
                                return Array.Empty<Selector.SelectorItem>();
                            return BuildRichSelectorItems(current);
                        });
                    InspectorVisuals.MarkFieldAxis(field);
                    field.RegisterValueChangedCallback(evt => changed(evt.newValue));
                    return field;
                }
            }

            var options = ResolveEnumOptions(spec);
            if (options == null || options.Length == 0)
                return BindToolkit(new TextField(label), token ?? string.Empty,
                    next => next, changed);
            var selectedIndex = ParameterTokenExtensions.TryMatchEnum(value.AsSpan(), options, out var parsed)
                ? parsed
                : -1;
            var selector = new SelectorField<string>(label,
                selectedIndex < 0 ? UnlistedValue(value) : options[selectedIndex],
                () => BuildSelectorItems(options));
            InspectorVisuals.MarkFieldAxis(selector);
            selector.RegisterValueChangedCallback(evt => changed(evt.newValue));
            return selector;
        }

        /// <summary>
        /// What a selector shows for a value its option set does not contain: the value itself, so
        /// the control never displays a value it does not hold, or the field's no-value presentation
        /// when there is none.
        /// </summary>
        private static string UnlistedValue(string value)
            => string.IsNullOrEmpty(value) ? null : value;

        private static VisualElement CreateToolkitUnit(ParamField field, string token,
            string fallback, Action<string> changed)
        {
            var spec = GetUnitSpec(field.spec);
            if (spec.entries.Length == 0)
                return BindToolkit(new TextField(field.name), token ?? string.Empty,
                    next => next, changed);
            var defaultUnit = ParseDefaultUnit(fallback);
            ParseUnitValue(token, spec.names, defaultUnit, out var value, out var unitIndex);
            if (unitIndex < 0)
            {
                ParseUnitValue(fallback, spec.names, defaultUnit, out value, out unitIndex);
                if (unitIndex < 0) unitIndex = 0;
            }

            var row = InspectorVisuals.CreateCompactRow();
            UniTextInspectorTheme.Initialize(row);
            var entry = spec.entries[unitIndex];
            var currentValue = value;
            var unit = new SelectorField<string>(spec.labels, unitIndex);
            unit.AddToClassList("unitext-inspector-field-row__unit");
            VisualElement number;
            if (entry.hasRange && entry.isInt)
            {
                var slider = new InspectorSliderInt(
                    field.name, (int)entry.min, (int)entry.max);
                slider.SetValueWithoutNotify(Mathf.RoundToInt(value));
                slider.RegisterValueChangedCallback(evt =>
                {
                    currentValue = evt.newValue;
                    Apply();
                });
                number = slider;
            }
            else if (entry.hasRange)
            {
                var slider = new InspectorSlider(field.name, entry.min, entry.max);
                slider.SetValueWithoutNotify(value);
                slider.RegisterValueChangedCallback(evt =>
                {
                    currentValue = evt.newValue;
                    Apply();
                });
                number = slider;
            }
            else
            {
                var input = new FloatField(field.name);
                input.SetValueWithoutNotify(value);
                input.RegisterValueChangedCallback(evt =>
                {
                    currentValue = evt.newValue;
                    Apply();
                });
                number = input;
            }
            InspectorVisuals.Attach(number);
            InspectorVisuals.MarkFieldAxis(number);
            number.AddToClassList("unitext-inspector-field-row__value");
            row.Add(number);
            row.Add(unit);
            void Apply()
            {
                changed(SerializeUnitValue(currentValue, spec.names[unit.Index]));
            }
            unit.RegisterValueChangedCallback(_ => Apply());
            return row;
        }

        private static VisualElement CreateToolkitUnitVector(ParamField field, string token,
            string fallback, Action<string> changed)
        {
            var spec = GetUnitSpec(field.spec);
            if (spec.entries.Length == 0)
                return BindToolkit(new EditorVector2DragField(field.name),
                    ParseVector2(token, fallback), FormatVector2, changed);
            ParseUnitVectorDefault(fallback, out var defaultValue, out var defaultUnit);
            ParseUnitVector2(token, spec.names, defaultValue, defaultUnit,
                out var value, out var unitIndex);
            if (unitIndex < 0)
            {
                ParseUnitVector2(fallback, spec.names, defaultValue, defaultUnit,
                    out value, out unitIndex);
                if (unitIndex < 0) unitIndex = 0;
            }
            var row = InspectorVisuals.CreateCompactRow();
            UniTextInspectorTheme.Initialize(row);
            var vector = new EditorVector2DragField(field.name);
            InspectorVisuals.MarkFieldAxis(vector);
            vector.AddToClassList("unitext-inspector-field-row__value");
            vector.SetValueWithoutNotify(value);
            var unit = new SelectorField<string>(spec.labels, unitIndex);
            unit.AddToClassList("unitext-inspector-field-row__unit");
            row.Add(vector);
            row.Add(unit);
            void Apply()
            {
                changed(SerializeUnitVector2(vector.value, spec.names[unit.Index]));
            }
            vector.RegisterValueChangedCallback(_ => Apply());
            unit.RegisterValueChangedCallback(_ => Apply());
            return row;
        }

        private static VisualElement CreateToolkitVariant(ParamField field, string token,
            Func<ParamContext> context, int fieldIndex, Action<ToolkitValue> changed)
        {
            var options = ParseVariantSpec(field.spec);
            if (options.Length == 0)
                return BindToolkit(new TextField(field.name), token ?? string.Empty,
                    next => next, next => changed(new ToolkitValue(next)));
            var root = InspectorVisuals.CreateStack();
            UniTextInspectorTheme.Initialize(root);
            var currentToken = token ?? string.Empty;
            var explicitIndex = context().VariantAt(fieldIndex);
            var labels = GetVariantLabels(field.spec, options);
            var selector = new SelectorField<string>(field.name, labels,
                ClassifyVariant(in field, options, currentToken, context().owner,
                    explicitIndex));
            InspectorVisuals.MarkFieldAxis(selector);
            root.Add(selector);
            var subFieldContainer = InspectorVisuals.CreateStack();
            subFieldContainer.style.display = DisplayStyle.None;
            root.Add(subFieldContainer);
            var renderedIndex = -1;

            void Refresh()
            {
                var currentIndex = ClassifyVariant(in field, options, currentToken,
                    context().owner, explicitIndex);
                selector.SetChoices(labels, currentIndex);
                if (currentIndex == renderedIndex) return;
                renderedIndex = currentIndex;
                InspectorVisuals.ClearContent(subFieldContainer);
                var current = options[currentIndex];
                subFieldContainer.style.display = current.isSubField
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                if (!current.isSubField) return;
                var subToken = current.subFieldKind == ParamKind.Enum ||
                               !string.IsNullOrEmpty(currentToken) &&
                               CanParseAs(current.subFieldKind, currentToken)
                    ? currentToken
                    : current.subFieldDefault;
                var subField = new ParamField
                {
                    name = current.label,
                    kind = current.subFieldKind,
                    spec = current.subFieldSpec,
                    defaultValue = current.subFieldDefault,
                };

                void Apply(string next)
                {
                    subToken = next;
                    currentToken = next;
                    changed(new ToolkitValue(next, explicitIndex));
                }

                var control = CreateToolkitControl(subField, subToken, context, -1,
                    next => Apply(next.token));
                control.AddManipulator(new InspectorContextMenuManipulator(
                    menu => AppendSubFieldClipboard(menu, in subField, subToken, Apply)));
                subFieldContainer.Add(control);
            }

            selector.RegisterValueChangedCallback(_ =>
            {
                var nextIndex = selector.Index;
                if ((uint)nextIndex >= (uint)options.Length) return;
                explicitIndex = nextIndex;
                var picked = options[nextIndex];
                currentToken = picked.isSubField
                    ? ResolveSubFieldInitialValue(picked, context().owner)
                    : picked.literalValue;
                changed(new ToolkitValue(currentToken, explicitIndex));
                Refresh();
            });
            Refresh();
            return root;
        }

        /// <summary>
        /// Clipboard commands for a variant's payload control, which joins the shared clipboard under
        /// the payload's own value type rather than the carrying parameter's.
        /// </summary>
        private static void AppendSubFieldClipboard(DropdownMenu menu, in ParamField subField,
            string token, Action<string> apply)
        {
            var value = ParseSubFieldValue(in subField, token);
            InspectorContextMenu.AppendCommand(menu, "Copy", InspectorContextMenu.CopyIcon,
                _ => PropertyClipboard.CopyObject(value));
            PropertyClipboardMenu.AppendPaste(menu, "Paste", value.GetType(),
                pasted => apply(FormatSubFieldToken(pasted)));
        }

        /// <summary>The payload a variant sub-field token holds, in the type its kind encodes.</summary>
        private static object ParseSubFieldValue(in ParamField field, string token) => field.kind switch
        {
            ParamKind.Color => ParseColor(token, field.defaultValue),
            ParamKind.Float => ParameterReader.ParseFloat(token.AsSpan(), out var number) ? number : 0f,
            ParamKind.Int => ParameterReader.ParseInt(token.AsSpan(), out var count) ? count : 0,
            ParamKind.Bool => ParameterTokenExtensions.TryParseBool(token.AsSpan(), out var flag) && flag,
            _ => token ?? string.Empty,
        };

        private static string FormatSubFieldToken(object value) => value switch
        {
            Color32 color => FormatColor(color),
            float number => FormatFloat(number),
            int count => FormatInt(count),
            bool flag => FormatBool(flag),
            _ => (string)value,
        };

        private static Vector2 ParseVector2(string token, string defaultValue)
        {
            var defaultReader = new ParameterReader(defaultValue);
            defaultReader.NextVector2(out var fallback);
            var reader = new ParameterReader(token);
            reader.NextVector2(out var value, fallback);
            return value;
        }

        private static string FormatVector2(Vector2 value)
            => $"{FormatFloat(value.x)} {FormatFloat(value.y)}";

        private static void ParseUnitVectorDefault(string token, out Vector2 value,
            out UnitKind unit)
        {
            var reader = new ParameterReader(token);
            reader.NextUnitVector2(out value, out unit,
                new UnitVector2(Vector2.zero, UnitKind.Absolute));
        }

        private static void ParseUnitDefault(string token, out float value, out UnitKind unit)
        {
            var reader = new ParameterReader(token);
            reader.NextUnitFloat(out value, out unit,
                new UnitValue(0f, UnitKind.Absolute));
        }

        private static void ParseUnitVector2(string token, string[] units, Vector2 fallback,
            UnitKind defaultUnit, out Vector2 value, out int unitIndex)
        {
            var reader = new ParameterReader(token);
            if (reader.NextUnitVector2(out value, out var unit,
                    new UnitVector2(fallback, defaultUnit)) &&
                (unitIndex = FindUnitIndex(units, unit)) >= 0)
                return;
            value = fallback;
            unitIndex = -1;
        }

        private static string SerializeUnitVector2(Vector2 value, string unit)
            => FormatFloat(value.x) + unit + " " + FormatFloat(value.y) + unit;

        private sealed class RichOptionsEntry
        {
            public readonly Dictionary<string, (int stamp, List<ParameterOption> options)> byKey = new();
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, RichOptionsEntry>
            richOptionsCaches = new();

        private static readonly RichOptionsEntry noModifierOptions = new();
        private static readonly Dictionary<string, string> catalogKeys = new(StringComparer.Ordinal);
        private static int richOptionsStamp;

        /// <summary>Freshness of a materialized catalog: editor changes and provider registrations both invalidate it, and neither counter ever decreases.</summary>
        private static int RichOptionsStamp => richOptionsStamp + ParameterProviders.Version;

        /// <summary>Provider catalog key an <c>@</c>-spec names; retained per spec, as option lookups run per repaint and per poll.</summary>
        private static string CatalogKey(string spec)
        {
            if (catalogKeys.TryGetValue(spec, out var key)) return key;
            key = spec.Substring(1);
            catalogKeys[spec] = key;
            return key;
        }

        [InitializeOnLoadMethod]
        private static void RegisterRichOptionsStamp()
        {
            ObjectChangeEvents.changesPublished += OnEditorObjectsChanged;
            EditorApplication.projectChanged += InvalidateRichOptions;
            Undo.undoRedoPerformed += InvalidateRichOptions;
        }

        private static void OnEditorObjectsChanged(ref ObjectChangeEventStream stream)
        {
            if (stream.length != 0) richOptionsStamp++;
        }

        private static void InvalidateRichOptions() => richOptionsStamp++;

        /// <summary>
        /// Provider catalogs are yield-enumerated with closures on every query, so the materialized
        /// list is reused until something that could have edited a catalog is reported: an object
        /// or project change, an undo, a provider registration. The stamp must never advance on its
        /// own — fields poll this to notice option-set changes, and a free-running stamp turns every
        /// poll into a full re-enumeration for as long as an inspector stays open.
        /// Keyed by the owner reference (weak table) — identity hash codes collide, and a collision
        /// would serve another owner's option list.
        /// </summary>
        private static bool TryGetRichOptionsCached(string key, object owner, out List<ParameterOption> options)
        {
            var byKey = (owner != null ? richOptionsCaches.GetOrCreateValue(owner) : noModifierOptions).byKey;
            var stamp = RichOptionsStamp;
            if (byKey.TryGetValue(key, out var entry) && entry.stamp == stamp)
            {
                options = entry.options;
                return options != null;
            }

            options = ParameterProviders.TryGetRichOptionsForOwner(key, owner, out var richOptions)
                ? richOptions as List<ParameterOption> ?? new List<ParameterOption>(richOptions)
                : null;
            byKey[key] = (stamp, options);
            return options != null;
        }

        private static int FindRichOption(List<ParameterOption> options, string value)
        {
            for (var i = 0; i < options.Count; i++)
                if (string.Equals(options[i].value, value, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static Selector.SelectorItem[] BuildSelectorItems(string[] options)
        {
            var items = new Selector.SelectorItem[options.Length];
            for (var i = 0; i < options.Length; i++)
                items[i] = new Selector.SelectorItem
                {
                    displayName = options[i],
                    searchText = options[i],
                    value = options[i],
                };
            return items;
        }

        private static Selector.SelectorItem[] BuildRichSelectorItems(List<ParameterOption> options)
        {
            var items = new Selector.SelectorItem[options.Count];
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                items[i] = new Selector.SelectorItem
                {
                    displayName = option.displayName,
                    value = option.value,
                    description = option.description,
                    createDecorator = option.createPreview,
                };
            }
            return items;
        }

        private static string[] ResolveEnumOptions(string spec)
        {
            if (spec.Length > 0 && spec[0] == '@')
            {
                var key = CatalogKey(spec);
                if (ParameterProviders.TryGetOptions(key, out var dynamicOptions))
                {
                    return new List<string>(dynamicOptions).ToArray();
                }
                return null;
            }
            return spec.Split('|');
        }

        private struct UnitEntry
        {
            public string name;
            public bool hasRange;
            public bool isInt;
            public float min, max;
        }

        private sealed class UnitSpec
        {
            public UnitEntry[] entries;
            public string[] names;
            public string[] labels;
        }

        private static readonly Dictionary<string, UnitSpec> unitSpecCache = new();

        private static UnitSpec GetUnitSpec(string spec)
        {
            if (unitSpecCache.TryGetValue(spec, out var cached)) return cached;

            var entries = ParseUnitEntries(spec);
            var names = new string[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                if (!UnitNames.TryKind(entries[i].name, out _))
                    throw new InvalidOperationException(
                        $"Unknown unit '{entries[i].name}' in [Unit(\"{spec}\")].");
                names[i] = entries[i].name;
            }

            var parsed = new UnitSpec { entries = entries, names = names, labels = GetUnitLabels(names) };
            unitSpecCache[spec] = parsed;
            return parsed;
        }

        /// <summary>Unit names of a <c>[Unit]</c> spec with per-unit range payloads stripped — the single unit-spec parser.</summary>
        internal static string[] GetUnitNames(string spec) =>
            string.IsNullOrEmpty(spec) ? Array.Empty<string>() : GetUnitSpec(spec).names;

        private static UnitEntry[] ParseUnitEntries(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return Array.Empty<UnitEntry>();

            var parts = spec.Split('|');
            var result = new UnitEntry[parts.Length];

            for (var i = 0; i < parts.Length; i++)
            {
                var entry = parts[i];
                var bracketOpen = entry.IndexOfAny(bracketChars);

                if (bracketOpen < 0)
                {
                    result[i].name = entry;
                    continue;
                }

                result[i].name = entry.Substring(0, bracketOpen);
                result[i].isInt = entry[bracketOpen] == '[';

                var bracketClose = entry.IndexOf(result[i].isInt ? ']' : ')', bracketOpen);
                if (bracketClose > bracketOpen)
                {
                    var inner = entry.AsSpan(bracketOpen + 1, bracketClose - bracketOpen - 1);
                    var comma = inner.IndexOf(',');
                    if (comma >= 0)
                    {
                        result[i].hasRange =
                            ParameterReader.ParseFloat(inner[..comma].Trim(), out result[i].min) &&
                            ParameterReader.ParseFloat(inner[(comma + 1)..].Trim(), out result[i].max);
                    }
                }
            }

            return result;
        }

        private static readonly char[] bracketChars = { '(', '[' };

        #endregion

        #region Variant

        private struct VariantOption
        {
            public string label;
            public bool isSubField;
            public string literalValue;
            public ParamKind subFieldKind;
            /// <summary>Payload for an enum sub-field (option list or <c>@key</c>); empty for primitive sub-fields.</summary>
            public string subFieldSpec;
            public string subFieldDefault;
        }

        private static readonly Dictionary<string, VariantOption[]> variantCache = new();
        private static readonly Dictionary<string, string[]> variantLabelCache = new();

        private static string[] GetVariantLabels(string spec, VariantOption[] options)
        {
            if (variantLabelCache.TryGetValue(spec, out var cached)) return cached;
            var labels = new string[options.Length];
            for (var i = 0; i < options.Length; i++) labels[i] = options[i].label;
            variantLabelCache[spec] = labels;
            return labels;
        }

        private static VariantOption[] ParseVariantSpec(string spec)
        {
            if (variantCache.TryGetValue(spec, out var cached)) return cached;

            var parts = spec.Split('|');
            var result = new VariantOption[parts.Length];

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var eqIdx = part.IndexOf('=');
                if (eqIdx < 0)
                {
                    result[i] = new VariantOption { label = part.Trim(), literalValue = "" };
                    continue;
                }

                var label = part.Substring(0, eqIdx).Trim();
                var rhs = part.Substring(eqIdx + 1);

                if (rhs.StartsWith("enum:"))
                {
                    result[i] = new VariantOption
                    {
                        label = label,
                        isSubField = true,
                        subFieldKind = ParamKind.Enum,
                        subFieldSpec = rhs.Substring("enum:".Length),
                        subFieldDefault = ""
                    };
                    continue;
                }

                var colonIdx = rhs.IndexOf(':');
                var typeName = colonIdx < 0 ? rhs : rhs.Substring(0, colonIdx);
                var defaultValue = colonIdx < 0 ? "" : rhs.Substring(colonIdx + 1);

                if (TryMapSubFieldKind(typeName, out var kind))
                {
                    result[i] = new VariantOption
                    {
                        label = label,
                        isSubField = true,
                        subFieldKind = kind,
                        subFieldSpec = "",
                        subFieldDefault = defaultValue
                    };
                }
                else
                {
                    result[i] = new VariantOption { label = label, literalValue = rhs };
                }
            }

            variantCache[spec] = result;
            return result;
        }

        private static bool TryMapSubFieldKind(string type, out ParamKind kind)
        {
            switch (type)
            {
                case "color": kind = ParamKind.Color; return true;
                case "float": kind = ParamKind.Float; return true;
                case "int": kind = ParamKind.Int; return true;
                case "bool": kind = ParamKind.Bool; return true;
                default: kind = ParamKind.String; return false;
            }
        }

        /// <summary>
        /// Picks the initial token value when the user switches the variant popup to a sub-field
        /// option. For sub-fields with an explicit default, returns it verbatim; for enum sub-fields
        /// without a default, queries the provider for the first available option so the new token
        /// resolves back to <em>this</em> variant option on the next frame (otherwise an empty token
        /// would silently revert the popup to the first sub-field whose <c>CanParseAs</c> accepts
        /// empty input — usually the wrong one).
        /// </summary>
        private static string ResolveSubFieldInitialValue(VariantOption opt, object owner)
        {
            if (!string.IsNullOrEmpty(opt.subFieldDefault))
                return opt.subFieldDefault;

            if (opt.subFieldKind == ParamKind.Enum)
            {
                var spec = opt.subFieldSpec ?? "";
                if (spec.Length > 0 && spec[0] == '@')
                {
                    var key = CatalogKey(spec);
                    if (ParameterProviders.TryGetRichOptionsForOwner(key, owner, out var rich) && rich != null)
                    {
                        foreach (var o in rich)
                            if (!string.IsNullOrEmpty(o.value))
                                return o.value;
                    }
                }
                else if (spec.Length > 0)
                {
                    var pipeIdx = spec.IndexOf('|');
                    return pipeIdx < 0 ? spec : spec.Substring(0, pipeIdx);
                }
            }

            return "";
        }

        /// <summary>Classifies token-only variants without letting broad scalar grammar steal literals, reserved hex colours, or enum/provider values.</summary>
        private static int FindVariantIndex(VariantOption[] options, string token,
            object owner = null)
        {
            if (string.IsNullOrEmpty(token))
            {
                for (var i = 0; i < options.Length; i++)
                    if (!options[i].isSubField && string.IsNullOrEmpty(options[i].literalValue))
                        return i;
                return 0;
            }

            for (var i = 0; i < options.Length; i++)
                if (!options[i].isSubField && options[i].literalValue.EqualsIgnoreCase(token))
                    return i;

            if (token[0] == '#')
                for (var i = 0; i < options.Length; i++)
                    if (options[i].isSubField && options[i].subFieldKind == ParamKind.Color)
                        return i;

            var enumFallback = -1;
            for (var i = 0; i < options.Length; i++)
            {
                if (!options[i].isSubField || options[i].subFieldKind != ParamKind.Enum) continue;
                if (enumFallback < 0) enumFallback = i;
                if (MatchesEnumOption(in options[i], token, owner))
                    return i;
            }

            if (enumFallback >= 0)
                return enumFallback;

            for (var i = 0; i < options.Length; i++)
                if (options[i].isSubField && options[i].subFieldKind != ParamKind.Enum &&
                    CanParseAs(options[i].subFieldKind, token))
                    return i;

            return 0;
        }

        /// <summary>Uses the field's actual markup grammar when available, then falls back to the variant DSL heuristic.</summary>
        private static int ClassifyVariant(in ParamField field, VariantOption[] options,
            string token, object owner, int explicitIndex = -1)
        {
            if ((uint)explicitIndex < (uint)options.Length)
                return explicitIndex;

            if (field.variantValues != null && field.valueType != null &&
                typeof(IMarkupValue).IsAssignableFrom(field.valueType))
            {
                var value = (IMarkupValue)Activator.CreateInstance(field.valueType);
                value.FromToken(token);
                var runtimeIndex = ResolveVariantIndex(in field, value);
                if ((uint)runtimeIndex < (uint)options.Length)
                    return runtimeIndex;
            }

            return FindVariantIndex(options, token, owner);
        }

        private static bool MatchesEnumOption(in VariantOption option, string token, object owner)
        {
            var spec = option.subFieldSpec ?? "";
            if (spec.Length > 0 && spec[0] == '@')
            {
                if (!TryGetRichOptionsCached(CatalogKey(spec), owner, out var options)) return false;
                for (var i = 0; i < options.Count; i++)
                    if (string.Equals(options[i].value, token, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }

            var values = spec.Split('|');
            return ParameterTokenExtensions.TryMatchEnum(token.AsSpan(), values, out _);
        }

        private static bool CanParseAs(ParamKind kind, string token)
        {
            switch (kind)
            {
                case ParamKind.Color: return ColorParsing.TryParse(token, out _);
                case ParamKind.Float: return ParameterReader.ParseFloat(token.AsSpan(), out _);
                case ParamKind.Int:   return ParameterReader.ParseInt(token.AsSpan(), out _);
                case ParamKind.Bool:  return ParameterTokenExtensions.TryParseBool(token.AsSpan(), out _);
                default: return false;
            }
        }

        #endregion

        #region Unit Parsing

        private static UnitKind ParseDefaultUnit(string token)
        {
            ParseUnitDefault(token, out _, out var unit);
            return unit;
        }

        private static void ParseUnitValue(string token, string[] units, UnitKind defaultUnit,
            out float value, out int unitIndex)
        {
            var reader = new ParameterReader(token);
            if (reader.NextUnitFloat(out value, out var unit,
                    new UnitValue(0f, defaultUnit)) &&
                (unitIndex = FindUnitIndex(units, unit)) >= 0)
                return;
            value = 0f;
            unitIndex = -1;
        }

        /// <summary>Token of an explicitly chosen unit entry: the value with the entry's own suffix — a bare number would re-parse in the surrounding context's unit, not the chosen one.</summary>
        private static string SerializeUnitValue(float value, string unit)
            => FormatFloat(value) + unit;

        /// <summary>The vocabulary's own spelling of a unit — <c>px</c> and <c>abs</c> share one <see cref="UnitKind"/>.</summary>
        internal static string NameFor(UnitKind value, string[] names)
        {
            for (var i = 0; i < names.Length; i++)
                if (UnitNames.TryKind(names[i], out var kind) && kind == value)
                    return names[i];
            return UnitNames.Name(value);
        }

        internal static int FindUnitIndex(IReadOnlyList<string> units, UnitKind unit)
        {
            var name = UnitNames.Name(unit);
            for (var i = 0; i < units.Count; i++)
                if (string.Equals(units[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            if (unit == UnitKind.Delta) unit = UnitKind.Absolute;
            if (unit != UnitKind.Absolute) return -1;
            for (var i = 0; i < units.Count; i++)
                if (UnitNames.Kind(units[i]) == UnitKind.Absolute) return i;
            return -1;
        }

        private static string[] GetUnitLabels(string[] units)
        {
            var labels = new string[units.Length];
            for (var i = 0; i < units.Length; i++)
                labels[i] = UnitNames.Kind(units[i]) == UnitKind.Delta ? "±" : units[i];
            return labels;
        }

        #endregion

        #region Parameter Serialization

        private static readonly BoundedMemo<string, string[]> splitCache = new(512);

        /// <summary>
        /// Split + trim of a comma-separated parameter, cached by the parameter string — this runs for
        /// every parameter block on every repaint. The returned array is SHARED: treat it as read-only;
        /// any mutation must clone first (see the copy-on-write in the draw loops).
        /// </summary>
        internal static string[] SplitParameter(string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return Array.Empty<string>();

            if (splitCache.TryGetValue(parameter, out var cached))
                return cached;

            var parts = SplitSyntax(parameter, ',');

            splitCache[parameter] = parts;
            return parts;
        }

        private static string[] SplitSyntax(string value, char separator)
        {
            if (string.IsNullOrEmpty(value)) return Array.Empty<string>();

            var result = new List<string>();
            var remaining = value.AsSpan();
            bool hadSeparator;
            do
            {
                var consumed = ParameterTokenizer.Next(remaining, separator, out var token, out hadSeparator);
                remaining = remaining.Slice(consumed);
                result.Add(token.ToString());
            }
            while (hadSeparator);

            return result.ToArray();
        }

        internal static string JoinParameter(string[] tokens, ParamField[] fields,
            ParamContext context = default)
        {
            var last = tokens.Length - 1;
            while (last >= 0)
            {
                var isEmpty = string.IsNullOrEmpty(tokens[last]);
                var isHidden = fields != null && last < fields.Length &&
                               !IsFieldVisible(in fields[last], tokens, fields, context);
                if (!isEmpty && !isHidden) break;
                last--;
            }

            if (last < 0)
                return "";

            if (last == 0)
                return tokens[0];

            return string.Join(",", tokens, 0, last + 1);
        }

        #endregion
    }
}
