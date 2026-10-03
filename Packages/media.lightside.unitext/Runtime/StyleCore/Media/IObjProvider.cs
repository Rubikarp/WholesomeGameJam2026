namespace LightSide
{
    /// <summary>
    /// Resolves named <see cref="InlineObject"/> entries for <see cref="ObjModifier"/>.
    /// </summary>
    /// <remarks>
    /// Marker interface over <see cref="INamedCatalog{TEntry}"/> — exists to scope the
    /// <c>[SerializeReference, TypeSelector]</c> dropdown and to give a domain-specific
    /// name. Built-in implementations: <see cref="InlineObjProvider"/>,
    /// <see cref="AssetObjProvider"/>. Custom providers should raise their
    /// <see cref="INamedCatalog{TEntry}.Changed"/> event whenever their resolution result changes.
    /// </remarks>
    /// <seealso cref="ObjModifier"/>
    [StateHierarchy]
    [TypeMenuSuffix("ObjProvider", "Provider")]
    public interface IObjProvider : INamedCatalog<InlineObject>
    {
    }
}
