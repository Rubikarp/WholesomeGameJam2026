using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Stores the result of a LaTeX math symbol lookup: the Unicode codepoint and atom type.
    /// </summary>
    internal readonly struct MathSymbolInfo
    {
        /// <summary>Unicode codepoint that this command produces. -1 for accent commands.</summary>
        public readonly int codepoint;
        /// <summary>TeX atom type for inter-atom spacing.</summary>
        public readonly MathAtomType atomType;
        /// <summary>True if this symbol is a math accent (hat, tilde, etc.).</summary>
        public readonly bool isAccent;
        /// <summary>Unicode combining codepoint for accents. 0 for non-accents.</summary>
        public readonly int accentCodepoint;

        public MathSymbolInfo(int codepoint, MathAtomType atomType)
        {
            this.codepoint = codepoint;
            this.atomType = atomType;
            this.isAccent = false;
            this.accentCodepoint = 0;
        }

        public MathSymbolInfo(int codepoint, MathAtomType atomType, int accentCodepoint)
        {
            this.codepoint = codepoint;
            this.atomType = atomType;
            this.isAccent = true;
            this.accentCodepoint = accentCodepoint;
        }

    }


    /// <summary>
    /// Zero-allocation LaTeX math command to Unicode codepoint lookup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Maps LaTeX command names (without leading backslash) to their Unicode codepoints
    /// and TeX atom types. Uses a sorted array with binary search on FNV-1a hashes
    /// for O(log n) lookup with zero garbage allocation.
    /// </para>
    /// <para>
    /// Covers all standard LaTeX math commands: Greek letters (lowercase and uppercase,
    /// including variant forms), binary operators, relations, arrows, large operators,
    /// delimiters, miscellaneous symbols, and accents.
    /// </para>
    /// </remarks>
    internal static class MathSymbols
    {
        /// <summary>
        /// Tries to resolve a LaTeX math command name to its symbol information.
        /// </summary>
        /// <param name="commandName">Command name without the leading backslash (e.g. "alpha", "leq").</param>
        /// <param name="info">Receives the symbol information if found.</param>
        /// <returns>True if the command was found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryLookup(ReadOnlySpan<char> commandName, out MathSymbolInfo info)
        {
            var hash = ComputeHash(commandName);
            var lo = 0;
            var hi = entries.Length - 1;

            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                ref readonly var entry = ref entries[mid];

                if (hash < entry.hash)
                    hi = mid - 1;
                else if (hash > entry.hash)
                    lo = mid + 1;
                else
                {
                    if (NameEquals(commandName, entry.nameStart, entry.nameLength))
                    {
                        info = entry.isAccent
                            ? new MathSymbolInfo(entry.codepoint, entry.atomType, entry.accentCodepoint)
                            : new MathSymbolInfo(entry.codepoint, entry.atomType);
                        return true;
                    }

                    for (var i = mid - 1; i >= 0 && entries[i].hash == hash; i--)
                    {
                        ref readonly var e = ref entries[i];
                        if (NameEquals(commandName, e.nameStart, e.nameLength))
                        {
                            info = e.isAccent
                                ? new MathSymbolInfo(e.codepoint, e.atomType, e.accentCodepoint)
                                : new MathSymbolInfo(e.codepoint, e.atomType);
                            return true;
                        }
                    }

                    for (var i = mid + 1; i < entries.Length && entries[i].hash == hash; i++)
                    {
                        ref readonly var e = ref entries[i];
                        if (NameEquals(commandName, e.nameStart, e.nameLength))
                        {
                            info = e.isAccent
                                ? new MathSymbolInfo(e.codepoint, e.atomType, e.accentCodepoint)
                                : new MathSymbolInfo(e.codepoint, e.atomType);
                            return true;
                        }
                    }

                    break;
                }
            }

            info = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryLookupCodepoint(int codepoint, out MathAtomType atomType)
        {
            var lo = 0;
            var hi = codepointEntries.Length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                ref readonly var entry = ref codepointEntries[mid];
                if (codepoint < entry.codepoint)
                    hi = mid - 1;
                else if (codepoint > entry.codepoint)
                    lo = mid + 1;
                else
                {
                    atomType = entry.atomType;
                    return true;
                }
            }

            atomType = default;
            return false;
        }

        #region Hashing and Name Storage

        private static readonly char[] namePool;
        private static readonly SymbolEntry[] entries;
        private static readonly CodepointEntry[] codepointEntries;

        private readonly struct SymbolEntry : IComparable<SymbolEntry>
        {
            public readonly uint hash;
            public readonly ushort nameStart;
            public readonly byte nameLength;
            public readonly int codepoint;
            public readonly MathAtomType atomType;
            public readonly bool isAccent;
            public readonly int accentCodepoint;

            public SymbolEntry(uint hash, ushort nameStart, byte nameLength, int codepoint,
                MathAtomType atomType, bool isAccent = false, int accentCodepoint = 0)
            {
                this.hash = hash;
                this.nameStart = nameStart;
                this.nameLength = nameLength;
                this.codepoint = codepoint;
                this.atomType = atomType;
                this.isAccent = isAccent;
                this.accentCodepoint = accentCodepoint;
            }

            public int CompareTo(SymbolEntry other) => hash.CompareTo(other.hash);
        }

        private readonly struct CodepointEntry : IComparable<CodepointEntry>
        {
            public readonly int codepoint;
            public readonly MathAtomType atomType;

            public CodepointEntry(int codepoint, MathAtomType atomType)
            {
                this.codepoint = codepoint;
                this.atomType = atomType;
            }

            public int CompareTo(CodepointEntry other) => codepoint.CompareTo(other.codepoint);
        }

        /// <summary>FNV-1a 32-bit hash for zero-allocation span hashing.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ComputeHash(ReadOnlySpan<char> text)
        {
            var hash = 2166136261u;
            for (var i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619u;
            }
            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool NameEquals(ReadOnlySpan<char> commandName, ushort nameStart, byte nameLength)
        {
            if (commandName.Length != nameLength) return false;
            for (var i = 0; i < nameLength; i++)
            {
                if (commandName[i] != namePool[nameStart + i]) return false;
            }
            return true;
        }

        #endregion

        #region Static Initialization

        static MathSymbols()
        {
            var defs = BuildDefinitions();
            var totalNameLength = 0;
            for (var i = 0; i < defs.Length; i++)
                totalNameLength += defs[i].name.Length;

            namePool = new char[totalNameLength];
            entries = new SymbolEntry[defs.Length];

            var nameOffset = 0;
            for (var i = 0; i < defs.Length; i++)
            {
                var name = defs[i].name;
                name.AsSpan().CopyTo(namePool.AsSpan(nameOffset));
                var hash = ComputeHash(name.AsSpan());
                entries[i] = new SymbolEntry(
                    hash,
                    (ushort)nameOffset,
                    (byte)name.Length,
                    defs[i].codepoint,
                    defs[i].atomType,
                    defs[i].isAccent,
                    defs[i].accentCodepoint);
                nameOffset += name.Length;
            }

            Array.Sort(entries);

            var codepoints = new CodepointEntry[defs.Length];
            var codepointCount = 0;
            for (var i = 0; i < defs.Length; i++)
            {
                ref readonly var definition = ref defs[i];
                if (definition.isAccent || definition.codepoint <= 0x7F)
                    continue;

                var duplicate = false;
                for (var j = 0; j < codepointCount; j++)
                {
                    if (codepoints[j].codepoint != definition.codepoint) continue;
                    duplicate = true;
                    break;
                }
                if (!duplicate)
                    codepoints[codepointCount++] = new CodepointEntry(
                        definition.codepoint, definition.atomType);
            }

            codepointEntries = new CodepointEntry[codepointCount];
            Array.Copy(codepoints, codepointEntries, codepointCount);
            Array.Sort(codepointEntries);
        }

        private readonly struct SymbolDef
        {
            public readonly string name;
            public readonly int codepoint;
            public readonly MathAtomType atomType;
            public readonly bool isAccent;
            public readonly int accentCodepoint;

            public SymbolDef(string name, int codepoint, MathAtomType atomType)
            {
                this.name = name;
                this.codepoint = codepoint;
                this.atomType = atomType;
                this.isAccent = false;
                this.accentCodepoint = 0;
            }

            public SymbolDef(string name, MathAtomType atomType, int accentCodepoint)
            {
                this.name = name;
                this.codepoint = -1;
                this.atomType = atomType;
                this.isAccent = true;
                this.accentCodepoint = accentCodepoint;
            }
        }

        private static SymbolDef[] BuildDefinitions()
        {
            return new[]
            {
                new SymbolDef("alpha", 0x03B1, MathAtomType.Ord),
                new SymbolDef("beta", 0x03B2, MathAtomType.Ord),
                new SymbolDef("gamma", 0x03B3, MathAtomType.Ord),
                new SymbolDef("delta", 0x03B4, MathAtomType.Ord),
                new SymbolDef("epsilon", 0x03F5, MathAtomType.Ord),
                new SymbolDef("zeta", 0x03B6, MathAtomType.Ord),
                new SymbolDef("eta", 0x03B7, MathAtomType.Ord),
                new SymbolDef("theta", 0x03B8, MathAtomType.Ord),
                new SymbolDef("iota", 0x03B9, MathAtomType.Ord),
                new SymbolDef("kappa", 0x03BA, MathAtomType.Ord),
                new SymbolDef("lambda", 0x03BB, MathAtomType.Ord),
                new SymbolDef("mu", 0x03BC, MathAtomType.Ord),
                new SymbolDef("nu", 0x03BD, MathAtomType.Ord),
                new SymbolDef("xi", 0x03BE, MathAtomType.Ord),
                new SymbolDef("omicron", 0x03BF, MathAtomType.Ord),
                new SymbolDef("pi", 0x03C0, MathAtomType.Ord),
                new SymbolDef("rho", 0x03C1, MathAtomType.Ord),
                new SymbolDef("sigma", 0x03C3, MathAtomType.Ord),
                new SymbolDef("tau", 0x03C4, MathAtomType.Ord),
                new SymbolDef("upsilon", 0x03C5, MathAtomType.Ord),
                new SymbolDef("phi", 0x03D5, MathAtomType.Ord),
                new SymbolDef("chi", 0x03C7, MathAtomType.Ord),
                new SymbolDef("psi", 0x03C8, MathAtomType.Ord),
                new SymbolDef("omega", 0x03C9, MathAtomType.Ord), new SymbolDef("varepsilon", 0x03B5, MathAtomType.Ord),
                new SymbolDef("vartheta", 0x03D1, MathAtomType.Ord),
                new SymbolDef("varpi", 0x03D6, MathAtomType.Ord),
                new SymbolDef("varrho", 0x03F1, MathAtomType.Ord),
                new SymbolDef("varsigma", 0x03C2, MathAtomType.Ord),
                new SymbolDef("varphi", 0x03C6, MathAtomType.Ord),
                new SymbolDef("varkappa", 0x03F0, MathAtomType.Ord),
                new SymbolDef("digamma", 0x03DD, MathAtomType.Ord), new SymbolDef("Gamma", 0x0393, MathAtomType.Ord),
                new SymbolDef("Delta", 0x0394, MathAtomType.Ord),
                new SymbolDef("Theta", 0x0398, MathAtomType.Ord),
                new SymbolDef("Lambda", 0x039B, MathAtomType.Ord),
                new SymbolDef("Xi", 0x039E, MathAtomType.Ord),
                new SymbolDef("Pi", 0x03A0, MathAtomType.Ord),
                new SymbolDef("Sigma", 0x03A3, MathAtomType.Ord),
                new SymbolDef("Upsilon", 0x03A5, MathAtomType.Ord),
                new SymbolDef("Phi", 0x03A6, MathAtomType.Ord),
                new SymbolDef("Psi", 0x03A8, MathAtomType.Ord),
                new SymbolDef("Omega", 0x03A9, MathAtomType.Ord), new SymbolDef("Alpha", 0x0391, MathAtomType.Ord),
                new SymbolDef("Beta", 0x0392, MathAtomType.Ord),
                new SymbolDef("Epsilon", 0x0395, MathAtomType.Ord),
                new SymbolDef("Zeta", 0x0396, MathAtomType.Ord),
                new SymbolDef("Eta", 0x0397, MathAtomType.Ord),
                new SymbolDef("Iota", 0x0399, MathAtomType.Ord),
                new SymbolDef("Kappa", 0x039A, MathAtomType.Ord),
                new SymbolDef("Mu", 0x039C, MathAtomType.Ord),
                new SymbolDef("Nu", 0x039D, MathAtomType.Ord),
                new SymbolDef("Omicron", 0x039F, MathAtomType.Ord),
                new SymbolDef("Rho", 0x03A1, MathAtomType.Ord),
                new SymbolDef("Tau", 0x03A4, MathAtomType.Ord),
                new SymbolDef("Chi", 0x03A7, MathAtomType.Ord), new SymbolDef("pm", 0x00B1, MathAtomType.Bin),
                new SymbolDef("mp", 0x2213, MathAtomType.Bin),
                new SymbolDef("times", 0x00D7, MathAtomType.Bin),
                new SymbolDef("div", 0x00F7, MathAtomType.Bin),
                new SymbolDef("cdot", 0x22C5, MathAtomType.Bin),
                new SymbolDef("ast", 0x2217, MathAtomType.Bin),
                new SymbolDef("star", 0x22C6, MathAtomType.Bin),
                new SymbolDef("circ", 0x2218, MathAtomType.Bin),
                new SymbolDef("bullet", 0x2219, MathAtomType.Bin),
                new SymbolDef("diamond", 0x22C4, MathAtomType.Bin),
                new SymbolDef("cap", 0x2229, MathAtomType.Bin),
                new SymbolDef("cup", 0x222A, MathAtomType.Bin),
                new SymbolDef("sqcap", 0x2293, MathAtomType.Bin),
                new SymbolDef("sqcup", 0x2294, MathAtomType.Bin),
                new SymbolDef("vee", 0x2228, MathAtomType.Bin),
                new SymbolDef("lor", 0x2228, MathAtomType.Bin),
                new SymbolDef("wedge", 0x2227, MathAtomType.Bin),
                new SymbolDef("land", 0x2227, MathAtomType.Bin),
                new SymbolDef("setminus", 0x2216, MathAtomType.Bin),
                new SymbolDef("wr", 0x2240, MathAtomType.Bin),
                new SymbolDef("oplus", 0x2295, MathAtomType.Bin),
                new SymbolDef("ominus", 0x2296, MathAtomType.Bin),
                new SymbolDef("otimes", 0x2297, MathAtomType.Bin),
                new SymbolDef("oslash", 0x2298, MathAtomType.Bin),
                new SymbolDef("odot", 0x2299, MathAtomType.Bin),
                new SymbolDef("uplus", 0x228E, MathAtomType.Bin),
                new SymbolDef("amalg", 0x2A3F, MathAtomType.Bin),
                new SymbolDef("dagger", 0x2020, MathAtomType.Bin),
                new SymbolDef("ddagger", 0x2021, MathAtomType.Bin),
                new SymbolDef("bigcirc", 0x25EF, MathAtomType.Bin),
                new SymbolDef("bigtriangleup", 0x25B3, MathAtomType.Bin),
                new SymbolDef("bigtriangledown", 0x25BD, MathAtomType.Bin),
                new SymbolDef("triangleleft", 0x25C3, MathAtomType.Bin),
                new SymbolDef("triangleright", 0x25B9, MathAtomType.Bin),
                new SymbolDef("lhd", 0x22B2, MathAtomType.Bin),
                new SymbolDef("rhd", 0x22B3, MathAtomType.Bin),
                new SymbolDef("unlhd", 0x22B4, MathAtomType.Bin),
                new SymbolDef("unrhd", 0x22B5, MathAtomType.Bin), new SymbolDef("dotplus", 0x2214, MathAtomType.Bin),
                new SymbolDef("smallsetminus", 0x2216, MathAtomType.Bin),
                new SymbolDef("Cap", 0x22D2, MathAtomType.Bin),
                new SymbolDef("doublecap", 0x22D2, MathAtomType.Bin),
                new SymbolDef("Cup", 0x22D3, MathAtomType.Bin),
                new SymbolDef("doublecup", 0x22D3, MathAtomType.Bin),
                new SymbolDef("barwedge", 0x22BC, MathAtomType.Bin),
                new SymbolDef("veebar", 0x22BB, MathAtomType.Bin),
                new SymbolDef("doublebarwedge", 0x2A5E, MathAtomType.Bin),
                new SymbolDef("boxminus", 0x229F, MathAtomType.Bin),
                new SymbolDef("boxplus", 0x229E, MathAtomType.Bin),
                new SymbolDef("boxtimes", 0x22A0, MathAtomType.Bin),
                new SymbolDef("boxdot", 0x22A1, MathAtomType.Bin),
                new SymbolDef("circleddash", 0x229D, MathAtomType.Bin),
                new SymbolDef("circledast", 0x229B, MathAtomType.Bin),
                new SymbolDef("circledcirc", 0x229A, MathAtomType.Bin),
                new SymbolDef("centerdot", 0x22C5, MathAtomType.Bin),
                new SymbolDef("intercal", 0x22BA, MathAtomType.Bin),
                new SymbolDef("divideontimes", 0x22C7, MathAtomType.Bin),
                new SymbolDef("ltimes", 0x22C9, MathAtomType.Bin),
                new SymbolDef("rtimes", 0x22CA, MathAtomType.Bin),
                new SymbolDef("leftthreetimes", 0x22CB, MathAtomType.Bin),
                new SymbolDef("rightthreetimes", 0x22CC, MathAtomType.Bin),
                new SymbolDef("curlywedge", 0x22CF, MathAtomType.Bin),
                new SymbolDef("curlyvee", 0x22CE, MathAtomType.Bin),
                new SymbolDef("lessdot", 0x22D6, MathAtomType.Bin),
                new SymbolDef("gtrdot", 0x22D7, MathAtomType.Bin), new SymbolDef("leq", 0x2264, MathAtomType.Rel),
                new SymbolDef("le", 0x2264, MathAtomType.Rel),
                new SymbolDef("geq", 0x2265, MathAtomType.Rel),
                new SymbolDef("ge", 0x2265, MathAtomType.Rel),
                new SymbolDef("neq", 0x2260, MathAtomType.Rel),
                new SymbolDef("ne", 0x2260, MathAtomType.Rel),
                new SymbolDef("approx", 0x2248, MathAtomType.Rel),
                new SymbolDef("equiv", 0x2261, MathAtomType.Rel),
                new SymbolDef("sim", 0x223C, MathAtomType.Rel),
                new SymbolDef("simeq", 0x2243, MathAtomType.Rel),
                new SymbolDef("cong", 0x2245, MathAtomType.Rel),
                new SymbolDef("subset", 0x2282, MathAtomType.Rel),
                new SymbolDef("supset", 0x2283, MathAtomType.Rel),
                new SymbolDef("subseteq", 0x2286, MathAtomType.Rel),
                new SymbolDef("supseteq", 0x2287, MathAtomType.Rel),
                new SymbolDef("in", 0x2208, MathAtomType.Rel),
                new SymbolDef("ni", 0x220B, MathAtomType.Rel),
                new SymbolDef("owns", 0x220B, MathAtomType.Rel),
                new SymbolDef("notin", 0x2209, MathAtomType.Rel),
                new SymbolDef("mid", 0x2223, MathAtomType.Rel),
                new SymbolDef("parallel", 0x2225, MathAtomType.Rel),
                new SymbolDef("perp", 0x22A5, MathAtomType.Rel),
                new SymbolDef("propto", 0x221D, MathAtomType.Rel),
                new SymbolDef("asymp", 0x224D, MathAtomType.Rel),
                new SymbolDef("doteq", 0x2250, MathAtomType.Rel),
                new SymbolDef("prec", 0x227A, MathAtomType.Rel),
                new SymbolDef("succ", 0x227B, MathAtomType.Rel),
                new SymbolDef("preceq", 0x2AAF, MathAtomType.Rel),
                new SymbolDef("succeq", 0x2AB0, MathAtomType.Rel),
                new SymbolDef("ll", 0x226A, MathAtomType.Rel),
                new SymbolDef("gg", 0x226B, MathAtomType.Rel),
                new SymbolDef("vdash", 0x22A2, MathAtomType.Rel),
                new SymbolDef("dashv", 0x22A3, MathAtomType.Rel),
                new SymbolDef("models", 0x22A8, MathAtomType.Rel),
                new SymbolDef("smile", 0x2323, MathAtomType.Rel),
                new SymbolDef("frown", 0x2322, MathAtomType.Rel),
                new SymbolDef("bowtie", 0x22C8, MathAtomType.Rel),
                new SymbolDef("sqsubseteq", 0x2291, MathAtomType.Rel),
                new SymbolDef("sqsupseteq", 0x2292, MathAtomType.Rel), new SymbolDef("leqq", 0x2266, MathAtomType.Rel),
                new SymbolDef("leqslant", 0x2A7D, MathAtomType.Rel),
                new SymbolDef("eqslantless", 0x2A95, MathAtomType.Rel),
                new SymbolDef("lesssim", 0x2272, MathAtomType.Rel),
                new SymbolDef("lessapprox", 0x2A85, MathAtomType.Rel),
                new SymbolDef("approxeq", 0x224A, MathAtomType.Rel),
                new SymbolDef("lessgtr", 0x2276, MathAtomType.Rel),
                new SymbolDef("lesseqgtr", 0x22DA, MathAtomType.Rel),
                new SymbolDef("lesseqqgtr", 0x2A8B, MathAtomType.Rel),
                new SymbolDef("doteqdot", 0x2251, MathAtomType.Rel),
                new SymbolDef("Doteq", 0x2251, MathAtomType.Rel),
                new SymbolDef("risingdotseq", 0x2253, MathAtomType.Rel),
                new SymbolDef("fallingdotseq", 0x2252, MathAtomType.Rel),
                new SymbolDef("backsim", 0x223D, MathAtomType.Rel),
                new SymbolDef("backsimeq", 0x22CD, MathAtomType.Rel),
                new SymbolDef("subseteqq", 0x2AC5, MathAtomType.Rel),
                new SymbolDef("Subset", 0x22D0, MathAtomType.Rel),
                new SymbolDef("sqsubset", 0x228F, MathAtomType.Rel),
                new SymbolDef("preccurlyeq", 0x227C, MathAtomType.Rel),
                new SymbolDef("curlyeqprec", 0x22DE, MathAtomType.Rel),
                new SymbolDef("precsim", 0x227E, MathAtomType.Rel),
                new SymbolDef("precapprox", 0x2AB7, MathAtomType.Rel),
                new SymbolDef("vartriangleleft", 0x22B2, MathAtomType.Rel),
                new SymbolDef("trianglelefteq", 0x22B4, MathAtomType.Rel),
                new SymbolDef("vDash", 0x22A8, MathAtomType.Rel),
                new SymbolDef("Vdash", 0x22A9, MathAtomType.Rel),
                new SymbolDef("Vvdash", 0x22AA, MathAtomType.Rel),
                new SymbolDef("smallsmile", 0x2323, MathAtomType.Rel),
                new SymbolDef("smallfrown", 0x2322, MathAtomType.Rel),
                new SymbolDef("bumpeq", 0x224F, MathAtomType.Rel),
                new SymbolDef("Bumpeq", 0x224E, MathAtomType.Rel),
                new SymbolDef("geqq", 0x2267, MathAtomType.Rel),
                new SymbolDef("geqslant", 0x2A7E, MathAtomType.Rel),
                new SymbolDef("eqslantgtr", 0x2A96, MathAtomType.Rel),
                new SymbolDef("gtrsim", 0x2273, MathAtomType.Rel),
                new SymbolDef("gtrapprox", 0x2A86, MathAtomType.Rel),
                new SymbolDef("gtrless", 0x2277, MathAtomType.Rel),
                new SymbolDef("gtreqless", 0x22DB, MathAtomType.Rel),
                new SymbolDef("gtreqqless", 0x2A8C, MathAtomType.Rel),
                new SymbolDef("eqcirc", 0x2256, MathAtomType.Rel),
                new SymbolDef("circeq", 0x2257, MathAtomType.Rel),
                new SymbolDef("triangleq", 0x225C, MathAtomType.Rel),
                new SymbolDef("thicksim", 0x223C, MathAtomType.Rel),
                new SymbolDef("thickapprox", 0x2248, MathAtomType.Rel),
                new SymbolDef("supseteqq", 0x2AC6, MathAtomType.Rel),
                new SymbolDef("Supset", 0x22D1, MathAtomType.Rel),
                new SymbolDef("sqsupset", 0x2290, MathAtomType.Rel),
                new SymbolDef("succcurlyeq", 0x227D, MathAtomType.Rel),
                new SymbolDef("curlyeqsucc", 0x22DF, MathAtomType.Rel),
                new SymbolDef("succsim", 0x227F, MathAtomType.Rel),
                new SymbolDef("succapprox", 0x2AB8, MathAtomType.Rel),
                new SymbolDef("vartriangleright", 0x22B3, MathAtomType.Rel),
                new SymbolDef("trianglerighteq", 0x22B5, MathAtomType.Rel),
                new SymbolDef("shortmid", 0x2223, MathAtomType.Rel),
                new SymbolDef("shortparallel", 0x2225, MathAtomType.Rel),
                new SymbolDef("between", 0x226C, MathAtomType.Rel),
                new SymbolDef("pitchfork", 0x22D4, MathAtomType.Rel),
                new SymbolDef("varpropto", 0x221D, MathAtomType.Rel),
                new SymbolDef("therefore", 0x2234, MathAtomType.Rel),
                new SymbolDef("because", 0x2235, MathAtomType.Rel),
                new SymbolDef("eqsim", 0x2242, MathAtomType.Rel),
                new SymbolDef("Join", 0x22C8, MathAtomType.Rel),
                new SymbolDef("lll", 0x22D8, MathAtomType.Rel),
                new SymbolDef("llless", 0x22D8, MathAtomType.Rel),
                new SymbolDef("ggg", 0x22D9, MathAtomType.Rel),
                new SymbolDef("gggtr", 0x22D9, MathAtomType.Rel),
                new SymbolDef("backepsilon", 0x220D, MathAtomType.Rel), new SymbolDef("nless", 0x226E, MathAtomType.Rel),
                new SymbolDef("ngtr", 0x226F, MathAtomType.Rel),
                new SymbolDef("nleq", 0x2270, MathAtomType.Rel),
                new SymbolDef("ngeq", 0x2271, MathAtomType.Rel),
                new SymbolDef("lneq", 0x2A87, MathAtomType.Rel),
                new SymbolDef("gneq", 0x2A88, MathAtomType.Rel),
                new SymbolDef("lneqq", 0x2268, MathAtomType.Rel),
                new SymbolDef("gneqq", 0x2269, MathAtomType.Rel),
                new SymbolDef("lnsim", 0x22E6, MathAtomType.Rel),
                new SymbolDef("gnsim", 0x22E7, MathAtomType.Rel),
                new SymbolDef("lnapprox", 0x2A89, MathAtomType.Rel),
                new SymbolDef("gnapprox", 0x2A8A, MathAtomType.Rel),
                new SymbolDef("nprec", 0x2280, MathAtomType.Rel),
                new SymbolDef("nsucc", 0x2281, MathAtomType.Rel),
                new SymbolDef("npreceq", 0x22E0, MathAtomType.Rel),
                new SymbolDef("nsucceq", 0x22E1, MathAtomType.Rel),
                new SymbolDef("precnsim", 0x22E8, MathAtomType.Rel),
                new SymbolDef("succnsim", 0x22E9, MathAtomType.Rel),
                new SymbolDef("precnapprox", 0x2AB9, MathAtomType.Rel),
                new SymbolDef("succnapprox", 0x2ABA, MathAtomType.Rel),
                new SymbolDef("precneqq", 0x2AB5, MathAtomType.Rel),
                new SymbolDef("succneqq", 0x2AB6, MathAtomType.Rel),
                new SymbolDef("nsim", 0x2241, MathAtomType.Rel),
                new SymbolDef("ncong", 0x2246, MathAtomType.Rel),
                new SymbolDef("nmid", 0x2224, MathAtomType.Rel),
                new SymbolDef("nparallel", 0x2226, MathAtomType.Rel),
                new SymbolDef("nvdash", 0x22AC, MathAtomType.Rel),
                new SymbolDef("nvDash", 0x22AD, MathAtomType.Rel),
                new SymbolDef("nVdash", 0x22AE, MathAtomType.Rel),
                new SymbolDef("nVDash", 0x22AF, MathAtomType.Rel),
                new SymbolDef("ntriangleleft", 0x22EA, MathAtomType.Rel),
                new SymbolDef("ntriangleright", 0x22EB, MathAtomType.Rel),
                new SymbolDef("ntrianglelefteq", 0x22EC, MathAtomType.Rel),
                new SymbolDef("ntrianglerighteq", 0x22ED, MathAtomType.Rel),
                new SymbolDef("subsetneq", 0x228A, MathAtomType.Rel),
                new SymbolDef("supsetneq", 0x228B, MathAtomType.Rel),
                new SymbolDef("subsetneqq", 0x2ACB, MathAtomType.Rel),
                new SymbolDef("supsetneqq", 0x2ACC, MathAtomType.Rel),
                new SymbolDef("nsubseteq", 0x2288, MathAtomType.Rel),
                new SymbolDef("nsupseteq", 0x2289, MathAtomType.Rel), new SymbolDef("leftarrow", 0x2190, MathAtomType.Rel),
                new SymbolDef("gets", 0x2190, MathAtomType.Rel),
                new SymbolDef("rightarrow", 0x2192, MathAtomType.Rel),
                new SymbolDef("to", 0x2192, MathAtomType.Rel),
                new SymbolDef("leftrightarrow", 0x2194, MathAtomType.Rel),
                new SymbolDef("uparrow", 0x2191, MathAtomType.Rel),
                new SymbolDef("downarrow", 0x2193, MathAtomType.Rel),
                new SymbolDef("updownarrow", 0x2195, MathAtomType.Rel),
                new SymbolDef("Leftarrow", 0x21D0, MathAtomType.Rel),
                new SymbolDef("Rightarrow", 0x21D2, MathAtomType.Rel),
                new SymbolDef("Leftrightarrow", 0x21D4, MathAtomType.Rel),
                new SymbolDef("Uparrow", 0x21D1, MathAtomType.Rel),
                new SymbolDef("Downarrow", 0x21D3, MathAtomType.Rel),
                new SymbolDef("Updownarrow", 0x21D5, MathAtomType.Rel),
                new SymbolDef("longleftarrow", 0x27F5, MathAtomType.Rel),
                new SymbolDef("longrightarrow", 0x27F6, MathAtomType.Rel),
                new SymbolDef("longleftrightarrow", 0x27F7, MathAtomType.Rel),
                new SymbolDef("Longleftarrow", 0x27F8, MathAtomType.Rel),
                new SymbolDef("Longrightarrow", 0x27F9, MathAtomType.Rel),
                new SymbolDef("Longleftrightarrow", 0x27FA, MathAtomType.Rel),
                new SymbolDef("mapsto", 0x21A6, MathAtomType.Rel),
                new SymbolDef("longmapsto", 0x27FC, MathAtomType.Rel),
                new SymbolDef("nearrow", 0x2197, MathAtomType.Rel),
                new SymbolDef("searrow", 0x2198, MathAtomType.Rel),
                new SymbolDef("swarrow", 0x2199, MathAtomType.Rel),
                new SymbolDef("nwarrow", 0x2196, MathAtomType.Rel),
                new SymbolDef("hookleftarrow", 0x21A9, MathAtomType.Rel),
                new SymbolDef("hookrightarrow", 0x21AA, MathAtomType.Rel),
                new SymbolDef("leftharpoonup", 0x21BC, MathAtomType.Rel),
                new SymbolDef("leftharpoondown", 0x21BD, MathAtomType.Rel),
                new SymbolDef("rightharpoonup", 0x21C0, MathAtomType.Rel),
                new SymbolDef("rightharpoondown", 0x21C1, MathAtomType.Rel),
                new SymbolDef("rightleftharpoons", 0x21CC, MathAtomType.Rel),
                new SymbolDef("leftrightharpoons", 0x21CB, MathAtomType.Rel), new SymbolDef("dashrightarrow", 0x21E2, MathAtomType.Rel),
                new SymbolDef("dashleftarrow", 0x21E0, MathAtomType.Rel),
                new SymbolDef("leftleftarrows", 0x21C7, MathAtomType.Rel),
                new SymbolDef("leftrightarrows", 0x21C6, MathAtomType.Rel),
                new SymbolDef("Lleftarrow", 0x21DA, MathAtomType.Rel),
                new SymbolDef("twoheadleftarrow", 0x219E, MathAtomType.Rel),
                new SymbolDef("leftarrowtail", 0x21A2, MathAtomType.Rel),
                new SymbolDef("looparrowleft", 0x21AB, MathAtomType.Rel),
                new SymbolDef("curvearrowleft", 0x21B6, MathAtomType.Rel),
                new SymbolDef("circlearrowleft", 0x21BA, MathAtomType.Rel),
                new SymbolDef("Lsh", 0x21B0, MathAtomType.Rel),
                new SymbolDef("upuparrows", 0x21C8, MathAtomType.Rel),
                new SymbolDef("upharpoonleft", 0x21BF, MathAtomType.Rel),
                new SymbolDef("downharpoonleft", 0x21C3, MathAtomType.Rel),
                new SymbolDef("rightrightarrows", 0x21C9, MathAtomType.Rel),
                new SymbolDef("rightleftarrows", 0x21C4, MathAtomType.Rel),
                new SymbolDef("twoheadrightarrow", 0x21A0, MathAtomType.Rel),
                new SymbolDef("rightarrowtail", 0x21A3, MathAtomType.Rel),
                new SymbolDef("looparrowright", 0x21AC, MathAtomType.Rel),
                new SymbolDef("curvearrowright", 0x21B7, MathAtomType.Rel),
                new SymbolDef("circlearrowright", 0x21BB, MathAtomType.Rel),
                new SymbolDef("Rsh", 0x21B1, MathAtomType.Rel),
                new SymbolDef("downdownarrows", 0x21CA, MathAtomType.Rel),
                new SymbolDef("upharpoonright", 0x21BE, MathAtomType.Rel),
                new SymbolDef("restriction", 0x21BE, MathAtomType.Rel),
                new SymbolDef("downharpoonright", 0x21C2, MathAtomType.Rel),
                new SymbolDef("rightsquigarrow", 0x21DD, MathAtomType.Rel),
                new SymbolDef("leadsto", 0x21DD, MathAtomType.Rel),
                new SymbolDef("leftrightsquigarrow", 0x21AD, MathAtomType.Rel),
                new SymbolDef("Rrightarrow", 0x21DB, MathAtomType.Rel),
                new SymbolDef("multimap", 0x22B8, MathAtomType.Rel),
                new SymbolDef("origof", 0x22B6, MathAtomType.Rel),
                new SymbolDef("imageof", 0x22B7, MathAtomType.Rel), new SymbolDef("nleftarrow", 0x219A, MathAtomType.Rel),
                new SymbolDef("nrightarrow", 0x219B, MathAtomType.Rel),
                new SymbolDef("nLeftarrow", 0x21CD, MathAtomType.Rel),
                new SymbolDef("nRightarrow", 0x21CF, MathAtomType.Rel),
                new SymbolDef("nleftrightarrow", 0x21AE, MathAtomType.Rel),
                new SymbolDef("nLeftrightarrow", 0x21CE, MathAtomType.Rel), new SymbolDef("sum", 0x2211, MathAtomType.Op),
                new SymbolDef("prod", 0x220F, MathAtomType.Op),
                new SymbolDef("coprod", 0x2210, MathAtomType.Op),
                new SymbolDef("int", 0x222B, MathAtomType.Op),
                new SymbolDef("intop", 0x222B, MathAtomType.Op),
                new SymbolDef("iint", 0x222C, MathAtomType.Op),
                new SymbolDef("iiint", 0x222D, MathAtomType.Op),
                new SymbolDef("oint", 0x222E, MathAtomType.Op),
                new SymbolDef("oiint", 0x222F, MathAtomType.Op),
                new SymbolDef("oiiint", 0x2230, MathAtomType.Op),
                new SymbolDef("smallint", 0x222B, MathAtomType.Op),
                new SymbolDef("bigcap", 0x22C2, MathAtomType.Op),
                new SymbolDef("bigcup", 0x22C3, MathAtomType.Op),
                new SymbolDef("bigsqcup", 0x2A06, MathAtomType.Op),
                new SymbolDef("bigvee", 0x22C1, MathAtomType.Op),
                new SymbolDef("bigwedge", 0x22C0, MathAtomType.Op),
                new SymbolDef("bigodot", 0x2A00, MathAtomType.Op),
                new SymbolDef("bigoplus", 0x2A01, MathAtomType.Op),
                new SymbolDef("bigotimes", 0x2A02, MathAtomType.Op),
                new SymbolDef("biguplus", 0x2A04, MathAtomType.Op), new SymbolDef("langle", 0x27E8, MathAtomType.Open),
                new SymbolDef("lfloor", 0x230A, MathAtomType.Open),
                new SymbolDef("lceil", 0x2308, MathAtomType.Open),
                new SymbolDef("lbrace", 0x007B, MathAtomType.Open),
                new SymbolDef("lbrack", 0x005B, MathAtomType.Open),
                new SymbolDef("lparen", 0x0028, MathAtomType.Open),
                new SymbolDef("lvert", 0x2223, MathAtomType.Open),
                new SymbolDef("lVert", 0x2225, MathAtomType.Open),
                new SymbolDef("lgroup", 0x27EE, MathAtomType.Open),
                new SymbolDef("lmoustache", 0x23B0, MathAtomType.Open),
                new SymbolDef("ulcorner", 0x231C, MathAtomType.Open),
                new SymbolDef("llcorner", 0x231E, MathAtomType.Open), new SymbolDef("rangle", 0x27E9, MathAtomType.Close),
                new SymbolDef("rfloor", 0x230B, MathAtomType.Close),
                new SymbolDef("rceil", 0x2309, MathAtomType.Close),
                new SymbolDef("rbrace", 0x007D, MathAtomType.Close),
                new SymbolDef("rbrack", 0x005D, MathAtomType.Close),
                new SymbolDef("rparen", 0x0029, MathAtomType.Close),
                new SymbolDef("rvert", 0x2223, MathAtomType.Close),
                new SymbolDef("rVert", 0x2225, MathAtomType.Close),
                new SymbolDef("rgroup", 0x27EF, MathAtomType.Close),
                new SymbolDef("rmoustache", 0x23B1, MathAtomType.Close),
                new SymbolDef("urcorner", 0x231D, MathAtomType.Close),
                new SymbolDef("lrcorner", 0x231F, MathAtomType.Close), new SymbolDef("ldotp", 0x002E, MathAtomType.Punct),
                new SymbolDef("cdotp", 0x22C5, MathAtomType.Punct),
                new SymbolDef("colon", 0x003A, MathAtomType.Punct),
                new SymbolDef("semicolon", 0x003B, MathAtomType.Punct), new SymbolDef("infty", 0x221E, MathAtomType.Ord),
                new SymbolDef("partial", 0x2202, MathAtomType.Ord),
                new SymbolDef("nabla", 0x2207, MathAtomType.Ord),
                new SymbolDef("forall", 0x2200, MathAtomType.Ord),
                new SymbolDef("exists", 0x2203, MathAtomType.Ord),
                new SymbolDef("nexists", 0x2204, MathAtomType.Ord),
                new SymbolDef("emptyset", 0x2205, MathAtomType.Ord),
                new SymbolDef("varnothing", 0x2205, MathAtomType.Ord),
                new SymbolDef("neg", 0x00AC, MathAtomType.Ord),
                new SymbolDef("lnot", 0x00AC, MathAtomType.Ord),
                new SymbolDef("top", 0x22A4, MathAtomType.Ord),
                new SymbolDef("bot", 0x22A5, MathAtomType.Ord),
                new SymbolDef("angle", 0x2220, MathAtomType.Ord),
                new SymbolDef("measuredangle", 0x2221, MathAtomType.Ord),
                new SymbolDef("sphericalangle", 0x2222, MathAtomType.Ord),
                new SymbolDef("triangle", 0x25B3, MathAtomType.Ord),
                new SymbolDef("vartriangle", 0x25B3, MathAtomType.Ord),
                new SymbolDef("triangledown", 0x25BD, MathAtomType.Ord),
                new SymbolDef("surd", 0x221A, MathAtomType.Ord),
                new SymbolDef("prime", 0x2032, MathAtomType.Ord),
                new SymbolDef("backprime", 0x2035, MathAtomType.Ord),
                new SymbolDef("hbar", 0x210F, MathAtomType.Ord),
                new SymbolDef("hslash", 0x210F, MathAtomType.Ord),
                new SymbolDef("ell", 0x2113, MathAtomType.Ord),
                new SymbolDef("wp", 0x2118, MathAtomType.Ord),
                new SymbolDef("Re", 0x211C, MathAtomType.Ord),
                new SymbolDef("Im", 0x2111, MathAtomType.Ord),
                new SymbolDef("aleph", 0x2135, MathAtomType.Ord),
                new SymbolDef("beth", 0x2136, MathAtomType.Ord),
                new SymbolDef("gimel", 0x2137, MathAtomType.Ord),
                new SymbolDef("daleth", 0x2138, MathAtomType.Ord),
                new SymbolDef("complement", 0x2201, MathAtomType.Ord),
                new SymbolDef("eth", 0x00F0, MathAtomType.Ord),
                new SymbolDef("mho", 0x2127, MathAtomType.Ord),
                new SymbolDef("Finv", 0x2132, MathAtomType.Ord),
                new SymbolDef("Game", 0x2141, MathAtomType.Ord),
                new SymbolDef("imath", 0x1D6A4, MathAtomType.Ord),
                new SymbolDef("jmath", 0x1D6A5, MathAtomType.Ord), new SymbolDef("square", 0x25A1, MathAtomType.Ord),
                new SymbolDef("Box", 0x25A1, MathAtomType.Ord),
                new SymbolDef("blacksquare", 0x25A0, MathAtomType.Ord),
                new SymbolDef("lozenge", 0x25CA, MathAtomType.Ord),
                new SymbolDef("Diamond", 0x25CA, MathAtomType.Ord),
                new SymbolDef("blacklozenge", 0x29EB, MathAtomType.Ord),
                new SymbolDef("blacktriangle", 0x25B2, MathAtomType.Ord),
                new SymbolDef("blacktriangledown", 0x25BC, MathAtomType.Ord),
                new SymbolDef("blacktriangleleft", 0x25C0, MathAtomType.Ord),
                new SymbolDef("blacktriangleright", 0x25B6, MathAtomType.Ord),
                new SymbolDef("bigstar", 0x2605, MathAtomType.Ord),
                new SymbolDef("circledS", 0x24C8, MathAtomType.Ord),
                new SymbolDef("circledR", 0x00AE, MathAtomType.Ord),
                new SymbolDef("diagup", 0x2571, MathAtomType.Ord),
                new SymbolDef("diagdown", 0x2572, MathAtomType.Ord), new SymbolDef("clubsuit", 0x2663, MathAtomType.Ord),
                new SymbolDef("diamondsuit", 0x2662, MathAtomType.Ord),
                new SymbolDef("heartsuit", 0x2661, MathAtomType.Ord),
                new SymbolDef("spadesuit", 0x2660, MathAtomType.Ord), new SymbolDef("flat", 0x266D, MathAtomType.Ord),
                new SymbolDef("natural", 0x266E, MathAtomType.Ord),
                new SymbolDef("sharp", 0x266F, MathAtomType.Ord), new SymbolDef("checkmark", 0x2713, MathAtomType.Ord),
                new SymbolDef("maltese", 0x2720, MathAtomType.Ord),
                new SymbolDef("degree", 0x00B0, MathAtomType.Ord), new SymbolDef("ldots", 0x2026, MathAtomType.Inner),
                new SymbolDef("cdots", 0x22EF, MathAtomType.Inner),
                new SymbolDef("ddots", 0x22F1, MathAtomType.Inner),
                new SymbolDef("vdots", 0x22EE, MathAtomType.Ord), new SymbolDef("Vert", 0x2225, MathAtomType.Ord),
                new SymbolDef("vert", 0x2223, MathAtomType.Ord), new SymbolDef("hat", MathAtomType.Ord, 0x0302), new SymbolDef("widehat", MathAtomType.Ord, 0x0302), new SymbolDef("check", MathAtomType.Ord, 0x030C), new SymbolDef("tilde", MathAtomType.Ord, 0x0303), new SymbolDef("widetilde", MathAtomType.Ord, 0x0303), new SymbolDef("acute", MathAtomType.Ord, 0x0301), new SymbolDef("grave", MathAtomType.Ord, 0x0300), new SymbolDef("dot", MathAtomType.Ord, 0x0307), new SymbolDef("ddot", MathAtomType.Ord, 0x0308), new SymbolDef("dddot", MathAtomType.Ord, 0x20DB), new SymbolDef("ddddot", MathAtomType.Ord, 0x20DC), new SymbolDef("breve", MathAtomType.Ord, 0x0306), new SymbolDef("bar", MathAtomType.Ord, 0x0304), new SymbolDef("vec", MathAtomType.Ord, 0x20D7), new SymbolDef("mathring", MathAtomType.Ord, 0x030A), new SymbolDef("sin", 0x0073, MathAtomType.Op),
                new SymbolDef("cos", 0x0063, MathAtomType.Op),
                new SymbolDef("tan", 0x0074, MathAtomType.Op),
                new SymbolDef("cot", 0x0063, MathAtomType.Op),
                new SymbolDef("sec", 0x0073, MathAtomType.Op),
                new SymbolDef("csc", 0x0063, MathAtomType.Op),
                new SymbolDef("arcsin", 0x0061, MathAtomType.Op),
                new SymbolDef("arccos", 0x0061, MathAtomType.Op),
                new SymbolDef("arctan", 0x0061, MathAtomType.Op),
                new SymbolDef("sinh", 0x0073, MathAtomType.Op),
                new SymbolDef("cosh", 0x0063, MathAtomType.Op),
                new SymbolDef("tanh", 0x0074, MathAtomType.Op),
                new SymbolDef("coth", 0x0063, MathAtomType.Op),
                new SymbolDef("log", 0x006C, MathAtomType.Op),
                new SymbolDef("ln", 0x006C, MathAtomType.Op),
                new SymbolDef("lg", 0x006C, MathAtomType.Op),
                new SymbolDef("exp", 0x0065, MathAtomType.Op),
                new SymbolDef("lim", 0x006C, MathAtomType.Op),
                new SymbolDef("liminf", 0x006C, MathAtomType.Op),
                new SymbolDef("limsup", 0x006C, MathAtomType.Op),
                new SymbolDef("sup", 0x0073, MathAtomType.Op),
                new SymbolDef("inf", 0x0069, MathAtomType.Op),
                new SymbolDef("min", 0x006D, MathAtomType.Op),
                new SymbolDef("max", 0x006D, MathAtomType.Op),
                new SymbolDef("det", 0x0064, MathAtomType.Op),
                new SymbolDef("dim", 0x0064, MathAtomType.Op),
                new SymbolDef("ker", 0x006B, MathAtomType.Op),
                new SymbolDef("hom", 0x0068, MathAtomType.Op),
                new SymbolDef("arg", 0x0061, MathAtomType.Op),
                new SymbolDef("deg", 0x0064, MathAtomType.Op),
                new SymbolDef("gcd", 0x0067, MathAtomType.Op),
                new SymbolDef("Pr", 0x0050, MathAtomType.Op),
                new SymbolDef("mod", 0x006D, MathAtomType.Op), new SymbolDef("quad", 0x2003, MathAtomType.Ord), new SymbolDef("qquad", 0x2003, MathAtomType.Ord), new SymbolDef("enspace", 0x2002, MathAtomType.Ord), new SymbolDef("thinspace", 0x2009, MathAtomType.Ord), new SymbolDef("negthinspace", 0x200A, MathAtomType.Ord),
            };
        }

        #endregion
    }
}
