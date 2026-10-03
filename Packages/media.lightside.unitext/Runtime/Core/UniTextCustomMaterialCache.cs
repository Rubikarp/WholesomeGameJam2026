using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightSide
{
    internal readonly struct CustomKey : IEquatable<CustomKey>
    {
        public readonly int sourceId;

        public CustomKey(int sourceId) => this.sourceId = sourceId;

        public bool Equals(CustomKey other) => sourceId == other.sourceId;
        public override bool Equals(object obj) => obj is CustomKey k && Equals(k);
        public override int GetHashCode() => sourceId;
    }

    /// <summary>
    /// Shared runtime clones of user materials for <see cref="MaterialModifier"/>: one clone per source
    /// material, so every modifier referencing the same source batches into one draw call. The glyph mode
    /// (SDF/MSDF/emoji) rides the vertex stream, so a single clone serves every mode.
    /// <see cref="BindSourceDirect"/> opts out for callers that want runtime edits on the source material
    /// to show immediately (at the cost of shared state across all its consumers).
    /// </summary>
    /// <remarks>
    /// Shader properties are copied from the source once, when the clone is first built. Runtime edits to
    /// the source (<c>SetColor</c> etc.) are not mirrored — edit the clone, animate through
    /// <see cref="MaterialModifier.ConstantUv2"/>/<see cref="MaterialModifier.ConstantUv3"/>, or call
    /// <see cref="InvalidateSource"/> and <see cref="UniTextBase.SetAppearanceDirty"/> to rebuild.
    /// </remarks>
    internal sealed class UniTextCustomMaterialCache : SharedMaterialClonePool<CustomKey>
    {
        public static readonly UniTextCustomMaterialCache Instance = new();

        protected override Material CreateClone(in CustomKey key, Object source)
            => new Material((Material)source)
            {
                name = $"UniText Custom [{source.name}]",
                hideFlags = HideFlags.HideAndDontSave,
            };

        /// <summary>Returns <paramref name="source"/> itself, for callers that want source edits visible immediately.</summary>
        public Material BindSourceDirect(Material source) => source;

        /// <summary>
        /// Rebuilds the clones for <paramref name="source"/> on their next use so subsequent text rebuilds
        /// pick up runtime edits to the source's properties or keywords. Does not trigger rebuilds itself —
        /// call <see cref="UniTextBase.SetAppearanceDirty"/> on affected components after.
        /// </summary>
        public void InvalidateSource(Material source) => InvalidateBySource(source);
    }
}
