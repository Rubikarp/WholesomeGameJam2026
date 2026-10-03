using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(UniTextFont.GlyphOverride))]
    internal sealed class GlyphOverrideDrawer : LightSidePropertyDrawer<UniTextFont.GlyphOverride>
    {
        private static readonly string[] tileLabels = { "Auto", "64", "128", "256" };
        private static readonly int[] tileValues = { 0, 64, 128, 256 };

        [InitializeOnLoadMethod]
        private static void RegisterToolkitRenderers()
        {
            SerializedPropertyField.RegisterRenderer<List<UniTextFont.GlyphOverride>>(
                CreateList);
        }

        private static VisualElement CreateList(SerializedPropertyContext context)
            => SerializedPropertyField.CreateCollection(context.Property, context.Label,
                (_, insert) => insert(new UniTextFont.GlyphOverride
                {
                    advanceScale = 1f,
                    scale = 1f,
                }));

        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var property = context.Property;
            var glyphIndex = InspectorHelpers.RequireRelative(property, "glyphIndex");
            var tileSize = InspectorHelpers.RequireRelative(property, "tileSizeOverride");
            var root = InspectorVisuals.CreateStack();
            root.Add(SerializedPropertyField.Create(glyphIndex, "Glyph"));

            var tileIndex = Array.IndexOf(tileValues, tileSize.intValue);
            if (tileIndex < 0)
                throw new InvalidOperationException(
                    $"Glyph override '{property.propertyPath}' has unsupported tile size {tileSize.intValue}.");
            var tile = new SelectorField<string>("Tile", tileLabels, tileIndex);
            InspectorVisuals.MarkFieldAxis(tile);
            root.Add(new SerializedPropertyContext(tileSize, "Tile")
                .Bind(tile, label => tileValues[Array.IndexOf(tileLabels, label)],
                    read: current =>
                    {
                        var index = Array.IndexOf(tileValues, (int)current);
                        if (index < 0)
                            throw new InvalidOperationException(
                                $"Glyph override '{property.propertyPath}' has unsupported tile size {current}.");
                        return tileLabels[index];
                    },
                    undoName: "Change Glyph Tile Size"));
            root.Add(SerializedPropertyField.CreateRelative(property, "advanceScale", "Advance"));
            root.Add(SerializedPropertyField.CreateRelative(property, "offset", "Offset"));
            root.Add(SerializedPropertyField.CreateRelative(property, "scale", "Size"));
            return root;
        }
    }
}
