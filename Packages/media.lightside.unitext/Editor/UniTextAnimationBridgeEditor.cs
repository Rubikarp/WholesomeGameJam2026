using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(UniTextAnimationBridge))]
    [CanEditMultipleObjects]
    internal sealed class UniTextAnimationBridgeEditor : FullWidthEditor
    {
        private SerializedProperty handlers;

        private void OnEnable() =>
            handlers = InspectorHelpers.RequireProperty(serializedObject, "handlers");

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = UniTextInspectorTheme.CreateRoot();
            root.Add(SerializedPropertyField.Create(handlers));
            return root;
        }
    }
}
