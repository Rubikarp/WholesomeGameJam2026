using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// TeX atom classification for inter-atom spacing.
    /// </summary>
    /// <remarks>
    /// Each math symbol is assigned an atom type that determines the spacing
    /// inserted between adjacent atoms according to the spacing table in
    /// Chapter 18 of The TeXBook. Thin space around Op, medium space around Bin,
    /// thick space around Rel.
    /// </remarks>
    internal enum MathAtomType : byte
    {
        /// <summary>Ordinary symbol: variables, constants, digits.</summary>
        Ord = 0,
        /// <summary>Large operator: sum, product, integral, etc.</summary>
        Op = 1,
        /// <summary>Binary operator: +, -, times, etc.</summary>
        Bin = 2,
        /// <summary>Relation: =, less, greater, subset, etc.</summary>
        Rel = 3,
        /// <summary>Opening delimiter: (, [, {, langle, etc.</summary>
        Open = 4,
        /// <summary>Closing delimiter: ), ], }, rangle, etc.</summary>
        Close = 5,
        /// <summary>Punctuation: comma, semicolon, etc.</summary>
        Punct = 6,
        /// <summary>Inner: fractions, \left..\right constructs, etc.</summary>
        Inner = 7
    }


    /// <summary>
    /// Inter-atom spacing table from The TeXbook, Chapter 18, page 170.
    /// Determines the amount of space TeX inserts between adjacent math atoms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The table encodes four spacing levels:
    /// <list type="bullet">
    /// <item><term>0</term><description>No space</description></item>
    /// <item><term>1 (thin)</term><description>thinmuskip = 3mu</description></item>
    /// <item><term>2 (medium)</term><description>medmuskip = 4mu plus 2mu minus 4mu</description></item>
    /// <item><term>3 (thick)</term><description>thickmuskip = 5mu plus 5mu</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// In script and scriptscript styles, some spacings are suppressed (set to 0).
    /// The table stores these as negative values: -1, -2, -3 mean the same magnitude
    /// but suppressed in script/scriptscript.
    /// </para>
    /// <para>
    /// 1 mu = 1/18 em, where em is the font size of the current math style.
    /// </para>
    /// </remarks>
    internal static class MathSpacing
    {
        /// <summary>Thin space: 3mu (thinmuskip).</summary>
        public const float ThinMu = 3f;
        /// <summary>Medium space: 4mu (medmuskip).</summary>
        public const float MediumMu = 4f;
        /// <summary>Thick space: 5mu (thickmuskip).</summary>
        public const float ThickMu = 5f;

        /// <summary>1 mu = 1/18 em.</summary>
        public const float MuPerEm = 18f;
        /// <summary>Reciprocal: em per mu = 1/18.</summary>
        public const float EmPerMu = 1f / 18f;

        private const int AtomTypeCount = 8;

        private static readonly sbyte[] spacingTable = new sbyte[AtomTypeCount * AtomTypeCount]
        {
             0,  1, -2, -3,  0,  0,  0, -1,
             1,  1,  0, -3,  0,  0,  0, -1,
            -2, -2,  0,  0, -2,  0,  0, -2,
            -3, -3,  0,  0, -3,  0,  0, -3,
             0,  0,  0,  0,  0,  0,  0,  0,
             0,  1, -2, -3,  0,  0,  0, -1,
            -1, -1,  0, -1, -1, -1, -1, -1,
            -1,  1, -2, -3, -1,  0, -1, -1
        };

        private static readonly float[] spacingMu = { 0f, ThinMu, MediumMu, ThickMu };

        /// <summary>
        /// Returns the inter-atom spacing in mu (math units) for the given atom pair.
        /// </summary>
        /// <param name="left">Atom type on the left.</param>
        /// <param name="right">Atom type on the right.</param>
        /// <param name="isScript">True if rendering in script or scriptscript style.</param>
        /// <returns>Spacing in mu. 0 means no space.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetSpacingMu(MathAtomType left, MathAtomType right, bool isScript)
        {
            var raw = spacingTable[(int)left * AtomTypeCount + (int)right];

            if (raw < 0)
            {
                if (isScript) return 0f;
                raw = (sbyte)(-raw);
            }

            return spacingMu[raw];
        }

        /// <summary>
        /// Returns the inter-atom spacing in pixels for the given atom pair.
        /// </summary>
        /// <param name="left">Atom type on the left.</param>
        /// <param name="right">Atom type on the right.</param>
        /// <param name="isScript">True if rendering in script or scriptscript style.</param>
        /// <param name="fontSize">Current font size in pixels.</param>
        /// <returns>Spacing in pixels.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetSpacingPx(MathAtomType left, MathAtomType right, bool isScript, float fontSize)
        {
            return GetSpacingMu(left, right, isScript) * fontSize * EmPerMu;
        }

        /// <summary>
        /// Converts math units (mu) to pixels for the given font size.
        /// 1 mu = 1/18 em. fontSize is treated as 1 em.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MuToPixels(float mu, float fontSize)
        {
            return mu * fontSize * EmPerMu;
        }

        /// <summary>
        /// Determines the effective atom type of a Bin atom based on context.
        /// In TeX, a Bin atom is converted to Ord when it has no suitable left neighbor.
        /// </summary>
        /// <remarks>
        /// From The TeXbook, Chapter 26, Rule 5:
        /// If the current atom is Bin and the previous atom was Bin, Op, Rel, Open,
        /// Punct, or there was no previous atom, the Bin becomes Ord.
        /// Similarly, if a Bin atom is followed by Rel, Close, or Punct, it becomes Ord.
        /// </remarks>
        /// <param name="prevType">The atom type to the left, or -1 if none.</param>
        /// <returns>True if the Bin should be treated as Ord.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldBinBecomeOrd(int prevType)
        {
            if (prevType < 0) return true;
            var t = (MathAtomType)prevType;
            return t != MathAtomType.Ord && t != MathAtomType.Close && t != MathAtomType.Inner;
        }

        /// <summary>
        /// Checks if a Bin atom should become Ord based on the atom to its right.
        /// A trailing Bin (followed by Rel, Close, Punct, or end of formula) becomes Ord.
        /// </summary>
        /// <param name="nextType">The atom type to the right, or -1 if none.</param>
        /// <returns>True if the Bin should be treated as Ord.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldBinBecomeOrdRight(int nextType)
        {
            if (nextType < 0) return true;
            var t = (MathAtomType)nextType;
            return t == MathAtomType.Rel || t == MathAtomType.Close || t == MathAtomType.Punct;
        }
    }
}
