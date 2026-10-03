using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LightSide
{
    /// <summary>Reflection rules shared by Unity serialized-property editor infrastructure.</summary>
    public static class SerializedReflection
    {
        /// <summary>Returns whether Unity serializes an instance field under its standard field rules.</summary>
        public static bool IsSerializedField(FieldInfo field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            return !field.IsStatic && !field.IsInitOnly && !field.IsNotSerialized &&
                   (field.IsPublic || field.IsDefined(typeof(SerializeField), true) ||
                    field.IsDefined(typeof(SerializeReference), true));
        }

        /// <summary>
        /// Returns the element type of an array or <see cref="List{T}"/> type, including a derived
        /// list type, or null when the type is not a Unity serialized collection.
        /// </summary>
        public static Type GetCollectionElementType(Type collectionType)
        {
            if (collectionType == null) throw new ArgumentNullException(nameof(collectionType));
            if (collectionType.IsArray) return collectionType.GetElementType();
            for (var type = collectionType; type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                    return type.GetGenericArguments()[0];
            return null;
        }

        /// <summary>Finds an instance field by walking a type and its base hierarchy.</summary>
        public static FieldInfo FindField(Type type, string name)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A field name is required.", nameof(name));
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }
    }
}
