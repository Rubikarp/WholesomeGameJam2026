using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LightSide
{
    /// <summary>
    /// Entry storing BiDi and joining properties for a codepoint range.
    /// </summary>
    internal readonly struct RangeEntry
    {
        /// <summary>First codepoint in the range.</summary>
        public readonly int startCodePoint;
        /// <summary>Last codepoint in the range (inclusive).</summary>
        public readonly int endCodePoint;
        /// <summary>BiDi class for this range.</summary>
        public readonly BidiClass bidiClass;
        /// <summary>Arabic joining type for this range.</summary>
        public readonly JoiningType joiningType;
        /// <summary>Arabic joining group for this range.</summary>
        public readonly JoiningGroup joiningGroup;

        public RangeEntry(int startCodePoint, int endCodePoint, BidiClass bidiClass, JoiningType joiningType, JoiningGroup joiningGroup)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.bidiClass = bidiClass;
            this.joiningType = joiningType;
            this.joiningGroup = joiningGroup;
        }
    }


    /// <summary>
    /// Entry mapping a codepoint to its BiDi mirror glyph (e.g., '(' to ')').
    /// </summary>
    internal readonly struct MirrorEntry
    {
        /// <summary>Source codepoint.</summary>
        public readonly int codePoint;
        /// <summary>Mirrored codepoint for RTL rendering.</summary>
        public readonly int mirroredCodePoint;

        public MirrorEntry(int codePoint, int mirroredCodePoint)
        {
            this.codePoint = codePoint;
            this.mirroredCodePoint = mirroredCodePoint;
        }
    }


    /// <summary>
    /// Entry storing default simple case mappings for a codepoint (UnicodeData.txt fields 12–14).
    /// </summary>
    /// <remarks>
    /// Each mapping defaults to the codepoint itself when the corresponding field is empty in
    /// UnicodeData.txt; entries are emitted only when at least one mapping differs from the
    /// codepoint, keeping the table sparse. Used by the engine instead of <c>char.ToUpperInvariant</c>
    /// because runtime case tables (Mono, IL2CPP) are incomplete — see Greek final sigma U+03C2.
    /// </remarks>
    internal readonly struct CaseMappingEntry
    {
        public readonly int codePoint;
        public readonly int simpleUppercase;
        public readonly int simpleLowercase;
        public readonly int simpleTitlecase;

        public CaseMappingEntry(int codePoint, int simpleUppercase, int simpleLowercase, int simpleTitlecase)
        {
            this.codePoint = codePoint;
            this.simpleUppercase = simpleUppercase;
            this.simpleLowercase = simpleLowercase;
            this.simpleTitlecase = simpleTitlecase;
        }
    }


    /// <summary>
    /// Entry storing paired bracket information for BiDi bracket matching (UAX #9 N0).
    /// </summary>
    internal readonly struct BracketEntry
    {
        /// <summary>Bracket codepoint.</summary>
        public readonly int codePoint;
        /// <summary>Matching bracket codepoint.</summary>
        public readonly int pairedCodePoint;
        /// <summary>Whether this is an opening or closing bracket.</summary>
        public readonly BidiPairedBracketType bracketType;

        public BracketEntry(int codePoint, int pairedCodePoint, BidiPairedBracketType bracketType)
        {
            this.codePoint = codePoint;
            this.pairedCodePoint = pairedCodePoint;
            this.bracketType = bracketType;
        }
    }


    /// <summary>Entry storing script property for a codepoint range (UAX #24).</summary>
    internal readonly struct ScriptRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly UnicodeScript script;

        public ScriptRangeEntry(int startCodePoint, int endCodePoint, UnicodeScript script)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.script = script;
        }
    }


    /// <summary>Entry storing line break class for a codepoint range (UAX #14).</summary>
    internal readonly struct LineBreakRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly LineBreakClass lineBreakClass;

        public LineBreakRangeEntry(int startCodePoint, int endCodePoint, LineBreakClass lineBreakClass)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.lineBreakClass = lineBreakClass;
        }
    }


    /// <summary>Entry marking a codepoint range as extended pictographic (emoji-related).</summary>
    internal readonly struct ExtendedPictographicRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;

        public ExtendedPictographicRangeEntry(int startCodePoint, int endCodePoint)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
        }
    }


    /// <summary>Entry storing general category for a codepoint range.</summary>
    internal readonly struct GeneralCategoryRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly GeneralCategory generalCategory;

        public GeneralCategoryRangeEntry(int startCodePoint, int endCodePoint, GeneralCategory generalCategory)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.generalCategory = generalCategory;
        }
    }


    /// <summary>Entry storing East Asian width for a codepoint range (UAX #11).</summary>
    internal readonly struct EastAsianWidthRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly EastAsianWidth eastAsianWidth;

        public EastAsianWidthRangeEntry(int startCodePoint, int endCodePoint, EastAsianWidth eastAsianWidth)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.eastAsianWidth = eastAsianWidth;
        }
    }


    /// <summary>Entry storing grapheme cluster break for a codepoint range (UAX #29).</summary>
    internal readonly struct GraphemeBreakRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly GraphemeClusterBreak graphemeBreak;

        public GraphemeBreakRangeEntry(int startCodePoint, int endCodePoint, GraphemeClusterBreak graphemeBreak)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.graphemeBreak = graphemeBreak;
        }
    }


    /// <summary>Entry storing the Word_Break property for a codepoint range (UAX #29).</summary>
    internal readonly struct WordBreakRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly WordBreakProperty wordBreak;

        public WordBreakRangeEntry(int startCodePoint, int endCodePoint, WordBreakProperty wordBreak)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.wordBreak = wordBreak;
        }
    }


    /// <summary>Entry storing Indic conjunct break for a codepoint range (UAX #29).</summary>
    internal readonly struct IndicConjunctBreakRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly IndicConjunctBreak indicConjunctBreak;

        public IndicConjunctBreakRangeEntry(int startCodePoint, int endCodePoint, IndicConjunctBreak indicConjunctBreak)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.indicConjunctBreak = indicConjunctBreak;
        }
    }


    /// <summary>Entry marking a codepoint range as default ignorable (ZWJ, ZWNJ, etc.).</summary>
    internal readonly struct DefaultIgnorableRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;

        public DefaultIgnorableRangeEntry(int startCodePoint, int endCodePoint)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
        }
    }


    /// <summary>Entry marking a codepoint range as having emoji presentation by default.</summary>
    internal readonly struct EmojiPresentationRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;

        public EmojiPresentationRangeEntry(int startCodePoint, int endCodePoint)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
        }
    }


    /// <summary>Entry marking a codepoint range as emoji modifier base (can have skin tone).</summary>
    internal readonly struct EmojiModifierBaseRangeEntry
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;

        public EmojiModifierBaseRangeEntry(int startCodePoint, int endCodePoint)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
        }
    }


    /// <summary>
    /// Blittable script-extensions record: the scripts for the range live in a shared pool at
    /// <see cref="scriptOffset"/>..<see cref="scriptOffset"/>+<see cref="scriptCount"/>. Replaces the
    /// former managed <c>UnicodeScript[]</c> field so the whole table can live in a NativeArray.
    /// </summary>
    internal readonly struct ScriptExtensionRecord
    {
        public readonly int startCodePoint;
        public readonly int endCodePoint;
        public readonly int scriptOffset;
        public readonly int scriptCount;

        public ScriptExtensionRecord(int startCodePoint, int endCodePoint, int scriptOffset, int scriptCount)
        {
            this.startCodePoint = startCodePoint;
            this.endCodePoint = endCodePoint;
            this.scriptOffset = scriptOffset;
            this.scriptCount = scriptCount;
        }
    }

    /// <summary>
    /// Provides Unicode character properties from a precompiled binary data file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides Unicode character property lookups from a compiled binary data blob.
    /// Loads compressed Unicode data from a binary blob for efficient lookup.
    /// </para>
    /// <para>
    /// Uses BMP (Basic Multilingual Plane) lookup tables for fast O(1) access to
    /// common codepoints (0-65535), with binary search for supplementary planes.
    /// </para>
    /// <para>
    /// All tables are stored in <see cref="NativeArray{T}"/> (Allocator.Persistent) so a later
    /// Burst phase can pass them straight into jobs. The instance owns that native memory and
    /// <b>must</b> be <see cref="Dispose">disposed</see> — see <see cref="UnicodeData"/> for the
    /// domain-reload / quit wiring.
    /// </para>
    /// </remarks>
    /// <seealso cref="UnicodeData"/>
    internal sealed unsafe class UnicodeDataProvider : IDisposable
    {
        /// <summary>
        /// Blob signature 'ULTT'. The header reserves 32 uint slots and includes Word_Break data.
        /// </summary>
        private const uint Magic = 0x554C5454;
        private const int BmpSize = 65536;

        private NativeArray<RangeEntry> ranges;
        private NativeArray<MirrorEntry> mirrors;
        private NativeArray<BracketEntry> brackets;
        private NativeArray<CaseMappingEntry> caseMappings;
        private NativeArray<ScriptRangeEntry> scriptRanges;
        private NativeArray<LineBreakRangeEntry> lineBreakRanges;
        private NativeArray<ExtendedPictographicRangeEntry> extendedPictographicRanges;
        private NativeArray<GeneralCategoryRangeEntry> generalCategoryRanges;
        private NativeArray<EastAsianWidthRangeEntry> eastAsianWidthRanges;
        private NativeArray<GraphemeBreakRangeEntry> graphemeBreakRanges;
        private NativeArray<WordBreakRangeEntry> wordBreakRanges;
        private NativeArray<IndicConjunctBreakRangeEntry> indicConjunctBreakRanges;
        private NativeArray<DefaultIgnorableRangeEntry> defaultIgnorableRanges;
        private NativeArray<EmojiPresentationRangeEntry> emojiPresentationRanges;
        private NativeArray<EmojiModifierBaseRangeEntry> emojiModifierBaseRanges;

        private NativeArray<ScriptExtensionRecord> scriptExtensionRecords;
        private NativeArray<UnicodeScript> scriptExtensionPool;

        private NativeArray<byte> bmpBidiClass;
        private NativeArray<byte> bmpJoiningType;
        private NativeArray<byte> bmpScript;
        private NativeArray<byte> bmpLineBreak;
        private NativeArray<byte> bmpGeneralCategory;
        private NativeArray<byte> bmpEastAsianWidth;
        private NativeArray<byte> bmpGraphemeBreak;
        private NativeArray<byte> bmpWordBreak;
        private NativeArray<byte> bmpIndicConjunctBreak;
        private NativeArray<byte> bmpDefaultIgnorable;
        private NativeArray<byte> bmpExtendedPictographic;

        [NativeDisableUnsafePtrRestriction] private RangeEntry* rangesPtr;
        [NativeDisableUnsafePtrRestriction] private MirrorEntry* mirrorsPtr;
        [NativeDisableUnsafePtrRestriction] private BracketEntry* bracketsPtr;
        [NativeDisableUnsafePtrRestriction] private CaseMappingEntry* caseMappingsPtr;
        [NativeDisableUnsafePtrRestriction] private ScriptRangeEntry* scriptRangesPtr;
        [NativeDisableUnsafePtrRestriction] private LineBreakRangeEntry* lineBreakRangesPtr;
        [NativeDisableUnsafePtrRestriction] private ExtendedPictographicRangeEntry* extendedPictographicRangesPtr;
        [NativeDisableUnsafePtrRestriction] private GeneralCategoryRangeEntry* generalCategoryRangesPtr;
        [NativeDisableUnsafePtrRestriction] private EastAsianWidthRangeEntry* eastAsianWidthRangesPtr;
        [NativeDisableUnsafePtrRestriction] private GraphemeBreakRangeEntry* graphemeBreakRangesPtr;
        [NativeDisableUnsafePtrRestriction] private WordBreakRangeEntry* wordBreakRangesPtr;
        [NativeDisableUnsafePtrRestriction] private IndicConjunctBreakRangeEntry* indicConjunctBreakRangesPtr;
        [NativeDisableUnsafePtrRestriction] private DefaultIgnorableRangeEntry* defaultIgnorableRangesPtr;
        [NativeDisableUnsafePtrRestriction] private EmojiPresentationRangeEntry* emojiPresentationRangesPtr;
        [NativeDisableUnsafePtrRestriction] private EmojiModifierBaseRangeEntry* emojiModifierBaseRangesPtr;
        [NativeDisableUnsafePtrRestriction] private ScriptExtensionRecord* scriptExtensionRecordsPtr;
        [NativeDisableUnsafePtrRestriction] private UnicodeScript* scriptExtensionPoolPtr;

        [NativeDisableUnsafePtrRestriction] private byte* bmpBidiClassPtr;
        [NativeDisableUnsafePtrRestriction] private byte* bmpJoiningTypePtr;
        [NativeDisableUnsafePtrRestriction] private byte* bmpScriptPtr;
        [NativeDisableUnsafePtrRestriction] private byte* bmpLineBreakPtr;
        [NativeDisableUnsafePtrRestriction] private byte* bmpGeneralCategoryPtr;
        [NativeDisableUnsafePtrRestriction] private byte* bmpEastAsianWidthPtr;
        [NativeDisableUnsafePtrRestriction] private byte* bmpGraphemeBreakPtr;
        [NativeDisableUnsafePtrRestriction] private byte* bmpWordBreakPtr;
        [NativeDisableUnsafePtrRestriction] private byte* bmpIndicConjunctBreakPtr;
        [NativeDisableUnsafePtrRestriction] private byte* bmpDefaultIgnorablePtr;
        [NativeDisableUnsafePtrRestriction] private byte* bmpExtendedPictographicPtr;

        private bool disposed;

        /// <summary>Gets the Unicode version encoded in the data file.</summary>
        public int UnicodeVersionRaw { get; }

        #region Dense BMP tables (byte-reinterpreted enums) for Burst jobs

        internal NativeArray<byte> BmpBidiClassTable => bmpBidiClass;
        internal NativeArray<byte> BmpJoiningTypeTable => bmpJoiningType;
        internal NativeArray<byte> BmpScriptTable => bmpScript;
        internal NativeArray<byte> BmpLineBreakTable => bmpLineBreak;
        internal NativeArray<byte> BmpGeneralCategoryTable => bmpGeneralCategory;
        internal NativeArray<byte> BmpEastAsianWidthTable => bmpEastAsianWidth;
        internal NativeArray<byte> BmpGraphemeBreakTable => bmpGraphemeBreak;
        internal NativeArray<byte> BmpWordBreakTable => bmpWordBreak;
        internal NativeArray<byte> BmpIndicConjunctBreakTable => bmpIndicConjunctBreak;
        internal NativeArray<byte> BmpDefaultIgnorableTable => bmpDefaultIgnorable;
        internal NativeArray<byte> BmpExtendedPictographicTable => bmpExtendedPictographic;

        #endregion

        #region Raw table pointers for Burst kernels

        /// <summary>Cached read-only pointer into the dense BMP script table (indexed 0..<see cref="BmpSize"/>-1); worker-thread safe (see <see cref="CachePointers"/>).</summary>
        internal byte* BmpScriptPtr => bmpScriptPtr;
        /// <summary>Cached read-only pointer into the supplementary-plane script range table, paired with <see cref="ScriptRangesLength"/>.</summary>
        internal ScriptRangeEntry* ScriptRangesPtr => scriptRangesPtr;
        /// <summary>Entry count of the range table behind <see cref="ScriptRangesPtr"/>.</summary>
        internal int ScriptRangesLength => scriptRanges.Length;

        /// <summary>Cached read-only pointer into the dense BMP grapheme-cluster-break table (indexed 0..<see cref="BmpSize"/>-1); worker-thread safe (see <see cref="CachePointers"/>).</summary>
        internal byte* BmpGraphemeBreakPtr => bmpGraphemeBreakPtr;
        /// <summary>Cached read-only pointer into the supplementary-plane grapheme-break range table, paired with <see cref="GraphemeBreakRangesLength"/>.</summary>
        internal GraphemeBreakRangeEntry* GraphemeBreakRangesPtr => graphemeBreakRangesPtr;
        /// <summary>Entry count of the range table behind <see cref="GraphemeBreakRangesPtr"/>.</summary>
        internal int GraphemeBreakRangesLength => graphemeBreakRanges.Length;

        /// <summary>Cached read-only pointer into the dense BMP Word_Break table.</summary>
        internal byte* BmpWordBreakPtr => bmpWordBreakPtr;
        /// <summary>Cached read-only pointer into the supplementary-plane Word_Break range table.</summary>
        internal WordBreakRangeEntry* WordBreakRangesPtr => wordBreakRangesPtr;
        /// <summary>Entry count of the range table behind <see cref="WordBreakRangesPtr"/>.</summary>
        internal int WordBreakRangesLength => wordBreakRanges.Length;

        /// <summary>Cached read-only pointer into the dense BMP Indic-conjunct-break table (indexed 0..<see cref="BmpSize"/>-1); worker-thread safe (see <see cref="CachePointers"/>).</summary>
        internal byte* BmpIndicConjunctBreakPtr => bmpIndicConjunctBreakPtr;
        /// <summary>Cached read-only pointer into the supplementary-plane Indic-conjunct-break range table, paired with <see cref="IndicConjunctBreakRangesLength"/>.</summary>
        internal IndicConjunctBreakRangeEntry* IndicConjunctBreakRangesPtr => indicConjunctBreakRangesPtr;
        /// <summary>Entry count of the range table behind <see cref="IndicConjunctBreakRangesPtr"/>.</summary>
        internal int IndicConjunctBreakRangesLength => indicConjunctBreakRanges.Length;

        /// <summary>Cached read-only pointer into the dense BMP extended-pictographic table (indexed 0..<see cref="BmpSize"/>-1, nonzero = pictographic); worker-thread safe (see <see cref="CachePointers"/>).</summary>
        internal byte* BmpExtendedPictographicPtr => bmpExtendedPictographicPtr;
        /// <summary>Cached read-only pointer into the supplementary-plane extended-pictographic range table, paired with <see cref="ExtendedPictographicRangesLength"/>.</summary>
        internal ExtendedPictographicRangeEntry* ExtendedPictographicRangesPtr => extendedPictographicRangesPtr;
        /// <summary>Entry count of the range table behind <see cref="ExtendedPictographicRangesPtr"/>.</summary>
        internal int ExtendedPictographicRangesLength => extendedPictographicRanges.Length;

        /// <summary>Entry count of the emoji-presentation range table.</summary>
        internal int EmojiPresentationRangesLength => emojiPresentationRanges.Length;

        /// <summary>Cached read-only pointer into the dense BMP line-break-class table (indexed 0..<see cref="BmpSize"/>-1); worker-thread safe (see <see cref="CachePointers"/>).</summary>
        internal byte* BmpLineBreakPtr => bmpLineBreakPtr;
        /// <summary>Cached read-only pointer into the supplementary-plane line-break range table, paired with <see cref="LineBreakRangesLength"/>.</summary>
        internal LineBreakRangeEntry* LineBreakRangesPtr => lineBreakRangesPtr;
        /// <summary>Entry count of the range table behind <see cref="LineBreakRangesPtr"/>.</summary>
        internal int LineBreakRangesLength => lineBreakRanges.Length;

        /// <summary>Cached read-only pointer into the dense BMP general-category table (indexed 0..<see cref="BmpSize"/>-1); worker-thread safe (see <see cref="CachePointers"/>).</summary>
        internal byte* BmpGeneralCategoryPtr => bmpGeneralCategoryPtr;
        /// <summary>Cached read-only pointer into the supplementary-plane general-category range table, paired with <see cref="GeneralCategoryRangesLength"/>.</summary>
        internal GeneralCategoryRangeEntry* GeneralCategoryRangesPtr => generalCategoryRangesPtr;
        /// <summary>Entry count of the range table behind <see cref="GeneralCategoryRangesPtr"/>.</summary>
        internal int GeneralCategoryRangesLength => generalCategoryRanges.Length;

        /// <summary>Cached read-only pointer into the dense BMP East-Asian-width table (indexed 0..<see cref="BmpSize"/>-1); worker-thread safe (see <see cref="CachePointers"/>).</summary>
        internal byte* BmpEastAsianWidthPtr => bmpEastAsianWidthPtr;
        /// <summary>Cached read-only pointer into the supplementary-plane East-Asian-width range table, paired with <see cref="EastAsianWidthRangesLength"/>.</summary>
        internal EastAsianWidthRangeEntry* EastAsianWidthRangesPtr => eastAsianWidthRangesPtr;
        /// <summary>Entry count of the range table behind <see cref="EastAsianWidthRangesPtr"/>.</summary>
        internal int EastAsianWidthRangesLength => eastAsianWidthRanges.Length;

        /// <summary>Cached read-only pointer into the dense BMP bidi-class table (indexed 0..<see cref="BmpSize"/>-1); worker-thread safe (see <see cref="CachePointers"/>).</summary>
        internal byte* BmpBidiClassPtr => bmpBidiClassPtr;
        /// <summary>Cached read-only pointer into the supplementary-plane bidi/joining <see cref="RangeEntry"/> table (its <c>bidiClass</c> field), paired with <see cref="BidiClassRangesLength"/>.</summary>
        internal RangeEntry* BidiClassRangesPtr => rangesPtr;
        /// <summary>Entry count of the range table behind <see cref="BidiClassRangesPtr"/>.</summary>
        internal int BidiClassRangesLength => ranges.Length;

        /// <summary>Cached read-only pointer into the paired-bracket <see cref="BracketEntry"/> table (UAX #9 N0), paired with <see cref="BracketsLength"/>.</summary>
        internal BracketEntry* BracketsPtr => bracketsPtr;
        /// <summary>Entry count of the table behind <see cref="BracketsPtr"/>.</summary>
        internal int BracketsLength => brackets.Length;

        #endregion

        /// <summary>
        /// Initializes the provider from binary Unicode data.
        /// </summary>
        /// <param name="data">Binary blob containing compiled Unicode property data.</param>
        /// <exception cref="InvalidDataException">The data format is invalid.</exception>
        public UnicodeDataProvider(byte[] data)
        {
            try
            {
                using var stream = new MemoryStream(data, false);
                using var reader = new BinaryReader(stream);

                var fileMagic = reader.ReadUInt32();
                if (fileMagic != Magic)
                    throw new InvalidDataException(
                        "Invalid or outdated Unicode data blob: magic mismatch. " +
                        "Regenerate UnicodeData.bytes via UniText/Unicode Data Generator " +
                        "(format changed: Word_Break data added).");

                UnicodeVersionRaw = unchecked((int)reader.ReadUInt32());

                var rangeOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var mirrorOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var bracketOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var scriptOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var lineBreakOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var extPictOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var gcOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var eawOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var gcbOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var incbOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var scxOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var diOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var epOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var embOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var caseOffset = reader.ReadUInt32();
                reader.ReadUInt32();
                var wordBreakOffset = reader.ReadUInt32();
                reader.ReadUInt32();

                stream.Position = rangeOffset;
                var rangeCount = reader.ReadUInt32();
                ranges = new NativeArray<RangeEntry>((int)rangeCount, Allocator.Persistent);

                for (var i = 0; i < rangeCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();
                    var bidi = reader.ReadByte();
                    var jt = reader.ReadByte();
                    var jg = reader.ReadByte();
                    reader.ReadByte();

                    ranges[i] = new RangeEntry(
                        unchecked((int)start),
                        unchecked((int)end),
                        (BidiClass)bidi,
                        (JoiningType)jt,
                        (JoiningGroup)jg);
                }

                if (mirrorOffset != 0)
                {
                    stream.Position = mirrorOffset;
                    var mirrorCount = reader.ReadUInt32();
                    mirrors = new NativeArray<MirrorEntry>((int)mirrorCount, Allocator.Persistent);

                    for (var i = 0; i < mirrorCount; i++)
                    {
                        var cp = reader.ReadUInt32();
                        var mirrored = reader.ReadUInt32();

                        mirrors[i] = new MirrorEntry(
                            unchecked((int)cp),
                            unchecked((int)mirrored));
                    }
                }
                else
                {
                    mirrors = new NativeArray<MirrorEntry>(0, Allocator.Persistent);
                }

                if (bracketOffset != 0)
                {
                    stream.Position = bracketOffset;
                    var bracketCount = reader.ReadUInt32();
                    brackets = new NativeArray<BracketEntry>((int)bracketCount, Allocator.Persistent);

                    for (var i = 0; i < bracketCount; i++)
                    {
                        var cp = reader.ReadUInt32();
                        var paired = reader.ReadUInt32();
                        var bpt = reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();

                        brackets[i] = new BracketEntry(
                            unchecked((int)cp),
                            unchecked((int)paired),
                            (BidiPairedBracketType)bpt);
                    }
                }
                else
                {
                    brackets = new NativeArray<BracketEntry>(0, Allocator.Persistent);
                }

                if (scriptOffset != 0)
                {
                    stream.Position = scriptOffset;
                    var scriptCount = reader.ReadUInt32();
                    scriptRanges = new NativeArray<ScriptRangeEntry>((int)scriptCount, Allocator.Persistent);

                    for (var i = 0; i < scriptCount; i++)
                    {
                        var start = reader.ReadUInt32();
                        var end = reader.ReadUInt32();
                        var script = reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();

                        scriptRanges[i] = new ScriptRangeEntry(
                            unchecked((int)start),
                            unchecked((int)end),
                            (UnicodeScript)script);
                    }
                }
                else
                {
                    scriptRanges = new NativeArray<ScriptRangeEntry>(0, Allocator.Persistent);
                }

                if (lineBreakOffset != 0)
                {
                    stream.Position = lineBreakOffset;
                    var lineBreakCount = reader.ReadUInt32();
                    lineBreakRanges = new NativeArray<LineBreakRangeEntry>((int)lineBreakCount, Allocator.Persistent);

                    for (var i = 0; i < lineBreakCount; i++)
                    {
                        var start = reader.ReadUInt32();
                        var end = reader.ReadUInt32();
                        var lbc = reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();

                        lineBreakRanges[i] = new LineBreakRangeEntry(
                            unchecked((int)start),
                            unchecked((int)end),
                            (LineBreakClass)lbc);
                    }
                }
                else
                {
                    lineBreakRanges = new NativeArray<LineBreakRangeEntry>(0, Allocator.Persistent);
                }

                if (extPictOffset != 0)
                {
                    stream.Position = extPictOffset;
                    var extPictCount = reader.ReadUInt32();
                    extendedPictographicRanges = new NativeArray<ExtendedPictographicRangeEntry>((int)extPictCount, Allocator.Persistent);

                    for (var i = 0; i < extPictCount; i++)
                    {
                        var start = reader.ReadUInt32();
                        var end = reader.ReadUInt32();

                        extendedPictographicRanges[i] = new ExtendedPictographicRangeEntry(
                            unchecked((int)start),
                            unchecked((int)end));
                    }
                }
                else
                {
                    extendedPictographicRanges = new NativeArray<ExtendedPictographicRangeEntry>(0, Allocator.Persistent);
                }

                if (gcOffset != 0)
                {
                    stream.Position = gcOffset;
                    var gcCount = reader.ReadUInt32();
                    generalCategoryRanges = new NativeArray<GeneralCategoryRangeEntry>((int)gcCount, Allocator.Persistent);

                    for (var i = 0; i < gcCount; i++)
                    {
                        var start = reader.ReadUInt32();
                        var end = reader.ReadUInt32();
                        var gc = reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();

                        generalCategoryRanges[i] = new GeneralCategoryRangeEntry(
                            unchecked((int)start),
                            unchecked((int)end),
                            (GeneralCategory)gc);
                    }
                }
                else
                {
                    generalCategoryRanges = new NativeArray<GeneralCategoryRangeEntry>(0, Allocator.Persistent);
                }

                if (eawOffset != 0)
                {
                    stream.Position = eawOffset;
                    var eawCount = reader.ReadUInt32();
                    eastAsianWidthRanges = new NativeArray<EastAsianWidthRangeEntry>((int)eawCount, Allocator.Persistent);

                    for (var i = 0; i < eawCount; i++)
                    {
                        var start = reader.ReadUInt32();
                        var end = reader.ReadUInt32();
                        var eaw = reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();

                        eastAsianWidthRanges[i] = new EastAsianWidthRangeEntry(
                            unchecked((int)start),
                            unchecked((int)end),
                            (EastAsianWidth)eaw);
                    }
                }
                else
                {
                    eastAsianWidthRanges = new NativeArray<EastAsianWidthRangeEntry>(0, Allocator.Persistent);
                }

                if (gcbOffset != 0)
                {
                    stream.Position = gcbOffset;
                    var gcbCount = reader.ReadUInt32();
                    graphemeBreakRanges = new NativeArray<GraphemeBreakRangeEntry>((int)gcbCount, Allocator.Persistent);

                    for (var i = 0; i < gcbCount; i++)
                    {
                        var start = reader.ReadUInt32();
                        var end = reader.ReadUInt32();
                        var gcb = reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();

                        graphemeBreakRanges[i] = new GraphemeBreakRangeEntry(
                            unchecked((int)start),
                            unchecked((int)end),
                            (GraphemeClusterBreak)gcb);
                    }
                }
                else
                {
                    graphemeBreakRanges = new NativeArray<GraphemeBreakRangeEntry>(0, Allocator.Persistent);
                }

                if (incbOffset != 0)
                {
                    stream.Position = incbOffset;
                    var incbCount = reader.ReadUInt32();
                    indicConjunctBreakRanges = new NativeArray<IndicConjunctBreakRangeEntry>((int)incbCount, Allocator.Persistent);

                    for (var i = 0; i < incbCount; i++)
                    {
                        var start = reader.ReadUInt32();
                        var end = reader.ReadUInt32();
                        var incb = reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadByte();

                        indicConjunctBreakRanges[i] = new IndicConjunctBreakRangeEntry(
                            unchecked((int)start),
                            unchecked((int)end),
                            (IndicConjunctBreak)incb);
                    }
                }
                else
                {
                    indicConjunctBreakRanges = new NativeArray<IndicConjunctBreakRangeEntry>(0, Allocator.Persistent);
                }

                if (scxOffset != 0)
                {
                    stream.Position = scxOffset;
                    var scxCount = reader.ReadUInt32();
                    scriptExtensionRecords = new NativeArray<ScriptExtensionRecord>((int)scxCount, Allocator.Persistent);
                    var pool = new List<UnicodeScript>();

                    for (var i = 0; i < scxCount; i++)
                    {
                        var start = reader.ReadUInt32();
                        var end = reader.ReadUInt32();
                        var scriptCount = reader.ReadByte();

                        var offset = pool.Count;
                        for (var j = 0; j < scriptCount; j++) pool.Add((UnicodeScript)reader.ReadByte());

                        var totalBytes = 8 + 1 + scriptCount;
                        var padding = (4 - totalBytes % 4) % 4;
                        for (var p = 0; p < padding; p++)
                            reader.ReadByte();

                        scriptExtensionRecords[i] = new ScriptExtensionRecord(
                            unchecked((int)start),
                            unchecked((int)end),
                            offset,
                            scriptCount);
                    }

                    scriptExtensionPool = new NativeArray<UnicodeScript>(pool.Count, Allocator.Persistent);
                    for (var k = 0; k < pool.Count; k++) scriptExtensionPool[k] = pool[k];
                }
                else
                {
                    scriptExtensionRecords = new NativeArray<ScriptExtensionRecord>(0, Allocator.Persistent);
                    scriptExtensionPool = new NativeArray<UnicodeScript>(0, Allocator.Persistent);
                }

                if (diOffset != 0)
                {
                    stream.Position = diOffset;
                    var diCount = reader.ReadUInt32();
                    defaultIgnorableRanges = new NativeArray<DefaultIgnorableRangeEntry>((int)diCount, Allocator.Persistent);

                    for (var i = 0; i < diCount; i++)
                    {
                        var start = reader.ReadUInt32();
                        var end = reader.ReadUInt32();

                        defaultIgnorableRanges[i] = new DefaultIgnorableRangeEntry(
                            unchecked((int)start),
                            unchecked((int)end));
                    }
                }
                else
                {
                    defaultIgnorableRanges = new NativeArray<DefaultIgnorableRangeEntry>(0, Allocator.Persistent);
                }

                if (epOffset != 0)
                {
                    stream.Position = epOffset;
                    var epCount = reader.ReadUInt32();
                    emojiPresentationRanges = new NativeArray<EmojiPresentationRangeEntry>((int)epCount, Allocator.Persistent);

                    for (var i = 0; i < epCount; i++)
                    {
                        var start = reader.ReadUInt32();
                        var end = reader.ReadUInt32();

                        emojiPresentationRanges[i] = new EmojiPresentationRangeEntry(
                            unchecked((int)start),
                            unchecked((int)end));
                    }
                }
                else
                {
                    emojiPresentationRanges = new NativeArray<EmojiPresentationRangeEntry>(0, Allocator.Persistent);
                }

                if (embOffset != 0)
                {
                    stream.Position = embOffset;
                    var embCount = reader.ReadUInt32();
                    emojiModifierBaseRanges = new NativeArray<EmojiModifierBaseRangeEntry>((int)embCount, Allocator.Persistent);

                    for (var i = 0; i < embCount; i++)
                    {
                        var start = reader.ReadUInt32();
                        var end = reader.ReadUInt32();

                        emojiModifierBaseRanges[i] = new EmojiModifierBaseRangeEntry(
                            unchecked((int)start),
                            unchecked((int)end));
                    }
                }
                else
                {
                    emojiModifierBaseRanges = new NativeArray<EmojiModifierBaseRangeEntry>(0, Allocator.Persistent);
                }

                if (caseOffset != 0)
                {
                    stream.Position = caseOffset;
                    var caseCount = reader.ReadUInt32();
                    caseMappings = new NativeArray<CaseMappingEntry>((int)caseCount, Allocator.Persistent);

                    for (var i = 0; i < caseCount; i++)
                    {
                        var cp = reader.ReadUInt32();
                        var upper = reader.ReadUInt32();
                        var lower = reader.ReadUInt32();
                        var title = reader.ReadUInt32();

                        caseMappings[i] = new CaseMappingEntry(
                            unchecked((int)cp),
                            unchecked((int)upper),
                            unchecked((int)lower),
                            unchecked((int)title));
                    }
                }
                else
                {
                    caseMappings = new NativeArray<CaseMappingEntry>(0, Allocator.Persistent);
                }

                if (wordBreakOffset == 0)
                {
                    throw new InvalidDataException("Unicode data has no Word_Break section.");
                }

                stream.Position = wordBreakOffset;
                var wordBreakCount = reader.ReadUInt32();
                wordBreakRanges = new NativeArray<WordBreakRangeEntry>((int)wordBreakCount, Allocator.Persistent);

                for (var i = 0; i < wordBreakCount; i++)
                {
                    var start = reader.ReadUInt32();
                    var end = reader.ReadUInt32();
                    var wordBreak = reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();

                    wordBreakRanges[i] = new WordBreakRangeEntry(
                        unchecked((int)start),
                        unchecked((int)end),
                        (WordBreakProperty)wordBreak);
                }

                bmpBidiClass = new NativeArray<byte>(BmpSize, Allocator.Persistent);
                bmpJoiningType = new NativeArray<byte>(BmpSize, Allocator.Persistent);
                bmpScript = new NativeArray<byte>(BmpSize, Allocator.Persistent);
                bmpLineBreak = new NativeArray<byte>(BmpSize, Allocator.Persistent);
                bmpGeneralCategory = new NativeArray<byte>(BmpSize, Allocator.Persistent);
                bmpEastAsianWidth = new NativeArray<byte>(BmpSize, Allocator.Persistent);
                bmpGraphemeBreak = new NativeArray<byte>(BmpSize, Allocator.Persistent);
                bmpWordBreak = new NativeArray<byte>(BmpSize, Allocator.Persistent);
                bmpIndicConjunctBreak = new NativeArray<byte>(BmpSize, Allocator.Persistent);
                bmpDefaultIgnorable = new NativeArray<byte>(BmpSize, Allocator.Persistent);
                bmpExtendedPictographic = new NativeArray<byte>(BmpSize, Allocator.Persistent);

                InitializeBmpTables();
                CachePointers();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void InitializeBmpTables()
        {
            foreach (var range in ranges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++)
                {
                    bmpBidiClass[cp] = (byte)range.bidiClass;
                    bmpJoiningType[cp] = (byte)range.joiningType;
                }
            }

            foreach (var range in scriptRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpScript[cp] = (byte)range.script;
            }

            foreach (var range in lineBreakRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpLineBreak[cp] = (byte)range.lineBreakClass;
            }

            foreach (var range in generalCategoryRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpGeneralCategory[cp] = (byte)range.generalCategory;
            }

            foreach (var range in eastAsianWidthRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpEastAsianWidth[cp] = (byte)range.eastAsianWidth;
            }

            foreach (var range in graphemeBreakRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpGraphemeBreak[cp] = (byte)range.graphemeBreak;
            }

            foreach (var range in wordBreakRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpWordBreak[cp] = (byte)range.wordBreak;
            }

            foreach (var range in indicConjunctBreakRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpIndicConjunctBreak[cp] = (byte)range.indicConjunctBreak;
            }

            foreach (var range in defaultIgnorableRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpDefaultIgnorable[cp] = 1;
            }

            foreach (var range in extendedPictographicRanges)
            {
                var start = Math.Max(0, range.startCodePoint);
                var end = Math.Min(BmpSize - 1, range.endCodePoint);
                for (var cp = start; cp <= end; cp++) bmpExtendedPictographic[cp] = 1;
            }
        }

        /// <summary>
        /// Caches a raw read-only pointer into every table's native buffer, taken once on the
        /// constructing (main) thread after all arrays are filled. The pointers carry no
        /// <c>AtomicSafetyHandle</c>, so the per-codepoint <c>Get*</c>/<c>Is*</c> lookups read them
        /// from raw WorkerPool analysis threads without tripping <c>ENABLE_UNITY_COLLECTIONS_CHECKS</c>,
        /// and the Burst kernels consume the same pointer-and-length pairs.
        /// The NativeArrays stay the owning storage; these are a read-only view over the same memory.
        /// </summary>
        private void CachePointers()
        {
            rangesPtr = (RangeEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(ranges);
            mirrorsPtr = (MirrorEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(mirrors);
            bracketsPtr = (BracketEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(brackets);
            caseMappingsPtr = (CaseMappingEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(caseMappings);
            scriptRangesPtr = (ScriptRangeEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(scriptRanges);
            lineBreakRangesPtr = (LineBreakRangeEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(lineBreakRanges);
            extendedPictographicRangesPtr = (ExtendedPictographicRangeEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(extendedPictographicRanges);
            generalCategoryRangesPtr = (GeneralCategoryRangeEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(generalCategoryRanges);
            eastAsianWidthRangesPtr = (EastAsianWidthRangeEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(eastAsianWidthRanges);
            graphemeBreakRangesPtr = (GraphemeBreakRangeEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(graphemeBreakRanges);
            wordBreakRangesPtr = (WordBreakRangeEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(wordBreakRanges);
            indicConjunctBreakRangesPtr = (IndicConjunctBreakRangeEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(indicConjunctBreakRanges);
            defaultIgnorableRangesPtr = (DefaultIgnorableRangeEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(defaultIgnorableRanges);
            emojiPresentationRangesPtr = (EmojiPresentationRangeEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(emojiPresentationRanges);
            emojiModifierBaseRangesPtr = (EmojiModifierBaseRangeEntry*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(emojiModifierBaseRanges);
            scriptExtensionRecordsPtr = (ScriptExtensionRecord*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(scriptExtensionRecords);
            scriptExtensionPoolPtr = (UnicodeScript*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(scriptExtensionPool);

            bmpBidiClassPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bmpBidiClass);
            bmpJoiningTypePtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bmpJoiningType);
            bmpScriptPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bmpScript);
            bmpLineBreakPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bmpLineBreak);
            bmpGeneralCategoryPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bmpGeneralCategory);
            bmpEastAsianWidthPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bmpEastAsianWidth);
            bmpGraphemeBreakPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bmpGraphemeBreak);
            bmpWordBreakPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bmpWordBreak);
            bmpIndicConjunctBreakPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bmpIndicConjunctBreak);
            bmpDefaultIgnorablePtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bmpDefaultIgnorable);
            bmpExtendedPictographicPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bmpExtendedPictographic);
        }

        public BidiClass GetBidiClass(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return (BidiClass)bmpBidiClassPtr[codePoint];

            var idx = FindRange(rangesPtr, ranges.Length, codePoint);
            return idx >= 0 ? rangesPtr[idx].bidiClass : BidiClass.LeftToRight;
        }

        public JoiningType GetJoiningType(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return (JoiningType)bmpJoiningTypePtr[codePoint];

            var idx = FindRange(rangesPtr, ranges.Length, codePoint);
            return idx >= 0 ? rangesPtr[idx].joiningType : JoiningType.NonJoining;
        }

        public UnicodeScript GetScript(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return (UnicodeScript)bmpScriptPtr[codePoint];

            var idx = FindRange(scriptRangesPtr, scriptRanges.Length, codePoint);
            return idx >= 0 ? scriptRangesPtr[idx].script : UnicodeScript.Unknown;
        }

        public LineBreakClass GetLineBreakClass(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return (LineBreakClass)bmpLineBreakPtr[codePoint];

            var idx = FindRange(lineBreakRangesPtr, lineBreakRanges.Length, codePoint);
            return idx >= 0 ? lineBreakRangesPtr[idx].lineBreakClass : LineBreakClass.XX;
        }

        public bool IsExtendedPictographic(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return bmpExtendedPictographicPtr[codePoint] != 0;

            return FindRange(extendedPictographicRangesPtr, extendedPictographicRanges.Length, codePoint) >= 0;
        }

        public bool IsEmojiPresentation(int codePoint)
        {
            return FindRange(emojiPresentationRangesPtr, emojiPresentationRanges.Length, codePoint) >= 0;
        }

        public bool IsEmojiModifierBase(int codePoint)
        {
            return FindRange(emojiModifierBaseRangesPtr, emojiModifierBaseRanges.Length, codePoint) >= 0;
        }

        public GeneralCategory GetGeneralCategory(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return (GeneralCategory)bmpGeneralCategoryPtr[codePoint];

            var idx = FindRange(generalCategoryRangesPtr, generalCategoryRanges.Length, codePoint);
            return idx >= 0 ? generalCategoryRangesPtr[idx].generalCategory : GeneralCategory.Cn;
        }

        public GraphemeClusterBreak GetGraphemeClusterBreak(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return (GraphemeClusterBreak)bmpGraphemeBreakPtr[codePoint];

            var idx = FindRange(graphemeBreakRangesPtr, graphemeBreakRanges.Length, codePoint);
            return idx >= 0 ? graphemeBreakRangesPtr[idx].graphemeBreak : GraphemeClusterBreak.Other;
        }

        public IndicConjunctBreak GetIndicConjunctBreak(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return (IndicConjunctBreak)bmpIndicConjunctBreakPtr[codePoint];

            var idx = FindRange(indicConjunctBreakRangesPtr, indicConjunctBreakRanges.Length, codePoint);
            return idx >= 0 ? indicConjunctBreakRangesPtr[idx].indicConjunctBreak : IndicConjunctBreak.None;
        }

        public UnicodeScript[] GetScriptExtensions(int codePoint)
        {
            var idx = FindRange(scriptExtensionRecordsPtr, scriptExtensionRecords.Length, codePoint);
            if (idx >= 0)
            {
                var record = scriptExtensionRecordsPtr[idx];
                var result = new UnicodeScript[record.scriptCount];
                for (var i = 0; i < record.scriptCount; i++)
                    result[i] = scriptExtensionPoolPtr[record.scriptOffset + i];
                return result;
            }

            var script = GetScript(codePoint);
            return new[] { script };
        }

        public bool IsDefaultIgnorable(int codePoint)
        {
            if ((uint)codePoint < BmpSize)
                return bmpDefaultIgnorablePtr[codePoint] != 0;

            return FindRange(defaultIgnorableRangesPtr, defaultIgnorableRanges.Length, codePoint) >= 0;
        }

        /// <summary>
        /// Returns the simple uppercase mapping for a codepoint (UnicodeData.txt field 12).
        /// Falls back to the codepoint itself when no mapping is defined.
        /// </summary>
        /// <remarks>
        /// "Simple" here matches Unicode terminology: a single-codepoint default mapping that
        /// ignores locale and the conditional rules in SpecialCasing.txt (e.g. Turkish dotless I,
        /// Lithuanian dot-above, German ß). Use this in preference to <c>char.ToUpperInvariant</c>
        /// because the latter relies on incomplete runtime tables on Mono/IL2CPP.
        /// </remarks>
        public int GetSimpleUppercase(int codePoint)
        {
            var idx = FindPoint(caseMappingsPtr, caseMappings.Length, codePoint);
            return idx >= 0 ? caseMappingsPtr[idx].simpleUppercase : codePoint;
        }


        /// <summary>
        /// Returns the simple lowercase mapping for a codepoint (UnicodeData.txt field 13).
        /// Falls back to the codepoint itself when no mapping is defined.
        /// </summary>
        public int GetSimpleLowercase(int codePoint)
        {
            var idx = FindPoint(caseMappingsPtr, caseMappings.Length, codePoint);
            return idx >= 0 ? caseMappingsPtr[idx].simpleLowercase : codePoint;
        }


        /// <summary>
        /// Returns the simple titlecase mapping for a codepoint (UnicodeData.txt field 14).
        /// Falls back to the codepoint itself when no mapping is defined.
        /// </summary>
        public int GetSimpleTitlecase(int codePoint)
        {
            var idx = FindPoint(caseMappingsPtr, caseMappings.Length, codePoint);
            return idx >= 0 ? caseMappingsPtr[idx].simpleTitlecase : codePoint;
        }

        #region Concrete binary searches (Burst-safe, no interface dispatch)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(RangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(ScriptRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(LineBreakRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(ExtendedPictographicRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(GeneralCategoryRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(GraphemeBreakRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(IndicConjunctBreakRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(DefaultIgnorableRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(EmojiPresentationRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(EmojiModifierBaseRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(ScriptExtensionRecord* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindPoint(CaseMappingEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var cp = entries[mid].codePoint;
                if (codePoint < cp) hi = mid - 1;
                else if (codePoint > cp) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        #endregion

        /// <summary>
        /// Frees every Allocator.Persistent NativeArray. Idempotent (guarded by <see cref="disposed"/>)
        /// and safe on a partially-constructed instance (per-array <c>IsCreated</c> check), so the
        /// constructor calls it on a parse failure. <see cref="UnicodeData"/> owns the live
        /// instance and releases it when the editor application domain unloads.
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            DisposeIfCreated(ref ranges);
            DisposeIfCreated(ref mirrors);
            DisposeIfCreated(ref brackets);
            DisposeIfCreated(ref caseMappings);
            DisposeIfCreated(ref scriptRanges);
            DisposeIfCreated(ref lineBreakRanges);
            DisposeIfCreated(ref extendedPictographicRanges);
            DisposeIfCreated(ref generalCategoryRanges);
            DisposeIfCreated(ref eastAsianWidthRanges);
            DisposeIfCreated(ref graphemeBreakRanges);
            DisposeIfCreated(ref wordBreakRanges);
            DisposeIfCreated(ref indicConjunctBreakRanges);
            DisposeIfCreated(ref defaultIgnorableRanges);
            DisposeIfCreated(ref emojiPresentationRanges);
            DisposeIfCreated(ref emojiModifierBaseRanges);
            DisposeIfCreated(ref scriptExtensionRecords);
            DisposeIfCreated(ref scriptExtensionPool);

            DisposeIfCreated(ref bmpBidiClass);
            DisposeIfCreated(ref bmpJoiningType);
            DisposeIfCreated(ref bmpScript);
            DisposeIfCreated(ref bmpLineBreak);
            DisposeIfCreated(ref bmpGeneralCategory);
            DisposeIfCreated(ref bmpEastAsianWidth);
            DisposeIfCreated(ref bmpGraphemeBreak);
            DisposeIfCreated(ref bmpWordBreak);
            DisposeIfCreated(ref bmpIndicConjunctBreak);
            DisposeIfCreated(ref bmpDefaultIgnorable);
            DisposeIfCreated(ref bmpExtendedPictographic);

            rangesPtr = null;
            mirrorsPtr = null;
            bracketsPtr = null;
            caseMappingsPtr = null;
            scriptRangesPtr = null;
            lineBreakRangesPtr = null;
            extendedPictographicRangesPtr = null;
            generalCategoryRangesPtr = null;
            eastAsianWidthRangesPtr = null;
            graphemeBreakRangesPtr = null;
            wordBreakRangesPtr = null;
            indicConjunctBreakRangesPtr = null;
            defaultIgnorableRangesPtr = null;
            emojiPresentationRangesPtr = null;
            emojiModifierBaseRangesPtr = null;
            scriptExtensionRecordsPtr = null;
            scriptExtensionPoolPtr = null;

            bmpBidiClassPtr = null;
            bmpJoiningTypePtr = null;
            bmpScriptPtr = null;
            bmpLineBreakPtr = null;
            bmpGeneralCategoryPtr = null;
            bmpEastAsianWidthPtr = null;
            bmpGraphemeBreakPtr = null;
            bmpWordBreakPtr = null;
            bmpIndicConjunctBreakPtr = null;
            bmpDefaultIgnorablePtr = null;
            bmpExtendedPictographicPtr = null;
        }

        private static void DisposeIfCreated<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated) array.Dispose();
        }
    }
}
