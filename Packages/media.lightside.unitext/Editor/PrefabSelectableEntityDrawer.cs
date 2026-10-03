using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    internal sealed class PrefabSelectableEntityDrawer :
        IManagedReferenceDrawer, IManagedReferenceHeaderDrawer
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            var drawer = new PrefabSelectableEntityDrawer();
            TypedManagedReferenceDrawerRegistry.Register(typeof(PrefabSelectionHandles), drawer);
            TypedManagedReferenceDrawerRegistry.Register(typeof(PrefabMagnifier), drawer);
            TypedManagedReferenceDrawerRegistry.Register(typeof(PrefabTextContextMenu), drawer);
        }

        public VisualElement CreateHeaderGUI(SerializedProperty property) =>
            SerializedPropertyField.Create(
                InspectorHelpers.RequireRelative(property, "prefab"), string.Empty);

        public VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var body = new VisualElement();
            body.style.display = DisplayStyle.None;
            return body;
        }
    }
}
