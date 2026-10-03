using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Defines an inline object (UI prefab) that can be embedded within text flow.
    /// </summary>
    /// <remarks>
    /// Pure data: the host <see cref="ObjModifier"/> owns lifecycle state (instance count,
    /// instantiated prefab clones), so the same <see cref="InlineObject"/> instance can safely
    /// be referenced by multiple modifiers.
    /// </remarks>
    [Serializable]
    public partial class InlineObject : InlineMedia
    {
        /// <summary>UI prefab to instantiate as the inline child.</summary>
        [SerializeField, StateProperty]
        private RectTransform prefab;
    }

    /// <summary>Presentation overrides for one inline object resolved from the modifier's provider.</summary>
    [Serializable]
    public sealed class InlineObjectOverride : InlineMediaOverride
    {
    }

    /// <summary>
    /// Embeds UI prefabs (images, icons, custom elements) inline with text. The set of named
    /// inline objects available to <c>&lt;obj=name&gt;</c> tags is supplied by an
    /// <see cref="IObjProvider"/>.
    /// </summary>
    /// <remarks>
    /// Parameter: <c>name[,size][,offset][,advance][,lineHeightAbove][,lineHeightBelow][,pivot][,rotation]</c>
    /// — the name resolves through <see cref="Provider"/>. The hierarchy is provider entry → keyed
    /// override → default parameters → tag attribute. Size, offset, advance, and
    /// line space use em units; pivot is normalized and
    /// rotation uses degrees. Built-in providers: <see cref="InlineObjProvider"/> (default — inline
    /// list on the modifier), <see cref="AssetObjProvider"/> (shared <see cref="UniTextObjects"/> asset).
    /// For dynamic catalogs, implement <see cref="IObjProvider"/> directly and raise its
    /// <see cref="INamedCatalog{TEntry}.Changed"/> event when the resolution result changes.
    /// </remarks>
    /// <seealso cref="SpriteModifier"/>
    /// <seealso cref="IObjProvider"/>
    /// <seealso cref="InlineObject"/>
    [Serializable]
    [TypeGroup("Inline", 5)]
    [TypeDescription("Embeds an inline object (image, icon) within text.")]
    public sealed partial class ObjModifier : InlineObjectModifier<InlineObject, PrefabMediaWrapper, InlineObjectOverride>
    {
        /// <summary>Source of named inline objects.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyProviderChange)),
         StateLink(nameof(OnProviderStateChanged))]
        [Tooltip("Source of named inline objects for <obj=name> tags handled by this modifier.")]
        private IObjProvider provider = new InlineObjProvider();

        private void ApplyProviderChange(IObjProvider previous, IObjProvider current)
        {
            RebindCatalog(previous, current);
            MarkTextDirty();
        }

        protected override INamedCatalog<InlineObject> Catalog => provider;

        protected override bool HasRenderable(InlineObject entry) => entry.Prefab is not null;

        protected override void ConfigureWrapper(PrefabMediaWrapper wrapper, InlineObject entry, int cluster)
        {
            if (wrapper.instance is not null &&
                !ReferenceEquals(wrapper.prefab, entry.Prefab))
                wrapper.needDestroy = true;
            wrapper.prefab = entry.Prefab;
        }
    }
}
