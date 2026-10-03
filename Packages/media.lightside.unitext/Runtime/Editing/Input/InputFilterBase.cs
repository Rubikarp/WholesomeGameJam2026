using System;

namespace LightSide
{
    /// <summary>
    /// Base for input filters — they reject characters as they are typed (digits-only, email chars, …) by
    /// subscribing to <see cref="UniTextEditable.InputFilter"/>. A separate concern from validation
    /// (<see cref="InputValidatorBase"/>), which lets input through and judges the whole value for a visible status.
    /// </summary>
    /// <remarks>
    /// <see cref="Allows"/> receives the full <see cref="EditProposal"/> and must judge the POST-state:
    /// text replacing a selection is acceptable whenever the resulting value is acceptable, even if the
    /// pre-state already contained the typed characters (the replaced ones are going away). Native
    /// platforms deliver multi-char strings (macOS NSString, Android CharSequence), so handle codepoints,
    /// surrogate pairs, and grapheme clusters. Same extensibility pattern as <see cref="BaseModifier"/> /
    /// <see cref="ParseRule"/>: stored via <c>[SerializeReference, TypeSelector]</c>; built-in and custom
    /// filters appear in the picker.
    /// </remarks>
    [Serializable]
    [TypeMenuSuffix("Filter")]
    public abstract class InputFilterBase : InputBehavior
    {
        private NativeKeyboardConfig preferredKeyboardConfig;

        protected override void OnEnable()
        {
            editable.InputFilter.Subscribe(Filter);
            editable.KeyboardResolver.Subscribe(ResolveKeyboard);
        }

        protected override void OnDisable()
        {
            editable.InputFilter.Unsubscribe(Filter);
            editable.KeyboardResolver.Unsubscribe(ResolveKeyboard);
        }

        private void Filter(ref InputEdit edit)
        {
            if (edit.Rejected || string.IsNullOrEmpty(edit.text)) return;
            var proposal = new EditProposal(
                edit.document,
                new TextRange(edit.insertCodepointIndex, edit.replacedCodepoints),
                edit.text.AsSpan(),
                edit.Reason);
            if (!Allows(in proposal))
                edit.Rejected = true;
        }

        /// <summary>
        /// Applies <see cref="PreferredKeyboardType"/> only when nothing else configured the
        /// keyboard: an explicit <see cref="NativeKeyboardBehavior"/> wins regardless of hook
        /// order — it assigns a whole config, while this fills in a null one.
        /// </summary>
        private void ResolveKeyboard(ref KeyboardRequest request)
        {
            var preferred = PreferredKeyboardType;
            if (preferred == KeyboardType.Default || request.config != null) return;
            preferredKeyboardConfig ??= new NativeKeyboardConfig();
            preferredKeyboardConfig.KeyboardType = preferred;
            request.config = preferredKeyboardConfig;
        }

        /// <summary>
        /// Whether the proposed edit may be applied, judged on the post-state it produces. Return
        /// <see langword="false"/> to drop it; the editor then discards it without firing
        /// <c>TextChanged</c>. Pure deletions never reach this — character filters do not block deleting.
        /// </summary>
        public abstract bool Allows(in EditProposal proposal);

        /// <summary>
        /// Preferred mobile keyboard type. The input field uses this when no
        /// <see cref="NativeKeyboardBehavior"/> overrides it.
        /// </summary>
        public virtual KeyboardType PreferredKeyboardType => KeyboardType.Default;

        /// <summary>
        /// Returns <see langword="true"/> if the document text SURVIVING the proposal (everything
        /// outside <see cref="EditProposal.ReplacedRange"/>) contains <paramref name="targetCodepoint"/> —
        /// the post-state membership test, minus the inserted text the filter is already iterating.
        /// O(document length) per call: chunked span scans keep the constant small, but a custom
        /// filter calling this per inserted character on a multiline document is O(n) per keystroke.
        /// </summary>
        protected static bool DocumentContains(in EditProposal proposal, int targetCodepoint)
        {
            var document = proposal.Document;
            return RangeContains(document, 0, proposal.ReplacedRange.start, targetCodepoint)
                   || RangeContains(document, proposal.ReplacedRange.End, document.CodepointCount, targetCodepoint);
        }

        private const int ScanChunkCodepoints = 128;

        /// <summary>
        /// Chunked vectorised membership scan over <c>[startCodepoint, endCodepoint)</c> —
        /// <see cref="MemoryExtensions.IndexOf{T}(ReadOnlySpan{T}, T)"/> over copied char chunks
        /// is several times faster than a per-codepoint virtual <see cref="ITextDocument.GetCodepointAt"/>
        /// walk. Chunks are cut on codepoint boundaries, so a surrogate-pair target never straddles two.
        /// </summary>
        private static bool RangeContains(ITextDocument document, int startCodepoint, int endCodepoint, int targetCodepoint)
        {
            if (endCodepoint <= startCodepoint) return false;
            Span<char> target = stackalloc char[2];
            var targetLen = UnicodeData.EncodeUtf16(targetCodepoint, target, 0);
            Span<char> chunk = stackalloc char[ScanChunkCodepoints * 2];

            for (var cp = startCodepoint; cp < endCodepoint; cp += ScanChunkCodepoints)
            {
                var count = Math.Min(ScanChunkCodepoints, endCodepoint - cp);
                var written = document.CopyCodepointRange(cp, count, chunk);
                var span = (ReadOnlySpan<char>)chunk.Slice(0, written);
                if (targetLen == 1)
                {
                    if (span.IndexOf(target[0]) >= 0) return true;
                }
                else if (span.IndexOf(target.Slice(0, 2)) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a <paramref name="sign"/> char at <paramref name="insertedCharOffset"/> within
        /// <see cref="EditProposal.Inserted"/> ends up as a single leading sign in the post-state:
        /// first inserted char, nothing surviving before it, no other sign surviving. Shared by the
        /// numeric filters.
        /// </summary>
        protected static bool AcceptsLeadingSign(in EditProposal proposal, int insertedCharOffset, char sign)
            => proposal.ReplacedRange.start == 0
               && insertedCharOffset == 0
               && !DocumentContains(in proposal, sign);
    }
}
