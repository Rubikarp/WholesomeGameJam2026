using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Fits every character of a range into a cell of one width, centering the glyph inside it.
    /// Advances and offsets change; glyphs are never substituted.
    /// </summary>
    /// <remarks>
    /// Parameter: the cell width with a unit — <c>1em</c> for a full-width CJK cell, <c>0.5em</c> for a
    /// half-width one, or a raw pixel amount. <c>auto</c>, and any non-positive width, measures the
    /// range and uses its widest glyph. Runs ahead of letter and word spacing, whose tracking is added
    /// on top of the finished cell.
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 1)]
    [TypeDescription("Fits characters into fixed-width cells.")]
    [GenerateParameters]
    public partial class CharacterWidthModifier : PooledAttributeModifier<float>
    {
        /// <summary>Cell width every character of a range is fitted to; a non-positive width measures the range's widest glyph. A per-range value overrides it.</summary>
        [SerializeField, Parameter(Parser = nameof(ParseWidth)), Unit("px|em"),
         Tooltip("Cell width. 0 — as wide as the widest glyph in the range."),
         StateProperty(nameof(MarkTextDirty))]
        private UnitValue width;

        /// <summary>Cell marker resolved after shaping to the widest advance of its span.</summary>
        private const float Measured = float.NaN;

        /// <summary>Subscription order ahead of the advance-adding modifiers, which subscribe at the default zero.</summary>
        private const int ShapedOrder = -100;

        protected sealed override string AttributeKey => AttributeKeys.CharacterWidth;

        [NonSerialized] private Channel pass;

        protected override AttributeChannel CreateChannel() => new Channel();

        protected override void OnEnable()
        {
            base.OnEnable();
            pass = (Channel)SharedChannel;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            pass = null;
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var value = Param.Width.Resolve(this, in context);

            var baseSize = buffers.shapingFontSize > 0 ? buffers.shapingFontSize : uniText.FontSize;
            var cell = value.ResolvePx(baseSize);
            var pending = cell <= 0f;

            attribute.FillRange(context.Segment.Range, pending ? Measured : cell);
            if (pending) pass.MarkMeasured();
        }

        /// <summary>
        /// Folds every glyph's advance into its own cluster's pending cell as a negative running
        /// maximum, which keeps the cell pending and lets one later walk resolve whole spans.
        /// </summary>
        private static void CollectWidest(float[] buffer, int cellCount,
            ShapedGlyph[] glyphs, ShapedRun[] runs, int runCount)
        {
            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;
                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    var advance = glyphs[g].advanceX;
                    if (advance <= 0f) continue;
                    var cluster = glyphs[g].cluster;
                    if ((uint)cluster >= (uint)cellCount) continue;
                    var cell = buffer[cluster];
                    if (float.IsNaN(cell)) buffer[cluster] = -advance;
                    else if (cell < 0f && -advance < cell) buffer[cluster] = -advance;
                }
            }
        }

        /// <summary>
        /// Resolves every pending span — a maximal run of adjacent pending cells — to the widest
        /// advance collected into it, so one authored range shares one cell width and neighbouring
        /// ranges never widen each other. Reports whether any cell is set at all.
        /// </summary>
        private static bool MeasureSpans(float[] buffer, int cellCount)
        {
            var hasCell = false;
            var start = 0;
            while (start < cellCount)
            {
                var cell = buffer[start];
                if (!IsPending(cell))
                {
                    hasCell |= cell > 0f;
                    start++;
                    continue;
                }

                var end = start + 1;
                while (end < cellCount && IsPending(buffer[end])) end++;

                var widest = 0f;
                for (var i = start; i < end; i++)
                {
                    var advance = -buffer[i];
                    if (advance > widest) widest = advance;
                }

                for (var i = start; i < end; i++) buffer[i] = widest;
                hasCell |= widest > 0f;
                start = end;
            }

            return hasCell;
        }

        private static bool IsPending(float cell) => float.IsNaN(cell) || cell < 0f;

        /// <summary>
        /// Sets every celled glyph's advance to its cell and centers the glyph in it. Zero-advance
        /// glyphs (marks) keep their attachment to the base glyph's cell.
        /// </summary>
        private static void FitCells(float[] buffer, int cellCount,
            ShapedGlyph[] glyphs, ShapedRun[] runs, int runCount)
        {
            for (var r = 0; r < runCount; r++)
            {
                ref var run = ref runs[r];
                var glyphEnd = run.glyphStart + run.glyphCount;
                var widthDelta = 0f;

                for (var g = run.glyphStart; g < glyphEnd; g++)
                {
                    var advance = glyphs[g].advanceX;
                    if (advance == 0f) continue;

                    var cluster = glyphs[g].cluster;
                    if ((uint)cluster >= (uint)cellCount) continue;

                    var cell = buffer[cluster];
                    if (cell <= 0f) continue;

                    var delta = cell - advance;
                    glyphs[g].advanceX = cell;
                    glyphs[g].offsetX += delta * 0.5f;
                    widthDelta += delta;
                }

                run.width += widthDelta;
            }
        }

        /// <summary>Reads <c>auto</c> as the measured cell; every other token as a unit value.</summary>
        private static bool ParseWidth(ReadOnlySpan<char> token, out UnitValue value)
        {
            if (token.EqualsIgnoreCase("auto"))
            {
                value = default;
                return true;
            }

            var reader = new ParameterReader(token);
            var parsed = reader.NextUnitFloat(out var number, out var unit);
            value = new UnitValue(number, unit);
            return parsed;
        }

        private sealed class Channel : AttributeChannel
        {
            private const string ResolvedAttributeKey = "cwidth.resolved";

            private PooledArrayAttribute<float> attribute;

            /// <summary>
            /// Final cell width per codepoint, produced and owned exclusively by the shaping pass;
            /// the authored cells the apply cycle rewrites are never resolved in place.
            /// </summary>
            private PooledArrayAttribute<float> resolvedAttribute;

            private Action shapedCallback;
            private bool measured;

            /// <summary>Records that a range left cells for the widest-advance walk to resolve.</summary>
            internal void MarkMeasured() => measured = true;

            protected override void OnActivate()
            {
                attribute = buffers.GetAttributeData<PooledArrayAttribute<float>>(Key);
                buffers.PrepareAttribute(ref resolvedAttribute, ResolvedAttributeKey);
                shapedCallback ??= OnShaped;
                uniText.TextProcessor.Shaped.Subscribe(shapedCallback, ShapedOrder);
            }

            protected override void OnDeactivate()
                => uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);

            protected override void OnBeginCycle() => measured = false;

            protected override void OnRelease()
            {
                buffers?.ReleaseAttributeData(ResolvedAttributeKey);
                attribute = null;
                resolvedAttribute = null;
            }

            private void OnShaped()
            {
                var authored = attribute?.buffer.data;
                var resolved = resolvedAttribute?.buffer.data;
                if (authored == null || resolved == null) return;

                var buf = buffers;
                var glyphs = buf.shapedGlyphs.data;
                var runs = buf.shapedRuns.data;
                var runCount = buf.shapedRuns.count;
                var cellCount = Math.Min(Math.Min(authored.Length, resolved.Length), buf.codepoints.count);

                Array.Copy(authored, resolved, cellCount);

                if (measured) CollectWidest(resolved, cellCount, glyphs, runs, runCount);
                if (MeasureSpans(resolved, cellCount))
                    FitCells(resolved, cellCount, glyphs, runs, runCount);
            }
        }
    }
}
