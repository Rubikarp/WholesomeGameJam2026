using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Displays a platform-native replica of the editor while preserving <see cref="UniTextEditable"/>
    /// as the authoritative document and input pipeline on Android, iOS, and WebGL. Other platforms and
    /// editors without this behavior use a transparent native input surface;
    /// <see cref="NativeKeyboardBehavior"/> configures keyboard traits independently.
    /// </summary>
    [Serializable]
    [TypeDescription("Render an authoritative UniText document through a native field presenter")]
    [TypeGroup("Native", 2)]
    public sealed partial class NativeFieldOverlayBehavior : InputBehavior
    {
        /// <summary>Identifies the built-in presenter that follows the current platform appearance.</summary>
        public const string SystemPresenter = "system";

        /// <summary>
        /// Gets or sets the registered presenter used when the field becomes active; activation fails when
        /// the target platform has no presenter with this identifier.
        /// </summary>
        [Tooltip("Native presenter registered on the active target platform. The built-in 'system' presenter follows the current host appearance.")]
        [SerializeField, StateProperty(nameof(ApplyPresentationChange))]
        private string presenterId = SystemPresenter;

        /// <summary>Gets or sets the control identity exposed to platform tooling.</summary>
        [Tooltip("Stable platform identity (Android tag / iOS accessibility identifier / DOM id).")]
        [SerializeField, StateProperty(nameof(ApplyPresentationChange))]
        private string identifier;

        /// <summary>Gets or sets opaque data interpreted only by the selected presenter.</summary>
        [Tooltip("Presenter-defined per-field data, such as JSON or another presenter-owned format.")]
        [TextArea, SerializeField, StateProperty(nameof(ApplyPresentationChange))]
        private string presenterData;

        protected override void OnEnable() => InvalidateInputSessionConfiguration();

        protected override void OnDisable() => InvalidateInputSessionConfiguration();

        internal NativeFieldPresentationState Capture(string placeholder)
            => new(string.IsNullOrWhiteSpace(presenterId) ? SystemPresenter : presenterId.Trim(),
                placeholder, identifier, presenterData);

        private void ApplyPresentationChange(StateMember member)
        {
            NotifyChanged(member);
            InvalidateInputSessionConfiguration();
        }
    }
}
