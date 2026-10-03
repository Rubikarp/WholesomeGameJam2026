using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Interface for custom attribute data stored in <see cref="UniTextBuffers"/>.
    /// </summary>
    /// <remarks>
    /// Implement this interface to create custom per-text attribute storage for modifiers.
    /// Use <see cref="UniTextBuffers.GetOrCreateAttributeData{T}"/> to retrieve instances.
    /// </remarks>
    /// <seealso cref="PooledArrayAttribute{T}"/>
    /// <seealso cref="UniTextBuffers"/>
    public interface IAttributeData
    {
        /// <summary>
        /// Sizes the storage to <paramref name="count"/> codepoints and clears it. Called for every
        /// registered attribute once per parse, before any modifier applies, so a write or read at
        /// any codepoint index of the current text is in bounds.
        /// </summary>
        /// <param name="count">The codepoint count of the text about to be laid out.</param>
        void Prepare(int count);

        /// <summary>
        /// Releases all pooled resources back to the pool.
        /// </summary>
        void Release();
    }


    /// <summary>
    /// A pooled array-based implementation of <see cref="IAttributeData"/>.
    /// </summary>
    /// <typeparam name="T">The type of elements stored in the array.</typeparam>
    /// <remarks>
    /// <para>
    /// Use this class to store per-codepoint or per-glyph attribute data in modifiers.
    /// The underlying array is rented from <see cref="ArrayPool{T}"/> for zero-allocation operation.
    /// </para>
    /// <para>
    /// Between two <see cref="Prepare"/> calls an attribute is written one way only: as the
    /// codepoint-indexed extent through <see cref="FillRange"/> and <see cref="WritableSpan()"/>,
    /// or as an appended list through <see cref="Add"/>. Mixing the two throws, and
    /// <see cref="Count"/> reports the valid element count of whichever way was claimed.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var colors = buffers.GetOrCreateAttributeData&lt;PooledArrayAttribute&lt;uint&gt;&gt;("colors");
    /// colors.FillRange(context.Segment.Range, packed);
    /// </code>
    /// </example>
    public sealed class PooledArrayAttribute<T> : IAttributeData
    {
        private int preparedLength;
        private bool indexed;

        /// <summary>
        /// The underlying pooled buffer. Its <see cref="PooledBuffer{T}.count"/> tracks appends only
        /// and its <see cref="PooledBuffer{T}.Capacity"/> is an allocation size that may exceed the
        /// prepared extent; <see cref="Count"/> is the bound a reader indexes against.
        /// </summary>
        public PooledBuffer<T> buffer;

        /// <summary>
        /// Gets the number of valid elements: the whole prepared extent once a range write claims it,
        /// the appended element count otherwise, and zero until the first write of either kind.
        /// </summary>
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => indexed ? preparedLength : buffer.count;
        }

        /// <summary>
        /// Gets a reference to the element at the specified index. Every index of the prepared extent
        /// reads its cleared value whether or not a write claimed the extent, so the bound is the
        /// extent rather than <see cref="Count"/>.
        /// </summary>
        /// <param name="i">The zero-based index of the element.</param>
        /// <returns>A reference to the element at the specified index.</returns>
        public ref T this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref buffer[i];
        }

        /// <summary>
        /// Appends an item after the last one, claiming the attribute for appended use.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <exception cref="InvalidOperationException">
        /// A range write already claimed the prepared extent since the last <see cref="Prepare"/>.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            if (indexed) ThrowMixedWrites();
            buffer.Add(item);
        }

        /// <summary>
        /// Prepares an exact logical extent for range writes, clears its previous contents and drops
        /// the previous write claim, leaving <see cref="Count"/> at zero until the next write.
        /// </summary>
        /// <param name="count">The minimum required element count.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="count"/> is negative.
        /// </exception>
        public void Prepare(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            buffer.EnsureCount(count);
            buffer.ClearData();
            buffer.count = 0;
            preparedLength = count;
            indexed = false;
        }

        /// <summary>
        /// Writes one value to every element of a range inside the extent prepared by the latest
        /// <see cref="Prepare"/> call.
        /// </summary>
        /// <param name="range">The exact range to overwrite.</param>
        /// <param name="value">The value written to each element.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The complete range does not fit inside the prepared extent. Validation happens before
        /// the first write, so the buffer is never partially changed.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The attribute was appended to since the last <see cref="Prepare"/>.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FillRange(TextRange range, T value) => WritableSpan(range).Fill(value);

        /// <summary>
        /// Gets the whole extent prepared by the latest <see cref="Prepare"/> call as a writable span
        /// and claims it, so <see cref="Count"/> reports that extent. Elements left unwritten keep the
        /// cleared value the preparation gave them.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The attribute was appended to since the last <see cref="Prepare"/>.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> WritableSpan()
        {
            if (buffer.count > 0) ThrowMixedWrites();
            indexed = true;
            return buffer.data.AsSpan(0, preparedLength);
        }

        /// <summary>
        /// Gets a range of the prepared extent as a writable span and claims the whole extent, so
        /// <see cref="Count"/> reports it.
        /// </summary>
        /// <param name="range">The range to write, which must lie inside the prepared extent.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The complete range does not fit inside the prepared extent.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The attribute was appended to since the last <see cref="Prepare"/>.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> WritableSpan(TextRange range)
        {
            if ((uint)range.start > (uint)preparedLength ||
                (uint)range.length > (uint)(preparedLength - range.start))
                ThrowRangeOutOfExtent(range);
            if (buffer.count > 0) ThrowMixedWrites();

            indexed = true;
            return buffer.data.AsSpan(range.start, range.length);
        }

        /// <summary>
        /// Zeroes the whole backing array and drops the write claim, leaving <see cref="Count"/> at
        /// zero over an extent that stays addressable.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearAll()
        {
            buffer.ClearAll();
            indexed = false;
        }

        /// <inheritdoc/>
        public void Release()
        {
            buffer.Return();
            preparedLength = 0;
            indexed = false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowRangeOutOfExtent(TextRange range)
            => throw new ArgumentOutOfRangeException(nameof(range),
                $"Range [{range.start}, {range.End}) exceeds prepared length {preparedLength}.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowMixedWrites()
            => throw new InvalidOperationException(
                "A pooled attribute is written either as a prepared extent or as an appended list, not both.");
    }


    /// <summary>
    /// Container for all intermediate and final buffers used during text processing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="UniTextBuffers"/> holds all data produced during the text processing pipeline:
    /// codepoints, BiDi levels, text runs, shaped glyphs, line breaks, and positioned glyphs.
    /// </para>
    /// <para>
    /// <b>Performance:</b> All buffers use <see cref="PooledBuffer{T}"/> for zero-allocation
    /// operation in steady state. Call <see cref="EnsureRentBuffers"/> before processing
    /// and <see cref="EnsureReturnBuffers"/> when done.
    /// </para>
    /// </remarks>
    /// <seealso cref="TextProcessor"/>
    /// <seealso cref="PooledBuffer{T}"/>
    public sealed class UniTextBuffers
    {
        private const int MinCodepointCapacity = 32;
        private const int MinRunCapacity = 64;
        private const int MinGlyphCapacity = 32;
        private const int MinLineCapacity = 32;
        private const int MinParagraphCapacity = 8;

        /// <summary>Parsed Unicode scalar values; unpaired UTF-16 surrogates become <see cref="UnicodeData.ReplacementCharacter"/>.</summary>
        public PooledBuffer<int> codepoints;

        /// <summary>The paragraph table: hard-break-delimited units with their slices into every flat buffer (see <see cref="Paragraph"/>). Rebuilt each first pass; the substrate for incremental rebuilds, per-paragraph parallelism and viewport culling.</summary>
        internal PooledBuffer<Paragraph> paragraphs;

        /// <summary>Pre-shaping runs (segmented by script, direction, font) — per-pass scratch of the paragraphs that actually re-shaped; cache-hit paragraphs contribute none. Read only by shaping; consume <see cref="shapedRuns"/> for whole-text run data.</summary>
        public PooledBuffer<TextRun> runs;

        /// <summary>Shaped runs with glyph ranges and metrics.</summary>
        public PooledBuffer<ShapedRun> shapedRuns;

        /// <summary>Shaped glyphs with glyph IDs, advances, and offsets.</summary>
        public PooledBuffer<ShapedGlyph> shapedGlyphs;

        /// <summary>Width of each codepoint for line breaking calculations.</summary>
        public PooledBuffer<float> cpWidths;

        /// <summary>Line break types per codepoint (UAX #14).</summary>
        public PooledBuffer<LineBreakType> breakOpportunities;

        /// <summary>Grapheme cluster boundaries per codepoint (UAX #29).</summary>
        public PooledBuffer<bool> graphemeBreaks;

        /// <summary>Word boundaries per codepoint position (UAX #29 plus dictionary tailoring).</summary>
        public PooledBuffer<bool> wordBoundaries;

        /// <summary><see cref="graphemeBreaks"/> as a span, or empty until the pipeline has produced them — the one place the not-ready check lives.</summary>
        public ReadOnlySpan<bool> GraphemeBreaksOrEmpty
            => graphemeBreaks.data != null && graphemeBreaks.count > 0 ? graphemeBreaks.Span : ReadOnlySpan<bool>.Empty;

        /// <summary><see cref="wordBoundaries"/> as a span, or empty until the pipeline has produced them.</summary>
        public ReadOnlySpan<bool> WordBoundariesOrEmpty
            => wordBoundaries.data != null && wordBoundaries.count > 0 ? wordBoundaries.Span : ReadOnlySpan<bool>.Empty;

        /// <summary>
        /// Whether the codepoint's resolved BiDi embedding level (UAX #9) is RTL — odd level.
        /// False when levels are absent or the index is out of range; the one home of the
        /// parity convention.
        /// </summary>
        public bool IsRtlLevelAt(int codepointIndex)
            => bidiLevels.data != null && (uint)codepointIndex < (uint)bidiLevels.count
               && (bidiLevels.data[codepointIndex] & 1) != 0;

        /// <summary>Computed text lines after line breaking.</summary>
        public PooledBuffer<TextLine> lines;

        /// <summary>Runs reordered for visual display within each line.</summary>
        public PooledBuffer<ShapedRun> orderedRuns;

        /// <summary>Final positioned glyphs ready for rendering.</summary>
        public PooledBuffer<PositionedGlyph> positionedGlyphs;

        /// <summary>Per-cluster visibility flags; bit ownership lives in <see cref="HiddenClusterBits"/>. <see cref="HiddenClusterBits.Collapse"/> additionally excludes the codepoint from shaping (itemization drops it from runs, so it produces no glyphs and no width). Every real-glyph consumer (mesh quads, inline media, virtual-glyph decorations) must skip clusters with any bit set (<c>!= 0</c>). Each producer owns its bits exclusively and must clear/set only those, only inside its own cluster ranges — ranges of different producers may overlap. Write via <see cref="PrepareHiddenClusters"/>; <c>count == 0</c> means nothing is hidden.</summary>
        internal PooledBuffer<byte> hiddenClusters;

        /// <summary>Per-codepoint painter-order modes (<see cref="PaintOrder"/> values) consumed by the mesh generator's quad sort. Write via <see cref="PreparePaintOrders"/>; <c>count == 0</c> means the whole block is layer-major.</summary>
        internal PooledBuffer<byte> paintOrders;

        /// <summary>Per-cluster horizontal quad-scale factors consumed by the mesh generator (fit
        /// glyph compression, justification glyph scaling). Owned entirely by the positioning pass,
        /// which rewrites every value it uses each run; <c>0</c> means unscaled and <c>count == 0</c>
        /// means the channel is inactive.</summary>
        internal PooledBuffer<float> glyphXScales;

        /// <summary>Pristine <see cref="ShapedGlyph.advanceX"/> snapshot retained while horizontal fit
        /// candidates are projected into the shaped glyphs; line-height-only fit requires no snapshot.</summary>
        internal PooledBuffer<float> fitBaseAdvances;

        /// <summary>Segment-level line break opportunities in codepoint space; filled by modifiers in the Shaped phase, consumed by line breaking. Text between consecutive entries wraps as a unit: it either fits entirely on the current line or starts a fresh one, and the entry's range — the delimiter — collapses when the break is taken on it (its codepoints stay in the line range but produce no glyphs and no width), the way trailing whitespace hangs at word-level breaks. A unit wider than the box wraps inside itself, and its lines accept no further units. A zero-length entry is a pure boundary with nothing to collapse.</summary>
        public PooledBuffer<TextRange> segmentBreaks;

        /// <summary>Virtual codepoints for synthesized glyphs (e.g., modifiers).</summary>
        public PooledBuffer<uint> virtualCodepoints;

        /// <summary>
        /// Requests atlas warm-up for a codepoint rendered outside the document text
        /// (decoration-line <c>_</c>, list bullets, ellipsis). The glyph is prepared in every font the
        /// text resolves to, so an injected character can be drawn in the face of the text around it,
        /// plus the font stack's own choice for it. Deduplicated — the buffer is
        /// shared across modifiers and survives granular re-applies, so a per-range or
        /// per-frame caller must not append the same codepoint unboundedly.
        /// </summary>
        /// <seealso cref="TryResolveInjectedGlyph"/>
        public void RequestVirtualCodepoint(uint codepoint)
        {
            for (var i = 0; i < virtualCodepoints.count; i++)
                if (virtualCodepoints.data[i] == codepoint) return;
            virtualCodepoints.Add(codepoint);
        }

        /// <summary>Pre-shaped glyph ids (with font) modifiers request for atlas rasterization when the id comes from OpenType shaping (GSUB), not a 1:1 codepoint lookup.</summary>
        public PooledBuffer<VirtualGlyph> virtualGlyphs;

        /// <summary>
        /// Requests atlas warm-up for an exact glyph in a registered font. Deduplicated because
        /// the shared buffer survives granular modifier re-applies; a repeated request keeps the
        /// larger silhouette-field reach (<paramref name="fieldExtent"/>, see <see cref="AttributeKeys.ColorGlyphField"/>).
        /// </summary>
        public void RequestVirtualGlyph(int fontId, uint glyphId, byte fieldExtent = 0)
        {
            for (var i = 0; i < virtualGlyphs.count; i++)
            {
                ref var glyph = ref virtualGlyphs.data[i];
                if (glyph.fontId != fontId || glyph.glyphId != glyphId) continue;
                if (fieldExtent > glyph.fieldExtent) glyph.fieldExtent = fieldExtent;
                return;
            }

            virtualGlyphs.Add(new VirtualGlyph { fontId = fontId, glyphId = glyphId, fieldExtent = fieldExtent });
        }

        /// <summary>Virtual positioned glyphs injected by modifiers (ellipsis, list markers). Separate from positionedGlyphs to not affect hit testing / selection.</summary>
        public PooledBuffer<PositionedGlyph> virtualPositionedGlyphs;

        /// <summary>
        /// Resolves a character drawn outside the document text against the face the text at
        /// <paramref name="cluster"/> resolved to, so a bold, italic, overridden or variable face
        /// carries into the injected glyph; falls back to the font stack when that face lacks the
        /// character. Declare the character through <see cref="RequestVirtualCodepoint"/> in any phase
        /// before glyph rasterization, or the resolved glyph has no atlas entry to draw from.
        /// </summary>
        /// <param name="fontProvider">The provider that issued the text's font ids.</param>
        /// <param name="codepoint">The character to draw.</param>
        /// <param name="cluster">Codepoint index whose face the glyph inherits; outside every shaped run only the font stack is consulted.</param>
        /// <param name="fontSize">Size the advance is measured at.</param>
        /// <returns><see langword="false"/> when no font carries the character.</returns>
        public bool TryResolveInjectedGlyph(UniTextFontProvider fontProvider, uint codepoint, int cluster,
            float fontSize, out InjectedGlyph glyph)
        {
            glyph = default;
            if (fontProvider == null) return false;

            return TryResolveIn(fontProvider, FontIdForCluster(cluster), codepoint, fontSize, out glyph)
                || TryResolveIn(fontProvider, fontProvider.FindFontForCodepoint((int)codepoint), codepoint,
                    fontSize, out glyph);
        }

        private static bool TryResolveIn(UniTextFontProvider fontProvider, int fontId, uint codepoint,
            float fontSize, out InjectedGlyph glyph)
        {
            glyph = default;
            if (fontId == 0) return false;

            var font = fontProvider.GetFont(fontId);
            if (font == null) return false;

            if (!Shaper.TryGetGlyphInfo(font, codepoint, fontSize, out var glyphIndex, out var advance,
                    fontProvider.GetNormalizationScale(font)) || glyphIndex == 0)
                return false;

            glyph = new InjectedGlyph(fontId, glyphIndex, advance);
            return true;
        }

        /// <summary>The font id the text at <paramref name="cluster"/> resolved to, or 0 outside every shaped run.</summary>
        public int FontIdForCluster(int cluster)
        {
            var runs = shapedRuns.data;
            for (var r = 0; r < shapedRuns.count; r++)
            {
                ref readonly var run = ref runs[r];
                if (cluster >= run.range.start && cluster < run.range.End)
                    return run.fontId;
            }

            return 0;
        }

        /// <summary>BiDi embedding levels per codepoint (UAX #9).</summary>
        public PooledBuffer<byte> bidiLevels;

        /// <summary>Unicode script per codepoint (UAX #24).</summary>
        public PooledBuffer<UnicodeScript> scripts;

        /// <summary>Start margins per codepoint (for list items, indentation).</summary>
        public PooledBuffer<float> startMargins;

        /// <summary>
        /// Number of lines whose <see cref="TextLine.advance"/> and <see cref="TextLine.advancePrefix"/>
        /// the last layout filled; zero from a line break until the heights are computed again.
        /// </summary>
        private int lineAdvanceCount;

        /// <summary>
        /// Whether every line carries the advance and prefix a Y coordinate is resolved against. False
        /// between a line break and the layout that follows it, when a caller must fall back to the
        /// metrics it can measure itself rather than read the fields.
        /// </summary>
        public bool HasLineAdvances => lineAdvanceCount == lines.count;

        /// <summary>Declares the lines' advance and prefix filled for <paramref name="count"/> lines, or cleared when zero.</summary>
        internal void SetLineAdvanceCount(int count) => lineAdvanceCount = count;

        /// <summary>The resolved base paragraph direction.</summary>
        public TextDirection baseDirection;

        /// <summary>Font size used during shaping (for scaling calculations).</summary>
        public float shapingFontSize;

        internal PooledBuffer<CachedGlyphData> glyphDataCache;

        /// <summary>Indicates whether <see cref="glyphDataCache"/> contains valid data.</summary>
        public bool hasValidGlyphCache;

        /// <summary>Indicates whether buffers are currently rented from the pool.</summary>
        public bool isRented;

        /// <summary>Maps an exact variation-instance id to the axis arrays shared by shaping and rasterization.</summary>
        internal Dictionary<int, VariationRunInfo> variationMap;

        /// <summary>Residual real/synthetic decisions and the effective weight produced by cluster font resolution.</summary>
        internal PooledBuffer<byte> fontStyleRealizations;
        internal PooledBuffer<ushort> fontStyleWeights;

        /// <summary>
        /// Resolves the correct varHash48 for a given fontId, checking the variationMap first.
        /// Use this instead of font.DefaultVarHash48 when rendering synthetic/virtual glyphs.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long ResolveVarHash48(int fontId, UniTextFont.Core font)
        {
            if (variationMap != null && variationMap.TryGetValue(fontId, out var info))
                return info.varHash48;
            return font.DefaultVarHash48;
        }

        /// <summary>
        /// Finds the varHash48 for a target base font that matches the variation context
        /// of the given source fontId. Used when a synthetic glyph (underline, kashida) lives
        /// in a different font than the text it decorates.
        /// Returns 0 if no matching variation is found.
        /// </summary>
        internal long FindCompanionVarHash(int sourceFontId, int targetBaseFontHash)
        {
            if (variationMap == null) return 0;
            if (!variationMap.TryGetValue(sourceFontId, out var source)) return 0;

            var sourceVars = source.hbVariations;
            if (sourceVars == null) return 0;

            foreach (var kvp in variationMap)
            {
                if (kvp.Value.baseFontHash != targetBaseFontHash) continue;

                var targetVars = kvp.Value.hbVariations;
                if (targetVars == null || targetVars.Length != sourceVars.Length) continue;

                var match = true;
                for (var i = 0; i < sourceVars.Length; i++)
                {
                    if (sourceVars[i].tag != targetVars[i].tag ||
                        sourceVars[i].value != targetVars[i].value)
                    {
                        match = false;
                        break;
                    }
                }

                if (match) return kvp.Value.varHash48;
            }

            return 0;
        }

        /// <summary>Variation configs written by VariationModifier and merged into cluster font requests.</summary>
        internal PooledBuffer<VariationConfig> variationConfigs;

        private Dictionary<string, IAttributeData> attributeData;
        private Dictionary<string, int> attributeRefCounts;
        private Dictionary<string, AttributeChannel> attributeChannels;

        /// <summary>
        /// Registers <paramref name="holder"/> as an active writer of <paramref name="key"/> and returns
        /// the channel owning that key's pipeline passes, creating it from <paramref name="factory"/> on
        /// the first writer. Returns <see langword="null"/> when the key carries no pass.
        /// </summary>
        /// <param name="key">The attribute key the writer fills.</param>
        /// <param name="holder">The writer entering its active span.</param>
        /// <param name="owner">The component the writer belongs to.</param>
        /// <param name="factory">Builds the channel; invoked at most once per key.</param>
        public AttributeChannel ActivateChannel(string key, BaseModifier holder, UniTextBase owner,
            Func<AttributeChannel> factory)
        {
            if (factory == null) return null;

            if (attributeChannels == null || !attributeChannels.TryGetValue(key, out var channel))
            {
                channel = factory();
                if (channel == null) return null;
                attributeChannels ??= new Dictionary<string, AttributeChannel>(8);
                attributeChannels[key] = channel;
            }

            channel.Activate(key, holder, owner, this);
            return channel;
        }

        /// <summary>
        /// Gets or creates typed attribute data for the specified key, incrementing its reference
        /// count. The matching <see cref="ReleaseAttributeData"/> call decrements; the underlying
        /// data is freed only when the count reaches zero. This lets multiple modifier instances
        /// safely share one keyed channel — destroying one no longer pulls the buffer out from
        /// under the rest.
        /// </summary>
        /// <typeparam name="T">The attribute data type, must implement <see cref="IAttributeData"/>.</typeparam>
        /// <param name="key">The unique key identifying this attribute data.</param>
        /// <returns>The existing or newly created attribute data instance.</returns>
        public T GetOrCreateAttributeData<T>(string key) where T : class, IAttributeData, new()
        {
            attributeData ??= new Dictionary<string, IAttributeData>(8);
            attributeRefCounts ??= new Dictionary<string, int>(8);

            if (attributeData.TryGetValue(key, out var existing))
            {
                attributeRefCounts[key] = attributeRefCounts.TryGetValue(key, out var n) ? n + 1 : 1;
                return (T)existing;
            }

            var data = new T();
            attributeData[key] = data;
            attributeRefCounts[key] = 1;
            return data;
        }

        /// <summary>
        /// Registers this modifier's keyed attribute and sizes it to the codepoint count. Sizing runs
        /// on registration only; from there on <see cref="PrepareAttributes"/> owns it, once per parse.
        /// </summary>
        public void PrepareAttribute<T>(ref PooledArrayAttribute<T> attribute, string key) where T : unmanaged
        {
            if (attribute != null) return;
            attribute = GetOrCreateAttributeData<PooledArrayAttribute<T>>(key);
            attribute.Prepare(codepoints.count);
        }

        /// <summary>
        /// Merges an OpenType feature set into every codepoint of a range, so shaping applies it there.
        /// Features already written to a codepoint are kept, and a tag both sets carry takes
        /// <paramref name="featureSet"/>'s value. <see cref="FontFeatureRegistry.Unset"/> writes nothing.
        /// </summary>
        /// <param name="range">The codepoint range the set applies to.</param>
        /// <param name="featureSet">A <see cref="FontFeatureRegistry"/> id.</param>
        /// <exception cref="ArgumentOutOfRangeException">The range does not fit the current text.</exception>
        public void AddFontFeatures(TextRange range, byte featureSet)
        {
            if (featureSet == FontFeatureRegistry.Unset || range.length <= 0) return;

            PrepareAttribute(ref fontFeatures, AttributeKeys.FontFeature);
            var ids = fontFeatures.WritableSpan(range);

            byte mergedFrom = FontFeatureRegistry.Unset;
            var merged = featureSet;
            for (var i = 0; i < ids.Length; i++)
            {
                var present = ids[i];
                if (present == FontFeatureRegistry.Unset)
                {
                    ids[i] = featureSet;
                    continue;
                }

                if (present != mergedFrom)
                {
                    mergedFrom = present;
                    merged = FontFeatureRegistry.Combine(present, featureSet);
                }
                ids[i] = merged;
            }
        }

        /// <summary>Per-codepoint feature-set ids for this parse, or <see langword="null"/> when nothing wrote features.</summary>
        internal byte[] FontFeatureIds => fontFeatures is { Count: > 0 } ? fontFeatures.buffer.data : null;

        private PooledArrayAttribute<byte> fontFeatures;

        /// <summary>
        /// Sizes every registered attribute to the current codepoint count and clears it. Runs once per
        /// parse, after decoding and before any modifier applies: an attribute owned by a modifier that
        /// did not re-initialize this parse is still indexed by the codepoints of the current text.
        /// </summary>
        public void PrepareAttributes()
        {
            if (attributeData == null) return;
            foreach (var data in attributeData.Values)
                data.Prepare(codepoints.count);
        }

        /// <summary>
        /// Gets typed attribute data for the specified key if it exists.
        /// </summary>
        /// <typeparam name="T">The attribute data type.</typeparam>
        /// <param name="key">The unique key identifying this attribute data.</param>
        /// <returns>The attribute data if found; otherwise, <see langword="null"/>.</returns>
        public T GetAttributeData<T>(string key) where T : class, IAttributeData
        {
            if (attributeData != null && attributeData.TryGetValue(key, out var data))
                return (T)data;
            return null;
        }

        /// <summary>
        /// Decrements the reference count for <paramref name="key"/>'s attribute data and
        /// releases the underlying buffer only when the count drops to zero. Pairs one-to-one
        /// with <see cref="GetOrCreateAttributeData{T}"/>.
        /// </summary>
        /// <param name="key">The unique key identifying the attribute data to release.</param>
        public void ReleaseAttributeData(string key)
        {
            if (attributeData == null || !attributeData.TryGetValue(key, out var data))
                return;

            if (attributeRefCounts != null && attributeRefCounts.TryGetValue(key, out var count))
            {
                count--;
                if (count > 0)
                {
                    attributeRefCounts[key] = count;
                    return;
                }
                attributeRefCounts.Remove(key);
            }

            data.Release();
            attributeData.Remove(key);
            if (ReferenceEquals(data, fontFeatures)) fontFeatures = null;

            if (attributeChannels != null && attributeChannels.Remove(key, out var channel))
                channel.Release();
        }

        /// <summary>
        /// Releases every channel before the data, so a channel releasing keys it owns of its own
        /// (<see cref="ReleaseAttributeData"/> reenters here) neither mutates a live enumeration nor
        /// leaves a buffer to be returned twice.
        /// </summary>
        private void ReleaseAllAttributeData()
        {
            if (attributeChannels is { Count: > 0 })
            {
                var pending = new List<AttributeChannel>(attributeChannels.Values);
                attributeChannels.Clear();
                for (var i = 0; i < pending.Count; i++)
                    pending[i].Release();
            }

            if (attributeData == null) return;
            foreach (var data in attributeData.Values)
                data.Release();
            attributeData.Clear();
            attributeRefCounts?.Clear();
            fontFeatures = null;
        }

        /// <summary>
        /// Gets the scale factor to convert from shaping font size to target font size.
        /// </summary>
        /// <param name="targetFontSize">The desired font size.</param>
        /// <returns>The scale factor to apply to glyph positions and advances.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetGlyphScale(float targetFontSize)
        {
            return shapingFontSize > 0 ? targetFontSize / shapingFontSize : 1f;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int EstimateCapacity(int textLength, int minCapacity)
        {
            if (textLength <= minCapacity) return minCapacity;
            return Mathf.NextPowerOfTwo(textLength);
        }

        /// <summary>
        /// Ensures all buffers are rented from the pool with appropriate initial capacity.
        /// </summary>
        /// <param name="textLength">The expected text length for capacity estimation.</param>
        /// <remarks>
        /// <para>
        /// Call this method before starting text processing. If buffers are already rented,
        /// this method returns immediately.
        /// </para>
        /// <para>
        /// <b>Performance:</b> Capacities are estimated from text length and rounded to
        /// power-of-two values for efficient pooling.
        /// </para>
        /// </remarks>
        public void EnsureRentBuffers(int textLength)
        {
            if (isRented) return;
            UniTextDebug.Increment(ref UniTextDebug.Buffers_RentCount);

            var codepointCapacity = EstimateCapacity(textLength, MinCodepointCapacity);
            var glyphCapacity = EstimateCapacity(textLength, MinGlyphCapacity);

            codepoints.Rent(codepointCapacity);
            paragraphs.Rent(MinParagraphCapacity);
            runs.Rent(MinRunCapacity);
            shapedRuns.Rent(MinRunCapacity);
            shapedGlyphs.Rent(glyphCapacity);
            cpWidths.Rent(codepointCapacity);
            breakOpportunities.Rent(codepointCapacity + 1);
            graphemeBreaks.Rent(codepointCapacity + 1);
            wordBoundaries.Rent(codepointCapacity + 1);
            lines.Rent(MinLineCapacity);
            orderedRuns.Rent(MinRunCapacity);
            positionedGlyphs.Rent(glyphCapacity);

            bidiLevels.Rent(codepointCapacity);
            scripts.Rent(codepointCapacity);
            glyphDataCache.Rent(glyphCapacity);

            isRented = true;
            Reset();
        }

        /// <summary>
        /// Returns all rented buffers back to the pool.
        /// </summary>
        /// <remarks>
        /// Call this method when text processing is complete and the buffers are no longer needed.
        /// This releases memory back to the pool for reuse by other instances.
        /// </remarks>
        public void EnsureReturnBuffers()
        {
            if (!isRented) return;

            hasValidGlyphCache = false;

            codepoints.Return();
            paragraphs.Return();
            runs.Return();
            shapedRuns.Return();
            shapedGlyphs.Return();
            cpWidths.Return();
            breakOpportunities.Return();
            graphemeBreaks.Return();
            wordBoundaries.Return();
            lines.Return();
            orderedRuns.Return();
            positionedGlyphs.Return();
            hiddenClusters.Return();
            paintOrders.Return();
            glyphXScales.Return();
            fitBaseAdvances.Return();
            segmentBreaks.Return();
            virtualCodepoints.Return();
            virtualGlyphs.Return();
            virtualPositionedGlyphs.Return();

            bidiLevels.Return();
            scripts.Return();
            startMargins.Return();
            lineAdvanceCount = 0;
            glyphDataCache.Return();
            variationConfigs.Return();
            fontStyleRealizations.Return();
            fontStyleWeights.Return();

            variationMap?.Clear();
            variationMap = null;

            ReleaseAllAttributeData();

            isRented = false;
        }

        /// <summary>
        /// Resets all buffer counts to zero without releasing pooled memory.
        /// </summary>
        /// <remarks>
        /// Use this method between processing passes to reuse buffers for new text
        /// without the overhead of returning and re-renting from the pool.
        /// </remarks>
        public void Reset()
        {
            codepoints.FakeClear();
            startMargins.FakeClear();
            paragraphs.FakeClear();
            runs.FakeClear();
            shapedRuns.FakeClear();
            shapedGlyphs.FakeClear();
            cpWidths.FakeClear();
            breakOpportunities.FakeClear();
            graphemeBreaks.FakeClear();
            wordBoundaries.FakeClear();
            lines.FakeClear();
            lineAdvanceCount = 0;
            orderedRuns.FakeClear();
            positionedGlyphs.FakeClear();
            hiddenClusters.FakeClear();
            paintOrders.FakeClear();
            glyphXScales.FakeClear();
            segmentBreaks.FakeClear();
            virtualCodepoints.FakeClear();
            virtualGlyphs.FakeClear();
            virtualPositionedGlyphs.FakeClear();
            bidiLevels.FakeClear();
            scripts.FakeClear();
            glyphDataCache.FakeClear();
            variationConfigs.FakeClear();
            fontStyleRealizations.FakeClear();
            fontStyleWeights.FakeClear();

            hasValidGlyphCache = false;
            baseDirection = TextDirection.LeftToRight;
        }

        /// <summary>
        /// Ensures codepoint-related buffers have at least the specified capacity.
        /// </summary>
        /// <param name="required">The minimum required codepoint capacity.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCodepointCapacity(int required)
        {
            if (codepoints.Capacity < required)
                GrowCodepointBuffers(required);
        }

        private void GrowCodepointBuffers(int required)
        {
            var newSize = Math.Max(required, codepoints.Capacity * 2);

            codepoints.EnsureCapacity(newSize);
            bidiLevels.EnsureCapacity(newSize);
            scripts.EnsureCapacity(newSize);
        }

        /// <summary>
        /// Lazily allocates <see cref="startMargins"/> to fit the current codepoint count
        /// and zero-initializes any region not yet prepared in this pass.
        /// </summary>
        /// <remarks>
        /// Call this from modifiers before writing start margins (e.g., for list indentation).
        /// Idempotent within a pass — subsequent calls with the same codepoint count are no-ops.
        /// </remarks>
        public void PrepareStartMargins()
        {
            var cpCount = codepoints.count;
            if (cpCount == 0) return;

            var prepared = startMargins.count;
            if (prepared >= cpCount) return;

            startMargins.EnsureCapacity(cpCount);
            startMargins.data.AsSpan(prepared, cpCount - prepared).Clear();
            startMargins.count = cpCount;
        }

        /// <summary>
        /// Lazily sizes <see cref="hiddenClusters"/> to the current codepoint count and returns its span.
        /// Idempotent within a pass: the first caller after <see cref="Reset"/> gets a zeroed span, later
        /// callers see flags already written this pass. A producer that recomputes its flags must clear
        /// its own cluster ranges before setting them — never the whole span, which may hold other
        /// producers' flags.
        /// </summary>
        internal Span<byte> PrepareHiddenClusters()
        {
            var cpCount = codepoints.count;
            if (cpCount == 0) return Span<byte>.Empty;

            var prepared = hiddenClusters.count;
            if (prepared < cpCount)
            {
                hiddenClusters.EnsureCapacity(cpCount);
                hiddenClusters.data.AsSpan(prepared, cpCount - prepared).Clear();
                hiddenClusters.count = cpCount;
            }

            return hiddenClusters.data.AsSpan(0, cpCount);
        }

        /// <summary>
        /// Lazily sizes <see cref="paintOrders"/> to the current codepoint count and returns its span.
        /// Idempotent within a pass: the first caller after <see cref="Reset"/> gets a zeroed
        /// (layer-major) span; later callers see modes already written this pass.
        /// </summary>
        internal Span<byte> PreparePaintOrders()
        {
            var cpCount = codepoints.count;
            if (cpCount == 0) return Span<byte>.Empty;

            var prepared = paintOrders.count;
            if (prepared < cpCount)
            {
                paintOrders.EnsureCapacity(cpCount);
                paintOrders.data.AsSpan(prepared, cpCount - prepared).Clear();
                paintOrders.count = cpCount;
            }

            return paintOrders.data.AsSpan(0, cpCount);
        }

        /// <summary>
        /// Sizes <see cref="glyphXScales"/> to the current codepoint count and returns its span.
        /// The positioning pass is the sole writer and rewrites every value it uses on each run,
        /// so previous contents are never trusted; deactivate by setting <c>count</c> to zero.
        /// </summary>
        internal Span<float> PrepareGlyphXScales()
        {
            var cpCount = codepoints.count;
            if (cpCount == 0) return Span<float>.Empty;

            glyphXScales.EnsureCapacity(cpCount);
            glyphXScales.count = cpCount;
            return glyphXScales.data.AsSpan(0, cpCount);
        }
    }


}
