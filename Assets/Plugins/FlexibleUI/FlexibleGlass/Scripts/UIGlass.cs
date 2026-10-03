using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;

namespace JeffGrawAssets.FlexibleUI
{
[ExecuteAlways]
[AddComponentMenu("UI/UI Glass", 11)]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
public partial class UIGlass : MonoBehaviour
{
    public const float MaxLensDepth = 20f;

#if UNITY_EDITOR
    public const string ReferenceSourceFieldName = nameof(referenceSource);
    public const string CameraReferenceFieldName = nameof(cameraReference);
    public const string FeatureNumberFieldName = nameof(featureNumber);
    public const string OperationFieldName = nameof(operation);
    public const string SdfSourceFieldName = nameof(sdfSource);
    public const string SdfSpriteFieldName = nameof(sdfSprite);
    public const string AlphaThresholdFieldName = nameof(alphaThreshold);
    public const string ShapeTypeFieldName = nameof(shapeType);
    public const string ShapeExponentFieldName = nameof(shapeExponent);
    public const string CanonicalCornerRadiusFieldName = nameof(canonicalCornerRadius);
    public const string SurfaceSmoothnessModeFieldName = nameof(surfaceSmoothnessMode);
    public const string SurfaceSmoothnessFieldName = nameof(surfaceSmoothness);
    public const string DepthFallbackFieldName = nameof(depthFallback);
    public const string RefractionStrengthFieldName = nameof(refractionStrength);
    public const string RefractiveIndexFieldName = nameof(refractiveIndex);
    public const string AbbeNumberFieldName = nameof(abbeNumber);
    public const string ShapeFieldName = nameof(shape);
    public const string AppearanceFieldName = nameof(appearance);
#endif

#if UNITY_6000_6_OR_NEWER
    [Unity.Scripting.LifecycleManagement.AutoStaticsCleanup]
#endif
    public static readonly Dictionary<(Camera camera, int featureNumber), List<UIGlass>> GlassDict = new();
#if UNITY_6000_6_OR_NEWER
    [Unity.Scripting.LifecycleManagement.AutoStaticsCleanup]
#endif
    private static readonly List<(Camera camera, int featureNumber)> StaleRegistrationKeys = new(2);

    public GlassReferenceSource referenceSource;
    public Camera cameraReference;
    [Min(0)] public int featureNumber;
    public GlassSdfOperation operation;
    [Tooltip("Uses the authored Glass shape or derives the retained field from a Sprite's alpha.")]
    public GlassSdfSource sdfSource;
    [Tooltip("Sprite alpha used as the Glass silhouette when Shape Source is Sprite Alpha.")]
    public Sprite sdfSprite;
    [Range(GlassMath.MinimumAlphaThreshold, GlassMath.MaximumAlphaThreshold)]
    [Tooltip("Sprite alpha value treated as the retained field perimeter.")]
    public float alphaThreshold = 0.5f;
    public GlassShapeType shapeType;
    [Min(0f)]
    [Tooltip("Canonical corner radius in Canvas units. Clamped to half the shorter side; resizing the panel otherwise leaves the corners unchanged.")]
    public float canonicalCornerRadius = 28f;
    [Range(2f, 16f)]
    [Tooltip("Canonical corner curve power. 2 gives circular corners; higher values flatten the transition into the straight sides.")]
    public float shapeExponent = 6.5f;
    [Tooltip("Auto adjusts smoothing from the Canonical corner radius and Roundness. Custom uses the Smoothness value directly. Auto applies only to Canonical shapes.")]
    public GlassSurfaceSmoothnessMode surfaceSmoothnessMode;
    [Range(0.01f, 5f)]
    [Tooltip("Controls the retained optical-field smoothing radius relative to the Optical Lip. Higher values smooth across a wider area.")]
    public float surfaceSmoothness = 0.75f;
    [SerializeField]
    [Tooltip("Falls back to geometric depth to prevent hard interior refraction seams at high smoothness. Can add an interior contour to some concave shapes. Blends with adjacent UIGlass appearance settings; does not rebuild the field.")]
    private bool depthFallback;
    public bool DepthFallback { get => depthFallback; set => depthFallback = value; }
    [Range(0f, 4f)]
    [Tooltip("Virtual travel depth relative to Optical Lip. Zero disables refraction, and one is the normal physical range.")]
    public float refractionStrength = 2f;
    [Range(1f, 2.5f)]
    [Tooltip("Refractive index at the Fraunhofer d spectral line.")]
    public float refractiveIndex = 1.5f;
    [Tooltip("Abbe number controlling wavelength dispersion. Higher values separate colors less.")]
    [Range(GlassMath.MinimumAbbeNumber, GlassMath.MaximumAbbeNumber)]
    public float abbeNumber = GlassMath.MaximumAbbeNumber;
    public GlassShapeSettings shape = new();
    public GlassAppearance appearance = new();

    public Canvas Canvas => canvas;
    public Rect ScreenBounds { get; private set; }

    private readonly Vector3[] worldCorners = new Vector3[4];
    private readonly Vector2[] screenCorners = new Vector2[4];
    private Canvas canvas;
    private CanvasRenderer canvasRenderer;
    private RectTransform rectTransform;
    private (Camera camera, int featureNumber) registeredKey;
    private bool registered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ClearRegistrations();
#if UNITY_EDITOR
        foreach (var glass in Resources.FindObjectsOfTypeAll<UIGlass>())
            if (glass && glass.gameObject.scene.IsValid() && glass.isActiveAndEnabled)
            {
                glass.CacheComponents();
                glass.RefreshRegistration();
            }
#endif
    }

    private static void ClearRegistrations() => GlassDict.Clear();

    private void OnEnable()
    {
        shape ??= new GlassShapeSettings();
        appearance ??= new GlassAppearance();
        CacheComponents();
        RefreshRegistration();
    }

    private void OnDisable() => RemoveRegistration();

    private void OnDestroy() => RemoveRegistration();

    private void OnTransformParentChanged()
    {
        CacheComponents();
        RefreshRegistration();
    }

    private void LateUpdate() => RefreshRegistration();

#if UNITY_EDITOR
    private void OnValidate()
    {
        shape ??= new GlassShapeSettings();
        appearance ??= new GlassAppearance();
        featureNumber = Mathf.Max(0, featureNumber);
        shapeExponent = Mathf.Clamp(shapeExponent, 2f, 16f);
        canonicalCornerRadius = Mathf.Max(0f, canonicalCornerRadius);
        alphaThreshold = GlassMath.ClampAlphaThreshold(alphaThreshold);
        surfaceSmoothness = Mathf.Clamp(surfaceSmoothness, 0.01f, 5f);
        refractionStrength = Mathf.Clamp(refractionStrength, 0f, MaxLensDepth);
        refractiveIndex = Mathf.Clamp(refractiveIndex, 1f, 2.5f);
        abbeNumber = Mathf.Clamp(abbeNumber, GlassMath.MinimumAbbeNumber, GlassMath.MaximumAbbeNumber);
        if (isActiveAndEnabled)
        {
            CacheComponents();
            RefreshRegistration();
        }
    }
#endif

    public (Camera camera, int featureNumber) GetCameraFeatureKey()
    {
        if (referenceSource == GlassReferenceSource.ReferenceProvider && canvas && GlassReferenceProvider.CameraReferenceDict.TryGetValue(canvas, out var providerReference) && providerReference.camera)
            return providerReference;

        return (cameraReference ? cameraReference : Camera.main, Mathf.Max(0, featureNumber));
    }

    internal bool TryBuildGpuData(Camera sourceCamera, Vector2 renderScale, out GlassElementGpu element, out Rect blurBounds, out Rect rasterBounds)
        => TryBuildGpuData(sourceCamera, renderScale, out element, out blurBounds, out rasterBounds, out _);

    internal bool TryBuildSdfDescriptor(Camera sourceCamera, out GlassSdfDescriptor descriptor)
    {
        descriptor = default;
        if (!isActiveAndEnabled || !sourceCamera || !canvas || !canvas.isActiveAndEnabled || !rectTransform || !gameObject.activeInHierarchy)
            return false;
        if (sdfSource == GlassSdfSource.SpriteAlpha && !sdfSprite || GetCameraFeatureKey().camera != sourceCamera)
            return false;

        var size = rectTransform.rect.size;
        size = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
        if (size.x <= 1e-5f || size.y <= 1e-5f || canvasRenderer && canvasRenderer.GetInheritedAlpha() <= 0f || appearance.color.a <= 0f)
            return false;

        descriptor = BuildSdfDescriptor(size);
        return true;
    }

    internal bool TryBuildGpuData(Camera sourceCamera, Vector2 renderScale, out GlassElementGpu element, out Rect blurBounds, out Rect rasterBounds, out GlassSdfDescriptor sdfDescriptor, GlassScreenProjection projection = default)
    {
        element = default;
        blurBounds = default;
        rasterBounds = default;
        sdfDescriptor = default;
        if (!isActiveAndEnabled || !sourceCamera || !canvas || !canvas.isActiveAndEnabled || !rectTransform || !gameObject.activeInHierarchy)
            return false;
        if (sdfSource == GlassSdfSource.SpriteAlpha && !sdfSprite)
            return false;

        var sourceKey = GetCameraFeatureKey();
        if (sourceKey.camera != sourceCamera)
            return false;

        var size = rectTransform.rect.size;
        size = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
        if (size.x <= 1e-5f || size.y <= 1e-5f)
            return false;

        rectTransform.GetWorldCorners(worldCorners);
        var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera ? canvas.worldCamera : sourceCamera;
        var cameraOffset = sourceCamera.pixelRect.position;
        for (int i = 0; i < 4; i++)
        {
            Vector3 screenPoint;
            if (uiCamera)
            {
                screenPoint = projection.WorldToScreenPoint(uiCamera, worldCorners[i]);
                if (screenPoint.z <= 1e-5f)
                    return false;
            }
            else
            {
                screenPoint = worldCorners[i];
            }

            screenCorners[i] = Vector2.Scale((Vector2)screenPoint - cameraOffset, renderScale);
        }

        var edge01 = screenCorners[1] - screenCorners[0];
        var edge02 = screenCorners[2] - screenCorners[0];
        var edge03 = screenCorners[3] - screenCorners[0];
        var twiceArea = Mathf.Abs(edge01.x * edge02.y - edge01.y * edge02.x + edge02.x * edge03.y - edge02.y * edge03.x);
        var maximumSpanSqr = Mathf.Max(edge02.sqrMagnitude, (screenCorners[3] - screenCorners[1]).sqrMagnitude, 1f);
        if (twiceArea <= maximumSpanSqr * 1e-5f)
            return false;

        if (!GlassMath.TryBuildScreenToUv(screenCorners[0], screenCorners[3], screenCorners[2], screenCorners[1], out var row0, out var row1, out var row2))
            return false;
        var screenCentroid = (screenCorners[0] + screenCorners[1] + screenCorners[2] + screenCorners[3]) * 0.25f;
        var centerDenominator = Vector3.Dot((Vector3)row2, new Vector3(screenCentroid.x, screenCentroid.y, 1f));
        if (Mathf.Abs(centerDenominator) <= 1e-7f)
            return false;
        var homographyScale = 1f / centerDenominator;
        row0 *= homographyScale;
        row1 *= homographyScale;
        row2 *= homographyScale;

        var min = screenCorners[0];
        var max = screenCorners[0];
        for (int i = 1; i < 4; i++)
        {
            min = Vector2.Min(min, screenCorners[i]);
            max = Vector2.Max(max, screenCorners[i]);
        }

        var averageRenderScale = (renderScale.x + renderScale.y) * 0.5f;
        var maximumRenderScale = Mathf.Max(renderScale.x, renderScale.y);
        var canvasScale = Mathf.Max(canvas.scaleFactor, 1e-4f);
        var projectedWidth = Mathf.Min(Vector2.Distance(screenCorners[0], screenCorners[3]), Vector2.Distance(screenCorners[1], screenCorners[2]));
        var projectedHeight = Mathf.Min(Vector2.Distance(screenCorners[0], screenCorners[1]), Vector2.Distance(screenCorners[3], screenCorners[2]));
        var projectedWidthScale = projectedWidth / size.x;
        var projectedHeightScale = projectedHeight / size.y;
        var projectedScale = Mathf.Min(projectedWidthScale, projectedHeightScale);
        var worldCenter = rectTransform.TransformPoint(rectTransform.rect.center);
        var screenCenter = Vector2.Scale((Vector2)(uiCamera ? projection.WorldToScreenPoint(uiCamera, worldCenter) : worldCenter) - cameraOffset, renderScale);
        var thickness = appearance.GetResolvedThickness(size) * projectedScale;
        var refraction = Mathf.Clamp(refractionStrength, 0f, MaxLensDepth);
        var maximumRefractiveIndex = GlassMath.GetRefractiveIndex(refractiveIndex, abbeNumber, 486.1327f);
        var refractionPadding = Mathf.Sqrt(Mathf.Max(0f, maximumRefractiveIndex * maximumRefractiveIndex - 1f)) * thickness * refraction / appearance.GetMagnification();
        var opticalPadding = Mathf.Max(2f, refractionPadding + thickness + 2f);
        blurBounds = Rect.MinMaxRect(min.x - opticalPadding, min.y - opticalPadding, max.x + opticalPadding, max.y + opticalPadding);

        var surfacePadding = 2f * maximumRenderScale;
        var rasterMin = min - Vector2.one * surfacePadding;
        var rasterMax = max + Vector2.one * surfacePadding;
        var hasShadow = appearance.HasVisibleShadow();
        if (hasShadow)
        {
            var shadowOffset = appearance.shadowOffset / canvasScale;
            var localRect = rectTransform.rect;
            for (int i = 0; i < 4; i++)
            {
                var localCorner = new Vector3(
                    (i < 2 ? localRect.xMin : localRect.xMax) + shadowOffset.x,
                    (i == 0 || i == 3 ? localRect.yMin : localRect.yMax) + shadowOffset.y);
                var worldCorner = rectTransform.TransformPoint(localCorner);
                var shadowScreenPoint = uiCamera ? projection.WorldToScreenPoint(uiCamera, worldCorner) : worldCorner;
                if (uiCamera && shadowScreenPoint.z <= 1e-5f)
                {
                    rasterMin = Vector2.zero;
                    rasterMax = Vector2.Scale(new Vector2(sourceCamera.pixelWidth, sourceCamera.pixelHeight), renderScale);
                    break;
                }
                var shadowScreenCorner = Vector2.Scale((Vector2)shadowScreenPoint - cameraOffset, renderScale);
                rasterMin = Vector2.Min(rasterMin, shadowScreenCorner);
                rasterMax = Vector2.Max(rasterMax, shadowScreenCorner);
            }
            var shadowReach = (appearance.GetShadowDistanceSupport() + GlassAppearance.ShadowAntialiasPadding) * averageRenderScale;
            rasterMin -= Vector2.one * shadowReach;
            rasterMax += Vector2.one * shadowReach;
        }
        rasterBounds = Rect.MinMaxRect(rasterMin.x, rasterMin.y, rasterMax.x, rasterMax.y);
        ScreenBounds = rasterBounds;

        var inheritedAlpha = canvasRenderer ? canvasRenderer.GetInheritedAlpha() : 1f;
        if (inheritedAlpha <= 0f || appearance.color.a <= 0f)
            return false;

        var glassColor = QualitySettings.activeColorSpace == ColorSpace.Linear ? appearance.color.linear : appearance.color;
        row0 *= size.x;
        row1 *= size.y;
        element.screenToUv0 = row0;
        element.screenToUv1 = row1;
        element.screenToUv2 = row2;
        sdfDescriptor = BuildSdfDescriptor(size);
        element.sizeOperationShape = new Vector4(size.x, size.y, (float)operation, 0f);
        element.color = new Vector4(glassColor.r, glassColor.g, glassColor.b, Mathf.Clamp01(appearance.colorMix));
        element.optics0 = new Vector4(refraction, appearance.transmission, appearance.GetMagnification(), screenCenter.x);
        element.optics1 = new Vector4(thickness, Mathf.Clamp(refractiveIndex, 1f, 2.5f), appearance.color.a * inheritedAlpha, screenCenter.y);
        var shadowColor = appearance.shadowColor;
        if (!hasShadow)
            shadowColor.a = 0f;
        else
        {
            // A projected surface narrower than the shadow kernel cannot carry the
            // opacity of an infinitely wide source. Preserve its projected coverage
            // so nearly edge-on panels fade instead of becoming bright shadow lines.
            var maximumProjectedEdge = Mathf.Max(
                Vector2.Distance(screenCorners[0], screenCorners[1]),
                Vector2.Distance(screenCorners[1], screenCorners[2]),
                Vector2.Distance(screenCorners[2], screenCorners[3]),
                Vector2.Distance(screenCorners[3], screenCorners[0]),
                1e-4f);
            var projectedMinorSpan = twiceArea / (2f * maximumProjectedEdge);
            var shadowKernelWidth = 2f * Mathf.Max(appearance.shadowSize, 0.5f) * averageRenderScale;
            shadowColor.a *= Mathf.Clamp01(projectedMinorSpan / Mathf.Max(shadowKernelWidth, 1e-4f));
        }
        var lipLightWidths = appearance.GetLipLightExtents(size);
        var packedEdgeLightWidths = GlassMath.PackColor(new Color(lipLightWidths.x, lipLightWidths.y, 0f, 0f));
        element.lighting = new Vector4(packedEdgeLightWidths, depthFallback ? 1f : 0f, Mathf.Clamp(abbeNumber, GlassMath.MinimumAbbeNumber, GlassMath.MaximumAbbeNumber), GlassMath.PackColor(shadowColor));
        element.shadow = new Vector4(canvasScale * averageRenderScale, appearance.shadowSize * averageRenderScale, appearance.shadowOffset.x / canvasScale, appearance.shadowOffset.y / canvasScale);
        element.sdfData.w = GetResolvedSurfaceSmoothness(size);
        return true;
    }

    public float GetResolvedSurfaceSmoothness(Vector2 size)
    {
        if (surfaceSmoothnessMode == GlassSurfaceSmoothnessMode.Auto && sdfSource == GlassSdfSource.Shape && shapeType == GlassShapeType.Canonical)
            return GlassMath.ResolveCanonicalSurfaceSmoothness(appearance.GetResolvedThickness(size), canonicalCornerRadius, shapeExponent, size);
        return Mathf.Clamp(surfaceSmoothness, 0.01f, 5f);
    }

    private GlassSdfDescriptor BuildSdfDescriptor(Vector2 size)
    {
        var canonical = shapeType == GlassShapeType.Canonical;
        var squircle = !canonical && shape.squircle;
        var shapeIndex = canonical ? 0 : squircle ? 2 : 1;
        var cornerRadii = canonical ? Vector4.one * GlassMath.ResolveCanonicalRadius(canonicalCornerRadius, size) : shape.GetCornerRadii(size);
        var cornerShape = canonical
            ? new Vector4(Mathf.Clamp(shapeExponent, 2f, 16f), 0f, 0f, 0f)
            : squircle
                ? Vector4.one * 2f + Vector4.Min(Vector4.one, Vector4.Max(Vector4.zero, shape.cornerRoundness)) * 8f
                : Vector4.one - Vector4.Min(Vector4.one, Vector4.Max(-Vector4.one, shape.cornerRoundness));
        var texture = sdfSource == GlassSdfSource.SpriteAlpha ? sdfSprite.texture : null;
        var textureUv = sdfSource == GlassSdfSource.SpriteAlpha ? DataUtility.GetOuterUV(sdfSprite) : Vector4.zero;
        return new GlassSdfDescriptor(sdfSource, shapeIndex, size, Vector2.Max(size / 32f, Vector2.one), cornerRadii, cornerShape, texture, textureUv, GlassMath.ClampAlphaThreshold(alphaThreshold), sdfSource == GlassSdfSource.SpriteAlpha ? sdfSprite : null);
    }

    internal static void PruneInvalidRegistrations()
    {
        StaleRegistrationKeys.Clear();
        foreach (var entry in GlassDict)
        {
            var glass = entry.Value;
            for (int i = glass.Count - 1; i >= 0; i--)
                if (!glass[i] || !glass[i].isActiveAndEnabled)
                    glass.RemoveAt(i);
            if (!entry.Key.camera || glass.Count == 0)
                StaleRegistrationKeys.Add(entry.Key);
        }
        foreach (var key in StaleRegistrationKeys)
            GlassDict.Remove(key);
        StaleRegistrationKeys.Clear();
    }

    internal static int CompareHierarchy(UIGlass left, UIGlass right)
    {
        if (left == right)
            return 0;
        if (!left)
            return 1;
        if (!right)
            return -1;

#if UNITY_6000_4_OR_NEWER
        var order = left.gameObject.scene.handle.GetRawData().CompareTo(right.gameObject.scene.handle.GetRawData());
#else
        var order = ((int)left.gameObject.scene.handle).CompareTo((int)right.gameObject.scene.handle);
#endif
        if (order != 0)
            return order;

        return CompareTransforms(left.transform, right.transform);
    }

    private static int CompareTransforms(Transform left, Transform right)
    {
        var leftDepth = GetDepth(left);
        var rightDepth = GetDepth(right);
        var leftCursor = left;
        var rightCursor = right;

        while (leftDepth > rightDepth)
        {
            leftCursor = leftCursor.parent;
            leftDepth--;
            if (leftCursor == right)
                return 1;
        }
        while (rightDepth > leftDepth)
        {
            rightCursor = rightCursor.parent;
            rightDepth--;
            if (rightCursor == left)
                return -1;
        }

        while (leftCursor.parent != rightCursor.parent)
        {
            leftCursor = leftCursor.parent;
            rightCursor = rightCursor.parent;
        }

        var siblingOrder = leftCursor.GetSiblingIndex().CompareTo(rightCursor.GetSiblingIndex());
#if UNITY_6000_4_OR_NEWER
        return siblingOrder != 0 ? siblingOrder : left.GetEntityId().CompareTo(right.GetEntityId());
#else
        return siblingOrder != 0 ? siblingOrder : left.GetInstanceID().CompareTo(right.GetInstanceID());
#endif

        static int GetDepth(Transform value)
        {
            var depth = 0;
            while (value.parent)
            {
                depth++;
                value = value.parent;
            }
            return depth;
        }
    }

    private void CacheComponents()
    {
        canvas = GetComponentInParent<Canvas>();
        canvasRenderer = GetComponent<CanvasRenderer>();
        rectTransform = transform as RectTransform;
    }

    private void RefreshRegistration()
    {
        if (!isActiveAndEnabled || !canvas || !canvas.isActiveAndEnabled)
        {
            RemoveRegistration();
            return;
        }

        var key = GetCameraFeatureKey();
        if (!key.camera)
        {
            RemoveRegistration();
            return;
        }

        if (registered && registeredKey == key)
            return;

        RemoveRegistration();
        if (!GlassDict.TryGetValue(key, out var list))
            list = GlassDict[key] = new List<UIGlass>();

        list.Add(this);
        registeredKey = key;
        registered = true;
    }

    private void RemoveRegistration()
    {
        if (!registered)
            return;

        if (GlassDict.TryGetValue(registeredKey, out var list))
        {
            list.Remove(this);
            if (list.Count == 0)
                GlassDict.Remove(registeredKey);
        }

        registeredKey = default;
        registered = false;
    }
}
}
