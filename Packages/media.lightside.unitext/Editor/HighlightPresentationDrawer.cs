using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(HighlightPresentation))]
    internal sealed class HighlightPresentationDrawer : LightSidePropertyDrawer<HighlightPresentation>
    {
        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var property = context.Property;
            var foldout = new InspectorSerializedFoldout(context);
            foldout.Header.SetContent(context.Label);
            foldout.Add(SerializedPropertyField.CreateRelative(property, "provider"));
            if (ModifierBodyDrawer.CreateLiveParameterList(
                    property, context.Binding.Value) is { } parameters)
                foldout.Add(parameters);

            return foldout.Observe();
        }
    }
}
