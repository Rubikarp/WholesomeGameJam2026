using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
public partial class FlexibleGlassCameraOverride
{
#if UNITY_EDITOR
    public const string UseFlexibleBlurFieldName = nameof(useFlexibleBlur);
    public const string FlexibleBlurFeatureNumberFieldName = nameof(flexibleBlurFeatureNumber);
    public const string FlexibleBlurPresetFieldName = nameof(flexibleBlurPreset);
    public const string FlexibleBlurSettingsFieldName = nameof(flexibleBlurSettings);
#endif

    [SerializeField] private bool useFlexibleBlur;
    [SerializeField] [Min(0)] private int flexibleBlurFeatureNumber;
    [SerializeField] private BlurPreset flexibleBlurPreset;
    [SerializeField] private BlurSettings flexibleBlurSettings = new();

    partial void ConfigureIntegratedBlurPlan(ref GlassBlurPlan plan)
    {
        if (!useFlexibleBlur)
            return;

        var settings = GetFlexibleBlurSettings();
        if (settings == null)
            return;

        plan.integrated = true;
        plan.integratedReach = FlexibleBlurPass.EstimateBlurReach(settings);
        plan.integrationData = settings;
        plan.integratedFeatureNumber = Mathf.Max(0, flexibleBlurFeatureNumber);
    }

    private BlurSettings GetFlexibleBlurSettings()
    {
        if (!flexibleBlurPreset)
            return flexibleBlurSettings ??= new BlurSettings();
        if (flexibleBlurPreset.Settings.Count == 0)
            return null;

        var quality = flexibleBlurPreset.preview >= 0 ? flexibleBlurPreset.preview : QualitySettings.GetQualityLevel();
        return flexibleBlurPreset.Settings[Mathf.Clamp(quality, 0, flexibleBlurPreset.Settings.Count - 1)];
    }
}
}
