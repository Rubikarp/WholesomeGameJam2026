using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LightSide
{
    /// <summary>
    /// AST node for a parsed LaTeX math expression, stored in a flat array.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All child references are indices into the owning <see cref="MathNodeList"/>'s node array,
    /// not pointers or references. This enables cache-friendly traversal and zero GC pressure.
    /// A child index of -1 indicates absence (nullable child).
    /// </para>
    /// <para>
    /// The node uses a discriminated union layout: <see cref="type"/> selects which fields are
    /// meaningful. Fields are packed into a fixed-size struct to avoid per-node heap allocation.
    /// </para>
    /// <para>
    /// <b>Field usage by node type:</b>
    /// <list type="table">
    /// <item><term>Atom</term><description>atomType, codepoint, commandStart/commandLength</description></item>
    /// <item><term>Group</term><description>childStart, childCount</description></item>
    /// <item><term>Reference</term><description>child0 (referenced node), payload (effective atom type)</description></item>
    /// <item><term>Fraction</term><description>child0 (numerator), child1 (denominator), flags (hasRule)</description></item>
    /// <item><term>Radical</term><description>child0 (body), child1 (degree, -1 if absent)</description></item>
    /// <item><term>Script</term><description>child0 (base), child1 (superscript, -1 if absent), child2 (subscript, -1 if absent)</description></item>
    /// <item><term>Delimiter</term><description>child0 (body), codepoint (left delim), delimRight (right delim)</description></item>
    /// <item><term>Accent</term><description>child0 (base), codepoint (accent character)</description></item>
    /// <item><term>Bar</term><description>child0 (base), flags (isUnderbar)</description></item>
    /// <item><term>Operator</term><description>codepoint, flags (limitPlacement, isLargeOperator), commandStart/commandLength</description></item>
    /// <item><term>Matrix</term><description>childStart, childCount (total cells), matrixCols (payload), matrixDelimiter (commandStart), matrixAlignment (commandLength), atomType=Inner</description></item>
    /// <item><term>Space</term><description>spaceWidth (in mu units)</description></item>
    /// <item><term>Text</term><description>commandStart (text start in source), commandLength (text length)</description></item>
    /// <item><term>StyleChange</term><description>targetStyle</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MathNode
    {
        /// <summary>The node type discriminator.</summary>
        public MathNodeType type;

        /// <summary>TeX atom classification (meaningful for Atom and Operator nodes).</summary>
        public MathAtomType atomType;

        /// <summary>
        /// Primary child node index.
        /// <para>Reference: referenced node. Fraction: numerator. Radical: body. Script: base.
        /// Delimiter: body. Accent: base.</para>
        /// </summary>
        public int child0;

        /// <summary>
        /// Secondary child node index.
        /// <para>Fraction: denominator. Radical: degree (-1 if absent).
        /// Script: superscript (-1 if absent).</para>
        /// </summary>
        public int child1;

        /// <summary>
        /// Tertiary child node index.
        /// <para>Script: subscript (-1 if absent).</para>
        /// </summary>
        public int child2;

        /// <summary>
        /// Start index of child nodes for Group and Matrix types.
        /// For Matrix, children are stored row-major: cell[row * matrixCols + col].
        /// </summary>
        public int childStart;

        /// <summary>
        /// Number of child nodes for Group and Matrix types.
        /// For Matrix, this is the total cell count (rows * matrixCols).
        /// </summary>
        public int childCount;

        /// <summary>
        /// Unicode codepoint.
        /// <para>Atom: the symbol codepoint. Operator: the operator codepoint.
        /// Delimiter: the left delimiter codepoint. Accent: the accent codepoint.</para>
        /// </summary>
        public int codepoint;

        /// <summary>
        /// Start index into the source string for command name or text content.
        /// <para>Atom/Operator: start of the command name (e.g., position of "\alpha").
        /// Text: start of the text content.</para>
        /// </summary>
        public int commandStart;

        /// <summary>
        /// Length of the command name or text content in the source string.
        /// </summary>
        public int commandLength;

        /// <summary>
        /// Union field for type-specific data.
        /// <para>Delimiter: right delimiter codepoint. Matrix: column count.
        /// Space: width in mu (reinterpret as float via <see cref="SpaceWidth"/>).
        /// StyleChange: target style (low byte).
        /// </para>
        /// </summary>
        public int payload;

        /// <summary>
        /// Bit flags for boolean properties.
        /// <para>Bit 0: Fraction hasRule. Bit 1: Bar isUnderbar. Bit 2: Operator isLargeOperator. Bits 3-4: Operator limitPlacement.</para>
        /// </summary>
        public byte flags;

        /// <summary>Sentinel value indicating no child node.</summary>
        public const int None = -1;

        private const byte FlagHasRule = 1 << 0;
        private const byte FlagIsUnderbar = 1 << 1;
        private const byte FlagIsLargeOperator = 1 << 2;
        private const int LimitPlacementShift = 3;
        private const byte LimitPlacementMask = 0b11 << LimitPlacementShift;

        /// <summary>Gets or sets whether a fraction has a visible rule (bar line).</summary>
        /// <remarks><c>\frac</c> is true; <c>\binom</c> is false.</remarks>
        public bool HasRule
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (flags & FlagHasRule) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => flags = value ? (byte)(flags | FlagHasRule) : (byte)(flags & ~FlagHasRule);
        }

        /// <summary>Gets or sets whether a Bar node draws its rule below the base.</summary>
        public bool IsUnderbar
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (flags & FlagIsUnderbar) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => flags = value ? (byte)(flags | FlagIsUnderbar) : (byte)(flags & ~FlagIsUnderbar);
        }

        /// <summary>Gets or sets when an operator displays stacked limits above and below.</summary>
        public MathLimitPlacement LimitPlacement
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (MathLimitPlacement)((flags & LimitPlacementMask) >> LimitPlacementShift);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => flags = (byte)((flags & ~LimitPlacementMask)
                                  | ((byte)value << LimitPlacementShift));
        }

        /// <summary>Gets or sets whether display style uses a large glyph variant for the operator.</summary>
        /// <remarks>This is independent of limit placement: integrals are large but normally keep scripts beside them.</remarks>
        public bool IsLargeOperator
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (flags & FlagIsLargeOperator) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => flags = value ? (byte)(flags | FlagIsLargeOperator) : (byte)(flags & ~FlagIsLargeOperator);
        }

        /// <summary>
        /// Gets or sets the right delimiter codepoint (for Delimiter nodes).
        /// Stored in <see cref="payload"/>.
        /// </summary>
        public int DelimiterRight
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => payload;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => payload = value;
        }

        /// <summary>
        /// Gets or sets the column count (for Matrix nodes).
        /// Stored in <see cref="payload"/>. Row count = childCount / matrixCols.
        /// </summary>
        public int MatrixCols
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => payload;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => payload = value;
        }

        /// <summary>
        /// Gets the row count for Matrix nodes.
        /// </summary>
        public int MatrixRows
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => payload > 0 ? childCount / payload : 0;
        }

        /// <summary>
        /// Gets or sets the matrix delimiter style.
        /// Stored in <see cref="commandStart"/> (unused by Matrix nodes).
        /// </summary>
        public MatrixDelimiterStyle MatrixDelimiter
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (MatrixDelimiterStyle)commandStart;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => commandStart = (int)value;
        }

        /// <summary>
        /// Gets or sets the column alignment policy for Matrix nodes.
        /// Stored in <see cref="commandLength"/> (unused otherwise by Matrix nodes).
        /// </summary>
        public MatrixAlignmentStyle MatrixAlignment
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (MatrixAlignmentStyle)commandLength;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => commandLength = (int)value;
        }

        /// <summary>
        /// Gets or sets the space width in mu units (for Space nodes).
        /// Stored as bit-reinterpreted float in <see cref="payload"/>.
        /// </summary>
        /// <remarks>
        /// 1 mu = 1/18 em. Standard TeX spaces: \, = 3mu, \: = 4mu, \; = 5mu,
        /// \! = -3mu, \quad = 18mu, \qquad = 36mu.
        /// </remarks>
        public float SpaceWidth
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => BitConverter.Int32BitsToSingle(payload);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => payload = BitConverter.SingleToInt32Bits(value);
        }

        /// <summary>
        /// Gets or sets the target math style (for StyleChange nodes).
        /// Stored in the low byte of <see cref="payload"/>.
        /// </summary>
        public MathStyle TargetStyle
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (MathStyle)(payload & 0xFF);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => payload = (int)value;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return type switch
            {
                MathNodeType.Atom => $"Atom({atomType}, U+{codepoint:X4})",
                MathNodeType.Group => $"Group({childCount} children)",
                MathNodeType.Reference => $"Reference({child0})",
                MathNodeType.Fraction => $"Fraction(rule={HasRule})",
                MathNodeType.Radical => $"Radical(degree={child1 != None})",
                MathNodeType.Script => $"Script(sup={child1 != None}, sub={child2 != None})",
                MathNodeType.Delimiter => $"Delim(L=U+{codepoint:X4}, R=U+{DelimiterRight:X4})",
                MathNodeType.Accent => $"Accent(U+{codepoint:X4})",
                MathNodeType.Bar => IsUnderbar ? "Underbar" : "Overbar",
                MathNodeType.Operator => $"Op(U+{codepoint:X4}, limits={LimitPlacement}, large={IsLargeOperator})",
                MathNodeType.Matrix => $"Matrix({MatrixRows}x{MatrixCols})",
                MathNodeType.Space => $"Space({SpaceWidth}mu)",
                MathNodeType.Text => $"Text(@{commandStart}..{commandStart + commandLength})",
                MathNodeType.StyleChange => $"Style({TargetStyle})",
                _ => $"MathNode({type})",
            };
        }
    }
}
