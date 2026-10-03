using System;
using System.Collections.Generic;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[CreateAssetMenu(menuName = "FlexibleUI/BlurPreset")]
public class BlurPreset : ScriptableObject
{
#if UNITY_EDITOR
    public static readonly string SettingsFieldName = nameof(settings);

    public static int GetQualitySettingsCount() =>
#if UNITY_2022_3_OR_NEWER
    QualitySettings.count;
#else
    QualitySettings.names.Length;
#endif

    private void OnValidate()
    {
        if (settings.Count == 0)
            settings.Add(new BlurSettings());

        while (settings.Count < GetQualitySettingsCount())
            Settings.Add(new BlurSettings(Settings[^1]));
    }

    public bool TryFillSettings()
    {
        var addedQualitySetting = false;
        if (settings.Count == 0)
        {
            addedQualitySetting = true;
            settings.Add(new BlurSettings());
        }

        while (settings.Count < GetQualitySettingsCount())
        {
            addedQualitySetting = true;
            Settings.Add(new BlurSettings(Settings[^1]));
        }

        return addedQualitySetting;
    }
#endif

    [SerializeField] private List<BlurSettings> settings = new();
    public List<BlurSettings> Settings => settings;
    public int preview = -1;
}

[Serializable]
public class BlurSection
{
#if UNITY_EDITOR
    public static readonly string AlgorithmIdxFieldName = nameof(algorithmIdx);
#endif

    public BlurAlgorithm BlurAlgorithm
    {
        get
        {
            algorithmIdx = Mathf.Clamp(algorithmIdx, 0, BlurAlgorithm.All.Length - 1);
            return BlurAlgorithm.All[algorithmIdx];
        }
    }

    [Tooltip("The BlurAlgorithm to use. \"x2\" algorithms are separable and incur two drawcalls per iteration, and tend also work best with an even number of iterations. Due to the central limit theorem, which BlurAlgorithm is used matters less when firstBlurIterations become large.")]
    [SerializeField] private int algorithmIdx;
    [Tooltip("The number of times to apply the BlurAlgorithm.")]
    public int iterations;
    // Only affects algorithms that accept a variable number of samples (assumed to be any separable algorithm with distinct horizontal and vertical passes).
    // "Samples per side" means the total number of samples taken is twice this number, plus 1 for the center sample.
    [Tooltip("The number of samples to take on each side of the center sample for the horizontal pass. Eg. 3 -> 1 + (3 * 2) = 7 samples")]
    public int horizontalSamplesPerSide;
    [Tooltip("The number of samples to take on each side of the center sample for the vertical pass. Eg. 3 -> 1 + (3 * 2) = 7 samples")]
    public int verticalSamplesPerSide;
    [Tooltip("Controls the size of the sampling pattern. Oftentimes, x.5 will work better than x.0 thanks to better exploitation of bilinear filtering.")]
    public float sampleDistance;

    public BlurSection(BlurAlgorithm algorithm, int iterations, float sampleDistance, int horizontalSamplesPerSide = 2, int verticalSamplesPerSide = 2) 
        : this(Array.IndexOf(BlurAlgorithm.All, algorithm), iterations, sampleDistance, horizontalSamplesPerSide, verticalSamplesPerSide) {}

    public BlurSection(int algorithmIdx, int iterations, float sampleDistance, int horizontalSamplesPerSide = 2, int verticalSamplesPerSide = 2) 
        => (this.algorithmIdx, this.iterations, this.sampleDistance, this.horizontalSamplesPerSide, this.verticalSamplesPerSide) = (algorithmIdx, iterations, sampleDistance, horizontalSamplesPerSide, verticalSamplesPerSide);

    public void SetAlgorithm(BlurAlgorithm algorithm) => algorithmIdx = Array.IndexOf(BlurAlgorithm.All, algorithm);

    public (bool isSeparable, bool setSamplesPerSide, bool skip, int firstKernelIdx, int secondKernelIdx) GetSectionBehaviour()
    {
        if (BlurAlgorithm.SecondKernelIdx < 0)
            return (false, false, false, BlurAlgorithm.FirstKernelIdx, -1);
        if (verticalSamplesPerSide <= 0)
            return (false, true, horizontalSamplesPerSide <= 0, BlurAlgorithm.FirstKernelIdx, -1);
        if (horizontalSamplesPerSide <= 0)
            return (false, true, false, BlurAlgorithm.SecondKernelIdx, -1);

        return (true, true, false, BlurAlgorithm.FirstKernelIdx, BlurAlgorithm.SecondKernelIdx);
    }
}

[Serializable]
public class BlurSettings
{
    public List<BlurSection> downscaleSections = new()
    {
        new BlurSection(BlurAlgorithm.Tap5Star, 2, 1.5f)
    };
    public List<BlurSection> blurSections = new()
    {
        new BlurSection(BlurAlgorithm.Tap4Cross, 4, 1.5f)
    };

    [Tooltip("VERY IMPORTANT: The reference resolution controls how blur areas are initially resized and allows blurs to appear consistent across display resolutions. This has a *very large* effect on performance. \"1080\" means that, for any resolution below 1080p the blur will be upscaled to the size it would be on a 1080p display, and vice versa. It's good practice to set this to the lowest common resolution you expect to be used, but setting it too low can introduce temporal noise. You should probably never feel the need for a value higher than 2160. Setting to 0 will disable rescaling entirely.")]
    public int referenceResolution = 1080;
    [Tooltip("If enabled, applies a 7-tap hexagonal filter when resampling to the reference resolution, as opposed to simple bilinear. Does nothing when the reference resolution is the same as the display resolution. When the reference resolution is set very low, this setting can greatly reduce temporal noise. When the reference resolution is relatively high however, the expectation is that the difference will not be noticeable. So, somewhat counter-intuitively, it makes the most sense to use high quality resampling primarily when optimizing for minimal performance cost, since it lets you push the reference resolution significantly lower than you might otherwise be comfortable with given the tradeoff to temporal stability.")]
    public bool hqResample;
    [Tooltip("How much further the sample downsampleDistance grows with each iteration of the blur effect.")]
    [Range(0, 16)] public float blurAdditionalDistancePerIteration = 1f;
    [Tooltip("How much dithering to apply to the blur. This can smooth out banding when the blur is especially strong, or when using low color depth.")]
    [Range(0, 5)] public float ditherStrength = 0.25f;
    [Tooltip("Adjust brightness of the blur effect.")]
    [Range(-1, 1)] public float brightness;
    [Tooltip("Adjust contrast of the blur effect.")]
    [Range(-1, 1)] public float contrast;
    [Tooltip("Adjust to make the output desaturated or vivid.")]
    [Range(-1, 1)] public float vibrancy;
    [Tooltip("Tints the final output. The alpha channel controls the strength of the tint.")]
    public Color tint = Color.clear;

    public BlurSettings() { }
    public BlurSettings(BlurSettings toClone) => CopySettings(toClone);

    public void CopySettings(BlurSettings from)
    {
        referenceResolution = from.referenceResolution;
        hqResample = from.hqResample;
        downscaleSections = new List<BlurSection>(from.downscaleSections);
        blurSections = new List<BlurSection>(from.blurSections);
        blurAdditionalDistancePerIteration = from.blurAdditionalDistancePerIteration;
        ditherStrength = from.ditherStrength;
        vibrancy = from.vibrancy;
        brightness = from.brightness;
        contrast = from.contrast;
        tint = from.tint;
    }
}
}
