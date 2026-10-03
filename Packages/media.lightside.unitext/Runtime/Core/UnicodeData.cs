using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// Provides access to Unicode character properties and the global data provider.
    /// </summary>
    /// <remarks>
    /// Contains common Unicode codepoint constants (whitespace, control characters, BiDi marks)
    /// and helper methods for character classification. The static <see cref="Provider"/> property
    /// gives access to the full Unicode property database for script, line break, and grapheme
    /// cluster information.
    /// </remarks>
    /// <seealso cref="UnicodeDataProvider"/>
    public static class UnicodeData
    {
        #region Unicode Codepoint Constants

        public const int Tab = 0x0009;
        public const int LineFeed = 0x000A;
        public const int VerticalTab = 0x000B;
        public const int FormFeed = 0x000C;
        public const int CarriageReturn = 0x000D;
        public const int Space = 0x0020;
        public const int Hyphen = 0x002D;
        public const int Delete = 0x007F;
        public const int NextLine = 0x0085;

        public const int NoBreakSpace = 0x00A0;
        public const int SoftHyphen = 0x00AD;
        public const int NonBreakingHyphen = 0x2011;

        public const int ZeroWidthSpace = 0x200B;
        public const int ZeroWidthNonJoiner = 0x200C;
        public const int ZeroWidthJoiner = 0x200D;
        public const int WordJoiner = 0x2060;
        public const int ByteOrderMark = 0xFEFF;
        public const int ReversedByteOrderMark = 0xFFFE;
        public const int RightSingleQuotationMark = 0x2019;
        public const int UnitSeparator = 0x001F;

        public const int LeftToRightMark = 0x200E;
        public const int RightToLeftMark = 0x200F;
        public const int ArabicLetterMark = 0x061C;

        public const int LineSeparator = 0x2028;
        public const int ParagraphSeparator = 0x2029;

        #endregion

        #region BiDi Representative Codepoints

        public const int LatinCapitalA = 0x0041;

        public const int HebrewAlef = 0x05D0;

        public const int PlusSign = 0x002B;
        public const int DollarSign = 0x0024;
        public const int ArabicIndicDigitZero = 0x0660;
        public const int Comma = 0x002C;
        public const int CombiningGraveAccent = 0x0300;

        public const int ExclamationMark = 0x0021;

        public const int LeftToRightEmbedding = 0x202A;
        public const int RightToLeftEmbedding = 0x202B;
        public const int PopDirectionalFormat = 0x202C;
        public const int LeftToRightOverride = 0x202D;
        public const int RightToLeftOverride = 0x202E;

        public const int LeftToRightIsolate = 0x2066;
        public const int RightToLeftIsolate = 0x2067;
        public const int FirstStrongIsolate = 0x2068;
        public const int PopDirectionalIsolate = 0x2069;

        #endregion

        #region Other Constants

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLineBreak(int cp)
        {
            return cp == LineFeed || cp == LineSeparator || cp == ParagraphSeparator;
        }

        /// <summary>
        /// Returns true if the codepoint is a line or paragraph separator that produces
        /// a mandatory break (UAX #14 classes BK, CR, LF, NL) and should not be shaped.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMandatoryBreakChar(int cp)
        {
            return cp == LineFeed
                || cp == CarriageReturn
                || cp == VerticalTab
                || cp == FormFeed
                || cp == NextLine
                || cp == LineSeparator
                || cp == ParagraphSeparator;
        }

        /// <summary>
        /// Removes every mandatory break character (the <see cref="IsMandatoryBreakChar"/> set)
        /// from the string, returning the same instance when there is nothing to strip.
        /// </summary>
        public static string StripMandatoryBreaks(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var first = 0;
            while (first < text.Length && !IsMandatoryBreakChar(text[first])) first++;
            if (first == text.Length) return text;

            var sb = new StringBuilder(text.Length - 1);
            sb.Append(text, 0, first);
            for (var i = first + 1; i < text.Length; i++)
            {
                if (!IsMandatoryBreakChar(text[i]))
                    sb.Append(text[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Whether <paramref name="index"/> holds the carriage return of a CRLF pair — the sequence
        /// UAX #14 LB5 and UAX #29 GB3 refuse to break, and which every line-ending rule counts as
        /// one terminator rather than two.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCrlfAt(ReadOnlySpan<char> text, int index)
            => text[index] == (char)CarriageReturn
               && index + 1 < text.Length && text[index + 1] == (char)LineFeed;

        /// <inheritdoc cref="IsCrlfAt(System.ReadOnlySpan{char},int)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCrlfAt(ReadOnlySpan<int> codepoints, int index)
            => codepoints[index] == CarriageReturn
               && index + 1 < codepoints.Length && codepoints[index + 1] == LineFeed;

        /// <summary>
        /// Rewrites every CRLF pair and every remaining lone carriage return as a single line feed,
        /// returning the same instance when the text holds no carriage return.
        /// </summary>
        public static string NormalizeNewlines(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf((char)CarriageReturn) < 0) return text;

            var source = text.AsSpan();
            var pairs = 0;
            for (var i = 0; i < source.Length; i++)
                if (IsCrlfAt(source, i)) pairs++;

            return string.Create(text.Length - pairs, text,
                static (span, src) => NormalizeNewlines(src.AsSpan(), span));
        }

        /// <summary>
        /// Copies <paramref name="source"/> into <paramref name="destination"/> with every CRLF pair
        /// and every lone carriage return rewritten as a single line feed, returning the number of
        /// chars written. <paramref name="destination"/> must be at least as long as
        /// <paramref name="source"/>.
        /// </summary>
        public static int NormalizeNewlines(ReadOnlySpan<char> source, Span<char> destination)
        {
            var written = 0;
            for (var i = 0; i < source.Length; i++)
            {
                if (IsCrlfAt(source, i)) continue;
                var c = source[i];
                destination[written++] = c == (char)CarriageReturn ? (char)LineFeed : c;
            }
            return written;
        }

        /// <summary>
        /// Returns true if the codepoint is a C0 control character (U+0000..U+001F)
        /// or DELETE (U+007F).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsC0ControlOrDelete(int cp)
        {
            return cp < Space || cp == Delete;
        }

        /// <summary>
        /// Returns true if the codepoint is a word-separator character eligible for stretching
        /// under CSS-style inter-word justification (CSS Text Module Level 3 §6.4.1).
        /// </summary>
        /// <remarks>
        /// Modelled on Blink/WebKit/Gecko behaviour: includes ASCII space (U+0020), TAB
        /// (U+0009, treated as space for justification), the CJK ideographic space (U+3000),
        /// and other Unicode general-category Zs space separators. Explicitly excludes the
        /// non-breaking variants — NBSP (U+00A0), NARROW NBSP (U+202F), FIGURE SPACE (U+2007) —
        /// which carry "do not break / do not stretch" semantics.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsJustifiableWordSeparator(int cp)
        {
            if (cp == Space) return true;
            if (cp == Tab) return true;
            if (cp == 0x3000) return true;
            if (cp == NoBreakSpace) return false;
            if (cp == 0x202F) return false;
            if (cp == 0x2007) return false;
            return Provider.GetGeneralCategory(cp) == GeneralCategory.Zs;
        }

        public const int LeftParenthesis = 0x0028;
        public const int RightParenthesis = 0x0029;
        public const int LeftPointingAngleBracket = 0x2329;
        public const int RightPointingAngleBracket = 0x232A;
        public const int LeftAngleBracket = 0x3008;
        public const int RightAngleBracket = 0x3009;

        public const int ArabicTatweel = 0x0640;
        public const int ArabicLam = 0x0644;
        public const int ArabicAlefMaddaAbove = 0x0622;
        public const int ArabicAlefHamzaAbove = 0x0623;
        public const int ArabicAlefHamzaBelow = 0x0625;
        public const int ArabicAlef = 0x0627;
        public const int ArabicAlefWasla = 0x0671;

        public const int ArabicLigatureLamAlefMaddaIsolated = 0xFEF5;
        public const int ArabicLigatureLamAlefMaddaFinal = 0xFEF6;
        public const int ArabicLigatureLamAlefHamzaAboveIsolated = 0xFEF7;
        public const int ArabicLigatureLamAlefHamzaAboveFinal = 0xFEF8;
        public const int ArabicLigatureLamAlefHamzaBelowIsolated = 0xFEF9;
        public const int ArabicLigatureLamAlefHamzaBelowFinal = 0xFEFA;
        public const int ArabicLigatureLamAlefIsolated = 0xFEFB;
        public const int ArabicLigatureLamAlefFinal = 0xFEFC;

        public const int ObjectReplacementCharacter = 0xFFFC;

        /// <summary>String form of <see cref="ObjectReplacementCharacter"/> for insertion APIs.</summary>
        public const string ObjectReplacementCharacterString = "\uFFFC";

        public const int ReplacementCharacter = 0xFFFD;
        public const int DottedCircle = 0x25CC;
        public const int Bullet = 0x2022;

        public const int ArabicBlockStart = 0x0600;
        public const int ArabicBlockEnd = 0x06FF;
        public const int ArabicSupplementStart = 0x0750;
        public const int ArabicSupplementEnd = 0x077F;
        public const int ArabicExtendedAStart = 0x08A0;
        public const int ArabicExtendedAEnd = 0x08FF;
        public const int ArabicPresentationFormsAStart = 0xFB50;
        public const int ArabicPresentationFormsAEnd = 0xFDFF;
        public const int ArabicPresentationFormsBStart = 0xFE70;
        public const int ArabicPresentationFormsBEnd = 0xFEFF;

        #endregion

        #region Unicode Range Constants

        public const int MaxBmp = 0xFFFF;
        public const int MaxCodepoint = 0x10FFFF;

        #endregion

        #region Emoji Constants

        public const int VariationSelector15 = 0xFE0E;
        public const int VariationSelector16 = 0xFE0F;
        public const int CombiningEnclosingKeycap = 0x20E3;
        public const int CombiningEnclosingCircleBackslash = 0x20E0;

        public const int RegionalIndicatorStart = 0x1F1E6;
        public const int RegionalIndicatorEnd = 0x1F1FF;

        public const int EmojiModifierStart = 0x1F3FB;
        public const int EmojiModifierEnd = 0x1F3FF;

        public const int TagSequenceStart = 0xE0020;
        public const int TagSequenceEnd = 0xE007E;
        public const int CancelTag = 0xE007F;
        public const int BlackFlagEmoji = 0x1F3F4;
        public const int GrinningFaceEmoji = 0x1F600;

        public const int NumberSign = 0x0023;
        public const int Asterisk = 0x002A;
        public const int DigitZero = 0x0030;
        public const int DigitNine = 0x0039;

        public const int EmojiRangeThreshold = 0x2000;

        public const int CommonEmojiRangeStart = 0x1F000;
        public const int CommonEmojiRangeSize = 0x1000;

        #endregion

        #region Emoji Helper Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRegionalIndicator(int cp)
        {
            return (uint)(cp - RegionalIndicatorStart) <= (uint)(RegionalIndicatorEnd - RegionalIndicatorStart);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsKeycapBase(int cp)
        {
            return cp == NumberSign || cp == Asterisk || (uint)(cp - DigitZero) <= (uint)(DigitNine - DigitZero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmojiModifier(int cp)
        {
            return (uint)(cp - EmojiModifierStart) <= (uint)(EmojiModifierEnd - EmojiModifierStart);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTagSequenceCodepoint(int cp)
        {
            return (uint)(cp - TagSequenceStart) <= (uint)(TagSequenceEnd - TagSequenceStart);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInCommonEmojiRange(int cp)
        {
            return (uint)(cp - CommonEmojiRangeStart) < CommonEmojiRangeSize;
        }

        #endregion

        private static UnicodeDataProvider provider;

        internal static UnicodeDataProvider Provider
        {
            get
            {
                EnsureInitialized();
                return provider;
            }
        }

        /// <summary>Returns whether the required Unicode tables have been initialized.</summary>
        public static bool IsInitialized => provider != null;

#if UNITY_EDITOR
        static UnicodeData() => EditorLifecycle.UnmanagedCleaning += DisposeProvider;

        private static void DisposeProvider()
        {
            provider?.Dispose();
            provider = null;
        }
#endif

        #region Public character properties (for custom modifiers & parse rules)

        /// <summary>
        /// Returns the simple uppercase mapping for a codepoint, falling back to the codepoint itself
        /// when no mapping is defined. Backed by UnicodeData.txt so behavior is identical across
        /// Mono/IL2CPP/standard .NET — unlike <c>char.ToUpperInvariant</c>, which has gaps for
        /// codepoints such as Greek final sigma U+03C2.
        /// </summary>
        /// <remarks>
        /// "Simple" means a single-codepoint default mapping that ignores locale and the conditional
        /// rules in SpecialCasing.txt (Turkish dotless I, Lithuanian dot-above, German ß → SS, etc.).
        /// Use this in custom <c>BaseModifier</c> implementations for predictable case conversion.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetSimpleUppercase(int codepoint) => Provider.GetSimpleUppercase(codepoint);

        /// <summary>
        /// Returns the simple lowercase mapping for a codepoint, falling back to the codepoint itself
        /// when no mapping is defined. See <see cref="GetSimpleUppercase"/> for the rationale.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetSimpleLowercase(int codepoint) => Provider.GetSimpleLowercase(codepoint);

        /// <summary>
        /// Returns the simple titlecase mapping for a codepoint, falling back to the codepoint itself
        /// when no mapping is defined. Differs from uppercase only for digraph letters such as
        /// U+01C5 (ǅ) — uppercase Ǆ, titlecase ǅ, lowercase ǆ.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetSimpleTitlecase(int codepoint) => Provider.GetSimpleTitlecase(codepoint);

        /// <summary>
        /// Returns the Unicode General Category of a codepoint. Useful for filtering codepoints in
        /// custom modifiers — apply only to letters (<c>Lu/Ll/Lt/Lm/Lo</c>), skip combining marks
        /// (<c>Mn/Mc/Me</c>), select punctuation (<c>Pc/Pd/Ps/Pe/Pi/Pf/Po</c>), and so on.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GeneralCategory GetGeneralCategory(int codepoint) => Provider.GetGeneralCategory(codepoint);

        /// <summary>
        /// <see langword="true"/> when the codepoint is a Unicode letter (general category L*). Uses the
        /// baked category table, so it is consistent across Mono/IL2CPP and covers supplementary letters
        /// that <c>char.IsLetter(char)</c> cannot reach.
        /// </summary>
        public static bool IsLetter(int codepoint)
        {
            var gc = Provider.GetGeneralCategory(codepoint);
            return gc == GeneralCategory.Lu || gc == GeneralCategory.Ll || gc == GeneralCategory.Lt
                || gc == GeneralCategory.Lm || gc == GeneralCategory.Lo;
        }

        /// <summary>
        /// <see langword="true"/> when the codepoint is whitespace per <c>char.IsWhiteSpace</c>.
        /// All Unicode whitespace lies in the BMP, so supplementary codepoints are never whitespace.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWhiteSpace(int codepoint) => codepoint <= MaxBmp && char.IsWhiteSpace((char)codepoint);

        /// <summary>
        /// Returns the Unicode Script of a codepoint (UAX #24). Useful for script-conditional
        /// modifiers — for example applying a stylistic effect only to Devanagari or only to Han.
        /// Values <see cref="UnicodeScript.Common"/> and <see cref="UnicodeScript.Inherited"/> are
        /// shared across scripts (punctuation, combining marks).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UnicodeScript GetScript(int codepoint) => Provider.GetScript(codepoint);

        /// <summary>
        /// Returns <see langword="true"/> when the codepoint has the Extended_Pictographic property
        /// (UTS #51). Distinct from emoji presentation: pictographic glyphs that may render either
        /// as text or as emoji depending on context.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsExtendedPictographic(int codepoint) => Provider.IsExtendedPictographic(codepoint);

        /// <summary>
        /// Returns <see langword="true"/> when the codepoint defaults to emoji-style presentation
        /// (UTS #51 Emoji_Presentation). Use this to skip emoji glyphs in text-only effects
        /// (color, gradient, outline) without relying on the live mesh-pass <c>font.IsColor</c> flag.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmojiPresentation(int codepoint) => Provider.IsEmojiPresentation(codepoint);

        /// <summary>
        /// Returns <see langword="true"/> when the codepoint is an emoji that accepts a skin-tone
        /// modifier (U+1F3FB..U+1F3FF) immediately after it (UTS #51 Emoji_Modifier_Base).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmojiModifierBase(int codepoint) => Provider.IsEmojiModifierBase(codepoint);

        /// <summary>
        /// Returns <see langword="true"/> when the codepoint has the Default_Ignorable_Code_Point
        /// property (formatting characters, variation selectors, ZWJ/ZWNJ, etc.). Custom modifiers
        /// that walk codepoints to compute statistics or apply effects should typically skip these.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDefaultIgnorable(int codepoint) => Provider.IsDefaultIgnorable(codepoint);

        /// <summary>
        /// Decodes one Unicode scalar and reports the consumed UTF-16 code units; an unpaired
        /// surrogate becomes <see cref="ReplacementCharacter"/> and consumes one unit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint DecodeAt(ReadOnlySpan<char> text, int index, out int size)
            => (uint)Utf16.DecodeScalarAt(text, index, out size);

        /// <summary>
        /// Returns the number of UTF-16 code units occupied at <paramref name="index"/>: two for a
        /// valid surrogate pair, otherwise one.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeAt(ReadOnlySpan<char> text, int index)
            => Utf16.SizeAt(text, index);

        /// <summary>
        /// Counts Unicode code points in a UTF-16 span (surrogate pair = 1 codepoint). Never throws on
        /// malformed input — an unpaired surrogate counts as one code point.
        /// </summary>
        internal static int CountCodepoints(ReadOnlySpan<char> text)
            => Utf16.CountCodepoints(text);

        /// <summary>
        /// Maps ascending UTF-16 char indices to codepoint indices in one text walk. Indices
        /// may repeat; out-of-range input clamps to the end. Output may alias the input span.
        /// </summary>
        public static void MapCharsToCodepoints(ReadOnlySpan<char> text, ReadOnlySpan<int> charIndices, Span<int> codepoints)
        {
            var cp = 0;
            var i = 0;
            for (var k = 0; k < charIndices.Length; k++)
            {
                var target = charIndices[k];
                while (i < text.Length && i < target)
                {
                    i += SizeAt(text, i);
                    cp++;
                }
                codepoints[k] = cp;
            }
        }

        /// <summary>
        /// Maps ascending codepoint indices to UTF-16 char indices in one text walk. Indices
        /// may repeat; past-the-end input clamps to <c>text.Length</c>. Output may alias the
        /// input span.
        /// </summary>
        public static void MapCodepointsToChars(ReadOnlySpan<char> text, ReadOnlySpan<int> codepoints, Span<int> charIndices)
        {
            var cp = 0;
            var i = 0;
            for (var k = 0; k < codepoints.Length; k++)
            {
                var target = codepoints[k];
                while (i < text.Length && cp < target)
                {
                    i += SizeAt(text, i);
                    cp++;
                }
                charIndices[k] = i;
            }
        }

        /// <summary>
        /// Encodes a Unicode scalar into UTF-16: one code unit in <paramref name="high"/> for BMP
        /// codepoints (<paramref name="low"/> is 0), a surrogate pair otherwise. Returns the number
        /// of code units written (1 or 2).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeUtf16(int codepoint, out ushort high, out ushort low)
        {
            if (codepoint <= MaxBmp)
            {
                high = (ushort)codepoint;
                low = 0;
                return 1;
            }
            int c = codepoint - 0x10000;
            high = (ushort)(0xD800 + (c >> 10));
            low = (ushort)(0xDC00 + (c & 0x3FF));
            return 2;
        }

        /// <summary>
        /// Encodes a Unicode scalar as UTF-16 into <paramref name="dst"/> starting at <paramref name="at"/>:
        /// one char for BMP codepoints, a surrogate pair otherwise. Returns the number of chars written (1 or 2).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeUtf16(int codepoint, Span<char> dst, int at)
        {
            int units = EncodeUtf16(codepoint, out ushort high, out ushort low);
            dst[at] = (char)high;
            if (units == 2) dst[at + 1] = (char)low;
            return units;
        }

        #endregion

        /// <summary>Initializes the required Unicode tables or throws when their project data is unavailable or invalid.</summary>
        public static void EnsureInitialized()
        {
            if (provider != null) return;
#if UNITY_EDITOR
            if (EditorLifecycle.IsReloading)
                throw new InvalidOperationException("Unicode tables were released for the assembly reload.");
#endif
            if (UniTextSettings.InstanceSilent == null)
                throw new InvalidOperationException(
                    "UniTextSettings not found at Resources/UniTextSettings.asset.");

            var asset = UniTextSettings.UnicodeDataAsset;
            if (asset == null)
                throw new InvalidOperationException(
                    "UnicodeData not found at Resources/UnicodeData.bytes.");

            var bytes = asset.bytes;
            provider = new UnicodeDataProvider(bytes);
            CatZones.unicode.Meow($"[UnicodeData] Initialized: thread={System.Threading.Thread.CurrentThread.ManagedThreadId}, bytes={bytes.Length}, extPict={provider.ExtendedPictographicRangesLength}, emojiPres={provider.EmojiPresentationRangesLength}, U+1F600 ep={provider.IsEmojiPresentation(0x1F600)} xp={provider.IsExtendedPictographic(0x1F600)}");
        }
    }
}
