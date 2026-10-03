using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>One entry of an opt-in parameter list: the relative property it edits, and the default its row hides at and resets to.</summary>
    public readonly struct OptInField
    {
        /// <summary>Relative property path from the list's owner, dots allowed.</summary>
        public readonly string Path;

        /// <summary>Row and menu label.</summary>
        public readonly string Label;

        /// <summary>Value the field rests at: the row hides while the property equals it, and the row's remove action writes it back.</summary>
        public readonly object Default;

        /// <summary>Compact type qualifier shown in the menu.</summary>
        public readonly string Hint;

        /// <summary>Optional explanation for the row and menu.</summary>
        public readonly string Tooltip;

        public OptInField(string path, string label, object defaultValue, string hint,
            string tooltip = null)
        {
            Path = path;
            Label = label;
            Default = defaultValue;
            Hint = hint;
            Tooltip = tooltip;
        }
    }

    /// <summary>
    /// The opt-in parameter list behind <see cref="OptInParameterAttribute"/>: marked serialized fields stay out
    /// of an inspector body until their value differs from the type's default or the author adds them from the
    /// list's menu; a row's remove action writes the default back and hides it again. Rows render through the
    /// standard field pipeline, so every field attribute keeps its own drawer. A drawer can also hand in an
    /// explicit <see cref="OptInField"/> schema for values the attribute cannot mark, such as members of a
    /// nested struct.
    /// </summary>
    public static class OptInParameters
    {
        private static readonly Dictionary<Type, OptInField[]> schemaCache = new();
        private static readonly OptInListState state = new("LightSide.Core.OptInParameter.");

        /// <summary>The marked field names of <paramref name="ownerType"/>, in declaration order — what a body drawer excludes from its always-visible section.</summary>
        public static IReadOnlyList<string> ParameterNames(Type ownerType)
        {
            var schema = Schema(ownerType);
            var names = new string[schema.Length];
            for (var i = 0; i < schema.Length; i++) names[i] = schema[i].Path;
            return names;
        }

        /// <summary>
        /// Creates the "Parameters" list over the marked fields of <paramref name="ownerType"/> for the object at
        /// <paramref name="owner"/>, or <see langword="null"/> when the type marks none.
        /// </summary>
        public static VisualElement CreateList(SerializedProperty owner, Type ownerType)
        {
            var schema = Schema(ownerType);
            return schema.Length == 0 ? null : new OptInParameterList(owner, schema, "Parameters");
        }

        /// <summary>Creates a list over an explicit schema, labelled <paramref name="label"/>, or <see langword="null"/> for an empty schema.</summary>
        public static VisualElement CreateList(SerializedProperty owner,
            IReadOnlyList<OptInField> fields, string label)
        {
            if (fields == null || fields.Count == 0) return null;
            var schema = new OptInField[fields.Count];
            for (var i = 0; i < fields.Count; i++) schema[i] = fields[i];
            return new OptInParameterList(owner, schema, label);
        }

        /// <summary>
        /// Builds an explicit schema whose defaults are read from <paramref name="defaults"/> — a
        /// default-constructed instance of the owner type — through each entry's relative field path.
        /// A null string default is normalized to empty, matching what serialization stores.
        /// </summary>
        public static OptInField[] SchemaFrom(object defaults,
            IReadOnlyList<(string path, string label)> fields)
        {
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            var schema = new OptInField[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                var (path, label) = fields[i];
                var value = ResolveDefault(defaults, path, out var fieldType);
                if (value == null && fieldType == typeof(string)) value = string.Empty;
                schema[i] = new OptInField(path, label, value, TypeHint(fieldType));
            }
            return schema;
        }

        private static object ResolveDefault(object instance, string path, out Type fieldType)
        {
            var current = instance;
            var type = instance.GetType();
            fieldType = null;
            foreach (var segment in path.Split('.'))
            {
                var field = FindField(type, segment) ??
                            throw new ArgumentException(
                                $"{instance.GetType().Name} declares no field path '{path}'.");
                fieldType = field.FieldType;
                type = field.FieldType;
                current = current == null ? null : field.GetValue(current);
            }
            return current;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static OptInField[] Schema(Type ownerType)
        {
            if (schemaCache.TryGetValue(ownerType, out var cached)) return cached;

            var fields = new List<FieldInfo>();
            Collect(ownerType, fields);
            var schema = new OptInField[fields.Count];
            if (fields.Count > 0)
            {
                var defaults = Activator.CreateInstance(ownerType, nonPublic: true);
                for (var i = 0; i < fields.Count; i++)
                {
                    var value = fields[i].GetValue(defaults);
                    if (value == null && fields[i].FieldType == typeof(string)) value = string.Empty;
                    schema[i] = new OptInField(
                        fields[i].Name,
                        ObjectNames.NicifyVariableName(fields[i].Name),
                        value,
                        TypeHint(fields[i].FieldType),
                        fields[i].GetCustomAttribute<TooltipAttribute>()?.tooltip);
                }
            }
            schemaCache[ownerType] = schema;
            return schema;
        }

        private static void Collect(Type type, List<FieldInfo> into)
        {
            if (type == null || type == typeof(object)) return;
            Collect(type.BaseType, into);
            var declared = type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            Array.Sort(declared, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            foreach (var field in declared)
                if (field.IsDefined(typeof(OptInParameterAttribute), false))
                    into.Add(field);
        }

        private static string TypeHint(Type type)
        {
            if (type == typeof(float) || type == typeof(double)) return "float";
            if (type == typeof(int) || type == typeof(long)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(Vector2)) return "vec2";
            if (type == typeof(Vector3)) return "vec3";
            if (type == typeof(Vector4)) return "vec4";
            if (type == typeof(Color) || type == typeof(Color32)) return "color";
            if (type.IsEnum) return "enum";
            return type.Name.ToLowerInvariant();
        }

        /// <summary>Value equality that treats NaN as equal to NaN, so a NaN sentinel default can hide its row — boxed floats already compare that way, vectors do not.</summary>
        private static bool ValuesEqual(object value, object other)
        {
            if (value is Vector2 v2 && other is Vector2 o2)
                return v2.x.Equals(o2.x) && v2.y.Equals(o2.y);
            if (value is Vector3 v3 && other is Vector3 o3)
                return v3.x.Equals(o3.x) && v3.y.Equals(o3.y) && v3.z.Equals(o3.z);
            if (value is Vector4 v4 && other is Vector4 o4)
                return v4.x.Equals(o4.x) && v4.y.Equals(o4.y) && v4.z.Equals(o4.z) && v4.w.Equals(o4.w);
            return Equals(value, other);
        }

        private sealed class OptInParameterList : VisualElement
        {
            private readonly SerializedPropertyBinding binding;
            private readonly OptInField[] schema;
            private readonly SerializedPropertyBinding[] memberBindings;
            private readonly string key;
            private readonly List<int> visibleRows = new();
            private readonly InspectorListView list;

            public OptInParameterList(SerializedProperty owner, OptInField[] schema, string label)
            {
                binding = new SerializedPropertyBinding(owner);
                this.schema = schema;
                memberBindings = new SerializedPropertyBinding[schema.Length];
                var observed = new SerializedProperty[schema.Length + 1];
                observed[0] = owner;
                for (var i = 0; i < schema.Length; i++)
                {
                    var member = InspectorHelpers.RequireRelative(owner, schema[i].Path);
                    memberBindings[i] = new SerializedPropertyBinding(member);
                    observed[i + 1] = member;
                }
                key = OptInListState.StateKey(owner) + "/" + label;
                state.RestoreRevealed(key, schema.Length);
                InspectorVisuals.Attach(this);
                list = new InspectorListView(label, MakeRow, BindRow,
                    InspectorVisuals.ClearContent, false, RowIdentity);
                Add(list);
                list.ExpandedChanged += value => state.SetExpanded(key, value);
                list.ClearRequested += ResetParameters;
                var add = list.Header.AddButton;
                add.clicked += () => OpenParameterSelector(add.worldBound);
                SerializedPropertyField.Observe(this, Refresh, observed);
            }

            private static VisualElement MakeRow() => InspectorVisuals.CreateParameterRow();

            private object RowIdentity(int displayIndex) => visibleRows[displayIndex];

            private bool Differs(int index)
                => memberBindings[index].HasMultipleValues ||
                   !ValuesEqual(memberBindings[index].Value, schema[index].Default);

            private void Refresh()
            {
                if (binding.FindSerializedProperty() == null) return;
                visibleRows.Clear();
                for (var i = 0; i < schema.Length; i++)
                    if (Differs(i) || state.IsRevealed(key, i))
                        visibleRows.Add(i);
                list.Rebuild(visibleRows.Count, state.GetExpanded(key));
            }

            private void BindRow(VisualElement row, int displayIndex)
            {
                var owner = binding.FindSerializedProperty();
                if (owner == null) return;
                var index = visibleRows[displayIndex];
                var member = InspectorHelpers.RequireRelative(owner, schema[index].Path);
                var field = SerializedPropertyField.Create(member, schema[index].Label);
                field.tooltip = schema[index].Tooltip;
                field.AddToClassList(InspectorVisuals.TooltipOwnerClass);
                field.AddToClassList(InspectorVisuals.ParameterFieldClass);
                SerializedPropertyField.OnChange(field, () =>
                {
                    if (Differs(index)) state.SetRevealed(key, index, true);
                }, member);
                row.Add(field);
                row.Add(InspectorListView.CreateRemoveButton(
                    () => { ResetField(index); Refresh(); },
                    $"Reset {schema[index].Label}"));
            }

            /// <summary>Opens the parameter toggle selector: checked entries are the rows the list currently shows; choosing one reveals it or resets it back to the default and hides it.</summary>
            private void OpenParameterSelector(Rect rect)
            {
                var items = new Selector.SelectorItem[schema.Length];
                for (var i = 0; i < schema.Length; i++)
                {
                    var hint = schema[i].Hint;
                    items[i] = new Selector.SelectorItem
                    {
                        displayName = schema[i].Label,
                        description = schema[i].Tooltip,
                        createDecorator = () => Selector.CreateTypeHint(hint),
                        value = i,
                    };
                }
                Selector.ShowMultiple(rect, items,
                    value => value is int index && visibleRows.Contains(index),
                    value =>
                    {
                        if (value is not int index) return;
                        if (visibleRows.Contains(index))
                        {
                            ResetField(index);
                        }
                        else
                        {
                            state.SetRevealed(key, index, true);
                            state.SetExpanded(key, true);
                        }
                        Refresh();
                        InternalEditorUtility.RepaintAllViews();
                    });
            }

            private void ResetField(int index)
            {
                state.SetRevealed(key, index, false);
                memberBindings[index].SetValue(schema[index].Default, "Reset " + schema[index].Label);
            }

            private void ResetParameters()
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Reset Parameters");
                var group = Undo.GetCurrentGroup();
                for (var i = 0; i < schema.Length; i++)
                    ResetField(i);
                Undo.CollapseUndoOperations(group);
                Refresh();
            }
        }
    }
}
