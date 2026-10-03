using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Affine RGB colour transform: each output channel is a weighted mix of the input channels plus a
    /// constant (<c>out = dot(row.xyz, rgb) + row.w</c>). Alpha is never touched — in LightSide
    /// rendering alpha is coverage, and every filter distribution law relies on it passing through.
    /// Composes by <see cref="Multiply"/>; value-equal matrices are interchangeable (atlas keys,
    /// caches). Transform results clamp to [0, 1].
    /// </summary>
    [Serializable]
    public struct ColorMatrix : IEquatable<ColorMatrix>
    {
        /// <summary>Red output row: <c>r' = dot(r.xyz, rgb) + r.w</c>.</summary>
        public Vector4 r;

        /// <summary>Green output row: <c>g' = dot(g.xyz, rgb) + g.w</c>.</summary>
        public Vector4 g;

        /// <summary>Blue output row: <c>b' = dot(b.xyz, rgb) + b.w</c>.</summary>
        public Vector4 b;

        /// <summary>Rec. 709 luma weights, the greyscale projection every luminance-based filter shares.</summary>
        public const float LumaR = 0.2126f, LumaG = 0.7152f, LumaB = 0.0722f;

        /// <summary>The transform that leaves every colour unchanged.</summary>
        public static ColorMatrix Identity => new()
        {
            r = new Vector4(1f, 0f, 0f, 0f),
            g = new Vector4(0f, 1f, 0f, 0f),
            b = new Vector4(0f, 0f, 1f, 0f),
        };

        /// <summary>Whether this transform is exactly the identity, so consumers can skip it entirely.</summary>
        public bool IsIdentity =>
            r is { x: 1f, y: 0f, z: 0f, w: 0f } &&
            g is { x: 0f, y: 1f, z: 0f, w: 0f } &&
            b is { x: 0f, y: 0f, z: 1f, w: 0f };

        /// <summary>Creates a matrix from its three output rows.</summary>
        public ColorMatrix(Vector4 red, Vector4 green, Vector4 blue)
        {
            r = red;
            g = green;
            b = blue;
        }

        /// <summary>The composition applying <paramref name="inner"/> first, then <paramref name="outer"/>.</summary>
        public static ColorMatrix Multiply(in ColorMatrix outer, in ColorMatrix inner)
        {
            return new ColorMatrix(Row(outer.r, in inner), Row(outer.g, in inner), Row(outer.b, in inner));

            static Vector4 Row(Vector4 o, in ColorMatrix m) => new(
                o.x * m.r.x + o.y * m.g.x + o.z * m.b.x,
                o.x * m.r.y + o.y * m.g.y + o.z * m.b.y,
                o.x * m.r.z + o.y * m.g.z + o.z * m.b.z,
                o.x * m.r.w + o.y * m.g.w + o.z * m.b.w + o.w);
        }

        /// <summary>This transform preceded by a per-channel multiply, so <c>result(c) = this(c × tint)</c>. Alpha of <paramref name="tint"/> is ignored.</summary>
        public ColorMatrix Tinted(Color32 tint)
        {
            if (tint.r == byte.MaxValue && tint.g == byte.MaxValue && tint.b == byte.MaxValue)
                return this;
            float tr = tint.r / 255f, tg = tint.g / 255f, tb = tint.b / 255f;
            return new ColorMatrix(
                new Vector4(r.x * tr, r.y * tg, r.z * tb, r.w),
                new Vector4(g.x * tr, g.y * tg, g.z * tb, g.w),
                new Vector4(b.x * tr, b.y * tg, b.z * tb, b.w));
        }

        /// <summary>Componentwise blend between two matrices — exact parameter interpolation for every filter whose matrix is affine in its parameter.</summary>
        public static ColorMatrix Lerp(in ColorMatrix from, in ColorMatrix to, float t) => new(
            Vector4.LerpUnclamped(from.r, to.r, t),
            Vector4.LerpUnclamped(from.g, to.g, t),
            Vector4.LerpUnclamped(from.b, to.b, t));

        /// <summary>Transforms a straight (non-premultiplied) colour; RGB clamps to [0, 1], alpha passes through.</summary>
        public Color Transform(Color c) => new(
            Mathf.Clamp01(r.x * c.r + r.y * c.g + r.z * c.b + r.w),
            Mathf.Clamp01(g.x * c.r + g.y * c.g + g.z * c.b + g.w),
            Mathf.Clamp01(b.x * c.r + b.y * c.g + b.z * c.b + b.w),
            c.a);

        /// <summary>Transforms a straight (non-premultiplied) colour; RGB clamps to [0, 255], alpha passes through.</summary>
        public Color32 Transform(Color32 c)
        {
            float cr = c.r, cg = c.g, cb = c.b;
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(r.x * cr + r.y * cg + r.z * cb + r.w * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(g.x * cr + g.y * cg + g.z * cb + g.w * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(b.x * cr + b.y * cg + b.z * cb + b.w * 255f), 0, 255),
                c.a);
        }

        public bool Equals(ColorMatrix other) =>
            r.x == other.r.x && r.y == other.r.y && r.z == other.r.z && r.w == other.r.w &&
            g.x == other.g.x && g.y == other.g.y && g.z == other.g.z && g.w == other.g.w &&
            b.x == other.b.x && b.y == other.b.y && b.z == other.b.z && b.w == other.b.w;

        public override bool Equals(object obj) => obj is ColorMatrix m && Equals(m);

        public override int GetHashCode() => HashCode.Combine(r, g, b);
    }
}
