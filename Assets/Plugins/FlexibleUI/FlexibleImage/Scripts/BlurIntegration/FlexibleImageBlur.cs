using UnityEngine;
using UnityEngine.Rendering;

namespace JeffGrawAssets.FlexibleUI
{
public partial class FlexibleImage : IBlur
{
    private static readonly int BlurTexPropID = Shader.PropertyToID("_BlurTex");

#if UNITY_EDITOR
    public static readonly string BlurEnabledFieldName = nameof(_blurEnabled);
    public static readonly string BlurCommonFieldName = nameof(_common);
    public static readonly string AlphaBlendFieldName = nameof(_alphaBlend);
    public static readonly string SourceImageFadeFieldName = nameof(_sourceImageFade);
#endif

    public float Alpha { get; private set; }

    [SerializeField] private bool _blurEnabled;
    partial void IsBlurEnabled(ref bool enabled) => enabled = _blurEnabled; // To safely check if blur is enabled without causing compile errors if the partial class is removed.
    public bool BlurEnabled
    {
        get => _blurEnabled;
        set
        {
            _blurEnabled = value;
            SetVerticesDirty();
        }
    }

    [SerializeField][Range(0,255)] private byte _alphaBlend = 255;
    public byte AlphaBlend
    {
        get => !BlurEnabled ? (byte)255 : _alphaBlend;
        set
        {
            _alphaBlend = value;
            SetVerticesDirty();
        }
    }

    partial void GetAlphaBlend(ref byte alphaBlend)
    {
        if (!BlurEnabled)
            return;

        alphaBlend = AlphaBlend;
    }

    [SerializeField] [Range(0, 255)] private byte _sourceImageFade;
    public byte SourceImageFade
    {
        get => !BlurEnabled ? (byte)255 : _sourceImageFade;
        set
        {
            _sourceImageFade = value;
            SetVerticesDirty();
        }
    }
    partial void GetSourceImageFade(ref byte sourceImageFade) => sourceImageFade = SourceImageFade;

    private static int lastComponentEnabledFrame = -1;

    public float additionalBlurPadding;
    public bool tryBatchWithSimilar;
    public bool fillEntireRenderTexture;
    public bool CanBatch => tryBatchWithSimilar && Common.blurPreset;
    public bool FillEntireRenderTexture => CanBatch && fillEntireRenderTexture;
    public bool ActiveAtZeroAlpha => false;
    [SerializeField] private UIBlurCommon _common = new();
    public UIBlurCommon Common => _common;

    private RTHandle rtHandle;

    partial void GetMaterialForRenderingBlur(ref Material mat)
    {
        if (!BlurEnabled)
            return;

        if (!canvas || !canvas.isActiveAndEnabled)
            return;

        var key = Common.GetCameraFeatureKey(canvas);
        if (!key.camera)
            return;

        if (!FlexibleBlurPass.TryGetImageRT(key.camera, key.featureNumber, Common.LayerRank, out var newHandle))
        {
            mat = null;
            return; 
        }

        mat = base.materialForRendering;
        if (mat != null)
            mat.SetTexture(BlurTexPropID, rtHandle = newHandle);
    }

    partial void OnEnableBlur()
    {
        Common.Init(this, FlexibleBlurFeature.ImageBasedBlurDict, FlexibleBlurFeature.ImageBasedLayersPerCameraDict);
        Common.ValidateBlur();
        FlexibleBlurPass.ComputeBlurEvent += Common.ComputeBlur;
        lastComponentEnabledFrame = Time.frameCount;
    }

    partial void OnDisableBlur()
    {
        Common.RemoveFromBlurList();
        FlexibleBlurPass.ComputeBlurEvent -= Common.ComputeBlur;
    }

#if UNITY_EDITOR
    partial void OnValidateBlur() => Common.ValidateBlur();
#endif

    partial void LateUpdateBlur()
    {
        if (!BlurEnabled || !canvas || !canvas.isActiveAndEnabled || canvasRenderer.cull || (!ActiveAtZeroAlpha && (canvasRenderer.GetInheritedAlpha() == 0 || color.a == 0)))
        {
            material = DefaultProceduralBlurredImageMat;
            return;
        }

        var key = Common.GetCameraFeatureKey(canvas);
        if (!key.camera || !FlexibleBlurPass.TryGetImageMaterial(key.camera, key.featureNumber, Common.LayerRank, ImageShader, out var mat))
            return;

        if (material != mat)
        {
            material = mat;
            SetMaterialDirty();
        }
        else if (!rtHandle?.rt)
        {
            SetMaterialDirty();
        }
    }

    partial void CalculateBlur()
    {
        if (canvasRenderer.cull || !BlurEnabled || !canvas || !canvas.isActiveAndEnabled)
        {
            Common.RemoveFromBlurList();
            return;
        }

        var canvasAlpha = canvasRenderer.GetInheritedAlpha();
        if (!ActiveAtZeroAlpha && (canvasAlpha == 0 || color.a == 0))
        {
            Common.RemoveFromBlurList();
            return;
        }

        var quadContainer = ActiveQuadDataContainer;
        var quadData = quadContainer.PrimaryQuadData;
        var useAnimationValues = animationValues != null && animationValues.Count == quadContainer.Count;
        var animationProps = useAnimationValues ? animationValues[quadContainer.primaryQuadIdx].CurrentProperties : null;
        var (offset, rotation, primaryColors) = useAnimationValues
            ? (animationProps.offset, animationProps.rotation, animationProps.primaryColors)
            : (quadData.Offset,       quadData.Rotation,       quadData.PrimaryColorsWithoutDirtying);

        Alpha = Mathf.Lerp(canvasAlpha * primaryColors[0].a, 1f, AlphaBlend / 255f) * Common.blurStrength;
        var sizeModifier = quadData.GetSizeModifier(rectTransform, animationProps);
        Common.CacheBlur(canvas, rectTransform, offset, rotation, quadData.FitRotatedImageWithinBounds, sizeModifier * canvas.scaleFactor, additionalBlurPadding, fillWholeScreen: fillEntireRenderTexture);
        if (lastComponentEnabledFrame != Time.frameCount)
            Common.ComputeBlurCommon(canvas, rectTransform, offset, rotation, quadData.FitRotatedImageWithinBounds, sizeModifier * canvas.scaleFactor, additionalBlurPadding, fillWholeScreen: fillEntireRenderTexture);
    }
}
}
