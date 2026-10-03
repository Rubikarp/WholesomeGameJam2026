using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[RequireComponent(typeof(IBlur))]
public class DemoBlurController : MonoBehaviour
{
    private IBlur blur;

    private void Awake() => blur = GetComponent<IBlur>();
    private void OnTransformParentChanged() => blur = GetComponent<IBlur>();
#if UNITY_EDITOR
    private void OnValidate() => blur = GetComponent<IBlur>();
#endif
    public void SetReferenceResolution(int referenceRes) => blur.Common.ActiveSettings.referenceResolution = referenceRes;
    public void SetBlurStrength(float strength) => blur.Common.blurStrength = strength;
    public void SetBlurDownScale(float downScale) => SetBlurDownScale((int)downScale);
    public void SetBlurDownScale(int downScale) => blur.Common.ActiveSettings.downscaleSections.ForEach(x => x.iterations = downScale);
    public void SetDownScaleDist(float downscaleDist) => blur.Common.ActiveSettings.downscaleSections.ForEach(x => x.sampleDistance = downscaleDist);
    public void SetBlurIterations(float iterations) => SetBlurIterations((int)iterations);
    public void SetBlurIterations(int iterations) => blur.Common.ActiveSettings.blurSections.ForEach(x => x.iterations = iterations);
    public void SetBlurSamplePointDistance (float samplePointDist) => blur.Common.ActiveSettings.blurSections.ForEach(x => x.sampleDistance = samplePointDist);
    public void SetBlurAdditionalSamplePointDistancePerIteration (float samplePointDist) => blur.Common.ActiveSettings.blurAdditionalDistancePerIteration = samplePointDist;
    public void SetDitherStrength (float ditherStrength) => blur.Common.ActiveSettings.ditherStrength = ditherStrength;
    public void SetVibrancy (float vibrancy) => blur.Common.ActiveSettings.vibrancy = vibrancy;
    public void SetTint (Color tint) => blur.Common.ActiveSettings.tint = tint;
}
}
