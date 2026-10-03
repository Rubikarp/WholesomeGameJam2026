using System;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Builds the shared UI Toolkit fields owned by Unity UI graphics.</summary>
    public static class GraphicInspectorFields
    {
        /// <summary>Adds the standard color field and, when requested, the material override.</summary>
        public static void AddAppearance(VisualElement root, SerializedObject serializedObject,
            bool includeMaterial = true)
        {
            Validate(root, serializedObject);
            root.Add(SerializedPropertyField.Create(serializedObject, "m_Color"));
            if (includeMaterial)
                root.Add(SerializedPropertyField.Create(serializedObject, "m_Material"));
        }

        /// <summary>Adds the standard raycast, raycast-padding, and maskable fields.</summary>
        public static void AddInteraction(VisualElement root, SerializedObject serializedObject,
            bool notifyRaycastChanges = true)
        {
            Validate(root, serializedObject);
            var raycast = InspectorHelpers.RequireProperty(serializedObject, "m_RaycastTarget");
            var padding = InspectorHelpers.RequireProperty(serializedObject, "m_RaycastPadding");
            var maskable = InspectorHelpers.RequireProperty(serializedObject, "m_Maskable");
            var row = InspectorVisuals.CreateCompactWrapRow();
            var paddingField = PaddingBoxButton.Create(padding, "Raycast Padding");
            var raycastBinding = new SerializedPropertyBinding(raycast);

            void MarkRaycastDirty()
            {
                foreach (var target in serializedObject.targetObjects)
                {
                    if (target is not Graphic graphic)
                        throw new InvalidOperationException(
                            $"'{target.GetType().FullName}' is not a Unity UI Graphic.");
                    graphic.SetRaycastDirty();
                }
            }

            void RefreshPadding()
                => InspectorMotion.SetExpanded(paddingField,
                    raycastBinding.HasMultipleValues || (bool)raycastBinding.Value);

            var raycastField = SerializedPropertyField.CreateToggle(raycast, "Raycast Target");
            if (notifyRaycastChanges)
                SerializedPropertyField.OnChange(raycastField, MarkRaycastDirty, raycast);
            row.Add(raycastField);
            row.Add(paddingField);
            row.Add(SerializedPropertyField.CreateToggle(maskable, "Maskable"));
            root.Add(row);
            SerializedPropertyField.Observe(paddingField, RefreshPadding, raycast);
        }

        private static void Validate(VisualElement root, SerializedObject serializedObject)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (serializedObject == null) throw new ArgumentNullException(nameof(serializedObject));
        }
    }
}
