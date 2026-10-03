using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Flat array storage for a math AST with index-based child references.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stores the tree as a flat <see cref="PooledBuffer{T}"/> of <see cref="MathNode"/> structs.
    /// All child references within nodes are indices into this array. This design avoids
    /// per-node heap allocation and enables cache-friendly layout traversal.
    /// </para>
    /// <para>
    /// <b>Lifecycle:</b> Call <see cref="Rent"/> before parsing. The parser calls <see cref="Add"/>
    /// to append nodes. The layout engine reads nodes by index. Call <see cref="Return"/>
    /// after the formula is no longer needed to release the buffer back to the pool.
    /// </para>
    /// <para>
    /// <b>Memory:</b> Uses pooled contiguous storage for zero-allocation steady state.
    /// Typical formulas use 10–100 nodes. The initial capacity is 32, growing as needed.
    /// </para>
    /// </remarks>
    /// <seealso cref="MathNode"/>
    /// <seealso cref="PooledBuffer{T}"/>
    internal struct MathNodeList
    {
        private const int DefaultCapacity = 32;

        /// <summary>The flat node storage buffer.</summary>
        public PooledBuffer<MathNode> nodes;

        /// <summary>
        /// Index of the root node in the array. Typically 0 or the last node added
        /// (depending on parser convention). Set by the parser after building the tree.
        /// </summary>
        public int root;

        /// <summary>Gets the number of nodes in the list.</summary>
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => nodes.count;
        }

        /// <summary>Gets a reference to the node at the specified index.</summary>
        /// <param name="index">The zero-based node index.</param>
        /// <returns>A reference to the node.</returns>
        public ref MathNode this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref nodes[index];
        }

        /// <summary>
        /// Rents a buffer from the pool and resets the list.
        /// </summary>
        /// <param name="capacity">Initial capacity hint. Defaults to 32 nodes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rent(int capacity = DefaultCapacity)
        {
            nodes.Rent(capacity);
            root = MathNode.None;
        }

        /// <summary>
        /// Returns the buffer to the pool and resets the list.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return()
        {
            nodes.Return();
            root = MathNode.None;
        }

        /// <summary>
        /// Resets the node count to zero without releasing the buffer.
        /// Allows reuse for parsing another formula.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            nodes.FakeClear();
            root = MathNode.None;
        }

        /// <summary>
        /// Appends a node to the list and returns its index.
        /// </summary>
        /// <param name="node">The node to add.</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Add(MathNode node)
        {
            var index = nodes.count;
            nodes.Add(node);
            return index;
        }

        /// <summary>
        /// Reserves space for a node and returns its index. The caller must fill in the node data.
        /// </summary>
        /// <returns>The index of the reserved slot.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Reserve()
        {
            var index = nodes.count;
            nodes.Add(default);
            return index;
        }

        /// <summary>
        /// Adds an Atom node and returns its index.
        /// </summary>
        /// <param name="atomType">The TeX atom classification.</param>
        /// <param name="codepoint">The Unicode codepoint of the symbol.</param>
        /// <param name="commandStart">Start index of the command in the source string.</param>
        /// <param name="commandLength">Length of the command in the source string.</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddAtom(MathAtomType atomType, int codepoint, int commandStart = 0, int commandLength = 0)
        {
            return Add(new MathNode
            {
                type = MathNodeType.Atom,
                atomType = atomType,
                codepoint = codepoint,
                commandStart = commandStart,
                commandLength = commandLength,
                child0 = MathNode.None,
                child1 = MathNode.None,
                child2 = MathNode.None,
            });
        }

        /// <summary>
        /// Adds a Group node and returns its index. Children should already be added
        /// at contiguous indices [childStart, childStart + childCount).
        /// </summary>
        /// <param name="childStart">Index of the first child node.</param>
        /// <param name="childCount">Number of child nodes.</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddGroup(int childStart, int childCount, MathAtomType atomType = MathAtomType.Ord)
        {
            return Add(new MathNode
            {
                type = MathNodeType.Group,
                atomType = atomType,
                childStart = childStart,
                childCount = childCount,
                child0 = MathNode.None,
                child1 = MathNode.None,
                child2 = MathNode.None,
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddReference(int nodeIndex)
        {
            var atomType = GetAtomType(ref nodes[nodeIndex]);
            return Add(new MathNode
            {
                type = MathNodeType.Reference,
                child0 = nodeIndex,
                child1 = MathNode.None,
                child2 = MathNode.None,
                payload = atomType
            });
        }

        /// <summary>
        /// Adds a Fraction node and returns its index.
        /// </summary>
        /// <param name="numerator">Index of the numerator node.</param>
        /// <param name="denominator">Index of the denominator node.</param>
        /// <param name="hasRule">Whether to draw the fraction bar.</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddFraction(int numerator, int denominator, bool hasRule)
        {
            var node = new MathNode
            {
                type = MathNodeType.Fraction,
                child0 = numerator,
                child1 = denominator,
                child2 = MathNode.None,
            };
            node.HasRule = hasRule;
            return Add(node);
        }

        /// <summary>
        /// Adds a Radical node and returns its index.
        /// </summary>
        /// <param name="body">Index of the body node.</param>
        /// <param name="degree">Index of the degree node, or <see cref="MathNode.None"/> for square root.</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddRadical(int body, int degree = MathNode.None)
        {
            return Add(new MathNode
            {
                type = MathNodeType.Radical,
                child0 = body,
                child1 = degree,
                child2 = MathNode.None,
            });
        }

        /// <summary>
        /// Adds a Script node (superscript and/or subscript) and returns its index.
        /// </summary>
        /// <param name="base_">Index of the base node.</param>
        /// <param name="superscript">Index of the superscript node, or <see cref="MathNode.None"/>.</param>
        /// <param name="subscript">Index of the subscript node, or <see cref="MathNode.None"/>.</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddScript(int base_, int superscript = MathNode.None, int subscript = MathNode.None)
        {
            var baseType = GetAtomType(ref nodes[base_]);
            return Add(new MathNode
            {
                type = MathNodeType.Script,
                atomType = baseType >= 0 ? (MathAtomType)baseType : MathAtomType.Ord,
                child0 = base_,
                child1 = superscript,
                child2 = subscript,
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetAtomType(ref MathNode node)
        {
            switch (node.type)
            {
                case MathNodeType.Atom:
                case MathNodeType.Group:
                case MathNodeType.Script:
                    return (int)node.atomType;
                case MathNodeType.Reference:
                    return node.payload;
                case MathNodeType.Operator:
                    return (int)MathAtomType.Op;
                case MathNodeType.Fraction:
                case MathNodeType.Delimiter:
                case MathNodeType.Matrix:
                    return (int)MathAtomType.Inner;
                case MathNodeType.Radical:
                case MathNodeType.Accent:
                case MathNodeType.Bar:
                case MathNodeType.Text:
                    return (int)MathAtomType.Ord;
                default:
                    return -1;
            }
        }

        /// <summary>
        /// Adds a Delimiter node and returns its index.
        /// </summary>
        /// <param name="body">Index of the body node.</param>
        /// <param name="leftDelimCodepoint">Unicode codepoint of the left delimiter (0 for none).</param>
        /// <param name="rightDelimCodepoint">Unicode codepoint of the right delimiter (0 for none).</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddDelimiter(int body, int leftDelimCodepoint, int rightDelimCodepoint)
        {
            var node = new MathNode
            {
                type = MathNodeType.Delimiter,
                atomType = MathAtomType.Inner,
                child0 = body,
                child1 = MathNode.None,
                child2 = MathNode.None,
                codepoint = leftDelimCodepoint,
            };
            node.DelimiterRight = rightDelimCodepoint;
            return Add(node);
        }

        /// <summary>
        /// Adds an Accent node and returns its index.
        /// </summary>
        /// <param name="base_">Index of the base node.</param>
        /// <param name="accentCodepoint">Unicode codepoint of the accent character.</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddAccent(int base_, int accentCodepoint)
        {
            return Add(new MathNode
            {
                type = MathNodeType.Accent,
                child0 = base_,
                child1 = MathNode.None,
                child2 = MathNode.None,
                codepoint = accentCodepoint,
            });
        }

        /// <summary>Adds an overbar or underbar node and returns its index.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddBar(int base_, bool underbar)
        {
            var node = new MathNode
            {
                type = MathNodeType.Bar,
                atomType = MathAtomType.Ord,
                child0 = base_,
                child1 = MathNode.None,
                child2 = MathNode.None,
            };
            node.IsUnderbar = underbar;
            return Add(node);
        }

        /// <summary>
        /// Adds an Operator node and returns its index.
        /// </summary>
        /// <param name="codepoint">Unicode codepoint of the operator symbol.</param>
        /// <param name="limitPlacement">When scripts become stacked limits.</param>
        /// <param name="isLargeOperator">Whether display style uses a large glyph variant.</param>
        /// <param name="commandStart">Start of the command name in source.</param>
        /// <param name="commandLength">Length of the command name.</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddOperator(int codepoint, MathLimitPlacement limitPlacement, bool isLargeOperator,
            int commandStart = 0, int commandLength = 0)
        {
            var node = new MathNode
            {
                type = MathNodeType.Operator,
                atomType = MathAtomType.Op,
                child0 = MathNode.None,
                child1 = MathNode.None,
                child2 = MathNode.None,
                codepoint = codepoint,
                commandStart = commandStart,
                commandLength = commandLength,
            };
            node.LimitPlacement = limitPlacement;
            node.IsLargeOperator = isLargeOperator;
            return Add(node);
        }

        /// <summary>
        /// Adds a Matrix node and returns its index.
        /// </summary>
        /// <param name="cellStart">Index of the first cell node.</param>
        /// <param name="cellCount">Total number of cell nodes (rows * cols).</param>
        /// <param name="cols">Number of columns.</param>
        /// <param name="delimiter">Matrix delimiter style.</param>
        /// <param name="alignment">Column alignment and spacing policy.</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddMatrix(int cellStart, int cellCount, int cols, MatrixDelimiterStyle delimiter,
            MatrixAlignmentStyle alignment)
        {
            var node = new MathNode
            {
                type = MathNodeType.Matrix,
                atomType = MathAtomType.Inner,
                child0 = MathNode.None,
                child1 = MathNode.None,
                child2 = MathNode.None,
                childStart = cellStart,
                childCount = cellCount,
            };
            node.MatrixCols = cols;
            node.MatrixDelimiter = delimiter;
            node.MatrixAlignment = alignment;
            return Add(node);
        }

        /// <summary>
        /// Adds a Space node and returns its index.
        /// </summary>
        /// <param name="widthMu">Space width in mu units (1 mu = 1/18 em).</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddSpace(float widthMu)
        {
            var node = new MathNode
            {
                type = MathNodeType.Space,
                child0 = MathNode.None,
                child1 = MathNode.None,
                child2 = MathNode.None,
            };
            node.SpaceWidth = widthMu;
            return Add(node);
        }

        /// <summary>
        /// Adds a Text node and returns its index.
        /// </summary>
        /// <param name="textStart">Start index of the text content in the source string.</param>
        /// <param name="textLength">Length of the text content.</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddText(int textStart, int textLength)
        {
            return Add(new MathNode
            {
                type = MathNodeType.Text,
                child0 = MathNode.None,
                child1 = MathNode.None,
                child2 = MathNode.None,
                commandStart = textStart,
                commandLength = textLength,
            });
        }

        /// <summary>
        /// Adds a StyleChange node and returns its index.
        /// </summary>
        /// <param name="style">The target math style.</param>
        /// <returns>The index of the added node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddStyleChange(MathStyle style)
        {
            var node = new MathNode
            {
                type = MathNodeType.StyleChange,
                child0 = MathNode.None,
                child1 = MathNode.None,
                child2 = MathNode.None,
            };
            node.TargetStyle = style;
            return Add(node);
        }

    }
}
