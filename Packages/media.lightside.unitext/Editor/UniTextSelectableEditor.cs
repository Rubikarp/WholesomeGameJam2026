using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(UniTextSelectable))]
    [CanEditMultipleObjects]
    internal class UniTextSelectableEditor : FullWidthEditor
    {
        private SerializedProperty selectionHighlightProp;
        private SerializedProperty contextMenuProp;
        private SerializedProperty selectionHandlesProp;
        private SerializedProperty magnifierProp;

        private void OnEnable()
        {
            selectionHighlightProp = InspectorHelpers.RequireProperty(serializedObject, "selectionHighlight");
            contextMenuProp = InspectorHelpers.RequireProperty(serializedObject, "contextMenu");
            selectionHandlesProp = InspectorHelpers.RequireProperty(serializedObject, "selectionHandles");
            magnifierProp = InspectorHelpers.RequireProperty(serializedObject, "magnifier");
        }

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = UniTextInspectorTheme.CreateRoot();
            root.Add(SerializedPropertyField.Create(selectionHighlightProp));
            root.Add(SerializedPropertyField.Create(contextMenuProp));
            root.Add(SerializedPropertyField.Create(selectionHandlesProp));
            root.Add(SerializedPropertyField.Create(magnifierProp));
            return root;
        }
    }
}
