using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(InlineMedia), true)]
    internal sealed class InlineMediaDrawer : LightSidePropertyDrawer<InlineMedia>
    {
        [InitializeOnLoadMethod]
        private static void RegisterModifierRenderer()
            => TypedManagedReferenceDrawerRegistry.Register(
                typeof(InlineMediaModifier<,>),
                new ModifierBodyDrawer("overrides", "provider"));

        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var property = context.Property;
            var name = InspectorHelpers.RequireRelative(property, "name");
            var media = property.FindPropertyRelative("sprite") ??
                        property.FindPropertyRelative("prefab");
            var foldout = new InspectorSerializedFoldout(context);
            foldout.AddToClassList("unitext-inline-media");
            var primary = InspectorVisuals.CreateCompactRow();
            primary.AddToClassList("unitext-inline-media__primary");
            var nameField = SerializedPropertyField.Create(name, name.displayName);
            nameField.AddToClassList("unitext-inline-media__field");
            primary.Add(nameField);
            if (media != null)
            {
                var mediaField = SerializedPropertyField.Create(media, string.Empty);
                mediaField.AddToClassList("unitext-inline-media__field");
                primary.Add(mediaField);
            }
            foldout.Header.Actions.Add(primary);
            foreach (var child in InspectorHelpers.VisibleChildren(property))
                if (child.propertyPath != name.propertyPath &&
                    (media == null || child.propertyPath != media.propertyPath))
                    foldout.Add(SerializedPropertyField.Create(child));

            return foldout.Observe();
        }

    }

    [CustomPropertyDrawer(typeof(InlineMediaOverride), true)]
    internal sealed class InlineMediaOverrideDrawer : LightSidePropertyDrawer<InlineMediaOverride>
    {
        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var property = context.Property;
            var root = InspectorVisuals.CreateStack();
            UniTextInspectorTheme.Initialize(root);
            root.Add(SerializedPropertyField.CreateRelative(property, "key"));

            var owner = InspectorHelpers.ResolveInstance(
                property.serializedObject.targetObject, property.propertyPath);
            if (ModifierBodyDrawer.CreateLiveParameterList(property, owner) is { } parameters)
                root.Add(parameters);
            return root;
        }
    }
}
