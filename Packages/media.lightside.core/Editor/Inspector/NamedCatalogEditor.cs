using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Full-width inspector base for a named-catalog asset: the "entries" collection rendered through
    /// the shared collection field. A package subclass overrides <see cref="CreateRoot"/> for its
    /// theme and may rebuild <see cref="CreateInspectorGUI"/> from <see cref="CreateEntriesField"/>
    /// when the asset needs more than the one field.
    /// </summary>
    public abstract class NamedCatalogEditor : FullWidthEditor
    {
        private SerializedProperty entriesProp;

        protected virtual void OnEnable() =>
            entriesProp = InspectorHelpers.RequireProperty(serializedObject, "entries");

        /// <summary>Creates the themed root the entries render into.</summary>
        protected virtual VisualElement CreateRoot() => InspectorVisuals.CreateRoot();

        /// <summary>Creates the bound entries collection field.</summary>
        protected VisualElement CreateEntriesField(string label = "Entries")
            => SerializedPropertyField.Create(entriesProp, label);

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            var root = CreateRoot();
            root.Add(CreateEntriesField());
            return root;
        }
    }
}
