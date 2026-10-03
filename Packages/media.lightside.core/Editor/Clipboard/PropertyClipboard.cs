using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>How a clipboard entry is measured against, and written into, a serialized destination.</summary>
    internal enum ClipboardTargetKind : byte
    {
        /// <summary>One serialized value is replaced; only single-value entries apply.</summary>
        Value,

        /// <summary>A collection's contents are replaced by the entry's values.</summary>
        CollectionReplace,

        /// <summary>The entry's values are appended to a collection.</summary>
        CollectionAppend,
    }

    /// <summary>One serialized destination that a paste is offered for.</summary>
    internal readonly struct ClipboardTarget
    {
        internal ClipboardTarget(Type type, ClipboardTargetKind kind,
            IReadOnlyList<object> currentValues = null)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Kind = kind;
            CurrentValues = currentValues;
        }

        /// <summary>Declared serialized type: a collection's element type, or the value's own type.</summary>
        internal Type Type { get; }

        internal ClipboardTargetKind Kind { get; }

        /// <summary>
        /// Current value of every selected target. Consulted only by an element paste adapter that
        /// restricts which existing values it may replace; null skips that restriction.
        /// </summary>
        internal IReadOnlyList<object> CurrentValues { get; }
    }

    /// <summary>One copied set of serialized values, detached from the property they came from.</summary>
    internal sealed class ClipboardEntry
    {
        internal ClipboardEntry(Type type, Type declaredType, object[] values, bool collection)
        {
            Type = type;
            DeclaredType = declaredType;
            Values = values;
            IsCollection = collection;
        }

        /// <summary>Type shared by every copied value, or the source's declared type when they share none.</summary>
        internal Type Type { get; private set; }

        /// <summary>Element type of the property copied from; the compatibility gate survives editing.</summary>
        internal Type DeclaredType { get; }

        /// <summary>Whether the entry came from a collection, and so reads back as one.</summary>
        internal bool IsCollection { get; }

        internal object[] Values { get; private set; }

        /// <summary>Replaces the copied values after the preview edited them, including their count.</summary>
        internal void SetValues(object[] values)
        {
            Values = values;
            Type = PropertyClipboard.CommonType(values, DeclaredType);
        }

        internal int Count => Values.Length;

        /// <summary>Derived on demand; a cached label goes stale when the previewed value is edited.</summary>
        internal InspectorLabel Label
        {
            get
            {
                if (Values.Length == 1) return ElementLabelUtility.Compose(Values[0]);
                var name = ManagedReferenceTypeMenu.TrimSuffix(Type.Name, Type);
                return ElementLabelUtility.Compose($"{name} ×{Values.Length}", Type);
            }
        }
    }

    /// <summary>
    /// Shared editor clipboard for detached copies of Unity-serializable managed objects. Copies
    /// accumulate: each one adds an entry, and copying a value equal to a stored one moves that entry
    /// to the front instead of duplicating it. Entries live until the editor reloads its assemblies.
    /// </summary>
    public static class PropertyClipboard
    {
        private static readonly List<ClipboardEntry> entries = new();
        private static readonly Dictionary<Type, Action<object>> duplicateFinalizers = new();
        private static readonly Dictionary<Type, Action<object>> resolvedDuplicateFinalizers = new();
        private static readonly Dictionary<Type, ElementPasteAdapter> elementPasteAdapters = new();

        /// <summary>Adds a detached copy of <paramref name="source"/> to the clipboard; null adds nothing.</summary>
        public static void CopyObject(object source)
        {
            if (source == null) return;
            Push(new[] { CloneObject(source) }, source.GetType(), false);
        }

        /// <summary>
        /// Adds detached copies of several values as one entry, so they paste together and stay one
        /// clipboard item. <paramref name="declaredType"/> is the element type they were copied as.
        /// </summary>
        public static void CopyObjects(IReadOnlyList<object> sources, Type declaredType)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (declaredType == null) throw new ArgumentNullException(nameof(declaredType));
            if (sources.Count == 0) return;
            var values = new object[sources.Count];
            for (var i = 0; i < sources.Count; i++) values[i] = CloneObject(sources[i]);
            Push(values, declaredType, true);
        }

        /// <summary>Whether the clipboard holds one or more values assignable to <paramref name="expectedType"/>.</summary>
        public static bool CanPasteObjects(Type expectedType)
            => expectedType != null && NewestAssignableSet(expectedType) != null;

        /// <summary>
        /// Creates detached copies of every value in the most recent entry assignable to
        /// <paramref name="expectedType"/> — one for a single copied value, all of them for a set.
        /// </summary>
        public static object[] PasteObjects(Type expectedType)
        {
            if (expectedType == null) throw new ArgumentNullException(nameof(expectedType));
            var entry = NewestAssignableSet(expectedType) ??
                        throw new InvalidOperationException(
                            $"The clipboard holds no value compatible with '{expectedType.FullName}'.");
            var result = new object[entry.Values.Length];
            for (var i = 0; i < result.Length; i++)
                result[i] = SerializedPropertyBinding.Coerce(expectedType,
                    CloneObject(entry.Values[i]));
            return result;
        }

        /// <summary>Whether the clipboard holds a value assignable to <paramref name="expectedType"/>.</summary>
        public static bool CanPasteObject(Type expectedType)
            => expectedType != null && NewestAssignable(expectedType) != null;

        /// <summary>Creates a detached copy of the most recent value assignable to <paramref name="expectedType"/>.</summary>
        public static object PasteObject(Type expectedType)
        {
            if (expectedType == null) throw new ArgumentNullException(nameof(expectedType));
            var entry = NewestAssignable(expectedType) ??
                        throw new InvalidOperationException(
                            $"The clipboard holds no value compatible with '{expectedType.FullName}'.");
            return SerializedPropertyBinding.Coerce(expectedType, CloneObject(entry.Values[0]));
        }

        /// <summary>Clones Unity-serialized managed state while preserving Unity object references.</summary>
        public static object CloneObject(object source)
            => CloneObject(source, new Dictionary<object, object>(
                ReferenceIdentityComparer<object>.Instance));

        /// <summary>Registers the post-clone step for duplicated managed references assignable to <typeparamref name="T"/>.</summary>
        public static void RegisterDuplicateFinalizer<T>(Action<T> finalizer)
        {
            if (finalizer == null) throw new ArgumentNullException(nameof(finalizer));
            duplicateFinalizers[typeof(T)] = value => finalizer((T)value);
            resolvedDuplicateFinalizers.Clear();
        }

        /// <summary>
        /// Registers how a copied element replaces the content of an inline wrapper and how it creates
        /// a new wrapper element, making a source type pasteable where it is not otherwise assignable.
        /// Both callbacks receive a detached copy of the clipboard value, and the wrapper returned by
        /// <paramref name="insert"/> is itself detached before it is serialized, so either may be
        /// built through the live API. <paramref name="canReplace"/> restricts which existing wrappers
        /// may be overwritten.
        /// </summary>
        public static void RegisterElementPasteAdapter<TSource, TTarget>(
            Action<SerializedProperty, TSource> replace,
            Func<TSource, TTarget> insert, Predicate<TTarget> canReplace = null)
            where TSource : class
            where TTarget : class
        {
            if (replace == null) throw new ArgumentNullException(nameof(replace));
            if (insert == null) throw new ArgumentNullException(nameof(insert));
            var sourceType = typeof(TSource);
            var targetType = typeof(TTarget);
            if (elementPasteAdapters.ContainsKey(targetType))
                throw new InvalidOperationException(
                    $"An element paste adapter for '{targetType.FullName}' is already registered.");

            elementPasteAdapters.Add(targetType, new ElementPasteAdapter(
                sourceType,
                (property, value) => replace(property, (TSource)value),
                value => insert((TSource)value),
                canReplace == null
                    ? null
                    : value => value is TTarget target && canReplace(target)));
        }

        #region Entries

        /// <summary>Copies one serialized value, most recent entry first.</summary>
        internal static void Copy(SerializedProperty property, Type type)
            => Push(new[] { Capture(property, type) }, type, false);

        /// <summary>Copies every element of a serialized collection as one entry, empty collections included.</summary>
        internal static void CopyCollection(SerializedProperty property, Type elementType)
        {
            var values = new object[property.arraySize];
            for (var i = 0; i < values.Length; i++)
                values[i] = Capture(property.GetArrayElementAtIndex(i), elementType);
            Push(values, elementType, true);
        }

        /// <summary>Entries that <paramref name="target"/> accepts, most recent first.</summary>
        internal static List<ClipboardEntry> Compatible(in ClipboardTarget target)
        {
            var result = new List<ClipboardEntry>();
            for (var i = 0; i < entries.Count; i++)
                if (Accepts(target, entries[i]))
                    result.Add(entries[i]);
            return result;
        }

        /// <summary>Writes a detached copy of <paramref name="entry"/> into <paramref name="target"/>.</summary>
        internal static void Paste(SerializedProperty property, in ClipboardTarget target,
            ClipboardEntry entry)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (target.Kind == ClipboardTargetKind.Value)
            {
                Write(property, target.Type, entry.Values[0], true);
                Promote(entry);
                return;
            }
            if (target.Kind == ClipboardTargetKind.CollectionReplace) property.ClearArray();
            for (var i = 0; i < entry.Values.Length; i++)
            {
                var index = property.arraySize;
                property.InsertArrayElementAtIndex(index);
                Write(property.GetArrayElementAtIndex(index), target.Type, entry.Values[i], false);
            }
            Promote(entry);
        }

        /// <summary>A detached copy of <paramref name="entry"/>'s value, in <paramref name="type"/>'s own encoding.</summary>
        internal static object TakeValue(Type type, ClipboardEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            var value = CloneObject(entry.Values[0]);
            FinalizeDuplicate(value);
            Promote(entry);
            return SerializedPropertyBinding.Coerce(type, value);
        }

        /// <summary>Moves a used entry to the front, so the clipboard reads most-recently-used first.</summary>
        private static void Promote(ClipboardEntry entry)
        {
            var index = entries.IndexOf(entry);
            if (index <= 0) return;
            entries.RemoveAt(index);
            entries.Insert(0, entry);
        }

        /// <summary>
        /// Inserts a deep copy of one array element after it: managed state is cloned with Unity
        /// object references preserved, and registered duplicate finalizers run on the copy.
        /// </summary>
        public static void DuplicateElement(SerializedProperty arrayProp, int index)
        {
            var source = arrayProp.GetArrayElementAtIndex(index);
            if (source.propertyType == SerializedPropertyType.ManagedReference)
            {
                var duplicate = CloneObject(source.managedReferenceValue);
                FinalizeDuplicate(duplicate);
                var newIndex = index + 1;
                arrayProp.InsertArrayElementAtIndex(newIndex);
                arrayProp.GetArrayElementAtIndex(newIndex).managedReferenceValue = duplicate;
                return;
            }
            if (source.propertyType == SerializedPropertyType.Generic)
            {
                var duplicate = CloneObject(source.boxedValue);
                FinalizeDuplicate(duplicate);
                var newIndex = index + 1;
                arrayProp.InsertArrayElementAtIndex(newIndex);
                arrayProp.GetArrayElementAtIndex(newIndex).boxedValue = duplicate;
                return;
            }

            var path = $"{arrayProp.propertyPath}[{index}]";
            if (!source.DuplicateCommand())
                throw new InvalidOperationException(
                    $"Serialized element '{path}' could not be duplicated.");
        }

        private static object Capture(SerializedProperty property, Type type)
            => CloneObject(SerializedPropertyBinding.Read(property, type));

        private static void Push(object[] values, Type declaredType, bool collection)
        {
            var type = CommonType(values, declaredType);
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Type != type || !ValuesEqual(entries[i].Values, values)) continue;
                var existing = entries[i];
                entries.RemoveAt(i);
                entries.Insert(0, existing);
                return;
            }
            entries.Insert(0, new ClipboardEntry(type, declaredType, values, collection));
        }

        private static bool Accepts(in ClipboardTarget target, ClipboardEntry entry)
        {
            if (target.Kind == ClipboardTargetKind.Value && entry.Count != 1) return false;
            if (SerializedPropertyBinding.Accepts(target.Type, entry.Type)) return true;
            if (!TryGetElementPasteAdapter(entry.Type, target.Type, out var adapter)) return false;
            if (target.Kind != ClipboardTargetKind.Value || target.CurrentValues == null) return true;
            for (var i = 0; i < target.CurrentValues.Count; i++)
                if (!adapter.CanReplace(target.CurrentValues[i])) return false;
            return true;
        }

        private static ClipboardEntry NewestAssignable(Type type)
        {
            for (var i = 0; i < entries.Count; i++)
                if (entries[i].Count == 1 &&
                    SerializedPropertyBinding.Accepts(type, entries[i].Type))
                    return entries[i];
            return null;
        }

        /// <summary>Newest entry of any size whose values are assignable to <paramref name="type"/>.</summary>
        private static ClipboardEntry NewestAssignableSet(Type type)
        {
            for (var i = 0; i < entries.Count; i++)
                if (entries[i].Count > 0 &&
                    SerializedPropertyBinding.Accepts(type, entries[i].Type))
                    return entries[i];
            return null;
        }

        private static void Write(SerializedProperty property, Type type, object payload,
            bool replace)
        {
            var value = CloneObject(payload);
            FinalizeDuplicate(value);
            if (value != null && !type.IsInstanceOfType(value) &&
                TryGetElementPasteAdapter(value.GetType(), type, out var adapter))
            {
                if (replace)
                {
                    adapter.Replace(property, value);
                    return;
                }
                value = adapter.Insert(value) ?? throw new InvalidOperationException(
                    $"Element paste adapter for '{type.FullName}' returned null.");
            }
            SerializedPropertyBinding.Write(property, value, type);
        }

        internal static Type CommonType(object[] values, Type declaredType)
        {
            Type common = null;
            for (var i = 0; i < values.Length; i++)
            {
                var type = values[i]?.GetType();
                if (type == null) return declaredType;
                if (i == 0)
                {
                    common = type;
                    continue;
                }
                common = CommonBase(common, type);
                if (common == null) return declaredType;
            }
            return common != null && declaredType.IsAssignableFrom(common) ? common : declaredType;
        }

        private static Type CommonBase(Type left, Type right)
        {
            while (left != null && !left.IsAssignableFrom(right)) left = left.BaseType;
            return left;
        }

        private static bool ValuesEqual(object[] left, object[] right)
        {
            if (left.Length != right.Length) return false;
            var visited = new HashSet<object>(ReferenceIdentityComparer<object>.Instance);
            for (var i = 0; i < left.Length; i++)
                if (!SerializedEquals(left[i], right[i], visited)) return false;
            return true;
        }

        /// <summary>
        /// Compares two detached serialized graphs field by field. Unity object references, curves and
        /// gradients compare by identity, so a false negative only costs one extra clipboard entry.
        /// </summary>
        private static bool SerializedEquals(object left, object right, HashSet<object> visited)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            var type = left.GetType();
            if (type != right.GetType()) return false;
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) ||
                type == typeof(decimal)) return left.Equals(right);
            if (left is UnityEngine.Object || left is AnimationCurve ||
                left is UnityEngine.Gradient) return false;
            if (!type.IsValueType && !visited.Add(left)) return true;

            if (left is IList leftList)
            {
                var rightList = (IList)right;
                if (leftList.Count != rightList.Count) return false;
                for (var i = 0; i < leftList.Count; i++)
                    if (!SerializedEquals(leftList[i], rightList[i], visited)) return false;
                return true;
            }

            for (var current = type; current != null; current = current.BaseType)
            {
                var fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (var i = 0; i < fields.Length; i++)
                {
                    if (!SerializedReflection.IsSerializedField(fields[i])) continue;
                    if (!SerializedEquals(fields[i].GetValue(left), fields[i].GetValue(right),
                            visited)) return false;
                }
            }
            return true;
        }

        #endregion

        #region Helpers

        private readonly struct ElementPasteAdapter
        {
            public readonly Type SourceType;
            public readonly Action<SerializedProperty, object> Replace;
            public readonly Func<object, object> Insert;
            private readonly Predicate<object> canReplace;

            public ElementPasteAdapter(Type sourceType,
                Action<SerializedProperty, object> replace, Func<object, object> insert,
                Predicate<object> canReplace)
            {
                SourceType = sourceType;
                Replace = replace;
                Insert = insert;
                this.canReplace = canReplace;
            }

            public bool CanReplace(object target)
                => canReplace == null || canReplace(target);
        }

        private static bool TryGetElementPasteAdapter(Type sourceType, Type targetType,
            out ElementPasteAdapter adapter)
        {
            adapter = default;
            return sourceType != null && targetType != null &&
                   elementPasteAdapters.TryGetValue(targetType, out adapter) &&
                   adapter.SourceType.IsAssignableFrom(sourceType);
        }

        private static void FinalizeDuplicate(object value)
            => FinalizeDuplicate(value, new HashSet<object>(
                ReferenceIdentityComparer<object>.Instance));

        private static void FinalizeDuplicate(object value, HashSet<object> visited)
        {
            if (value == null) return;
            var valueType = value.GetType();
            if (valueType.IsPrimitive || valueType.IsEnum || valueType == typeof(string) ||
                valueType == typeof(decimal) || value is UnityEngine.Object ||
                value is AnimationCurve || value is UnityEngine.Gradient)
                return;
            if (!valueType.IsValueType && !visited.Add(value)) return;

            var finalizer = ResolveDuplicateFinalizer(valueType);
            if (finalizer != null)
            {
                finalizer(value);
                return;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    FinalizeDuplicate(item, visited);
                return;
            }

            for (var current = valueType; current != null; current = current.BaseType)
            {
                var fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (var i = 0; i < fields.Length; i++)
                    if (SerializedReflection.IsSerializedField(fields[i]))
                        FinalizeDuplicate(fields[i].GetValue(value), visited);
            }
        }

        private static Action<object> ResolveDuplicateFinalizer(Type valueType)
        {
            if (resolvedDuplicateFinalizers.TryGetValue(valueType, out var resolved))
                return resolved;
            Type finalizerType = null;
            foreach (var pair in duplicateFinalizers)
            {
                if (!pair.Key.IsAssignableFrom(valueType)) continue;
                if (finalizerType == null || finalizerType.IsAssignableFrom(pair.Key))
                {
                    finalizerType = pair.Key;
                    resolved = pair.Value;
                }
                else if (!pair.Key.IsAssignableFrom(finalizerType))
                {
                    throw new InvalidOperationException(
                        $"Duplicate finalizers '{finalizerType.FullName}' and '{pair.Key.FullName}' are both applicable to '{valueType.FullName}'.");
                }
            }
            resolvedDuplicateFinalizers[valueType] = resolved;
            return resolved;
        }

        private static object CloneObject(object source, Dictionary<object, object> clones)
        {
            if (source == null) return null;
            var sourceType = source.GetType();
            if (sourceType.IsPrimitive || sourceType.IsEnum || sourceType == typeof(string) ||
                sourceType == typeof(decimal) || source is UnityEngine.Object)
                return source;
            if (source is AnimationCurve curve)
                return new AnimationCurve(curve.keys)
                {
                    preWrapMode = curve.preWrapMode,
                    postWrapMode = curve.postWrapMode,
                };
            if (source is UnityEngine.Gradient gradient)
                return new UnityEngine.Gradient
                {
                    mode = gradient.mode,
                    colorKeys = gradient.colorKeys,
                    alphaKeys = gradient.alphaKeys,
                };
            if (!sourceType.IsValueType && clones.TryGetValue(source, out var existing))
                return existing;

            if (source is Array array)
            {
                var clone = Array.CreateInstance(sourceType.GetElementType(), array.Length);
                clones[source] = clone;
                for (var i = 0; i < array.Length; i++)
                    clone.SetValue(CloneObject(array.GetValue(i), clones), i);
                return clone;
            }

            if (source is IList list)
            {
                var clone = (IList)Activator.CreateInstance(sourceType, true);
                clones[source] = clone;
                for (var i = 0; i < list.Count; i++) clone.Add(CloneObject(list[i], clones));
                return clone;
            }

            var result = Activator.CreateInstance(sourceType, true);
            if (!sourceType.IsValueType) clones[source] = result;
            for (var current = sourceType; current != null; current = current.BaseType)
            {
                var fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (var i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (!SerializedReflection.IsSerializedField(field)) continue;
                    field.SetValue(result, CloneObject(field.GetValue(source), clones));
                }
            }
            return result;
        }

        #endregion
    }
}
