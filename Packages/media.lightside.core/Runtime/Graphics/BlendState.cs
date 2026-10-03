using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>Describes fixed-function RGB and alpha blending for premultiplied layer rendering.</summary>
    internal readonly struct BlendState
    {
        internal static readonly int SourceRgbId = Shader.PropertyToID("_SrcBlend");
        internal static readonly int DestinationRgbId = Shader.PropertyToID("_DstBlend");
        internal static readonly int SourceAlphaId = Shader.PropertyToID("_SrcBlendAlpha");
        internal static readonly int DestinationAlphaId = Shader.PropertyToID("_DstBlendAlpha");
        internal static readonly int RgbOperationId = Shader.PropertyToID("_BlendOp");
        internal static readonly int AlphaOperationId = Shader.PropertyToID("_BlendOpAlpha");

        internal BlendMode SourceRgb { get; }
        internal BlendMode DestinationRgb { get; }
        internal BlendMode SourceAlpha { get; }
        internal BlendMode DestinationAlpha { get; }
        internal BlendOp RgbOperation { get; }
        internal BlendOp AlphaOperation { get; }

        private BlendState(
            BlendMode sourceRgb,
            BlendMode destinationRgb,
            BlendMode sourceAlpha,
            BlendMode destinationAlpha,
            BlendOp rgbOperation,
            BlendOp alphaOperation)
        {
            SourceRgb = sourceRgb;
            DestinationRgb = destinationRgb;
            SourceAlpha = sourceAlpha;
            DestinationAlpha = destinationAlpha;
            RgbOperation = rgbOperation;
            AlphaOperation = alphaOperation;
        }

        internal static BlendState Resolve(LayerBlend blend)
        {
            switch (blend)
            {
                case LayerBlend.Normal:
                    return SourceOver(BlendMode.One, BlendMode.OneMinusSrcAlpha);
                case LayerBlend.Multiply:
                    return SourceOver(BlendMode.DstColor, BlendMode.OneMinusSrcAlpha);
                case LayerBlend.Screen:
                    return SourceOver(BlendMode.One, BlendMode.OneMinusSrcColor);
                case LayerBlend.Additive:
                    return SourceOver(BlendMode.One, BlendMode.One);
                case LayerBlend.Exclusion:
                    return SourceOver(BlendMode.OneMinusDstColor, BlendMode.OneMinusSrcColor);
                default:
                    throw new ArgumentOutOfRangeException(nameof(blend), blend, "Unsupported layer blend mode.");
            }
        }

        internal static void Apply(Material material, LayerBlend blend)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));

            var state = Resolve(blend);
            material.SetFloat(SourceRgbId, (float)state.SourceRgb);
            material.SetFloat(DestinationRgbId, (float)state.DestinationRgb);
            material.SetFloat(SourceAlphaId, (float)state.SourceAlpha);
            material.SetFloat(DestinationAlphaId, (float)state.DestinationAlpha);
            material.SetFloat(RgbOperationId, (float)state.RgbOperation);
            material.SetFloat(AlphaOperationId, (float)state.AlphaOperation);
        }

        internal static void Validate(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (material.HasProperty(SourceRgbId) && material.HasProperty(DestinationRgbId) &&
                material.HasProperty(SourceAlphaId) && material.HasProperty(DestinationAlphaId) &&
                material.HasProperty(RgbOperationId) && material.HasProperty(AlphaOperationId))
                return;
            throw new InvalidOperationException(
                $"Material '{material.name}' does not implement the LightSide layer blend contract.");
        }

        private static BlendState SourceOver(BlendMode sourceRgb, BlendMode destinationRgb,
            BlendOp rgbOperation = BlendOp.Add)
        {
            return new BlendState(
                sourceRgb,
                destinationRgb,
                BlendMode.One,
                BlendMode.OneMinusSrcAlpha,
                rgbOperation,
                BlendOp.Add);
        }
    }
}
