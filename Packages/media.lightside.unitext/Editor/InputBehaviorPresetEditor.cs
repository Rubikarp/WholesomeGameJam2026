using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(InputBehaviorPreset))]
    [CanEditMultipleObjects]
    internal sealed class InputBehaviorPresetEditor : FullWidthEditor
    {
        private SerializedProperty behaviors;

        private void OnEnable() =>
            behaviors = InspectorHelpers.RequireProperty(serializedObject, "behaviors");

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = UniTextInspectorTheme.CreateRoot();
            root.Add(SerializedPropertyField.Create(behaviors));
            var warnings = InspectorVisuals.CreateStack();
            root.Add(warnings);

            void RefreshWarnings()
            {
                InspectorVisuals.ClearContent(warnings);
                for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
                {
                    var state = new SerializedObject(targets[targetIndex]);
                    state.UpdateIfRequiredOrScript();
                    var iterator = state.GetIterator();
                    var enterChildren = true;
                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = true;
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                        enterChildren = false;

                        var value = iterator.objectReferenceValue;
                        if (value == null || EditorUtility.IsPersistent(value)) continue;

                        var owner = targets.Length == 1
                            ? string.Empty
                            : $"{targets[targetIndex].name}: ";
                        warnings.Add(new HelpBox(
                            $"{owner}'{iterator.displayName}' references a scene object — a preset asset cannot store it " +
                            "and it will be saved as null. Move this behavior to the editor's local Behaviors " +
                            "list, or assign the target at runtime.",
                            HelpBoxMessageType.Warning));
                    }
                }
            }

            return new SerializedPropertyContext(behaviors)
                .Observe(root, RefreshWarnings);
        }
    }
}
