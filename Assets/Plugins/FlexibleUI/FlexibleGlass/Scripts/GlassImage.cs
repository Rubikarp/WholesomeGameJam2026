using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Sprites;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace JeffGrawAssets.FlexibleUI
{
[ExecuteAlways]
[AddComponentMenu("UI/Glass Image", 12)]
public partial class GlassImage : Image
{
    private GlassSpriteMesh.Sampler spriteHitTest;
    private GlassSdfRaycastField raycastField;
    private Matrix4x4 shadowProjection;
    private bool hasShadowProjection;
#if UNITY_EDITOR
    public const string ReferenceSourceFieldName = nameof(referenceSource);
    public const string CameraReferenceFieldName = nameof(cameraReference);
    public const string FeatureNumberFieldName = nameof(featureNumber);
    public const string ShapeTypeFieldName = nameof(shapeType);
    public const string ShapeExponentFieldName = nameof(shapeExponent);
    public const string CanonicalCornerRadiusFieldName = nameof(canonicalCornerRadius);
    public const string AlphaThresholdFieldName = nameof(alphaThreshold);
    public const string SurfaceSmoothnessModeFieldName = nameof(surfaceSmoothnessMode);
    public const string SurfaceSmoothnessFieldName = nameof(surfaceSmoothness);
    public const string DepthFallbackFieldName = nameof(depthFallback);
    public const string ShapeFieldName = nameof(shape);
    public const string AppearanceFieldName = nameof(appearance);
    public const string RefractionStrengthFieldName = nameof(refractionStrength);
    public const string RefractiveIndexFieldName = nameof(refractiveIndex);
    public const string AbbeNumberFieldName = nameof(abbeNumber);
    public const string SdfRaycastFieldName = nameof(sdfRaycast);
    public const string RaycastExpansionFieldName = nameof(raycastExpansion);
#endif

    private const string ImageShaderName = "Hidden/JeffGrawAssets/GlassImage";
    public const float MaxLensDepth = UIGlass.MaxLensDepth;
    private const int FillEnabledFlag = 8;
    private const int FillReflexFlag = 16;
    private const int DepthFallbackFlag = 32;
    private static readonly int BlurTextureId = Shader.PropertyToID("_GlassImageTex");
    private static readonly int StereoBlurTextureId = Shader.PropertyToID("_GlassImageTexArray");
    private static Shader imageShader;
    private static Material defaultGlassMaterial;

#if UNITY_6000_6_OR_NEWER
    [Unity.Scripting.LifecycleManagement.AutoStaticsCleanup]
#endif
    internal static readonly Dictionary<(Camera camera, int featureNumber), List<GlassImage>> ImageDict = new();
#if UNITY_6000_6_OR_NEWER
    [Unity.Scripting.LifecycleManagement.AutoStaticsCleanup]
#endif
    private static readonly List<(Camera camera, int featureNumber)> StaleRegistrationKeys = new(2);

    public GlassReferenceSource referenceSource;
    public Camera cameraReference;
    [Min(0)] public int featureNumber;
    public GlassImageShapeType shapeType = GlassImageShapeType.Canonical;
    [Min(0f)]
    [Tooltip("Canonical corner radius in Canvas units. Clamped to half the shorter side; resizing the panel otherwise leaves the corners unchanged.")]
    public float canonicalCornerRadius = 28f;
    [Range(2f, 16f)]
    [Tooltip("Canonical corner curve power. 2 gives circular corners; higher values flatten the transition into the straight sides.")]
    public float shapeExponent = 6.5f;
    [Range(GlassMath.MinimumAlphaThreshold, GlassMath.MaximumAlphaThreshold)]
    [Tooltip("Sprite alpha value treated as the retained field perimeter.")]
    public float alphaThreshold = 0.5f;
    [Tooltip("Auto adjusts smoothing from the Canonical corner radius and Roundness. Custom uses the Smoothness value directly. Auto applies only to Canonical shapes.")]
    public GlassSurfaceSmoothnessMode surfaceSmoothnessMode;
    [Range(0.01f, 5f)]
    [Tooltip("Controls the retained optical-field smoothing radius relative to the Optical Lip. Higher values smooth across a wider area.")]
    public float surfaceSmoothness = 0.75f;
    [SerializeField]
    [Tooltip("Falls back to geometric depth to prevent hard interior refraction seams at high smoothness. Can add an interior contour to some concave shapes. Does not rebuild the field.")]
    private bool depthFallback;
    public bool DepthFallback
    {
        get => depthFallback;
        set
        {
            if (depthFallback == value) return;
            depthFallback = value;
            SetVerticesDirty();
        }
    }
    [Range(0f, 4f)]
    [Tooltip("Virtual travel depth relative to Optical Lip. 0 disables refraction and 1 is the normal physical range.")]
    public float refractionStrength = 2f;
    [Range(1f, 2.5f)]
    [Tooltip("Refractive index of the glass. 1 does not bend light; ordinary glass is around 1.5.")]
    public float refractiveIndex = 1.5f;
    [Tooltip("Controls wavelength dispersion. Higher values separate colors less. Ordinary glass is around 58.")]
    [Range(GlassMath.MinimumAbbeNumber, GlassMath.MaximumAbbeNumber)]
    public float abbeNumber = GlassMath.MaximumAbbeNumber;
    public GlassShapeSettings shape = new();
    public GlassAppearance appearance = new();

    [SerializeField]
    [Tooltip("Test the cached shape distance instead of procedural geometry or source alpha. Requires asynchronous GPU readback; new shapes accept hits once their CPU field is ready. Sprite Read/Write is not required.")]
    private bool sdfRaycast;
    [SerializeField]
    [Tooltip("Hit-area adjustment in local UI units. Positive expands the shape; negative contracts it. Does not change the rendered glass or its cached field. Image fill and UI masks still clip hits.")]
    private float raycastExpansion;
    [SerializeField, HideInInspector] private float appliedRaycastExpansion;

    public bool SdfRaycast
    {
        get => sdfRaycast;
        set
        {
            sdfRaycast = value;
            if (!value) raycastField = null;
            UpdateRaycastBounds();
        }
    }

    public float RaycastExpansion
    {
        get => raycastExpansion;
        set
        {
            raycastExpansion = float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
            UpdateRaycastBounds();
        }
    }

    private void UpdateRaycastBounds()
    {
        var expansion = sdfRaycast && isActiveAndEnabled ? Mathf.Max(0f, raycastExpansion) : 0f;
        if (expansion == appliedRaycastExpansion)
            return;
        // GraphicRaycaster checks this rectangle before calling the shape filter.
        raycastPadding += Vector4.one * (appliedRaycastExpansion - expansion);
        appliedRaycastExpansion = expansion;
    }

    private RTHandle blurHandle;
    private readonly Vector3[] thicknessWorldCorners = new Vector3[4];
    private readonly Vector2[] screenCorners = new Vector2[4];
    private Vector4 retainedSdfData = new(0f, 0f, -1f, 0f);
    private (Camera camera, int featureNumber) registeredKey;
    private bool registered;

    private bool HasVisibleFill => type != Type.Filled || fillAmount >= 0.001f;

    private Rect GetGlassRect()
    {
        var rect = GetPixelAdjustedRect();
        var activeSprite = overrideSprite;
        if (shapeType != GlassImageShapeType.Sprite || !preserveAspect || !activeSprite || rect.width <= 0f || rect.height <= 0f)
            return rect;

        var spriteSize = activeSprite.rect.size;
        if (spriteSize.x <= 0f || spriteSize.y <= 0f)
            return rect;

        var ratio = spriteSize.x / spriteSize.y;
        if (ratio > rect.width / rect.height)
        {
            var height = rect.width / ratio;
            rect.y += (rect.height - height) * rectTransform.pivot.y;
            rect.height = height;
        }
        else
        {
            var width = rect.height * ratio;
            rect.x += (rect.width - width) * rectTransform.pivot.x;
            rect.width = width;
        }
        return rect;
    }

    private void GetGlassWorldCorners()
    {
        var rect = GetGlassRect();
        thicknessWorldCorners[0] = rectTransform.TransformPoint(new Vector3(rect.xMin, rect.yMin));
        thicknessWorldCorners[1] = rectTransform.TransformPoint(new Vector3(rect.xMin, rect.yMax));
        thicknessWorldCorners[2] = rectTransform.TransformPoint(new Vector3(rect.xMax, rect.yMax));
        thicknessWorldCorners[3] = rectTransform.TransformPoint(new Vector3(rect.xMax, rect.yMin));
    }

    private int GetFillPlanes(out Vector3 first, out Vector3 second)
    {
        first = second = Vector3.zero;
        if (type != Type.Filled || fillAmount >= 1f)
            return 0;

        if (fillMethod == FillMethod.Horizontal || fillMethod == FillMethod.Vertical)
        {
            var axis = fillMethod == FillMethod.Horizontal ? Vector2.right : Vector2.up;
            var normal = fillOrigin == 0 ? -axis : axis;
            first = second = new Vector3(normal.x, normal.y, fillAmount - (fillOrigin == 0 ? 0f : 1f));
            return FillEnabledFlag;
        }

        Vector2 center;
        float startAngle, sweep;
        if (fillMethod == FillMethod.Radial90)
        {
            center = new Vector2(fillOrigin >= 2 ? 1f : 0f, fillOrigin == 1 || fillOrigin == 2 ? 1f : 0f);
            startAngle = 90f - fillOrigin * 90f - (fillClockwise ? 0f : 90f);
            sweep = 90f * fillAmount;
        }
        else if (fillMethod == FillMethod.Radial180)
        {
            center = fillOrigin switch
            {
                0 => new Vector2(0.5f, 0f),
                1 => new Vector2(0f, 0.5f),
                2 => new Vector2(0.5f, 1f),
                _ => new Vector2(1f, 0.5f)
            };
            startAngle = 180f - fillOrigin * 90f - (fillClockwise ? 0f : 180f);
            sweep = 180f * fillAmount;
        }
        else
        {
            center = new Vector2(0.5f, 0.5f);
            startAngle = -90f + fillOrigin * 90f;
            sweep = 360f * fillAmount;
        }

        var direction = fillClockwise ? -1f : 1f;
        var start = startAngle * Mathf.Deg2Rad;
        var end = (startAngle + direction * sweep) * Mathf.Deg2Rad;
        var firstNormal = direction * new Vector2(-Mathf.Sin(start), Mathf.Cos(start));
        var secondNormal = -direction * new Vector2(-Mathf.Sin(end), Mathf.Cos(end));
        if (fillMethod == FillMethod.Radial180)
        {
            var halfRectScale = fillOrigin == 0 || fillOrigin == 2 ? new Vector2(2f, 1f) : new Vector2(1f, 2f);
            firstNormal = Vector2.Scale(firstNormal, halfRectScale);
            secondNormal = Vector2.Scale(secondNormal, halfRectScale);
        }
        first = new Vector3(firstNormal.x, firstNormal.y, -Vector2.Dot(firstNormal, center));
        second = new Vector3(secondNormal.x, secondNormal.y, -Vector2.Dot(secondNormal, center));
        return FillEnabledFlag | (sweep > 180f ? FillReflexFlag : 0);
    }

    private static Shader ImageShader => imageShader ? imageShader : imageShader = Shader.Find(ImageShaderName);
    private static Material DefaultGlassMaterial
    {
        get
        {
            if (!defaultGlassMaterial && ImageShader)
            {
                defaultGlassMaterial = CoreUtils.CreateEngineMaterial(ImageShader);
                defaultGlassMaterial.name = "DefaultGlassImage";
            }
            return defaultGlassMaterial ? defaultGlassMaterial : defaultGraphicMaterial;
        }
    }

#if UNITY_EDITOR && !UNITY_6000_6_OR_NEWER
    static GlassImage() => AssemblyReloadEvents.beforeAssemblyReload += ReleaseStaticState;
#endif

#if UNITY_6000_6_OR_NEWER
    [Unity.Scripting.LifecycleManagement.OnCodeUnloading]
    private static void OnCodeUnloading() => ReleaseStaticState();
#endif

    private static void ReleaseStaticState()
    {
        ClearRegistrations();
        if (defaultGlassMaterial)
            CoreUtils.Destroy(defaultGlassMaterial);
        defaultGlassMaterial = null;
        imageShader = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ClearRegistrations();
#if UNITY_EDITOR
        foreach (var image in Resources.FindObjectsOfTypeAll<GlassImage>())
            if (image && image.gameObject.scene.IsValid() && image.isActiveAndEnabled)
                image.RefreshRegistration();
#endif
    }

    private static void ClearRegistrations() => ImageDict.Clear();

    protected override void OnEnable()
    {
        base.OnEnable();
        shape ??= new GlassShapeSettings();
        appearance ??= new GlassAppearance();
        featureNumber = Mathf.Max(0, featureNumber);
        shapeExponent = Mathf.Clamp(shapeExponent, 2f, 16f);
        canonicalCornerRadius = Mathf.Max(0f, canonicalCornerRadius);
        alphaThreshold = GlassMath.ClampAlphaThreshold(alphaThreshold);
        surfaceSmoothness = Mathf.Clamp(surfaceSmoothness, 0.01f, 5f);
        ValidateCanvasShaderChannels();
        UpdateRaycastBounds();
        RefreshRegistration();
#if UNITY_EDITOR
        Canvas.preWillRenderCanvases += RefreshEditorPreview;
#endif
    }

    protected override void OnDisable()
    {
        raycastField = null;
        raycastPadding += Vector4.one * appliedRaycastExpansion;
        appliedRaycastExpansion = 0f;
#if UNITY_EDITOR
        Canvas.preWillRenderCanvases -= RefreshEditorPreview;
#endif
        RemoveRegistration();
        base.OnDisable();
    }

#if UNITY_EDITOR
    private void RefreshEditorPreview()
    {
        if (Application.isPlaying)
            return;
        RefreshRegistration();
        LateUpdate();
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        shape ??= new GlassShapeSettings();
        appearance ??= new GlassAppearance();
        refractionStrength = Mathf.Clamp(refractionStrength, 0f, MaxLensDepth);
        refractiveIndex = Mathf.Clamp(refractiveIndex, 1f, 2.5f);
        abbeNumber = Mathf.Clamp(abbeNumber, GlassMath.MinimumAbbeNumber, GlassMath.MaximumAbbeNumber);
        featureNumber = Mathf.Max(0, featureNumber);
        shapeExponent = Mathf.Clamp(shapeExponent, 2f, 16f);
        canonicalCornerRadius = Mathf.Max(0f, canonicalCornerRadius);
        alphaThreshold = GlassMath.ClampAlphaThreshold(alphaThreshold);
        surfaceSmoothness = Mathf.Clamp(surfaceSmoothness, 0.01f, 5f);
        if (float.IsNaN(raycastExpansion) || float.IsInfinity(raycastExpansion)) raycastExpansion = 0f;
        UpdateRaycastBounds();
        SetVerticesDirty();
        if (isActiveAndEnabled)
            RefreshRegistration();
    }
#endif

    private void Update()
    {
        UpdateRaycastBounds();
        RefreshRegistration();
    }

    private void LateUpdate()
    {
        if (!canvas || !canvas.isActiveAndEnabled)
            return;

        RefreshShadowProjection();
        RefreshRetainedSdfBinding();
        var desiredMaterial = DefaultGlassMaterial;
        var key = GetCameraFeatureKey();
        if (!canvasRenderer.cull && canvasRenderer.GetInheritedAlpha() > 0f && color.a > 0f &&
            key.camera && ImageShader && FlexibleGlassPass.TryGetImageMaterial(key.camera, key.featureNumber, ImageShader, out var sharedMaterial))
            desiredMaterial = sharedMaterial;

        if (material != desiredMaterial)
        {
            material = desiredMaterial;
            SetMaterialDirty();
        }
        else if (desiredMaterial != DefaultGlassMaterial && !blurHandle?.rt)
        {
            SetMaterialDirty();
        }
    }

    private void RefreshShadowProjection()
    {
        if (!appearance.HasVisibleShadow() || !rectTransform)
        {
            hasShadowProjection = false;
            return;
        }

        var eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var projection = eventCamera
            ? eventCamera.projectionMatrix * eventCamera.worldToCameraMatrix * rectTransform.localToWorldMatrix
            : rectTransform.localToWorldMatrix;
        if (hasShadowProjection && projection == shadowProjection)
            return;

        shadowProjection = projection;
        hasShadowProjection = true;
        SetVerticesDirty();
    }

    public override Material materialForRendering
    {
        get
        {
            if (!canvas || !canvas.isActiveAndEnabled)
                return DefaultGlassMaterial;

            var key = GetCameraFeatureKey();
            if (!key.camera || !FlexibleGlassPass.TryGetImageRT(key.camera, key.featureNumber, out var newHandle))
                return base.materialForRendering;

            var result = base.materialForRendering;
            if (result)
            {
                FlexibleGlassPass.ConfigureImageMaterialForRendering(key.camera, key.featureNumber, result);
                result.SetTexture(newHandle.rt.dimension == TextureDimension.Tex2DArray ? StereoBlurTextureId : BlurTextureId, blurHandle = newHandle);
            }
            return result;
        }
    }

    public (Camera camera, int featureNumber) GetCameraFeatureKey()
    {
        if (referenceSource == GlassReferenceSource.ReferenceProvider && canvas && GlassReferenceProvider.CameraReferenceDict.TryGetValue(canvas, out var providerReference) && providerReference.camera)
            return providerReference;

        return (cameraReference ? cameraReference : Camera.main, Mathf.Max(0, featureNumber));
    }

    internal bool TryGetBlurBounds(Camera sourceCamera, Vector2 renderScale, out Rect bounds, GlassScreenProjection projection = default)
    {
        bounds = default;
        if (!isActiveAndEnabled || !HasVisibleFill || !sourceCamera || !canvas || !canvas.isActiveAndEnabled || !rectTransform || !gameObject.activeInHierarchy || canvasRenderer.cull || canvasRenderer.GetInheritedAlpha() <= 0f || color.a <= 0f)
            return false;
        if (GetCameraFeatureKey().camera != sourceCamera)
            return false;

        var rect = GetGlassRect();
        var size = new Vector2(Mathf.Abs(rect.width), Mathf.Abs(rect.height));
        if (size.x <= 1e-5f || size.y <= 1e-5f)
            return false;

        GetGlassWorldCorners();
        var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera ? canvas.worldCamera : sourceCamera;
        var cameraOffset = sourceCamera.pixelRect.position;
        for (int i = 0; i < 4; i++)
        {
            Vector3 screenPoint;
            if (uiCamera)
            {
                screenPoint = projection.WorldToScreenPoint(uiCamera, thicknessWorldCorners[i]);
                if (screenPoint.z <= 1e-5f)
                    return false;
            }
            else
            {
                screenPoint = thicknessWorldCorners[i];
            }

            screenCorners[i] = Vector2.Scale((Vector2)screenPoint - cameraOffset, renderScale);
        }

        var min = screenCorners[0];
        var max = screenCorners[0];
        for (int i = 1; i < 4; i++)
        {
            min = Vector2.Min(min, screenCorners[i]);
            max = Vector2.Max(max, screenCorners[i]);
        }

        GetScreenMeasurements(size, out var thickness);
        var averageRenderScale = (renderScale.x + renderScale.y) * 0.5f;
        var maximumIndex = GlassMath.GetRefractiveIndex(Mathf.Clamp(refractiveIndex, 1f, 2.5f), Mathf.Clamp(abbeNumber, GlassMath.MinimumAbbeNumber, GlassMath.MaximumAbbeNumber), 486.1327f);
        var refractionPadding = Mathf.Sqrt(Mathf.Max(0f, maximumIndex * maximumIndex - 1f)) * thickness * Mathf.Clamp(refractionStrength, 0f, MaxLensDepth) / appearance.GetMagnification();
        var padding = Mathf.Max(2f, refractionPadding + thickness + 2f) * averageRenderScale;
        bounds = Rect.MinMaxRect(min.x - padding, min.y - padding, max.x + padding, max.y + padding);
        return max.x > 0f && max.y > 0f && min.x < sourceCamera.pixelWidth * renderScale.x && min.y < sourceCamera.pixelHeight * renderScale.y;
    }

    internal bool TryBuildSdfDescriptor(Camera sourceCamera, out GlassSdfDescriptor descriptor)
    {
        descriptor = default;
        if (!isActiveAndEnabled || !HasVisibleFill || !sourceCamera || !canvas || !canvas.isActiveAndEnabled || !rectTransform || !gameObject.activeInHierarchy || canvasRenderer.cull)
            return false;
        if (!(sdfRaycast && raycastTarget) && (canvasRenderer.GetInheritedAlpha() <= 0f || color.a <= 0f))
            return false;
        var activeSprite = overrideSprite;
        if (GetCameraFeatureKey().camera != sourceCamera || shapeType == GlassImageShapeType.Sprite && !activeSprite)
            return false;

        var rect = GetGlassRect();
        var size = new Vector2(Mathf.Abs(rect.width), Mathf.Abs(rect.height));
        if (size.x <= 1e-5f || size.y <= 1e-5f)
            return false;

        var canonical = shapeType == GlassImageShapeType.Canonical;
        var perCorner = shapeType == GlassImageShapeType.PerCorner;
        var squircle = perCorner && shape.squircle;
        var source = shapeType == GlassImageShapeType.Sprite ? GlassSdfSource.SpriteAlpha : GlassSdfSource.Shape;
        var shapeIndex = canonical ? 0 : squircle ? 2 : 1;
        var radii = canonical ? Vector4.one * GlassMath.ResolveCanonicalRadius(canonicalCornerRadius, size) : perCorner ? shape.GetCornerRadii(size) : Vector4.zero;
        var cornerShape = canonical
            ? new Vector4(Mathf.Clamp(shapeExponent, 2f, 16f), 0f, 0f, 0f)
            : squircle
                ? Vector4.one * 2f + Vector4.Min(Vector4.one, Vector4.Max(Vector4.zero, shape.cornerRoundness)) * 8f
                : Vector4.one - Vector4.Min(Vector4.one, Vector4.Max(-Vector4.one, shape.cornerRoundness));
        var texture = source == GlassSdfSource.SpriteAlpha ? activeSprite.texture : null;
        var textureUv = source == GlassSdfSource.SpriteAlpha ? DataUtility.GetOuterUV(activeSprite) : Vector4.zero;
        descriptor = new GlassSdfDescriptor(source, shapeIndex, size, Vector2.Max(size / 32f, Vector2.one), radii, cornerShape, texture, textureUv, GlassMath.ClampAlphaThreshold(alphaThreshold), source == GlassSdfSource.SpriteAlpha ? activeSprite : null);
        return true;
    }

    internal void SetRetainedSdfBinding(Vector4 sdfData)
    {
        if (retainedSdfData == sdfData)
            return;
        retainedSdfData = sdfData;
        SetVerticesDirty();
    }

    internal void SetRaycastField(GlassSdfRaycastField field) => raycastField = field;

    internal void ClearRetainedSdfBinding()
    {
        raycastField = null;
        SetRetainedSdfBinding(new Vector4(0f, 0f, -1f, 0f));
    }

    internal static void PruneInvalidRegistrations()
    {
        StaleRegistrationKeys.Clear();
        foreach (var entry in ImageDict)
        {
            var images = entry.Value;
            for (int i = images.Count - 1; i >= 0; i--)
                if (!images[i] || !images[i].isActiveAndEnabled)
                    images.RemoveAt(i);
            if (!entry.Key.camera || images.Count == 0)
                StaleRegistrationKeys.Add(entry.Key);
        }
        foreach (var key in StaleRegistrationKeys)
            ImageDict.Remove(key);
        StaleRegistrationKeys.Clear();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        if (!HasVisibleFill)
            return;
        ValidateCanvasShaderChannels();
        var rect = GetGlassRect();
        if (rect.width <= 0f || rect.height <= 0f)
            return;
        var fillFlags = GetFillPlanes(out var firstFillPlane, out var secondFillPlane);
        var quadVertex = UIVertex.simpleVert;
        quadVertex.color = color;
        quadVertex.position = new Vector2(rect.xMin, rect.yMin);
        vertexHelper.AddVert(quadVertex);
        quadVertex.position = new Vector2(rect.xMin, rect.yMax);
        vertexHelper.AddVert(quadVertex);
        quadVertex.position = new Vector2(rect.xMax, rect.yMax);
        vertexHelper.AddVert(quadVertex);
        quadVertex.position = new Vector2(rect.xMax, rect.yMin);
        vertexHelper.AddVert(quadVertex);
        vertexHelper.AddTriangle(0, 1, 2);
        vertexHelper.AddTriangle(2, 3, 0);

        var rectSize = new Vector2(Mathf.Abs(rect.width), Mathf.Abs(rect.height));
        var resolvedSurfaceSmoothness = GetResolvedSurfaceSmoothness(rectSize);
        var hasPrecomputedLods = GetScreenMeasurements(rectSize, out var thickness, out var opticalLod, out var lightingLod);
        var colorMix = Mathf.Clamp01(appearance.colorMix);
        var index = Mathf.Clamp(refractiveIndex, 1f, 2.5f);
        var optics = new Vector4(Mathf.Clamp(refractionStrength, 0f, MaxLensDepth), index, Mathf.Clamp(appearance.transmission, 0f, 2f), appearance.GetMagnification());
        var dispersion = GlassMath.GetPhysicalDispersionCoefficient(index, Mathf.Clamp(abbeNumber, GlassMath.MinimumAbbeNumber, GlassMath.MaximumAbbeNumber));
        var lipLightWidths = appearance.GetLipLightExtents(rectSize);
        var edgeWidths = GlassMath.PackBytes(0, 0, (byte)Mathf.RoundToInt(lipLightWidths.y * 255f), (byte)Mathf.RoundToInt(lipLightWidths.x * 255f));
        var hasShadow = appearance.HasVisibleShadow();
        var shadowColor = appearance.shadowColor;
        if (!hasShadow)
            shadowColor.a = 0f;
        else if (TryGetProjectedMinorSpan(out var projectedMinorSpan))
        {
            // Conserve projected coverage when a transformed image becomes narrower
            // than its shadow kernel, matching the compositor shadow path.
            var shadowKernelWidth = 2f * Mathf.Max(appearance.shadowSize, 0.5f);
            shadowColor.a *= Mathf.Clamp01(projectedMinorSpan / Mathf.Max(shadowKernelWidth, 1e-4f));
        }
        var packedShadowColor = GlassMath.PackColor(shadowColor);
        var colorMixByte = (byte)Mathf.RoundToInt(colorMix * 255f);
        var maximumLod = Mathf.Max(Mathf.Log(Mathf.Max(retainedSdfData.w, 1f), 2f), 1f);
        var lightingLodBits = (ushort)Mathf.RoundToInt(Mathf.Clamp01(lightingLod / maximumLod) * 65535f);
        var baseFlags = (byte)(fillFlags | (hasPrecomputedLods ? 1 : 0) | (depthFallback ? DepthFallbackFlag : 0));
        var baseControls = GlassMath.PackBytes((byte)(lightingLodBits >> 8), (byte)lightingLodBits, colorMixByte, baseFlags);
        var shadowFlags = (byte)(baseFlags | 2 | (appearance.shadowOffset.sqrMagnitude > 1e-8f ? 4 : 0));
        var shadowControls = GlassMath.PackBytes((byte)(lightingLodBits >> 8), (byte)lightingLodBits, colorMixByte, shadowFlags);
        var packedShadowSize = Mathf.Round(Mathf.Clamp(appearance.shadowSize, 0f, 32f) * 64f);
        var sourceVertices = UnityEngine.Pool.ListPool<UIVertex>.Get();
        var outputVertices = UnityEngine.Pool.ListPool<UIVertex>.Get();
        vertexHelper.GetUIVertexStream(sourceVertices);
        var canvasScale = Mathf.Max(canvas ? canvas.scaleFactor : 1f, 1e-4f);

        if (hasShadow)
        {
            var shadowReach = (appearance.GetShadowDistanceSupport() + GlassAppearance.ShadowAntialiasPadding) / canvasScale;
            var shadowOffset = appearance.shadowOffset / canvasScale;
            var shadowRect = Rect.MinMaxRect(
                rect.xMin + Mathf.Min(shadowOffset.x, 0f) - shadowReach,
                rect.yMin + Mathf.Min(shadowOffset.y, 0f) - shadowReach,
                rect.xMax + Mathf.Max(shadowOffset.x, 0f) + shadowReach,
                rect.yMax + Mathf.Max(shadowOffset.y, 0f) + shadowReach);
            var bottomLeft = CreateShadowVertex(new Vector2(shadowRect.xMin, shadowRect.yMin));
            var topLeft = CreateShadowVertex(new Vector2(shadowRect.xMin, shadowRect.yMax));
            var topRight = CreateShadowVertex(new Vector2(shadowRect.xMax, shadowRect.yMax));
            var bottomRight = CreateShadowVertex(new Vector2(shadowRect.xMax, shadowRect.yMin));
            outputVertices.Add(bottomLeft);
            outputVertices.Add(topLeft);
            outputVertices.Add(topRight);
            outputVertices.Add(topRight);
            outputVertices.Add(bottomRight);
            outputVertices.Add(bottomLeft);
        }

        for (int i = 0; i < sourceVertices.Count; i++)
        {
            var vertex = sourceVertices[i];
            PackVertex(ref vertex, baseControls, false);
            outputVertices.Add(vertex);
        }
        vertexHelper.Clear();
        vertexHelper.AddUIVertexTriangleStream(outputVertices);
        UnityEngine.Pool.ListPool<UIVertex>.Release(outputVertices);
        UnityEngine.Pool.ListPool<UIVertex>.Release(sourceVertices);

        UIVertex CreateShadowVertex(Vector2 position)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            PackVertex(ref vertex, shadowControls, true);
            return vertex;
        }

        void PackVertex(ref UIVertex vertex, float controls, bool shadowOnly)
        {
            var local = new Vector2(vertex.position.x - rect.xMin, vertex.position.y - rect.yMin);
            var normalized = new Vector3(local.x / rectSize.x, local.y / rectSize.y, 1f);
            vertex.uv0 = new Vector4(Vector3.Dot(firstFillPlane, normalized), Vector3.Dot(secondFillPlane, normalized), local.x, local.y);
            vertex.uv1 = new Vector4(rectSize.x, rectSize.y, retainedSdfData.z, hasPrecomputedLods ? opticalLod : resolvedSurfaceSmoothness);
            vertex.uv2 = shadowOnly ? new Vector4(0f, 0f, packedShadowColor, canvasScale) : optics;
            vertex.uv3 = shadowOnly
                ? new Vector4(packedShadowSize, appearance.shadowOffset.x, appearance.shadowOffset.y, controls)
                : new Vector4(thickness, dispersion, edgeWidths, controls);
            vertex.normal = Vector3.zero;
            vertex.tangent = Vector4.zero;
        }
    }

    private void RefreshRetainedSdfBinding()
    {
        var key = GetCameraFeatureKey();
        if (!key.camera || !TryBuildSdfDescriptor(key.camera, out var descriptor))
        {
            ClearRetainedSdfBinding();
            return;
        }

        // A descriptor edit can reach the Canvas before the renderer has advanced
        // its retained-field cache. Keep the last complete field for that one
        // frame instead of replacing it with an invalid slice.
        if (FlexibleGlassPass.TryGetImageSdfBinding(key.camera, key.featureNumber, descriptor, out var sdfData))
        {
            SetRetainedSdfBinding(sdfData);
            raycastField = sdfRaycast && raycastTarget ? FlexibleGlassPass.GetImageRaycastField(key.camera, key.featureNumber, descriptor) : null;
        }
        else if (!FlexibleGlassPass.HasImagePass(key.camera, key.featureNumber))
        {
            ClearRetainedSdfBinding();
        }
    }

    public override bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        if (!HasVisibleFill || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out var localPoint))
            return false;

        var rect = GetGlassRect();
        var expansion = sdfRaycast ? Mathf.Max(0f, raycastExpansion) : 0f;
        var hitRect = Rect.MinMaxRect(rect.xMin - expansion, rect.yMin - expansion, rect.xMax + expansion, rect.yMax + expansion);
        if (!hitRect.Contains(localPoint) || rect.width <= 0f || rect.height <= 0f)
            return false;
        var size = new Vector2(Mathf.Abs(rect.width), Mathf.Abs(rect.height));
        var position = localPoint - rect.min;
        var fillFlags = GetFillPlanes(out var firstFillPlane, out var secondFillPlane);
        var normalizedPoint = new Vector3(position.x / size.x, position.y / size.y, 1f);
        var firstDistance = Vector3.Dot(firstFillPlane, normalizedPoint);
        var secondDistance = Vector3.Dot(secondFillPlane, normalizedPoint);
        if (fillFlags != 0 && ((fillFlags & FillReflexFlag) != 0 ? Mathf.Max(firstDistance, secondDistance) : Mathf.Min(firstDistance, secondDistance)) < 0f)
            return false;

        if (sdfRaycast)
        {
            var key = GetCameraFeatureKey();
            return raycastField != null && TryBuildSdfDescriptor(key.camera, out var descriptor) &&
                   raycastField.TrySample(descriptor, position, out var distance) && distance <= raycastExpansion;
        }

        if (shapeType == GlassImageShapeType.Sprite)
        {
            var activeSprite = overrideSprite;
            if (!activeSprite || alphaHitTestMinimumThreshold > 1f)
                return false;
            if (alphaHitTestMinimumThreshold <= 0f)
                return true;
            var uv = DataUtility.GetOuterUV(activeSprite);
            var texturePoint = new Vector2(Mathf.Lerp(uv.x, uv.z, normalizedPoint.x), Mathf.Lerp(uv.y, uv.w, normalizedPoint.y));
            if (activeSprite.packed && !(spriteHitTest ??= new GlassSpriteMesh.Sampler()).TryGetTextureUv(activeSprite, normalizedPoint, out texturePoint))
                return false;
            try
            {
                return activeSprite.texture.GetPixelBilinear(texturePoint.x, texturePoint.y).a >= alphaHitTestMinimumThreshold;
            }
            catch (UnityException)
            {
                Debug.LogError("GlassImage alpha hit testing requires Read/Write on the Sprite texture or its Sprite Atlas.", this);
                return true;
            }
        }
        if (shapeType == GlassImageShapeType.Canonical)
            return GlassMath.ContainsCanonical(position, size, canonicalCornerRadius, shapeExponent);
        return GlassMath.RectDistance(position, size, shape.GetCornerRadii(size), shape.cornerRoundness, shape.squircle) <= 0f;
    }

    private void GetScreenMeasurements(Vector2 size, out float thickness)
    {
        thickness = 0f;
        if (!TryGetScreenScales(size, out var horizontalScale, out var verticalScale, out _))
            return;

        thickness = ResolveScreenThickness(size, horizontalScale, verticalScale);
    }

    private bool GetScreenMeasurements(Vector2 size, out float thickness, out float opticalLod, out float lightingLod)
    {
        thickness = opticalLod = lightingLod = 0f;
        if (!TryGetScreenScales(size, out var horizontalScale, out var verticalScale, out var affine))
            return false;

        thickness = ResolveScreenThickness(size, horizontalScale, verticalScale);
        if (!affine || retainedSdfData.w < 1f)
            return false;

        var padding = Vector2.Max(size / 32f, Vector2.one);
        var domainSize = size + 2f * padding;
        domainSize = Vector2.one * Mathf.Max(domainSize.x, domainSize.y);
        var localTexelSize = domainSize / retainedSdfData.w;
        var screenTexelX = horizontalScale * localTexelSize.x;
        var screenTexelY = verticalScale * localTexelSize.y;
        var screenTexelSize = Mathf.Sqrt(Mathf.Max(screenTexelX * screenTexelY, 1e-6f));
        var thicknessTexels = Mathf.Max(thickness, 0f) / screenTexelSize;
        var maximumLod = Mathf.Log(retainedSdfData.w, 2f);
        opticalLod = Mathf.Clamp(Mathf.Log(1f + GetResolvedSurfaceSmoothness(size) * thicknessTexels, 2f), 0f, maximumLod);
        lightingLod = Mathf.Clamp(Mathf.Log(1f + thicknessTexels, 2f), 0f, maximumLod);
        return true;
    }

    public float GetResolvedSurfaceSmoothness(Vector2 size)
    {
        if (surfaceSmoothnessMode == GlassSurfaceSmoothnessMode.Auto && shapeType == GlassImageShapeType.Canonical)
            return GlassMath.ResolveCanonicalSurfaceSmoothness(appearance.GetResolvedThickness(size), canonicalCornerRadius, shapeExponent, size);
        return Mathf.Clamp(surfaceSmoothness, 0.01f, 5f);
    }

    private bool TryGetScreenScales(Vector2 size, out float horizontalScale, out float verticalScale, out bool affine)
    {
        horizontalScale = verticalScale = 0f;
        affine = false;
        if (size.x <= 1e-5f || size.y <= 1e-5f || !rectTransform)
            return false;

        GetGlassWorldCorners();
        var eventCamera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var bottomLeft = RectTransformUtility.WorldToScreenPoint(eventCamera, thicknessWorldCorners[0]);
        var topLeft = RectTransformUtility.WorldToScreenPoint(eventCamera, thicknessWorldCorners[1]);
        var topRight = RectTransformUtility.WorldToScreenPoint(eventCamera, thicknessWorldCorners[2]);
        var bottomRight = RectTransformUtility.WorldToScreenPoint(eventCamera, thicknessWorldCorners[3]);
        horizontalScale = Mathf.Min(Vector2.Distance(bottomLeft, bottomRight), Vector2.Distance(topLeft, topRight)) / size.x;
        verticalScale = Mathf.Min(Vector2.Distance(bottomLeft, topLeft), Vector2.Distance(bottomRight, topRight)) / size.y;
        var horizontalDelta = (bottomRight - bottomLeft) - (topRight - topLeft);
        var verticalDelta = (topLeft - bottomLeft) - (topRight - bottomRight);
        var affineTolerance = Mathf.Max(size.x * horizontalScale, size.y * verticalScale) * 1e-5f + 1e-4f;
        affine = horizontalDelta.sqrMagnitude <= affineTolerance * affineTolerance && verticalDelta.sqrMagnitude <= affineTolerance * affineTolerance;
        return true;
    }

    private bool TryGetProjectedMinorSpan(out float projectedMinorSpan)
    {
        projectedMinorSpan = 0f;
        if (!rectTransform)
            return false;

        GetGlassWorldCorners();
        var eventCamera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var bottomLeft = RectTransformUtility.WorldToScreenPoint(eventCamera, thicknessWorldCorners[0]);
        var topLeft = RectTransformUtility.WorldToScreenPoint(eventCamera, thicknessWorldCorners[1]);
        var topRight = RectTransformUtility.WorldToScreenPoint(eventCamera, thicknessWorldCorners[2]);
        var bottomRight = RectTransformUtility.WorldToScreenPoint(eventCamera, thicknessWorldCorners[3]);
        var twiceArea = Mathf.Abs(Cross(topLeft - bottomLeft, topRight - bottomLeft) + Cross(topRight - bottomLeft, bottomRight - bottomLeft));
        var maximumEdge = Mathf.Max(
            Vector2.Distance(bottomLeft, topLeft),
            Vector2.Distance(topLeft, topRight),
            Vector2.Distance(topRight, bottomRight),
            Vector2.Distance(bottomRight, bottomLeft),
            1e-4f);
        projectedMinorSpan = twiceArea / (2f * maximumEdge);
        return true;

        float Cross(Vector2 left, Vector2 right) => left.x * right.y - left.y * right.x;
    }

    private float ResolveScreenThickness(Vector2 size, float horizontalScale, float verticalScale) =>
        appearance.GetResolvedThickness(size) * Mathf.Max(0f, Mathf.Min(horizontalScale, verticalScale));

    private void ValidateCanvasShaderChannels()
    {
        if (canvas)
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
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
        if (!ImageDict.TryGetValue(key, out var images))
            images = ImageDict[key] = new List<GlassImage>();
        images.Add(this);
        registeredKey = key;
        registered = true;
    }

    private void RemoveRegistration()
    {
        if (!registered)
            return;
        if (ImageDict.TryGetValue(registeredKey, out var images))
        {
            images.Remove(this);
            if (images.Count == 0)
                ImageDict.Remove(registeredKey);
        }
        registeredKey = default;
        registered = false;
    }

}
}
