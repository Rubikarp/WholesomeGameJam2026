using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Sets ruby (furigana) — small annotation text placed above a base run to show its reading or
    /// meaning, as used in Japanese and other East-Asian typography.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pair it with <see cref="RubyParseRule"/> for standard markup
    /// <c>&lt;ruby&gt;漢字&lt;rt&gt;かんじ&lt;/rt&gt;&lt;/ruby&gt;</c> and the shorthand
    /// <c>&lt;ruby=かんじ&gt;漢字&lt;/ruby&gt;</c>:
    /// <c>uniText.Styles.Add(new Style { Source = new RubyParseRule(), Modifier = new RubyModifier() })</c>. The reading reaches the
    /// modifier as the range parameter, so any rule that supplies one works.
    /// </para>
    /// <para>
    /// A single base+reading is group-ruby; several <c>&lt;rt&gt;</c> segments give mono-ruby
    /// (one reading per character): <c>&lt;ruby&gt;東&lt;rt&gt;とう&lt;/rt&gt;京&lt;rt&gt;きょう&lt;/rt&gt;&lt;/ruby&gt;</c>.
    /// </para>
    /// <para>
    /// Layout follows CSS Ruby / JIS X 4051: the annotation is half the base size by default and is
    /// centered over the base. When the annotation is narrower than the base it is distributed by
    /// <see cref="Align"/> (default <see cref="RubyAlign.SpaceAround"/> — the JIS 1:2:1 rule); when it
    /// is wider, the base columns are spread to fit it. The annotation raises the line height of the
    /// rows it sits on, so allow clearance above the first line (top padding or vertical centering).
    /// Horizontal, line-over annotations on left-to-right base text.
    /// </para>
    /// </remarks>
    /// <seealso cref="ScriptPositionModifier"/>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Adds ruby (furigana) annotations above the base text.")]
    public partial class RubyModifier : BaseModifier
    {
        /// <summary>
        /// How the annotation is positioned within its base column when it is narrower than the base.
        /// </summary>
        public enum RubyAlign : byte
        {
            /// <summary>Half a unit of space at each edge and a full unit between glyphs (CSS <c>space-around</c> / JIS X 4051 mono-ruby 1:2:1). The default.</summary>
            SpaceAround = 0,

            /// <summary>Centered as a solid block, no internal spacing (CSS <c>center</c>).</summary>
            Center = 1,

            /// <summary>Flush to both edges, extra space only between glyphs (CSS <c>space-between</c>); falls back to centered for a single glyph.</summary>
            SpaceBetween = 2,

            /// <summary>Flush to the start of the base, extra space trails at the end (CSS <c>start</c>).</summary>
            Start = 3,
        }

        /// <summary>Annotation side relative to the base.</summary>
        public enum RubyPosition : byte
        {
            /// <summary>Above the base (CSS <c>over</c>); the default for horizontal text.</summary>
            Over = 0,

            /// <summary>Below the base (CSS <c>under</c>).</summary>
            Under = 1,
        }

        private struct Entry
        {
            public int baseStart;
            public int baseEnd;
            public int rubyStart;
            public int rubyCount;
        }

        /// <summary>
        /// One shaped annotation, produced and owned exclusively by the shaping pass; carries its own copy
        /// of the spans placement needs so placement never reads <see cref="Entry"/>.
        /// </summary>
        private struct Reading
        {
            public int baseStart;
            public int baseEnd;
            public int rubyStart;
            public int shapedStart;
            public int shapedCount;
            public float rubyWidth;
        }

        private PooledList<Entry> entries;
        private PooledList<Reading> readings;
        private PooledBuffer<uint> rubyCodepoints;
        private PooledBuffer<ShapedGlyph> rubyShaped;
        private PooledBuffer<int> rubyShapedFont;

        private UniTextFontProvider fontProvider;
        private float effectiveScale;

        /// <summary>
        /// Annotation font size as a fraction of the base size. Default <c>0.5</c> (the typographic
        /// norm). Clamped to <c>[0.1, 1]</c>.
        /// </summary>
        [SerializeField, StateProperty(nameof(ApplyRubyScaleChange))] private float rubyScale = 0.5f;

        private void ApplyRubyScaleChange(float previous, ref float current)
        {
            current = Mathf.Clamp(current, 0.1f, 1f);
            if (!Mathf.Approximately(previous, current)) MarkTextDirty();
        }

        /// <summary>How a narrower annotation is distributed over its base column.</summary>
        [SerializeField, StateProperty(nameof(MarkLayoutDirty))] private RubyAlign align = RubyAlign.SpaceAround;

        /// <summary>Annotation side relative to the base: above (over) or below (under).</summary>
        [SerializeField, StateProperty(nameof(MarkLayoutDirty))] private RubyPosition position = RubyPosition.Over;

        private Action shapedCallback;
        private OrderedEventHandler<LineHeightContext> lineHeightCallback;
        private Action injectRubyCallback;

        protected override void OnEnable()
        {
            entries ??= new PooledList<Entry>(8);
            entries.FakeClear();
            readings ??= new PooledList<Reading>(8);
            readings.FakeClear();
            rubyCodepoints.Rent(64);
            rubyCodepoints.FakeClear();
            rubyShaped.Rent(64);
            rubyShapedFont.Rent(64);
            fontProvider = uniText.FontProvider;
            effectiveScale = Mathf.Clamp(rubyScale, 0.1f, 1f);

            shapedCallback ??= OnShaped;
            uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
            lineHeightCallback ??= OnCalculateLineHeight;
            uniText.TextProcessor.OnCalculateLineHeight.Subscribe(lineHeightCallback);
            injectRubyCallback ??= InjectRubyGlyphs;
            uniText.BeforeGenerateMesh.Subscribe(injectRubyCallback);
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
            uniText.TextProcessor.OnCalculateLineHeight.Unsubscribe(lineHeightCallback);
            uniText.BeforeGenerateMesh.Unsubscribe(injectRubyCallback);
        }

        protected override void OnDestroy()
        {
            entries?.Return();
            entries = null;
            readings?.Return();
            readings = null;
            rubyCodepoints.Return();
            rubyShaped.Return();
            rubyShapedFont.Return();
            fontProvider = null;
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var reader = context.Parameters.GetReader();
            if (!reader.Next(out var annotation) || annotation.IsEmpty) return;

            var cpCount = buffers.codepoints.count;
            var start = context.Segment.Range.start;
            var end = context.Segment.Range.End;
            if (start >= cpCount) return;
            end = Math.Min(end, cpCount);
            if (end <= start) return;

            var rubyStart = rubyCodepoints.count;
            var count = DecodeCodepoints(annotation);
            if (count == 0) return;

            entries.Add(new Entry { baseStart = start, baseEnd = end, rubyStart = rubyStart, rubyCount = count });
        }

        protected override void BeforeApply()
        {
            entries?.FakeClear();
            rubyCodepoints.FakeClear();
        }

        /// <summary>
        /// Shapes each annotation (see <see cref="ShapeReading"/>) and widens the base column to fit a wider
        /// annotation. Runs before <c>ComputeCpWidths</c>, so widening a base glyph's advance flows into line
        /// breaking automatically; only <c>run.width</c> must be recomputed by hand. Also prohibits breaks
        /// inside each base span so a wrap can't strand the annotation from its base.
        /// </summary>
        private void OnShaped()
        {
            readings.FakeClear();
            if (entries.Count == 0) return;

            var buf = buffers;
            rubyShaped.count = 0;
            rubyShapedFont.count = 0;

            var glyphs = buf.shapedGlyphs.data;
            var glyphCount = buf.shapedGlyphs.count;
            var breaks = buf.breakOpportunities.data;
            var breaksCount = buf.breakOpportunities.count;
            var widened = false;

            for (var e = 0; e < entries.Count; e++)
            {
                ref readonly var entry = ref entries[e];
                var keepEnd = Math.Min(entry.baseEnd, breaksCount);
                for (var i = entry.baseStart + 1; i < keepEnd; i++)
                    if (breaks[i] == LineBreakType.Optional) breaks[i] = LineBreakType.None;

                var reading = ShapeReading(in entry);
                readings.Add(reading);

                var baseWidth = 0f;
                for (var g = 0; g < glyphCount; g++)
                {
                    var c = glyphs[g].cluster;
                    if (c >= entry.baseStart && c < entry.baseEnd)
                        baseWidth += glyphs[g].advanceX;
                }

                if (baseWidth > 0f && reading.rubyWidth > baseWidth + 0.01f)
                {
                    WidenBase(entry, reading.rubyWidth - baseWidth, glyphs, glyphCount);
                    widened = true;
                }
            }

            if (widened)
                RecalcRunWidths();
        }

        /// <summary>
        /// Shapes one annotation's reading as a bidi-isolated run (CSS Ruby §3.5/§4.2): its own direction
        /// and full OpenType shaping (GSUB/GPOS), never joined to the base. Itemizes the reading by font and
        /// script, shapes each sub-run through HarfBuzz into <see cref="rubyShaped"/> (with a parallel font id
        /// per glyph), and registers the shaped glyph ids for atlas rasterization. Width and advances are in
        /// shaping units (like base glyphs); <see cref="InjectRubyGlyphs"/> rescales them by glyphScale.
        /// </summary>
        private Reading ShapeReading(in Entry entry)
        {
            var reading = new Reading
            {
                baseStart = entry.baseStart,
                baseEnd = entry.baseEnd,
                rubyStart = entry.rubyStart,
                shapedStart = rubyShaped.count
            };
            if (entry.rubyCount == 0) return reading;

            var buf = buffers;
            var ctx = MemoryMarshal.Cast<uint, int>(rubyCodepoints.data.AsSpan(entry.rubyStart, entry.rubyCount));

            uniText.TextProcessor.ShapeIsolatedRun(ctx, effectiveScale, ref rubyShaped, ref rubyShapedFont, out var width);
            reading.rubyWidth = width;
            reading.shapedCount = rubyShaped.count - reading.shapedStart;

            for (var g = reading.shapedStart; g < rubyShaped.count; g++)
                buf.RequestVirtualGlyph(rubyShapedFont[g], (uint)rubyShaped[g].glyphId);

            return reading;
        }

        /// <summary>
        /// Spreads a base group so its column matches a wider annotation: extra space goes between the
        /// base clusters (CSS <c>space-between</c>), keeping the first and last flush to the column
        /// edges. A single base cluster is centered instead.
        /// </summary>
        private void WidenBase(in Entry entry, float extra, ShapedGlyph[] glyphs, int glyphCount)
        {
            var clusters = 0;
            var prev = -1;
            var first = -1;
            var last = -1;
            for (var g = 0; g < glyphCount; g++)
            {
                var c = glyphs[g].cluster;
                if (c < entry.baseStart || c >= entry.baseEnd) continue;
                if (c != prev) { clusters++; prev = c; }
                if (first < 0) first = g;
                last = g;
            }
            if (clusters == 0) return;

            if (clusters == 1)
            {
                glyphs[first].offsetX += extra * 0.5f;
                glyphs[last].advanceX += extra;
                return;
            }

            var perGap = extra / (clusters - 1);
            prev = -1;
            var prevLastGlyph = -1;
            for (var g = 0; g < glyphCount; g++)
            {
                var c = glyphs[g].cluster;
                if (c < entry.baseStart || c >= entry.baseEnd) continue;
                if (c != prev)
                {
                    if (prevLastGlyph >= 0)
                        glyphs[prevLastGlyph].advanceX += perGap;
                    prev = c;
                }
                prevLastGlyph = g;
            }
        }

        private void RecalcRunWidths()
        {
            var runs = buffers.shapedRuns.data;
            var glyphs = buffers.shapedGlyphs.data;
            for (var r = 0; r < buffers.shapedRuns.count; r++)
            {
                ref var run = ref runs[r];
                var w = 0f;
                var end = run.glyphStart + run.glyphCount;
                for (var g = run.glyphStart; g < end; g++)
                    w += glyphs[g].advanceX;
                run.width = w;
            }
        }

        private void OnCalculateLineHeight(ref LineHeightContext context)
        {
            if (entries.Count == 0) return;

            for (var e = 0; e < entries.Count; e++)
            {
                var entry = entries[e];
                if (entry.baseEnd > context.startCluster && entry.baseStart < context.endCluster)
                {
                    var atBlockEdge = position == RubyPosition.Over
                        ? context.lineIndex == 0
                        : context.lineIndex == buffers.lines.count - 1;
                    var deficit = RubyLeadingDeficit(context.fontSize, context.lineAdvance, atBlockEdge);
                    if (deficit > 0f)
                    {
                        if (position == RubyPosition.Over)
                            uniText.TextProcessor.ReserveLineSpace(context.lineIndex, deficit, 0f);
                        else
                            uniText.TextProcessor.ReserveLineSpace(context.lineIndex, 0f, deficit);
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Extra leading a ruby line needs (CSS Ruby "Line Spacing", matching Blink <c>ComputeAnnotationOverflow</c>).
        /// Calibrated to Chrome/Blink by pixel measurement across line-height and size: between lines the line
        /// grows by <c>fontAscent + annotationEm - lineHeight</c> — the annotation contributes its em on top of
        /// the line font's ascent, the rest absorbed by the neighbour's leading. At a block edge (first line for
        /// over, last for under) nothing absorbs the overhang, so reserve the full box clearance against the
        /// line's ascent or descent. Assumes half-leading distribution.
        /// </summary>
        private float RubyLeadingDeficit(float fontSize, float lineHeight, bool atBlockEdge)
        {
            var annotationEm = fontSize * effectiveScale;
            fontProvider.GetLineMetrics(fontSize, out var ascender, out var descender, out _);

            if (!atBlockEdge)
                return Math.Max(0f, ascender + annotationEm - lineHeight);

            NormalizedEm(fontProvider.PrimaryFontId, fontSize, out var baseAscent, out var baseDescent);

            if (position == RubyPosition.Over)
            {
                var lineAscent = (lineHeight + ascender + descender) * 0.5f;
                return Math.Max(0f, baseAscent + annotationEm - lineAscent);
            }

            var lineDescent = (lineHeight - ascender - descender) * 0.5f;
            return Math.Max(0f, baseDescent + annotationEm - lineDescent);
        }

        /// <summary>
        /// Em-normalized typographic ascent/descent (Blink/Gecko ruby model): typo metrics rescaled so
        /// ascent + descent == em, which strips the per-font padding (e.g. Noto's 1.07em typo ascent) that
        /// would otherwise inflate the annotation gap. <paramref name="em"/> is the already-shrunk em for the
        /// annotation. Falls back to hhea ascent/descent when the font has no OS/2 typo metrics.
        /// </summary>
        private void NormalizedEm(int fontId, float em, out float ascent, out float descent)
        {
            var font = fontProvider.GetFont(fontId);
            if (font == null) { ascent = em * 0.8f; descent = em * 0.2f; return; }
            var fi = font.FaceInfo;
            var rawAscent = fi.typoAscent > 0 ? fi.typoAscent : fi.ascentLine;
            var rawDescent = fi.typoDescent < 0 ? -fi.typoDescent : -fi.descentLine;
            var sum = rawAscent + rawDescent;
            if (sum <= 0f) { ascent = em * 0.8f; descent = em * 0.2f; return; }
            ascent = em * rawAscent / sum;
            descent = em - ascent;
        }

        /// <summary>
        /// Places annotation glyphs as virtual glyphs on a common annotation baseline per text line. Each side is
        /// an em box (<see cref="NormalizedEm"/>) stacked flush against the base, taking the per-line MAX base
        /// ascent and MAX annotation descent so mixed fonts align on one row (Blink RubyBlockPositionCalculator /
        /// Gecko nsRubyFrame). Pass 1 measures each entry; pass 2 aggregates the line maxima and emits glyphs.
        /// </summary>
        private void InjectRubyGlyphs()
        {
            if (readings.Count == 0) return;

            var buf = buffers;
            var fontSize = uniText.CurrentFontSize;
            var glyphScale = buf.GetGlyphScale(fontSize);
            var over = position == RubyPosition.Over;

            var pos = buf.positionedGlyphs.data;
            var posCount = buf.positionedGlyphs.count;
            var shaped = buf.shapedGlyphs.data;
            var hiddenFlags = buf.hiddenClusters.data;
            var hiddenCount = buf.hiddenClusters.count;

            var n = readings.Count;
            var gLeft = ArrayPool<float>.Rent(n);
            var gRight = ArrayPool<float>.Rent(n);
            var gBaseY = ArrayPool<float>.Rent(n);
            var gAnchor = ArrayPool<float>.Rent(n);
            var gAnnot = ArrayPool<float>.Rent(n);
            var gFound = ArrayPool<bool>.Rent(n);
            try
            {
                for (var e = 0; e < n; e++)
                {
                    gFound[e] = false;
                    var entry = readings[e];
                    if (entry.shapedCount == 0) continue;

                    var leftX = float.PositiveInfinity;
                    var rightX = float.NegativeInfinity;
                    var baselineY = 0f;
                    var anchor = 0f;
                    var found = false;
                    for (var i = 0; i < posCount; i++)
                    {
                        ref readonly var pg = ref pos[i];
                        if (pg.cluster < entry.baseStart || pg.cluster >= entry.baseEnd) continue;
                        if ((uint)pg.cluster < (uint)hiddenCount && hiddenFlags[pg.cluster] != 0) continue;
                        if (found && pg.y != baselineY) continue;

                        float origin, advance;
                        if (pg.shapedGlyphIndex >= 0)
                        {
                            ref readonly var sg = ref shaped[pg.shapedGlyphIndex];
                            origin = pg.x - sg.offsetX * glyphScale;
                            advance = sg.advanceX * glyphScale;
                        }
                        else
                        {
                            origin = pg.x;
                            advance = pg.right - pg.left;
                        }

                        if (origin < leftX) leftX = origin;
                        if (origin + advance > rightX) rightX = origin + advance;

                        if (fontProvider.GetFont(pg.fontId) != null)
                        {
                            NormalizedEm(pg.fontId, fontSize, out var na, out var nd);
                            var v = over ? na : nd;
                            if (v > anchor) anchor = v;
                        }

                        if (!found) { baselineY = pg.y; found = true; }
                    }
                    if (!found) continue;
                    if (anchor <= 0f) anchor = fontSize * (over ? 0.8f : 0.2f);

                    var shapedEnd = entry.shapedStart + entry.shapedCount;
                    var annotEm = fontSize * effectiveScale;
                    var annot = 0f;
                    for (var g = entry.shapedStart; g < shapedEnd; g++)
                    {
                        if (fontProvider.GetFont(rubyShapedFont[g]) == null) continue;
                        NormalizedEm(rubyShapedFont[g], annotEm, out var na, out var nd);
                        var v = over ? nd : na;
                        if (v > annot) annot = v;
                    }
                    if (annot <= 0f) annot = annotEm * (over ? 0.2f : 0.8f);

                    gLeft[e] = leftX;
                    gRight[e] = rightX;
                    gBaseY[e] = baselineY;
                    gAnchor[e] = anchor;
                    gAnnot[e] = annot;
                    gFound[e] = true;
                }

                for (var e = 0; e < n; e++)
                {
                    if (!gFound[e]) continue;
                    var entry = readings[e];

                    var baselineY = gBaseY[e];
                    var lineAnchor = gAnchor[e];
                    var lineAnnot = gAnnot[e];
                    for (var j = 0; j < n; j++)
                    {
                        if (j == e || !gFound[j] || gBaseY[j] != baselineY) continue;
                        if (gAnchor[j] > lineAnchor) lineAnchor = gAnchor[j];
                        if (gAnnot[j] > lineAnnot) lineAnnot = gAnnot[j];
                    }

                    var penY = over
                        ? baselineY - lineAnchor - lineAnnot
                        : baselineY + lineAnchor + lineAnnot;

                    var shapedEnd = entry.shapedStart + entry.shapedCount;
                    var clusters = 0;
                    var prevCluster = int.MinValue;
                    for (var g = entry.shapedStart; g < shapedEnd; g++)
                        if (rubyShaped[g].cluster != prevCluster) { clusters++; prevCluster = rubyShaped[g].cluster; }

                    var slack = gRight[e] - gLeft[e] - entry.rubyWidth * glyphScale;
                    if (slack < 0f) slack = 0f;
                    var spread = align == RubyAlign.SpaceAround || align == RubyAlign.SpaceBetween;
                    var distAlign = spread && !IsCjkReading(entry.rubyStart) ? RubyAlign.Center : align;
                    Distribute(distAlign, slack, clusters, out var lead, out var inner);

                    var penX = gLeft[e] + lead;
                    prevCluster = int.MinValue;
                    for (var g = entry.shapedStart; g < shapedEnd; g++)
                    {
                        ref readonly var sg = ref rubyShaped[g];
                        if (sg.cluster != prevCluster && prevCluster != int.MinValue) penX += inner;
                        prevCluster = sg.cluster;

                        var adv = sg.advanceX * glyphScale;
                        var gx = penX + sg.offsetX * glyphScale;
                        var gy = penY - sg.offsetY * glyphScale;
                        buf.virtualPositionedGlyphs.Add(new PositionedGlyph
                        {
                            glyphId = sg.glyphId,
                            cluster = entry.baseStart,
                            x = gx,
                            y = gy,
                            fontId = rubyShapedFont[g],
                            scale = effectiveScale,
                            shapedGlyphIndex = -1,
                            left = gx,
                            right = gx + adv,
                            top = penY,
                            bottom = penY
                        });
                        penX += adv;
                    }
                }
            }
            finally
            {
                ArrayPool<float>.Return(gLeft);
                ArrayPool<float>.Return(gRight);
                ArrayPool<float>.Return(gBaseY);
                ArrayPool<float>.Return(gAnchor);
                ArrayPool<float>.Return(gAnnot);
                ArrayPool<bool>.Return(gFound);
            }
        }

        private bool IsCjkReading(int rubyStart)
        {
            var s = UnicodeData.GetScript((int)rubyCodepoints[rubyStart]);
            return s == UnicodeScript.Han || s == UnicodeScript.Hiragana
                || s == UnicodeScript.Katakana || s == UnicodeScript.Bopomofo;
        }

        private static void Distribute(RubyAlign align, float slack, int n, out float lead, out float inner)
        {
            switch (align)
            {
                case RubyAlign.Center:
                    lead = slack * 0.5f;
                    inner = 0f;
                    break;
                case RubyAlign.SpaceBetween when n > 1:
                    lead = 0f;
                    inner = slack / (n - 1);
                    break;
                case RubyAlign.SpaceBetween:
                    lead = slack * 0.5f;
                    inner = 0f;
                    break;
                case RubyAlign.Start:
                    lead = 0f;
                    inner = 0f;
                    break;
                default:
                    lead = slack / (2 * n);
                    inner = slack / n;
                    break;
            }
        }

        private int DecodeCodepoints(ReadOnlySpan<char> text)
        {
            var count = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var cp = UnicodeData.DecodeAt(text, i, out var size);
                rubyCodepoints.Add(cp);
                count++;
                i += size - 1;
            }

            return count;
        }
    }
}
