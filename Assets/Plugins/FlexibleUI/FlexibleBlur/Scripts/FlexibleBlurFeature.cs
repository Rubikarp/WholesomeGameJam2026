#if !UNITY_6000_3_OR_NEWER || (URP_COMPATIBILITY_MODE && !UNITY_6000_4_OR_NEWER)
#define FLEXIBLE_UI_COMPATIBILITY
#endif

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.XR;

#if !UNITY_2023_1_OR_NEWER
using System.Reflection;
#endif

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace JeffGrawAssets.FlexibleUI
{
public class FlexibleBlurFeature : ScriptableRendererFeature 
{
    private enum FilterMode { Point, Bilinear }

#if UNITY_EDITOR
    public static readonly string RenderPassEventFieldName = nameof(renderPassEvent);
    public static readonly string DestinationFilterModeFieldName = nameof(destinationFilterMode);
    public static readonly string UIBlurLayersSeeLowerFieldName = nameof(uiBlurLayersSeeLower);
    public static readonly string BlurredImagesSeeUIBlursFieldName = nameof(blurredImagesSeeUIBlurs);
    public static readonly string BlurredImageLayersSeeLowerFieldName = nameof(blurredImageLayersSeeLower);
    public static readonly string UseComputeShadersFieldName = nameof(useComputeShaders);
    public static readonly string ResultFormatFieldName = nameof(resultFormat);
    public static readonly string BlurFormatFieldName = nameof(blurFormat);
    public static readonly string PlatformDataFieldName = nameof(platformData);
    public static readonly string LayerResolutionRatioFieldName = nameof(layerResolutionRatio);
    public static readonly string MaxLayerResolutionFieldName = nameof(maxLayerResolution);
    public static readonly string OverlayCompatibilityFixFieldName = nameof(overlayCompatibilityFix);
    public static readonly string MaxStabilizationPixelsFieldName = nameof(maxStabilizationPixels);
    //public static readonly string TestCaseFieldName = nameof(testCase);

    [HideInInspector] public string platformData;

    public void UsePlatformSettings(BuildTarget target)
    {
        var targetKey = BuildPipeline.GetBuildTargetName(target);
        var dataDict = DecodePlatformData(platformData);

        if (!dataDict.TryGetValue(targetKey, out var value))
            return;

        useComputeShaders = value.useComputeShaders;
        resultFormat = value.resultFormat;
        blurFormat = value.blurFormat;
        layerResolutionRatio = value.layerResolutionRatio;
        maxLayerResolution = value.maxLayerResolution;
    }

    public static Dictionary<string, (bool useComputeShaders, GraphicsFormat resultFormat, GraphicsFormat blurFormat, float layerResolutionRatio, int maxLayerResolution)> DecodePlatformData(string input)
    {
        var dictionary = new Dictionary<string, (bool useComputeShaders, GraphicsFormat resultFormat, GraphicsFormat blurFormat, float layerResolutionRatio, int maxLayerResolution)>();
        if (string.IsNullOrEmpty(input))
            return dictionary;

        var entries = input.Split(';');
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var parts = entry.Split(':');
            var key = parts[0];
            var values = parts[1].Split(',');
            if (!bool.TryParse(values[0], out var useComputeShaderValue)) { useComputeShaderValue = false; }
            var resultFormatValue = (GraphicsFormat)Enum.Parse(typeof(GraphicsFormat), values[1]);
            var blurFormatValue = (GraphicsFormat)Enum.Parse(typeof(GraphicsFormat), values[2]);
            var layerResolutionRatioValue = 1f;
            var maxLayerResolutionValue = 1080;
            if (values.Length > 3)
            {
                float.TryParse(values[3], out layerResolutionRatioValue);
                int.TryParse(values[4], out maxLayerResolutionValue);
            }
            dictionary.Add(key, (useComputeShaderValue, resultFormatValue, blurFormatValue, layerResolutionRatioValue, maxLayerResolutionValue));
        }
        return dictionary;
    }

    public static string EncodePlatformData(Dictionary<string, (bool useComputeShaders, GraphicsFormat resultFormat, GraphicsFormat blurFormat, float layerResolutionRatio, int maxLayerResolution)> input)
    {
        var entries = input.Select(x => $"{x.Key}:{x.Value.useComputeShaders},{x.Value.resultFormat},{x.Value.blurFormat},{x.Value.layerResolutionRatio.ToString(CultureInfo.InvariantCulture)},{x.Value.maxLayerResolution.ToString(CultureInfo.InvariantCulture)}");
        return string.Join(";", entries);
    }
#endif

    public static readonly Dictionary<(Camera camera, int featureIdx), List<IBlur>> ImageBasedBlurDict = new();
    public static readonly Dictionary<(Camera camera, int featureIdx), int> ImageBasedLayersPerCameraDict = new();
    public static readonly Dictionary<GraphicsFormat, GraphicsFormat> ResultFormatFallbackDict = new();
    public static readonly Dictionary<GraphicsFormat, GraphicsFormat> BlurFormatFallbackDict = new();

    static FlexibleBlurFeature()
    {
        SceneManager.sceneLoaded += (_, _) => RemoveEmptyDictEntriesOnStartup();
#if UNITY_EDITOR
        EditorSceneManager.sceneOpened += (_, _) => RemoveEmptyDictEntriesOnStartup();
#endif
        void RemoveEmptyDictEntriesOnStartup()
        {
            for (int i = 0; i < ImageBasedBlurDict.Count; i++)
            {
                var key = ImageBasedBlurDict.ElementAt(i).Key;
                if (key.camera)
                    continue;

                ImageBasedBlurDict.Remove(key);
                i--;
            }

            for (int i = 0; i < ImageBasedLayersPerCameraDict.Count; i++)
            {
                var key = ImageBasedLayersPerCameraDict.ElementAt(i).Key;
                if (key.camera)
                    continue;

                ImageBasedLayersPerCameraDict.Remove(key);
                i--;
            }
        }
    }

    public static bool GloballyPaused { get; set; }

    public static readonly Dictionary<(Camera, int featureIdx), FlexibleBlurPass> GlobalFlexibleBlurPassDict = new();
    public static readonly Dictionary<(Camera camera, int featureIdx, int layer), RTHandle> NewLayerHandles = new();
    public static readonly List<Shader> RegisteredImageShaders = new();
    public static readonly Dictionary<(Shader shader, Camera camera, int featureIdx, int layer), Material> NewMaterials = new();

    [SerializeField][Tooltip("What stage of the rendering pipeline blurs are drawn in. If set to After Rendering Transparents or above, then blurred Images should reference a Camera *below* their Canvas Camera to avoid accumulation and becoming blown out. UIBlurs are generally less sensitive to blow out.")]
    private RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    [SerializeField][Tooltip("What filtering mode destination (blur layer) textures should use. Use point filtering for a small performance gain, or for artistic effect (eg. paired with a low layer resolution for a pixelated look). Otherwise, stick with bilinear.")]
    private FilterMode destinationFilterMode = FilterMode.Bilinear;
    [SerializeField][Tooltip("When enabled, UIBlur layers stack so that higher layers blur the results of lower layers. Costs one blit per layer, after the first layer.")]
    private bool uiBlurLayersSeeLower = true;
    [SerializeField][Tooltip("When enabled, FlexibleImage will blur the results of UIBlurs. No significant performance difference, but occasionally requires an additional render texture.")]
    private bool blurredImagesSeeUIBlurs = true;
    [SerializeField][Tooltip("When enabled, FlexibleImage layers stack so that higher layers blur the results of lower layers. Costs one blit and requires an additional render texture per layer, after the first layer.")]
    private bool blurredImageLayersSeeLower = true;

    //[SerializeField]private bool testCase = false;
    //public static bool TestCase { get; private set; }

    [SerializeField] private bool useComputeShaders = true;
    [SerializeField] private bool overlayCompatibilityFix;
    [SerializeField] private int maxStabilizationPixels = 8;
    [SerializeField] private GraphicsFormat resultFormat = GraphicsFormat.R16G16B16A16_SFloat;
    [SerializeField] private GraphicsFormat blurFormat = GraphicsFormat.R16G16B16A16_SFloat;
    [FormerlySerializedAs("LayerResolutionRatio")] [SerializeField] private float layerResolutionRatio = 1f;
    [SerializeField] private int maxLayerResolution = 1080;

    private FlexibleBlurPass pass;

    public override void Create()
    {
        pass?.Dispose();
        GlobalFlexibleBlurPassDict.Clear();
        TryPreregisterShaders();
        pass = new(FindFeatureIdx(), renderPassEvent, (UnityEngine.FilterMode)destinationFilterMode, useComputeShaders, VerifyResultFormat(resultFormat), VerifyBlurFormat(blurFormat), uiBlurLayersSeeLower, blurredImagesSeeUIBlurs, blurredImageLayersSeeLower, overlayCompatibilityFix, maxStabilizationPixels, maxLayerResolution, layerResolutionRatio);

        int FindFeatureIdx()
        {
            var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!urpAsset)
                return 0;

#if UNITY_2023_1_OR_NEWER
            foreach (var rendererData in urpAsset.rendererDataList)
#else
            var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                return 0;

            if (field.GetValue(urpAsset) is not ScriptableRendererData[] rendererDataArray)
                return 0;

            foreach (var rendererData in rendererDataArray)
#endif
            {
                if (rendererData is not UniversalRendererData)
                    continue;

                int thisFeatureIdx = 0;
                foreach (var feature in rendererData.rendererFeatures)
                {
                    if (feature == this)
                        return thisFeatureIdx;
                    if (feature is FlexibleBlurFeature)
                        thisFeatureIdx++;
                }
            }
            return 0;
        }
    }

    public static GraphicsFormat VerifyResultFormat(GraphicsFormat format, bool silentAndDontUpdateDict = false)
    {
        if (ResultFormatFallbackDict.TryGetValue(format, out var existingValue))
            return existingValue;

#if UNITY_2023_2_OR_NEWER
        var filter = GraphicsFormatUsage.Render;
#else
        var filter = FormatUsage.Render;
#endif
        if (SystemInfo.IsFormatSupported(format, filter))
            return ResultFormatFallbackDict[format] = format;

        var fallbackFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SRGB, filter) 
            ? GraphicsFormat.R8G8B8A8_SRGB
            : GraphicsFormat.B8G8R8A8_UNorm;

        if (silentAndDontUpdateDict)
            return fallbackFormat;

        Debug.LogWarning($"Unsupported graphics format {format} for result format. Using fallback format {fallbackFormat}. This warning will display once.");
        return ResultFormatFallbackDict[format] = fallbackFormat;
    }

    public static GraphicsFormat VerifyBlurFormat(GraphicsFormat format, bool silentAndDontUpdateDict = false)
    {
        if (BlurFormatFallbackDict.TryGetValue(format, out var existingValue))  
            return existingValue;

#if UNITY_2023_2_OR_NEWER
        var filter = GraphicsFormatUsage.Render;
#else
        var filter = FormatUsage.Render;
#endif
        if (SystemInfo.IsFormatSupported(format, filter))
            return BlurFormatFallbackDict[format] = format;

        var fallbackFormat = QualitySettings.activeColorSpace == ColorSpace.Linear && SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, filter) 
            ? GraphicsFormat.B10G11R11_UFloatPack32
            : SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, filter)
                ? GraphicsFormat.R8G8B8A8_SRGB
                : GraphicsFormat.B8G8R8A8_UNorm;

        if (silentAndDontUpdateDict)
            return fallbackFormat;

        Debug.LogWarning($"Unsupported graphics format {format} for blur format. Using fallback format {fallbackFormat}. This warning will display once.");
        return BlurFormatFallbackDict[format] = fallbackFormat;
    }

    private void TryPreregisterShaders()
    {
        var blurredImageShader = Shader.Find("Hidden/JeffGrawAssets/BlurredImage");
        if (blurredImageShader != null && !RegisteredImageShaders.Contains(blurredImageShader))
            RegisteredImageShaders.Add(blurredImageShader);

        var flexibleImageShader = Shader.Find("Hidden/JeffGrawAssets/ProceduralBlurredImage");
        if (flexibleImageShader != null && !RegisteredImageShaders.Contains(flexibleImageShader))
            RegisteredImageShaders.Add(flexibleImageShader);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        pass.ConfigureInput(ScriptableRenderPassInput.Color);
#if !UNITY_2022_2_OR_NEWER
        pass.Setup(renderer, renderingData);
#endif
        renderer.EnqueuePass(pass);
    }

#if UNITY_2022_2_OR_NEWER && FLEXIBLE_UI_COMPATIBILITY
#if UNITY_6000_2_OR_NEWER
    [Obsolete]
#endif
    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData) =>
        pass.Setup(renderer, renderingData);
#endif

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
 
        foreach (var layerKvp in NewLayerHandles)
        {
            if (GlobalFlexibleBlurPassDict.TryGetValue((layerKvp.Key.camera, layerKvp.Key.featureIdx), out var blurPass))
                blurPass.ReleaseLayerHandle(layerKvp.Key.camera, layerKvp.Value);
        }

        foreach (var matKvp in NewMaterials)
            CoreUtils.Destroy(matKvp.Value);

        NewLayerHandles.Clear();
        NewMaterials.Clear();
        GlobalFlexibleBlurPassDict.Clear();

        pass?.Dispose();
    }
}

public partial class FlexibleBlurPass : ScriptableRenderPass
{
    private const string ProfilerTag = nameof(FlexibleBlurPass);
    private const string ImageShaderBlurKeyword = "HAS_BLUR";
    private const string BlursShader = "Hidden/JeffGrawAssets/Blurs";
    private const string FullScreenBlitsShader = "Hidden/JeffGrawAssets/FullScreenBlits";
    private const string RegionalBlitsShader = "Hidden/JeffGrawAssets/RegionalBlits";
    private const string QuadBlitsShader = "Hidden/JeffGrawAssets/QuadBlits";

    private const int ThreadGroupSizeX = 8;
    private const int ThreadGroupSizeY = 8;
    public static event Action<(Camera camera, int featureIdx), float> ComputeBlurEvent;

    private static Matrix4x4 OverlayUIProjectionMatrix = Matrix4x4.identity;
    private static int currentBlurPassIdx;
    private static int UIBlurIntermediateID = Shader.PropertyToID("FlexibleBlurUIBlurIntermediate");
    private static int Temp1Id = Shader.PropertyToID("FlexibleBlurIntermediateRT_0");
    private static int Temp2Id = Shader.PropertyToID("FlexibleBlurIntermediateRT_1");

    // fragment PropertyIDs
    private static readonly int BlurSampleDistID = Shader.PropertyToID("_BlurSampleDistance");
    private static readonly int SampleOffsetID = Shader.PropertyToID("_SampleOffset");
    private static readonly int TapsPerSideHorID = Shader.PropertyToID("_TapsPerSideHor");
    private static readonly int TapsPerSideVertID = Shader.PropertyToID("_TapsPerSideVert");
    private static readonly int BlurIterationID = Shader.PropertyToID("_BlurIteration");
    private static readonly int OffsetCenterID = Shader.PropertyToID("_OffsetCenter");
    private static readonly int SourceOffsetID = Shader.PropertyToID("_SourceOffset");
    private static readonly int SourceOffsetRightID = Shader.PropertyToID("_SourceOffsetRight");
    private static readonly int ScaleFactorID = Shader.PropertyToID("_ScaleFactor");
    private static readonly int ScaleFactorRightID = Shader.PropertyToID("_ScaleFactorRight");
    private static readonly int TintID = Shader.PropertyToID("_Tint");
    private static readonly int VibrancyID = Shader.PropertyToID("_Vibrancy");
    private static readonly int BrightnessID = Shader.PropertyToID("_Brightness");
    private static readonly int ContrastID = Shader.PropertyToID("_Contrast");
    private static readonly int DitherStrengthID = Shader.PropertyToID("_DitherStrength");
    private static readonly int DestinationRegionSizeID = Shader.PropertyToID("_DestinationRegionSize");
    private static readonly int DestinationRegionSizeRightID = Shader.PropertyToID("_DestinationRegionSizeRight");
    private static readonly int CornersID = Shader.PropertyToID("_Corners");
    private static readonly int CornersRightID = Shader.PropertyToID("_CornersRight");
    private static readonly int MainTexID = Shader.PropertyToID("_MainTex");
    private static readonly int DestinationTexID = Shader.PropertyToID("_DestTex");
    private static readonly int RenderScaleID = Shader.PropertyToID("_RenderScale");
    private static readonly int BlurRegionID = Shader.PropertyToID("_BlurRegion");
    private static readonly int BlurRegionRightID = Shader.PropertyToID("_BlurRegionRight");
    // compute PropertyIDs
    private static readonly int ComputeSourceID = Shader.PropertyToID("Source");
    private static readonly int ComputeResultID = Shader.PropertyToID("Result");
    private static readonly int ComputeResultDimensionsID = Shader.PropertyToID("ResultDimensions");
    private static readonly int ComputeSampleDistID = Shader.PropertyToID("SampleDist");
    private static readonly int TapsPerSideHorComputeID = Shader.PropertyToID("TapsPerSideHor");
    private static readonly int TapsPerSideVertComputeID = Shader.PropertyToID("TapsPerSideVert");
    private static readonly int ComputeSampleOffsetID = Shader.PropertyToID("SampleOffset");
    private static readonly int ComputeOffsetCenterID = Shader.PropertyToID("OffsetCenter");
    private static readonly int ComputeBlurIterationID = Shader.PropertyToID("BlurIteration");

    private static readonly Dictionary<(Camera camera, int featureIdx), List<RTHandle>> globalRTHandleDict = new();
    private static readonly Dictionary<Shader, Dictionary<(Camera camera, int featureIdx), List<Material>>> globalImageMatDict = new();

    private static ComputeShader computeBlurs, vrComputeBlurs;
    private static Material blursMat, fullScreenBlitsMat, regionalBlitsMat, quadBlitsMat;

    private readonly FilterMode destinationFilterMode;
    private readonly GraphicsFormat blurGraphicsFormat, resultGraphicsFormat;
    private readonly Dictionary<Camera, RTHandleSystem> instanceHandleSystemDict = new();
    private readonly Dictionary<(Camera camera, int featureIdx), List<RTHandle>> instanceLayerRTHandleDict = new();
    private readonly Dictionary<Shader, Dictionary<(Camera camera, int featureIdx), List<Material>>> instanceImageMatDict = new();
    private readonly PooledListDictionary<(BlurSettings blurSettings, float alpha), List<IBlur>, IBlur> batchedBlurs = new ();
    private readonly float layerResolutionRatio;
    private readonly int featureIdx, maxLayerResolution, maxStabilizationPixels;
    private readonly bool enabled, uiBlurLayersSeeLower, blurredImageSeeUIBlurs, blurredImageLayersSeeLower, useComputeShaders, overlayCompatibilityFix;

    public bool IndividuallyPaused { get; set; }

    private List<RTHandle> currentRTHandleList;
    private readonly List<int> currentLayerMipLevels = new();
    private Camera currentCamera;
    private Vector2 layerTextureScaleFactor;
#if XR_MANAGEMENT_INSTALLED
    private XRSettings.StereoRenderingMode prevStereoRenderingMode;
#endif
    private int prevFrameCount, numLayerTextures;
    private static bool blurLayerAdddedThisFrame;

    public static bool TryGetImageMaterial(Camera camera, int featureIndex, int layer, Shader shader, out Material mat)
    {
        if (!globalImageMatDict.TryGetValue(shader, out var innerGlobalImageMatDict))
        {
            innerGlobalImageMatDict = globalImageMatDict[shader] = new Dictionary<(Camera, int), List<Material>>();
            if (!FlexibleBlurFeature.RegisteredImageShaders.Contains(shader))
                FlexibleBlurFeature.RegisteredImageShaders.Add(shader);
        }

        if (innerGlobalImageMatDict.TryGetValue((camera, featureIndex), out var matList))
        {
            if (layer < matList.Count)
            {
                mat = matList[layer];
                return true;
            }
        }

        var key = (shader, camera, featureIndex, layer);
        if (FlexibleBlurFeature.NewMaterials.TryGetValue(key, out mat))
            return true;

        if (!FlexibleBlurFeature.GlobalFlexibleBlurPassDict.TryGetValue((camera, featureIndex), out _))
            return false;

        mat = CoreUtils.CreateEngineMaterial(shader);
        mat.EnableKeyword(ImageShaderBlurKeyword);
        FlexibleBlurFeature.NewMaterials[key] = mat;
        return true;
    }

    public static bool TryGetImageRT(Camera camera, int featureIndex, int layer, out RTHandle handle) =>
        TryGetImageRT(camera, featureIndex, layer, 0, out handle);

    public static bool TryGetImageRT(Camera camera, int featureIndex, int layer, int requestedMipLevels, out RTHandle handle)
    {
        if (globalRTHandleDict.TryGetValue((camera, featureIndex), out var rtList))
        {
            if (layer < rtList.Count)
            {
                handle = rtList[layer];
                return true;
            }
        }

        var key = (camera, featureIndex, layer);
        if (FlexibleBlurFeature.NewLayerHandles.TryGetValue(key, out handle))
        {
            if (requestedMipLevels <= 0 || handle?.rt && handle.rt.useMipMap)
                return true;
            if (!FlexibleBlurFeature.GlobalFlexibleBlurPassDict.TryGetValue((camera, featureIndex), out var allocatingPass))
                return true;
            allocatingPass.ReleaseLayerHandle(camera, handle);
            FlexibleBlurFeature.NewLayerHandles.Remove(key);
        }

        if (!FlexibleBlurFeature.GlobalFlexibleBlurPassDict.TryGetValue((camera, featureIndex), out var pass))
            return false;

        var layerScale = Mathf.Min(pass.layerResolutionRatio, (float)pass.maxLayerResolution / camera.pixelHeight);
        var layerScaleVec2 = new Vector2(layerScale, layerScale);

#if XR_MANAGEMENT_INSTALLED

        (VRTextureUsage vrUsage, TextureDimension dimension, int slices) = XRSettings.stereoRenderingMode >= XRSettings.StereoRenderingMode.SinglePassInstanced ? (VRTextureUsage.TwoEyes, TextureDimension.Tex2DArray, 2) : (VRTextureUsage.None, TextureDimension.Tex2D, 1);
#else
        (VRTextureUsage vrUsage, TextureDimension dimension, int slices) = (VRTextureUsage.None, TextureDimension.Tex2D, 1);
#endif
#if UNITY_2022_2_OR_NEWER
        handle = pass.AllocLayerHandle(camera, layerScaleVec2, pass.resultGraphicsFormat, pass.destinationFilterMode, vrUsage, dimension, slices, requestedMipLevels);
#else
        handle = pass.AllocLayerHandle(camera, layerScaleVec2, pass.resultGraphicsFormat, pass.destinationFilterMode, dimension, slices, requestedMipLevels);
#endif
        FlexibleBlurFeature.NewLayerHandles[key] = handle;
        blurLayerAdddedThisFrame = true;
        return true;
    }

    public FlexibleBlurPass(int featureIdx, RenderPassEvent renderPassEvent, FilterMode destinationFilterMode, bool useComputeShaders, GraphicsFormat resultGraphicsFormat, GraphicsFormat blurGraphicsFormat, bool uiBlurLayersSeeLower, bool blurredImageSeeUIBlurs, bool blurredImageLayersSeeLower, bool overlayCompatibilityFix, int maxStabilizationPixels, int maxLayerResolution, float layerResolutionRatio)
    {
        (this.featureIdx, this.renderPassEvent, this.destinationFilterMode, this.useComputeShaders, this.resultGraphicsFormat, this.blurGraphicsFormat, this.uiBlurLayersSeeLower, this.blurredImageSeeUIBlurs, this.blurredImageLayersSeeLower, this.overlayCompatibilityFix, this.maxStabilizationPixels, this.maxLayerResolution, this.layerResolutionRatio) =
        (featureIdx,           renderPassEvent,      destinationFilterMode,      useComputeShaders,      resultGraphicsFormat,      blurGraphicsFormat,      uiBlurLayersSeeLower,      blurredImageSeeUIBlurs,      blurredImageLayersSeeLower,      overlayCompatibilityFix,      maxStabilizationPixels,      maxLayerResolution,      layerResolutionRatio);

        if (useComputeShaders)
        {
            if (computeBlurs == null)
            {
                computeBlurs = (ComputeShader)Resources.Load("ComputeBlurs");
                vrComputeBlurs = (ComputeShader)Resources.Load("VRComputeBlurs");
            }
        }
        else if (blursMat == null)
        {
            blursMat = CoreUtils.CreateEngineMaterial(Shader.Find(BlursShader));
        }

        if (fullScreenBlitsMat == null)
        {
            fullScreenBlitsMat = CoreUtils.CreateEngineMaterial(Shader.Find(FullScreenBlitsShader));
            regionalBlitsMat = CoreUtils.CreateEngineMaterial(Shader.Find(RegionalBlitsShader));
            quadBlitsMat = CoreUtils.CreateEngineMaterial(Shader.Find(QuadBlitsShader));
        }

#if XR_MANAGEMENT_INSTALLED
        if (XRSettings.enabled)
            prevStereoRenderingMode = XRSettings.stereoRenderingMode;
#endif
    }

    public void Dispose()
    {
        foreach (var shader in FlexibleBlurFeature.RegisteredImageShaders)
        {
            if (!instanceImageMatDict.TryGetValue(shader, out var innerInstanceImageMatDict))
                continue;

            foreach (var kvp in innerInstanceImageMatDict)
            {
                var (camera, instanceMatList) = (kvp.Key, kvp.Value);
                if (globalImageMatDict.TryGetValue(shader, out var innerGlobalImageMatDict) && innerGlobalImageMatDict.TryGetValue(camera, out var globalMatList) && globalMatList == instanceMatList)
                    innerGlobalImageMatDict.Remove(camera);

                instanceMatList.ForEach(CoreUtils.Destroy);
            }
        }

        foreach (var kvp in instanceLayerRTHandleDict)
        {
            var (camera, instanceRtList) = (kvp.Key, kvp.Value);
            if (globalRTHandleDict.TryGetValue(camera, out var globalRtList) && globalRtList == instanceRtList)
                globalRTHandleDict.Remove(camera);
        }

        foreach (var system in instanceHandleSystemDict.Values)
            system.Dispose();

        instanceHandleSystemDict.Clear();
    }

#if UNITY_2022_2_OR_NEWER
    public RTHandle AllocLayerHandle(Camera camera, Vector2 scaleFactor, GraphicsFormat colorFormat, FilterMode filterMode, VRTextureUsage vrUsage, TextureDimension dimension, int slices, int mipLevels = 0)
        => AllocLayerHandle(GetHandleSystem(camera), scaleFactor, colorFormat, filterMode, vrUsage, dimension, slices, mipLevels);

    private static RTHandle AllocLayerHandle(RTHandleSystem handleSystem, Vector2 scaleFactor, GraphicsFormat colorFormat, FilterMode filterMode, VRTextureUsage vrUsage, TextureDimension dimension, int slices, int mipLevels = 0)
    {
        var useMipMap = mipLevels > 0;
        return handleSystem.Alloc(scaleFactor, wrapMode: TextureWrapMode.Clamp, colorFormat: colorFormat, memoryless: RenderTextureMemoryless.Depth, filterMode: useMipMap ? FilterMode.Trilinear : filterMode, useMipMap: useMipMap, autoGenerateMips: false, msaaSamples: MSAASamples.None, vrUsage: vrUsage, dimension: dimension, slices: slices);
    }
#else
    public RTHandle AllocLayerHandle(Camera camera, Vector2 scaleFactor, GraphicsFormat colorFormat, FilterMode filterMode, TextureDimension dimension, int slices, int mipLevels = 0)
        => AllocLayerHandle(GetHandleSystem(camera), scaleFactor, colorFormat, filterMode, dimension, slices, mipLevels);

    private static RTHandle AllocLayerHandle(RTHandleSystem handleSystem, Vector2 scaleFactor, GraphicsFormat colorFormat, FilterMode filterMode, TextureDimension dimension, int slices, int mipLevels = 0)
    {
        var useMipMap = mipLevels > 0;
        return handleSystem.Alloc(scaleFactor, wrapMode: TextureWrapMode.Clamp, colorFormat: colorFormat, memoryless: RenderTextureMemoryless.Depth, filterMode: useMipMap ? FilterMode.Trilinear : filterMode, useMipMap: useMipMap, autoGenerateMips: false, msaaSamples: MSAASamples.None, dimension: dimension, slices: slices);
    }
#endif

    public void ReleaseLayerHandle(Camera camera, RTHandle handle)
    {
        if (handle == null)
            return;

        if (instanceHandleSystemDict.TryGetValue(camera, out var handleSystem))
            handleSystem.Release(handle);
        else
            handle.Release();
    }

    private RTHandleSystem GetHandleSystem(Camera camera)
    {
#if XR_MANAGEMENT_INSTALLED
        if (XRSettings.enabled)
            return GetHandleSystem(camera, XRSettings.eyeTextureWidth, XRSettings.eyeTextureHeight);
#endif
        return GetHandleSystem(camera, camera ? camera.pixelWidth : Screen.width, camera ? camera.pixelHeight : Screen.height);
    }

    private RTHandleSystem GetHandleSystem(Camera camera, int width, int height)
    {
        if (!instanceHandleSystemDict.TryGetValue(camera, out var handleSystem))
        {
            handleSystem = new RTHandleSystem();
            handleSystem.Initialize(width, height);
            instanceHandleSystemDict[camera] = handleSystem;
            return handleSystem;
        }

        if (handleSystem.GetMaxWidth() != width || handleSystem.GetMaxHeight() != height)
            handleSystem.ResetReferenceSize(width, height);

        return handleSystem;
    }

#if FLEXIBLE_UI_COMPATIBILITY
    public void Setup(ScriptableRenderer _, in RenderingData renderingData)
    {
        var camData = renderingData.cameraData;
        Setup(camData.camera, camData.cameraTargetDescriptor, camData.renderScale);
    }
#endif

    private void Setup(Camera camera, RenderTextureDescriptor descriptor, float renderScale)
    {
        currentCamera = camera;
        var key = (currentCamera, featureIdx);

        renderScale = Mathf.Min(renderScale, 1f);
        var layerScale = Mathf.Min(layerResolutionRatio, (float)maxLayerResolution / camera.pixelHeight);
        layerTextureScaleFactor = new Vector2(layerScale, layerScale);

        if (!overlayCompatibilityFix)
            OverlayUIProjectionMatrix = Matrix4x4.Ortho(0, camera.pixelWidth, 0, camera.pixelHeight, -1000f, 1000f);

        var filteringPadding = 1.5f / (renderScale * renderScale) / layerScale;
        ComputeBlurEvent?.Invoke((camera, featureIdx), filteringPadding);

        var handleSystem = GetHandleSystem(camera, descriptor.width, descriptor.height);

        int texturesNeededForUiBlurs = 0;
        UIBlur.LayersPerCameraDict.TryGetValue(key, out var uiBlurLayers);
        if (uiBlurLayers > 0)
            texturesNeededForUiBlurs++;

#if XR_MANAGEMENT_INSTALLED
        var stereoRenderingMode = XRSettings.stereoRenderingMode;
        if (prevStereoRenderingMode != stereoRenderingMode)
        {
            prevStereoRenderingMode = stereoRenderingMode;
            foreach (var kvp in instanceLayerRTHandleDict)
            {
                var (instanceCamera, instanceRtList) = (kvp.Key, kvp.Value);
                instanceRtList.ForEach(rtHandle => ReleaseLayerHandle(instanceCamera.camera, rtHandle));
                instanceRtList.Clear();

                if (globalRTHandleDict.TryGetValue(instanceCamera, out var globalRtList) && globalRtList == instanceRtList)
                    globalRTHandleDict.Remove(instanceCamera);
            }
        }
        (VRTextureUsage vrUsage, TextureDimension dimension, int slices) = stereoRenderingMode >= XRSettings.StereoRenderingMode.SinglePassInstanced ? (VRTextureUsage.TwoEyes, TextureDimension.Tex2DArray, 2) : (VRTextureUsage.None, TextureDimension.Tex2D, 1);
#else
        (VRTextureUsage vrUsage, TextureDimension dimension, int slices) = (VRTextureUsage.None, TextureDimension.Tex2D, 1);
#endif

        if (FlexibleBlurFeature.ImageBasedBlurDict.TryGetValue(key, out var blurredImageAreas) && blurredImageAreas is { Count: > 0 })
        {
            FlexibleBlurFeature.ImageBasedLayersPerCameraDict.TryGetValue(key, out var texturesNeededForImageBlurs);

            numLayerTextures = Math.Max(texturesNeededForUiBlurs, texturesNeededForImageBlurs);

            currentLayerMipLevels.Clear();
            for (int i = 0; i < numLayerTextures; i++)
                currentLayerMipLevels.Add(0);
            foreach (var imageBlur in blurredImageAreas)
                currentLayerMipLevels[imageBlur.Layer] = Math.Max(currentLayerMipLevels[imageBlur.Layer], Math.Max(0, imageBlur.RequestedMipLevels));

            if (!instanceLayerRTHandleDict.TryGetValue(key, out currentRTHandleList))
                currentRTHandleList = instanceLayerRTHandleDict[key] = new List<RTHandle>();

            while (currentRTHandleList.Count < numLayerTextures)
            {
                blurLayerAdddedThisFrame = true;
                var newHandleKey = (camera, featureIdx, currentRTHandleList.Count);
                if (FlexibleBlurFeature.NewLayerHandles.TryGetValue(newHandleKey, out var newHandle))
                {
                    currentRTHandleList.Add(newHandle);
                    FlexibleBlurFeature.NewLayerHandles.Remove(newHandleKey);
                }
                else
                {
#if UNITY_2022_2_OR_NEWER
                    currentRTHandleList.Add(AllocLayerHandle(handleSystem, layerTextureScaleFactor, resultGraphicsFormat, destinationFilterMode, vrUsage, dimension, slices, currentLayerMipLevels[currentRTHandleList.Count]));
#else
                    currentRTHandleList.Add(AllocLayerHandle(handleSystem, layerTextureScaleFactor, resultGraphicsFormat, destinationFilterMode, dimension, slices, currentLayerMipLevels[currentRTHandleList.Count]));
#endif
                }
            }

            for (int i = 0; i < numLayerTextures; i++)
            {
                var handle = currentRTHandleList[i];
                if (handle?.rt && handle.rt.useMipMap == (currentLayerMipLevels[i] > 0))
                    continue;
                ReleaseLayerHandle(camera, handle);
#if UNITY_2022_2_OR_NEWER
                currentRTHandleList[i] = AllocLayerHandle(handleSystem, layerTextureScaleFactor, resultGraphicsFormat, destinationFilterMode, vrUsage, dimension, slices, currentLayerMipLevels[i]);
#else
                currentRTHandleList[i] = AllocLayerHandle(handleSystem, layerTextureScaleFactor, resultGraphicsFormat, destinationFilterMode, dimension, slices, currentLayerMipLevels[i]);
#endif
            }

            globalRTHandleDict[key] = currentRTHandleList;

            foreach (var shader in FlexibleBlurFeature.RegisteredImageShaders)
            {
                if (!instanceImageMatDict.TryGetValue(shader, out var innerImageMatDict))
                    innerImageMatDict = instanceImageMatDict[shader] = new Dictionary<(Camera, int), List<Material>>();

                if (!innerImageMatDict.TryGetValue(key, out var matList))
                    matList = innerImageMatDict[key] = new List<Material>();

                while (matList.Count < texturesNeededForImageBlurs)
                {
                    var newMaterialKey = (shader, camera, featureIdx, matList.Count);
                    if (FlexibleBlurFeature.NewMaterials.TryGetValue(newMaterialKey, out var newMaterial))
                    {
                        matList.Add(newMaterial);
                        FlexibleBlurFeature.NewMaterials.Remove(newMaterialKey);
                    }
                    else
                    {
                        var newMat = CoreUtils.CreateEngineMaterial(shader);
                        newMat.EnableKeyword(ImageShaderBlurKeyword);
                        matList.Add(newMat);
                    }
                }

                if (!globalImageMatDict.TryGetValue(shader, out var innerGlobalImageMatDict))
                    innerGlobalImageMatDict = globalImageMatDict[shader] = new Dictionary<(Camera, int), List<Material>>();

                innerGlobalImageMatDict[key] = matList;
            }
        }
        else if (texturesNeededForUiBlurs == 1)
        {
            numLayerTextures = 1;
            currentLayerMipLevels.Clear();
            currentLayerMipLevels.Add(0);
            if (!instanceLayerRTHandleDict.TryGetValue(key, out currentRTHandleList))
                currentRTHandleList = instanceLayerRTHandleDict[key] = new List<RTHandle>(1);

            if (currentRTHandleList.Count == 0)
            {
#if UNITY_2022_2_OR_NEWER
                currentRTHandleList.Add(AllocLayerHandle(handleSystem, layerTextureScaleFactor, resultGraphicsFormat, destinationFilterMode, vrUsage, dimension, slices));
#else
                currentRTHandleList.Add(AllocLayerHandle(handleSystem, layerTextureScaleFactor, resultGraphicsFormat, destinationFilterMode, dimension, slices));
#endif
            }
            else if (currentRTHandleList[0]?.rt && currentRTHandleList[0].rt.useMipMap)
            {
                ReleaseLayerHandle(camera, currentRTHandleList[0]);
#if UNITY_2022_2_OR_NEWER
                currentRTHandleList[0] = AllocLayerHandle(handleSystem, layerTextureScaleFactor, resultGraphicsFormat, destinationFilterMode, vrUsage, dimension, slices);
#else
                currentRTHandleList[0] = AllocLayerHandle(handleSystem, layerTextureScaleFactor, resultGraphicsFormat, destinationFilterMode, dimension, slices);
#endif
            }

            globalRTHandleDict[key] = currentRTHandleList;
        }

        FlexibleBlurFeature.GlobalFlexibleBlurPassDict[(camera, featureIdx)] = this;
    }

    public override void FrameCleanup(CommandBuffer cmd)
    {
        for (int i = 0; i < FlexibleBlurFeature.NewLayerHandles.Count; i++)
        {
            var element = FlexibleBlurFeature.NewLayerHandles.ElementAt(i);
            if (element.Key.camera != currentCamera || element.Key.featureIdx != featureIdx)
                continue;

            ReleaseLayerHandle(element.Key.camera, element.Value);
            FlexibleBlurFeature.NewLayerHandles.Remove(element.Key);
            i--;
        }

        for (int i = 0; i < FlexibleBlurFeature.NewMaterials.Count; i++)
        {
            var element = FlexibleBlurFeature.NewMaterials.ElementAt(i);
            if (element.Key.camera != currentCamera || element.Key.featureIdx != featureIdx)
                continue;

            CoreUtils.Destroy(element.Value);
            FlexibleBlurFeature.NewMaterials.Remove(element.Key);
            i--;
        }

        // At the end of every frame, check for cameras that have been destroyed and free any resources they may have used.
        var frameCount = Time.frameCount;
        if (frameCount != prevFrameCount)
        {
            // Somewhat roundabout approach to removing null keys, but has the benefit of 0 allocations.
            int idx = 0;
            while (idx < instanceLayerRTHandleDict.Count)
            {
                foreach (var key in instanceLayerRTHandleDict.Keys)
                {
                    if (key.camera)
                    {
                        idx++;
                        continue;
                    }

                    foreach (var rtHandle in instanceLayerRTHandleDict[key])
                        ReleaseLayerHandle(key.camera, rtHandle);

                    instanceLayerRTHandleDict.Remove(key);
                    globalRTHandleDict.Remove(key);
                    if (instanceHandleSystemDict.Remove(key.camera, out var handleSystem))
                        handleSystem.Dispose();
                    idx = 0;
                    break;
                }
            }

            foreach (var shader in FlexibleBlurFeature.RegisteredImageShaders)
            {
                if (!instanceImageMatDict.TryGetValue(shader, out var innerImageMatDict) || !globalImageMatDict.TryGetValue(shader, out var innerGlobalImageMatDict))
                    continue;

                idx = 0;
                while (idx < innerImageMatDict.Count)
                {
                    foreach (var key in innerImageMatDict.Keys)
                    {
                        if (key.camera)
                        {
                            idx++;
                            continue;
                        }
            
                        foreach (var material in innerImageMatDict[key])
                            CoreUtils.Destroy(material);
            
                        innerImageMatDict.Remove(key);
                        innerGlobalImageMatDict.Remove(key);
                        idx = 0;
                        break;
                    }
                }
            }
        }

        prevFrameCount = frameCount;

        while (currentRTHandleList?.Count > numLayerTextures)
        {
            var handle = currentRTHandleList[^1];
            currentRTHandleList.RemoveAt(currentRTHandleList.Count - 1);
            ReleaseLayerHandle(currentCamera, handle);
        }
    }

#if FLEXIBLE_UI_COMPATIBILITY
#if UNITY_2023_3_OR_NEWER
    [Obsolete]
#endif
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (!blurLayerAdddedThisFrame && (IndividuallyPaused || FlexibleBlurFeature.GloballyPaused))
            return;

        blurLayerAdddedThisFrame = false;
        var camera = renderingData.cameraData.camera;
        var key = (camera, featureIdx);

        UIBlur.BlurDict.TryGetValue(key, out var blurAreas);
        FlexibleBlurFeature.ImageBasedBlurDict.TryGetValue(key, out var blurredImageAreas);
        TryGetTextureRequests(camera, featureIdx, out var textureRequests);

        var haveUIBlurAreas = blurAreas is { Count: > 0 };
        var haveBlurredImageAreas = blurredImageAreas is { Count: > 0 };
        var haveTextureRequests = textureRequests is { Count: > 0 };
        if (!haveUIBlurAreas && !haveBlurredImageAreas && !haveTextureRequests)
            return;

#if XR_MANAGEMENT_INSTALLED
        var rightEye = XRSettings.enabled && renderingData.cameraData.xr.multipassId == 1;
        var singlePassVR = XRSettings.enabled && XRSettings.stereoRenderingMode >= XRSettings.StereoRenderingMode.SinglePassInstanced;
        var multiPassVR = XRSettings.enabled && XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.MultiPass;
#else
        var (rightEye, singlePassVR, multiPassVR) = (false, false, false);
#endif

        var renderScale = renderingData.cameraData.renderScale;
        var cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
        var originalHeight = cameraTargetDescriptor.height;
        var originalWidth = cameraTargetDescriptor.width;
#if UNITY_2022_2_OR_NEWER
        var cameraRT = renderingData.cameraData.renderer.cameraColorTargetHandle;
#else
        var cameraRT = renderingData.cameraData.renderer.cameraColorTarget;
#endif
        cameraTargetDescriptor.graphicsFormat = blurGraphicsFormat;
        cameraTargetDescriptor.enableRandomWrite = useComputeShaders;
        cameraTargetDescriptor.useMipMap = false;
        cameraTargetDescriptor.msaaSamples = 1;
        cameraTargetDescriptor.depthBufferBits = 0;
        cameraTargetDescriptor.depthStencilFormat = GraphicsFormat.None;
        var destinationRequiresClear = haveUIBlurAreas && (!haveBlurredImageAreas || blurredImageSeeUIBlurs);

        var cmd = CommandBufferPool.Get(ProfilerTag);
        if (blurredImageSeeUIBlurs)
        {
            HandleUIBlurs();
            HandleBlurredImages();
        }
        else
        {
            HandleBlurredImages();
            HandleUIBlurs(haveBlurredImageAreas);
        }
        HandleTextureRequests();

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);

        void HandleUIBlurs(bool useTempRT = false)
        {
            if (!haveUIBlurAreas)
                return;

            RenderTargetIdentifier layerRT;
            if (useTempRT)
            {
                destinationRequiresClear = !blurredImageSeeUIBlurs;
                cameraTargetDescriptor.height = originalHeight;
                cameraTargetDescriptor.width = originalWidth;
                cmd.GetTemporaryRT(UIBlurIntermediateID, cameraTargetDescriptor, FilterMode.Bilinear);
                layerRT = UIBlurIntermediateID;
            }
            else
            {
                layerRT = currentRTHandleList[0];
            }

            if (uiBlurLayersSeeLower)
            {
                int currentLayer = blurAreas[0].Layer;
                foreach (var blur in blurAreas)
                {
                    if (blur.Layer > currentLayer)
                    {
                        currentLayer = blur.Layer;
                        FullScreenBlit(cmd, layerRT, cameraRT, fullScreenBlitsMat, 1);
                    }

                    ApplyBlur(blur, cameraRT, layerRT, 0);
                }
            }
            else
            {
                foreach (var blurArea in blurAreas)
                    ApplyBlur(blurArea, cameraRT, layerRT, 0);
            }

            if (!destinationRequiresClear)
            {
                FullScreenBlit(cmd, layerRT, cameraRT, fullScreenBlitsMat, 1);
            }

            if (useTempRT)
                cmd.ReleaseTemporaryRT(UIBlurIntermediateID);
        }

        void HandleBlurredImages()
        {
            if (!haveBlurredImageAreas)
                return;

            int numImageLayers = FlexibleBlurFeature.ImageBasedLayersPerCameraDict[key];
            var source = cameraRT;
            int destinationIdx = 0;
            var destination = currentRTHandleList[destinationIdx];
            if (blurredImageLayersSeeLower && destinationIdx < numImageLayers - 1)
            {
                cmd.SetRenderTarget(new RenderTargetIdentifier(destination, 0, CubemapFace.Unknown, -1));
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.SetRenderTarget(source);
                FullScreenBlit(cmd, source, destination, fullScreenBlitsMat);
            }

            int currentLayer = blurredImageAreas[0].Layer;
            int currentPriority = blurredImageAreas[0].Priority;
            foreach (var blurImage in blurredImageAreas)
            {
                if (blurImage.Layer > currentLayer || blurImage.Priority > currentPriority)
                {
                    TryApplyBatchedBlurs();

                    if (blurImage.Layer > currentLayer)
                    {
                        if (destinationIdx < currentLayerMipLevels.Count && currentLayerMipLevels[destinationIdx] > 0)
                            cmd.GenerateMips(destination);
                        destination = currentRTHandleList[++destinationIdx];
                        if (blurredImageLayersSeeLower)
                        {
                            source = currentRTHandleList[destinationIdx - 1];
                            cmd.SetRenderTarget(source);

                            if (destinationIdx < numImageLayers - 1)
                            {
                                FullScreenBlit(cmd, source, destination, fullScreenBlitsMat);
                            }
                        }
                    }
                    currentLayer = blurImage.Layer;
                    currentPriority = blurImage.Priority;
                }

                if (blurImage.CanBatch)
                    batchedBlurs.Add((blurImage.Settings, blurImage.Alpha), blurImage);
                else
                    ApplyBlur(blurImage, source, destination, maxStabilizationPixels);
            }

            TryApplyBatchedBlurs();

            if (destinationIdx < currentLayerMipLevels.Count && currentLayerMipLevels[destinationIdx] > 0)
                cmd.GenerateMips(destination);

            if (blurredImageLayersSeeLower)
                cmd.SetRenderTarget(cameraRT);

            void TryApplyBatchedBlurs()
            {
                foreach (var kvp in batchedBlurs)
                {
                    bool fillEntireRenderTexture = false;
                    foreach (var blur in kvp.Value)
                    {
                        if (!blur.FillEntireRenderTexture)
                            continue;

                        fillEntireRenderTexture = true;
                        break;
                    }

                    if (!fillEntireRenderTexture && kvp.Value.Count == 1)
                    {
                        ApplyBlur(kvp.Value[0], source, destination, maxStabilizationPixels);
                        continue;
                    }

                    float minX, minY, maxX, maxY;
                    if (fillEntireRenderTexture)
                    {
                        (minX, minY, maxX, maxY) = (0, 0, originalWidth, originalHeight);
                    }
                    else
                    {
                        (minX, minY, maxX, maxY) = (float.PositiveInfinity, float.PositiveInfinity, float.NegativeInfinity, float.NegativeInfinity);
                        foreach (var batchedBlur in kvp.Value)
                        {
                            minX = Math.Min(minX, batchedBlur.MinX(rightEye));
                            minY = Math.Min(minY, batchedBlur.MinY(rightEye));
                            maxX = Math.Max(maxX, batchedBlur.MaxX(rightEye));
                            maxY = Math.Max(maxY, batchedBlur.MaxY(rightEye));
                        }
                    }

                    if (destinationRequiresClear)
                    {
                        cmd.SetRenderTarget(new RenderTargetIdentifier(destination, 0, CubemapFace.Unknown, -1));
                        cmd.ClearRenderTarget(false, true, Color.clear);
                        cmd.SetRenderTarget(source);
                        destinationRequiresClear = false;
                    }

                    var settings = kvp.Key.blurSettings;
                    if (singlePassVR)
                    {
                        // Left eye extents already calculated. Min/Max that with the right eye values to get a region that covers both eyes.
                        foreach (var batchedBlur in kvp.Value)
                        {
                            minX = Math.Min(minX, batchedBlur.MinX(true));
                            minY = Math.Min(minY, batchedBlur.MinY(true));
                            maxX = Math.Max(maxX, batchedBlur.MaxX(true));
                            maxY = Math.Max(maxY, batchedBlur.MaxY(true));
                        }
                    }

                    var blurRegion = UIBlurCommon.ComputeBlurRegion(minX, minY, maxX, maxY);
                    ApplyBlurUnified(source, destination, settings, blurRegion, singlePassVR ? blurRegion : null, null, null, kvp.Key.alpha, maxStabilizationPixels, false, Matrix4x4.identity);
                }

                batchedBlurs.Clear();
            }
        }

        void HandleTextureRequests()
        {
            if (!haveTextureRequests)
                return;

            foreach (var request in textureRequests)
            {
                var destination = new RenderTargetIdentifier(request.Destination, 0, CubemapFace.Unknown, -1);
                var source = request.Source != null ? (RenderTargetIdentifier)request.Source : cameraRT;
                cmd.SetRenderTarget(destination);
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.SetRenderTarget(source);
                var bounds = request.Bounds;
                var region = new Vector4(bounds.x, bounds.y, bounds.width, bounds.height);
                ApplyBlurUnified(source, destination, request.Settings, region, singlePassVR ? region : null, null, null, request.Strength, maxStabilizationPixels, false, Matrix4x4.identity);
                if (request.MipLevels > 0)
                    cmd.GenerateMips(destination);
            }
        }

        void ApplyBlur(IBlur iBlur, RenderTargetIdentifier source, RenderTargetIdentifier destination, int maxStabilizationPixels)
        {
            if (!iBlur.HasVisiblePixels(rightEye))
                return;

            if (destinationRequiresClear)
            {
                cmd.SetRenderTarget(new RenderTargetIdentifier(destination, 0, CubemapFace.Unknown, -1));
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.SetRenderTarget(source);
                destinationRequiresClear = false;
            }

            if (singlePassVR)
            {
                var (regionRight, cornersRight, region, corners) = (iBlur.Common.BlurRegionRight, iBlur.Common.ScreenCornersRight, iBlur.Common.BlurRegion, iBlur.Common.ScreenCorners);
                ApplyBlurUnified(source, destination, iBlur.Settings, region, regionRight, corners, cornersRight, iBlur.Alpha, maxStabilizationPixels, iBlur.IsAngled, iBlur.Matrix, iBlur.Common.WorldCamera);
            }
            else
            {
                var (region, corners) = rightEye ? (iBlur.Common.BlurRegionRight, iBlur.Common.ScreenCornersRight) : (iBlur.Common.BlurRegion, iBlur.Common.ScreenCorners);
                ApplyBlurUnified(source, destination, iBlur.Settings, region, null, corners, null, iBlur.Alpha, maxStabilizationPixels, iBlur.IsAngled, iBlur.Matrix, iBlur.Common.WorldCamera);
            }
        }

        void ApplyBlurUnified(RenderTargetIdentifier source, RenderTargetIdentifier destination, BlurSettings settings, Vector4 blurRegion, Vector4? blurRegionRight, Vector4[] blurCorners, Vector4[] blurCornersRight, float alpha, int maxStabilizationPixels, bool isAngled, Matrix4x4 transformationMatrix, Camera uiCamera = null)
        {
            var useQuadBlit = UsesQuadBlit(transformationMatrix, uiCamera, overlayCompatibilityFix);
            blurRegion = PrepareBlurRegion(blurRegion, settings, alpha, renderScale, originalWidth, originalHeight, maxStabilizationPixels);
            var hasRightEye = blurRegionRight.HasValue;
            if (hasRightEye)
                blurRegionRight = PrepareBlurRegion(blurRegionRight.Value, settings, alpha, renderScale, originalWidth, originalHeight, maxStabilizationPixels);

            var (blurRegionWidth, blurRegionHeight) = hasRightEye ? (Mathf.Max(blurRegion.z, blurRegionRight.Value.z), Mathf.Max(blurRegion.w, Mathf.Max(blurRegionRight.Value.w))) : (blurRegion.z, blurRegion.w);

            var aspect = blurRegionWidth / blurRegionHeight;
            var stabilizeImageRegion = maxStabilizationPixels > 0;
            var initialDimensions = GetInitialBlurDimensions(blurRegionWidth, blurRegionHeight, settings, renderScale, originalWidth, originalHeight, stabilizeImageRegion);
            cameraTargetDescriptor.width = initialDimensions.x;
            cameraTargetDescriptor.height = initialDimensions.y;

            var referenceWidthForDownScale = cameraTargetDescriptor.width;
            var referenceHeightForDownScale = cameraTargetDescriptor.height;

            cmd.GetTemporaryRT(Temp1Id, cameraTargetDescriptor, FilterMode.Bilinear);
            var scaleFactor = new Vector2(originalWidth / blurRegion.z, originalHeight / blurRegion.w);
            var offset = scaleFactor * new Vector2(blurRegion.x / originalWidth, blurRegion.y / originalHeight);
            cmd.SetGlobalVector(ScaleFactorID, scaleFactor);
            cmd.SetGlobalVector(SourceOffsetID, offset);

            if (hasRightEye)
            {
                var scaleFactorRight = new Vector2(originalWidth / blurRegionRight.Value.z, originalHeight / blurRegionRight.Value.w);
                cmd.SetGlobalVector(ScaleFactorRightID, scaleFactorRight);
                var offsetRight = scaleFactorRight * new Vector2(blurRegionRight.Value.x / originalWidth, blurRegionRight.Value.y / originalHeight);
                cmd.SetGlobalVector(SourceOffsetRightID, offsetRight);
            }

            FullScreenBlit(cmd, source, Temp1Id, regionalBlitsMat, Convert.ToInt32(settings.hqResample));

            if (alpha > 0)
            {
                if (useComputeShaders)
                    ComputeBlur();
                else
                    TraditionalBlur();
            }
            FinalBlitToDestination();

            void ComputeBlur()
            {
                int totalIterations = 0;
                var threadGroupsX = (cameraTargetDescriptor.width + ThreadGroupSizeX - 1) / ThreadGroupSizeX;
                var threadGroupsY = (cameraTargetDescriptor.height + ThreadGroupSizeY - 1) / ThreadGroupSizeY;
                var computeShader = hasRightEye ? vrComputeBlurs : computeBlurs;
                var temp2NeedsInit = true;

                cmd.SetComputeIntParam(computeShader, ComputeOffsetCenterID, 0);
                foreach (var section in settings.downscaleSections)
                {
                    var (isSeparable, setSamplesPerSide, _, firstKernelIdx, secondKernelIdx) = section.GetSectionBehaviour();

                    if (setSamplesPerSide)
                    {
                        cmd.SetComputeIntParam(computeShader, TapsPerSideHorComputeID, section.horizontalSamplesPerSide);
                        cmd.SetComputeIntParam(computeShader, TapsPerSideVertComputeID, section.verticalSamplesPerSide);
                    }

                    var iterations = section.iterations;
                    var baseSampleDistance = section.sampleDistance;
                    var sectionWidth = cameraTargetDescriptor.width;
                    var sectionHeight = cameraTargetDescriptor.height;

                    for (int i = 0; i < iterations; i++, totalIterations++)
                    {
                        cmd.SetComputeIntParam(computeShader, ComputeBlurIterationID, i);
                        var dimensions = GetDownscaledBlurDimensions(referenceWidthForDownScale, referenceHeightForDownScale, aspect, alpha, totalIterations, stabilizeImageRegion);
                        cameraTargetDescriptor.width = dimensions.x;
                        cameraTargetDescriptor.height = dimensions.y;
                        cmd.SetComputeVectorParam(computeShader, ComputeSampleOffsetID, GetDownscaleSampleOffset(sectionWidth, sectionHeight, cameraTargetDescriptor.width, cameraTargetDescriptor.height, i, iterations));

                        cmd.ReleaseTemporaryRT(Temp2Id);
                        cmd.GetTemporaryRT(Temp2Id, cameraTargetDescriptor, FilterMode.Bilinear);

                        cmd.SetComputeVectorParam(computeShader, ComputeResultDimensionsID, new Vector2(cameraTargetDescriptor.width, cameraTargetDescriptor.height));
                        cmd.SetComputeFloatParam(computeShader, ComputeSampleDistID, alpha * baseSampleDistance * renderScale);

                        cmd.SetComputeTextureParam(computeShader, firstKernelIdx, ComputeSourceID, Temp1Id);
                        cmd.SetComputeTextureParam(computeShader, firstKernelIdx, ComputeResultID, Temp2Id);

                        threadGroupsX = (cameraTargetDescriptor.width + ThreadGroupSizeX - 1) / ThreadGroupSizeX;
                        threadGroupsY = (cameraTargetDescriptor.height + ThreadGroupSizeY - 1) / ThreadGroupSizeY;
                        cmd.DispatchCompute(computeShader, firstKernelIdx, threadGroupsX, threadGroupsY, 1);
                        cmd.ReleaseTemporaryRT(Temp1Id);

                        if (!isSeparable)
                        {
                            temp2NeedsInit = true;
                            (Temp1Id, Temp2Id) = (Temp2Id, Temp1Id);
                            continue;
                        }

                        temp2NeedsInit = false;
                        cmd.GetTemporaryRT(Temp1Id, cameraTargetDescriptor, FilterMode.Bilinear);
                        cmd.SetComputeTextureParam(computeShader, secondKernelIdx, ComputeSourceID, Temp2Id);
                        cmd.SetComputeTextureParam(computeShader, secondKernelIdx, ComputeResultID, Temp1Id);
                        cmd.DispatchCompute(computeShader, secondKernelIdx, threadGroupsX, threadGroupsY, 1);
                    }
                }

                if (temp2NeedsInit)
                {
                    cmd.ReleaseTemporaryRT(Temp2Id);
                    cmd.GetTemporaryRT(Temp2Id, cameraTargetDescriptor, FilterMode.Bilinear);
                    cmd.SetComputeVectorParam(computeShader, ComputeResultDimensionsID, new Vector2(cameraTargetDescriptor.width, cameraTargetDescriptor.height));
                }

                totalIterations = 0;
                cmd.SetComputeIntParam(computeShader, ComputeOffsetCenterID, 1);
                foreach (var section in settings.blurSections)
                {
                    var (isSeparable, setSamplesPerSide, skip, firstKernelIdx, secondKernelIdx) = section.GetSectionBehaviour();
                    if (skip)
                        continue;

                    if (setSamplesPerSide)
                    {
                        cmd.SetComputeIntParam(computeShader, TapsPerSideHorComputeID, section.horizontalSamplesPerSide);
                        cmd.SetComputeIntParam(computeShader, TapsPerSideVertComputeID, section.verticalSamplesPerSide);
                    }

                    var iterations = section.iterations;
                    var baseSampleDistance = section.sampleDistance;

                    if (baseSampleDistance + settings.blurAdditionalDistancePerIteration <= 0)
                        continue;

                    if (!isSeparable)
                    {
                        for (int i = 0; i < iterations; i++, totalIterations++)
                        {
                            cmd.SetComputeVectorParam(computeShader, ComputeSampleOffsetID, GetBlurSampleOffset(i, iterations));

                            cmd.SetComputeIntParam(computeShader, ComputeBlurIterationID, i);
                            var sampleDistance = alpha * (baseSampleDistance + settings.blurAdditionalDistancePerIteration * totalIterations);
                            cmd.SetComputeFloatParam(computeShader, ComputeSampleDistID, sampleDistance * renderScale);
                            cmd.SetComputeTextureParam(computeShader, firstKernelIdx, ComputeSourceID, Temp1Id);
                            cmd.SetComputeTextureParam(computeShader, firstKernelIdx, ComputeResultID, Temp2Id);
                            cmd.DispatchCompute(computeShader, firstKernelIdx, threadGroupsX, threadGroupsY, 1);
                            (Temp1Id, Temp2Id) = (Temp2Id, Temp1Id);
                        }
                    }
                    else
                    {
                        cmd.SetComputeTextureParam(computeShader, firstKernelIdx, ComputeSourceID, Temp1Id);
                        cmd.SetComputeTextureParam(computeShader, firstKernelIdx, ComputeResultID, Temp2Id);
                        cmd.SetComputeTextureParam(computeShader, secondKernelIdx, ComputeSourceID, Temp2Id);
                        cmd.SetComputeTextureParam(computeShader, secondKernelIdx, ComputeResultID, Temp1Id);
                        for (int i = 0; i < iterations; i++, totalIterations++)
                        {
                            cmd.SetComputeVectorParam(computeShader, ComputeSampleOffsetID, GetBlurSampleOffset(i, iterations));

                            var sampleDistance = alpha * (baseSampleDistance + settings.blurAdditionalDistancePerIteration * totalIterations);
                            cmd.SetComputeFloatParam(computeShader, ComputeSampleDistID, sampleDistance * renderScale);
                            cmd.DispatchCompute(computeShader, firstKernelIdx, threadGroupsX, threadGroupsY, 1);
                            cmd.DispatchCompute(computeShader, secondKernelIdx, threadGroupsX, threadGroupsY, 1);
                        }
                    }
                }
            }

            void TraditionalBlur()
            {
                int totalIterations = 0;
                var temp2NeedsInit = true;
                cmd.SetGlobalInt(OffsetCenterID, 0);
                foreach (var section in settings.downscaleSections)
                { 
                    var (isSeparable, setSamplesPerSide, _, firstKernelIdx, secondKernelIdx) = section.GetSectionBehaviour();

                    if (setSamplesPerSide)
                    {
                        cmd.SetGlobalInt(TapsPerSideHorID, section.horizontalSamplesPerSide);
                        cmd.SetGlobalInt(TapsPerSideVertID, section.verticalSamplesPerSide);
                    }

                    var iterations = section.iterations;
                    var baseSampleDistance = section.sampleDistance;
                    var sectionWidth = cameraTargetDescriptor.width;
                    var sectionHeight = cameraTargetDescriptor.height;

                    for (int i = 0; i < iterations; i++, totalIterations++)
                    {
                        cmd.SetGlobalInt(BlurIterationID, i);
                        var dimensions = GetDownscaledBlurDimensions(referenceWidthForDownScale, referenceHeightForDownScale, aspect, alpha, totalIterations, stabilizeImageRegion);
                        cameraTargetDescriptor.width = dimensions.x;
                        cameraTargetDescriptor.height = dimensions.y;
                        cmd.SetGlobalVector(SampleOffsetID, GetDownscaleSampleOffset(sectionWidth, sectionHeight, cameraTargetDescriptor.width, cameraTargetDescriptor.height, i, iterations));
                        cmd.ReleaseTemporaryRT(Temp2Id);
                        cmd.GetTemporaryRT(Temp2Id, cameraTargetDescriptor, FilterMode.Bilinear);

                        cmd.SetGlobalFloat(BlurSampleDistID, alpha * baseSampleDistance * renderScale);
                        FullScreenBlit(cmd, Temp1Id, Temp2Id, blursMat, firstKernelIdx);
                        cmd.ReleaseTemporaryRT(Temp1Id);

                        if (!isSeparable)
                        {
                            temp2NeedsInit = true;
                            (Temp1Id, Temp2Id) = (Temp2Id, Temp1Id);
                            continue;
                        }

                        temp2NeedsInit = false;
                        cmd.GetTemporaryRT(Temp1Id, cameraTargetDescriptor, FilterMode.Bilinear);
                        FullScreenBlit(cmd, Temp2Id, Temp1Id, blursMat, secondKernelIdx);
                    }
                }

                if (temp2NeedsInit)
                {
                    cmd.ReleaseTemporaryRT(Temp2Id);
                    cmd.GetTemporaryRT(Temp2Id, cameraTargetDescriptor, FilterMode.Bilinear);
                }

                totalIterations = 0;
                cmd.SetGlobalInt(OffsetCenterID, 1);
                foreach (var section in settings.blurSections)
                {
                    var (isSeparable, setSamplesPerSide, skip, firstKernelIdx, secondKernelIdx) = section.GetSectionBehaviour();
                    if (skip)
                        continue;

                    if (setSamplesPerSide)
                    {
                        cmd.SetGlobalInt(TapsPerSideHorID, section.horizontalSamplesPerSide);
                        cmd.SetGlobalInt(TapsPerSideVertID, section.verticalSamplesPerSide);
                    }

                    var baseSampleDistance = section.sampleDistance;
                    var iterations = section.iterations;

                    for (int i = 0; i < iterations; i++, totalIterations++)
                    {
                        cmd.SetGlobalInt(BlurIterationID, i);
                        cmd.SetGlobalVector(SampleOffsetID, GetBlurSampleOffset(i, iterations));

                        var sampleDistance = alpha * (baseSampleDistance + settings.blurAdditionalDistancePerIteration * totalIterations);
                        cmd.SetGlobalFloat(BlurSampleDistID, sampleDistance * renderScale);
                        FullScreenBlit(cmd, Temp1Id, Temp2Id, blursMat, firstKernelIdx);

                        if (!isSeparable)
                        {
                            (Temp1Id, Temp2Id) = (Temp2Id, Temp1Id);
                            continue;
                        }

                        FullScreenBlit(cmd, Temp2Id, Temp1Id, blursMat, secondKernelIdx);
                    }
                }
            }

            void FinalBlitToDestination()
            {
                cmd.SetGlobalFloat(DitherStrengthID, alpha * settings.ditherStrength);

                cmd.SetGlobalVector(DestinationRegionSizeID, blurRegion);
                if (hasRightEye)
                    cmd.SetGlobalVector(DestinationRegionSizeRightID, blurRegionRight.Value);

                cmd.SetGlobalFloat(VibrancyID, (alpha * settings.vibrancy + 1) * 0.5f);
                cmd.SetGlobalFloat(BrightnessID, alpha * settings.brightness);
                cmd.SetGlobalFloat(ContrastID, alpha * settings.contrast + 1);
                cmd.SetGlobalVector(TintID, alpha * settings.tint);

                if (useQuadBlit)
                {
                    if (multiPassVR)
                    {
                        BlitToQuad(cmd, Temp1Id, destination, quadBlitsMat, transformationMatrix, blurRegion, blurRegionRight, originalWidth, originalHeight, 0);
                    }
                    else
                    {
                        cmd.SetProjectionMatrix(uiCamera?.projectionMatrix ?? OverlayUIProjectionMatrix);
                        cmd.SetViewMatrix(uiCamera?.worldToCameraMatrix ?? Matrix4x4.identity);
                        BlitToQuad(cmd, Temp1Id, destination, quadBlitsMat, transformationMatrix, blurRegion, blurRegionRight, originalWidth, originalHeight, 0);
                        cmd.SetProjectionMatrix(camera.projectionMatrix);
                        cmd.SetViewMatrix(camera.worldToCameraMatrix);
                    }
                }
                else
                {
                    if (isAngled)
                    {
                        cmd.SetGlobalFloat(RenderScaleID, renderScale);
                        cmd.SetGlobalVectorArray(CornersID, blurCorners);
                        if (hasRightEye)
                            cmd.SetGlobalVectorArray(CornersRightID, blurCornersRight);
                    }

                    BlitToRegion(cmd, Temp1Id, destination, regionalBlitsMat, blurRegion, originalWidth, originalHeight, isAngled ? 3 : 2);
                }

                cmd.SetRenderTarget(cameraRT);
                cmd.ReleaseTemporaryRT(Temp1Id);
                cmd.ReleaseTemporaryRT(Temp2Id);
            }
        }
    }
#endif

    private static bool UsesQuadBlit(Matrix4x4 transformationMatrix, Camera uiCamera, bool overlayCompatibilityFix)
    {
        return transformationMatrix != Matrix4x4.identity && (!overlayCompatibilityFix || uiCamera != null);
    }

    private static Vector4 PrepareBlurRegion(Vector4 region, BlurSettings settings, float alpha, float renderScale, float targetWidth, float targetHeight, int maxStabilizationPixels)
    {
        region *= renderScale;
        if (maxStabilizationPixels <= 0 || !Mathf.Approximately(renderScale, 1f))
            return region;
        if (region.x <= 0f && region.y <= 0f && region.x + region.z >= targetWidth && region.y + region.w >= targetHeight)
            return region;

        var iterations = 0;
        foreach (var section in settings.downscaleSections)
            if (section != null)
                iterations += Mathf.Max(0, section.iterations);

        var processingDimensions = GetProcessingDimensions(settings, targetWidth, targetHeight);
        var divisor = Mathf.Pow(1f + Mathf.Clamp01(alpha), iterations);
        var finalHeight = Mathf.Max(1, Mathf.RoundToInt(processingDimensions.y / divisor));
        var finalWidth = Mathf.Max(1, Mathf.RoundToInt(finalHeight * targetWidth / targetHeight));
        var alignmentX = Mathf.Min(maxStabilizationPixels, processingDimensions.x / (float)finalWidth);
        var alignmentY = Mathf.Min(maxStabilizationPixels, processingDimensions.y / (float)finalHeight);
        var scaleX = processingDimensions.x / targetWidth;
        var scaleY = processingDimensions.y / targetHeight;

        var minX = Mathf.Max(0f, Mathf.Floor(region.x * scaleX / alignmentX) * alignmentX);
        var minY = Mathf.Max(0f, Mathf.Floor(region.y * scaleY / alignmentY) * alignmentY);
        var maxX = Mathf.Min(processingDimensions.x, Mathf.Ceil((region.x + region.z) * scaleX / alignmentX) * alignmentX);
        var maxY = Mathf.Min(processingDimensions.y, Mathf.Ceil((region.y + region.w) * scaleY / alignmentY) * alignmentY);
        return new Vector4(minX / scaleX, minY / scaleY, (maxX - minX) / scaleX, (maxY - minY) / scaleY);
    }

    private static Vector2Int GetProcessingDimensions(BlurSettings settings, float targetWidth, float targetHeight)
    {
        var height = settings.referenceResolution > 0 ? settings.referenceResolution : Mathf.RoundToInt(targetHeight);
        return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(height * targetWidth / targetHeight)), Mathf.Max(1, height));
    }

    private static Vector2Int GetInitialBlurDimensions(float width, float height, BlurSettings settings, float renderScale, float targetWidth, float targetHeight, bool stabilizeImageRegion)
    {
        var scale = renderScale * (settings.referenceResolution > 0 ? settings.referenceResolution / targetHeight : 1f);
        var textureHeight = Mathf.Max(1, Mathf.RoundToInt(scale * height));
        if (!stabilizeImageRegion || !Mathf.Approximately(renderScale, 1f))
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(textureHeight * width / height)), textureHeight);

        var processingDimensions = GetProcessingDimensions(settings, targetWidth, targetHeight);
        var textureWidth = Mathf.Max(1, Mathf.RoundToInt(width * processingDimensions.x / targetWidth));
        return new Vector2Int(textureWidth, textureHeight);
    }

    private static Vector2Int GetDownscaledBlurDimensions(int initialWidth, int initialHeight, float aspect, float alpha, int iteration, bool stabilizeImageRegion)
    {
        var divisor = Mathf.Pow(1f + alpha, iteration + 1);
        var height = Mathf.Max(1, Mathf.RoundToInt(initialHeight / divisor));
        var width = stabilizeImageRegion
            ? Mathf.Max(1, Mathf.RoundToInt(initialWidth / divisor))
            : Mathf.Max(1, Mathf.RoundToInt(height * aspect));
        return new Vector2Int(width, height);
    }

    internal static Vector4 GetDownscaleSampleOffset(int sectionWidth, int sectionHeight, int destinationWidth, int destinationHeight, int iteration, int iterations)
    {
        if (iteration == iterations - 1 && iteration % 2 == 0)
            return Vector4.zero;

        var sign = iteration % 2 == 0 ? 1f : -1f;
        return new Vector4(sign * destinationWidth / Mathf.Max(1f, sectionWidth), sign * destinationHeight / Mathf.Max(1f, sectionHeight), 0f, 0f);
    }

    internal static Vector4 GetBlurSampleOffset(int iteration, int iterations)
    {
        if (iteration == iterations - 1 && iteration % 2 == 0)
            return Vector4.zero;
        var offset = iteration % 2 == 0 ? 0.5f : -0.5f;
        return new Vector4(offset, offset, 0f, 0f);
    }

    private static void BlitToQuad(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, Material material, Matrix4x4 quadMatrix, Vector4 blurRegion, Vector4? blurRegionRight, float width, float height, int passIndex)
    {
        cmd.SetGlobalTexture(MainTexID, source);
        cmd.SetRenderTarget(new RenderTargetIdentifier
        (
            destination, 0, CubemapFace.Unknown, -1),
            RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store,
            RenderBufferLoadAction.DontCare,
            RenderBufferStoreAction.DontCare
        );

        cmd.SetGlobalVector(BlurRegionID, new Vector4(blurRegion.x / width, blurRegion.y / height, blurRegion.z / width, blurRegion.w / height));
        if (blurRegionRight.HasValue)
            cmd.SetGlobalVector(BlurRegionRightID, new Vector4(blurRegionRight.Value.x / width, blurRegionRight.Value.y / height, blurRegionRight.Value.z / width, blurRegionRight.Value.w / height));

        cmd.DrawMesh(FullScreenMesh, quadMatrix, material, 0, passIndex);
    }

    private static void BlitToRegion(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, Material material, Vector4 blurRegion, float width, float height, int passIndex = 0)
    {
        cmd.SetGlobalTexture(MainTexID, source);
        cmd.SetRenderTarget(new RenderTargetIdentifier
        (
            destination, 0, CubemapFace.Unknown, -1),
            RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store,
            RenderBufferLoadAction.DontCare,
            RenderBufferStoreAction.DontCare
        );

        var left     = blurRegion.x / width * 2 - 1;
        var right    = (blurRegion.x + blurRegion.z) / width * 2 - 1;
        var bottom   = blurRegion.y / height * 2 - 1;
        var top      = (blurRegion.y + blurRegion.w) / height * 2 - 1;

        var transformationMatrix = Matrix4x4.TRS(new Vector3((left + right) * 0.5f, (top + bottom) * 0.5f, 0), Quaternion.identity, new Vector3((right - left) * 0.5f, (top - bottom) * 0.5f, 1));
        cmd.DrawMesh(FullScreenMesh, transformationMatrix, material, 0, passIndex);
    }

    private static void FullScreenBlit(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, Material material, int passIndex = 0)
    {
        cmd.SetGlobalTexture(MainTexID, source);
        cmd.SetGlobalTexture(DestinationTexID, destination);
        cmd.SetRenderTarget(new RenderTargetIdentifier
        (
            destination, 0, CubemapFace.Unknown, -1),
            RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store,
            RenderBufferLoadAction.DontCare,
            RenderBufferStoreAction.DontCare
        );
        cmd.DrawMesh(FullScreenMesh, Matrix4x4.identity, material, 0, passIndex);
    }

    private static Mesh _fullScreenMesh;
    private static Mesh FullScreenMesh
    {
        get
        {
            if (_fullScreenMesh != null)
                return _fullScreenMesh;

            return _fullScreenMesh = GetDefaultQuadMesh(true);
        }
    }

    private static Mesh GetDefaultQuadMesh(bool markNoLongerReadable)
    {
        var mesh = new Mesh { name = "Quad" };
        mesh.SetVertices(new Vector3[]
        {
            new (-1.0f, -1.0f, 0.0f),
            new (-1.0f,  1.0f, 0.0f),
            new (1.0f, -1.0f, 0.0f),
            new (1.0f,  1.0f, 0.0f)
        });

        mesh.SetUVs(0, new Vector2[]
        {
            new (0.0f, 0.0f),
            new (0.0f, 1.0f),
            new (1.0f, 0.0f),
            new (1.0f, 1.0f)
        });

        mesh.SetIndices(new[] { 0, 1, 2, 2, 1, 3 }, MeshTopology.Triangles, 0, false);
        mesh.UploadMeshData(markNoLongerReadable);
        return mesh;
    }
}
}
