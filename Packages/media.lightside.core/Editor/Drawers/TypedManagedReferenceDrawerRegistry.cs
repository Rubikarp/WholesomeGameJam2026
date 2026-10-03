using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Retained inspector body renderer selected by a managed-reference runtime type.</summary>
    public interface IManagedReferenceDrawer
    {
        /// <summary>Creates the body below the owning type-selector header; hide its root when it exposes no content.</summary>
        VisualElement CreatePropertyGUI(SerializedProperty property);
    }

    /// <summary>Adds type-specific controls to the owning type-selector header.</summary>
    public interface IManagedReferenceHeaderDrawer
    {
        /// <summary>Creates inline content after the type selector.</summary>
        VisualElement CreateHeaderGUI(SerializedProperty property);
    }

    /// <summary>Maps managed-reference types to their UI Toolkit body renderers.</summary>
    public static class TypedManagedReferenceDrawerRegistry
    {
        private static readonly Dictionary<Type, IManagedReferenceDrawer> drawers = new();

        /// <summary>Registers a renderer for a runtime type, base type, or open generic base.</summary>
        public static void Register(Type type, IManagedReferenceDrawer drawer)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            drawers[type] = drawer ?? throw new ArgumentNullException(nameof(drawer));
        }

        /// <summary>Finds the nearest exact, base-type, or open-generic renderer.</summary>
        public static bool TryGet(Type type, out IManagedReferenceDrawer drawer)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (drawers.TryGetValue(current, out drawer))
                    return true;
                if (current.IsGenericType &&
                    drawers.TryGetValue(current.GetGenericTypeDefinition(), out drawer))
                    return true;
            }
            drawer = null;
            return false;
        }
    }
}
