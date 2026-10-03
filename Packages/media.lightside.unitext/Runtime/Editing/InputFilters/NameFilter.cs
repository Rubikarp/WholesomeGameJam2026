using System;

namespace LightSide
{
    /// <summary>Filters name input: Unicode letters, space, hyphen, and apostrophes (incl. U+2019).</summary>
    [Serializable]
    [TypeGroup("Filtering", 0)]
    public sealed class NameFilter : InputFilterBase
    {
        /// <inheritdoc/>
        public override bool Allows(in EditProposal proposal)
        {
            var input = proposal.Inserted;
            for (int i = 0; i < input.Length;)
            {
                int cp = (int)UnicodeData.DecodeAt(input, i, out int size);
                i += size;
                if (UnicodeData.IsLetter(cp)) continue;
                if (cp == ' ' || cp == '-' || cp == '\'' || cp == UnicodeData.RightSingleQuotationMark) continue;
                return false;
            }
            return true;
        }
    }
}
