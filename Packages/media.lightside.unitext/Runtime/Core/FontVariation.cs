using System;
using System.Runtime.InteropServices;

namespace LightSide
{
    [Flags]
    internal enum AxisMask : byte
    {
        None = 0,
        Wght = 1 << 0,
        Wdth = 1 << 1,
        Ital = 1 << 2,
        Slnt = 1 << 3,
        Opsz = 1 << 4,
    }

    internal enum AxisValueMode : byte
    {
        Absolute,
        Percent,
        Delta,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AxisValue
    {
        public float value;
        public AxisValueMode mode;
    }

    /// <summary>
    /// A unique combination of axis overrides produced by one <c>&lt;var=...&gt;</c> tag.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VariationConfig : IEquatable<VariationConfig>
    {
        public AxisValue wght;
        public AxisValue wdth;
        public AxisValue ital;
        public AxisValue slnt;
        public AxisValue opsz;
        public AxisMask mask;

        public AxisValue this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return wght;
                    case 1: return wdth;
                    case 2: return ital;
                    case 3: return slnt;
                    case 4: return opsz;
                    default: return default;
                }
            }
        }

        public bool Equals(VariationConfig other)
        {
            if (mask != other.mask) return false;
            for (var i = 0; i < FontVariation.AxisCount; i++)
            {
                var bit = (AxisMask)(1 << i);
                if ((mask & bit) == 0) continue;
                var left = this[i];
                var right = other[i];
                var leftInvalid = float.IsNaN(left.value) || float.IsInfinity(left.value);
                var rightInvalid = float.IsNaN(right.value) || float.IsInfinity(right.value);
                if (leftInvalid || rightInvalid)
                {
                    if (leftInvalid != rightInvalid) return false;
                    continue;
                }
                if (left.mode != right.mode || left.value != right.value) return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is VariationConfig other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(mask);
            for (var i = 0; i < FontVariation.AxisCount; i++)
            {
                if ((mask & (AxisMask)(1 << i)) == 0) continue;
                var axis = this[i];
                var invalid = float.IsNaN(axis.value) || float.IsInfinity(axis.value);
                hash.Add(invalid ? 0f : axis.value);
                if (!invalid) hash.Add(axis.mode);
            }
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Variable-font axis machinery owned by the layout core: the canonical axis order/tags and the
    /// resolution from a <see cref="VariationConfig"/> to concrete HarfBuzz/FreeType instance arrays.
    /// Style modifiers encode configs; the run-splitting and shaping passes decode them here.
    /// </summary>
    internal static class FontVariation
    {
        internal const int AxisCount = 5;

        internal static readonly uint[] axisTags =
        {
            0x77676874, 0x77647468, 0x6974616C, 0x736C6E74, 0x6F70737A,
        };

        /// <summary>
        /// Resolves the config over the font's axes into per-axis values ready to
        /// hash the instance. Baseline and the base for percentage/delta come from <paramref name="axisDefaults"/>
        /// (the font's configured per-axis defaults, aligned to <paramref name="fontAxes"/>) when supplied, otherwise
        /// from each axis's fvar default, so a tag that touches one axis leaves the rest at the configured default.
        /// Pair with <see cref="BuildInstanceArrays"/>, called only when the instance is new, so dedup hits allocate
        /// nothing. <paramref name="resolved"/> must hold at least <c>fontAxes.Length</c> entries.
        /// </summary>
        internal static void ResolveAxes(in VariationConfig config, HB.hb_ot_var_axis_info_t[] fontAxes,
            float[] axisDefaults, float[] resolved)
        {
            int axisCount = fontAxes.Length;

            for (int i = 0; i < axisCount; i++)
                resolved[i] = axisDefaults != null ? axisDefaults[i] : fontAxes[i].defaultValue;

            for (int ci = 0; ci < AxisCount; ci++)
            {
                var bit = (AxisMask)(1 << ci);
                if ((config.mask & bit) == 0) continue;

                var tag = axisTags[ci];
                for (int fi = 0; fi < axisCount; fi++)
                {
                    if (fontAxes[fi].tag != tag) continue;

                    var baseValue = axisDefaults != null ? axisDefaults[fi] : fontAxes[fi].defaultValue;
                    var av = config[ci];
                    float value = av.mode switch
                    {
                        AxisValueMode.Absolute => av.value,
                        AxisValueMode.Percent => baseValue * av.value / 100f,
                        AxisValueMode.Delta => baseValue + av.value,
                        _ => baseValue
                    };

                    if (float.IsNaN(value) || float.IsInfinity(value)) value = baseValue;
                    resolved[fi] = Math.Max(fontAxes[fi].minValue, Math.Min(fontAxes[fi].maxValue, value));
                    break;
                }
            }

            for (var i = 0; i < axisCount; i++)
                resolved[i] = FromFixed(ToFixed(resolved[i]));
        }

        internal static void BuildInstanceArrays(float[] resolved, HB.hb_ot_var_axis_info_t[] fontAxes,
            out HB.hb_variation_t[] hbVariations, out int[] ftCoords)
        {
            int axisCount = fontAxes.Length;
            ftCoords = new int[axisCount];
            int diffCount = 0;
            for (int fi = 0; fi < axisCount; fi++)
            {
                ftCoords[fi] = ToFixed(resolved[fi]);
                if (ftCoords[fi] != ToFixed(fontAxes[fi].defaultValue)) diffCount++;
            }

            hbVariations = new HB.hb_variation_t[diffCount];
            int hbIdx = 0;
            for (int fi = 0; fi < axisCount; fi++)
                if (ftCoords[fi] != ToFixed(fontAxes[fi].defaultValue))
                    hbVariations[hbIdx++] = new HB.hb_variation_t
                        { tag = fontAxes[fi].tag, value = FromFixed(ftCoords[fi]) };
        }

        internal static int ToFixed(float value)
        {
            if (float.IsNaN(value)) return 0;
            var scaled = Math.Round((double)value * 65536.0, MidpointRounding.AwayFromZero);
            if (scaled <= int.MinValue) return int.MinValue;
            if (scaled >= int.MaxValue) return int.MaxValue;
            return (int)scaled;
        }

        internal static float FromFixed(int value) => value / 65536f;
    }
}
