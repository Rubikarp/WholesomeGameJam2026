using System;

namespace LightSide
{
    /// <summary>
    /// <see cref="IObjProvider"/> backed by an explicit <see cref="UniTextObjects"/>
    /// asset referenced on the modifier. Use this when multiple components should share
    /// the same inline-object catalog managed as a project asset.
    /// </summary>
    [Serializable]
    [TypeDescription("Resolves names through an explicit UniTextObjects asset reference.")]
    public sealed class AssetObjProvider : AssetNamedCatalog<InlineObject, UniTextObjects>, IObjProvider
    {
    }
}
