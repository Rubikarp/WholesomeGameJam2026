using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Text and optional icon shown by a retained-mode inspector header.</summary>
    public readonly struct InspectorLabel
    {
        /// <summary>
        /// Creates header content without coupling it to an immediate-mode control. Supply
        /// <paramref name="plainText"/> whenever <paramref name="text"/> carries colour markup:
        /// surfaces that disable rich text, and search, use the plain form.
        /// </summary>
        public InspectorLabel(string text, Texture2D image = null, string plainText = null,
            object accentKey = null)
        {
            Text = text;
            Image = image;
            PlainText = plainText ?? text;
            AccentKey = accentKey;
        }

        /// <summary>Rich text displayed by the header.</summary>
        public string Text { get; }

        /// <summary>Optional icon displayed before the text.</summary>
        public Texture2D Image { get; }

        /// <summary>The same content without colour markup.</summary>
        public string PlainText { get; }

        /// <summary>Type whose accent colours this label where the surface colours a whole row.</summary>
        public object AccentKey { get; }
    }

    /// <summary>
    /// Base for list-element drawers that render a rich foldout label over the property's children.
    /// Subclasses supply only the header content.
    /// </summary>
    public abstract class FoldoutElementDrawer : LightSidePropertyBridge
    {
        /// <summary>
        /// Builds the same retained foldout for callers using the shared serialized-property pipeline.
        /// </summary>
        protected VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var foldout = new InspectorSerializedFoldout(context);
            var bodyBuilt = false;

            return foldout.Observe(property =>
            {
                var content = BuildLabel(property, property.displayName);
                if (context.Binding.AnyTargetProperty((_, targetProperty) =>
                    {
                        var targetContent = BuildLabel(targetProperty, targetProperty.displayName);
                        return targetContent.Text != content.Text || targetContent.Image != content.Image;
                    }))
                    content = new InspectorLabel("—");
                foldout.Header.SetContent(content.Text, content.Image);

                if (bodyBuilt) return;
                bodyBuilt = true;
                foreach (var child in InspectorHelpers.VisibleChildren(property))
                    foldout.Add(SerializedPropertyField.Create(child));
            });
        }

        /// <summary>Builds the foldout header shown for the serialized element.</summary>
        protected abstract InspectorLabel BuildLabel(
            SerializedProperty property, string label);
    }
}
