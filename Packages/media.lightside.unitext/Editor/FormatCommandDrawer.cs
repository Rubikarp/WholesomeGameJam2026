using UnityEditor;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(TextFormattingBehavior.FormatCommand))]
    internal class FormatCommandDrawer : FoldoutElementDrawer
    {
        private static readonly FormatCommandDrawer toolkit = new();

        [InitializeOnLoadMethod]
        private static void RegisterToolkitRenderer()
            => SerializedPropertyField.RegisterRenderer<TextFormattingBehavior.FormatCommand>(
                toolkit.CreateToolkit);

        protected override InspectorLabel BuildLabel(
            SerializedProperty property, string given)
        {
            var name = InspectorHelpers.RequireRelative(property, "name").stringValue;
            var targetProp = InspectorHelpers.RequireRelative(property, "target");
            var styleSourceProp = targetProp.FindPropertyRelative("modifier") ?? targetProp;
            var modifier = ElementLabelUtility.ColoredName(styleSourceProp);
            var shortcut = ShortcutText(property);

            string text;
            if (string.IsNullOrEmpty(name) && modifier == null)
                text = ElementLabelUtility.IsArrayElement(property) ? "(Empty)" : given;
            else if (string.IsNullOrEmpty(name))
                text = modifier;
            else if (modifier == null)
                text = name;
            else
                text = $"{name} — {modifier}";

            if (shortcut != null) text = $"{text} ({shortcut})";

            return new InspectorLabel(text, ElementLabelUtility.Icon(styleSourceProp));
        }

        private static string ShortcutText(SerializedProperty property)
        {
            var keyProp = InspectorHelpers.RequireRelative(property, "key");
            var key = (NativeKeyCode)keyProp.intValue;
            if (key == NativeKeyCode.None) return null;

            var primary = PlatformKeySemantics.PrimaryModifierIsCommand ? "Cmd" : "Ctrl";
            var shift = InspectorHelpers.RequireRelative(property, "shift").boolValue ? "+Shift" : "";
            return $"{primary}{shift}+{key}";
        }
    }
}
