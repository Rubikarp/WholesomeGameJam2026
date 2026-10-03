using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(FontFamily))]
    internal sealed class FontFamilyDrawer : LightSidePropertyDrawer<FontFamily>
    {
        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var family = context.Property;
            var name = InspectorHelpers.RequireRelative(family, "name");
            var primary = InspectorHelpers.RequireRelative(family, "primary");
            var faces = InspectorHelpers.RequireRelative(family, "faces");
            var preferredLanguage = InspectorHelpers.RequireRelative(family, "preferredLanguage");
            var foldout = new InspectorSerializedFoldout(context);
            UniTextInspectorTheme.Initialize(foldout);
            foldout.AddToClassList("unitext-font-family");
            var primaryRow = InspectorVisuals.CreateCompactRow();
            primaryRow.AddToClassList("unitext-font-family__primary");
            var primaryField = SerializedPropertyField.Create(primary, string.Empty);
            primaryField.AddToClassList("unitext-font-family__font");
            var nameField = SerializedPropertyField.Create(name, name.displayName);
            nameField.AddToClassList("unitext-font-family__name");
            primaryRow.Add(primaryField);
            primaryRow.Add(nameField);
            foldout.Header.Actions.Add(primaryRow);
            var primaryWarning = new HelpBox(
                "Primary font is required. This family will be ignored without it.",
                HelpBoxMessageType.Warning);
            var familyWarning = new HelpBox(string.Empty, HelpBoxMessageType.Info);

            foldout.Add(SerializedPropertyField.Create(preferredLanguage, "Preferred Language"));
            foldout.Add(primaryWarning);
            foldout.Add(familyWarning);
            foldout.Add(SerializedPropertyField.Create(faces, "Faces"));

            return foldout.Observe(current =>
            {
                var currentPrimary = InspectorHelpers.RequireRelative(current, "primary");
                var currentFaces = InspectorHelpers.RequireRelative(current, "faces");

                AnalyzeSelection(context.Binding, out var missingPrimary,
                    out var familyMismatches);
                var targetCount = context.Binding.SerializedObject.targetObjects.Length;
                primaryWarning.text = targetCount == 1
                    ? "Primary font is required. This family will be ignored without it."
                    : $"{missingPrimary}/{targetCount} selected families have no primary font and will be ignored.";
                primaryWarning.style.display = missingPrimary > 0
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                var primaryFont = currentPrimary.objectReferenceValue as UniTextFont;
                familyWarning.text = familyMismatches == 0
                    ? string.Empty
                    : targetCount == 1
                        ? FamilyMismatch(primaryFont, currentFaces) ?? string.Empty
                        : $"{familyMismatches}/{targetCount} selected families contain faces whose family name differs from their primary font.";
                familyWarning.style.display = string.IsNullOrEmpty(familyWarning.text)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            });
        }

        private static string FamilyMismatch(UniTextFont primary, SerializedProperty faces)
        {
            var primaryFamily = primary?.FaceInfo.familyName;
            if (string.IsNullOrEmpty(primaryFamily)) return null;
            for (var i = 0; i < faces.arraySize; i++)
            {
                var face = faces.GetArrayElementAtIndex(i).objectReferenceValue as UniTextFont;
                var family = face?.FaceInfo.familyName;
                if (!string.IsNullOrEmpty(family) && family != primaryFamily)
                    return $"Face \"{family}\" differs from primary \"{primaryFamily}\". " +
                           "This is allowed but may be unintentional.";
            }
            return null;
        }

        private static void AnalyzeSelection(SerializedPropertyBinding binding,
            out int missingPrimary, out int familyMismatches)
        {
            var missing = 0;
            var mismatches = 0;
            binding.VisitTargetProperties((_, family) =>
            {
                var primary = InspectorHelpers.RequireRelative(family, "primary")
                    .objectReferenceValue as UniTextFont;
                if (primary == null) missing++;
                if (FamilyMismatch(primary,
                        InspectorHelpers.RequireRelative(family, "faces")) != null)
                    mismatches++;
            });
            missingPrimary = missing;
            familyMismatches = mismatches;
        }
    }
}
