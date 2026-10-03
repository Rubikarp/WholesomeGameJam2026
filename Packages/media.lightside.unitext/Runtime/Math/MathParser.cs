using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Recursive descent parser for LaTeX math expressions backed by pooled node storage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transforms a LaTeX math string into a flat <see cref="MathNodeList"/> AST.
    /// The parser uses a <see cref="MathTokenizer"/> for lexing and
    /// <see cref="MathSymbols"/> for command resolution. All storage goes through the
    /// caller-provided <see cref="MathNodeList"/>, producing no garbage.
    /// </para>
    /// <para>
    /// <b>Grammar hierarchy (recursive descent):</b>
    /// <list type="number">
    /// <item><see cref="Parse"/> — entry point, calls ParseExpression then expects End</item>
    /// <item><see cref="ParseExpression"/> — loop of ParseAtom + ParseScript, collects into Group</item>
    /// <item><see cref="ParseAtom"/> — single element: letter, number, command, group, operator, delimiter</item>
    /// <item><see cref="ParseScript"/> — checks for <c>^</c> / <c>_</c> after an atom base</item>
    /// <item><see cref="ParseGroupInner"/> — brace-delimited <c>{...}</c> subexpression</item>
    /// <item><see cref="ParseFraction"/> — <c>\frac{num}{den}</c></item>
    /// <item><see cref="ParseRadical"/> — <c>\sqrt{body}</c> or <c>\sqrt[n]{body}</c></item>
    /// <item><see cref="ParseDelimited"/> — <c>\left...\right</c> with stretchy delimiters</item>
    /// <item><see cref="ParseEnvironment"/> — <c>\begin{name}...\end{name}</c></item>
    /// <item><see cref="ParseMatrix"/> — <c>&amp;</c>-separated columns, <c>\\</c>-separated rows</item>
    /// <item><see cref="ParseAccent"/> — <c>\hat{x}</c>, <c>\vec{x}</c>, etc.</item>
    /// <item><see cref="ParseTextGroup"/> — <c>\text{...}</c> raw text capture</item>
    /// </list>
    /// </para>
    /// <para>
    /// Malformed or unsupported input fails with a <see cref="FormatException"/> at its source offset.
    /// </para>
    /// </remarks>
    internal ref struct MathParser
    {
        private MathTokenizer tokenizer;
        private readonly ReadOnlySpan<char> source;
        private MathNodeList nodes;
        private PooledBuffer<int> sequenceScratch;
        private int errorPosition;
        private int romanDepth;

        private bool success
        {
            get => errorPosition < 0;
            set
            {
                if (value)
                    errorPosition = -1;
                else if (errorPosition < 0)
                    errorPosition = tokenizer.Position;
            }
        }

        /// <summary>
        /// Parses a LaTeX math expression into the provided node list.
        /// </summary>
        /// <param name="input">The LaTeX math expression to parse.</param>
        /// <param name="nodes">
        /// The node list to populate. Must already be rented via <see cref="MathNodeList.Rent"/>.
        /// Updated in place with the parsed AST and root index.
        /// </param>
        public static void Parse(ReadOnlySpan<char> input, ref MathNodeList nodes)
        {
            var parser = new MathParser(input, nodes);
            parser.sequenceScratch.Rent(32);
            try
            {
                parser.nodes.Clear();
                var root = parser.ParseExpression(BreakCondition.EndOfInput);
                parser.nodes.root = root;
                var trailing = parser.PeekSkipWhitespace();
                if (trailing.type != MathTokenType.End && parser.errorPosition < 0)
                    parser.errorPosition = trailing.start;
                nodes = parser.nodes;
                if (!parser.success)
                    throw new FormatException(
                        $"Invalid math expression at UTF-16 offset {parser.errorPosition}: \"{input.ToString()}\".");
            }
            finally
            {
                parser.sequenceScratch.Return();
            }
        }

        private MathParser(ReadOnlySpan<char> input, MathNodeList nodes)
        {
            tokenizer = new MathTokenizer(input);
            source = input;
            this.nodes = nodes;
            sequenceScratch = default;
            errorPosition = -1;
            romanDepth = 0;
        }

        [Flags]
        private enum BreakCondition
        {
            EndOfInput = 0,
            CloseBrace = 1 << 0,
            Right = 1 << 1,
            End = 1 << 2,
            Ampersand = 1 << 3,
            Newline = 1 << 4,
            CloseBracket = 1 << 5,
        }

        /// <summary>
        /// Peeks at the next non-whitespace token without consuming it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MathToken PeekSkipWhitespace()
        {
            var saved = tokenizer.Position;
            var tok = SkipWhitespace();
            tokenizer.Seek(saved);
            return tok;
        }

        /// <summary>
        /// Consumes and returns the next non-whitespace token.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MathToken SkipWhitespace()
        {
            while (true)
            {
                var tok = tokenizer.NextToken();
                if (tok.type != MathTokenType.Whitespace)
                    return tok;
            }
        }

        /// <summary>
        /// Returns the span text of a command token, excluding the leading backslash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<char> CommandName(MathToken token)
        {
            if (token.length > 1 && source[token.start] == '\\')
                return source.Slice(token.start + 1, token.length - 1);
            return token.Slice(source);
        }

        /// <summary>
        /// Parses a sequence of atoms with optional scripts.
        /// Returns a single node index if only one atom was found, or a Group node index
        /// if multiple atoms were parsed.
        /// </summary>
        /// <remarks>
        /// Top-level children of an expression are NOT contiguous in the flat node array
        /// because compound nodes (fractions, radicals, scripts) insert their internal
        /// children before the parent. We collect top-level indices in a pooled buffer
        /// and re-emit them as contiguous reference nodes at the end.
        /// </remarks>
        private int ParseExpression(BreakCondition breakOn)
        {
            var scratchStart = sequenceScratch.count;

            while (true)
            {
                var peek = PeekSkipWhitespace();

                if (peek.type == MathTokenType.End)
                    break;

                if ((breakOn & BreakCondition.CloseBrace) != 0 && peek.type == MathTokenType.CloseBrace)
                    break;

                if ((breakOn & BreakCondition.CloseBracket) != 0 && peek.type == MathTokenType.CloseBracket)
                    break;

                if ((breakOn & BreakCondition.Ampersand) != 0 && peek.type == MathTokenType.Ampersand)
                    break;

                if ((breakOn & BreakCondition.Newline) != 0 && peek.type == MathTokenType.Newline)
                    break;

                if ((breakOn & BreakCondition.Right) != 0 && peek.type == MathTokenType.Command)
                {
                    var cmd = CommandName(peek);
                    if (SpanEquals(cmd, "right"))
                        break;
                }

                if ((breakOn & BreakCondition.End) != 0 && peek.type == MathTokenType.Command)
                {
                    var cmd = CommandName(peek);
                    if (SpanEquals(cmd, "end"))
                        break;
                }

                var atomIndex = ParseAtom();
                if (atomIndex == MathNode.None)
                {
                    var nextPeek = PeekSkipWhitespace();
                    if (nextPeek.type == MathTokenType.Superscript || nextPeek.type == MathTokenType.Subscript)
                    {
                        atomIndex = nodes.AddGroup(nodes.Count, 0);
                    }
                    else
                    {
                        break;
                    }
                }

                var resultIndex = ParseScript(atomIndex);

                sequenceScratch.Add(resultIndex);
            }

            var childCount = sequenceScratch.count - scratchStart;

            if (childCount == 0)
            {
                sequenceScratch.count = scratchStart;
                return nodes.AddGroup(nodes.Count, 0);
            }

            if (childCount == 1)
            {
                var single = sequenceScratch[scratchStart];
                sequenceScratch.count = scratchStart;
                return single;
            }

            var groupChildStart = nodes.Count;

            for (var i = 0; i < childCount; i++)
                nodes.AddReference(sequenceScratch[scratchStart + i]);

            sequenceScratch.count = scratchStart;
            return nodes.AddGroup(groupChildStart, childCount);
        }

        /// <summary>
        /// Parses a single atomic element from the token stream.
        /// </summary>
        /// <returns>The node index of the parsed atom, or <see cref="MathNode.None"/> on failure.</returns>
        private int ParseAtom()
        {
            var token = SkipWhitespace();

            switch (token.type)
            {
                case MathTokenType.Letter:
                    return ParseLetterAtom(token);

                case MathTokenType.Number:
                    return ParseNumberAtom(token);

                case MathTokenType.Command:
                    return ParseCommand(token);

                case MathTokenType.OpenBrace:
                    return ParseGroupInner();

                case MathTokenType.OpenParen:
                    return nodes.AddAtom(MathAtomType.Open, '(', token.start, token.length);

                case MathTokenType.CloseParen:
                    return nodes.AddAtom(MathAtomType.Close, ')', token.start, token.length);

                case MathTokenType.OpenBracket:
                    return nodes.AddAtom(MathAtomType.Open, '[', token.start, token.length);

                case MathTokenType.CloseBracket:
                    return nodes.AddAtom(MathAtomType.Close, ']', token.start, token.length);

                case MathTokenType.Operator:
                    return ParseOperatorChar(token);

                case MathTokenType.Punctuation:
                    return ParsePunctuationChar(token);

                case MathTokenType.Superscript:
                case MathTokenType.Subscript:
                    tokenizer.Seek(token.start);
                    return MathNode.None;

                case MathTokenType.End:
                case MathTokenType.CloseBrace:
                case MathTokenType.Ampersand:
                case MathTokenType.Newline:
                    tokenizer.Seek(token.start);
                    return MathNode.None;

                default:
                    return MathNode.None;
            }
        }

        /// <summary>
        /// Parses a letter token into an Ord atom.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ParseLetterAtom(MathToken token)
        {
            var codepoint = (int)UnicodeData.DecodeAt(source, token.start, out _);
            if (codepoint == 0x2212)
                return nodes.AddAtom(MathAtomType.Bin, codepoint, token.start, token.length);

            if (MathSymbols.TryLookupCodepoint(codepoint, out var atomType))
            {
                if (atomType == MathAtomType.Op)
                    return nodes.AddOperator(codepoint, DefaultLimitPlacement(codepoint), IsLargeOperator(codepoint),
                        token.start, token.length);
                return nodes.AddAtom(atomType,
                    atomType == MathAtomType.Ord && romanDepth == 0
                        ? ToMathItalic(codepoint)
                        : codepoint,
                    token.start, token.length);
            }

            return nodes.AddAtom(MathAtomType.Ord,
                romanDepth == 0 ? ToMathItalic(codepoint) : codepoint,
                token.start, token.length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ToMathItalic(int codepoint)
        {
            if ((uint)(codepoint - 'A') <= 'Z' - 'A')
                return 0x1D434 + codepoint - 'A';
            if ((uint)(codepoint - 'a') <= 'z' - 'a')
                return codepoint == 'h' ? 0x210E : 0x1D44E + codepoint - 'a';
            if ((uint)(codepoint - 0x03B1) <= 0x03C9 - 0x03B1)
                return 0x1D6FC + codepoint - 0x03B1;

            switch (codepoint)
            {
                case 0x2202: return 0x1D715;
                case 0x03F5: return 0x1D716;
                case 0x03D1: return 0x1D717;
                case 0x03F0: return 0x1D718;
                case 0x03D5: return 0x1D719;
                case 0x03F1: return 0x1D71A;
                case 0x03D6: return 0x1D71B;
                default: return codepoint;
            }
        }

        /// <summary>
        /// Parses a number token into a sequence of Ord atoms (one per digit)
        /// wrapped in a Group if multiple digits, or a single Atom if one digit.
        /// </summary>
        private int ParseNumberAtom(MathToken token)
        {
            var text = token.Slice(source);
            if (text.Length == 1)
                return nodes.AddAtom(MathAtomType.Ord, text[0], token.start, 1);

            var groupStart = nodes.Count;
            for (var i = 0; i < text.Length; i++)
                nodes.AddAtom(MathAtomType.Ord, text[i], token.start + i, 1);

            return nodes.AddGroup(groupStart, text.Length);
        }

        /// <summary>
        /// Parses a single operator character (+, -, =, etc.) into an Atom
        /// with the appropriate atom type.
        /// </summary>
        private int ParseOperatorChar(MathToken token)
        {
            var ch = source[token.start];
            MathAtomType atomType;

            switch (ch)
            {
                case '=':
                case '<':
                case '>':
                    atomType = MathAtomType.Rel;
                    break;

                case '+':
                case '*':
                    atomType = MathAtomType.Bin;
                    break;

                case '-':
                    atomType = MathAtomType.Bin;
                    ch = '\u2212';
                    break;

                case '/':
                    atomType = MathAtomType.Ord;
                    break;

                case '|':
                    atomType = MathAtomType.Ord;
                    break;

                case '!':
                    atomType = MathAtomType.Ord;
                    break;

                case '\'':
                    atomType = MathAtomType.Ord;
                    break;

                default:
                    atomType = MathAtomType.Ord;
                    break;
            }

            return nodes.AddAtom(atomType, ch, token.start, token.length);
        }

        /// <summary>
        /// Parses a punctuation character into a Punct atom.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ParsePunctuationChar(MathToken token)
        {
            var ch = source[token.start];
            var atomType = ch == ':' ? MathAtomType.Rel : MathAtomType.Punct;
            return nodes.AddAtom(atomType, ch, token.start, token.length);
        }

        /// <summary>
        /// Dispatches a command token to the appropriate parse method.
        /// </summary>
        private int ParseCommand(MathToken token)
        {
            var cmd = CommandName(token);

            if (SpanEquals(cmd, "frac")) return ParseFraction();
            if (SpanEquals(cmd, "dfrac")) return WrapInStyle(ParseFraction(), MathStyle.Display);
            if (SpanEquals(cmd, "tfrac")) return WrapInStyle(ParseFraction(), MathStyle.Text);
            if (SpanEquals(cmd, "cfrac")) return WrapInStyle(ParseFraction(), MathStyle.Display);
            if (SpanEquals(cmd, "binom")) return ParseBinom();
            if (SpanEquals(cmd, "dbinom")) return WrapInStyle(ParseBinom(), MathStyle.Display);
            if (SpanEquals(cmd, "tbinom")) return WrapInStyle(ParseBinom(), MathStyle.Text);
            if (SpanEquals(cmd, "sqrt")) return ParseRadical();
            if (SpanEquals(cmd, "left")) return ParseDelimited();
            if (SpanEquals(cmd, "begin")) return ParseEnvironment();
            if (SpanEquals(cmd, "text")) return ParseTextGroup(token);
            if (SpanEquals(cmd, "mathrm")) return ParseRomanGroup();
            if (SpanEquals(cmd, "textrm")) return ParseTextGroup(token);
            if (SpanEquals(cmd, "operatorname")) return ParseOperatorName(token);
            if (SpanEquals(cmd, "overline")) return nodes.AddBar(ParseRequiredGroup(), false);
            if (SpanEquals(cmd, "underline")) return nodes.AddBar(ParseRequiredGroup(), true);

            if (SpanEquals(cmd, "displaystyle")) return nodes.AddStyleChange(MathStyle.Display);
            if (SpanEquals(cmd, "textstyle")) return nodes.AddStyleChange(MathStyle.Text);
            if (SpanEquals(cmd, "scriptstyle")) return nodes.AddStyleChange(MathStyle.Script);
            if (SpanEquals(cmd, "scriptscriptstyle")) return nodes.AddStyleChange(MathStyle.ScriptScript);

            if (cmd.Length == 1)
            {
                switch (cmd[0])
                {
                    case ',': return nodes.AddSpace(3f);
                    case ':': return nodes.AddSpace(4f);
                    case ';': return nodes.AddSpace(5f);
                    case '!': return nodes.AddSpace(-3f);
                    case ' ': return nodes.AddSpace(MathSpacing.MediumMu);

                    case '{': return nodes.AddAtom(MathAtomType.Open, '{', token.start, token.length);
                    case '}': return nodes.AddAtom(MathAtomType.Close, '}', token.start, token.length);
                    case '%':
                    case '_':
                    case '#':
                    case '$':
                    case '&':
                        return nodes.AddAtom(MathAtomType.Ord, cmd[0], token.start, token.length);
                    case '|':
                        return nodes.AddAtom(MathAtomType.Ord, 0x2225, token.start, token.length);
                }
            }

            if (SpanEquals(cmd, "backslash"))
                return nodes.AddAtom(MathAtomType.Ord, 0x2216, token.start, token.length);

            if (SpanEquals(cmd, "quad")) return nodes.AddSpace(18f);
            if (SpanEquals(cmd, "qquad")) return nodes.AddSpace(36f);
            if (SpanEquals(cmd, "enspace")) return nodes.AddSpace(9f);
            if (SpanEquals(cmd, "thinspace")) return nodes.AddSpace(3f);
            if (SpanEquals(cmd, "medspace")) return nodes.AddSpace(4f);
            if (SpanEquals(cmd, "thickspace")) return nodes.AddSpace(5f);
            if (SpanEquals(cmd, "negthinspace")) return nodes.AddSpace(-3f);
            if (SpanEquals(cmd, "negmedspace")) return nodes.AddSpace(-4f);
            if (SpanEquals(cmd, "negthickspace")) return nodes.AddSpace(-5f);
            if (SpanEquals(cmd, "hspace")) return ParseHSpace();

            if (SpanEquals(cmd, "not"))
            {
                success = false;
                return nodes.AddAtom(MathAtomType.Ord, '?', token.start, token.length);
            }

            if (MathSymbols.TryLookup(cmd, out var info))
            {
                if (info.isAccent)
                    return ParseAccent(info.accentCodepoint);

                if (info.atomType == MathAtomType.Op)
                    return ParseNamedOperator(token, cmd, info);

                var codepoint = info.atomType == MathAtomType.Ord && romanDepth == 0
                    ? ToMathItalic(info.codepoint)
                    : info.codepoint;
                return nodes.AddAtom(info.atomType, codepoint, token.start, token.length);
            }

            success = false;
            return nodes.AddAtom(MathAtomType.Ord, '?', token.start, token.length);
        }

        /// <summary>
        /// Checks for <c>^</c> and/or <c>_</c> following an atom base
        /// and wraps it in a Script node if found.
        /// </summary>
        /// <param name="baseIndex">Index of the base atom node.</param>
        /// <returns>
        /// A Script node index wrapping the base with super/subscripts,
        /// or <paramref name="baseIndex"/> unchanged if no scripts found.
        /// </returns>
        private int ParseScript(int baseIndex)
        {
            var superscript = MathNode.None;
            var subscript = MathNode.None;

            while (true)
            {
                var peek = PeekSkipWhitespace();

                if (peek.type == MathTokenType.Command
                    && TryGetLimitPlacement(CommandName(peek), out var limitPlacement))
                {
                    SkipWhitespace();
                    ref var baseNode = ref nodes[baseIndex];
                    if (baseNode.type == MathNodeType.Operator)
                        baseNode.LimitPlacement = limitPlacement;
                    else
                        success = false;
                }
                else if (peek.type == MathTokenType.Superscript)
                {
                    if (superscript != MathNode.None)
                    {
                        success = false;
                    }
                    SkipWhitespace();
                    superscript = ParseScriptArgument();
                }
                else if (peek.type == MathTokenType.Subscript)
                {
                    if (subscript != MathNode.None)
                    {
                        success = false;
                    }
                    SkipWhitespace();
                    subscript = ParseScriptArgument();
                }
                else if (peek.type == MathTokenType.Operator && source[peek.start] == '\'')
                {
                    var primeStart = nodes.Count;
                    var primeCount = 0;
                    while (true)
                    {
                        var p = PeekSkipWhitespace();
                        if (p.type == MathTokenType.Operator && source[p.start] == '\'')
                        {
                            SkipWhitespace();
                            nodes.AddAtom(MathAtomType.Ord, 0x2032, p.start, 1);
                            primeCount++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (primeCount > 0)
                    {
                        var primeGroup = primeCount == 1
                            ? primeStart
                            : nodes.AddGroup(primeStart, primeCount);

                        if (superscript != MathNode.None)
                        {
                            var mergeStart = nodes.Count;
                            var w0 = nodes.Reserve();
                            var w1 = nodes.Reserve();
                            nodes[w0] = nodes[superscript];
                            nodes[w1] = nodes[primeGroup];
                            superscript = nodes.AddGroup(mergeStart, 2);
                        }
                        else
                        {
                            superscript = primeGroup;
                        }
                    }
                }
                else
                {
                    break;
                }
            }

            if (superscript == MathNode.None && subscript == MathNode.None)
                return baseIndex;

            return nodes.AddScript(baseIndex, superscript, subscript);
        }

        /// <summary>
        /// Parses the argument to a <c>^</c> or <c>_</c> operator.
        /// Accepts either a single token or a brace-delimited group.
        /// </summary>
        private int ParseScriptArgument()
        {
            var peek = PeekSkipWhitespace();

            if (peek.type == MathTokenType.OpenBrace)
            {
                SkipWhitespace();
                return ParseGroupInner();
            }

            var atom = ParseAtom();
            if (atom == MathNode.None)
            {
                success = false;
                return nodes.AddGroup(nodes.Count, 0);
            }
            return atom;
        }

        /// <summary>
        /// Parses the interior of a <c>{...}</c> group (opening brace already consumed).
        /// </summary>
        private int ParseGroupInner()
        {
            var inner = ParseExpression(BreakCondition.CloseBrace);

            var next = SkipWhitespace();
            if (next.type != MathTokenType.CloseBrace)
            {
                success = false;
                tokenizer.Seek(next.start);
            }

            return inner;
        }

        /// <summary>
        /// Parses a required brace group argument <c>{...}</c>.
        /// Returns the expression index or an empty group on error.
        /// </summary>
        private int ParseRequiredGroup()
        {
            var peek = PeekSkipWhitespace();
            if (peek.type == MathTokenType.OpenBrace)
            {
                SkipWhitespace();
                return ParseGroupInner();
            }

            success = false;
            var atom = ParseAtom();
            return atom != MathNode.None ? atom : nodes.AddGroup(nodes.Count, 0);
        }

        /// <summary>
        /// Parses <c>\frac{numerator}{denominator}</c>.
        /// </summary>
        private int ParseFraction()
        {
            var numerator = ParseRequiredGroup();
            var denominator = ParseRequiredGroup();
            return nodes.AddFraction(numerator, denominator, hasRule: true);
        }

        /// <summary>
        /// Parses <c>\binom{n}{k}</c> — fraction without a rule line, with parenthesis delimiters.
        /// </summary>
        private int ParseBinom()
        {
            var top = ParseRequiredGroup();
            var bottom = ParseRequiredGroup();
            var frac = nodes.AddFraction(top, bottom, hasRule: false);
            return nodes.AddDelimiter(frac, '(', ')');
        }

        /// <summary>
        /// Wraps a node in a Group with a preceding StyleChange node,
        /// used for \dfrac/\tfrac/\dbinom/\tbinom which force a specific style.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int WrapInStyle(int contentIndex, MathStyle style)
        {
            var styleNode = nodes.AddStyleChange(style);
            var groupStart = nodes.Count;
            var w0 = nodes.Reserve();
            var w1 = nodes.Reserve();
            nodes[w0] = nodes[styleNode];
            nodes[w1] = nodes[contentIndex];
            var atomType = MathNodeList.GetAtomType(ref nodes[contentIndex]);
            return nodes.AddGroup(groupStart, 2,
                atomType >= 0 ? (MathAtomType)atomType : MathAtomType.Ord);
        }

        /// <summary>
        /// Parses <c>\sqrt{body}</c> or <c>\sqrt[degree]{body}</c>.
        /// </summary>
        private int ParseRadical()
        {
            var degree = MathNode.None;

            var peek = PeekSkipWhitespace();
            if (peek.type == MathTokenType.OpenBracket)
            {
                SkipWhitespace();
                degree = ParseExpression(BreakCondition.CloseBracket);

                var close = SkipWhitespace();
                if (close.type != MathTokenType.CloseBracket)
                {
                    success = false;
                    tokenizer.Seek(close.start);
                }
            }

            var body = ParseRequiredGroup();
            return nodes.AddRadical(body, degree);
        }

        /// <summary>
        /// Parses <c>\left delim ... \right delim</c>.
        /// </summary>
        private int ParseDelimited()
        {
            var leftDelim = ParseDelimiterCodepoint();
            var body = ParseExpression(BreakCondition.Right);

            var rightToken = SkipWhitespace();
            if (rightToken.type != MathTokenType.Command || !SpanEquals(CommandName(rightToken), "right"))
            {
                success = false;
                tokenizer.Seek(rightToken.start);
                return nodes.AddDelimiter(body, leftDelim, 0);
            }

            var rightDelim = ParseDelimiterCodepoint();
            return nodes.AddDelimiter(body, leftDelim, rightDelim);
        }

        /// <summary>
        /// Reads the next token and interprets it as a delimiter codepoint.
        /// Handles <c>(</c>, <c>)</c>, <c>[</c>, <c>]</c>, <c>|</c>, <c>.</c> (invisible),
        /// and command delimiters like <c>\{</c>, <c>\langle</c>, etc.
        /// </summary>
        private int ParseDelimiterCodepoint()
        {
            var tok = SkipWhitespace();

            switch (tok.type)
            {
                case MathTokenType.OpenParen: return '(';
                case MathTokenType.CloseParen: return ')';
                case MathTokenType.OpenBracket: return '[';
                case MathTokenType.CloseBracket: return ']';

                case MathTokenType.Operator:
                    var ch = source[tok.start];
                    if (ch == '|' || ch == '/' || ch == '<' || ch == '>') return ch;
                    break;

                case MathTokenType.Punctuation:
                    if (source[tok.start] == '.') return 0;
                    break;

                case MathTokenType.Command:
                    var cmd = CommandName(tok);
                    if (SpanEquals(cmd, "{") || SpanEquals(cmd, "lbrace")) return '{';
                    if (SpanEquals(cmd, "}") || SpanEquals(cmd, "rbrace")) return '}';
                    if (SpanEquals(cmd, "langle")) return 0x27E8;
                    if (SpanEquals(cmd, "rangle")) return 0x27E9;
                    if (SpanEquals(cmd, "lfloor")) return 0x230A;
                    if (SpanEquals(cmd, "rfloor")) return 0x230B;
                    if (SpanEquals(cmd, "lceil")) return 0x2308;
                    if (SpanEquals(cmd, "rceil")) return 0x2309;
                    if (SpanEquals(cmd, "lvert") || SpanEquals(cmd, "vert")) return 0x2223;
                    if (SpanEquals(cmd, "rvert")) return 0x2223;
                    if (SpanEquals(cmd, "lVert") || SpanEquals(cmd, "Vert")) return 0x2225;
                    if (SpanEquals(cmd, "rVert")) return 0x2225;
                    if (SpanEquals(cmd, "backslash")) return 0x2216;
                    if (SpanEquals(cmd, "uparrow")) return 0x2191;
                    if (SpanEquals(cmd, "downarrow")) return 0x2193;
                    if (SpanEquals(cmd, "updownarrow")) return 0x2195;
                    if (SpanEquals(cmd, "Uparrow")) return 0x21D1;
                    if (SpanEquals(cmd, "Downarrow")) return 0x21D3;
                    if (SpanEquals(cmd, "Updownarrow")) return 0x21D5;

                    if (MathSymbols.TryLookup(cmd, out var info)
                        && (info.atomType == MathAtomType.Open || info.atomType == MathAtomType.Close))
                        return info.codepoint;
                    break;
            }

            success = false;
            return 0;
        }

        /// <summary>
        /// Parses <c>\begin{name}...\end{name}</c>, dispatching to the appropriate handler.
        /// </summary>
        private int ParseEnvironment()
        {
            var peek = PeekSkipWhitespace();
            if (peek.type != MathTokenType.OpenBrace)
            {
                success = false;
                return nodes.AddGroup(nodes.Count, 0);
            }
            SkipWhitespace();

            var nameStart = tokenizer.Position;
            var nameEnd = nameStart;
            while (!tokenizer.IsAtEnd)
            {
                var tok = tokenizer.NextToken();
                if (tok.type == MathTokenType.CloseBrace)
                {
                    nameEnd = tok.start;
                    break;
                }
            }
            if (nameEnd <= nameStart)
                nameEnd = tokenizer.Position;

            var envName = source.Slice(nameStart, nameEnd - nameStart);

            MatrixDelimiterStyle delimStyle;
            MatrixAlignmentStyle alignmentStyle;
            if (SpanEquals(envName, "matrix"))
            {
                delimStyle = MatrixDelimiterStyle.None;
                alignmentStyle = MatrixAlignmentStyle.Centered;
            }
            else if (SpanEquals(envName, "pmatrix"))
            {
                delimStyle = MatrixDelimiterStyle.Parentheses;
                alignmentStyle = MatrixAlignmentStyle.Centered;
            }
            else if (SpanEquals(envName, "bmatrix"))
            {
                delimStyle = MatrixDelimiterStyle.Brackets;
                alignmentStyle = MatrixAlignmentStyle.Centered;
            }
            else if (SpanEquals(envName, "Bmatrix"))
            {
                delimStyle = MatrixDelimiterStyle.Braces;
                alignmentStyle = MatrixAlignmentStyle.Centered;
            }
            else if (SpanEquals(envName, "vmatrix"))
            {
                delimStyle = MatrixDelimiterStyle.Vertical;
                alignmentStyle = MatrixAlignmentStyle.Centered;
            }
            else if (SpanEquals(envName, "Vmatrix"))
            {
                delimStyle = MatrixDelimiterStyle.DoubleVertical;
                alignmentStyle = MatrixAlignmentStyle.Centered;
            }
            else if (SpanEquals(envName, "cases"))
            {
                delimStyle = MatrixDelimiterStyle.Cases;
                alignmentStyle = MatrixAlignmentStyle.Left;
            }
            else if (SpanEquals(envName, "aligned") || SpanEquals(envName, "align")
                     || SpanEquals(envName, "split"))
            {
                delimStyle = MatrixDelimiterStyle.None;
                alignmentStyle = MatrixAlignmentStyle.Alternating;
            }
            else if (SpanEquals(envName, "gathered") || SpanEquals(envName, "gather"))
            {
                delimStyle = MatrixDelimiterStyle.None;
                alignmentStyle = MatrixAlignmentStyle.Centered;
            }
            else
            {
                success = false;
                delimStyle = MatrixDelimiterStyle.None;
                alignmentStyle = MatrixAlignmentStyle.Centered;
            }

            var result = ParseMatrix(delimStyle, alignmentStyle);

            ConsumeEndEnvironment(envName);

            return result;
        }

        /// <summary>
        /// Consumes the <c>\end{name}</c> token sequence.
        /// </summary>
        private void ConsumeEndEnvironment(ReadOnlySpan<char> expectedName)
        {
            var tok = SkipWhitespace();
            if (tok.type != MathTokenType.Command || !SpanEquals(CommandName(tok), "end"))
            {
                success = false;
                tokenizer.Seek(tok.start);
                return;
            }

            var brace = SkipWhitespace();
            if (brace.type != MathTokenType.OpenBrace)
            {
                success = false;
                tokenizer.Seek(brace.start);
                return;
            }

            var nameStart = tokenizer.Position;
            var nameEnd = -1;
            while (!tokenizer.IsAtEnd)
            {
                var inner = tokenizer.NextToken();
                if (inner.type == MathTokenType.CloseBrace)
                {
                    nameEnd = inner.start;
                    break;
                }
            }

            if (nameEnd < nameStart || !source.Slice(nameStart, nameEnd - nameStart).SequenceEqual(expectedName))
                success = false;
        }

        /// <summary>
        /// Parses a matrix body: <c>&amp;</c>-separated columns and <c>\\</c>-separated rows.
        /// </summary>
        private int ParseMatrix(MatrixDelimiterStyle delimStyle, MatrixAlignmentStyle alignmentStyle)
        {
            var scratchStart = sequenceScratch.count;
            var colCount = 0;
            var currentRowCols = 0;
            var firstRow = true;

            while (true)
            {
                var cell = ParseExpression(BreakCondition.Ampersand | BreakCondition.Newline | BreakCondition.End);

                sequenceScratch.Add(cell);
                currentRowCols++;

                var next = PeekSkipWhitespace();

                if (next.type == MathTokenType.Ampersand)
                {
                    SkipWhitespace();
                    continue;
                }

                if (next.type == MathTokenType.Newline)
                {
                    SkipWhitespace();
                    if (firstRow)
                    {
                        colCount = currentRowCols;
                        firstRow = false;
                    }
                    else if (currentRowCols != colCount)
                    {
                        success = false;
                    }
                    currentRowCols = 0;

                    var afterNewline = PeekSkipWhitespace();
                    if (afterNewline.type == MathTokenType.Command && SpanEquals(CommandName(afterNewline), "end"))
                        break;
                    if (afterNewline.type == MathTokenType.End)
                        break;

                    continue;
                }

                if (firstRow)
                    colCount = currentRowCols;
                else if (currentRowCols != colCount)
                    success = false;
                break;
            }

            if (colCount == 0) colCount = 1;

            var cellCount = sequenceScratch.count - scratchStart;

            while (cellCount % colCount != 0)
            {
                var emptyCell = nodes.AddGroup(nodes.Count, 0);
                sequenceScratch.Add(emptyCell);
                cellCount++;
            }

            var matrixCellStart = nodes.Count;

            for (var i = 0; i < cellCount; i++)
            {
                nodes.Add(new MathNode
                {
                    type = MathNodeType.Group,
                    childStart = sequenceScratch[scratchStart + i],
                    childCount = 1,
                    child0 = MathNode.None,
                    child1 = MathNode.None,
                    child2 = MathNode.None,
                });
            }

            sequenceScratch.count = scratchStart;
            return nodes.AddMatrix(matrixCellStart, cellCount, colCount, delimStyle, alignmentStyle);
        }

        /// <summary>
        /// Parses an accent command applied to the next group/atom.
        /// </summary>
        /// <param name="accentCodepoint">The Unicode combining codepoint of the accent.</param>
        private int ParseAccent(int accentCodepoint)
        {
            var base_ = ParseRequiredGroup();
            return nodes.AddAccent(base_, accentCodepoint);
        }

        /// <summary>
        /// Parses <c>\text{...}</c> — captures the raw brace-delimited content.
        /// </summary>
        private int ParseTextGroup(MathToken commandToken)
        {
            var peek = PeekSkipWhitespace();
            if (peek.type != MathTokenType.OpenBrace)
            {
                success = false;
                return nodes.AddText(commandToken.start, commandToken.length);
            }
            SkipWhitespace();

            var textStart = tokenizer.Position;
            var textEnd = textStart;
            var depth = 1;

            while (!tokenizer.IsAtEnd && depth > 0)
            {
                var tok = tokenizer.NextToken();
                if (tok.type == MathTokenType.OpenBrace)
                {
                    success = false;
                    depth++;
                }
                else if (tok.type == MathTokenType.CloseBrace)
                {
                    depth--;
                    if (depth == 0)
                        textEnd = tok.start;
                }
                else if (tok.type == MathTokenType.Command || tok.type == MathTokenType.Newline)
                {
                    success = false;
                }
            }

            if (depth != 0)
            {
                success = false;
                textEnd = tokenizer.Position;
            }

            return nodes.AddText(textStart, textEnd - textStart);
        }

        private int ParseRomanGroup()
        {
            romanDepth++;
            try
            {
                return ParseRequiredGroup();
            }
            finally
            {
                romanDepth--;
            }
        }

        /// <summary>
        /// Parses <c>\operatorname{name}</c> — named operator with custom name.
        /// </summary>
        private int ParseOperatorName(MathToken commandToken)
        {
            var peek = PeekSkipWhitespace();
            var limitPlacement = MathLimitPlacement.Side;
            if (peek.type == MathTokenType.Operator && source[peek.start] == '*')
            {
                SkipWhitespace();
                limitPlacement = MathLimitPlacement.Display;
                peek = PeekSkipWhitespace();
            }
            if (peek.type != MathTokenType.OpenBrace)
            {
                success = false;
                return nodes.AddOperator('?', limitPlacement, false,
                    commandToken.start, commandToken.length);
            }
            SkipWhitespace();

            var nameStart = tokenizer.Position;
            var nameEnd = nameStart;
            var depth = 1;

            while (!tokenizer.IsAtEnd && depth > 0)
            {
                var tok = tokenizer.NextToken();
                if (tok.type == MathTokenType.OpenBrace)
                {
                    success = false;
                    depth++;
                }
                else if (tok.type == MathTokenType.CloseBrace)
                {
                    depth--;
                    if (depth == 0)
                        nameEnd = tok.start;
                }
                else if (tok.type == MathTokenType.Command || tok.type == MathTokenType.Newline)
                {
                    success = false;
                }
            }

            if (depth != 0)
            {
                success = false;
                nameEnd = tokenizer.Position;
            }

            return nodes.AddOperator(0, limitPlacement, false,
                nameStart, nameEnd - nameStart);
        }

        /// <summary>
        /// Creates an Operator node for a named math operator/function
        /// from <see cref="MathSymbols"/>.
        /// </summary>
        private int ParseNamedOperator(MathToken token, ReadOnlySpan<char> cmd, MathSymbolInfo info)
        {
            var limitPlacement = DefaultLimitPlacement(cmd, info.codepoint);
            var isLargeOperator = !SpanEquals(cmd, "smallint") && IsLargeOperator(info.codepoint);

            return nodes.AddOperator(info.codepoint, limitPlacement, isLargeOperator,
                token.start + 1, token.length - 1);
        }

        private static MathLimitPlacement DefaultLimitPlacement(ReadOnlySpan<char> cmd, int codepoint)
        {
            if (SpanEquals(cmd, "intop")) return MathLimitPlacement.Display;

            if (SpanEquals(cmd, "lim")) return MathLimitPlacement.Display;
            if (SpanEquals(cmd, "liminf")) return MathLimitPlacement.Display;
            if (SpanEquals(cmd, "limsup")) return MathLimitPlacement.Display;
            if (SpanEquals(cmd, "sup")) return MathLimitPlacement.Display;
            if (SpanEquals(cmd, "inf")) return MathLimitPlacement.Display;
            if (SpanEquals(cmd, "min")) return MathLimitPlacement.Display;
            if (SpanEquals(cmd, "max")) return MathLimitPlacement.Display;
            if (SpanEquals(cmd, "det")) return MathLimitPlacement.Display;
            if (SpanEquals(cmd, "gcd")) return MathLimitPlacement.Display;
            if (SpanEquals(cmd, "Pr")) return MathLimitPlacement.Display;

            return DefaultLimitPlacement(codepoint);
        }

        private static MathLimitPlacement DefaultLimitPlacement(int codepoint)
        {
            return IsLargeOperator(codepoint) && !IsIntegralOperator(codepoint)
                ? MathLimitPlacement.Display
                : MathLimitPlacement.Side;
        }

        private static bool TryGetLimitPlacement(ReadOnlySpan<char> command,
            out MathLimitPlacement placement)
        {
            if (SpanEquals(command, "limits"))
            {
                placement = MathLimitPlacement.OverUnder;
                return true;
            }
            if (SpanEquals(command, "nolimits"))
            {
                placement = MathLimitPlacement.Side;
                return true;
            }
            if (SpanEquals(command, "displaylimits"))
            {
                placement = MathLimitPlacement.Display;
                return true;
            }

            placement = default;
            return false;
        }

        private static bool IsLargeOperator(int codepoint)
        {
            switch (codepoint)
            {
                case 0x220F:
                case 0x2210:
                case 0x2211:
                case 0x222B:
                case 0x222C:
                case 0x222D:
                case 0x222E:
                case 0x222F:
                case 0x2230:
                case 0x22C0:
                case 0x22C1:
                case 0x22C2:
                case 0x22C3:
                case 0x2A00:
                case 0x2A01:
                case 0x2A02:
                case 0x2A04:
                case 0x2A06:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsIntegralOperator(int codepoint)
        {
            return codepoint >= 0x222B && codepoint <= 0x2230;
        }

        /// <summary>
        /// Parses <c>\hspace{Nmu}</c> or <c>\hspace{Nem}</c>.
        /// </summary>
        private int ParseHSpace()
        {
            var peek = PeekSkipWhitespace();
            if (peek.type != MathTokenType.OpenBrace)
            {
                success = false;
                return nodes.AddSpace(0f);
            }
            SkipWhitespace();

            var contentStart = tokenizer.Position;
            var contentEnd = -1;
            while (!tokenizer.IsAtEnd)
            {
                var tok = tokenizer.NextToken();
                if (tok.type == MathTokenType.CloseBrace)
                {
                    contentEnd = tok.start;
                    break;
                }
            }
            if (contentEnd < contentStart)
            {
                success = false;
                return nodes.AddSpace(0f);
            }
            var content = source.Slice(contentStart, contentEnd - contentStart).Trim();

            var value = ParseSimpleFloat(content, out var unitStart, out var hasDigits);

            var unit = content.Slice(unitStart).Trim();
            if (!hasDigits || !SpanEquals(unit, "mu") && !SpanEquals(unit, "em"))
            {
                success = false;
                return nodes.AddSpace(0f);
            }

            if (SpanEquals(unit, "em"))
                value *= MathSpacing.MuPerEm;

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                success = false;
                return nodes.AddSpace(0f);
            }

            return nodes.AddSpace(value);
        }

        /// <summary>
        /// Parses a simple float value from a span, returning the position after the number.
        /// </summary>
        private static float ParseSimpleFloat(ReadOnlySpan<char> span, out int endPos,
            out bool hasDigits)
        {
            var negative = false;
            var i = 0;
            hasDigits = false;

            if (i < span.Length && span[i] == '-')
            {
                negative = true;
                i++;
            }
            else if (i < span.Length && span[i] == '+')
            {
                i++;
            }

            float result = 0;
            while (i < span.Length && (uint)(span[i] - '0') <= 9)
            {
                hasDigits = true;
                result = result * 10 + (span[i] - '0');
                i++;
            }

            if (i < span.Length && span[i] == '.')
            {
                i++;
                var frac = 0f;
                var divisor = 10f;
                while (i < span.Length && (uint)(span[i] - '0') <= 9)
                {
                    hasDigits = true;
                    frac += (span[i] - '0') / divisor;
                    divisor *= 10f;
                    i++;
                }
                result += frac;
            }

            endPos = i;
            return negative ? -result : result;
        }

        /// <summary>
        /// Compares a span to a string literal without allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SpanEquals(ReadOnlySpan<char> span, string literal)
        {
            if (span.Length != literal.Length) return false;
            for (var i = 0; i < span.Length; i++)
            {
                if (span[i] != literal[i]) return false;
            }
            return true;
        }
    }
}
