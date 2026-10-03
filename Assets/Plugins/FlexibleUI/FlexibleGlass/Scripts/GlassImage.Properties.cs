using System.Collections.Generic;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
public partial class GlassImage
{
    private GlassShapeSettings ShapeData => shape ??= new GlassShapeSettings();
    private GlassAppearance AppearanceData => appearance ??= new GlassAppearance();

    public GlassImageShapeType ShapeType
    {
        get => shapeType;
        set => SetGlassValue(ref shapeType, value);
    }

    public float CanonicalCornerRadius
    {
        get => canonicalCornerRadius;
        set => SetGlassValue(ref canonicalCornerRadius, Mathf.Max(0f, value));
    }

    public float ShapeExponent
    {
        get => shapeExponent;
        set => SetGlassValue(ref shapeExponent, Mathf.Clamp(value, 2f, 16f));
    }

    public float Roundness
    {
        get => Mathf.InverseLerp(16f, 2f, shapeExponent);
        set => SetGlassValue(ref shapeExponent, Mathf.Lerp(16f, 2f, Mathf.Clamp01(value)));
    }

    public float AlphaThreshold
    {
        get => alphaThreshold;
        set => SetGlassValue(ref alphaThreshold, GlassMath.ClampAlphaThreshold(value));
    }

    public float SurfaceSmoothness
    {
        get => surfaceSmoothness;
        set => SetGlassValue(ref surfaceSmoothness, Mathf.Clamp(value, 0.01f, 5f));
    }

    public GlassSurfaceSmoothnessMode SurfaceSmoothnessMode
    {
        get => surfaceSmoothnessMode;
        set => SetGlassValue(ref surfaceSmoothnessMode, value);
    }

    public float LensDepth
    {
        get => refractionStrength;
        set => SetGlassValue(ref refractionStrength, Mathf.Clamp(value, 0f, MaxLensDepth));
    }

    public float RefractiveIndex
    {
        get => refractiveIndex;
        set => SetGlassValue(ref refractiveIndex, Mathf.Clamp(value, 1f, 2.5f));
    }

    public float AbbeNumber
    {
        get => abbeNumber;
        set => SetGlassValue(ref abbeNumber, Mathf.Clamp(value, GlassMath.MinimumAbbeNumber, GlassMath.MaximumAbbeNumber));
    }

    public bool NormalizeCorners
    {
        get => ShapeData.normalizeCorners;
        set => SetGlassValue(ref ShapeData.normalizeCorners, value);
    }

    public bool Squircle
    {
        get => ShapeData.squircle;
        set => SetGlassValue(ref ShapeData.squircle, value);
    }

    public Vector4 CornerRadii
    {
        get => ShapeData.cornerRadii;
        set => SetGlassValue(ref ShapeData.cornerRadii, Vector4.Max(value, Vector4.zero));
    }

    public Vector4 CornerRoundness
    {
        get => ShapeData.cornerRoundness;
        set => SetGlassValue(ref ShapeData.cornerRoundness, value);
    }

    public float ColorMix
    {
        get => AppearanceData.colorMix;
        set => SetGlassValue(ref AppearanceData.colorMix, Mathf.Clamp01(value));
    }

    public float Magnification
    {
        get => AppearanceData.magnification;
        set => SetGlassValue(ref AppearanceData.magnification, Mathf.Clamp(value, GlassAppearance.MinimumMagnification, GlassAppearance.MaximumMagnification));
    }

    public float Transmission
    {
        get => AppearanceData.transmission;
        set => SetGlassValue(ref AppearanceData.transmission, Mathf.Clamp(value, 0f, 2f));
    }

    public GlassThicknessUnits OpticalLipUnits
    {
        get => AppearanceData.thicknessUnits;
        set => SetGlassValue(ref AppearanceData.thicknessUnits, value);
    }

    public float OpticalLip
    {
        get => AppearanceData.thickness;
        set => SetGlassValue(ref AppearanceData.thickness, Mathf.Max(0f, value));
    }

    public GlassLipLightUnits LipLightUnits
    {
        get => AppearanceData.lipLightUnits;
        set => SetGlassValue(ref AppearanceData.lipLightUnits, value);
    }

    public float InnerLipLightThickness
    {
        get => AppearanceData.innerEdgeLightThickness;
        set => SetGlassValue(ref AppearanceData.innerEdgeLightThickness, Mathf.Max(0f, value));
    }

    public float OuterLipLightThickness
    {
        get => AppearanceData.outerEdgeLightThickness;
        set => SetGlassValue(ref AppearanceData.outerEdgeLightThickness, Mathf.Max(0f, value));
    }

    public float InnerEdgeLightThickness { get => InnerLipLightThickness; set => InnerLipLightThickness = value; }

    public float OuterEdgeLightThickness { get => OuterLipLightThickness; set => OuterLipLightThickness = value; }

    public Color ShadowColor
    {
        get => AppearanceData.shadowColor;
        set => SetGlassValue(ref AppearanceData.shadowColor, value);
    }

    public float ShadowSize
    {
        get => AppearanceData.shadowSize;
        set => SetGlassValue(ref AppearanceData.shadowSize, Mathf.Clamp(value, 0f, 32f));
    }

    public Vector2 ShadowOffset
    {
        get => AppearanceData.shadowOffset;
        set => SetGlassValue(ref AppearanceData.shadowOffset, value);
    }

    public GlassReferenceSource ReferenceSource
    {
        get => referenceSource;
        set
        {
            if (SetGlassValue(ref referenceSource, value))
                RefreshRegistration();
        }
    }

    public Camera CameraReference
    {
        get => cameraReference;
        set
        {
            if (SetGlassValue(ref cameraReference, value))
                RefreshRegistration();
        }
    }

    public int FeatureNumber
    {
        get => featureNumber;
        set
        {
            if (SetGlassValue(ref featureNumber, Mathf.Max(0, value)))
                RefreshRegistration();
        }
    }

    private bool SetGlassValue<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        SetVerticesDirty();
        return true;
    }
}
}
