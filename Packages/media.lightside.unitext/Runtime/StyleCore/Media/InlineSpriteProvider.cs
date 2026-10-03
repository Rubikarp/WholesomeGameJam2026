using System;

namespace LightSide
{
    /// <summary>
    /// <see cref="ISpriteProvider"/> with an inline list of <see cref="InlineSprite"/> entries
    /// edited directly on the modifier. Use this for one-off catalogs that don't deserve a
    /// dedicated asset and aren't shared across components.
    /// </summary>
    [Serializable]
    [TypeDescription("Inline list of named sprites edited directly on the modifier.")]
    public sealed class InlineSpriteProvider : InlineNamedCatalog<InlineSprite>, ISpriteProvider
    {
        protected override string GetEntryName(InlineSprite entry) => entry?.Name;
    }
}

