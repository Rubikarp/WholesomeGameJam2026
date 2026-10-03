using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Bit ownership registry for <see cref="UniTextBuffers.hiddenClusters"/>. <see cref="Collapse"/>
    /// removes codepoints before itemization/shaping; other channels may additionally be selected by
    /// a layout pass. Each producer clears exclusively its own bits.
    /// </summary>
    internal static class HiddenClusterBits
    {
        public const byte Ellipsis = 1;
        public const byte Collapse = 2;
        public const byte Reveal = 4;
        public const byte Scramble = 8;
        public const byte Rolling = 16;
        public const byte Replacement = 32;

        /// <summary>
        /// Taken out of the layout after shaping, by a producer that asked for it through
        /// <c>RebreakHidden</c>. The clusters keep their shaping and their pristine widths — the
        /// channel for hiding decided too late for <see cref="Collapse"/>, which must be known before
        /// itemization, and the one a frontier numbering its own lines depends on.
        /// </summary>
        public const byte Reflow = 64;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsHidden(ReadOnlySpan<byte> flags, int cluster, byte mask)
            => mask != 0 && (uint)cluster < (uint)flags.Length && (flags[cluster] & mask) != 0;
    }
}
