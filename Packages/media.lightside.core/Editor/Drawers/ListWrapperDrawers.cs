using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    internal abstract class ListWrapperDrawer : LightSidePropertyBridge
    {
        /// <inheritdoc/>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var items = InspectorHelpers.RequireRelative(property, "items");
            var result = SerializedPropertyField.Create(items, property.displayName);
            result.tooltip = property.tooltip;
            return result;
        }
    }

    [CustomPropertyDrawer(typeof(TypedList<>))]
    internal sealed class TypedListDrawer : ListWrapperDrawer { }

    [CustomPropertyDrawer(typeof(StyledList<>))]
    internal sealed class StyledListDrawer : ListWrapperDrawer { }
}
