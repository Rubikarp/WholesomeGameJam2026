using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// ScriptableObject containing named inline objects (UI prefabs) for use with &lt;obj=name&gt; tags.
    /// </summary>
    /// <remarks>
    /// Create via Assets → Create → UniText → Objects. Reference from an
    /// <see cref="AssetObjProvider"/> on an <see cref="ObjModifier"/> to share a catalog of
    /// inline prefabs across multiple components.
    /// </remarks>
    /// <seealso cref="ObjModifier"/>
    /// <seealso cref="IObjProvider"/>
    [CreateAssetMenu(fileName = "UniTextObjects", menuName = UniTextMenu.CreateAsset.Objects)]
    public sealed class UniTextObjects : NamedCatalogAsset<InlineObject>
    {
        protected override string GetEntryName(InlineObject entry) => entry?.Name;
    }
}
