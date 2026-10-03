using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Provides grapheme-cluster-aware cursor navigation over <see cref="UniTextBuffers.graphemeBreaks"/> data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All methods operate on codepoint indices and use the grapheme break array produced by
    /// <see cref="GraphemeBreaker"/>. The break array has length <c>codepointCount + 1</c>, where
    /// <c>graphemeBreaks[i] == true</c> indicates a grapheme cluster boundary before codepoint <c>i</c>.
    /// Both <c>graphemeBreaks[0]</c> and <c>graphemeBreaks[codepointCount]</c> are always <see langword="true"/>.
    /// </para>
    /// <para>
    /// Cursor positions (caret positions) always sit on grapheme cluster boundaries. A cursor at
    /// codepoint index <c>i</c> means the caret is placed before codepoint <c>i</c> (or after the
    /// last codepoint when <c>i == codepointCount</c>).
    /// </para>
    /// </remarks>
    internal static class GraphemeNavigator
    {
        /// <summary>
        /// Returns the codepoint index at the start of the next grapheme cluster boundary
        /// after <paramref name="codepointIndex"/>.
        /// </summary>
        /// <param name="graphemeBreaks">
        /// Grapheme boundary flags with length <c>codepointCount + 1</c>.
        /// </param>
        /// <param name="codepointIndex">Current codepoint index (cursor position).</param>
        /// <returns>
        /// The next grapheme cluster boundary, or <c>codepointCount</c> (end of text)
        /// if already at or past the last boundary.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NextGraphemeCluster(ReadOnlySpan<bool> graphemeBreaks, int codepointIndex)
        {
            Debug.Assert(graphemeBreaks.Length > 0, "GraphemeNavigator: graphemeBreaks span is empty");
            Debug.Assert(codepointIndex >= 0, "GraphemeNavigator.NextGraphemeCluster: negative index");

            var codepointCount = graphemeBreaks.Length - 1;

            if (codepointIndex >= codepointCount)
                return codepointCount;

            for (var i = codepointIndex + 1; i <= codepointCount; i++)
            {
                if (graphemeBreaks[i])
                    return i;
            }

            return codepointCount;
        }

        /// <summary>
        /// Returns the codepoint index at the start of the previous grapheme cluster boundary
        /// before <paramref name="codepointIndex"/>.
        /// </summary>
        /// <param name="graphemeBreaks">
        /// Grapheme boundary flags with length <c>codepointCount + 1</c>.
        /// </param>
        /// <param name="codepointIndex">Current codepoint index (cursor position).</param>
        /// <returns>
        /// The previous grapheme cluster boundary, or <c>0</c> if already at or before
        /// the start of the text.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PreviousGraphemeCluster(ReadOnlySpan<bool> graphemeBreaks, int codepointIndex)
        {
            Debug.Assert(graphemeBreaks.Length > 0, "GraphemeNavigator: graphemeBreaks span is empty");
            Debug.Assert(codepointIndex >= 0, "GraphemeNavigator.PreviousGraphemeCluster: negative index");

            if (codepointIndex <= 0)
                return 0;

            var start = codepointIndex - 1;
            if (start >= graphemeBreaks.Length)
                start = graphemeBreaks.Length - 2;

            for (var i = start; i >= 0; i--)
            {
                if (graphemeBreaks[i])
                    return i;
            }

            return 0;
        }

        /// <summary>
        /// Snaps a codepoint index to the nearest grapheme cluster boundary.
        /// If <paramref name="codepointIndex"/> is already on a boundary, it is returned unchanged.
        /// Otherwise, the boundary at or before the index is returned (snaps to cluster start).
        /// </summary>
        /// <param name="graphemeBreaks">
        /// Grapheme boundary flags with length <c>codepointCount + 1</c>.
        /// </param>
        /// <param name="codepointIndex">The codepoint index to snap.</param>
        /// <returns>The nearest grapheme cluster boundary at or before the given index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SnapToClusterBoundary(ReadOnlySpan<bool> graphemeBreaks, int codepointIndex)
        {
            Debug.Assert(graphemeBreaks.Length > 0, "GraphemeNavigator: graphemeBreaks span is empty");
            Debug.Assert(codepointIndex >= 0, "GraphemeNavigator.SnapToClusterBoundary: negative index");

            var codepointCount = graphemeBreaks.Length - 1;

            if (codepointIndex <= 0)
                return 0;
            if (codepointIndex >= codepointCount)
                return codepointCount;
            if (graphemeBreaks[codepointIndex])
                return codepointIndex;

            for (var i = codepointIndex - 1; i >= 0; i--)
            {
                if (graphemeBreaks[i])
                    return i;
            }

            return 0;
        }
    }
}
