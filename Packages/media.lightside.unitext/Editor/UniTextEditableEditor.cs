using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(UniTextEditable))]
    [CanEditMultipleObjects]
    internal class UniTextEditableEditor : FullWidthEditor
    {
        private SerializedProperty caretRendererProp;
        private SerializedProperty readOnlyProp;
        private SerializedProperty behaviorsProp;
        private SerializedProperty behaviorPresetsProp;

        private void OnEnable()
        {
            caretRendererProp = InspectorHelpers.RequireProperty(serializedObject, "caretRenderer");
            readOnlyProp = InspectorHelpers.RequireProperty(serializedObject, "readOnly");
            behaviorsProp = InspectorHelpers.RequireProperty(serializedObject, "behaviors");
            behaviorPresetsProp = InspectorHelpers.RequireProperty(serializedObject, "behaviorPresets");
        }

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = UniTextInspectorTheme.CreateRoot();
            root.Add(SerializedPropertyField.Create(readOnlyProp, "Read Only"));
            root.Add(SerializedPropertyField.Create(caretRendererProp));
            root.Add(SerializedPropertyField.Create(behaviorsProp));
            root.Add(SerializedPropertyField.Create(behaviorPresetsProp));
            return root;
        }
    }
}
