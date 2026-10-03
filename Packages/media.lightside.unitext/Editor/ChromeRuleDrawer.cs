using UnityEditor;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(ChromeRule))]
    internal class ChromeRuleDrawer : FoldoutElementDrawer
    {
        private static readonly ChromeRuleDrawer toolkit = new();

        [InitializeOnLoadMethod]
        private static void RegisterToolkitRenderer()
            => SerializedPropertyField.RegisterRenderer<ChromeRule>(toolkit.CreateToolkit);

        protected override InspectorLabel BuildLabel(
            SerializedProperty property, string given)
        {
            var rule = SerializedPropertyBinding.ResolveInstance(
                property.serializedObject.targetObject, property.propertyPath) as ChromeRule;
            if (rule == null || (rule.Style == null && rule.Selector == null))
                return new InspectorLabel(
                    ElementLabelUtility.IsArrayElement(property) ? "(Empty)" : given);
            return ElementLabelUtility.Compose(rule);
        }
    }
}
