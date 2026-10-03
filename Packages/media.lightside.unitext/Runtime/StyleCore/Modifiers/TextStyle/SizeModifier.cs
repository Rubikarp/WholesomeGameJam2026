using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies font size changes to text ranges.
    /// </summary>
    /// <remarks>
    /// Parameter: size value with optional unit.
    /// <list type="bullet">
    /// <item><c>24</c> — absolute size in pixels</item>
    /// <item><c>150%</c> — percentage of base font size</item>
    /// <item><c>+10</c> — relative increase in pixels</item>
    /// <item><c>-5</c> — relative decrease in pixels</item>
    /// </list>
    /// Percentages and deltas are relative to the component font size, never to an enclosing tag —
    /// nested ranges overwrite, they do not compound (unlike CSS), and so do overlapping ranges of
    /// separate size modifiers.
    /// A line's height follows the largest size on it, in both directions, so a line whose visible
    /// content is entirely scaled grows or shrinks with it while a line that also carries unscaled
    /// text keeps its base height; an absolute <c>&lt;lh&gt;</c> in pixels pins the line regardless.
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Changes the font size of the text.")]
    [GenerateParameters]
    public partial class SizeModifier : PooledAttributeModifier<float>
    {
        /// <summary>Target size used when a range does not override it.</summary>
        [SerializeField, Parameter, Unit("px|%|delta"), StateProperty(nameof(MarkTextDirty))]
        private UnitValue size = UnitValue.Absolute(24);

        protected sealed override string AttributeKey => AttributeKeys.Size;

        protected override AttributeChannel CreateChannel() => new Channel();

        protected override void OnApply(in RangeApplyContext context)
        {
            var resolved = Param.Size.Resolve(this, in context);
            var value = resolved.value;
            var unit = resolved.unit;

            var baseSize = buffers.shapingFontSize > 0 ? buffers.shapingFontSize : uniText.FontSize;
            var scale = unit switch
            {
                UnitKind.Percent => value / 100f,
                UnitKind.Delta => (baseSize + value) / baseSize,
                _ => value / baseSize
            };
            if (scale <= 0f) return;

            attribute.FillRange(context.Segment.Range, scale);
        }

        private sealed class Channel : AttributeChannel
        {
            private PooledArrayAttribute<float> attribute;

            private Action shapedCallback;
            private Action glyphCallback;

            protected override void OnActivate()
            {
                attribute = buffers.GetAttributeData<PooledArrayAttribute<float>>(Key);

                shapedCallback ??= OnShaped;
                uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
                glyphCallback ??= OnGlyph;
                uniText.MeshGenerator.onGlyph.Subscribe(glyphCallback);
            }

            protected override void OnDeactivate()
            {
                uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
                uniText.MeshGenerator.onGlyph.Unsubscribe(glyphCallback);
            }

            protected override void OnRelease() => attribute = null;

            private void OnShaped()
            {
                var buf = buffers;
                var glyphs = buf.shapedGlyphs.data;
                var runs = buf.shapedRuns.data;
                var runCount = buf.shapedRuns.count;
                var bufLen = attribute.buffer.Capacity;

                for (var r = 0; r < runCount; r++)
                {
                    ref var run = ref runs[r];
                    var glyphEnd = run.glyphStart + run.glyphCount;
                    float width = 0f;

                    for (var g = run.glyphStart; g < glyphEnd; g++)
                    {
                        var cluster = glyphs[g].cluster;

                        if ((uint)cluster < (uint)bufLen)
                        {
                            var scale = attribute.buffer[cluster];
                            if (scale > 0f)
                            {
                                glyphs[g].advanceX *= scale;
                                glyphs[g].offsetX *= scale;
                                glyphs[g].offsetY *= scale;
                            }
                        }

                        width += glyphs[g].advanceX;
                    }

                    run.width = width;
                }
            }

            private void OnGlyph()
            {
                var gen = uniText.MeshGenerator;
                if (gen.currentPositionedIndex < 0) return;

                var cluster = gen.currentCluster;

                if ((uint)cluster >= (uint)attribute.buffer.Capacity)
                    return;

                var scale = attribute.buffer[cluster];
                if (scale <= 0f || Math.Abs(scale - 1f) < 0.001f)
                    return;

                gen.ScaleFace(gen.faceBaseIdx, gen.cursorX, gen.baselineY, scale);
            }
        }
    }
}
