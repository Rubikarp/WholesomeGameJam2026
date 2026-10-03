using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Specifies the type of a LaTeX math token.
    /// </summary>
    internal enum MathTokenType : byte
    {
        /// <summary>Numeric literal: digits with optional decimal point (e.g., "3", "3.14").</summary>
        Number,

        /// <summary>Single letter used as a variable (e.g., "x", "y").</summary>
        Letter,

        /// <summary>LaTeX command: backslash followed by one or more letters (e.g., \frac, \alpha).</summary>
        Command,

        /// <summary>Superscript operator: <c>^</c>.</summary>
        Superscript,

        /// <summary>Subscript operator: <c>_</c>.</summary>
        Subscript,

        /// <summary>Opening brace: <c>{</c>.</summary>
        OpenBrace,

        /// <summary>Closing brace: <c>}</c>.</summary>
        CloseBrace,

        /// <summary>Opening square bracket: <c>[</c>.</summary>
        OpenBracket,

        /// <summary>Closing square bracket: <c>]</c>.</summary>
        CloseBracket,

        /// <summary>Opening parenthesis: <c>(</c>.</summary>
        OpenParen,

        /// <summary>Closing parenthesis: <c>)</c>.</summary>
        CloseParen,

        /// <summary>Ampersand column separator: <c>&amp;</c> (used in matrices and alignment).</summary>
        Ampersand,

        /// <summary>Double backslash row separator: <c>\\</c> (used in matrices and alignment).</summary>
        Newline,

        /// <summary>Binary or relational operator: <c>+</c>, <c>-</c>, <c>*</c>, <c>/</c>, <c>=</c>, <c>&lt;</c>, <c>&gt;</c>, <c>|</c>, <c>!</c>, <c>'</c>, <c>@</c>.</summary>
        Operator,

        /// <summary>Punctuation: <c>,</c>, <c>;</c>, <c>:</c>, <c>.</c> when not part of a number.</summary>
        Punctuation,

        /// <summary>Whitespace: spaces, tabs, newlines (generally ignored in math mode but preserved for completeness).</summary>
        Whitespace,

        /// <summary>End of input sentinel. Returned when no more characters remain.</summary>
        End,
    }


    /// <summary>
    /// Represents a single token from a LaTeX math expression.
    /// </summary>
    /// <remarks>
    /// Value type with no heap allocations. References the source text by index and length
    /// rather than storing a substring. Use <see cref="Slice"/> to obtain the token's text
    /// as a <see cref="ReadOnlySpan{T}"/> from the original source.
    /// </remarks>
    internal readonly struct MathToken
    {
        /// <summary>The token classification.</summary>
        public readonly MathTokenType type;

        /// <summary>Start index of this token in the source string.</summary>
        public readonly int start;

        /// <summary>Number of characters in this token.</summary>
        public readonly int length;

        /// <summary>
        /// Initializes a new token.
        /// </summary>
        /// <param name="type">The token type.</param>
        /// <param name="start">Start index in the source string.</param>
        /// <param name="length">Number of characters.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MathToken(MathTokenType type, int start, int length)
        {
            this.type = type;
            this.start = start;
            this.length = length;
        }

        /// <summary>Gets the exclusive end index of this token in the source string.</summary>
        public int End
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => start + length;
        }

        /// <summary>
        /// Extracts this token's text from the source as a span. Zero allocation.
        /// </summary>
        /// <param name="source">The original source string passed to the tokenizer.</param>
        /// <returns>A read-only span over the token's characters.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<char> Slice(ReadOnlySpan<char> source)
        {
            return source.Slice(start, length);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"[{type} @{start}..{End})";
        }
    }


    /// <summary>
    /// Zero-allocation tokenizer for LaTeX math expressions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scans a LaTeX math string character by character, producing <see cref="MathToken"/> values
    /// that reference the source by index. No heap allocation occurs during tokenization.
    /// </para>
    /// <para>
    /// Modeled after KaTeX's lexer: backslash + letters = command, backslash + single non-letter = control symbol,
    /// double backslash = newline, digits with optional decimal = number.
    /// </para>
    /// <para>
    /// Usage:
    /// <code>
    /// var tokenizer = new MathTokenizer(input);
    /// while (true)
    /// {
    ///     var token = tokenizer.NextToken();
    ///     if (token.type == MathTokenType.End) break;
    ///     // process token...
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    internal ref struct MathTokenizer
    {
        private readonly ReadOnlySpan<char> source;
        private int position;

        /// <summary>
        /// Initializes the tokenizer with a span over the input text.
        /// </summary>
        /// <param name="source">The LaTeX math expression to tokenize.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MathTokenizer(ReadOnlySpan<char> source)
        {
            this.source = source;
            position = 0;
        }

        /// <summary>Gets the current read position within the source span.</summary>
        public int Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => position;
        }

        /// <summary>Returns true if there are no more characters to consume.</summary>
        public bool IsAtEnd
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => position >= source.Length;
        }

        /// <summary>
        /// Advances past the next token and returns it.
        /// </summary>
        /// <returns>
        /// The next token. Returns a token with <see cref="MathTokenType.End"/> when
        /// the end of input is reached. Subsequent calls continue returning End.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MathToken NextToken()
        {
            while (position < source.Length && source[position] == '%')
                SkipComment();

            if (position >= source.Length)
                return new MathToken(MathTokenType.End, source.Length, 0);

            var c = source[position];

            if (c == '\\')
                return ScanBackslash();

            if (IsDigit(c))
                return ScanNumber();

            if (IsLetter(c))
                return ScanLetter();

            if (Ascii.IsWhitespace(c))
                return ScanWhitespace();

            return ScanSingleChar(c);
        }

        /// <summary>
        /// Resets the tokenizer to a specific position.
        /// </summary>
        /// <param name="pos">The position to seek to (must be within bounds or equal to length).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Seek(int pos)
        {
            position = pos;
        }

        /// <summary>
        /// Skips a <c>%</c> comment through the end of its line. TeX catcode 14.
        /// </summary>
        private void SkipComment()
        {
            while (position < source.Length)
            {
                var c = source[position];
                position++;
                if (c == '\n') break;
                if (c == '\r')
                {
                    if (position < source.Length && source[position] == '\n')
                        position++;
                    break;
                }
            }
        }

        /// <summary>
        /// Scans a backslash sequence: command (<c>\frac</c>), newline (<c>\\</c>),
        /// escaped brace (<c>\{</c>, <c>\}</c>), or a single control symbol (<c>\,</c>).
        /// </summary>
        private MathToken ScanBackslash()
        {
            var start = position;
            position++;

            if (position >= source.Length)
                return new MathToken(MathTokenType.Command, start, 1);

            var next = source[position];

            if (next == '\\')
            {
                position++;
                return new MathToken(MathTokenType.Newline, start, 2);
            }

            if (IsLetter(next))
            {
                position++;
                while (position < source.Length && IsLetter(source[position]))
                    position++;

                var tokenLength = position - start;

                while (position < source.Length && Ascii.IsWhitespace(source[position]))
                    position++;

                return new MathToken(MathTokenType.Command, start, tokenLength);
            }

            position++;
            return new MathToken(MathTokenType.Command, start, 2);
        }

        /// <summary>
        /// Scans a numeric literal: one or more digits with an optional decimal point
        /// followed by more digits (e.g., <c>3</c>, <c>3.14</c>).
        /// </summary>
        /// <remarks>
        /// Called only when the first character is already known to be a digit.
        /// A trailing dot without subsequent digits (e.g., <c>3.</c>) is not consumed —
        /// the dot will be emitted as a separate Punctuation token.
        /// </remarks>
        private MathToken ScanNumber()
        {
            var start = position;
            while (position < source.Length && IsDigit(source[position]))
                position++;

            if (position < source.Length && source[position] == '.')
            {
                var afterDot = position + 1;
                if (afterDot < source.Length && IsDigit(source[afterDot]))
                {
                    position = afterDot + 1;
                    while (position < source.Length && IsDigit(source[position]))
                        position++;
                }
            }

            return new MathToken(MathTokenType.Number, start, position - start);
        }

        /// <summary>
        /// Scans a single letter token (variable name).
        /// In LaTeX math mode, each letter is its own variable — "xy" is x * y.
        /// </summary>
        /// <remarks>
        /// For surrogate pairs (characters outside the BMP, e.g., mathematical italic 𝑎 = U+1D44E),
        /// both surrogates are emitted as a single token.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MathToken ScanLetter()
        {
            var start = position;
            position += Utf16.SizeAt(source, position);

            return new MathToken(MathTokenType.Letter, start, position - start);
        }

        /// <summary>
        /// Scans a run of contiguous whitespace characters (spaces, tabs, newlines).
        /// </summary>
        private MathToken ScanWhitespace()
        {
            var start = position;
            while (position < source.Length && Ascii.IsWhitespace(source[position]))
                position++;

            return new MathToken(MathTokenType.Whitespace, start, position - start);
        }

        /// <summary>
        /// Classifies and emits a single-character token based on its character value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MathToken ScanSingleChar(char c)
        {
            var start = position;

            var len = Utf16.SizeAt(source, position);

            position += len;

            var type = ClassifyChar(c);
            return new MathToken(type, start, len);
        }

        /// <summary>
        /// Classifies a single character into its <see cref="MathTokenType"/>.
        /// Used for characters that are not part of a multi-character token.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MathTokenType ClassifyChar(char c)
        {
            switch (c)
            {
                case '^': return MathTokenType.Superscript;
                case '_': return MathTokenType.Subscript;
                case '{': return MathTokenType.OpenBrace;
                case '}': return MathTokenType.CloseBrace;
                case '[': return MathTokenType.OpenBracket;
                case ']': return MathTokenType.CloseBracket;
                case '(': return MathTokenType.OpenParen;
                case ')': return MathTokenType.CloseParen;
                case '&': return MathTokenType.Ampersand;

                case '+':
                case '-':
                case '*':
                case '/':
                case '=':
                case '<':
                case '>':
                case '|':
                case '!':
                case '\'':
                case '@':
                    return MathTokenType.Operator;

                case '~':
                    return MathTokenType.Whitespace;

                case ',':
                case ';':
                case ':':
                case '.':
                    return MathTokenType.Punctuation;

                case '$':
                case '#':
                    return MathTokenType.Operator;

                default:
                    return MathTokenType.Letter;
            }
        }

        /// <summary>Returns true if the character is an ASCII digit ('0'–'9').</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDigit(char c)
        {
            return (uint)(c - '0') <= 9;
        }

        /// <summary>Returns true if the character is an ASCII letter ('A'–'Z' or 'a'–'z').</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLetter(char c)
        {
            return (uint)((c | 0x20) - 'a') < 26;
        }
    }
}
