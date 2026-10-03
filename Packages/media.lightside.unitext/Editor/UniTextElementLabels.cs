using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Names the two UniText composites whose serialized field order is not the order they read in,
    /// so every surface — list row, clipboard, tooltip — shows the same label.
    /// </summary>
    internal static class UniTextElementLabels
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ElementLabelUtility.RegisterComposer<Style>(Compose);
            ElementLabelUtility.RegisterComposer<ChromeRule>(Compose);
        }

        /// <summary>A standalone parse rule carries the whole style, with no modifier to name.</summary>
        private static InspectorLabel Compose(Style style)
            => style.Source is ParseRule { IsStandalone: true }
                ? ElementLabelUtility.Compose(style.Source)
                : ElementLabelUtility.ComposeParts(
                    (null, style.Modifier), ("Source", style.Source));

        private static InspectorLabel Compose(ChromeRule rule)
            => ElementLabelUtility.ComposeParts(
                (null, rule.Style), (null, rule.Selector));
    }
}
