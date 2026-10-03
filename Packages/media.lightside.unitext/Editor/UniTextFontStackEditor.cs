using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(UniTextFontStack))]
    [CanEditMultipleObjects]
    internal sealed class UniTextFontStackEditor : FullWidthEditor
    {
        private SerializedProperty families;
        private SerializedProperty fallbackStack;

        private void OnEnable()
        {
            families = InspectorHelpers.RequireProperty(serializedObject, "families");
            fallbackStack = InspectorHelpers.RequireProperty(serializedObject, "fallbackStack");
        }

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = UniTextInspectorTheme.CreateRoot();
            root.Add(SerializedPropertyField.Create(families, "Font Families"));
            root.Add(SerializedPropertyField.Create(fallbackStack, "Fallback Stack"));
            return root;
        }
    }
}
