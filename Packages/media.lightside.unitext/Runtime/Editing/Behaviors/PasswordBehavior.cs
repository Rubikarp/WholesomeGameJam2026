using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// A single-line password field: Enter submits, line breaks are rejected, every codepoint renders as
    /// the mask character, copy/cut are blocked unless <see cref="AllowCopy"/>, and native input uses secure
    /// entry. The stored text remains plain text; <see cref="Revealed"/> only changes its presentation.
    /// </summary>
    [Serializable]
    [TypeDescription("Single-line password: mask input; block copy; secure native keyboard")]
    [TypeGroup("Field", 4)]
    public sealed partial class PasswordBehavior : SingleLineBehavior
    {
        /// <summary>Glyph shown for every hidden character.</summary>
        [SerializeField, StateProperty(nameof(ApplyMaskCharacterChange))]
        [Tooltip("Single glyph shown for each hidden character (an emoji like 🔒 works). Only the first codepoint of a longer string is used — masking is one glyph per character.")]
        private string maskChar = "•";

        /// <summary>Whether copy and cut may expose the stored password.</summary>
        [SerializeField, StateProperty(nameof(ApplyCopyPolicyChange))]
        [Tooltip("Allow Copy/Cut of the value. Off (default) blocks it — the iOS/Android secure-field convention. Turn on for password-manager / reveal-and-copy flows.")]
        private bool allowCopy;

        [NonSerialized] private int maskCodepoint;
        [NonSerialized] private bool revealed;
        [NonSerialized] private Saved<bool> savedCopyBlocked;

        /// <summary>
        /// Show-password toggle for an "eye" control: when <see langword="true"/>, the text renders
        /// unmasked. Affects only this layer's rendering — in native-overlay mode the OS field owns its own
        /// reveal. Does not change <see cref="AllowCopy"/>.
        /// </summary>
        public bool Revealed
        {
            get => revealed;
            set
            {
                if (revealed == value) return;
                revealed = value;
                ApplyMask();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            editable.RequireSecureInput();
            CacheMaskCodepoint();
            ApplyMask();
            editable.CopyBlocked = savedCopyBlocked.Capture(editable.CopyBlocked, !allowCopy);
        }

        protected override void OnDisable()
        {
            editable.displayMaskCodepoint = 0;
            editable.CopyBlocked = savedCopyBlocked.Restore();
            editable.ReleaseSecureInput();
            editable.textDirty = true;
            base.OnDisable();
        }

        private void ApplyMask()
        {
            if (!IsEnabled) return;
            editable.displayMaskCodepoint = revealed ? 0 : maskCodepoint;
            editable.textDirty = true;
        }

        private void CacheMaskCodepoint()
        {
            maskCodepoint = string.IsNullOrEmpty(maskChar) ? '•' : (int)UnicodeData.DecodeAt(maskChar, 0, out _);
        }

        private void ApplyMaskCharacterChange()
        {
            CacheMaskCodepoint();
            ApplyMask();
            NotifyChanged(Members.MaskChar);
        }

        private void ApplyCopyPolicyChange()
        {
            if (IsEnabled) editable.CopyBlocked = !allowCopy;
            NotifyChanged(Members.AllowCopy);
        }
    }
}
