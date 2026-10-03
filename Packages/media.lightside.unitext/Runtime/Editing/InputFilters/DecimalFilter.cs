using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Filters decimal input: digits [0-9], at most one decimal separator (<c>.</c>, <c>,</c>, or the
    /// current culture's), plus a single leading minus sign when <see cref="AllowNegative"/>. The
    /// separator is kept as typed — normalization is a commit concern, not the filter's. The negative
    /// option also switches the mobile keyboard to a layout that has a minus key — the iOS decimal
    /// pad does not.
    /// </summary>
    [Serializable]
    [TypeGroup("Filtering", 0)]
    public sealed partial class DecimalFilter : InputFilterBase
    {
        /// <summary>Whether a single leading minus sign is accepted and a signed-capable keyboard requested.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Accept a leading minus sign. Also selects a mobile keyboard that has a minus key.")]
        private bool allowNegative;

        public override KeyboardType PreferredKeyboardType
            => allowNegative ? KeyboardType.NumbersAndPunctuation : KeyboardType.DecimalPad;

        /// <inheritdoc/>
        public override bool Allows(in EditProposal proposal)
        {
            var sep = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            char cultureSep = sep.Length > 0 ? sep[0] : '.';
            bool hasSeparator = DocumentContains(in proposal, '.') || DocumentContains(in proposal, ',')
                                || DocumentContains(in proposal, cultureSep);
            var input = proposal.Inserted;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c >= '0' && c <= '9') continue;
                if (c == '.' || c == ',' || c == cultureSep)
                {
                    if (hasSeparator) return false;
                    hasSeparator = true;
                    continue;
                }
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
