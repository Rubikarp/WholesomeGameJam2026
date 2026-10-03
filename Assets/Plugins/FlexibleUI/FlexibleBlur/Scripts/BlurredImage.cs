using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
[ExecuteAlways]
public class BlurredImage : Image, IBlur
{
#if UNITY_EDITOR
    public static readonly string BlurCommonFieldName = nameof(_common);
    public static readonly string AlphaBlendFieldName = nameof(_alphaBlend);
    public static readonly string SourceImageFadeFieldName = nameof(_sourceImageFade);
#endif

    private const string ImageShaderName = "Hidden/JeffGrawAssets/BlurredImage";
    private static Shader _imageShader;
    private static Shader ImageShader
    {
        get
        {
            if (!_imageShader)
                _imageShader = Shader.Find(ImageShaderName);
            return _imageShader;
        }
    }

    private static readonly int BlurTexPropID = Shader.PropertyToID("_BlurTex");

    private static Material _defaultBlurMaterial;
    private static Material DefaultBlurMaterial
    {
        get
        {
            if (_defaultBlurMaterial == null)
                _defaultBlurMaterial = new Material(ImageShader) { name = "DefaultBlurredImage" };

            return _defaultBlurMaterial;
        }
    }

    [SerializeField] private UIBlurCommon _common = new();
    public UIBlurCommon Common => _common;

    [SerializeField] [Range(0, 255)] private byte _alphaBlend = 255;
    public byte AlphaBlend
    {
        get => _alphaBlend;
        set
        {
            _alphaBlend = value;
            SetVerticesDirty();
        }
    }

    [SerializeField] [Range(0, 255)] private byte _sourceImageFade;
    public byte SourceImageFade
    {
        get => _sourceImageFade;
        set
        {
            _sourceImageFade = value;
            SetVerticesDirty();
        }
    }

    public float Alpha { get; private set; }

    public float additionalBlurPadding;
    public bool tryBatchWithSimilar;
    public bool fillEntireRenderTexture;
    public bool CanBatch => tryBatchWithSimilar && Common.blurPreset;
    public bool FillEntireRenderTexture => CanBatch && fillEntireRenderTexture;

    public bool ActiveAtZeroAlpha => false;

    private RTHandle rtHandle;

    protected override void OnEnable()
    {
        base.OnEnable();
        Common.Init(this, FlexibleBlurFeature.ImageBasedBlurDict, FlexibleBlurFeature.ImageBasedLayersPerCameraDict);
        Common.ValidateBlur();
        FlexibleBlurPass.ComputeBlurEvent += Common.ComputeBlur;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Common.RemoveFromBlurList();
        FlexibleBlurPass.ComputeBlurEvent -= Common.ComputeBlur;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
        Common.ValidateBlur();
    }
#endif

    private void ValidateCanvasShaderChannels()
    {
        if (!canvas)
            return;

        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
    }

    void Update()
    {
        CalculateBlur();
    }

    void LateUpdate()
    {
        if (!canvas || !canvas.isActiveAndEnabled)
            return;

        material = DefaultBlurMaterial;
        if (canvasRenderer.cull || (!ActiveAtZeroAlpha && (canvasRenderer.GetInheritedAlpha() == 0 || color.a == 0)))
            return;

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

    private void CalculateBlur()
    {
        if (canvasRenderer.cull || !canvas || !canvas.isActiveAndEnabled)
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

        Alpha = Mathf.Lerp(canvasAlpha * color.a, 1f, AlphaBlend / 255f) * Common.blurStrength;

        Common.CacheBlur(canvas, rectTransform, Vector2.zero, 0f, false, Vector2.zero, additionalBlurPadding, fillWholeScreen: fillEntireRenderTexture);
        Common.ComputeBlurCommon(canvas, rectTransform, Vector2.zero, 0f, false, Vector2.zero, additionalBlurPadding, fillWholeScreen: fillEntireRenderTexture);
    }

    public override Material materialForRendering
    {
        get
        {
            if (!canvas || !canvas.isActiveAndEnabled)
                return DefaultBlurMaterial;

            var key = Common.GetCameraFeatureKey(canvas);
            if (!key.camera)
                return DefaultBlurMaterial;

            if (!FlexibleBlurPass.TryGetImageRT(key.camera, key.featureNumber, Common.LayerRank, out var newHandle))
                return null;

            var mat = base.materialForRendering;
            if (mat != null)
                mat.SetTexture(BlurTexPropID, rtHandle = newHandle);

            return mat;
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        ValidateCanvasShaderChannels();

        if (sprite == null && type == Type.Filled)
            FilledMeshHelper.GenerateFilledMesh(vh, this);
        else
            base.OnPopulateMesh(vh);

        var sourceImageFadeNormalized = SourceImageFade / 255f;
        var alphaBlendNormalized = AlphaBlend / 255f;
        var vertices = UnityEngine.Pool.ListPool<UIVertex>.Get();
        vh.GetUIVertexStream(vertices);
        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            vertex.uv0 = new Vector4(vertex.uv0.x, vertex.uv0.y, sourceImageFadeNormalized, alphaBlendNormalized);
            vertices[i] = vertex;
        }
        vh.Clear();
        vh.AddUIVertexTriangleStream(vertices);
        UnityEngine.Pool.ListPool<UIVertex>.Release(vertices);
    }
}
}