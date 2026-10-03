using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Toolkit editor for one named object slot: the name and the source's type picker share the element's
    /// header row, and the selected source's own configuration fills the foldout body below.
    /// </summary>
    [CustomPropertyDrawer(typeof(ObjectSlot))]
    internal sealed class ObjectSlotDrawer : LightSidePropertyDrawer<ObjectSlot>
    {
        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var foldout = new InspectorSerializedFoldout(context);
            foldout.AddToClassList("lightside-object-slot");
            var structureBuilt = false;

            return foldout.Observe(slot =>
            {
                if (structureBuilt) return;
                structureBuilt = true;

                var primary = InspectorVisuals.CreateCompactRow();
                primary.AddToClassList("lightside-object-slot__primary");
                var nameProperty = InspectorHelpers.RequireRelative(slot, "name");
                var nameField = SerializedPropertyField.Create(nameProperty, nameProperty.displayName);
                nameField.AddToClassList("lightside-object-slot__name");
                primary.Add(nameField);
                TypeSelectorDrawer.CreateEmbedded(InspectorHelpers.RequireRelative(slot, "source"),
                    out var sourceHeader, out var sourceBody);
                sourceHeader.AddToClassList("lightside-object-slot__source");
                primary.Add(sourceHeader);
                foldout.Add(sourceBody);
                foldout.Header.Actions.Add(primary);
            });
        }
    }
}
