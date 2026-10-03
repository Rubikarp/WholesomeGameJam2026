using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Shows a complete consumer-neutral paint.</summary>
    [CustomPropertyDrawer(typeof(Paint))]
    internal sealed class PaintDrawer : LightSidePropertyDrawer<Paint>
    {
        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var property = context.Binding.FindSerializedProperty();
            if (property == null)
                throw new InvalidOperationException("Paint property is unavailable.");

            var root = new VisualElement();
            root.Add(SerializedPropertyField.Create(
                InspectorHelpers.RequireRelative(property, "source"), context.Label));
            root.Add(SerializedPropertyField.CreateRelative(property, "projection"));
            root.Add(SerializedPropertyField.CreateRelative(property, "blend"));
            return root;
        }
    }

    /// <summary>Shows a paint source kind and only the payload authored for that kind.</summary>
    [CustomPropertyDrawer(typeof(PaintSource))]
    internal sealed class PaintSourceDrawer : LightSidePropertyDrawer<PaintSource>
    {
        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var property = context.Binding.FindSerializedProperty();
            if (property == null)
                throw new InvalidOperationException("Paint source property is unavailable.");
            var kind = InspectorHelpers.RequireRelative(property, "kind");
            var kindContext = new SerializedPropertyContext(kind);
            var root = new VisualElement();

            void Rebuild()
            {
                var current = context.Binding.FindSerializedProperty();
                if (current == null)
                {
                    InspectorVisuals.ClearContent(root);
                    return;
                }

                var currentKind = InspectorHelpers.RequireRelative(current, "kind");
                InspectorVisuals.ClearContent(root);
                var kindField = SerializedPropertyField.Create(currentKind, context.Label);
                kindField.RegisterCallback<ChangeEvent<Enum>>(_ => Rebuild());
                root.Add(kindField);
                if (currentKind.hasMultipleDifferentValues) return;

                var color = InspectorHelpers.RequireRelative(current, "color");
                switch ((PaintSourceKind)currentKind.intValue)
                {
                    case PaintSourceKind.Gradient:
                        root.Add(SerializedPropertyField.Create(color, "Tint"));
                        root.Add(SerializedPropertyField.CreateRelative(current, "gradient", "Gradient"));
                        break;
                    case PaintSourceKind.Texture:
                        root.Add(SerializedPropertyField.Create(color, "Tint"));
                        root.Add(SerializedPropertyField.CreateRelative(current, "texture", "Texture"));
                        break;
                    default:
                        root.Add(SerializedPropertyField.Create(color, "Color"));
                        break;
                }
            }

            return kindContext.Observe(root, Rebuild);
        }
    }

    /// <summary>Shows the complete consumer-neutral paint projection.</summary>
    [CustomPropertyDrawer(typeof(PaintProjection))]
    internal sealed class PaintProjectionDrawer : LightSidePropertyDrawer<PaintProjection>
    {
        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var property = context.Binding.FindSerializedProperty();
            if (property == null)
                throw new InvalidOperationException("Paint projection property is unavailable.");

            var root = new VisualElement();
            root.Add(SerializedPropertyField.Create(
                InspectorHelpers.RequireRelative(property, "kind"), context.Label));
            root.Add(SerializedPropertyField.CreateRelative(property, "fit"));
            root.Add(SerializedPropertyField.CreateRelative(property, "angle"));
            root.Add(SerializedPropertyField.CreateRelative(property, "scale"));
            root.Add(SerializedPropertyField.CreateRelative(property, "spread"));
            root.Add(SerializedPropertyField.CreateRelative(property, "offset"));
            return root;
        }
    }
}
