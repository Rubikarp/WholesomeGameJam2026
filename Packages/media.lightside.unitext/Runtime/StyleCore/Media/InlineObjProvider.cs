using System;

namespace LightSide
{
    /// <summary>
    /// <see cref="IObjProvider"/> with an inline list of <see cref="InlineObject"/> entries
    /// edited directly on the modifier. Use this for one-off catalogs that don't deserve a
    /// dedicated asset and aren't shared across components.
    /// </summary>
    [Serializable]
    [TypeDescription("Inline list of named inline objects edited directly on the modifier.")]
    public sealed class InlineObjProvider : InlineNamedCatalog<InlineObject>, IObjProvider
    {
        protected override string GetEntryName(InlineObject entry) => entry?.Name;
    }
}
