using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightSide
{
    /// <summary>
    /// Configures the native soft keyboard for the field: layout, return key, capitalization, autofill,
    /// and smart features. The single composable home for keyboard traits — the editor reads it when it
    /// shows the keyboard. The traits apply both in the default transparent-keyboard mode and when a
    /// <see cref="NativeFieldOverlayBehavior"/> renders the OS's own field. Password masking is a separate
    /// concern (<see cref="PasswordBehavior"/>). Desktop has no soft keyboard — the behavior is inert there.
    /// </summary>
    [Serializable]
    [TypeDescription("Native soft-keyboard traits: layout, return key, autofill, smart features")]
    [TypeGroup("Native", 2)]
    public sealed partial class NativeKeyboardBehavior : InputBehavior
    {
        /// <summary>The keyboard trait bundle the editor hands to the native layer on show.</summary>
        [SerializeField, StateProperty(nameof(ApplyKeyboardChange), Owned = true)]
        [FormerlySerializedAs("config")]
        private NativeKeyboardConfig keyboard = new();

        internal override void OnChangeSinkChanged(
            IInputBehaviorChangeSink previous, IInputBehaviorChangeSink current)
            => keyboard?.SetChangeCallback(current != null ? OnNestedKeyboardChanged : null);

        protected override void OnEnable()
        {
            editable.KeyboardResolver.Subscribe(Resolve);
            InvalidateInputSessionConfiguration();
        }

        protected override void OnDisable()
        {
            editable.KeyboardResolver.Unsubscribe(Resolve);
            InvalidateInputSessionConfiguration();
        }

        private void Resolve(ref KeyboardRequest request) => request.config = keyboard;

        private void ApplyKeyboardChange(NativeKeyboardConfig previous, ref NativeKeyboardConfig current)
        {
            current ??= new NativeKeyboardConfig();
            if (previous?.HasChangeCallback(OnNestedKeyboardChanged) == true)
                previous.SetChangeCallback(null);
            current.SetChangeCallback(HasChangeSink ? OnNestedKeyboardChanged : null);
            NotifyStructureChanged(Members.Keyboard);
            InvalidateInputSessionConfiguration();
        }

        private void OnNestedKeyboardChanged(
            IStateMemberReplay source, StateMember member)
        {
            NotifyNestedChanged(source, member);
            InvalidateInputSessionConfiguration();
        }

        internal override bool ReplayNestedStateMember(InputBehavior authoredBehavior,
            IStateMemberReplay source, StateMember member)
            => authoredBehavior is NativeKeyboardBehavior authored &&
               ReferenceEquals(authored.keyboard, source) &&
               keyboard is IStateMemberReplay target &&
               target.ReplayStateMember(source, member);

    }
}
