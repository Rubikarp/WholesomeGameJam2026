using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Renders the body of a <c>[SerializeReference]</c>-d <see cref="FixedRangeSource"/> as a styled
    /// list, matching the look-and-feel of <see cref="UniTextBase.styles"/> and other lists in
    /// the inspector. Registered through <see cref="TypedManagedReferenceDrawerRegistry"/> so it
    /// composes with <see cref="TypeSelectorDrawer"/>, which keeps the header (foldout + type
    /// picker) and delegates the body rendering here.
    /// </summary>
    internal sealed class FixedRangeSourceDrawer : IManagedReferenceDrawer
    {
        [InitializeOnLoadMethod]
        private static void Register() =>
            TypedManagedReferenceDrawerRegistry.Register(typeof(FixedRangeSource),
                new FixedRangeSourceDrawer());

        /// <inheritdoc/>
        public VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var ranges = InspectorHelpers.RequireRelative(property, "ranges");
            return SerializedPropertyField.Create(ranges, "Ranges");
        }
    }
}
