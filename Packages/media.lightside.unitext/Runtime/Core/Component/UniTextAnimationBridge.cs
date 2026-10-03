using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Bridges a Unity <c>Animator</c> to a <see cref="UniTextBase"/>: Animator writes hit serialized
    /// fields directly and bypass the property setters that normally invalidate the text pipeline, so
    /// this turns them back into the correct <see cref="UniTextDirty"/> invalidation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sibling capability component, like <c>UniTextSelectable</c>: add it to a GameObject that
    /// already has a <see cref="UniText"/> or <see cref="UniTextWorld"/>. Its presence enables animation
    /// diffing; without it the host does no per-frame diff work, so non-animated text pays nothing.
    /// </para>
    /// <para>
    /// All animation is opt-in through <see cref="Handlers"/>: add the field handler for your component
    /// (<see cref="UniTextFieldsAnimationHandler"/> for a <see cref="UniText"/>,
    /// <see cref="UniTextWorldFieldsAnimationHandler"/> for a <see cref="UniTextWorld"/>) to animate its
    /// own fields, and <see cref="ModifierFieldsAnimationHandler"/> to animate the fields of any
    /// modifier in the host's styles. Per-range animation goes through the parameter surface
    /// (<see cref="UniTextDriver"/>, ownership) instead.
    /// </para>
    /// </remarks>
    /// <seealso cref="AnimationHandler"/>
    [ExecuteAlways]
    [RequireComponent(typeof(UniTextBase))]
    [DisallowMultipleComponent]
    [AddComponentMenu(UniTextMenu.AddComponent.AnimationBridge)]
    public sealed partial class UniTextAnimationBridge : MonoBehaviour
    {
        /// <summary>Animation watchers diffed after Animator updates.</summary>
        [SerializeReference, TypeSelector, StateList(nameof(BindHandlers))]
        [Tooltip("Animation watchers. Each diffs one set of Animator-driven values and invalidates the text pipeline when they move.")]
        private AnimationHandler[] handlers = System.Array.Empty<AnimationHandler>();

        private UniTextBase uniText;
        private bool subscribed;
        [System.NonSerialized] private ReferenceBinding<AnimationHandler> boundHandlers;

        /// <summary>
        /// The text component this animator drives. Same GameObject; lazily resolved so layout queries
        /// work identically in edit and play mode (single runtime path, no edit-time branch).
        /// </summary>
        public UniTextBase TextComponent
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (uniText == null) uniText = GetComponent<UniTextBase>();
                return uniText;
            }
        }

        private void Awake() => uniText = GetComponent<UniTextBase>();

        private void OnEnable()
        {
            var text = TextComponent;
            if (text == null) return;

            if (!subscribed)
            {
                subscribed = true;
                text.Animated += RunHandlers;
            }
            BindHandlers();
        }

        private void OnDisable()
        {
            if (uniText != null && subscribed)
            {
                subscribed = false;
                uniText.Animated -= RunHandlers;
            }
            boundHandlers?.Clear();
        }

        /// <summary>
        /// Runs every handler's diff after the Animator applied properties. Each handler raises its
        /// own granular invalidation.
        /// </summary>
        internal void RunHandlers()
        {
            if (boundHandlers == null) return;
            for (var i = 0; i < boundHandlers.Count; i++)
                boundHandlers[i].Diff();
        }

        private void BindHandlers()
        {
            if (!isActiveAndEnabled) return;
            if (handlers is not { Length: > 0 }) { boundHandlers?.Clear(); return; }
            var text = TextComponent;
            if (text == null) return;

            boundHandlers ??= new ReferenceBinding<AnimationHandler>(
                handler => handler.Bind(uniText), handler => handler.Unbind());
            boundHandlers.Reconcile(handlers);
        }
    }
}
