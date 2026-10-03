using System;
using UnityEditor;

namespace LightSide
{
    /// <summary>Base class for inspectors that own their full-width layout.</summary>
    public abstract class FullWidthEditor : Editor
    {
        /// <inheritdoc/>
        public sealed override bool UseDefaultMargins() => false;
    }

    /// <summary>Marks a custom inspector whose product behavior intentionally requires one selected object.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SingleObjectEditorAttribute : Attribute
    {
    }

    internal static class EditorArchitectureGuard
    {
        [InitializeOnLoadMethod]
        private static void ValidateCustomEditors()
        {
            foreach (var type in TypeCache.GetTypesWithAttribute<CustomEditor>())
            {
                var assemblyName = type.Assembly.GetName().Name;
                if (type.IsAbstract || assemblyName == null ||
                    !assemblyName.StartsWith("LightSide.", StringComparison.Ordinal)) continue;
                if (Attribute.IsDefined(type, typeof(CanEditMultipleObjects), false) ||
                    Attribute.IsDefined(type, typeof(SingleObjectEditorAttribute), false)) continue;
                throw new InvalidOperationException(
                    $"{type.FullName} must declare [CanEditMultipleObjects] or [SingleObjectEditor].");
            }
        }
    }
}
