using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public static partial class GlassImageMenuItems
{
#if UNITY_6000_3_OR_NEWER
    [MenuItem("GameObject/UI (Canvas)/Glass Image", false, 3)]
#else
    [MenuItem("GameObject/UI/Glass Image", false, 3)]
#endif
    private static void CreateGlassImage(MenuCommand menuCommand)
    {
        var go = new GameObject("Glass Image", typeof(RectTransform));
        PlaceUIElementRoot(go, menuCommand);
        Finish(Undo.AddComponent<GlassImage>(go));
    }

    [MenuItem("CONTEXT/UIGlass/Convert to GlassImage", false, 0)]
    private static void FromUIGlass(MenuCommand menuCommand)
    {
        var source = menuCommand.context as UIGlass;
        if (!source)
            return;

        var go = source.gameObject;
        if (go.GetComponent<Graphic>())
        {
            Debug.LogError($"Cannot convert from {nameof(UIGlass)} to {nameof(GlassImage)}. {go.name} already contains a Graphic component!");
            return;
        }

        var referenceSource = source.referenceSource;
        var cameraReference = source.cameraReference;
        var featureNumber = source.featureNumber;
        var operation = source.operation;
        var shapeType = source.sdfSource == GlassSdfSource.SpriteAlpha
            ? GlassImageShapeType.Sprite
            : source.shapeType == GlassShapeType.Canonical ? GlassImageShapeType.Canonical : GlassImageShapeType.PerCorner;
        var shapeExponent = source.shapeExponent;
        var canonicalCornerRadius = source.canonicalCornerRadius;
        var sdfSprite = source.sdfSprite;
        var alphaThreshold = source.alphaThreshold;
        var surfaceSmoothness = source.surfaceSmoothness;
        var refractionStrength = source.refractionStrength;
        var refractiveIndex = source.refractiveIndex;
        var abbeNumber = source.abbeNumber;
        var shape = source.shape;
        var appearance = source.appearance;
        var glassColor = appearance.color;
        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<GlassImage>(go);
        CopyGlassProps(shape, appearance, target.shape, target.appearance);
        target.color = glassColor;
        target.refractionStrength = refractionStrength;
        target.refractiveIndex = refractiveIndex;
        target.abbeNumber = abbeNumber;
        target.referenceSource = referenceSource;
        target.cameraReference = cameraReference;
        target.featureNumber = featureNumber;
        target.shapeType = shapeType;
        target.shapeExponent = shapeExponent;
        target.canonicalCornerRadius = canonicalCornerRadius;
        target.alphaThreshold = alphaThreshold;
        target.surfaceSmoothness = surfaceSmoothness;
        if (shapeType == GlassImageShapeType.Sprite)
            target.sprite = sdfSprite;
        Finish(target);

        if (operation != GlassSdfOperation.Add)
            Debug.LogWarning($"{nameof(GlassImage)} is an individual image effect, so the {nameof(UIGlass)} Cutout operation was not retained.", target);
    }

    [MenuItem("CONTEXT/GlassImage/Convert to UIGlass", false, 0)]
    private static void ToUIGlass(MenuCommand menuCommand)
    {
        var source = menuCommand.context as GlassImage;
        if (!source)
            return;

        var go = source.gameObject;
        if (go.GetComponent<UIGlass>())
        {
            Debug.LogError($"Cannot convert from {nameof(GlassImage)} to {nameof(UIGlass)}. {go.name} already contains a {nameof(UIGlass)} component!");
            return;
        }

        var referenceSource = source.referenceSource;
        var cameraReference = source.cameraReference;
        var featureNumber = source.featureNumber;
        var shapeType = source.shapeType;
        var shapeExponent = source.shapeExponent;
        var canonicalCornerRadius = source.canonicalCornerRadius;
        var sprite = source.sprite;
        var refractionStrength = source.refractionStrength;
        var refractiveIndex = source.refractiveIndex;
        var abbeNumber = source.abbeNumber;
        var alphaThreshold = source.alphaThreshold;
        var surfaceSmoothness = source.surfaceSmoothness;
        var shape = source.shape;
        var appearance = source.appearance;
        var glassColor = source.color;
        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<UIGlass>(go);
        CopyGlassProps(shape, appearance, target.shape, target.appearance);
        target.appearance.color = glassColor;
        target.shapeType = shapeType == GlassImageShapeType.Canonical ? GlassShapeType.Canonical : GlassShapeType.PerCorner;
        target.shapeExponent = shapeExponent;
        target.canonicalCornerRadius = canonicalCornerRadius;
        target.alphaThreshold = alphaThreshold;
        target.surfaceSmoothness = surfaceSmoothness;
        if (shapeType == GlassImageShapeType.Sprite)
        {
            target.sdfSource = GlassSdfSource.SpriteAlpha;
            target.sdfSprite = sprite;
        }
        target.refractionStrength = refractionStrength;
        target.refractiveIndex = refractiveIndex;
        target.abbeNumber = abbeNumber;
        target.referenceSource = referenceSource;
        target.cameraReference = cameraReference;
        target.featureNumber = featureNumber;
        Finish(target);
    }

    [MenuItem("CONTEXT/Image/Convert to GlassImage", false, 2)]
    private static void FromImage(MenuCommand menuCommand)
    {
        var source = menuCommand.context as Image;
        if (!source || source.GetType() != typeof(Image))
            return;

        var go = source.gameObject;
        var image = new ImageSnapshot(source);
        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<GlassImage>(go);
        image.Apply(target);
        target.shapeType = GlassImageShapeType.Sprite;
        Finish(target);
    }

    [MenuItem("CONTEXT/GlassImage/Convert to Image", false, 99)]
    private static void ToImage(MenuCommand menuCommand)
    {
        var source = menuCommand.context as GlassImage;
        if (!source)
            return;

        var go = source.gameObject;
        var image = new ImageSnapshot(source);
        Undo.DestroyObjectImmediate(source);
        image.Apply(Undo.AddComponent<Image>(go));
        HandleMask(go);
    }

    private static void Finish(GlassImage target)
    {
        FlexibleGlassMenuItems.TrySetGlassCamera(target, target.gameObject);
        _ = target.materialForRendering;
        HandleMask(target.gameObject);
    }

    private static void Finish(UIGlass target)
    {
        FlexibleGlassMenuItems.TrySetGlassCamera(target, target.gameObject);
        HandleMask(target.gameObject);
    }

    private static void CopyGlassProps(GlassShapeSettings sourceShape, GlassAppearance sourceAppearance, GlassShapeSettings targetShape, GlassAppearance targetAppearance)
    {
        targetShape.normalizeCorners = sourceShape.normalizeCorners;
        targetShape.squircle = sourceShape.squircle;
        targetShape.cornerRadii = sourceShape.cornerRadii;
        targetShape.cornerRoundness = sourceShape.cornerRoundness;

        targetAppearance.color = sourceAppearance.color;
        targetAppearance.colorMix = sourceAppearance.colorMix;
        targetAppearance.magnification = sourceAppearance.magnification;
        targetAppearance.transmission = sourceAppearance.transmission;
        targetAppearance.thicknessUnits = sourceAppearance.thicknessUnits;
        targetAppearance.thickness = sourceAppearance.thickness;
        targetAppearance.lipLightUnits = sourceAppearance.lipLightUnits;
        targetAppearance.innerEdgeLightThickness = sourceAppearance.innerEdgeLightThickness;
        targetAppearance.outerEdgeLightThickness = sourceAppearance.outerEdgeLightThickness;
        targetAppearance.shadowColor = sourceAppearance.shadowColor;
        targetAppearance.shadowSize = sourceAppearance.shadowSize;
        targetAppearance.shadowOffset = sourceAppearance.shadowOffset;
    }

    private readonly struct ImageSnapshot
    {
        private readonly Color color;
        private readonly Sprite sprite;
        private readonly bool raycastTarget;
        private readonly Vector4 raycastPadding;
        private readonly Image.Type type;
        private readonly bool useSpriteMesh, preserveAspect, fillCenter, fillClockwise;
        private readonly Image.FillMethod fillMethod;
        private readonly int fillOrigin;
        private readonly float fillAmount, pixelsPerUnitMultiplier;

        public ImageSnapshot(Image source) =>
            (color, sprite, raycastTarget, raycastPadding, type, useSpriteMesh, preserveAspect, fillCenter, fillMethod, fillOrigin, fillAmount, fillClockwise, pixelsPerUnitMultiplier) =
            (source.color, source.sprite, source.raycastTarget, source.raycastPadding, source.type, source.useSpriteMesh, source.preserveAspect, source.fillCenter, source.fillMethod, source.fillOrigin, source.fillAmount, source.fillClockwise, source.pixelsPerUnitMultiplier);

        public void Apply(Image target)
        {
            target.color = color;
            target.sprite = sprite;
            target.raycastTarget = raycastTarget;
            target.raycastPadding = raycastPadding;
            target.type = type;
            target.useSpriteMesh = useSpriteMesh;
            target.preserveAspect = preserveAspect;
            target.fillCenter = fillCenter;
            target.fillMethod = fillMethod;
            target.fillOrigin = fillOrigin;
            target.fillAmount = fillAmount;
            target.fillClockwise = fillClockwise;
            target.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
        }
    }

    private static void PlaceUIElementRoot(GameObject element, MenuCommand menuCommand)
    {
        var parent = menuCommand.context as GameObject;
        if (!parent || !parent.GetComponentInParent<Canvas>())
            parent = MenuItemCommon.GetOrCreateCanvasGameObject();
        element.name = GameObjectUtility.GetUniqueNameForSibling(parent.transform, element.name);
        Undo.RegisterCreatedObjectUndo(element, "Create " + element.name);
        Undo.SetTransformParent(element.transform, parent.transform, "Parent " + element.name);
        GameObjectUtility.SetParentAndAlign(element, parent);
        Selection.activeGameObject = element;
    }

    private static void HandleMask(GameObject go)
    {
        var mask = go.GetComponent<Mask>();
        if (!mask)
            return;
        var enabled = mask.enabled;
        var showGraphic = mask.showMaskGraphic;
        Object.DestroyImmediate(mask);
        mask = go.AddComponent<Mask>();
        mask.enabled = enabled;
        mask.showMaskGraphic = showGraphic;
    }
}
}
