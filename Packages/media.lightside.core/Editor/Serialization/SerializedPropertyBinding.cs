using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>A prefab an override can be written into.</summary>
    public readonly struct PrefabApplyTarget
    {
        internal PrefabApplyTarget(string assetPath, string name, bool recordsOverride)
        {
            AssetPath = assetPath;
            Name = name;
            RecordsOverride = recordsOverride;
        }

        /// <summary>Project path of the prefab asset.</summary>
        public string AssetPath { get; }

        /// <summary>Name of the prefab's root GameObject.</summary>
        public string Name { get; }

        /// <summary>Whether applying here stores an override in this prefab rather than changing the value it defines.</summary>
        public bool RecordsOverride { get; }
    }

    /// <summary>
    /// Provides custom inspector controls with typed access to one Unity serialized property.
    /// All persisted writes, including list reordering, use <see cref="SerializedProperty"/> so
    /// Unity remains authoritative for Undo, prefab overrides, multi-object editing, persistence,
    /// and the generated host-level reconciliation boundary.
    /// </summary>
    public sealed class SerializedPropertyBinding
    {
        internal static event Action<IReadOnlyCollection<Object>> Written;

        private readonly SerializedObject serializedObject;
        private readonly string propertyPath;
        private readonly string displayName;
        private readonly FieldInfo serializedField;
        private readonly Type valueType;

        /// <summary>Creates a serialized binding for the supplied property path.</summary>
        public SerializedPropertyBinding(SerializedProperty property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            serializedObject = property.serializedObject;
            propertyPath = property.propertyPath;
            displayName = property.displayName;
            ResolveMetadata(serializedObject.targetObject, propertyPath, out serializedField,
                out valueType);
        }

        /// <summary>The serialized object that owns the persisted field.</summary>
        public SerializedObject SerializedObject => serializedObject;

        /// <summary>The original serialized field path.</summary>
        public string PropertyPath => propertyPath;

        /// <summary>Unity's display name for the serialized field.</summary>
        public string DisplayName => displayName;

        /// <summary>The exact serialized value type represented by the path.</summary>
        public Type ValueType => valueType;

        /// <summary>The backing serialized field used for attributes and other inspector metadata.</summary>
        public FieldInfo SerializedField => serializedField;

        /// <summary>The primary target's current serialized value.</summary>
        public object Value => ReadValue(serializedObject.targetObject,
            RequireSerializedProperty());

        /// <summary>Whether selected targets currently contain different serialized values.</summary>
        public bool HasMultipleValues => RequireSerializedProperty().hasMultipleDifferentValues;

        /// <summary>Whether a projection of this value differs across the selected targets.</summary>
        public bool HaveDifferentValues<T>(Func<object, T> select)
        {
            if (select == null) throw new ArgumentNullException(nameof(select));
            var targets = serializedObject.targetObjects;
            return targets.Length > 1 && !TryGetCommonValue(select, out _);
        }

        /// <summary>Reads a projection shared by every selected target.</summary>
        public bool TryGetCommonValue<T>(Func<object, T> select, out T value)
        {
            if (select == null) throw new ArgumentNullException(nameof(select));
            var targets = serializedObject.targetObjects;
            if (targets.Length == 0)
            {
                value = default;
                return false;
            }

            value = select(GetValue(targets[0]));
            var comparer = EqualityComparer<T>.Default;
            for (var i = 1; i < targets.Length; i++)
                if (!comparer.Equals(value, select(GetValue(targets[i])))) return false;
            return true;
        }

        /// <summary>Whether this property is overridden on any selected prefab instance.</summary>
        public bool HasPrefabOverrides
        {
            get => AnyTargetProperty((_, property) => property.prefabOverride);
        }

        /// <summary>Reverts this property on every selected prefab instance where it is overridden.</summary>
        public void RevertPrefabOverrides()
        {
            var targets = serializedObject.targetObjects;
            var changed = false;
            for (var i = 0; i < targets.Length; i++)
            {
                var stateOwner = FindSerializedStateOwner(targets[i], propertyPath);
                SerializedStateEditorDriver.PrepareSerializedEdit(targets[i], stateOwner);
                var state = new SerializedObject(targets[i]);
                state.UpdateIfRequiredOrScript();
                var property = state.FindProperty(propertyPath) ??
                               throw MissingProperty(targets[i]);
                if (!property.prefabOverride) continue;
                PrefabUtility.RevertPropertyOverride(
                    property, InteractionMode.UserAction);
                changed = true;
                SerializedStateEditorDriver.QueueSerializedWrite(targets[i]);
            }
            if (changed) SerializedStateEditorDriver.DrainPendingHosts();
            serializedObject.SetIsDifferentCacheDirty();
            serializedObject.UpdateIfRequiredOrScript();
            if (changed) Written?.Invoke(targets);
        }

        /// <summary>
        /// Prefabs this property's override can be written into, ordered from the prefab the
        /// instance was created from outward to the one that defines the value. Prefabs that cannot
        /// receive an apply are omitted, as is every target while more than one object is selected
        /// or the selected one is itself an asset.
        /// </summary>
        public IReadOnlyList<PrefabApplyTarget> GetPrefabApplyTargets()
        {
            var applyTargets = new List<PrefabApplyTarget>();
            var targets = serializedObject.targetObjects;
            if (targets.Length != 1 || EditorUtility.IsPersistent(targets[0])) return applyTargets;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(targets[0]);
            while (source != null)
            {
                var owner = source as GameObject ?? (source as Component)?.gameObject;
                if (owner == null) break;
                var root = owner.transform.root.gameObject;
                var next = PrefabUtility.GetCorrespondingObjectFromSource(source);
                if (PrefabUtility.IsPartOfPrefabThatCanBeAppliedTo(root))
                    applyTargets.Add(new PrefabApplyTarget(
                        AssetDatabase.GetAssetPath(source), root.name, next != null));
                source = next;
            }
            return applyTargets;
        }

        /// <summary>
        /// Writes this property's value from every selected prefab instance into the prefab asset at
        /// <paramref name="assetPath"/>, which must be one of <see cref="GetPrefabApplyTargets"/>.
        /// </summary>
        public void ApplyPrefabOverrides(string assetPath)
        {
            if (assetPath == null) throw new ArgumentNullException(nameof(assetPath));
            var targets = serializedObject.targetObjects;
            for (var i = 0; i < targets.Length; i++)
            {
                var stateOwner = FindSerializedStateOwner(targets[i], propertyPath);
                SerializedStateEditorDriver.PrepareSerializedEdit(targets[i], stateOwner);
                var state = new SerializedObject(targets[i]);
                state.UpdateIfRequiredOrScript();
                var property = state.FindProperty(propertyPath) ??
                               throw MissingProperty(targets[i]);
                PrefabUtility.ApplyPropertyOverride(
                    property, assetPath, InteractionMode.UserAction);
                SerializedStateEditorDriver.QueueSerializedWrite(targets[i]);
            }
            SerializedStateEditorDriver.DrainPendingHosts();
            serializedObject.SetIsDifferentCacheDirty();
            serializedObject.UpdateIfRequiredOrScript();
            Written?.Invoke(targets);
        }

        /// <summary>Reads this property path from one selected root object.</summary>
        public object GetValue(Object target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var targets = serializedObject.targetObjects;
            if (targets.Length == 1 && ReferenceEquals(targets[0], target))
                return ReadValue(target, RequireSerializedProperty());
            var targetState = new SerializedObject(target);
            targetState.UpdateIfRequiredOrScript();
            var property = targetState.FindProperty(propertyPath) ??
                           throw MissingProperty(target);
            return ReadValue(target, property);
        }

        /// <summary>Writes equivalent, independently owned values to every selected target.</summary>
        public void SetValue(object value, string undoName = null)
            => SetValue(_ => value, undoName);

        /// <summary>Writes a target-specific value to every selected target through Unity serialization.</summary>
        public void SetValue(Func<Object, object> valueFactory, string undoName = null)
        {
            if (valueFactory == null) throw new ArgumentNullException(nameof(valueFactory));
            EditTargets(undoName ?? $"Change {displayName}", false,
                (target, property) => Write(property, valueFactory(target), valueType));
        }

        /// <summary>Transforms each selected target's current value within the active serialized pass.</summary>
        public void TransformValue(Func<object, object> transform, string undoName = null)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            EditTargets(undoName ?? $"Change {displayName}", false,
                (target, property) =>
                    Write(property, transform(ReadValue(target, property)), valueType));
        }

        /// <summary>Transforms each selected target's current value with access to its owning root object.</summary>
        public void TransformValue(Func<Object, object, object> transform, string undoName = null)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            EditTargets(undoName ?? $"Change {displayName}", false,
                (target, property) =>
                    Write(property, transform(target, ReadValue(target, property)), valueType));
        }

        /// <summary>Edits this serialized property independently on every selected target.</summary>
        public void EditSerializedProperties(Action<SerializedProperty> edit,
            string undoName = null)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            EditTargets(undoName ?? $"Change {displayName}", true,
                (_, property) => edit(property));
        }

        /// <summary>Edits within Unity's current Undo group for a continuous interaction.</summary>
        public void EditSerializedPropertiesInCurrentUndoGroup(
            Action<SerializedProperty> edit, string undoName = null)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            EditTargets(undoName ?? $"Change {displayName}", false,
                (_, property) => edit(property));
        }

        /// <summary>Reacquires the serialized property after a structural edit or Undo/Redo.</summary>
        public SerializedProperty FindSerializedProperty()
        {
            if (!SerializedPropertyField.IsSerializedObjectCurrent(serializedObject))
                serializedObject.UpdateIfRequiredOrScript();
            return serializedObject.FindProperty(propertyPath);
        }

        /// <summary>
        /// Reacquires the serialized property and fails with target context when its required path
        /// is unavailable.
        /// </summary>
        public SerializedProperty RequireSerializedProperty() =>
            FindSerializedProperty() ?? throw MissingProperty(serializedObject.targetObject);

        /// <summary>
        /// Reads the bound property from every selected target. A property is valid only for the
        /// duration of the callback. A single target is visited through the bound serialized object;
        /// several targets are each read independently and no modified state is applied.
        /// </summary>
        public void VisitTargetProperties(Action<Object, SerializedProperty> visitor)
        {
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            VisitTargetProperties((target, property) =>
            {
                visitor(target, property);
                return false;
            });
        }

        /// <summary>
        /// Returns whether a predicate matches the bound property on any selected target, stopping
        /// at the first match. No modified state is applied.
        /// </summary>
        public bool AnyTargetProperty(Func<Object, SerializedProperty, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return VisitTargetProperties(predicate);
        }

        internal bool TryGetContainingCollection(out SerializedPropertyBinding collection,
            out int index)
        {
            index = FinalCollectionIndex(propertyPath, out var collectionPath);
            if (index < 0)
            {
                collection = null;
                return false;
            }

            if (!SerializedPropertyField.IsSerializedObjectCurrent(serializedObject))
                serializedObject.UpdateIfRequiredOrScript();
            var property = serializedObject.FindProperty(collectionPath) ??
                           throw new InvalidOperationException(
                               $"Serialized collection '{collectionPath}' does not exist on '{serializedObject.targetObject.name}'.");
            collection = new SerializedPropertyBinding(property);
            return true;
        }

        /// <summary>Resolves the live object located at a Unity serialized property path.</summary>
        public static object ResolveInstance(object root, string propertyPath)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            return string.IsNullOrEmpty(propertyPath)
                ? root
                : InspectorHelpers.ResolveInstance(root, propertyPath) ??
                  throw new InvalidOperationException(
                      $"Serialized path '{propertyPath}' cannot be resolved on '{root.GetType().FullName}'.");
        }

        private InvalidOperationException MissingProperty(Object target) =>
            new($"Serialized property '{propertyPath}' does not exist on '{target.name}'.");

        private void EditTargets(string undoName, bool incrementUndo,
            Action<Object, SerializedProperty> edit)
        {
            if (incrementUndo) Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            var changed = false;
            var targets = serializedObject.targetObjects;
            foreach (var target in targets)
            {
                var stateOwner = FindSerializedStateOwner(target, propertyPath);
                SerializedStateEditorDriver.PrepareSerializedEdit(target, stateOwner);
                var targetState = new SerializedObject(target);
                targetState.UpdateIfRequiredOrScript();
                var property = targetState.FindProperty(propertyPath) ??
                               throw MissingProperty(target);
                edit(target, property);
                if (targetState.ApplyModifiedProperties())
                {
                    changed = true;
                    SerializedStateEditorDriver.QueueSerializedWrite(target);
                }
            }
            Undo.CollapseUndoOperations(group);
            if (changed) SerializedStateEditorDriver.DrainPendingHosts();
            serializedObject.SetIsDifferentCacheDirty();
            serializedObject.UpdateIfRequiredOrScript();
            if (changed) Written?.Invoke(targets);
        }

        private static IEditorSerializedStateOwner FindSerializedStateOwner(
            Object target, string path)
        {
            while (true)
            {
                path = ParentPath(path);
                var value = string.IsNullOrEmpty(path)
                    ? target
                    : InspectorHelpers.ResolveInstance(target, path);
                if (value is IEditorSerializedStateOwner owner) return owner;
                if (string.IsNullOrEmpty(path)) return null;
            }
        }

        private static string ParentPath(string path)
        {
            const string element = ".Array.data[";
            var collection = path.LastIndexOf(element, StringComparison.Ordinal);
            if (collection >= 0 && path[path.Length - 1] == ']' &&
                path.IndexOf('.', collection + element.Length) < 0)
                return path.Substring(0, collection);
            var separator = path.LastIndexOf('.');
            return separator < 0 ? string.Empty : path.Substring(0, separator);
        }

        private bool VisitTargetProperties(Func<Object, SerializedProperty, bool> visitor)
        {
            var targets = serializedObject.targetObjects;
            if (targets.Length == 1) return visitor(targets[0], RequireSerializedProperty());
            for (var i = 0; i < targets.Length; i++)
            {
                var state = new SerializedObject(targets[i]);
                state.UpdateIfRequiredOrScript();
                var property = state.FindProperty(propertyPath) ?? throw MissingProperty(targets[i]);
                if (visitor(targets[i], property)) return true;
            }
            return false;
        }

        private object ReadValue(Object target, SerializedProperty property) =>
            IsCollectionProperty(property, valueType)
                ? ResolveInstance(target, propertyPath)
                : Read(property, valueType);

        private static bool IsCollectionProperty(SerializedProperty property, Type type) =>
            property.isArray && type != typeof(string);

        internal static void ResolveMetadata(object target, string path, out FieldInfo field,
            out Type type)
        {
            var elementIndex = FinalCollectionIndex(path, out var collectionPath);
            if (elementIndex >= 0) path = collectionPath;
            var separator = path.LastIndexOf('.');
            var fieldName = separator < 0 ? path : path.Substring(separator + 1);
            var ownerPath = separator < 0 ? string.Empty : path.Substring(0, separator);
            var owner = string.IsNullOrEmpty(ownerPath)
                ? target
                : ResolveInstance(target, ownerPath);
            field = SerializedReflection.FindField(owner.GetType(), fieldName) ??
                    throw new InvalidOperationException(
                        $"Serialized field '{owner.GetType().FullName}.{fieldName}' does not exist.");
            type = elementIndex < 0
                ? field.FieldType
                : SerializedReflection.GetCollectionElementType(field.FieldType) ??
                  throw new InvalidOperationException(
                      $"Serialized collection '{field.FieldType.FullName}' must be an array or List<T>.");
        }

        internal static object Read(SerializedProperty property, Type type)
        {
            if (type == typeof(Gradient)) return ReadGradient(property);
            return property.propertyType switch
            {
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Boolean => property.boolValue,
                SerializedPropertyType.Integer when type == typeof(LayerMask) =>
                    (LayerMask)property.intValue,
                SerializedPropertyType.Integer when type == typeof(uint) =>
                    unchecked((uint)property.longValue),
                SerializedPropertyType.Integer when type == typeof(long) => property.longValue,
                SerializedPropertyType.Integer when type.IsEnum => Enum.ToObject(type, property.intValue),
                SerializedPropertyType.Integer => property.intValue,
                SerializedPropertyType.Enum => Enum.ToObject(type, property.intValue),
                SerializedPropertyType.Float when type == typeof(double) => property.doubleValue,
                SerializedPropertyType.Float => property.floatValue,
                SerializedPropertyType.Color when type == typeof(Color32) => (Color32)property.colorValue,
                SerializedPropertyType.Color => property.colorValue,
                SerializedPropertyType.ObjectReference => property.objectReferenceValue,
                SerializedPropertyType.ManagedReference => property.managedReferenceValue,
                SerializedPropertyType.Vector2 => property.vector2Value,
                SerializedPropertyType.Vector3 => property.vector3Value,
                SerializedPropertyType.Vector4 => property.vector4Value,
                SerializedPropertyType.Vector2Int => property.vector2IntValue,
                SerializedPropertyType.Vector3Int => property.vector3IntValue,
                SerializedPropertyType.Rect => property.rectValue,
                SerializedPropertyType.RectInt => property.rectIntValue,
                SerializedPropertyType.Bounds => property.boundsValue,
                SerializedPropertyType.BoundsInt => property.boundsIntValue,
                SerializedPropertyType.AnimationCurve => property.animationCurveValue,
                SerializedPropertyType.Quaternion => property.quaternionValue,
                _ => property.boxedValue,
            };
        }

        internal static void Write(SerializedProperty property, object value, Type type)
        {
            ValidateValue(type, value);
            if (type == typeof(Gradient))
            {
                WriteGradient(property, (Gradient)value);
                return;
            }
            if (IsCollectionProperty(property, type))
            {
                var values = value as IList ?? throw new InvalidOperationException(
                    $"Serialized array '{property.propertyPath}' requires an IList value.");
                property.arraySize = values.Count;
                var elementType = SerializedReflection.GetCollectionElementType(type) ??
                                  throw new InvalidOperationException(
                                      $"Serialized collection '{type.FullName}' must be an array or List<T>.");
                for (var i = 0; i < values.Count; i++)
                    Write(property.GetArrayElementAtIndex(i), values[i], elementType);
                return;
            }
            switch (property.propertyType)
            {
                case SerializedPropertyType.String:
                    property.stringValue = (string)value;
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = (bool)value;
                    break;
                case SerializedPropertyType.Integer when type == typeof(long):
                    property.longValue = (long)value;
                    break;
                case SerializedPropertyType.Integer when type == typeof(LayerMask):
                    property.intValue = ((LayerMask)value).value;
                    break;
                case SerializedPropertyType.Integer when type == typeof(uint):
                    property.longValue = (uint)value;
                    break;
                case SerializedPropertyType.Integer when type.IsEnum:
                    property.intValue = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Integer:
                    property.intValue = (int)value;
                    break;
                case SerializedPropertyType.Enum:
                    property.intValue = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Float when type == typeof(double):
                    property.doubleValue = (double)value;
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = (float)value;
                    break;
                case SerializedPropertyType.Color:
                    property.colorValue = value is Color32 color32 ? color32 : (Color)value;
                    break;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = (Object)value;
                    break;
                case SerializedPropertyType.ManagedReference:
                    property.managedReferenceValue = PropertyClipboard.CloneObject(value);
                    break;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = (Vector2)value;
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = (Vector3)value;
                    break;
                case SerializedPropertyType.Vector4:
                    property.vector4Value = (Vector4)value;
                    break;
                case SerializedPropertyType.Vector2Int:
                    property.vector2IntValue = (Vector2Int)value;
                    break;
                case SerializedPropertyType.Vector3Int:
                    property.vector3IntValue = (Vector3Int)value;
                    break;
                case SerializedPropertyType.Rect:
                    property.rectValue = (Rect)value;
                    break;
                case SerializedPropertyType.RectInt:
                    property.rectIntValue = (RectInt)value;
                    break;
                case SerializedPropertyType.Bounds:
                    property.boundsValue = (Bounds)value;
                    break;
                case SerializedPropertyType.BoundsInt:
                    property.boundsIntValue = (BoundsInt)value;
                    break;
                case SerializedPropertyType.AnimationCurve:
                    property.animationCurveValue = (AnimationCurve)value;
                    break;
                case SerializedPropertyType.Quaternion:
                    property.quaternionValue = (Quaternion)value;
                    break;
                default:
                    property.boxedValue = PropertyClipboard.CloneObject(value);
                    break;
            }
        }

        private static Gradient ReadGradient(SerializedProperty property)
        {
            var stopsProperty = InspectorHelpers.RequireRelative(property, "stops");
            var interpolationProperty = InspectorHelpers.RequireRelative(property, "interpolation");
            var stops = new GradientStop[stopsProperty.arraySize];
            for (var i = 0; i < stops.Length; i++)
            {
                var stop = stopsProperty.GetArrayElementAtIndex(i);
                stops[i] = new GradientStop(
                    InspectorHelpers.RequireRelative(stop, "time").floatValue,
                    InspectorHelpers.RequireRelative(stop, "color").colorValue);
            }
            return new Gradient(stops,
                (GradientInterpolation)interpolationProperty.enumValueIndex);
        }

        private static void WriteGradient(SerializedProperty property, in Gradient value)
        {
            var stopsProperty = InspectorHelpers.RequireRelative(property, "stops");
            var stops = value.Stops;
            stopsProperty.arraySize = stops.Length;
            for (var i = 0; i < stops.Length; i++)
            {
                var stop = stopsProperty.GetArrayElementAtIndex(i);
                InspectorHelpers.RequireRelative(stop, "time").floatValue = stops[i].time;
                InspectorHelpers.RequireRelative(stop, "color").colorValue = stops[i].color;
            }
            InspectorHelpers.RequireRelative(property, "interpolation").enumValueIndex =
                (int)value.Interpolation;
        }

        private static int FinalCollectionIndex(string path, out string collectionPath)
        {
            const string marker = ".Array.data[";
            var markerIndex = path.LastIndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0 || path[path.Length - 1] != ']')
            {
                collectionPath = null;
                return -1;
            }
            var numberStart = markerIndex + marker.Length;
            if (!int.TryParse(path.Substring(numberStart, path.Length - numberStart - 1),
                    out var index) || index < 0)
                throw new InvalidOperationException(
                    $"Serialized collection element path '{path}' has an invalid index.");
            collectionPath = path.Substring(0, markerIndex);
            return index;
        }

        /// <summary>
        /// Whether a value of <paramref name="valueType"/> can be written to a serialized field of
        /// <paramref name="fieldType"/>. <see cref="Color"/> and <see cref="Color32"/> are one
        /// serialized colour in two encodings and are interchangeable in either direction.
        /// </summary>
        internal static bool Accepts(Type fieldType, Type valueType)
            => fieldType.IsAssignableFrom(valueType) || IsColor(fieldType) && IsColor(valueType);

        /// <summary>The value in the encoding a serialized field of <paramref name="fieldType"/> stores.</summary>
        internal static object Coerce(Type fieldType, object value) => value switch
        {
            Color color when fieldType == typeof(Color32) => (Color32)color,
            Color32 color when fieldType == typeof(Color) => (Color)color,
            _ => value,
        };

        private static bool IsColor(Type type) => type == typeof(Color) || type == typeof(Color32);

        private static void ValidateValue(Type expected, object value)
        {
            if (value == null)
            {
                if (expected.IsValueType && Nullable.GetUnderlyingType(expected) == null)
                    throw new InvalidOperationException(
                        $"Null cannot be assigned to serialized value type '{expected.FullName}'.");
                return;
            }
            if (!Accepts(expected, value.GetType()))
                throw new InvalidOperationException(
                    $"Value of type '{value.GetType().FullName}' cannot be assigned to '{expected.FullName}'.");
        }
    }
}
