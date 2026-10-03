using System;
using System.Collections.Generic;
using UnityEditor;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>Shared helpers for resolving and mutating serialized editor state.</summary>
    public static class InspectorHelpers
    {
        /// <summary>
        /// Resolves the live object addressed by a serialized property path. Plain fields and
        /// <c>.Array.data[n]</c> segments are supported; an unavailable segment returns null.
        /// </summary>
        public static object ResolveInstance(object target, string propertyPath)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (propertyPath == null) throw new ArgumentNullException(nameof(propertyPath));
            object current = target;
            var path = propertyPath.Replace(".Array.data[", "[");
            foreach (var segment in path.Split('.'))
            {
                if (current == null) return null;

                var name = segment;
                var index = -1;
                var bracket = segment.IndexOf('[');
                if (bracket >= 0)
                {
                    name = segment.Substring(0, bracket);
                    index = int.Parse(segment.Substring(bracket + 1, segment.Length - bracket - 2));
                }

                current = GetFieldValue(current, name);
                if (index < 0) continue;

                if (current is System.Collections.IList list)
                    current = index < list.Count ? list[index] : null;
                else
                    return null;
            }
            return current;
        }

        /// <summary>
        /// Applies a value produced independently for every selected target through the shared
        /// serialized-property binding and records one named undo operation.
        /// </summary>
        public static void ApplySerializedProperty(SerializedObject serializedObject,
            string propertyPath, string undoLabel, Func<Object, object> valueFactory)
        {
            var property = RequireProperty(serializedObject, propertyPath);
            new SerializedPropertyBinding(property).SetValue(valueFactory, undoLabel);
        }

        /// <summary>Finds a required root serialized property or fails at the inspector boundary.</summary>
        public static SerializedProperty RequireProperty(SerializedObject serializedObject,
            string propertyPath)
        {
            if (serializedObject == null) throw new ArgumentNullException(nameof(serializedObject));
            if (string.IsNullOrEmpty(propertyPath))
                throw new ArgumentException("A serialized property path is required.", nameof(propertyPath));
            return serializedObject.FindProperty(propertyPath) ??
                   throw new InvalidOperationException(
                       $"Required serialized property '{propertyPath}' is unavailable on " +
                       $"'{serializedObject.targetObject.GetType().FullName}'.");
        }

        /// <summary>Finds a required child serialized property or fails at the inspector boundary.</summary>
        public static SerializedProperty RequireRelative(SerializedProperty property,
            string relativePath)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (string.IsNullOrEmpty(relativePath))
                throw new ArgumentException("A relative property path is required.", nameof(relativePath));
            return property.FindPropertyRelative(relativePath) ??
                   throw new InvalidOperationException(
                       $"Required serialized property '{property.propertyPath}.{relativePath}' is unavailable.");
        }

        /// <summary>
        /// Enumerates the immediate visible children of a serialized property in Unity order.
        /// Each yielded property is an independent copy that remains stable as enumeration advances.
        /// </summary>
        public static IEnumerable<SerializedProperty> VisibleChildren(SerializedProperty property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var iterator = property.Copy();
            var end = iterator.GetEndProperty();
            if (!iterator.NextVisible(true)) yield break;
            do
            {
                if (SerializedProperty.EqualContents(iterator, end)) yield break;
                yield return iterator.Copy();
            }
            while (iterator.NextVisible(false));
        }

        private static object GetFieldValue(object source, string fieldName)
            => SerializedReflection.FindField(source.GetType(), fieldName)?.GetValue(source);
    }
}
