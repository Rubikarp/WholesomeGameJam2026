using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    internal sealed class InlineFieldParseRuleDrawer :
        IManagedReferenceDrawer, IManagedReferenceHeaderDrawer
    {
        private readonly string fieldName;

        private InlineFieldParseRuleDrawer(string fieldName) => this.fieldName = fieldName;

        [InitializeOnLoadMethod]
        private static void Register()
        {
            TypedManagedReferenceDrawerRegistry.Register(
                typeof(TagRule), new InlineFieldParseRuleDrawer("tag"));
            TypedManagedReferenceDrawerRegistry.Register(
                typeof(MarkdownWrapRule), new InlineFieldParseRuleDrawer("marker"));
        }

        public VisualElement CreateHeaderGUI(SerializedProperty property)
            => SerializedPropertyField.Create(
                InspectorHelpers.RequireRelative(property, fieldName), string.Empty);

        public VisualElement CreatePropertyGUI(SerializedProperty property) =>
            SerializedPropertyField.CreateRelative(property, "defaultParameter");
    }
}
