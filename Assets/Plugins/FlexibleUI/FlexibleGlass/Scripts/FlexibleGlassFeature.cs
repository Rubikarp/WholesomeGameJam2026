#if !UNITY_6000_3_OR_NEWER || (URP_COMPATIBILITY_MODE && !UNITY_6000_4_OR_NEWER)
#define FLEXIBLE_UI_COMPATIBILITY
#endif

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if !UNITY_2023_1_OR_NEWER
using System.Reflection;
#endif

namespace JeffGrawAssets.FlexibleUI
{
public enum GlassEdgeLightMode
{
    Directional,
    Opposing,
    Point
}

internal struct GlassBlurPlan
{
    public bool integrated;
    public float integratedReach;
    public object integrationData;
    public int integratedFeatureNumber;
    public int kawaseIterations;
    public float kawaseRadius;
    public float kawaseDitherStrength;

    public GlassBlurPlan(int iterations, float radius, float ditherStrength = 0f)
    {
        this = default;
        (kawaseIterations, kawaseRadius, kawaseDitherStrength) = (iterations, radius, ditherStrength);
    }

    public void ResolveStandaloneRadius(int height)
    {
        if (!integrated)
            kawaseRadius *= FlexibleGlassPass.GetKawaseResolutionScale(height);
    }
}

internal readonly struct GlassLightingPlan
{
    public readonly GlassEdgeLightMode mode;
    public readonly Vector4 lighting;
    public readonly Color highlight;
    public readonly Color shadow;

    public GlassLightingPlan(GlassEdgeLightMode mode, Vector4 lighting, Color highlight, Color shadow) =>
        (this.mode, this.lighting, this.highlight, this.shadow) = (mode, lighting, highlight, shadow);
}

internal readonly struct GlassCameraPlan
{
    public readonly float compositionBlend;
    public readonly int backdropMipLevels;
    public readonly float blurPadding;
    public readonly GraphicsFormat blurFormat;
    public readonly GlassLightingPlan lighting;
    public readonly GlassBlurPlan blur;

    public GlassCameraPlan(float compositionBlend, int backdropMipLevels, float blurPadding, GraphicsFormat blurFormat, GlassLightingPlan lighting, GlassBlurPlan blur) =>
        (this.compositionBlend, this.backdropMipLevels, this.blurPadding, this.blurFormat, this.lighting, this.blur) =
        (     compositionBlend,      backdropMipLevels,      blurPadding,      blurFormat,      lighting,      blur);
}

public partial class FlexibleGlassFeature : ScriptableRendererFeature
{
#if UNITY_EDITOR
    public const string RenderPassEventFieldName = nameof(renderPassEvent);
    public const string IterationsFieldName = nameof(iterations);
    public const string SampleRadiusFieldName = nameof(sampleRadius);
    public const string DitherStrengthFieldName = nameof(ditherStrength);
    public const string CompositionBlendFieldName = nameof(compositionBlend);
    public const string SdfResolutionFieldName = nameof(sdfResolution);
    public const string BackdropMipLevelsFieldName = nameof(backdropMipLevels);
    public const string EdgeLightAngleFieldName = nameof(edgeLightAngle);
    public const string EdgeLightModeFieldName = nameof(edgeLightMode);
    public const string PointLightPositionFieldName = nameof(pointLightPosition);
    public const string PointLightRadiusFieldName = nameof(pointLightRadius);
    public const string EdgeLightSpreadFieldName = nameof(edgeLightSpread);
    public const string EdgeHighlightFieldName = nameof(edgeHighlight);
    public const string EdgeShadowFieldName = nameof(edgeShadow);
    public const string OpposingEdgeLightStrengthFieldName = nameof(opposingEdgeLightStrength);
    public const string BlurPaddingFieldName = nameof(blurPadding);
    public const string BlurFormatFieldName = nameof(blurFormat);
#endif

#if UNITY_6000_6_OR_NEWER
    [Unity.Scripting.LifecycleManagement.AutoStaticsCleanup]
#endif
    public static readonly Dictionary<GraphicsFormat, GraphicsFormat> FormatFallbackDict = new();

    [SerializeField] private RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    [SerializeField] [Range(0, 6)] private int iterations = 2;
    [SerializeField] [Range(0.5f, 2f)] private float sampleRadius = 1f;
    [SerializeField] [Range(0f, 5f)] private float ditherStrength = 0.25f;
    [SerializeField] [Range(0f, 100f)] private float compositionBlend = 16f;
    [SerializeField] private int sdfResolution = 256;
    [SerializeField] [Range(0, 8)] private int backdropMipLevels = 4;
    [SerializeField] private GlassEdgeLightMode edgeLightMode;
    [SerializeField] private float edgeLightAngle = 60f;
    [SerializeField] private Vector2 pointLightPosition = new(0.5f, 0.5f);
    [SerializeField] [Min(0.01f)] private float pointLightRadius = 0.5f;
    [SerializeField] [Range(0f, 1f)] private float edgeLightSpread = 0.5f;
    [SerializeField] [ColorUsage(true)] private Color edgeHighlight = new(1f, 1f, 1f, 0.12f);
    [SerializeField] [ColorUsage(true)] private Color edgeShadow = new(0f, 0f, 0f, 0f);
    [SerializeField] [Range(0f, 1f)] private float opposingEdgeLightStrength = 0.5f;
    [SerializeField] [Min(0f)] private float blurPadding;
    [SerializeField] private GraphicsFormat blurFormat;

    private FlexibleGlassPass pass;
    private int featureIndex;

    private void OnEnable()
    {
        if (blurFormat == GraphicsFormat.None)
            blurFormat = DefaultBlurFormat;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticCaches() => FormatFallbackDict.Clear();

    public override void Create()
    {
        featureIndex = FindFeatureIndex();
        var resolvedSdfResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(sdfResolution), 64, 1024);
        if (pass == null || !pass.IsValid || pass.FeatureIndex != featureIndex)
        {
            pass?.Dispose();
            pass = new FlexibleGlassPass(featureIndex, renderPassEvent, resolvedSdfResolution, BuildCameraPlan);
        }
        else
        {
            pass.Reconfigure(renderPassEvent, resolvedSdfResolution);
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (pass == null)
            Create();

        if (pass == null || !pass.IsValid || renderingData.cameraData.isPreviewCamera)
            return;

        pass.ConfigureStereo(renderingData.cameraData.camera, renderingData.cameraData.xr);
        pass.UpdateIntegratedBlurRequest(renderingData.cameraData.camera, renderingData.cameraData.cameraTargetDescriptor);
        pass.ConfigureInput(ScriptableRenderPassInput.Color);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            pass?.Dispose();
            pass = null;
        }
    }

    public static GraphicsFormat VerifyFormat(GraphicsFormat format, bool silent = false)
    {
        if (FormatFallbackDict.TryGetValue(format, out var verified))
            return verified;

#if UNITY_2023_2_OR_NEWER
        var usage = GraphicsFormatUsage.Render;
#else
        var usage = FormatUsage.Render;
#endif
        if (SystemInfo.IsFormatSupported(format, usage))
            return FormatFallbackDict[format] = format;

        var fallback = DefaultBlurFormat;
        if (!SystemInfo.IsFormatSupported(fallback, usage))
            fallback = SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SRGB, usage) ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.B8G8R8A8_UNorm;
        if (!silent)
            Debug.LogWarning($"Unsupported Flexible Glass format {format}. Using {fallback}.");
        return FormatFallbackDict[format] = fallback;
    }

    internal static GraphicsFormat DefaultBlurFormat => QualitySettings.activeColorSpace == ColorSpace.Linear
        ? GraphicsFormat.B10G11R11_UFloatPack32
        : GraphicsFormat.R8G8B8A8_UNorm;

    internal GlassBlurPlan BuildBlurPlan(Camera camera)
    {
        if (FlexibleGlassCameraOverride.TryGet(camera, featureIndex, out var cameraSettings) && cameraSettings.OverridesBlur)
            return cameraSettings.BuildBlurPlan();

        var plan = new GlassBlurPlan(Mathf.Clamp(iterations, 0, 6), Mathf.Clamp(sampleRadius, 0.5f, 2f), Mathf.Clamp(ditherStrength, 0f, 5f));
        ConfigureIntegratedBlurPlan(ref plan);
        return plan;
    }

    internal GlassCameraPlan BuildCameraPlan(Camera camera)
    {
        FlexibleGlassCameraOverride.TryGet(camera, featureIndex, out var cameraOverride);
        var resolvedCompositionBlend = cameraOverride && cameraOverride.OverridesComposition ? cameraOverride.EffectiveCompositionBlend : Mathf.Max(0f, compositionBlend);
        var resolvedBackdropMipLevels = cameraOverride && cameraOverride.OverridesRefraction ? cameraOverride.EffectiveBackdropMipLevels : Mathf.Clamp(backdropMipLevels, 0, 8);
        var resolvedBlurPadding = cameraOverride && cameraOverride.OverridesBlur ? cameraOverride.EffectiveBlurPadding : Mathf.Max(0f, blurPadding);
        var requestedFormat = cameraOverride && cameraOverride.OverridesBlur ? cameraOverride.EffectiveBlurFormat : blurFormat;
        var resolvedLighting = cameraOverride && cameraOverride.OverridesLighting
            ? cameraOverride.BuildLightingPlan()
            : BuildLightingPlan(edgeLightMode, edgeLightAngle, pointLightPosition, pointLightRadius, edgeLightSpread, edgeHighlight, edgeShadow, opposingEdgeLightStrength);
        return new GlassCameraPlan(resolvedCompositionBlend, resolvedBackdropMipLevels, resolvedBlurPadding, VerifyFormat(requestedFormat), resolvedLighting, BuildBlurPlan(camera));
    }

    internal static GlassLightingPlan BuildLightingPlan(GlassEdgeLightMode mode, float angle, Vector2 pointPosition, float pointRadius, float spreadValue, Color highlightColor, Color shadowColor, float opposingStrength)
    {
        var radians = Mathf.Repeat(angle, 360f) * Mathf.Deg2Rad;
        var pointLight = mode == GlassEdgeLightMode.Point;
        var spread = Mathf.Clamp01(spreadValue);
        // Point uses angular spread; Directional/Opposing use an element-centered strip.
        var spreadControl = pointLight ? 1f + 31f * (1f - spread) * (1f - spread) : 2f / Mathf.Max(spread, 1e-4f);
        var lighting = new Vector4
        (
            pointLight ? pointPosition.x : Mathf.Cos(radians),
            pointLight ? pointPosition.y : Mathf.Sin(radians),
            spread > 1e-4f ? spreadControl : 0f,
            pointLight ? -Mathf.Max(pointRadius, 0.01f) : mode == GlassEdgeLightMode.Opposing ? Mathf.Clamp01(opposingStrength) : 0f
        );
        var highlight = QualitySettings.activeColorSpace == ColorSpace.Linear ? highlightColor.linear : highlightColor;
        var shadow = QualitySettings.activeColorSpace == ColorSpace.Linear ? shadowColor.linear : shadowColor;
        return new GlassLightingPlan(mode, lighting, highlight, shadow);
    }

    partial void ConfigureIntegratedBlurPlan(ref GlassBlurPlan plan);

    private int FindFeatureIndex()
    {
        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (!urpAsset)
            return 0;

#if UNITY_2023_1_OR_NEWER
        foreach (var rendererData in urpAsset.rendererDataList)
#else
        var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(urpAsset) is not ScriptableRendererData[] rendererDataArray)
            return 0;
        foreach (var rendererData in rendererDataArray)
#endif
        {
            if (rendererData == null)
                continue;
            var index = FindFeatureIndex(rendererData.rendererFeatures, this);
            if (index >= 0)
                return index;
        }

        return 0;
    }

    internal static int FindFeatureIndex(IEnumerable<ScriptableRendererFeature> features, FlexibleGlassFeature target)
    {
        var index = 0;
        foreach (var feature in features)
        {
            if (feature == target)
                return index;
            if (feature is FlexibleGlassFeature)
                index++;
        }
        return -1;
    }
}

public partial class FlexibleGlassPass : ScriptableRenderPass
{
    private const string ProfilerTag = nameof(FlexibleGlassPass);
    private const string ShaderName = "Hidden/JeffGrawAssets/FlexibleGlass";
    private const string OpposingEdgeLightKeyword = "FLEXIBLE_GLASS_EDGE_OPPOSING";
    private const string PointEdgeLightKeyword = "FLEXIBLE_GLASS_EDGE_POINT";
    private const string EdgeLightDisabledKeyword = "FLEXIBLE_GLASS_EDGE_DISABLED";
    private const int ExtractPass = 0;
    private const int KawaseDownPass = 1;
    private const int KawaseUpPass = 2;
    private const int CompositePass = 3;
    private const int MaxKawaseTextures = 6;

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int DestinationTexId = Shader.PropertyToID("_DestTex");
    private static readonly int SourceRegionId = Shader.PropertyToID("_SourceRegion");
    private static readonly int SourceTexelSizeId = Shader.PropertyToID("_SourceTexelSize");
    private static readonly int SampleOffsetId = Shader.PropertyToID("_GlassSampleOffset");
    private static readonly int DitherStrengthId = Shader.PropertyToID("_GlassDitherStrength");
    private static readonly int DitherOffsetId = Shader.PropertyToID("_GlassDitherOffset");
    private static readonly int SharpTexId = Shader.PropertyToID("_GlassSharpTex");
    private static readonly int BlurTexId = Shader.PropertyToID("_GlassBlurTex");
    private static readonly int RegionId = Shader.PropertyToID("_GlassRegion");
    private static readonly int BlurRegionId = Shader.PropertyToID("_GlassBlurRegion");
    private static readonly int TargetSizeId = Shader.PropertyToID("_GlassTargetSize");
    private static readonly int CompositionBlendId = Shader.PropertyToID("_GlassCompositionBlend");
    private static readonly int CompositionInverseBlendId = Shader.PropertyToID("_GlassCompositionInverseBlend");
    private static readonly int UniformAppearanceId = Shader.PropertyToID("_GlassUniformAppearance");
    private static readonly int ShadowModeId = Shader.PropertyToID("_GlassShadowMode");
    private static readonly int ReconstructionMaxLodId = Shader.PropertyToID("_GlassReconstructionMaxLod");
    private static readonly int BlurMaxLodId = Shader.PropertyToID("_GlassBlurMaxLod");
    private static readonly int ImageSourceScaleId = Shader.PropertyToID("_GlassImageSourceScale");
    private static readonly int UseBlurId = Shader.PropertyToID("_GlassUseBlur");
    private static readonly int ElementCountId = Shader.PropertyToID("_GlassElementCount");
    private static readonly int ElementBufferId = Shader.PropertyToID("_GlassElements");
    private static readonly int SdfAtlasId = Shader.PropertyToID("_GlassSdfAtlas");
    private static readonly int SdfResolutionId = Shader.PropertyToID("_GlassSdfResolution");
    private static readonly int SdfMaxLodId = Shader.PropertyToID("_GlassSdfMaxLod");
    private static readonly int EdgeLightingId = Shader.PropertyToID("_GlassEdgeLighting");
    private static readonly int EdgeHighlightId = Shader.PropertyToID("_GlassEdgeHighlight");
    private static readonly int EdgeShadowId = Shader.PropertyToID("_GlassEdgeShadow");
    private static readonly int CaptureTextureId = Shader.PropertyToID("FlexibleGlassCapture");
    private static readonly int BackdropTextureId = Shader.PropertyToID("FlexibleGlassBackdrop");
    private static readonly int[] KawaseTextureIds =
    {
        Shader.PropertyToID("FlexibleGlassKawase_0"),
        Shader.PropertyToID("FlexibleGlassKawase_1"),
        Shader.PropertyToID("FlexibleGlassKawase_2"),
        Shader.PropertyToID("FlexibleGlassKawase_3"),
        Shader.PropertyToID("FlexibleGlassKawase_4"),
        Shader.PropertyToID("FlexibleGlassKawase_5")
    };
    private static readonly Comparison<UIGlass> HierarchyComparison = UIGlass.CompareHierarchy;

    private readonly List<UIGlass> sortedGlass = new(8);
    private readonly List<PreparedGlass> preparedGlass = new(8);
    private readonly List<PreparedImage> preparedImages = new(8);
    private readonly List<GlassElementGpu> gpuElements = new(8);
    private readonly HashSet<GlassSdfDescriptor> activeSdfDescriptors = new(8);
    private readonly Func<Camera, GlassCameraPlan> buildCameraPlan;
    private readonly int featureIndex;
    private readonly Material material;
    private GlassSdfCache sdfCache;

    private static readonly Dictionary<(Camera camera, int featureIndex), FlexibleGlassPass> GlobalPasses = new();
    private static readonly Dictionary<(Camera camera, int featureIndex), RTHandle> GlobalImageHandles = new();
    private readonly Dictionary<Camera, RTHandleSystem> imageHandleSystems = new(2);
    private readonly Dictionary<Camera, RTHandle> imageHandles = new(2);
    private readonly Dictionary<Camera, Material> imageMaterials = new(2);
    private readonly List<Camera> staleImageCameras = new(2);
    private readonly List<(Camera camera, int featureIndex)> staleGlobalPassKeys = new(2);

    private GraphicsBuffer elementBuffer;
    private int elementBufferCapacity;
    private GraphicsBuffer rightEyeElementBuffer;
    private int rightEyeElementBufferCapacity;
    private GlassScreenProjection firstEyeProjection, secondEyeProjection;
    private int viewCount = 1, multipassId, prefetchedMultipassId;
    private int sdfFrame;
    private Camera prefetchedCamera;
    private RenderTextureDescriptor prefetchedDescriptor;
    private FrameInfo prefetchedFrame;
    private bool prefetchedFrameAvailable, prefetchedFrameValid;
    private bool warnedShaderModel;

    private readonly struct PreparedGlass
    {
        public readonly UIGlass glass;
        public readonly GlassElementGpu element;
        public readonly Rect blurBounds, rasterBounds;
        public readonly GlassSdfDescriptor descriptor;
        public readonly bool visible;

        public PreparedGlass(UIGlass glass, GlassElementGpu element, Rect blurBounds, Rect rasterBounds, GlassSdfDescriptor descriptor, bool visible = true) =>
            (this.glass, this.element, this.blurBounds, this.rasterBounds, this.descriptor, this.visible) = (glass, element, blurBounds, rasterBounds, descriptor, visible);
    }

    private readonly struct PreparedImage
    {
        public readonly GlassImage image;
        public readonly GlassSdfDescriptor descriptor;
        public readonly Rect blurBounds;
        public readonly bool hasDescriptor, hasBlurBounds;

        public PreparedImage(GlassImage image, GlassSdfDescriptor descriptor, Rect blurBounds, bool hasDescriptor, bool hasBlurBounds) =>
            (this.image, this.descriptor, this.blurBounds, this.hasDescriptor, this.hasBlurBounds) = (image, descriptor, blurBounds, hasDescriptor, hasBlurBounds);
    }

    internal readonly struct FrameInfo
    {
        public readonly Camera camera;
        public readonly RectInt blurRegion, rasterRegion;
        public readonly int targetWidth, targetHeight, elementCount, reconstructionLevels, shadowMode;
        public readonly float compositionBlend;
        public readonly bool hasGlassImages, hasRetainedFields, sdfOnly, uniformAppearance;
        public readonly GlassBlurPlan blurPlan;
        public readonly GraphicsFormat blurFormat;
        public readonly GlassLightingPlan lighting;
        public readonly GraphicsBuffer elements;

        public FrameInfo(Camera camera, RectInt blurRegion, RectInt rasterRegion, int targetWidth, int targetHeight, int elementCount, float compositionBlend, int shadowMode, bool hasGlassImages, bool hasRetainedFields, bool sdfOnly, bool uniformAppearance, int requestedReconstructionLevels, GlassBlurPlan blurPlan, GraphicsFormat blurFormat, GlassLightingPlan lighting, GraphicsBuffer elements = null) =>
            (this.camera, this.blurRegion, this.rasterRegion, this.targetWidth, this.targetHeight, this.elementCount, this.compositionBlend, this.shadowMode, this.hasGlassImages, this.hasRetainedFields, this.sdfOnly, this.uniformAppearance, this.blurPlan, this.blurFormat, this.lighting, reconstructionLevels, this.elements) =
            (     camera,      blurRegion,      rasterRegion,      targetWidth,      targetHeight,      elementCount,      compositionBlend,      shadowMode,      hasGlassImages,      hasRetainedFields,      sdfOnly,      uniformAppearance,      blurPlan,      blurFormat,      lighting, GetReconstructionLevelCount(targetWidth, targetHeight, requestedReconstructionLevels), elements);
    }

    internal FlexibleGlassPass(int featureIndex, RenderPassEvent renderPassEvent, int sdfResolution, Func<Camera, GlassCameraPlan> buildCameraPlan)
    {
        (this.featureIndex, this.renderPassEvent, this.buildCameraPlan) = (featureIndex, renderPassEvent, buildCameraPlan);
        sdfCache = new GlassSdfCache(sdfResolution);

        var shader = Shader.Find(ShaderName);
        if (shader)
        {
            material = CoreUtils.CreateEngineMaterial(shader);
            ConfigureEdgeLightKeywords(material, buildCameraPlan(null).lighting);
        }
        else
            Debug.LogError($"Missing shader {ShaderName}. Flexible Glass will not render.");
    }

    internal int FeatureIndex => featureIndex;
    internal bool IsValid => material;

    internal void Reconfigure(RenderPassEvent renderPassEvent, int sdfResolution)
    {
        this.renderPassEvent = renderPassEvent;
        prefetchedFrameAvailable = false;
        sdfCache.SetResolution(sdfResolution);
        ConfigureEdgeLightKeywords(material, buildCameraPlan(null).lighting);
        foreach (var imageMaterial in imageMaterials)
            ConfigureImageMaterial(imageMaterial.Value, imageMaterial.Key);
    }

    public void Dispose()
    {
        RemoveIntegratedBlurRequests();
        elementBuffer?.Dispose();
        elementBuffer = null;
        elementBufferCapacity = 0;
        rightEyeElementBuffer?.Dispose();
        rightEyeElementBuffer = null;
        rightEyeElementBufferCapacity = 0;
        sdfCache.Dispose();
        foreach (var entry in imageHandles)
        {
            GlobalImageHandles.Remove((entry.Key, featureIndex));
        }
        staleGlobalPassKeys.Clear();
        foreach (var entry in GlobalPasses)
            if (entry.Value == this)
                staleGlobalPassKeys.Add(entry.Key);
        foreach (var key in staleGlobalPassKeys)
            GlobalPasses.Remove(key);
        foreach (var imageMaterial in imageMaterials.Values)
            CoreUtils.Destroy(imageMaterial);
        foreach (var handleSystem in imageHandleSystems.Values)
            handleSystem.Dispose();
        imageMaterials.Clear();
        imageHandles.Clear();
        imageHandleSystems.Clear();
        CoreUtils.Destroy(material);
    }

    partial void RemoveIntegratedBlurRequests();
    partial void UpdateIntegratedBlurRequestCore(Camera camera, RenderTextureDescriptor descriptor);

    internal void UpdateIntegratedBlurRequest(Camera camera, RenderTextureDescriptor descriptor) =>
        UpdateIntegratedBlurRequestCore(camera, descriptor);

    internal bool PrefetchFrame(Camera camera, RenderTextureDescriptor descriptor, out FrameInfo frame)
    {
        prefetchedFrameValid = PrepareFrameCore(camera, descriptor, out prefetchedFrame);
        prefetchedCamera = camera;
        prefetchedMultipassId = multipassId;
        prefetchedDescriptor = descriptor;
        prefetchedFrameAvailable = true;
        frame = prefetchedFrame;
        return prefetchedFrameValid;
    }

    public static bool TryGetImageRT(Camera camera, int featureIndex, out RTHandle handle) =>
        GlobalImageHandles.TryGetValue((camera, featureIndex), out handle) && handle != null && handle.rt;

    public static bool TryGetImageMipLevels(Camera camera, int featureIndex, out int mipLevels)
    {
        mipLevels = 0;
        if (!camera || !GlobalPasses.TryGetValue((camera, featureIndex), out var pass))
            return false;
        mipLevels = pass.GetBackdropMipLevels(camera);
        return true;
    }

    internal static bool TryGetImageSdfBinding(Camera camera, int featureIndex, GlassSdfDescriptor descriptor, out Vector4 sdfData)
    {
        sdfData = default;
        return camera && GlobalPasses.TryGetValue((camera, featureIndex), out var pass) && pass.sdfCache.TryRequest(descriptor, out sdfData);
    }

    internal static bool HasImagePass(Camera camera, int featureIndex) => camera && GlobalPasses.ContainsKey((camera, featureIndex));

    internal static GlassSdfRaycastField GetImageRaycastField(Camera camera, int featureIndex, GlassSdfDescriptor descriptor) =>
        camera && GlobalPasses.TryGetValue((camera, featureIndex), out var pass) ? pass.sdfCache.RequestRaycastField(descriptor) : null;

    public static bool TryGetImageMaterial(Camera camera, int featureIndex, Shader shader, out Material imageMaterial)
    {
        imageMaterial = null;
        if (!shader || !GlobalPasses.TryGetValue((camera, featureIndex), out var pass))
            return false;

        if (!pass.imageMaterials.TryGetValue(camera, out imageMaterial))
        {
            imageMaterial = CoreUtils.CreateEngineMaterial(shader);
            imageMaterial.EnableKeyword("HAS_BLUR");
            pass.imageMaterials[camera] = imageMaterial;
            pass.ConfigureImageMaterial(imageMaterial, camera);
        }
        return imageMaterial;
    }

    internal static void ConfigureImageMaterialForRendering(Camera camera, int featureIndex, Material imageMaterial)
    {
        if (camera && imageMaterial && GlobalPasses.TryGetValue((camera, featureIndex), out var pass))
            pass.ConfigureImageMaterial(imageMaterial, camera);
    }

    private void ConfigureImageMaterial(Material imageMaterial, Camera camera)
    {
        ConfigureImageMaterial(imageMaterial, camera, buildCameraPlan(camera));
    }

    private void ConfigureImageMaterial(Material imageMaterial, Camera camera, GlassCameraPlan plan)
    {
        ConfigureEdgeLightKeywords(imageMaterial, plan.lighting);
        imageMaterial.SetVector(EdgeLightingId, plan.lighting.lighting);
        imageMaterial.SetColor(EdgeHighlightId, plan.lighting.highlight);
        imageMaterial.SetColor(EdgeShadowId, plan.lighting.shadow);
        imageMaterial.SetFloat(BlurMaxLodId, plan.backdropMipLevels);
        var sourceScale = Vector2.one;
        if (camera && GlobalImageHandles.TryGetValue((camera, featureIndex), out var imageHandle) && imageHandle != null && imageHandle.rt)
        {
            sourceScale.x = imageHandle.rt.width / Mathf.Max(camera.pixelWidth, 1f);
            sourceScale.y = imageHandle.rt.height / Mathf.Max(camera.pixelHeight, 1f);
        }
        imageMaterial.SetVector(ImageSourceScaleId, new Vector4(sourceScale.x, sourceScale.y, 1f / Mathf.Max(sourceScale.x, 1e-4f), 1f / Mathf.Max(sourceScale.y, 1e-4f)));
        if (sdfCache.FieldAtlas != null && sdfCache.FieldAtlas.rt)
            imageMaterial.SetTexture(SdfAtlasId, sdfCache.FieldAtlas.rt);
        imageMaterial.SetFloat(SdfResolutionId, sdfCache.Resolution);
        imageMaterial.SetFloat(SdfMaxLodId, sdfCache.MaximumLod);
    }

    private static void ConfigureEdgeLightKeywords(Material targetMaterial, GlassLightingPlan lighting)
    {
        var edgeLightDisabled = lighting.lighting.z <= 1e-4f || Mathf.Max(lighting.highlight.a, lighting.shadow.a) <= 1e-4f;
        CoreUtils.SetKeyword(targetMaterial, EdgeLightDisabledKeyword, edgeLightDisabled);
        CoreUtils.SetKeyword(targetMaterial, OpposingEdgeLightKeyword, !edgeLightDisabled && lighting.mode == GlassEdgeLightMode.Opposing);
        CoreUtils.SetKeyword(targetMaterial, PointEdgeLightKeyword, !edgeLightDisabled && lighting.mode == GlassEdgeLightMode.Point);
    }

    internal RTHandle GetImageOutput(Camera camera, RenderTextureDescriptor descriptor, int mipLevels = 0)
    {
        if (!camera)
            return null;

        var useMipMap = mipLevels > 0;

        if (!imageHandleSystems.TryGetValue(camera, out var handleSystem))
        {
            handleSystem = new RTHandleSystem();
            handleSystem.Initialize(descriptor.width, descriptor.height);
            imageHandleSystems[camera] = handleSystem;
        }
        else if (handleSystem.GetMaxWidth() != descriptor.width || handleSystem.GetMaxHeight() != descriptor.height)
        {
            handleSystem.ResetReferenceSize(descriptor.width, descriptor.height);
        }

        imageHandles.TryGetValue(camera, out var handle);
        if (handle != null && (!handle.rt || handle.rt.graphicsFormat != descriptor.graphicsFormat || handle.rt.dimension != descriptor.dimension || handle.rt.volumeDepth != descriptor.volumeDepth || handle.rt.useMipMap != useMipMap))
        {
            handleSystem.Release(handle);
            imageHandles.Remove(camera);
            GlobalImageHandles.Remove((camera, featureIndex));
            handle = null;
        }
        if (handle == null)
        {
#if UNITY_2022_2_OR_NEWER
            handle = handleSystem.Alloc(Vector2.one, wrapMode: TextureWrapMode.Clamp, colorFormat: descriptor.graphicsFormat, memoryless: RenderTextureMemoryless.Depth, filterMode: useMipMap ? FilterMode.Trilinear : FilterMode.Bilinear, useMipMap: useMipMap, autoGenerateMips: false, msaaSamples: MSAASamples.None, vrUsage: descriptor.vrUsage, dimension: descriptor.dimension, slices: descriptor.volumeDepth);
#else
            handle = handleSystem.Alloc(Vector2.one, wrapMode: TextureWrapMode.Clamp, colorFormat: descriptor.graphicsFormat, memoryless: RenderTextureMemoryless.Depth, filterMode: useMipMap ? FilterMode.Trilinear : FilterMode.Bilinear, useMipMap: useMipMap, autoGenerateMips: false, msaaSamples: MSAASamples.None, dimension: descriptor.dimension, slices: descriptor.volumeDepth);
#endif
            imageHandles[camera] = handle;
        }

        GlobalPasses[(camera, featureIndex)] = this;
        GlobalImageHandles[(camera, featureIndex)] = handle;
        return handle;
    }

    internal bool PrepareFrame(Camera camera, RenderTextureDescriptor descriptor, out FrameInfo frame)
    {
        if (prefetchedFrameAvailable && prefetchedCamera == camera && prefetchedMultipassId == multipassId && MatchesPrefetchedDescriptor(descriptor))
        {
            prefetchedFrameAvailable = false;
            frame = prefetchedFrame;
            return prefetchedFrameValid;
        }

        prefetchedFrameAvailable = false;
        return PrepareFrameCore(camera, descriptor, out frame);
    }

    internal void ConfigureStereo(Camera camera, XRPass xr)
    {
        viewCount = xr != null && xr.enabled && xr.singlePassEnabled ? 2 : 1;
        multipassId = xr != null && xr.enabled && !xr.singlePassEnabled ? xr.multipassId : 0;
        firstEyeProjection = xr != null && xr.enabled
            ? new GlassScreenProjection(camera, xr.GetViewMatrix(), xr.GetProjMatrix()) : default;
        secondEyeProjection = viewCount == 2
            ? new GlassScreenProjection(camera, xr.GetViewMatrix(1), xr.GetProjMatrix(1)) : default;
    }

    private bool PrepareFrameCore(Camera camera, RenderTextureDescriptor descriptor, out FrameInfo frame)
    {
        frame = default;
        PruneImageResources();
        if (!material || !camera || descriptor.width <= 0 || descriptor.height <= 0)
            return false;
        if (SystemInfo.graphicsShaderLevel < 45)
        {
            if (!warnedShaderModel)
            {
                warnedShaderModel = true;
                Debug.LogWarning("Flexible Glass requires shader model 4.5 or newer. The effect is disabled on this device.");
            }
            return false;
        }

        UIGlass.PruneInvalidRegistrations();
        GlassImage.PruneInvalidRegistrations();
        var key = (camera, featureIndex);
        UIGlass.GlassDict.TryGetValue(key, out var registeredGlass);
        GlassImage.ImageDict.TryGetValue(key, out var registeredImages);
        if ((registeredGlass == null || registeredGlass.Count == 0) && (registeredImages == null || registeredImages.Count == 0))
            return false;
        if (registeredImages is { Count: > 0 })
            GlobalPasses[key] = this;

        sortedGlass.Clear();
        if (registeredGlass != null)
            sortedGlass.AddRange(registeredGlass);
        sortedGlass.Sort(HierarchyComparison);
        var renderScale = new Vector2(descriptor.width / Mathf.Max(1f, camera.pixelWidth), descriptor.height / Mathf.Max(1f, camera.pixelHeight));
        var cameraPlan = buildCameraPlan(camera);
        var compositionBlendPixels = cameraPlan.compositionBlend * (renderScale.x + renderScale.y) * 0.5f;
        preparedGlass.Clear();
        preparedImages.Clear();
        activeSdfDescriptors.Clear();
        foreach (var glass in sortedGlass)
        {
            if (!glass)
                continue;

            var firstVisible = glass.TryBuildGpuData(camera, renderScale, out var first, out var firstBlur, out var firstRaster, out var firstDescriptor, firstEyeProjection);
            if (viewCount == 1)
            {
                if (!firstVisible)
                    continue;
                preparedGlass.Add(new PreparedGlass(glass, first, firstBlur, firstRaster, firstDescriptor));
                activeSdfDescriptors.Add(firstDescriptor);
                continue;
            }

            var secondVisible = glass.TryBuildGpuData(camera, renderScale, out var second, out var secondBlur, out var secondRaster, out var secondDescriptor, secondEyeProjection);
            if (!firstVisible && !secondVisible)
                continue;
            var descriptorForBothEyes = firstVisible ? firstDescriptor : secondDescriptor;
            preparedGlass.Add(new PreparedGlass(glass, firstVisible ? first : second, firstBlur, firstRaster, descriptorForBothEyes, firstVisible));
            preparedGlass.Add(new PreparedGlass(glass, secondVisible ? second : first, secondBlur, secondRaster, descriptorForBothEyes, secondVisible));
            activeSdfDescriptors.Add(descriptorForBothEyes);
        }

        if (registeredImages != null)
        {
            foreach (var image in registeredImages)
            {
                if (!image)
                    continue;

                var hasDescriptor = image.TryBuildSdfDescriptor(camera, out var imageSdfDescriptor);
                var hasBlurBounds = image.TryGetBlurBounds(camera, renderScale, out var imageBounds, firstEyeProjection);
                if (viewCount == 2 && image.TryGetBlurBounds(camera, renderScale, out var secondBounds, secondEyeProjection))
                {
                    imageBounds = hasBlurBounds ? Rect.MinMaxRect(Mathf.Min(imageBounds.xMin, secondBounds.xMin), Mathf.Min(imageBounds.yMin, secondBounds.yMin), Mathf.Max(imageBounds.xMax, secondBounds.xMax), Mathf.Max(imageBounds.yMax, secondBounds.yMax)) : secondBounds;
                    hasBlurBounds = true;
                }
                preparedImages.Add(new PreparedImage(image, imageSdfDescriptor, imageBounds, hasDescriptor, hasBlurBounds));
                if (hasDescriptor)
                    activeSdfDescriptors.Add(imageSdfDescriptor);
            }
        }

        CollectOtherCameraDescriptors(key);
        sdfCache.BeginFrame(++sdfFrame, activeSdfDescriptors);

        gpuElements.Clear();
        var blurMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var blurMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var rasterMin = blurMin;
        var rasterMax = blurMax;
        var shadowMode = 0;
        var hasAdditiveAppearance = false;
        var uniformAppearance = true;
        var firstAdditiveAppearance = default(GlassElementGpu);
        var compositionPadding = Mathf.Max(0f, compositionBlendPixels) + 1f;
        var rasterPadding = Mathf.Max(0f, compositionBlendPixels) * 0.25f + 1f;
        var uvStartsAtTop = SystemInfo.graphicsUVStartsAtTop;
        foreach (var prepared in preparedGlass)
        {
            var gpuElement = prepared.element;
            if (!sdfCache.TryRequest(prepared.descriptor, ref gpuElement))
                continue;

            if (!prepared.visible)
            {
                gpuElement.screenBounds = new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue);
                gpuElements.Add(gpuElement);
                continue;
            }
            PrepareAffineOptics(ref gpuElement);
            var influenceMin = Vector2.Min(prepared.rasterBounds.min, prepared.blurBounds.min);
            var influenceMax = Vector2.Max(prepared.rasterBounds.max, prepared.blurBounds.max);
            gpuElement.screenBounds = AlignBoundsToPixelQuads(influenceMin - Vector2.one * compositionPadding, influenceMax + Vector2.one * compositionPadding, descriptor.height, uvStartsAtTop);
            gpuElements.Add(gpuElement);
            if (prepared.glass.operation == GlassSdfOperation.Add)
            {
                if (!hasAdditiveAppearance)
                {
                    firstAdditiveAppearance = gpuElement;
                    hasAdditiveAppearance = true;
                }
                else
                {
                    uniformAppearance &= gpuElement.color.Equals(firstAdditiveAppearance.color) &&
                                         gpuElement.optics0.Equals(firstAdditiveAppearance.optics0) &&
                                         gpuElement.optics1.Equals(firstAdditiveAppearance.optics1) &&
                                         gpuElement.lighting.Equals(firstAdditiveAppearance.lighting) &&
                                         gpuElement.shadow.y.Equals(firstAdditiveAppearance.shadow.y);
                }
            }
            if (prepared.glass.operation == GlassSdfOperation.Add && prepared.glass.appearance.HasVisibleShadow())
                shadowMode = Mathf.Max(shadowMode, prepared.glass.appearance.shadowOffset.sqrMagnitude > 1e-8f ? 2 : 1);
            blurMin = Vector2.Min(blurMin, prepared.blurBounds.min);
            blurMax = Vector2.Max(blurMax, prepared.blurBounds.max);
            rasterMin = Vector2.Min(rasterMin, prepared.rasterBounds.min);
            rasterMax = Vector2.Max(rasterMax, prepared.rasterBounds.max);
        }

        var hasGlassImages = false;
        var retainedImageCount = 0;
        foreach (var prepared in preparedImages)
        {
            if (prepared.hasDescriptor && sdfCache.TryRequest(prepared.descriptor, out var imageSdfData))
            {
                prepared.image.SetRetainedSdfBinding(imageSdfData);
                prepared.image.SetRaycastField(prepared.image.SdfRaycast && prepared.image.raycastTarget ? sdfCache.RequestRaycastField(prepared.descriptor) : null);
                retainedImageCount++;
            }
            else if (!prepared.hasDescriptor)
                prepared.image.ClearRetainedSdfBinding();

            if (!prepared.hasBlurBounds)
                continue;

            hasGlassImages = true;
            blurMin = Vector2.Min(blurMin, prepared.blurBounds.min);
            blurMax = Vector2.Max(blurMax, prepared.blurBounds.max);
        }

        if (imageMaterials.TryGetValue(camera, out var imageMaterial))
            ConfigureImageMaterial(imageMaterial, camera, cameraPlan);

        if (gpuElements.Count == 0 && !hasGlassImages && retainedImageCount == 0)
            return false;
        var blurPlan = cameraPlan.blur;
        blurPlan.ResolveStandaloneRadius(descriptor.height);
        var requestedBackdropMipLevels = cameraPlan.backdropMipLevels;
        var hasRetainedFields = gpuElements.Count > 0 || retainedImageCount > 0;
        var sdfOnly = gpuElements.Count == 0 && !hasGlassImages;
        if (sdfOnly)
        {
            frame = new FrameInfo(camera, new RectInt(0, 0, 1, 1), default, descriptor.width, descriptor.height, 0, compositionBlendPixels, 0, false, hasRetainedFields, true, false, requestedBackdropMipLevels, blurPlan, cameraPlan.blurFormat, cameraPlan.lighting);
            return true;
        }
        var blurReach = GetRegionalPadding(blurPlan, compositionBlendPixels, cameraPlan.blurPadding, (renderScale.x + renderScale.y) * 0.5f);
        var blurMinX = Mathf.Clamp(Mathf.FloorToInt(blurMin.x - blurReach), 0, descriptor.width);
        var blurMinY = Mathf.Clamp(Mathf.FloorToInt(blurMin.y - blurReach), 0, descriptor.height);
        var blurMaxX = Mathf.Clamp(Mathf.CeilToInt(blurMax.x + blurReach), 0, descriptor.width);
        var blurMaxY = Mathf.Clamp(Mathf.CeilToInt(blurMax.y + blurReach), 0, descriptor.height);
        var rasterMinX = 0;
        var rasterMinY = 0;
        var rasterMaxX = 0;
        var rasterMaxY = 0;
        if (gpuElements.Count > 0)
        {
            rasterMinX = Mathf.Clamp(Mathf.FloorToInt(rasterMin.x - rasterPadding), 0, descriptor.width);
            rasterMinY = Mathf.Clamp(Mathf.FloorToInt(rasterMin.y - rasterPadding), 0, descriptor.height);
            rasterMaxX = Mathf.Clamp(Mathf.CeilToInt(rasterMax.x + rasterPadding), 0, descriptor.width);
            rasterMaxY = Mathf.Clamp(Mathf.CeilToInt(rasterMax.y + rasterPadding), 0, descriptor.height);
        }
        if (blurMaxX <= blurMinX || blurMaxY <= blurMinY || gpuElements.Count > 0 && (rasterMaxX <= rasterMinX || rasterMaxY <= rasterMinY))
            return false;

        GraphicsBuffer frameElements = null;
        if (gpuElements.Count > 0)
        {
            frameElements = EnsureElementBuffer(gpuElements.Count);
            frameElements.SetData(gpuElements);
        }
        var blurRegion = new RectInt(blurMinX, blurMinY, blurMaxX - blurMinX, blurMaxY - blurMinY);
        if (!blurPlan.integrated)
            blurRegion = PadKawaseRegion(blurRegion, blurPlan.kawaseIterations);
        var rasterRegion = new RectInt(rasterMinX, rasterMinY, rasterMaxX - rasterMinX, rasterMaxY - rasterMinY);
        frame = new FrameInfo(camera, blurRegion, rasterRegion, descriptor.width, descriptor.height, gpuElements.Count / viewCount, compositionBlendPixels, shadowMode, hasGlassImages, hasRetainedFields, false, hasAdditiveAppearance && uniformAppearance, requestedBackdropMipLevels, blurPlan, cameraPlan.blurFormat, cameraPlan.lighting, frameElements);
        return true;
    }

    private int GetBackdropMipLevels(Camera camera) => buildCameraPlan(camera).backdropMipLevels;

    private bool MatchesPrefetchedDescriptor(RenderTextureDescriptor descriptor) =>
        prefetchedDescriptor.width == descriptor.width &&
        prefetchedDescriptor.height == descriptor.height &&
        prefetchedDescriptor.graphicsFormat == descriptor.graphicsFormat &&
        prefetchedDescriptor.dimension == descriptor.dimension &&
        prefetchedDescriptor.volumeDepth == descriptor.volumeDepth &&
        prefetchedDescriptor.msaaSamples == descriptor.msaaSamples;

    private void CollectOtherCameraDescriptors((Camera camera, int featureNumber) currentKey)
    {
        foreach (var registered in UIGlass.GlassDict)
        {
            if (registered.Key.featureNumber != featureIndex || registered.Key == currentKey)
                continue;
            foreach (var glass in registered.Value)
                if (glass && glass.TryBuildSdfDescriptor(registered.Key.camera, out var descriptor))
                    activeSdfDescriptors.Add(descriptor);
        }

        foreach (var registered in GlassImage.ImageDict)
        {
            if (registered.Key.featureNumber != featureIndex || registered.Key == currentKey)
                continue;
            foreach (var image in registered.Value)
                if (image && image.TryBuildSdfDescriptor(registered.Key.camera, out var descriptor))
                    activeSdfDescriptors.Add(descriptor);
        }
    }

    private void PruneImageResources()
    {
        staleImageCameras.Clear();
        foreach (var entry in imageHandleSystems)
        {
            if (!entry.Key)
                staleImageCameras.Add(entry.Key);
        }
        foreach (var camera in staleImageCameras)
        {
            imageHandleSystems[camera].Dispose();
            imageHandleSystems.Remove(camera);
            imageHandles.Remove(camera);
            GlobalImageHandles.Remove((camera, featureIndex));
            GlobalPasses.Remove((camera, featureIndex));
            if (imageMaterials.Remove(camera, out var imageMaterial))
                CoreUtils.Destroy(imageMaterial);
        }

        staleGlobalPassKeys.Clear();
        foreach (var entry in GlobalPasses)
        {
            if (entry.Value == this && !entry.Key.camera)
                staleGlobalPassKeys.Add(entry.Key);
        }
        foreach (var key in staleGlobalPassKeys)
            GlobalPasses.Remove(key);
    }

    private static float GetBlurReach(GlassBlurPlan plan)
    {
        if (plan.integrated)
            return plan.integratedReach;

        var levels = Mathf.Pow(2f, plan.kawaseIterations + 1) - 2f;
        return 2f + (plan.kawaseRadius + 1f) * levels * 2f;
    }

    internal static float GetRegionalPadding(GlassBlurPlan plan, float compositionBlendPixels, float blurPadding, float renderScale) =>
        GetBlurReach(plan) + Mathf.Max(0f, compositionBlendPixels) * 0.25f + Mathf.Max(0f, blurPadding) * Mathf.Max(0f, renderScale) + 1f;

    internal static Vector4 AlignBoundsToPixelQuads(Vector2 min, Vector2 max, int targetHeight, bool uvStartsAtTop)
    {
        // Culling must keep the same elements in all four lanes used by fwidth in the composite.
        var yOffset = uvStartsAtTop ? targetHeight & 1 : 0;
        return new Vector4(
            Mathf.Floor(min.x * 0.5f) * 2f,
            Mathf.Floor((min.y - yOffset) * 0.5f) * 2f + yOffset,
            Mathf.Ceil(max.x * 0.5f) * 2f,
            Mathf.Ceil((max.y - yOffset) * 0.5f) * 2f + yOffset);
    }

    internal static float GetKawaseResolutionScale(int height) => Mathf.Max(1, height) / 1080f;

    internal static int GetKawaseDimension(int fullSize, int level) => Mathf.Max(1, fullSize >> (level + 1));

    internal static RectInt PadKawaseRegion(RectInt region, int iterations)
    {
        var alignment = 1 << Mathf.Clamp(iterations, 0, 6);
        var minX = Mathf.FloorToInt(region.xMin / (float)alignment) * alignment;
        var minY = Mathf.FloorToInt(region.yMin / (float)alignment) * alignment;
        var maxX = Mathf.CeilToInt(region.xMax / (float)alignment) * alignment;
        var maxY = Mathf.CeilToInt(region.yMax / (float)alignment) * alignment;
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    internal static int GetReconstructionLevelCount(int width, int height, int requestedLevels)
    {
        var size = Mathf.Max(width, height);
        var availableLevels = 0;
        while (size > 1)
        {
            size >>= 1;
            availableLevels++;
        }
        return Mathf.Min(Mathf.Max(0, requestedLevels), availableLevels);
    }

    private static int GetBlurReconstructionLevelCount(FrameInfo frame)
    {
        if (frame.blurPlan.integrated)
            return frame.elementCount > 0 || frame.hasGlassImages ? frame.reconstructionLevels : 0;
        if (frame.elementCount == 0)
            return 0;
        return frame.blurPlan.kawaseIterations == 0 ? 0 : GetReconstructionLevelCount(frame.blurRegion.width, frame.blurRegion.height, frame.reconstructionLevels);
    }

    private static int GetImageReconstructionLevelCount(FrameInfo frame) => frame.hasGlassImages ? frame.reconstructionLevels : 0;

    private static int GetBackdropReconstructionLevelCount(FrameInfo frame) =>
        UsesBlur(frame) ? 0 : frame.reconstructionLevels;

    private static bool UsesBlur(FrameInfo frame) => frame.blurPlan.integrated || frame.blurPlan.kawaseIterations > 0;

    private static bool NeedsSharpBackdrop(FrameInfo frame) => frame.elementCount > 0 && !UsesBlur(frame);

    private GraphicsBuffer EnsureElementBuffer(int count)
    {
        ref var buffer = ref (multipassId != 0 ? ref rightEyeElementBuffer : ref elementBuffer);
        ref var capacity = ref (multipassId != 0 ? ref rightEyeElementBufferCapacity : ref elementBufferCapacity);
        if (buffer != null && capacity >= count)
            return buffer;

        buffer?.Dispose();
        capacity = Mathf.NextPowerOfTwo(count);
        return buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, GlassElementGpu.Stride);
    }

    private void PrepareAffineOptics(ref GlassElementGpu element)
    {
        if (Mathf.Abs(element.screenToUv2.x) > 1e-7f || Mathf.Abs(element.screenToUv2.y) > 1e-7f)
            return;

        var domainSize = Vector2.Max((Vector2)element.sizeOperationShape + 2f * Vector2.Max((Vector2)element.sdfData, Vector2.zero), Vector2.one * 1e-5f);
        var inverseDenominator = 1f / Mathf.Max(Mathf.Abs(element.screenToUv2.z), 1e-6f);
        var localDx = new Vector2(element.screenToUv0.x, element.screenToUv1.x) * inverseDenominator;
        var localDy = new Vector2(element.screenToUv0.y, element.screenToUv1.y) * inverseDenominator;
        var localTexelSize = domainSize / Mathf.Max(sdfCache.Resolution, 1);
        var determinant = Mathf.Max(Mathf.Abs(localDx.x * localDy.y - localDx.y * localDy.x), 1e-6f);
        var screenTexelX = new Vector2(localDy.y, -localDx.y).magnitude * localTexelSize.x / determinant;
        var screenTexelY = new Vector2(-localDy.x, localDx.x).magnitude * localTexelSize.y / determinant;
        var screenTexelSize = Mathf.Sqrt(Mathf.Max(screenTexelX * screenTexelY, 1e-6f));
        var thicknessTexels = Mathf.Max(element.optics1.x, 0f) / screenTexelSize;
        element.screenToUv0.w = Mathf.Clamp(Mathf.Log(1f + Mathf.Max(element.sdfData.w, 0f) * thicknessTexels, 2f), 0f, sdfCache.MaximumLod);
        element.screenToUv1.w = Mathf.Clamp(Mathf.Log(1f + thicknessTexels, 2f), 0f, sdfCache.MaximumLod);
        var localDxLengthSquared = localDx.sqrMagnitude;
        var localDyLengthSquared = localDy.sqrMagnitude;
        var maximumLengthSquared = Mathf.Max(localDxLengthSquared, localDyLengthSquared, 1e-12f);
        var orthogonalityScale = Mathf.Sqrt(Mathf.Max(localDxLengthSquared * localDyLengthSquared, 1e-24f));
        var conformal = Mathf.Abs(localDxLengthSquared - localDyLengthSquared) <= maximumLengthSquared * 1e-5f && Mathf.Abs(Vector2.Dot(localDx, localDy)) <= orthogonalityScale * 1e-5f;
        element.sizeOperationShape.w = conformal ? 1f / Mathf.Sqrt((localDxLengthSquared + localDyLengthSquared) * 0.5f) : 0f;
        element.screenToUv2.w = 1f;
    }

#if FLEXIBLE_UI_COMPATIBILITY
#if UNITY_2023_3_OR_NEWER
    [Obsolete]
#endif
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        ConfigureStereo(renderingData.cameraData.camera, renderingData.cameraData.xr);
        var descriptor = renderingData.cameraData.cameraTargetDescriptor;
        if (!PrepareFrame(renderingData.cameraData.camera, descriptor, out var frame))
            return;
        if (frame.sdfOnly && !sdfCache.HasPendingJobs)
            return;
        descriptor.depthBufferBits = 0;
        descriptor.depthStencilFormat = GraphicsFormat.None;
        descriptor.msaaSamples = 1;
        descriptor.enableRandomWrite = false;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        descriptor.mipCount = 1;

        var cmd = CommandBufferPool.Get(ProfilerTag);
        // Earlier passes can swap the renderer's color buffers after SetupRenderPasses.
#if UNITY_2022_2_OR_NEWER
#pragma warning disable CS0618 // Camera target access is required by this compatibility-only path.
        var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
#pragma warning restore CS0618
#else
        var source = renderingData.cameraData.renderer.cameraColorTarget;
#endif
        ExecuteCompatibility(cmd, source, descriptor, frame);
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    private void ExecuteCompatibility(CommandBuffer cmd, RenderTargetIdentifier source, RenderTextureDescriptor descriptor, FrameInfo frame)
    {
        sdfCache.GeneratePending(cmd);
        if (frame.sdfOnly)
            return;
        var region = frame.blurRegion;
        RenderTargetIdentifier finalBlur;
        if (frame.blurPlan.integrated)
        {
            finalBlur = GetImageOutput(frame.camera, descriptor, GetBlurReconstructionLevelCount(frame));
        }
        else
        {
            var blurDescriptor = descriptor;
            blurDescriptor.graphicsFormat = frame.blurFormat;
            blurDescriptor.width = region.width;
            blurDescriptor.height = region.height;
            var captureMipLevels = frame.blurPlan.kawaseIterations > 0 ? GetBlurReconstructionLevelCount(frame) : 0;
            blurDescriptor.useMipMap = captureMipLevels > 0;
            blurDescriptor.mipCount = captureMipLevels + 1;
            cmd.GetTemporaryRT(CaptureTextureId, blurDescriptor, captureMipLevels > 0 ? FilterMode.Trilinear : FilterMode.Bilinear);
            ExtractRegion(cmd, source, CaptureTextureId, frame);
            blurDescriptor.useMipMap = false;
            blurDescriptor.mipCount = 1;
            finalBlur = ApplyKawaseCompatibility(cmd, CaptureTextureId, blurDescriptor, frame);
            if (frame.hasGlassImages)
            {
                blurDescriptor.width = frame.targetWidth;
                blurDescriptor.height = frame.targetHeight;
                PublishImageBlur(cmd, finalBlur, GetImageOutput(frame.camera, blurDescriptor, GetImageReconstructionLevelCount(frame)), frame);
            }
        }

        if (frame.elementCount > 0)
        {
            if (NeedsSharpBackdrop(frame))
            {
                descriptor.width = frame.targetWidth;
                descriptor.height = frame.targetHeight;
                var reconstructionLevels = GetBackdropReconstructionLevelCount(frame);
                descriptor.useMipMap = reconstructionLevels > 0;
                descriptor.mipCount = reconstructionLevels + 1;
                cmd.GetTemporaryRT(BackdropTextureId, descriptor, FilterMode.Trilinear);
                CaptureBackdrop(cmd, source, BackdropTextureId, frame);
                Composite(cmd, source, BackdropTextureId, finalBlur, frame);
                cmd.ReleaseTemporaryRT(BackdropTextureId);
            }
            else
                Composite(cmd, source, finalBlur, finalBlur, frame);
        }

        if (!frame.blurPlan.integrated)
        {
            cmd.ReleaseTemporaryRT(CaptureTextureId);
            var textureCount = frame.blurPlan.kawaseIterations;
            for (int i = 0; i < textureCount; i++)
                cmd.ReleaseTemporaryRT(KawaseTextureIds[i]);
        }
    }

    private RenderTargetIdentifier ApplyKawaseCompatibility(CommandBuffer cmd, RenderTargetIdentifier capture, RenderTextureDescriptor descriptor, FrameInfo frame)
    {
        var plan = frame.blurPlan;
        var reconstructionLevels = GetBlurReconstructionLevelCount(frame);
        var previous = capture;
        var sourceWidth = frame.blurRegion.width;
        var sourceHeight = frame.blurRegion.height;
        for (int i = 0; i < plan.kawaseIterations; i++)
        {
            var width = GetKawaseDimension(frame.blurRegion.width, i);
            var height = GetKawaseDimension(frame.blurRegion.height, i);
            descriptor.width = width;
            descriptor.height = height;
            cmd.GetTemporaryRT(KawaseTextureIds[i], descriptor, FilterMode.Bilinear);
            KawaseBlit(cmd, previous, KawaseTextureIds[i], sourceWidth, sourceHeight, width, height, plan.kawaseRadius, KawaseDownPass, 0f, default);
            previous = KawaseTextureIds[i];
            sourceWidth = width;
            sourceHeight = height;
        }

        for (int i = 0; i < plan.kawaseIterations; i++)
        {
            var sourceLevel = plan.kawaseIterations - i - 2;
            var width = sourceLevel >= 0 ? GetKawaseDimension(frame.blurRegion.width, sourceLevel) : frame.blurRegion.width;
            var height = sourceLevel >= 0 ? GetKawaseDimension(frame.blurRegion.height, sourceLevel) : frame.blurRegion.height;
            var finalUpsample = i == plan.kawaseIterations - 1;
            var destination = finalUpsample ? CaptureTextureId : KawaseTextureIds[sourceLevel];
            KawaseBlit(cmd, previous, destination, sourceWidth, sourceHeight, width, height, plan.kawaseRadius, KawaseUpPass, finalUpsample ? plan.kawaseDitherStrength : 0f, finalUpsample ? frame.blurRegion.position : default);
            previous = destination;
            sourceWidth = width;
            sourceHeight = height;
        }

        if (reconstructionLevels > 0)
            cmd.GenerateMips(previous);

        return previous;
    }
#endif

    private void ExtractRegion(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, FrameInfo frame)
    {
        var region = frame.blurRegion;
        cmd.SetGlobalVector(SourceRegionId, new Vector4((float)region.x / frame.targetWidth, (float)region.y / frame.targetHeight, (float)region.width / frame.targetWidth, (float)region.height / frame.targetHeight));
        FullScreenBlit(cmd, source, destination, region.width, region.height, ExtractPass);
    }

    private void CaptureBackdrop(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, FrameInfo frame)
    {
        cmd.SetGlobalVector(SourceRegionId, new Vector4(0f, 0f, 1f, 1f));
        FullScreenBlit(cmd, source, destination, frame.targetWidth, frame.targetHeight, ExtractPass);
        if (GetBackdropReconstructionLevelCount(frame) > 0)
            cmd.GenerateMips(destination);
    }

    private void KawaseBlit(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, int sourceWidth, int sourceHeight, int destinationWidth, int destinationHeight, float sampleOffset, int passIndex, float ditherStrength, Vector2 ditherOffset)
    {
        cmd.SetGlobalVector(SourceTexelSizeId, new Vector4(1f / Mathf.Max(1, sourceWidth), 1f / Mathf.Max(1, sourceHeight), sourceWidth, sourceHeight));
        cmd.SetGlobalFloat(SampleOffsetId, Mathf.Max(0f, sampleOffset));
        cmd.SetGlobalFloat(DitherStrengthId, Mathf.Max(0f, ditherStrength));
        cmd.SetGlobalVector(DitherOffsetId, ditherOffset);
        FullScreenBlit(cmd, source, destination, destinationWidth, destinationHeight, passIndex);
    }

    private void Composite(CommandBuffer cmd, RenderTargetIdentifier destination, RenderTargetIdentifier sharp, RenderTargetIdentifier blurred, FrameInfo frame)
    {
        ConfigureEdgeLightKeywords(material, frame.lighting);
        cmd.SetGlobalVector(EdgeLightingId, frame.lighting.lighting);
        cmd.SetGlobalColor(EdgeHighlightId, frame.lighting.highlight);
        cmd.SetGlobalColor(EdgeShadowId, frame.lighting.shadow);
        cmd.SetGlobalTexture(SharpTexId, sharp);
        cmd.SetGlobalTexture(BlurTexId, blurred);
        cmd.SetGlobalVector(RegionId, new Vector4(frame.rasterRegion.x, frame.rasterRegion.y, frame.rasterRegion.width, frame.rasterRegion.height));
        cmd.SetGlobalVector(BlurRegionId, frame.blurPlan.integrated ? new Vector4(0f, 0f, frame.targetWidth, frame.targetHeight) : new Vector4(frame.blurRegion.x, frame.blurRegion.y, frame.blurRegion.width, frame.blurRegion.height));
        cmd.SetGlobalVector(TargetSizeId, new Vector4(frame.targetWidth, frame.targetHeight, 1f / frame.targetWidth, 1f / frame.targetHeight));
        cmd.SetGlobalFloat(CompositionBlendId, frame.compositionBlend);
        cmd.SetGlobalFloat(CompositionInverseBlendId, frame.compositionBlend > 1e-4f ? 1f / frame.compositionBlend : 0f);
        cmd.SetGlobalInt(UniformAppearanceId, frame.uniformAppearance ? 1 : 0);
        cmd.SetGlobalInt(ShadowModeId, frame.shadowMode);
        cmd.SetGlobalFloat(ReconstructionMaxLodId, frame.reconstructionLevels);
        cmd.SetGlobalFloat(BlurMaxLodId, GetBlurReconstructionLevelCount(frame));
        cmd.SetGlobalFloat(UseBlurId, frame.blurPlan.integrated || frame.blurPlan.kawaseIterations > 0 ? 1f : 0f);
        cmd.SetGlobalInt(ElementCountId, frame.elementCount);
        cmd.SetGlobalBuffer(ElementBufferId, frame.elements);
        cmd.SetGlobalTexture(SdfAtlasId, sdfCache.FieldAtlas);
        cmd.SetGlobalFloat(SdfResolutionId, sdfCache.Resolution);
        cmd.SetGlobalFloat(SdfMaxLodId, sdfCache.MaximumLod);
        DrawRegion(cmd, destination, frame, CompositePass);
    }

    private void PublishImageBlur(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, FrameInfo frame)
    {
        cmd.SetRenderTarget(new RenderTargetIdentifier(destination, 0, CubemapFace.Unknown, -1));
        cmd.ClearRenderTarget(false, true, Color.clear);
        cmd.SetGlobalVector(SourceRegionId, new Vector4(0f, 0f, 1f, 1f));
        cmd.SetGlobalTexture(MainTexId, source);
        DrawRegion(cmd, destination, frame.blurRegion, frame.targetWidth, frame.targetHeight, ExtractPass);
        if (GetImageReconstructionLevelCount(frame) > 0)
            cmd.GenerateMips(destination);
    }

    private void FullScreenBlit(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, int width, int height, int passIndex)
        => FullScreenBlit(cmd, source, destination, width, height, material, passIndex);

    private void FullScreenBlit(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, int width, int height, Material blitMaterial, int passIndex)
    {
        cmd.SetGlobalTexture(MainTexId, source);
        cmd.SetGlobalTexture(DestinationTexId, destination);
        cmd.SetRenderTarget(new RenderTargetIdentifier(destination, 0, CubemapFace.Unknown, -1), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
        cmd.SetViewport(new Rect(0f, 0f, width, height));
        cmd.DisableScissorRect();
        cmd.DrawMesh(FullScreenMesh, Matrix4x4.identity, blitMaterial, 0, passIndex);
    }

    private void DrawRegion(CommandBuffer cmd, RenderTargetIdentifier destination, FrameInfo frame, int passIndex)
        => DrawRegion(cmd, destination, frame.rasterRegion, frame.targetWidth, frame.targetHeight, passIndex);

    private void DrawRegion(CommandBuffer cmd, RenderTargetIdentifier destination, RectInt region, int targetWidth, int targetHeight, int passIndex)
    {
        cmd.SetRenderTarget(new RenderTargetIdentifier(destination, 0, CubemapFace.Unknown, -1), RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
        cmd.SetViewport(new Rect(0f, 0f, targetWidth, targetHeight));
        cmd.DisableScissorRect();
        var left = (float)region.x / targetWidth * 2f - 1f;
        var right = (float)region.xMax / targetWidth * 2f - 1f;
        var bottom = (float)region.y / targetHeight * 2f - 1f;
        var top = (float)region.yMax / targetHeight * 2f - 1f;
        var matrix = Matrix4x4.TRS(new Vector3((left + right) * 0.5f, (bottom + top) * 0.5f), Quaternion.identity, new Vector3((right - left) * 0.5f, (top - bottom) * 0.5f, 1f));
        cmd.DrawMesh(FullScreenMesh, matrix, material, 0, passIndex);
    }

    private static Mesh fullScreenMesh;

#if UNITY_6000_6_OR_NEWER
    [Unity.Scripting.LifecycleManagement.OnCodeUnloading]
    private static void ReleaseStaticResources()
    {
        var passes = new HashSet<FlexibleGlassPass>(GlobalPasses.Values);
        foreach (var activePass in passes)
            activePass?.Dispose();
        GlobalPasses.Clear();
        GlobalImageHandles.Clear();

        if (fullScreenMesh)
            CoreUtils.Destroy(fullScreenMesh);
        fullScreenMesh = null;
    }
#endif

    private static Mesh FullScreenMesh
    {
        get
        {
            if (fullScreenMesh)
                return fullScreenMesh;

            fullScreenMesh = new Mesh { name = "Flexible Glass Quad" };
            fullScreenMesh.SetVertices(new[]
            {
                new Vector3(-1f, -1f), new Vector3(-1f, 1f), new Vector3(1f, -1f), new Vector3(1f, 1f)
            });
            fullScreenMesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 1f)
            });
            fullScreenMesh.SetIndices(new[] { 0, 1, 2, 2, 1, 3 }, MeshTopology.Triangles, 0, false);
            fullScreenMesh.UploadMeshData(true);
            return fullScreenMesh;
        }
    }
}
}
