namespace LightSide
{
    /// <summary>
    /// The shape of one document mutation: <see cref="Removed"/> codepoints replaced by
    /// <see cref="Inserted"/> codepoints at <see cref="Start"/>. Produced by every gap-buffer
    /// mutation; consumers use it to patch derived indexed state (range maps, cached
    /// counts, undo history) instead of recomputing from the whole document.
    /// </summary>
    public readonly struct EditShape
    {
        public readonly int Start;
        public readonly int Removed;
        public readonly int Inserted;

        /// <summary>Net change in document codepoint count.</summary>
        public int Delta => Inserted - Removed;

        public EditShape(int start, int removed, int inserted)
        {
            Start = start;
            Removed = removed;
            Inserted = inserted;
        }

        /// <summary>
        /// Maps a codepoint index valid before this edit to the equivalent index after it:
        /// indices before <see cref="Start"/> are unchanged, indices at or after the removed
        /// range shift by <see cref="Delta"/> (an index exactly at a pure insertion point moves
        /// after the inserted text), and indices strictly inside the removed range collapse
        /// to <see cref="Start"/>.
        /// </summary>
        public int MapIndex(int oldIndex)
        {
            if (oldIndex < Start) return oldIndex;
            if (oldIndex >= Start + Removed) return oldIndex + Inserted - Removed;
            return Start;
        }
    }
}
