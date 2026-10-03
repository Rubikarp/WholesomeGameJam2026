using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Controls variable font axis values per text range.
    /// </summary>
    /// <remarks>
    /// Parameter: positional axis values in order wght, wdth, ital, slnt, opsz.
    /// Use <c>~</c> to skip an axis. Each value supports absolute, percentage, or delta:
    /// <list type="bullet">
    /// <item><c>700</c> — absolute axis value</item>
    /// <item><c>150%</c> — percentage of font's default</item>
    /// <item><c>+200</c> — delta from font's default</item>
    /// </list>
    /// Examples: <c>700</c>, <c>~,80</c>, <c>700,~,~,-12</c>
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 1)]
    [TypeDescription("Controls variable font axis values (weight, width, italic, slant, optical size).")]
    [GenerateParameters]
    public partial class VariationModifier : PooledAttributeModifier<byte>
    {
        /// <summary>Weight axis (wght). A per-range value overrides it; whole-text/bare uses this.</summary>
        [SerializeField, Parameter, Unit("abs[1,1000]|%(25,225)|delta[-300,500]"), StateProperty(nameof(MarkTextDirty))] private UnitValue weight = UnitValue.Absolute(400);
        /// <summary>Width axis (wdth). A per-range value overrides it; whole-text/bare uses this.</summary>
        [SerializeField, Parameter, Unit("abs[50,200]|%(50,200)|delta[-50,100]"), StateProperty(nameof(MarkTextDirty))] private UnitValue width = UnitValue.Absolute(100);
        /// <summary>Italic axis (ital). A per-range value overrides it; whole-text/bare uses this.</summary>
        [SerializeField, Parameter, Unit("abs[0,1]|delta[0,1]"), StateProperty(nameof(MarkTextDirty))] private UnitValue italic = UnitValue.Absolute(0);
        /// <summary>Slant axis (slnt). A per-range value overrides it; whole-text/bare uses this.</summary>
        [SerializeField, Parameter, Unit("abs(-90,90)|delta(-90,90)"), StateProperty(nameof(MarkTextDirty))] private UnitValue slant = UnitValue.Absolute(0);
        /// <summary>Optical-size axis (opsz). A per-range value overrides it; whole-text/bare uses this.</summary>
        [SerializeField, Parameter, Unit("abs(1,144)|%(8,1200)|delta[-11,132]"), StateProperty(nameof(MarkTextDirty))] private UnitValue opticalSize = UnitValue.Absolute(12);

        protected sealed override string AttributeKey => AttributeKeys.Variation;

        private static readonly ParameterDescriptor<VariationModifier, UnitValue>[] axisParams =
            { Param.Weight, Param.Width, Param.Italic, Param.Slant, Param.OpticalSize };

        protected override void OnEnable()
        {
            base.OnEnable();
            buffers.variationConfigs.FakeClear();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            attribute?.ClearAll();
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var config = default(VariationConfig);
            var anySet = false;
            for (var axis = 0; axis < FontVariation.AxisCount; axis++)
            {
                if (!axisParams[axis].TryResolve(this, in context, out var value)) continue;
                config.mask |= (AxisMask)(1 << axis);
                SetAxis(ref config, axis, ToAxisValue(value));
                anySet = true;
            }
            if (!anySet) config = SeedFromFields();

            if (config.mask == AxisMask.None) return;

            var configIndex = FindOrAddConfig(ref config);
            if (configIndex < 0) return;

            var encoded = (byte)(configIndex + 1);
            attribute.FillRange(context.Segment.Range, encoded);
        }

        /// <summary>Builds a config with every axis set from the serialized fields — the whole-text/bare default.</summary>
        private VariationConfig SeedFromFields()
        {
            var config = new VariationConfig();
            AddAxis(ref config, 0, weight);
            AddAxis(ref config, 1, width);
            AddAxis(ref config, 2, italic);
            AddAxis(ref config, 3, slant);
            AddAxis(ref config, 4, opticalSize);
            return config;
        }

        private static void AddAxis(ref VariationConfig config, int index, UnitValue v)
        {
            var mode = v.unit switch
            {
                UnitKind.Percent => AxisValueMode.Percent,
                UnitKind.Delta => AxisValueMode.Delta,
                _ => AxisValueMode.Absolute,
            };
            config.mask |= (AxisMask)(1 << index);
            SetAxis(ref config, index, new AxisValue { value = v.value, mode = mode });
        }

        private int FindOrAddConfig(ref VariationConfig config)
        {
            ref var configs = ref buffers.variationConfigs;
            for (var i = 0; i < configs.count; i++)
            {
                if (configs[i].Equals(config))
                    return i;
            }

            if (configs.count >= 255)
                return -1;

            configs.Add(config);
            return configs.count - 1;
        }

        internal static bool TryParse(ReadOnlySpan<char> param, out VariationConfig config)
        {
            var reader = new ParameterReader(param);
            config = default;
            for (var axis = 0; axis < FontVariation.AxisCount; axis++)
            {
                if (!reader.NextUnitFloat(out var value, out var unit))
                {
                    if (reader.IsEmpty) break;
                    continue;
                }
                config.mask |= (AxisMask)(1 << axis);
                SetAxis(ref config, axis, ToAxisValue(new UnitValue(value, unit)));
            }
            return config.mask != AxisMask.None;
        }

        private static AxisValue ToAxisValue(UnitValue value) => new()
        {
            value = value.value,
            mode = value.unit switch
            {
                UnitKind.Percent => AxisValueMode.Percent,
                UnitKind.Delta => AxisValueMode.Delta,
                _ => AxisValueMode.Absolute
            },
        };

        private static void SetAxis(ref VariationConfig config, int index, AxisValue value)
        {
            switch (index)
            {
                case 0: config.wght = value; break;
                case 1: config.wdth = value; break;
                case 2: config.ital = value; break;
                case 3: config.slnt = value; break;
                case 4: config.opsz = value; break;
            }
        }

    }
}
