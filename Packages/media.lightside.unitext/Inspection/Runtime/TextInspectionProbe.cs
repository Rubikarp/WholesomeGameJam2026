using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;


namespace LightSide.Inspection
{
    /// <summary>
    /// Reads laid-out text data for a single point: maps a glyph hit to its codepoint, shaping
    /// metrics, run, line, and the modifier spans covering it. Pure data — no rendering.
    /// Main-thread only.
    /// </summary>
    public static class TextInspectionProbe
    {
        private static readonly List<(int start, int end, BaseModifier modifier, string parameter)> scratchSpans = new();
        private static readonly List<ModifierRange> scratchRanges = new();
        private static readonly StringBuilder scratchResolved = new();

        /// <summary>Probes the glyph under a screen-space point. <paramref name="camera"/> is null for overlay canvases.</summary>
        public static TextInspectionSnapshot ProbeScreen(UniTextBase text, Vector2 screenPosition, Camera camera,
            List<ModifierInspection> modifiersOut = null, float maxDistance = 0f)
        {
            if (text == null) return default;
            return Probe(text, text.HitTestRange(screenPosition, camera, maxDistance), modifiersOut);
        }

        /// <summary>Builds a snapshot from an existing hit. Pass <paramref name="modifiersOut"/> to also collect covering modifier spans.</summary>
        public static TextInspectionSnapshot Probe(UniTextBase text, in TextHitResult hit,
            List<ModifierInspection> modifiersOut = null)
        {
            modifiersOut?.Clear();

            var snap = new TextInspectionSnapshot();
            if (text == null || !hit.hit) return snap;

            var processor = text.TextProcessor;
            if (processor == null) return snap;
            var buf = processor.buf;

            var glyphs = text.ResultGlyphs;
            if ((uint)hit.glyphIndex >= (uint)glyphs.Length) return snap;

            snap.hit = true;
            snap.glyph = BuildGlyph(buf, glyphs, hit.glyphIndex, text.FontProvider);
            snap.run = BuildRun(buf, snap.glyph.runIndex);
            snap.line = BuildLine(buf, snap.glyph.lineIndex);

            if (modifiersOut != null)
                CollectModifiers(text, snap.glyph.cluster, modifiersOut);

            return snap;
        }

        private static GlyphInspection BuildGlyph(UniTextBuffers buf, System.ReadOnlySpan<PositionedGlyph> glyphs, int index, UniTextFontProvider provider)
        {
            var g = glyphs[index];

            var gi = new GlyphInspection
            {
                valid = true,
                glyphIndex = index,
                cluster = g.cluster,
                glyphId = g.glyphId,
                isNotdef = g.glyphId == 0,
                fontId = g.fontId,
                left = g.left,
                top = g.top,
                right = g.right,
                bottom = g.bottom,
                x = g.x,
                y = g.y
            };

            var cps = buf.codepoints.Span;
            if ((uint)g.cluster < (uint)cps.Length)
                gi.codepoint = cps[g.cluster];

            gi.script = ScriptAt(buf, g.cluster);
            gi.bidiLevel = BidiAt(buf, g.cluster);
            gi.direction = (gi.bidiLevel & 1) == 0 ? TextDirection.LeftToRight : TextDirection.RightToLeft;

            var shaped = buf.shapedGlyphs.Span;
            if ((uint)g.shapedGlyphIndex < (uint)shaped.Length)
            {
                ref readonly var sg = ref shaped[g.shapedGlyphIndex];
                gi.advanceX = sg.advanceX;
                gi.advanceY = sg.advanceY;
                gi.offsetX = sg.offsetX;
                gi.offsetY = sg.offsetY;
            }

            gi.category = CategoryOf(gi.codepoint);
            if (provider != null)
            {
                gi.fontName = provider.GetFont(g.fontId)?.Name;
                gi.isFallback = g.fontId != provider.PrimaryFontId;
            }
            gi.runIndex = FindRun(buf, g.shapedGlyphIndex);
            gi.lineIndex = FindLine(buf, index);
            return gi;
        }

        private static RunInspection BuildRun(UniTextBuffers buf, int runIndex)
        {
            var runs = buf.shapedRuns.Span;
            if ((uint)runIndex >= (uint)runs.Length) return default;

            ref readonly var r = ref runs[runIndex];
            return new RunInspection
            {
                valid = true,
                runIndex = runIndex,
                range = r.range,
                script = ScriptAt(buf, r.range.start),
                direction = r.direction,
                bidiLevel = r.bidiLevel,
                language = r.language,
                fontId = r.fontId,
                glyphCount = r.glyphCount,
                width = r.width
            };
        }

        private static LineInspection BuildLine(UniTextBuffers buf, int lineIndex)
        {
            var lines = buf.lines.Span;
            if ((uint)lineIndex >= (uint)lines.Length) return default;

            ref readonly var l = ref lines[lineIndex];
            return new LineInspection
            {
                valid = true,
                lineIndex = lineIndex,
                range = l.range,
                runCount = l.runCount,
                glyphCount = l.glyphCount,
                width = l.width,
                widthPx = l.widthPx,
                trailingWhitespace = l.trailingWhitespace,
                isRtl = l.IsRtl,
                startMargin = l.startMargin,
                endedByMandatoryBreak = l.endedByMandatoryBreak
            };
        }

        private static int FindRun(UniTextBuffers buf, int shapedGlyphIndex)
        {
            var runs = buf.shapedRuns.Span;
            for (var i = 0; i < runs.Length; i++)
            {
                ref readonly var r = ref runs[i];
                if (shapedGlyphIndex >= r.glyphStart && shapedGlyphIndex < r.glyphStart + r.glyphCount)
                    return i;
            }
            return -1;
        }

        private static int FindLine(UniTextBuffers buf, int glyphIndex)
        {
            var lines = buf.lines.Span;
            for (var i = 0; i < lines.Length; i++)
            {
                ref readonly var l = ref lines[i];
                if (glyphIndex >= l.glyphStart && glyphIndex < l.glyphStart + l.glyphCount)
                    return i;
            }
            return -1;
        }

        private static void CollectModifiers(UniTextBase text, int cluster, List<ModifierInspection> output)
        {
            var parser = text.AttributeParser;
            if (parser == null) return;

            parser.CollectSpansForInspection(scratchSpans);
            for (var i = 0; i < scratchSpans.Count; i++)
            {
                var s = scratchSpans[i];
                if (cluster >= s.start && cluster < s.end)
                    output.Add(new ModifierInspection(s.modifier?.GetType().Name, s.parameter,
                        s.start, s.end, ResolveParameters(parser, s.modifier, cluster)));
            }
        }

        /// <summary>
        /// Formats the modifier's declared parameters resolved on the range covering
        /// <paramref name="cluster"/> — cascade plus owned values — or null when the modifier
        /// declares none or the cluster sits on no materialized range.
        /// </summary>
        private static string ResolveParameters(AttributeParser parser, BaseModifier modifier,
            int cluster)
        {
            if (modifier == null || modifier.Descriptors.Length == 0) return null;
            scratchRanges.Clear();
            parser.CollectRangesFor(modifier, scratchRanges);
            for (var i = 0; i < scratchRanges.Count; i++)
            {
                var range = scratchRanges[i];
                if (cluster < range.Range.start || cluster >= range.Range.End) continue;
                var descriptors = modifier.Descriptors;
                scratchResolved.Clear();
                for (var d = 0; d < descriptors.Length; d++)
                {
                    var value = descriptors[d].DescribeOn(in range);
                    if (value == null) continue;
                    if (scratchResolved.Length > 0) scratchResolved.Append("  ");
                    scratchResolved.Append(descriptors[d].Id).Append('=').Append(value);
                }
                return scratchResolved.Length > 0 ? scratchResolved.ToString() : null;
            }
            return null;
        }

        private static UnicodeScript ScriptAt(UniTextBuffers buf, int cp)
        {
            var arr = buf.scripts.data;
            return arr != null && (uint)cp < (uint)buf.codepoints.count && cp < arr.Length ? arr[cp] : default;
        }

        private static byte BidiAt(UniTextBuffers buf, int cp)
        {
            var arr = buf.bidiLevels.data;
            return arr != null && (uint)cp < (uint)buf.codepoints.count && cp < arr.Length ? arr[cp] : (byte)0;
        }

        private static UnicodeCategory CategoryOf(int codepoint)
        {
            if (!Utf16.IsUnicodeScalar(codepoint))
                return UnicodeCategory.OtherNotAssigned;

            return CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(codepoint), 0);
        }
    }
}
