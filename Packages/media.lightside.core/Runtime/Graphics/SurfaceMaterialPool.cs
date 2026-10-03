using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightSide
{
    internal readonly struct SurfaceMaterialKey : IEquatable<SurfaceMaterialKey>
    {
        internal readonly int sourceId;
        internal readonly int textureId;
        internal readonly LayerBlend blend;
        internal readonly Texture texture;

        internal SurfaceMaterialKey(Material source, LayerBlend blend, Texture texture = null)
        {
            sourceId = ObjectUtils.GetInstanceIdCompat(source);
            textureId = texture == null ? 0 : ObjectUtils.GetInstanceIdCompat(texture);
            this.blend = blend;
            this.texture = texture;
        }

        public bool Equals(SurfaceMaterialKey other)
            => sourceId == other.sourceId && textureId == other.textureId && blend == other.blend &&
               ReferenceEquals(texture, other.texture);

        public override bool Equals(object obj) => obj is SurfaceMaterialKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(sourceId, textureId, (int)blend);
    }

    /// <summary>
    /// Shared clones of a LightSide surface material, keyed by source, compositing blend, and paint
    /// texture — the one pool behind every blend and texture batch of text, decorations, and shapes,
    /// so equal combinations share one clone and keep batching together.
    /// </summary>
    internal sealed class SurfaceMaterialPool : SharedMaterialClonePool<SurfaceMaterialKey>
    {
        internal static readonly SurfaceMaterialPool Instance = new();

        private SurfaceMaterialPool() { }

        internal static void ValidateUse(Material material, LayerBlend blend)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            BlendState.Resolve(blend);
            if (blend != LayerBlend.Normal) BlendState.Validate(material);
        }

        protected override Material CreateClone(in SurfaceMaterialKey key, Object source)
        {
            if (source is not Material material)
                throw new ArgumentException("A surface material source must be a Material.", nameof(source));

            return new Material(material)
            {
                name = key.texture == null
                    ? $"{material.name} ({key.blend})"
                    : $"{material.name} ({key.blend}, {key.texture.name})",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        protected override void OnRebind(Material runtime, in SurfaceMaterialKey key, Object source)
        {
            var material = (Material)source;
            if (key.blend != LayerBlend.Normal) BlendState.Validate(material);
            if (runtime.shader != material.shader) runtime.shader = material.shader;
            runtime.CopyPropertiesFromMaterial(material);
            BlendState.Apply(runtime, key.blend);

            if (key.texture != null)
            {
                runtime.EnableKeyword(LightSideShaderIds.PaintTextureKeyword);
                runtime.SetTexture(LightSideShaderIds.PaintTexture, key.texture);
            }
        }
    }
}
