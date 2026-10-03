using System;

namespace LightSide
{
    /// <summary>
    /// A full pending edit as an input filter sees it: <see cref="Inserted"/> text about to replace
    /// <see cref="ReplacedRange"/> in the pre-edit <see cref="Document"/> (an empty range at the caret
    /// for a pure insert). Filters judge the post-state this proposal produces — replacing a selection
    /// is legal whenever the result is legal, regardless of what the replaced text contained.
    /// </summary>
    public readonly ref struct EditProposal
    {
        /// <summary>Pre-edit document, read-only.</summary>
        public readonly ITextDocument Document;

        /// <summary>Codepoint range being replaced. Empty at the caret for a pure insert.</summary>
        public readonly TextRange ReplacedRange;

        /// <summary>Replacement text. Empty for a pure deletion.</summary>
        public readonly ReadOnlySpan<char> Inserted;

        /// <summary>Why the edit is happening — a <see cref="TextChangeReason"/> constant.</summary>
        public readonly string Reason;

        public EditProposal(ITextDocument document, TextRange replacedRange, ReadOnlySpan<char> inserted, string reason)
        {
            Document = document;
            ReplacedRange = replacedRange;
            Inserted = inserted;
            Reason = reason;
        }
    }
}
