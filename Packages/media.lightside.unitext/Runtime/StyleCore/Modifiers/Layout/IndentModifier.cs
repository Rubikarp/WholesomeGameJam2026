using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Adds left indentation that begins where the tag opens and persists across wrapped lines
    /// until the tag closes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two complementary mechanisms produce a continuous visual indent:
    /// <list type="bullet">
    /// <item>Every line whose first codepoint falls inside the tagged range is pushed right by
    /// the resolved value (line-start margin).</item>
    /// <item>If the tag opens in the middle of a line (i.e. the codepoint preceding the tag is
    /// not a mandatory line break), the advance of that previous codepoint is widened by the
    /// indent so that content following the open boundary visibly shifts right within the
    /// current line.</item>
    /// </list>
    /// As soon as the tag closes, subsequent codepoints carry no extra indent, so the next line
    /// wraps back to the container edge.
    /// </para>
    /// <para>
    /// Indents from overlapping or nested ranges accumulate, so writing
    /// <c>&lt;indent=1em&gt;outer &lt;indent=1em&gt;inner&lt;/indent&gt;&lt;/indent&gt;</c>
    /// indents the inner content by two ems. Negative values pull the line back, which can be
    /// used to outdent a nested run or to balance an outer indent.
    /// </para>
    /// <para>
    /// Parameter — single value with optional unit:
    /// <list type="bullet">
    /// <item><c>20</c> or <c>20px</c> — layout units; like every other UniText geometric modifier
    /// these scale together with auto-sized text.</item>
    /// <item><c>1.5em</c> — multiplied by the current shaping font size.</item>
    /// <item><c>10%</c> — fraction of the host RectTransform's width, resolved into render
    /// space using the active glyph scale so the on-screen indent stays at the requested
    /// percentage with or without auto-size. Ignored when the rect has no finite width.</item>
    /// <item><c>+5</c> / <c>-5</c> — explicit delta. Functionally identical to the unsigned form
    /// because indents always compose additively.</item>
    /// </list>
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Layout", 5)]
    [TypeDescription("Indents content from the tag opening through every wrapped line within the range.")]
    [GenerateParameters]
    public partial class IndentModifier : PooledAttributeModifier<float>
    {
        /// <summary>Left indent added across the range. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Unit("px|em|%"), StateProperty(nameof(MarkTextDirty))] private UnitValue indent = UnitValue.Em(1f);

        private const string MidLineBumpKey = "indent.midLineBump";
        protected sealed override string AttributeKey => MidLineBumpKey;

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
            var value = Param.Indent.Resolve(this, in context);
            var resolved = ResolveToShapingUnits(value.value, value.unit);
            if (resolved == 0f)
                return;

            buffers.PrepareStartMargins();

            var margins = buffers.startMargins.data;
            if (margins == null)
                return;

            var cpCount = buffers.codepoints.count;
            var start = context.Segment.Range.start;
            var end = context.Segment.Range.End;
            var safeEnd = Math.Min(end, cpCount);
            for (var i = start; i < safeEnd; i++)
                margins[i] += resolved;

            var bumpIndex = -1;
            if (start > 0 && start < cpCount && !PrecededByMandatoryBreak(start))
            {
                bumpIndex = start - 1;
                attribute.buffer[bumpIndex] += resolved;
            }

            pass.Record(new AppliedIndent
            {
                start = start,
                end = safeEnd,
                bumpIndex = bumpIndex,
                amount = resolved
            });
        }

        /// <summary>
        /// True when the codepoint at <c>index - 1</c> ends a line per UAX#14 (BK / CR / LF / NL).
        /// In that case the tag opens at a hard line start and the line-start margin alone
        /// produces the visible indent — no glyph-advance bump is needed.
        /// </summary>
        private bool PrecededByMandatoryBreak(int index)
        {
            var cls = UnicodeData.Provider.GetLineBreakClass(buffers.codepoints[index - 1]);
            return cls == LineBreakClass.BK
                || cls == LineBreakClass.CR
                || cls == LineBreakClass.LF
                || cls == LineBreakClass.NL;
        }

        private float ResolveToShapingUnits(float value, UnitKind unit)
        {
            switch (unit)
            {
                case UnitKind.Em:
                    var emBase = buffers.shapingFontSize > 0 ? buffers.shapingFontSize : uniText.FontSize;
                    return value * emBase;

                case UnitKind.Percent:
                    var maxWidth = uniText.cachedTransformData.rect.width;
                    if (float.IsNaN(maxWidth) || float.IsInfinity(maxWidth) || maxWidth <= 0f)
                        return 0f;
                    var glyphScale = buffers.GetGlyphScale(uniText.CurrentFontSize);
                    if (glyphScale <= 0f) glyphScale = 1f;
                    return value * 0.01f * maxWidth / glyphScale;

                default:
                    return value;
            }
        }

        private struct AppliedIndent
        {
            public int start;
            public int end;
            /// <summary>Codepoint carrying the mid-line advance bump, or -1 when the range opens at a hard line start.</summary>
            public int bumpIndex;
            public float amount;
        }

        private sealed class Channel : AttributeChannel
        {
            private PooledArrayAttribute<float> attribute;
            private PooledList<AppliedIndent> applied;
            private Action shapedCallback;

            internal void Record(in AppliedIndent entry) => applied.Add(entry);

            protected override void OnActivate()
            {
                attribute = buffers.GetAttributeData<PooledArrayAttribute<float>>(Key);
                applied ??= new PooledList<AppliedIndent>(8);
                applied.FakeClear();
                shapedCallback ??= OnShaped;
                uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
            }

            protected override void OnDeactivate()
                => uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);

            protected override void OnBeginCycle() => Withdraw();

            protected override void OnRelease()
            {
                attribute = null;
                applied?.Return();
                applied = null;
            }

            /// <summary>
            /// Takes this key's own indent back out of the shared start-margin buffer and drops its
            /// unconsumed advance bumps, leaving values written under other keys untouched.
            /// </summary>
            private void Withdraw()
            {
                if (applied == null || applied.Count == 0) return;

                var prepared = buffers.startMargins.count;
                if (prepared > 0)
                {
                    var margins = buffers.startMargins.data;
                    var bumps = attribute?.buffer.data;
                    var bumpLen = attribute?.buffer.Capacity ?? 0;

                    for (var i = 0; i < applied.Count; i++)
                    {
                        ref var entry = ref applied[i];
                        var end = Math.Min(entry.end, prepared);
                        for (var c = entry.start; c < end; c++)
                            margins[c] -= entry.amount;

                        if (bumps != null && (uint)entry.bumpIndex < (uint)bumpLen)
                            bumps[entry.bumpIndex] = 0f;
                    }
                }

                applied.FakeClear();
            }

            private void OnShaped()
            {
                var bufLen = attribute.buffer.Capacity;
                if (bufLen == 0) return;

                var bumps = attribute.buffer.data;
                if (bumps == null) return;

                var glyphs = buffers.shapedGlyphs.data;
                var runs = buffers.shapedRuns.data;
                var runCount = buffers.shapedRuns.count;

                for (var r = 0; r < runCount; r++)
                {
                    ref var run = ref runs[r];
                    var glyphEnd = run.glyphStart + run.glyphCount;
                    float widthDelta = 0f;

                    for (var g = run.glyphStart; g < glyphEnd; g++)
                    {
                        var cluster = glyphs[g].cluster;
                        if ((uint)cluster >= (uint)bufLen) continue;
                        var bump = bumps[cluster];
                        if (bump == 0f) continue;

                        glyphs[g].advanceX += bump;
                        widthDelta += bump;
                        bumps[cluster] = 0f;
                    }

                    run.width += widthDelta;
                }
            }
        }
    }
}
