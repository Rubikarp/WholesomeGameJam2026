using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(ModifierGraphPreset))]
    [CanEditMultipleObjects]
    internal sealed class ModifierGraphPresetEditor : FullWidthEditor
    {
        private readonly List<ModifierGraphIssue> issues = new();
        private SerializedProperty root;

        private void OnEnable() =>
            root = InspectorHelpers.RequireProperty(serializedObject, "root");

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var container = UniTextInspectorTheme.CreateRoot();
            container.Add(SerializedPropertyField.Create(root, string.Empty));
            var messages = InspectorVisuals.CreateStack();
            container.Add(messages);

            void RefreshIssues()
            {
                InspectorVisuals.ClearContent(messages);
                for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
                {
                    var preset = (ModifierGraphPreset)targets[targetIndex];
                    issues.Clear();
                    ModifierGraphValidator.Validate(preset, issues);
                    ModifierGraphIssueRenderer.Append(messages, issues,
                        targets.Length == 1 ? null : preset.name);
                }
            }

            return new SerializedPropertyContext(root, string.Empty)
                .Observe(container, RefreshIssues);
        }
    }
}
