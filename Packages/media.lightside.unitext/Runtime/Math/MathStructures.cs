using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// AST node type discriminator for parsed LaTeX math expressions.
    /// </summary>
    internal enum MathNodeType : byte
    {
        /// <summary>Single symbol atom: letter, digit, or named symbol (\alpha, \infty).</summary>
        Atom,

        /// <summary>Brace-delimited group of nodes: {a + b}.</summary>
        Group,

        /// <summary>Sequence entry that references a previously emitted node.</summary>
        Reference,

        /// <summary>Fraction: \frac{num}{den} or \binom{n}{k}.</summary>
        Fraction,

        /// <summary>Radical: \sqrt{x}, \sqrt[n]{x}.</summary>
        Radical,

        /// <summary>Superscript and/or subscript: x^2, x_i, x^2_i.</summary>
        Script,

        /// <summary>Delimited group: \left(\right), \bigl, \Bigr, etc.</summary>
        Delimiter,

        /// <summary>Accent above a base: \hat{x}, \tilde{x}, \dot{x}, \vec{x}.</summary>
        Accent,

        /// <summary>Rule above or below a base: \overline{x}, \underline{x}.</summary>
        Bar,

        /// <summary>Named operator or large operator: \sin, \log, \sum, \prod.</summary>
        Operator,

        /// <summary>Matrix or array: \begin{matrix}...\end{matrix}, pmatrix, bmatrix, vmatrix.</summary>
        Matrix,

        /// <summary>Explicit spacing: \quad, \,, \;, \!, \hspace, or mu-width space.</summary>
        Space,

        /// <summary>Raw text within math from commands such as <c>\text{...}</c>.</summary>
        Text,

        /// <summary>Style override: \displaystyle, \textstyle, \scriptstyle, \scriptscriptstyle.</summary>
        StyleChange,

    }


    /// <summary>
    /// Matrix delimiter style, determining the surrounding brackets.
    /// </summary>
    internal enum MatrixDelimiterStyle : byte
    {
        /// <summary>No delimiters: \begin{matrix}.</summary>
        None = 0,

        /// <summary>Parentheses: \begin{pmatrix}.</summary>
        Parentheses = 1,

        /// <summary>Square brackets: \begin{bmatrix}.</summary>
        Brackets = 2,

        /// <summary>Curly braces: \begin{Bmatrix}.</summary>
        Braces = 3,

        /// <summary>Single vertical bars: \begin{vmatrix} (determinant).</summary>
        Vertical = 4,

        /// <summary>Double vertical bars: \begin{Vmatrix} (norm).</summary>
        DoubleVertical = 5,

        /// <summary>Cases: \begin{cases} — left brace, no right delimiter.</summary>
        Cases = 6,
    }


    /// <summary>
    /// Controls horizontal cell alignment and inter-column spacing in matrix-like environments.
    /// </summary>
    internal enum MatrixAlignmentStyle : byte
    {
        /// <summary>Centers every column, as in matrix and gathered environments.</summary>
        Centered = 0,

        /// <summary>Left-aligns every column, as in cases.</summary>
        Left = 1,

        /// <summary>Alternates right- and left-aligned columns around equation alignment points.</summary>
        Alternating = 2,
    }


    /// <summary>Controls whether operator scripts stay beside the glyph or become stacked limits.</summary>
    internal enum MathLimitPlacement : byte
    {
        /// <summary>Always keeps scripts beside the operator.</summary>
        Side = 0,

        /// <summary>Stacks limits only in display style.</summary>
        Display = 1,

        /// <summary>Always stacks limits above and below the operator.</summary>
        OverUnder = 2,
    }


    /// <summary>
    /// Style transition helpers implementing The TeXBook Appendix G rules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The eight styles form a lattice with two orthogonal axes:
    /// size level (D > T > S > SS) and cramped flag. Transitions follow precise rules:
    /// </para>
    /// <list type="bullet">
    /// <item>Superscript: one level smaller, inherits cramped from parent.</item>
    /// <item>Subscript: one level smaller, always cramped.</item>
    /// <item>Numerator: D→T, others→S/SS, inherits cramped.</item>
    /// <item>Denominator: same size as numerator, always cramped.</item>
    /// </list>
    /// </remarks>
    internal static class MathStyleUtil
    {
        /// <summary>
        /// Returns the style for a superscript of the given style.
        /// Goes one size level smaller (minimum SS), inherits cramped flag.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MathStyle Superscript(MathStyle style)
        {
            var s = (int)style;
            var sizeLevel = s >> 1;
            var cramped = s & 1;

            var newLevel = sizeLevel < 2 ? 2 : 3;
            return (MathStyle)((newLevel << 1) | cramped);
        }

        /// <summary>
        /// Returns the style for a subscript of the given style.
        /// Goes one size level smaller (minimum SS), always cramped.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MathStyle Subscript(MathStyle style)
        {
            var sizeLevel = (int)style >> 1;

            var newLevel = sizeLevel < 2 ? 2 : 3;
            return (MathStyle)((newLevel << 1) | 1);
        }

        /// <summary>
        /// Returns the style for the numerator of a fraction.
        /// Goes one size level smaller, inherits cramped flag.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MathStyle Numerator(MathStyle style)
        {
            var s = (int)style;
            var sizeLevel = s >> 1;
            var cramped = s & 1;

            var newLevel = sizeLevel < 3 ? sizeLevel + 1 : 3;
            return (MathStyle)((newLevel << 1) | cramped);
        }

        /// <summary>
        /// Returns the style for the denominator of a fraction.
        /// Goes one size level smaller, always cramped.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MathStyle Denominator(MathStyle style)
        {
            var sizeLevel = (int)style >> 1;

            var newLevel = sizeLevel < 3 ? sizeLevel + 1 : 3;
            return (MathStyle)((newLevel << 1) | 1);
        }

        /// <summary>
        /// Returns the cramped variant of the given style.
        /// Used under radicals, accents, and overlines.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MathStyle Cramped(MathStyle style)
        {
            return (MathStyle)((int)style | 1);
        }

        /// <summary>
        /// Returns true if the style is a cramped variant (D', T', S', SS').
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCramped(MathStyle style)
        {
            return ((int)style & 1) != 0;
        }

        /// <summary>
        /// Returns the size level: 0 = Display/Text, 1 = Script, 2 = ScriptScript.
        /// </summary>
        /// <remarks>
        /// Display and Text share the same font size (level 0). Script is level 1,
        /// ScriptScript is level 2. This maps to the three sizes in the OpenType MATH table.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeLevel(MathStyle style)
        {
            var level = (int)style >> 1;
            return level < 2 ? 0 : level - 1;
        }

        /// <summary>
        /// Returns true if the style is Display or DisplayCramped.
        /// Used to decide whether large operators show limits above/below vs inline.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDisplay(MathStyle style)
        {
            return (int)style < 2;
        }
    }
}
