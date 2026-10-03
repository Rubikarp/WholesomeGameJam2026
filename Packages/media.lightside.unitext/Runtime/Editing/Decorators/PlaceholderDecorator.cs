using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Shows a target while the field is empty and hides it once there is text. The classic placeholder.
    /// </summary>
    [Serializable]
    [TypeGroup("Placeholder", 0)]
    [TypeDescription("Show a target while the field is empty; hide it once text is entered")]
    public sealed partial class PlaceholderDecorator : FieldDecorator
    {
        /// <summary>Text object shown only while the edited document is empty.</summary>
        [SerializeField, StateProperty(nameof(ApplyTargetChange))]
        [Tooltip("Object shown only while the field is empty. Set its text on the component itself. " +
                 "A platform-native field replica shows this text as its own placeholder.")]
        private UniTextBase target;

        [NonSerialized] private UniTextBase watchedTarget;
        [NonSerialized] private string observedPlaceholder;

        internal string PlaceholderText => target != null ? target.Text : null;

        private void ApplyTargetChange(StateMember member)
        {
            NotifyChanged(member);
            WatchTarget();
            InvalidatePlaceholderPresentation();
        }

        protected override void OnAttach()
        {
            WatchTarget();
            observedPlaceholder = PlaceholderText;
        }

        private void WatchTarget()
        {
            if (ReferenceEquals(watchedTarget, target)) return;
            if (watchedTarget != null) watchedTarget.DirtyFlagsChanged -= OnTargetDirty;
            watchedTarget = IsEnabled ? target : null;
            if (watchedTarget != null) watchedTarget.DirtyFlagsChanged += OnTargetDirty;
        }

        /// <summary>
        /// A native replica captures the placeholder string when its session opens, so an edit to
        /// the target's own text has to re-open that capture. The dirty stream is the only signal
        /// that survives the target being deactivated while the field carries text; it also fires
        /// for the target's own enable and disable cycles, so only an actual string change may
        /// reach the session.
        /// </summary>
        private void OnTargetDirty(UniTextDirty flags)
        {
            if ((flags & UniTextDirty.Text) != 0) InvalidatePlaceholderPresentation();
        }

        private void InvalidatePlaceholderPresentation()
        {
            var text = PlaceholderText;
            if (string.Equals(text, observedPlaceholder, StringComparison.Ordinal)) return;
            observedPlaceholder = text;
            InvalidateInputSessionConfiguration();
        }

        protected override void OnFieldState(in FieldState state)
        {
            if (target == null) return;
            if (target.gameObject.activeSelf != state.IsEmpty)
                target.gameObject.SetActive(state.IsEmpty);
        }

        protected override void OnDetach()
        {
            if (watchedTarget != null)
            {
                watchedTarget.DirtyFlagsChanged -= OnTargetDirty;
                watchedTarget = null;
            }
            if (target != null && !target.gameObject.activeSelf)
                target.gameObject.SetActive(true);
        }
    }
}
