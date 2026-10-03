using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(StylePreset))]
    [CanEditMultipleObjects]
    internal class StylePresetEditor : FullWidthEditor
    {
        private SerializedProperty stylesProp;
        private readonly List<ModifierGraphIssue> issues = new();

        private void OnEnable()
        {
            stylesProp = InspectorHelpers.RequireProperty(serializedObject, "styles");
        }

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = UniTextInspectorTheme.CreateRoot();
            root.Add(SerializedPropertyField.Create(stylesProp, "Styles"));
            var messages = InspectorVisuals.CreateStack();
            root.Add(messages);

            void RefreshIssues()
            {
                InspectorVisuals.ClearContent(messages);
                for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
                {
                    var preset = (StylePreset)targets[targetIndex];
                    issues.Clear();
                    ModifierGraphValidator.Validate(preset, issues);
                    ModifierGraphIssueRenderer.Append(messages, issues,
                        targets.Length == 1 ? null : preset.name);
                }
            }

            return new SerializedPropertyContext(stylesProp, "Styles")
                .Observe(root, RefreshIssues);
        }
    }
}
