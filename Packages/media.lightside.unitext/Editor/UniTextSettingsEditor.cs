using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(UniTextSettings))]
    [CanEditMultipleObjects]
    internal sealed class UniTextSettingsEditor : FullWidthEditor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = UniTextInspectorTheme.CreateRoot();
            var activeSettings = UniTextSettings.Instance;
            for (var i = 0; i < targets.Length; i++)
            {
                if (targets[i] != activeSettings) continue;
                AddActiveSettingsNotice(root);
                return root;
            }

            AddInactiveSettingsNotice(root, targets.Length > 1);
            var fields = InspectorVisuals.CreateSection("Settings Asset");
            var iterator = serializedObject.GetIterator();
            if (iterator.NextVisible(true))
            {
                do
                {
                    var property = iterator.Copy();
                    if (property.propertyPath != "m_Script")
                        fields.Add(SerializedPropertyField.Create(property));
                }
                while (iterator.NextVisible(false));
            }
            root.Add(fields);
            return root;
        }

        private static void AddActiveSettingsNotice(VisualElement root)
        {
            var section = InspectorVisuals.CreateSection("UniText Settings");
            section.Add(new HelpBox(
                "The active UniTextSettings asset is read-only in the Inspector. Edit it through " +
                "Edit → Project Settings → UniText.",
                HelpBoxMessageType.Info));
            section.Add(new Button(() => SettingsService.OpenProjectSettings(UniTextMenu.Settings))
            {
                text = "Open UniText Settings",
            });
            root.Add(section);
        }

        private static void AddInactiveSettingsNotice(VisualElement root, bool multiEdit)
        {
            var section = InspectorVisuals.CreateSection(
                multiEdit ? "Settings Selection" : "Inactive Settings");
            section.Add(new HelpBox(
                multiEdit
                    ? "Changes apply to every selected settings asset. Only the active UniTextSettings loaded from a Resources folder affects runtime behavior."
                    : "This is not the active UniTextSettings. The active settings are loaded from a " +
                      "Resources folder at runtime. Use Edit → Project Settings → UniText to edit settings.",
                HelpBoxMessageType.Warning));

            var active = UniTextSettings.Instance;
            if (active != null)
            {
                var row = InspectorVisuals.CreateRow();
                var path = new Label($"Active Settings: {AssetDatabase.GetAssetPath(active)}")
                {
                    style = { flexGrow = 1f },
                };
                row.Add(path);
                row.Add(new Button(() => EditorGUIUtility.PingObject(active)) { text = "Ping" });
                row.Add(new Button(() => Selection.activeObject = active) { text = "Select" });
                section.Add(row);
            }

            section.Add(new Button(() => SettingsService.OpenProjectSettings(UniTextMenu.Settings))
            {
                text = "Open Project Settings",
            });
            root.Add(section);
        }
    }
}
