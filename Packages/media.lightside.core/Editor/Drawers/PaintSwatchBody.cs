using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// The shared body of a "name + <see cref="Paint"/>" swatch drawer: the primary header row (name,
    /// kind, value), the kind-gated projection rows, and the structure key that keeps value edits from
    /// rebuilding them. A package drawer wraps it in its own foldout shell and may insert extra rows
    /// after the Tint row.
    /// </summary>
    public static class PaintSwatchBody
    {
        /// <summary>
        /// Computes the structure key of a swatch body against <paramref name="swatch"/> — everything
        /// the body's layout depends on, and nothing a bound field tracks by itself.
        /// </summary>
        public static object Structure(SerializedProperty swatch, SerializedPropertyBinding binding)
        {
            var paint = InspectorHelpers.RequireRelative(swatch, "paint");
            var source = InspectorHelpers.RequireRelative(paint, "source");
            var projection = InspectorHelpers.RequireRelative(paint, "projection");
            var kindProperty = InspectorHelpers.RequireRelative(source, "kind");
            var kind = (PaintSourceKind)kindProperty.intValue;
            var fitProperty = InspectorHelpers.RequireRelative(projection, "fit");
            var fit = kind == PaintSourceKind.Texture ? (PaintFit)fitProperty.intValue : default;
            var mixedFit = kind == PaintSourceKind.Texture && fitProperty.hasMultipleDifferentValues;
            var textureProperty = InspectorHelpers.RequireRelative(source, "texture");
            var texture = kind == PaintSourceKind.Texture
                ? textureProperty.objectReferenceValue as Texture
                : null;
            var textureId = kind == PaintSourceKind.Texture && textureProperty.hasMultipleDifferentValues
                ? int.MaxValue
                : fit != PaintFit.Tile || texture == null
                ? 0
                : ObjectUtils.GetInstanceIdCompat(texture);
            var shapeProperty = InspectorHelpers.RequireRelative(projection, "kind");
            var mixedShape = shapeProperty.hasMultipleDifferentValues;
            var shape = (PaintProjectionKind)shapeProperty.intValue;
            return (
                kind,
                kindProperty.hasMultipleDifferentValues,
                fit,
                mixedFit,
                textureId,
                !kindProperty.hasMultipleDifferentValues && !mixedFit &&
                kind == PaintSourceKind.Texture && fit == PaintFit.Tile && NeedsRepeat(binding),
                kind == PaintSourceKind.Gradient && ShowsSpread(mixedShape, shape),
                kind == PaintSourceKind.Gradient && ShowsFit(mixedShape, shape));
        }

        /// <summary>
        /// Builds the swatch body into <paramref name="foldout"/>: the primary row into its header
        /// actions, the blend and kind-gated projection rows into its content.
        /// <paramref name="extraRows"/> inserts package rows after the Tint row.
        /// </summary>
        public static void Build(InspectorSerializedFoldout foldout, SerializedProperty swatch,
            SerializedPropertyBinding binding, Action<VisualElement, SerializedProperty> extraRows = null)
        {
            var paintProperty = InspectorHelpers.RequireRelative(swatch, "paint");
            var sourceProperty = InspectorHelpers.RequireRelative(paintProperty, "source");
            var projectionProperty = InspectorHelpers.RequireRelative(paintProperty, "projection");
            var kindProperty = InspectorHelpers.RequireRelative(sourceProperty, "kind");
            var mixedKind = kindProperty.hasMultipleDifferentValues;
            var kind = (PaintSourceKind)kindProperty.intValue;
            var fitProperty = InspectorHelpers.RequireRelative(projectionProperty, "fit");
            var mixedFit = fitProperty.hasMultipleDifferentValues;
            var needsRepeat = !mixedKind && !mixedFit && kind == PaintSourceKind.Texture &&
                              (PaintFit)fitProperty.intValue == PaintFit.Tile && NeedsRepeat(binding);
            var shapeProperty = InspectorHelpers.RequireRelative(projectionProperty, "kind");
            var mixedShape = shapeProperty.hasMultipleDifferentValues;
            var shape = (PaintProjectionKind)shapeProperty.intValue;

            InspectorVisuals.ClearContent(foldout.Header.Actions);
            var primary = InspectorVisuals.CreateCompactRow();
            primary.AddToClassList("lightside-paint-swatch__primary");
            var nameProperty = InspectorHelpers.RequireRelative(swatch, "name");
            var nameField = SerializedPropertyField.Create(nameProperty, nameProperty.displayName);
            nameField.AddToClassList("lightside-paint-swatch__name");
            var kindField = SerializedPropertyField.Create(kindProperty, string.Empty);
            kindField.AddToClassList("lightside-paint-swatch__kind");
            primary.Add(nameField);
            primary.Add(kindField);
            if (!mixedKind)
            {
                var valueField = SerializedPropertyField.Create(InspectorHelpers.RequireRelative(sourceProperty,
                    kind switch
                    {
                        PaintSourceKind.Gradient => "gradient",
                        PaintSourceKind.Texture => "texture",
                        _ => "color",
                    }), string.Empty);
                valueField.AddToClassList("lightside-paint-swatch__value");
                primary.Add(valueField);
            }
            foldout.Header.Actions.Add(primary);
            foldout.Add(SerializedPropertyField.CreateRelative(paintProperty, "blend"));
            if (mixedKind || kind == PaintSourceKind.Solid) return;

            if (kind == PaintSourceKind.Gradient)
            {
                foldout.Add(SerializedPropertyField.Create(shapeProperty, "Shape"));
                if (ShowsFit(mixedShape, shape))
                    foldout.Add(SerializedPropertyField.Create(fitProperty));
            }
            else
            {
                foldout.Add(SerializedPropertyField.Create(fitProperty));
                if (needsRepeat) foldout.Add(RepeatWarning());
            }
            foldout.Add(SerializedPropertyField.CreateRelative(sourceProperty, "color", "Tint"));
            extraRows?.Invoke(foldout, swatch);
            foldout.Add(SerializedPropertyField.CreateRelative(projectionProperty, "angle"));
            foldout.Add(SerializedPropertyField.Create(
                InspectorHelpers.RequireRelative(projectionProperty, "scale"),
                !mixedFit && (PaintFit)fitProperty.intValue == PaintFit.Tile ? "Tiling" : null));
            if (kind == PaintSourceKind.Gradient && ShowsSpread(mixedShape, shape))
                foldout.Add(SerializedPropertyField.CreateRelative(projectionProperty, "spread"));
            foldout.Add(SerializedPropertyField.CreateRelative(projectionProperty, "offset"));
        }

        /// <summary>Whether a gradient's spread row applies — every projection but Angular reads it.</summary>
        public static bool ShowsSpread(bool mixedShape, PaintProjectionKind shape)
            => mixedShape || shape != PaintProjectionKind.Angular;

        /// <summary>Whether a gradient's fit row applies — every projection but Linear reads it.</summary>
        public static bool ShowsFit(bool mixedShape, PaintProjectionKind shape)
            => mixedShape || shape != PaintProjectionKind.Linear;

        /// <summary>The warning shown when Tile fit meets a texture whose Wrap Mode is not Repeat.</summary>
        public static HelpBox RepeatWarning()
            => new("Tile needs every selected texture's Wrap Mode set to Repeat.",
                HelpBoxMessageType.Warning);

        /// <summary>Whether any selected swatch samples a texture whose Wrap Mode is not Repeat.</summary>
        public static bool NeedsRepeat(SerializedPropertyBinding binding)
            => binding.AnyTargetProperty((_, swatch) =>
            {
                var texture = InspectorHelpers.RequireRelative(
                        InspectorHelpers.RequireRelative(
                            InspectorHelpers.RequireRelative(swatch, "paint"), "source"),
                        "texture")
                    .objectReferenceValue as Texture;
                return texture != null && texture.wrapMode != TextureWrapMode.Repeat;
            });
    }
}
