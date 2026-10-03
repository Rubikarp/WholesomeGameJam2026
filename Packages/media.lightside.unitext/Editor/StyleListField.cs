using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    internal static class StyleListField
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            SerializedPropertyField.RegisterRenderer<StyledList<Style>>(Create);
            PropertyClipboard.RegisterElementPasteAdapter<BaseModifier, Style>(
                ReplaceModifier, modifier => Style.WholeText(modifier),
                style => style.Source is not ParseRule { IsStandalone: true });
        }

        private static void ReplaceModifier(SerializedProperty style, BaseModifier modifier)
            => InspectorHelpers.RequireRelative(style, "modifier").managedReferenceValue = modifier;

        private static VisualElement Create(SerializedPropertyContext context)
        {
            var items = InspectorHelpers.RequireRelative(context.Property, "items");
            return SerializedPropertyField.CreateCollection(items, context.Label,
                (anchor, insert) => UniTextBaseEditor.ShowStylePresetSelector(
                    anchor,
                    new[] { context.Binding.SerializedObject.targetObject },
                    context.Binding.SerializedObject,
                    (_, style) => insert(style)));
        }
    }
}
