using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Keyboard layouts with defined iOS, Android, and Web mappings.</summary>
    /// <remarks>iOS-only layouts are selected through <see cref="NativeKeyboardConfig.IOSKeyboardTypeOverride"/>.</remarks>
    public enum KeyboardType
    {
        /// <summary>Use the system default keyboard.</summary>
        Default = 0,
        ASCIICapable,
        NumbersAndPunctuation,
        URL,
        NumberPad,
        PhonePad,
        EmailAddress,
        DecimalPad,
        WebSearch,
    }

    /// <summary>Return-key actions with defined iOS, Android, and Web mappings.</summary>
    /// <remarks>
    /// The key performs what it declares. A field that accepts line breaks spends the key on them,
    /// so it always shows the plain return key and its declared action reaches the native field
    /// presenter instead, which surfaces it as its own control. iOS-only labels are selected through
    /// <see cref="NativeKeyboardConfig.IOSReturnKeyOverride"/>.
    /// </remarks>
    public enum ReturnKeyType
    {
        /// <summary>Done for a field that accepts no line breaks, the plain return key otherwise.</summary>
        Default = 0,
        Go,
        Search,
        Send,
        Next,
        Previous,
        Done,
        Enter,
    }

    /// <summary>Automatic capitalization policy requested from native text input.</summary>
    public enum AutoCapitalization
    {
        /// <summary>Use the user's system preference.</summary>
        Default = 0,
        /// <summary>No automatic capitalization.</summary>
        None,
        /// <summary>Capitalize the first letter of each word.</summary>
        Words,
        /// <summary>Capitalize the first letter of each sentence.</summary>
        Sentences,
        /// <summary>Capitalize all characters.</summary>
        AllCharacters,
    }

    /// <summary>Autocorrection policy requested from native text input.</summary>
    public enum AutoCorrection
    {
        /// <summary>Use the user's system preference.</summary>
        Default = 0,
        /// <summary>Force autocorrection on.</summary>
        Enabled,
        /// <summary>Force autocorrection off.</summary>
        Disabled,
    }

    /// <summary>Spell-checking policy requested from native text input.</summary>
    /// <remarks>Android maps <see cref="Disabled"/> to its combined no-suggestions behavior.</remarks>
    public enum SpellChecking
    {
        /// <summary>Use the user's system preference.</summary>
        Default = 0,
        /// <summary>Force spell checking on.</summary>
        Enabled,
        /// <summary>Force spell checking off.</summary>
        Disabled,
    }

    /// <summary>Semantic field purposes exposed to native autofill services.</summary>
    public enum AutofillHint
    {
        /// <summary>Supplies no semantic autofill hint.</summary>
        None = 0,
        Username,
        Password,
        NewPassword,
        OneTimeCode,
        EmailAddress,
        TelephoneNumber,
        Name,
        GivenName,
        FamilyName,
        PostalAddress,
        PostalCode,
        CreditCardNumber,
        URL,
    }

    /// <summary>Tri-state policy for an iOS smart-input feature.</summary>
    public enum SmartFeatureMode
    {
        /// <summary>Follow the user's system preference (default).</summary>
        Default = 0,
        /// <summary>Force the feature on.</summary>
        Yes = 1,
        /// <summary>Force the feature off.</summary>
        No = 2,
    }

    /// <summary>Optional Android IME policies combined as flags.</summary>
    [Flags]
    public enum AndroidImeFlags
    {
        /// <summary>No additional IME flags.</summary>
        None                   = 0,
        /// <summary>Incognito keyboard mode — disable personalized learning.</summary>
        NoPersonalizedLearning = 1 << 0,
        /// <summary>Request an ASCII-capable IME.</summary>
        ForceAscii             = 1 << 1,
    }

    /// <summary>Native software-keyboard and text-input traits for an editing session.</summary>
    /// <remarks>
    /// Every enum defaults to <c>Default = 0</c>, leaving platform policy intact except
    /// <see cref="ReturnKeyType.Default"/>, which follows whether the field accepts line breaks.
    /// </remarks>
    [Serializable]
    public sealed partial class NativeKeyboardConfig
    {
        [NonSerialized] private NestedStateChangedHandler changed;
        [NonSerialized] private int version;

        /// <summary>Keyboard layout presented by the OS.</summary>
        [Header("Cross-Platform")]
        [Tooltip("Keyboard layout presented by the OS (number pad, email, URL, etc.).")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private KeyboardType keyboardType;

        /// <summary>Action the return key declares, which also selects its label.</summary>
        [Tooltip("Action the return key declares (Go, Search, Done, etc.), which also selects its " +
                 "label. A field that accepts line breaks keeps the plain return key and gets a " +
                 "separate button for this action on the native field.")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private ReturnKeyType returnKeyType;

        /// <summary>Automatic capitalization behavior.</summary>
        [Tooltip("Automatic capitalization (none, words, sentences, all).")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private AutoCapitalization autoCapitalization;

        /// <summary>Whether the OS autocorrects typed text.</summary>
        [Tooltip("Whether the OS autocorrects typed text.")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private AutoCorrection autoCorrection;

        /// <summary>Whether the OS provides inline spell-checking.</summary>
        [Tooltip("Whether the OS provides inline spell-checking.")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private SpellChecking spellChecking;

        /// <summary>Semantic hint for the OS autofill system.</summary>
        [Tooltip("Autofill hint for the OS (username, password, email, etc.).")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private AutofillHint autofillHint;

        /// <summary>Gets or sets automatic quote substitution on iOS.</summary>
        [Header("iOS Only")]
        [Tooltip("Automatically convert straight quotes to curly quotes (iOS only). Set to No for code / regex / API token fields.")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private SmartFeatureMode smartQuotes;

        /// <summary>Gets or sets automatic dash substitution on iOS.</summary>
        [Tooltip("Automatically convert double hyphens to em-dashes (iOS only). Set to No for code / CLI fields.")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private SmartFeatureMode smartDashes;

        /// <summary>Gets or sets automatic whitespace adjustment around edits on iOS.</summary>
        [Tooltip("Automatically add/remove whitespace around pasted text (iOS only). Set to No for code editors.")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private SmartFeatureMode smartInsertDelete;

        /// <summary>Gets or sets whether iOS enables the return key only for non-empty text.</summary>
        [Tooltip("Dim the return key when the field is empty (iOS only).")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private bool enablesReturnKeyAutomatically;

        /// <summary>Gets or sets iOS password-generation rules, or an empty value for none.</summary>
        [Tooltip("Password generation rules for iOS Password AutoFill (iOS only). Leave empty for no rules.")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private string passwordRules;

        /// <summary>Gets or sets a supported iOS-only keyboard type name that overrides <see cref="KeyboardType"/>.</summary>
        [Tooltip("iOS-only keyboard by UIKeyboardType name (twitter, namePhonePad, asciiCapableNumberPad). Overrides Keyboard Type on iOS; ignored elsewhere.")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private string iOSKeyboardTypeOverride;

        /// <summary>Gets or sets a supported iOS-only return-key name that overrides <see cref="ReturnKeyType"/>.</summary>
        [Tooltip("iOS-only return key by UIReturnKeyType name (google, yahoo, route, join, emergencyCall, continue). Overrides Return Key Type on iOS; ignored elsewhere.")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private string iOSReturnKeyOverride;

        /// <summary>Gets or sets optional Android IME policies.</summary>
        [Header("Android Only")]
        [Tooltip("Additional Android IME flags, such as private-learning suppression or forced ASCII input.")]
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private AndroidImeFlags imeFlags;

        /// <summary>Creates a keyboard configuration using platform defaults.</summary>
        public NativeKeyboardConfig() { }

        /// <summary>Monotonic count of applied member changes.</summary>
        internal int Version => version;

        internal void SetChangeCallback(NestedStateChangedHandler callback)
        {
            changed = callback;
        }

        internal bool HasChangeCallback(NestedStateChangedHandler callback)
            => changed?.Equals(callback) == true;

        internal void Validate()
        {
            if ((uint)keyboardType > (uint)KeyboardType.WebSearch)
                throw new ArgumentOutOfRangeException(nameof(keyboardType), keyboardType,
                    "The keyboard type is undefined.");
            if ((uint)returnKeyType > (uint)ReturnKeyType.Enter)
                throw new ArgumentOutOfRangeException(nameof(returnKeyType), returnKeyType,
                    "The return-key type is undefined.");
            if ((uint)autoCapitalization > (uint)AutoCapitalization.AllCharacters)
                throw new ArgumentOutOfRangeException(nameof(autoCapitalization), autoCapitalization,
                    "The capitalization mode is undefined.");
            if ((uint)autoCorrection > (uint)AutoCorrection.Disabled)
                throw new ArgumentOutOfRangeException(nameof(autoCorrection), autoCorrection,
                    "The autocorrection mode is undefined.");
            if ((uint)spellChecking > (uint)SpellChecking.Disabled)
                throw new ArgumentOutOfRangeException(nameof(spellChecking), spellChecking,
                    "The spell-checking mode is undefined.");
            if ((uint)autofillHint > (uint)AutofillHint.URL)
                throw new ArgumentOutOfRangeException(nameof(autofillHint), autofillHint,
                    "The autofill hint is undefined.");
            if ((uint)smartQuotes > (uint)SmartFeatureMode.No)
                throw new ArgumentOutOfRangeException(nameof(smartQuotes), smartQuotes,
                    "The smart-quotes mode is undefined.");
            if ((uint)smartDashes > (uint)SmartFeatureMode.No)
                throw new ArgumentOutOfRangeException(nameof(smartDashes), smartDashes,
                    "The smart-dashes mode is undefined.");
            if ((uint)smartInsertDelete > (uint)SmartFeatureMode.No)
                throw new ArgumentOutOfRangeException(nameof(smartInsertDelete), smartInsertDelete,
                    "The smart insert-delete mode is undefined.");
            const AndroidImeFlags knownImeFlags = AndroidImeFlags.NoPersonalizedLearning |
                                                   AndroidImeFlags.ForceAscii;
            if ((imeFlags & ~knownImeFlags) != 0)
                throw new ArgumentOutOfRangeException(nameof(imeFlags), imeFlags,
                    "The Android IME flags contain undefined bits.");
            if (!string.IsNullOrEmpty(iOSKeyboardTypeOverride) &&
                !iOSKeyboardTypeOverride.EqualsIgnoreCase("namePhonePad") &&
                !iOSKeyboardTypeOverride.EqualsIgnoreCase("twitter") &&
                !iOSKeyboardTypeOverride.EqualsIgnoreCase("asciiCapableNumberPad"))
                throw new ArgumentException("The iOS keyboard-type override is unknown.",
                    nameof(iOSKeyboardTypeOverride));
            if (!string.IsNullOrEmpty(iOSReturnKeyOverride) &&
                !iOSReturnKeyOverride.EqualsIgnoreCase("google") &&
                !iOSReturnKeyOverride.EqualsIgnoreCase("join") &&
                !iOSReturnKeyOverride.EqualsIgnoreCase("route") &&
                !iOSReturnKeyOverride.EqualsIgnoreCase("yahoo") &&
                !iOSReturnKeyOverride.EqualsIgnoreCase("emergencyCall") &&
                !iOSReturnKeyOverride.EqualsIgnoreCase("continue"))
                throw new ArgumentException("The iOS return-key override is unknown.",
                    nameof(iOSReturnKeyOverride));
        }

        private void NotifyChanged(StateMember member)
        {
            version++;
            changed?.Invoke(this, member);
        }

    }
}
