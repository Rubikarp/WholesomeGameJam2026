using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Anchor / focus / affinity selection over codepoint indices. <see cref="Anchor"/> is
    /// where the selection began; <see cref="Focus"/> is the current caret position (always
    /// where the caret is rendered). <see cref="Affinity"/> disambiguates caret rendering at
    /// soft-wrap and BiDi boundaries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Indices are codepoint offsets — the same space used by <see cref="UniTextBuffers.codepoints"/>
    /// and <see cref="PositionedGlyph.cluster"/>. Cursor movement at the consumer level is
    /// grapheme-cluster-aware (via <see cref="GraphemeNavigator"/>), but the storage
    /// representation is codepoints (D-006).
    /// </para>
    /// <para>
    /// Direction is implicit in the <see cref="Anchor"/>/<see cref="Focus"/> ordering — matches
    /// the W3C Selection API and Flutter <c>TextSelection</c> conventions. Multi-cursor
    /// (P3 code-editor) is reserved as a non-breaking <c>IReadOnlyList&lt;TextSelection&gt;</c>
    /// overload alongside the single-region property (D-001 revised).
    /// </para>
    /// <para>
    /// Immutable — mutations go through <see cref="UniTextSelectable"/> named methods
    /// (<c>SetCaret</c> / <c>SetSelection</c> / <c>ExtendSelection</c>). Direct construction
    /// is allowed for read-only producers (parsers, deserializers, snapshot diffs).
    /// </para>
    /// </remarks>
    public readonly struct TextSelection : IEquatable<TextSelection>
    {
        /// <summary>
        /// Codepoint index where the selection originated. Equals <see cref="Focus"/> when
        /// the selection is collapsed (caret only).
        /// </summary>
        public int Anchor { get; }

        /// <summary>
        /// Current codepoint index of the cursor — the moving end of the selection. The
        /// caret is rendered here.
        /// </summary>
        public int Focus { get; }

        /// <summary>
        /// Visual side of the caret at a soft-wrap or BiDi run boundary. See
        /// <see cref="CaretAffinity"/>.
        /// </summary>
        public CaretAffinity Affinity { get; }

        /// <summary>
        /// Constructs a selection. <paramref name="anchor"/> equal to <paramref name="focus"/>
        /// produces a collapsed caret at that position.
        /// </summary>
        public TextSelection(int anchor, int focus, CaretAffinity affinity = CaretAffinity.Downstream)
        {
            Anchor = anchor;
            Focus = focus;
            Affinity = affinity;
        }

        /// <summary>
        /// Caret-only selection at <paramref name="codepointIndex"/> with the given affinity.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TextSelection Caret(int codepointIndex, CaretAffinity affinity = CaretAffinity.Downstream)
            => new TextSelection(codepointIndex, codepointIndex, affinity);

        /// <summary>
        /// True when <see cref="Anchor"/> equals <see cref="Focus"/> — there is no selected
        /// range, only a caret. Matches the W3C <c>Selection.isCollapsed</c> name and semantics.
        /// </summary>
        public bool IsCollapsed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Anchor == Focus;
        }

        /// <summary>
        /// Smaller of <see cref="Anchor"/> and <see cref="Focus"/> — start of the selected
        /// range (inclusive).
        /// </summary>
        public int Start
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Anchor < Focus ? Anchor : Focus;
        }

        /// <summary>
        /// Larger of <see cref="Anchor"/> and <see cref="Focus"/> — end of the selected range
        /// (exclusive).
        /// </summary>
        public int End
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Anchor > Focus ? Anchor : Focus;
        }

        /// <summary>
        /// Length of the selected range in codepoints. Zero when collapsed.
        /// </summary>
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => End - Start;
        }

        /// <summary>
        /// Returns a new <see cref="TextSelection"/> with both endpoints clamped to
        /// <c>[0, <paramref name="codepointCount"/>]</c>. Returns the original instance when
        /// no clamping is needed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextSelection Clamp(int codepointCount)
        {
            int newAnchor = Anchor < 0 ? 0 : (Anchor > codepointCount ? codepointCount : Anchor);
            int newFocus = Focus < 0 ? 0 : (Focus > codepointCount ? codepointCount : Focus);
            if (newAnchor == Anchor && newFocus == Focus) return this;
            return new TextSelection(newAnchor, newFocus, Affinity);
        }

        /// <inheritdoc/>
        public bool Equals(TextSelection other)
            => Anchor == other.Anchor && Focus == other.Focus && Affinity == other.Affinity;

        /// <inheritdoc/>
        public override bool Equals(object obj)
            => obj is TextSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
            => HashCode.Combine(Anchor, Focus, (int)Affinity);

        public static bool operator ==(TextSelection left, TextSelection right) => left.Equals(right);
        public static bool operator !=(TextSelection left, TextSelection right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
            => IsCollapsed
                ? $"Caret[{Focus}, {Affinity}]"
                : $"Selection[anchor={Anchor}, focus={Focus}, {Affinity}]";
    }
}
