using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Adjusts the gap at spaces between words (the regular space and no-break space), leaving
    /// letter-to-letter spacing untouched. The CSS <c>word-spacing</c> analogue.
    /// </summary>
    /// <remarks>
    /// Parameter: spacing with a unit — <c>0.25em</c> (relative to font size) or a raw pixel amount such
    /// as <c>4</c>; negative values tighten. Applies at the regular space and no-break space; tabs and
    /// fixed-width spaces (U+2000–U+200A, U+3000) are left alone. Overlapping ranges resolve
    /// innermost-wins and never compound, across modifiers as well as across nested tags.
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 1)]
    [TypeDescription("Adjusts the spacing at spaces between words.")]
    [GenerateParameters]
    public partial class WordSpacingModifier : PooledAttributeModifier<float>
    {
        /// <summary>Extra gap added at word-separating spaces. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkTextDirty))] private UnitValue spacing = UnitValue.Em(0.25f);

        protected sealed override string AttributeKey => AttributeKeys.WordSpacing;

        protected override AttributeChannel CreateChannel() => new Channel();

        protected override void OnApply(in RangeApplyContext context)
        {
            var value = Param.Spacing.Resolve(this, in context);

            var baseSize = buffers.shapingFontSize > 0 ? buffers.shapingFontSize : uniText.FontSize;
            var resolved = value.ResolvePx(baseSize);
            if (resolved == 0f) return;

            var buffer = attribute?.buffer.data;
            if (buffer == null) return;

            var start = context.Segment.Range.start;
            var end = context.Segment.Range.End;
            var clampedEnd = Math.Min(end, buffers.codepoints.count);
            for (var i = start; i < clampedEnd; i++)
                if ((uint)i < (uint)buffer.Length)
                    buffer[i] = resolved;
        }

        private static bool IsWordSeparator(int cp)
            => cp == UnicodeData.Space || cp == UnicodeData.NoBreakSpace;

        private sealed class Channel : AttributeChannel
        {
            private PooledArrayAttribute<float> attribute;

            private Action shapedCallback;
            private Action linesBrokenCallback;

            protected override void OnActivate()
            {
                attribute = buffers.GetAttributeData<PooledArrayAttribute<float>>(Key);
                shapedCallback ??= OnShaped;
                uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
                linesBrokenCallback ??= OnLinesBroken;
                uniText.TextProcessor.LinesBroken.Subscribe(linesBrokenCallback, -1000);
            }

            protected override void OnDeactivate()
            {
                uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
                uniText.TextProcessor.LinesBroken.Unsubscribe(linesBrokenCallback);
            }

            protected override void OnRelease() => attribute = null;

            private void OnShaped()
            {
                var buffer = attribute?.buffer.data;
                if (buffer == null) return;

                var buf = buffers;
                var glyphs = buf.shapedGlyphs.data;
                var runs = buf.shapedRuns.data;
                var runCount = buf.shapedRuns.count;
                var codepoints = buf.codepoints.data;
                var cpCount = buf.codepoints.count;
                var bufLen = buffer.Length;

                for (var r = 0; r < runCount; r++)
                {
                    ref var run = ref runs[r];
                    var glyphEnd = run.glyphStart + run.glyphCount;
                    var width = 0f;

                    for (var g = run.glyphStart; g < glyphEnd; g++)
                    {
                        var cluster = glyphs[g].cluster;
                        if ((uint)cluster < (uint)bufLen && (uint)cluster < (uint)cpCount)
                        {
                            var spacing = buffer[cluster];
                            if (spacing != 0f && glyphs[g].advanceX != 0f && IsWordSeparator(codepoints[cluster]))
                                glyphs[g].advanceX += spacing;
                        }

                        width += glyphs[g].advanceX;
                    }

                    run.width = width;
                }
            }

            private void OnLinesBroken()
            {
                var spacingBuf = attribute?.buffer.data;
                if (spacingBuf == null) return;

                var lines = buffers.lines.data;
                var lineCount = buffers.lines.count;
                if (lineCount == 0) return;

                var codepoints = buffers.codepoints.data;
                var cpCount = buffers.codepoints.count;
                var spacingLen = spacingBuf.Length;

                for (var i = 0; i < lineCount; i++)
                {
                    ref var line = ref lines[i];
                    var rangeStart = line.range.start;
                    var rangeEnd = rangeStart + line.range.length - 1;
                    if (rangeEnd < rangeStart || (uint)rangeEnd >= (uint)cpCount) continue;

                    var lastCp = -1;
                    for (var cp = rangeEnd; cp >= rangeStart; cp--)
                    {
                        if (uniText.TextProcessor.IsClusterHiddenFromLayout(cp)) continue;
                        if (!LineBreaker.IsHangingWhitespace(codepoints[cp]))
                        {
                            lastCp = cp;
                            break;
                        }
                    }
                    if (lastCp < 0 || (uint)lastCp >= (uint)spacingLen) continue;

                    var spacing = spacingBuf[lastCp];
                    if (spacing != 0f && IsWordSeparator(codepoints[lastCp]))
                        line.width -= spacing;
                }
            }
        }
    }
}
