using System;

namespace LightSide
{
    /// <summary><see cref="IPaintProvider"/> with an inline list of named paints edited on the modifier.</summary>
    [Serializable]
    [TypeDescription("Inline list of named paints edited directly on the modifier.")]
    public sealed class InlinePaintProvider : InlineNamedCatalog<PaintSwatch>, IPaintProvider
    {
        protected override string GetEntryName(PaintSwatch entry) => entry.name;
    }
}
