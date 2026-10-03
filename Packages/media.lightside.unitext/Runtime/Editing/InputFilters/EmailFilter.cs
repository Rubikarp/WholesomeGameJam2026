using System;

namespace LightSide
{
    /// <summary>
    /// Filters email-address characters as they are typed: ASCII letters/digits, one <c>@</c> (not at start),
    /// <c>.</c> (not at start, not right after <c>@</c>), and <c>- _ + %</c>. A character filter, not a full
    /// RFC 5322 check — pair with a validator for completeness.
    /// </summary>
    [Serializable]
    [TypeGroup("Filtering", 0)]
    public sealed class EmailFilter : InputFilterBase
    {
        public override KeyboardType PreferredKeyboardType => KeyboardType.EmailAddress;

        /// <inheritdoc/>
        public override bool Allows(in EditProposal proposal)
        {
            var document = proposal.Document;
            var input = proposal.Inserted;
            bool hasAt = DocumentContains(in proposal, '@');
            int resultPosOfInsert = proposal.ReplacedRange.start;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                int pos = resultPosOfInsert + i;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) continue;
                if (c == '@')
                {
                    if (hasAt || pos == 0) return false;
                    hasAt = true;
                    continue;
                }
                if (c == '.')
                {
                    if (pos == 0) return false;
                    if (i > 0) { if (input[i - 1] == '@') return false; }
                    else if (resultPosOfInsert > 0 && document.GetCodepointAt(resultPosOfInsert - 1) == '@') return false;
                    continue;
                }
                if (c == '-' || c == '_' || c == '+' || c == '%') continue;
                return false;
            }
            return true;
        }
    }
}
