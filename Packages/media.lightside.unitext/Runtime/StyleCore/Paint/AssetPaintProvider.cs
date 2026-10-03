using System;

namespace LightSide
{
    /// <summary><see cref="IPaintProvider"/> backed by an explicit <see cref="UniTextPaints"/> asset reference.</summary>
    [Serializable]
    [TypeDescription("Resolves names through an explicit UniTextPaints asset reference.")]
    public sealed class AssetPaintProvider : AssetNamedCatalog<PaintSwatch, UniTextPaints>, IPaintProvider
    {
    }
}
