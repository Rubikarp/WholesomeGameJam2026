using System;

namespace LightSide
{
    /// <summary>
    /// Read-only view of a text document with codepoint-indexed access. Implemented by
    /// <see cref="UniTextEditable"/>; consumed by generic text-aware features (find /
    /// replace, validators, accessibility readers) that should not depend on the concrete
    /// editor type.
    /// </summary>
    public interface ITextDocument
    {
        /// <summary>Number of Unicode codepoints in the document. Surrogate pairs count as one.</summary>
        int CodepointCount { get; }

        /// <summary>Number of UTF-16 chars in the document — the unit Win32 / AppKit / UIKit IMEs and most string APIs use.</summary>
        int CharCount { get; }

        /// <summary>Monotonically increasing counter incremented on every mutation. Lets consumers cache derived state and invalidate cheaply.</summary>
        int Version { get; }

        /// <summary>Copies <paramref name="codepointCount"/> codepoints starting at <paramref name="startCodepoint"/> into <paramref name="destination"/>. Returns the number of UTF-16 chars written.</summary>
        int CopyCodepointRange(int startCodepoint, int codepointCount, Span<char> destination);

        /// <summary>Returns the Unicode codepoint at <paramref name="codepointIndex"/>. Out-of-range indices return 0.</summary>
        int GetCodepointAt(int codepointIndex);
    }
}
