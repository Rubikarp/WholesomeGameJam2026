using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Toolkit editor for a named reveal handler: one collapsible row holding the name and the
    /// handler's type selector, with the handler's configuration and the hide-handler slot in the
    /// foldout body below.
    /// </summary>
    [CustomEditor(typeof(UniTextRevealHandlers))]
    [CanEditMultipleObjects]
    internal sealed class UniTextRevealHandlersEditor : UniTextNamedCatalogEditor
    {
    }

    [CustomPropertyDrawer(typeof(RevealHandlerEntry))]
    internal sealed class RevealHandlerEntryDrawer : LightSidePropertyDrawer<RevealHandlerEntry>
    {
        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var foldout = new InspectorSerializedFoldout(context);
            UniTextInspectorTheme.Initialize(foldout);
            foldout.AddToClassList("unitext-reveal-handler");
            var structureBuilt = false;

            return foldout.Observe(entry =>
            {
                if (structureBuilt) return;
                structureBuilt = true;

                var row = InspectorVisuals.CreateCompactRow();
                row.AddToClassList("unitext-reveal-handler__primary");
                var nameProperty = InspectorHelpers.RequireRelative(entry, "name");
                var nameField = SerializedPropertyField.Create(
                    nameProperty, nameProperty.displayName);
                nameField.AddToClassList("unitext-reveal-handler__name");
                row.Add(nameField);
                TypeSelectorDrawer.CreateEmbedded(
                    InspectorHelpers.RequireRelative(entry, "handler"),
                    out var handlerHeader, out var handlerBody);
                handlerHeader.AddToClassList("unitext-reveal-handler__selector");
                row.Add(handlerHeader);
                foldout.Add(handlerBody);
                var hideProperty = InspectorHelpers.RequireRelative(entry, "hideHandler");
                foldout.Add(SerializedPropertyField.Create(
                    hideProperty, hideProperty.displayName));
                foldout.Header.Actions.Add(row);
            });
        }
    }
}
