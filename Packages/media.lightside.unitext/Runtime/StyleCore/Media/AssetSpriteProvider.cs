using System;

namespace LightSide
{
    /// <summary>
    /// <see cref="ISpriteProvider"/> backed by an explicit <see cref="UniTextSprites"/>
    /// asset referenced on the modifier. Use this when multiple components should share
    /// the same sprite catalog managed as a project asset.
    /// </summary>
    [Serializable]
    [TypeDescription("Resolves names through an explicit UniTextSprites asset reference.")]
    public sealed class AssetSpriteProvider : AssetNamedCatalog<InlineSprite, UniTextSprites>, ISpriteProvider
    {
    }
}
