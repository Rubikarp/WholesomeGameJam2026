using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// ScriptableObject containing named inline sprites for use with &lt;sprite=name&gt; tags.
    /// </summary>
    /// <remarks>
    /// Create via Assets → Create → UniText → Sprites. Reference from an
    /// <see cref="AssetSpriteProvider"/> on a <see cref="SpriteModifier"/> to share a catalog
    /// across multiple components without prefabbing each icon.
    /// </remarks>
    /// <seealso cref="SpriteModifier"/>
    /// <seealso cref="ISpriteProvider"/>
    [CreateAssetMenu(fileName = "UniTextSprites", menuName = UniTextMenu.CreateAsset.Sprites)]
    public sealed class UniTextSprites : NamedCatalogAsset<InlineSprite>
    {
        protected override string GetEntryName(InlineSprite entry) => entry?.Name;
    }
}
