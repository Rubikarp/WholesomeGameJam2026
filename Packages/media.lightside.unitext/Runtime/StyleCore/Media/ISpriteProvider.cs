namespace LightSide
{
    /// <summary>
    /// Resolves named <see cref="InlineSprite"/> entries for <see cref="SpriteModifier"/>.
    /// </summary>
    /// <remarks>
    /// Marker interface over <see cref="INamedCatalog{TEntry}"/> — exists to scope the
    /// <c>[SerializeReference, TypeSelector]</c> dropdown and to give a domain-specific
    /// name. Built-in implementations: <see cref="InlineSpriteProvider"/>,
    /// <see cref="AssetSpriteProvider"/>. Custom providers should raise their
    /// <see cref="INamedCatalog{TEntry}.Changed"/> event whenever their resolution result changes.
    /// </remarks>
    /// <seealso cref="SpriteModifier"/>
    [StateHierarchy]
    [TypeMenuSuffix("SpriteProvider", "Provider")]
    public interface ISpriteProvider : INamedCatalog<InlineSprite>
    {
    }
}
