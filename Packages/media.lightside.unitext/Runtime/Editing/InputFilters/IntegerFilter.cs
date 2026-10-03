using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Filters integer input: digits [0-9], plus a single leading minus sign when
    /// <see cref="AllowNegative"/>. The negative option also switches the mobile keyboard to a layout
    /// that has a minus key — the iOS number pad does not.
    /// </summary>
    [Serializable]
    [TypeGroup("Filtering", 0)]
    public sealed partial class IntegerFilter : InputFilterBase
    {
        /// <summary>Whether a single leading minus sign is accepted and a signed-capable keyboard requested.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Accept a leading minus sign. Also selects a mobile keyboard that has a minus key.")]
        private bool allowNegative;

        public override KeyboardType PreferredKeyboardType
            => allowNegative ? KeyboardType.NumbersAndPunctuation : KeyboardType.NumberPad;

        /// <inheritdoc/>
        public override bool Allows(in EditProposal proposal)
        {
            var input = proposal.Inserted;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c >= '0' && c <= '9') continue;
                if (c == '-')
                {
                    if (!allowNegative || !AcceptsLeadingSign(in proposal, i, '-')) return false;
                    continue;
                }
                return false;
            }
            return true;
        }
    }
}
