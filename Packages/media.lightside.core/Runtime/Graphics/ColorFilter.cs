using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// A colour adjustment expressed as a <see cref="ColorMatrix"/> — the shared filter vocabulary of
    /// every LightSide paint consumer (text filter modifiers, shape filter layers). A filter defines
    /// only what the adjustment is; how far it is applied belongs to the consumer, which fades the
    /// matrix toward identity, so no filter carries a strength of its own. Subclass and implement
    /// <see cref="ToMatrix"/> to add a custom named filter; instances are plain serialized data —
    /// after mutating one at runtime, dirty its owner (the modifier or the shape) to re-render.
    /// </summary>
    [Serializable]
    [TypeMenuSuffix("Filter")]
    public abstract class ColorFilter
    {
        /// <summary>The transform this filter stands for, at its authored settings.</summary>
        public abstract ColorMatrix ToMatrix();

        /// <summary>The filter's kind — its type name without the <c>Filter</c> suffix.</summary>
        public override string ToString()
        {
            const string suffix = "Filter";
            var name = GetType().Name;
            return name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal)
                ? name[..^suffix.Length]
                : name;
        }

        private protected static ColorMatrix LumaProjection => new(
            new Vector4(ColorMatrix.LumaR, ColorMatrix.LumaG, ColorMatrix.LumaB, 0f),
            new Vector4(ColorMatrix.LumaR, ColorMatrix.LumaG, ColorMatrix.LumaB, 0f),
            new Vector4(ColorMatrix.LumaR, ColorMatrix.LumaG, ColorMatrix.LumaB, 0f));

        private protected static ColorMatrix Saturation(float s)
            => ColorMatrix.Lerp(LumaProjection, ColorMatrix.Identity, s);
    }

    /// <summary>Replaces colour with its luminance — full desaturation to grey.</summary>
    [Serializable]
    [TypeDescription("Desaturates fully to grey.")]
    public sealed class GrayscaleFilter : ColorFilter
    {
        public override ColorMatrix ToMatrix() => LumaProjection;
    }

    /// <summary>Scales colour saturation: 0 is grey, 1 leaves colour untouched, above 1 over-saturates.</summary>
    [Serializable]
    [TypeDescription("Scales saturation; 0 is grey, above 1 over-saturates.")]
    public sealed class SaturationFilter : ColorFilter
    {
        [SerializeField, Range(0f, 3f)] private float amount = 1f;

        /// <summary>Saturation multiplier: 0 grey, 1 unchanged, above 1 boosted.</summary>
        public float Amount
        {
            get => amount;
            set => amount = value;
        }

        public override ColorMatrix ToMatrix() => Saturation(Mathf.Max(0f, amount));
    }

    /// <summary>Rotates every hue around the colour wheel by an angle in degrees.</summary>
    [Serializable]
    [TypeDescription("Rotates every hue by an angle in degrees.")]
    public sealed class HueRotateFilter : ColorFilter
    {
        [SerializeField, Range(-180f, 180f)] private float degrees;

        /// <summary>Rotation angle in degrees; 0 leaves colour untouched.</summary>
        public float Degrees
        {
            get => degrees;
            set => degrees = value;
        }

        public override ColorMatrix ToMatrix()
        {
            var radians = degrees * Mathf.Deg2Rad;
            var c = Mathf.Cos(radians);
            var s = Mathf.Sin(radians);
            const float lr = ColorMatrix.LumaR, lg = ColorMatrix.LumaG, lb = ColorMatrix.LumaB;
            return new ColorMatrix(
                new Vector4(lr + c * (1f - lr) - s * lr, lg - c * lg - s * lg, lb - c * lb + s * (1f - lb), 0f),
                new Vector4(lr - c * lr + s * 0.143f, lg + c * (1f - lg) + s * 0.140f, lb - c * lb - s * 0.283f, 0f),
                new Vector4(lr - c * lr - s * (1f - lr), lg - c * lg + s * lg, lb + c * (1f - lb) + s * lb, 0f));
        }
    }

    /// <summary>Multiplies colour by a gain: 0 is black, 1 leaves colour untouched, above 1 brightens.</summary>
    [Serializable]
    [TypeDescription("Multiplies brightness; 0 is black, above 1 brightens.")]
    public sealed class BrightnessFilter : ColorFilter
    {
        [SerializeField, Range(0f, 3f)] private float amount = 1f;

        /// <summary>Brightness multiplier: 0 black, 1 unchanged.</summary>
        public float Amount
        {
            get => amount;
            set => amount = value;
        }

        public override ColorMatrix ToMatrix()
        {
            var k = Mathf.Max(0f, amount);
            return new ColorMatrix(
                new Vector4(k, 0f, 0f, 0f),
                new Vector4(0f, k, 0f, 0f),
                new Vector4(0f, 0f, k, 0f));
        }
    }

    /// <summary>Scales contrast around mid-grey: 0 is flat grey, 1 leaves colour untouched, above 1 steepens.</summary>
    [Serializable]
    [TypeDescription("Scales contrast around mid-grey; 0 is flat grey.")]
    public sealed class ContrastFilter : ColorFilter
    {
        [SerializeField, Range(0f, 3f)] private float amount = 1f;

        /// <summary>Contrast multiplier: 0 flat mid-grey, 1 unchanged.</summary>
        public float Amount
        {
            get => amount;
            set => amount = value;
        }

        public override ColorMatrix ToMatrix()
        {
            var k = Mathf.Max(0f, amount);
            var offset = 0.5f * (1f - k);
            return new ColorMatrix(
                new Vector4(k, 0f, 0f, offset),
                new Vector4(0f, k, 0f, offset),
                new Vector4(0f, 0f, k, offset));
        }
    }

    /// <summary>Photographic exposure in stops: each stop doubles or halves the light; 0 leaves colour untouched.</summary>
    [Serializable]
    [TypeDescription("Photographic exposure in stops; each stop doubles the light.")]
    public sealed class ExposureFilter : ColorFilter
    {
        [SerializeField, Range(-4f, 4f)] private float stops;

        /// <summary>Exposure in stops: +1 doubles, −1 halves, 0 unchanged.</summary>
        public float Stops
        {
            get => stops;
            set => stops = value;
        }

        public override ColorMatrix ToMatrix()
        {
            var k = Mathf.Pow(2f, stops);
            return new ColorMatrix(
                new Vector4(k, 0f, 0f, 0f),
                new Vector4(0f, k, 0f, 0f),
                new Vector4(0f, 0f, k, 0f));
        }
    }

    /// <summary>Turns colour into its negative.</summary>
    [Serializable]
    [TypeDescription("Inverts colour to its negative.")]
    public sealed class InvertFilter : ColorFilter
    {
        public override ColorMatrix ToMatrix() => new(
            new Vector4(-1f, 0f, 0f, 1f),
            new Vector4(0f, -1f, 0f, 1f),
            new Vector4(0f, 0f, -1f, 1f));
    }

    /// <summary>Shifts colour to the warm brown of aged photographs.</summary>
    [Serializable]
    [TypeDescription("Warm aged-photo toning.")]
    public sealed class SepiaFilter : ColorFilter
    {
        public override ColorMatrix ToMatrix() => new(
            new Vector4(0.393f, 0.769f, 0.189f, 0f),
            new Vector4(0.349f, 0.686f, 0.168f, 0f),
            new Vector4(0.272f, 0.534f, 0.131f, 0f));
    }

    /// <summary>Multiplies colour by a tint colour.</summary>
    [Serializable]
    [TypeDescription("Multiplies colour by a tint colour.")]
    public sealed class TintFilter : ColorFilter
    {
        [SerializeField] private Color color = Color.white;

        /// <summary>The colour multiplied into the input.</summary>
        public Color Color
        {
            get => color;
            set => color = value;
        }

        public override ColorMatrix ToMatrix() => new(
            new Vector4(color.r, 0f, 0f, 0f),
            new Vector4(0f, color.g, 0f, 0f),
            new Vector4(0f, 0f, color.b, 0f));
    }

    /// <summary>
    /// A user-authored <see cref="ColorMatrix"/> — the raw 3×4 affine transform: channel mixing,
    /// ported filter constants, anything the named filters do not cover.
    /// </summary>
    [Serializable]
    [TypeDescription("A raw 3×4 colour matrix — channel mixing and custom filters.")]
    public sealed class ColorMatrixFilter : ColorFilter
    {
        [SerializeField] private ColorMatrix matrix = ColorMatrix.Identity;

        /// <summary>The authored transform.</summary>
        public ColorMatrix Matrix
        {
            get => matrix;
            set => matrix = value;
        }

        public override ColorMatrix ToMatrix() => matrix;
    }
}
