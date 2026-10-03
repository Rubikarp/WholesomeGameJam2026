using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Iterates one range's codepoints as grapheme clusters over the revealable ordinal axis shared
    /// by reveal-style modifiers: cluster leads that are not mandatory breaks — and, optionally,
    /// not spaces — receive consecutive ordinals, and followers carry their lead's eligibility.
    /// </summary>
    internal ref struct RevealableClusterWalk
    {
        private readonly ReadOnlySpan<int> cps;
        private readonly ReadOnlySpan<bool> breaks;
        private readonly bool excludeSpaces;
        private TextUnitWalk units;
        private int index;
        private bool eligible;
        private int ordinal;

        /// <summary>Current codepoint offset from the range start.</summary>
        public int Index => index;

        /// <summary>Whether the current codepoint starts a grapheme cluster.</summary>
        public bool IsLead => breaks[index];

        /// <summary>Whether the current cluster sits on the ordinal axis; carried to followers.</summary>
        public bool Eligible => eligible;

        /// <summary>Zero-based axis ordinal of the current eligible cluster; clusters of one unit share it.</summary>
        public int Ordinal => ordinal - 1;

        /// <summary>Units the walk has opened so far.</summary>
        public int UnitCount => ordinal;

        private RevealableClusterWalk(ReadOnlySpan<int> cps, ReadOnlySpan<bool> breaks,
            bool excludeSpaces, TextUnitWalk units)
        {
            this.cps = cps;
            this.breaks = breaks;
            this.excludeSpaces = excludeSpaces;
            this.units = units;
            index = -1;
            eligible = false;
            ordinal = 0;
        }

        /// <summary>
        /// Fills <paramref name="breaks"/> with the span's break opportunities and returns a walk
        /// positioned before the first codepoint.
        /// </summary>
        public static RevealableClusterWalk Over(ReadOnlySpan<int> cps, Span<bool> breaks,
            bool excludeSpaces)
            => Over(cps, breaks, excludeSpaces,
                new TextUnitWalk(TextUnit.Cluster, cps, default, default, 0));

        /// <inheritdoc cref="Over(ReadOnlySpan{int}, Span{bool}, bool)"/>
        /// <param name="units">Numbering the ordinal follows; clusters inside one unit share its ordinal.</param>
        public static RevealableClusterWalk Over(ReadOnlySpan<int> cps, Span<bool> breaks,
            bool excludeSpaces, TextUnitWalk units)
        {
            SharedPipelineComponents.GraphemeBreaker.GetBreakOpportunities(cps, breaks);
            return new RevealableClusterWalk(cps, breaks, excludeSpaces, units);
        }

        /// <summary>Counts the eligible cluster leads of an already-broken span.</summary>
        public static int CountEligible(ReadOnlySpan<int> cps, ReadOnlySpan<bool> breaks,
            bool excludeSpaces)
        {
            var total = 0;
            for (var i = 0; i < cps.Length; i++)
                if (breaks[i] && !Excluded(cps[i], excludeSpaces))
                    total++;
            return total;
        }

        /// <summary>Whether a cluster-lead codepoint stays off the revealable ordinal axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Excluded(int cp, bool excludeSpaces)
            => UnicodeData.IsMandatoryBreakChar(cp) || (excludeSpaces && cp == ' ');

        /// <summary>Advances to the next codepoint of the range.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (++index >= cps.Length) return false;
            if (breaks[index])
            {
                eligible = !Excluded(cps[index], excludeSpaces);
                if (!eligible) units.Skip(index);
                else if (units.Starts(index)) ordinal++;
            }
            return true;
        }
    }
}
