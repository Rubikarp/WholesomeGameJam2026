using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// A GpuAtlas seen only as a live, replaceable sampleable texture — the minimal surface a binder
    /// needs, so <see cref="AtlasGlobalBinding"/> stays agnostic of the atlas's entry type.
    /// Implemented by every <see cref="GpuAtlas{TEntry}"/>.
    /// </summary>
    public interface IGpuAtlasTextureSource
    {
        /// <summary>The currently published atlas texture. Replaced by a new object on storage grow, compaction, or device-loss recovery.</summary>
        Texture AtlasTexture { get; }

        /// <summary>Raised when <see cref="AtlasTexture"/> is replaced by a different object (never for in-place content changes).</summary>
        event Action<Texture> AtlasTextureChanged;
    }

    /// <summary>
    /// Publishes one <see cref="IGpuAtlasTextureSource"/> as a global shader texture, following texture
    /// and atlas-instance replacement. Global rather than per-material because an atlas is process-wide:
    /// materials that sample it keep no binding of their own, so the texture never enters a material's
    /// identity and never splits a Canvas batch. The property must therefore NOT be declared in a
    /// shader's <c>Properties</c> block — a material-level value shadows the global.
    /// </summary>
    public sealed class AtlasGlobalBinding
    {
        private readonly int textureId;
        private IGpuAtlasTextureSource subscribed;
        private Texture current;

        public AtlasGlobalBinding(int textureId) => this.textureId = textureId;

        /// <summary>The texture currently published under this binding's property.</summary>
        public Texture Current => current;

        /// <summary>
        /// Re-points the binding at <paramref name="source"/> — unsubscribing the previous one — and
        /// republishes its current texture. A no-op when already subscribed to the same source, so it is
        /// cheap to call every frame while atlas instances appear lazily. A process-lifetime subscription
        /// instead would keep publishing a retired atlas's destroyed texture after the source is replaced.
        /// </summary>
        public void EnsureSubscription(IGpuAtlasTextureSource source)
        {
            if (ReferenceEquals(subscribed, source)) return;
            if (subscribed != null)
                subscribed.AtlasTextureChanged -= SetTexture;
            subscribed = source;
            if (source != null)
                source.AtlasTextureChanged += SetTexture;
            SetTexture(source?.AtlasTexture);
        }

        public void SetTexture(Texture atlas)
        {
            current = atlas;
            Shader.SetGlobalTexture(textureId, atlas);
        }
    }
}
