using System;
using System.Globalization;
using UnityEngine;

namespace LightSide
{
    /// <summary>How italic is realized. Auto prefers a real italic face and shears synthetically only when none exists; the others force one behaviour.</summary>
    public enum ItalicMode : byte
    {
        /// <summary>Prefer a real italic face (family cut, variable ital/slnt axis, OS fallback); shear synthetically only if none exists.</summary>
        Auto,
        /// <summary>Always shear by <see cref="ItalicStyle.slant"/> percent of height, ignoring real faces.</summary>
        Synthetic,
        /// <summary>Shear synthetically using the run font's own <see cref="UniTextFont.Core.ItalicStyle"/>.</summary>
        FontSlant,
        /// <summary>Use a real italic face only; leave the text upright when none is available.</summary>
        RealOnly
    }

    /// <summary>
    /// Italic choice: a mode plus, for <see cref="ItalicMode.Synthetic"/>, a shear percentage. Serializes to a single
    /// markup token — empty for auto, the percentage for synthetic, <c>f</c> for font-slant, <c>r</c> for real-only.
    /// </summary>
    [Serializable]
    public struct ItalicStyle : IMarkupValue
    {
        public ItalicMode mode;
        public int slant;

        public string ToToken() => mode switch
        {
            ItalicMode.Synthetic => slant.ToString(CultureInfo.InvariantCulture),
            ItalicMode.FontSlant => "f",
            ItalicMode.RealOnly => "r",
            _ => ""
        };

        public void FromToken(string token)
            => FromToken(token.AsSpan());

        internal void FromToken(ReadOnlySpan<char> token)
        {
            if (token.IsEmpty) this = default;
            else if (token.EqualsIgnoreCase("f")) this = new ItalicStyle { mode = ItalicMode.FontSlant };
            else if (token.EqualsIgnoreCase("r")) this = new ItalicStyle { mode = ItalicMode.RealOnly };
            else if (ParameterReader.ParseFloat(token, out var s)) this = new ItalicStyle { mode = ItalicMode.Synthetic, slant = UnityEngine.Mathf.RoundToInt(s) };
            else this = default;
        }
    }

    /// <summary>
    /// Renders italic by slanting glyphs. Tags: <c>&lt;i&gt;</c> = auto, <c>&lt;i=20&gt;</c> = synthetic shear of
    /// 20% of height, <c>&lt;i=f&gt;</c> = the run font's own slant, <c>&lt;i=r&gt;</c> = a real italic face only.
    /// </summary>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Slants glyphs to italic. Prefers a real italic face; a parameter forces synthetic shear or real-only.")]
    [GenerateParameters]
    public partial class ItalicModifier : PooledAttributeModifier<byte>
    {
        /// <summary>Gets or sets the complete italic configuration.</summary>
        [SerializeField, Parameter(Parser = nameof(ParseStyle), Invalidate = nameof(MarkTextDirty)), Variant("Auto|Slant=int:30|Font slant=f|Real only=r", Discriminator = nameof(ItalicStyle.mode)), StateProperty(nameof(ApplyStyleChange))] private ItalicStyle style;

        private static bool ParseStyle(ReadOnlySpan<char> token, out ItalicStyle value)
        {
            value = default;
            value.FromToken(token);
            return true;
        }

        private void ApplyStyleChange(ItalicStyle previous, ItalicStyle current)
        {
            if (previous.mode == current.mode && previous.slant == current.slant) return;
            if (previous.mode == current.mode) MarkMeshDirty();
            else MarkTextDirty();
        }

        /// <summary>How italic is realized. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        public ItalicMode Mode
        {
            get => style.mode;
            set
            {
                var current = style;
                current.mode = value;
                Style = current;
            }
        }

        /// <summary>Synthetic shear in percent of height (−100…100; 30 leans the top right by 0.30 of the height, ≈16.7°, not 30°), applied when <see cref="Mode"/> is <see cref="ItalicMode.Synthetic"/>. A per-range value overrides it.</summary>
        public int Slant
        {
            get => style.slant;
            set
            {
                var current = style;
                current.slant = value;
                Style = current;
            }
        }

        private const int MinSlant = -100;
        private const int MaxSlant = 100;

        protected sealed override string AttributeKey => AttributeKeys.Italic;

        protected override AttributeChannel CreateChannel() => new Channel();

        protected override void OnApply(in RangeApplyContext context)
        {
            var s = Param.Style.Resolve(this, in context);
            var encoded = Encode(in s);

            attribute.FillRange(context.Segment.Range, encoded);
        }

        private static byte Encode(in ItalicStyle s) => s.mode switch
        {
            ItalicMode.Auto => FontStyleEncoding.ItalicAuto,
            ItalicMode.FontSlant => FontStyleEncoding.ItalicFakeUsesFontSlant,
            ItalicMode.RealOnly => FontStyleEncoding.ItalicRealOnly,
            _ => EncodeSlant(s.slant)
        };

        private static byte EncodeSlant(int slant) => (byte)(Math.Clamp(slant, MinSlant, MaxSlant) - MinSlant + 3);
        private static int DecodeSlant(byte encoded) => encoded - 3 + MinSlant;

        private static float Shear(byte encoded, UniTextFont.Core font)
            => (encoded == FontStyleEncoding.ItalicAuto || encoded == FontStyleEncoding.ItalicFakeUsesFontSlant
                ? font.ItalicStyle
                : DecodeSlant(encoded)) * 0.01f;

        private static float HalfXHeight(in FaceInfo faceInfo)
            => faceInfo.meanLine > 0 ? faceInfo.meanLine * 0.5f : faceInfo.ascentLine * 0.35f;

        private sealed class Channel : AttributeChannel
        {
            private PooledArrayAttribute<byte> attribute;
            private Action onGlyphCallback;
            private Action shapedCallback;
            private Action linesBrokenCallback;
            private bool hasSlant;

            protected override void OnActivate()
            {
                attribute = buffers.GetAttributeData<PooledArrayAttribute<byte>>(Key);
                hasSlant = false;

                onGlyphCallback ??= OnGlyph;
                uniText.MeshGenerator.onGlyph.Subscribe(onGlyphCallback);
                shapedCallback ??= OnShaped;
                uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
                linesBrokenCallback ??= OnLinesBroken;
                uniText.TextProcessor.LinesBroken.Subscribe(linesBrokenCallback);
            }

            protected override void OnDeactivate()
            {
                uniText.MeshGenerator.onGlyph.Unsubscribe(onGlyphCallback);
                uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
                uniText.TextProcessor.LinesBroken.Unsubscribe(linesBrokenCallback);
                attribute?.ClearAll();
                hasSlant = false;
            }

            protected override void OnRelease()
            {
                attribute = null;
                onGlyphCallback = null;
                shapedCallback = null;
                linesBrokenCallback = null;
            }

            private void OnShaped()
            {
                hasSlant = attribute != null && attribute.buffer.data.HasAnyFlags();
                if (!hasSlant) return;

                var reserve = 0f;
                var paragraphs = buffers.paragraphs.data;
                var paragraphCount = buffers.paragraphs.count;

                for (var p = 0; p < paragraphCount; p++)
                {
                    ref var paragraph = ref paragraphs[p];
                    if (!ClampSpan(paragraph.cpStart, paragraph.CpEnd - 1, out var start, out var end))
                        continue;

                    var rtl = (paragraph.baseLevel & 1) != 0;
                    var sum = LeadingBleed(start, end, rtl) + TrailingBleed(start, end, rtl);
                    if (sum > reserve) reserve = sum;
                }

                if (reserve > 0f) uniText.TextProcessor.ReserveLineWidth(reserve);
            }

            private void OnLinesBroken()
            {
                if (!hasSlant) return;

                var lines = buffers.lines.data;
                var lineCount = buffers.lines.count;

                for (var i = 0; i < lineCount; i++)
                {
                    ref var line = ref lines[i];
                    if (!ClampSpan(line.range.start, line.range.start + line.range.length - 1,
                            out var start, out var end))
                        continue;

                    var rtl = line.IsRtl;
                    line.startMargin = BaseStartMargin(line.range.start) + LeadingBleed(start, end, rtl);
                    line.width += TrailingBleed(start, end, rtl);
                }
            }

            private bool ClampSpan(int spanStart, int spanEnd, out int start, out int end)
            {
                start = spanStart;
                end = spanEnd;
                var cpCount = buffers.codepoints.count;
                if (end >= cpCount) end = cpCount - 1;
                return start >= 0 && end >= start;
            }

            /// <summary>Ink reaching past the span's leading edge, walking inward over glyphs that occupy no width.</summary>
            private float LeadingBleed(int start, int end, bool rtl)
            {
                var processor = uniText.TextProcessor;
                for (var cp = start; cp <= end; cp++)
                {
                    if (processor.IsClusterHiddenFromLayout(cp)) continue;
                    var edge = MeasureEdge(cp, out var left, out var right);
                    if (edge == EdgeInk.Transparent) continue;
                    return edge == EdgeInk.Measured ? (rtl ? right : left) : 0f;
                }
                return 0f;
            }

            /// <summary>Ink reaching past the span's trailing edge; hanging whitespace does not hold the edge.</summary>
            private float TrailingBleed(int start, int end, bool rtl)
            {
                var processor = uniText.TextProcessor;
                var codepoints = buffers.codepoints.data;
                for (var cp = end; cp >= start; cp--)
                {
                    if (processor.IsClusterHiddenFromLayout(cp)) continue;
                    if (LineBreaker.IsHangingWhitespace(codepoints[cp])) continue;
                    var edge = MeasureEdge(cp, out var left, out var right);
                    if (edge == EdgeInk.Transparent) continue;
                    return edge == EdgeInk.Measured ? (rtl ? left : right) : 0f;
                }
                return 0f;
            }

            /// <summary>The margin the line breaker itself derives for a line starting at <paramref name="lineStart"/>; assigning over it keeps the bleed idempotent across repositions that reuse the existing breaks.</summary>
            private float BaseStartMargin(int lineStart)
                => (uint)lineStart < (uint)buffers.startMargins.count
                    ? buffers.startMargins.data[lineStart]
                    : 0f;

            /// <summary>What a cluster does to the edge it sits on: holds it upright, occupies no width so the edge falls through to the next cluster, or carries slanted ink past it.</summary>
            private enum EdgeInk : byte { Blocked, Transparent, Measured }

            /// <summary>
            /// How much further than upright a cluster's slanted ink reaches left of its origin and right
            /// of its advance, in shaping units — the reach the font's own side bearings already grant it
            /// stays uncharged, so a glyph designed to overhang keeps overhanging.
            /// </summary>
            private EdgeInk MeasureEdge(int cluster, out float left, out float right)
            {
                left = 0f;
                right = 0f;

                var runIndex = FindRun(cluster);
                if (runIndex < 0) return EdgeInk.Blocked;

                ref var run = ref buffers.shapedRuns.data[runIndex];
                var fontProvider = uniText.FontProvider;
                var font = fontProvider.GetFont(run.fontId);
                if (font == null || font.IsColor) return EdgeInk.Blocked;

                var glyphs = buffers.shapedGlyphs.data;
                var g = FindGlyph(glyphs, in run, cluster);
                if (g < 0) return EdgeInk.Blocked;

                var glyphId = glyphs[g].glyphId;
                if (glyphId == ShapedGlyph.NoGlyph || !Shaper.TryGetGlyphInk(font, (uint)glyphId, out var ink))
                    return glyphs[g].advanceX == 0f ? EdgeInk.Transparent : EdgeInk.Blocked;

                var italic = attribute.buffer.data;
                if (!italic.HasFlag(cluster)) return EdgeInk.Blocked;

                var realization = buffers.fontStyleRealizations.data;
                if ((uint)cluster >= (uint)buffers.fontStyleRealizations.count
                    || ((FontStyleRealization)realization[cluster] & FontStyleRealization.SyntheticSlant) == 0)
                    return EdgeInk.Blocked;

                var shear = Shear(italic[cluster], font);
                if (shear == 0f) return EdgeInk.Blocked;

                var size = buffers.shapingFontSize > 0f ? buffers.shapingFontSize : uniText.FontSize;
                var scale = fontProvider.MetricScale(font, size);
                var faceInfo = font.FaceInfo;
                var pivot = HalfXHeight(in faceInfo) * scale;

                var inkTop = ink.yBearing * scale;
                var inkBottom = (ink.yBearing + ink.height) * scale;
                var leaningLow = shear > 0f ? inkBottom : inkTop;
                var leaningHigh = shear > 0f ? inkTop : inkBottom;

                var offsetX = glyphs[g].offsetX;
                var uprightLeft = ink.xBearing * scale + offsetX;
                var uprightRight = (ink.xBearing + ink.width) * scale + offsetX;
                var slantedLeft = uprightLeft + shear * (leaningLow - pivot);
                var slantedRight = uprightRight + shear * (leaningHigh - pivot);
                var advance = glyphs[g].advanceX;

                left = Math.Max(0f, -slantedLeft) - Math.Max(0f, -uprightLeft);
                right = Math.Max(0f, slantedRight - advance) - Math.Max(0f, uprightRight - advance);
                if (left < 0f) left = 0f;
                if (right < 0f) right = 0f;
                return EdgeInk.Measured;
            }

            private int FindRun(int cluster)
            {
                var runs = buffers.shapedRuns.data;
                var low = 0;
                var high = buffers.shapedRuns.count - 1;
                while (low <= high)
                {
                    var middle = low + ((high - low) >> 1);
                    ref var run = ref runs[middle];
                    if (cluster < run.range.start) high = middle - 1;
                    else if (cluster >= run.range.End) low = middle + 1;
                    else return middle;
                }
                return -1;
            }

            /// <summary>
            /// First glyph of the cluster covering <paramref name="cluster"/> within a run, found over the
            /// monotone cluster sequence HarfBuzz emits (ascending for LTR runs, descending for RTL).
            /// </summary>
            private static int FindGlyph(ShapedGlyph[] glyphs, in ShapedRun run, int cluster)
            {
                var first = run.glyphStart;
                var last = first + run.glyphCount - 1;
                if (last < first) return -1;

                var ascending = glyphs[first].cluster <= glyphs[last].cluster;
                var low = first;
                var high = last;
                var found = -1;

                while (low <= high)
                {
                    var middle = low + ((high - low) >> 1);
                    var value = glyphs[middle].cluster;
                    var covered = value <= cluster;
                    if (covered && (found < 0 || value > glyphs[found].cluster)) found = middle;
                    if (ascending == covered) low = middle + 1;
                    else high = middle - 1;
                }

                if (found < 0) return -1;
                var covering = glyphs[found].cluster;
                while (found > first && glyphs[found - 1].cluster == covering) found--;
                return found;
            }

            private void OnGlyph()
            {
                var gen = uniText.MeshGenerator;
                if (gen.font.IsColor) return;

                var buf = attribute.buffer.data;
                var cluster = gen.currentCluster;
                if (!buf.HasFlag(cluster)) return;

                var encoded = buf[cluster];
                var realization = buffers.fontStyleRealizations.data;
                if ((uint)cluster >= (uint)buffers.fontStyleRealizations.count
                    || ((FontStyleRealization)realization[cluster] & FontStyleRealization.SyntheticSlant) == 0)
                    return;

                var shearValue = Shear(encoded, gen.font);
                var baseIdx = gen.faceBaseIdx;
                var verts = gen.Vertices;

                var faceInfo = gen.font.FaceInfo;
                var pivotY = gen.baselineY
                             + HalfXHeight(in faceInfo) / gen.font.UnitsPerEm
                             * gen.fontMetricFactor * gen.currentGlyphScale;

                var blY = verts[baseIdx].y;
                var tlY = verts[baseIdx + 1].y;

                var topShearX = shearValue * (tlY - pivotY);
                var bottomShearX = shearValue * (blY - pivotY);

                verts[baseIdx].x += bottomShearX;
                verts[baseIdx + 1].x += topShearX;
                verts[baseIdx + 2].x += topShearX;
                verts[baseIdx + 3].x += bottomShearX;
            }
        }
    }
}
