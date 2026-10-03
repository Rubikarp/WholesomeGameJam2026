using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Toolkit editor for a paint swatch. Identity and primary paint value stay visible while the
    /// kind-specific projection fields share one dynamic details body.
    /// </summary>
    [CustomPropertyDrawer(typeof(PaintSwatch))]
    internal sealed class PaintSwatchDrawer : LightSidePropertyDrawer<PaintSwatch>
    {
        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var foldout = new InspectorSerializedFoldout(context);
            UniTextInspectorTheme.Initialize(foldout);
            foldout.AddToClassList("lightside-paint-swatch");
            foldout.RegisterCallback<ChangeEvent<Enum>>(_ => foldout.Refresh());
            foldout.RegisterCallback<ChangeEvent<UnityEngine.Object>>(_ => foldout.Refresh());

            var body = new RetainedBody(foldout,
                property => PaintSwatchBody.Structure(property, context.Binding),
                property => PaintSwatchBody.Build(foldout, property, context.Binding,
                    static (root, swatch) =>
                        root.Add(SerializedPropertyField.CreateRelative(swatch, "mapping"))));
            return foldout.Observe(property => body.Refresh(property));
        }
    }
}
