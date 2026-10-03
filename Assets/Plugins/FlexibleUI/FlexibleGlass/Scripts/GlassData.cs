using System;
using System.Runtime.InteropServices;
using UnityEngine;

#if UNITY_6000_4_OR_NEWER
using ObjectIdentifier = UnityEngine.EntityId;
#else
using ObjectIdentifier = System.Int32;
#endif

namespace JeffGrawAssets.FlexibleUI
{
public enum GlassSdfOperation
{
    Add,
    Subtract
}

public enum GlassShapeType
{
    Canonical = 0,
    PerCorner = 4
}

public enum GlassImageShapeType
{
    Canonical,
    PerCorner,
    Sprite
}

public enum GlassSdfSource
{
    Shape,
    SpriteAlpha
}

public enum GlassThicknessUnits
{
    AbsoluteCanvasUnits,
    PercentOfSmallerSide,
    PercentOfLargerSide
}

public enum GlassLipLightUnits
{
    [InspectorName("Percent of Optical Lip")] PercentOfOpticalLip,
    [InspectorName("Canvas Units (Absolute)")] AbsoluteCanvasUnits
}

public enum GlassReferenceSource
{
    Self,
    ReferenceProvider
}

public enum GlassSurfaceSmoothnessMode
{
    Custom,
    Auto
}

[Serializable]
public class GlassShapeSettings
{
    [Tooltip("Scales all corner radii proportionally when opposing corners would overlap.")]
    public bool normalizeCorners = true;
    [Tooltip("Interprets the corner shape values as Smoothing instead of Concavity.")]
    public bool squircle = true;
    [Tooltip("Top-left, top-right, bottom-left, and bottom-right chamfer in Canvas units.")]
    public Vector4 cornerRadii = Vector4.one * 28f;
    [Tooltip("Stored corner shape. The inspector presents this as Smoothing from 0 to 1, or Concavity from 0 (rounded) through 1 (flat) to 2 (concave).")]
    public Vector4 cornerRoundness = Vector4.one * 0.4f;

    public Vector4 GetCornerRadii(Vector2 size) => GetCornerRadii(size, squircle);

    public Vector4 GetCornerRadii(Vector2 size, bool useSquircle)
    {
        var radii = normalizeCorners ? GlassMath.NormalizeCornerRadii(cornerRadii, size) : Vector4.Max(cornerRadii, Vector4.zero);
        radii = GlassMath.AdjustCornerRadii(radii, cornerRoundness, useSquircle);
        return normalizeCorners && useSquircle ? GlassMath.NormalizeSquircleCornerRadii(radii, cornerRoundness, size) : radii;
    }
}

[Serializable]
public class GlassAppearance
{
    public const float MinimumMagnification = 1f;
    public const float MaximumMagnification = 4f;
    public const float ShadowFalloffExtent = 6f;
    public const float ShadowAntialiasPadding = 2f;

    [Tooltip("Glass color. Alpha controls opacity.")]
    public Color color = Color.white;
    [Tooltip("How much Glass Color is mixed into the captured background.")]
    [Range(0f, 1f)] public float colorMix;
    [Tooltip("Scales the captured background around the center of the glass without changing its silhouette. 1 leaves the background unchanged.")]
    [Range(MinimumMagnification, MaximumMagnification)] public float magnification = 1f;
    [Tooltip("Amount of background light transmitted through the glass.")]
    [Range(0f, 2f)] public float transmission = 1f;
    [Tooltip("How Optical Lip is measured before it is clamped against both element axes.")]
    public GlassThicknessUnits thicknessUnits;
    [InspectorName("Optical Lip")]
    [Tooltip("Width of the rounded optical edge. Zero produces a flat pane.")]
    [Min(0f)] public float thickness = 14f;
    [Tooltip("Units shared by the inner and outer lip-light widths.")]
    public GlassLipLightUnits lipLightUnits;
    [InspectorName("Inner Lip Light")]
    [Tooltip("Inner lip-light width. Stored as a 0-1 fraction in percentage mode, or Canvas units in absolute mode. Rendering clamps it to the Optical Lip.")]
    [Min(0f)] public float innerEdgeLightThickness;
    [InspectorName("Outer Lip Light")]
    [Tooltip("Outer lip-light width. Stored as a 0-1 fraction in percentage mode, or Canvas units in absolute mode. Rendering clamps it to the Optical Lip.")]
    [Min(0f)] public float outerEdgeLightThickness = 0.1f;
    [Tooltip("Drop-shadow color. Alpha controls shadow opacity.")]
    public Color shadowColor = new(0f, 0f, 0f, 36f / 255f);
    [Tooltip("Drop-shadow softness in pixels.")]
    [Range(0f, 32f)] public float shadowSize = 14f;
    [Tooltip("Drop-shadow offset in screen pixels.")]
    public Vector2 shadowOffset = Vector2.zero;

    public float GetInnerEdgeLightExtent() => Mathf.Clamp01(innerEdgeLightThickness);

    public float GetOuterEdgeLightExtent() => Mathf.Clamp01(outerEdgeLightThickness);

    public Vector2 GetLipLightExtents(Vector2 size)
    {
        var opticalLip = GetResolvedThickness(size);
        if (!(opticalLip > 0f))
            return Vector2.zero;
        return new Vector2(ResolveLipLightExtent(innerEdgeLightThickness, opticalLip), ResolveLipLightExtent(outerEdgeLightThickness, opticalLip));
    }

    private float ResolveLipLightExtent(float width, float opticalLip)
    {
        if (!(width > 0f))
            return 0f;
        return Mathf.Clamp01(lipLightUnits == GlassLipLightUnits.AbsoluteCanvasUnits ? width / opticalLip : width);
    }

    public float GetMagnification() => Mathf.Clamp(magnification, MinimumMagnification, MaximumMagnification);

    public float GetResolvedThickness(Vector2 size)
    {
        size = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
        var value = Mathf.Max(0f, thickness);
        if (thicknessUnits == GlassThicknessUnits.PercentOfSmallerSide)
            value *= Mathf.Min(size.x, size.y) * 0.01f;
        else if (thicknessUnits == GlassThicknessUnits.PercentOfLargerSide)
            value *= Mathf.Max(size.x, size.y) * 0.01f;
        return Mathf.Min(value, Mathf.Min(size.x, size.y) * 0.5f);
    }

    public float GetShadowDistanceSupport() => Mathf.Max(shadowSize, 0.5f) * ShadowFalloffExtent;

    public bool HasVisibleShadow() => shadowColor.a > 0f && (shadowSize > 0f || shadowOffset.sqrMagnitude > 0f);

}

internal readonly struct GlassScreenProjection
{
    private readonly Camera camera;
    private readonly Matrix4x4 view, viewProjection;

    public GlassScreenProjection(Camera camera, Matrix4x4 view, Matrix4x4 projection) =>
        (this.camera, this.view, viewProjection) = (camera, view, projection * view);

    public Vector3 WorldToScreenPoint(Camera target, Vector3 position)
    {
        if (!camera || target != camera)
            return target.WorldToScreenPoint(position);

        var clip = viewProjection * new Vector4(position.x, position.y, position.z, 1f);
        var viewport = camera.pixelRect;
        var inverseW = Mathf.Abs(clip.w) > 1e-6f ? 1f / clip.w : 0f;
        return new Vector3(viewport.x + (clip.x * inverseW * 0.5f + 0.5f) * viewport.width,
            viewport.y + (clip.y * inverseW * 0.5f + 0.5f) * viewport.height,
            -view.MultiplyPoint(position).z);
    }
}

public static class GlassMath
{
    public const float MinimumAlphaThreshold = 0.01f;
    public const float MaximumAlphaThreshold = 0.99f;
    public const float MinimumAbbeNumber = 0.1f;
    public const float MaximumAbbeNumber = 64f;

    public static float ClampAlphaThreshold(float value) => Mathf.Clamp(value, MinimumAlphaThreshold, MaximumAlphaThreshold);

    public static float GetPhysicalDispersionCoefficient(float refractiveIndex, float abbeNumber)
    {
        const float blueInverseSquare = 1000000f / (486.1327f * 486.1327f);
        const float redInverseSquare = 1000000f / (656.2725f * 656.2725f);
        return (Mathf.Max(refractiveIndex, 1f) - 1f) / Mathf.Clamp(abbeNumber, MinimumAbbeNumber, MaximumAbbeNumber) / (blueInverseSquare - redInverseSquare);
    }

    public static float GetRefractiveIndex(float refractiveIndex, float abbeNumber, float wavelengthNanometers)
    {
        const float blueWavelength = 486.1327f;
        const float referenceWavelength = 587.5618f;
        const float redWavelength = 656.2725f;
        refractiveIndex = Mathf.Max(1f, refractiveIndex);
        var principalDispersion = (refractiveIndex - 1f) / Mathf.Clamp(abbeNumber, MinimumAbbeNumber, MaximumAbbeNumber);
        var blueInverseSquare = 1000000f / (blueWavelength * blueWavelength);
        var redInverseSquare = 1000000f / (redWavelength * redWavelength);
        var coefficient = principalDispersion / (blueInverseSquare - redInverseSquare);
        var constant = refractiveIndex - coefficient * 1000000f / (referenceWavelength * referenceWavelength);
        var wavelength = Mathf.Clamp(wavelengthNanometers, blueWavelength, redWavelength);
        var dispersedIndex = constant + coefficient * 1000000f / (wavelength * wavelength);
        return Mathf.Max(1f, dispersedIndex);
    }

    internal static float PackColor(Color value)
    {
        var color = (Color32)value;
        return PackBytes(color.r, color.g, color.b, color.a);
    }

    internal static float PackBytes(byte first, byte second, byte third, byte fourth)
    {
        var packed = (uint)(first << 24 | second << 16 | third << 8 | fourth);
        var value = MemoryMarshal.Cast<uint, float>(MemoryMarshal.CreateSpan(ref packed, 1))[0];
        if (MemoryMarshal.Cast<float, uint>(MemoryMarshal.CreateSpan(ref value, 1))[0] == packed)
            return value;

        packed ^= 1u << 24;
        return MemoryMarshal.Cast<uint, float>(MemoryMarshal.CreateSpan(ref packed, 1))[0];
    }

    internal static float PackRadii(float first, float second)
    {
        var packed = (uint)Mathf.RoundToInt(Mathf.Clamp(first, 0f, 1023.75f) * 4f) << 20;
        packed |= (uint)Mathf.RoundToInt(Mathf.Clamp(second, 0f, 1023.75f) * 4f) << 8;
        var value = MemoryMarshal.Cast<uint, float>(MemoryMarshal.CreateSpan(ref packed, 1))[0];
        if (MemoryMarshal.Cast<float, uint>(MemoryMarshal.CreateSpan(ref value, 1))[0] == packed)
            return value;

        packed ^= 1u << 23;
        return MemoryMarshal.Cast<uint, float>(MemoryMarshal.CreateSpan(ref packed, 1))[0];
    }

    public static float UnionDistance(float current, float element, float blend = 0f)
    {
        return float.IsPositiveInfinity(current) ? element : SmoothMinimum(current, element, Mathf.Max(0f, blend));
    }

    public static float ComposeDistance(float additive, float cutout, float blend = 0f)
    {
        if (float.IsPositiveInfinity(additive) || float.IsPositiveInfinity(cutout))
            return additive;
        return SmoothMaximum(additive, -cutout, Mathf.Max(0f, blend));
    }

    public static float SmoothMinimum(float left, float right, float blend)
    {
        blend = Mathf.Max(0f, blend);
        if (blend <= 0f)
            return Mathf.Min(left, right);

        var weight = Mathf.Clamp01(0.5f + 0.5f * (left - right) / blend);
        return Mathf.Lerp(left, right, weight) - blend * weight * (1f - weight);
    }

    public static float SmoothMaximum(float left, float right, float blend) => -SmoothMinimum(-left, -right, blend);

    public static Vector4 NormalizeCornerRadii(Vector4 radii, Vector2 size)
    {
        radii = Vector4.Max(radii, Vector4.zero);
        size = Vector2.Max(size, Vector2.zero);

        var scale = 1f;
        scale = FitPair(scale, size.x, radii.x + radii.y);
        scale = FitPair(scale, size.x, radii.z + radii.w);
        scale = FitPair(scale, size.y, radii.x + radii.z);
        scale = FitPair(scale, size.y, radii.y + radii.w);
        return radii * scale;

        static float FitPair(float current, float available, float requested) => requested > 0f ? Mathf.Min(current, available / requested) : current;
    }

    public static bool TryBuildScreenToUv(Vector2 bottomLeft, Vector2 bottomRight, Vector2 topRight, Vector2 topLeft, out Vector4 row0, out Vector4 row1, out Vector4 row2)
    {
        var dx1 = bottomRight.x - topRight.x;
        var dx2 = topLeft.x - topRight.x;
        var dx3 = bottomLeft.x - bottomRight.x + topRight.x - topLeft.x;
        var dy1 = bottomRight.y - topRight.y;
        var dy2 = topLeft.y - topRight.y;
        var dy3 = bottomLeft.y - bottomRight.y + topRight.y - topLeft.y;

        float g, h;
        var denominator = dx1 * dy2 - dx2 * dy1;
        if (Mathf.Abs(dx3) < 1e-5f && Mathf.Abs(dy3) < 1e-5f)
        {
            g = h = 0f;
        }
        else
        {
            if (Mathf.Abs(denominator) < 1e-7f)
            {
                row0 = row1 = row2 = Vector4.zero;
                return false;
            }

            g = (dx3 * dy2 - dx2 * dy3) / denominator;
            h = (dx1 * dy3 - dx3 * dy1) / denominator;
        }

        var a = bottomRight.x - bottomLeft.x + g * bottomRight.x;
        var b = topLeft.x - bottomLeft.x + h * topLeft.x;
        var c = bottomLeft.x;
        var d = bottomRight.y - bottomLeft.y + g * bottomRight.y;
        var e = topLeft.y - bottomLeft.y + h * topLeft.y;
        var f = bottomLeft.y;

        var i00 = e - f * h;
        var i01 = c * h - b;
        var i02 = b * f - c * e;
        var i10 = f * g - d;
        var i11 = a - c * g;
        var i12 = c * d - a * f;
        var i20 = d * h - e * g;
        var i21 = b * g - a * h;
        var i22 = a * e - b * d;
        var determinant = a * i00 + b * i10 + c * i20;
        if (Mathf.Abs(determinant) < 1e-7f)
        {
            row0 = row1 = row2 = Vector4.zero;
            return false;
        }

        var inverseDeterminant = 1f / determinant;
        row0 = new Vector4(i00, i01, i02, 0f) * inverseDeterminant;
        row1 = new Vector4(i10, i11, i12, 0f) * inverseDeterminant;
        row2 = new Vector4(i20, i21, i22, 0f) * inverseDeterminant;
        return true;
    }

    public static Vector2 TransformScreenToUv(Vector2 screenPosition, Vector4 row0, Vector4 row1, Vector4 row2)
    {
        var point = new Vector3(screenPosition.x, screenPosition.y, 1f);
        var denominator = Vector3.Dot((Vector3)row2, point);
        if (Mathf.Abs(denominator) < 1e-7f)
            return new Vector2(float.PositiveInfinity, float.PositiveInfinity);

        return new Vector2(Vector3.Dot((Vector3)row0, point), Vector3.Dot((Vector3)row1, point)) / denominator;
    }

    internal static float ResolveCanonicalRadius(float radius, Vector2 size)
        => Mathf.Clamp(radius, 0f, Mathf.Max(0f, Mathf.Min(size.x, size.y) * 0.5f));

    public static float ResolveCanonicalSurfaceSmoothness(float opticalLip, float cornerRadius, float exponent, Vector2 size)
    {
        var radius = ResolveCanonicalRadius(cornerRadius, size);
        if (radius <= 1e-5f)
            return 5f;

        // Canonical corners are superellipses. Their peak curvature occurs on
        // the diagonal and increases sharply as the exponent squares the corner.
        // Exponent 2 is circular and therefore remains the baseline of 1.
        exponent = Mathf.Clamp(exponent, 2f, 16f);
        var peakCurvatureScale = (exponent - 1f) * Mathf.Pow(2f, 1f / exponent - 0.5f);
        return Mathf.Clamp(Mathf.Max(opticalLip, 0f) * peakCurvatureScale / radius, 0.01f, 5f);
    }

    internal static bool ContainsCanonical(Vector2 position, Vector2 size, float radius, float exponent)
    {
        var edge = Vector2.Min(position, size - position);
        if (edge.x < 0f || edge.y < 0f)
            return false;
        radius = ResolveCanonicalRadius(radius, size);
        if (radius <= 0f || edge.x >= radius || edge.y >= radius)
            return true;
        var corner = Vector2.one - edge / radius;
        exponent = Mathf.Clamp(exponent, 2f, 16f);
        return Mathf.Pow(corner.x, exponent) + Mathf.Pow(corner.y, exponent) <= 1f;
    }

    public static float RectDistance(Vector2 position, Vector2 size, Vector4 radii, Vector4 roundness, bool squircle)
    {
        var edge = new Vector4(position.x, position.y, size.x - position.x, size.y - position.y);
        var insideDistance = Mathf.Min(Mathf.Min(edge.x, edge.y), Mathf.Min(edge.z, edge.w));
        if (radii.x > 0f && edge.x < radii.x && edge.w < radii.x)
            insideDistance = Mathf.Min(insideDistance, CornerDistance(new Vector2(edge.x, edge.w), radii.x, roundness.x, squircle));
        if (radii.y > 0f && edge.z < radii.y && edge.w < radii.y)
            insideDistance = Mathf.Min(insideDistance, CornerDistance(new Vector2(edge.z, edge.w), radii.y, roundness.y, squircle));
        if (radii.z > 0f && edge.x < radii.z && edge.y < radii.z)
            insideDistance = Mathf.Min(insideDistance, CornerDistance(new Vector2(edge.x, edge.y), radii.z, roundness.z, squircle));
        if (radii.w > 0f && edge.z < radii.w && edge.y < radii.w)
            insideDistance = Mathf.Min(insideDistance, CornerDistance(new Vector2(edge.z, edge.y), radii.w, roundness.w, squircle));
        return -insideDistance;
    }

    private static float CornerDistance(Vector2 edgePair, float radius, float roundness, bool squircle)
    {
        roundness = Mathf.Clamp(roundness, -1f, 1f);
        if (squircle)
            return radius - LpLength(Vector2.one * radius - edgePair, Mathf.Lerp(2f, 10f, Mathf.Clamp01(roundness)));

        var curved = radius - Vector2.Distance(edgePair, Vector2.one * radius);
        var flat = 2f / 3f * (edgePair.x + edgePair.y) - radius / 3f;
        return Mathf.LerpUnclamped(curved, flat, 1f - roundness);
    }

    public static Vector4 AdjustCornerRadii(Vector4 radii, Vector4 roundness, bool squircle)
    {
        return new Vector4(
            AdjustCornerRadius(radii.x, roundness.x, squircle),
            AdjustCornerRadius(radii.y, roundness.y, squircle),
            AdjustCornerRadius(radii.z, roundness.z, squircle),
            AdjustCornerRadius(radii.w, roundness.w, squircle)
        );
    }

    public static Vector4 NormalizeSquircleCornerRadii(Vector4 radii, Vector4 roundness, Vector2 size)
    {
        var visibleRadii = new Vector4(
            GetSquircleVisibleRadius(radii.x, roundness.x),
            GetSquircleVisibleRadius(radii.y, roundness.y),
            GetSquircleVisibleRadius(radii.z, roundness.z),
            GetSquircleVisibleRadius(radii.w, roundness.w)
        );
        size = Vector2.Max(size, Vector2.zero);
        var scale = 1f;
        scale = FitPair(scale, size.x, visibleRadii.x + visibleRadii.y);
        scale = FitPair(scale, size.x, visibleRadii.z + visibleRadii.w);
        scale = FitPair(scale, size.y, visibleRadii.x + visibleRadii.z);
        scale = FitPair(scale, size.y, visibleRadii.y + visibleRadii.w);
        return radii * scale;

        static float FitPair(float current, float available, float requested) => requested > 0f ? Mathf.Min(current, available / requested) : current;
    }

    private static float AdjustCornerRadius(float radius, float roundness, bool squircle)
    {
        if (radius <= 0f)
            return 0f;

        if (squircle)
        {
            var exponent = Mathf.Lerp(2f, 10f, Mathf.Clamp01(roundness));
            var circleMid = 1f - Mathf.Sqrt(0.5f);
            var squircleMid = 1f - Mathf.Pow(2f, -1f / exponent);
            return radius * circleMid / squircleMid;
        }

        var concavity = 1f - Mathf.Clamp(roundness, -1f, 1f);
        var squared = concavity * concavity;
        var cubed = squared * concavity;
        var fourth = cubed * concavity;
        return radius * (1f + 1.708333f * concavity - 1.166667f * squared + 0.5416667f * cubed - 0.08333333f * fourth);
    }

    private static float GetSquircleVisibleRadius(float radius, float roundness)
    {
        if (radius <= 0f || roundness <= 0f)
            return radius;

        var exponent = Mathf.Lerp(2f, 10f, Mathf.Clamp01(roundness));
        var inset = Mathf.Clamp01(1f / radius);
        return radius * (1f - Mathf.Pow(1f - Mathf.Pow(1f - inset, exponent), 1f / exponent));
    }

    private static float LpLength(Vector2 value, float exponent)
    {
        value = new Vector2(Mathf.Abs(value.x), Mathf.Abs(value.y));
        return Mathf.Pow(Mathf.Pow(value.x, exponent) + Mathf.Pow(value.y, exponent), 1f / exponent);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct GlassElementGpu
{
    public const int Stride = 11 * 16;

    public Vector4 screenToUv0;
    public Vector4 screenToUv1;
    public Vector4 screenToUv2;
    public Vector4 sizeOperationShape;
    public Vector4 screenBounds;
    public Vector4 color;
    public Vector4 optics0;
    public Vector4 optics1;
    public Vector4 lighting;
    public Vector4 shadow;
    public Vector4 sdfData;
}

internal readonly struct GlassSdfDescriptor : IEquatable<GlassSdfDescriptor>
{
    public readonly GlassSdfSource source;
    public readonly int shapeType;
    public readonly Vector2 size;
    public readonly Vector2 padding;
    public readonly Vector4 cornerRadii;
    public readonly Vector4 cornerShape;
    public readonly Texture texture;
    public readonly ObjectIdentifier textureInstanceId;
    public readonly uint textureUpdateCount;
    public readonly Vector4 textureUv;
    public readonly Sprite sprite;
    public readonly ObjectIdentifier spriteInstanceId;
    public readonly bool packedSprite;
    public readonly float alphaThreshold;

    public GlassSdfDescriptor(GlassSdfSource source, int shapeType, Vector2 size, Vector2 padding, Vector4 cornerRadii, Vector4 cornerShape, Texture texture, Vector4 textureUv, float alphaThreshold, Sprite sprite = null)
    {
        this.source = source;
        this.shapeType = shapeType;
        this.size = size;
        padding = Vector2.Max(padding, Vector2.zero);
        var domainSize = size + 2f * padding;
        var squareExtent = Mathf.Max(domainSize.x, domainSize.y);
        this.padding = Vector2.Max(padding, (Vector2.one * squareExtent - size) * 0.5f);
        this.cornerRadii = cornerRadii;
        this.cornerShape = cornerShape;
        this.texture = texture;
#if UNITY_6000_4_OR_NEWER
        textureInstanceId = texture ? texture.GetEntityId() : default;
#else
        textureInstanceId = texture ? texture.GetInstanceID() : 0;
#endif
        textureUpdateCount = texture ? texture.updateCount : 0;
        this.textureUv = textureUv;
        this.sprite = sprite;
#if UNITY_6000_4_OR_NEWER
        spriteInstanceId = sprite ? sprite.GetEntityId() : default;
#else
        spriteInstanceId = sprite ? sprite.GetInstanceID() : 0;
#endif
        packedSprite = sprite && sprite.packed;
        this.alphaThreshold = GlassMath.ClampAlphaThreshold(alphaThreshold);
    }

    public bool Equals(GlassSdfDescriptor other) =>
        source == other.source && shapeType == other.shapeType && size == other.size && padding == other.padding &&
        cornerRadii == other.cornerRadii && cornerShape == other.cornerShape && textureInstanceId == other.textureInstanceId &&
        textureUpdateCount == other.textureUpdateCount && textureUv == other.textureUv && spriteInstanceId == other.spriteInstanceId &&
        packedSprite == other.packedSprite && alphaThreshold.Equals(other.alphaThreshold);

    public override bool Equals(object obj) => obj is GlassSdfDescriptor other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)source;
            hash = hash * 397 ^ shapeType;
            hash = hash * 397 ^ size.GetHashCode();
            hash = hash * 397 ^ padding.GetHashCode();
            hash = hash * 397 ^ cornerRadii.GetHashCode();
            hash = hash * 397 ^ cornerShape.GetHashCode();
            hash = hash * 397 ^ textureInstanceId.GetHashCode();
            hash = hash * 397 ^ (int)textureUpdateCount;
            hash = hash * 397 ^ textureUv.GetHashCode();
            hash = hash * 397 ^ spriteInstanceId.GetHashCode();
            hash = hash * 397 ^ packedSprite.GetHashCode();
            return hash * 397 ^ alphaThreshold.GetHashCode();
        }
    }
}
}
