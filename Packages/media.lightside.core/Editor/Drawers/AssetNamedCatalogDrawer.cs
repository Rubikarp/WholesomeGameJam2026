using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    internal sealed class AssetNamedCatalogDrawer :
        IManagedReferenceDrawer, IManagedReferenceHeaderDrawer
    {
        [InitializeOnLoadMethod]
        private static void Register() =>
            TypedManagedReferenceDrawerRegistry.Register(
                typeof(AssetNamedCatalog<,>), new AssetNamedCatalogDrawer());

        public VisualElement CreateHeaderGUI(SerializedProperty property) =>
            SerializedPropertyField.Create(
                InspectorHelpers.RequireRelative(property, "asset"), string.Empty);

        public VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var body = new VisualElement();
            body.style.display = DisplayStyle.None;
            return body;
        }
    }
}
