namespace LightSide
{
    /// <summary>
    /// Resolves named <see cref="PaintSwatch"/> entries for paint parameters. Marker over
    /// <see cref="INamedCatalog{TEntry}"/> that scopes the <c>[SerializeReference, TypeSelector]</c>
    /// dropdown. Built-in: <see cref="InlinePaintProvider"/>, <see cref="AssetPaintProvider"/>,
    /// <see cref="GlobalSettingsPaintProvider"/>.
    /// </summary>
    [StateHierarchy]
    [TypeMenuSuffix("PaintProvider", "Provider")]
    public interface IPaintProvider : INamedCatalog<PaintSwatch>
    {
    }
}
