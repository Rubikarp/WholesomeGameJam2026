using System;

namespace LightSide
{
    /// <summary>Filters alphanumeric input: Unicode letters and ASCII digits [0-9].</summary>
    [Serializable]
    [TypeGroup("Filtering", 0)]
    public sealed class AlphanumericFilter : InputFilterBase
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
                if (cp >= '0' && cp <= '9') continue;
                return false;
            }
            return true;
        }
    }
}
