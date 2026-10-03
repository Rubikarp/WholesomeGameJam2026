using System;

namespace LightSide
{
    /// <summary>
    /// Hook that can override the source text of a <see cref="UniTextBase"/> before it is
    /// parsed, shaped and laid out — without touching the serialized <c>text</c> field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Typical use cases: editor-time localization preview (swap keys for translated strings
    /// without dirtying scenes or prefabs), runtime localization bound to a language signal,
    /// or any derived-text scenario where the authored <c>text</c> is a template or key.
    /// </para>
    /// <para>
    /// Assign via <see cref="UniTextBase.TextResolver"/>. When a resolver is attached and
    /// <see cref="TryResolve"/> returns <see langword="true"/>, the component renders the
    /// override; the serialized text remains untouched, so scenes and prefabs are not marked
    /// dirty.
    /// </para>
    /// <para>
    /// Threading — same two-phase contract as <see cref="BaseModifier"/>:
    /// <list type="bullet">
    /// <item><see cref="PrepareForParallel"/> always runs on the main thread. Override to cache
    /// values from Unity APIs that are main-thread-only (localization tables, ScriptableObject
    /// lookups, <c>AssetDatabase</c> in editor).</item>
    /// <item><see cref="TryResolve"/> may run on a worker thread when batched parallel
    /// processing is active. Do not call Unity APIs directly from it — read from caches
    /// populated in <see cref="PrepareForParallel"/>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// To trigger a rebuild when your external source changes (language switch, scriptable
    /// object edit, etc.) call <c>owner.SetDirty(UniTextDirty.Text)</c> — the owner is
    /// supplied to <see cref="OnAttached"/>.
    /// </para>
    /// </remarks>
    public interface IUniTextResolver
    {
        /// <summary>
        /// Called after the resolver has been assigned to <paramref name="owner"/>. Use this
        /// to cache the owner reference and subscribe to any external signal that should
        /// trigger a re-resolve (e.g. a localization language-changed event).
        /// </summary>
        /// <param name="owner">The component this resolver is now attached to.</param>
        void OnAttached(UniTextBase owner) { }

        /// <summary>
        /// Called when the resolver is replaced, cleared, or its owner is destroyed.
        /// Unsubscribe from any signals registered in <see cref="OnAttached"/>.
        /// </summary>
        /// <param name="owner">The component this resolver is being detached from.</param>
        void OnDetached(UniTextBase owner) { }

        /// <summary>
        /// Called on the main thread before parallel processing begins, once per rebuild.
        /// Override to cache values from Unity APIs that are main-thread-only so that
        /// <see cref="TryResolve"/> can read them without touching the Unity API.
        /// </summary>
        void PrepareForParallel() { }

        /// <summary>
        /// Produces the effective text to feed into the parsing pipeline.
        /// </summary>
        /// <param name="source">The current source text of the owner (the serialized <c>text</c>
        /// field or the buffer set via <see cref="UniTextBase.SetText"/>).</param>
        /// <param name="result">When the method returns <see langword="true"/>, receives the
        /// override text. The memory must remain valid until the next <see cref="TryResolve"/>
        /// call on this component or until <see cref="OnDetached"/> is invoked.</param>
        /// <returns>
        /// <see langword="true"/> to use <paramref name="result"/> as the text to render;
        /// <see langword="false"/> to pass <paramref name="source"/> through unchanged.
        /// </returns>
        /// <remarks>
        /// May be invoked on a worker thread when batched parallel processing is active.
        /// Do not call Unity APIs directly — populate caches in <see cref="PrepareForParallel"/>
        /// and read them here.
        /// </remarks>
        bool TryResolve(ReadOnlyMemory<char> source, out ReadOnlyMemory<char> result);
    }
}
