using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace JeffGrawAssets.FlexibleUI
{
public partial class FlexibleGlassFeature
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

public partial class FlexibleGlassPass
{
    private readonly Dictionary<Camera, FlexibleBlurTextureRequest> integratedBlurRequests = new(2);
    private readonly List<Camera> staleIntegratedBlurCameras = new(2);

    partial void UpdateIntegratedBlurRequestCore(Camera camera, RenderTextureDescriptor descriptor)
    {
        PruneIntegratedBlurRequests();
        if (!PrefetchFrame(camera, descriptor, out var frame) || !frame.blurPlan.integrated || frame.blurPlan.integrationData is not BlurSettings settings)
        {
            RemoveIntegratedBlurRequest(camera);
            return;
        }

        if (!integratedBlurRequests.TryGetValue(camera, out var request))
            request = integratedBlurRequests[camera] = new FlexibleBlurTextureRequest();

        var mipLevels = GetBlurReconstructionLevelCount(frame);
        var output = GetImageOutput(camera, descriptor, mipLevels);
        var scaleX = camera.pixelWidth / (float)Mathf.Max(1, descriptor.width);
        var scaleY = camera.pixelHeight / (float)Mathf.Max(1, descriptor.height);
        var region = frame.blurRegion;
        var minX = Mathf.FloorToInt(region.xMin * scaleX);
        var minY = Mathf.FloorToInt(region.yMin * scaleY);
        var maxX = Mathf.CeilToInt(region.xMax * scaleX);
        var maxY = Mathf.CeilToInt(region.yMax * scaleY);
        request.Update(camera, frame.blurPlan.integratedFeatureNumber, output, settings, new RectInt(minX, minY, maxX - minX, maxY - minY), mipLevels: mipLevels);
    }

    partial void RemoveIntegratedBlurRequests()
    {
        foreach (var request in integratedBlurRequests.Values)
            request.Dispose();
        integratedBlurRequests.Clear();
    }

    private void RemoveIntegratedBlurRequest(Camera camera)
    {
        if (ReferenceEquals(camera, null) || !integratedBlurRequests.Remove(camera, out var request))
            return;
        request.Dispose();
    }

    private void PruneIntegratedBlurRequests()
    {
        staleIntegratedBlurCameras.Clear();
        foreach (var entry in integratedBlurRequests)
        {
            if (entry.Key)
                continue;
            entry.Value.Dispose();
            staleIntegratedBlurCameras.Add(entry.Key);
        }
        foreach (var camera in staleIntegratedBlurCameras)
            integratedBlurRequests.Remove(camera);
    }
}
}
