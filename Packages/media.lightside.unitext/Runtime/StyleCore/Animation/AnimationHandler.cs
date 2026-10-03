using System;

namespace LightSide
{
    /// <summary>
    /// One Animator-driven diff unit on a <see cref="UniTextAnimationBridge"/>. A Unity <c>Animator</c>
    /// writes serialized fields directly, bypassing the property setters that normally raise
    /// <see cref="UniTextDirty"/>; a handler caches a baseline of the values it watches and, after each
    /// apply, re-issues the invalidation those setters would have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Add handlers to the bridge's inspector list. Built-ins cover the component's own fields
    /// (<see cref="UniTextFieldsAnimationHandler"/>, <see cref="UniTextWorldFieldsAnimationHandler"/>);
    /// author your own by subclassing this for any other component-level diff.
    /// </para>
    /// <para>
    /// The bridge binds every handler on enable. Diffing runs once per
    /// <c>OnDidApplyAnimationProperties</c> — keep it allocation-free in steady state.
    /// </para>
    /// </remarks>
    /// <seealso cref="UniTextAnimationBridge"/>
    [Serializable]
    [TypeMenuSuffix("AnimationHandler")]
    public abstract class AnimationHandler
    {
        [NonSerialized] private UniTextBase host;

        /// <summary>The component this handler diffs against, or <see langword="null"/> before <see cref="Bind"/>.</summary>
        protected UniTextBase Host => host;

        internal void Bind(UniTextBase component)
        {
            host = component;
            if (component != null) OnBind(component);
        }

        internal void Unbind()
        {
            OnUnbind();
            host = null;
        }

        internal void Diff()
        {
            if (host != null) OnDiff(host);
        }

        /// <summary>Snapshots the watched values as the comparison baseline and caches any per-bind metadata.</summary>
        protected abstract void OnBind(UniTextBase host);

        /// <summary>Drops any live binding held beyond <see cref="Host"/>. Default: nothing.</summary>
        protected virtual void OnUnbind() { }

        /// <summary>Diffs watched values, refreshes their baselines, and raises the matching granular invalidation.</summary>
        protected abstract void OnDiff(UniTextBase host);
    }
}
