using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// ScriptableObject of named <see cref="PaintSwatch"/> entries (colour / gradient / texture)
    /// resolved by name in paint parameters (e.g. <c>&lt;fill=ember&gt;</c>). Reference it in
    /// <see cref="UniTextSettings"/> for a project-wide catalog, or per modifier via
    /// <see cref="AssetPaintProvider"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "UniTextPaints", menuName = UniTextMenu.CreateAsset.Paints)]
    public sealed class UniTextPaints : NamedCatalogAsset<PaintSwatch>
    {
        protected override string GetEntryName(PaintSwatch entry) => entry.name;
    }
}
