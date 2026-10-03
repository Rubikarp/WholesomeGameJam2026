using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace JeffGrawAssets.FlexibleUI
{
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Flexible UI/Flexible Glass Camera Override")]
public partial class FlexibleGlassCameraOverride : MonoBehaviour
{
#if UNITY_EDITOR
    public const string FeatureNumberFieldName = nameof(featureNumber);
    public const string OverrideCompositionFieldName = nameof(overrideComposition);
    public const string CompositionBlendFieldName = nameof(compositionBlend);
    public const string OverrideRefractionFieldName = nameof(overrideRefraction);
    public const string BackdropMipLevelsFieldName = nameof(backdropMipLevels);
    public const string OverrideLightingFieldName = nameof(overrideLighting);
    public const string EdgeLightModeFieldName = nameof(edgeLightMode);
    public const string EdgeLightAngleFieldName = nameof(edgeLightAngle);
    public const string PointLightPositionFieldName = nameof(pointLightPosition);
    public const string PointLightRadiusFieldName = nameof(pointLightRadius);
    public const string EdgeLightSpreadFieldName = nameof(edgeLightSpread);
    public const string EdgeHighlightFieldName = nameof(edgeHighlight);
    public const string EdgeShadowFieldName = nameof(edgeShadow);
    public const string OpposingEdgeLightStrengthFieldName = nameof(opposingEdgeLightStrength);
    public const string OverrideBlurFieldName = nameof(overrideBlur);
    public const string IterationsFieldName = nameof(iterations);
    public const string SampleRadiusFieldName = nameof(sampleRadius);
    public const string DitherStrengthFieldName = nameof(ditherStrength);
    public const string BlurPaddingFieldName = nameof(blurPadding);
    public const string BlurFormatFieldName = nameof(blurFormat);
#endif

#if UNITY_6000_6_OR_NEWER
    [Unity.Scripting.LifecycleManagement.AutoStaticsCleanup]
#endif
    private static readonly Dictionary<(Camera camera, int featureNumber), FlexibleGlassCameraOverride> Overrides = new();

    [SerializeField] [Min(0)] private int featureNumber;

    [SerializeField] private bool overrideComposition;
    [SerializeField] [Range(0f, 100f)] private float compositionBlend = 16f;

    [SerializeField] private bool overrideRefraction = true;
    [SerializeField] [Range(0, 8)] private int backdropMipLevels = 4;

    [SerializeField] private bool overrideLighting;
    [SerializeField] private GlassEdgeLightMode edgeLightMode;
    [SerializeField] private float edgeLightAngle = 60f;
    [SerializeField] private Vector2 pointLightPosition = new(0.5f, 0.5f);
    [SerializeField] [Min(0.01f)] private float pointLightRadius = 0.5f;
    [SerializeField] [Range(0f, 1f)] private float edgeLightSpread = 0.5f;
    [SerializeField] [ColorUsage(true)] private Color edgeHighlight = new(1f, 1f, 1f, 0.12f);
    [SerializeField] [ColorUsage(true)] private Color edgeShadow = new(0f, 0f, 0f, 0f);
    [SerializeField] [Range(0f, 1f)] private float opposingEdgeLightStrength = 0.5f;

    [SerializeField] private bool overrideBlur = true;
    [SerializeField] [Range(0, 6)] private int iterations = 2;
    [SerializeField] [Range(0.5f, 2f)] private float sampleRadius = 1f;
    [SerializeField] [Range(0f, 5f)] private float ditherStrength = 0.25f;
    [SerializeField] [Min(0f)] private float blurPadding;
    [SerializeField] private GraphicsFormat blurFormat;

    private Camera targetCamera;
    private int registeredFeatureNumber = -1;

    public int FeatureNumber
    {
        get => featureNumber;
        set
        {
            featureNumber = Mathf.Max(0, value);
            RefreshRegistration();
        }
    }

    public bool OverrideComposition { get => overrideComposition; set => overrideComposition = value; }
    public float CompositionBlend { get => compositionBlend; set => compositionBlend = Mathf.Clamp(value, 0f, 100f); }
    public bool OverrideRefraction { get => overrideRefraction; set => overrideRefraction = value; }
    public int BackdropMipLevels { get => backdropMipLevels; set => backdropMipLevels = Mathf.Clamp(value, 0, 8); }
    public bool OverrideLighting { get => overrideLighting; set => overrideLighting = value; }
    public GlassEdgeLightMode EdgeLightMode { get => edgeLightMode; set => edgeLightMode = value; }
    public float EdgeLightAngle { get => edgeLightAngle; set => edgeLightAngle = value; }
    public Vector2 PointLightPosition { get => pointLightPosition; set => pointLightPosition = value; }
    public float PointLightRadius { get => pointLightRadius; set => pointLightRadius = Mathf.Max(0.01f, value); }
    public float EdgeLightSpread { get => edgeLightSpread; set => edgeLightSpread = Mathf.Clamp01(value); }
    public Color EdgeHighlight { get => edgeHighlight; set => edgeHighlight = value; }
    public Color EdgeShadow { get => edgeShadow; set => edgeShadow = value; }
    public float OpposingEdgeLightStrength { get => opposingEdgeLightStrength; set => opposingEdgeLightStrength = Mathf.Clamp01(value); }
    public bool OverrideBlur { get => overrideBlur; set => overrideBlur = value; }
    public int Iterations { get => iterations; set => iterations = Mathf.Clamp(value, 0, 6); }
    public float SampleRadius { get => sampleRadius; set => sampleRadius = Mathf.Clamp(value, 0.5f, 2f); }
    public float DitherStrength { get => ditherStrength; set => ditherStrength = Mathf.Clamp(value, 0f, 5f); }
    public float BlurPadding { get => blurPadding; set => blurPadding = Mathf.Max(0f, value); }
    public GraphicsFormat BlurFormat { get => blurFormat; set => blurFormat = value; }

    internal bool OverridesComposition => overrideComposition;
    internal bool OverridesRefraction => overrideRefraction;
    internal bool OverridesLighting => overrideLighting;
    internal bool OverridesBlur => overrideBlur;
    internal float EffectiveCompositionBlend => Mathf.Clamp(compositionBlend, 0f, 100f);
    internal int EffectiveBackdropMipLevels => Mathf.Clamp(backdropMipLevels, 0, 8);
    internal float EffectiveBlurPadding => Mathf.Max(0f, blurPadding);
    internal GraphicsFormat EffectiveBlurFormat => blurFormat == GraphicsFormat.None ? FlexibleGlassFeature.DefaultBlurFormat : blurFormat;

    internal GlassLightingPlan BuildLightingPlan() => FlexibleGlassFeature.BuildLightingPlan(edgeLightMode, edgeLightAngle, pointLightPosition, pointLightRadius, edgeLightSpread, edgeHighlight, edgeShadow, opposingEdgeLightStrength);

    internal GlassBlurPlan BuildBlurPlan()
    {
        var plan = new GlassBlurPlan(Mathf.Clamp(iterations, 0, 6), Mathf.Clamp(sampleRadius, 0.5f, 2f), Mathf.Clamp(ditherStrength, 0f, 5f));
        ConfigureIntegratedBlurPlan(ref plan);
        return plan;
    }

    partial void ConfigureIntegratedBlurPlan(ref GlassBlurPlan plan);

    internal static bool TryGet(Camera camera, int featureNumber, out FlexibleGlassCameraOverride settings)
    {
        settings = null;
        return camera && Overrides.TryGetValue((camera, featureNumber), out settings) && settings && settings.isActiveAndEnabled;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Overrides.Clear();
#if UNITY_EDITOR
        foreach (var settings in Resources.FindObjectsOfTypeAll<FlexibleGlassCameraOverride>())
            if (settings && settings.gameObject.scene.IsValid() && settings.isActiveAndEnabled)
                settings.RefreshRegistration();
#endif
    }

    private void Reset() => InitializeDefaults();

    private void OnEnable()
    {
        InitializeDefaults();
        RefreshRegistration();
    }

    private void OnDisable() => RemoveRegistration();
    private void OnDestroy() => RemoveRegistration();

    private void OnValidate()
    {
        featureNumber = Mathf.Max(0, featureNumber);
        compositionBlend = Mathf.Clamp(compositionBlend, 0f, 100f);
        backdropMipLevels = Mathf.Clamp(backdropMipLevels, 0, 8);
        pointLightRadius = Mathf.Max(0.01f, pointLightRadius);
        edgeLightSpread = Mathf.Clamp01(edgeLightSpread);
        opposingEdgeLightStrength = Mathf.Clamp01(opposingEdgeLightStrength);
        iterations = Mathf.Clamp(iterations, 0, 6);
        sampleRadius = Mathf.Clamp(sampleRadius, 0.5f, 2f);
        ditherStrength = Mathf.Clamp(ditherStrength, 0f, 5f);
        blurPadding = Mathf.Max(0f, blurPadding);
        InitializeDefaults();
        if (isActiveAndEnabled)
            RefreshRegistration();
    }

    private void InitializeDefaults()
    {
        if (blurFormat == GraphicsFormat.None)
            blurFormat = FlexibleGlassFeature.DefaultBlurFormat;
    }

    private void RefreshRegistration()
    {
        RemoveRegistration();
        if (!isActiveAndEnabled || !TryGetComponent(out targetCamera))
            return;

        registeredFeatureNumber = featureNumber;
        Overrides[(targetCamera, registeredFeatureNumber)] = this;
    }

    private void RemoveRegistration()
    {
        if (!targetCamera || registeredFeatureNumber < 0)
            return;

        var key = (targetCamera, registeredFeatureNumber);
        if (Overrides.TryGetValue(key, out var current) && current == this)
            Overrides.Remove(key);
        registeredFeatureNumber = -1;
    }
}
}
