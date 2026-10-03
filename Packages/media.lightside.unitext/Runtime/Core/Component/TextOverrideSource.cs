using System;

namespace LightSide
{
    /// <summary>
    /// Possible sources of a runtime override on top of the serialized
    /// <see cref="UniTextBase.Text"/> — i.e. reasons the rendered text may diverge from
    /// what is stored in the scene or prefab. Flags may combine (e.g.
    /// <see cref="SetText"/> | <see cref="Resolver"/> when <c>SetText</c> assigned a
    /// runtime buffer and a resolver further substitutes that buffer's content).
    /// </summary>
    [Flags]
    public enum TextOverrideSource
    {
        /// <summary>
        /// No override — <see cref="UniTextBase.RenderedText"/> equals
        /// <see cref="UniTextBase.RawText"/> equals the serialized <c>text</c> field.
        /// </summary>
        None = 0,

        /// <summary>
        /// One of the <c>SetText</c> overloads on <see cref="UniTextBase"/> assigned a
        /// runtime source, so <see cref="UniTextBase.RawText"/> diverges from the
        /// serialized <see cref="UniTextBase.Text"/> field.
        /// </summary>
        SetText = 1 << 0,

        /// <summary>
        /// An attached <see cref="IUniTextResolver"/> returned a substitute from
        /// <see cref="IUniTextResolver.TryResolve"/> on the last rebuild, so
        /// <see cref="UniTextBase.RenderedText"/> is the resolver's output rather than
        /// <see cref="UniTextBase.RawText"/>.
        /// </summary>
        Resolver = 1 << 1,
    }
}
