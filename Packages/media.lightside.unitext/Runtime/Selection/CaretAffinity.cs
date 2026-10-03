namespace LightSide
{
    /// <summary>
    /// Visual side of the caret at an ambiguous boundary — either a soft-wrap line break
    /// (right edge of the upstream line vs left edge of the downstream line) or a BiDi run
    /// boundary (LTR-end vs RTL-start). The same hint resolves both cases (Mozilla unified
    /// hint pattern: see https://bugzilla.mozilla.org/show_bug.cgi?id=1213589). Without an
    /// explicit affinity the caret position at such boundaries is undefined: codepoint
    /// indices alone cannot disambiguate "end of line N" from "start of line N+1" when both
    /// map to the same buffer offset.
    /// </summary>
    public enum CaretAffinity
    {
        /// <summary>
        /// Caret renders at the left edge of the downstream side — start of line N+1 at a
        /// soft wrap, or start of an RTL run at a BiDi boundary. Default when nothing else
        /// dictates: forward typing, click landing inside a glyph, programmatic SetCaret
        /// without a specified affinity. <see cref="default"/> of this enum and of any
        /// enclosing <see cref="TextSelection"/>.
        /// </summary>
        Downstream = 0,

        /// <summary>
        /// Caret renders at the right edge of the upstream side — end of line N at a soft
        /// wrap, or end of an LTR run at a BiDi boundary. Set explicitly by End / Down /
        /// any movement that lands the caret at the close of its visual line.
        /// </summary>
        Upstream = 1,
    }
}
